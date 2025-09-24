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
    internal sealed class AudioReceiveHandler : BaseHandler, IOutHandler<CircularBuffer>
    {
        private readonly IAudioDecoder _audioDecoder;
        private readonly ObjectPool<Workflow<CircularBuffer>> _workflowPool;
        private readonly ObjectPool<Workflow<string>> _stringWorkflowPool;
        private readonly CircularBuffer _receivedPcmPacketFrame;
        private IVad _vad;

        public AudioReceiveHandler([FromKeyedServices(GlobalProviderNames.GLOBAL_VAD)] IVad vad,
            [FromKeyedServices(GlobalProviderNames.GLOBAL_AUDIO_DECODER)] IAudioDecoder audioDecoder,
            ObjectPool<Workflow<CircularBuffer>> workflowPool,
            ObjectPool<Workflow<string>> stringWorkflowPool,
            XiaoZhiConfig config,
            ILogger<AudioReceiveHandler> logger) : base(config, logger)
        {
            this._vad = vad;
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
            if (privateProvider.Vad is not null)
            {
                this._vad = privateProvider.Vad;
            }
            return true;
        }

        public async Task Handle(byte[] opusData)
        {
            Session session = this.SendOutter.GetSession();
            if (!session.IsIdle)
            {
#if DEBUG
                this.Logger.LogDebug("The previous audio packet is processing, this packet would be ignored.");
#endif
                return;
            }
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            try
            {
                float[] pcmData = await this._audioDecoder.DecodeAsync(opusData, session.SessionCtsToken);

                session.SessionCtsToken.ThrowIfCancellationRequested();
                if (session.ListenMode != ListenMode.Manual)
                {
                    session.AudioPacketContext.VadPacket.Push(pcmData);
                }
                this._receivedPcmPacketFrame.Push(pcmData);
                bool haveVoice = false;

                if (session.ListenMode != ListenMode.Manual)
                    haveVoice = await this._vad.AnalysisVoiceAsync(session, session.SessionCtsToken);
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
                session.SessionCtsToken.ThrowIfCancellationRequested();
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

        private async void OnVoiceDetected(Session sessionContext)
        {
            sessionContext.AudioPacketContext.VadPacket.Reset();

            var workflow = this._workflowPool.Get();
            try
            {
                workflow.Initialize(sessionContext.SessionId, this._receivedPcmPacketFrame);
                await this.NextWriter.WriteAsync(workflow);
            }
            finally
            {
                this._workflowPool.Return(workflow);
            }
        }

        private void NoVoiceCloseConnect(Session sessionContext)
        {
            if (sessionContext.VadStatusContext.HaveVoiceLatestTime == 0)
            {
                sessionContext.VadStatusContext.HaveVoiceLatestTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }
            else
            {
                long noVoiceTime = DateTimeOffset.Now.ToUnixTimeMilliseconds() - sessionContext.VadStatusContext.HaveVoiceLatestTime;
                long closeConnectionNoVoiceTime = (this.Config.CloseConnectionNoVoiceTime ?? 40) * 1000;
                if (!sessionContext.CloseAfterChat && noVoiceTime >= closeConnectionNoVoiceTime)
                {
                    sessionContext.CloseAfterChat = true;
                    string prompt = "请你以\"时间过得真快\"为来头，用富有感情、依依不舍的话来结束这场对话吧。";

                    var workflow = this._stringWorkflowPool.Get();
                    try
                    {
                        workflow.Initialize(sessionContext.SessionId, prompt);
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
        }
    }
}
