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
using XiaoZhi.Net.Server.I18n;
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

                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(cluster) || string.IsNullOrEmpty(speaker))
                {
                    this.Logger.LogWarning(Lang.HuoshanHttpTTS_Build_ConfigIncomplete);
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

                this.Logger.LogInformation(Lang.HuoshanHttpTTS_Build_Built, this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.HuoshanHttpTTS_Build_Failed, this.ModelName);
                return false;
            }
        }

        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            if (string.IsNullOrEmpty(this._appId) || string.IsNullOrEmpty(this._accessToken) || string.IsNullOrEmpty(this._cluster))
            {
                throw new InvalidOperationException(Lang.HuoshanHttpTTS_SynthesisAsync_ModelNotBuilt);
            }

            if (!this.CheckDeviceRegistered(workflow.DeviceId, workflow.SessionId))
            {
                throw new InvalidOperationException(Lang.HuoshanHttpTTS_SynthesisAsync_DevNotReg);
            }

            OutSegment seg = workflow.Data;
            if (string.IsNullOrEmpty(seg.SentenceId))
            {
                this.Logger.LogWarning(Lang.HuoshanHttpTTS_SynthesisAsync_MissingSentenceId);
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
                    this.Logger.LogError(Lang.HuoshanHttpTTS_SynthesisAsync_RequestFailed, response.StatusCode, err);
                    this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Failed);
                    return;
                }

                TTSHttpResponse ttsHttpResponse = await response.GetJsonAsync<TTSHttpResponse>().ConfigureAwait(false);

                if (ttsHttpResponse.Code == 3000 && !string.IsNullOrEmpty(ttsHttpResponse.Data))
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
                    this.Logger.LogError(Lang.HuoshanHttpTTS_SynthesisAsync_ApiError, ttsHttpResponse.Code, ttsHttpResponse.Message);
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
                this.Logger.LogError(ex, Lang.HuoshanHttpTTS_SynthesisAsync_GeneralFailed);
                this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Failed);
                throw;
            }
        }

        public override void Dispose()
        {

        }
    }
}
