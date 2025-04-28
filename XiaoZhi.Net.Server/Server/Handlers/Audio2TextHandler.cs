using Serilog;
using SherpaOnnx;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class Audio2TextHandler : BaseHandler, IInHandler<CircularBuffer>, IOutHandler<string>
    {
        private readonly IAsr _asr;
        private readonly IPunctuation _punctuation;
        private readonly int _sampleRate;
        private readonly int _frameSize;
        private readonly IProtocolEngine _protocolEngine;

        public Audio2TextHandler(IAsr asr, IPunctuation punctuation, IAudioDecoder audioDecoder, IProtocolEngine protocolEngine, XiaoZhiConfig config, ILogger logger) : base(config, logger)
        {
            this._asr = asr;
            this._punctuation = punctuation;
            this._sampleRate = audioDecoder.SampleRate;
            this._frameSize = audioDecoder.FrameSize;
            this._protocolEngine = protocolEngine;
        }
        public override string HandlerName => nameof(Audio2TextHandler);

        public ChannelReader<Workflow<CircularBuffer>> PreviousReader { get; set; }
        public ChannelWriter<Workflow<string>> NextWriter { get; set; }

        public async Task Handle()
        {
            await foreach (var reader in this.PreviousReader.ReadAllAsync()) await this.Handle(reader);
        }

        public async Task Handle(Workflow<CircularBuffer> workflow)
        {
            Session session = this._protocolEngine.GetSessionContext(workflow.SessionId);
            if (session == null || session.ShouldIgnore())
            {
                return;
            }
            try
            {
                string speechText = await this._asr.ConvertSpeechText(workflow.Data, this._sampleRate, this._frameSize, session.SessionCtsToken);

                if (string.IsNullOrEmpty(DialogueHelper.GetStringNoPunctuationOrEmoji(speechText)))
                {
                    session.Reset();
                    this.Logger.Debug($"Device {session.DeviceId} no speak.");
                    return;
                }
                await this._protocolEngine.SendSttMessageAsync(session.SessionId, speechText);
                this.Logger.Debug($"Device {session.DeviceId} speak the text: {speechText}");
                speechText = await this._punctuation.AppendPunctuationAsync(speechText!, session.SessionCtsToken);

                await this.NextWriter!.WriteAsync(workflow.NextFlow(speechText));
            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "audio to text");
            }
        }

        public void Dispose()
        {
            this.NextWriter.Complete();
        }

    }
}
