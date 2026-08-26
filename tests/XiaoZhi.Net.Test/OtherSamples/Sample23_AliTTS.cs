using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using XiaoZhi.Net.Test.Socket;

namespace XiaoZhi.Net.Test.OtherSamples
{
    /// <summary>
    /// 实时 WebSocket、HTTP 非流式和 HTTP SSE 流式测试用例
    /// 音频均为 24 kHz、16-bit、单声道原始 PCM，输出到 data/tts-cache。
    /// </summary>
    internal class Sample23_AliTTS
    {
        private const string AliyunApiKey = "your api key";
        // HTTP 测试可直接配置完整 HTTPS Endpoint；为空时才使用 WorkspaceId 构造北京地域端点。
        private const string AliyunHttpEndpoint = "";
        private const string AliyunWorkspaceId = "your workspace id";

        private const string RealtimeEndpoint = "wss://dashscope.aliyuncs.com/api-ws/v1/inference";
        private const string HttpEndpointPath = "/api/v1/services/audio/tts/SpeechSynthesizer";
        private const string ModelName = "qwen-audio-3.0-tts-flash";
        private const string Voice = "longanfengyue";
        private const string TestText = "春眠不觉晓，处处闻啼鸟。夜来风雨声，花落知多少。";

        private const int SampleRate = 24000;
        private const int Volume = 50;
        private const float Rate = 1.0F;
        private const float Pitch = 1.0F;

        private static readonly TimeSpan s_connectionTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan s_realtimeResponseTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan s_httpResponseTimeout = TimeSpan.FromSeconds(120);
        private static readonly HttpClient s_httpClient = new();

        /// <summary>
        /// 手动选择并调用 TestAliyunRealtimeTtsAsync、TestAliyunHttpNonStreamingTtsAsync
        /// 或 TestAliyunHttpStreamingTtsAsync。
        /// </summary>
        public static async Task RunAsync()
        {
            //await TestAliyunRealtimeTtsAsync();
            //await TestAliyunHttpNonStreamingTtsAsync();
            await TestAliyunHttpStreamingTtsAsync();
        }

        /// <summary>
        /// 通过阿里云实时 WebSocket TTS 合成固定文本。
        /// </summary>
        public static async Task<byte[]> TestAliyunRealtimeTtsAsync(CancellationToken token = default)
        {
            EnsureConfigured(AliyunApiKey, nameof(AliyunApiKey));

            using var webSocketClient = new WebSocketClient(new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {AliyunApiKey}"
            });

            var taskStartedCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var taskFinishedCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var audio = new List<byte>();
            object audioLock = new();
            string taskId = Guid.NewGuid().ToString("N");
            bool expectedClose = false;

            webSocketClient.OnTextMessage += HandleTextMessage;
            webSocketClient.OnBinaryMessage += HandleBinaryMessage;
            webSocketClient.OnError += HandleWebSocketError;
            webSocketClient.OnClose += HandleWebSocketClose;

            try
            {
                await webSocketClient.ConnectAsync(RealtimeEndpoint, token);
                if (!webSocketClient.IsConnected)
                {
                    throw new WebSocketException("Unable to connect to the Aliyun TTS WebSocket service.");
                }

                await webSocketClient.SendAsync(JsonHelper.Serialize(CreateRealtimeRunTaskRequest(taskId)));
                await taskStartedCompletion.Task.WaitAsync(s_connectionTimeout, token);

                await webSocketClient.SendAsync(JsonHelper.Serialize(CreateRealtimeContinueTaskRequest(taskId, TestText)));
                await webSocketClient.SendAsync(JsonHelper.Serialize(CreateRealtimeFinishTaskRequest(taskId)));
                await taskFinishedCompletion.Task.WaitAsync(s_realtimeResponseTimeout, token);

                byte[] audioData;
                lock (audioLock)
                {
                    audioData = audio.ToArray();
                }

                return await SavePcmAsync("aliyun_realtime", audioData, token);
            }
            finally
            {
                expectedClose = true;
                webSocketClient.OnTextMessage -= HandleTextMessage;
                webSocketClient.OnBinaryMessage -= HandleBinaryMessage;
                webSocketClient.OnError -= HandleWebSocketError;
                webSocketClient.OnClose -= HandleWebSocketClose;
                await webSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "TTS test completed");
            }

