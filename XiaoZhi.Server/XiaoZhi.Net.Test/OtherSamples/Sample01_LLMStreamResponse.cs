using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text;
using System.Text.RegularExpressions;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample01_LLMStreamResponse
    {
        public static async Task Run()
        {
            await TestLLMStreamResponse();
        }

        static async Task TestLLMStreamResponse()
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

            var chatClient = openAIClient.GetChatClient(chatModel);

            List<ChatMessage> chatMessages = new List<ChatMessage>
            {
                ChatMessage.CreateUserMessage("介绍一下四川美食")
            };

            var chatCompletionOptions = new ChatCompletionOptions
            {
                Temperature = 0.5f,
                MaxOutputTokenCount = 50,
                ResponseFormat = ChatResponseFormat.CreateTextFormat()
            };

            bool isThinkingFinished = true;
            StringBuilder segmentResponse = new StringBuilder();
            List<OutSegment> allResponse = new List<OutSegment>();
            Regex sentenceSplitRegex = new Regex(@"(?<![0-9])[.?!;:](?=\s|$)|[。？！；：，]");
            await foreach (var item in chatClient.CompleteChatStreamingAsync(chatMessages, chatCompletionOptions))
            {
                string text = (item.ContentUpdate.First().Text ?? "").Replace(Environment.NewLine, string.Empty).Replace("\n", string.Empty);
                segmentResponse.Append(text);

                // 处理流结束的情况
                if (item.FinishReason == ChatFinishReason.Stop && allResponse.Any())
                {
                    OutSegment lastOutSegment = allResponse.Last();
                    lastOutSegment.IsLast = true;
                }

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
                    Console.WriteLine(sentence); // 输出当前分割的句子

                    // 重置累积内容为剩余部分
                    segmentResponse.Clear();
                    segmentResponse.Append(remaining);
                    currentSegment = remaining;
                    match = sentenceSplitRegex.Match(currentSegment);
                }

                // 处理流结束时剩余的文本
                if (item.FinishReason == ChatFinishReason.Stop && segmentResponse.Length > 0)
                {
                    OutSegment lastSegment = new OutSegment(segmentResponse.ToString());
                    if (allResponse.Count == 0) lastSegment.IsFirst = true;
                    lastSegment.IsLast = true;
                    allResponse.Add(lastSegment);
                    Console.WriteLine(segmentResponse.ToString());
                    segmentResponse.Clear();
                }


                //if (text.Contains("<think>"))
                //{
                //    isThinkingFinished = false;
                //    text = text.Split("<think>", StringSplitOptions.RemoveEmptyEntries)[0];
                //}
                //if (text.Contains("</think>"))
                //{
                //    isThinkingFinished = true;
                //    text = text.Split("</think>", StringSplitOptions.RemoveEmptyEntries)[-1];
                //}

                //if (isThinkingFinished)
                //{

                //    if (isFirst)
                //    {

                //    }
                //    else
                //    {

                //    }
                //}
                //else
                //{ 

                //}
            }
            Console.WriteLine("最后的所有回复：" + string.Join(string.Empty, allResponse.Select(a => a.Content)));
        }

    }
}
