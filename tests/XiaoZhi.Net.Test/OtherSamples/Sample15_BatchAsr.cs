using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SherpaOnnx;

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

    #region WaveHeader
    // Copyright (c)  2023  Xiaomi Corporation (authors: Fangjun Kuang)
    [StructLayout(LayoutKind.Sequential)]
    public struct WaveHeader
    {
        public int ChunkID;
        public int ChunkSize;
        public int Format;
        public int SubChunk1ID;
        public int SubChunk1Size;
        public short AudioFormat;
        public short NumChannels;
        public int SampleRate;
        public int ByteRate;
        public short BlockAlign;
        public short BitsPerSample;
        public int SubChunk2ID;
        public int SubChunk2Size;

        public bool Validate()
        {
            if (this.ChunkID != 0x46464952)
            {
                Console.WriteLine($"Invalid chunk ID: 0x{this.ChunkID:X}. Expect 0x46464952");
                return false;
            }

            //               E V A W
            if (this.Format != 0x45564157)
            {
                Console.WriteLine($"Invalid format: 0x{this.Format:X}. Expect 0x45564157");
                return false;
            }

            //                      t m f
            if (this.SubChunk1ID != 0x20746d66)
            {
                Console.WriteLine($"Invalid SubChunk1ID: 0x{this.SubChunk1ID:X}. Expect 0x20746d66");
                return false;
            }

            if (this.SubChunk1Size != 16)
            {
                Console.WriteLine($"Invalid SubChunk1Size: {this.SubChunk1Size}. Expect 16");
                return false;
            }

            if (this.AudioFormat != 1)
            {
                Console.WriteLine($"Invalid AudioFormat: {this.AudioFormat}. Expect 1");
                return false;
            }

            if (this.NumChannels != 1)
            {
                Console.WriteLine($"Invalid NumChannels: {this.NumChannels}. Expect 1");
                return false;
            }

            if (this.ByteRate != (this.SampleRate * this.NumChannels * this.BitsPerSample / 8))
            {
                Console.WriteLine($"Invalid byte rate: {this.ByteRate}.");
                return false;
            }

            if (this.BlockAlign != (this.NumChannels * this.BitsPerSample / 8))
            {
                Console.WriteLine($"Invalid block align: {this.ByteRate}.");
                return false;
            }

            if (this.BitsPerSample != 16)
            {  // we support only 16 bits per sample
                Console.WriteLine($"Invalid bits per sample: {this.BitsPerSample}. Expect 16");
                return false;
            }

            return true;
        }
    }

    // It supports only 16-bit, single channel WAVE format.
    // The sample rate can be any value.
    public class WaveReader
    {
        public WaveReader(string fileName)
        {
            if (!File.Exists(fileName))
            {
                throw new ApplicationException($"{fileName} does not exist!");
            }

            using var stream = File.Open(fileName, FileMode.Open);
            using var reader = new BinaryReader(stream);

            this._header = ReadHeader(reader);

            if (!this._header.Validate())
            {
                throw new ApplicationException($"Invalid wave file ${fileName}");
            }

            this.SkipMetaData(reader);

            // now read samples
            // _header.SubChunk2Size contains number of bytes in total.
            // we assume each sample is of type int16
            var buffer = reader.ReadBytes(this._header.SubChunk2Size);
            var samples_int16 = new short[this._header.SubChunk2Size / 2];
            Buffer.BlockCopy(buffer, 0, samples_int16, 0, buffer.Length);

            this.Samples = new float[samples_int16.Length];

            for (var i = 0; i < samples_int16.Length; ++i)
            {
                this.Samples[i] = samples_int16[i] / 32768.0F;
            }
        }

        private static WaveHeader ReadHeader(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(Marshal.SizeOf(typeof(WaveHeader)));

            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            WaveHeader header = (WaveHeader)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(WaveHeader))!;
            handle.Free();

            return header;
        }

        private void SkipMetaData(BinaryReader reader)
        {
            var bs = reader.BaseStream;

            var subChunk2ID = this._header.SubChunk2ID;
            var subChunk2Size = this._header.SubChunk2Size;

            while (bs.Position != bs.Length && subChunk2ID != 0x61746164)
            {
                bs.Seek(subChunk2Size, SeekOrigin.Current);
                subChunk2ID = reader.ReadInt32();
                subChunk2Size = reader.ReadInt32();
            }
            this._header.SubChunk2ID = subChunk2ID;
            this._header.SubChunk2Size = subChunk2Size;
        }

        private WaveHeader _header;

        // Samples are normalized to the range [-1, 1]

        public int SampleRate => this._header.SampleRate;

        public float[] Samples { get; }

        public static void Test(string fileName)
        {
            WaveReader reader = new WaveReader(fileName);
            Console.WriteLine($"samples length: {reader.Samples.Length}");
            Console.WriteLine($"samples rate: {reader.SampleRate}");
        }
    }
    #endregion
}
