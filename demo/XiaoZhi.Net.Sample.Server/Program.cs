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
    string environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environments.Production;
    string configDirectory = Path.Combine(Environment.CurrentDirectory, "Configs");
    var configurationBuilder = new ConfigurationBuilder()
        .SetBasePath(configDirectory)
        .AddJsonFile("config.json", optional: false, reloadOnChange: false);

    // 按照文件名顺序加载所有 config_*.json 文件
    // 靠后加载的配置会覆盖靠前面的配置
    foreach (string path in Directory.EnumerateFiles(configDirectory, "config_*.json").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
    {
        configurationBuilder.AddJsonFile(Path.GetFileName(path), optional: false, reloadOnChange: false);
    }
    configuration = (ConfigurationRoot)configurationBuilder
        .AddJsonFile($"config.{environmentName}.json", optional: true, reloadOnChange: false)
        .Build();
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
        //    options.ListenUrl = "http://0.0.0.0:8003";
        //    options.PublicExplainUrl = "https://your-public-host/mcp/vision/explain";
        //    options.UploadTokenSigningKey = "replace-with-at-least-32-byte-secret";
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
    if (!Console.IsInputRedirected)
    {
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
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
