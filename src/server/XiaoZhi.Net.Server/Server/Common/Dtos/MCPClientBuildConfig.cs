using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.Dtos
{
    internal class MCPClientBuildConfig
    {
        public MCPClientBuildConfig(Session session, ModelSetting modelSetting)
        {
            this.Session = session;
            this.ModelSetting = modelSetting;
        }

        public Session Session { get; set; }
        public ModelSetting ModelSetting { get; set; }
    }
}
