using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAudioEncoder : IProvider<AudioSetting>
    {
        int SampleRate { get; }
        int Channels { get; }
        int FrameDuration { get; }
        int FrameSize { get; }
        Task<byte[]> EncodeAsync(float[] pcmData, CancellationToken token);
    }
}
