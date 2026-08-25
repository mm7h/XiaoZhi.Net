using System.Collections.Generic;

namespace XiaoZhi.Net.Server.Providers.VAD.Contexts
{
    /// <summary>
    /// Per-session state for VAD processing.
    /// </summary>
    internal class VadSessionState
    {
        private const int DEFAULT_VOICE_WINDOW_SIZE = 8;

        public VadSessionState() : this(DEFAULT_VOICE_WINDOW_SIZE)
        {
        }

        public VadSessionState(int voiceWindowSize)
        {
            this.VoiceWindow = new Queue<bool>(voiceWindowSize);
            this._voiceWindowSize = voiceWindowSize;
        }

        private readonly int _voiceWindowSize;

        private readonly List<float> _pendingAudio = [];

        /// <summary>
        /// Latest time when voice was detected (Unix timestamp in milliseconds).
        /// </summary>
        public long HaveVoiceLatestTime { get; set; }

        /// <summary>
        /// Indicates whether voice has been detected in the current session.
        /// </summary>
        public bool HaveVoice { get; set; }

        /// <summary>
        /// Indicates whether voice was detected in the last frame (for hysteresis).
        /// </summary>
        public bool LastIsVoice { get; set; }

        /// <summary>
        /// Indicates whether voice has stopped after being detected.
        /// </summary>
        public bool VoiceStop { get; set; }
        public int ProcessedSamplesSinceReset { get; private set; }

        /// <summary>
        /// Sliding window to track voice activity across multiple frames.
        /// </summary>
        public Queue<bool> VoiceWindow { get; private set; }

        /// <summary>
        /// Appends newly decoded canonical PCM. Each frame is consumed once,
        /// independently of the utterance buffer retained by AudioPacket.
        /// </summary>
        public void AppendAudio(float[] audioData)
        {
            if (audioData is { Length: > 0 })
            {
                this._pendingAudio.AddRange(audioData);
            }
        }

        public bool TryDequeueFrame(int frameSize, out float[] frame)
        {
            if (this._pendingAudio.Count < frameSize)
            {
                frame = [];
                return false;
            }

            frame = this._pendingAudio.GetRange(0, frameSize).ToArray();
            this._pendingAudio.RemoveRange(0, frameSize);
            return true;
        }

        public void MarkFrameProcessed(int sampleCount)
        {
            this.ProcessedSamplesSinceReset += sampleCount;
        }

        /// <summary>
        /// Adds a voice detection result to the sliding window.
        /// </summary>
        public void AddToVoiceWindow(bool isVoice)
        {
            if (this.VoiceWindow.Count >= this._voiceWindowSize)
            {
                this.VoiceWindow.Dequeue();
            }
            this.VoiceWindow.Enqueue(isVoice);
        }

        /// <summary>
        /// Counts how many true values are in the voice window.
        /// </summary>
        public int CountVoiceInWindow()
        {
            int count = 0;
            foreach (bool isVoice in this.VoiceWindow)
            {
                if (isVoice)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Resets the state.
        /// </summary>
        public void Reset()
        {
            this.HaveVoice = false;
            this.HaveVoiceLatestTime = 0;
            this.LastIsVoice = false;
            this.VoiceStop = false;
            this.ProcessedSamplesSinceReset = 0;
            this.VoiceWindow.Clear();
            this._pendingAudio.Clear();
        }
    }
}
