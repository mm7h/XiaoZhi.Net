using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
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

        public AudioReceiveHandler([FromKeyedServices(GlobalProviderNames.GLOBAL_AUDIO_DECODER)] IAudioDecoder audioDecoder,
            ObjectPool<Workflow<float[]>> workflowPool,
            ObjectPool<Workflow<string>> stringWorkflowPool,
            XiaoZhiConfig config,
            ILogger<AudioReceiveHandler> logger) : base(config, logger)
        {
            this._audioDecoder = audioDecoder;
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
                this.Logger.LogError("VAD provider is not configured for the device: {deviceId}.", session.DeviceId);
                return false;
            }

            if (privateProvider.AudioDecoder is null)
            {
                this.Logger.LogError("Audio decoder is not configured for the device: {deviceId}.", session.DeviceId);
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

                session.AudioPacket.PushAudio(pcmData);

                if (session.ListenMode != ListenMode.Manual)
                {
                    await this._vad.AnalysisVoiceAsync(session.DeviceId, session.SessionId, session.AudioPacket.GetAllAudio(), this.HandlerToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                this.Logger.LogError(ex, "Failed to process the audio packet from device: {deviceId}.", session.DeviceId);
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

            // Trim old audio data to reduce memory pressure during long silence
            session.AudioPacket.TrimOldAudio();

            // Handle long term silence - close connection
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

        private void HandleVoiceDetected(Session session, float[] audioData)
        {
            this.HandlerToken.ThrowIfCancellationRequested();

            if (audioData.Length < 50)
            {
                // Audio too short, cannot recognize
                this.Logger.LogDebug("The voice is too short for the session {sesssionId}.", session.SessionId);
                session.Reset();
                return;
            }

            session.AudioPacket.ResetAudioBuffer();
            var workflow = this._audioBufferWorkflowPool.Get();
            workflow.Initialize(session, audioData); 
            this.NextWriter.WriteAsync(workflow);
        }

        /// <summary>
        /// Handles manual stop from TextHandler.
        /// </summary>
        /// <param name="session">The session.</param>
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
            this.NextWriter.Complete();
            base.Dispose();
        }
    }
}
