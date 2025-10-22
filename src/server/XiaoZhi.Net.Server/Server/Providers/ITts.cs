using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface ITts : IProvider<ModelSetting>
    {
        event Action<OutSegment> OnBeforeProcessing;
        event Action<float[]> OnProcessing;
        event Action<float[], OutSegment, double> OnProcessed;
        int GetTtsSampleRate();
        Task SynthesisAsync(Workflow<OutSegment> sessionContext, CancellationToken token);
    }
}
