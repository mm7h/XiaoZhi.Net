using System.Threading.Channels;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Handlers
{
    internal interface IOutHandler<TOut> : IHandler
    {
        ChannelWriter<Workflow<TOut>> NextWriter { get; set; }
    }
    internal interface IOutHandler<TOut1, TOut2> : IOutHandler<TOut1>
    {
        ChannelWriter<Workflow<TOut2>> NextWriter2 { get; set; }
    }

    internal interface IOutHandler<TOut1, TOut2, TOut3> : IOutHandler<TOut1, TOut2>
    {
        ChannelWriter<Workflow<TOut3>> NextWriter3 { get; set; }
    }
}
