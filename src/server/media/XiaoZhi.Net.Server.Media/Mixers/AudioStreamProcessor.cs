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


        public AudioType AudioType => _audioType;
        public bool IsFirstFrame => _isFirstFrame;
        public bool IsLastFrame => _isLastFrame;
        public bool IsComplete => _bufferQueue.IsEmpty && _metaQueue.IsEmpty && (_isComplete || IsStopping);
        public int ProcessedFrameCount => _processedFrameCount;
        public int AvailableDataCount => _bufferQueue.Count;
        public bool IsStopping => _stopRequested || _isLastFrame || _streamEnded;

        public AudioStreamProcessor(AudioType audioType, int sampleRate, int channels, int frameDuration, AudioMixerConfig config)
        {
            _audioType = audioType;
            _config = config;
        }

        public void AddData(float[] audioData, string? sentenceId = null)
        {
            if (_disposed || _stopRequested || audioData == null)
            {
                return;
            }

            if (audioData.Length == 0 && string.IsNullOrEmpty(sentenceId))
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
                    
                    // Reset meta state
                    while (_metaQueue.TryDequeue(out _)) { }
                    _currentMetaRemaining = 0;
                    _currentMetaId = null;
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

                int dataLength = audioData.Length;
                bool hasSignificantAudio = false;
                
                if (dataLength > 0)
                {
                    hasSignificantAudio = HasSignificantAudio(audioData);
                    foreach (float sample in audioData)
                    {
                        _bufferQueue.Enqueue(sample);
                    }

                    if (!hasSignificantAudio)
                    {
                        _silentFrameCount++;
                    }
                    else
                    {
                        _silentFrameCount = 0;
                    }
                }

                if (_silentFrameCount >= _maxSilentFrames && _hasReceivedData)
                {
                    _isLastFrame = true;
                    _streamEnded = true;
                }

                _metaQueue.Enqueue((dataLength, sentenceId));
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
            return !_bufferQueue.IsEmpty || !_metaQueue.IsEmpty;
        }

        // New overload that reports how many real samples were consumed from the buffer
        public float[]? GetFrameDataWithPartialSupport(int frameSampleCount, out int samplesRead, out string? sentenceId)
        {
            samplesRead = 0;
            sentenceId = null;
            if (_disposed || frameSampleCount <= 0)
            {
                return null;
            }

            // Check for pending zero-length meta at the very beginning
            if (_currentMetaRemaining <= 0 && _metaQueue.TryPeek(out var meta) && meta.Count == 0)
            {
                _metaQueue.TryDequeue(out _);
                sentenceId = meta.SentenceId;
                
                // Check if stream should be marked as complete after consuming this meta
                if (IsStopping && _bufferQueue.IsEmpty && _metaQueue.IsEmpty)
                {
                    _isComplete = true;
                }
                
                return Array.Empty<float>();
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
            bool idSet = false;

            while (samplesRead < targetSamples)
            {
                // Check meta state before dequeuing sample
                if (_currentMetaRemaining <= 0)
                {
                    if (_metaQueue.TryPeek(out var nextMeta))
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
                                _metaQueue.TryDequeue(out _);
                                sentenceId = nextMeta.SentenceId;
                                return Array.Empty<float>();
                            }
                        }

                        // Normal meta. Consume it.
                        _metaQueue.TryDequeue(out _);
                        _currentMetaRemaining = nextMeta.Count;
                        _currentMetaId = nextMeta.SentenceId;
                    }
                    else
                    {
                        // No meta? Should match buffer.
                        // If buffer has data but no meta, use null ID.
                        _currentMetaRemaining = int.MaxValue;
                        _currentMetaId = null;
                    }
                }

                if (!idSet)
                {
                    sentenceId = _currentMetaId;
                    idSet = true;
                }

                // Now dequeue sample
                if (_bufferQueue.TryDequeue(out float sample))
                {
                    frameData[samplesRead++] = sample;
                    _currentMetaRemaining--;
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

            if (IsStopping && _bufferQueue.IsEmpty && _metaQueue.IsEmpty)
            {
                _isComplete = true;
            }

            return frameData;
        }

        // Backward-compatible method
        public float[]? GetFrameDataWithPartialSupport(int frameSampleCount)
        {
            var data = GetFrameDataWithPartialSupport(frameSampleCount, out _, out _);
            return data;
        }
        
        public float[]? GetFrameDataWithPartialSupport(int frameSampleCount, out int samplesRead)
        {
            var data = GetFrameDataWithPartialSupport(frameSampleCount, out samplesRead, out _);
            return data;
        }

        public void MarkFrameProcessed()
        {
            lock (_syncLock)
            {
                _isFirstFrame = false;
                _processedFrameCount++;

                if (IsStopping && _bufferQueue.IsEmpty && _metaQueue.IsEmpty)
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
                while (_metaQueue.TryDequeue(out _)) { }
                _currentMetaRemaining = 0;
                _currentMetaId = null;
                
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
