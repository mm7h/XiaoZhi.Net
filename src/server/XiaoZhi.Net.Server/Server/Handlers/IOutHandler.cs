using System;
using System.Threading.Channels;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Handlers
{
    internal interface IOutHandler<TOut> : IHandler, IDisposable
    {
        ChannelWriter<Workflow<TOut>> NextWriter { get; set; }
    }
}
