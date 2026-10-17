using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using XiaoZhi.Net.Test.Runtime;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample01_LLMStreamResponse
    {
        public static async Task RunAsync()
        {
            using SampleAiRuntime runtime = SampleAiRuntime.Create();
            ChatClientAgent agent = new(runtime.ChatClient, new ChatClientAgentOptions
            {
                Name = nameof(Sample01_LLMStreamResponse),
                Description = "Streams a simple chat response.",
                ChatOptions = new ChatOptions
                {
                    Temperature = 0.5f,
                    MaxOutputTokens = 50,
                    ResponseFormat = ChatResponseFormat.Text
                }
            });

            AgentSession session = await agent.CreateSessionAsync();
            StringBuilder segmentResponse = new();
            List<OutSegment> allResponse = [];
            Regex sentenceSplitRegex = new(@"(?<![0-9])[.?!;:](?=\s|$)|[。？！；：，]");

            await foreach (AgentResponseUpdate update in agent.RunStreamingAsync("介绍一下四川美食", session))
            {
                string text = (update.Text ?? string.Empty).Replace(Environment.NewLine, string.Empty).Replace("\n", string.Empty);
                segmentResponse.Append(text);
                string currentSegment = segmentResponse.ToString();
                Match match = sentenceSplitRegex.Match(currentSegment);

                while (match.Success)
                {
                    int splitPosition = match.Index + match.Length;
                    string sentence = currentSegment[..splitPosition];
                    string remaining = currentSegment[splitPosition..];
                    OutSegment outSegment = new(sentence) { IsFirst = allResponse.Count == 0 };
                    allResponse.Add(outSegment);
                    Console.WriteLine(sentence);

                    segmentResponse.Clear();
                    segmentResponse.Append(remaining);
                    currentSegment = remaining;
                    match = sentenceSplitRegex.Match(currentSegment);
                }
            }

            if (segmentResponse.Length > 0)
            {
                OutSegment lastSegment = new(segmentResponse.ToString()) { IsFirst = allResponse.Count == 0 };
                allResponse.Add(lastSegment);
                Console.WriteLine(lastSegment.Content);
            }

            if (allResponse.Count > 0)
            {
                allResponse[^1].IsLast = true;
            }

            Console.WriteLine("最后的所有回复：" + string.Join(string.Empty, allResponse.Select(a => a.Content)));
        }
    }
}
