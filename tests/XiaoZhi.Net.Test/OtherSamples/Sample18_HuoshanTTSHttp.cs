using System.Text;
using Flurl.Http;
using Flurl.Http.Configuration;
using XiaoZhi.Net.Test.OtherSamples.Huoshan.Protocols.Models;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample18_HuoshanTTSHttp
    {
        public static async Task RunAsync()
        {
            HuoshanHttpTTS tts = new();
            string apiId = Environment.GetEnvironmentVariable("HuoshanAppId", EnvironmentVariableTarget.User)!;
            string accessToken = Environment.GetEnvironmentVariable("HuoshanAccessToken", EnvironmentVariableTarget.User)!;

            if (!tts.Build(apiId, accessToken))
            {
                Console.WriteLine("Build failed");
                return;
            }
            var output = await tts.SynthesisAsync("春眠不觉晓，处处闻啼鸟。夜来风雨声，花落知多少。", CancellationToken.None);
            Console.WriteLine($"Received audio bytes: {output.Length}");
        }
    }

    file class HuoshanHttpTTS
    {
        private const string ServiceEndPoint = "https://openspeech.bytedance.com/api/v1/tts";
        private const int SampleRate = 24000;

        private bool _saveFile = false;
        private string? _savePath;
        private string? _speaker;
        private readonly string _audioFormat = "wav";

        private string? _appId;
        private string? _accessToken;
        private string? _cluster;

        public bool Build(string appId, string accessToken)
        {
            try
            {
                this._speaker = "zh_female_wanwanxiaohe_moon_bigtts";
                this._appId = appId;
                this._accessToken = accessToken;
                this._cluster = "volcano_tts";
                this._saveFile = true;

                if (this._saveFile)
                {
                    this._savePath = Path.Combine(Environment.CurrentDirectory, "data", "tts-cache");
                    if (!Directory.Exists(this._savePath))
                    {
                        Directory.CreateDirectory(this._savePath);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public async Task<byte[]> SynthesisAsync(string text, CancellationToken token)
        {
            var ttsReq = new
            {
                App = new
                {
                    Appid = this._appId,
                    Token = this._accessToken,
                    Cluster = this._cluster
                },
                User = new { Uid = Guid.NewGuid().ToString() },
                Audio = new
                {
                    VoiceType = this._speaker ?? "zh_female_wanwanxiaohe_moon_bigtts",
                    Encoding = this._audioFormat,
                    Rate = SampleRate
                },
                Request = new
                {
                    Reqid = "my-request-id",
                    Text = text,
                    TextType = "plain",
                    Operation = "query",
                    WithFrontend = 1,
                    FrontendType = "unitTson"
                },
                ExtraParam =
                    JsonHelper.Serialize(new
                    {
                        DisableEmojiFilter = false,
                        DisableMarkdownFilter = false,
                        CacheConfig = new
                        {
                            TextType = 1,
                            UseCache = true
                        }
                    })
            };
            IFlurlClient client = new FlurlClient(ServiceEndPoint)
            {
                Settings = { JsonSerializer = new DefaultJsonSerializer(JsonHelper.OPTIONS) }
            };

            using IFlurlResponse response = await client.Request()
                //.WithOAuthBearerToken(this._accessToken)// wtf?
                .WithHeader("Authorization", $"Bearer;{this._accessToken}")
                .AllowAnyHttpStatus()
                .PostJsonAsync(ttsReq, cancellationToken: token);

            string audioFilePath = Path.Combine(this._savePath ?? string.Empty, $"{Guid.NewGuid()}.{this._audioFormat}");

            using FileStream audioFs = new FileStream(
                audioFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite,
                8192,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            TTSHttpResponse ttsHttpResponse = await response.GetJsonAsync<TTSHttpResponse>();

            if (ttsHttpResponse.Code == 3000 && !string.IsNullOrEmpty(ttsHttpResponse.Data))
            {
                byte[] bytes = Convert.FromBase64String(ttsHttpResponse.Data);
                if (audioFs is not null)
                {
                    await audioFs.WriteAsync(bytes, token);
                }
            }
            else
            {
                Console.WriteLine("Huoshan HTTP TTS error: code={0} message={1}", ttsHttpResponse.Code, ttsHttpResponse.Message);
            }

            if (audioFs is not null)
            {
                try
                {
                    await audioFs.FlushAsync(token);
                }
                catch
                {
                    Console.WriteLine("Failed to flush audio file stream.");
                }
                audioFs.Dispose();
            }

            return await File.ReadAllBytesAsync(audioFilePath, token);
        }
    }

    file class HuoshanHttpV3TTS
    {
        private const string ServiceEndPoint = "https://openspeech.bytedance.com/api/v3/tts/unidirectional";
        private const int SampleRate = 24000;

        private bool _saveFile = false;
        private string? _savePath;
        private string? _speaker;
        private readonly string _audioFormat = "wav";
        private readonly IDictionary<string, string> _headers;
        public HuoshanHttpV3TTS()
        {
            this._headers = new Dictionary<string, string>();
        }

        public bool Build(string appId, string accessToken, string resourceId)
        {
            try
            {
                this._speaker = "zh_female_wanwanxiaohe_moon_bigtts";

                this._saveFile = true;

                if (this._saveFile)
                {
                    this._savePath = Path.Combine(Environment.CurrentDirectory, "data", "tts-cache");
                    if (!Directory.Exists(this._savePath))
                    {
                        Directory.CreateDirectory(this._savePath);
                    }
                }

                this._headers.Add("X-Api-App-Key", appId);
                this._headers.Add("X-Api-Access-Key", accessToken);
                this._headers.Add("X-Api-Resource-Id", resourceId);
                this._headers.Add("X-Api-Request-Id", Guid.NewGuid().ToString());

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public async Task<byte[]> SynthesisAsync(string text, CancellationToken token)
        {
            var ttsReq = new
            {
                User = new { Uid = Guid.NewGuid().ToString() },
                ReqParams = new
                {
                    Text = text,
                    Speaker = this._speaker ?? "zh_female_wanwanxiaohe_moon_bigtts",
                    AudioParams = new
                    {
                        Format = this._audioFormat,
                        SampleRate,
                        EnableTimestamp = false
                    },
                    Additions =
                        JsonHelper.Serialize(new
                        {
                            DisableMarkdownFilter = false,
                            CacheConfig = new
                            {
                                TextType = 1,
                                UseCache = true
                            }
                        })
                }
            };
            IFlurlClient client = new FlurlClient(ServiceEndPoint)
            {
                Settings = { JsonSerializer = new DefaultJsonSerializer(JsonHelper.OPTIONS) }
            };

            using IFlurlResponse response = await client.Request()
                .WithHeaders(this._headers)
                .AllowAnyHttpStatus()
                .PostJsonAsync(ttsReq, cancellationToken: token);
            if (!response.ResponseMessage.IsSuccessStatusCode)
            {
                Console.WriteLine("HTTP request failed with status code: " + response.StatusCode);
                return Array.Empty<byte>();
            }
            await using Stream stream = await response.GetStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string audioFilePath = Path.Combine(this._savePath ?? string.Empty, $"{Guid.NewGuid()}.{this._audioFormat}");

            using FileStream audioFs = new FileStream(
                audioFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite,
                8192,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            try
            {
                while (!reader.EndOfStream)
                {
                    string? line = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }

                    TTSHttpResponseChunk? message = JsonHelper.Deserialize<TTSHttpResponseChunk>(line);

                    if (message is null || !message.Code.HasValue)
                    {
                        continue;
                    }

                    Console.WriteLine($"data: {message.Data?.Length ?? 0}, sentence: {message.Sentence}, message: {message.Message}, code: {message.Code}");

                    if (message.Code == 0)
                    {
                        if (message.Sentence is not null)
                        {
                            string sentence = message.Sentence["text"]?.GetValue<string>() ?? string.Empty;
                            continue;
                        }

                        if (!string.IsNullOrEmpty(message.Data))
                        {
                            byte[] bytes = Convert.FromBase64String(message.Data);

                            if (audioFs is not null)
                            {
                                await audioFs.WriteAsync(bytes);
                            }

                            continue;
                        }
                    }
                    if (message.Code == 20000000)
                    {
                        break;
                    }
                    if (message.Code.HasValue && message.Code > 0)
                    {
                        Console.WriteLine("Huoshan HTTP TTS error: code={0} message={1}", message.Code, message.Message);
                        return Array.Empty<byte>();
                    }
                }

                if (audioFs != null)
                {
                    await audioFs.FlushAsync(token);
                    audioFs.Dispose();
                    if (audioFilePath != null && File.Exists(audioFilePath))
                    {
                        return await File.ReadAllBytesAsync(audioFilePath, token);
                    }
                }
                return Array.Empty<byte>();
            }
            finally
            {
                if (audioFs != null)
                {
                    await audioFs.DisposeAsync();
                }
            }
        }
    }
}
