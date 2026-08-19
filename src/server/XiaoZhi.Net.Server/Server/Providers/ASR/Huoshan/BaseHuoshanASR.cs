using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol.WebSocket;
using XiaoZhi.Net.Server.Providers.ASR.Huoshan.Protocols;

namespace XiaoZhi.Net.Server.Providers.ASR.Huoshan
{
    internal abstract class BaseHuoshanASR<TLogger> : BaseProvider<TLogger, ModelSetting>, IAsr
    {
        private const int TargetSampleRate = 16000;
        private const int BitsPerSample = 16;
        private const int ChannelCount = 1;

        private readonly SemaphoreSlim _streamLock = new(1, 1);
        private readonly List<byte> _pendingAudio = [];
        private readonly List<string> _definiteUtterances = [];

        private WebSocketClient? _webSocketClient;
        private IAsrEventCallback? _asrEventCallback;
        private TaskCompletionSource<HuoshanAsrResponse>? _initializationCompletion;
        private TaskCompletionSource<string>? _finalResultCompletion;

        private string _apiKey = string.Empty;
        private string _appId = string.Empty;
        private string _accessToken = string.Empty;
        private string _resourceId = string.Empty;
        private string _asrModelName = "bigmodel";
        private string? _language;
        private string _latestText = string.Empty;
        private int _nextSequence;
        private int _packetSizeBytes;
        private bool _streamActive;
        private bool _finishRequested;
        private bool _abortRequested;
        private bool _expectedClose;
        private bool _enableItn;
        private bool _enablePunc;
        private bool _enableDdc;
        private int _responseTimeoutSeconds;

        protected BaseHuoshanASR(ILogger<TLogger> logger) : base(logger)
        {
        }

        public override string ProviderType => "asr";
        public bool IsStreaming => true;
        protected abstract string ServiceEndpoint { get; }
        protected virtual bool SupportsLanguage => false;

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                this._apiKey = modelSetting.Config.GetConfigValueOrDefault("ApiKey", string.Empty);
                this._appId = modelSetting.Config.GetConfigValueOrDefault("AppId", string.Empty);
                this._accessToken = modelSetting.Config.GetConfigValueOrDefault("AccessToken", string.Empty);
                this._resourceId = modelSetting.Config.GetConfigValueOrDefault("ResourceId", string.Empty);

                bool hasApiKey = !string.IsNullOrWhiteSpace(this._apiKey);
                bool hasLegacyCredentials = !string.IsNullOrWhiteSpace(this._appId) && !string.IsNullOrWhiteSpace(this._accessToken);
                if (string.IsNullOrWhiteSpace(this._resourceId) || (!hasApiKey && !hasLegacyCredentials))
                {
                    this.Logger.LogWarning("Huoshan ASR configuration is incomplete for {ModelName}.", this.ModelName);
                    return false;
                }

                int segmentDurationMs = modelSetting.Config.GetConfigValueOrDefault("SegmentDurationMs", 200);
                if (segmentDurationMs is < 100 or > 1000)
                {
                    this.Logger.LogWarning("Huoshan ASR SegmentDurationMs must be between 100 and 1000 milliseconds.");
                    return false;
                }

                this._packetSizeBytes = TargetSampleRate * ChannelCount * (BitsPerSample / 8) * segmentDurationMs / 1000;
                this._responseTimeoutSeconds = modelSetting.Config.GetConfigValueOrDefault("ResponseTimeoutSeconds", 15);
                this._responseTimeoutSeconds = Math.Clamp(this._responseTimeoutSeconds, 1, 60);
                this._asrModelName = modelSetting.Config.GetConfigValueOrDefault("AsrModelName", "bigmodel");
                this._enableItn = modelSetting.Config.GetConfigValueOrDefault("EnableItn", true);
                this._enablePunc = modelSetting.Config.GetConfigValueOrDefault("EnablePunc", true);
                this._enableDdc = modelSetting.Config.GetConfigValueOrDefault("EnableDdc", true);
                this._language = this.SupportsLanguage ? modelSetting.Config.GetConfigValueOrDefault<string?>("Language") : null;

                this.Logger.LogInformation("Huoshan ASR provider {ModelName} was built.", this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Huoshan ASR provider {ModelName} could not be built.", this.ModelName);
                return false;
            }
        }

