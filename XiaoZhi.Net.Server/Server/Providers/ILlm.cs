using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using XiaoZhi.Net.Server.Common.Entities;
using System.Threading.Tasks;
using System.Threading;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface ILlm : IProvider
    {
        event Action<string> OnBeforeTokenGenerate;
        event Action<string, OutSegment> OnTokenGenerating;
        event Action<string, string> OnTokenGenerated;
        Task ChatAsync( IEnumerable<Dialogue> dialogues,  Workflow<string> workflow, CancellationToken token);
        Task ChatByStreamingAsync( IEnumerable<Dialogue> dialogues,  Workflow<string> workflow, CancellationToken token);
    }
}
