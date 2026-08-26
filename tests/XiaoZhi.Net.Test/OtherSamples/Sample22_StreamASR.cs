using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using NAudio.Wave;
using XiaoZhi.Net.Test.Socket;
using XiaoZhi.Test.OtherSamples.Huoshan;

namespace XiaoZhi.Net.Test.OtherSamples
{
    /// <summary>
    /// 音频文件必须是 16 kHz、单声道、16-bit PCM WAV，与 Server 传给流式 ASR 的标准格式一致。
    /// </summary>
    internal class Sample22_StreamASR
    {
        private const string TestFilePath = "./audioFile/max_output_size.wav";

        private const string AliyunApiKey = "your api key";

        private const string HuoshanResourceId = "volc.bigasr.sauc.duration";
        private const string HuoshanApiKey = "your api key";
        private const string HuoshanAccessKey = "your access key";

        private const string AliyunEndpoint = "wss://dashscope.aliyuncs.com/api-ws/v1/inference";
        private const string AliyunModelName = "qwen-audio-3.0-asr-flash-streaming";
        private const string HuoshanEndpoint = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async";
        private const string HuoshanModelName = "bigmodel";

        private const int TargetSampleRate = 16000;
        private const int TargetChannels = 1;
        private const int TargetBitsPerSample = 16;
        private const int AliyunPacketSizeBytes = 3200; // 100 ms
        private const int HuoshanPacketSizeBytes = 6400; // 200 ms
        private static readonly TimeSpan s_connectionTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan s_responseTimeout = TimeSpan.FromSeconds(15);
        public static async Task RunAsync()
        {
            //await TestAliyunRealtimeAsrAsync();
            await TestHuoshanBidirectionAsrAsync();
        }

        /// <summary>
        /// 将固定 WAV 文件按 100 ms PCM 分包实时发送到阿里云 DashScope 流式 ASR。
        /// </summary>
        public static async Task<string> TestAliyunRealtimeAsrAsync(CancellationToken token = default)
        {
            EnsureConfigured(AliyunApiKey, nameof(AliyunApiKey));
            using WaveFileReader audioStream = OpenCanonicalPcmWave();
            using var webSocketClient = new WebSocketClient(new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {AliyunApiKey}"
            });

            var taskStartedCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finalResultCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sentences = new SortedDictionary<int, string>();
            object sentencesLock = new();
            string taskId = Guid.NewGuid().ToString("N");
            int nextSentenceIndex = 0;
            bool expectedClose = false;

            webSocketClient.OnTextMessage += HandleTextMessage;
            webSocketClient.OnError += HandleError;
            webSocketClient.OnClose += HandleClose;

            try
            {
                await webSocketClient.ConnectAsync(AliyunEndpoint, token);
                if (!webSocketClient.IsConnected)
                {
                    throw new WebSocketException("Unable to connect to the Aliyun ASR WebSocket service.");
                }
                string taskJson = JsonHelper.Serialize(new
                {
                    Header = new
                    {
                        Action = "run-task",
                        TaskId = taskId,
                        Streaming = "duplex"
                    },
                    Payload = new
                    {
                        TaskGroup = "audio",
                        Task = "asr",
                        Function = "recognition",
                        Model = AliyunModelName,
                        Input = new { },
                        Parameters = new
                        {
                            Format = "pcm",
                            SampleRate = TargetSampleRate,
                            LanguageHints = new[] { "zh", "en" }
                        }
                    }
                });
                await webSocketClient.SendAsync(taskJson);

                await taskStartedCompletion.Task.WaitAsync(s_connectionTimeout, token);
                await SendPcmStreamAsync(audioStream, AliyunPacketSizeBytes, webSocketClient.SendAsync, token);
                await webSocketClient.SendAsync(JsonHelper.Serialize(new
                {
                    Header = new
                    {
                        Action = "finish-task",
                        TaskId = taskId,
                        Streaming = "duplex"
                    },
                    Payload = new
                    {
                        Input = new { }
                    }
                }));

                string text = await finalResultCompletion.Task.WaitAsync(s_responseTimeout, token);
                Console.WriteLine($"Aliyun realtime ASR: {text}");
                return text;
            }
            finally
            {
                expectedClose = true;
                webSocketClient.OnTextMessage -= HandleTextMessage;
                webSocketClient.OnError -= HandleError;
                webSocketClient.OnClose -= HandleClose;
                await webSocketClient.CloseAsync();
            }

