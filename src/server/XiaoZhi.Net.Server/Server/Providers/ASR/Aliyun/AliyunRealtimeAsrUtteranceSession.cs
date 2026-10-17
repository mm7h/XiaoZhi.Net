using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol.WebSocket;

namespace XiaoZhi.Net.Server.Providers.ASR.Aliyun
{
    internal sealed record AliyunRealtimeAsrOptions(
        string Endpoint,
        string ApiKey,
        string ModelName,
        string[] LanguageHints,
        int PacketSizeBytes,
        int ConnectionTimeoutSeconds,
        int ResponseTimeoutSeconds);

    /// <summary>
    /// 管理一轮 DashScope 实时 ASR 任务。Provider 复用 WebSocket 包装对象，
    /// 而任务专属状态和事件订阅会随本会话结束。
    /// </summary>
    internal sealed class AliyunRealtimeAsrUtteranceSession : IDisposable
    {
        private readonly WebSocketClient _webSocketClient;
        private readonly AliyunRealtimeAsrOptions _options;
        private readonly List<byte> _pendingAudio = [];
        private readonly SortedDictionary<int, string> _sentences = [];
        private readonly TaskCompletionSource _taskStartedCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _finalResultCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly string _taskId = Guid.NewGuid().ToString("N");
        private int _nextSentenceIndex;
        private bool _streamActive;
        private bool _finishRequested;
        private bool _abortRequested;
        private bool _expectedClose;
        private bool _handlersAttached;
        private bool _disposed;

        public AliyunRealtimeAsrUtteranceSession(
            WebSocketClient webSocketClient,
            AliyunRealtimeAsrOptions options,
            long turnId)
        {
            this._webSocketClient = webSocketClient ?? throw new ArgumentNullException(nameof(webSocketClient));
            this._options = options ?? throw new ArgumentNullException(nameof(options));
            this.TurnId = turnId;
        }

        public long TurnId { get; }

        public bool IsAborted => this._abortRequested;

        public bool IsFinishing => this._finishRequested;

        public async Task StartAsync(float[] initialAudio, int sampleRate, CancellationToken token)
        {
            this.ThrowIfDisposed();
            ValidateCanonicalSampleRate(sampleRate);
            this.AttachHandlers();

            try
            {
                await this._webSocketClient.ConnectAsync(
                        this._options.Endpoint,
                        this.CreateHeaders(),
                        token)
                    ;
                if (!this._webSocketClient.IsConnected)
                {
                    throw new WebSocketException("Unable to connect to the Aliyun ASR WebSocket service.");
                }

                await this._webSocketClient.SendAsync(JsonHelper.Serialize(this.BuildRunTaskRequest()))
                    ;
                await this._taskStartedCompletion.Task
                    .WaitAsync(TimeSpan.FromSeconds(this._options.ConnectionTimeoutSeconds), token)
                    ;
                this._streamActive = true;
                this.AppendAudioCore(initialAudio);
            }
            catch
            {
                await this.CloseAsync(expectedClose: true);
                throw;
            }
        }

        public void AppendAudio(float[] audioData, int sampleRate)
        {
            this.ThrowIfDisposed();
            ValidateCanonicalSampleRate(sampleRate);
            if (!this._streamActive || this._finishRequested || this._abortRequested)
            {
                return;
            }

            this.AppendAudioCore(audioData);
        }

        public async Task<string?> FinishAsync(CancellationToken token)
        {
            this.ThrowIfDisposed();
            if (!this._streamActive || this._finishRequested || this._abortRequested)
            {
                return null;
            }

            this._finishRequested = true;
            try
            {
                this.FlushAudioCore();
                await this._webSocketClient.SendAsync(JsonHelper.Serialize(this.BuildFinishTaskRequest()))
                    ;
                return await this._finalResultCompletion.Task
                    .WaitAsync(TimeSpan.FromSeconds(this._options.ResponseTimeoutSeconds), token)
                    ;
            }
            catch (OperationCanceledException) when (this._abortRequested)
            {
                return null;
            }
            finally
            {
                this._streamActive = false;
                this._pendingAudio.Clear();
                await this.CloseAsync(expectedClose: true);
            }
        }

        public async Task AbortAsync()
        {
            if (this._disposed || this._abortRequested)
            {
                return;
            }

            this._abortRequested = true;
            this._streamActive = false;
            this._pendingAudio.Clear();
            this._taskStartedCompletion.TrySetCanceled();
            this._finalResultCompletion.TrySetCanceled();
            await this.CloseAsync(expectedClose: true);
        }

        public void Dispose()
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;
            this.DetachHandlers();
            this._pendingAudio.Clear();
            this._sentences.Clear();
        }

        private void AppendAudioCore(float[] audioData)
        {
            if (audioData is { Length: > 0 })
            {
                this._pendingAudio.AddRange(audioData.Float2PcmBytes(
                    GlobalVariables.AudioProcessingBitsPerSample,
                    GlobalVariables.AudioProcessingChannels));
            }

            while (this._pendingAudio.Count >= this._options.PacketSizeBytes)
            {
                byte[] packet = this._pendingAudio.Take(this._options.PacketSizeBytes).ToArray();
                this._pendingAudio.RemoveRange(0, this._options.PacketSizeBytes);
                this._webSocketClient.SendAsync(packet).GetAwaiter().GetResult();
            }
        }

