namespace XiaoZhi.Net.Server.Abstractions.ConfigSettings;

public sealed class DeviceBindSetting
{
    public string BindCodePromptFilePath { get; set; } = "configs/assets/bind_code.wav";
    public string BindCodeDigitFolderPath { get; set; } = "configs/assets/bind_code";
    public string BindNotFoundFilePath { get; set; } = "configs/assets/bind_not_found.wav";
}
