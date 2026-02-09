using XiaoZhi.Net.Sample.Server.Plugins;
using Microsoft.Extensions.Hosting;
using XiaoZhi.Net.Server;
using XiaoZhi.Net.Server.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;


Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");

IHost? serverHost = null;
// 获取服务引擎构建器
IServerBuilder serverBuilder = EngineFactory.CreateXiaoZhiServerBuilder();
try
{
    Console.WriteLine("Hello, Xiao Zhi!");

    string configJson = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "configs", "config.json"));

    // 快速从json文件中获取配置信息
    var options = new JsonSerializerOptions
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true
    };
    options.Converters.Add(new LenientStringConverter());
    XiaoZhiConfig? config = JsonSerializer.Deserialize<XiaoZhiConfig>(configJson, options);

    if (config is not null)
    {
#if DEBUG
        string? apiKey = Environment.GetEnvironmentVariable("OPEN_AI_API_KEY", EnvironmentVariableTarget.User);
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.WriteLine("Please set the environment variable \"OPEN_AI_API_KEY\"");
            return;
        }
        config.ConfiguredSettings["LLM"][config.SelectedSettings.GetValueOrDefault("ChatLLM", "ChatGlm")]["ApiKey"] = "sk-2e9a2711d0524f4482b7a6fac345504c";
        if (config.SelectedSettings["TTS"].StartsWith("Huoshan"))
        {
            config.ConfiguredSettings["TTS"][config.SelectedSettings.GetValueOrDefault("TTS", "HuoshanBidirection")]["AppId"] = Environment.GetEnvironmentVariable("HuoshanAppId", EnvironmentVariableTarget.User)!;
            config.ConfiguredSettings["TTS"][config.SelectedSettings.GetValueOrDefault("TTS", "HuoshanBidirection")]["AccessToken"] = Environment.GetEnvironmentVariable("HuoshanAccessToken", EnvironmentVariableTarget.User)!;
        }
#endif

        // 开始初始化服务
        serverHost = serverBuilder.Initialize(config)
            // 添加插件
            .WithPlugin<GetTime>(nameof(GetTime))
            // 多媒体文件格式支持
            .WithAllMedia(useFFmpegAudioMixer: true)
            //.WithManageApi("http://localhost:5118", "your-secret")
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

public class LenientStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Number)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return doc.RootElement.ToString();
        }
        if (reader.TokenType is JsonTokenType.True) return "true";
        if (reader.TokenType is JsonTokenType.False) return "false";
        if (reader.TokenType is JsonTokenType.StartObject || reader.TokenType is JsonTokenType.StartArray)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return doc.RootElement.GetRawText();
        }
        return reader.GetString()!;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}