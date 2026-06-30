using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SuperSocket.Server.Abstractions.Session;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.Common.Attributes;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Abstractions.FunctionTools;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;
using XiaoZhi.Net.Server.Resources;

namespace XiaoZhi.Net.Server.Management
{
    internal class FunctionToolManager
    {
        #region 内部类型

        /// <summary>缓存单个方法的元数据（类型级，不依赖实例）。</summary>
        private sealed class CachedToolMethodMetadata
        {
            public string FunctionName { get; init; } = null!;
            public string? Description { get; init; }
            public ToolBehaviorAttribute? Behavior { get; init; }
            public Type[] ParameterTypes { get; init; } = [];
            public Type ReturnType { get; init; } = null!;
            public MethodInfo Method { get; init; } = null!;
        }

        /// <summary>缓存整个工具类型的元数据。</summary>
        private sealed class CachedToolTypeMetadata
        {
            public Type ToolType { get; init; } = null!;
            public bool IsPrivate { get; init; }
            public CachedToolMethodMetadata[] Methods { get; init; } = [];
        }

        /// <summary>已注册的工具类型记录。</summary>
        private sealed class ToolTypeRegistration
        {
            public Type Type { get; init; } = null!;
            public bool IsPrivate { get; init; }
        }

        #endregion

        #region 字段

        private readonly IServiceProvider _serviceProvider;
        private readonly ILoggerFactory _loggerFactory;
        private readonly XiaoZhiConfig _config;
        private readonly ISessionContainer _sessionContainer;

        /// <summary>类型级元数据缓存（启动时一次反射，后续只读）。</summary>
        private readonly ConcurrentDictionary<Type, CachedToolTypeMetadata> _typeMetadataCache = new();

        /// <summary>ServerBuilder 注册的工具类型列表。</summary>
        private readonly List<ToolTypeRegistration> _toolTypeRegistrations = [];

        /// <summary>Singleton 工具实例缓存。</summary>
        private readonly Dictionary<Type, FunctionTool> _singletonToolInstances = [];

        /// <summary>Singleton 工具预构建的注册项（按 function name 索引）。</summary>
        private readonly Dictionary<string, FunctionToolRegistration> _singletonRegistrations = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Per-session 的 PrivateFunctionTool 实例。</summary>
        private readonly ConcurrentDictionary<string, List<PrivateFunctionTool>> _sessionPrivateTools = new();

        #endregion

        #region 构造与注册

        public FunctionToolManager(
            IServiceProvider serviceProvider,
            ILoggerFactory loggerFactory,
            XiaoZhiConfig config,
            ISessionContainer sessionContainer)
        {
            this._serviceProvider = serviceProvider;
            this._loggerFactory = loggerFactory;
            this._config = config;
            this._sessionContainer = sessionContainer;
        }

