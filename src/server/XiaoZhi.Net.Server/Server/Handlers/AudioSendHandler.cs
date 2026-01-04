using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Media.Abstractions.Dtos;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal class AudioSendHandler : BaseHandler, IInHandler<MixedAudioPacket>
    {
        private readonly ObjectPool<MixedAudioPacket> _mixedAudioPacketPool;
        private readonly ObjectPool<Workflow<MixedAudioPacket>> _mixedAudioPacketWorkflowPool;

        private IAudioProcessor? _audioProcessor;
        private IAudioEncoder? _audioEncoder;

        public AudioSendHandler(ObjectPool<MixedAudioPacket> mixedAudioPacketPool, ObjectPool<Workflow<MixedAudioPacket>> mixedAudioPacketWorkflowPool, XiaoZhiConfig config, ILogger<AudioSendHandler> logger) : base(config, logger)
        {
            this._mixedAudioPacketPool = mixedAudioPacketPool;
            this._mixedAudioPacketWorkflowPool = mixedAudioPacketWorkflowPool;

        }
        public override string HandlerName => nameof(AudioSendHandler);

        public ChannelReader<Workflow<MixedAudioPacket>> PreviousReader { get; set; } = null!;

        public override bool Build(PrivateProvider privateProvider)
        {
            Session session = this.SendOutter.GetSession();

            if (privateProvider.AudioProcessor is null)
            {
                this.Logger.LogError("Audio processor is not configured for the device: {deviceId}.", session.DeviceId);
                return false;
            }

            if (privateProvider.AudioEncoder is null)
            {
                this.Logger.LogError("Audio encoder is not configured for the device: {deviceId}.", session.DeviceId);
                return false;
            }

            this._audioProcessor = privateProvider.AudioProcessor;
            this._audioEncoder = privateProvider.AudioEncoder;
            this.RegisterCancellationToken();
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
                    this._mixedAudioPacketPool.Return(workflow.Data);
                    this._mixedAudioPacketWorkflowPool.Return(workflow);
                }
            }
        }

        public async Task Handle(Workflow<MixedAudioPacket> workflow)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            if (this._audioProcessor is null)
            {
                this.Logger.LogError("Audio processor is not configured for the device: {deviceId}.", session.DeviceId);
                return;
            }
            if (this._audioEncoder is null)
            {
                this.Logger.LogError("Audio encoder is not configured for the device: {deviceId}.", session.DeviceId);
                return;
            }
            try
            {
                MixedAudioPacket audioPacket = workflow.Data;

                if (!string.IsNullOrEmpty(audioPacket.SentenceId) && this._audioProcessor.GetSubtitle(audioPacket.SentenceId, out AudioSubtitle subtitle))
                {
                    await this.SendOutter.SendTtsMessageAsync(subtitle.TtsStatus, subtitle.SubtitleText);
                    await this.SendOutter.SendLlmMessageAsync(subtitle.Emotion);
                }

                if (audioPacket.IsFirstFrame)
                {
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.Start);
                    this.Logger.LogDebug("Send the first audio frame from the device: {deviceId}.", session.DeviceId);
                }

                if (audioPacket.Data is not null && audioPacket.Data.Length > 0)
                {
                    byte[] opusData = await this._audioEncoder.EncodeAsync(audioPacket.Data, this.HandlerToken);
                    await this.SendOutter.SendAsync(opusData);
                }

                if (audioPacket.IsLastFrame)
                {
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.Stop);
                    this.Logger.LogDebug("Send the last audio frame from the device: {deviceId}.", session.DeviceId);
                    if (session.CloseAfterChat)
                    {
                        await this.SendOutter.CloseSessionAsync("Close Chat");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "audio sending");
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to process the audio packet from device: {deviceId}.", session.DeviceId);
            }
        }

    }
}

