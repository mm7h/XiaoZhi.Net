using System.Collections.Concurrent;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Dtos;

namespace XiaoZhi.Net.Server.Media.Mixers
{
#pragma warning disable IDE0290 // 保持构造函数签名，避免未读取参数的编译器警告。
    /// <summary>
    /// 采用改进缓冲策略并支持自动帧边界检测的增强音频输入流。
    /// </summary>
    internal class AudioStreamProcessor : IDisposable
    {
        private readonly ConcurrentQueue<float> _bufferQueue = new();
        private readonly ConcurrentQueue<(int Count, string? SentenceId)> _metaQueue = new();
        private readonly object _syncLock = new();
        private readonly AudioMixerConfig _config;
        private bool _disposed;

        // 内部帧边界跟踪。
        private volatile bool _isFirstFrame = false;
        private volatile bool _isLastFrame = false;
        private volatile bool _isComplete = false;
        private volatile bool _stopRequested = false;
        private volatile int _processedFrameCount = 0;
        private volatile bool _hasReceivedData = false;
        private volatile bool _streamEnded = false;
        private volatile int _silentFrameCount = 0;
        private readonly int _maxSilentFrames = 10; // Consider stream ended after 10 silent frames

        // 为消费者跟踪元数据。
        private int _currentMetaRemaining = 0;
        private string? _currentMetaId = null;


        public AudioType AudioType { get; }
        public bool IsFirstFrame => this._isFirstFrame;
        public bool IsLastFrame => this._isLastFrame;
        public bool IsComplete => this._bufferQueue.IsEmpty && this._metaQueue.IsEmpty && (this._isComplete || this.IsStopping);
        public int ProcessedFrameCount => this._processedFrameCount;
        public int AvailableDataCount => this._bufferQueue.Count;
        public bool IsStopping => this._stopRequested || this._isLastFrame || this._streamEnded;

        public AudioStreamProcessor(AudioType audioType, int _, int __, int ___, AudioMixerConfig config)
        {
            this.AudioType = audioType;
            this._config = config;
        }

        public void AddData(float[] audioData, string? sentenceId = null)
        {
            if (this._disposed || this._stopRequested || audioData == null)
            {
                return;
            }

            if (audioData.Length == 0 && string.IsNullOrEmpty(sentenceId))
            {
                return;
            }


            lock (this._syncLock)
            {
                // 如果上一个逻辑流已完成且缓冲区为空，
                // 则重置状态，使下一次添加成为带有首帧的新分段。
                if (this._isComplete && this._bufferQueue.IsEmpty)
                {
                    this._isFirstFrame = false;
                    this._isLastFrame = false;
                    this._isComplete = false;
                    this._stopRequested = false;
                    this._processedFrameCount = 0;
                    this._hasReceivedData = false;
                    this._streamEnded = false;
                    this._silentFrameCount = 0;

                    // 重置元数据状态。
                    while (this._metaQueue.TryDequeue(out _)) { }
                    this._currentMetaRemaining = 0;
                    this._currentMetaId = null;
                }

                // 自动检测首帧。
                if (!this._hasReceivedData)
                {
                    this._isFirstFrame = true;
                    this._hasReceivedData = true;
                    this._isComplete = false;
                    this._stopRequested = false;
                    this._processedFrameCount = 0;
                    this._streamEnded = false;
                    this._silentFrameCount = 0;
                }

                int dataLength = audioData.Length;
                bool hasSignificantAudio = false;

                if (dataLength > 0)
                {
                    hasSignificantAudio = this.HasSignificantAudio(audioData);
                    foreach (float sample in audioData)
                    {
                        this._bufferQueue.Enqueue(sample);
                    }

                    if (!hasSignificantAudio)
                    {
                        this._silentFrameCount++;
                    }
                    else
                    {
                        this._silentFrameCount = 0;
                    }
                }

                if (this._silentFrameCount >= this._maxSilentFrames && this._hasReceivedData)
                {
                    this._isLastFrame = true;
                    this._streamEnded = true;
                }

                this._metaQueue.Enqueue((dataLength, sentenceId));
            }
        }

        private bool HasSignificantAudio(float[] audioData)
        {
            const float Threshold = 0.001f; // Silence threshold
            for (int i = 0; i < audioData.Length; i++)
            {
                if (Math.Abs(audioData[i]) > Threshold)
                {
                    return true;
                }
            }
            return false;
        }

        public bool HasDataForFrame(int frameSampleCount)
        {
            return this._bufferQueue.Count >= frameSampleCount;
        }

        public bool HasAnyData()
        {
            return !this._bufferQueue.IsEmpty || !this._metaQueue.IsEmpty;
        }

