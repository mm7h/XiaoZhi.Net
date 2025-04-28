namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class OutSegment
    {
        public OutSegment(string content)
        {
            Content = content;
            IsFirst = false;
            IsLast = false;
        }
        public OutSegment(string content, bool isFirst, bool isLast)
        {
            Content = content;
            IsFirst = isFirst;
            IsLast = isLast;
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
