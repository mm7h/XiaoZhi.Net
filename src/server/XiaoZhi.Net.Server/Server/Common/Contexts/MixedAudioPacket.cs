using System;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class MixedAudioPacket
    {
        private float[] _audioData = Array.Empty<float>();
        public MixedAudioPacket()
        {
        }
        public float[] Data => this._audioData;
        public bool IsFirstFrame { get; set; }
        public bool IsLastFrame { get; set; }
        public string? SentenceId { get; set; }
        public void Initialize(float[] audioData, bool isFirstFrame, bool isLastFrame, string? sentenceId = null)
        {
            this._audioData = audioData;
            this.IsFirstFrame = isFirstFrame;
            this.IsLastFrame = isLastFrame;
            this.SentenceId = sentenceId;
        }

        public void Reset()
        {
            this._audioData = Array.Empty<float>();
            this.IsFirstFrame = false;
            this.IsLastFrame = false;
            this.SentenceId = null;
        }

    }
}
