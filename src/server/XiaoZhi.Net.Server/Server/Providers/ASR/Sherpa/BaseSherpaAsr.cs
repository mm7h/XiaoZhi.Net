using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal abstract class BaseSherpaAsr<TLogger> : BaseProvider<TLogger, ModelSetting>
    {
        private const int MAX_WAITING_TIME_MS = 100;
        //private const int MAX_QUEUE_SIZE = 100;

        private readonly ConcurrentQueue<AsrRequest> _requestQueue;
        private readonly CancellationTokenSource _shutdownCts;

        private readonly ConcurrentDictionary<string, OfflineStream> _streamMapping;

        private OfflineRecognizer? _offlineRecognizer;
        private Task? _backgroudProcessingTask;

        protected BaseSherpaAsr(ILogger<TLogger> logger) : base(logger)
        {
            this._requestQueue = new ConcurrentQueue<AsrRequest>();
            this._streamMapping = new ConcurrentDictionary<string, OfflineStream>();
            this._shutdownCts = new CancellationTokenSource();
        }

        public int MaxBatchSize { get; protected set; } = 50;

        public override string ProviderType => "asr";

        public async Task<string> ConvertSpeechTextAsync(Workflow<CircularBuffer> workflow, int sampleRate, int frameSize, CancellationToken token)
        {
            if (this._offlineRecognizer == null)
            {
                throw new ArgumentNullException("Please build asr provider first.");
            }
            if (workflow.Data.Size <= 50)
            {
                this.Logger.LogWarning("The audio data for the device {deviceId} is too short.", workflow.DeviceId);
                workflow.Data.Reset();
                return string.Empty;
            }
            try
            {
                OfflineStream offlineStream = this._streamMapping.GetOrAdd(workflow.SessionId, (key) => this._offlineRecognizer.CreateStream());

                while (workflow.Data.GetFrames(frameSize, out float[] chunk))
                {
                    offlineStream.AcceptWaveform(sampleRate, chunk);
                }

                AsrRequest asrRequest = new AsrRequest(workflow.SessionId, workflow.DeviceId, offlineStream, sampleRate, frameSize, token);

                this._requestQueue.Enqueue(asrRequest);
                string result = await asrRequest.ResultTcs.Task;
                return result;
            }
            catch (OperationCanceledException)
            {
                workflow.Data.Reset();
                this.Logger.LogWarning("User canceled the job for {providerType}.", this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                workflow.Data.Reset();
                this.Logger.LogError(ex, "Unexpected error(s) for {providerType}.", this.ProviderType);
                return string.Empty;
            }
        }

        protected void Build(OfflineRecognizerConfig offlineRecognizerConfig, ModelSetting modelSetting)
        {
            offlineRecognizerConfig.ModelConfig.Tokens = Path.Combine(ModelFileFoler, "tokens.txt");

            if (!string.IsNullOrEmpty(modelSetting.Config.HotwordsFile))
            {
                offlineRecognizerConfig.HotwordsFile = Path.Combine(ModelFileFoler, "hotwords.txt");
                offlineRecognizerConfig.HotwordsScore = modelSetting.Config.HotwordsScore ?? 1.5F;
                offlineRecognizerConfig.DecodingMethod = "modified_beam_search";
                offlineRecognizerConfig.MaxActivePaths = modelSetting.Config.MaxActivePaths ?? 4;
            }
            else
            {
                offlineRecognizerConfig.DecodingMethod = "greedy_search";
            }
            //this._config.RuleFsts = this.ModelSetting.Config.RuleFsts;

            this.MaxBatchSize = modelSetting.Config.MaxBatchSize ?? 50;

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
            TimeSpan maxWaitTime = TimeSpan.FromMilliseconds(MAX_WAITING_TIME_MS);
            Stopwatch stopwatch = Stopwatch.StartNew();

            while (!shutDownToken.IsCancellationRequested)
            {
                if (this._requestQueue.Count >= this.MaxBatchSize || stopwatch.Elapsed >= maxWaitTime)
                {
                    List<AsrRequest> batchRequests = new List<AsrRequest>(this._requestQueue.Count);
                    while (batchRequests.Count < this.MaxBatchSize && this._requestQueue.TryDequeue(out AsrRequest? request))
                    {
                        if (request != null)
                        {
                            if (request.Token.IsCancellationRequested)
                            {
                                request.ResultTcs.SetCanceled();
                                request.Stream.Dispose();
                                this._streamMapping.Remove(request.SessionId, out _);
                                continue;
                            }
                            batchRequests.Add(request);
                        }
                    }
                    if (batchRequests.Count > 0)
                    {
                        await Task.Run(() =>
                        {
                            this._offlineRecognizer.Decode(batchRequests.Select(b => b.Stream));
                        });

                        // 将返回结果返回给各个请求
                        foreach (AsrRequest request in batchRequests)
                        {
                            if (request.Token.IsCancellationRequested)
                            {
                                request.ResultTcs.SetCanceled();
                                request.Stream.Dispose();
                                this._streamMapping.Remove(request.SessionId, out _);
                                continue;
                            }
                            string resultText = request.Stream.Result.Text;
                            request.ResultTcs.SetResult(resultText);
                        }
                    }
                    stopwatch.Restart();
                }
                else
                {
                    await Task.Delay(10, shutDownToken);
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
            this._streamMapping.Clear();
        }
    }
}
