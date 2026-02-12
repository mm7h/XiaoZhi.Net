using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Protocol.WebSocket;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Enums;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Models;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan
{
    internal abstract class HuoshanStreamTTS<TLogger> : BaseHuoshanTTS<TLogger>
    {

        private readonly object _audioBufferLock = new();
        private readonly Dictionary<string, List<float>> _sessionAudioBuffers = new();
        private readonly List<PendingWait> _waits = new();
        private readonly object _waitsLock = new();
        private static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(15);

        public HuoshanStreamTTS(IAudioEditor audioEditor, ILogger<TLogger> logger) : base(audioEditor, logger)
        {
            this.ProcessingSegments = new ConcurrentDictionary<string, OutSegment>();
        }

        protected WebSocketClient? WebSocketClient { get; set; }
        protected IDictionary<string, OutSegment> ProcessingSegments { get; }
        protected bool StreamingActive { get; set; } = false;

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                string? appId = modelSetting.Config.GetConfigValueOrDefault("AppId");
                string? accessToken = modelSetting.Config.GetConfigValueOrDefault("AccessToken");
                string? resourceId = modelSetting.Config.GetConfigValueOrDefault("ResourceId");
                string? speaker = modelSetting.Config.GetConfigValueOrDefault("Speaker");

                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(resourceId) || string.IsNullOrEmpty(speaker))
                {
                    this.Logger.LogWarning(Lang.HuoshanStreamTTS_Build_ConfigIncomplete);
                    return false;
                }
                this.SpeakerId = speaker;
                this.SpeechRate = modelSetting.Config.GetConfigValueOrDefault("SpeechRate", 0);
                this.LoudnessRate = modelSetting.Config.GetConfigValueOrDefault("LoudnessRate", 0);
                this.BuildAudioSavingConfig(modelSetting);

                IDictionary<string, string> headers = new Dictionary<string, string>
                {
                    { "X-Api-App-Key", appId },
                    { "X-Api-Access-Key", accessToken },
                    { "X-Api-Resource-Id", resourceId },
                    { "X-Api-Connect-Id", Guid.NewGuid().ToString() }
                };
                this.WebSocketClient = new WebSocketClient(headers);
                this.WebSocketClient.OnOpen += this.WebSocketClient_OnOpen;
                this.WebSocketClient.OnBinaryMessage += this.WebSocketClient_OnBinaryMessage;
                this.WebSocketClient.OnClose += this.WebSocketClient_OnClose;
                this.WebSocketClient.OnError += this.WebSocketClient_OnError;

                this.Logger.LogInformation(Lang.HuoshanStreamTTS_Build_Built, this.ProviderType, this.ModelName);

                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.HuoshanStreamTTS_Build_Failed, this.ModelName);
                return false;
            }
        }

        #region Huoshan TTS services API
        protected async Task ConnectAsync(string endPoint, CancellationToken token)
        {
            if (this.WebSocketClient is null)
            {
                this.Logger.LogError(Lang.HuoshanStreamTTS_ConnectAsync_ClientNotInitLog);
                throw new InvalidOperationException(Lang.HuoshanStreamTTS_ConnectAsync_ClientNotInitEx);
            }
            await this.WebSocketClient.ConnectAsync(endPoint, token);
        }
        protected async Task TaskRequestAsync(object ttsReq)
        {
            var message = Message.Create(MsgType.FullClientRequest, MsgTypeFlagBits.NoSeq);
            message.Payload = JsonHelper.SerializeToUtf8Bytes(ttsReq);
            await this.SendMessage(message);
        }

        protected async Task TaskRequestAsync(string sessionId, byte[] payload)
        {
            var message = Message.Create(MsgType.FullClientRequest, MsgTypeFlagBits.WithEvent);
            message.EventType = EventType.TaskRequest;
            message.SessionId = sessionId;
            message.Payload = payload;
            await this.SendMessage(message);
        }

        protected async Task SendMessage(Message message)
        {
            if (this.WebSocketClient is null)
            {
                return;
            }
            var data = message.Marshal();
            await this.WebSocketClient.SendAsync(data);
        }

        protected void StartNewAudioBuffer(string sessionId)
        {
            if (this.AudioSavingConfig is null || !this.AudioSavingConfig.SaveFile || string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            lock (this._audioBufferLock)
            {
                if (!this._sessionAudioBuffers.ContainsKey(sessionId))
                {
                    this._sessionAudioBuffers[sessionId] = new List<float>();
                }
            }
        }

        protected void AppendAudioPayloadChunk(string sessionId, byte[] audioData)
        {
            if (this.AudioSavingConfig is null || !this.AudioSavingConfig.SaveFile || string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            float[] pcmData = audioData.PcmBytesToFloat(16);
            if (pcmData.Length == 0)
            {
                return;
            }

            lock (this._audioBufferLock)
            {
                if (!this._sessionAudioBuffers.TryGetValue(sessionId, out var buffer))
                {
                    buffer = new List<float>();
                    this._sessionAudioBuffers[sessionId] = buffer;
                }

                buffer.AddRange(pcmData);
            }
        }
        protected async Task<Message> StartConnectionAsync(CancellationToken cancellationToken)
        {
            var message = Message.Create(MsgType.FullClientRequest, MsgTypeFlagBits.WithEvent);
            message.EventType = EventType.StartConnection;
            message.Payload = JsonHelper.SerializeToUtf8Bytes(new { });

            var waitTask = this.WaitForEventAsync(MsgType.FullServerResponse, EventType.ConnectionStarted, cancellationToken, null);
            await this.SendMessage(message);
            return await waitTask.ConfigureAwait(false);
        }



        protected async Task<Message> StartSessionAsync(string sessionId, byte[] payload, CancellationToken cancellationToken)
        {
            var message = Message.Create(MsgType.FullClientRequest, MsgTypeFlagBits.WithEvent);
            message.EventType = EventType.StartSession;
            message.SessionId = sessionId;
            message.Payload = payload;

            var waitTask = this.WaitForEventAsync(MsgType.FullServerResponse, EventType.SessionStarted, cancellationToken, null);
            await this.SendMessage(message);
            return await waitTask.ConfigureAwait(false);
        }

        protected async Task<Message> FinishSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            var message = Message.Create(MsgType.FullClientRequest, MsgTypeFlagBits.WithEvent);
            message.EventType = EventType.FinishSession;
            message.SessionId = sessionId;
            message.Payload = JsonHelper.SerializeToUtf8Bytes(new { });

            var waitTask = this.WaitForEventAsync(MsgType.FullServerResponse, EventType.SessionFinished, cancellationToken, null);
            await this.SendMessage(message);
            try
            {
                return await waitTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogDebug("FinishSession cancelled for session {SessionId}", sessionId);
                throw;
            }
        }

        protected async Task<Message> FinishConnectionAsync(CancellationToken cancellationToken)
        {
            var message = Message.Create(MsgType.FullClientRequest, MsgTypeFlagBits.WithEvent);
            message.EventType = EventType.FinishConnection;
            message.Payload = JsonHelper.SerializeToUtf8Bytes(new { });

            var waitTask = this.WaitForEventAsync(MsgType.FullServerResponse, EventType.ConnectionFinished, cancellationToken, null);
            await this.SendMessage(message);
            return await waitTask.ConfigureAwait(false);
        }

        protected async Task FinalizeSessionAudioAsync(string sessionId, string deviceId)
        {
            List<float>? audioBuffer = null;
            lock (this._audioBufferLock)
            {
                if (this._sessionAudioBuffers.TryGetValue(sessionId, out var buffer))
                {
                    audioBuffer = new List<float>(buffer);
                    this._sessionAudioBuffers.Remove(sessionId);
                }
            }

            if (audioBuffer is not null && audioBuffer.Any())
            {
                await this.SaveAudioFileAsync(deviceId, sessionId, audioBuffer.ToArray()).ConfigureAwait(false);
            }
        }

        protected void ClearSessionAudioBuffer(string sessionId)
        {
            lock (this._audioBufferLock)
            {
                this._sessionAudioBuffers.Remove(sessionId);
            }
        }

        protected void ClearAllSessionAudioBuffers()
        {
            lock (this._audioBufferLock)
            {
                this._sessionAudioBuffers.Clear();
            }
        }

        protected async Task<Message> CancelSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            var message = Message.Create(MsgType.FullClientRequest, MsgTypeFlagBits.WithEvent);
            message.EventType = EventType.CancelSession;
            message.SessionId = sessionId;
            message.Payload = JsonHelper.SerializeToUtf8Bytes(new { });

            var waitTask = this.WaitForEventAsync(MsgType.FullServerResponse, EventType.SessionCanceled, cancellationToken, null);
            await this.SendMessage(message);
            return await waitTask.ConfigureAwait(false);
        }

        protected Task<Message> WaitForEventAsync(MsgType msgType, EventType eventType, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            var tcs = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pw = new PendingWait
            {
                Match = m => m.MsgType == msgType && m.EventType == eventType,
                Tcs = tcs
            };

            CancellationTokenRegistration ctr = default;
            CancellationTokenSource? timeoutCts = null;

            lock (this._waitsLock)
            {
                this._waits.Add(pw);
            }

            if (cancellationToken.CanBeCanceled)
            {
                ctr = cancellationToken.Register(() =>
                {
                    bool removed;
                    lock (this._waitsLock)
                    {
                        removed = this._waits.Remove(pw);
                    }
                    if (removed)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                    }
                });
            }

            var effectiveTimeout = timeout ?? DefaultWaitTimeout;
            timeoutCts = new CancellationTokenSource();
            _ = Task.Delay(effectiveTimeout, timeoutCts.Token).ContinueWith(_ =>
            {
                bool removed;
                lock (this._waitsLock)
                {
                    removed = _waits.Remove(pw);
                }
                if (removed)
                {
                    tcs.TrySetException(new TimeoutException(string.Format(Lang.HuoshanStreamTTS_WaitForEventAsync_Timeout, eventType)));
                }
            }, TaskScheduler.Default);

            return tcs.Task.ContinueWith(t =>
            {
                ctr.Dispose();
                timeoutCts.Cancel();
                timeoutCts.Dispose();
                return t.GetAwaiter().GetResult();
            }, TaskScheduler.Default);
        }
        #endregion

        protected virtual (string, Emotion) GetSubtitle(Message message, bool isSentenceStart)
        {
            string sentence = JsonObject.Parse(message.Payload)?["text"]?.GetValue<string>() ?? string.Empty;
            Emotion segmentEmotion = this.ProcessingSegments.TryGetValue(message.SessionId ?? string.Empty, out var seg) ? seg.Emotion : Emotion.Neutral;
            return (sentence, segmentEmotion);
        }

        #region WebsocketClient
        protected void FailAllWaits(Exception ex)
        {
            lock (this._waitsLock)
            {
                foreach (var w in this._waits)
                {
                    w.Tcs.TrySetException(ex);
                }
                this._waits.Clear();
            }
        }

        private void WebSocketClient_OnOpen()
        {
            if (this.WebSocketClient is null)
            {
                return;
            }
            this.Logger.LogDebug(Lang.HuoshanStreamTTS_OnOpen_Connected, this.DeviceId);
        }

        private void WebSocketClient_OnClose(System.Net.WebSockets.WebSocketCloseStatus? status, string? desc)
        {
            this.Logger.LogDebug(Lang.HuoshanStreamTTS_OnClose_Closed, status, desc);
            this.ClearAllSessionAudioBuffers();
            this.FailAllWaits(new OperationCanceledException(string.Format(Lang.HuoshanStreamTTS_OnClose_ClosedEx, status, desc)));
            if (this.StreamingActive)
            {
                this.TTSEventCallback?.OnProcessed(string.Empty, false, false, TtsGenerateResult.Failed);
            }
        }

        private void WebSocketClient_OnError(System.Net.WebSockets.WebSocketError error, string message)
        {
            this.Logger.LogError(Lang.HuoshanStreamTTS_OnError_Error, error, message);
            this.ClearAllSessionAudioBuffers();
            this.FailAllWaits(new Exception(string.Format(Lang.HuoshanStreamTTS_OnError_ErrorEx, error, message)));
            if (this.StreamingActive)
            {
                this.TTSEventCallback?.OnProcessed(string.Empty, false, false, TtsGenerateResult.Failed);
            }
        }

        private void WebSocketClient_OnBinaryMessage(byte[] data)
        {
            if (this.WebSocketClient is null || data.Length == 0)
            {
                return;
            }

            Message message;
            try
            {
                message = Message.FromBytes(data);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.HuoshanStreamTTS_OnBinaryMessage_ParseFailed);
                return;
            }

            // Sentence start marker -> push empty first frame
            if (message.MsgType == MsgType.FullServerResponse && message.EventType == EventType.TTSSentenceStart)
            {
                if (this.AudioSavingConfig is not null && this.AudioSavingConfig.SaveFile && !string.IsNullOrEmpty(message.SessionId))
                {
                    this.StartNewAudioBuffer(message.SessionId);
                }

                if (this.StreamingActive && !string.IsNullOrEmpty(message.SessionId))
                {
                    (string sentence, Emotion segmentEmotion) = this.GetSubtitle(message, true);
                    this.TTSEventCallback?.OnSentenceStart(sentence, segmentEmotion, message.SessionId);
                }
                return;
            }

            // Audio frame streaming
            if (message.MsgType == MsgType.AudioOnlyServer && message.Payload != null && message.Payload.Length > 0)
            {
                if (this.AudioSavingConfig is not null && this.AudioSavingConfig.SaveFile && !string.IsNullOrEmpty(message.SessionId))
                {
                    try
                    {
                        this.AppendAudioPayloadChunk(message.SessionId, message.Payload);
                    }
                    catch (Exception ex)
                    {
                        this.Logger.LogError(ex, Lang.HuoshanStreamTTS_OnBinaryMessage_AppendFailed, message.SessionId);
                    }
                }

                float[] pcmAudioData = message.Payload.PcmBytesToFloat(16);
                if (this.StreamingActive)
                {
                    this.TTSEventCallback?.OnProcessing(pcmAudioData, false, false);
                }
                return;
            }

            // Sentence end marker -> seal current producing subtitle so subsequent samples go to next sentence
            if (message.MsgType == MsgType.FullServerResponse && message.EventType == EventType.TTSSentenceEnd)
            {
                if (this.AudioSavingConfig is not null && this.AudioSavingConfig.SaveFile && !string.IsNullOrEmpty(message.SessionId))
                {
                    this.FinalizeSessionAudioAsync(message.SessionId, this.DeviceId).ConfigureAwait(false);
                }

                if (this.StreamingActive && !string.IsNullOrEmpty(message.SessionId))
                {
                    (string sentence, Emotion segmentEmotion) = this.GetSubtitle(message, false);
                    this.TTSEventCallback?.OnSentenceEnd(sentence, segmentEmotion, message.SessionId);
                }
                return;
            }

            bool matched = false;
            lock (this._waitsLock)
            {
                for (int i = 0; i < _waits.Count; i++)
                {
                    var pw = this._waits[i];
                    if (pw.Match(message))
                    {
                        this._waits.RemoveAt(i);
                        pw.Tcs.TrySetResult(message);
                        matched = true;
                        break;
                    }
                }
            }

            // Handle session finished -> finalize file
            if (message.MsgType == MsgType.FullServerResponse && message.EventType == EventType.SessionFinished)
            {
                return;
            }

            if (!matched)
            {
                if (message.MsgType == MsgType.FullServerResponse)
                {
                    if (message.EventType == EventType.ConnectionFailed || message.EventType == EventType.SessionFailed)
                    {
                        var ex = new Exception(string.Format(Lang.HuoshanStreamTTS_OnBinaryMessage_ServerFailure, message));
                        this.FailScopedWaits(message, ex);
                        if (this.StreamingActive)
                        {
                            this.TTSEventCallback?.OnProcessed(string.Empty, false, false, TtsGenerateResult.Failed);
                        }
                    }
                }
                else if (message.MsgType == MsgType.Error)
                {
                    var ex = new Exception(string.Format(Lang.HuoshanStreamTTS_OnBinaryMessage_ServerError, message));
                    this.FailScopedWaits(message, ex);
                    if (this.StreamingActive)
                    {
                        this.TTSEventCallback?.OnProcessed(string.Empty, false, false, TtsGenerateResult.Failed);
                    }
                }
            }
        }

        private void FailScopedWaits(Message message, Exception ex)
        {
            lock (this._waitsLock)
            {
                for (int i = this._waits.Count - 1; i >= 0; i--)
                {
                    var pw = this._waits[i];
                    if (pw.Match(message))
                    {
                        this._waits.RemoveAt(i);
                        pw.Tcs.TrySetException(ex);
                    }
                }
            }
        }
        #endregion
    }
}
