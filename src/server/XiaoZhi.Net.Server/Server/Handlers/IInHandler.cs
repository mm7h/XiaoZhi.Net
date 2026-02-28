using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Handlers
{
    internal interface IInHandler<TIn> : IHandler
    {
        Task Handle();
        ChannelReader<Workflow<TIn>> PreviousReader { get; set; }
    }

    internal interface IInHandler<TIn1, TIn2> : IInHandler<TIn1>
    {
        Task Handle2();
        ChannelReader<Workflow<TIn2>> PreviousReader2 { get; set; }
    }

    internal interface IInHandler<TIn1, TIn2, TIn3> : IInHandler<TIn1, TIn2>
    {
        Task Handle3();
        ChannelReader<Workflow<TIn3>> PreviousReader3 { get; set; }
    }
}
