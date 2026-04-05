using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers;
using XiaoZhi.Net.Server.Providers.VAD;

namespace XiaoZhi.Net.Server.Handlers
{
    internal class AudioReceiveHandler : BaseHandler, IOutHandler<float[]>, IVadEventCallback
    {
        private readonly ObjectPool<Workflow<float[]>> _audioBufferWorkflowPool;
        private readonly ObjectPool<Workflow<string>> _stringWorkflowPool;

        private IVad? _vad;
        private IAudioDecoder? _audioDecoder;

        public AudioReceiveHandler(ObjectPool<Workflow<float[]>> workflowPool,
            ObjectPool<Workflow<string>> stringWorkflowPool,
            XiaoZhiConfig config,
            ILogger<AudioReceiveHandler> logger) : base(config, logger)
        {
            this._audioBufferWorkflowPool = workflowPool;
            this._stringWorkflowPool = stringWorkflowPool;
        }

        public event Action<Workflow<string>>? OnNoVoiceCloseConnect;
        public override string HandlerName => nameof(AudioReceiveHandler);
        public ChannelWriter<Workflow<float[]>> NextWriter { get; set; } = null!;

        public override bool Build(PrivateProvider privateProvider)
        {
            Session session = this.SendOutter.GetSession();
            if (privateProvider.Vad is null)
            {
                this.Logger.LogError(Lang.AudioReceiveHandler_Build_VadNotConfigured, session.DeviceId);
                return false;
            }

            if (privateProvider.AudioDecoder is null)
            {
                this.Logger.LogError(Lang.AudioReceiveHandler_Build_AudioDecoderNotConfigured, session.DeviceId);
                return false;
            }

            this._vad = privateProvider.Vad;
            this._vad.RegisterDevice(session.DeviceId, session.SessionId, this);

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
                this.Logger.LogError(Lang.AudioReceiveHandler_Handle_VadNotConfigured, session.DeviceId);
                return;
            }
            if (this._audioDecoder is null)
            { 
                this.Logger.LogError(Lang.AudioReceiveHandler_Handle_AudioDecoderNotConfigured, session.DeviceId);
                return;
            }
            if (session.IsAudioProcessing)
            {
#if DEBUG
                this.Logger.LogDebug(Lang.AudioReceiveHandler_Handle_PacketIgnored);
#endif
                return;
            }
            try
            {
                float[] pcmData = await this._audioDecoder.DecodeAsync(opusData, this.HandlerToken);

                this.HandlerToken.ThrowIfCancellationRequested();

                session.AudioPacket.PushAudio(pcmData);

                if (session.ListenMode != ListenMode.Manual)
                {
                    await this._vad.AnalysisVoiceAsync(session.DeviceId, session.SessionId, session.AudioPacket.GetAllAudio(), this.HandlerToken);
                }
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogDebug(Lang.AudioReceiveHandler_Handle_Cancelled, session.DeviceId);
            }
            catch (Exception ex)
            {
                session.AudioPacket.Reset();
                this.Logger.LogError(ex, Lang.AudioReceiveHandler_Handle_ProcessFailed, session.DeviceId);
            }
        }

        public void OnVoiceDetected(float[] audioData)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            session.RejectIncomingAudio();
            session.AudioPacket.ResetAudioBuffer();
            session.AudioPacket.VoiceStop = true;
            this.HandleVoiceDetected(session, audioData);
        }

        public void OnVoiceSilence()
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            session.AudioPacket.TrimOldAudio();
        }

        public void OnLongTermSilence()
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            session.AudioPacket.Reset();

            if (session.CloseAfterChat)
            {
                return;
            }

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

        private async void HandleVoiceDetected(Session session, float[] audioData)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }

            if (audioData.Length < 50)
            {
                // Audio too short, cannot recognize
                this.Logger.LogDebug(Lang.AudioReceiveHandler_HandleVoiceDetected_VoiceTooShort, session.SessionId);
                session.Reset();
                return;
            }
            
            session.RefreshLastActivityTime();
            session.AudioPacket.ResetAudioBuffer();
            var workflow = this._audioBufferWorkflowPool.Get();
            workflow.Initialize(session, audioData);
            
            try
            {
                await this.NextWriter.WriteAsync(workflow, this.HandlerToken);
            }
            catch (OperationCanceledException)
            {
                this._audioBufferWorkflowPool.Return(workflow);
            }
        }

        public void HandleManualStop(Session session)
        {
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            this.HandleVoiceDetected(session, session.AudioPacket.GetAllAudio());
        }

        public override void Dispose()
        {
            if (this._vad is not null)
            {
                Session session = this.SendOutter.GetSession();
                if (session is not null)
                {
                    this._vad.UnregisterDevice(session.DeviceId, session.SessionId);
                }
                if (!this._vad.IsSherpaModel)
                {
                    this._vad.Dispose();
                }
            }
            this.NextWriter.Complete();
            base.Dispose();
        }
    }
}

