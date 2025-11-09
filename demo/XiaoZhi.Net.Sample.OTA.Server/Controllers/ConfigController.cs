using Microsoft.AspNetCore.Mvc;
using XiaoZhi.Net.Server;
using XiaoZhi.Net.Server.Common.Dtos;

namespace XiaoZhi.Net.Sample.OTA.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConfigController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        public ConfigController(IWebHostEnvironment env)
        {
            this._env = env;
        }

        [HttpGet]
        public XiaoZhiConfig GetConfig()
        {
            string configPath = Path.GetFullPath(Path.Combine(this._env.ContentRootPath, "..", "XiaoZhi.Net.Sample.Server", "configs", "config.json"));
            if (!System.IO.File.Exists(configPath))
                throw new FileNotFoundException($"The file not found: {configPath}");

            string configJson = System.IO.File.ReadAllText(configPath);

            XiaoZhiConfig? config = Newtonsoft.Json.JsonConvert.DeserializeObject<XiaoZhiConfig>(configJson);
            if (config is not null)
            {
#if DEBUG
                string? apiKey = Environment.GetEnvironmentVariable("OPEN_AI_API_KEY", EnvironmentVariableTarget.User);
                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new Exception("Please set the environment variable \"OPEN_AI_API_KEY\"");
                }
                config.LlmSettings.First().Config.ApiKey = apiKey;
                if (config.TtsSetting.ModelName == "huoshan-bidirection")
                {
                    config.TtsSetting.Config.AppId = Environment.GetEnvironmentVariable("HuoshanAppId", EnvironmentVariableTarget.User)!;
                    config.TtsSetting.Config.AccessToken = Environment.GetEnvironmentVariable("HuoshanAccessToken", EnvironmentVariableTarget.User)!;
                    config.TtsSetting.Config.ResourceId = "volc.service_type.10029";
                    config.TtsSetting.Config.Speaker = "zh_female_cancan_mars_bigtts";
                }
#endif
                return config;
            }
            else
            {
                throw new Exception("Cannot read the config settings.");
            }
        }
        [HttpGet("private-config")]
        public PrivateModelsConfig GetPrivateConfig(string deviceId, string sessionId)
        {
            Console.WriteLine($"[Info] {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} - Got the request from the device id: {deviceId} and session Id: {sessionId}.");

            string appId = Environment.GetEnvironmentVariable("HuoshanAppId", EnvironmentVariableTarget.User)!;
            string accessToken = Environment.GetEnvironmentVariable("HuoshanAccessToken", EnvironmentVariableTarget.User)!;

            PrivateModelsConfig config = new PrivateModelsConfig
            {
                TtsSetting = new ModelSetting
                {
                    ModelName = "huoshan-bidirection",
                    Config = new
                    {
                        Save2File = true,
                        AppId = appId,
                        AccessToken = accessToken,
                        ResourceId = "volc.service_type.10029",
                        Speaker = "zh_female_cancan_mars_bigtts",
                        SpeechRate = 0,
                        LoudnessRate = 0
                    }
                }
            };

            return config;
        }
    }
}
