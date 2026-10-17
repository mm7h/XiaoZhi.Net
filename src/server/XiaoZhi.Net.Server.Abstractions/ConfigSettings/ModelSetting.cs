namespace XiaoZhi.Net.Server.Abstractions.ConfigSettings;

public sealed class ModelSetting
{
    public string ModelName { get; set; } = null!;
    public Dictionary<string, string> Config { get; set; } = new Dictionary<string, string>();
}
