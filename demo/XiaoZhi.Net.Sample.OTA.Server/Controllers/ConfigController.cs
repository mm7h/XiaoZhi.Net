using Microsoft.AspNetCore.Mvc;
using XiaoZhi.Net.Server;
using XiaoZhi.Net.Server.Abstractions.Common.Dtos;

namespace XiaoZhi.Net.Sample.OTA.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConfigController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ConfigController> _logger;
        public ConfigController(IWebHostEnvironment env, ILogger<ConfigController> logger)
        {
            this._env = env;
            this._logger = logger;
        }

        [HttpGet]
        public XiaoZhiConfig GetConfig()
        {
            this._logger.LogInformation("Got the request to get the xiao zhi config.");
            string configPath = Path.GetFullPath(Path.Combine(this._env.ContentRootPath, "..", "XiaoZhi.Net.Sample.Server", "configs", "config.json"));
            if (!System.IO.File.Exists(configPath))
                throw new FileNotFoundException($"The file not found: {configPath}");

            string configJson = System.IO.File.ReadAllText(configPath);

            XiaoZhiConfig? config = Newtonsoft.Json.JsonConvert.DeserializeObject<XiaoZhiConfig>(configJson);
            if (config is not null)
            {
                return config;
            }
            else
            {
                throw new Exception("Cannot read the config settings.");
            }
        }
        [HttpGet("private-config")]
        public ApiResponse<PrivateModelsConfig> GetPrivateConfig(string deviceId, string sessionId)
        {
            this._logger.LogInformation($"Got the request from the device id: {deviceId} and session Id: {sessionId}.");

            string ttsAppId = Environment.GetEnvironmentVariable("HuoshanAppId", EnvironmentVariableTarget.User)!;
            string ttsAccessToken = Environment.GetEnvironmentVariable("HuoshanAccessToken", EnvironmentVariableTarget.User)!;

            PrivateModelsConfig config = new PrivateModelsConfig
            {
                //TtsSetting = new ModelSetting
                //{
                //    ModelName = "huoshan-bidirection",
                //    Config = new Dictionary<string, string>
                //    {
                //        ["SaveFile"] = "true",
                //        ["AppId"] = ttsAppId,
                //        ["AccessToken"] = ttsAccessToken,
                //        ["ResourceId"] = "volc.service_type.10029",
                //        ["Speaker"] = "zh_female_cancan_mars_bigtts",
                //        ["SpeechRate"] = "0",
                //        ["LoudnessRate"] = "0"
                //    }
                //}
            };

            return ApiResponse<PrivateModelsConfig>.Success(config);
        }
    }
}
