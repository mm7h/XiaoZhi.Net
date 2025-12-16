using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class DialogueHandler : BaseHandler, IInHandler<string, string>, IOutHandler<OutSegment>
    {
        private readonly ObjectPool<Workflow<string>> _stringWorkflowPool;
        private readonly ObjectPool<Workflow<OutSegment>> _outSegmentWorkflowPool;
        private readonly ObjectPool<OutSegment> _outSegmentPool;

        private ILlm? _llm;

        public DialogueHandler(ObjectPool<Workflow<string>> stringWorkflowPool,
            ObjectPool<Workflow<OutSegment>> outSegmentWorkflowPool,
            ObjectPool<OutSegment> outSegmentPool,
            XiaoZhiConfig config,
            ILogger<DialogueHandler> logger) : base(config, logger)
        {
            this._stringWorkflowPool = stringWorkflowPool;
            this._outSegmentWorkflowPool = outSegmentWorkflowPool;
            this._outSegmentPool = outSegmentPool;
        }

        public override string HandlerName => nameof(DialogueHandler);
        public ChannelReader<Workflow<string>> PreviousReader { get; set; } = null!;
        public ChannelReader<Workflow<string>> PreviousReader2 { get; set; } = null!;
        public ChannelWriter<Workflow<OutSegment>> NextWriter { get; set; } = null!;

        public override bool Build(PrivateProvider privateProvider)
        {
            Session session = this.SendOutter.GetSession();
            if (privateProvider.Llm is null)
            {
                this.Logger.LogError("LLM provider is not configured for the device: {deviceId}.", session.DeviceId);
                return false;
            }

            this._llm = privateProvider.Llm;
            this._llm.OnBeforeTokenGenerate += this.OnBeforeTokenGenerate;
            this._llm.OnTokenGenerating += this.OnTokenGenerating;
            this._llm.OnTokenGenerated += this.OnTokenGenerated;
            this._llm.RegisterDevice(session.DeviceId, session.SessionId);

            return true;
        }

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
            await foreach (var workflow in this.PreviousReader2.ReadAllAsync())
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

        public async Task Handle(Workflow<string> workflow, bool addToChatHistory = true)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            if (this._llm is null)
            {
                this.Logger.LogError("LLM provider is not configured for the device: {deviceId}.", session.DeviceId);
                return;
            }

            if (!session.IsDeviceBinded)
            {
                var outSegment = this._outSegmentPool.Get();
                var notBindWorkflow = this._outSegmentWorkflowPool.Get();

                outSegment.Initialize("NOT_BIND", true, true, Emotion.Neutral);
                notBindWorkflow.Initialize(workflow.SessionId, workflow.DeviceId, outSegment);
                await this.NextWriter.WriteAsync(notBindWorkflow);
                return;
            }

            try
            {
                using (CodeTimer timer = CodeTimer.Create("Calling the LLM takes {elapsed:F2} ms.", this.Logger))
                {
                    if (this._llm.UseStreaming)
                    {
                        await this._llm.ChatByStreamingAsync(workflow.Data, session.SessionCtsToken);
                    }
                    else
                    {
                        await this._llm.ChatAsync(workflow.Data, session.SessionCtsToken);
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

        public async Task SendCustomMessage(string sessionId, string deviceId, IEnumerable<OutSegment> segments)
        {

            foreach (OutSegment segment in segments)
            {
                var workflow = this._outSegmentWorkflowPool.Get();

                workflow.Initialize(sessionId, deviceId, segment);
                await this.NextWriter.WriteAsync(workflow);
            }
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
            Session session = this.SendOutter.GetSession();
            workflow.Initialize(session.SessionId, session.DeviceId, outSegment);
            await this.NextWriter.WriteAsync(workflow);
        }

        private async void OnTokenGenerated(IEnumerable<OutSegment> outSegments)
        {
            if (this._llm is null)
            {
                this.Logger.LogError("The LLM model is not initialized.");
                return;
            }

            this.Logger.LogDebug("LLM's response text: {content}", string.Join(string.Empty, outSegments.Select(o => o.Content)));

            if (!this._llm.UseStreaming)
            {
                Session session = this.SendOutter.GetSession();
                await this.SendCustomMessage(session.SessionId, session.DeviceId, outSegments);
            }

            await this.SendOutter.SendLlmMessageAsync(Emotion.Winking);
        }

        public override void Dispose()
        {
            if (this._llm is not null)
            {
                this._llm.OnBeforeTokenGenerate -= this.OnBeforeTokenGenerate;
                this._llm.OnTokenGenerating -= this.OnTokenGenerating;
                this._llm.OnTokenGenerated -= this.OnTokenGenerated;
            }
            this.NextWriter.Complete();
        }
    }
}
