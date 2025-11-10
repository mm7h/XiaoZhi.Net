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

        private string? _tssSessionId = null;

        // File saving state per session
        private readonly object _fileLock = new();
        private readonly Dictionary<string, TTSAudioFile> _sessionFiles = new();

        public HuoshanBidirectionTTS(ILogger<HuoshanBidirectionTTS> logger) : base(logger)
        {
        }

        public override string ModelName => nameof(HuoshanBidirectionTTS);
        public override string ProviderType => "tts";

        public event Action<OutSegment>? OnBeforeProcessing;
        public event Action<float[]>? OnProcessing;
        public event Action<float[], OutSegment>? OnProcessed;

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
                this.SpeakerId = speaker;
                this.SpeechRate = modelSetting.Config?.SpeechRate ?? 0;
                this.LoudnessRate = modelSetting.Config?.LoudnessRate ?? 0;

                this.Save2File = modelSetting.Config?.Save2File ?? false;

                if (this.Save2File)
                {
                    this.SavePath = modelSetting.Config?.SavePath ?? Path.Combine(Environment.CurrentDirectory, "data", "tts-cache");
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

        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            try
            {
                if (this.WebSocketClient is null)
                {
                    this.Logger.LogError("WebSocket client is not initialized for Huoshan bidirection TTS.");
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

                this.OnBeforeProcessing?.Invoke(outSegment);

                if (outSegment.IsFirstSegment)
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
                    await this.StartSessionAsync(this._tssSessionId, JsonHelper.SerializeToUtf8Bytes(startReq), token);
                }
                token.ThrowIfCancellationRequested();

                Dictionary<string, object> ttsReq = new Dictionary<string, object>
                {
                    { "User", new { Uid = workflow.DeviceId } },
                    { "Event", (int)EventType.TaskRequest },
                    { "Namespace", TTS_NAMESPACE },
                    { "ReqParams",
                        new {
                            Text = outSegment.Content,
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
                await this.TaskRequestAsync(this._tssSessionId, JsonHelper.SerializeToUtf8Bytes(ttsReq));
                token.ThrowIfCancellationRequested();

                if (outSegment.IsLastSegment)
                {
                    // capture sid before it is nulled
                    var sid = this._tssSessionId!;

                    await this.FinishSessionAsync(sid, token);

                    // After session finished, aggregate full audio and trigger OnProcessed
                    #region Collect all tts audio data and save to file if required
                    try
                    {
                        float[]? allFloats = null;
                        if (this.Save2File && !string.IsNullOrEmpty(this.SavePath))
                        {
                            TTSAudioFile? entry = null;
                            lock (this._fileLock)
                            {
                                this._sessionFiles.TryGetValue(sid, out entry);
                                // Ensure any buffered data is flushed before we read from disk
                                if (entry != null)
                                {
                                    try { entry.Stream.Flush(); } catch { }
                                }
                            }

                            if (entry != null)
                            {
                                // Read all bytes from the tmp file while writer still open (allow concurrent read). Use FileShare.ReadWrite.
                                byte[] allBytes;
                                try
                                {
                                    using var rs = new FileStream(entry.TmpPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                    allBytes = new byte[rs.Length];
                                    int read = 0;
                                    while (read < allBytes.Length)
                                    {
                                        int r = rs.Read(allBytes, read, allBytes.Length - read);
                                        if (r == 0) break;
                                        read += r;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    this.Logger.LogError(ex, "Failed to read aggregated audio for TTS session {SessionId}", sid);
                                    allBytes = Array.Empty<byte>();
                                }

                                if (allBytes.Length > 0)
                                {
                                    try
                                    {
                                        allFloats = allBytes.PcmBytesToFloat(16);
                                    }
                                    catch (Exception ex)
                                    {
                                        this.Logger.LogError(ex, "Failed to convert audio bytes to float for TTS session {SessionId}", sid);
                                    }
                                }

                                // finalize and close writer stream, and move tmp -> final
                                try
                                {
                                    this.CloseSessionFile(sid, finalize: true);
                                }
                                catch (Exception ex)
                                {
                                    this.Logger.LogError(ex, "Failed to finalize session file for TTS session {SessionId}", sid);
                                }
                            }
                            else
                            {
                                this.Logger.LogWarning("Session file entry not found when finishing TTS session {SessionId}. OnProcessed will be skipped.", sid);
                            }
                        }
                        else
                        {
                            // No persistent saving configured; cannot aggregate full audio with current implementation
                            this.Logger.LogWarning("Save2File is disabled, cannot aggregate full audio for OnProcessed in TTS session {SessionId}.", sid);
                        }

                        if (allFloats != null && allFloats.Length > 0)
                        {
                            try
                            {
                                this.OnProcessed?.Invoke(allFloats, outSegment);
                            }
                            catch (Exception ex)
                            {
                                this.Logger.LogError(ex, "OnProcessed handler raised an exception for TTS session {SessionId}", sid);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        this.Logger.LogError(ex, "Unexpected error when aggregating audio for OnProcessed in TTS session {SessionId}", sid);
                    } 
                    #endregion

                    this._tssSessionId = null;
                }
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!string.IsNullOrEmpty(this._tssSessionId))
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await this.CancelSessionAsync(this._tssSessionId, cts.Token);
                    }
                }
                catch (TimeoutException tex)
                {
                    this.Logger.LogWarning(tex, "CancelSession timed out for {providerType}.", this.ProviderType);
                }
                catch (Exception ex)
                {
                    this.Logger.LogDebug(ex, "CancelSession during cancellation raised an exception.");
                }

                this.Logger.LogWarning("User canceled the job for {providerType}.", this.ProviderType);
                throw;
            }
            catch (TimeoutException tex)
            {
                this.Logger.LogWarning(tex, "CancelSession timed out for {providerType}.", this.ProviderType);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Unexpected error(s) for {providerType}.", this.ProviderType);
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
            this.Logger.LogDebug("Huoshan WebSocket connected.");
        }

        private void WebSocketClient_OnClose(System.Net.WebSockets.WebSocketCloseStatus? status, string? desc)
        {
            this.Logger.LogDebug("Huoshan WebSocket closed: {Status} {Description}", status, desc);
            // ensure files are closed (leave as .tmp)
            this.CloseAllSessionFiles(finalize: false);
            this.FailAllWaits(new OperationCanceledException($"WebSocket closed: {status} {desc}"));
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
                // Save raw audio chunk data if configured
                if (this.Save2File && !string.IsNullOrEmpty(this.SavePath) && !string.IsNullOrEmpty(this._tssSessionId))
                {
                    try
                    {
                        this.AppendAudioPayloadChunk(this._tssSessionId, message.Payload);
                    }
                    catch (Exception ex)
                    {
                        this.Logger.LogError(ex, "Failed to append audio data for TTS session {SessionId}", this._tssSessionId);
                    }
                }

                this.OnProcessing?.Invoke(message.Payload.PcmBytesToFloat(16));
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
                    // create new file (allow concurrent read + write)
                    var fileBase = $"{sessionId}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}";
                    var tmpPath = Path.Combine(this.SavePath, fileBase + "." + AUDIO_ENCODING + ".tmp");
                    var finalPath = Path.Combine(this.SavePath, fileBase + "." + AUDIO_ENCODING);
                    var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    entry = new TTSAudioFile(sessionId, fs, tmpPath, finalPath);
                    this._sessionFiles[sessionId] = entry;
                    this.Logger.LogDebug("Start saving audio data for session {SessionId} -> {File}", sessionId, tmpPath);
                }

                // write chunk
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
                            // move .tmp -> .AUDIO_ENCODING
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
