namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    /// <summary>
    /// 控制当前会话
    /// </summary>
    public interface ISessionController
    {
        /// <summary>
        /// 请求在当前回复的音频发送完成后关闭会话。
        /// </summary>
        /// <returns>首次成功请求时为 true；会话已在关闭时为 false。</returns>
        bool TryRequestCloseAfterChat();
    }
}
