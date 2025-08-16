using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IVad : IProvider<ModelSetting>
    {
        int FrameSize { get; }
        Task<bool> AnalysisVoiceAsync( Session sessionContext, CancellationToken token);
    }
}