        public static IHostBuilder RegisterServices(IHostBuilder builder)
        {
            return builder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<FunctionToolManager>();
            });
        }

        public static void RegisterToolType(IHostBuilder builder, Type type, bool isPrivate)
        {
            // this._toolTypeRegistrations.Add(new ToolTypeRegistration
            // {
            //     Type = type,
            //     IsPrivate = isPrivate
            // });
        }

        #endregion

        #region 启动初始化

        public bool BuildComponent(IServiceProvider serviceProvider)
        {
            try
            {
                var singletonRegs = this._toolTypeRegistrations.Where(static t => !t.IsPrivate).ToList();

                var concurrentSingletonRegs = new ConcurrentDictionary<string, FunctionToolRegistration>(StringComparer.OrdinalIgnoreCase);
                var concurrentToolInstances = new ConcurrentDictionary<Type, FunctionTool>();

                Parallel.ForEach(singletonRegs, reg =>
                {
                    FunctionTool? instance = serviceProvider.GetService(reg.Type) as FunctionTool;
                    if (instance is null) return;

                    // 注入基础设施
                    instance.Logger = this._loggerFactory.CreateLogger(reg.Type);
                    instance.ServerInfo = this.CreateServerInfoAdapter();
                    instance.SessionStore = new SessionStoreAdapter(this._sessionContainer);

                    // 提取类型级元数据
                    CachedToolTypeMetadata typeMeta = this.ExtractTypeMetadata(reg.Type, isPrivate: false);
                    this._typeMetadataCache[reg.Type] = typeMeta;

                    // 预构建 FunctionToolRegistration
                    foreach (CachedToolMethodMetadata methodMeta in typeMeta.Methods)
                    {
                        FunctionToolRegistration registration = this.BuildRegistrationFromCache(instance, methodMeta);
                        concurrentSingletonRegs[registration.Function.Name] = registration;
                    }

                    concurrentToolInstances[reg.Type] = instance;
                });

                // 合并并行结果到顺序集合
                foreach (var kvp in concurrentSingletonRegs)
                {
                    this._singletonRegistrations[kvp.Key] = kvp.Value;
                }
                foreach (var kvp in concurrentToolInstances)
                {
                    this._singletonToolInstances[kvp.Key] = kvp.Value;
                }

                if (concurrentToolInstances.Values.Count > 0)
                {
                    FunctionToolHelper.FireHooksSafely(concurrentToolInstances.Values.Select(
                        instance => instance.OnFunctionToolInitializedAsync().AsTask()),
                        nameof(FunctionTool.OnFunctionToolInitializedAsync));
                }

                var transientRegs = this._toolTypeRegistrations.Where(static t => t.IsPrivate).ToList();
                Parallel.ForEach(transientRegs, reg =>
                {
                    CachedToolTypeMetadata typeMeta = this.ExtractTypeMetadata(reg.Type, isPrivate: true);
                    this._typeMetadataCache[reg.Type] = typeMeta;
                });

                return true;
            }
            catch (AggregateException ae)
            {
                // Parallel.ForEach 将异常包装为 AggregateException
                foreach (Exception inner in ae.InnerExceptions)
                {
                    Serilog.Log.Error(inner, "FunctionToolManager.BuildComponent 并行任务失败");
                }
                return false;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "FunctionToolManager.BuildComponent 失败");
                return false;
            }
        }

        #endregion

        #region Session 生命周期
        public Task OnSessionConnectedAsync(Session session)
        {
            List<ToolTypeRegistration> transientRegs = this._toolTypeRegistrations.Where(static t => t.IsPrivate).ToList();
            if (transientRegs.Count == 0) return Task.CompletedTask;

            var instances = new List<PrivateFunctionTool>(transientRegs.Count);

            foreach (ToolTypeRegistration reg in transientRegs)
            {
                PrivateFunctionTool? instance = this._serviceProvider.GetService(reg.Type) as PrivateFunctionTool;
                if (instance is null) continue;

                instance.Logger = this._loggerFactory.CreateLogger(reg.Type);
                instance.ServerInfo = this.CreateServerInfoAdapter();
                instance.SessionStore = new SessionStoreAdapter(this._sessionContainer);

                instances.Add(instance);
            }

            if (instances.Count > 0)
            {
                FunctionToolHelper.FireHooksSafely(instances.Select(instance => instance.OnSessionConnectedAsync().AsTask()),
                    nameof(PrivateFunctionTool.OnSessionConnectedAsync));
            }

            this._sessionPrivateTools[session.SessionId] = instances;
            return Task.CompletedTask;
        }

        public Task InitializeSessionToolsAsync(Session session)
        {
            foreach (FunctionToolRegistration registration in this._singletonRegistrations.Values)
            {
                session.PrivateProvider.AddFunctionToolRegistration(registration);
            }

            if (!this._sessionPrivateTools.TryGetValue(session.SessionId, out List<PrivateFunctionTool>? privateInstances))
                return Task.CompletedTask;

            foreach (PrivateFunctionTool instance in privateInstances)
            {
                instance.SessionContext = new SessionContextAdapter(session);
                instance.MediaTool = new MediaToolAdapter(session);
            }

            FunctionToolHelper.FireHooksSafely(privateInstances.Select(instance => instance.OnFunctionToolInitializedAsync().AsTask()),
                nameof(PrivateFunctionTool.OnFunctionToolInitializedAsync));

            var registrationsPerInstance = new ConcurrentBag<(PrivateFunctionTool Instance, FunctionToolRegistration Registration)>();

            Parallel.ForEach(privateInstances, instance =>
            {
                if (!this._typeMetadataCache.TryGetValue(instance.GetType(), out CachedToolTypeMetadata? typeMeta))
                    return;

                foreach (CachedToolMethodMetadata methodMeta in typeMeta.Methods)
                {
                    FunctionToolRegistration registration = this.BuildRegistrationFromCache(instance, methodMeta);
                    registrationsPerInstance.Add((instance, registration));
                }
            });

            foreach (var (instance, registration) in registrationsPerInstance)
            {
                session.PrivateProvider.AddFunctionToolRegistration(registration);
            }
            return Task.CompletedTask;
        }

        public Task OnSessionClosedAsync(Session session)
        {
            if (!this._sessionPrivateTools.TryRemove(session.SessionId, out List<PrivateFunctionTool>? instances))
                return Task.CompletedTask;

            FunctionToolHelper.FireHooksSafely(instances.Select(instance => instance.OnSessionClosedAsync().AsTask()),
                nameof(PrivateFunctionTool.OnSessionClosedAsync));
            FunctionToolHelper.FireHooksSafely(instances.Select(instance => instance.OnFunctionToolReleasedAsync().AsTask()),
                nameof(PrivateFunctionTool.OnFunctionToolReleasedAsync));

            foreach (PrivateFunctionTool instance in instances)
            {
                if (instance is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            return Task.CompletedTask;
        }

        public Task OnReleaseAsync()
        {
            FunctionToolHelper.FireHooksSafely(this._singletonToolInstances.Values.Select(
                instance => instance.OnFunctionToolReleasedAsync().AsTask()),
                nameof(FunctionTool.OnFunctionToolReleasedAsync));

            this._singletonToolInstances.Clear();
            this._singletonRegistrations.Clear();
            this._typeMetadataCache.Clear();

            return Task.CompletedTask;
        }

        #endregion

        #region 私有辅助方法

        private CachedToolTypeMetadata ExtractTypeMetadata(Type toolType, bool isPrivate)
        {
            MethodInfo[] methods = toolType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(static m => !m.IsSpecialName)
                .ToArray();

            var cachedMethods = new List<CachedToolMethodMetadata>(methods.Length);

            foreach (MethodInfo method in methods)
            {
                string functionName = method.Name;
                string? description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;
                ToolBehaviorAttribute? behavior = method.GetCustomAttribute<ToolBehaviorAttribute>();
                Type[] parameterTypes = method.GetParameters().Select(static p => p.ParameterType).ToArray();

                cachedMethods.Add(new CachedToolMethodMetadata
                {
                    FunctionName = functionName,
                    Description = description,
                    Behavior = behavior,
                    ParameterTypes = parameterTypes,
                    ReturnType = method.ReturnType,
                    Method = method
                });
            }

            return new CachedToolTypeMetadata
            {
                ToolType = toolType,
                IsPrivate = isPrivate,
                Methods = cachedMethods.ToArray()
            };
        }

        private FunctionToolRegistration BuildRegistrationFromCache(object instance, CachedToolMethodMetadata methodMeta)
        {
            Delegate methodDelegate = this.CreateDelegateFromCache(instance, methodMeta);

            AIFunction aiFunction = AIFunctionFactory.Create(methodDelegate, new AIFunctionFactoryOptions
            {
                Name = methodMeta.FunctionName,
                Description = methodMeta.Description ?? methodMeta.FunctionName
            });

            FunctionMetadata metadata = aiFunction.ToFunctionMetadata();

            return new FunctionToolRegistration(
                aiFunction,
                metadata,
                methodMeta.Behavior?.DefaultAction ?? ToolAction.Continue);
        }

        private Delegate CreateDelegateFromCache(object target, CachedToolMethodMetadata methodMeta)
        {
            Type delegateType;
            if (methodMeta.ReturnType == typeof(void))
            {
                delegateType = Expression.GetActionType(methodMeta.ParameterTypes);
            }
            else
            {
                delegateType = Expression.GetFuncType(
                    methodMeta.ParameterTypes.Concat([methodMeta.ReturnType]).ToArray());
            }

            return methodMeta.Method.CreateDelegate(delegateType, target);
        }

        private ServerInfoAdapter CreateServerInfoAdapter()
        {
            return new ServerInfoAdapter("Xiao Zhi .Net Server", this._config);
        }

        #endregion
    }
}