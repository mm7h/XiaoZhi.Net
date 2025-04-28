using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI.Chat;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Entities;
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
        private readonly IProtocolEngine _protocolEngine;
        private bool _useStreaming;

        public DialogueHandler(ILlm llm, IMemory memory, IProtocolEngine protocolEngine, XiaoZhiConfig config, ILogger logger) : base(config, logger)
        {
            this._llm = llm;
            this._memory = memory;
            this._protocolEngine = protocolEngine;
            this._llm.OnBeforeTokenGenerate += this.OnBeforeTokenGenerate;
            this._llm.OnTokenGenerating += this.OnTokenGenerating;
            this._llm.OnTokenGenerated += this.OnTokenGenerated;
        }

        public override string HandlerName => nameof(DialogueHandler);
        public ChannelReader<Workflow<string>> PreviousReader { get; set; }
        public ChannelWriter<Workflow<OutSegment>> NextWriter { get; set; }

        public void InitializePrompt(Session sessionContext)
        {
            this._useStreaming = this.Config.LlmSetting.Config.UseStreaming ?? false;
            string prompt = this.Config.Prompt.Replace("{date_time}", DateTime.Now.ToString());
            //if (this.Config.UsePrivateConfig)
            //{
            //    //todo UsePrivateConfig
            //}
            Dialogue dialogue = new Dialogue(sessionContext.DeviceId, sessionContext.SessionId, AuthorRole.System, prompt);
            _ = this.AddDialogue(sessionContext.SessionId, sessionContext.DeviceId, dialogue);
        }

        public async Task<bool> AddDialogue(Session sessionContext, AuthorRole role, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            Dialogue dialogue = new Dialogue(sessionContext.DeviceId, sessionContext.SessionId, role, message);
            return await this.AddDialogue(sessionContext.DeviceId, sessionContext.SessionId, dialogue);
        }

        public async Task<bool> AddDialogue(string deviceId, string sessionId, Dialogue dialogue)
        {
            return await this._memory.AppendDialogue(deviceId, sessionId, dialogue);
        }

        public async Task<IEnumerable<Dialogue>> GetDialogues(string deviceId, string sessionId)
        {
            return await this._memory.GetDialogues(deviceId, sessionId);
        }

        public async Task Handle()
        {
            await foreach (var reader in PreviousReader.ReadAllAsync()) await this.Handle(reader);
        }

        public async Task Handle(Workflow<string> workflow, bool addToChatHistory = true)
        {
            Session session = this._protocolEngine.GetSessionContext(workflow.SessionId);
            if (session == null || session.ShouldIgnore())
            {
                return;
            }
            try
            {
                Dialogue dialogue = new Dialogue(session.DeviceId, session.SessionId, AuthorRole.User, workflow.Data);

                if (addToChatHistory)
                {
                    bool addResult = await this.AddDialogue(session.DeviceId, session.SessionId, dialogue);

                    if (!addResult)
                    {
                        this.Logger.Error($"Failed to save dialogue for device: {session.DeviceId}.");
                        return;
                    }
                }

                var dialogues = await this.GetDialogues(session.DeviceId, session.SessionId);

                using (CodeTimer timer = CodeTimer.Create())
                {
                    if (this._useStreaming)
                    {
                        await this._llm.ChatByStreamingAsync(dialogues, workflow, session.SessionCtsToken);
                    }
                    else
                    {
                        await this._llm.ChatAsync(dialogues, workflow, session.SessionCtsToken);
                    }
                    timer.Message = $"Calling the LLM takes {timer.ElapsedMilliseconds} ms.";
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
            this._protocolEngine.SendLlmMessageAsync(sessionId, Emotion.Thinking);
            this._protocolEngine.SendSttMessageAsync(sessionId, "思考中...");
        }

        private async void OnTokenGenerating(string sessionId, OutSegment outSegment)
        {
            string segment = DialogueHelper.GetStringNoPunctuationOrEmoji(outSegment.Content);
            await this.NextWriter!.WriteAsync(new Workflow<OutSegment>(sessionId, outSegment));
        }

        private async void OnTokenGenerated(string sessionId, string content)
        {
            this.Logger.Debug($"LLM's response text: {content}");

            Session session = this._protocolEngine.GetSessionContext(sessionId);
            Dialogue assistantDialogue = new Dialogue(session.DeviceId, session.SessionId, AuthorRole.Assistant, content);
            if (!this._useStreaming)
            {
                await this.SendCustomMessage(sessionId, content);
            }
            await this.AddDialogue(session.DeviceId, session.SessionId, assistantDialogue);
            await this._protocolEngine.SendLlmMessageAsync(session.SessionId, Emotion.Winking);
        }
    }
}
