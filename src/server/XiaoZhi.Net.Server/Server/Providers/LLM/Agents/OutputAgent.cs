using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;
using XiaoZhi.Net.Server.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents
{
    internal class OutputAgent : BaseAgent<OutputAgent>
    {
        public OutputAgent(IServiceProvider serviceProvider, ILogger<OutputAgent> logger) : base(SubAgentNames.OutputAgent, serviceProvider, logger)
        {
        }

        public override int Order => 99;

        public override bool Build(LLMAgentBuildConfig buildConfig)
        {
            return true;
        }

        protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
        {
            return protocolBuilder.ConfigureRoutes(routeBuilder =>
            {
                routeBuilder.AddHandler<IntentResult>(this.HandleOutputsAsync)
                .AddHandler<string>(this.HandleOutputsAsync);
            })
            .YieldsOutput<WorkflowOutputs>();
        }

        [MessageHandler]
        public async ValueTask HandleOutputsAsync(IntentResult intentResult, IWorkflowContext context, CancellationToken token)
        {
            await context.YieldOutputAsync(intentResult, token);
            ChatMessageItemResult chatMessageItemResult = new(Emotion.Neutral, intentResult.Feedback);
            //return new WorkflowOutputs(false, [chatMessageItemResult]);
        }

        [MessageHandler]
        public async ValueTask HandleOutputsAsync(string chatMessageResult, IWorkflowContext context, CancellationToken token)
        {
            await context.YieldOutputAsync(chatMessageResult, token);
            //return new WorkflowOutputs(false, chatMessageResult);
        }

        public override void Dispose()
        {

        }
    }
}
