using System;
using System.Collections.Generic;
using System.Net;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace XiaoZhi.Net.Server.Protocol.WebSocket
{
    internal sealed class WebSocketService : WebSocketBehavior, IProtocolService
    {
        public WebSocketService() { }

        public event Func<string, IDictionary<string, string>, IPEndPoint, bool>? OnConnecting;
        public event Action<string, string>? OnTextMessage;
        public event Action<string, byte[]>? OnBinaryMessage;
        public event Action<string>? OnConnectionClose;

        protected override void OnOpen()
        {
            IDictionary<string, string> headers = new Dictionary<string, string>();
            foreach (string key in this.Context.Headers.AllKeys)
            {
                headers.Add(key.ToLower(), this.Context.Headers[key]);
            }

            bool? ret = this.OnConnecting?.Invoke(this.ID, headers, this.Context.UserEndPoint);
            if (ret.HasValue && !ret.Value)
            { 
                this.Context.WebSocket.Close(CloseStatusCode.Normal, "Authentication failed");
            }
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            if (e.IsBinary)
            {
                this.OnBinaryMessage?.Invoke(this.ID, e.RawData);
            }
            else if (e.IsText)
            {
                this.OnTextMessage?.Invoke(this.ID, e.Data);
            }
        }

        protected override void OnClose(CloseEventArgs e)
        {
            this.OnConnectionClose?.Invoke(this.ID);
        }
    }
}
