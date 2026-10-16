using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Common.Constants;
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
        private readonly object _streamingQueueGate = new();

        private const int MinimumSpeechDurationMilliseconds = 200;

        private IVad? _vad;
        private IAudioDecoder? _audioDecoder;
        private IAudioResampler? _inputAudioResampler;
        private IAsr? _asr;
        private Task _streamingOperationTail = Task.CompletedTask;
        private bool _streamingUtteranceActive;
        private long _streamingUtteranceTurnId = -1;
        private bool _audioMonitoringSkipped;
        private int _maxQueuedStreamingAudioFrames = 1;
        private int _queuedStreamingAudioFrames;
        private long _audioDiagnosticStarted = Environment.TickCount64;
        private int _receivedPackets;
        private int _ignoredPackets;
        private int _decodedSamples;
        private double _decodedSquareSum;
        private float _decodedPeak;

        public AudioReceiveHandler(
            ObjectPool<Workflow<float[]>> workflowPool,
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
            this._inputAudioResampler = privateProvider.InputAudioResampler;
            this._audioDecoder.RegisterDevice(session.DeviceId, session.SessionId);

            int frameDuration = Math.Max(1, session.AudioSetting.FrameDuration);
            this._maxQueuedStreamingAudioFrames = Math.Max(1, GlobalVariables.StreamingAsrMaxQueuedAudioMilliseconds / frameDuration);
            this.RegisterCancellationToken();
            this.Builded = true;
            return true;
        }

        public async Task HandleAsync(byte[] opusData)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore() || session.CloseAfterChat)
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
            this.LogAudioDiagnostics(session);
            this._receivedPackets++;
            if (session.IsAudioProcessing)
            {
                this._ignoredPackets++;
                // 播放或处理期间丢弃的音频不会进入 VAD，记录监听已中断，恢复时重置静默计时。
                this._audioMonitoringSkipped = true;
#if DEBUG
                //this.Logger.LogDebug(Lang.AudioReceiveHandler_Handle_PacketIgnored);
#endif
                return;
            }

            try
            {
                // decode the opus data from the client first
                float[] clientPcm = await this._audioDecoder.DecodeAsync(opusData, this.HandlerToken);

                // to resample to 16kHz mono PCM for VAD/ASR processing, if needed
                float[] pcmData = await this.ToProcessingPcmAsync(clientPcm);
                if (this.Logger.IsEnabled(LogLevel.Debug))
                {
                    this._decodedSamples += pcmData.Length;
                    foreach (float sample in pcmData)
                    {
                        this._decodedSquareSum += (double)sample * sample;
                        this._decodedPeak = Math.Max(this._decodedPeak, Math.Abs(sample));
                    }
                }
                this.HandlerToken.ThrowIfCancellationRequested();
                if (session.IsAudioProcessing)
                {
                    this._ignoredPackets++;
                    this._audioMonitoringSkipped = true;
                    return;
                }

                // The retained utterance buffer and all downstream consumers use the
                // same 16 kHz mono PCM stream as VAD/ASR.
                session.AudioPacket.PushAudio(pcmData);

                if (this._asr?.IsStreaming == true && session.IsDeviceBinded)
                {
                    this.HandleStreamingAudio(session, pcmData);
                }
                if (session.ListenMode != ListenMode.Manual)
                {
                    this.ResetVadSilenceBaselineIfSkipped(session);
                    await this._vad.AnalysisVoiceAsync(session.DeviceId, session.SessionId, pcmData, this.HandlerToken);
                }
                else
                {
                    // 手动模式下 VAD 不参与监听，切回自动监听时同样需要重置静默计时。
                    this._audioMonitoringSkipped = true;
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
            if (session is null || session.ShouldIgnore() || session.CloseAfterChat || session.IsAudioProcessing || !session.IsDeviceBinded || this._asr?.IsStreaming != true)
            {
                return;
            }

            this.StartStreamingUtterance(session);
        }

        public void OnVoiceDetected(float[] audioData)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore() || session.CloseAfterChat || session.IsAudioProcessing)
            {
                return;
            }

            session.AudioPacket.VoiceStop = true;

            if (audioData.Length < GlobalVariables.AudioProcessingSampleRate * MinimumSpeechDurationMilliseconds / 1000)
            {
                this.Logger.LogDebug(Lang.AudioReceiveHandler_HandleVoiceDetected_VoiceTooShort, session.SessionId);
                session.AudioPacket.ResetAudioBuffer();
                if (this._asr?.IsStreaming == true && session.IsDeviceBinded)
                {
                    this.AbortStreamingUtterance(session);
                }
                session.Reset();
                return;
            }

            if (this._asr?.IsStreaming == true && session.IsDeviceBinded)
            {
                session.AudioPacket.ResetAudioBuffer();
                if (!this.FinishStreamingUtterance(session))
                {
                    session.Reset();
                }
                return;
            }

            long turnId = session.TurnId;
            session.BeginSpeechRecognition(turnId);
            session.AudioPacket.ResetAudioBuffer();
            this.ObserveTask(this.HandleVoiceDetectedAsync(session, audioData, turnId), Lang.AudioReceiveHandler_OnVoiceDetected_DispatchFailed, session.DeviceId);
        }

        public void OnVoiceSilence()
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore() || session.CloseAfterChat)
            {
                return;
            }
            // VAD 确认起声前仍会触发静默清理，保留与流式 ASR 起声相同长度的预录。
            int preRollSamples = GlobalVariables.AudioProcessingSampleRate * GlobalVariables.StreamingAsrPreRollMilliseconds / 1000;
            session.AudioPacket.TrimOldAudio(preRollSamples);
        }

        public void OnLongTermSilence()
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            if (!session.TryRequestCloseAfterChat())
            {
                return;
            }

            if (this._asr?.IsStreaming == true)
            {
                this.AbortStreamingUtterance(session);
            }

            const string Prompt = "请你以\"时间过得真快\"为来头，用富有感情、依依不舍的话来结束这场对话吧。";
            Workflow<string> workflow = this._stringWorkflowPool.Get();
            try
            {
                workflow.Initialize(session, Prompt);
                this.OnNoVoiceCloseConnect?.Invoke(workflow);
            }
            finally
            {
                this._stringWorkflowPool.Return(workflow);
            }
        }

        public void HandleManualStop(Session session)
        {
            if (session is null || session.ShouldIgnore() || session.CloseAfterChat || session.IsAudioProcessing)
            {
                return;
            }

            if (this._asr?.IsStreaming == true && session.IsDeviceBinded)
            {
                if (!this.FinishStreamingUtterance(session))
                {
                    session.Reset();
                }
                return;
            }

            float[] utteranceAudio = session.AudioPacket.TakeAllAudio();
            long turnId = session.TurnId;
            session.BeginSpeechRecognition(turnId);
            this.ObserveTask(this.HandleVoiceDetectedAsync(session, utteranceAudio, turnId), Lang.AudioReceiveHandler_HandleManualStop_DispatchFailed, session.DeviceId);
        }

        private async Task HandleVoiceDetectedAsync(Session session, float[] audioData, long turnId)
        {
            if (this.HandlerToken.IsCancellationRequested || session.CloseAfterChat)
            {
                return;
            }

            if (audioData.Length < GlobalVariables.AudioProcessingSampleRate * MinimumSpeechDurationMilliseconds / 1000)
            {
                this.Logger.LogDebug(Lang.AudioReceiveHandler_HandleVoiceDetected_VoiceTooShort, session.SessionId);
                if (turnId == session.TurnId)
                {
                    session.Reset();
                }
                else
                {
                    session.AcceptIncomingAudio(turnId);
                }
                return;
            }

            session.RefreshLastActivityTime();
            Workflow<float[]> workflow = this._audioBufferWorkflowPool.Get();
            workflow.Initialize(
                session.SessionId,
                session.DeviceId,
                audioData,
                turnId);
            try
            {
                await this.NextWriter.WriteAsync(workflow, this.HandlerToken);
            }
            catch (OperationCanceledException)
            {
                this._audioBufferWorkflowPool.Return(workflow);
            }
        }

        public override void Dispose()
        {
            if (this.IsDisposed)
            {
                return;
            }
            Session session = this.SendOutter.GetSession();
            if (this._vad is not null)
            {
                if (session is not null)
                {
                    this._vad.UnregisterDevice(session.DeviceId, session.SessionId);
                }
            }
            this.NextWriter?.TryComplete();
            base.Dispose();
        }

        /// <summary>
        /// 会话在播放或其他处理期间会丢弃客户端音频，VAD 也随之停止计时。
        /// 恢复监听时重置 VAD 会话状态，避免把监听中断期间的时间累计进长时间静默判断，
        /// 否则第一帧恢复的音频就会立即触发告别。
        /// </summary>
        private void ResetVadSilenceBaselineIfSkipped(Session session)
        {
            if (!this._audioMonitoringSkipped)
            {
                return;
            }

            this._audioMonitoringSkipped = false;
            this._vad?.ResetSessionState(session.DeviceId, session.SessionId);
        }

        private async Task<float[]> ToProcessingPcmAsync(float[] clientPcm)
        {
            if (this._inputAudioResampler is null || clientPcm.Length == 0)
            {
                return clientPcm;
            }

            (float[] pcmData, _) = await this._inputAudioResampler.ResampleAsync(clientPcm, this.HandlerToken);
            return pcmData;
        }

        private void HandleStreamingAudio(Session session, float[] pcmData)
        {
            if (!this._streamingUtteranceActive)
            {
                if (session.ListenMode == ListenMode.Manual)
                {
                    this.StartStreamingUtterance(session);
                }
                return;
            }

            this.QueueStreamingOperation(
                session,
                pcmData,
                StreamingAsrOperation.Audio,
                this._streamingUtteranceTurnId);
        }

        private void StartStreamingUtterance(Session session)
        {
            if (this._asr is null || this._streamingUtteranceActive || this.HandlerToken.IsCancellationRequested)
            {
                return;
            }

            this._streamingUtteranceActive = true;
            this._streamingUtteranceTurnId = session.TurnId;
            int preRollSamples = GlobalVariables.AudioProcessingSampleRate * GlobalVariables.StreamingAsrPreRollMilliseconds / 1000;
            float[] initialAudio = session.AudioPacket.GetLatestAudio(preRollSamples);
            this.Logger.LogInformation(Lang.AudioReceiveHandler_StartStreamingUtterance_PreRoll, session.SessionId, initialAudio.Length);
            this.QueueStreamingOperation(
                session,
                initialAudio,
                StreamingAsrOperation.Start,
                this._streamingUtteranceTurnId);
        }

        private bool FinishStreamingUtterance(Session session)
        {
            if (this._asr is null || !this._streamingUtteranceActive)
            {
                return false;
            }

            this._streamingUtteranceActive = false;
            session.BeginSpeechRecognition(this._streamingUtteranceTurnId);
            this.QueueStreamingOperation(
                session,
                Array.Empty<float>(),
                StreamingAsrOperation.Finish,
                this._streamingUtteranceTurnId);
            return true;
        }

        private void AbortStreamingUtterance(Session session)
        {
            if (this._asr is null)
            {
                return;
            }

            this._streamingUtteranceActive = false;
            // Abort must be allowed to cancel a Finish that is waiting for a remote final result.
            this.ObserveTask(
                this.SendStreamingOperationAsync(
                    session,
                    Array.Empty<float>(),
                    StreamingAsrOperation.Abort,
                    this._streamingUtteranceTurnId),
                Lang.AudioReceiveHandler_AbortStreamingUtterance_Failed,
                session.DeviceId);
        }

        private void QueueStreamingOperation(
            Session session,
            float[] audioData,
            StreamingAsrOperation operation,
            long turnId)
        {
            if (this._asr is null)
            {
                return;
            }

            if (operation == StreamingAsrOperation.Audio
                && Interlocked.Increment(ref this._queuedStreamingAudioFrames) > this._maxQueuedStreamingAudioFrames)
            {
                Interlocked.Decrement(ref this._queuedStreamingAudioFrames);
                this.Logger.LogWarning(Lang.AudioReceiveHandler_QueueStreamingOperation_QueueOverflow, session.DeviceId);
                this.AbortStreamingUtterance(session);
                return;
            }

            lock (this._streamingQueueGate)
            {
                this._streamingOperationTail = this._streamingOperationTail
                    .ContinueWith(
                        _ => this.SendStreamingOperationSafelyAsync(
                            session,
                            audioData,
                            operation,
                            turnId),
                        CancellationToken.None,
                        TaskContinuationOptions.None,
                        TaskScheduler.Default)
                    .Unwrap();
            }
        }

        private async Task SendStreamingOperationSafelyAsync(
            Session session,
            float[] audioData,
            StreamingAsrOperation operation,
            long turnId)
        {
            try
            {
                await this.SendStreamingOperationAsync(
                    session,
                    audioData,
                    operation,
                    turnId);
            }
            catch (OperationCanceledException)
            {
                if (operation == StreamingAsrOperation.Start)
                {
                    this._streamingUtteranceActive = false;
                    this._streamingUtteranceTurnId = -1;
                }
                if (operation is StreamingAsrOperation.Start or StreamingAsrOperation.Finish)
                {
                    // 起声尚未完成时 Finish 也可能已排队并锁定接收，取消后必须释放该轮状态。
                    session.CompleteSpeechRecognition(turnId);
                    session.AcceptIncomingAudio(turnId);
                }
            }
            catch (Exception ex)
            {
                if (operation == StreamingAsrOperation.Start)
                {
                    this._streamingUtteranceActive = false;
                    this._streamingUtteranceTurnId = -1;
                }
                if (operation is StreamingAsrOperation.Start or StreamingAsrOperation.Finish)
                {
                    session.CompleteSpeechRecognition(turnId);
                    session.AcceptIncomingAudio(turnId);
                }
                this.Logger.LogError(ex, Lang.AudioReceiveHandler_SendStreamingOperationSafelyAsync_Failed, operation, session.DeviceId);
            }
            finally
            {
                if (operation == StreamingAsrOperation.Audio)
                {
                    Interlocked.Decrement(ref this._queuedStreamingAudioFrames);
                }
            }
        }

        private async Task SendStreamingOperationAsync(
            Session session,
            float[] audioData,
            StreamingAsrOperation operation,
            long turnId)
        {
            if (this._asr is null)
            {
                return;
            }

            Workflow<float[]> workflow = this._audioBufferWorkflowPool.Get();
            try
            {
                workflow.Initialize(
                    session.SessionId,
                    session.DeviceId,
                    audioData,
                    turnId);
                await this._asr.ConvertSpeechTextStreamingAsync(
                    workflow,
                    GlobalVariables.AudioProcessingSampleRate,
                    this.GetProcessingFrameSize(session),
                    operation,
                    this.HandlerToken);
            }
            finally
            {
                this._audioBufferWorkflowPool.Return(workflow);
            }
        }

        private int GetProcessingFrameSize(Session session)
        {
            return GlobalVariables.AudioProcessingSampleRate * session.AudioSetting.FrameDuration * GlobalVariables.AudioProcessingChannels / 1000;
        }

        private void LogAudioDiagnostics(Session session)
        {
            long now = Environment.TickCount64;
            long elapsed = now - this._audioDiagnosticStarted;
            if (!this.Logger.IsEnabled(LogLevel.Debug) || elapsed < 1000)
            {
                return;
            }
            double rms = this._decodedSamples == 0 ? 0 : Math.Sqrt(this._decodedSquareSum / this._decodedSamples);
            this.Logger.LogDebug(
                Lang.AudioReceiveHandler_AudioDiagnostics,
                session.SessionId,
                elapsed,
                this._receivedPackets,
                this._ignoredPackets,
                this._decodedSamples,
                rms,
                this._decodedPeak);
            this._audioDiagnosticStarted = now;
            this._receivedPackets = 0;
            this._ignoredPackets = 0;
            this._decodedSamples = 0;
            this._decodedSquareSum = 0;
            this._decodedPeak = 0;
        }

        private void ObserveTask(Task task, string messageTemplate, string deviceId)
        {
            _ = task.ContinueWith(
                completed => this.Logger.LogError(completed.Exception, messageTemplate, deviceId),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }
}
