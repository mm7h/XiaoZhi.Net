using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Protocol.WebSocket
{
    internal sealed class WebSocketClient : IDisposable
    {
        private ClientWebSocket WebSocket;
        private readonly CancellationTokenSource CancellationTokenSource = new();
        private CancellationToken CancellationToken => CancellationTokenSource.Token;
        private const int PollBufferSize = 16384;
        private readonly MemoryStream MemoryStream = new();

        private readonly int MaxReconnectAttempts = 3;
        private readonly int ReconnectIntervalSeconds = 5;

        private int _currentReconnectAttempts = 0;
        private bool _isReconnecting = false;

        public WebSocketState State => this.WebSocket.State;
        public ClientWebSocketOptions Options => this.WebSocket.Options;
        public Uri Uri { get; private set; }
        public bool IsOpen => this.State == WebSocketState.Open;

        public WebSocketClient(string endpointUrl, IDictionary<string, string>? headers)
        {
            this.Uri = new Uri(endpointUrl);
            this.WebSocket = new ClientWebSocket();
        }

        #region Events

        public event Action? OnOpen;
        public event Action<WebSocketError, string>? OnError;
        public event Action<WebSocketCloseStatus, string?>? OnClose;
        public event Action<string>? OnTextMessage;
        public event Action<byte[]>? OnBinaryMessage;

        private void ThrowIfCloseError()
        {
            if (!this.IsOpen && this.WebSocket.CloseStatus.HasValue)
            {
                this.OnClose?.Invoke(this.WebSocket.CloseStatus.Value, this.WebSocket.CloseStatusDescription);
            }
        }

        #endregion

        #region Connection

        public async Task ConnectAsync()
        {
            if (this.IsOpen || this.State == WebSocketState.Connecting) return;

            try
            {
                await this.WebSocket.ConnectAsync(this.Uri, this.CancellationToken);

                // 启动消息接收线程
                await Task.Factory.StartNew(async () =>
                {
                    while (this.IsOpen) await this.Poll();
                }, TaskCreationOptions.LongRunning).ConfigureAwait(false);

                // 重置重连计数
                this._currentReconnectAttempts = 0;
                this.OnOpen?.Invoke();
            }
            catch (Exception exception)
            {
                if (exception is WebSocketException wsException)
                {
                    this.OnError?.Invoke(wsException.WebSocketErrorCode, wsException.Message);
                }
                else
                {
                    this.OnError?.Invoke(WebSocketError.Faulted, exception.Message);
                }
                this.ThrowIfCloseError();

                // 尝试重连
                await this.TryReconnectAsync();
            }
        }

        private async Task TryReconnectAsync()
        {
            if (this._isReconnecting) return;

            this._isReconnecting = true;

            try
            {
                while ((this.MaxReconnectAttempts <= 0 || _currentReconnectAttempts < this.MaxReconnectAttempts)
                       && !IsOpen)
                {
                    this._currentReconnectAttempts++;

                    // 等待指定的重连间隔
                    await Task.Delay(TimeSpan.FromSeconds(this.ReconnectIntervalSeconds));

                    try
                    {
                        // 创建新的 WebSocket 实例
                        this.WebSocket.Dispose();
                        this.WebSocket = new ClientWebSocket();

                        // 重新连接
                        await this.WebSocket.ConnectAsync(this.Uri, this.CancellationToken);

                        // 重新启动消息接收线程
                        await Task.Factory.StartNew(async () =>
                        {
                            while (this.IsOpen) await this.Poll();
                        }, TaskCreationOptions.LongRunning).ConfigureAwait(false);

                        // 连接成功，重置计数器
                        this._currentReconnectAttempts = 0;
                        this.OnOpen?.Invoke();
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (ex is WebSocketException wsEx)
                        {
                            this.OnError?.Invoke(wsEx.WebSocketErrorCode,
                                $"Re-try to connect {this._currentReconnectAttempts} times but faliled: {wsEx.Message}");
                        }
                        else
                        {
                            this.OnError?.Invoke(WebSocketError.Faulted,
                                $"Re-try to connect {this._currentReconnectAttempts} times but faliled: {ex.Message}");
                        }
                    }
                }

                if (!this.IsOpen)
                {
                    this.OnError?.Invoke(WebSocketError.Faulted,
                        $"Re-connect failed to max times: {this.MaxReconnectAttempts}");
                }
            }
            finally
            {
                this._isReconnecting = false;
            }
        }

        public async Task CloseAsync(WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure, string? closeMessage = null)
        {
            if (!IsOpen) return;

            try
            {
                // 停止重连尝试
                this._isReconnecting = false;
                this._currentReconnectAttempts = 0;

                await this.WebSocket.CloseAsync(closeStatus, closeMessage, CancellationToken);
                this.OnClose?.Invoke(closeStatus, closeMessage);
            }
            catch (WebSocketException exception)
            {
                this.OnError?.Invoke(exception.WebSocketErrorCode, exception.Message);
                this.ThrowIfCloseError();
            }
            catch (Exception exception)
            {
                this.OnError?.Invoke(WebSocketError.Faulted, exception.Message);
                this.ThrowIfCloseError();
            }
            finally
            {
                this.CancellationTokenSource.Cancel();
            }
        }

        #endregion

        #region Sending

        private async Task InternalSend(ArraySegment<byte> data, WebSocketMessageType messageType = WebSocketMessageType.Binary)
        {
            if (!this.IsOpen) return;

            try
            {
                await this.WebSocket.SendAsync(data, messageType, true, this.CancellationToken);
            }
            catch (WebSocketException exception)
            {
                this.OnError?.Invoke(exception.WebSocketErrorCode, exception.Message);
                this.ThrowIfCloseError();
            }
            catch (Exception ex)
            {
                this.OnError?.Invoke(WebSocketError.Faulted, ex.Message);
                this.ThrowIfCloseError();
            }
        }
        public async Task SendAsync(byte[] data)
            => await InternalSend(new ArraySegment<byte>(data));
        public async Task SendAsync(string text)
            => await InternalSend(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text);

        #endregion

        #region Receiving

        private async Task Poll()
        {
            try
            {
                while (this.IsOpen)
                {
                    WebSocketReceiveResult result = null;
                    this.MemoryStream.SetLength(0); // Reset stream at start of each message

                    do
                    {
                        byte[] buffer = ArrayPool<byte>.Shared.Rent(PollBufferSize);

                        try
                        {
                            result = await WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken);

                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await this.CloseAsync();
                                return;
                            }

                            if (result.Count > 0)
                            {
                                this.MemoryStream.Write(buffer, 0, result.Count);
                            }

                            if (result.EndOfMessage && this.MemoryStream.Length > 0)
                            {
                                switch (result.MessageType)
                                {
                                    case WebSocketMessageType.Text:
                                        string textMessage = Encoding.UTF8.GetString(this.MemoryStream.ToArray());
                                        this.OnTextMessage?.Invoke(textMessage);
                                        break;
                                    case WebSocketMessageType.Binary:
                                        this.OnBinaryMessage?.Invoke(this.MemoryStream.ToArray());
                                        break;
                                }

                            }
                        }
                        catch (WebSocketException exception)
                        {
                            this.OnError?.Invoke(exception.WebSocketErrorCode, exception.Message);
                            if (this.WebSocket.State != WebSocketState.Open)
                            {
                                this.ThrowIfCloseError();
                                return; // Exit if connection is closed
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            return; // WebSocket was disposed, exit gracefully
                        }
                        catch (OperationCanceledException)
                        {
                            return; // Operation was cancelled, exit gracefully
                        }
                        catch (Exception ex)
                        {
                            this.OnError?.Invoke(WebSocketError.Faulted, ex.Message);
                            this.ThrowIfCloseError();
                            return;
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    } while (result != null && !result.EndOfMessage && IsOpen);
                }
            }
            catch (Exception ex)
            {
                this.OnError?.Invoke(WebSocketError.Faulted, $"Internal error: {ex.Message}");
                await this.CloseAsync(WebSocketCloseStatus.InternalServerError, "Internal error");
            }
        }

        public string ReadString(ArraySegment<byte> message) => Encoding.UTF8.GetString(message);

        #endregion

        public void Dispose()
        {
            this.CancellationTokenSource?.Cancel();
            this.WebSocket?.Dispose();
            this.CancellationTokenSource?.Dispose();
            this.MemoryStream?.Dispose();
        }
    }
}
