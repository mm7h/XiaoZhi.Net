using System.Buffers;
using System.Net;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    public interface ISessionContext
    {
        string DeviceId { get; }
        string SessionId { get; }
        DateTimeOffset LoginTime { get; }
        DateTimeOffset LastActiveTime { get; }
        EndPoint LocalEndPoint { get; }
        EndPoint RemoteEndPoint { get; }
        ValueTask SendAsync(string textMessage, CancellationToken cancellationToken);
        ValueTask SendAsync(ReadOnlySequence<byte> pcmData, CancellationToken cancellationToken);
    }
}
