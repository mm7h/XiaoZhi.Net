using Microsoft.AspNetCore.Mvc;
using XiaoZhi.Net.Server.Common.Dtos;

namespace XiaoZhi.Net.Sample.OTA.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConfigController : ControllerBase
    {
        private readonly string _apiId;
        private readonly string _accessToken;
        private readonly string _resourceId = "volc.service_type.10029";
        private readonly string _speaker = "zh_female_cancan_mars_bigtts";

        public ConfigController()
        {
            this._apiId = Environment.GetEnvironmentVariable("HuoshanAppId", EnvironmentVariableTarget.User)!;
            this._accessToken = Environment.GetEnvironmentVariable("HuoshanAccessToken", EnvironmentVariableTarget.User)!;
        }

        [HttpGet("private-config")]
        public PrivateModelsConfig GetPrivateConfig(string deviceId, string sessionId)
        {
            Console.WriteLine($"[Info] {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} - Got the request from the device id: {deviceId} and session Id: {sessionId}.");

            PrivateModelsConfig config = new PrivateModelsConfig
            {
                TtsSetting = new ModelSetting
                { 
                    ModelName = "huoshan-bidirection",
                    Config = new 
                    {
                        Save2File = true,
                        AppId = this._apiId,
                        AccessToken = this._accessToken,
                        ResourceId = this._resourceId,
                        Speaker = this._speaker,
                        SpeechRate = 0,
                        LoudnessRate = 0
                    }
                }
            };

            return config;
        }
    }
}
