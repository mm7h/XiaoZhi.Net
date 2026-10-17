using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    /// <summary>
    /// 会话内函数工具的发布快照和 MCP 首次加载状态。
    /// </summary>
    internal sealed class FunctionToolsContext
    {
        private readonly object _lock = new object();
        private IReadOnlyDictionary<string, FunctionToolRegistration> _registrations;
        private IReadOnlyList<AITool> _tools;
        private string _toolDescriptions = string.Empty;
        private string? _intentInstructionPrefix;
        private string _intentInstructions = string.Empty;
        private long _version;
        private TaskCompletionSource? _mcpClientReadyTcs;
        private Task<bool>? _initialMcpToolsWaitTask;

        public FunctionToolsContext()
        {
            this._registrations = new ReadOnlyDictionary<string, FunctionToolRegistration>(new Dictionary<string, FunctionToolRegistration>(StringComparer.OrdinalIgnoreCase));
            this._tools = Array.Empty<AITool>();
        }

        public List<PrivateFunctionTool> PrivateFunctionTools { get; } = [];

        public IReadOnlyList<AITool> Tools
        {
            get
            {
                lock (this._lock)
                {
                    return this._tools;
                }
            }
        }

        public Task? McpClientReadyTask
        {
            get
            {
                lock (this._lock)
                {
                    return this._mcpClientReadyTcs?.Task;
                }
            }
        }

        public void AddFunctionToolRegistration(FunctionToolRegistration registration)
        {
            lock (this._lock)
            {
                this.PublishFunctionToolRegistration(registration);
            }
        }

        public void AddFunctionToolRegistrations(IEnumerable<FunctionToolRegistration> registrations)
        {
            lock (this._lock)
            {
                Dictionary<string, FunctionToolRegistration> updatedRegistrations = new Dictionary<string, FunctionToolRegistration>(this._registrations, StringComparer.OrdinalIgnoreCase);
                foreach (FunctionToolRegistration registration in registrations)
                {
                    updatedRegistrations[registration.Function.Name] = registration;
                }

                this.PublishFunctionTools(new ReadOnlyDictionary<string, FunctionToolRegistration>(updatedRegistrations));
            }
        }

        public void AddPrivateFunctionTool(PrivateFunctionTool privateFunctionTool)
        {
            lock (this._lock)
            {
                if (!this.PrivateFunctionTools.Any(registeredTool => ReferenceEquals(registeredTool, privateFunctionTool)))
                {
                    this.PrivateFunctionTools.Add(privateFunctionTool);
                }
            }
        }

        public bool TryGetFunctionToolRegistration(string functionName, out FunctionToolRegistration? registration)
        {
            lock (this._lock)
            {
                return this._registrations.TryGetValue(functionName, out registration);
            }
        }

        public string GetToolDescriptions()
        {
            lock (this._lock)
            {
                return this._toolDescriptions;
            }
        }

        public (long Version, IReadOnlyList<AITool> Tools, IReadOnlyDictionary<string, FunctionToolRegistration> Registrations, string IntentInstructions) Capture()
        {
            lock (this._lock)
            {
                return (this._version, this._tools, this._registrations, this._intentInstructions);
            }
        }

        public void ConfigureIntentInstructions(string instructionPrefix)
        {
            lock (this._lock)
            {
                this._intentInstructionPrefix = instructionPrefix;
                this.PublishFunctionTools(this._registrations);
            }
        }

        public void SetMcpClientPending()
        {
            lock (this._lock)
            {
                this._mcpClientReadyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                this._initialMcpToolsWaitTask = null;
            }
        }

        public void SetMcpClientReady()
        {
            TaskCompletionSource? readyTcs;
            lock (this._lock)
            {
                readyTcs = this._mcpClientReadyTcs;
            }
            readyTcs?.TrySetResult();
        }

        public void SetMcpClientFailed(Exception exception)
        {
            TaskCompletionSource? readyTcs;
            lock (this._lock)
            {
                readyTcs = this._mcpClientReadyTcs;
            }
            readyTcs?.TrySetException(exception);
        }

        public Task<bool> WaitForInitialMcpToolsAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            lock (this._lock)
            {
                this._initialMcpToolsWaitTask ??= this.WaitForMcpToolsAsync(timeout);
                return this._initialMcpToolsWaitTask.WaitAsync(cancellationToken);
            }
        }

        public void Release()
        {
            TaskCompletionSource? readyTcs;
            lock (this._lock)
            {
                readyTcs = this._mcpClientReadyTcs;
                this.PrivateFunctionTools.Clear();
                this._initialMcpToolsWaitTask = null;
                this.PublishFunctionTools(new ReadOnlyDictionary<string, FunctionToolRegistration>(new Dictionary<string, FunctionToolRegistration>(StringComparer.OrdinalIgnoreCase)));
            }
            readyTcs?.TrySetCanceled();
        }

        private void PublishFunctionToolRegistration(FunctionToolRegistration registration)
        {
            Dictionary<string, FunctionToolRegistration> registrations = new Dictionary<string, FunctionToolRegistration>(this._registrations, StringComparer.OrdinalIgnoreCase)
            {
                [registration.Function.Name] = registration
            };
            this.PublishFunctionTools(new ReadOnlyDictionary<string, FunctionToolRegistration>(registrations));
        }

        private void PublishFunctionTools(IReadOnlyDictionary<string, FunctionToolRegistration> registrations)
        {
            this._registrations = registrations;
            this._tools = new ReadOnlyCollection<AITool>(registrations.Values.Select(static registration => (AITool)registration.Function).ToList());
            this._toolDescriptions = FunctionToolHelper.BuildToolDescriptions(registrations.Values);
            this._intentInstructions = this._intentInstructionPrefix is null
                ? string.Empty
                : this._intentInstructionPrefix + (string.IsNullOrWhiteSpace(this._toolDescriptions) ? "当前没有可用函数。" : this._toolDescriptions);
            this._version++;
        }

        private async Task<bool> WaitForMcpToolsAsync(TimeSpan timeout)
        {
            Task? readyTask = this.McpClientReadyTask;
            if (readyTask is null || readyTask.IsCompletedSuccessfully)
            {
                return true;
            }

            try
            {
                await readyTask.WaitAsync(timeout).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
