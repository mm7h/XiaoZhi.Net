using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Providers.ASR.Contexts;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal abstract class SherpaOfflineAsr<TLogger> : BaseSherpaAsr<TLogger>
    {
        private readonly IAudioEditor _audioEditor;
        private readonly Channel<AsrRequest> _requestChannel;
        private readonly CancellationTokenSource _shutdownCts;
        private OfflineRecognizer? _offlineRecognizer;
        private Task? _backgroudProcessingTask;

        protected SherpaOfflineAsr(IAudioEditor audioEditor, ILogger<TLogger> logger) : base(logger)
        {
            this._audioEditor = audioEditor;
            this._requestChannel = Channel.CreateUnbounded<AsrRequest>();
            this._shutdownCts = new CancellationTokenSource();
        }

        public int MaxBatchSize { get; protected set; } = 10;
        public int BatchWaitTimeMs { get; protected set; } = 20;
        public AudioSavingConfig? AudioSavingConfig { get; protected set; }
        public override bool IsStreaming => false;

        protected void Build(OfflineRecognizerConfig offlineRecognizerConfig, ModelSetting modelSetting, bool useTokens = true)
        {
            if (useTokens)
            {
                offlineRecognizerConfig.ModelConfig.Tokens = Path.Combine(this.ModelFileFoler, "tokens.txt");
            }
            offlineRecognizerConfig.ModelConfig.NumThreads = modelSetting.Config.GetConfigValueOrDefault("NumThreads", 1);
            offlineRecognizerConfig.ModelConfig.Provider = modelSetting.Config.GetConfigValueOrDefault("Provider", "cpu");
            offlineRecognizerConfig.ModelConfig.Debug = modelSetting.Config.GetConfigValueOrDefault("Debug", 0);

            string? hotwordsFile = modelSetting.Config.GetConfigValueOrDefault("HotwordsFile");
            if (!string.IsNullOrWhiteSpace(hotwordsFile))
            {
                offlineRecognizerConfig.HotwordsFile = Path.Combine(this.ModelFileFoler, hotwordsFile);
                offlineRecognizerConfig.HotwordsScore = modelSetting.Config.GetConfigValueOrDefault("HotwordsScore", 1.5F);
                offlineRecognizerConfig.DecodingMethod = "modified_beam_search";
                offlineRecognizerConfig.MaxActivePaths = modelSetting.Config.GetConfigValueOrDefault("MaxActivePaths", 4);
            }
            else
            {
                offlineRecognizerConfig.DecodingMethod = "greedy_search";
            }

            this.MaxBatchSize = modelSetting.Config.GetConfigValueOrDefault("MaxBatchSize", 10);
            this.BatchWaitTimeMs = modelSetting.Config.GetConfigValueOrDefault("BatchWaitTimeMs", 20);
            this.AudioSavingConfig = modelSetting.Config.GetConfigValueOrDefault("FileSavingOption", new AudioSavingConfig(false));
            if (this.AudioSavingConfig.SaveFile && !Directory.Exists(this.AudioSavingConfig.SavePath))
            {
                Directory.CreateDirectory(this.AudioSavingConfig.SavePath);
            }
            this._offlineRecognizer = new OfflineRecognizer(offlineRecognizerConfig);
            this._backgroudProcessingTask = Task.Run(this.ProcessingAsync);
        }

        public override async Task ConvertSpeechTextAsync(Workflow<float[]> workflow, int sampleRate, int frameSize, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(workflow.DeviceId, workflow.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._offlineRecognizer == null)
            {
                throw new ArgumentNullException(Lang.BaseSherpaAsr_ConvertSpeechTextAsync_ProviderNotBuilt);
            }

            if (this.TryGetCallback(workflow.DeviceId, workflow.SessionId, out IAsrEventCallback? callback) && callback is not null)
            {
                OfflineStream? offlineStream = null;
                try
                {
                    if (this.AudioSavingConfig is not null && this.AudioSavingConfig.SaveFile)
                    {
                        string fileName = this.GenerateAudioFileName(workflow);
                        string filePath = Path.Combine(this.AudioSavingConfig.SavePath, $"{this.ProviderType}_{fileName}.{this.AudioSavingConfig.Format}");
                        bool saved = await this._audioEditor.SaveAudioFileAsync(filePath, workflow.Data);
                        if (saved)
                        {
                            this.Logger.LogDebug(Lang.BaseSherpaAsr_ConvertSpeechTextAsync_AudioSaved, fileName);
                        }
                        else
                        {
                            this.Logger.LogWarning(Lang.BaseSherpaAsr_ConvertSpeechTextAsync_AudioNotSaved, fileName);
                        }
                    }

                    offlineStream = this._offlineRecognizer.CreateStream();
                    offlineStream.AcceptWaveform(sampleRate, workflow.Data);
                    AsrRequest request = new AsrRequest(
                        workflow.SessionId,
                        workflow.DeviceId,
                        offlineStream,
                        sampleRate,
                        frameSize,
                        workflow.TurnId,
                        callback,
                        token);
                    await this._requestChannel.Writer.WriteAsync(request, token);
                    offlineStream = null;
                }
                catch (OperationCanceledException)
                {
                    this.Logger.LogDebug(Lang.BaseSherpaAsr_ConvertSpeechTextAsync_RequestCancelled, workflow.DeviceId);
                    throw;
                }
                catch (Exception ex)
                {
                    this.Logger.LogError(ex, Lang.BaseSherpaAsr_ConvertSpeechTextAsync_UnexpectedError, this.ProviderType);
                    throw;
                }
                finally
                {
                    offlineStream?.Dispose();
                }
            }
        }

        public override Task ConvertSpeechTextStreamingAsync(Workflow<float[]> workflow, int sampleRate, int frameSize, StreamingAsrOperation operation, CancellationToken token)
        {
            throw new NotSupportedException($"{this.ModelName} does not support streaming ASR.");
        }

        public override void Dispose()
        {
            this._shutdownCts.Cancel();
            try
            {
                this._backgroudProcessingTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                this.Logger.LogError(Lang.BaseSherpaAsr_Dispose_WaitFailed);
            }

            this._offlineRecognizer?.Dispose();
            this._shutdownCts.Dispose();
            this.ClearRegisteredDevices();
        }

        private string GenerateAudioFileName<T>(Workflow<T> workflow)
        {
            string devicePart = this.ReplaceMacDelimiters(workflow.DeviceId, "_");
            string sessionPart = workflow.SessionId.Replace("-", string.Empty);
            if (sessionPart.Length > 7)
            {
                sessionPart = sessionPart.Substring(0, 7);
            }
            return $"{devicePart}_{sessionPart}_{workflow.TurnId}";
        }

        private async Task ProcessingAsync()
        {
            if (this._offlineRecognizer == null)
            {
                throw new ArgumentNullException(Lang.BaseSherpaAsr_Processing_ProviderNotBuilt);
            }
            CancellationToken shutdownToken = this._shutdownCts.Token;

            while (!shutdownToken.IsCancellationRequested)
            {
                try
                {
                    if (!await this._requestChannel.Reader.WaitToReadAsync(shutdownToken))
                    {
                        break;
                    }

                    List<AsrRequest> batchRequests = new List<AsrRequest>(this.MaxBatchSize);
                    if (this._requestChannel.Reader.TryRead(out AsrRequest? firstRequest))
                    {
                        batchRequests.Add(firstRequest);
                    }

                    if (this.MaxBatchSize > 1)
                    {
                        while (batchRequests.Count < this.MaxBatchSize && this._requestChannel.Reader.TryRead(out AsrRequest? request))
                        {
                            batchRequests.Add(request);
                        }

                        if (this.BatchWaitTimeMs > 0 && batchRequests.Count < this.MaxBatchSize)
                        {
                            Task timeoutTask = Task.Delay(this.BatchWaitTimeMs, shutdownToken);
                            while (batchRequests.Count < this.MaxBatchSize)
                            {
                                Task waitToReadTask = this._requestChannel.Reader.WaitToReadAsync(shutdownToken).AsTask();
                                Task completed = await Task.WhenAny(waitToReadTask, timeoutTask);
                                if (completed == timeoutTask || shutdownToken.IsCancellationRequested)
                                {
                                    break;
                                }
                                while (batchRequests.Count < this.MaxBatchSize && this._requestChannel.Reader.TryRead(out AsrRequest? request))
                                {
                                    batchRequests.Add(request);
                                }
                            }
                        }
                    }

                    List<AsrRequest> validRequests = new List<AsrRequest>(batchRequests.Count);
                    foreach (AsrRequest request in batchRequests)
                    {
                        if (request.Token.IsCancellationRequested)
                        {
                            request.Stream.Dispose();
                            this.Logger.LogDebug(Lang.BaseSherpaAsr_Processing_RequestCancelledBeforeProcessing, request.DeviceId);
                            continue;
                        }
                        validRequests.Add(request);
                    }

                    if (validRequests.Count == 0)
                    {
                        continue;
                    }

                    this._offlineRecognizer.Decode(validRequests.Select(request => request.Stream));
                    foreach (AsrRequest request in validRequests)
                    {
                        try
                        {
                            if (request.Token.IsCancellationRequested)
                            {
                                this.Logger.LogDebug(Lang.BaseSherpaAsr_Processing_RequestCancelledAfterDecoding, request.DeviceId);
                            }
                            else
                            {
                                request.Callback.OnSpeechTextConverted(request.TurnId, true, request.Stream.Result.Text);
                            }
                        }
                        catch (Exception ex)
                        {
                            request.Callback.OnSpeechTextConverted(request.TurnId, false, string.Empty);
                            this.Logger.LogError(ex, Lang.BaseSherpaAsr_Processing_ResultProcessingError, request.DeviceId);
                        }
                        finally
                        {
                            request.Stream.Dispose();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    this.Logger.LogError(ex, Lang.BaseSherpaAsr_Processing_ErrorLoop);
                }
            }
        }
    }
}
