using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Providers.ASR;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAsr : IProvider<ModelSetting>
    {
        void RegisterDevice(string deviceId, string sessionId, IAsrEventCallback callback);
        Task ConvertSpeechTextAsync(Workflow<float[]> workflow, int sampleRate, int frameSize, CancellationToken token);
    }
}
