using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SuperSocket.Server.Abstractions.Session;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.Common.Attributes;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Abstractions.FunctionTools;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Models;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;
using XiaoZhi.Net.Server.Resources;
using XiaoZhi.Net.Server.Common.Constants;

namespace XiaoZhi.Net.Server.Management
{
    internal class FunctionToolManager : BaseManager
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IMusicFileProvider _musicFileProvider;
        private readonly ISessionContainer _sessionContainer;

        private readonly List<FunctionTool> _globalFunctionTools;
        private readonly Dictionary<Type, IEnumerable<FunctionToolMethodMetadata>> _globalFunctionToolMethodMetadata;
        private readonly Dictionary<Type, IEnumerable<FunctionToolMethodMetadata>> _privateFunctionToolMethodMetadata;

        private bool _hasFunctionTools = false;

        public FunctionToolManager(
            ILoggerFactory loggerFactory,
            IMusicFileProvider musicFileProvider,
            ISessionContainer sessionContainer,
            IServiceProvider serviceProvider,
            XiaoZhiConfig config,
            ILogger<FunctionToolManager> logger) : base(serviceProvider, config, logger)
        {
            this._loggerFactory = loggerFactory;
            this._musicFileProvider = musicFileProvider;
            this._sessionContainer = sessionContainer;

            this._globalFunctionTools = [];
            this._globalFunctionToolMethodMetadata = [];
            this._privateFunctionToolMethodMetadata = [];
        }

