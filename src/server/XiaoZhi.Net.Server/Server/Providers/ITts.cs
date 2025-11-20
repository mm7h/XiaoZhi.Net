using System.Collections.Generic;
using System.Threading;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface ITts : IProvider<ModelSetting>
    {
        int GetTtsSampleRate();
        IAsyncEnumerable<OutAudioSegment> SynthesisEnumerableAsync(Workflow<OutSegment> workflow, CancellationToken token);
    }
}
