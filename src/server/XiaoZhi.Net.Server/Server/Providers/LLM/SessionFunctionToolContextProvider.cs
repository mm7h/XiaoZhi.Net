using Microsoft.Agents.AI;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal sealed class SessionFunctionToolContextProvider : AIContextProvider
    {
        private static readonly IReadOnlyList<string> EMPTY_STATE_KEYS = Array.Empty<string>();

        private readonly PrivateProvider _sessionPrivateProvider;

        public SessionFunctionToolContextProvider(PrivateProvider sessionPrivateProvider)
        {
            this._sessionPrivateProvider = sessionPrivateProvider;
        }

        public override IReadOnlyList<string> StateKeys => EMPTY_STATE_KEYS;

        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
        {
            AIContext aiContext = new AIContext();
            if (this._sessionPrivateProvider.FunctionTools.Count > 0)
            {
                aiContext.Tools = this._sessionPrivateProvider.FunctionTools;
            }

            return ValueTask.FromResult(aiContext);
        }
    }
}