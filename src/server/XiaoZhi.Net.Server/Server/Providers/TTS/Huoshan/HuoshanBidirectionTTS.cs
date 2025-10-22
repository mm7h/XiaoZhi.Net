using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol.WebSocket;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Enums;

namespace XiaoZhi.Net.Server.Providers.TTS
{
    internal sealed class HuoshanBidirectionTTS : HuoshanTTS<HuoshanBidirectionTTS>, ITts
    {
        private const string SERVICE_END_POINT = "wss://openspeech.bytedance.com/api/v3/tts/bidirection";
        private const string TTS_NAMESPACE = "BidirectionalTTS";
        private const int SAMPLE_RATE = 16000;
        private static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(15);

        private sealed class PendingWait
        {
            public required Func<Message, bool> Match { get; init; }
            public required TaskCompletionSource<Message> Tcs { get; init; }
        }
        private readonly List<PendingWait> _waits = new();
        private readonly object _waitsLock = new();

        private bool _save2File = false;
        private string? _savePath;
        private string? _speaker;
        private int _speechRate = 0;
        private int _loudnessRate = 0;

        private string? _tssSessionId = null;

        // File saving state per session
        private readonly object _fileLock = new();
        private readonly Dictionary<string, (FileStream stream, string tmpPath, string finalPath)> _sessionFiles = new();

        public HuoshanBidirectionTTS(ILogger<HuoshanBidirectionTTS> logger) : base(logger)
        {
        }

        public override string ModelName => nameof(HuoshanBidirectionTTS);
        public override string ProviderType => "tts";

        public event Action<OutSegment>? OnBeforeProcessing;
        public event Action<float[]>? OnProcessing;
        public event Action<float[], OutSegment, double>? OnProcessed;

        public int GetTtsSampleRate() => SAMPLE_RATE;

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                string? appId = modelSetting.Config?.AppId;
                string? accessToken = modelSetting.Config?.AccessToken;
                string? resourceId = modelSetting.Config?.ResourceId;
                string? speaker = modelSetting.Config?.Speaker;

                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(resourceId) || string.IsNullOrEmpty(speaker))
                {
                    this.Logger.LogWarning("Huoshan TTS configuration is incomplete, please check AppId, AccessToken, ResourceId and speaker.");
                    return false;
                }
                this._speaker = speaker;
                this._speechRate = modelSetting.Config?.SpeechRate ?? 0;
                this._loudnessRate = modelSetting.Config?.LoudnessRate ?? 0;

                this._save2File = modelSetting.Config?.Save2File ?? false;

