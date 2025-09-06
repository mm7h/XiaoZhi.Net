namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal record OutAudioSegment : OutSegment
    {
        public OutAudioSegment(float[] audioData, bool isFirst, bool isLast, bool needResample = true) : base(string.Empty, isFirst, isLast)
        {
            this.AudioData = audioData;
            this.NeedResample = needResample;
        }
        public OutAudioSegment(float[] audioData, OutSegment outSegment, bool needResample = true) : base(outSegment.Content, outSegment.IsFirst, outSegment.IsLast)
        {
            this.AudioData = audioData;
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
    }
}
