using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IVad : IProvider
    {
        int FrameSize { get; }
        Task<bool> AnalysisVoiceAsync( Session sessionContext, CancellationToken token);
    }
}
