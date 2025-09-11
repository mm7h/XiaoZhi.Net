using System.Collections.Concurrent;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Dtos;

namespace XiaoZhi.Net.Server.FFmpeg.Mixers
{
    /// <summary>
    /// Enhanced audio input stream with improved buffering strategy
    /// </summary>
    internal class AudioStreamProcessor : IDisposable
    {
        private readonly AudioType _audioType;
        private readonly ConcurrentQueue<float> _bufferQueue = new();
        private readonly object _syncLock = new();
        private readonly AudioMixerConfig _config;
        private bool _disposed;
        private readonly int _frameSampleCount;
        private volatile bool _isFirstFrame = false;
        private volatile bool _isLastFrame = false;
        private volatile bool _isComplete = false;
        private volatile bool _stopRequested = false;
        private volatile int _processedFrameCount = 0;
        private string? _currentContent = null;

        public AudioType AudioType => _audioType;
        public bool IsFirstFrame => _isFirstFrame;
        public bool IsLastFrame => _isLastFrame;
        public bool IsComplete => _isComplete;
        public int ProcessedFrameCount => _processedFrameCount;
        public int AvailableDataCount => _bufferQueue.Count;
        public string? CurrentContent => _currentContent;

        public AudioStreamProcessor(AudioType audioType, int sampleRate, int channels, int frameDuration, AudioMixerConfig config)
        {
            _audioType = audioType;
            _frameSampleCount = sampleRate * frameDuration / 1000 * channels;
            _config = config;
        }

        public void AddData(float[] audioData, bool isFirst, bool isLast, string? content = null)
        {
            if (_disposed || audioData == null || audioData.Length == 0 || _stopRequested)
            {
                return;
            }

            lock (_syncLock)
            {
                if (isFirst)
                {
                    _isFirstFrame = true;
                    _isComplete = false;
                    _stopRequested = false;
                    _processedFrameCount = 0;
                    _currentContent = content;
                }

                if (isLast)
                {
                    _isLastFrame = true;
                }

                int maxBufferSize = _config.MaxBufferFrames * _frameSampleCount;
                int currentBufferSize = _bufferQueue.Count;
                int newDataSize = audioData.Length;

                int excessSize = (currentBufferSize + newDataSize) - maxBufferSize;
                if (excessSize > 0)
                {
                    int samplesToRemove = Math.Min(excessSize, currentBufferSize);
                    for (int i = 0; i < samplesToRemove; i++)
                    {
                        _bufferQueue.TryDequeue(out _);
                    }
                }

                foreach (float sample in audioData)
                {
                    _bufferQueue.Enqueue(sample);
                }
            }
        }

        public bool HasDataForFrame(int frameSampleCount)
        {
            return _bufferQueue.Count >= frameSampleCount;
        }

        public bool HasAnyData()
        {
            return !_bufferQueue.IsEmpty;
        }

        public float[]? GetFrameDataWithPartialSupport(int frameSampleCount)
        {
            if (_disposed || frameSampleCount <= 0)
            {
                return null;
            }

            int availableData = _bufferQueue.Count;
            if (availableData == 0)
            {
                return null;
            }

            bool isNewStream = _processedFrameCount < 3;
            int samplesToRead = frameSampleCount;

            if (isNewStream && availableData < frameSampleCount)
            {
                int minRequiredSamples = (int)(frameSampleCount * _config.NewStreamBufferTolerance);
                if (availableData >= minRequiredSamples)
                {
                    samplesToRead = Math.Min(availableData, frameSampleCount);
                }
                else
                {
                    return null;
                }
            }
            else if (availableData < frameSampleCount)
            {
                return null;
            }

            var frameData = new float[frameSampleCount];
            int samplesRead = 0;

            while (samplesRead < samplesToRead && _bufferQueue.TryDequeue(out float sample))
            {
                frameData[samplesRead++] = sample;
            }

            for (int i = samplesRead; i < frameSampleCount; i++)
            {
                frameData[i] = 0.0f;
            }

            if (_isLastFrame && _bufferQueue.IsEmpty)
            {
                _isComplete = true;
            }

            return frameData;
        }

        public void MarkFrameProcessed()
        {
            lock (_syncLock)
            {
                _isFirstFrame = false;
                _processedFrameCount++;

                if ((_isLastFrame || _stopRequested) && _bufferQueue.IsEmpty)
                {
                    _isComplete = true;
                }
            }
        }

        public void ClearBuffer()
        {
            lock (_syncLock)
            {
                while (_bufferQueue.TryDequeue(out _)) { }
                _isFirstFrame = false;
                _isLastFrame = false;
                _isComplete = false;
                _stopRequested = false;
                _processedFrameCount = 0;
                _currentContent = null;
            }
        }

        public void Stop()
        {
            lock (_syncLock)
            {
                _stopRequested = true;
                _isLastFrame = true;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            ClearBuffer();
            _disposed = true;
        }
    }
}
