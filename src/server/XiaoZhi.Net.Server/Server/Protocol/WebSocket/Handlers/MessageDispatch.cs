using SuperSocket.WebSocket;
using SuperSocket.WebSocket.Server;
using System;
using System.Buffers;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Protocol.WebSocket.Contexts;

namespace XiaoZhi.Net.Server.Protocol.WebSocket.Handlers
{
    internal class MessageDispatch
    {
        public static async ValueTask DispatchAsync(WebSocketSession appSession, WebSocketPackage package)
        {
            if (appSession is null || package is null)
            {
                throw new ArgumentNullException(Lang.MessageDispatch_DispatchAsync_ArgumentNull);
            }
            if (appSession is SocketSession session && session.XiaoZhiSession is not null)
            {
                if (session.XiaoZhiSession.CloseAfterChat)
                {
                    return;
                }

                switch (package.OpCode)
                {
                    case OpCode.Text:
                        JsonNode? jsonObject = JsonNode.Parse(package.Message);
                        string? type = jsonObject?["type"]?.GetValue<string>()?.ToLower();
                        if (jsonObject is JsonObject jsonObj && !string.IsNullOrWhiteSpace(type) && type == "hello")
                        {
                            await session.XiaoZhiSession.HandlerPipeline.HandleHelloMessageAsync(jsonObj);
                        }
                        else
                        {
                            session.XiaoZhiSession.HandlerPipeline.HandleTextMessage(package.Message);
                        }
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
