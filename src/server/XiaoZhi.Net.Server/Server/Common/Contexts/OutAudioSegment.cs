using System;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class OutAudioSegment : OutSegment
    {
        private float[] _audioData = Array.Empty<float>();

        public OutAudioSegment() : base()
        {
        }

        public float[] AudioData => this._audioData;

        public AudioType AudioType { get; private set; }
        public bool IsFirstFrame { get; set; }
        public bool IsLastFrame { get; set; }

        public void Initialize(float[]? audioData = null, AudioType audioType = AudioType.None, string? content = null, bool isFirstSegment = false, bool isLastSegment = false,
            bool isFirstFrame = false, bool isLastFrame = false)
        {
            this._audioData = audioData ?? Array.Empty<float>();
            this.AudioType = audioType;
            this.IsFirstFrame = isFirstFrame;
            this.IsLastFrame = isLastFrame;
            base.Initialize(content ?? string.Empty, isFirstSegment, isLastSegment);
        }

        public override void Reset()
        {
            this._audioData = Array.Empty<float>();
            this.AudioType = AudioType.None;
            this.IsFirstFrame = false;
            this.IsLastFrame = false;
            base.Reset();
        }
    }
}
