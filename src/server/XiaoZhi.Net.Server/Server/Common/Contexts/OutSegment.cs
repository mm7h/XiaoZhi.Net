namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class OutSegment
    {
        private string _content;

        public OutSegment()
        {
            this._content = string.Empty;
            this.IsFirstSegment = false;
            this.IsLastSegment = false;
        }

        /// <summary>
        /// 段落内容
        /// </summary>
        public string Content => _content;

        /// <summary>
        /// 是否为第一段
        /// </summary>
        public bool IsFirstSegment { get; set; }

        /// <summary>
        /// 是否为最后一段
        /// </summary>
        public bool IsLastSegment { get; set; }

        public void Initialize(string content)
        {
            this._content = content;
            this.IsFirstSegment = false;
            this.IsLastSegment = false;
        }

        public void Initialize(string content, bool isFirst, bool isLast)
        {
            this._content = content;
            this.IsFirstSegment = isFirst;
            this.IsLastSegment = isLast;
        }

        public virtual void Reset()
        {
            this._content = string.Empty;
            this.IsFirstSegment = false;
            this.IsLastSegment = false;
        }
    }
}
