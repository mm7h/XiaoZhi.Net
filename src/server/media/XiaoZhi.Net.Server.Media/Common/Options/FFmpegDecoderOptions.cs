namespace XiaoZhi.Net.Server.Media.Common.Options;

/// <summary>
/// 表示可通过 <see cref="FFmpegDecoder"/> 传入的指定音频源解码和（或）重采样选项。
/// 此类不能被继承。
/// </summary>
internal sealed record FFmpegDecoderOptions(int SampleRate = 44100, int Channels = 2, int FrameDuration = 60);
