namespace XiaoZhi.Net.Server.Abstractions.Common.Dtos
{
    public class PrivateModelsConfig
    {
        public PrivateModelsConfig()
        {
            this.AgentSettings = new Dictionary<string, ModelSetting>();
        }
        public ModelSetting? VadSetting { get; set; }
        public ModelSetting? AsrSetting { get; set; }
        public Dictionary<string, ModelSetting> AgentSettings { get; set; }
        //public ModelSetting? MemorySetting { get; set; }
        public ModelSetting? TtsSetting { get; set; }
    }
}
