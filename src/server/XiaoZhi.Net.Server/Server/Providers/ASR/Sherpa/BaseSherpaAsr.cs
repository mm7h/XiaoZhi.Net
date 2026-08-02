using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.ASR.Contexts;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal abstract class BaseSherpaAsr<TLogger> : BaseProvider<TLogger, ModelSetting>
    {
        private readonly IAudioEditor _audioEditor;
        private readonly ConcurrentDictionary<string, IAsrEventCallback> _asrSessions;
        private readonly Channel<AsrRequest> _requestChannel;
        private readonly CancellationTokenSource _shutdownCts;

        private OfflineRecognizer? _offlineRecognizer;

        private Task? _backgroudProcessingTask;

        protected BaseSherpaAsr(IAudioEditor audioEditor, ILogger<TLogger> logger) : base(logger)
        {
            this._audioEditor = audioEditor;
            this._asrSessions = new ConcurrentDictionary<string, IAsrEventCallback>();
            this._requestChannel = Channel.CreateUnbounded<AsrRequest>();
            this._shutdownCts = new CancellationTokenSource();
        }


        public int MaxBatchSize { get; protected set; } = 10;
        public int BatchWaitTimeMs { get; protected set; } = 20;
        public AudioSavingConfig? AudioSavingConfig { get; protected set; }
        public override string ProviderType => "asr";

        protected void Build(OfflineRecognizerConfig offlineRecognizerConfig, ModelSetting modelSetting)
        {
            offlineRecognizerConfig.ModelConfig.Tokens = Path.Combine(this.ModelFileFoler, "tokens.txt");

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
            //this._config.RuleFsts = this.ModelSetting.Config.RuleFsts;

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

        public void RegisterDevice(string deviceId, string sessionId, IAsrEventCallback callback)
        {
            this._asrSessions.AddOrUpdate(deviceId, callback, (_, _) => callback);
            this.Logger.LogDebug(Lang.BaseSherpaAsr_RegisterDevice_Registered, deviceId, sessionId);
        }

        public override void UnregisterDevice(string deviceId, string sessionId)
        {
            if (this._asrSessions.TryRemove(deviceId, out _))
            {
                this.Logger.LogDebug(Lang.BaseSherpaAsr_UnregisterDevice_Unregistered, deviceId, sessionId);
            }
        }

        public override bool CheckDeviceRegistered(string deviceId, string sessionId)
        {
            return this._asrSessions.ContainsKey(deviceId);
        }

        public async Task ConvertSpeechTextAsync(Workflow<float[]> workflow, int sampleRate, int frameSize, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(workflow.DeviceId, workflow.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._offlineRecognizer == null)
            {
                throw new ArgumentNullException(Lang.BaseSherpaAsr_ConvertSpeechTextAsync_ProviderNotBuilt);
            }

            if (this._asrSessions.TryGetValue(workflow.DeviceId, out var callback))
            {
                OfflineStream? offlineStream = null;
                try
                {
                    if (this.AudioSavingConfig is not null && this.AudioSavingConfig.SaveFile)
                    {
                        string fileName = this.GenerateAudioFileName(workflow);
                        string filePath = Path.Combine(this.AudioSavingConfig.SavePath, $"{this.ProviderType}_{fileName}.{this.AudioSavingConfig.Format}");

                        bool userSpeechFileSavingResult = await this._audioEditor.SaveAudioFileAsync(filePath, workflow.Data);
                        if (userSpeechFileSavingResult)
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

                    AsrRequest asrRequest = new AsrRequest(workflow.SessionId, workflow.DeviceId, offlineStream, sampleRate, frameSize, callback, token);

                    await this._requestChannel.Writer.WriteAsync(asrRequest, token);
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
            CancellationToken shutDownToken = this._shutdownCts.Token;

            while (!shutDownToken.IsCancellationRequested)
            {
                try
                {
                    if (!await this._requestChannel.Reader.WaitToReadAsync(shutDownToken))
                    {
                        break;
                    }

                    var batchRequests = new List<AsrRequest>(this.MaxBatchSize);

                    if (this._requestChannel.Reader.TryRead(out var firstRequest))
                    {
                        batchRequests.Add(firstRequest);
                    }

                    if (this.MaxBatchSize > 1)
                    {
                        while (batchRequests.Count < this.MaxBatchSize && this._requestChannel.Reader.TryRead(out var req))
                        {
                            batchRequests.Add(req);
                        }

                        if (this.BatchWaitTimeMs > 0 && batchRequests.Count < this.MaxBatchSize)
                        {
                            Task timeoutTask = Task.Delay(this.BatchWaitTimeMs, shutDownToken);

                            while (batchRequests.Count < this.MaxBatchSize)
                            {
                                Task waitToReadTask = this._requestChannel.Reader.WaitToReadAsync(shutDownToken).AsTask();
                                Task completed = await Task.WhenAny(waitToReadTask, timeoutTask);

                                if (completed == timeoutTask || shutDownToken.IsCancellationRequested)
                                {
                                    break;
                                }

                                while (batchRequests.Count < this.MaxBatchSize && this._requestChannel.Reader.TryRead(out var req))
                                {
                                    batchRequests.Add(req);
                                }
                            }
                        }
                    }

                    if (batchRequests.Count > 0)
                    {
                        // Filter cancelled requests
                        var validRequests = new List<AsrRequest>(batchRequests.Count);
                        foreach (var request in batchRequests)
                        {
                            if (request.Token.IsCancellationRequested)
                            {
                                request.Stream.Dispose();
                                this.Logger.LogDebug(Lang.BaseSherpaAsr_Processing_RequestCancelledBeforeProcessing, request.DeviceId);
                                continue;
                            }
                            validRequests.Add(request);
                        }


                        if (validRequests.Count > 0)
                        {
                            this._offlineRecognizer.Decode(validRequests.Select(b => b.Stream));

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
                                        string resultText = request.Stream.Result.Text;
                                        request.Callback.OnSpeechTextConverted(true, resultText);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    request.Callback.OnSpeechTextConverted(false, string.Empty);
                                    this.Logger.LogError(ex, Lang.BaseSherpaAsr_Processing_ResultProcessingError, request.DeviceId);
                                }
                                finally
                                {
                                    request.Stream.Dispose();
                                }
                            }
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
        }

    }
}
