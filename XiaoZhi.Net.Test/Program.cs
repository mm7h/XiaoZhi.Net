using WebSocketSharp.Server;
using XiaoZhi.Net.Server;
using XiaoZhi.Net.Test.Plugins;

namespace XiaoZhi.Net.Test
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            await StartXiaoZhiServer();
        }

        static async Task StartXiaoZhiServer()
        {
            // 获取服务引擎
            IServerEngine serverEngine = EngineFactory.GetServerEngine();
            try
            {
                Console.WriteLine("Hello, Xiao Zhi!");

                string configJson = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "configs", "config.json"));

                // 快速从json文件中获取配置信息
                XiaoZhiConfig? config = Newtonsoft.Json.JsonConvert.DeserializeObject<XiaoZhiConfig>(configJson);
                if (config != null)
                {
#if DEBUG
                    string? apiKey = Environment.GetEnvironmentVariable("OPEN_AI_API_KEY", EnvironmentVariableTarget.User);
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        Console.WriteLine("Please set the environment variable \"OPEN_AI_API_KEY\"");
                        return;
                    }
                    config.LlmSetting.Config.ApiKey = apiKey;
#endif

                    // 开始初始化服务
                    await serverEngine.Initialize(config)
                        // 添加插件
                        .WithPlugin<PlayMusic>(nameof(PlayMusic))
                        .WithPlugin<GetTime>(nameof(GetTime))
                        .WithPlugin<ConversationSummary>(nameof(ConversationSummary))
                        .StartAsync();

                    Console.WriteLine("Type \"exit\" to stop the service.");

                    while (true)
                    {
                        // 输入exit退出
                        string? resKey = Console.ReadLine();
                        if (!string.IsNullOrEmpty(resKey) && resKey.ToLower() == "exit")
                        {
                            break;
                        }
                    }
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
                if (serverEngine.Started)
                {
                    await serverEngine.StopAsync();
                }
                Console.WriteLine("The server stopped.");
            }
        }

    }
}