            void HandleTextMessage(string message)
            {
                try
                {
                    JsonObject? response = JsonNode.Parse(message) as JsonObject;
                    string? eventName = GetString(response?["header"]?["event"]);
                    switch (eventName)
                    {
                        case "task-started":
                            taskStartedCompletion.TrySetResult();
                            break;
                        case "result-generated":
                            ExtractAliyunSentence(response);
                            break;
                        case "task-finished":
                            ExtractAliyunSentence(response);
                            lock (sentencesLock)
                            {
                                finalResultCompletion.TrySetResult(string.Concat(sentences.Values));
                            }
                            break;
                        case "task-failed":
                            FailPendingOperations(new InvalidOperationException(GetAliyunErrorMessage(response)));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    FailPendingOperations(ex);
                }
            }

            void ExtractAliyunSentence(JsonObject? response)
            {
                JsonObject? output = response?["payload"]?["output"] as JsonObject;
                JsonObject? sentence = output?["sentence"] as JsonObject ?? output;
                string? text = GetString(sentence?["text"]);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                int sentenceId = GetInt(sentence?["sentence_id"]) ?? nextSentenceIndex;
                lock (sentencesLock)
                {
                    nextSentenceIndex = Math.Max(nextSentenceIndex, sentenceId + 1);
                    sentences[sentenceId] = text;
                }
            }

            void HandleError(WebSocketError error, string message) =>
                FailPendingOperations(new WebSocketException($"Aliyun ASR WebSocket error {error}: {message}"));

            void HandleClose(WebSocketCloseStatus? status, string? description)
            {
                if (!expectedClose)
                {
                    FailPendingOperations(new WebSocketException(
                        $"Aliyun ASR WebSocket closed unexpectedly: {status} {description}"));
                }
            }

            void FailPendingOperations(Exception exception)
            {
                taskStartedCompletion.TrySetException(exception);
                finalResultCompletion.TrySetException(exception);
            }
        }

