using Flurl.Http;
using Flurl.Http.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Models;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan
{
    internal class HuoshanHttpTTS : BaseHuoshanTTS<HuoshanHttpTTS>, ITts
    {
        private const string SERVICE_END_POINT = "https://openspeech.bytedance.com/api/v1/tts";
        private const string FLURL_CLIENT_NAME = nameof(HuoshanHttpTTS);

        private readonly IFlurlClientCache _flurlClientCache;
        private string? _appId;
        private string? _accessToken;
        private string? _cluster;

        private float? _speedRatio;
        private float? _volumeRatio;
        private float? _pitchRatio;

        public HuoshanHttpTTS(IAudioEditor audioEditor, IFlurlClientCache flurlClientCache, ILogger<HuoshanHttpTTS> logger) : base(audioEditor, logger)
        {
            this._flurlClientCache = flurlClientCache;
        }
        public override string ModelName => nameof(HuoshanHttpTTS);

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                string? appId = modelSetting.Config.GetConfigValueOrDefault("AppId");
                string? accessToken = modelSetting.Config.GetConfigValueOrDefault("AccessToken");
                string? cluster = modelSetting.Config.GetConfigValueOrDefault("Cluster");
                string? speaker = modelSetting.Config.GetConfigValueOrDefault("Speaker");

                if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(cluster) || string.IsNullOrWhiteSpace(speaker))
                {
                    this.Logger.LogWarning("火山双向 TTS 配置不完整，请检查 AppId、AccessToken、Cluster 和 speaker。");
                    return false;
                }

                this._appId = appId;
                this._accessToken = accessToken;
                this._cluster = cluster;
                this.SpeakerId = speaker;

                this._speedRatio = modelSetting.Config.GetConfigValueOrDefault("SpeedRatio", 1.0f);
                this._volumeRatio = modelSetting.Config.GetConfigValueOrDefault("VolumeRatio", 1.0f);
                this._pitchRatio = modelSetting.Config.GetConfigValueOrDefault("PitchRatio", 1.0f);

                this.BuildAudioSavingConfig(modelSetting);

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
            if (string.IsNullOrWhiteSpace(this._appId) || string.IsNullOrWhiteSpace(this._accessToken) || string.IsNullOrWhiteSpace(this._cluster))
            {
                throw new InvalidOperationException("TTS 模型未构建。");
            }

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

            var ttsReq = new
            {
                App = new
                {
                    Appid = this._appId,
                    Token = this._accessToken,
                    Cluster = this._cluster
                },
                User = new { Uid = workflow.DeviceId },
                Audio = new
                {
                    VoiceType = this.SpeakerId,
                    Encoding = this.AudioEncoding,
                    SpeedRatio = this._speedRatio,
                    VolumeRatio = this._volumeRatio,
                    PitchRatio = this._pitchRatio,
                    Rate = this.GetTtsSampleRate(),
                    EnableEmotion = true,
                    Emotion = this.ConvertEmotion(seg.Emotion)
                },
                Request = new
                {
                    Reqid = seg.SentenceId,
                    Text = seg.Content,
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

            this.TTSEventCallback?.OnBeforeProcessing(seg.Content, seg.IsFirstSegment, seg.IsLastSegment);

            try
            {
                IFlurlClient flurlClient = this._flurlClientCache.Get(FLURL_CLIENT_NAME);

                token.ThrowIfCancellationRequested();
                using IFlurlResponse response = await flurlClient.Request(SERVICE_END_POINT)
                    .WithHeader("Authorization", $"Bearer;{this._accessToken}")
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

                TTSHttpResponse ttsHttpResponse = await response.GetJsonAsync<TTSHttpResponse>().ConfigureAwait(false);

                if (ttsHttpResponse.Code == 3000 && !string.IsNullOrWhiteSpace(ttsHttpResponse.Data))
                {
                    this.TTSEventCallback?.OnSentenceStart(seg.Content, seg.Emotion, seg.SentenceId);
                    byte[] audioData = Convert.FromBase64String(ttsHttpResponse.Data);

                    float[] pcmData = audioData.PcmBytesToFloat(16);
                    if (pcmData.Length > 0)
                    {
                        await this.SaveAudioFileAsync(this.DeviceId, seg.SentenceId, pcmData).ConfigureAwait(false);
                        this.TTSEventCallback?.OnProcessing(pcmData, false, false);
                    }
                    this.TTSEventCallback?.OnSentenceEnd(seg.Content, seg.Emotion, seg.SentenceId);
                    this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Success);
                }
                else
                {
                    this.Logger.LogError("火山 HTTP TTS 错误：代码={code} 消息={message}", ttsHttpResponse.Code, ttsHttpResponse.Message);
                    this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Failed);
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
