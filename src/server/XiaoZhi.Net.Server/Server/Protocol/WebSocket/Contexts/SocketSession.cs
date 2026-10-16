using Microsoft.Extensions.Logging;
using System;
using SuperSocket.WebSocket;
using SuperSocket.WebSocket.Server;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Management;
using System.Text.Json.Nodes;
using XiaoZhi.Net.Server.Resources.Fonts;

namespace XiaoZhi.Net.Server.Protocol.WebSocket.Contexts
{
    internal class SocketSession : WebSocketSession, IBizSendOutter
    {
        private readonly HandlerManager _handlerManager;
        private readonly ProviderManager _providerManager;
        private readonly FunctionToolManager _functionToolManager;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly GlyphResource _glyphResource;

        public SocketSession(
            HandlerManager handlerManager,
            ProviderManager providerManager,
            FunctionToolManager functionToolManager,
            IServiceScopeFactory serviceScopeFactory,
            GlyphResource glyphResource)
        {
            this._handlerManager = handlerManager;
            this._providerManager = providerManager;
            this._functionToolManager = functionToolManager;
            this._serviceScopeFactory = serviceScopeFactory;
            this._glyphResource = glyphResource;
        }

        public Session? XiaoZhiSession { get; set; }

        #region ISendOutter
        public string SessionId => this.SessionID;
        public Session GetSession()
        {
            if (this.XiaoZhiSession is null)
            {
                throw new SessionNotInitializedException();
            }
            else
            {
                return this.XiaoZhiSession;
            }
        }
        public new Task SendAsync(string json)
        {
            this.Logger.LogDebug(Lang.SocketSession_SendAsync_SendingJson, this.XiaoZhiSession?.DeviceId, Regex.Unescape(!string.IsNullOrWhiteSpace(json) ? json : string.Empty));
            return base.SendAsync(json).AsTask();
        }
        public Task SendAsync(byte[] opusPacket)
        {
            return base.SendAsync(opusPacket).AsTask();
        }
        public Task SendTtsMessageAsync(TtsStatus state, string? text = null)
        {
            if (this.XiaoZhiSession is null || this.XiaoZhiSession.PrivateProvider.Tts is null)
            {
                this.Logger.LogError(Lang.SocketSession_SendTtsMessageAsync_SessionNotInitialized);
                return Task.FromException(new SessionNotInitializedException());
            }
            var msg = new Dictionary<string, object>
            {
                ["type"] = "tts",
                ["state"] = state.GetDescription(),
                ["session_id"] = this.SessionId
            };

            if (state == TtsStatus.Start)
            {
                msg["sample_rate"] = this.XiaoZhiSession.OutputAudioSetting.SampleRate;
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                msg["text"] = text;
            }

            JsonObject message = JsonHelper.ToNode(msg)!.AsObject();
            if (state == TtsStatus.SentenceStart && !string.IsNullOrWhiteSpace(text))
            {
                this.AttachGlyphs(message, text);
            }
            string json = JsonHelper.Serialize(message);

            if (state == TtsStatus.Stop)
            {
                this.XiaoZhiSession.CompletePlayback();
            }
            return this.SendAsync(json);
        }
        public Task SendSttMessageAsync(string sttText)
        {
            if (this.XiaoZhiSession is null)
            {
                this.Logger.LogError(Lang.SocketSession_SendSttMessageAsync_SessionNotInitialized);
                return Task.FromException(new SessionNotInitializedException());
            }
            var msg = new
            {
                Type = "stt",
                Text = sttText,
                SessionId = this.SessionId
            };
            JsonObject message = JsonHelper.ToNode(msg)!.AsObject();
            this.AttachGlyphs(message, sttText);
            return this.SendAsync(JsonHelper.Serialize(message));
        }
        private void AttachGlyphs(JsonObject message, string text)
        {
            JsonObject? glyphs = this._glyphResource.CreatePayload(this.XiaoZhiSession?.TextFontCapability, text);
            if (glyphs is not null)
            {
                message["glyph_push"] = glyphs;
            }
        }
        public Task SendLlmMessageAsync(Emotion emotion)
        {
            if (this.XiaoZhiSession is null)
            {
                this.Logger.LogError(Lang.SocketSession_SendLlmMessageAsync_SessionNotInitialized);
                return Task.FromException(new SessionNotInitializedException());
            }
            var emo = new
            {
                Type = "llm",
                Text = emotion.GetDescription(),
                Emotion = emotion.GetName().ToLower(),
                SessionId = this.SessionId
            };
            return this.SendAsync(JsonHelper.Serialize(emo));
        }
        public Task SendAbortMessageAsync()
        {
            if (this.XiaoZhiSession is null)
            {
                this.Logger.LogError(Lang.SocketSession_SendAbortMessageAsync_SessionNotInitialized);
                return Task.FromException(new SessionNotInitializedException());
            }
            var abortMessage = new
            {
                type = "tts",
                state = "stop",
                session_id = this.SessionId
            };
            return this.SendAsync(JsonHelper.Serialize(abortMessage));
        }
        public Task CloseSessionAsync(string reason = "")
        {
            return this.CloseAsync(CloseReason.NormalClosure, reason).AsTask();
        }
        #endregion

        protected override async ValueTask OnSessionConnectedAsync()
        {
            string deviceId = this.HttpHeader.Items.Get("device-id")!;
            string token = this.HttpHeader.Items.Get("authorization")!;
            IPEndPoint userEndPoint = (this.RemoteEndPoint as IPEndPoint)!;

            IServiceScope serviceScope = this._serviceScopeFactory.CreateScope();
            Session session = new Session(
                this.SessionId,
                deviceId,
                token,
                userEndPoint,
                this,
                serviceScope);
            if (this.LocalEndPoint is IPEndPoint localEndPoint)
            {
                session.SetLocalEndPoint(localEndPoint);
            }
            this.XiaoZhiSession = session;
            session.ProtocolVersionHeader = this.HttpHeader.Items.Get("protocol-version");

            await this._handlerManager.OnSessionConnectedAsync(session);
            await this._providerManager.OnSessionConnectedAsync(session);
            await this._functionToolManager.OnSessionConnectedAsync(session);
            session.RefreshLastActivityTime();
        }

        protected override async ValueTask OnSessionClosedAsync(SuperSocket.Connection.CloseEventArgs e)
        {
            if (this.XiaoZhiSession is not null)
            {
                try
                {
                    await this._handlerManager.OnSessionClosedAsync(this.XiaoZhiSession);
                    await this._providerManager.OnSessionClosedAsync(this.XiaoZhiSession);
                    await this._functionToolManager.OnSessionClosedAsync(this.XiaoZhiSession);
                }
                finally
                {
                    await this.XiaoZhiSession.ReleaseAsync();
                }

                this.Logger.LogDebug(Lang.SocketSession_OnSessionClosedAsync_ClientOffline, this.XiaoZhiSession.DeviceId, this.XiaoZhiSession.SessionId, e.Reason);
            }
        }
    }
}
