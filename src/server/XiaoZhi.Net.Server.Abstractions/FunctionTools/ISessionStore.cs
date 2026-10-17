namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    public interface ISessionStore
    {
        /// <summary>
        /// 获取指定SessionId的会话上下文
        /// </summary>
        /// <param name="sessionId"></param>
        /// <returns></returns>
        ISessionContext GetSession(string sessionId);
        /// <summary>
        /// 获取当前已连接的Session会话数量
        /// </summary>
        /// <returns></returns>
        long GetSessionCount();
    }
}
