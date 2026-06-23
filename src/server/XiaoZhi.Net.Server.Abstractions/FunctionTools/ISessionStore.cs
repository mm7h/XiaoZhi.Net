namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    public interface ISessionStore
    {
        ISessionContext GetSession(string sessionId);
        long GetSessionCount();
    }
}
