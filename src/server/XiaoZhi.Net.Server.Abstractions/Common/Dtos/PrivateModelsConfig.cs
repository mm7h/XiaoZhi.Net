namespace XiaoZhi.Net.Server.Abstractions.Common.Dtos
{
    public class PrivateModelsConfig
    {
        public ModelSetting? VadSetting { get; set; }
        public ModelSetting? AsrSetting { get; set; }
        public ModelSetting? LlmSetting { get; set; }
        //public ModelSetting? MemorySetting { get; set; }
        public ModelSetting? TtsSetting { get; set; }
    }
}
