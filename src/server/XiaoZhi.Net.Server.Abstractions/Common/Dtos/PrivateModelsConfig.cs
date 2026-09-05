namespace XiaoZhi.Net.Server.Abstractions.Common.Dtos
{
    using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

    public class PrivateModelsConfig
    {
        public PrivateModelsConfig()
        {
            this.AgentSettings = new Dictionary<string, ModelSetting>();
        }
        public ModelSetting? VadSetting { get; set; }
        public ModelSetting? AsrSetting { get; set; }
        public Dictionary<string, ModelSetting> AgentSettings { get; set; }
        public ModelSetting? TtsSetting { get; set; }
    }
}
