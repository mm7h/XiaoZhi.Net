using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers.LLM.AIContextProviders
{
    internal sealed class FunctionToolsContextProvider : AIContextProvider
    {
        private readonly FunctionToolsContext _functionToolsContext;
        private readonly Func<bool> _isFunctionCallEnabled;

        public FunctionToolsContextProvider(FunctionToolsContext functionToolsContext, Func<bool> isFunctionCallEnabled)
        {
            this._functionToolsContext = functionToolsContext;
            this._isFunctionCallEnabled = isFunctionCallEnabled;
        }

        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
        {
            if (this._isFunctionCallEnabled())
            {
                return ValueTask.FromResult(new AIContext
                {
                    Tools = this._functionToolsContext.Capture().Tools
                });
            }

            string toolDescriptions = this._functionToolsContext.GetToolDescriptions();
            return ValueTask.FromResult(new AIContext
            {
                Instructions = string.IsNullOrWhiteSpace(toolDescriptions)
                    ? string.Empty
                    : $"当前已注册以下能力。回答能力相关问题时，应据此说明你可以协助用户完成对应操作：{Environment.NewLine}{toolDescriptions}"
            });
        }
    }
}