            void HandleTextMessage(string text)
            {
                try
                {
                    var response = DeserializeJson(text, new
                    {
                        Header = new
                        {
                            TaskId = string.Empty,
                            Event = string.Empty,
                            ErrorMessage = string.Empty
                        },
                        Payload = new
                        {
                            Output = new
                            {
                                Message = string.Empty
                            }
                        }
                    }) ?? throw new InvalidOperationException("Aliyun TTS WebSocket returned invalid JSON.");

                    if (!taskId.Equals(response.Header?.TaskId, StringComparison.Ordinal))
                    {
                        return;
                    }

                    switch (response.Header?.Event)
                    {
                        case "task-started":
                            taskStartedCompletion.TrySetResult();
                            break;
                        case "task-finished":
                            taskFinishedCompletion.TrySetResult();
                            break;
                        case "task-failed":
                            SetWebSocketFailure(new InvalidOperationException(
                                response.Header?.ErrorMessage
                                ?? response.Payload?.Output?.Message
                                ?? "Aliyun TTS task failed."));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    SetWebSocketFailure(ex);
                }
            }

            void HandleBinaryMessage(byte[] audioData)
            {
                if (audioData.Length == 0)
                {
                    return;
                }

                lock (audioLock)
                {
                    audio.AddRange(audioData);
                }
            }

            void HandleWebSocketError(WebSocketError error, string message) =>
                SetWebSocketFailure(new WebSocketException($"Aliyun TTS WebSocket error {error}: {message}"));

            void HandleWebSocketClose(WebSocketCloseStatus? status, string? description)
            {
                if (!expectedClose && !taskFinishedCompletion.Task.IsCompleted)
                {
                    SetWebSocketFailure(new WebSocketException($"Aliyun TTS WebSocket closed: {status} {description}"));
                }
            }

            void SetWebSocketFailure(Exception exception)
            {
                taskStartedCompletion.TrySetException(exception);
                taskFinishedCompletion.TrySetException(exception);
            }
        }

        /// <summary>
        /// 通过阿里云 HTTP TTS 的非流式 URL 下载模式合成固定文本。
        /// </summary>
        public static Task<byte[]> TestAliyunHttpNonStreamingTtsAsync(CancellationToken token = default) =>
            TestAliyunHttpTtsAsync(streaming: false, token);

        /// <summary>
        /// 通过阿里云 HTTP TTS 的 SSE 流式模式合成固定文本。
        /// </summary>
        public static Task<byte[]> TestAliyunHttpStreamingTtsAsync(CancellationToken token = default) =>
            TestAliyunHttpTtsAsync(streaming: true, token);

        private static async Task<byte[]> TestAliyunHttpTtsAsync(bool streaming, CancellationToken token)
        {
            EnsureConfigured(AliyunApiKey, nameof(AliyunApiKey));

            using var request = new HttpRequestMessage(HttpMethod.Post, ResolveHttpEndpoint())
            {
                Content = new StringContent(
                    JsonHelper.Serialize(CreateHttpRequest(TestText)),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AliyunApiKey);
            if (streaming)
            {
                request.Headers.TryAddWithoutValidation("X-DashScope-SSE", "enable");
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutSource.CancelAfter(s_httpResponseTimeout);
            using HttpResponseMessage response = await s_httpClient.SendAsync(
                request,
                streaming ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                timeoutSource.Token);
            await EnsureSuccessAsync(response, "Aliyun HTTP TTS request");

            byte[] audioData = streaming
                ? await ReadSseAudioAsync(response, timeoutSource.Token)
                : await DownloadNonStreamingAudioAsync(response, timeoutSource.Token);

            return await SavePcmAsync(
                streaming ? "aliyun_http_streaming" : "aliyun_http",
                audioData,
                token);
        }

        private static object CreateRealtimeRunTaskRequest(string taskId)
        {
            return new
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
                    Task = "tts",
                    Function = "SpeechSynthesizer",
                    Model = ModelName,
                    Input = new { },
                    Parameters = new
                    {
                        TextType = "PlainText",
                        Voice,
                        Format = "pcm",
                        SampleRate,
                        Volume,
                        Rate,
                        Pitch,
                        LanguageHints = new[] { "zh" }
                    }
                }
            };
        }

        private static object CreateRealtimeContinueTaskRequest(string taskId, string text) => new
        {
            Header = new
            {
                Action = "continue-task",
                TaskId = taskId,
                Streaming = "duplex"
            },
            Payload = new
            {
                Input = new { Text = text }
            }
        };

