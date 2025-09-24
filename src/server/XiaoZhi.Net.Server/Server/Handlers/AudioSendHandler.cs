using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;

namespace XiaoZhi.Net.Server.Handlers
{
    internal class AudioSendHandler : BaseHandler, IInHandler<MixedAudioPacket>
    {
        private readonly ObjectPool<MixedAudioPacket> _mixedAudioPacketPool;
        private readonly ObjectPool<Workflow<MixedAudioPacket>> _mixedAudioPacketWorkflowPool;
        public AudioSendHandler(ObjectPool<MixedAudioPacket> mixedAudioPacketPool, ObjectPool<Workflow<MixedAudioPacket>> mixedAudioPacketWorkflowPool, XiaoZhiConfig config, ILogger<AudioMixingHandler> logger) : base(config, logger)
        {
            this._mixedAudioPacketPool = mixedAudioPacketPool;
            this._mixedAudioPacketWorkflowPool = mixedAudioPacketWorkflowPool;

        }
        public override string HandlerName => nameof(AudioSendHandler);

        public ChannelReader<Workflow<MixedAudioPacket>> PreviousReader { get; set; } = null!;

        public override bool Build(PrivateProvider privateProvider)
        {
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

            try
            {
                MixedAudioPacket audioPacket = workflow.Data;

                if (audioPacket.IsFirstFrame)
                {
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.Start);
                    await this.SendOutter.SendLlmMessageAsync(Emotion.Cool);
                    this.Logger.LogInformation("Send the first audio from the device: {deviceId}.", session.DeviceId);
                }

                byte[] opusData = await session.PrivateProvider.AudioEncoder!.EncodeAsync(audioPacket.Data, session.SessionCtsToken);
                await this.SendOutter.SendAsync(opusData);
                await Task.Delay(session.AudioSetting.FrameDuration);

                if (audioPacket.IsLastFrame)
                {
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.Stop);
                    await this.SendOutter.SendLlmMessageAsync(Emotion.Cool);
                    this.Logger.LogInformation("Send the last audio from the device: {deviceId}.", session.DeviceId);
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
        }
    }
}
