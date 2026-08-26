using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts.Aliyun;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Protocol.WebSocket;

namespace XiaoZhi.Net.Server.Providers.TTS.Aliyun
{
    /// <summary>
    /// 管理一轮实时语音合成任务的状态和事件处理。
    /// 任务正常结束后保留连接，以便下一轮回复复用已鉴权的 WebSocket。
    /// </summary>
    internal sealed class AliyunRealtimeTtsTurnSession : IDisposable
    {
        private readonly WebSocketClient _webSocketClient;
        private readonly AliyunRealtimeTtsOptions _options;
        private readonly ITtsEventCallback? _ttsEventCallback;
        private readonly Action<AliyunRealtimeTtsTurnSession, Exception> _onFaulted;
        private readonly TaskCompletionSource _taskStartedCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _taskFinishedCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _segmentsLock = new();
        private readonly List<AliyunRealtimeTtsSegment> _submittedSegments = [];
        private readonly Dictionary<int, AliyunRealtimeTtsSentence> _sentences = [];

        private readonly string _taskId = Guid.NewGuid().ToString();
        private int _nextFallbackSegmentIndex;
        private int _nextSentenceIndex;
        private int _failureReported;
        private bool _streamActive;
        private bool _finishRequested;
        private bool _abortRequested;
        private bool _expectedClose;
        private bool _handlersAttached;
        private bool _disposed;

        public AliyunRealtimeTtsTurnSession(
            WebSocketClient webSocketClient,
            AliyunRealtimeTtsOptions options,
            ITtsEventCallback? ttsEventCallback,
            Action<AliyunRealtimeTtsTurnSession, Exception> onFaulted)
        {
            this._webSocketClient = webSocketClient ?? throw new ArgumentNullException(nameof(webSocketClient));
            this._options = options ?? throw new ArgumentNullException(nameof(options));
            this._ttsEventCallback = ttsEventCallback;
            this._onFaulted = onFaulted ?? throw new ArgumentNullException(nameof(onFaulted));
        }

        public string TaskId => this._taskId;

        public async Task StartAsync(CancellationToken token)
        {
            this.ThrowIfDisposed();
            this.AttachHandlers();
            try
            {
                if (!this._webSocketClient.IsConnected)
                {
                    await this._webSocketClient.ConnectAsync(this._options.Endpoint, token);
                }

                if (!this._webSocketClient.IsConnected)
                {
                    throw new WebSocketException(Lang.AliyunRealtimeTTS_Start_ConnectionFailed);
                }

                await this._webSocketClient.SendAsync(JsonHelper.Serialize(this.BuildRunTaskRequest()));
                await this._taskStartedCompletion.Task.WaitAsync(
                    TimeSpan.FromSeconds(this._options.ConnectionTimeoutSeconds),
                    token);
                this._streamActive = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                this.Fail(ex);
                throw;
            }
        }

        public async Task AppendTextAsync(AliyunRealtimeTtsSegment segment, CancellationToken token)
        {
            this.ThrowIfDisposed();
            token.ThrowIfCancellationRequested();
            if (!this._streamActive || this._finishRequested || this._abortRequested)
            {
                throw new InvalidOperationException(Lang.AliyunRealtimeTTS_Append_NotAcceptingText);
            }

            lock (this._segmentsLock)
            {
                this._submittedSegments.Add(segment);
            }

            try
            {
                await this._webSocketClient.SendAsync(JsonHelper.Serialize(this.BuildContinueTaskRequest(segment.Content)));
            }
            catch (Exception ex)
            {
                this.Fail(ex);
                throw;
            }
        }

