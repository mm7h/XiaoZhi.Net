using SuperSocket.WebSocket;
using SuperSocket.WebSocket.Server;
using System;
using System.Buffers;
using System.Linq;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Protocol.WebSocket.Contexts;

namespace XiaoZhi.Net.Server.Protocol.WebSocket.Handlers
{
    internal class MessageDispatch
    {
        public static async ValueTask DispatchAsync(WebSocketSession appSession, WebSocketPackage package)
        {
            if (appSession is null || package is null)
            {
                throw new ArgumentNullException("Session or package cannot be null.");
            }
            if (appSession is SocketSession session && session.XiaoZhiSession is not null)
            {
                switch (package.OpCode)
                {
                    case OpCode.Text:
                        session.XiaoZhiSession.HandlerPipeline.HandleTextMessage(package.Message);
                        break;
                    case OpCode.Binary:
                       await session.XiaoZhiSession.HandlerPipeline.HandleBinaryMessageAsync(package.Data.ToArray());
                        break;
                    //case OpCode.Ping:
                    //    break;
                    //case OpCode.Pong:
                    //    break;
                }
            }
        }
    }
}
