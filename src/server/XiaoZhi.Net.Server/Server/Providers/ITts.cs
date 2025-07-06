using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface ITts : IProvider
    {
        event Action<string, OutSegment> OnBeforeProcessing;
        event Action<string, float[]> OnProcessing;
        event Action<string, OutSegment, int> OnProcessed;
        int GetTtsSampleRate();
        Task SynthesisAsync(Workflow<OutSegment> sessionContext, Session session, CancellationToken token);
    }
}
