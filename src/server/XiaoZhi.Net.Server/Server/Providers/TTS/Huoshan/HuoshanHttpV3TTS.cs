using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Flurl.Http;
using Flurl.Http.Configuration;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Contexts.Huoshan.Models;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan
{
    internal class HuoshanHttpV3TTS : BaseHuoshanTTS<HuoshanHttpV3TTS>, ITts
    {
        private const string SERVICE_END_POINT = "https://openspeech.bytedance.com/api/v3/tts/unidirectional";
        private const string FLURL_CLIENT_NAME = nameof(HuoshanHttpV3TTS);

        private readonly IFlurlClientCache _flurlClientCache;
        private readonly IDictionary<string, string> _headers;

        public HuoshanHttpV3TTS(IAudioEditor audioEditor, IFlurlClientCache flurlClientCache, ILogger<HuoshanHttpV3TTS> logger) : base(audioEditor, logger)
        {
            this._headers = new Dictionary<string, string>();
            this._flurlClientCache = flurlClientCache;
        }
        public override string ModelName => nameof(HuoshanHttpV3TTS);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                string? appId = modelSetting.Config.GetConfigValueOrDefault("AppId");
                string? accessToken = modelSetting.Config.GetConfigValueOrDefault("AccessToken");
                string? resourceId = modelSetting.Config.GetConfigValueOrDefault("ResourceId");
                string? speaker = modelSetting.Config.GetConfigValueOrDefault("Speaker");

                if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(resourceId) || string.IsNullOrWhiteSpace(speaker))
                {
                    this.Logger.LogWarning("火山 HTTP V3 TTS 配置不完整，请检查 AppId、AccessToken、ResourceId 和 speaker。");
                    return false;
                }

                this.SpeakerId = speaker;

                this.SpeechRate = modelSetting.Config.GetConfigValueOrDefault("SpeechRate", 0);
                this.LoudnessRate = modelSetting.Config.GetConfigValueOrDefault("LoudnessRate", 0);
                this.BuildAudioSavingConfig(modelSetting);
                this._headers.Add("X-Api-App-Key", appId);
                this._headers.Add("X-Api-Access-Key", accessToken);
                this._headers.Add("X-Api-Resource-Id", resourceId);
                this._headers.Add("X-Api-Request-Id", Guid.NewGuid().ToString());
                this._headers.Add("Content-Type", "application/json");

                this.Logger.LogInformation("已构建 {providerType} 模型：{modelName}", this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "构建 {modelName} 失败。", this.ModelName);
                return false;
            }
        }

        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(workflow.DeviceId, workflow.SessionId))
            {
                throw new InvalidOperationException("设备/会话未注册。");
            }

            OutSegment seg = workflow.Data;
            if (string.IsNullOrWhiteSpace(seg.SentenceId))
            {
                this.Logger.LogWarning("由于缺少句子 ID，处理片段失败。");
                return;
            }

            this._headers["X-Api-Request-Id"] = seg.SentenceId;
            var ttsReq = new
            {
                User = new { Uid = workflow.DeviceId },
                ReqParams = new
                {
                    Text = seg.Content,
                    Speaker = this.SpeakerId,
                    AudioParams = new
                    {
                        Format = this.AudioEncoding,
                        SampleRate = this.GetTtsSampleRate(),
                        EnableTimestamp = false,
                        this.SpeechRate,
                        this.LoudnessRate,
                        Emotion = this.ConvertEmotion(seg.Emotion)
                    },
                    Additions =
                        JsonHelper.Serialize(new
                        {
                            DisableMarkdownFilter = false,
                            CacheConfig = new
                            {
                                TextType = 1,
                                UseCache = true
                            },
                            SectionId = seg.ParagraphId
                        })
                }
            };

            this.TTSEventCallback?.OnBeforeProcessing(seg.Content, seg.IsFirstSegment, seg.IsLastSegment);

            List<byte> audioBuffer = new List<byte>();
            List<float> pcmBuffer = new List<float>();

            try
            {
                IFlurlClient flurlClient = this._flurlClientCache.Get(FLURL_CLIENT_NAME);

                using IFlurlResponse response = await flurlClient.Request(SERVICE_END_POINT)
                    .WithHeaders(this._headers)
                    .AllowAnyHttpStatus()
                    .PostJsonAsync(ttsReq, cancellationToken: token)
                    .ConfigureAwait(false);

                if (!response.ResponseMessage.IsSuccessStatusCode)
                {
                    string err = await response.GetStringAsync().ConfigureAwait(false);
                    this.Logger.LogError("火山 HTTP TTS 失败：状态={status} 正文={body}", response.StatusCode, err);
                    this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Failed);
                    return;
                }


                await using Stream stream = await response.GetStreamAsync().ConfigureAwait(false);
                using StreamReader reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);

                this.TTSEventCallback?.OnSentenceStart(seg.Content, seg.Emotion, seg.SentenceId);

                while (!reader.EndOfStream)
                {
                    token.ThrowIfCancellationRequested();
                    string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    TTSHttpResponseChunk? message = JsonHelper.Deserialize<TTSHttpResponseChunk>(line);

                    if (message is null || !message.Code.HasValue)
                    {
                        continue;
                    }

                    if (message.Code == 0)
                    {
                        if (message.Sentence is not null)
                        {
                            this.TTSEventCallback?.OnSentenceEnd(seg.Content, seg.Emotion, seg.SentenceId);
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(message.Data))
                        {
                            byte[] bytes = Convert.FromBase64String(message.Data);
                            audioBuffer.AddRange(bytes);

                            float[] pcm = bytes.PcmBytesToFloat(16);
                            if (pcm.Length > 0)
                            {
                                pcmBuffer.AddRange(pcm);
                                this.TTSEventCallback?.OnProcessing(pcm, false, false);
                            }
                            continue;
                        }
                    }

                    if (message.Code == 20000000)
                    {
                        if (pcmBuffer.Count > 0)
                        {
                            await this.SaveAudioFileAsync(workflow.DeviceId, seg.SentenceId, pcmBuffer.ToArray()).ConfigureAwait(false);
                        }

                        this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Success);
                        break;
                    }


                    if (message.Code.HasValue && message.Code > 0)
                    {
                        this.Logger.LogError("火山 HTTP TTS 错误：代码={code} 消息={message}", message.Code, message.Message);
                        this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Failed);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Aborted);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "火山 HTTP TTS 合成失败。");
                this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Failed);
                throw;
            }
        }

        public override void Dispose()
        {

        }
    }
}
