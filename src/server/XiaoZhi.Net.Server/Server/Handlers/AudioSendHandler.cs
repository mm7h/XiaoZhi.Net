using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Buffers;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;
using XiaoZhi.Net.Server.Server.Common.Enums;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class AudioSendHandler : BaseHandler, IInHandler<OutAudioSegment>
    {
        private readonly IAudioEncoder _audioEncoder;
        private readonly CircularBuffer _sendOpusPacketFrame;

        public AudioSendHandler([FromKeyedServices(GlobalProviderNames.GLOBAL_AUDIO_ENCODER)] IAudioEncoder audioEncoder, XiaoZhiConfig config, ILogger<AudioSendHandler> logger) : base(config, logger)
        {
            this._audioEncoder = audioEncoder;
            this._sendOpusPacketFrame = new CircularBuffer(960 * 100);
        }

        public override string HandlerName => nameof(AudioSendHandler);
        public IBizSendOutter SendOutter { get; set; } = null!;
        public ChannelReader<Workflow<OutAudioSegment>> PreviousReader { get; set; } = null!;

        public async Task Handle()
        {
            await foreach (var reader in this.PreviousReader.ReadAllAsync()) await this.Handle(reader);
        }

        public async Task Handle(Workflow<OutAudioSegment> workflow)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            int frameSize = session.PrivateProvider is not null && session.PrivateProvider.AudioEncoder is not null ? session.PrivateProvider.AudioEncoder.FrameSize : this._audioEncoder.FrameSize;
            int frameDuration = this.Config.AudioSetting.FrameDuration;

            float[] chunk = ArrayPool<float>.Shared.Rent(frameSize);

            OutAudioSegment outAudioSegment = workflow.Data;
            try
            {

                this._sendOpusPacketFrame.Push(outAudioSegment.AudioData);

                if (outAudioSegment.IsFirst)
                {
                    this.Logger.LogInformation("Send the first audio from segment: {content}", outAudioSegment.Content);
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.Start);
                    await this.SendOutter.SendLlmMessageAsync(Emotion.Cool);
                }

                await this.SendOutter.SendTtsMessageAsync(TtsStatus.SentenceStart, outAudioSegment.Content);

                while (this._sendOpusPacketFrame.GetFrames(frameSize, out chunk))
                {
                    session.SessionCtsToken.ThrowIfCancellationRequested();

                    byte[] opusData;
                    if (session.PrivateProvider is not null && session.PrivateProvider.AudioEncoder is not null)
                    {
                        opusData = await session.PrivateProvider.AudioEncoder.EncodeAsync(chunk, session.SessionCtsToken);
                    }
                    else
                    {
                        opusData = await this._audioEncoder.EncodeAsync(chunk, session.SessionCtsToken);
                    }

                    await Task.Delay(frameDuration);
                    await this.SendOutter.SendAsync(opusData);
                }

            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "audio sending");
            }
            finally
            {
                ArrayPool<float>.Shared.Return(chunk);
                this._sendOpusPacketFrame.Reset();

                await this.SendOutter.SendTtsMessageAsync(TtsStatus.SentenceEnd, outAudioSegment.Content);
                if (outAudioSegment.IsLast)
                {
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.Stop);
                    await this.SendOutter.SendLlmMessageAsync(Emotion.Confident);
                }

                if (session.CloseAfterChat)
                {
                    await this.SendOutter.CloseSessionAsync("Close Chat");
                }
            }
        }
    }
}