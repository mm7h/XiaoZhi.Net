using XiaoZhi.Net.Sample.Server.Plugins;
using Microsoft.Extensions.Hosting;
using XiaoZhi.Net.Server;
using XiaoZhi.Net.Server.Abstractions;


Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");

IHost? serverHost = null;
// 获取服务引擎构建器
IServerBuilder serverBuilder = EngineFactory.CreateXiaoZhiServerBuilder();
try
{
    Console.WriteLine("Hello, Xiao Zhi!");

    string configJson = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "configs", "config.json"));

    // 快速从json文件中获取配置信息
    XiaoZhiConfig? config = Newtonsoft.Json.JsonConvert.DeserializeObject<XiaoZhiConfig>(configJson);
    if (config is not null)
    {
#if DEBUG
        string? apiKey = Environment.GetEnvironmentVariable("OPEN_AI_API_KEY", EnvironmentVariableTarget.User);
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.WriteLine("Please set the environment variable \"OPEN_AI_API_KEY\"");
            return;
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

        // 开始初始化服务
        serverHost = serverBuilder.Initialize(config)
            // 添加插件
            .WithPlugin<GetTime>(nameof(GetTime))
            // ffmpeg音频支持
            .InitializeFFmpeg()
            .WithAllMedia(false)
            // 构建服务引擎
            .Build();

        await serverHost.RunAsync();
    }
    else
    {
        Console.WriteLine("Cannot read the config settings.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Got an error: {ex.Message}");
}
finally
{
    if (serverHost is not null)
    {
        await serverHost.StopAsync();
    }
    Console.WriteLine("The server stopped.");
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
}