        /// <summary>
        /// 将固定 WAV 文件按 200 ms PCM 分包实时发送到火山引擎双向流式 ASR。
        /// </summary>
        public static async Task<string> TestHuoshanBidirectionAsrAsync(CancellationToken token = default)
        {
            EnsureConfigured(HuoshanApiKey, nameof(HuoshanApiKey));
            EnsureConfigured(HuoshanAccessKey, nameof(HuoshanAccessKey));
            EnsureConfigured(HuoshanResourceId, nameof(HuoshanResourceId));
            using WaveFileReader audioStream = OpenCanonicalPcmWave();
            using var webSocketClient = new WebSocketClient(new Dictionary<string, string>
            {
                ["X-Api-App-Key"] = HuoshanApiKey,
                ["X-Api-Access-Key"] = HuoshanAccessKey,
                ["X-Api-Resource-Id"] = HuoshanResourceId,
                ["X-Api-Request-Id"] = Guid.NewGuid().ToString(),
                ["X-Api-Connect-Id"] = Guid.NewGuid().ToString()
            });

            var initializationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finalResultCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var definiteUtterances = new Dictionary<string, string>();
            object utterancesLock = new();
            string latestText = string.Empty;
            int nextUtteranceIndex = 0;
            int nextSequence = 1;
            bool expectedClose = false;

            webSocketClient.OnBinaryMessage += HandleBinaryMessage;
            webSocketClient.OnError += HandleError;
            webSocketClient.OnClose += HandleClose;

            try
            {
                await webSocketClient.ConnectAsync(HuoshanEndpoint, token);
                if (!webSocketClient.IsConnected)
                {
                    throw new WebSocketException("Unable to connect to the Huoshan ASR WebSocket service.");
                }

                byte[] startRequest = HuoshanAsrFrameCodec.CreateFullRequest(
                    NextSequence(),
                    JsonHelper.SerializeToUtf8Bytes(new
                    {
                        User = new { Uid = "sample22-stream-asr" },
                        Audio = new
                        {
                            Format = "pcm",
                            Codec = "raw",
                            Rate = TargetSampleRate,
                            Bits = TargetBitsPerSample,
                            Channel = TargetChannels
                        },
                        Request = new
                        {
                            ModelName = HuoshanModelName,
                            EnableItn = true,
                            EnablePunc = true,
                            EnableDdc = true,
                            ShowUtterances = true
                        }
                    }));
                await webSocketClient.SendAsync(startRequest);

                await initializationCompletion.Task.WaitAsync(s_responseTimeout, token);
                await SendPcmStreamAsync(
                    audioStream,
                    HuoshanPacketSizeBytes,
                    packet => webSocketClient.SendAsync(HuoshanAsrFrameCodec.CreateAudioRequest(NextSequence(), packet, isLast: false)),
                    token);
                await webSocketClient.SendAsync(HuoshanAsrFrameCodec.CreateAudioRequest(NextSequence(), Array.Empty<byte>(), isLast: true));

                string text = await finalResultCompletion.Task.WaitAsync(s_responseTimeout, token);
                Console.WriteLine($"Huoshan bidirection ASR: {text}");
                return text;
            }
            finally
            {
                expectedClose = true;
                webSocketClient.OnBinaryMessage -= HandleBinaryMessage;
                webSocketClient.OnError -= HandleError;
                webSocketClient.OnClose -= HandleClose;
                await webSocketClient.CloseAsync();
            }

            int NextSequence() => nextSequence++;

            void HandleBinaryMessage(byte[] data)
            {
                try
                {
                    HuoshanAsrResponse response = HuoshanAsrFrameCodec.ParseResponse(data);
                    if (response.ErrorCode != 0)
                    {
                        FailPendingOperations(new InvalidOperationException(
                            $"Huoshan ASR returned protocol error {response.ErrorCode}."));
                        return;
                    }

                    JsonObject? payload = response.Payload as JsonObject;
                    int? serviceCode = GetInt(payload?["code"]);
                    if (serviceCode.HasValue && serviceCode is not 1000 and not 1013)
                    {
                        string serviceError = GetString(payload?["error"])
                            ?? GetString(payload?["message"])
                            ?? "Unknown service error";
                        FailPendingOperations(new InvalidOperationException(
                            $"Huoshan ASR returned service error {serviceCode}: {serviceError}"));
                        return;
                    }

                    initializationCompletion.TrySetResult();
                    ExtractHuoshanText(payload);
                    if (response.IsLastPackage)
                    {
                        lock (utterancesLock)
                        {
                            finalResultCompletion.TrySetResult(
                                definiteUtterances.Count > 0
                                    ? string.Concat(definiteUtterances.Values)
                                    : latestText);
                        }
                    }
                }
                catch (Exception ex)
                {
                    FailPendingOperations(ex);
                }
            }

            void ExtractHuoshanText(JsonObject? payload)
            {
                if (payload?["result"] is not JsonObject resultObject)
                {
                    return;
                }

                string? text = GetString(resultObject["text"]);
                lock (utterancesLock)
                {
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        latestText = text;
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
                            ?? $"local-{nextUtteranceIndex++:D8}";
                        definiteUtterances[utteranceKey] = utteranceText;
                    }
                }
            }

