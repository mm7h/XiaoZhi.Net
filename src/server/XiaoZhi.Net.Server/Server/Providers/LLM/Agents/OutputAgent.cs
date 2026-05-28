using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Helpers;
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
                routeBuilder
                    .AddHandler<IntentResult>(this.HandleIntentResultAsync)
                    .AddHandler<string>(this.HandleChatSentenceAsync);
            })
            .YieldsOutput<WorkflowOutputs>();
        }

        /// <summary>意图路径：将feedback包装为WorkflowOutputs并输出</summary>
        [MessageHandler]
        public async ValueTask HandleIntentResultAsync(IntentResult intentResult, IWorkflowContext context, CancellationToken token)
        {
            List<ChatMessageItemResult> results = new List<ChatMessageItemResult>();
            if (!string.IsNullOrWhiteSpace(intentResult.Feedback))
            {
                results.Add(new ChatMessageItemResult(Emotion.Neutral, intentResult.Feedback));
            }
            await context.YieldOutputAsync(new WorkflowOutputs(true, results), token);
        }

        /// <summary>对话路径：解析ChatAgent发来的单句文本（含Emotion标识），yield WorkflowOutputs</summary>
        [MessageHandler]
        public async ValueTask HandleChatSentenceAsync(string sentence, IWorkflowContext context, CancellationToken token)
        {
            EmotionTagParser.ParsedEmotionSegment parsed = EmotionTagParser.Parse(sentence);
            string cleanContent = DialogueHelper.GetStringNoPunctuationOrEmoji(parsed.Content);
            if (string.IsNullOrWhiteSpace(cleanContent)) return;

            List<ChatMessageItemResult> results = new List<ChatMessageItemResult>
            {
                new ChatMessageItemResult(parsed.Emotion, cleanContent)
            };
            await context.YieldOutputAsync(new WorkflowOutputs(false, results), token);
        }

        public override void Dispose()
        {

        }
    }
}
