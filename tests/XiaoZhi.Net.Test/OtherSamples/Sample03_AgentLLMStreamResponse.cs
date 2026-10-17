using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using XiaoZhi.Net.Test.Runtime;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample03_AgentLLMStreamResponse
    {
        public static async Task RunAsync()
        {
            using SampleAiRuntime runtime = SampleAiRuntime.Create();
            ChatClientAgent agent = new(runtime.ChatClient, new ChatClientAgentOptions
            {
                Name = nameof(Sample03_AgentLLMStreamResponse),
                Description = "Streams a response through a MAF agent session.",
                ChatOptions = new ChatOptions
                {
                    Instructions = "请用简洁中文回答。",
                    Temperature = 0.5f,
                    MaxOutputTokens = 80,
                    ResponseFormat = ChatResponseFormat.Text
                }
            });

            AgentSession session = await agent.CreateSessionAsync();
            await foreach (AgentResponseUpdate update in agent.RunStreamingAsync("介绍一下四川美食。", session))
            {
                Console.Write(update.Text);
            }

            Console.WriteLine();
        }
    }
}