        private static object CreateRealtimeFinishTaskRequest(string taskId) => new
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
        };

        private static object CreateHttpRequest(string text) => new
        {
            Model = ModelName,
            Input = new
            {
                Text = text,
                Voice,
                Format = "pcm",
                SampleRate,
                Volume,
                Rate,
                Pitch,
                LanguageHints = new[] { "zh" }
            }
        };

        private static async Task<byte[]> DownloadNonStreamingAudioAsync(
            HttpResponseMessage response,
            CancellationToken token)
        {
            string synthesisJson = await response.Content.ReadAsStringAsync(token);
            var synthesisResponse = DeserializeJson(synthesisJson, new
            {
                RequestId = string.Empty,
                Output = new
                {
                    FinishReason = string.Empty,
                    Audio = new
                    {
                        Data = string.Empty,
                        Url = string.Empty
                    }
                },
                Code = string.Empty,
                Message = string.Empty
            }) ?? throw new InvalidOperationException("Aliyun HTTP TTS returned an empty or invalid JSON response.");
            ThrowIfApiError(synthesisResponse.RequestId, synthesisResponse.Code, synthesisResponse.Message);

            string? audioUrl = synthesisResponse?.Output?.Audio?.Url;
            if (!Uri.TryCreate(audioUrl, UriKind.Absolute, out Uri? audioUri))
            {
                throw new InvalidOperationException("Aliyun HTTP TTS response does not contain a valid audio URL.");
            }

            using HttpResponseMessage audioResponse = await s_httpClient.GetAsync(audioUri, token);
            await EnsureSuccessAsync(audioResponse, "Aliyun HTTP TTS audio download");
            byte[] audioData = await audioResponse.Content.ReadAsByteArrayAsync(token);
            EnsureAudioReceived(audioData);
            return audioData;
        }

        private static async Task<byte[]> ReadSseAudioAsync(HttpResponseMessage response, CancellationToken token)
        {
            var audio = new List<byte>();
            var eventData = new StringBuilder();
            bool completed = false;

            await using Stream stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            while (true)
            {
                string? line = await reader.ReadLineAsync(token);
                if (line is null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    completed |= ProcessSseEvent(eventData, audio);
                    eventData.Clear();
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (eventData.Length > 0)
                    {
                        eventData.AppendLine();
                    }

                    eventData.Append(line.AsSpan(5).TrimStart());
                }
            }

            completed |= ProcessSseEvent(eventData, audio);
            if (!completed)
            {
                throw new InvalidOperationException("Aliyun HTTP TTS SSE stream ended before a stop result was received.");
            }

            byte[] audioData = audio.ToArray();
            EnsureAudioReceived(audioData);
            return audioData;
        }

        private static bool ProcessSseEvent(StringBuilder eventData, List<byte> audio)
        {
            if (eventData.Length == 0 || eventData.ToString().Equals("[DONE]", StringComparison.Ordinal))
            {
                return false;
            }

            var message = DeserializeJson(eventData.ToString(), new
            {
                RequestId = string.Empty,
                Output = new
                {
                    FinishReason = string.Empty,
                    Audio = new
                    {
                        Data = string.Empty,
                        Url = string.Empty
                    }
                },
                Code = string.Empty,
                Message = string.Empty
            }) ?? throw new InvalidOperationException("Aliyun HTTP TTS SSE event does not contain valid JSON.");
            ThrowIfApiError(message.RequestId, message.Code, message.Message);
            if (message.Output is null)
            {
                throw new InvalidOperationException("Aliyun HTTP TTS SSE event does not contain output data.");
            }

            string? encodedAudio = message.Output.Audio?.Data;
            if (!string.IsNullOrWhiteSpace(encodedAudio))
            {
                try
                {
                    audio.AddRange(Convert.FromBase64String(encodedAudio));
                }
                catch (FormatException ex)
                {
                    throw new InvalidOperationException("Aliyun HTTP TTS SSE event contains invalid Base64 audio data.", ex);
                }
            }

            return message.Output.FinishReason?.Equals("stop", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            string body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"{operation} failed: status={(int)response.StatusCode}, body={body}");
        }

        private static void ThrowIfApiError(string? requestId, string? code, string? message)
        {
            if (string.IsNullOrWhiteSpace(code)
                || code.Equals("0", StringComparison.OrdinalIgnoreCase)
                || code.Equals("200", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Aliyun HTTP TTS failed: requestId={requestId}, code={code}, message={message}");
        }

        private static async Task<byte[]> SavePcmAsync(string filePrefix, byte[] audioData, CancellationToken token)
        {
            EnsureAudioReceived(audioData);

            string saveDirectory = Path.Combine(Environment.CurrentDirectory, "data", "tts-cache");
            Directory.CreateDirectory(saveDirectory);
            string path = Path.Combine(saveDirectory, $"{filePrefix}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{Guid.NewGuid():N}.pcm");
            await File.WriteAllBytesAsync(path, audioData, token);
            Console.WriteLine($"{filePrefix}: received {audioData.Length} PCM bytes. Saved to {path}");
            return audioData;
        }

        private static void EnsureConfigured(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.StartsWith("your ", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Please set {name} in {nameof(Sample23_AliTTS)} before running this test.");
            }
        }

        private static void EnsureAudioReceived(byte[] audioData)
        {
            if (audioData.Length == 0)
            {
                throw new InvalidOperationException("Aliyun TTS completed without audio data.");
            }
        }

        private static string ResolveHttpEndpoint()
        {
            if (!string.IsNullOrWhiteSpace(AliyunHttpEndpoint))
            {
                if (Uri.TryCreate(AliyunHttpEndpoint, UriKind.Absolute, out Uri? endpoint)
                    && endpoint.Scheme == Uri.UriSchemeHttps)
                {
                    return endpoint.ToString();
                }

                throw new InvalidOperationException($"{nameof(AliyunHttpEndpoint)} must be an absolute HTTPS URL.");
            }

            if (string.IsNullOrWhiteSpace(AliyunWorkspaceId))
            {
                throw new InvalidOperationException(
                    $"Please set either {nameof(AliyunHttpEndpoint)} or {nameof(AliyunWorkspaceId)} in {nameof(Sample23_AliTTS)} before running an HTTP TTS test.");
            }

            return $"https://{AliyunWorkspaceId}.cn-beijing.maas.aliyuncs.com{HttpEndpointPath}";
        }

        private static T? DeserializeJson<T>(string json, T template) where T : class =>
            JsonHelper.Deserialize<T>(json);
    }
}
