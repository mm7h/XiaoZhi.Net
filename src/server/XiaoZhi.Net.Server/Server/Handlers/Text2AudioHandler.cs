using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class Text2AudioHandler : BaseHandler, IInHandler<OutSegment>, IOutHandler<OutAudioSegment>
    {
        private readonly ITts _tts;
        private bool _privateTTSInitialized = false;

        public Text2AudioHandler([FromKeyedServices(GlobalProviderNames.GLOBAL_TTS)] ITts tts, XiaoZhiConfig config, ILogger<Text2AudioHandler> logger) : base(config, logger)
        {
            this._tts = tts;
            this._tts.OnBeforeProcessing += this.TTS_OnBeforeProcessing;
            this._tts.OnProcessed += this.TTS_OnProcessed;
        }


        public override string HandlerName => nameof(Text2AudioHandler);
        public IBizSendOutter SendOutter { get; set; } = null!;
        public ChannelReader<Workflow<OutSegment>> PreviousReader { get; set; } = null!;
        public ChannelWriter<Workflow<OutAudioSegment>> NextWriter { get; set; } = null!;

        public async Task Handle()
        {
            await foreach (var reader in this.PreviousReader.ReadAllAsync()) await this.Handle(reader);
        }

        public async Task Handle(Workflow<OutSegment> workflow)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            try
            {
                if (string.IsNullOrEmpty(workflow.Data.Content))
                {
                    this.Logger.LogInformation("No tts required, the query text is empty.");
                    return;
                }

                if (session.PrivateProvider is not null && session.PrivateProvider.Tts is not null)
                {
                    if (!this._privateTTSInitialized)
                    {
                        session.PrivateProvider.Tts.OnBeforeProcessing += this.TTS_OnBeforeProcessing;
                        session.PrivateProvider.Tts.OnProcessed += this.TTS_OnProcessed;
                        this._privateTTSInitialized = true;
                    }
                    await session.PrivateProvider.Tts.SynthesisAsync(workflow, session, session.SessionCtsToken);
                }
                else
                {
                    await this._tts.SynthesisAsync(workflow, session, session.SessionCtsToken);
                }

            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "text to audio");
            }
        }
        public void Dispose()
        {
            this._tts.OnBeforeProcessing -= this.TTS_OnBeforeProcessing;
            this._tts.OnProcessed -= this.TTS_OnProcessed;
            Session session = this.SendOutter.GetSession();
            if (session is not null && session.PrivateProvider is not null && session.PrivateProvider.Tts is not null && this._privateTTSInitialized)
            {
                session.PrivateProvider.Tts.OnBeforeProcessing -= this.TTS_OnBeforeProcessing;
                session.PrivateProvider.Tts.OnProcessed -= this.TTS_OnProcessed;
            }
            this.NextWriter.Complete();
        }

        private void TTS_OnBeforeProcessing(string sessionId, OutSegment segment)
        {
            if (segment.IsFirst)
            {
                this.Logger.LogInformation("Send the first audio from segment: {content}", segment.Content);
            }
        }

        private void TTS_OnProcessed(string sessionId, float[] audioData, OutSegment segment, double duration)
        {
            OutAudioSegment outAudioSegment = new OutAudioSegment(audioData, duration, segment);
            this.NextWriter.WriteAsync(new Workflow<OutAudioSegment>(sessionId, outAudioSegment));
        }
    }
}
