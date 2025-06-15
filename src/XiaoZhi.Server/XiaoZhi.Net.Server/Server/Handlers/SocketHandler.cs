using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Channels;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class SocketHandler : BaseHandler, IOutHandler<byte[]>
    {
        private readonly AuthHandler _authHandler;
        private readonly IProtocolEngine _protocolEngine;
        public SocketHandler(AuthHandler authHandler, IProtocolEngine protocolEngine, XiaoZhiConfig config, ILogger logger) : base(config, logger)
        {
            this._authHandler = authHandler;
            this._protocolEngine = protocolEngine;

            this._protocolEngine.OnConnecting += this.DeviceConnecting;
            this._protocolEngine.OnTextMessage += this.HandleTextMessage;
            this._protocolEngine.OnBinaryMessage += this.HandleBinaryMessage;
            this._protocolEngine.OnConnectionClose += this.HandleConnectionClose;
        }

        public event Action<Session> OnDeviceConnected;
        public event Action<string, string> OnTextPacket;

        public override string HandlerName => nameof(SocketHandler);

        public ChannelWriter<Workflow<byte[]>> NextWriter { get; set; }

        private bool DeviceConnecting(string sessionId, IDictionary<string, string> headers, IPEndPoint userEndPoint)
        {

            string ip = userEndPoint.Address.ToString();
            int port = userEndPoint.Port;
            try
            {
                bool checkResult = this._authHandler.Handle(headers, userEndPoint);

                if (checkResult && headers.TryGetValue("device-id", out string deviceId))
                {
                    this.Logger.Information("New device: {deviceId} with ip {ip} connected", deviceId, ip);

                    /*
                     private config
                     */
                    Session sessionContext = new Session(sessionId, deviceId, userEndPoint);
                    this.OnDeviceConnected.Invoke(sessionContext);

                    this._protocolEngine.AddSessionContext(sessionId, sessionContext);
                    return true;
                }
                else
                {
                    this.Logger.Error("The device from ip: {ip} authentication failed.", ip);
                    return false;
                }
            }
            catch (Exception ex)
            {
                this.Logger.Debug(ex, "Failed to process the connection from ip: {ip}, error: {message}.", ip, ex.Message);
                this.Logger.Error("Failed to process the connection from ip: {ip}.", ip);
                return false;
            }
        }

        private void HandleTextMessage(string connId, string data)
        {
            this.OnTextPacket.Invoke(connId, data);
        }
        private async void HandleBinaryMessage(string connId, byte[] data)
        {
            Session session = this._protocolEngine.GetSessionContext(connId);
            if (session == null || session.ShouldIgnore())
            {
                return;
            }
            try
            {
                if (!session.IsIdle)
                {
#if DEBUG
                    this.Logger.Debug("The previous audio packet is processing, this packet would be ignored, frame size {length}.", data.Length);
#endif
                    return;
                }

                await this.NextWriter.WriteAsync(new Workflow<byte[]>(connId, data));
            }
            catch (Exception ex)
            {
                this.Logger.Debug(ex, "Failed to process the message packet from device: {deviceId} and session id: {sessionId}, error: {message}.", session.DeviceId, session.SessionId, ex.Message);
                this.Logger.Error("Failed to process the message packet from device: {deviceId} and session id: {sessionId}.", session.DeviceId, session.SessionId);
            }
        }
        public void HandleConnectionClose(string connId)
        {
            Session session = this._protocolEngine.GetSessionContext(connId);
            if (session != null)
            {
                this.Logger.Debug("Client offline, device id: {deviceId} and session id: {sessionId}.", session.DeviceId, session.SessionId);
                session.Release();
                this._protocolEngine.RemoveSessionContext(connId);
                //todo: save the mermory
            }
        }
        public void Dispose()
        {
            this.NextWriter.Complete();
        }

    }
}
