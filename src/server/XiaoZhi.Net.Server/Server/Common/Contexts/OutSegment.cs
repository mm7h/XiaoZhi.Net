using XiaoZhi.Net.Server.Abstractions.Common.Enums;

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
            this.Emotion = Emotion.Neutral;
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

        /// <summary>
        /// 当前段落的情绪
        /// </summary>
        public Emotion Emotion { get; set; }

        /// <summary>
        /// 段落Id
        /// </summary>
        public string? ParagraphId { get; set; }

        /// <summary>
        /// 句子Id
        /// </summary>
        public string? SentenceId { get; set; }

        public void Initialize(string content, Emotion emotion, string? paragraphId = null, string? sentenceId = null)

        {
            this._content = content;
            this.IsFirstSegment = false;
            this.IsLastSegment = false;
            this.Emotion = emotion;
            this.ParagraphId = paragraphId;
            this.SentenceId = sentenceId;
        }

        public void Initialize(string content, bool isFirst, bool isLast, Emotion emotion, string? paragraphId = null, string? sentenceId = null)
        {
            this._content = content;
            this.IsFirstSegment = isFirst;
            this.IsLastSegment = isLast;
            this.Emotion = emotion;
            this.ParagraphId = paragraphId;
            this.SentenceId = sentenceId;
        }

        public virtual void Reset()
        {
            this._content = string.Empty;
            this.IsFirstSegment = false;
            this.IsLastSegment = false;
            this.Emotion = Emotion.Neutral;
            this.SentenceId = null;
            this.ParagraphId = null;
        }

    }
}
