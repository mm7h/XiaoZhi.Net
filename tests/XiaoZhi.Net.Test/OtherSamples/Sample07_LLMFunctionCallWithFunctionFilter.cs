using System.Reflection;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using XiaoZhi.Net.Test.Plugins;
using XiaoZhi.Net.Test.Runtime;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample07_LLMFunctionCallWithFunctionFilter
    {
        public static async Task RunAsync()
        {
            using SampleAiRuntime runtime = SampleAiRuntime.Create();
            List<AITool> tools =
            [
                CreateFunction(new PlayMusic(), nameof(PlayMusic.GetLocalMusicFiles)),
                CreateFunction(new PlayMusic(), nameof(PlayMusic.Play)),
                CreateFunction(new TimePlugin(), nameof(TimePlugin.GetCurrentTime)),
                CreateFunction(new WeatherPlugin(), nameof(WeatherPlugin.GetWeather)),
                AIFunctionFactory.Create(OpenUrl, new AIFunctionFactoryOptions
                {
                    Name = "open_url",
                    Description = "打开指定网址"
                })
            ];

            ChatClientAgent baseAgent = new(runtime.ChatClient, new ChatClientAgentOptions
            {
                Name = nameof(Sample07_LLMFunctionCallWithFunctionFilter),
                Description = "Demonstrates MAF function invocation middleware.",
                ChatOptions = new ChatOptions
                {
                    Temperature = 0.5f,
                    MaxOutputTokens = 80,
                    ResponseFormat = ChatResponseFormat.Text,
                    ToolMode = ChatToolMode.Auto,
                    Tools = tools
                }
            });

            const string sessionId = "my-test-id";
            AIAgent agent = baseAgent.AsBuilder()
                .Use(async (_, context, next, cancellationToken) =>
                {
                    foreach ((string key, object? value) in context.Arguments)
                    {
                        Console.WriteLine($"arg key: {key}, arg val: {value}");
                    }

                    if (string.Equals(context.Function.Name, "open_url", StringComparison.Ordinal))
                    {
                        Console.WriteLine($"当前获取到 SessionId: {sessionId}，阻止执行 open_url。");
                        return "function调用失败，无法打开网站";
                    }

                    Console.WriteLine($"调用了 function: {context.Function.Name}");
                    return await next(context, cancellationToken);
                })
                .Build();

            AgentSession session = await agent.CreateSessionAsync();
            AgentResponse response = await agent.RunAsync("我本地目录下有哪些音乐文件", session);
            Console.WriteLine(response.Text);
        }

        private static AIFunction CreateFunction(object instance, string methodName)
        {
            MethodInfo method = instance.GetType().GetMethod(methodName)
                ?? throw new InvalidOperationException($"找不到工具方法：{methodName}");
            return AIFunctionFactory.Create(method, instance);
        }

        private static string OpenUrl(string url)
        {
            Console.WriteLine("function调用：" + url);
            return "打开网站成功";
        }
    }
}
