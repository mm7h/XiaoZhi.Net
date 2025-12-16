using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol.WebSocket;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Enums;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Models;

namespace XiaoZhi.Net.Server.Providers.TTS
{
    internal sealed class HuoshanBidirectionTTS : HuoshanTTS<HuoshanBidirectionTTS>, ITts
    {
        private const string SERVICE_END_POINT = "wss://openspeech.bytedance.com/api/v3/tts/bidirection";
        private const string TTS_NAMESPACE = "BidirectionalTTS";
        private const string AUDIO_ENCODING = "pcm";
        private const int SAMPLE_RATE = 24000;
        private static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(15);

        private readonly List<PendingWait> _waits = new();
        private readonly object _waitsLock = new();

        private string? _ttsSessionId = null;

        // File saving state per session
        private readonly object _fileLock = new();
        private readonly Dictionary<string, TTSAudioFile> _sessionFiles = new Dictionary<string, TTSAudioFile>();

        private readonly Dictionary<string, OutSegment> _processingSegments = new Dictionary<string, OutSegment>();

        private ITtsEventCallback? _ttsEventCallback;
        private bool _streamingActive = false;

        public HuoshanBidirectionTTS(ILogger<HuoshanBidirectionTTS> logger) : base(logger)
        {
        }

        public override string ModelName => nameof(HuoshanBidirectionTTS);
        public override string ProviderType => "tts";

        public int GetTtsSampleRate() => SAMPLE_RATE;

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
                    this.Logger.LogWarning("Huoshan TTS configuration is incomplete, please check AppId, AccessToken, ResourceId and speaker.");
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

        public void RegisterDevice(string deviceId, string sessionId, ITtsEventCallback callback)
        {
            this._ttsEventCallback = callback;
            this.RegisterDevice(deviceId, sessionId);
        }

        // Streaming synthesis using channel (sentence based)
        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered())
            {
                throw new SessionNotInitializedException();
            }
            if (this.WebSocketClient is null)
            {
                throw new InvalidOperationException("WebSocket client is not initialized.");
            }

            if (!this.WebSocketClient.IsConnected)
            {
                await this.ConnectAsync(SERVICE_END_POINT, token);
                await this.StartConnectionAsync(token);
            }

            if (string.IsNullOrEmpty(this._ttsSessionId))
            {
                this._ttsSessionId = Guid.NewGuid().ToString();
                this._processingSegments.TryAdd(this._ttsSessionId, workflow.Data);
            }

            this._streamingActive = true;

            OutSegment seg = workflow.Data;

            if (seg.IsFirstSegment)
            {
                Dictionary<string, object> startReq = new Dictionary<string, object>
                {
                    { "User", new { Uid = workflow.DeviceId } },
                    { "Event", (int)EventType.StartSession },
                    { "Namespace", TTS_NAMESPACE },
                    { "ReqParams",
                        new {
                            Speaker = this.SpeakerId,
                            AudioParams = new {
                                Format = AUDIO_ENCODING,
                                SampleRate = SAMPLE_RATE,
                                EnableTimestamp = false,
                                this.SpeechRate,
                                this.LoudnessRate,
                            }
                        }
                    },
                    { "additions",
                        JsonHelper.Serialize(new {
                            DisableMarkdownFilter = false,
                            CacheConfig = new
                            {
                                TextType = 1,
                                UseCache = true
                            }
                        })
                    }
                };
                await this.StartSessionAsync(this._ttsSessionId, JsonHelper.SerializeToUtf8Bytes(startReq), token);
            }
            token.ThrowIfCancellationRequested();

            Dictionary<string, object> ttsReq = new Dictionary<string, object>
            {
                { "User", new { Uid = workflow.DeviceId } },
                { "Event", (int)EventType.TaskRequest },
                { "Namespace", TTS_NAMESPACE },
                { "ReqParams",
                    new {
                        Text = seg.Content,
                        Speaker = this.SpeakerId,
                        AudioParams = new {
                            Format = AUDIO_ENCODING,
                            SampleRate = SAMPLE_RATE,
                            EnableTimestamp = false,
                            this.SpeechRate,
                            this.LoudnessRate,
                        }
                    }
                },
            };

            this._ttsEventCallback?.OnBeforeProcessing(seg.Content, seg.IsFirstSegment, seg.IsLastSegment);

            await this.TaskRequestAsync(this._ttsSessionId, JsonHelper.SerializeToUtf8Bytes(ttsReq));
            token.ThrowIfCancellationRequested();

