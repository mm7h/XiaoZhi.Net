using System.Collections.Concurrent;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Dtos;

namespace XiaoZhi.Net.Server.Media.Mixers
{
    /// <summary>
    /// Enhanced audio input stream with improved buffering strategy and automatic frame boundary detection
    /// </summary>
    internal class AudioStreamProcessor : IDisposable
    {
        private readonly AudioType _audioType;
        private readonly ConcurrentQueue<float> _bufferQueue = new();
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

        public AudioType AudioType => _audioType;
        public bool IsFirstFrame => _isFirstFrame;
        public bool IsLastFrame => _isLastFrame;
        public bool IsComplete => _isComplete;
        public int ProcessedFrameCount => _processedFrameCount;
        public int AvailableDataCount => _bufferQueue.Count;
        public bool IsStopping => _stopRequested || _isLastFrame || _streamEnded;

        public AudioStreamProcessor(AudioType audioType, int sampleRate, int channels, int frameDuration, AudioMixerConfig config)
        {
            _audioType = audioType;
            _config = config;
        }

        public void AddData(float[] audioData)
        {
            if (_disposed || audioData == null || audioData.Length == 0 || _stopRequested)
            {
                return;
            }

            lock (_syncLock)
            {
                // If previous logical stream has completed and buffer is empty,
                // reset state so the next add becomes a new segment with first-frame.
                if (_isComplete && _bufferQueue.IsEmpty)
                {
                    _isFirstFrame = false;
                    _isLastFrame = false;
                    _isComplete = false;
                    _stopRequested = false;
                    _processedFrameCount = 0;
                    _hasReceivedData = false;
                    _streamEnded = false;
                    _silentFrameCount = 0;
                }

                // Auto-detect first frame
                if (!_hasReceivedData)
                {
                    _isFirstFrame = true;
                    _hasReceivedData = true;
                    _isComplete = false;
                    _stopRequested = false;
                    _processedFrameCount = 0;
                    _streamEnded = false;
                    _silentFrameCount = 0;
                }

                bool hasSignificantAudio = HasSignificantAudio(audioData);
                if (!hasSignificantAudio)
                {
                    _silentFrameCount++;
                }
                else
                {
                    _silentFrameCount = 0;
                }

                if (_silentFrameCount >= _maxSilentFrames && _hasReceivedData)
                {
                    _isLastFrame = true;
                    _streamEnded = true;
                }

                int currentBufferSize = _bufferQueue.Count;
                int newDataSize = audioData.Length;

                foreach (float sample in audioData)
                {
                    _bufferQueue.Enqueue(sample);
                }
            }
        }

        private bool HasSignificantAudio(float[] audioData)
        {
            const float threshold = 0.001f; // Silence threshold
            for (int i = 0; i < audioData.Length; i++)
            {
                if (Math.Abs(audioData[i]) > threshold)
                {
                    return true;
                }
            }
            return false;
        }

        public bool HasDataForFrame(int frameSampleCount)
        {
            return _bufferQueue.Count >= frameSampleCount;
        }

        public bool HasAnyData()
        {
            return !_bufferQueue.IsEmpty;
        }

        // New overload that reports how many real samples were consumed from the buffer
        public float[]? GetFrameDataWithPartialSupport(int frameSampleCount, out int samplesRead)
        {
            samplesRead = 0;
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
            int targetSamples = frameSampleCount;

            if (availableData < frameSampleCount)
            {
                if (isNewStream)
                {
                    int minRequiredSamples = (int)(frameSampleCount * _config.NewStreamBufferTolerance);
                    if (availableData >= minRequiredSamples)
                    {
                        targetSamples = Math.Min(availableData, frameSampleCount);
                    }
                    else
                    {
                        return null;
                    }
                }
                else if (IsStopping)
                {
                    targetSamples = availableData;
                }
                else
                {
                    return null;
                }
            }

            var frameData = new float[frameSampleCount];

            while (samplesRead < targetSamples && _bufferQueue.TryDequeue(out float sample))
            {
                frameData[samplesRead++] = sample;
            }

            for (int i = samplesRead; i < frameSampleCount; i++)
            {
                frameData[i] = 0.0f;
            }

            if ((IsStopping) && _bufferQueue.IsEmpty)
            {
                _isComplete = true;
            }

            return frameData;
        }

        // Backward-compatible method
        public float[]? GetFrameDataWithPartialSupport(int frameSampleCount)
        {
            var data = GetFrameDataWithPartialSupport(frameSampleCount, out _);
            return data;
        }

        public void MarkFrameProcessed()
        {
            lock (_syncLock)
            {
                _isFirstFrame = false;
                _processedFrameCount++;

                if ((IsStopping) && _bufferQueue.IsEmpty)
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
                _hasReceivedData = false;
                _streamEnded = false;
                _silentFrameCount = 0;
            }
        }

        public void Stop()
        {
            lock (_syncLock)
            {
                _stopRequested = true;
                _isLastFrame = true;
                _streamEnded = true;
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