        public async Task FinishAsync(CancellationToken token)
        {
            this.ThrowIfDisposed();
            if (!this._streamActive || this._finishRequested || this._abortRequested)
            {
                return;
            }

            this._finishRequested = true;
            try
            {
                await this._webSocketClient.SendAsync(JsonHelper.Serialize(this.BuildFinishTaskRequest()));
                await this._taskFinishedCompletion.Task.WaitAsync(
                    TimeSpan.FromSeconds(this._options.ResponseTimeoutSeconds),
                    token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                this.Fail(ex);
                throw;
            }
            finally
            {
                this._streamActive = false;
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
            this._taskStartedCompletion.TrySetCanceled();
            this._taskFinishedCompletion.TrySetCanceled();
            this._expectedClose = true;
            await this._webSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "TTS turn aborted");
        }

        public bool TryMarkFailureReported() => Interlocked.Exchange(ref this._failureReported, 1) == 0;

        public void Dispose()
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;
            this.DetachHandlers();
            lock (this._segmentsLock)
            {
                this._submittedSegments.Clear();
                this._sentences.Clear();
            }
        }

        private object BuildRunTaskRequest()
        {
            var parameters = new Dictionary<string, object?>
            {
                ["text_type"] = "PlainText",
                ["voice"] = this._options.Voice,
                ["format"] = "pcm",
                ["sample_rate"] = this._options.SampleRate,
                ["volume"] = this._options.Volume,
                ["rate"] = this._options.Rate,
                ["pitch"] = this._options.Pitch
            };
            if (this._options.LanguageHints.Length > 0)
            {
                parameters["language_hints"] = this._options.LanguageHints;
            }
            if (!string.IsNullOrWhiteSpace(this._options.Instruction))
            {
                parameters["instruction"] = this._options.Instruction;
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
                    Task = "tts",
                    Function = "SpeechSynthesizer",
                    Model = this._options.ModelName,
                    Input = new { },
                    Parameters = parameters
                }
            };
        }

