namespace XiaoZhi.Net.Server.Common.Dtos
{
    internal class PrivateModelsConfig
    {
        public string Prompt { get; set; } = null!;
        public bool UseStreaming { get; private set; } = true;
        public string? LlmModelName { get; set; }
        public string? SummaryMemory { get; set; }
        public ModelSetting? VadSetting { get; set; }
        public ModelSetting? AsrSetting { get; set; }
        public ModelSetting? LlmSetting { get; set; }
        //public ModelSetting? MemorySetting { get; set; }
        public ModelSetting? TtsSetting { get; set; }
    }
}
