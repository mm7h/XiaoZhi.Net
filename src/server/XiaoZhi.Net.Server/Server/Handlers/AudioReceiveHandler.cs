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

        private IVad? _vad;
        private IAudioDecoder? _audioDecoder;
        private IAudioResampler? _inputAudioResampler;
        private IAsr? _asr;
        private Task _streamingOperationTail = Task.CompletedTask;
        private bool _streamingUtteranceActive;
        private int _maxQueuedStreamingAudioFrames = 1;
        private int _queuedStreamingAudioFrames;

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
                // decode the opus data from the client first
                float[] clientPcm = await this._audioDecoder.DecodeAsync(opusData, this.HandlerToken);

                // to resample to 16kHz mono PCM for VAD/ASR processing, if needed
                float[] pcmData = await this.ToProcessingPcmAsync(clientPcm);
                this.HandlerToken.ThrowIfCancellationRequested();

                // The retained utterance buffer and all downstream consumers use the
                // same 16 kHz mono PCM stream as VAD/ASR.
                session.AudioPacket.PushAudio(pcmData);

                if (this._asr?.IsStreaming == true && session.IsDeviceBinded)
                {
                    this.HandleStreamingAudio(session, pcmData);
                }
                if (session.ListenMode != ListenMode.Manual)
                {
                    await this._vad.AnalysisVoiceAsync(session.DeviceId, session.SessionId, pcmData, this.HandlerToken);
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

            this.StartStreamingUtterance(session);
        }

        public void OnVoiceDetected(float[] _)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            session.RejectIncomingAudio();
            session.AudioPacket.VoiceStop = true;

            if (this._asr?.IsStreaming == true && session.IsDeviceBinded)
            {
                session.AudioPacket.ResetAudioBuffer();
                this.FinishStreamingUtterance(session);
                return;
            }

            float[] utteranceAudio = session.AudioPacket.TakeAllAudio();
            this.ObserveTask(this.HandleVoiceDetectedAsync(session, utteranceAudio), Lang.AudioReceiveHandler_OnVoiceDetected_DispatchFailed, session.DeviceId);
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
                this.AbortStreamingUtterance(session);
            }

            session.AudioPacket.Reset();
            if (session.CloseAfterChat)
            {
                return;
            }

            session.CloseAfterChat = true;
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
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            if (this._asr?.IsStreaming == true && session.IsDeviceBinded)
            {
                this.FinishStreamingUtterance(session);
                return;
            }

            float[] utteranceAudio = session.AudioPacket.TakeAllAudio();
            this.ObserveTask(this.HandleVoiceDetectedAsync(session, utteranceAudio), Lang.AudioReceiveHandler_HandleManualStop_DispatchFailed, session.DeviceId);
        }

        private async Task HandleVoiceDetectedAsync(Session session, float[] audioData)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }

            if (audioData.Length < 50)
            {
                this.Logger.LogDebug(Lang.AudioReceiveHandler_HandleVoiceDetected_VoiceTooShort, session.SessionId);
                session.Reset();
                return;
            }

            session.RefreshLastActivityTime();
            Workflow<float[]> workflow = this._audioBufferWorkflowPool.Get();
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

        public override void Dispose()
        {
            Session session = this.SendOutter.GetSession();
            if (session is not null && this._asr?.IsStreaming == true)
            {
                this.AbortStreamingUtterance(session);
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

            this.QueueStreamingOperation(session, pcmData, StreamingAsrOperation.Audio);
        }

        private void StartStreamingUtterance(Session session)
        {
            if (this._asr is null || this._streamingUtteranceActive || this.HandlerToken.IsCancellationRequested)
            {
                return;
            }

            this._streamingUtteranceActive = true;
            int preRollSamples = GlobalVariables.AudioProcessingSampleRate * GlobalVariables.StreamingAsrPreRollMilliseconds / 1000;
            this.QueueStreamingOperation(
                session,
                session.AudioPacket.GetLatestAudio(preRollSamples),
                StreamingAsrOperation.Start);
        }

        private void FinishStreamingUtterance(Session session)
        {
            if (this._asr is null || !this._streamingUtteranceActive)
            {
                return;
            }

            this._streamingUtteranceActive = false;
            this.QueueStreamingOperation(session, Array.Empty<float>(), StreamingAsrOperation.Finish);
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
                this.SendStreamingOperationAsync(session, Array.Empty<float>(), StreamingAsrOperation.Abort),
                Lang.AudioReceiveHandler_AbortStreamingUtterance_Failed,
                session.DeviceId);
        }

        private void QueueStreamingOperation(Session session, float[] audioData, StreamingAsrOperation operation)
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
                        _ => this.SendStreamingOperationSafelyAsync(session, audioData, operation),
                        CancellationToken.None,
                        TaskContinuationOptions.None,
                        TaskScheduler.Default)
                    .Unwrap();
            }
        }

        private async Task SendStreamingOperationSafelyAsync(Session session, float[] audioData, StreamingAsrOperation operation)
        {
            try
            {
                await this.SendStreamingOperationAsync(session, audioData, operation);
            }
            catch (OperationCanceledException)
            {
                if (operation == StreamingAsrOperation.Start)
                {
                    this._streamingUtteranceActive = false;
                }
            }
            catch (Exception ex)
            {
                if (operation == StreamingAsrOperation.Start)
                {
                    this._streamingUtteranceActive = false;
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
