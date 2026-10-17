using NAudio.Wave;
using XiaoZhi.Net.Server.Media;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample10_NumberAudioPlayer
    {
        // 数字音频文件目录
        private const string DigitAudioDirectory = "./audioFile/digits/";

        // 支持的音频文件扩展名
        private static readonly string[] s_supportedAudioExtensions = { ".wav", ".mp3", ".aac", ".flac", ".ogg", ".m4a", ".wma" };

        // 预加载的数字音频文件流
        private static readonly Dictionary<int, byte[]> s_digitAudioCache = [];

        // 存储检测到的音频格式
        private static readonly Dictionary<int, string> s_digitAudioFormats = [];

        public static async Task RunAsync()
        {
            // 初始化数字音频文件缓存
            await InitializeDigitAudioCacheAsync();

            // 测试播放单个数字
            await TestPlaySingleDigitAsync(5);

            // 测试播放多位数字
            await TestPlayMultipleDigitsAsync(123456);

            // 测试播放另一个数字
            await TestPlayMultipleDigitsAsync(897098);
        }

        /// <summary>
        /// 初始化数字音频文件缓存，将0-9的音频文件预加载到内存
        /// 支持多种音频格式：WAV, MP3, AAC, FLAC, OGG, M4A, WMA
        /// </summary>
        private static async Task InitializeDigitAudioCacheAsync()
        {
            Console.WriteLine("Initializing digit audio cache...");
            Console.WriteLine($"Supported formats: {string.Join(", ", s_supportedAudioExtensions)}");

            for (int digit = 0; digit <= 9; digit++)
            {
                string? foundFilePath = null;
                string foundFormat = "";

                // 按优先级查找音频文件（WAV优先，然后是其他格式）
                foreach (var extension in s_supportedAudioExtensions)
                {
                    string audioFilePath = Path.Combine(DigitAudioDirectory, $"{digit}{extension}");

                    if (File.Exists(audioFilePath))
                    {
                        foundFilePath = audioFilePath;
                        foundFormat = extension.ToUpper().TrimStart('.');
                        break;
                    }
                }

                if (foundFilePath != null)
                {
                    try
                    {
                        byte[] audioData = await File.ReadAllBytesAsync(foundFilePath);
                        s_digitAudioCache[digit] = audioData;
                        s_digitAudioFormats[digit] = foundFormat;
                        Console.WriteLine($"Loaded {foundFormat} audio file for digit {digit}: {audioData.Length} bytes");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to load audio file for digit {digit}: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"No supported audio file found for digit {digit} in directory: {DigitAudioDirectory}");
                }
            }

            Console.WriteLine($"Digit audio cache initialized with {s_digitAudioCache.Count} files");

            // 显示加载的格式统计
            var formatStats = s_digitAudioFormats.GroupBy(x => x.Value)
                                               .ToDictionary(g => g.Key, g => g.Count());

            Console.WriteLine("Format statistics:");
            foreach (var stat in formatStats)
            {
                Console.WriteLine($"  {stat.Key}: {stat.Value} files");
            }
        }

        /// <summary>
        /// 播放单个数字
        /// </summary>
        private static async Task TestPlaySingleDigitAsync(int digit)
        {
            Console.WriteLine($"\n=== Testing single digit: {digit} ===");

            if (!s_digitAudioCache.ContainsKey(digit))
            {
                Console.WriteLine($"Audio data not found for digit {digit}");
                return;
            }

            var format = s_digitAudioFormats.GetValueOrDefault(digit, "UNKNOWN");
            Console.WriteLine($"Playing digit {digit} in {format} format");

            using var stream = new MemoryStream(s_digitAudioCache[digit]);
            await PlayAudioStreamAsync(stream, $"digit_{digit}_{format}");
        }

        /// <summary>
        /// 播放多位数字（支持混合格式）
        /// </summary>
        private static async Task TestPlayMultipleDigitsAsync(int number)
        {
            Console.WriteLine($"\n=== Testing multiple digits: {number} ===");

            // 将数字转换为数字列表
            var digits = GetDigits(number);
            Console.WriteLine($"Digits: [{string.Join(", ", digits)}]");

            // 显示每个数字的格式
            var digitFormats = digits.Select(d => $"{d}({s_digitAudioFormats.GetValueOrDefault(d, "?")})");
            Console.WriteLine($"Formats: [{string.Join(", ", digitFormats)}]");

            // 检查是否所有文件都是WAV格式
            bool allWav = digits.All(d => s_digitAudioFormats.GetValueOrDefault(d, "") == "WAV");

            if (allWav)
            {
                // 如果都是WAV格式，使用WAV合并方式
                Console.WriteLine("All files are WAV format - using optimized WAV merging");
                using var combinedStream = CreateCombinedWavStream(digits);

                if (combinedStream == null)
                {
                    Console.WriteLine("Failed to create combined WAV stream");
                    return;
                }

                await PlayAudioStreamAsync(combinedStream, $"number_{number}_WAV_combined");
            }
            else
            {
                // 混合格式，需要逐个播放
                Console.WriteLine("Mixed formats detected - playing sequentially");
                await PlayDigitsSequentiallyAsync(digits, $"number_{number}_mixed");
            }
        }

        /// <summary>
        /// 逐个播放数字（用于混合格式）
        /// </summary>
        private static async Task PlayDigitsSequentiallyAsync(List<int> digits, string description)
        {
            Console.WriteLine($"[{description}] Starting sequential playback of {digits.Count} digits");
            var startTime = DateTime.Now;

            for (int i = 0; i < digits.Count; i++)
            {
                var digit = digits[i];
                if (s_digitAudioCache.ContainsKey(digit))
                {
                    var format = s_digitAudioFormats.GetValueOrDefault(digit, "UNKNOWN");
                    Console.WriteLine($"[{description}] Playing digit {digit} ({i + 1}/{digits.Count}) in {format} format");

                    using var stream = new MemoryStream(s_digitAudioCache[digit]);
                    await PlayAudioStreamAsync(stream, $"{description}_digit_{digit}");

                    // 在数字之间添加短暂间隔（可选）
                    if (i < digits.Count - 1)
                    {
                        await Task.Delay(100); // 100ms间隔
                    }
                }
                else
                {
                    Console.WriteLine($"[{description}] Missing audio data for digit {digit}");
                }
            }

            var endTime = DateTime.Now;
            Console.WriteLine($"[{description}] Sequential playback completed after {(endTime - startTime).TotalSeconds:F2} seconds");
        }

        /// <summary>
        /// 将数字分解为数字列表
        /// </summary>
        private static List<int> GetDigits(int number)
        {
            if (number == 0)
            {
                return [0];
            }

            var digits = new List<int>();
            int temp = Math.Abs(number);

            while (temp > 0)
            {
                digits.Insert(0, temp % 10);
                temp /= 10;
            }

            return digits;
        }

        /// <summary>
        /// 创建合并的WAV音频流（仅适用于WAV格式）
        /// </summary>
        private static Stream? CreateCombinedWavStream(List<int> digits)
        {
            var audioDataList = new List<byte[]>();

            foreach (var digit in digits)
            {
                if (s_digitAudioCache.ContainsKey(digit))
                {
                    // 验证是否为WAV格式
                    if (s_digitAudioFormats.GetValueOrDefault(digit, "") != "WAV")
                    {
                        Console.WriteLine($"Digit {digit} is not in WAV format, cannot use WAV merging");
                        return null;
                    }
                    audioDataList.Add(s_digitAudioCache[digit]);
                }
                else
                {
                    Console.WriteLine($"Missing audio data for digit {digit}");
                    return null;
                }
            }

            return audioDataList.Count == 0 ? null : (Stream)new CombinedWavStream(audioDataList);
        }

        /// <summary>
        /// 使用StreamAudioPlayer播放音频流（支持所有FFmpeg支持的格式）
        /// </summary>
        private static async Task PlayAudioStreamAsync(Stream audioStream, string description)
        {
            MediaFactory.InitializeFFmpeg();

            IStreamAudioPlayer audioPlayer = MediaFactory.CreateStreamAudioPlayer();

            if (!await audioPlayer.CheckFFmpegInstalledAsync())
            {
                Console.WriteLine("Failed to initialize the ffmpeg.");
                return;
            }

            audioPlayer.Volume = 0.5f;

            const int SampleRate = 16000;
            const int Channels = 1;
            const int FrameDurationMs = 60;

            using var waveOut = new WaveOutEvent();
            var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels))
            {
                BufferLength = SampleRate * 2 * Channels * 4
            };
            waveOut.Init(provider);
            waveOut.Play();

            audioPlayer.StateChanged += (s) =>
            {
                Console.WriteLine($"[{description}] StateChanged: {s} at {DateTime.Now:HH:mm:ss.fff}");
            };

            DateTime lastPositionUpdate = DateTime.Now;
            audioPlayer.PositionChanged += (p) =>
            {
                var now = DateTime.Now;
                var timeSinceLastUpdate = (now - lastPositionUpdate).TotalMilliseconds;
                Console.WriteLine($"[{description}] PositionChanged: {p} (Real time: {timeSinceLastUpdate:F0}ms since last update) at {now:HH:mm:ss.fff}");
                lastPositionUpdate = now;
            };

            audioPlayer.OnAudioDataAvailable += (pcmData, isFirst, isLast) =>
            {
                var byteData = new byte[pcmData.Length * 4];
                Buffer.BlockCopy(pcmData, 0, byteData, 0, byteData.Length);

                while (provider.BufferedBytes + byteData.Length > provider.BufferLength)
                {
                    Thread.Sleep(10);
                }
                provider.AddSamples(byteData, 0, byteData.Length);

                if (isFirst)
                {
                    Console.WriteLine($"*** [{description}] First audio frame received - playback started");
                }
                if (isLast)
                {
                    Console.WriteLine($"*** [{description}] Last audio frame received - playback ending (pause/stop/complete)");
                }
            };

            try
            {
                await audioPlayer.LoadAsync(audioStream, SampleRate, Channels, FrameDurationMs);
                Console.WriteLine($"[{description}] Starting playback...");

                var startTime = DateTime.Now;
                await audioPlayer.PlayAsync();
                var endTime = DateTime.Now;

                Console.WriteLine($"[{description}] Playback completed after {(endTime - startTime).TotalSeconds:F2} seconds");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{description}] Error during playback: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 专门用于合并WAV文件的流类，正确处理WAV文件头和数据部分
    /// </summary>
    public class CombinedWavStream : Stream
    {
        private readonly byte[] _combinedWavData;
        private long _position;

        public CombinedWavStream(List<byte[]> wavFiles)
        {
            if (wavFiles == null || wavFiles.Count == 0)
            {
                throw new ArgumentException("WAV files list cannot be null or empty");
            }

            this._combinedWavData = this.CombineWavFiles(wavFiles);
            this._position = 0;
        }

        private byte[] CombineWavFiles(List<byte[]> wavFiles)
        {
            if (wavFiles.Count == 1)
            {
                return wavFiles[0];
            }

            // 获取第一个文件作为基础
            var firstFile = wavFiles[0];

            // 验证是否为有效的WAV文件
            if (firstFile.Length < 44 ||
                !firstFile.Take(4).SequenceEqual(new byte[] { 0x52, 0x49, 0x46, 0x46 })) // "RIFF"
            {
                throw new ArgumentException("First file is not a valid WAV file");
            }

            // 提取第一个文件的头部信息（前44字节）
            var header = new byte[44];
            Array.Copy(firstFile, 0, header, 0, 44);

            // 计算所有文件的PCM数据总长度
            long totalPcmDataLength = 0;
            var pcmDataChunks = new List<byte[]>();

            foreach (var wavFile in wavFiles)
            {
                // 验证WAV文件格式
                if (wavFile.Length < 44 ||
                    !wavFile.Take(4).SequenceEqual(new byte[] { 0x52, 0x49, 0x46, 0x46 }))
                {
                    throw new ArgumentException("One of the files is not a valid WAV file");
                }

                // 提取PCM数据（跳过44字节头部）
                var pcmData = new byte[wavFile.Length - 44];
                Array.Copy(wavFile, 44, pcmData, 0, pcmData.Length);
                pcmDataChunks.Add(pcmData);
                totalPcmDataLength += pcmData.Length;
            }

            // 创建新的WAV文件
            var totalFileSize = 44 + totalPcmDataLength;
            var result = new byte[totalFileSize];

            // 复制头部
            Array.Copy(header, 0, result, 0, 44);

            // 更新文件大小信息（RIFF chunk size = 总文件大小 - 8）
            var riffChunkSize = (uint)(totalFileSize - 8);
            var riffChunkSizeBytes = BitConverter.GetBytes(riffChunkSize);
            Array.Copy(riffChunkSizeBytes, 0, result, 4, 4);

            // 更新数据块大小信息（位置40-43是data chunk size）
            var dataChunkSize = (uint)totalPcmDataLength;
            var dataChunkSizeBytes = BitConverter.GetBytes(dataChunkSize);
            Array.Copy(dataChunkSizeBytes, 0, result, 40, 4);

            // 合并所有PCM数据
            long offset = 44;
            foreach (var pcmData in pcmDataChunks)
            {
                Array.Copy(pcmData, 0, result, offset, pcmData.Length);
                offset += pcmData.Length;
            }

            Console.WriteLine($"Combined WAV: {wavFiles.Count} files, total size: {totalFileSize} bytes, PCM data: {totalPcmDataLength} bytes");

            return result;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => this._combinedWavData.Length;

        public override long Position
        {
            get => this._position;
            set => this.Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (offset + count > buffer.Length)
            {
                throw new ArgumentException("The sum of offset and count is larger than the buffer length.");
            }

            if (this._position >= this._combinedWavData.Length)
            {
                return 0;
            }

            var bytesToRead = (int)Math.Min(count, this._combinedWavData.Length - this._position);
            Array.Copy(this._combinedWavData, this._position, buffer, offset, bytesToRead);
            this._position += bytesToRead;

            return bytesToRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => this._position + offset,
                SeekOrigin.End => this._combinedWavData.Length + offset,
                _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
            };

            if (newPosition < 0)
            {
                newPosition = 0;
            }
            else if (newPosition > this._combinedWavData.Length)
            {
                newPosition = this._combinedWavData.Length;
            }

            this._position = newPosition;
            return this._position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void Flush()
        {
            // 不需要实现，因为这是只读流
        }
    }

    /// <summary>
    /// 原有的合并音频流类（保留以供参考，但不推荐用于WAV文件）
    /// </summary>
    public class CombinedAudioStream(List<byte[]> audioDataList) : Stream
    {
        private readonly List<byte[]> _audioDataList = audioDataList ?? throw new ArgumentNullException(nameof(audioDataList));
        private int _currentStreamIndex = 0;
        private long _currentPosition = 0;
        private readonly long _totalLength = audioDataList.Sum(data => (long)data.Length);

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => this._totalLength;

        public override long Position
        {
            get => this._currentPosition;
            set => this.Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (offset + count > buffer.Length)
            {
                throw new ArgumentException("The sum of offset and count is larger than the buffer length.");
            }

            int totalBytesRead = 0;
            int remainingBytes = count;

            while (remainingBytes > 0 && this._currentStreamIndex < this._audioDataList.Count)
            {
                var currentAudioData = this._audioDataList[this._currentStreamIndex];
                long positionInCurrentStream = this._currentPosition - this.GetStreamStartPosition(this._currentStreamIndex);

                // 如果当前流已经读完，移动到下一个流
                if (positionInCurrentStream >= currentAudioData.Length)
                {
                    this._currentStreamIndex++;
                    continue;
                }

                // 计算当前流中可读取的字节数
                int availableBytesInCurrentStream = (int)(currentAudioData.Length - positionInCurrentStream);
                int bytesToRead = Math.Min(remainingBytes, availableBytesInCurrentStream);

                // 从当前流中读取数据
                Array.Copy(currentAudioData, positionInCurrentStream, buffer, offset + totalBytesRead, bytesToRead);

                totalBytesRead += bytesToRead;
                remainingBytes -= bytesToRead;
                this._currentPosition += bytesToRead;
            }

            return totalBytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => this._currentPosition + offset,
                SeekOrigin.End => this._totalLength + offset,
                _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
            };

            if (newPosition < 0)
            {
                newPosition = 0;
            }
            else if (newPosition > this._totalLength)
            {
                newPosition = this._totalLength;
            }

            this._currentPosition = newPosition;

            // 更新当前流索引
            this._currentStreamIndex = 0;
            long accumulatedLength = 0;

            for (int i = 0; i < this._audioDataList.Count; i++)
            {
                if (newPosition <= accumulatedLength + this._audioDataList[i].Length)
                {
                    this._currentStreamIndex = i;
                    break;
                }
                accumulatedLength += this._audioDataList[i].Length;
            }

            return this._currentPosition;
        }

        private long GetStreamStartPosition(int streamIndex)
        {
            long position = 0;
            for (int i = 0; i < streamIndex && i < this._audioDataList.Count; i++)
            {
                position += this._audioDataList[i].Length;
            }
            return position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void Flush()
        {
            // 不需要实现，因为这是只读流
        }
    }
}
