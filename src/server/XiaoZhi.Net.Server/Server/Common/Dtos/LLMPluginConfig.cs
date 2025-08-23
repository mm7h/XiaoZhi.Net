using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.Dtos
{
    internal class LLMPluginConfig<TPluginSetting>
    {
        public LLMPluginConfig(Session session, TPluginSetting setting)
        {
            this.Session = session;
            this.Setting = setting;
        }

        public Session Session { get; set; }
        public TPluginSetting Setting { get; set; }
    }
}
