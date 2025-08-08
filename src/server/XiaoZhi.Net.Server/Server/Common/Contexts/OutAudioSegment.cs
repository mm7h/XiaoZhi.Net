namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal record OutAudioSegment : OutSegment
    {
        public OutAudioSegment(float[] audioData, double duration, OutSegment outSegment) : base(outSegment.Content, outSegment.IsFirst, outSegment.IsLast)
        {
            this.AudioData = audioData;
            this.Duration = duration;
        }

        /// <summary>
        /// 段落内容
        /// </summary>
        public float[] AudioData { get; }

        /// <summary>
        /// 音频流时长
        /// </summary>
        public double Duration { get; }
    }
}
