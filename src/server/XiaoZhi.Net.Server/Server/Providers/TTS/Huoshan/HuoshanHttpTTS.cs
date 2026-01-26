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

        public HuoshanHttpTTS(IFlurlClientCache flurlClientCache, ILogger<HuoshanHttpTTS> logger) : base(logger)
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
                    this.Logger.LogWarning("Huoshan bidirection TTS configuration is incomplete, please check AppId, AccessToken, Cluster and speaker.");
                    return false;
                }

                this._appId = appId;
                this._accessToken = accessToken;
                this._cluster = cluster;
                this.SpeakerId = speaker;

                this._speedRatio = modelSetting.Config.GetConfigValueOrDefault("SpeedRatio", 1.0f);
                this._volumeRatio = modelSetting.Config.GetConfigValueOrDefault("VolumeRatio", 1.0f);
                this._pitchRatio = modelSetting.Config.GetConfigValueOrDefault("PitchRatio", 1.0f);

                this.Save2File = modelSetting.Config.GetConfigValueOrDefault("Save2File", false);

                if (this.Save2File)
                {
                    this.SavePath = modelSetting.Config.GetConfigValueOrDefault("SavePath", Path.Combine(Environment.CurrentDirectory, "data", "tts-cache"));
                    if (!Directory.Exists(this.SavePath))
                        Directory.CreateDirectory(this.SavePath);
                }

                this.Logger.LogInformation("Builded the {providerType} model: {modelName}", this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to build {modelName}.", this.ModelName);
                return false;
            }
        }

        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            if (string.IsNullOrEmpty(this._appId) || string.IsNullOrEmpty(this._accessToken) || string.IsNullOrEmpty(this._cluster))
            {
                throw new InvalidOperationException("TTS model not built.");
            }

            if (!this.CheckDeviceRegistered())
            {
                throw new InvalidOperationException("Device/session not registered.");
            }

            OutSegment seg = workflow.Data;
            if (string.IsNullOrEmpty(seg.SentenceId))
            {
                this.Logger.LogWarning("Failed to process segment due to missing sentence id.");
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
                    this.Logger.LogError("Huoshan HTTP TTS failed: status={status} body={body}", response.StatusCode, err);
                    this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Failed);
                    return;
                }

                TTSHttpResponse ttsHttpResponse = await response.GetJsonAsync<TTSHttpResponse>().ConfigureAwait(false);

                if (ttsHttpResponse.Code == 3000 && !string.IsNullOrEmpty(ttsHttpResponse.Data))
                {
                    this.TTSEventCallback?.OnSentenceStart(seg.Content, seg.Emotion, seg.SentenceId);
                    byte[] bytes = Convert.FromBase64String(ttsHttpResponse.Data);

                    if (this.Save2File && !string.IsNullOrEmpty(this.SavePath))
                    {
                        try
                        {
                            string audioPath = Path.Combine(this.SavePath, $"{seg.SentenceId}.{this.AudioEncoding}");
                            await File.WriteAllBytesAsync(audioPath, bytes, token).ConfigureAwait(false);
                            this.Logger.LogInformation("Saved TTS audio file to {audioPath} for the device {deviceId}.", audioPath, this.DeviceId);
                        }
                        catch (Exception ex)
                        {
                            this.Logger.LogWarning(ex, "Failed to save audio file for the device {deviceId}.", this.DeviceId);
                        }
                    }

                    float[] pcm = bytes.PcmBytesToFloat(16);
                    if (pcm.Length > 0)
                    {
                        this.TTSEventCallback?.OnProcessing(pcm, false, false);
                    }
                    this.TTSEventCallback?.OnSentenceEnd(seg.Content, seg.Emotion, seg.SentenceId);
                    this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Success);
                }
                else
                {
                    this.Logger.LogError("Huoshan HTTP TTS error: code={code} message={message}", ttsHttpResponse.Code, ttsHttpResponse.Message);
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
                this.Logger.LogError(ex, "Huoshan HTTP TTS synthesis failed.");
                this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Failed);
                throw;
            }
        }

        public override void Dispose()
        {

        }
    }
}
