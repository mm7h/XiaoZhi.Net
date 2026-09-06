using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Websocket.Client;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Protocol.WebSocket
{
    internal class WebSocketClient : IDisposable
    {
        private WebsocketClient? _socket;
        private readonly SemaphoreSlim _socketSemaphore = new(1, 1);

        public bool IsConnected => this._socket?.IsRunning ?? false;
        private readonly IReadOnlyDictionary<string, string>? _headers;

        public WebSocketClient(IDictionary<string, string>? headers)
        {
            this._headers = headers is null
                ? null
                : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
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
            await this.ConnectAsync(endpointUrl, this._headers, cancellationToken);
        }

        /// <summary>
        /// 使用仅属于当前底层 WebSocket 连接的请求头建立连接。
        /// <see cref="CloseAsync"/> 后可复用包装对象；流式 ASR 等调用方仍可为每轮
        /// utterance 提供新的请求 ID 或连接 ID。
        /// </summary>
        public async Task ConnectAsync(
            string endpointUrl,
            IReadOnlyDictionary<string, string>? headers,
            CancellationToken cancellationToken = default)
        {
            Uri endpointUrlValue = new(endpointUrl);
            IReadOnlyDictionary<string, string>? connectionHeaders = headers is null
                ? null
                : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
            bool lockTaken = false;

            try
            {
                await this._socketSemaphore.WaitAsync(cancellationToken);
                lockTaken = true;
                this.EndpointUrl = endpointUrlValue;

                if (this._socket is not null)
                {
                    this._socket.Dispose();
                    this._socket = null;
                }

                if (this._socket is null)
                {
                    this._socket = new WebsocketClient(endpointUrlValue, () =>
                    {
                        ClientWebSocket socket = new ClientWebSocket();
                        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                        socket.Options.CollectHttpResponseDetails = true;
                        if (connectionHeaders is not null)
                        {
                            foreach (var item in connectionHeaders)
                            {
                                socket.Options.SetRequestHeader(item.Key, item.Value);
                            }
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
                        .Where(msg => !string.IsNullOrWhiteSpace(msg.Text))
                        .Subscribe(msg => this.OnTextMessage?.Invoke(msg.Text!));

                    this._socket.MessageReceived
                         .Where(msg => msg.MessageType == WebSocketMessageType.Binary)
                         .Where(msg => msg.Binary is not null)
                         .Subscribe(msg => this.OnBinaryMessage?.Invoke(msg.Binary!));

                    this._socket.ReconnectionHappened
                        .Subscribe(e =>
                        {
                            //Console.WriteLine("ReconnectionHappened: " + e.Type);
                        });

                    this._socket.DisconnectionHappened
                        .Subscribe(e =>
                        {
                            e.CancelReconnection = true;
                            if (e.Exception is not null)
                            {
                                this.OnError?.Invoke(WebSocketError.ConnectionClosedPrematurely, e.Exception.Message);
                            }

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
                if (lockTaken)
                {
                    this._socketSemaphore.Release();
                }
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
            bool lockTaken = false;
            try
            {
                await this._socketSemaphore.WaitAsync();
                lockTaken = true;

                if (this._socket?.IsRunning == true)
                {
                    await this._socket.StopOrFail(webSocketCloseStatus, statusDescription);
                }
            }
            catch (Exception)
            {
                this.OnError?.Invoke(WebSocketError.ConnectionClosedPrematurely, Lang.WebSocketClient_CloseAsync_CloseFailed);
            }
            finally
            {
                if (lockTaken)
                {
                    this._socketSemaphore.Release();
                }
            }
        }

        public void Dispose()
        {
            this._socket?.Dispose();
        }
    }
}
