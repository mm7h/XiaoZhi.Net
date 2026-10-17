using Figgle.Fonts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using XiaoZhi.Net.Sample.Server.Configs;
using XiaoZhi.Net.Sample.Server.FunctionTools;
using XiaoZhi.Net.Sample.Server.MemoryStore;
using XiaoZhi.Net.Server;
using XiaoZhi.Net.Server.Abstractions;

PrintBanner();

IHost? serverHost = null;
ConfigurationRoot? configuration = null;

try
{
#if DEBUG
    // 开发调试时可直接启动，默认读取现有配置及 Development 覆盖文件。
    string defaultEnvironmentName = Environments.Development;
#else
    string defaultEnvironmentName = Environments.Production;
#endif
    string environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? defaultEnvironmentName;
    configuration = XiaoZhiConfigurationExtensions.LoadConfiguration(args, environmentName);
    XiaoZhiConfig config = configuration.GetXiaoZhiConfig();

    // 获取服务引擎构建器
    IServerBuilder serverBuilder = EngineFactory.CreateXiaoZhiServerBuilder();
    // 开始初始化服务
    serverBuilder.Initialize(config)
        // 使用 SQLite 保存每台设备最近一次会话的记忆
        .WithAgentMemory<SqliteAgentMemory>()
        // 添加自定义函数工具
        .WithFunctionTools<GetTime>()
        .WithPrivateFunctionTools<GetWeather>()
        .WithPrivateFunctionTools<MusicPlayer>()
        .WithPrivateFunctionTools<ExitConversationFunctionTool>()
        .WithPrivateFunctionTools<TtsSettingsFunctionTool>()
        // 多媒体文件格式支持
        .WithMedia(useFFmpegAudioMixer: true)
        // 视觉模块独立管理 HTTP 上传、设备令牌和模型配置；主服务只下发 MCP capability。
        //.WithVision(options =>
        //{
        //    // MCP 视觉模块独立运行，监听本地HTTP端口，接收设备上传的图片并调用模型进行分析。
        //    options.ListenUrl = "http://0.0.0.0:8003";
        //    options.PublicExplainUrl = "https://your-public-host/mcp/vision/explain";
        //    options.UploadTokenSigningKey = "replace-with-at-least-32-byte-secret";
        //    // MCP 视觉模块使用 OpenAI 兼容的模型服务进行图像分析，配置如下：
        //    options.Model.Endpoint = "https://your-openai-compatible-endpoint/v1";
        //    options.Model.ApiKey = "read-from-a-secret-store";
        //    options.Model.ModelName = "your-vision-model";
        //})
        //.WithManageApi("http://localhost:4531", "your-secret")
        //使用 RAG
        //.WithRagVectorStore(KnowledgeBaseBuilder.VectorStore)
        // 设置日志输出语言
        .WithCulture("zh-CN");

    // Host 与 XiaoZhiConfig 使用同一份配置和环境名称。
    serverBuilder.HostBuilder.UseEnvironment(environmentName)
        .ConfigureAppConfiguration((_, builder) =>
        {
            builder.Sources.Clear();
            builder.AddConfiguration(configuration, shouldDisposeConfiguration: false);
        });
    serverHost = serverBuilder.Build();

    await serverHost
        // 构建示例知识库
        //.BuildKnowledgeBase(config, Path.Combine(Environment.CurrentDirectory, "document"))
        .RunAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Got an error: {ex}");
    Environment.ExitCode = 1;
}
finally
{
    serverHost?.Dispose();
    configuration?.Dispose();
    Console.WriteLine("The server stopped.");
#if DEBUG
    if (!Console.IsInputRedirected)
    {
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
#endif
}

static void PrintBanner()
{
    string version = typeof(EngineFactory).Assembly.GetName().Version?.ToString()
        ?? "unknown";
    string serverInfo = $"XiaoZhi.Net.Server v{version} \t by mm7h";
    int consoleWidth;
    try
    {
        consoleWidth = Console.WindowWidth;
    }
    catch (IOException)
    {
        consoleWidth = 80;
    }
    Console.WriteLine(FiggleFonts.Swampland.Render("XiaoZhi.Net"));
    Console.WriteLine($"{new string(' ', Math.Max(0, (consoleWidth - serverInfo.Length) / 2))}{serverInfo}");
    Console.WriteLine();
    Console.WriteLine(new string('=', Math.Max(1, consoleWidth - 1)));
    Console.WriteLine();
}
