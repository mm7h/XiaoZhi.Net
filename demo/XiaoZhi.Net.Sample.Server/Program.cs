using XiaoZhi.Net.Sample.Server.FunctionTools;
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
        // 开始初始化服务
        serverHost = serverBuilder.Initialize(config)
            // 添加自定义函数工具
            .WithFunctionTools<GetTime>()
            .WithPrivateFunctionTools<GetWeather>()
            .WithPrivateFunctionTools<MusicPlayer>()
            // 多媒体文件格式支持
            .WithMedia(useFFmpegAudioMixer: true)
            //.WithManageApi("http://localhost:5118", "your-secret")
            // 设置日志输出语言
            .WithCulture("zh-CN")
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