using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Common.Models;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server.Protocol.WebSocket
{
    internal sealed class WebSocketEngine : IProtocolEngine
    {
        private readonly WebSocketOption _webSocketOption;
        private readonly IStore _connectionStore;
        private readonly ILogger _logger;
        private string? _path;
        private WebSocketServer? _server;
        private WebSocketSessionManager? _webSocketSessionManager;

        public WebSocketEngine(WebSocketOption webSocketOption, IStore store, ILogger logger)
        {
            this._webSocketOption = webSocketOption;
            this._connectionStore = store;
            this._logger = logger;
        }

        public bool Started => this._server?.IsListening ?? false;

        public IProtocolService Service { get; private set; }

        public void Initialize()
        {
            if (this._webSocketOption == null)
            {
                throw new ArgumentNullException(nameof(this._webSocketOption));
            }
            string url = this._webSocketOption.Url;
            bool isWss = url.ToLower().StartsWith("wss");
            this._path = this._webSocketOption.Path;
            this._server = new WebSocketServer(url)
            {
                //KeepClean = true
            };
            if (isWss)
            {
                WssOption? wssOption = this._webSocketOption.WssOption;
                if (wssOption == null)
                {
                    throw new ArgumentNullException(nameof(wssOption));
                }
                this._server.SslConfiguration.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(wssOption.CertFilePath, wssOption.CertPassword);
            }

            WebSocketService webSocketService = new WebSocketService();
            this.Service = webSocketService;
            _server.Log.Level = LogLevel.Debug;
            _server.Log.File = "server.log";
            _server!.AddWebSocketService(this._path, () => webSocketService);
        }

        public Task StartAsync()
        {
            this._server!.Start();
            this._logger.Information($"Server started and listing on: {(this._server.IsSecure ? "wss://" : "ws://")}{this.GetLocalIP()}:{this._server.Port}{this._path}");
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            this._server!.Stop();
            return Task.CompletedTask;
        }
        public Task SendAsync(string connId, string json)
        {
            this.GetWebSocketSessionManager().SendTo(json, connId);
            this._logger.Debug($"Sent json to client: {json}");
            return Task.CompletedTask;

        }
        public Task SendAsync(string connId, byte[] opusPacket)
        {
            this.GetWebSocketSessionManager().SendTo(opusPacket, connId);
            return Task.CompletedTask;
        }

        public Task SendTtsMessageAsync(string sessionId, string state, string? text = null)
        {
            var msg = new Dictionary<string, string>
            {
                ["type"] = "tts",
                ["state"] = state,
                ["session_id"] = sessionId
            };

            if (!string.IsNullOrEmpty(text))
            {
                msg["text"] = text;
            }

            string json = JsonSerializer.Serialize(msg);

            if (state == "stop")
            {
                Session sessionContext = GetSessionContext(sessionId);
                if (sessionContext == null)
                    return Task.CompletedTask;
                sessionContext.Reset();
            }
            return this.SendAsync(sessionId, json);
        }
        public Task SendLlmMessageAsync(string sessionId, Emotion emotion)
        {
            var emo = new Dictionary<string, string>
            {
                ["type"] = "llm",
                ["text"] = emotion.GetDescription(),
                ["emotion"] = emotion.GetName().ToLower(),
                ["session_id"] = sessionId
            };
            return this.SendAsync(sessionId, JsonSerializer.Serialize(emo));
        }
        public Task SendSttMessageAsync(string sessionId, string sttText)
        {
            var msg = new Dictionary<string, string>
            {
                ["type"] = "stt",
                ["text"] = sttText,
                ["session_id"] = sessionId
            };
            return this.SendAsync(sessionId, JsonSerializer.Serialize(msg));
        }
        public void AddSessionContext(string sessionId, Session sessionContext)
        {
            this._connectionStore.Add(sessionId, sessionContext);
        }
        public IDictionary<string, SessionDevice> GetAllSessions()
        {
            return this._connectionStore.GetAll<Session>().ToDictionary(k => k.Key, v => new SessionDevice(v.Value.SessionId, v.Value.DeviceId, v.Value.EndPoint));
        }
        public Session GetSessionContext(string sessionId)
        {
            return this._connectionStore.Get<Session>(sessionId);
        }
        public void UpdateSessionContext(string sessionId, Session newSession)
        {
            this._connectionStore.Update(sessionId, newSession);
        }
        public void RemoveSessionContext(string sessionId)
        {
            this._connectionStore.Remove(sessionId);
        }
        public void CloseSession(string sessionId)
        {
            this.GetWebSocketSessionManager().CloseSession(sessionId);
            this.RemoveSessionContext(sessionId);
        }

        private WebSocketSessionManager GetWebSocketSessionManager()
        {
            if (this._webSocketSessionManager == null)
            {
                this._webSocketSessionManager = this._server!.WebSocketServices.Hosts.First().Sessions;
            }
            return this._webSocketSessionManager;
        }

        private string GetLocalIP()
        {
            string hostName = Dns.GetHostName();
            return Dns.GetHostAddresses(hostName).FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "127.0.0.1";
        }
    }
}
