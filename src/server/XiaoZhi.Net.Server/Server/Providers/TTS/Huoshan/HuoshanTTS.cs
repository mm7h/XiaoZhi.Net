using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol.WebSocket;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Enums;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Models;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan
{
    internal abstract class HuoshanTTS<TLogger> : BaseProvider<TLogger, ModelSetting>
    {
        private const string LANG_ZH = "zh-CN";
        private const int SAMPLE_RATE = 24000;

        private readonly object _fileLock = new();
        private readonly Dictionary<string, TTSAudioFile> _sessionFiles = new();
        private readonly List<PendingWait> _waits = new();
        private readonly object _waitsLock = new();
        private static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(15);

        public HuoshanTTS(ILogger<TLogger> logger) : base(logger)
        {
            this.ProcessingSegments = new ConcurrentDictionary<string, OutSegment>();
        }

        protected WebSocketClient? WebSocketClient { get; set; }

        public override string ProviderType => "tts";
        public bool Save2File { get; protected set; }
        public string SavePath { get; protected set; } = string.Empty;
        public string SpeakerId { get; protected set; } = string.Empty;
        public int SpeechRate { get; protected set; } = 0;
        public int LoudnessRate { get; protected set; } = 0;
        protected string AudioEcoding { get; set; } = "pcm";
        protected IDictionary<string, OutSegment> ProcessingSegments { get; }
        protected bool StreamingActive { get; set; } = false;
        protected ITtsEventCallback? TTSEventCallback { get; set; }

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
                    this.Logger.LogWarning("Huoshan bidirection TTS configuration is incomplete, please check AppId, AccessToken, ResourceId and speaker.");
                    return false;
                }
                this.SpeakerId = speaker;
                this.SpeechRate = modelSetting.Config.GetConfigValueOrDefault("SpeechRate", 0);
                this.LoudnessRate = modelSetting.Config.GetConfigValueOrDefault("LoudnessRate", 0);

                this.Save2File = modelSetting.Config.GetConfigValueOrDefault("Save2File", false);

                if (this.Save2File)
                {
                    this.SavePath = modelSetting.Config.GetConfigValueOrDefault("SavePath", Path.Combine(Environment.CurrentDirectory, "data", "tts-cache"));
                    if (!Directory.Exists(this.SavePath))
                        Directory.CreateDirectory(this.SavePath);
                }

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

                this.Logger.LogInformation("Builded the {providerType} model: {modelName}", this.ProviderType, this.ModelName);

                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to build HuoshanBidirectionTTS.");
                return false;
            }
        }
        public int GetTtsSampleRate() => SAMPLE_RATE;

        public void RegisterDevice(string deviceId, string sessionId, ITtsEventCallback callback)
        {
            this.TTSEventCallback = callback;
            this.RegisterDevice(deviceId, sessionId);
        }

        #region Huoshan TTS services API
        protected async Task ConnectAsync(string endPoint, CancellationToken token)
        {
            if (this.WebSocketClient is null)
            {
                this.Logger.LogError("WebSocket client is not initialized for Huoshan TTS.");
                throw new InvalidOperationException("WebSocket client is not initialized.");
            }
            await this.WebSocketClient.ConnectAsync(endPoint, token);
        }
        protected async Task TaskRequestAsync(Dictionary<string, object> ttsReq)
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

        protected void AppendAudioPayloadChunk(string sessionId, byte[] audioData, string fileExtension)
        {
            if (!this.Save2File || string.IsNullOrEmpty(this.SavePath) || string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            lock (this._fileLock)
            {
                if (!this._sessionFiles.TryGetValue(sessionId, out var entry))
                {
                    var fileBase = $"{sessionId}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}";
                    var tmpPath = Path.Combine(this.SavePath, fileBase + "." + fileExtension + ".tmp");
                    var finalPath = Path.Combine(this.SavePath, fileBase + "." + fileExtension);
                    var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    entry = new TTSAudioFile(sessionId, fs, tmpPath, finalPath);
                    this._sessionFiles[sessionId] = entry;
                }

                entry.Stream.Write(audioData, 0, audioData.Length);
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
            return await waitTask.ConfigureAwait(false);
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

        protected void CloseSessionFile(string sessionId, bool finalize)
        {
            lock (this._fileLock)
            {
                if (this._sessionFiles.TryGetValue(sessionId, out var entry))
                {
                    try
                    {
                        entry.Stream.Flush();
                    }
                    finally
                    {
                        entry.Stream.Dispose();
                    }

                    if (finalize)
                    {
                        try
                        {
                            if (File.Exists(entry.FinalPath))
                            {
                                File.Delete(entry.FinalPath);
                            }
                            File.Move(entry.TmpPath, entry.FinalPath);
                        }
                        catch
                        {
                        }
                    }

                    this._sessionFiles.Remove(sessionId);
                }
            }
        }

        protected void CloseAllSessionFiles(bool finalize)
        {
            string[] keys;
            lock (this._fileLock)
            {
                keys = new string[this._sessionFiles.Keys.Count];
                this._sessionFiles.Keys.CopyTo(keys, 0);
            }

            foreach (var sid in keys)
            {
                this.CloseSessionFile(sid, finalize);
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
                    tcs.TrySetException(new TimeoutException($"Wait for {eventType} timed out."));
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

        protected string ConvertEmotion(Emotion emotion, string? lang = LANG_ZH)
        {
            bool isZh = !string.IsNullOrEmpty(lang) && lang == LANG_ZH;

            return (isZh, emotion) switch
            {
                (true, Emotion.Neutral) => "neutral",
                (true, Emotion.Happy) => "happy",
                (true, Emotion.Laughing) => "excited",
                (true, Emotion.Funny) => "happy",
                (true, Emotion.Sad) => "sad",
                (true, Emotion.Angry) => "angry",
                (true, Emotion.Crying) => "sad",
                (true, Emotion.Loving) => "lovey-dovey",
                (true, Emotion.Embarrassed) => "shy",
                (true, Emotion.Surprised) => "surprised",
                (true, Emotion.Shocked) => "surprised",
                (true, Emotion.Thinking) => "neutral",
                (true, Emotion.Winking) => "happy",
                (true, Emotion.Cool) => "coldness",
                (true, Emotion.Relaxed) => "tender",
                (true, Emotion.Delicious) => "happy",
                (true, Emotion.Kissy) => "lovey-dovey",
                (true, Emotion.Confident) => "magnetic",
                (true, Emotion.Sleepy) => "depressed",
                (true, Emotion.Silly) => "happy",
                (true, Emotion.Confused) => "neutral",

                (false, Emotion.Neutral) => "neutral",
                (false, Emotion.Happy) => "happy",
                (false, Emotion.Laughing) => "excited",
                (false, Emotion.Funny) => "chat",
                (false, Emotion.Sad) => "sad",
                (false, Emotion.Angry) => "angry",
                (false, Emotion.Crying) => "sad",
                (false, Emotion.Loving) => "affectionate",
                (false, Emotion.Embarrassed) => "chat",
                (false, Emotion.Surprised) => "excited",
                (false, Emotion.Shocked) => "excited",
                (false, Emotion.Thinking) => "chat",
                (false, Emotion.Winking) => "happy",
                (false, Emotion.Cool) => "authoritative",
                (false, Emotion.Relaxed) => "warm",
                (false, Emotion.Delicious) => "happy",
                (false, Emotion.Kissy) => "affectionate",
                (false, Emotion.Confident) => "authoritative",
                (false, Emotion.Sleepy) => "warm",
                (false, Emotion.Silly) => "chat",
                (false, Emotion.Confused) => "chat",

                _ => "neutral"
            };
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
            this.Logger.LogDebug("Huoshan WebSocket connected for the device: {deviceId}.", this.DeviceId);
        }

        private void WebSocketClient_OnClose(System.Net.WebSockets.WebSocketCloseStatus? status, string? desc)
        {
            this.Logger.LogDebug("Huoshan WebSocket closed: {Status} {Description}", status, desc);
            this.CloseAllSessionFiles(finalize: false);
            this.FailAllWaits(new OperationCanceledException($"WebSocket closed: {status} {desc}"));
            if (this.StreamingActive)
            {
                this.TTSEventCallback?.OnProcessed(string.Empty, false, false, TtsGenerateResult.Failed);
            }
        }

        private void WebSocketClient_OnError(System.Net.WebSockets.WebSocketError error, string message)
        {
            this.Logger.LogError("Huoshan WebSocket error: {Error} {Message}", error, message);
            this.CloseAllSessionFiles(finalize: false);
            this.FailAllWaits(new Exception($"WebSocket error: {error} {message}"));
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
                this.Logger.LogError(ex, "Failed to parse websocket binary message.");
                return;
            }

            // Sentence start marker -> push empty first frame
            if (message.MsgType == MsgType.FullServerResponse && message.EventType == EventType.TTSSentenceStart)
            {
                if (this.StreamingActive && !string.IsNullOrEmpty(message.SessionId))
                {
                    string sentence = JsonObject.Parse(message.Payload)?["text"]?.GetValue<string>() ?? string.Empty;
                    Emotion segmentEmotion = this.ProcessingSegments.TryGetValue(message.SessionId, out var seg) ? seg.Emotion : Emotion.Neutral;
                    this.TTSEventCallback?.OnSentenceStart(sentence, segmentEmotion, this.GenerateId());
                }
                return;
            }

            // Audio frame streaming
            if (message.MsgType == MsgType.AudioOnlyServer && message.Payload != null && message.Payload.Length > 0)
            {
                if (this.Save2File && !string.IsNullOrEmpty(this.SavePath) && !string.IsNullOrEmpty(message.SessionId))
                {
                    try
                    {
                        this.AppendAudioPayloadChunk(message.SessionId, message.Payload, this.AudioEcoding);
                    }
                    catch (Exception ex)
                    {
                        this.Logger.LogError(ex, "Failed to append audio data for TTS session {ttsSessionId}", message.SessionId);
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
                if (this.StreamingActive && !string.IsNullOrEmpty(message.SessionId))
                {
                    string sentence = JsonObject.Parse(message.Payload)?["text"]?.GetValue<string>() ?? string.Empty;
                    Emotion segmentEmotion = this.ProcessingSegments.TryGetValue(message.SessionId, out var seg) ? seg.Emotion : Emotion.Neutral;
                    this.TTSEventCallback?.OnSentenceEnd(sentence, segmentEmotion, this.GenerateId());
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
                        var ex = new Exception($"Server reported failure: {message}");
                        this.FailScopedWaits(message, ex);
                        if (this.StreamingActive)
                        {
                            this.TTSEventCallback?.OnProcessed(string.Empty, false, false, TtsGenerateResult.Failed);
                        }
                    }
                }
                else if (message.MsgType == MsgType.Error)
                {
                    var ex = new Exception($"Server error: {message}");
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