        private void FlushAudioCore()
        {
            if (this._pendingAudio.Count == 0)
            {
                return;
            }

            byte[] packet = this._pendingAudio.ToArray();
            this._pendingAudio.Clear();
            this._webSocketClient.SendAsync(packet).GetAwaiter().GetResult();
        }

        private IReadOnlyDictionary<string, string> CreateHeaders() =>
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] = $"Bearer {this._options.ApiKey}"
            };

        private object BuildRunTaskRequest()
        {
            var parameters = new Dictionary<string, object>
            {
                ["Format"] = "pcm",
                ["SampleRate"] = GlobalVariables.AudioProcessingSampleRate
            };
            if (this._options.LanguageHints.Length > 0)
            {
                parameters["LanguageHints"] = this._options.LanguageHints;
            }

            return new
            {
                Header = new
                {
                    Action = "run-task",
                    TaskId = this._taskId,
                    Streaming = "duplex"
                },
                Payload = new
                {
                    TaskGroup = "audio",
                    Task = "asr",
                    Function = "recognition",
                    Model = this._options.ModelName,
                    Input = new { },
                    Parameters = parameters
                }
            };
        }

        private object BuildFinishTaskRequest() => new
        {
            Header = new
            {
                Action = "finish-task",
                TaskId = this._taskId,
                Streaming = "duplex"
            },
            Payload = new
            {
                Input = new { }
            }
        };

        private void AttachHandlers()
        {
            if (this._handlersAttached)
            {
                return;
            }

            this._webSocketClient.OnTextMessage += this.OnTextMessage;
            this._webSocketClient.OnClose += this.OnWebSocketClosed;
            this._webSocketClient.OnError += this.OnWebSocketError;
            this._handlersAttached = true;
        }

        private void DetachHandlers()
        {
            if (!this._handlersAttached)
            {
                return;
            }

            this._webSocketClient.OnTextMessage -= this.OnTextMessage;
            this._webSocketClient.OnClose -= this.OnWebSocketClosed;
            this._webSocketClient.OnError -= this.OnWebSocketError;
            this._handlersAttached = false;
        }

        private void OnTextMessage(string text)
        {
            try
            {
                JsonObject? response = JsonNode.Parse(text) as JsonObject;
                string? eventName = GetString(response?["header"]?["event"]);
                if (string.IsNullOrWhiteSpace(eventName))
                {
                    return;
                }

                switch (eventName)
                {
                    case "task-started":
                        this._taskStartedCompletion.TrySetResult();
                        break;
                    case "result-generated":
                        this.ExtractSentence(response);
                        break;
                    case "task-finished":
                        this.ExtractSentence(response);
                        this._finalResultCompletion.TrySetResult(this.GetFinalText());
                        break;
                    case "task-failed":
                        this.FailPendingOperations(new InvalidOperationException(GetErrorMessage(response)));
                        break;
                }
            }
            catch (Exception ex)
            {
                this.FailPendingOperations(ex);
            }
        }

        private void ExtractSentence(JsonObject? response)
        {
            JsonObject? output = response?["payload"]?["output"] as JsonObject;
            JsonObject? sentence = output?["sentence"] as JsonObject ?? output;
            string? text = GetString(sentence?["text"]);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            int sentenceId = GetInt(sentence?["sentence_id"]) ?? this._nextSentenceIndex;
            this._nextSentenceIndex = Math.Max(this._nextSentenceIndex, sentenceId + 1);
            this._sentences[sentenceId] = text;
        }

        private string GetFinalText() => string.Concat(this._sentences.Values);

        private void OnWebSocketClosed(WebSocketCloseStatus? status, string? description)
        {
            if (!this._expectedClose && (this._streamActive || !this._taskStartedCompletion.Task.IsCompleted))
            {
                this.FailPendingOperations(new WebSocketException(
                    $"Aliyun ASR WebSocket closed unexpectedly: {status} {description}"));
            }
        }

        private void OnWebSocketError(WebSocketError error, string message)
        {
            this.FailPendingOperations(new WebSocketException($"Aliyun ASR WebSocket error {error}: {message}"));
        }

        private void FailPendingOperations(Exception exception)
        {
            this._taskStartedCompletion.TrySetException(exception);
            this._finalResultCompletion.TrySetException(exception);
        }

        private async Task CloseAsync(bool expectedClose)
        {
            this._expectedClose = expectedClose;
            try
            {
                await this._webSocketClient.CloseAsync();
            }
            finally
            {
                this.DetachHandlers();
            }
        }

        private static void ValidateCanonicalSampleRate(int sampleRate)
        {
            if (sampleRate != GlobalVariables.AudioProcessingSampleRate)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleRate),
                    $"Aliyun ASR expects {GlobalVariables.AudioProcessingSampleRate} Hz canonical PCM.");
            }
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(this._disposed, this);

        private static string GetErrorMessage(JsonObject? response) =>
            GetString(response?["header"]?["error_message"])
            ?? GetString(response?["payload"]?["output"]?["message"])
            ?? "Aliyun ASR task failed.";

        private static string? GetString(JsonNode? node) => node is JsonValue value
            && value.TryGetValue<string>(out string? result)
                ? result
                : null;

        private static int? GetInt(JsonNode? node)
        {
            if (node is JsonValue value && value.TryGetValue<int>(out int result))
            {
                return result;
            }

            return node is JsonValue stringValue
                && stringValue.TryGetValue<string>(out string? stringResult)
                && int.TryParse(stringResult, out int parsed)
                    ? parsed
                    : null;
        }
    }
}
