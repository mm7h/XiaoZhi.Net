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
            VoiceWindow = new Queue<bool>(voiceWindowSize);
            _voiceWindowSize = voiceWindowSize;
        }

        private readonly int _voiceWindowSize;

        /// <summary>
        /// Index of the next sample to analyze in the audio buffer.
        /// This allows VAD to analyze without removing data from the buffer.
        /// </summary>
        public int AnalyzedIndex { get; set; }

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

        /// <summary>
        /// Sliding window to track voice activity across multiple frames.
        /// </summary>
        public Queue<bool> VoiceWindow { get; private set; }

        /// <summary>
        /// Adds a voice detection result to the sliding window.
        /// </summary>
        public void AddToVoiceWindow(bool isVoice)
        {
            if (VoiceWindow.Count >= _voiceWindowSize)
            {
                VoiceWindow.Dequeue();
            }
            VoiceWindow.Enqueue(isVoice);
        }

        /// <summary>
        /// Counts how many true values are in the voice window.
        /// </summary>
        public int CountVoiceInWindow()
        {
            int count = 0;
            foreach (bool isVoice in VoiceWindow)
            {
                if (isVoice) count++;
            }
            return count;
        }

        /// <summary>
        /// Resets the state.
        /// </summary>
        public void Reset()
        {
            HaveVoice = false;
            HaveVoiceLatestTime = 0;
            AnalyzedIndex = 0;
            LastIsVoice = false;
            VoiceStop = false;
            VoiceWindow.Clear();
        }
    }
}
