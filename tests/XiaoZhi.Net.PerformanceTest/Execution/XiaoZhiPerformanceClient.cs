using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Serilog;
using XiaoZhi.Net.PerformanceTest.Audio;

namespace XiaoZhi.Net.PerformanceTest.Execution;

internal sealed class XiaoZhiPerformanceClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly TimeSpan s_connectionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_helloTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_detectTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan s_roundTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan s_betweenDetectAndAudio = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_audioFrameDuration = TimeSpan.FromMilliseconds(60);

    private readonly Uri _serverUri;
    private readonly string _deviceId;
    private readonly CachedAudio _audio;
    private readonly ILogger _logger;
    private readonly ClientWebSocket _socket = new();
    private readonly TaskCompletionSource<HelloResponse> _hello = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<long> _detectFirstAudio = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _detectStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<long> _inputFirstAudio = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _conversationStop = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _receiveTask;
    private string? _sessionId;
    private int _phase = (int)ConversationPhase.Initial;
    private int _earlyAudioBeforeStop;

    public XiaoZhiPerformanceClient(Uri serverUri, string deviceId, CachedAudio audio, ILogger logger)
    {
        this._serverUri = serverUri;
        this._deviceId = deviceId;
        this._audio = audio;
        this._logger = logger;
        this._socket.Options.SetRequestHeader("device-id", deviceId);
    }

    public async Task<RoundResult> ExecuteAsync(int clientNumber, int roundNumber, CancellationToken cancellationToken)
    {
        RoundResult result = new(clientNumber, roundNumber);
        try
        {
            long connectionStart = Stopwatch.GetTimestamp();
            try
            {
                await this._socket.ConnectAsync(this._serverUri, cancellationToken).WaitAsync(s_connectionTimeout, cancellationToken);
                result.Connection = StageMeasurement.Success(Elapsed(connectionStart));
            }
            catch (Exception exception)
            {
                result.Connection = StageMeasurement.Failed(Elapsed(connectionStart), DescribeException("connection", exception));
                result.Failure = result.Connection.Failure;
                return result;
            }

            this._lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            this._receiveTask = this.ReceiveLoopAsync(this._lifetimeCancellation.Token);

            long helloStart = Stopwatch.GetTimestamp();
            try
            {
                await this.SendJsonAsync(new
                {
                    type = "hello",
                    audio_params = new
                    {
                        format = "opus",
                        sample_rate = this._audio.Format.SampleRate,
                        channels = this._audio.Format.Channels,
                        frame_duration = this._audio.Format.FrameDurationMilliseconds
                    }
                }, cancellationToken);
                HelloResponse hello = await WaitForAsync(this._hello.Task, s_helloTimeout, "Hello", cancellationToken);
                this._sessionId = hello.SessionId;
                result.Hello = StageMeasurement.Success(Elapsed(helloStart, hello.ReceivedAt));
            }
            catch (Exception exception)
            {
                result.Hello = StageMeasurement.Failed(Elapsed(helloStart), DescribeException("hello", exception));
                result.Failure = result.Hello.Failure;
                return result;
            }

            long detectStart = Stopwatch.GetTimestamp();
            try
            {
                this.SetPhase(ConversationPhase.AwaitingDetectAudio);
                await this.SendJsonAsync(new
                {
                    type = "listen",
                    state = "detect",
                    text = "你好小智",
                    session_id = this._sessionId
                }, cancellationToken);
                long detectAudioAt = await WaitForAsync(this._detectFirstAudio.Task, s_detectTimeout, "Detect 首帧音频", cancellationToken);
                result.Detect = StageMeasurement.Success(Elapsed(detectStart, detectAudioAt));
                await WaitForAsync(this._detectStop.Task, s_roundTimeout, "Detect TTS stop", cancellationToken);
            }
            catch (Exception exception)
            {
                result.Detect = StageMeasurement.Failed(Elapsed(detectStart), DescribeException("detect", exception));
                result.Failure = result.Detect.Failure;
                return result;
            }

            await Task.Delay(s_betweenDetectAndAudio, cancellationToken);

            long audioPreparationStart = Stopwatch.GetTimestamp();
            long audioStart = 0;
            try
            {
                this.SetPhase(ConversationPhase.StreamingInput);
                await this.SendJsonAsync(new
                {
                    type = "listen",
                    state = "start",
                    mode = "manual",
                    session_id = this._sessionId
                }, cancellationToken);

                await this.SendAudioFramesAsync(cancellationToken);

                this.SetPhase(ConversationPhase.AwaitingInputAudio);
                audioStart = Stopwatch.GetTimestamp();
                await this.SendJsonAsync(new
                {
                    type = "listen",
                    state = "stop",
                    mode = "manual",
                    session_id = this._sessionId
                }, cancellationToken);

                if (Volatile.Read(ref this._earlyAudioBeforeStop) != 0)
                {
                    throw new PerformanceStageException("在发送 listen/stop 前收到响应音频，无法按指定口径计算首帧时延。");
                }

                long firstAudioAt = await WaitForAsync(this._inputFirstAudio.Task, s_roundTimeout, "固定音频首帧", cancellationToken);
                result.AudioFirstFrame = StageMeasurement.Success(Elapsed(audioStart, firstAudioAt));
                this.SetPhase(ConversationPhase.AwaitingConversationStop);
                await WaitForAsync(this._conversationStop.Task, s_roundTimeout, "音频响应 TTS stop", cancellationToken);
                result.Completed = true;
                this.SetPhase(ConversationPhase.Completed);
                return result;
            }
            catch (Exception exception)
            {
                long failureStart = audioStart == 0 ? audioPreparationStart : audioStart;
                result.AudioFirstFrame = StageMeasurement.Failed(Elapsed(failureStart), DescribeException("audio", exception));
                result.Failure = result.AudioFirstFrame.Failure;
                return result;
            }
        }
        finally
        {
            await this.DisposeAsync();
        }
    }

    private async Task SendAudioFramesAsync(CancellationToken cancellationToken)
    {
        long scheduleStart = Stopwatch.GetTimestamp();
        for (int index = 0; index < this._audio.OpusFrames.Count; index++)
        {
            await this._socket.SendAsync(this._audio.OpusFrames[index], WebSocketMessageType.Binary, true, cancellationToken);
            if (index >= this._audio.OpusFrames.Count - 1)
            {
                continue;
            }

            long nextFrameAt = scheduleStart + (long)((index + 1) * s_audioFrameDuration.TotalSeconds * Stopwatch.Frequency);
            await DelayUntilAsync(nextFrameAt, cancellationToken);
        }
    }

    private async Task SendJsonAsync<T>(T message, CancellationToken cancellationToken)
    {
        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, s_jsonOptions));
        await this._socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using MemoryStream message = new();
                WebSocketMessageType? messageType = null;
                WebSocketReceiveResult receiveResult;
                do
                {
                    receiveResult = await this._socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        throw new WebSocketException($"服务端关闭 WebSocket: {this._socket.CloseStatus} {this._socket.CloseStatusDescription}");
                    }

                    messageType ??= receiveResult.MessageType;
                    if (messageType != receiveResult.MessageType)
                    {
                        throw new WebSocketException("服务端发送了类型不一致的分片消息。");
                    }

                    if (receiveResult.MessageType == WebSocketMessageType.Binary)
                    {
                        this.HandleBinary(Stopwatch.GetTimestamp());
                    }
                    else
                    {
                        message.Write(buffer, 0, receiveResult.Count);
                    }
                }
                while (!receiveResult.EndOfMessage);

                if (messageType == WebSocketMessageType.Text)
                {
                    this.HandleText(Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length)));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            this.CompletePendingWithException(exception);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void HandleText(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string? type = GetString(root, "type");
            if (string.Equals(type, "hello", StringComparison.OrdinalIgnoreCase))
            {
                string? sessionId = GetString(root, "session_id");
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    this._hello.TrySetException(new PerformanceStageException("收到的 Hello 不包含 session_id。"));
                    return;
                }

                this._hello.TrySetResult(new HelloResponse(sessionId, Stopwatch.GetTimestamp()));
                return;
            }

            if (string.Equals(type, "tts", StringComparison.OrdinalIgnoreCase)
                && string.Equals(GetString(root, "state"), "stop", StringComparison.OrdinalIgnoreCase))
            {
                ConversationPhase phase = this.GetPhase();
                if (phase is ConversationPhase.AwaitingDetectAudio or ConversationPhase.AwaitingDetectStop)
                {
                    this._detectStop.TrySetResult();
                }
                else if (phase is ConversationPhase.AwaitingInputAudio or ConversationPhase.AwaitingConversationStop)
                {
                    this._conversationStop.TrySetResult();
                }
            }
        }
        catch (JsonException exception)
        {
            this._logger.Warning(exception, "[{DeviceId}] 收到无效 JSON 文本消息。", this._deviceId);
        }
    }

    private void HandleBinary(long receivedAt)
    {
        ConversationPhase phase = this.GetPhase();
        if (phase == ConversationPhase.AwaitingDetectAudio)
        {
            this._detectFirstAudio.TrySetResult(receivedAt);
            this.SetPhase(ConversationPhase.AwaitingDetectStop);
            return;
        }

        if (phase == ConversationPhase.StreamingInput)
        {
            Interlocked.Exchange(ref this._earlyAudioBeforeStop, 1);
            return;
        }

        if (phase is ConversationPhase.AwaitingInputAudio or ConversationPhase.AwaitingConversationStop)
        {
            this._inputFirstAudio.TrySetResult(receivedAt);
            this.SetPhase(ConversationPhase.AwaitingConversationStop);
        }
    }

    private static string? GetString(JsonElement element, string expectedName)
    {
        string normalizedExpected = NormalizePropertyName(expectedName);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (NormalizePropertyName(property.Name) == normalizedExpected && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static string NormalizePropertyName(string value) => value.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private void CompletePendingWithException(Exception exception)
    {
        this._hello.TrySetException(exception);
        this._detectFirstAudio.TrySetException(exception);
        this._detectStop.TrySetException(exception);
        this._inputFirstAudio.TrySetException(exception);
        this._conversationStop.TrySetException(exception);
    }

    private void SetPhase(ConversationPhase phase) => Volatile.Write(ref this._phase, (int)phase);
    private ConversationPhase GetPhase() => (ConversationPhase)Volatile.Read(ref this._phase);

    private static async Task<T> WaitForAsync<T>(Task<T> task, TimeSpan timeout, string stage, CancellationToken cancellationToken)
    {
        try
        {
            return await task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            throw new PerformanceStageException($"{stage} 超时（{timeout.TotalSeconds:0} 秒）。", exception);
        }
    }

    private static async Task WaitForAsync(Task task, TimeSpan timeout, string stage, CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            throw new PerformanceStageException($"{stage} 超时（{timeout.TotalSeconds:0} 秒）。", exception);
        }
    }

    private static async Task DelayUntilAsync(long targetTimestamp, CancellationToken cancellationToken)
    {
        long remainingTicks = targetTimestamp - Stopwatch.GetTimestamp();
        if (remainingTicks > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(remainingTicks / (double)Stopwatch.Frequency), cancellationToken);
        }
    }

    private static TimeSpan Elapsed(long start) => Elapsed(start, Stopwatch.GetTimestamp());
    private static TimeSpan Elapsed(long start, long end) => TimeSpan.FromSeconds((end - start) / (double)Stopwatch.Frequency);
    private static string DescribeException(string stage, Exception exception) => $"{stage}: {exception.GetType().Name}: {exception.Message}";

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (this._socket.State == WebSocketState.Open)
            {
                await this._socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "performance test round complete", CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        catch (WebSocketException)
        {
        }
        catch (TimeoutException)
        {
        }
        finally
        {
            this._lifetimeCancellation?.Cancel();
            if (this._receiveTask is not null)
            {
                try
                {
                    await this._receiveTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            this._lifetimeCancellation?.Dispose();
            this._socket.Dispose();
        }
    }

}
