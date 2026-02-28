using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Providers.TTS;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface ITts : IProvider<ModelSetting>
    {
        int GetTtsSampleRate();
        void RegisterDevice(string deviceId, string sessionId, ITtsEventCallback callback);
        Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token);
    }
}
