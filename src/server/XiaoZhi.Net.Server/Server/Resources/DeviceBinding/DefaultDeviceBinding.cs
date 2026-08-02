using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Resources.DeviceBinding
{
    internal class DefaultDeviceBinding : BaseResource<DefaultDeviceBinding, DeviceBindSetting>, IDeviceBinding
    {
        private const string BindCodePrompt = "BindCodePrompt";
        private const string BindNotFound = "BindNotFound";

        private readonly IDictionary<string, byte[]> _audioFilesCache;
        private bool _isAllWavFormat = true;

        public DefaultDeviceBinding(ILogger<DefaultDeviceBinding> logger) : base(logger)
        {
            this._audioFilesCache = new Dictionary<string, byte[]>();
        }

        public override string ResourceName => "DeviceBinding";

        public override bool Load(DeviceBindSetting settings)
        {
            try
            {
                string bindCodePromptFilePath = Path.Combine(Environment.CurrentDirectory, settings.BindCodePromptFilePath);
                if (File.Exists(bindCodePromptFilePath))
                {
                    byte[] fileData = File.ReadAllBytes(bindCodePromptFilePath);
                    this._audioFilesCache[BindCodePrompt] = fileData;
                }
                else
                {
                    this.Logger.LogError(Lang.DefaultDeviceBinding_Load_BindCodePromptNotExist, bindCodePromptFilePath);
                    return false;
                }

                string bindNotFoundFilePath = Path.Combine(Environment.CurrentDirectory, settings.BindNotFoundFilePath);
                if (File.Exists(bindNotFoundFilePath))
                {
                    byte[] fileData = File.ReadAllBytes(bindNotFoundFilePath);
                    this._audioFilesCache[BindNotFound] = fileData;
                }
                else
                {
                    this.Logger.LogError(Lang.DefaultDeviceBinding_Load_BindNotFoundNotExist, bindNotFoundFilePath);
                    return false;
                }

                string[] digitFiles = Directory.GetFiles(Path.Combine(Environment.CurrentDirectory, settings.BindCodeDigitFolderPath));
                if (digitFiles.Length != 10)
                {
                    this.Logger.LogError(Lang.DefaultDeviceBinding_Load_DigitFilesCountError);
                    return false;
                }
                foreach (string digitFile in digitFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(digitFile);

                    this._isAllWavFormat = this._isAllWavFormat && Path.GetExtension(digitFile).Equals(".wav", StringComparison.OrdinalIgnoreCase);

                    if (int.TryParse(fileName, out int digit) && digit >= 0 && digit <= 9)
                    {
                        byte[] fileData = File.ReadAllBytes(digitFile);
                        this._audioFilesCache[digit.ToString()] = fileData;
                    }
                    else
                    {
                        this.Logger.LogWarning(Lang.DefaultDeviceBinding_Load_InvalidDigitFile, fileName);
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.DefaultDeviceBinding_Load_InvalidResourceLoading, this.ResourceName);
                return false;
            }
        }

        public Stream? GetDeviceNotFoundAudioStream()
        {
            if (this._audioFilesCache.TryGetValue(BindNotFound, out byte[]? audioData))
            {
                return new CombinedAudioStream([audioData]);
            }
            else
            {
                this.Logger.LogError(Lang.DefaultDeviceBinding_GetDeviceNotFoundAudioStream_NotLoaded);
                return null;
            }
        }

        public Stream? GetDeviceBindCodeAudioStream(string bindCode)
        {
            if (string.IsNullOrWhiteSpace(bindCode) || bindCode.Length != 6 || !bindCode.All(char.IsDigit))
            {
                this.Logger.LogError(Lang.DefaultDeviceBinding_GetDeviceBindCodeAudioStream_InvalidBindCode, bindCode);
                return null;
            }
            List<byte[]> audioDataList = new();
            if (this._audioFilesCache.TryGetValue(BindCodePrompt, out byte[]? promptData))
            {
                audioDataList.Add(promptData);
            }
            else
            {
                this.Logger.LogError(Lang.DefaultDeviceBinding_GetDeviceBindCodeAudioStream_PromptNotLoaded);
                return null;
            }
            foreach (char digit in bindCode)
            {
                if (this._audioFilesCache.TryGetValue(digit.ToString(), out byte[]? digitData))
                {
                    audioDataList.Add(digitData);
                }
                else
                {
                    this.Logger.LogError(Lang.DefaultDeviceBinding_GetDeviceBindCodeAudioStream_DigitNotLoaded, digit);
                    return null;
                }
            }
            if (this._isAllWavFormat)
            {
                return new CombinedWavStream(audioDataList);
            }
            else
            {
                return new CombinedAudioStream(audioDataList);
            }
        }

        public override void Dispose()
        {
            this._audioFilesCache.Clear();
        }
    }

    /// <summary>
    /// 用于合并WAV文件的流类，正确处理WAV文件头和数据部分
    /// </summary>
    file class CombinedWavStream : Stream
    {
        private readonly byte[] _combinedWavData;
        private long _position;

        public CombinedWavStream(List<byte[]> wavFiles)
        {
            if (wavFiles == null || wavFiles.Count == 0)
            {
                throw new ArgumentException(Lang.DefaultDeviceBinding_CombinedStream_ListEmpty);
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
                throw new ArgumentException(Lang.DefaultDeviceBinding_CombinedStream_FirstFileInvalid);
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
                    throw new ArgumentException(Lang.DefaultDeviceBinding_CombinedStream_FileInvalid);
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
                throw new ArgumentException(Lang.DefaultDeviceBinding_CombinedStream_BufferOverflow);
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
                _ => throw new ArgumentException(Lang.DefaultDeviceBinding_CombinedStream_InvalidOrigin, nameof(origin))
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
    file class CombinedAudioStream : Stream
    {
        private readonly List<byte[]> _audioDataList;
        private int _currentStreamIndex;
        private long _currentPosition;
        private readonly long _totalLength;

        public CombinedAudioStream(List<byte[]> audioDataList)
        {
            this._audioDataList = audioDataList ?? throw new ArgumentNullException(nameof(audioDataList));
            this._currentStreamIndex = 0;
            this._currentPosition = 0;
            this._totalLength = audioDataList.Sum(data => (long)data.Length);
        }

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
                throw new ArgumentException(Lang.DefaultDeviceBinding_CombinedStream_BufferOverflow);
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
                _ => throw new ArgumentException(Lang.DefaultDeviceBinding_CombinedStream_InvalidOrigin, nameof(origin))
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
