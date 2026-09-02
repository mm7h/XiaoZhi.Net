using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
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
            if (!this._isFunctionCallEnabled())
            {
                return ValueTask.FromResult(new AIContext());
            }

            return ValueTask.FromResult(new AIContext
            {
                Tools = this._functionToolsContext.Capture().Tools
            });
        }
    }
}
