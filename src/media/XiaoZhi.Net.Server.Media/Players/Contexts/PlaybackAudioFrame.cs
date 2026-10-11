using XiaoZhi.Net.Server.Media.Common.Models;

namespace XiaoZhi.Net.Server.Media.Players.Contexts;

internal sealed record PlaybackAudioFrame(AudioFrame Frame, int Generation);
