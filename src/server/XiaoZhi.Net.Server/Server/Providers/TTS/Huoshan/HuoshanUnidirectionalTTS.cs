using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol.WebSocket;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan
{
    internal class HuoshanUnidirectionalTTS : HuoshanTTS<HuoshanUnidirectionalTTS>, ITts
    {
        private const string AUDIO_ENCODING = "pcm";
        private const int SAMPLE_RATE = 24000;

        private ITtsEventCallback? _ttsEventCallback;

        public HuoshanUnidirectionalTTS(ILogger<HuoshanUnidirectionalTTS> logger) : base(logger)
        {

        }

        public override string ModelName => nameof(HuoshanUnidirectionalTTS);
        public override string ProviderType => "tts";
        public int GetTtsSampleRate() => SAMPLE_RATE;
        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                string? appId = modelSetting.Config.GetConfigValueOrDefault("AppId");
                string? accessToken = modelSetting.Config.GetConfigValueOrDefault("AccessToken");
                string? resourceId = modelSetting.Config.GetConfigValueOrDefault("ResourceId");
                string? speaker = modelSetting.Config.GetConfigValueOrDefault("Speaker");
                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(resourceId) || string.IsNullOrEmpty(speaker))
                {
                    this.Logger.LogWarning("Huoshan unidirectional TTS configuration is incomplete, please check AppId, AccessToken, ResourceId and speaker.");
                    return false;
                }

                this.SpeakerId = speaker;
                this.Save2File = modelSetting.Config.GetConfigValueOrDefault("Save2File", false);

                if (this.Save2File)
                {
                    this.SavePath = modelSetting.Config.GetConfigValueOrDefault("SavePath", Path.Combine(Environment.CurrentDirectory, "data", "tts-cache"));
                    if (!Directory.Exists(this.SavePath))
                        Directory.CreateDirectory(this.SavePath);
                }

                IDictionary<string, string> headers = new Dictionary<string, string>
                {
                    { "X-Api-App-Key", appId },
                    { "X-Api-Access-Key", accessToken },
                    { "X-Api-Resource-Id", resourceId },
                    { "X-Api-Connect-Id", Guid.NewGuid().ToString() }
                };
                this.WebSocketClient = new WebSocketClient(headers);
                //this.WebSocketClient.OnOpen += this.WebSocketClient_OnOpen;
                //this.WebSocketClient.OnBinaryMessage += this.WebSocketClient_OnBinaryMessage;
                //this.WebSocketClient.OnClose += this.WebSocketClient_OnClose;
                //this.WebSocketClient.OnError += this.WebSocketClient_OnError;

                this.Logger.LogInformation("Builded the {providerType} model: {modelName}", this.ProviderType, this.ModelName);

                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to build HuoshanUnidirectionalTTS.");
                return false;
            }
        }

        public void RegisterDevice(string deviceId, string sessionId, ITtsEventCallback callback)
        {
            this._ttsEventCallback = callback;
            this.RegisterDevice(deviceId, sessionId);
        }

        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {

        }

        public override void Dispose()
        {

        }
    }
}
