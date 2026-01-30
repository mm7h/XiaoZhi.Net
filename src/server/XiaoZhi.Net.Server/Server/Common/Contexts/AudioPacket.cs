using SherpaOnnx;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class AudioPacket
    {
        private bool _released;
        private const int DEFAULT_BUFFER_CAPACITY = 960 * 100;
        private readonly CircularBuffer _audioBuffer;

        public AudioPacket()
        {
            this._audioBuffer = new CircularBuffer(DEFAULT_BUFFER_CAPACITY);
        }

        /// <summary>
        /// Last time voice was detected (Unix timestamp in milliseconds).
        /// </summary>
        public long LastHaveVoiceTime { get; set; }

        /// <summary>
        /// Latest time when voice was detected (Unix timestamp in milliseconds).
        /// </summary>
        public long HaveVoiceLatestTime { get; set; }

        /// <summary>
        /// Indicates whether voice has been detected in the current session.
        /// </summary>
        public bool HaveVoice { get; set; }

        /// <summary>
        /// Indicates whether voice has stopped (silence detected after voice).
        /// </summary>
        public bool VoiceStop { get; set; }

        /// <summary>
        /// Last voice detection state for hysteresis logic (dual-threshold).
        /// </summary>
        public bool LastIsVoice { get; set; }

        public int BufferHead => this._audioBuffer.Head;
        public int BufferSize => this._audioBuffer.Size;

        /// <summary>
        /// Pushes audio data to the buffer.
        /// </summary>
        public void PushAudio(float[] audioData)
        {
            if (!this._released && audioData.Length > 0)
            {
                this._audioBuffer.Push(audioData);
            }
        }

        /// <summary>
        /// Gets audio frames for VAD analysis without removing them.
        /// </summary>
        /// <param name="startIndex">Start index in the buffer.</param>
        /// <param name="frameSize">Number of samples to get.</param>
        /// <returns>Audio samples.</returns>
        public float[] GetFrames(int startIndex, int frameSize)
        {
            if (this._released || this._audioBuffer.Size < startIndex + frameSize)
            {
                return [];
            }
            return this._audioBuffer.Get(startIndex, frameSize);
        }

        /// <summary>
        /// Removes processed frames from the buffer.
        /// </summary>
        /// <param name="count">Number of samples to remove.</param>
        public void PopFrames(int count)
        {
            if (!this._released && count > 0 && this._audioBuffer.Size >= count)
            {
                this._audioBuffer.Pop(count);
            }
        }

        /// <summary>
        /// Gets all audio data in the buffer (for saving to file).
        /// </summary>
        public float[] GetAllAudio()
        {
            if (this._released || this._audioBuffer.Size == 0)
            {
                return [];
            }
            return this._audioBuffer.Get(this._audioBuffer.Head, this._audioBuffer.Size);
        }

        /// <summary>
        /// Resets the audio buffer.
        /// </summary>
        public void ResetAudioBuffer()
        {
            if (!this._released)
            {
                this._audioBuffer.Reset();
            }
        }

        /// <summary>
        /// Resets all status and buffers.
        /// </summary>
        public void Reset()
        {
            this.VoiceStop = false;
            this.HaveVoice = false;
            this.HaveVoiceLatestTime = 0;
            this.LastHaveVoiceTime = 0;
            this.LastIsVoice = false;
            this.ResetAudioBuffer();
        }

        /// <summary>
        /// Releases all resources.
        /// </summary>
        public void Release()
        {
            this._released = true;
            this._audioBuffer.Dispose();
        }

        /// <summary>
        /// Trims old audio data to reduce memory pressure during long silence.
        /// </summary>
        /// <param name="keepFrames">Number of frames to keep at the end.</param>
        public void TrimOldAudio(int keepFrames = 1024)
        {
            if (!this._released && this._audioBuffer.Size > keepFrames)
            {
                this._audioBuffer.Pop(this._audioBuffer.Size - keepFrames);
            }
        }
    }
}
