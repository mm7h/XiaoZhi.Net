using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text;
using System.Text.RegularExpressions;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample03_KernelLLMStreamResponse
    {
        public static async Task Run()
        {
            await TestKernelLLMStreamResponse();
        }


        static async Task TestKernelLLMStreamResponse()
        {
            string endPoint = "https://open.bigmodel.cn/api/paas/v4/";
            string apiKey = Environment.GetEnvironmentVariable("OPEN_AI_API_KEY", EnvironmentVariableTarget.User)!;
            string chatModel = "glm-4";
            OpenAIClientOptions options = new OpenAIClientOptions
            {
                Endpoint = new Uri(endPoint),
                ProjectId = "Xiao Zhi Test"
            };
            OpenAIClient openAIClient = new OpenAIClient(new ApiKeyCredential(apiKey), options);

            IKernelBuilder builder = Kernel.CreateBuilder();

            var chatCompletionOptions = new OpenAIPromptExecutionSettings
            {
                Temperature = 0.5f,
                MaxTokens = 80,
                ResponseFormat = ChatResponseFormat.CreateTextFormat()
            };

            builder.AddOpenAIChatCompletion(chatModel, openAIClient);

            Kernel kernel = builder.Build();
            IChatCompletionService chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

            ChatHistory chatHistory = new ChatHistory();
            chatHistory.AddUserMessage("介绍一下四川美食。");
            StringBuilder segmentResponse = new StringBuilder();
            List<OutSegment> allResponse = new List<OutSegment>();
            Regex sentenceSplitRegex = new Regex(@"(?<![0-9])[.?!;:](?=\s|$)|[。？！；：，]");

            await foreach (var item in chatCompletionService.GetStreamingChatMessageContentsAsync(chatHistory, chatCompletionOptions, kernel))
            {
                string text = (item.Content ?? string.Empty).Replace(Environment.NewLine, string.Empty).Replace("\n", string.Empty);
                segmentResponse.Append(text);

                // 在累积的文本中查找分割点
                string currentSegment = segmentResponse.ToString();
                Match match = sentenceSplitRegex.Match(currentSegment);

                while (match.Success)
                {
                    int splitPosition = match.Index + match.Length;
                    string sentence = currentSegment.Substring(0, splitPosition);
                    string remaining = currentSegment.Substring(splitPosition);

                    OutSegment outSegment = new OutSegment(sentence);
                    if (allResponse.Count == 0) outSegment.IsFirst = true;

                    allResponse.Add(outSegment);

                    // 重置累积内容为剩余部分
                    segmentResponse.Clear();
                    segmentResponse.Append(remaining);
                    currentSegment = remaining;
                    match = sentenceSplitRegex.Match(currentSegment);
                }

            }

            // 处理流结束的情况
            if (allResponse.Any())
            {
                OutSegment lastOutSegment = allResponse.Last();
                lastOutSegment.IsLast = true;
            }
            else
            {
                // 处理LLM回复的内容无法被句子分隔的问题
                if (segmentResponse.Length > 0)
                {
                    OutSegment segment = new OutSegment(segmentResponse.ToString());
                    segment.IsFirst = true;
                    segment.IsLast = true;
                }
            }
            segmentResponse.Clear();
            Console.WriteLine("最后的所有回复：" + string.Join(string.Empty, allResponse.Select(a => a.Content)));
        }

    }
}
