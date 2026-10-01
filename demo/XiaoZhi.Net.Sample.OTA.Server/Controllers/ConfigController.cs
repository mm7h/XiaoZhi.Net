using Microsoft.AspNetCore.Mvc;
using XiaoZhi.Net.Sample.OTA.Server.Helpers;
using Microsoft.Extensions.Options;
using Demo.OTA.Server.Models;
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
        private readonly XiaoZhiOptions _options;
        public ConfigController(IWebHostEnvironment env, ILogger<ConfigController> logger, IOptions<XiaoZhiOptions> options)
        {
            this._env = env;
            this._logger = logger;
            this._options = options.Value;
        }

        [HttpGet]
        public XiaoZhiConfig GetConfig()
        {
            this._logger.LogInformation("Got the request to get the xiao zhi config.");
            string configDirectory = string.IsNullOrWhiteSpace(this._options.ConfigDirectory)
                ? Path.GetFullPath(Path.Combine(this._env.ContentRootPath, "..", "XiaoZhi.Net.Sample.Server", "Configs"))
                : Path.GetFullPath(this._options.ConfigDirectory, this._env.ContentRootPath);
            string configPath = Path.Combine(configDirectory, "config.json");
            if (!System.IO.File.Exists(configPath))
            {
                throw new FileNotFoundException($"The file not found: {configPath}");
            }

            return ConfigHelper.Load(configDirectory, this._env.EnvironmentName);
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
