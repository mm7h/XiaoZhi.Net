using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Management;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class HelloMessageHandler : BaseHandler
    {
        private readonly ProviderManager _providerManager;
        public HelloMessageHandler(ProviderManager providerManager, XiaoZhiConfig config,
            ILogger<TextHandler> logger) : base(config, logger)
        {
            this._providerManager = providerManager;
        }
        public override string HandlerName => nameof(HelloMessageHandler);
     

        public override bool Build(PrivateProvider privateProvider)
        {
            return true;
        }

        public void Handle(JsonObject helloMessage)
        {
            Session session = this.SendOutter.GetSession();

            AudioParams defaultAudioParams = new AudioParams(this.Config.AudioSetting.SampleRate, this.Config.AudioSetting.Channels, this.Config.AudioSetting.FrameDuration);
            HelloMessage defultHelloMessage = new HelloMessage(this.SendOutter.SessionId, this.Config.ServerProtocol.GetDescription().ToLower(), defaultAudioParams);

            if (helloMessage.TryGetPropertyValue("audio_params", out var audioParams) && audioParams is not null)
            {
                JsonObject audioParamsObj = audioParams.AsObject();
                string format = audioParamsObj["format"]?.GetValue<string>() ?? "opus";
                int sampleRate = audioParamsObj["sample_rate"]?.GetValue<int>() ?? 16000;
                int channels = audioParamsObj["channels"]?.GetValue<int>() ?? 1;
                int frameDuration = audioParamsObj["frame_duration"]?.GetValue<int>() ?? 60;

                session.AudioSetting.Format = format;
                session.AudioSetting.SampleRate = sampleRate;
                session.AudioSetting.Channels = channels;
                session.AudioSetting.FrameDuration = frameDuration;
                session.IsDeviceBinded = true;
                this._providerManager.BuildAudioPlayer(session);
                this._providerManager.BuildAudioMixer(session);
                this._providerManager.RegisterAudioResampler(session);
                this._providerManager.RegisterAudioEncoder(session);

                defultHelloMessage.AudioParams.Format = format;
                defultHelloMessage.AudioParams.SampleRate = sampleRate;
                defultHelloMessage.AudioParams.Channels = channels;
                defultHelloMessage.AudioParams.FrameDuration = frameDuration;
            }

            this.SendOutter.SendAsync(JsonHelper.Serialize(defultHelloMessage));

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

        public override void Dispose()
        {
        }
    }
}
