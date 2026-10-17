namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class OutSegment
    {
        public OutSegment(string content)
        {
            this.Content = content;
            this.IsFirst = false;
            this.IsLast = false;
        }
        public OutSegment(string content, bool isFirst, bool isLast)
        {
            this.Content = content;
            this.IsFirst = isFirst;
            this.IsLast = isLast;
        }
        /// <summary>
        /// 段落内容
        /// </summary>
        public string Content { get; }
        /// <summary>
        /// 是否为第一段
        /// </summary>
        public bool IsFirst { get; set; }
        /// <summary>
        /// 是否为最后一段
        /// </summary>
        public bool IsLast { get; set; }
    }
}
