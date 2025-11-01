namespace XiaoZhi.Net.Server.Common.Dtos
{
    public class PrivateModelsConfig
    {
        public ModelSetting? VadSetting { get; set; }
        public ModelSetting? AsrSetting { get; set; }
        public ModelSetting? LlmSetting { get; set; }
        //public ModelSetting? MemorySetting { get; set; }
        public ModelSetting? TtsSetting { get; set; }
    }

    public class ModelSetting
    {
        public string ModelName { get; set; } = null!;
        public dynamic Config { get; set; } = null!;
    }
}
