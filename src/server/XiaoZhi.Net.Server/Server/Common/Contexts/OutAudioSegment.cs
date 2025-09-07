using XiaoZhi.Net.Server.Common.Enums;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal record OutAudioSegment : OutSegment
    {
        public OutAudioSegment(float[] audioData, AudioType audioType, bool isFirst, bool isLast, bool needResample = true) : base(string.Empty, isFirst, isLast)
        {
            this.AudioData = audioData;
            this.AudioType = audioType;
            this.NeedResample = needResample;
        }
        public OutAudioSegment(float[] audioData, AudioType audioType, OutSegment outSegment, bool needResample = true) : base(outSegment.Content, outSegment.IsFirst, outSegment.IsLast)
        {
            this.AudioData = audioData;
            this.AudioType = audioType;
            this.NeedResample = needResample;
        }

        /// <summary>
        /// 段落内容
        /// </summary>
        public float[] AudioData { get; }

        /// <summary>
        /// 是否需要重采样
        /// </summary>
        public bool NeedResample { get; }

        /// <summary>
        /// 发送的音频类型
        /// </summary>
        public AudioType AudioType { get; }
    }
}
