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
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal abstract class BaseSherpaAsr<TLogger> : BaseProvider<TLogger, ModelSetting>
    {
        private const int MAX_WAITING_TIME_MS = 100;
        //private const int MAX_QUEUE_SIZE = 100;

        private readonly Channel<AsrRequest> _requestChannel;
        private readonly CancellationTokenSource _shutdownCts;

        private OfflineRecognizer? _offlineRecognizer;

        private Task? _backgroudProcessingTask;

        protected BaseSherpaAsr(ILogger<TLogger> logger) : base(logger)
        {
            this._requestChannel = Channel.CreateUnbounded<AsrRequest>();
            this._shutdownCts = new CancellationTokenSource();
        }


        public int MaxBatchSize { get; protected set; } = 50;

        public override string ProviderType => "asr";

        public async Task<string> ConvertSpeechTextAsync(Workflow<float[]> workflow, int sampleRate, int frameSize, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered())
            {
                throw new SessionNotInitializedException();
            }
            if (this._offlineRecognizer == null)
            {
                throw new ArgumentNullException("Please build asr provider first.");
            }

            try
            {
                OfflineStream offlineStream = this._offlineRecognizer.CreateStream();

                offlineStream.AcceptWaveform(sampleRate, workflow.Data);


                AsrRequest asrRequest = new AsrRequest(workflow.SessionId, workflow.DeviceId, offlineStream, sampleRate, frameSize, token);

                await this._requestChannel.Writer.WriteAsync(asrRequest, token);
                string result = await asrRequest.ResultTcs.Task;
                return result;
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogWarning("User canceled the job for {providerType}.", this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Unexpected error(s) for {providerType}.", this.ProviderType);
                return string.Empty;
            }
        }

        protected void Build(OfflineRecognizerConfig offlineRecognizerConfig, ModelSetting modelSetting)
        {
            offlineRecognizerConfig.ModelConfig.Tokens = Path.Combine(ModelFileFoler, "tokens.txt");

            string? hotwordsFile = modelSetting.Config.GetConfigValueOrDefault("HotwordsFile");
            if (!string.IsNullOrEmpty(hotwordsFile))
            {
                offlineRecognizerConfig.HotwordsFile = Path.Combine(ModelFileFoler, hotwordsFile);
                offlineRecognizerConfig.HotwordsScore = modelSetting.Config.GetConfigValueOrDefault("HotwordsScore", 1.5F);
                offlineRecognizerConfig.DecodingMethod = "modified_beam_search";
                offlineRecognizerConfig.MaxActivePaths = modelSetting.Config.GetConfigValueOrDefault("MaxActivePaths", 4);
            }
            else
            {
                offlineRecognizerConfig.DecodingMethod = "greedy_search";
            }
            //this._config.RuleFsts = this.ModelSetting.Config.RuleFsts;

            this.MaxBatchSize = modelSetting.Config.GetConfigValueOrDefault("MaxBatchSize", 50);

            this._offlineRecognizer = new OfflineRecognizer(offlineRecognizerConfig);
            this._backgroudProcessingTask = Task.Run(this.Processing);
        }

        private async Task Processing()
        {
            if (this._offlineRecognizer == null)
            {
                throw new ArgumentNullException("Please build asr provider first.");
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

                        if (batchRequests.Count < this.MaxBatchSize)
                        {
                            using var timeoutCts = new CancellationTokenSource(MAX_WAITING_TIME_MS);
                            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(shutDownToken, timeoutCts.Token);

                            try
                            {
                                while (batchRequests.Count < this.MaxBatchSize)
                                {
                                    if (await this._requestChannel.Reader.WaitToReadAsync(linkedCts.Token))
                                    {
                                        while (batchRequests.Count < this.MaxBatchSize && this._requestChannel.Reader.TryRead(out var req))
                                        {
                                            batchRequests.Add(req);
                                        }
                                    }
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // Ignore timeout, but respect shutdown
                                if (shutDownToken.IsCancellationRequested) break;
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
                                request.ResultTcs.SetCanceled();
                                request.Stream.Dispose();
                                continue;
                            }
                            validRequests.Add(request);
                        }


                        if (validRequests.Count > 0)
                        {
                            await Task.Run(() =>
                            {
                                this._offlineRecognizer.Decode(validRequests.Select(b => b.Stream));
                            });

                            foreach (AsrRequest request in validRequests)
                            {
                                if (request.Token.IsCancellationRequested)
                                {
                                    request.ResultTcs.SetCanceled();
                                    request.Stream.Dispose();
                                    continue;
                                }
                                string resultText = request.Stream.Result.Text;

                                request.Stream.Dispose();
                                request.ResultTcs.SetResult(resultText);
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
                    this.Logger.LogError(ex, "Error in ASR processing loop.");
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
                this.Logger.LogError("Failed to waiting for background task to complete.");
            }

            this._offlineRecognizer?.Dispose();
            this._shutdownCts.Dispose();
        }

    }
}
