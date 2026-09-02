using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace XiaoZhi.Net.Server.Providers.LLM.AIContextProviders
{
    internal sealed class IntentInstructionsContextProvider : AIContextProvider
    {
        private readonly Func<string> _instructionsFactory;

        public IntentInstructionsContextProvider(Func<string> instructionsFactory)
        {
            this._instructionsFactory = instructionsFactory;
        }

        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new AIContext { Instructions = this._instructionsFactory() });
        }
    }
}
