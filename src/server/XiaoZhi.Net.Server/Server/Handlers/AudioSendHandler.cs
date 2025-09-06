using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Buffers;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;
using static System.Collections.Specialized.BitVector32;

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
        public ChannelReader<Workflow<float[]>> PreviousReader2 { get; set; } = null!;

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
            int frameDuration = session.AudioSetting.FrameDuration;

            bool isContentNotEmpty = !string.IsNullOrWhiteSpace(workflow.Data.Content);
            float[] chunk = ArrayPool<float>.Shared.Rent(frameSize);

            OutAudioSegment outAudioSegment = workflow.Data;
            try
            {

                this._sendOpusPacketFrame.Push(outAudioSegment.AudioData);

                if (outAudioSegment.IsFirst)
                {
                    if (isContentNotEmpty)
                    {
                        this.Logger.LogInformation("Send the first audio from the device: {deviceId}, the segment: {content}.", session.DeviceId, outAudioSegment.Content);
                    }
                    else
                    {
                        this.Logger.LogInformation("Send the first audio from the device: {deviceId}.", session.DeviceId);
                    }
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.Start);
                    await this.SendOutter.SendLlmMessageAsync(Emotion.Cool);
                }

                if (isContentNotEmpty)
                {
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.SentenceStart, outAudioSegment.Content);
                }

                while (this._sendOpusPacketFrame.GetFrames(frameSize, out chunk))
                {
                    session.SessionCtsToken.ThrowIfCancellationRequested();

                    if (session.PrivateProvider is not null)
                    {
                        float[]? resampledChunk = null;
                        int? resampledFrameSize = null;
                        if (outAudioSegment.NeedResample && session.PrivateProvider.AudioResampler is not null)
                        {
                            (resampledChunk, resampledFrameSize) = await session.PrivateProvider.AudioResampler.ResampleAsync(chunk, session.SessionCtsToken);

                            frameDuration = resampledFrameSize.HasValue ? resampledFrameSize.Value * 1000 / (session.PrivateProvider.AudioResampler.OutSampleRate * session.PrivateProvider.AudioResampler.Channels) : frameDuration;
                        }

                        if (session.PrivateProvider.AudioEncoder is not null)
                        {
                            byte[] opusData = await session.PrivateProvider.AudioEncoder.EncodeAsync(resampledChunk ?? chunk, session.SessionCtsToken);

                            await Task.Delay(frameDuration);
                            await this.SendOutter.SendAsync(opusData);
                        }
                        else
                        {
                            await this.SendAudioDataByGlobalEncoderAsync(resampledChunk ?? chunk, frameDuration, session.SessionCtsToken);
                        }
                    }
                    else
                    {
                        await this.SendAudioDataByGlobalEncoderAsync(chunk, frameDuration, session.SessionCtsToken);
                    }
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

                if (isContentNotEmpty)
                {
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.SentenceEnd, outAudioSegment.Content);
                }
                if (outAudioSegment.IsLast)
                {
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.Stop);
                    await this.SendOutter.SendLlmMessageAsync(Emotion.Cool);
                }

                if (session.CloseAfterChat)
                {
                    await this.SendOutter.CloseSessionAsync("Close Chat");
                }
            }
        }

        private async Task SendAudioDataByGlobalEncoderAsync(float[] chunk, int frameDuration, CancellationToken token)
        {
            byte[] opusData = await this._audioEncoder.EncodeAsync(chunk, token);

            await Task.Delay(frameDuration);
            await this.SendOutter.SendAsync(opusData);
        }
    }
}