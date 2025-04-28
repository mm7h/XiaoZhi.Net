using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Handlers
{
    internal interface IInHandler<TIn>
    {
        Task Handle();
        ChannelReader<Workflow<TIn>> PreviousReader { get; set; }
    }
}
