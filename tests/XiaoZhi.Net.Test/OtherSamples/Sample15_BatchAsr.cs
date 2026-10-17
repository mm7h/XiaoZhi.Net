using System.Collections.Concurrent;
using SherpaOnnx;
using XiaoZhi.Net.Test.Helpers;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample15_BatchAsr
    {
        private const int MaxBatchSize = 5;
        private const int MaxWaitingTimeMs = 100;
        private const string ModelFileFoler = "./models/sense-voice";
        private const string TestWavFile1 = "./audioFile/max_output_size.wav";
        private const string TestWavFile2 = "./audioFile/bind_code.wav";

        private static readonly CancellationTokenSource s_shutdownCts = new CancellationTokenSource();
        private static readonly ConcurrentDictionary<string, OfflineStream> s_streamMapping = new ConcurrentDictionary<string, OfflineStream>();
        private static readonly ConcurrentQueue<AsrRequest> s_requestQueue = new ConcurrentQueue<AsrRequest>();

        public static async Task RunAsync()
        {
            Console.WriteLine("Sample15_BatchAsr");
            await TestBatchAsr();
        }

        public static Task TestBatchAsr()
        {
            OfflineRecognizerConfig offlineRecognizerConfig = new OfflineRecognizerConfig();
            offlineRecognizerConfig.ModelConfig.SenseVoice.Model = Path.Combine(ModelFileFoler, "model.onnx");
            offlineRecognizerConfig.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
            offlineRecognizerConfig.ModelConfig.Tokens = Path.Combine(ModelFileFoler, "tokens.txt");

            if (offlineRecognizerConfig.DecodingMethod == "modified_beam_search")
            {
                offlineRecognizerConfig.MaxActivePaths = 4;
            }
            if (File.Exists(Path.Combine(ModelFileFoler, "hotwords.txt")))
            {
                offlineRecognizerConfig.HotwordsFile = Path.Combine(ModelFileFoler, "hotwords.txt");
                offlineRecognizerConfig.DecodingMethod = "modified_beam_search";
            }
            else
            {
                offlineRecognizerConfig.DecodingMethod = "greedy_search";
            }

            offlineRecognizerConfig.HotwordsScore = 1.5F;
            var offlineRecognizer = new OfflineRecognizer(offlineRecognizerConfig);

            Console.WriteLine("ASR model created.");
            _ = Task.Run(() => ProcessingAsync(offlineRecognizer));

            Console.WriteLine("type \"1\" or \"2\" to add the audio data.");
            Console.WriteLine("Press exit to quit.");
            while (true)
            {
                string? key = Console.ReadLine();
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }
                if (key.ToLower() == "exit")
                {
                    s_shutdownCts.Cancel();
                    break;
                }

                if (int.TryParse(key, out int wavFileIndex))
                {
                    string sessionId = Guid.NewGuid().ToString();
                    switch (wavFileIndex)
                    {
                        case 1:
                            _ = ConvertSpeechTextAsync(sessionId, TestWavFile1, offlineRecognizer);
                            break;
                        case 2:
                            _ = ConvertSpeechTextAsync(sessionId, TestWavFile2, offlineRecognizer);
                            break;
                        default:
                            Console.WriteLine("Invalid number");
                            break;
                    }
                    Console.WriteLine("Please continue...");
                }
                else
                {
                    Console.WriteLine("Please input a number.");
                }
            }
            Console.WriteLine("Done.");
            return Task.CompletedTask;
        }

        private static async Task ProcessingAsync(OfflineRecognizer offlineRecognizer)
        {
            CancellationToken shutDownToken = s_shutdownCts.Token;
            DateTime lastProcessTime = DateTime.Now;
            TimeSpan maxWaitTime = TimeSpan.FromMilliseconds(MaxWaitingTimeMs);
            while (!shutDownToken.IsCancellationRequested)
            {
                if (s_requestQueue.Count > MaxBatchSize || DateTime.Now - lastProcessTime > maxWaitTime)
                {
                    List<AsrRequest> batchRequests = new List<AsrRequest>(s_requestQueue.Count);
                    while (batchRequests.Count < MaxBatchSize && s_requestQueue.TryDequeue(out AsrRequest? request))
                    {
                        if (request != null)
                        {
                            if (request.Token.IsCancellationRequested)
                            {
                                request.ResultTcs.SetCanceled();
                                request.Stream.Dispose();
                                s_streamMapping.Remove(request.SessionId, out _);
                                continue;
                            }
                            batchRequests.Add(request);
                        }
                    }
                    if (batchRequests.Count > 0)
                    {
                        await Task.Run(() =>
                        {
                            offlineRecognizer.Decode(batchRequests.Select(b => b.Stream));
                        });

                        // 将返回结果返回给各个请求
                        foreach (AsrRequest request in batchRequests)
                        {
                            if (request.Token.IsCancellationRequested)
                            {
                                request.ResultTcs.SetCanceled();
                                request.Stream.Dispose();
                                s_streamMapping.Remove(request.SessionId, out _);
                                continue;
                            }
                            string resultText = request.Stream.Result.Text;
                            request.ResultTcs.SetResult(resultText);
                        }
                    }
                }
                else
                {
                    lastProcessTime = DateTime.Now;
                    await Task.Delay(200, shutDownToken);
                }
            }
        }

        private static async Task ConvertSpeechTextAsync(string sessionId, string wavFile, OfflineRecognizer offlineRecognizer)
        {
            Console.WriteLine($"Session {sessionId} trys to process.");

            OfflineStream offlineStream = s_streamMapping.GetOrAdd(sessionId, (key) => offlineRecognizer.CreateStream());

            var reader = new WaveReader(wavFile);

            string deviceId = $"device-{Random.Shared.Next(0, 10)}";
            offlineStream.AcceptWaveform(reader.SampleRate, reader.Samples);
            AsrRequest asrRequest = new AsrRequest(sessionId, deviceId, offlineStream, reader.SampleRate, CancellationToken.None);

            s_requestQueue.Enqueue(asrRequest);
            string result = await asrRequest.ResultTcs.Task;
            await Console.Out.WriteLineAsync($"Session: {sessionId}, result: {result}");
        }
    }

    internal sealed class AsrRequest(string sessionId, string deviceId, OfflineStream stream, int sampleRate, CancellationToken token)
    {
        public string SessionId { get; set; } = sessionId;
        public string DeviceId { get; set; } = deviceId;
        public OfflineStream Stream { get; set; } = stream;
        public int SampleRate { get; set; } = sampleRate;
        public TaskCompletionSource<string> ResultTcs { get; set; } = new TaskCompletionSource<string>();
        public CancellationToken Token { get; set; } = token;
    }

    
}
