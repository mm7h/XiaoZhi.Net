using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers;
using XiaoZhi.Net.Server.Providers.ASR.Contexts;
using XiaoZhi.Net.Server.Providers.VAD;

namespace XiaoZhi.Net.Server.Handlers
{
    internal class AudioReceiveHandler : BaseHandler, IOutHandler<float[]>, IVadEventCallback
    {
        private readonly ObjectPool<Workflow<float[]>> _audioBufferWorkflowPool;
        private readonly ObjectPool<Workflow<string>> _stringWorkflowPool;

        private IVad? _vad;
        private IAudioDecoder? _audioDecoder;
        private IAsr? _asr;
        private readonly Queue<float[]> _streamingPreRollFrames = new();
        private bool _streamingUtteranceActive;
        private const int STREAMING_PRE_ROLL_FRAME_COUNT = 10;

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

            this._asr = privateProvider.Asr;

            this._audioDecoder = privateProvider.AudioDecoder;
            this._audioDecoder.RegisterDevice(session.DeviceId, session.SessionId);
            this.RegisterCancellationToken();
            this.Builded = true;
            return true;
        }

        public async Task HandleAsync(byte[] opusData)
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

                if (this._asr?.IsStreaming == true && session.IsDeviceBinded)
                {
                    await this.HandleStreamingAudioAsync(session, pcmData).ConfigureAwait(false);
                }

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

        public void OnVoiceStarted()
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore() || !session.IsDeviceBinded || this._asr?.IsStreaming != true)
            {
                return;
            }

            _ = this.StartStreamingUtteranceAsync(session);
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

            if (this._asr?.IsStreaming == true && session.IsDeviceBinded)
            {
                this.FinishStreamingUtteranceAsync(session);
                return;
            }
            this.HandleVoiceDetectedAsync(session, audioData);
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

            if (this._asr?.IsStreaming == true)
            {
                this.AbortStreamingUtteranceAsync(session);
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

        private async void HandleVoiceDetectedAsync(Session session, float[] audioData)
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

            if (this._asr?.IsStreaming == true && session.IsDeviceBinded)
            {
                this.FinishStreamingUtteranceAsync(session);
                return;
            }

            this.HandleVoiceDetectedAsync(session, session.AudioPacket.GetAllAudio());
        }

        public override void Dispose()
        {
            Session session = this.SendOutter.GetSession();
            if (session is not null && this._asr?.IsStreaming == true)
            {
                this.AbortStreamingUtteranceAsync(session);
            }

            if (this._vad is not null)
            {
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

        private async Task HandleStreamingAudioAsync(Session session, float[] pcmData)
        {
            if (this._asr is null)
            {
                return;
            }

            if (!this._streamingUtteranceActive)
            {
                this.CacheStreamingPreRollFrame(pcmData);
                if (session.ListenMode == ListenMode.Manual)
                {
                    await this.StartStreamingUtteranceAsync(session).ConfigureAwait(false);
                }
                return;
            }

            await this.SendStreamingOperationAsync(session, pcmData, StreamingAsrOperation.Audio).ConfigureAwait(false);
        }

        private async Task StartStreamingUtteranceAsync(Session session)
        {
            if (this._asr is null || this._streamingUtteranceActive || this.HandlerToken.IsCancellationRequested)
            {
                return;
            }

            this._streamingUtteranceActive = true;
            float[] preRollAudio = this.DrainStreamingPreRollAudio();
            try
            {
                await this.SendStreamingOperationAsync(session, preRollAudio, StreamingAsrOperation.Start).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                this._streamingUtteranceActive = false;
            }
            catch (Exception ex)
            {
                this._streamingUtteranceActive = false;
                this.Logger.LogError(ex, "Failed to start streaming ASR for {DeviceId}.", session.DeviceId);
            }
        }

        private async void FinishStreamingUtteranceAsync(Session session)
        {
            if (this._asr is null || !this._streamingUtteranceActive)
            {
                return;
            }

            this._streamingUtteranceActive = false;
            try
            {
                await this.SendStreamingOperationAsync(session, Array.Empty<float>(), StreamingAsrOperation.Finish).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Session cancellation is expected during user abort or disconnect.
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to finish streaming ASR for {DeviceId}.", session.DeviceId);
            }
        }

        private async void AbortStreamingUtteranceAsync(Session session)
        {
            if (this._asr is null)
            {
                return;
            }

            this._streamingUtteranceActive = false;
            this._streamingPreRollFrames.Clear();
            try
            {
                await this.SendStreamingOperationAsync(session, Array.Empty<float>(), StreamingAsrOperation.Abort).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.Logger.LogDebug(ex, "Failed to abort streaming ASR for {DeviceId}.", session.DeviceId);
            }
        }

        private async Task SendStreamingOperationAsync(Session session, float[] audioData, StreamingAsrOperation operation)
        {
            if (this._asr is null)
            {
                return;
            }

            Workflow<float[]> workflow = this._audioBufferWorkflowPool.Get();
            try
            {
                workflow.Initialize(session, audioData);
                await this._asr.ConvertSpeechTextStreamingAsync(workflow, session.AudioSetting.SampleRate, session.AudioSetting.FrameSize, operation, this.HandlerToken).ConfigureAwait(false);
            }
            finally
            {
                this._audioBufferWorkflowPool.Return(workflow);
            }
        }

        private void CacheStreamingPreRollFrame(float[] pcmData)
        {
            this._streamingPreRollFrames.Enqueue(pcmData);
            while (this._streamingPreRollFrames.Count > STREAMING_PRE_ROLL_FRAME_COUNT)
            {
                this._streamingPreRollFrames.Dequeue();
            }
        }

        private float[] DrainStreamingPreRollAudio()
        {
            if (this._streamingPreRollFrames.Count == 0)
            {
                return Array.Empty<float>();
            }

            float[] audio = this._streamingPreRollFrames.SelectMany(frame => frame).ToArray();
            this._streamingPreRollFrames.Clear();
            return audio;
        }
    }
}

