using Serilog;
using SherpaOnnx;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class AudioReceiveHandler : BaseHandler, IInHandler<byte[]>, IOutHandler<CircularBuffer>
    {
        private readonly IVad _vad;
        private readonly IAudioDecoder _audioDecoder;
        private readonly IProtocolEngine _protocolEngine;
        public AudioReceiveHandler(IVad vad, IAudioDecoder audioDecoder, IProtocolEngine protocolEngine, XiaoZhiConfig config, ILogger logger) : base(config, logger)
        {
            this._vad = vad;
            this._audioDecoder = audioDecoder;
            this._protocolEngine = protocolEngine;
        }

        public event Action<Workflow<string>> OnNoVoiceCloseConnect;

        public override string HandlerName => nameof(AudioReceiveHandler);
        public ChannelReader<Workflow<byte[]>> PreviousReader { get; set; }

        public ChannelWriter<Workflow<CircularBuffer>> NextWriter { get; set; }


        public async Task Handle()
        {
            await foreach (var reader in this.PreviousReader.ReadAllAsync()) await this.Handle(reader);
        }
        public async Task Handle(Workflow<byte[]> workflow)
        {
            Session session = this._protocolEngine.GetSessionContext(workflow.SessionId);
            if (session == null || session.ShouldIgnore())
            {
                return;
            }
            try
            {
                float[] pcmData = await this._audioDecoder.DecodeAsync(workflow.Data, session.SessionCtsToken);

                session.SessionCtsToken.ThrowIfCancellationRequested();
                if (session.ListenMode != ListenMode.Manual)
                {
                    session.AudioPacketContext.VadPacket.Push(pcmData);
                }
                session.AudioPacketContext.AsrPackets.Push(pcmData);
                bool haveVoice = false;

                if (session.ListenMode != ListenMode.Manual)
                    haveVoice = await this._vad.AnalysisVoiceAsync(session, session.SessionCtsToken);
                else
                    haveVoice = session.VadStatusContext.HaveVoice;

                if (!haveVoice && !session.VadStatusContext.HaveVoice)
                {
                    session.AudioPacketContext.AsrPackets.Pop(Math.Max(0, session.AudioPacketContext.AsrPackets.Size - 15));
                    this.NoVoiceCloseConnect(session);
                    return;
                }
                this.HandleAudio(session);
            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "receive audio");
            }
        }

        public void HandleAudio(Session sessionContext)
        {
            if (sessionContext.VadStatusContext.VoiceStop)
            {
                sessionContext.SessionCtsToken.ThrowIfCancellationRequested();
                sessionContext.RejectIncomingAudio();

                if (sessionContext.AudioPacketContext.AsrPackets.Size < 15)
                {
                    //音频太短了，无法识别
                    this.Logger.Debug("The voice is too short.");
                    sessionContext.Reset();
                    return;
                }

                this.OnVoiceDetected(sessionContext);
            }
        }

        public void Dispose()
        {
            this.NextWriter.Complete();
        }

        private async void OnVoiceDetected(Session sessionContext)
        {
            sessionContext.AudioPacketContext.VadPacket.Reset();

            Workflow<CircularBuffer> session = sessionContext.ToWorkflow(sessionContext.AudioPacketContext.AsrPackets);
            await this.NextWriter!.WriteAsync(session);
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
                long closeConnectionNoVoiceTime = (this.Config.CloseConnectionNoVoiceTime ?? 120) * 1000;
                if (!sessionContext.CloseAfterChat && noVoiceTime >= closeConnectionNoVoiceTime)
                {

                    sessionContext.CloseAfterChat = true;
                    string prompt = "请你以“时间过得真快”为来头，用富有感情、依依不舍的话来结束这场对话吧。";
                    this.OnNoVoiceCloseConnect.Invoke(sessionContext.ToWorkflow(prompt));
                }
            }
        }
    }
}
