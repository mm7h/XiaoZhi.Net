using System.Collections.Generic;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class VadStatus
    {
        private const int VOICE_WINDOW_SIZE = 10;
        private readonly Queue<bool> _voiceWindow;

        public VadStatus()
        {
            this._voiceWindow = new Queue<bool>(VOICE_WINDOW_SIZE);
        }

        public long HaveVoiceLatestTime { get; set; }
        public bool HaveVoice { get; set; }
        public bool VoiceStop { get; set; }

        /// <summary>
        /// Last voice detection state for hysteresis logic (dual-threshold).
        /// </summary>
        public bool LastIsVoice { get; set; }

        /// <summary>
        /// Adds a voice frame detection result to the sliding window.
        /// </summary>
        public void AddVoiceFrame(bool isVoice)
        {
            if (this._voiceWindow.Count >= VOICE_WINDOW_SIZE)
            {
                this._voiceWindow.Dequeue();
            }
            this._voiceWindow.Enqueue(isVoice);
        }

        /// <summary>
        /// Counts the number of voice frames in the sliding window.
        /// </summary>
        public int CountVoiceFrames()
        {
            int count = 0;
            foreach (bool frame in this._voiceWindow)
            {
                if (frame)
                {
                    count++;
                }
            }
            return count;
        }

        public void Reset()
        {
            this.VoiceStop = false;
            this.HaveVoice = false;
            this.HaveVoiceLatestTime = 0;
            this.LastIsVoice = false;
            this._voiceWindow.Clear();
        }
    }
}
