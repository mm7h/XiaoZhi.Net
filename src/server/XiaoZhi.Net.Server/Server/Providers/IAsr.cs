using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Providers.ASR;
using XiaoZhi.Net.Server.Providers.ASR.Contexts;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAsr : IProvider<ModelSetting>
    {
        bool IsStreaming => false;
        void RegisterDevice(string deviceId, string sessionId, IAsrEventCallback callback);
        Task ConvertSpeechTextAsync(Workflow<float[]> workflow, int sampleRate, int frameSize, CancellationToken token);
        Task ConvertSpeechTextStreamingAsync(Workflow<float[]> workflow, int sampleRate, int frameSize, StreamingAsrOperation operation, CancellationToken token);
    }
}
