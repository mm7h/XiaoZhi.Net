using SherpaOnnx;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAsr : IProvider<ModelSetting>
    {
        Task<string> ConvertSpeechTextAsync(Workflow<CircularBuffer> workflow, int sampleRate, int frameSize, CancellationToken token);
    }
}
