using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Models;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Management;
using XiaoZhi.Net.Server.Protocol.WebSocket;

namespace XiaoZhi.Net.Server.Handlers
{
    internal class HelloMessageHandler : BaseHandler
    {
        private const string DEFAULT_AUDIO_FORMAT = "opus";
        private static readonly int[] s_supportedOpusSampleRates = [8000, 12000, 16000, 24000, 48000];
        private static readonly int[] s_supportedOpusFrameDurations = [10, 20, 40, 60];
        private readonly ProviderManager _providerManager;
        private readonly HandlerManager _handlerManager;
        private readonly FunctionToolManager _functionToolManager;
        public HelloMessageHandler(ProviderManager providerManager, HandlerManager handlerManager, XiaoZhiConfig config,
            FunctionToolManager functionToolManager,
            ILogger<HelloMessageHandler> logger) : base(config, logger)
        {
            this._providerManager = providerManager;
            this._handlerManager = handlerManager;
            this._functionToolManager = functionToolManager;
        }
        public override string HandlerName => nameof(HelloMessageHandler);


        public override bool Build(PrivateProvider privateProvider)
        {
            this.Builded = true;
            return true;
        }

        public async Task HandleAsync(JsonObject helloMessage)
        {
            Session session = this.SendOutter.GetSession();

            if (!WebSocketProtocolValidation.IsSupported(helloMessage, session.ProtocolVersionHeader))
            {
                this.Logger.LogError(Lang.HelloMessageHandler_UnsupportedProtocolVersion);
                await this.SendOutter.CloseSessionAsync(Lang.HelloMessageHandler_UnsupportedProtocolVersion);
                return;
            }

            if (helloMessage.TryGetPropertyValue("audio_params", out var audioParams) && audioParams is not null)
            {
                JsonObject audioParamsObj = audioParams.AsObject();
                string format = audioParamsObj["format"]?.GetValue<string>() ?? DEFAULT_AUDIO_FORMAT;
                int sampleRate = audioParamsObj["sample_rate"]?.GetValue<int>() ?? 16000;
                int channels = audioParamsObj["channels"]?.GetValue<int>() ?? 1;
                int frameDuration = audioParamsObj["frame_duration"]?.GetValue<int>() ?? 60;
                if (!format.Equals(DEFAULT_AUDIO_FORMAT, StringComparison.OrdinalIgnoreCase)
                    || channels != GlobalVariables.AudioProcessingChannels
                    || !s_supportedOpusSampleRates.Contains(sampleRate)
                    || !s_supportedOpusFrameDurations.Contains(frameDuration))
                {
                    this.Logger.LogError(
                        Lang.HelloMessageHandler_Handle_UnsupportedAudioParameters,
                        session.DeviceId, format, sampleRate, channels, frameDuration);
                    return;
                }

                // 上行参数用于解码和输入重采样，不作为混音输出帧时长。
                session.AudioSetting.Format = format;
                session.AudioSetting.SampleRate = sampleRate;
                session.AudioSetting.Channels = channels;
                session.AudioSetting.FrameDuration = frameDuration;
            }

            session.OutputAudioSetting.Format = DEFAULT_AUDIO_FORMAT;
            session.OutputAudioSetting.SampleRate = session.AudioSetting.SampleRate;
            session.OutputAudioSetting.Channels = session.AudioSetting.Channels;
            session.OutputAudioSetting.FrameDuration = this.Config.AudioSetting.FrameDuration;
            session.TextFontCapability = TextFontCapability.Parse(helloMessage);
            HelloMessage defaultHelloMessage = new(this.SendOutter.SessionId, this.Config.Protocol.GetDescription().ToLower(), session.OutputAudioSetting);

            bool providerInitResult = await this._providerManager.OnSessionPropertyInitializingAsync(session);
            bool handlerInitResult = await this._handlerManager.OnSessionPropertyInitializingAsync(session);
            bool functionToolInitResult = await this._functionToolManager.OnSessionPropertyInitializingAsync(session);

            if (providerInitResult && handlerInitResult && functionToolInitResult)
            {
                await this.SendOutter.SendAsync(JsonHelper.Serialize(defaultHelloMessage));
                await this._handlerManager.OnSessionPropertyInitializedAsync(session, helloMessage);
                await this._providerManager.OnSessionPropertyInitializedAsync(session, helloMessage);
                await this._functionToolManager.OnSessionPropertyInitializedAsync(session, helloMessage);
            }
            else
            {
                this.Logger.LogError(Lang.HelloMessageHandler_Handle_InitFailed, session.DeviceId);
            }
        }
    }
}
