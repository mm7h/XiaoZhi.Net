using SherpaOnnx;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class AudioPacket
    {
        private bool _released;
        private const int DEFAULT_BUFFER_CAPACITY = 960 * 100;
        private readonly CircularBuffer _audioBuffer;
        private readonly object _audioBufferGate = new();

        public AudioPacket()
        {
            this._audioBuffer = new CircularBuffer(DEFAULT_BUFFER_CAPACITY);
        }

        public long LastHaveVoiceTime { get; set; }

        public long HaveVoiceLatestTime { get; set; }

        public bool HaveVoice { get; set; }

        public bool VoiceStop { get; set; }

        public int BufferHead
        {
            get
            {
                lock (this._audioBufferGate)
                {
                    return this._released ? 0 : this._audioBuffer.Head;
                }
            }
        }

        public int BufferSize
        {
            get
            {
                lock (this._audioBufferGate)
                {
                    return this._released ? 0 : this._audioBuffer.Size;
                }
            }
        }

        public void PushAudio(float[] audioData)
        {
            if (audioData.Length == 0)
            {
                return;
            }

            lock (this._audioBufferGate)
            {
                if (!this._released)
                {
                    this._audioBuffer.Push(audioData);
                }
            }
        }

        public float[] GetFrames(int startIndex, int frameSize)
        {
            lock (this._audioBufferGate)
            {
                if (this._released || this._audioBuffer.Size < startIndex + frameSize)
                {
                    return [];
                }
                return this._audioBuffer.Get(startIndex, frameSize);
            }
        }

        public void PopFrames(int count)
        {
            lock (this._audioBufferGate)
            {
                if (!this._released && count > 0 && this._audioBuffer.Size >= count)
                {
                    this._audioBuffer.Pop(count);
                }
            }
        }

        public float[] GetAllAudio()
        {
            lock (this._audioBufferGate)
            {
                if (this._released || this._audioBuffer.Size == 0)
                {
                    return [];
                }
                return this._audioBuffer.Get(this._audioBuffer.Head, this._audioBuffer.Size);
            }
        }

        public float[] GetLatestAudio(int maxSamples)
        {
            if (maxSamples <= 0)
            {
                return [];
            }

            lock (this._audioBufferGate)
            {
                if (this._released || this._audioBuffer.Size == 0)
                {
                    return [];
                }

                int sampleCount = System.Math.Min(maxSamples, this._audioBuffer.Size);
                int startIndex = this._audioBuffer.Head + this._audioBuffer.Size - sampleCount;
                return this._audioBuffer.Get(startIndex, sampleCount);
            }
        }

        public float[] TakeAllAudio()
        {
            lock (this._audioBufferGate)
            {
                if (this._released || this._audioBuffer.Size == 0)
                {
                    return [];
                }

                float[] audio = this._audioBuffer.Get(this._audioBuffer.Head, this._audioBuffer.Size);
                this._audioBuffer.Reset();
                return audio;
            }
        }

        public void ResetAudioBuffer()
        {
            lock (this._audioBufferGate)
            {
                if (!this._released)
                {
                    this._audioBuffer.Reset();
                }
            }
        }

        public void Reset()
        {
            this.VoiceStop = false;
            this.HaveVoice = false;
            this.HaveVoiceLatestTime = 0;
            this.LastHaveVoiceTime = 0;
            this.ResetAudioBuffer();
        }

        public void Release()
        {
            lock (this._audioBufferGate)
            {
                if (this._released)
                {
                    return;
                }

                this._released = true;
                this._audioBuffer.Dispose();
            }
        }

        public void TrimOldAudio(int keepFrames = 1024)
        {
            lock (this._audioBufferGate)
            {
                if (!this._released && this._audioBuffer.Size > keepFrames)
                {
                    this._audioBuffer.Pop(this._audioBuffer.Size - keepFrames);
                }
            }
        }
    }
}
