namespace XiaoZhi.Net.Server.Media.Common.Models
{
    internal struct OutputBufferFrame
    {
        public float[] Data;
        public bool IsFirst;
        public bool IsLast;
        public string? SentenceId;

        public OutputBufferFrame(float[] data, bool isFirst, bool isLast, string? sentenceId)
        {
            Data = data;
            IsFirst = isFirst;
            IsLast = isLast;
            SentenceId = sentenceId;
        }

    }
}

