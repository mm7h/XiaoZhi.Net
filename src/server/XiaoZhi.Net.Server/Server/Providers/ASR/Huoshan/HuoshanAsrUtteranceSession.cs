using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts.Huoshan;
using XiaoZhi.Net.Server.Common.Contexts.Huoshan.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol.WebSocket;

namespace XiaoZhi.Net.Server.Providers.ASR.Huoshan
{
    internal sealed record HuoshanAsrOptions(
        string Endpoint,
        string ApiKey,
        string AppId,
        string AccessToken,
        string ResourceId,
        string ModelName,
        string? Language,
        int PacketSizeBytes,
        int ResponseTimeoutSeconds,
        bool EnableItn,
        bool EnablePunc,
        bool EnableDdc);

    /// <summary>
    /// 仅管理一轮火山 ASR 语句。可复用的 WebSocket 包装对象属于 Provider，
    /// 本会话则拥有全部每轮状态，并且只在自身生命周期内订阅该包装对象的事件。
    /// </summary>
    internal sealed class HuoshanAsrUtteranceSession : IDisposable
    {
        private const int TargetSampleRate = GlobalVariables.AudioProcessingSampleRate;
        private const int BitsPerSample = GlobalVariables.AudioProcessingBitsPerSample;
        private const int ChannelCount = GlobalVariables.AudioProcessingChannels;

        private readonly WebSocketClient _webSocketClient;
        private readonly HuoshanAsrOptions _options;
        private readonly string _deviceId;
        private readonly List<byte> _pendingAudio = [];
        private readonly Dictionary<string, string> _definiteUtterances = [];
        private readonly TaskCompletionSource<HuoshanAsrResponse> _initializationCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _finalResultCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        private string _latestText = string.Empty;
        private int _nextUtteranceIndex;
        private int _nextSequence = 1;
        private bool _streamActive;
        private bool _finishRequested;
        private bool _abortRequested;
        private bool _expectedClose;
        private bool _handlersAttached;
        private bool _disposed;

        public HuoshanAsrUtteranceSession(
            WebSocketClient webSocketClient,
            HuoshanAsrOptions options,
            string deviceId,
            long turnId)
        {
            this._webSocketClient = webSocketClient ?? throw new ArgumentNullException(nameof(webSocketClient));
            this._options = options ?? throw new ArgumentNullException(nameof(options));
            this._deviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
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
                        token);
                if (!this._webSocketClient.IsConnected)
                {
                    throw new WebSocketException("Unable to connect to the Huoshan ASR WebSocket service.");
                }

                byte[] request = HuoshanAsrMessageCodec.CreateFullRequest(
                    this.NextSequence(),
                    JsonHelper.SerializeToUtf8Bytes(this.BuildStartRequest()));
                await this._webSocketClient.SendAsync(request);

                await this._initializationCompletion.Task
                    .WaitAsync(TimeSpan.FromSeconds(this._options.ResponseTimeoutSeconds), token);
                this._streamActive = true;
                this.AppendAudioCore(initialAudio, isFinal: false);
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

            this.AppendAudioCore(audioData, isFinal: false);
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
                this.FlushFinalAudioCore();
                return await this._finalResultCompletion.Task
                    .WaitAsync(TimeSpan.FromSeconds(this._options.ResponseTimeoutSeconds), token);
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
            this._initializationCompletion.TrySetCanceled();
            this._finalResultCompletion.TrySetCanceled();
            await this.CloseAsync(expectedClose: true);
        }

        private void AppendAudioCore(float[] audioData, bool isFinal)
        {
            if (audioData is { Length: > 0 })
            {
                this._pendingAudio.AddRange(audioData.Float2PcmBytes(BitsPerSample, ChannelCount));
            }

            while (!isFinal && this._pendingAudio.Count >= this._options.PacketSizeBytes)
            {
                byte[] packet = this._pendingAudio.Take(this._options.PacketSizeBytes).ToArray();
                this._pendingAudio.RemoveRange(0, this._options.PacketSizeBytes);
                this.SendAudioPacketCore(packet, isLast: false);
            }
        }

        private void FlushFinalAudioCore()
        {
            while (this._pendingAudio.Count > this._options.PacketSizeBytes)
            {
                byte[] packet = this._pendingAudio.Take(this._options.PacketSizeBytes).ToArray();
                this._pendingAudio.RemoveRange(0, this._options.PacketSizeBytes);
                this.SendAudioPacketCore(packet, isLast: false);
            }

            byte[] finalPacket = this._pendingAudio.ToArray();
            this._pendingAudio.Clear();
            this.SendAudioPacketCore(finalPacket, isLast: true);
        }

        private void SendAudioPacketCore(byte[] audioData, bool isLast)
        {
            byte[] frame = HuoshanAsrMessageCodec.CreateAudioRequest(this.NextSequence(), audioData, isLast);
            this._webSocketClient.SendAsync(frame).GetAwaiter().GetResult();
        }

