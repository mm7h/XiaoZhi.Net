using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class DialogueHandler : BaseHandler, IInHandler<string, string>, IOutHandler<OutSegment>
    {
        private readonly ILlm _llm;
        private readonly IMemory _memory;
        private readonly ObjectPool<Workflow<string>> _stringWorkflowPool;
        private readonly ObjectPool<Workflow<OutSegment>> _outSegmentWorkflowPool;
        private readonly ObjectPool<OutSegment> _outSegmentPool;
        private bool _useStreaming;

        public DialogueHandler([FromKeyedServices(GlobalProviderNames.GLOBAL_LLM)] ILlm llm, 
            [FromKeyedServices(GlobalProviderNames.GLOBAL_MEMORY)] IMemory memory,
            ObjectPool<Workflow<string>> stringWorkflowPool,
            ObjectPool<Workflow<OutSegment>> outSegmentWorkflowPool,
            ObjectPool<OutSegment> outSegmentPool,
            XiaoZhiConfig config, 
            ILogger<DialogueHandler> logger) : base(config, logger)
        {
            this._llm = llm;
            this._useStreaming = this.Config.LlmSettings.First().Config.UseStreaming ?? false;
            this._memory = memory;
            this._stringWorkflowPool = stringWorkflowPool;
            this._outSegmentWorkflowPool = outSegmentWorkflowPool;
            this._outSegmentPool = outSegmentPool;
            this._llm.OnBeforeTokenGenerate += this.OnBeforeTokenGenerate;
            this._llm.OnTokenGenerating += this.OnTokenGenerating;
            this._llm.OnTokenGenerated += this.OnTokenGenerated;
        }

        public override string HandlerName => nameof(DialogueHandler);
        public IBizSendOutter SendOutter { get; set; } = null!;
        public ChannelReader<Workflow<string>> PreviousReader { get; set; } = null!;
        public ChannelReader<Workflow<string>> PreviousReader2 { get; set; } = null!;
        public ChannelWriter<Workflow<OutSegment>> NextWriter { get; set; } = null!;

        public async Task Handle()
        {
            await foreach (var workflow in this.PreviousReader.ReadAllAsync())
            {
                try
                {
                    await this.Handle(workflow);
                }
                finally
                {
                    this._stringWorkflowPool.Return(workflow);
                }
            }
        }

        public async Task Handle2()
        {
            await foreach (var reader in this.PreviousReader2.ReadAllAsync()) await this.Handle(reader);
        }

        public async Task Handle(Workflow<string> workflow, bool addToChatHistory = true)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            
            if (!session.IsDeviceBinded)
            {
                // 从对象池获取OutSegment对象
                var outSegment = this._outSegmentPool.Get();
                var notBindWorkflow = this._outSegmentWorkflowPool.Get();

                outSegment.Initialize("NOT_BIND", true, true);
                notBindWorkflow.Initialize(workflow.SessionId, outSegment);
                await this.NextWriter.WriteAsync(notBindWorkflow);
                return;
            }
            
            try
            {
                Dialogue dialogue = new Dialogue(session.DeviceId, session.SessionId, AuthorRole.User, workflow.Data);
                session.Dialogues.Add(dialogue);

                using (CodeTimer timer = CodeTimer.Create("Calling the LLM takes {elapsed:F2} ms.", this.Logger))
                {
                    DialogueContext dialogueContext = new DialogueContext(session.SessionId, session.PrivateProvider.Kernel, session.PrivateProvider.LlmModelName, session.Dialogues);

                    bool useStreaming = session.PrivateProvider.UseStreaming || this._useStreaming;

                    if (useStreaming)
                    {
                        await this._llm.ChatByStreamingAsync(dialogueContext, session.SessionCtsToken);
                    }
                    else
                    {
                        await this._llm.ChatAsync(dialogueContext, session.SessionCtsToken);
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

            List<OutSegment> outSegments = new List<OutSegment>();
            List<Workflow<OutSegment>> workflows = new List<Workflow<OutSegment>>();

            foreach (string segment in segments)
            {
                string segmentResult = DialogueHelper.GetStringNoPunctuationOrEmoji(segment);

                segmentIndex++;
                bool isFirst = segmentIndex == 1;
                bool isLast = segmentIndex == segmentsCount;

                // 从对象池获取对象
                var outSegment = this._outSegmentPool.Get();
                var workflow = this._outSegmentWorkflowPool.Get();

                outSegments.Add(outSegment);
                workflows.Add(workflow);

                outSegment.Initialize(segmentResult, isFirst, isLast);
                workflow.Initialize(sessionId, outSegment);
                await this.NextWriter.WriteAsync(workflow);
            }
        }

        public void Dispose()
        {
            this._llm.OnBeforeTokenGenerate -= this.OnBeforeTokenGenerate;
            this._llm.OnTokenGenerating -= this.OnTokenGenerating;
            this._llm.OnTokenGenerated -= this.OnTokenGenerated;
            this.NextWriter.Complete();
        }

        private void OnBeforeTokenGenerate()
        {
            this.SendOutter.SendLlmMessageAsync(Emotion.Thinking);
            this.SendOutter.SendSttMessageAsync("思考中...");
        }

        private async void OnTokenGenerating(OutSegment outSegment)
        {
            string segment = DialogueHelper.GetStringNoPunctuationOrEmoji(outSegment.Content);

            var workflow = this._outSegmentWorkflowPool.Get();
            workflow.Initialize(this.SendOutter.SessionId, outSegment);
            await this.NextWriter.WriteAsync(workflow);
        }

        private async void OnTokenGenerated(string content)
        {
            this.Logger.LogDebug("LLM's response text: {content}", content);

            Session session = this.SendOutter.GetSession();
            Dialogue assistantDialogue = new Dialogue(session.DeviceId, session.SessionId, AuthorRole.Assistant, content);
            if (!this._useStreaming)
            {
                await this.SendCustomMessage(this.SendOutter.SessionId, content);
            }
            session.Dialogues.Add(assistantDialogue);
            await this.SendOutter.SendLlmMessageAsync(Emotion.Winking);
        }
    }
}
