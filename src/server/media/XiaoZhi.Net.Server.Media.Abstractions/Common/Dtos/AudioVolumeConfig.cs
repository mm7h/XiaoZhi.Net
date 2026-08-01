using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Media.Abstractions.Common.Dtos
{
    /// <summary>
    /// 音频音量配置
    /// </summary>
    /// <param name="AudioType">音频类型</param>
    /// <param name="BaseVolume">基础音量值 (0.0 - 1.0)</param>
    /// <param name="SuppressionVolume">抑制时的音量值 (0.0 - 1.0)</param>
    public record AudioVolumeConfig(AudioType AudioType, float BaseVolume, float SuppressionVolume);
}
