using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;

namespace XiaoZhi.Net.Server.Providers.TTS.Sherpa
{
    internal abstract class BaseSherpaTts<TLogger> : BaseProvider<TLogger, ModelSetting>, ITts
    {
#if DEBUG
        private const int BOUNDED_CAPACITY = 500;
#else
        private const int BOUNDED_CAPACITY = 1500;
#endif
        private OfflineTts? _offlineTts;

        // ActionBlock-based multi-consumer processing
        private ActionBlock<TtsRequest>? _ttsBlock;
        private int _degreeOfParallelism = Math.Max(1, Environment.ProcessorCount);

        protected BaseSherpaTts(ILogger<TLogger> logger) : base(logger)
        {
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

        public async IAsyncEnumerable<OutAudioSegment> SynthesisEnumerableAsync(Workflow<OutSegment> workflow, [EnumeratorCancellation] CancellationToken token)
        {
            if (!this.CheckDeviceRegistered())
            {
                throw new SessionNotInitializedException();
            }
            if (this._offlineTts == null || this._ttsBlock == null)
            {
                throw new ArgumentNullException("Please build tts provider first.");
            }

            token.ThrowIfCancellationRequested();

            TtsRequest request = new TtsRequest(
                workflow.SessionId,
                workflow.DeviceId,
                workflow.Data.Content,
                workflow.Data.IsFirstSegment,
                workflow.Data.IsLastSegment,
                token);

            bool accepted = await this._ttsBlock.SendAsync(request, token).ConfigureAwait(false);
            if (!accepted)
            {
                throw new InvalidOperationException("TTS block has been completed or declined the request.");
            }

            TtsResponse ttsResponse = await request.ResultTcs.Task.ConfigureAwait(false);
            yield return new OutAudioSegment();
        }

        private void ProcessRequest(TtsRequest request)
        {
            try
            {
                if (request.Token.IsCancellationRequested)
                {
                    request.ResultTcs.TrySetCanceled(request.Token);
                    return;
                }

                if (this._offlineTts is null)
                {
                    request.ResultTcs.TrySetException(new InvalidOperationException("TTS engine is not initialized."));
                    return;
                }

                string segment = request.Content;
                Stopwatch timer = Stopwatch.StartNew();
                OfflineTtsGeneratedAudio audio = this._offlineTts.Generate(segment, this.SpeechRate, this.SpeakerId);

                double duration = Math.Max((this.CalculateDuration(audio.SampleRate, audio.NumSamples) * 1000 - (request.IsFirstSegment ? 300 + timer.ElapsedMilliseconds : 0)), 0);

                OutSegment outSegment = new OutSegment();
                outSegment.Initialize(request.Content, request.IsFirstSegment, request.IsLastSegment);

                // Complete request
                request.ResultTcs.TrySetResult(new TtsResponse(request.SessionId, request.DeviceId, outSegment, audio.Samples));

                if (this.Save2File)
                {
                    string fileName = $"{this.ReplaceMacDelimiters(request.DeviceId)}_{DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString()}.wav";
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
            catch (OperationCanceledException)
            {
                // Prefer canceling the individual request; if not available, cancel with provider token
                request.ResultTcs.TrySetCanceled(request.Token.CanBeCanceled ? request.Token : CancellationToken.None);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Unexpected error(s) for {providerType}.", this.ProviderType);
                request.ResultTcs.TrySetException(ex);
            }
        }

        protected void Build(OfflineTtsConfig offlineTtsConfig, ModelSetting modelSetting)
        {
            offlineTtsConfig.Model.NumThreads = 2;
            offlineTtsConfig.Model.Provider = "cpu";

            this.Save2File = modelSetting.Config.Save2File ?? false;
            this.SpeechRate = modelSetting.Config.SpeechRate ?? 1.0f;
            this.SpeakerId = modelSetting.Config.SpeakerId ?? 50;

            if (this.Save2File)
            {
                this.SavePath = modelSetting.Config.SavePath ?? Path.Combine(Environment.CurrentDirectory, "data", "tts-cache");
                if (!Directory.Exists(this.SavePath))
                    Directory.CreateDirectory(this.SavePath);
            }
            this._offlineTts = new OfflineTts(offlineTtsConfig);

            ExecutionDataflowBlockOptions options = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
                BoundedCapacity = BOUNDED_CAPACITY,
                EnsureOrdered = false
            };

            this._ttsBlock = new ActionBlock<TtsRequest>(this.ProcessRequest, options);
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
            this._ttsBlock?.Complete();
            this._offlineTts?.Dispose();
        }
    }
}
