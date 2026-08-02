namespace XiaoZhi.Net.Server.Abstractions.ConfigSettings;

public sealed class LogSetting
{
    public string LogLevel { get; set; } = "INFO";
    public string LogFilePath { get; set; } = "logs/server_log.log";
    public string OutputTemplate { get; set; } = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u4}] {Message:lj}{NewLine}{Exception}";
    public int RetainedFileCountLimit { get; set; } = 7;
}
