using Serilog;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class Text2AudioHandler : BaseHandler, IInHandler<OutSegment>, IOutHandler<float[]>
    {
        private readonly ITts _tts;
        private readonly IProtocolEngine _protocolEngine;
        public Text2AudioHandler(ITts tts, IProtocolEngine protocolEngine, XiaoZhiConfig config, ILogger logger) : base(config, logger)
        {
            this._tts = tts;
            this._protocolEngine = protocolEngine;
            this._tts.OnBeforeProcessing += this.TTS_OnBeforeProcessing;
            this._tts.OnProcessing += this.TTS_OnProcessing;
            this._tts.OnProcessed += this.TTS_OnProcessed;
        }


        public override string HandlerName => nameof(Text2AudioHandler);


        public ChannelReader<Workflow<OutSegment>> PreviousReader { get; set; } = default!;
        public ChannelWriter<Workflow<float[]>> NextWriter { get; set; } = default!;

        public async Task Handle()
        {
            await foreach (var reader in this.PreviousReader.ReadAllAsync()) await this.Handle(reader);
        }

        public async Task Handle(Workflow<OutSegment> workflow)
        {
            Session session = this._protocolEngine.GetSessionContext(workflow.SessionId);
            if (session == null || session.ShouldIgnore())
            {
                return;
            }
            try
            {
                if (string.IsNullOrEmpty(workflow.Data.Content))
                {
                    this.Logger.Information("No tts required, the query text is empty.");
                    return;
                }

                await this._tts.SynthesisAsync(workflow, session, session.SessionCtsToken);

            }
            catch (OperationCanceledException)
            {
                session.SentenceTimeAxisContext.Reset();

                await this._protocolEngine.SendLlmMessageAsync(session.SessionId, Emotion.Confident);
                await this._protocolEngine.SendTtsMessageAsync(session.SessionId, "stop");

                this.FireAbort(session.DeviceId, session.SessionId, "text to audio");
            }
        }
        public void Dispose()
        {
            this.NextWriter.Complete();
        }

        private async void TTS_OnBeforeProcessing(string sessionId, OutSegment segment)
        {
            if (segment.IsFirst)
            {
                this.Logger.Information($"Send the first audio from segment: {segment.Content}");
                await this._protocolEngine.SendTtsMessageAsync(sessionId, "start");
                await this._protocolEngine.SendLlmMessageAsync(sessionId, Emotion.Cool);
            }
            Session session
                = this._protocolEngine.GetSessionContext(sessionId);
            if (session != null)
            {
                Func<Task> sendSentenceAction = new Func<Task>(async () =>
                {
                    if (session.ShouldIgnore())
                    {
                        return;
                    }
                    await this._protocolEngine.SendTtsMessageAsync(sessionId, "sentence_start", segment.Content);
                });
                await session.SentenceTimeAxisContext.AddSendSentenceActionAsync(sendSentenceAction, session.SessionCtsToken);
            }
        }

        private async void TTS_OnProcessing(string sessionId, float[] audioFrame)
        {
            await this.NextWriter.WriteAsync(new Workflow<float[]>(sessionId, audioFrame));
        }

        private async void TTS_OnProcessed(string sessionId, OutSegment segment, int duration)
        {
            Session session
                = this._protocolEngine.GetSessionContext(sessionId);
            if (session != null)
            {
                Func<Task> sendSentenceAction = new Func<Task>(async () =>
                {
                    if (session.ShouldIgnore())
                    {
                        return;
                    }
                    this.Logger.Debug($"duration: {duration}, content: {segment.Content}");
                    await Task.Delay(duration, session.SessionCtsToken);
                    this.Logger.Debug($"duration: {duration}, content: {segment.Content}");
                    await this._protocolEngine.SendTtsMessageAsync(sessionId, "sentence_end", segment.Content);

                    if (segment.IsLast)
                    {
                        session.SentenceTimeAxisContext.Reset();
                        await this._protocolEngine.SendLlmMessageAsync(session.SessionId, Emotion.Confident);
                        await this._protocolEngine.SendTtsMessageAsync(session.SessionId, "stop");

                        if (session.CloseAfterChat)
                            this._protocolEngine.CloseSession(session.SessionId);
                    }
                });
                await session.SentenceTimeAxisContext.AddSendSentenceActionAsync(sendSentenceAction, session.SessionCtsToken);
            }
        }
    }
}
