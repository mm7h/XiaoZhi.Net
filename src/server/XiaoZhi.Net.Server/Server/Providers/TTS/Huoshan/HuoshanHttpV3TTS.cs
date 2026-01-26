using Flurl.Http;
using Flurl.Http.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Models;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan
{
    internal class HuoshanHttpV3TTS : BaseHuoshanTTS<HuoshanHttpV3TTS>, ITts
    {
        private const string SERVICE_END_POINT = "https://openspeech.bytedance.com/api/v3/tts/unidirectional";
        private const string FLURL_CLIENT_NAME = nameof(HuoshanHttpV3TTS);

        private readonly IFlurlClientCache _flurlClientCache;
        private readonly IDictionary<string, string> _headers;

        public HuoshanHttpV3TTS(IFlurlClientCache flurlClientCache, ILogger<HuoshanHttpV3TTS> logger) : base(logger)
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

                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(resourceId) || string.IsNullOrEmpty(speaker))
                {
                    this.Logger.LogWarning("Huoshan http v3 TTS configuration is incomplete, please check AppId, AccessToken, ResourceId and speaker.");
                    return false;
                }

                this.SpeakerId = speaker;

                this.SpeechRate = modelSetting.Config.GetConfigValueOrDefault("SpeechRate", 0);
                this.LoudnessRate = modelSetting.Config.GetConfigValueOrDefault("LoudnessRate", 0);
                this.Save2File = modelSetting.Config.GetConfigValueOrDefault("Save2File", false);

                if (this.Save2File)
                {
                    this.SavePath = modelSetting.Config.GetConfigValueOrDefault("SavePath", Path.Combine(Environment.CurrentDirectory, "data", "tts-cache"));
                    if (!Directory.Exists(this.SavePath))
                        Directory.CreateDirectory(this.SavePath);
                }
                this._headers.Add("X-Api-App-Key", appId);
                this._headers.Add("X-Api-Access-Key", accessToken);
                this._headers.Add("X-Api-Resource-Id", resourceId);
                this._headers.Add("X-Api-Request-Id", Guid.NewGuid().ToString());
                this._headers.Add("Content-Type", "application/json");

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

            FileStream? audioFs = null;
            string? audioPath = null;

            if (this.Save2File && !string.IsNullOrEmpty(this.SavePath))
            {
                audioPath = Path.Combine(this.SavePath, $"{seg.SentenceId}.{this.AudioEncoding}");
                audioFs = new FileStream(audioPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
            }

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
                    this.Logger.LogError("Huoshan HTTP TTS failed: status={status} body={body}", response.StatusCode, err);
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
                    if (string.IsNullOrEmpty(line))
                        continue;

                    TTSHttpResponseChunk? message = JsonHelper.Deserialize<TTSHttpResponseChunk>(line);

                    if (message is null || !message.Code.HasValue)
                        continue;

                    if (message.Code == 0)
                    {
                        if (message.Sentence is not null)
                        {
                            this.TTSEventCallback?.OnSentenceEnd(seg.Content, seg.Emotion, seg.SentenceId);
                            continue;
                        }

                        if (!string.IsNullOrEmpty(message.Data))
                        {
                            byte[] bytes = Convert.FromBase64String(message.Data);

                            if (audioFs is not null)
                            {
                                await audioFs.WriteAsync(bytes, token).ConfigureAwait(false);
                            }

                            float[] pcm = bytes.PcmBytesToFloat(16);
                            if (pcm.Length > 0)
                            {
                                this.TTSEventCallback?.OnProcessing(pcm, false, false);
                            }
                            continue;
                        }
                    }

                    if (message.Code == 20000000)
                    {
                        this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Success);
                        break;
                    }

                    if (message.Code.HasValue && message.Code > 0)
                    {
                        this.Logger.LogError("Huoshan HTTP TTS error: code={code} message={message}", message.Code, message.Message);
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
                this.Logger.LogError(ex, "Huoshan HTTP TTS synthesis failed.");
                this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Failed);
                throw;
            }
            finally
            {
                if (audioFs is not null)
                {
                    try
                    {
                        await audioFs.FlushAsync(token).ConfigureAwait(false);
                        this.Logger.LogInformation("Saved TTS audio file to {audioPath} for the device {deviceId}.", audioFs.Name ?? audioPath, this.DeviceId);
                    }
                    catch (Exception ex)
                    {
                        this.Logger.LogWarning(ex, "Failed to flush audio file stream for the device {deviceId}.", this.DeviceId);
                    }
                    audioFs.Dispose();
                }
            }
        }

        public override void Dispose()
        {

        }
    }
}
