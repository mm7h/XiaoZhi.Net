namespace XiaoZhi.Net.Server.Media.Common.Models
{
    internal struct OutputBufferFrame(float[] data, bool isFirst, bool isLast, string? sentenceId)
    {
        public float[] Data = data;
        public bool IsFirst = isFirst;
        public bool IsLast = isLast;
        public string? SentenceId = sentenceId;
    }
}

