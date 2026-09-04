using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.Common.Attributes;
using XiaoZhi.Net.Server.Abstractions.Common.Contexts;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Sample.Server.FunctionTools
{
    internal class GetWeather : PrivateFunctionTool
    {
        public override ValueTask OnFunctionToolInitializedAsync()
        {
            this.Logger.LogInformation("GetWeather function tool initialized.");
            return base.OnFunctionToolInitializedAsync();
        }
        public override ValueTask OnFunctionToolReleasedAsync()
        {
            this.Logger.LogInformation("GetWeather function tool released.");
            return base.OnFunctionToolReleasedAsync();
        }
        public override ValueTask OnSessionConnectedAsync()
        {
            this.Logger.LogInformation("Session connected to GetWeather function tool.");
            return base.OnSessionConnectedAsync();
        }
        public override ValueTask OnSessionClosedAsync()
        {
            this.Logger.LogInformation("Session closed from GetWeather function tool.");
            return base.OnSessionClosedAsync();
        }

        [ToolBehavior(ToolAction.DirectResponse)]
        [System.ComponentModel.Description("根据城市名称查询当前天气信息")]
        public FunctionReturn<string> GetWeatherInfo(string city)
        {
            // Here you would implement the logic to get weather information for the specified city.
            // For demonstration purposes, we'll return a mock response.
            string weatherInfo = $"The current weather in {city} is sunny with a temperature of 25°C.";
            this.Logger.LogInformation($"Retrieved weather information for {city}: {weatherInfo}");
            return new FunctionReturn<string>
            {
                Result = weatherInfo,
                Response = weatherInfo
            };
        }
    }
}