        public void RegisterDevice(string deviceId, string sessionId, IAsrEventCallback callback)
        {
            this._asrEventCallback = callback;
            base.RegisterDevice(deviceId, sessionId);
        }

        public override void UnregisterDevice(string deviceId, string sessionId)
        {
            this.AbortSynchronously();
            this._asrEventCallback = null;
            base.UnregisterDevice(deviceId, sessionId);
        }

        public async Task ConvertSpeechTextAsync(Workflow<float[]> workflow, int sampleRate, int frameSize, CancellationToken token)
        {
            await this.ConvertSpeechTextStreamingAsync(workflow, sampleRate, frameSize, StreamingAsrOperation.Start, token).ConfigureAwait(false);
            await this.ConvertSpeechTextStreamingAsync(workflow, sampleRate, frameSize, StreamingAsrOperation.Finish, token).ConfigureAwait(false);
        }

        public async Task ConvertSpeechTextStreamingAsync(Workflow<float[]> workflow, int sampleRate, int frameSize, StreamingAsrOperation operation, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(workflow.DeviceId, workflow.SessionId))
            {
                throw new InvalidOperationException("The Huoshan ASR provider is not registered for this session.");
            }

            switch (operation)
            {
                case StreamingAsrOperation.Start:
                    await this.StartUtteranceAsync(workflow.Data, sampleRate, token).ConfigureAwait(false);
                    break;
                case StreamingAsrOperation.Audio:
                    await this.AppendAudioAsync(workflow.Data, sampleRate, token).ConfigureAwait(false);
                    break;
                case StreamingAsrOperation.Finish:
                    await this.FinishUtteranceAsync(token).ConfigureAwait(false);
                    break;
                case StreamingAsrOperation.Abort:
                    await this.AbortUtteranceAsync().ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
            }
        }

        private async Task StartUtteranceAsync(float[] initialAudio, int sampleRate, CancellationToken token)
        {
            await this._streamLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (this._streamActive)
                {
                    return;
                }

                if (sampleRate is not 8000 and not TargetSampleRate)
                {
                    throw new ArgumentOutOfRangeException(nameof(sampleRate), "Huoshan ASR accepts 8 kHz or 16 kHz source audio.");
                }

                this.ResetUtteranceState(sampleRate);
                this.CreateWebSocketClient();
                this._initializationCompletion = new TaskCompletionSource<HuoshanAsrResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                this._finalResultCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

                await this._webSocketClient!.ConnectAsync(this.ServiceEndpoint, token).ConfigureAwait(false);
                if (!this._webSocketClient.IsConnected)
                {
                    throw new WebSocketException("Unable to connect to the Huoshan ASR WebSocket service.");
                }
                byte[] request = HuoshanAsrMessageCodec.CreateFullRequest(this.NextSequence(), JsonHelper.SerializeToUtf8Bytes(this.BuildStartRequest()));
                await this._webSocketClient.SendAsync(request).ConfigureAwait(false);

                await this._initializationCompletion.Task.WaitAsync(TimeSpan.FromSeconds(this._responseTimeoutSeconds), token).ConfigureAwait(false);
                this._streamActive = true;
                this.AppendAudioCore(initialAudio, sampleRate, isFinal: false);
            }
            catch
            {
                await this.CloseWebSocketAsync(expectedClose: true).ConfigureAwait(false);
                this.ResetUtteranceState(sampleRate);
                throw;
            }
            finally
            {
                this._streamLock.Release();
            }
        }

        private async Task AppendAudioAsync(float[] audioData, int sampleRate, CancellationToken token)
        {
            await this._streamLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!this._streamActive || this._finishRequested || this._abortRequested)
                {
                    return;
                }

                this.AppendAudioCore(audioData, sampleRate, isFinal: false);
            }
            finally
            {
                this._streamLock.Release();
            }
        }

        private async Task FinishUtteranceAsync(CancellationToken token)
        {
            string finalText = string.Empty;
            bool shouldNotifyFailure = false;
            Task<string>? finalResultTask = null;

            await this._streamLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!this._streamActive || this._finishRequested || this._abortRequested)
                {
                    return;
                }

                this._finishRequested = true;
                this.FlushFinalAudioCore();
                finalResultTask = this._finalResultCompletion!.Task;
            }
            finally
            {
                this._streamLock.Release();
            }

            try
            {
                finalText = await finalResultTask!.WaitAsync(TimeSpan.FromSeconds(this._responseTimeoutSeconds), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (this._abortRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                shouldNotifyFailure = !this._abortRequested;
                this.Logger.LogError(ex, "Huoshan ASR {ModelName} failed while waiting for the final result.", this.ModelName);
            }
            finally
            {
                await this._streamLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    this._streamActive = false;
                    await this.CloseWebSocketAsync(expectedClose: true).ConfigureAwait(false);
                    this._pendingAudio.Clear();
                }
                finally
                {
                    this._streamLock.Release();
                }
            }

            if (!this._abortRequested)
            {
                this._asrEventCallback?.OnSpeechTextConverted(!shouldNotifyFailure, shouldNotifyFailure ? string.Empty : finalText);
            }
        }

        private async Task AbortUtteranceAsync()
        {
            await this._streamLock.WaitAsync().ConfigureAwait(false);
            try
            {
                this._abortRequested = true;
                this._streamActive = false;
                this._pendingAudio.Clear();
                this._initializationCompletion?.TrySetCanceled();
                this._finalResultCompletion?.TrySetCanceled();
                await this.CloseWebSocketAsync(expectedClose: true).ConfigureAwait(false);
            }
            finally
            {
                this._streamLock.Release();
            }
        }

        private void AppendAudioCore(float[] audioData, int sampleRate, bool isFinal)
        {
            if (audioData is { Length: > 0 })
            {
                float[] normalized = this.NormalizeSampleRate(audioData, sampleRate);
                this._pendingAudio.AddRange(normalized.Float2PcmBytes(BitsPerSample, ChannelCount));
            }

            while (!isFinal && this._pendingAudio.Count >= this._packetSizeBytes)
            {
                byte[] packet = this._pendingAudio.Take(this._packetSizeBytes).ToArray();
                this._pendingAudio.RemoveRange(0, this._packetSizeBytes);
                this.SendAudioPacketCore(packet, isLast: false);
            }
        }

        private void FlushFinalAudioCore()
        {
            while (this._pendingAudio.Count > this._packetSizeBytes)
            {
                byte[] packet = this._pendingAudio.Take(this._packetSizeBytes).ToArray();
                this._pendingAudio.RemoveRange(0, this._packetSizeBytes);
                this.SendAudioPacketCore(packet, isLast: false);
            }

            byte[] finalPacket = this._pendingAudio.ToArray();
            this._pendingAudio.Clear();
            this.SendAudioPacketCore(finalPacket, isLast: true);
        }

        private void SendAudioPacketCore(byte[] audioData, bool isLast)
        {
            if (this._webSocketClient is null)
            {
                throw new InvalidOperationException("The Huoshan ASR WebSocket is not initialized.");
            }

            byte[] frame = HuoshanAsrMessageCodec.CreateAudioRequest(this.NextSequence(), audioData, isLast);
            this._webSocketClient.SendAsync(frame).GetAwaiter().GetResult();
        }

        private void CreateWebSocketClient()
        {
            IDictionary<string, string> headers = new Dictionary<string, string>
            {
                ["X-Api-Resource-Id"] = this._resourceId,
                ["X-Api-Request-Id"] = Guid.NewGuid().ToString(),
                ["X-Api-Connect-Id"] = Guid.NewGuid().ToString()
            };

            if (!string.IsNullOrWhiteSpace(this._apiKey))
            {
                headers["X-Api-Key"] = this._apiKey;
            }
            else
            {
                headers["X-Api-App-Key"] = this._appId;
                headers["X-Api-Access-Key"] = this._accessToken;
            }

            this._webSocketClient = new WebSocketClient(headers);
            this._webSocketClient.OnBinaryMessage += this.OnBinaryMessage;
            this._webSocketClient.OnClose += this.OnWebSocketClosed;
            this._webSocketClient.OnError += this.OnWebSocketError;
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

            if (this.SupportsLanguage && !string.IsNullOrWhiteSpace(this._language))
            {
                audio["Language"] = this._language;
            }

            return new
            {
                User = new { Uid = this.DeviceId },
                Audio = audio,
                Request = new
                {
                    ModelName = this._asrModelName,
                    EnableItn = this._enableItn,
                    EnablePunc = this._enablePunc,
                    EnableDdc = this._enableDdc,
                    ShowUtterances = true
                }
            };
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
                    string serviceError = GetString(payload?["error"]) ?? GetString(payload?["message"]) ?? "Unknown service error";
                    this.FailPendingOperations(new InvalidOperationException($"Huoshan ASR returned service error {serviceCode}: {serviceError}"));
                    return;
                }

                this._initializationCompletion?.TrySetResult(response);
                this.ExtractText(payload);

                if (response.IsLastPackage)
                {
                    this._finalResultCompletion?.TrySetResult(this.GetFinalText());
                }
            }
            catch (Exception ex)
            {
                this.FailPendingOperations(ex);
            }
        }

        private void ExtractText(JsonObject? payload)
        {
            JsonNode? result = payload?["result"];
            if (result is JsonObject resultObject)
            {
                string? text = GetString(resultObject["text"]);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    this._latestText = text;
                }

                if (resultObject["utterances"] is JsonArray utterances)
                {
                    foreach (JsonNode? utteranceNode in utterances)
                    {
                        if (utteranceNode is not JsonObject utterance || GetBool(utterance["definite"]) != true)
                        {
                            continue;
                        }

                        string? utteranceText = GetString(utterance["text"]);
                        if (!string.IsNullOrWhiteSpace(utteranceText) && !this._definiteUtterances.Contains(utteranceText, StringComparer.Ordinal))
                        {
                            this._definiteUtterances.Add(utteranceText);
                        }
                    }
                }
            }
        }

        private string GetFinalText()
        {
            return this._definiteUtterances.Count > 0 ? string.Concat(this._definiteUtterances) : this._latestText;
        }

        private float[] NormalizeSampleRate(float[] audioData, int sampleRate)
        {
            if (sampleRate == TargetSampleRate)
            {
                return audioData;
            }

            if (sampleRate != 8000)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "Huoshan ASR accepts 8 kHz or 16 kHz source audio.");
            }

            var resampled = new float[audioData.Length * 2];
            for (int i = 0; i < audioData.Length; i++)
            {
                resampled[i * 2] = audioData[i];
                resampled[i * 2 + 1] = audioData[i];
            }
            return resampled;
        }

        private void OnWebSocketClosed(WebSocketCloseStatus? status, string? description)
        {
            if (!this._expectedClose && this._streamActive)
            {
                this.FailPendingOperations(new WebSocketException($"Huoshan ASR WebSocket closed unexpectedly: {status} {description}"));
            }
        }

        private void OnWebSocketError(WebSocketError error, string message)
        {
            this.FailPendingOperations(new WebSocketException($"Huoshan ASR WebSocket error {error}: {message}"));
        }

        private void FailPendingOperations(Exception exception)
        {
            this._initializationCompletion?.TrySetException(exception);
            this._finalResultCompletion?.TrySetException(exception);
        }

        private async Task CloseWebSocketAsync(bool expectedClose)
        {
            this._expectedClose = expectedClose;
            WebSocketClient? webSocketClient = this._webSocketClient;
            this._webSocketClient = null;
            if (webSocketClient is null)
            {
                return;
            }

            try
            {
                await webSocketClient.CloseAsync().ConfigureAwait(false);
            }
            finally
            {
                webSocketClient.Dispose();
            }
        }

        private void ResetUtteranceState(int sampleRate)
        {
            this._pendingAudio.Clear();
            this._definiteUtterances.Clear();
            this._latestText = string.Empty;
            this._nextSequence = 1;
            this._streamActive = false;
            this._finishRequested = false;
            this._abortRequested = false;
            this._expectedClose = false;
        }

        private int NextSequence() => this._nextSequence++;

        private void AbortSynchronously()
        {
            try
            {
                this.AbortUtteranceAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                this.Logger.LogDebug(ex, "Huoshan ASR abort during cleanup failed.");
            }
        }

        public override void Dispose()
        {
            this.AbortSynchronously();
            this._streamLock.Dispose();
        }

        private static string? GetString(JsonNode? node)
        {
            return node is JsonValue value && value.TryGetValue<string>(out string? result) ? result : null;
        }

        private static int? GetInt(JsonNode? node)
        {
            return node is JsonValue value && value.TryGetValue<int>(out int result) ? result : null;
        }

        private static bool? GetBool(JsonNode? node)
        {
            return node is JsonValue value && value.TryGetValue<bool>(out bool result) ? result : null;
        }
    }
}
