using XiaoZhi.Net.Test.OtherSamples.Huoshan.Protocols.Enums;
using XiaoZhi.Net.Test.OtherSamples.Huoshan.Protocols.Models;
using XiaoZhi.Net.Test.Socket;
using XiaoZhi.Test.OtherSamples.Huoshan;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample14_HuoshanBidirection
    {
        public static async Task Run()
        {
            HuoshanBidirectionTTS tts = new HuoshanBidirectionTTS();

            string apiId = Environment.GetEnvironmentVariable("HuoshanAppId", EnvironmentVariableTarget.User)!;

            string accessToken = Environment.GetEnvironmentVariable("HuoshanAccessToken", EnvironmentVariableTarget.User)!;

            if (!tts.Build(apiId, accessToken, "volc.service_type.10029"))
            {
                Console.WriteLine("Build failed");
                return;
            }
            await tts.SynthesisAsync("落霞与孤鹜齐飞，秋水共长天一色", true, true, CancellationToken.None);
        }


    }

    file class HuoshanBidirectionTTS
    {
        private const string SERVICE_END_POINT = "wss://openspeech.bytedance.com/api/v3/tts/bidirection";
        private const string TTS_NAMESPACE = "BidirectionalTTS";
        private const int SAMPLE_RATE = 16000;
        private static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(15);

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
        private readonly Dictionary<string, TTSAudioFile> _sessionFiles = new();

        public event Action<OutSegment>? OnBeforeProcessing;
        public event Action<byte[]>? OnProcessing;
        public event Action<byte[], OutSegment, double>? OnProcessed;

        protected WebSocketClient? WebSocketClient { get; set; }
        public int GetTtsSampleRate() => SAMPLE_RATE;

        public bool Build(string appId, string accessToken, string resourceId)
        {
            try
            {

                this._speaker = "zh_female_cancan_mars_bigtts";
                this._speechRate = 0;
                this._loudnessRate = 0;

                this._save2File = true;

                if (this._save2File)
                {
                    this._savePath = Path.Combine(Environment.CurrentDirectory, "data", "tts-cache");
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
                return false;
            }
        }

        public async Task SynthesisAsync(string text, bool isFirstSegment, bool isLastSegment, CancellationToken token)
        {
            try
            {
                if (this.WebSocketClient is null)
                {
                    return;
                }

                if (!this.WebSocketClient.IsConnected)
                {
                    await this.ConnectAsync(SERVICE_END_POINT, token);
                    await this.StartConnectionAsync(token);
                }


                if (string.IsNullOrEmpty(this._tssSessionId))
                {
                    this._tssSessionId = Guid.NewGuid().ToString();
                }

                if (isFirstSegment)
                {
                    Dictionary<string, object> startReq = new Dictionary<string, object>
                    {
                        { "User", new { Uid = "my_test" } },
                        { "Event", (int)EventType.StartSession },
                        { "Namespace", TTS_NAMESPACE },
                        { "ReqParams",
                            new {
                                Speaker = this._speaker,
                                AudioParams = new {
                                    Format = "wav",
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
                token.ThrowIfCancellationRequested();

                Dictionary<string, object> ttsReq = new Dictionary<string, object>
                {
                    { "User", new { Uid = "my_test" } },
                    { "Event", (int)EventType.TaskRequest },
                    { "Namespace", TTS_NAMESPACE },
                    { "ReqParams",
                        new {
                            Text = text,
                            Speaker = this._speaker,
                            AudioParams = new {
                                Format = "wav",
                                SampleRate = SAMPLE_RATE,
                                EnableTimestamp = false,
                                SpeechRate = this._speechRate,
                                LoudnessRate = this._loudnessRate,
                            }
                        }
                    },
                };
                Console.WriteLine(JsonHelper.Serialize(ttsReq));
                await this.TaskRequestAsync(this._tssSessionId, JsonHelper.SerializeToUtf8Bytes(ttsReq));
                token.ThrowIfCancellationRequested();

                if (isLastSegment)
                {
                    await this.FinishSessionAsync(this._tssSessionId, token);
                    this._tssSessionId = null;
                }
            }
            catch (OperationCanceledException)
            {
                if (!string.IsNullOrEmpty(this._tssSessionId))
                {
                    this.CancelSessionAsync(this._tssSessionId, CancellationToken.None).GetAwaiter().GetResult();
                }
                throw;
            }
            catch (Exception ex)
            {
            }
        }

        public void Dispose()
        {
            this._tssSessionId = null;
            try
            {
                this.FinishConnectionAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
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
        }

        private void WebSocketClient_OnClose(System.Net.WebSockets.WebSocketCloseStatus? status, string? desc)
        {
            // ensure files are closed (leave as .tmp)
            this.CloseAllSessionFiles(finalize: false);
            FailAllWaits(new OperationCanceledException($"WebSocket closed: {status} {desc}"));
        }

        private void WebSocketClient_OnError(System.Net.WebSockets.WebSocketError error, string message)
        {
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
                return;
            }
            Console.WriteLine("Recived message:" + message);
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
                    }
                }

                this.OnProcessing?.Invoke(message.Payload);
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
                        this.CloseSessionFile(sid, finalize: true);
                    }
                    catch (Exception ex)
                    {
                    }
                }

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

        private void AppendPcmChunk(string sessionId, byte[] pcmData)
        {
            if (string.IsNullOrEmpty(_savePath)) return;
            lock (_fileLock)
            {
                if (!_sessionFiles.TryGetValue(sessionId, out var entry))
                {
                    // create new file
                    var fileBase = $"{sessionId}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}";
                    var tmpPath = Path.Combine(_savePath, fileBase + ".wav.tmp");
                    var finalPath = Path.Combine(_savePath, fileBase + ".wav");
                    var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    entry = new TTSAudioFile(sessionId, fs, tmpPath, finalPath);
                    this._sessionFiles[sessionId] = entry;
                }

                // write chunk
                entry.Stream.Write(pcmData, 0, pcmData.Length);
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
                            // move .tmp -> .wav
                            if (File.Exists(entry.FinalPath))
                            {
                                File.Delete(entry.FinalPath);
                            }
                            File.Move(entry.TmpPath, entry.FinalPath);
                            Console.WriteLine($"Audio saved to {entry.FinalPath}");
                        }
                        catch (Exception ex)
                        {
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

        private async Task SendMessage(Message message)
        {
            if (this.WebSocketClient is null)
            {
                return;
            }
            var data = message.Marshal();
            await this.WebSocketClient.SendAsync(data);
        }
    }
}
