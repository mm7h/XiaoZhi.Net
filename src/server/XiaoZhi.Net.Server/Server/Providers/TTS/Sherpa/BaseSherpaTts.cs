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
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.TTS.Sherpa
{
    internal abstract class BaseSherpaTts<TLogger> : BaseProvider<TLogger, ModelSetting>, ITts
    {
        private readonly ConcurrentDictionary<string, ITtsEventCallback> _ttsEventCallbackMapping;
        private OfflineTts? _offlineTts;



        protected BaseSherpaTts(ILogger<TLogger> logger) : base(logger)
        {
            this._ttsEventCallbackMapping = new ConcurrentDictionary<string, ITtsEventCallback>();
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


        public void RegisterDevice(string deviceId, string sessionId, ITtsEventCallback callback)
        {
            this._ttsEventCallbackMapping.TryAdd(deviceId, callback);
        }

        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            if (this._offlineTts == null)
            {
                throw new ArgumentNullException("Please build tts provider first.");
            }

            try
            {
                OutSegment segment = workflow.Data;

                if (this._ttsEventCallbackMapping.TryGetValue(workflow.DeviceId, out ITtsEventCallback? sessionCallback) && sessionCallback is not null)
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
                            sessionCallback.OnSentenceStart(segment.Content, segment.Emotion, this.GenerateId());
                            firstFrameSent = true;
                        }

                        if (progress == 1.0f)
                        {
                            sessionCallback.OnSentenceEnd(segment.Content, segment.Emotion, this.GenerateId());
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
                        string fileName = $"{this.ReplaceMacDelimiters(workflow.DeviceId)}_{DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString()}.wav";
                        string filePath = Path.Combine(this.SavePath, fileName);
                        if (File.Exists(filePath))
                            File.Delete(filePath);
                        bool saved = audio.SaveToWaveFile(filePath);
                        if (saved)
                        {
                            this.Logger.LogDebug("Saved tts wave file {fileName} successed, the duration of file is: {duration}s.", fileName, this.FormatDuration(duration));
                        }
                        else
                        {
                            this.Logger.LogDebug("Failed to save tts wave file {fileName}.", fileName);
                        }
                    }
                    else
                    {
                        this.Logger.LogDebug("TTS generated success, the duration of the voice is: {duration}.", this.FormatDuration(duration));
                    }
                    audio.Dispose();
                    timer.Stop();
                }
                else
                {
                    this.Logger.LogError("TTS event callback is not registered for device {deviceId}.", workflow.DeviceId);
                }
                await Task.CompletedTask;

            }
            catch (OperationCanceledException)
            {
                this.Logger.LogWarning("User canceled the job for {providerType}.", this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Unexpected error(s) for {providerType}.", this.ProviderType);
            }
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