            void HandleError(WebSocketError error, string message) =>
                FailPendingOperations(new WebSocketException($"Huoshan ASR WebSocket error {error}: {message}"));

            void HandleClose(WebSocketCloseStatus? status, string? description)
            {
                if (!expectedClose)
                {
                    FailPendingOperations(new WebSocketException(
                        $"Huoshan ASR WebSocket closed unexpectedly: {status} {description}"));
                }
            }

            void FailPendingOperations(Exception exception)
            {
                initializationCompletion.TrySetException(exception);
                finalResultCompletion.TrySetException(exception);
            }
        }

        private static WaveFileReader OpenCanonicalPcmWave()
        {
            if (!File.Exists(TestFilePath))
            {
                throw new FileNotFoundException($"Test audio file was not found: {TestFilePath}", TestFilePath);
            }

            var reader = new WaveFileReader(TestFilePath);
            WaveFormat format = reader.WaveFormat;
            if (format.Encoding == WaveFormatEncoding.Pcm
                && format.SampleRate == TargetSampleRate
                && format.Channels == TargetChannels
                && format.BitsPerSample == TargetBitsPerSample)
            {
                return reader;
            }

            reader.Dispose();
            throw new NotSupportedException(
                $"{TestFilePath} must be {TargetSampleRate} Hz, {TargetChannels} channel, {TargetBitsPerSample}-bit PCM WAV. "
                + $"Actual format: {format.Encoding}, {format.SampleRate} Hz, {format.Channels} channel, {format.BitsPerSample}-bit.");
        }

        private static async Task SendPcmStreamAsync(
            Stream audioStream,
            int packetSizeBytes,
            Func<byte[], Task> sendAsync,
            CancellationToken token)
        {
            byte[] buffer = new byte[packetSizeBytes];
            while (true)
            {
                int bytesRead = await ReadUpToAsync(audioStream, buffer, token);
                if (bytesRead == 0)
                {
                    return;
                }

                byte[] packet = bytesRead == buffer.Length ? buffer.ToArray() : buffer[..bytesRead];
                await sendAsync(packet);
                await Task.Delay(TimeSpan.FromSeconds((double)bytesRead / GetPcmBytesPerSecond()), token);
            }
        }

