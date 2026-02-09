using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAudioResampler : IProvider<ResamplerBuildConfig>
    {
        public int Channels { get; }
        public int InSampleRate { get; }
        public int OutSampleRate { get; }
        Task<(float[], int)> ResampleAsync(float[] inputData, CancellationToken token);
    }
}
