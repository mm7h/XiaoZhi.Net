using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using XiaoZhi.Net.Test.Runtime;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample02_LLMFunctionCallResponse
    {
        public static async Task RunAsync()
        {
            using SampleAiRuntime runtime = SampleAiRuntime.Create();
            AIFunction musicTool = AIFunctionFactory.Create(GetMusicName, new AIFunctionFactoryOptions
            {
                Name = nameof(GetMusicName),
                Description = "唱歌、听歌、播放音乐的方法。"
            });

            AIFunction switchAssistantTool = AIFunctionFactory.Create(SwitchAssistantAsync, new AIFunctionFactoryOptions
            {
                Name = nameof(SwitchAssistantAsync),
                Description = "切换到指定的目标 Assistant。"
            });

            ChatClientAgent agent = new(runtime.ChatClient, new ChatClientAgentOptions
            {
                Name = nameof(Sample02_LLMFunctionCallResponse),
                Description = "Demonstrates automatic local function invocation.",
                ChatOptions = new ChatOptions
                {
                    Temperature = 0.5f,
                    MaxOutputTokens = 50,
                    ResponseFormat = ChatResponseFormat.Text,
                    ToolMode = ChatToolMode.Auto,
                    Tools = [musicTool, switchAssistantTool]
                }
            });

            AgentSession session = await agent.CreateSessionAsync();
            AgentResponse response = await agent.RunAsync("Hello, 帮我切换到号码“10086”", session);
            Console.WriteLine(response.Text);
        }

        [Description("返回要播放的歌曲名称；没有指定歌名时返回 random。")]
        private static string GetMusicName([Description("用户指定的歌曲名称。")] string songName = "random")
        {
            Console.WriteLine("songName: " + songName);
            return $"准备播放：{songName}";
        }

        [Description("根据 Assistant 号码切换的方法工具。在不结束当前电话的情况下，将来电切换到另一个专业 Assistant。仅当来电者明确需要其他专业服务时调用，不要用它代替当前 Assistant 直接回答问题。")]
        private static string SwitchAssistantAsync([Description("要切换到的目标 Assistant 拨号号码。")] string targetAssistantNumber, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"已经切换到 {targetAssistantNumber}");
            return $"已经成功切换到 {targetAssistantNumber}";
        }
    }
}
