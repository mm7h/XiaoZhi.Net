using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.TTS.Sherpa
{
    internal abstract class BaseSherpaTts<TLogger> : BaseProvider<TLogger, ModelSetting>, ITts
    {
        private readonly IAudioEditor _audioEditor;
        private readonly ConcurrentDictionary<string, ITtsEventCallback> _ttsSessions;
        private OfflineTts? _offlineTts;

        protected BaseSherpaTts(IAudioEditor audioEditor, ILogger<TLogger> logger) : base(logger)
        {
            this._audioEditor = audioEditor;
            this._ttsSessions = new ConcurrentDictionary<string, ITtsEventCallback>();
        }
        public override string ProviderType => "tts";
        public AudioSavingConfig? AudioSavingConfig { get; protected set; }
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

            this.SpeechRate = modelSetting.Config.GetConfigValueOrDefault("SpeechRate", 1.0f);
            this.SpeakerId = modelSetting.Config.GetConfigValueOrDefault("SpeakerId", 50);
            this.AudioSavingConfig = modelSetting.Config.GetConfigValueOrDefault("FileSavingOption", new AudioSavingConfig(false));
            if (this.AudioSavingConfig.SaveFile && !Directory.Exists(this.AudioSavingConfig.SavePath))
            {
                Directory.CreateDirectory(this.AudioSavingConfig.SavePath);
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

        public override bool CheckDeviceRegistered(string deviceId, string sessionId)
        {
            return this._ttsSessions.ContainsKey(deviceId);
        }

        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(workflow.DeviceId, workflow.SessionId))
            {
                throw new SessionNotInitializedException();
            }
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


                    if (this.AudioSavingConfig is not null && this.AudioSavingConfig.SaveFile)
                    {
                        string fileName = $"{this.ProviderType}_{segment.SentenceId}.{this.AudioSavingConfig.Format}";
                        string filePath = Path.Combine(this.AudioSavingConfig.SavePath, fileName);
                        if (File.Exists(filePath))
                            File.Delete(filePath);
                        bool saved = await this._audioEditor.SaveAudioFileAsync(filePath, audio.Samples, this.GetTtsSampleRate(), 1, 128000);
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
