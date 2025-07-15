using Microsoft.SemanticKernel.ChatCompletion;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class DialogueHandler : BaseHandler, IInHandler<string>, IOutHandler<OutSegment>
    {
        private readonly ILlm _llm;
        private readonly IMemory _memory;
        private bool _useStreaming;

        public DialogueHandler(ILlm llm, IMemory memory, XiaoZhiConfig config, ILogger logger) : base(config, logger)
        {
            this._llm = llm;
            this._useStreaming = this.Config.LlmSettings.First().Config.UseStreaming ?? false;
            this._memory = memory;
            this._llm.OnBeforeTokenGenerate += this.OnBeforeTokenGenerate;
            this._llm.OnTokenGenerating += this.OnTokenGenerating;
            this._llm.OnTokenGenerated += this.OnTokenGenerated;
        }

        public override string HandlerName => nameof(DialogueHandler);
        public IBizSendOutter SendOutter { get; set; } = null!;
        public ChannelReader<Workflow<string>> PreviousReader { get; set; } = null!;
        public ChannelWriter<Workflow<OutSegment>> NextWriter { get; set; } = null!;

        public async Task Handle()
        {
            await foreach (var reader in PreviousReader.ReadAllAsync()) await this.Handle(reader);
        }

        public async Task Handle(Workflow<string> workflow, bool addToChatHistory = true)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            try
            {
                Dialogue dialogue = new Dialogue(session.DeviceId, session.SessionId, AuthorRole.User, workflow.Data);
                session.Dialogues.Add(dialogue);

                using (CodeTimer timer = CodeTimer.Create("Calling the LLM takes {timer.ElapsedMilliseconds} ms.", this.Logger))
                {
                    DialogueContext dialogueContext = new DialogueContext(session.SessionId, session.PrivateProvider?.LlmModelName, session.Dialogues);
                    Workflow<DialogueContext> nextWorkflow = workflow.NextFlow(dialogueContext);

                    bool useStreaming = (session.PrivateProvider is not null && session.PrivateProvider.UseStreaming) || this._useStreaming;

                    if (useStreaming)
                    {
                        await this._llm.ChatByStreamingAsync(nextWorkflow, session.SessionCtsToken);
                    }
                    else
                    {
                        await this._llm.ChatAsync(nextWorkflow, session.SessionCtsToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "llm request");
            }
        }

        public async void NoVoiceCloseConnect(Workflow<string> workflow)
        {
            await this.Handle(workflow, false);
        }

        public async Task SendCustomMessage(string sessionId, string content)
        {
            content = DialogueHelper.GetStringNoPunctuationOrEmoji(content);

            IEnumerable<string> segments = DialogueHelper.SplitContentByPunctuations(content);
            int segmentsCount = segments.Count();
            int segmentIndex = 0;

            foreach (string segment in segments)
            {
                string segmentResult = DialogueHelper.GetStringNoPunctuationOrEmoji(segment);

                segmentIndex++;
                bool isFirst = segmentIndex == 1;
                bool isLast = segmentIndex == segmentsCount;
                OutSegment outSegment = new OutSegment(segmentResult, isFirst, isLast);

                await this.NextWriter!.WriteAsync(new Workflow<OutSegment>(sessionId, outSegment));
            }
        }

        public void Dispose()
        {
            this.NextWriter.Complete();
        }

        private void OnBeforeTokenGenerate(string sessionId)
        {
            this.SendOutter.SendLlmMessageAsync(Emotion.Thinking);
            this.SendOutter.SendSttMessageAsync("思考中...");
        }

        private async void OnTokenGenerating(string sessionId, OutSegment outSegment)
        {
            string segment = DialogueHelper.GetStringNoPunctuationOrEmoji(outSegment.Content);
            await this.NextWriter!.WriteAsync(new Workflow<OutSegment>(sessionId, outSegment));
        }

        private async void OnTokenGenerated(string sessionId, string content)
        {
            this.Logger.Debug("LLM's response text: {content}", content);

            Session session = this.SendOutter.GetSession();
            Dialogue assistantDialogue = new Dialogue(session.DeviceId, session.SessionId, AuthorRole.Assistant, content);
            if (!this._useStreaming)
            {
                await this.SendCustomMessage(sessionId, content);
            }
            session.Dialogues.Add(assistantDialogue);
            await this.SendOutter.SendLlmMessageAsync(Emotion.Winking);
        }
    }
}
