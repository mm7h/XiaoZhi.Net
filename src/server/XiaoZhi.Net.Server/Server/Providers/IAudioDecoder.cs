using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAudioDecoder : IProvider<AudioSetting>
    {
        int SampleRate { get; }
        int Channels { get; }
        int FrameDuration { get; }
        int FrameSize { get; }
        Task<float[]> DecodeAsync(byte[] opusData, CancellationToken token);
    }
}
