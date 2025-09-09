using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Dtos
{
    public record AudioVolumeConfig(AudioType AudioType, float BaseVolume, float SoundSuppressionPercentage);
}
