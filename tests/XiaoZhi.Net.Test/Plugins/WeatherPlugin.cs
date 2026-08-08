using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace XiaoZhi.Net.Test.Plugins
{
    public class WeatherPlugin
    {
        [KernelFunction, Description("根据城市名称获取当天的天气信息")]
        public string GetWeather(string cityName)
        {
            return cityName switch
            {
                "成都" => "61 and rainy",
                "重庆" => "55 and cloudy",
                "西安" => "80 and sunny",
                "兰州" => "60 and rainy",
                "西藏" => "50 and sunny",
                "昆明" => "75 and sunny",
                "贵阳" => "80 and sunny",
                _ => "No information",
            };
        }
    }
}
