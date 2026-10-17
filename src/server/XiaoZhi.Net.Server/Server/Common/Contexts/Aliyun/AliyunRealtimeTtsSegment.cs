using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Common.Contexts.Aliyun
{
    /// <summary>
    /// 发送到阿里云实时语音合成任务的文本片段。
    /// </summary>
    internal sealed record AliyunRealtimeTtsSegment(
        string Content,
        Emotion Emotion,
        string? ParagraphId,
        string? SentenceId,
        bool IsFirstSegment,
        bool IsLastSegment)
    {
        public static AliyunRealtimeTtsSegment From(OutSegment segment) => new(
            segment.Content,
            segment.Emotion,
            segment.ParagraphId,
            segment.SentenceId,
            segment.IsFirstSegment,
            segment.IsLastSegment);
    }
}