                if (this._save2File)
                {
                    this._savePath = modelSetting.Config?.SavePath ?? Path.Combine(Environment.CurrentDirectory, "data", "tts-cache");
                    if (!Directory.Exists(this._savePath))
                        Directory.CreateDirectory(this._savePath);
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

                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to build HuoshanBidirectionTTS.");
                return false;
            }
        }

        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            if (this.WebSocketClient is null)
            {
                this.Logger.LogError("WebSocket client is not initialized for Huoshan TTS.");
                return;
            }

            if (!this.WebSocketClient.IsConnected)
            {
                await this.ConnectAsync(SERVICE_END_POINT, token);
                await this.StartConnectionAsync(token);
            }

            string sessionId = workflow.SessionId;
            OutSegment outSegment = workflow.Data;

            if (string.IsNullOrEmpty(this._tssSessionId))
            {
                this._tssSessionId = Guid.NewGuid().ToString();
            }

            if (outSegment.IsFirstSegment)
            {
                Dictionary<string, object> startReq = new Dictionary<string, object>
                {
                    { "user", workflow.DeviceId },
                    { "event", (int)EventType.StartSession },
                    { "namespace", TTS_NAMESPACE },
                    { "req_params",
                        new {
                            Speaker = this._speaker,
                            AudioParams = new {
                                Format = "pcm",
                                SampleRate = SAMPLE_RATE,
                                EnableTimestamp = false,
                                SpeechRate = this._speechRate,
                                LoudnessRate = this._loudnessRate,
                            }
                        }
                    },
                    { "additions",
                        JsonHelper.Serialize(new {
                            DisableMarkdownFilter = false
                        })
                    }
                };
                await this.StartSessionAsync(this._tssSessionId, JsonHelper.SerializeToUtf8Bytes(startReq), token);
            }

            Dictionary<string, object> ttsReq = new Dictionary<string, object>
            {
                { "user", workflow.DeviceId },
                { "event", (int)EventType.TaskRequest },
                { "namespace", TTS_NAMESPACE },
                { "req_params",
                    new {
                        Text = outSegment.Content,
                        Speaker = this._speaker,
                        AudioParams = new {
                            Format = "pcm",
                            SampleRate = SAMPLE_RATE,
                            EnableTimestamp = false,
                            SpeechRate = this._speechRate,
                            LoudnessRate = this._loudnessRate,
                        }
                    }
                },
            };
            await this.TaskRequestAsync(this._tssSessionId, JsonHelper.SerializeToUtf8Bytes(ttsReq));

            if (outSegment.IsLastSegment)
            {
                await this.FinishSessionAsync(this._tssSessionId, token);
                this._tssSessionId = null;
            }
        }

        public override void Dispose()
        {
            this._tssSessionId = null;
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
                // Fail any pending waits to avoid hanging tasks
                this.FailAllWaits(new OperationCanceledException("TTS provider disposed"));
                // Close any pending session files without finalizing (keep .tmp)
                this.CloseAllSessionFiles(finalize: false);
            }
        }

        private void WebSocketClient_OnOpen()
        {
            if (this.WebSocketClient is null)
            {
                return;
            }
            this.Logger.LogInformation("Huoshan WebSocket connected.");
        }

        private void WebSocketClient_OnClose(System.Net.WebSockets.WebSocketCloseStatus? status, string? desc)
        {
            this.Logger.LogWarning("Huoshan WebSocket closed: {Status} {Description}", status, desc);
            // ensure files are closed (leave as .tmp)
            this.CloseAllSessionFiles(finalize: false);
            FailAllWaits(new OperationCanceledException($"WebSocket closed: {status} {desc}"));
        }

        private void WebSocketClient_OnError(System.Net.WebSockets.WebSocketError error, string message)
        {
            this.Logger.LogError("Huoshan WebSocket error: {Error} {Message}", error, message);
            // ensure files are closed (leave as .tmp)
            this.CloseAllSessionFiles(finalize: false);
            FailAllWaits(new Exception($"WebSocket error: {error} {message}"));
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

            // Route audio frames if needed in future
            if (message.MsgType == MsgType.AudioOnlyServer && message.Payload != null && message.Payload.Length > 0)
            {
                // Save raw PCM chunk if configured
                if (this._save2File && !string.IsNullOrEmpty(this._savePath))
                {
                    var sid = this._tssSessionId ?? "unknown";
                    try
                    {
                        this.AppendPcmChunk(sid, message.Payload);
                    }
                    catch (Exception ex)
                    {
                        this.Logger.LogError(ex, "Failed to append PCM data for session {SessionId}", sid);
                    }
                }

                this.OnProcessing?.Invoke(message.Payload.Bytes2Float());
                return;
            }

            if (message.EventType == EventType.SessionFinished)
            {
                // Finalize and close the PCM file for this session
                if (this._save2File && !string.IsNullOrEmpty(this._savePath))
                {
                    var sid = this._tssSessionId ?? "unknown";
                    try
                    {
                        CloseSessionFile(sid, finalize: true);
                    }
                    catch (Exception ex)
                    {
                        this.Logger.LogError(ex, "Failed to finalize PCM file for session {SessionId}", sid);
                    }
                }

                return;
            }

            // Complete matching waiter if any
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

            // In case of failures, propagate to interested waiters
            if (!matched)
            {
                if (message.MsgType == MsgType.FullServerResponse)
                {
                    if (message.EventType == EventType.ConnectionFailed || message.EventType == EventType.SessionFailed)
                    {
                        var ex = new Exception($"Server reported failure: {message}");
                        this.FailScopedWaits(message, ex);
                    }
                }
                else if (message.MsgType == MsgType.Error)
                {
                    var ex = new Exception($"Server error: {message}");
                    this.FailScopedWaits(message, ex);
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
                    // If waiter would have matched this failure message, fail it
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
                Match = m => m.MsgType != msgType || m.EventType != eventType,
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
                return t.Result;
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

        private void AppendPcmChunk(string sessionId, byte[] pcmData)
        {
            if (string.IsNullOrEmpty(_savePath)) return;
            lock (_fileLock)
            {
                if (!_sessionFiles.TryGetValue(sessionId, out var entry))
                {
                    // create new file
                    var fileBase = $"{sessionId}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}";
                    var tmpPath = Path.Combine(_savePath, fileBase + ".pcm.tmp");
                    var finalPath = Path.Combine(_savePath, fileBase + ".pcm");
                    var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    entry = (fs, tmpPath, finalPath);
                    this._sessionFiles[sessionId] = entry;
                    this.Logger.LogInformation("Start saving PCM for session {SessionId} -> {File}", sessionId, tmpPath);
                }

                // write chunk
                entry.stream.Write(pcmData, 0, pcmData.Length);
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
                        entry.stream.Flush();
                    }
                    finally
                    {
                        entry.stream.Dispose();
                    }

                    if (finalize)
                    {
                        try
                        {
                            // move .tmp -> .pcm
                            if (File.Exists(entry.finalPath))
                            {
                                File.Delete(entry.finalPath);
                            }
                            File.Move(entry.tmpPath, entry.finalPath);
                            this.Logger.LogInformation("Saved PCM for session {SessionId} -> {File}", sessionId, entry.finalPath);
                        }
                        catch (Exception ex)
                        {
                            this.Logger.LogError(ex, "Failed to finalize PCM file {Tmp} -> {Final}", entry.tmpPath, entry.finalPath);
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
