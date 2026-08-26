using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Common.Contexts.Aliyun
{
    /// <summary>
    /// 实时语音合成回调中关联到本地文本的句子信息。
    /// </summary>
    internal sealed record AliyunRealtimeTtsSentence(string Id, string Text, Emotion Emotion);
}