        private object BuildContinueTaskRequest(string text) => new
        {
            Header = new
            {
                Action = "continue-task",
                TaskId = this._taskId,
                Streaming = "duplex"
            },
            Payload = new
            {
                Input = new { Text = text }
            }
        };

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
            this._webSocketClient.OnBinaryMessage += this.OnBinaryMessage;
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
            this._webSocketClient.OnBinaryMessage -= this.OnBinaryMessage;
            this._webSocketClient.OnClose -= this.OnWebSocketClosed;
            this._webSocketClient.OnError -= this.OnWebSocketError;
            this._handlersAttached = false;
        }

        private void OnTextMessage(string text)
        {
            try
            {
                JsonObject? response = JsonNode.Parse(text) as JsonObject;
                JsonObject? header = response?["header"] as JsonObject;
                if (!this._taskId.Equals(GetString(header?["task_id"]), StringComparison.Ordinal))
                {
                    return;
                }

                switch (GetString(header?["event"]))
                {
                    case "task-started":
                        this._taskStartedCompletion.TrySetResult();
                        break;
                    case "result-generated":
                        this.HandleResultGenerated(response);
                        break;
                    case "task-finished":
                        this._taskFinishedCompletion.TrySetResult();
                        break;
                    case "task-failed":
                        this.Fail(new InvalidOperationException(GetErrorMessage(response)));
                        break;
                }
            }
            catch (Exception ex)
            {
                this.Fail(ex);
            }
        }

        private void HandleResultGenerated(JsonObject? response)
        {
            JsonObject? output = response?["payload"]?["output"] as JsonObject;
            string? type = GetString(output?["type"]);
            if (string.IsNullOrWhiteSpace(type))
            {
                return;
            }

            int sentenceIndex = GetInt(output?["sentence"]?["index"]) ?? this._nextSentenceIndex;
            this._nextSentenceIndex = Math.Max(this._nextSentenceIndex, sentenceIndex + 1);
            string originalText = GetString(output?["original_text"]) ?? string.Empty;

            switch (type)
            {
                case "sentence-begin":
                {
                    AliyunRealtimeTtsSentence sentence = this.ResolveSentence(sentenceIndex, originalText);
                    this._ttsEventCallback?.OnSentenceStart(sentence.Text, sentence.Emotion, sentence.Id);
                    break;
                }
                case "sentence-end":
                {
                    AliyunRealtimeTtsSentence sentence = this.GetOrResolveSentence(sentenceIndex, originalText);
                    this._ttsEventCallback?.OnSentenceEnd(sentence.Text, sentence.Emotion, sentence.Id);
                    break;
                }
            }
        }

        private void OnBinaryMessage(byte[] audioData)
        {
            if (!this._streamActive || audioData.Length == 0)
            {
                return;
            }

            try
            {
                float[] pcm = audioData.PcmBytesToFloat(16);
                if (pcm.Length > 0)
                {
                    this._ttsEventCallback?.OnProcessing(pcm, false, false);
                }
            }
            catch (Exception ex)
            {
                this.Fail(ex);
            }
        }

        private void OnWebSocketClosed(WebSocketCloseStatus? status, string? description)
        {
            if (!this._expectedClose && (this._streamActive || !this._taskStartedCompletion.Task.IsCompleted))
            {
                this.Fail(new WebSocketException(string.Format(
                    Lang.AliyunRealtimeTTS_WebSocket_UnexpectedClose,
                    status,
                    description)));
            }
        }

        private void OnWebSocketError(WebSocketError error, string message) =>
            this.Fail(new WebSocketException(string.Format(
                Lang.AliyunRealtimeTTS_WebSocket_Error,
                error,
                message)));

        private AliyunRealtimeTtsSentence ResolveSentence(int sentenceIndex, string originalText)
        {
            lock (this._segmentsLock)
            {
                if (this._sentences.TryGetValue(sentenceIndex, out AliyunRealtimeTtsSentence? existing))
                {
                    return existing;
                }

                AliyunRealtimeTtsSegment? segment = this.FindSegmentForText(originalText);
                var sentence = new AliyunRealtimeTtsSentence(
                    $"{this._taskId}_{sentenceIndex}",
                    string.IsNullOrWhiteSpace(originalText) ? segment?.Content ?? string.Empty : originalText,
                    segment?.Emotion ?? Emotion.Neutral);
                this._sentences[sentenceIndex] = sentence;
                return sentence;
            }
        }

        private AliyunRealtimeTtsSentence GetOrResolveSentence(int sentenceIndex, string originalText)
        {
            lock (this._segmentsLock)
            {
                if (this._sentences.TryGetValue(sentenceIndex, out AliyunRealtimeTtsSentence? sentence))
                {
                    return sentence;
                }
            }

            return this.ResolveSentence(sentenceIndex, originalText);
        }

        private AliyunRealtimeTtsSegment? FindSegmentForText(string originalText)
        {
            string normalizedText = NormalizeText(originalText);
            for (int index = this._nextFallbackSegmentIndex; index < this._submittedSegments.Count; index++)
            {
                AliyunRealtimeTtsSegment candidate = this._submittedSegments[index];
                string normalizedCandidate = NormalizeText(candidate.Content);
                if (normalizedText.Length > 0
                    && (normalizedCandidate.Contains(normalizedText, StringComparison.Ordinal)
                        || normalizedText.Contains(normalizedCandidate, StringComparison.Ordinal)))
                {
                    this._nextFallbackSegmentIndex = index;
                    return candidate;
                }
            }

            if (this._nextFallbackSegmentIndex < this._submittedSegments.Count)
            {
                return this._submittedSegments[this._nextFallbackSegmentIndex++];
            }

            return this._submittedSegments.Count > 0 ? this._submittedSegments[^1] : null;
        }

        private void Fail(Exception exception)
        {
            if (this._abortRequested || this._disposed)
            {
                return;
            }

            this._streamActive = false;
            this._taskStartedCompletion.TrySetException(exception);
            this._taskFinishedCompletion.TrySetException(exception);
            this._onFaulted(this, exception);
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(this._disposed, this);

        private static string NormalizeText(string text) => string.Concat(text.Where(static c => !char.IsWhiteSpace(c)));

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

        private static string GetErrorMessage(JsonObject? response) =>
            GetString(response?["header"]?["error_message"])
            ?? GetString(response?["payload"]?["output"]?["message"])
            ?? Lang.AliyunRealtimeTTS_Task_Failed_Default;

    }
}
