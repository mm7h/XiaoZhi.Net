namespace XiaoZhi.Net.Server.Abstractions.ConfigSettings;

public sealed class MusicSetting
{
    public string MusicFolderPath { get; set; } = "./musics";

    /// <summary>
    /// 获取或设置音乐控制命令的最长等待时间。
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// 获取或设置停止音乐并等待底层播放器结束的最长时间。
    /// </summary>
    public int StopTimeoutSeconds { get; set; } = 10;
}
