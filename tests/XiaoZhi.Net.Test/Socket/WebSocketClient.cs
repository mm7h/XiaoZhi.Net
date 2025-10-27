using System.Net.WebSockets;
using System.Reactive.Linq;
using Websocket.Client;

namespace XiaoZhi.Net.Test.Socket
{
    internal class WebSocketClient : IDisposable
    {
        private WebsocketClient? _socket;
        private readonly SemaphoreSlim _socketSemaphore = new(1, 1);

        public bool IsConnected => this._socket?.IsRunning ?? false;
        private readonly IDictionary<string, string>? _headers;

        public WebSocketClient(IDictionary<string, string>? headers)
        {
            this._headers = headers;
        }

        public Uri? EndpointUrl { get; private set; }

        #region Events

        public event Action? OnOpen;
        public event Action<WebSocketError, string>? OnError;
        public event Action<WebSocketCloseStatus?, string?>? OnClose;
        public event Action<string>? OnTextMessage;
        public event Action<byte[]>? OnBinaryMessage;

        #endregion

        public async Task ConnectAsync(string endpointUrl, CancellationToken cancellationToken = default)
        {
            this.EndpointUrl = new Uri(endpointUrl);

            try
            {
                await this._socketSemaphore.WaitAsync(cancellationToken);

                if (this._socket is not null)
                {
                    this._socket.Dispose();
                    this._socket = null;
                }

                if (this._socket is null)
                {
                    this._socket = new WebsocketClient(this.EndpointUrl, () =>
                    {
                        ClientWebSocket socket = new ClientWebSocket();
                        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                        socket.Options.CollectHttpResponseDetails = true;
                        if (this._headers is not null)
                            foreach (var item in this._headers)
                            {
                                socket.Options.SetRequestHeader(item.Key, item.Value);
                            }
                        return socket;
                    })
                    {
                        IsReconnectionEnabled = false,
                        LostReconnectTimeout = null,
                        ErrorReconnectTimeout = null,
                        ReconnectTimeout = null
                    };

                    this._socket.MessageReceived
                        .Where(msg => msg.MessageType == WebSocketMessageType.Text)
                        .Where(msg => !string.IsNullOrEmpty(msg.Text))
                        .Subscribe(msg => this.OnTextMessage?.Invoke(msg.Text!));

                    this._socket.MessageReceived
                         .Where(msg => msg.MessageType == WebSocketMessageType.Binary)
                         .Where(msg => msg.Binary is not null)
                         .Subscribe(msg => this.OnBinaryMessage?.Invoke(msg.Binary!));

                    this._socket.ReconnectionHappened
                        .Subscribe(e =>
                        {
                            Console.WriteLine("ReconnectionHappened: " + e.Type);
                        });

                    this._socket.DisconnectionHappened
                        .Subscribe(e =>
                        {
                            e.CancelReconnection = true;
                            this.OnClose?.Invoke(e.CloseStatus, e.CloseStatusDescription);

                        });
                }

                await this._socket.StartOrFail();

                this.OnOpen?.Invoke();
            }
            catch (Exception ex)
            {
                this.OnError?.Invoke(WebSocketError.ConnectionClosedPrematurely, ex.Message);
                return;
            }
            finally
            {
                this._socketSemaphore.Release();
            }
        }
        public Task SendAsync(string text)
        {
            this._socket?.Send(text);
            return Task.CompletedTask;
        }
        public Task SendAsync(byte[] data)
        {
            this._socket?.Send(data);
            return Task.CompletedTask;
        }

        public async Task CloseAsync(WebSocketCloseStatus webSocketCloseStatus = WebSocketCloseStatus.Empty, string statusDescription = "")
        {
            if (!this.IsConnected)
            {
                return;
            }
            try
            {
                this.OnClose?.Invoke(webSocketCloseStatus, statusDescription);
                if (this._socket is not null)
                {
                    await this._socket.StopOrFail(webSocketCloseStatus, statusDescription);
                }
            }
            catch (Exception)
            {
                this.OnError?.Invoke(WebSocketError.ConnectionClosedPrematurely, "Failed to close WebSocket connection gracefully.");
            }
        }

        public void Dispose()
        {
            this._socket?.Dispose();
        }
    }
}
