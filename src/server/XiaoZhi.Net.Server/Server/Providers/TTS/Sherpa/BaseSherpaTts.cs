using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Providers.TTS.Sherpa
{
    internal abstract class BaseSherpaTts<TLogger> : BaseProvider<TLogger, ModelSetting>, ITts
    {
        private readonly ConcurrentDictionary<string, ITtsEventCallback> _ttsSessions;
        private OfflineTts? _offlineTts;



        protected BaseSherpaTts(ILogger<TLogger> logger) : base(logger)
        {
            this._ttsSessions = new ConcurrentDictionary<string, ITtsEventCallback>();
        }
        public override string ProviderType => "tts";

        public bool Save2File { get; private set; }
        public string SavePath { get; private set; } = string.Empty;
        //https://k2-fsa.github.io/sherpa/onnx/tts/pretrained_models/kokoro.html#map-between-speaker-id-and-speaker-name
        public int SpeakerId { get; private set; } = 50;
        public float SpeechRate { get; private set; } = 1.0f;

        public virtual int GetTtsSampleRate()
        {
            return this._offlineTts?.SampleRate ?? 24000;
        }

        protected void Build(OfflineTtsConfig offlineTtsConfig, ModelSetting modelSetting)
        {
            offlineTtsConfig.Model.NumThreads = 2;
            offlineTtsConfig.Model.Provider = "cpu";

            this.Save2File = modelSetting.Config.GetConfigValueOrDefault("Save2File", false);
            this.SpeechRate = modelSetting.Config.GetConfigValueOrDefault("SpeechRate", 1.0f);
            this.SpeakerId = modelSetting.Config.GetConfigValueOrDefault("SpeakerId", 50);

            if (this.Save2File)
            {
                this.SavePath = modelSetting.Config.GetConfigValueOrDefault("SavePath", Path.Combine(Environment.CurrentDirectory, "data", "tts-cache"));
                if (!Directory.Exists(this.SavePath))
                    Directory.CreateDirectory(this.SavePath);
            }
            this._offlineTts = new OfflineTts(offlineTtsConfig);
        }

        public void RegisterDevice(string deviceId, string sessionId, ITtsEventCallback callback)
        {
            this._ttsSessions.TryAdd(deviceId, callback);
        }

        public override void UnregisterDevice(string deviceId, string sessionId)
        {
            if (this._ttsSessions.TryRemove(deviceId, out _))
            {
                this.Logger.LogDebug(Lang.BaseSherpaTts_UnregisterDevice_Unregistered, deviceId, sessionId);
            }
        }

        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            if (this._offlineTts == null)
            {
                throw new ArgumentNullException(Lang.BaseSherpaTts_SynthesisAsync_ProviderNotBuilt);
            }

            try
            {
                OutSegment segment = workflow.Data;

                if (string.IsNullOrEmpty(segment.ParagraphId) || string.IsNullOrEmpty(segment.SentenceId))
                {
                    this.Logger.LogWarning(Lang.BaseSherpaTts_SynthesisAsync_MissingIds);
                    return;
                }

                if (this._ttsSessions.TryGetValue(workflow.DeviceId, out ITtsEventCallback? sessionCallback) && sessionCallback is not null)
                {
                    Stopwatch timer = Stopwatch.StartNew();

                    bool firstFrameSent = false;

                    sessionCallback.OnBeforeProcessing(segment.Content, segment.IsFirstSegment, segment.IsLastSegment);

                    OfflineTtsGeneratedAudio audio = this._offlineTts.GenerateWithCallbackProgress(segment.Content, this.SpeechRate, this.SpeakerId, (nint samples, int n, float progress) =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            return 0;
                        }
                        float[] data = new float[n];
                        Marshal.Copy(samples, data, 0, n);

                        if (!firstFrameSent)
                        {
                            sessionCallback.OnSentenceStart(segment.Content, segment.Emotion, segment.SentenceId);
                            firstFrameSent = true;
                        }

                        if (progress == 1.0f)
                        {
                            sessionCallback.OnSentenceEnd(segment.Content, segment.Emotion, segment.SentenceId);
                        }

                        sessionCallback.OnProcessing(data, false, false);
                        return 1;
                    });

                    if (token.IsCancellationRequested)
                    {
                        sessionCallback.OnProcessed(segment.Content, segment.IsFirstSegment, segment.IsLastSegment, TtsGenerateResult.Aborted);
                    }
                    else
                    {
                        sessionCallback.OnProcessed(segment.Content, segment.IsFirstSegment, segment.IsLastSegment, TtsGenerateResult.Success);
                    }

                    double duration = Math.Max((this.CalculateDuration(audio.SampleRate, audio.NumSamples) * 1000 - (workflow.Data.IsFirstSegment ? 300 + timer.ElapsedMilliseconds : 0)), 0);


                    if (this.Save2File)
                    {
                        string fileName = $"{segment.SentenceId}.wav";
                        string filePath = Path.Combine(this.SavePath, fileName);
                        if (File.Exists(filePath))
                            File.Delete(filePath);
                        bool saved = audio.SaveToWaveFile(filePath);
                        if (saved)
                        {
                            this.Logger.LogDebug(Lang.BaseSherpaTts_SynthesisAsync_FileSaved, fileName, this.FormatDuration(duration));
                        }
                        else
                        {
                            this.Logger.LogDebug(Lang.BaseSherpaTts_SynthesisAsync_SaveFailed, fileName);
                        }
                    }
                    else
                    {
                        this.Logger.LogDebug(Lang.BaseSherpaTts_SynthesisAsync_Generated, this.FormatDuration(duration));
                    }
                    audio.Dispose();
                    timer.Stop();
                }
                else
                {
                    this.Logger.LogError(Lang.BaseSherpaTts_SynthesisAsync_CallbackNotRegistered, workflow.DeviceId);
                }
                await Task.CompletedTask;

            }
            catch (OperationCanceledException)
            {
                this.Logger.LogWarning(Lang.BaseSherpaTts_SynthesisAsync_UserCanceled, this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.BaseSherpaTts_SynthesisAsync_UnexpectedError, this.ProviderType);
            }
        }

        private double CalculateDuration(int sampleRate, int numSamples)
        {
            return (double)numSamples / sampleRate;
        }
        private string FormatDuration(double durationInMillisecond)
        {
            int durationInSeconds = (int)durationInMillisecond / 1000;
            int minutes = durationInSeconds / 60;
            int seconds = durationInSeconds % 60;
            return $"{minutes}m {seconds}s";
        }
        public override void Dispose()
        {
            this._offlineTts?.Dispose();
        }
    }
}
