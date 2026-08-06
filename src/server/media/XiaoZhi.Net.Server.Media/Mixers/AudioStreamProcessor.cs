using System.Collections.Concurrent;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Dtos;

namespace XiaoZhi.Net.Server.Media.Mixers
{
#pragma warning disable IDE0290 // 保持构造函数签名，避免未读取参数的编译器警告。
    /// <summary>
    /// Enhanced audio input stream with improved buffering strategy and automatic frame boundary detection
    /// </summary>
    internal class AudioStreamProcessor : IDisposable
    {
        private readonly ConcurrentQueue<float> _bufferQueue = new();
        private readonly ConcurrentQueue<(int Count, string? SentenceId)> _metaQueue = new();
        private readonly object _syncLock = new();
        private readonly AudioMixerConfig _config;
        private bool _disposed;

        // Internal frame boundary tracking
        private volatile bool _isFirstFrame = false;
        private volatile bool _isLastFrame = false;
        private volatile bool _isComplete = false;
        private volatile bool _stopRequested = false;
        private volatile int _processedFrameCount = 0;
        private volatile bool _hasReceivedData = false;
        private volatile bool _streamEnded = false;
        private volatile int _silentFrameCount = 0;
        private readonly int _maxSilentFrames = 10; // Consider stream ended after 10 silent frames

        // Meta tracking for consumer
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
                // If previous logical stream has completed and buffer is empty,
                // reset state so the next add becomes a new segment with first-frame.
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

                    // Reset meta state
                    while (this._metaQueue.TryDequeue(out _)) { }
                    this._currentMetaRemaining = 0;
                    this._currentMetaId = null;
                }

                // Auto-detect first frame
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

        // New overload that reports how many real samples were consumed from the buffer
        public float[]? GetFrameDataWithPartialSupport(int frameSampleCount, out int samplesRead, out string? sentenceId)
        {
            samplesRead = 0;
            sentenceId = null;
            if (this._disposed || frameSampleCount <= 0)
            {
                return null;
            }

            // Check for pending zero-length meta at the very beginning
            if (this._currentMetaRemaining <= 0 && this._metaQueue.TryPeek(out var meta) && meta.Count == 0)
            {
                this._metaQueue.TryDequeue(out _);
                sentenceId = meta.SentenceId;

                // Check if stream should be marked as complete after consuming this meta
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
                // Check meta state before dequeuing sample
                if (this._currentMetaRemaining <= 0)
                {
                    if (this._metaQueue.TryPeek(out var nextMeta))
                    {
                        if (nextMeta.Count == 0)
                        {
                            // Zero-length meta found.
                            if (samplesRead > 0)
                            {
                                // We have data in this frame already. Stop here so next call picks up the zero-length meta.
                                break;
                            }
                            else
                            {
                                // Start of frame. Consume this meta and return empty frame.
                                this._metaQueue.TryDequeue(out _);
                                sentenceId = nextMeta.SentenceId;
                                return [];
                            }
                        }

                        // Normal meta. Consume it.
                        this._metaQueue.TryDequeue(out _);
                        this._currentMetaRemaining = nextMeta.Count;
                        this._currentMetaId = nextMeta.SentenceId;
                    }
                    else
                    {
                        // No meta? Should match buffer.
                        // If buffer has data but no meta, use null ID.
                        this._currentMetaRemaining = int.MaxValue;
                        this._currentMetaId = null;
                    }
                }

                if (!idSet)
                {
                    sentenceId = this._currentMetaId;
                    idSet = true;
                }

                // Now dequeue sample
                if (this._bufferQueue.TryDequeue(out float sample))
                {
                    frameData[samplesRead++] = sample;
                    this._currentMetaRemaining--;
                }
                else
                {
                    // Should not happen if we checked availableData, but for safety
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

        // Backward-compatible method
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
