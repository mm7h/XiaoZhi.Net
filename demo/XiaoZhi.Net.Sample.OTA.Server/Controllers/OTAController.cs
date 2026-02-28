using Demo.OTA.Server.Helpers;
using Demo.OTA.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace Demo.OTA.Server.Controllers
{
    [ApiController]
    [Route("/xiaozhi/ota")]
    public class OTAController : ControllerBase
    {
        private readonly XiaoZhiOptions _option;
        private readonly ILogger<OTAController> _logger;

        public OTAController(IOptions<XiaoZhiOptions> option, ILogger<OTAController> logger)
        {
            this._option = option.Value;
            this._logger = logger;
        }

        [HttpPost]
        public Task<OtaResponse> PostOtaResponse(OtaRequest request)
        {
            OtaResponse response = new OtaResponse
            {
                Mqtt = new MqttInfo(),
                Websocket = new WebSocketInfo
                {
                    Url = this._option.Websocket.Url,
                    Token = this._option.Websocket.Token
                },
                ServerTime = new ServerTimeInfo
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Timezone = TimeZoneInfo.Local.StandardName,
                    TimezoneOffset = (int)TimeZoneInfo.Local.BaseUtcOffset.TotalMinutes
                },
                Firmware = new FirmwareInfo
                {
                    Version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                              ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                              ?? "1.0.0.0",
                    Url = ""
                }
            };
            this._logger.LogInformation("Received OTA request: {@Request}", request.ToJson());
            return Task.FromResult(response);
        }
    }
}
