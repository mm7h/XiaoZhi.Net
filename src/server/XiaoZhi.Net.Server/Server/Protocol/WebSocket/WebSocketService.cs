using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Management;

namespace XiaoZhi.Net.Server.Protocol.WebSocket
{
    internal sealed class WebSocketService : WebSocketBehavior, IBizSendOutter
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly XiaoZhiConfig _config;
        private readonly SessionManager _sessionManager;
        private readonly ProviderManager _providerManager;
        private readonly ILogger _logger;

        private Session? _currentSession;

        public WebSocketService(IServiceProvider serviceProvider, ILogger logger)
        {
            this._serviceProvider = serviceProvider;
            this._logger = logger;
            this._config = this._serviceProvider.GetRequiredService<XiaoZhiConfig>();
            this._sessionManager = this._serviceProvider.GetRequiredService<SessionManager>();
            this._providerManager = this._serviceProvider.GetRequiredService<ProviderManager>();
        }

        #region ISendOutter
        public string SessionId => this._currentSession?.SessionId ?? string.Empty;
        public Session GetSession()
        {
            if (this._currentSession is null)
            {
                throw new SessionNotInitializedException();
            }
            else
            {
                return this._currentSession;
            }
        }
        public Task SendAsync(string json)
        {
            this.Send(json);
            return Task.CompletedTask;
        }
        public Task SendAsync(byte[] opusPacket)
        {
            this.Send(opusPacket);
            return Task.CompletedTask;
        }
        public Task SendTtsMessageAsync(string state, string? text = null)
        {
            if (this._currentSession is null)
            {
                this._logger.LogError("Cannot send TTS message, current session has not been initialized yet.");
                return Task.FromException(new SessionNotInitializedException());
            }
            var msg = new Dictionary<string, string>
            {
                ["type"] = "tts",
                ["state"] = state,
                ["session_id"] = this._currentSession.SessionId
            };

            if (!string.IsNullOrEmpty(text))
            {
                msg["text"] = text;
            }

            string json = JsonHelper.Serialize(msg);

            if (state == "stop")
            {
                this._currentSession.Reset();
            }
            return this.SendAsync(json);
        }
        public Task SendSttMessageAsync(string sttText)
        {
            if (this._currentSession is null)
            {
                this._logger.LogError("Cannot send STT message, current session has not been initialized yet.");
                return Task.FromException(new SessionNotInitializedException());
            }
            var msg = new
            {
                Type = "stt",
                Text = sttText,
                SessionId = this._currentSession.SessionId
            };
            return this.SendAsync(JsonHelper.Serialize(msg));
        }
        public Task SendLlmMessageAsync(Emotion emotion)
        {
            if (this._currentSession is null)
            {
                this._logger.LogError("Cannot send LLM message, current session has not been initialized yet.");
                return Task.FromException(new SessionNotInitializedException());
            }
            var emo = new
            {
                Type = "llm",
                Text = emotion.GetDescription(),
                Emotion = emotion.GetName().ToLower(),
                SessionId = this._currentSession.SessionId
            };
            return this.SendAsync(JsonHelper.Serialize(emo));
        }
        public Task SendAbortMessageAsync()
        {
            if (this._currentSession is null)
            {
                this._logger.LogError("Cannot send LLM message, current session has not been initialized yet.");
                return Task.FromException(new SessionNotInitializedException());
            }
            var abortMessage = new
            {
                type = "tts",
                state = "stop",
                session_id = this._currentSession.SessionId
            };
            return this.SendAsync(JsonHelper.Serialize(abortMessage));
        }
        public Task CloseSessionAsync(string reason = "")
        {
            this.Context.WebSocket.Close(CloseStatusCode.Normal, reason);
            return Task.CompletedTask;
        }
        #endregion

        protected override void OnOpen()
        {
            IDictionary<string, string> headers = new Dictionary<string, string>();
            foreach (string key in this.Context.Headers.AllKeys)
            {
                headers.Add(key.ToLower(), this.Context.Headers[key]);
            }

            string ip = this.Context.UserEndPoint.Address.ToString();
            int port = this.Context.UserEndPoint.Port;

            try
            {
                if (headers.TryGetValue("device-id", out string deviceId) && !string.IsNullOrEmpty(deviceId))
                {

                    bool verifyResult = true;
                    string authToken = headers.TryGetValue("authorization", out string token) ? token : string.Empty;

                    if (this._config.AuthEnabled)
                    {
                        IBasicVerify? basicVerify = this._serviceProvider.GetService<IBasicVerify>();
                        if (basicVerify is not null)
                        {
                            verifyResult = basicVerify.Verify(deviceId, authToken, this.Context.UserEndPoint);
                        }
                    }

                    if (verifyResult)
                    {
                        this._currentSession = this._sessionManager.CreateSession(this.ID, deviceId, authToken, this.Context.UserEndPoint, this);

                        this._providerManager.InitializePrivateConfig(this._currentSession).ConfigureAwait(false);

                        this._currentSession.RefreshLastActivityTime();

                        this._sessionManager.AddSession(this._currentSession.SessionId, this._currentSession);
                        this._logger.LogInformation("New device: {deviceId} with ip {ip} connected", deviceId, ip);
                    }
                    else
                    {
                        this._logger.LogError("The device {deviceId} from ip: {ip} authentication failed.", deviceId, ip);
                        this.Context.WebSocket.Close(CloseStatusCode.Normal, "Authentication failed");
                    }
                }
                else
                {
                    this._logger.LogError("Cannot get the device id from ip: {ip} authentication failed.", ip);
                }
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Failed to process the connection from ip: {ip}.", ip);
            }
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            if (this._currentSession is not null)
            {
                if (e.IsBinary)
                {
                    this._currentSession.HandlerPipeline.HandleBinaryMessage(e.RawData).ConfigureAwait(false);
                }
                else if (e.IsText)
                {
                    this._currentSession.HandlerPipeline.HandleTextMessage(e.Data);
                }
                else if (e.IsPing)
                {

                }
                else
                {
                    this._logger.LogWarning("Received message from uninitialized session. Message: {message}", e.Data);
                }
            }
            else
            {
                this._logger.LogWarning("The session has not been initialized yet.");
            }
        }

        protected override void OnClose(CloseEventArgs e)
        {
            if (this._currentSession is not null)
            {
                this._providerManager.SaveMemoryAsync(this._currentSession).ConfigureAwait(false);

                this._currentSession.Release();
                this._sessionManager.RemoveSession(this._currentSession.SessionId);

                this._logger.LogDebug("Client offline, device id: {deviceId} and session id: {sessionId}.", this._currentSession.DeviceId, this._currentSession.SessionId);
            }
        }
    }
}
