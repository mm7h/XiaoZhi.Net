using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Test.OtherSamples.Huoshan.Protocols.Enums;
using XiaoZhi.Net.Test.OtherSamples.Huoshan.Protocols.Models;
using XiaoZhi.Net.Test.Socket;
using XiaoZhi.Test.OtherSamples.Huoshan;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample17_HuoshanUnidirectional
    {
        public static async Task RunAsync()
        {
            HuoshanUnidirectionalTTS tts = new();

            string apiId = Environment.GetEnvironmentVariable("HuoshanAppId", EnvironmentVariableTarget.User)!;
            string accessToken = Environment.GetEnvironmentVariable("HuoshanAccessToken", EnvironmentVariableTarget.User)!;

            if (!tts.Build(apiId, accessToken, "volc.service_type.10029"))
            {
                Console.WriteLine("Build failed");
                return;
            }

            var output = await tts.SynthesisAsync("春眠不觉晓，处处闻啼鸟。夜来风雨声，花落知多少。", CancellationToken.None);
            Console.WriteLine($"Received audio bytes: {output.Length}");
        }
    }

    file class HuoshanUnidirectionalTTS
    {
        private const string ServiceEndPoint = "wss://openspeech.bytedance.com/api/v3/tts/unidirectional/stream";
        private const int SampleRate = 24000;
        private static readonly TimeSpan s_defaultWaitTimeout = TimeSpan.FromSeconds(15);

        private TaskCompletionSource _sessionFinishedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<byte> _audio = [];
        private readonly object _audioLock = new();

        private bool _saveFile = false;
        private string? _savePath;
        private string? _speaker;
        private readonly string _audioFormat = "wav";
        private readonly bool _enableTimestamp = true;

        private string? _connectId;

        protected WebSocketClient? WebSocketClient { get; set; }
        public int GetTtsSampleRate()
        {
            return SampleRate;
        }

        public bool Build(string appId, string accessToken, string resourceId)
        {
            try
            {
                this._speaker = "zh_female_cancan_mars_bigtts";
                this._connectId = Guid.NewGuid().ToString();

                this._saveFile = true;

                if (this._saveFile)
                {
                    this._savePath = Path.Combine(Environment.CurrentDirectory, "data", "tts-cache");
                    if (!Directory.Exists(this._savePath))
                    {
                        Directory.CreateDirectory(this._savePath);
                    }
                }

                IDictionary<string, string> headers = new Dictionary<string, string>
                {
                    { "X-Api-App-Key", appId },
                    { "X-Api-Access-Key", accessToken },
                    { "X-Api-Resource-Id", resourceId },
                    { "X-Api-Connect-Id", this._connectId }
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

        public async Task<byte[]> SynthesisAsync(string text, CancellationToken token)
        {
            if (this.WebSocketClient is null)
            {
                return Array.Empty<byte>();
            }

            if (!this.WebSocketClient.IsConnected)
            {
                await this.ConnectAsync(ServiceEndPoint, token);
            }

            // reset per request state
            lock (this._audioLock)
            {
                this._audio.Clear();
            }

            this._sessionFinishedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // Send request payload (official demo style: FullClientRequest JSON)
            var request = new Dictionary<string, object>
            {
                ["user"] = new Dictionary<string, object>
                {
                    ["uid"] = Guid.NewGuid().ToString()
                },
                ["req_params"] = new Dictionary<string, object>
                {
                    ["text"] = text,
                    ["speaker"] = this._speaker ?? "zh_female_cancan_mars_bigtts",
                    ["audio_params"] = new Dictionary<string, object>
                    {
                        ["format"] = this._audioFormat,
                        ["sample_rate"] = SampleRate,
                        ["enable_timestamp"] = this._enableTimestamp,
                    },
                    ["additions"] = JsonHelper.Serialize(new Dictionary<string, object>
                    {
                        ["disable_markdown_filter"] = false,
                    })
                }
            };

            var sendBytes = JsonHelper.SerializeToUtf8Bytes(request);
            var message = Message.Create(MsgType.FullClientRequest, MsgTypeFlagBits.NoSeq);
            message.Payload = sendBytes;
            await this.SendMessageAsync(message);

            using var finishedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            finishedCts.CancelAfter(s_defaultWaitTimeout);

            try
            {
                await this._sessionFinishedTcs.Task.WaitAsync(finishedCts.Token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            byte[] audioBytes;
            lock (this._audioLock)
            {
                audioBytes = this._audio.ToArray();
            }

            if (this._saveFile && !string.IsNullOrEmpty(this._savePath))
            {
                var fileBase = $"unidirectional_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}";
                var path = Path.Combine(this._savePath, $"{fileBase}.{this._audioFormat}");
                try
                {
                    await File.WriteAllBytesAsync(path, audioBytes, CancellationToken.None);
                    Console.WriteLine($"Audio saved to {path}");
                }
                catch
                {
                }
            }

            return audioBytes;
        }

        private async Task ConnectAsync(string endPoint, CancellationToken token)
        {
            if (this.WebSocketClient is null)
            {
                throw new InvalidOperationException("WebSocket client is not initialized.");
            }
            await this.WebSocketClient.ConnectAsync(endPoint, token);
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
            this.ResetSessionFinishedWithException(new OperationCanceledException($"WebSocket closed: {status} {desc}"));
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
            catch
            {
                return;
            }
            Console.WriteLine(message);
            switch (message.MsgType)
            {
                case MsgType.AudioOnlyServer:
                    if (message.Payload != null && message.Payload.Length > 0)
                    {
                        lock (this._audioLock)
                        {
                            this._audio.AddRange(message.Payload);
                        }
                    }
                    break;
                case MsgType.FullServerResponse:
                    if (message.EventType == EventType.SessionFinished)
                    {
                        this._sessionFinishedTcs.TrySetResult();
                    }
                    else if (message.EventType == EventType.SessionFailed || message.EventType == EventType.ConnectionFailed)
                    {
                        this._sessionFinishedTcs.TrySetException(new Exception($"Server reported failure: {message}"));
                    }
                    break;
                case MsgType.Error:
                    this._sessionFinishedTcs.TrySetException(new Exception($"Server error: {message}"));
                    break;
            }
        }

        private void WebSocketClient_OnError(System.Net.WebSockets.WebSocketError error, string message)
        {
            this.ResetSessionFinishedWithException(new Exception($"WebSocket error: {error} {message}"));
        }

        private async Task SendMessageAsync(Message message)
        {
            if (this.WebSocketClient is null)
            {
                return;
            }
            var data = message.Marshal();
            await this.WebSocketClient.SendAsync(data);
        }

        private void ResetSessionFinishedWithException(Exception ex)
        {
            this._sessionFinishedTcs.TrySetException(ex);
        }
    }
}
