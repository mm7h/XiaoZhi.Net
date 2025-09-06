using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.Dtos
{
    internal record LLMPluginConfig(Session Session);

    internal record LLMPluginConfig<TPluginSetting> : LLMPluginConfig
    {
        public LLMPluginConfig(Session session, TPluginSetting setting) : base(session)
        {
            this.Setting = setting;
        }

        public TPluginSetting Setting { get; }
    }
}
