using SherpaOnnx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample15_BatchAsr
    {
        const int MAX_BATCH_SIZE = 5;
        const int MAX_WAITING_TIME_MS = 100;
        const string MODEL_FILE_FOLER = "./models/sense-voice";
        const string TEST_WAV_FILE1 = "./audioFile/max_output_size.wav";
        const string TEST_WAV_FILE2 = "./audioFile/bind_code.wav";

        private static CancellationTokenSource _shutdownCts = new CancellationTokenSource();
        private static ConcurrentDictionary<string, OfflineStream> _streamMapping = new ConcurrentDictionary<string, OfflineStream>();
        private static ConcurrentQueue<AsrRequest> _requestQueue = new ConcurrentQueue<AsrRequest>();

        public static async Task Run()
        {
            Console.WriteLine("Sample15_BatchAsr");
            await TestBatchAsr();
        }

        public static async Task TestBatchAsr()
        {
            OfflineRecognizerConfig offlineRecognizerConfig = new OfflineRecognizerConfig();
            offlineRecognizerConfig.ModelConfig.SenseVoice.Model = Path.Combine(MODEL_FILE_FOLER, "model.onnx");
            offlineRecognizerConfig.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
            offlineRecognizerConfig.ModelConfig.Tokens = Path.Combine(MODEL_FILE_FOLER, "tokens.txt");
            offlineRecognizerConfig.DecodingMethod = "greedy_search";
            if (offlineRecognizerConfig.DecodingMethod == "modified_beam_search")
            {
                offlineRecognizerConfig.MaxActivePaths = 4;
            }
            offlineRecognizerConfig.HotwordsFile = Path.Combine(MODEL_FILE_FOLER, "hotwords.txt");
            offlineRecognizerConfig.HotwordsScore = 1.5F;
            var offlineRecognizer = new OfflineRecognizer(offlineRecognizerConfig);

            Console.WriteLine("ASR model created.");

            await Task.Run(() => Processing(offlineRecognizer));

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
                    _shutdownCts.Cancel();
                    break;
                }

                if (int.TryParse(key, out int wavFileIndex))
                {
                    string sessionId = Guid.NewGuid().ToString();
                    switch (wavFileIndex)
                    {
                        case 1:
                            await ConvertSpeechTextAsync(sessionId, TEST_WAV_FILE1, offlineRecognizer);
                            break;
                        case 2:
                            await ConvertSpeechTextAsync(sessionId, TEST_WAV_FILE2, offlineRecognizer);
                            break;
                        default:
                            Console.WriteLine("Invalid number");
                            break;
                    }
                }
                else
                {
                    Console.WriteLine("Please input a number.");
                }
            }
            Console.WriteLine("Done.");
        }

        private static async Task Processing(OfflineRecognizer offlineRecognizer)
        {
            CancellationToken shutDownToken = _shutdownCts.Token;
            DateTime lastProcessTime = DateTime.Now;
            TimeSpan maxWaitTime = TimeSpan.FromMilliseconds(MAX_WAITING_TIME_MS);
            while (!shutDownToken.IsCancellationRequested)
            {
                if (_requestQueue.Count > MAX_BATCH_SIZE || lastProcessTime - DateTime.Now > maxWaitTime)
                {

                    List<AsrRequest> batchRequests = new List<AsrRequest>(_requestQueue.Count);
                    while (batchRequests.Count < MAX_BATCH_SIZE && _requestQueue.TryDequeue(out AsrRequest? request))
                    {
                        if (request != null)
                        {
                            if (request.Token.IsCancellationRequested)
                            {
                                request.ResultTcs.SetCanceled();
                                request.Stream.Dispose();
                                _streamMapping.Remove(request.SessionId, out _);
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
                                _streamMapping.Remove(request.SessionId, out _);
                                continue;
                            }
                            string resultText = request.Stream.Result.Text;
                            request.ResultTcs.SetResult(resultText);
                        }
                    }
                    lastProcessTime = DateTime.Now;
                }
                else
                {
                    await Task.Delay(10, shutDownToken);
                }
            }
        }

        private static async Task<string> ConvertSpeechTextAsync(string sessionId, string wavFile, OfflineRecognizer offlineRecognizer)
        {
            Console.WriteLine($"Session {sessionId} trys to process.");
            OfflineStream offlineStream = _streamMapping.GetOrAdd(sessionId, (key) => offlineRecognizer.CreateStream());

            var reader = new WaveReader(wavFile);

            string deviceId = $"device-{Random.Shared.Next(0, 10)}";

            AsrRequest asrRequest = new AsrRequest(sessionId, deviceId, offlineStream, 16000, CancellationToken.None);

            _requestQueue.Enqueue(asrRequest);
            string result = await asrRequest.ResultTcs.Task;
            return result;
        }
    }

    internal sealed class AsrRequest
    {
        public AsrRequest(string sessionId, string deviceId, OfflineStream stream, int sampleRate, CancellationToken token)
        {
            this.SessionId = sessionId;
            this.DeviceId = deviceId;
            this.Stream = stream;
            this.SampleRate = sampleRate;
            this.ResultTcs = new TaskCompletionSource<string>();
            this.Token = token;
        }

        public string SessionId { get; set; }
        public string DeviceId { get; set; }
        public OfflineStream Stream { get; set; }
        public int SampleRate { get; set; }
        public TaskCompletionSource<string> ResultTcs { get; set; }
        public CancellationToken Token { get; set; }
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
            if (ChunkID != 0x46464952)
            {
                Console.WriteLine($"Invalid chunk ID: 0x{ChunkID:X}. Expect 0x46464952");
                return false;
            }

            //               E V A W
            if (Format != 0x45564157)
            {
                Console.WriteLine($"Invalid format: 0x{Format:X}. Expect 0x45564157");
                return false;
            }

            //                      t m f
            if (SubChunk1ID != 0x20746d66)
            {
                Console.WriteLine($"Invalid SubChunk1ID: 0x{SubChunk1ID:X}. Expect 0x20746d66");
                return false;
            }

            if (SubChunk1Size != 16)
            {
                Console.WriteLine($"Invalid SubChunk1Size: {SubChunk1Size}. Expect 16");
                return false;
            }

            if (AudioFormat != 1)
            {
                Console.WriteLine($"Invalid AudioFormat: {AudioFormat}. Expect 1");
                return false;
            }

            if (NumChannels != 1)
            {
                Console.WriteLine($"Invalid NumChannels: {NumChannels}. Expect 1");
                return false;
            }

            if (ByteRate != (SampleRate * NumChannels * BitsPerSample / 8))
            {
                Console.WriteLine($"Invalid byte rate: {ByteRate}.");
                return false;
            }

            if (BlockAlign != (NumChannels * BitsPerSample / 8))
            {
                Console.WriteLine($"Invalid block align: {ByteRate}.");
                return false;
            }

            if (BitsPerSample != 16)
            {  // we support only 16 bits per sample
                Console.WriteLine($"Invalid bits per sample: {BitsPerSample}. Expect 16");
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

            _header = ReadHeader(reader);

            if (!_header.Validate())
            {
                throw new ApplicationException($"Invalid wave file ${fileName}");
            }

            SkipMetaData(reader);

            // now read samples
            // _header.SubChunk2Size contains number of bytes in total.
            // we assume each sample is of type int16
            var buffer = reader.ReadBytes(_header.SubChunk2Size);
            var samples_int16 = new short[_header.SubChunk2Size / 2];
            Buffer.BlockCopy(buffer, 0, samples_int16, 0, buffer.Length);

            _samples = new float[samples_int16.Length];

            for (var i = 0; i < samples_int16.Length; ++i)
            {
                _samples[i] = samples_int16[i] / 32768.0F;
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

            var subChunk2ID = _header.SubChunk2ID;
            var subChunk2Size = _header.SubChunk2Size;

            while (bs.Position != bs.Length && subChunk2ID != 0x61746164)
            {
                bs.Seek(subChunk2Size, SeekOrigin.Current);
                subChunk2ID = reader.ReadInt32();
                subChunk2Size = reader.ReadInt32();
            }
            _header.SubChunk2ID = subChunk2ID;
            _header.SubChunk2Size = subChunk2Size;
        }

        private WaveHeader _header;

        // Samples are normalized to the range [-1, 1]
        private float[] _samples;

        public int SampleRate => _header.SampleRate;

        public float[] Samples => _samples;

        public static void Test(string fileName)
        {
            WaveReader reader = new WaveReader(fileName);
            Console.WriteLine($"samples length: {reader.Samples.Length}");
            Console.WriteLine($"samples rate: {reader.SampleRate}");
        }
    }
    #endregion
}