            // Only finish session on last segment (last sentence in paragraph)
            if (seg.IsLastSegment)
            {
                try
                {
                    await this.FinishSessionAsync(this._ttsSessionId, token);
                    this.CloseSessionFile(this._ttsSessionId, finalize: true);
                    this._ttsEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Success);
                }
                catch (Exception ex)
                {
                    this.Logger.LogError(ex, "FinishSession failed for streaming TTS.");
                }
                finally
                {
                    this._processingSegments.Remove(this._ttsSessionId);
                    this._ttsSessionId = null;
                }
            }

            // Cleanup per-sentence
            this._streamingActive = false;
        }

        public override void Dispose()
        {
            this._ttsSessionId = null;
            try
            {
                this.FinishConnectionAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                this.Logger.LogDebug(ex, "FinishConnection during dispose raised an exception.");
            }
            finally
            {
                this.FailAllWaits(new OperationCanceledException("TTS provider disposed"));
                this.CloseAllSessionFiles(finalize: false);
                this._ttsEventCallback?.OnProcessed(string.Empty, false, false, TtsGenerateResult.Aborted);
            }
        }

        private void WebSocketClient_OnOpen()
        {
            if (this.WebSocketClient is null)
            {
                return;
            }
            this.Logger.LogDebug("Huoshan WebSocket connected.");
        }

        private void WebSocketClient_OnClose(System.Net.WebSockets.WebSocketCloseStatus? status, string? desc)
        {
            this.Logger.LogDebug("Huoshan WebSocket closed: {Status} {Description}", status, desc);
            this.CloseAllSessionFiles(finalize: false);
            this.FailAllWaits(new OperationCanceledException($"WebSocket closed: {status} {desc}"));
            if (this._streamingActive)
            {
                this._ttsEventCallback?.OnProcessed(string.Empty, false, false, TtsGenerateResult.Failed);
            }
        }

        private void WebSocketClient_OnError(System.Net.WebSockets.WebSocketError error, string message)
        {
            this.Logger.LogError("Huoshan WebSocket error: {Error} {Message}", error, message);
            this.CloseAllSessionFiles(finalize: false);
            this.FailAllWaits(new Exception($"WebSocket error: {error} {message}"));
            if (this._streamingActive)
            {
                this._ttsEventCallback?.OnProcessed(string.Empty, false, false, TtsGenerateResult.Failed);
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
                if (this._streamingActive && !string.IsNullOrEmpty(message.SessionId))
                {
                    string sentence = JsonObject.Parse(message.Payload)?["text"]?.GetValue<string>() ?? string.Empty;
                    Emotion segmentEmotion = this._processingSegments.TryGetValue(message.SessionId, out var seg) ? seg.Emotion : Emotion.Neutral;
                    this._ttsEventCallback?.OnSentenceStart(sentence, segmentEmotion);
                }
                return;
            }

            // Audio frame streaming
            if (message.MsgType == MsgType.AudioOnlyServer && message.Payload != null && message.Payload.Length > 0)
            {
                if (this.Save2File && !string.IsNullOrEmpty(this.SavePath) && !string.IsNullOrEmpty(this._ttsSessionId))
                {
                    try
                    {
                        this.AppendAudioPayloadChunk(this._ttsSessionId, message.Payload);
                    }
                    catch (Exception ex)
                    {
                        this.Logger.LogError(ex, "Failed to append audio data for TTS session {ttsSessionId}", this._ttsSessionId);
                    }
                }

                float[] pcmAudioData = message.Payload.PcmBytesToFloat(16);
                if (this._streamingActive)
                {
                    this._ttsEventCallback?.OnProcessing(pcmAudioData, false, false);
                }
                return;
            }

            // Sentence end marker -> seal current producing subtitle so subsequent samples go to next sentence
            if (message.MsgType == MsgType.FullServerResponse && message.EventType == EventType.TTSSentenceEnd)
            {
                if (this._streamingActive && !string.IsNullOrEmpty(message.SessionId))
                {
                    string sentence = JsonObject.Parse(message.Payload)?["text"]?.GetValue<string>() ?? string.Empty;
                    Emotion segmentEmotion = this._processingSegments.TryGetValue(message.SessionId, out var seg) ? seg.Emotion : Emotion.Neutral;
                    this._ttsEventCallback?.OnSentenceEnd(sentence, segmentEmotion);
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
                        if (this._streamingActive)
                        {
                            this._ttsEventCallback?.OnProcessed(string.Empty, false, false, TtsGenerateResult.Failed);
                        }
                    }
                }
                else if (message.MsgType == MsgType.Error)
                {
                    var ex = new Exception($"Server error: {message}");
                    this.FailScopedWaits(message, ex);
                    if (this._streamingActive)
                    {
                        this._ttsEventCallback?.OnProcessed(string.Empty, false, false, TtsGenerateResult.Failed);
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

        private void FailAllWaits(Exception ex)
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

        private Task<Message> WaitForEventAsync(MsgType msgType, EventType eventType, CancellationToken cancellationToken, TimeSpan? timeout = null)
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

        #region Huoshan TTS services API
        private async Task ConnectAsync(string endPoint, CancellationToken token)
        {
            if (this.WebSocketClient is null)
            {
                this.Logger.LogError("WebSocket client is not initialized for Huoshan TTS.");
                throw new InvalidOperationException("WebSocket client is not initialized.");
            }
            await this.WebSocketClient.ConnectAsync(endPoint, token);
        }

        private async Task<Message> StartConnectionAsync(CancellationToken cancellationToken)
        {
            var message = Message.Create(MsgType.FullClientRequest, MsgTypeFlagBits.WithEvent);
            message.EventType = EventType.StartConnection;
            message.Payload = JsonHelper.SerializeToUtf8Bytes(new { });

            var waitTask = this.WaitForEventAsync(MsgType.FullServerResponse, EventType.ConnectionStarted, cancellationToken, null);
            await this.SendMessage(message);
            return await waitTask.ConfigureAwait(false);
        }

        private async Task<Message> FinishConnectionAsync(CancellationToken cancellationToken)
        {
            var message = Message.Create(MsgType.FullClientRequest, MsgTypeFlagBits.WithEvent);
            message.EventType = EventType.FinishConnection;
            message.Payload = JsonHelper.SerializeToUtf8Bytes(new { });

            var waitTask = this.WaitForEventAsync(MsgType.FullServerResponse, EventType.ConnectionFinished, cancellationToken, null);
            await this.SendMessage(message);
            return await waitTask.ConfigureAwait(false);
        }

        private async Task<Message> StartSessionAsync(string sessionId, byte[] payload, CancellationToken cancellationToken)
        {
            var message = Message.Create(MsgType.FullClientRequest, MsgTypeFlagBits.WithEvent);
            message.EventType = EventType.StartSession;
            message.SessionId = sessionId;
            message.Payload = payload;

            var waitTask = this.WaitForEventAsync(MsgType.FullServerResponse, EventType.SessionStarted, cancellationToken, null);
            await this.SendMessage(message);
            return await waitTask.ConfigureAwait(false);
        }

        private async Task TaskRequestAsync(string sessionId, byte[] payload)
        {
            var message = Message.Create(MsgType.FullClientRequest, MsgTypeFlagBits.WithEvent);
            message.EventType = EventType.TaskRequest;
            message.SessionId = sessionId;
            message.Payload = payload;
            await this.SendMessage(message);
        }

        private async Task<Message> CancelSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            var message = Message.Create(MsgType.FullClientRequest, MsgTypeFlagBits.WithEvent);
            message.EventType = EventType.CancelSession;
            message.SessionId = sessionId;
            message.Payload = JsonHelper.SerializeToUtf8Bytes(new { });

            var waitTask = this.WaitForEventAsync(MsgType.FullServerResponse, EventType.SessionCanceled, cancellationToken, null);
            await this.SendMessage(message);
            return await waitTask.ConfigureAwait(false);
        }

        private async Task<Message> FinishSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            var message = Message.Create(MsgType.FullClientRequest, MsgTypeFlagBits.WithEvent);
            message.EventType = EventType.FinishSession;
            message.SessionId = sessionId;
            message.Payload = JsonHelper.SerializeToUtf8Bytes(new { });

            var waitTask = this.WaitForEventAsync(MsgType.FullServerResponse, EventType.SessionFinished, cancellationToken, null);
            await this.SendMessage(message);
            return await waitTask.ConfigureAwait(false);
        }
        #endregion

        private void AppendAudioPayloadChunk(string sessionId, byte[] audioData)
        {
            if (string.IsNullOrEmpty(this.SavePath)) return;
            lock (_fileLock)
            {
                if (!_sessionFiles.TryGetValue(sessionId, out var entry))
                {
                    var fileBase = $"{sessionId}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}";
                    var tmpPath = Path.Combine(this.SavePath, fileBase + "." + AUDIO_ENCODING + ".tmp");
                    var finalPath = Path.Combine(this.SavePath, fileBase + "." + AUDIO_ENCODING);
                    var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    entry = new TTSAudioFile(sessionId, fs, tmpPath, finalPath);
                    this._sessionFiles[sessionId] = entry;
                    this.Logger.LogDebug("Start saving audio data for session {SessionId} -> {File}", sessionId, tmpPath);
                }

                entry.Stream.Write(audioData, 0, audioData.Length);
            }
        }

        private void CloseSessionFile(string sessionId, bool finalize)
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
                            this.Logger.LogDebug("Saved audio data for session {SessionId} -> {File}", sessionId, entry.FinalPath);
                        }
                        catch (Exception ex)
                        {
                            this.Logger.LogError(ex, "Failed to finalize audio file {Tmp} -> {Final}", entry.TmpPath, entry.FinalPath);
                        }
                    }

                    this._sessionFiles.Remove(sessionId);
                }
            }
        }

        private void CloseAllSessionFiles(bool finalize)
        {
            // snapshot keys to avoid modifying collection during iteration
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
    }
}
