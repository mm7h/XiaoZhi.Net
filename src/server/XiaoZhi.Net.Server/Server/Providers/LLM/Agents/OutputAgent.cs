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
                routeBuilder.AddHandler<IntentResult, ValueTask<WorkflowOutputs>>(this.HandleOutputsAsync)
                .AddHandler<ChatMessageResult, ValueTask<WorkflowOutputs>>(this.HandleOutputsAsync);
            });
        }

        [MessageHandler]
        public async ValueTask<WorkflowOutputs> HandleOutputsAsync(IntentResult intentResult, IWorkflowContext context, CancellationToken token)
        {
            await context.YieldOutputAsync(intentResult, token);
            ChatMessageItemResult chatMessageItemResult = new(Emotion.Neutral, intentResult.Feedback);
            ChatMessageResult chatMessageResult = new ChatMessageResult([chatMessageItemResult]);
            return new WorkflowOutputs(false, chatMessageResult);
        }

        [MessageHandler]
        public async ValueTask<WorkflowOutputs> HandleOutputsAsync(ChatMessageResult chatMessageResult, IWorkflowContext context, CancellationToken token)
        {
            await context.YieldOutputAsync(chatMessageResult, token);
            return new WorkflowOutputs(false, chatMessageResult);
        }

        public override void Dispose()
        {

        }
    }
}