        private IReadOnlyDictionary<string, string> CreateHeaders()
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Api-Resource-Id"] = this._options.ResourceId,
                ["X-Api-Request-Id"] = Guid.NewGuid().ToString(),
                ["X-Api-Connect-Id"] = Guid.NewGuid().ToString()
            };

            if (!string.IsNullOrWhiteSpace(this._options.ApiKey))
            {
                headers["X-Api-Key"] = this._options.ApiKey;
            }
            else
            {
                headers["X-Api-App-Key"] = this._options.AppId;
                headers["X-Api-Access-Key"] = this._options.AccessToken;
            }

            return headers;
        }

        private object BuildStartRequest()
        {
            var audio = new Dictionary<string, object>
            {
                ["Format"] = "pcm",
                ["Codec"] = "raw",
                ["Rate"] = TargetSampleRate,
                ["Bits"] = BitsPerSample,
                ["Channel"] = ChannelCount
            };

            if (!string.IsNullOrWhiteSpace(this._options.Language))
            {
                audio["Language"] = this._options.Language;
            }

            return new
            {
                User = new { Uid = this._deviceId },
                Audio = audio,
                Request = new
                {
                    ModelName = this._options.ModelName,
                    EnableItn = this._options.EnableItn,
                    EnablePunc = this._options.EnablePunc,
                    EnableDdc = this._options.EnableDdc,
                    ShowUtterances = true
                }
            };
        }

        private void AttachHandlers()
        {
            if (this._handlersAttached)
            {
                return;
            }

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

            this._webSocketClient.OnBinaryMessage -= this.OnBinaryMessage;
            this._webSocketClient.OnClose -= this.OnWebSocketClosed;
            this._webSocketClient.OnError -= this.OnWebSocketError;
            this._handlersAttached = false;
        }

        private void OnBinaryMessage(byte[] data)
        {
            try
            {
                HuoshanAsrResponse response = HuoshanAsrMessageCodec.ParseResponse(data);
                if (response.ErrorCode != 0)
                {
                    this.FailPendingOperations(new InvalidOperationException($"Huoshan ASR returned protocol error {response.ErrorCode}."));
                    return;
                }

                JsonObject? payload = response.Payload as JsonObject;
                int? serviceCode = GetInt(payload?["code"]);
                if (serviceCode.HasValue && serviceCode is not 1000 and not 1013)
                {
                    string serviceError = GetString(payload?["error"])
                        ?? GetString(payload?["message"])
                        ?? "Unknown service error";
                    this.FailPendingOperations(new InvalidOperationException(
                        $"Huoshan ASR returned service error {serviceCode}: {serviceError}"));
                    return;
                }

                this._initializationCompletion.TrySetResult(response);
                this.ExtractText(payload);

                if (response.IsLastPackage)
                {
                    this._finalResultCompletion.TrySetResult(this.GetFinalText());
                }
            }
            catch (Exception ex)
            {
                this.FailPendingOperations(ex);
            }
        }

        private void ExtractText(JsonObject? payload)
        {
            if (payload?["result"] is not JsonObject resultObject)
            {
                return;
            }

            string? text = GetString(resultObject["text"]);
            if (!string.IsNullOrWhiteSpace(text))
            {
                this._latestText = text;
            }

            if (resultObject["utterances"] is not JsonArray utterances)
            {
                return;
            }

            foreach (JsonObject utterance in utterances.OfType<JsonObject>())
            {
                if (GetBool(utterance["definite"]) != true)
                {
                    continue;
                }

                string? utteranceText = GetString(utterance["text"]);
                if (string.IsNullOrWhiteSpace(utteranceText))
                {
                    continue;
                }

                string utteranceKey = GetString(utterance["utterance_id"])
                    ?? GetString(utterance["id"])
                    ?? GetString(utterance["start_time"])
                    ?? GetInt(utterance["start_time"])?.ToString()
                    ?? $"local-{this._nextUtteranceIndex++:D8}";
                this._definiteUtterances[utteranceKey] = utteranceText;
            }
        }

        private string GetFinalText() => this._definiteUtterances.Count > 0
            ? string.Concat(this._definiteUtterances.Values)
            : this._latestText;

        private void OnWebSocketClosed(WebSocketCloseStatus? status, string? description)
        {
            if (!this._expectedClose && (this._streamActive || !this._initializationCompletion.Task.IsCompleted))
            {
                this.FailPendingOperations(new WebSocketException(
                    $"Huoshan ASR WebSocket closed unexpectedly: {status} {description}"));
            }
        }

        private void OnWebSocketError(WebSocketError error, string message)
        {
            this.FailPendingOperations(new WebSocketException($"Huoshan ASR WebSocket error {error}: {message}"));
        }

        private void FailPendingOperations(Exception exception)
        {
            this._initializationCompletion.TrySetException(exception);
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

        private int NextSequence() => this._nextSequence++;

        private static void ValidateCanonicalSampleRate(int sampleRate)
        {
            if (sampleRate != TargetSampleRate)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), $"Huoshan ASR expects {TargetSampleRate} Hz canonical PCM.");
            }
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(this._disposed, this);

        private static string? GetString(JsonNode? node) => node is JsonValue value
            && value.TryGetValue<string>(out string? result) ? result : null;

        private static int? GetInt(JsonNode? node) => node is JsonValue value
            && value.TryGetValue<int>(out int result) ? result : null;

        private static bool? GetBool(JsonNode? node) => node is JsonValue value
            && value.TryGetValue<bool>(out bool result) ? result : null;

        public void Dispose()
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;
            this.DetachHandlers();
            this._pendingAudio.Clear();
            this._definiteUtterances.Clear();
        }
    }
}
