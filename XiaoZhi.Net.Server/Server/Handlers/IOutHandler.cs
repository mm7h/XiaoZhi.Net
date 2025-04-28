using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Handlers
{
    internal interface IOutHandler<TOut> : IDisposable
    {
        ChannelWriter<Workflow<TOut>> NextWriter { get; set; }
    }
}
