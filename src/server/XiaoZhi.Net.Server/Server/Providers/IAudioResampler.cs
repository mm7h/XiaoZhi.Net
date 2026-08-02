using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAudioResampler : IProvider<ResamplerBuildConfig>
    {
        int Channels { get; }
        int InSampleRate { get; }
        int OutSampleRate { get; }
        Task<(float[], int)> ResampleAsync(float[] inputData, CancellationToken token);
    }
}
