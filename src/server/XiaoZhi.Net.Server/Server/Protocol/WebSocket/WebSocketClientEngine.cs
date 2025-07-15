using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Protocol.WebSocket
{
    internal class WebSocketClientEngine : ISocketSendOutter
    {
        private readonly WebSocketSharp.WebSocket _client;
        private readonly IDictionary<string, string>? _headers;

        public WebSocketClientEngine(string websocketUrl, IDictionary<string, string>? headers)
        {
            this._client = new WebSocketSharp.WebSocket(websocketUrl);
            this._headers = headers;
        }

        public event Action? OnOpen;
        public event Action<string>? OnMessage;
        public event Action<string>? OnError;
        public event Action<int, string>? OnClose;

        public bool Connected => this._client.IsAlive;

        public bool Connect()
        {
            try
            {
                if (this._headers is not null)
                {
                    this._client.CustomHeaders = this._headers;
                }


                this._client.OnOpen += this.Client_OnOpen;
                this._client.OnMessage += this.Client_OnMessage;
                this._client.OnError += this.Client_OnError;
                this._client.OnClose += this.Client_OnClose;

                this._client.Connect();

                return true;
            }
            catch
            {
                return false;
            }
        }

        public Task SendAsync(string message)
        {
            if (this._client.IsAlive)
            {
                this._client.Send(message);
            }
            return Task.CompletedTask;
        }
        public Task SendAsync(byte[] bytePacket)
        {
            if (this._client.IsAlive)
            {
                this._client.Send(bytePacket);
            }
            return Task.CompletedTask;
        }
        public void Close()
        {
            if (this._client.IsAlive)
            {
                this._client.Close();
            }
        }

        private void Client_OnOpen(object sender, EventArgs e)
        {
            this.OnOpen?.Invoke();
        }


        private void Client_OnMessage(object sender, WebSocketSharp.MessageEventArgs e)
        {
            if (e.IsBinary)
            {
                return;
            }
            if (e.IsPing)
            {

            }
            if (e.IsText)
            {
                this.OnMessage?.Invoke(e.Data);
                return;
            }
        }

        private void Client_OnError(object sender, WebSocketSharp.ErrorEventArgs e)
        {
            this.OnError?.Invoke(e.Message);

        }

        private void Client_OnClose(object sender, WebSocketSharp.CloseEventArgs e)
        {
            if (e.WasClean)
            {
                this.OnClose?.Invoke(e.Code, e.Reason);
            }
            else
            {
                this.OnClose?.Invoke(e.Code, "Connection closed unexpectedly: " + e.Reason);
            }

        }
    }
}