        private static async Task<int> ReadUpToAsync(Stream stream, byte[] buffer, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(totalRead), token);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            return totalRead;
        }

        private static int GetPcmBytesPerSecond() =>
            TargetSampleRate * TargetChannels * (TargetBitsPerSample / 8);

        private static void EnsureConfigured(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Please set {name} in {nameof(Sample22_StreamASR)} before running this test.");
            }
        }

        private static string? GetString(JsonNode? node) => node is JsonValue value
            && value.TryGetValue<string>(out string? result) ? result : null;

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

        private static bool? GetBool(JsonNode? node) => node is JsonValue value
            && value.TryGetValue<bool>(out bool result) ? result : null;

        private static string GetAliyunErrorMessage(JsonObject? response) =>
            GetString(response?["header"]?["error_message"])
            ?? GetString(response?["payload"]?["output"]?["message"])
            ?? "Aliyun ASR task failed.";
    }

    internal sealed record HuoshanAsrResponse(
        byte MessageType,
        byte Flags,
        int Sequence,
        int EventType,
        int ErrorCode,
        JsonNode? Payload)
    {
        public bool IsLastPackage => (this.Flags & 0b0010) != 0;
    }

    /// <summary>
    /// 火山引擎 V3 SAUC ASR 协议。结束音频帧使用 0b0011 标志和负序号，不能复用 TTS 的旧协议对象。
    /// </summary>
    internal static class HuoshanAsrFrameCodec
    {
        private const byte Version1 = 1;
        private const byte HeaderSize4 = 1;
        private const byte FullClientRequest = 0b0001;
        private const byte AudioOnlyClient = 0b0010;
        private const byte Error = 0b1111;
        private const byte Json = 0b0001;
        private const byte Gzip = 0b0001;
        private const byte PositiveSequence = 0b0001;
        private const byte LastWithSequence = 0b0011;

        public static byte[] CreateFullRequest(int sequence, byte[] jsonPayload) =>
            CreateFrame(FullClientRequest, PositiveSequence, sequence, jsonPayload);

        public static byte[] CreateAudioRequest(int sequence, byte[] audioPayload, bool isLast) =>
            CreateFrame(
                AudioOnlyClient,
                isLast ? LastWithSequence : PositiveSequence,
                isLast ? -sequence : sequence,
                audioPayload);

        public static HuoshanAsrResponse ParseResponse(byte[] data)
        {
            if (data is null || data.Length < 4)
            {
                throw new InvalidDataException("The Huoshan ASR response is shorter than its header.");
            }
            if ((data[0] >> 4) != Version1)
            {
                throw new InvalidDataException("The Huoshan ASR response uses an unsupported protocol version.");
            }

            int headerSize = data[0] & 0x0F;
            if (headerSize < 1 || data.Length < headerSize * 4)
            {
                throw new InvalidDataException("The Huoshan ASR response has an invalid header size.");
            }

            byte messageType = (byte)(data[1] >> 4);
            byte flags = (byte)(data[1] & 0x0F);
            byte serialization = (byte)(data[2] >> 4);
            byte compression = (byte)(data[2] & 0x0F);
            int offset = headerSize * 4;

            int sequence = 0;
            if ((flags & PositiveSequence) != 0)
            {
                EnsureAvailable(data, offset, 4);
                sequence = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
                offset += 4;
            }

            int eventType = 0;
            if ((flags & 0b0100) != 0)
            {
                EnsureAvailable(data, offset, 4);
                eventType = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
                offset += 4;
            }

            int errorCode = 0;
            if (messageType == Error)
            {
                EnsureAvailable(data, offset, 4);
                errorCode = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
                offset += 4;
            }

            EnsureAvailable(data, offset, 4);
            uint payloadLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
            offset += 4;
            int payloadLengthValue = checked((int)payloadLength);
            EnsureAvailable(data, offset, payloadLengthValue);
            byte[] payload = data.AsSpan(offset, payloadLengthValue).ToArray();
            if (offset + payloadLengthValue != data.Length)
            {
                throw new InvalidDataException("The Huoshan ASR response has trailing bytes after its payload.");
            }

            if (compression == Gzip && payload.Length > 0)
            {
                payload = Decompress(payload);
            }

            JsonNode? jsonPayload = serialization == Json && payload.Length > 0
                ? JsonNode.Parse(payload)
                : null;
            return new HuoshanAsrResponse(messageType, flags, sequence, eventType, errorCode, jsonPayload);
        }

        private static byte[] CreateFrame(byte messageType, byte flags, int sequence, byte[] payload)
        {
            byte[] compressedPayload = Compress(payload ?? Array.Empty<byte>());
            byte[] frame = new byte[12 + compressedPayload.Length];
            frame[0] = (byte)((Version1 << 4) | HeaderSize4);
            frame[1] = (byte)((messageType << 4) | flags);
            frame[2] = (byte)((Json << 4) | Gzip);
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(4, 4), sequence);
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8, 4), (uint)compressedPayload.Length);
            compressedPayload.CopyTo(frame, 12);
            return frame;
        }

        private static byte[] Compress(byte[] payload)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                gzip.Write(payload, 0, payload.Length);
            }
            return output.ToArray();
        }

        private static byte[] Decompress(byte[] payload)
        {
            using var input = new MemoryStream(payload);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }

        private static void EnsureAvailable(byte[] data, int offset, int count)
        {
            if (offset < 0 || count < 0 || data.Length - offset < count)
            {
                throw new InvalidDataException("The Huoshan ASR response is truncated.");
            }
        }
    }
}
