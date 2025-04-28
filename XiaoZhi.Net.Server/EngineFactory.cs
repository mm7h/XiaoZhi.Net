
namespace XiaoZhi.Net.Server
{
    public sealed class EngineFactory
    {

        public static IServerEngine GetServerEngine()
        {
            return ServerEngine.Create();
        }
    }
}
