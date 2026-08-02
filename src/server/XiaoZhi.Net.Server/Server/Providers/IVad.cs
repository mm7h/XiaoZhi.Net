using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Providers.VAD;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IVad : IProvider<ModelSetting>
    {
        int FrameSize { get; }

        void RegisterDevice(string deviceId, string sessionId, IVadEventCallback callback);

        Task AnalysisVoiceAsync(string deviceId, string sessionId, float[] audioData, CancellationToken token);

        void ResetSessionState(string deviceId, string sessionId);
    }
}
