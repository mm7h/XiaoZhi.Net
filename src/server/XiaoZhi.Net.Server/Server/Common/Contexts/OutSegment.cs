namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class OutSegment
    {
        private string _content;

        public OutSegment()
        {
            this._content = string.Empty;
            this.IsFirst = false;
            this.IsLast = false;
        }

        /// <summary>
        /// 段落内容
        /// </summary>
        public string Content => _content;

        /// <summary>
        /// 是否为第一段
        /// </summary>
        public bool IsFirst { get; set; }

        /// <summary>
        /// 是否为最后一段
        /// </summary>
        public bool IsLast { get; set; }

        protected void SetContent(string content)
        {
            this._content = content;
        }

        public void Initialize(string content)
        {
            this._content = content;
            this.IsFirst = false;
            this.IsLast = false;
        }

        public void Initialize(string content, bool isFirst, bool isLast)
        {
            this._content = content;
            this.IsFirst = isFirst;
            this.IsLast = isLast;
        }

        public virtual void Reset()
        {
            this._content = string.Empty;
            this.IsFirst = false;
            this.IsLast = false;
        }
    }
}