        // 新重载会报告从缓冲区消耗的实际采样数量。
        public float[]? GetFrameDataWithPartialSupport(int frameSampleCount, out int samplesRead, out string? sentenceId)
        {
            samplesRead = 0;
            sentenceId = null;
            if (this._disposed || frameSampleCount <= 0)
            {
                return null;
            }

            // 检查开头是否存在待处理的零长度元数据。
            if (this._currentMetaRemaining <= 0 && this._metaQueue.TryPeek(out var meta) && meta.Count == 0)
            {
                this._metaQueue.TryDequeue(out _);
                sentenceId = meta.SentenceId;

                // 检查消耗此元数据后是否应将流标记为完成。
                if (this.IsStopping && this._bufferQueue.IsEmpty && this._metaQueue.IsEmpty)
                {
                    this._isComplete = true;
                }

                return [];
            }

            int availableData = this._bufferQueue.Count;
            if (availableData == 0)
            {
                return null;
            }

            bool isNewStream = this._processedFrameCount < 3;
            int targetSamples = frameSampleCount;

            if (availableData < frameSampleCount)
            {
                if (isNewStream)
                {
                    int minRequiredSamples = (int)(frameSampleCount * this._config.NewStreamBufferTolerance);
                    if (availableData >= minRequiredSamples)
                    {
                        targetSamples = Math.Min(availableData, frameSampleCount);
                    }
                    else
                    {
                        return null;
                    }
                }
                else if (this.IsStopping)
                {
                    targetSamples = availableData;
                }
                else
                {
                    return null;
                }
            }

            var frameData = new float[frameSampleCount];
            bool idSet = false;

            while (samplesRead < targetSamples)
            {
                // 在出队采样前检查元数据状态。
                if (this._currentMetaRemaining <= 0)
                {
                    if (this._metaQueue.TryPeek(out var nextMeta))
                    {
                        if (nextMeta.Count == 0)
                        {
                            // 找到零长度元数据。
                            if (samplesRead > 0)
                            {
                                // 当前帧已有数据。此处停止，让下次调用读取该零长度元数据。
                                break;
                            }
                            else
                            {
                                // 帧起始位置。消耗此元数据并返回空帧。
                                this._metaQueue.TryDequeue(out _);
                                sentenceId = nextMeta.SentenceId;
                                return [];
                            }
                        }

                        // 常规元数据。消耗它。
                        this._metaQueue.TryDequeue(out _);
                        this._currentMetaRemaining = nextMeta.Count;
                        this._currentMetaId = nextMeta.SentenceId;
                    }
                    else
                    {
                        // 没有元数据？应与缓冲区匹配。
                        // 如果缓冲区有数据但没有元数据，则使用 null ID。
                        this._currentMetaRemaining = int.MaxValue;
                        this._currentMetaId = null;
                    }
                }

                if (!idSet)
                {
                    sentenceId = this._currentMetaId;
                    idSet = true;
                }

                // 现在出队采样数据。
                if (this._bufferQueue.TryDequeue(out float sample))
                {
                    frameData[samplesRead++] = sample;
                    this._currentMetaRemaining--;
                }
                else
                {
                    // 如果已检查 availableData 则不应发生，但为安全起见仍作处理。
                    break;
                }
            }

            for (int i = samplesRead; i < frameSampleCount; i++)
            {
                frameData[i] = 0.0f;
            }

            if (this.IsStopping && this._bufferQueue.IsEmpty && this._metaQueue.IsEmpty)
            {
                this._isComplete = true;
            }

            return frameData;
        }

        // 向后兼容的方法。
        public float[]? GetFrameDataWithPartialSupport(int frameSampleCount)
        {
            var data = this.GetFrameDataWithPartialSupport(frameSampleCount, out _, out _);
            return data;
        }

        public float[]? GetFrameDataWithPartialSupport(int frameSampleCount, out int samplesRead)
        {
            var data = this.GetFrameDataWithPartialSupport(frameSampleCount, out samplesRead, out _);
            return data;
        }

        public void MarkFrameProcessed()
        {
            lock (this._syncLock)
            {
                this._isFirstFrame = false;
                this._processedFrameCount++;

                if (this.IsStopping && this._bufferQueue.IsEmpty && this._metaQueue.IsEmpty)
                {
                    this._isComplete = true;
                }
            }
        }

        public void ClearBuffer()
        {
            lock (this._syncLock)
            {
                while (this._bufferQueue.TryDequeue(out _)) { }
                while (this._metaQueue.TryDequeue(out _)) { }
                this._currentMetaRemaining = 0;
                this._currentMetaId = null;

                this._isFirstFrame = false;
                this._isLastFrame = false;
                this._isComplete = false;
                this._stopRequested = false;
                this._processedFrameCount = 0;
                this._hasReceivedData = false;
                this._streamEnded = false;
                this._silentFrameCount = 0;
            }
        }

        public void Stop()
        {
            lock (this._syncLock)
            {
                this._stopRequested = true;
                this._isLastFrame = true;
                this._streamEnded = true;
            }
        }

        public void Dispose()
        {
            if (this._disposed)
            {
                return;
            }

            this.ClearBuffer();
            this._disposed = true;
        }
    }
#pragma warning restore IDE0290
}
