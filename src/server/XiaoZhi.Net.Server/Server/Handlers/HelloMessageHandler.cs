using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Management;

namespace XiaoZhi.Net.Server.Handlers
{
    internal class HelloMessageHandler : BaseHandler
    {
        private const string DEFAULT_AUDIO_FORMAT = "opus";
        private readonly ProviderManager _providerManager;
        private readonly HandlerManager _handlerManager;
        public HelloMessageHandler(ProviderManager providerManager, HandlerManager handlerManager, XiaoZhiConfig config,
            ILogger<TextHandler> logger) : base(config, logger)
        {
            this._providerManager = providerManager;
            this._handlerManager = handlerManager;
        }
        public override string HandlerName => nameof(HelloMessageHandler);


        public override bool Build(PrivateProvider privateProvider)
        {
            return true;
        }

        public async Task Handle(JsonObject helloMessage)
        {
            Session session = this.SendOutter.GetSession();

            AudioSetting defaultAudioParams = new AudioSetting(DEFAULT_AUDIO_FORMAT, this.Config.AudioSetting.SampleRate, this.Config.AudioSetting.Channels, this.Config.AudioSetting.FrameDuration);
            HelloMessage defultHelloMessage = new HelloMessage(this.SendOutter.SessionId, this.Config.ServerProtocol.GetDescription().ToLower(), defaultAudioParams);

            if (helloMessage.TryGetPropertyValue("audio_params", out var audioParams) && audioParams is not null)
            {
                JsonObject audioParamsObj = audioParams.AsObject();
                string format = audioParamsObj["format"]?.GetValue<string>() ?? DEFAULT_AUDIO_FORMAT;
                int sampleRate = audioParamsObj["sample_rate"]?.GetValue<int>() ?? 16000;
                int channels = audioParamsObj["channels"]?.GetValue<int>() ?? 1;
                int frameDuration = audioParamsObj["frame_duration"]?.GetValue<int>() ?? 60;

                session.AudioSetting.Format = format;
                session.AudioSetting.SampleRate = sampleRate;
                session.AudioSetting.Channels = channels;
                session.AudioSetting.FrameDuration = frameDuration;
                session.AudioSetting.OutSampleRate = this.Config.AudioSetting.OutSampleRate;

                defultHelloMessage.AudioParams.Format = format;
                defultHelloMessage.AudioParams.SampleRate = sampleRate;
                defultHelloMessage.AudioParams.Channels = channels;
                defultHelloMessage.AudioParams.FrameDuration = frameDuration;
            }
            bool providerInitResult = await this._providerManager.InitializePrivateConfigAsync(session);
            bool handlerInitResult = this._handlerManager.InitializePrivateConfig(session);

            if (providerInitResult && handlerInitResult)
            {
                await this.SendOutter.SendAsync(JsonHelper.Serialize(defultHelloMessage));

                if (helloMessage.TryGetPropertyValue("features", out var features) && features is not null)
                {
                    JsonObject featuresObj = features.AsObject();
                    if (featuresObj.TryGetPropertyValue("mcp", out var mcp) && mcp is not null)
                    {
                        bool isSupportMCP = mcp.GetValue<bool>();
                        if (isSupportMCP)
                        {
                            this._providerManager.BuildMCP(session);
                        }
                    }
                }
            }
            else
            {
                this.Logger.LogError(Lang.HelloMessageHandler_Handle_InitFailed, session.DeviceId);
            }
        }
    }
}
