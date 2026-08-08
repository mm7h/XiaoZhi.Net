namespace XiaoZhi.Net.Server.Media.Common.Models;

/// <summary>
/// 表示通常由音频编解码器获取的音频流信息。
/// 此类不能被继承。
/// </summary>
/// <remarks>
/// 初始化 <see cref="AudioStreamInfo"/> 结构。
/// </remarks>
/// <param name="channels">音频声道数。</param>
/// <param name="sampleRate">音频采样率。</param>
/// <param name="duration">音频流时长。</param>
internal readonly struct AudioStreamInfo(int channels, int sampleRate, TimeSpan duration)
{

    /// <summary>
    /// 获取音频声道数。
    /// </summary>
    public int Channels { get; } = channels;

    /// <summary>
    /// 获取音频采样率。
    /// </summary>
    public int SampleRate { get; } = sampleRate;

    /// <summary>
    /// 获取音频流时长。
    /// </summary>
    public TimeSpan Duration { get; } = duration;
}
