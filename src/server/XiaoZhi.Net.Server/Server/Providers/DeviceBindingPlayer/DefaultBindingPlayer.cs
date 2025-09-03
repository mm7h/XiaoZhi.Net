using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.AudioPlayer.Abstractions;
using XiaoZhi.Net.Server.AudioPlayer.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.DeviceBindingPlayer
{
    internal class DefaultBindingPlayer : BaseProvider<DefaultBindingPlayer, DeviceBindSetting>, IDeviceBindingPlayer
    {
        private const string BIND_CODE_PROMPT = "BindCodePrompt";
        private const string BIND_NOT_FOUND = "BindNotFound";
        private readonly IStreamAudioPlayer _audioPlayer;
        private readonly IDictionary<string, byte[]> _digitAudioCache;

        private bool _isAllWavFormat = false;

        public DefaultBindingPlayer(IStreamAudioPlayer audioPlayer, XiaoZhiConfig config, ILogger<DefaultBindingPlayer> logger) : base(logger)
        {
            this._audioPlayer = audioPlayer;
            this._audioPlayer.OnAudioDataAvailable += this.OnAudioData;
            this._audioPlayer.StateChanged += this.OnPlayStateChanged;
            this._digitAudioCache = new Dictionary<string, byte[]>();
        }
        public override string ProviderType => "audio player";

        public override string ModelName => nameof(DefaultBindingPlayer);

        public event Action<PlaybackState>? OnPlayStateChanged;
        public event Action<float[]>? OnAudioData;

        public override bool Build(DeviceBindSetting settings)
        {
            try
            {
                if (!_audioPlayer.CheckFFmpegInstalled())
                {
                    Logger.LogError("Failed to initialize FFmpeg, please check your the ffmpeg path configuration.");
                    return false;
                }

                string bindCodePromptFilePath = Path.Combine(Environment.CurrentDirectory, settings.BindCodePromptFilePath);
                if (File.Exists(bindCodePromptFilePath))
                {
                    byte[] fileData = File.ReadAllBytes(bindCodePromptFilePath);
                    _digitAudioCache[BIND_CODE_PROMPT] = fileData;
                }
                else
                {
                    Logger.LogError("The bind code prompt file does not exist: {filePath}", bindCodePromptFilePath);
                    return false;
                }

                string bindNotFoundFilePath = Path.Combine(Environment.CurrentDirectory, settings.BindNotFoundFilePath);
                if (File.Exists(bindNotFoundFilePath))
                {
                    byte[] fileData = File.ReadAllBytes(bindNotFoundFilePath);
                    _digitAudioCache[BIND_NOT_FOUND] = fileData;
                }
                else
                {
                    Logger.LogError("The bind not found file does not exist: {filePath}", bindNotFoundFilePath);
                    return false;
                }

                string[] digitFiles = Directory.GetFiles(Path.Combine(Environment.CurrentDirectory, settings.BindCodeDigitFolderPath));
                if (digitFiles.Length != 10)
                {
                    Logger.LogError("The digit files folder must contain exactly 10 files for digits 0-9.");
                    return false;
                }
                foreach (string digitFile in digitFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(digitFile);

                    _isAllWavFormat = _isAllWavFormat && Path.GetExtension(digitFile).Equals(".wav", StringComparison.OrdinalIgnoreCase);

                    if (int.TryParse(fileName, out int digit) && digit >= 0 && digit <= 9)
                    {
                        byte[] fileData = File.ReadAllBytes(digitFile);
                        _digitAudioCache[digit.ToString()] = fileData;
                    }
                    else
                    {
                        Logger.LogWarning("Invalid digit file: {fileName}", fileName);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", ProviderType, ModelName);
                return false;
            }
        }


        public async Task PlayBindCodeAsync(string bindCode, AudioSetting sessionAudioSetting)
        {
            if (string.IsNullOrEmpty(bindCode))
            {
                Logger.LogError("Bind code is null or empty.");
                return;
            }
            byte[] promptAudio = _digitAudioCache[BIND_CODE_PROMPT];

            using var promptStream = new CombinedAudioStream([promptAudio]);
            await _audioPlayer.LoadAsync(promptStream, sessionAudioSetting.SampleRate, sessionAudioSetting.Channels, sessionAudioSetting.FrameDuration);
            _audioPlayer.Play(true);

            List<byte[]> audioSegments = new List<byte[]>();
            // 添加每个数字的音频
            foreach (char digit in bindCode)
            {
                if (_digitAudioCache.TryGetValue(digit.ToString(), out byte[]? digitAudio))
                {
                    audioSegments.Add(digitAudio);
                }
                else
                {
                    Logger.LogWarning("Audio for digit '{digit}' not found in cache.", digit);
                    return;
                }
            }


            if (_isAllWavFormat)
            {
                // 合并音频段
                using var combinedStream = new CombinedWavStream(audioSegments);
                // 播放合并后的音频
                await _audioPlayer.LoadAsync(combinedStream, sessionAudioSetting.SampleRate, sessionAudioSetting.Channels, sessionAudioSetting.FrameDuration);
                _audioPlayer.Play(true);
            }
            else
            {
                using var combinedStream = new CombinedAudioStream(audioSegments);
                // 播放合并后的音频
                await _audioPlayer.LoadAsync(combinedStream, sessionAudioSetting.SampleRate, sessionAudioSetting.Channels, sessionAudioSetting.FrameDuration);
                _audioPlayer.Play(true);
            }

        }

        public async Task PlayNotFoundAsync(AudioSetting sessionAudioSetting)
        {
            byte[] notFoundAudio = _digitAudioCache[BIND_NOT_FOUND];
            using var notFoundStream = new CombinedAudioStream([notFoundAudio]);
            await _audioPlayer.LoadAsync(notFoundStream, sessionAudioSetting.SampleRate, sessionAudioSetting.Channels, sessionAudioSetting.FrameDuration);
            _audioPlayer.Play(true);
        }

        public override void Dispose()
        {
            _digitAudioCache.Clear();
            _audioPlayer.Dispose();
        }
    }

    /// <summary>
    /// 专门用于合并WAV文件的流类，正确处理WAV文件头和数据部分
    /// </summary>
    file class CombinedWavStream : Stream
    {
        private readonly byte[] _combinedWavData;
        private long _position;

        public CombinedWavStream(List<byte[]> wavFiles)
        {
            if (wavFiles == null || wavFiles.Count == 0)
                throw new ArgumentException("WAV files list cannot be null or empty");

            _combinedWavData = CombineWavFiles(wavFiles);
            _position = 0;
        }

        private byte[] CombineWavFiles(List<byte[]> wavFiles)
        {
            if (wavFiles.Count == 1)
                return wavFiles[0];

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
        public override long Length => _combinedWavData.Length;

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (offset + count > buffer.Length)
                throw new ArgumentException("The sum of offset and count is larger than the buffer length.");

            if (_position >= _combinedWavData.Length)
                return 0;

            var bytesToRead = (int)Math.Min(count, _combinedWavData.Length - _position);
            Array.Copy(_combinedWavData, _position, buffer, offset, bytesToRead);
            _position += bytesToRead;

            return bytesToRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _combinedWavData.Length + offset,
                _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
            };

            if (newPosition < 0)
                newPosition = 0;
            else if (newPosition > _combinedWavData.Length)
                newPosition = _combinedWavData.Length;

            _position = newPosition;
            return _position;
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
    file class CombinedAudioStream : Stream
    {
        private readonly List<byte[]> _audioDataList;
        private int _currentStreamIndex;
        private long _currentPosition;
        private long _totalLength;

        public CombinedAudioStream(List<byte[]> audioDataList)
        {
            _audioDataList = audioDataList ?? throw new ArgumentNullException(nameof(audioDataList));
            _currentStreamIndex = 0;
            _currentPosition = 0;
            _totalLength = audioDataList.Sum(data => (long)data.Length);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _totalLength;

        public override long Position
        {
            get => _currentPosition;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (offset + count > buffer.Length)
                throw new ArgumentException("The sum of offset and count is larger than the buffer length.");

            int totalBytesRead = 0;
            int remainingBytes = count;

            while (remainingBytes > 0 && _currentStreamIndex < _audioDataList.Count)
            {
                var currentAudioData = _audioDataList[_currentStreamIndex];
                long positionInCurrentStream = _currentPosition - GetStreamStartPosition(_currentStreamIndex);

                // 如果当前流已经读完，移动到下一个流
                if (positionInCurrentStream >= currentAudioData.Length)
                {
                    _currentStreamIndex++;
                    continue;
                }

                // 计算当前流中可读取的字节数
                int availableBytesInCurrentStream = (int)(currentAudioData.Length - positionInCurrentStream);
                int bytesToRead = Math.Min(remainingBytes, availableBytesInCurrentStream);

                // 从当前流中读取数据
                Array.Copy(currentAudioData, positionInCurrentStream, buffer, offset + totalBytesRead, bytesToRead);

                totalBytesRead += bytesToRead;
                remainingBytes -= bytesToRead;
                _currentPosition += bytesToRead;
            }

            return totalBytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _currentPosition + offset,
                SeekOrigin.End => _totalLength + offset,
                _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
            };

            if (newPosition < 0)
                newPosition = 0;
            else if (newPosition > _totalLength)
                newPosition = _totalLength;

            _currentPosition = newPosition;

            // 更新当前流索引
            _currentStreamIndex = 0;
            long accumulatedLength = 0;

            for (int i = 0; i < _audioDataList.Count; i++)
            {
                if (newPosition <= accumulatedLength + _audioDataList[i].Length)
                {
                    _currentStreamIndex = i;
                    break;
                }
                accumulatedLength += _audioDataList[i].Length;
            }

            return _currentPosition;
        }

        private long GetStreamStartPosition(int streamIndex)
        {
            long position = 0;
            for (int i = 0; i < streamIndex && i < _audioDataList.Count; i++)
            {
                position += _audioDataList[i].Length;
            }
            return position;
        }

        public override void SetLength(long value)
        {
            return;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            return;
        }

        public override void Flush()
        {
            return;
        }
    }
}