        public static IHostBuilder RegisterServices(IHostBuilder builder)
        {
            return builder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<FunctionToolManager>();
            });
        }

        public override bool BuildComponent()
        {
            try
            {
                List<IFunctionTool> globalFunctionTools = this.ServiceProvider.GetServices<IFunctionTool>().ToList();
                List<IPrivateFunctionTool> privateFunctionTools = this.ServiceProvider.GetServices<IPrivateFunctionTool>().ToList();

                Parallel.ForEach(globalFunctionTools, item =>
                {
                    if (item is FunctionTool instance)
                    {
                        instance.Logger = this._loggerFactory.CreateLogger(instance.GetType());
                        instance.ServerInfo = this.CreateServerInfoAdapter();
                        instance.SessionStore = new SessionStoreAdapter(this._sessionContainer);

                        Type instanceType = instance.GetType();

                        this._globalFunctionToolMethodMetadata.Add(instanceType, this.ExtractTypeMetadata(instance, instanceType));
                        _ = instance.OnFunctionToolInitializedAsync().AsTask();

                        this._globalFunctionTools.Add(instance);
                    }
                });

                Parallel.ForEach(privateFunctionTools, instance =>
                {
                    Type instanceType = instance.GetType();
                    this._privateFunctionToolMethodMetadata.Add(instanceType, this.ExtractTypeMetadata(instance, instanceType));
                });

                this._hasFunctionTools = this._globalFunctionToolMethodMetadata.Any() || this._privateFunctionToolMethodMetadata.Any();
                return true;
            }
            catch (AggregateException ae)
            {
                // Parallel.ForEach 将异常包装为 AggregateException
                foreach (Exception inner in ae.InnerExceptions)
                {
                    this.Logger.LogError(inner, "FunctionToolManager.BuildComponent 并行任务失败");
                }
                return false;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "FunctionToolManager.BuildComponent 失败");
                return false;
            }
        }

        public override Task OnSessionConnectedAsync(Session session)
        {
            if (!this._hasFunctionTools)
            {
                //log
                return Task.CompletedTask;
            }

            List<FunctionToolRegistration> globalRegistrations = [];
            foreach (FunctionTool instance in this._globalFunctionTools)
            {
                if (!this._globalFunctionToolMethodMetadata.TryGetValue(instance.GetType(), out IEnumerable<FunctionToolMethodMetadata>? methodMetas))
                {
                    continue;
                }

                foreach (FunctionToolMethodMetadata methodMeta in methodMetas)
                {
                    FunctionToolRegistration registration = this.BuildRegistration(instance, methodMeta);
                    globalRegistrations.Add(registration);
                }
            }
            session.PrivateProvider.FunctionToolsContext.AddFunctionToolRegistrations(globalRegistrations);

            Dictionary<Type, IPrivateFunctionTool> privateFunctionTools = this.ServiceProvider.GetServices<IPrivateFunctionTool>().ToDictionary(i => i.GetType());
            List<Exception> functionExceptions = [];

            foreach (var item in privateFunctionTools)
            {
                if (!this._privateFunctionToolMethodMetadata.TryGetValue(item.Key, out var methodMetas))
                {
                    //log
                    continue;
                }

                if (item.Value is PrivateFunctionTool instance)
                {
                    session.PrivateProvider.FunctionToolsContext.AddPrivateFunctionTool(instance);
                    List<FunctionToolRegistration> registrations = [];
                    foreach (FunctionToolMethodMetadata methodMeta in methodMetas)
                    {
                        FunctionToolRegistration registration = this.BuildRegistration(instance, methodMeta);
                        registrations.Add(registration);
                    }
                    session.PrivateProvider.FunctionToolsContext.AddFunctionToolRegistrations(registrations);

                    instance.Logger = this._loggerFactory.CreateLogger(instance.GetType());
                    instance.ServerInfo = this.CreateServerInfoAdapter();
                    instance.SessionStore = new SessionStoreAdapter(this._sessionContainer);
                    instance.SessionContext = new SessionContextAdapter(session);
                    instance.SessionController = new SessionControllerAdapter(session);
                    instance.MediaTool = new MediaToolAdapter(session, this._musicFileProvider);

                    try
                    {
                        _ = instance.OnFunctionToolInitializedAsync().AsTask();
                    }
                    catch (Exception ex)
                    {
                        functionExceptions.Add(ex);
                    }
                }
            }

            if (functionExceptions.Any())
            {
                AggregateException aggregateException = new AggregateException(functionExceptions);
                this.Logger.LogError(aggregateException, "FunctionToolManager.OnSessionConnectedAsync 处理函数工具时发生异常");
            }

            return Task.CompletedTask;
        }



        public override Task OnSessionClosedAsync(Session session)
        {
            if (!session.PrivateProvider.FunctionToolsContext.PrivateFunctionTools.Any())
            {
                return Task.CompletedTask;
            }

            try
            {
                Parallel.ForEach(session.PrivateProvider.FunctionToolsContext.PrivateFunctionTools, instance =>
                {
                    _ = instance.OnSessionClosedAsync().AsTask();
                    _ = instance.OnFunctionToolReleasedAsync().AsTask();
                    if (instance is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                });
            }
            catch (AggregateException ae)
            {
                this.Logger.LogError(ae, "FunctionToolManager.OnSessionClosedAsync 处理函数工具时发生异常");
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "FunctionToolManager.OnSessionClosedAsync 处理函数工具时发生异常");
            }

            return Task.CompletedTask;
        }

        public override Task OnSessionPropertyInitializedAsync(Session session, JsonObject helloMessage)
        {
            if (session.PrivateProvider.FunctionToolsContext.PrivateFunctionTools.Any())
            {
                _ = Task.WhenAll(session.PrivateProvider.FunctionToolsContext.PrivateFunctionTools.Select(instance => instance.OnSessionConnectedAsync().AsTask()));
            }
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            Task.WhenAll(this._globalFunctionTools.Select(async instance =>
            {
                await instance.OnFunctionToolReleasedAsync().AsTask();
                if (instance is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            })).GetAwaiter().GetResult();
        }

        private IEnumerable<FunctionToolMethodMetadata> ExtractTypeMetadata(object? instance, Type toolType)
        {
            foreach (MethodInfo method in toolType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(static m => !m.IsSpecialName)
                .Where(static m =>
                {
                    MethodInfo baseDefinition = m.GetBaseDefinition();
                    Type? declaringType = baseDefinition.DeclaringType;
                    return declaringType != typeof(FunctionTool) && declaringType != typeof(PrivateFunctionTool);
                }))
            {
                string functionName = method.Name;
                string? description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;
                ToolBehaviorAttribute? behavior = method.GetCustomAttribute<ToolBehaviorAttribute>();
                AIFunction aiFunction = AIFunctionFactory.Create(method, new AIFunctionFactoryOptions
                {
                    Name = functionName,
                    Description = description ?? functionName,
                    SerializerOptions = JsonHelper.OPTIONS
                });

                FunctionToolMethodMetadata functionToolMethodMetadata = new FunctionToolMethodMetadata
                {
                    FunctionName = functionName,
                    Description = description,
                    Behavior = behavior,
                    ParameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray(),
                    ReturnType = method.ReturnType,
                    Method = method
                };

                yield return functionToolMethodMetadata;
            }
        }

        private FunctionToolRegistration BuildRegistration(object instance, FunctionToolMethodMetadata methodMeta)
        {
            AIFunction aiFunction = AIFunctionFactory.Create(methodMeta.Method, instance, new AIFunctionFactoryOptions
            {
                Name = methodMeta.FunctionName,
                Description = methodMeta.Description ?? methodMeta.FunctionName,
                SerializerOptions = JsonHelper.OPTIONS
            });

            FunctionMetadata metadata = aiFunction.ToFunctionMetadata();

            return new FunctionToolRegistration(
                aiFunction,
                metadata,
                methodMeta.Behavior?.DefaultAction ?? ToolAction.Continue);
        }

        private ServerInfoAdapter CreateServerInfoAdapter()
        {
            return new ServerInfoAdapter(GlobalVariables.ServerName, this.Config);
        }
    }
}
