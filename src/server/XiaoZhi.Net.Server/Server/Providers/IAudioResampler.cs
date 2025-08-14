using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAudioResampler : IProvider
    {
        public int Channels { get; }
        public int InSampleRate { get; }
        public int OutSampleRate { get; }
        Task<(float[], int)> ResampleAsync(float[] inputData, CancellationToken token);
    }
}
