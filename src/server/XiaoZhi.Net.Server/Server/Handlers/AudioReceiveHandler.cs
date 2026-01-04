using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using SherpaOnnx;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal class AudioReceiveHandler : BaseHandler, IOutHandler<CircularBuffer>
    {
        private readonly ObjectPool<Workflow<CircularBuffer>> _workflowPool;
        private readonly ObjectPool<Workflow<string>> _stringWorkflowPool;
        private readonly CircularBuffer _receivedPcmPacketFrame;

        private IVad? _vad;
        private IAudioDecoder? _audioDecoder;

        public AudioReceiveHandler([FromKeyedServices(GlobalProviderNames.GLOBAL_AUDIO_DECODER)] IAudioDecoder audioDecoder,
            ObjectPool<Workflow<CircularBuffer>> workflowPool,
            ObjectPool<Workflow<string>> stringWorkflowPool,
            XiaoZhiConfig config,
            ILogger<AudioReceiveHandler> logger) : base(config, logger)
        {
            this._audioDecoder = audioDecoder;
            this._workflowPool = workflowPool;
            this._stringWorkflowPool = stringWorkflowPool;
            this._receivedPcmPacketFrame = new CircularBuffer(960 * 100);
        }

        public event Action<Workflow<string>>? OnNoVoiceCloseConnect;
        public override string HandlerName => nameof(AudioReceiveHandler);
        public ChannelWriter<Workflow<CircularBuffer>> NextWriter { get; set; } = null!;

        public override bool Build(PrivateProvider privateProvider)
        {
            Session session = this.SendOutter.GetSession();
            if (privateProvider.Vad is null)
            {
                this.Logger.LogError("VAD provider is not configured for the device: {deviceId}.", session.DeviceId);
                return false;
            }

            if (privateProvider.AudioDecoder is null)
            {
                this.Logger.LogError("Audio decoder is not configured for the device: {deviceId}.", session.DeviceId);
                return false;
            }

            this._vad = privateProvider.Vad;
            this._vad.RegisterDevice(session.DeviceId, session.SessionId);

            this._audioDecoder = privateProvider.AudioDecoder;
            this._audioDecoder.RegisterDevice(session.DeviceId, session.SessionId);
            this.RegisterCancellationToken();
            return true;
        }

        public async Task Handle(byte[] opusData)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            if (this._vad is null)
            {
                this.Logger.LogError("VAD provider is not configured for the device: {deviceId}.", session.DeviceId);
                return;
            }
            if (this._audioDecoder is null)
            { 
                this.Logger.LogError("Audio decoder is not configured for the device: {deviceId}.", session.DeviceId);
                return;
            }
            if (!session.IsIdle)
            {
#if DEBUG
                this.Logger.LogDebug("The previous audio packet is processing, this packet would be ignored.");
#endif
                return;
            }
            try
            {
                float[] pcmData = await this._audioDecoder.DecodeAsync(opusData, this.HandlerToken);

                this.HandlerToken.ThrowIfCancellationRequested();
                if (session.ListenMode != ListenMode.Manual)
                {
                    session.AudioPacketContext.VadPacket.Push(pcmData);
                }
                this._receivedPcmPacketFrame.Push(pcmData);
                bool haveVoice = false;

                if (session.ListenMode != ListenMode.Manual)
                    haveVoice = await this._vad.AnalysisVoiceAsync(session, this.HandlerToken);
                else
                    haveVoice = session.VadStatusContext.HaveVoice;

                if (!haveVoice && !session.VadStatusContext.HaveVoice)
                {
                    this._receivedPcmPacketFrame.Pop(Math.Max(0, this._receivedPcmPacketFrame.Size - 50));
                    this.NoVoiceCloseConnect(session);
                    return;
                }
                this.HandleAudio(session);
            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "receive audio");
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to process the message packet from device: {deviceId}.", session.DeviceId);
            }
        }

        public void HandleAudio(Session session)
        {
            if (session.VadStatusContext.VoiceStop)
            {
                this.HandlerToken.ThrowIfCancellationRequested();
                session.RejectIncomingAudio();

                if (this._receivedPcmPacketFrame.Size < 50)
                {
                    //音频太短了，无法识别
                    this.Logger.LogDebug("The voice is too short for the session {sesssionId}.", session.SessionId);
                    session.Reset();
                    return;
                }

                this.OnVoiceDetected(session);
            }
        }

        private async void OnVoiceDetected(Session session)
        {
            session.AudioPacketContext.VadPacket.Reset();

            var workflow = this._workflowPool.Get();
            workflow.Initialize(session, this._receivedPcmPacketFrame);
            await this.NextWriter.WriteAsync(workflow);
        }

        private void NoVoiceCloseConnect(Session session)
        {
            if (session.VadStatusContext.HaveVoiceLatestTime == 0)
            {
                session.VadStatusContext.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }
            else
            {
                long noVoiceTime = DateTimeOffset.Now.ToUnixTimeMilliseconds() - session.VadStatusContext.HaveVoiceLatestTime;
                long closeConnectionNoVoiceTime = (this.Config.CloseConnectionNoVoiceTime ?? 40) * 1000;
                if (!session.CloseAfterChat && noVoiceTime >= closeConnectionNoVoiceTime)
                {
                    session.CloseAfterChat = true;
                    string prompt = "请你以\"时间过得真快\"为来头，用富有感情、依依不舍的话来结束这场对话吧。";

                    var workflow = this._stringWorkflowPool.Get();
                    try
                    {
                        workflow.Initialize(session, prompt);
                        this.OnNoVoiceCloseConnect?.Invoke(workflow);
                    }
                    finally
                    {
                        this._stringWorkflowPool.Return(workflow);
                    }
                }
            }
        }

        public override void Dispose()
        {
            this.NextWriter.Complete();
            base.Dispose();
        }
    }
}
