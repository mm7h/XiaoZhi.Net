namespace XiaoZhi.Net.Server.Media.Common.Dtos;

/// <summary>
/// Options for decoding (and, or) resampling specified audio source that can be passed
/// through <see cref="FFmpegDecoder"/> class. This class cannot be inherited.
/// </summary>
internal sealed record FFmpegDecoderOptions(int SampleRate = 44100, int Channels = 2, int FrameDuration = 60);