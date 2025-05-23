using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample02_LLMFunctionCallResponse
    {
        public static async Task Run()
        {
            await TestLLMFunctionResponse();
        }


        static async Task TestLLMFunctionResponse()
        {
            try
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


                ChatTool getMusicNameTool = ChatTool.CreateFunctionTool(
                    functionName: nameof(GetMusicName),
                    functionDescription: "唱歌、听歌、播放音乐的方法。",
                    functionParameters: BinaryData.FromBytes("""
                    {
                        "type": "object",
                        "properties": {
                            "songName": {
                                "type": "string",
                                "description": "歌曲名称，如果用户没有指定具体歌名则为'random', 明确指定的时返回音乐的名字 示例: ```用户:播放两只老虎\n参数：两只老虎``` ```用户:播放音乐 \n参数：random ```"
                            }
                        },
                        "required": ["songName"]
                    }
                    """u8.ToArray()));


                var chatCompletionOptions = new ChatCompletionOptions
                {
                    Temperature = 0.5f,
                    MaxOutputTokenCount = 50,
                    ResponseFormat = ChatResponseFormat.CreateTextFormat(),
                    Tools = { getMusicNameTool }
                };

                var chatClient = openAIClient.GetChatClient(chatModel);

                List<ChatMessage> chatMessages = new List<ChatMessage>
                {
                    ChatMessage.CreateUserMessage("Hello, 来点音乐")
                };

                bool requiresAction;

                do
                {
                    requiresAction = false;
                    ChatCompletion completion = chatClient.CompleteChat(chatMessages, chatCompletionOptions);

                    switch (completion.FinishReason)
                    {
                        case ChatFinishReason.Stop:
                            {
                                // Add the assistant message to the conversation history.
                                chatMessages.Add(new AssistantChatMessage(completion));
                                break;
                            }

                        case ChatFinishReason.ToolCalls:
                            {
                                // First, add the assistant message with tool calls to the conversation history.
                                chatMessages.Add(new AssistantChatMessage(completion));

                                // Then, add a new tool message for each tool call that is resolved.
                                foreach (ChatToolCall toolCall in completion.ToolCalls)
                                {
                                    switch (toolCall.FunctionName)
                                    {

                                        case nameof(GetMusicName):
                                            {
                                                // The arguments that the model wants to use to call the function are specified as a
                                                // stringified JSON object based on the schema defined in the tool definition. Note that
                                                // the model may hallucinate arguments too. Consequently, it is important to do the
                                                // appropriate parsing and validation before calling the function.
                                                using JsonDocument argumentsJson = JsonDocument.Parse(toolCall.FunctionArguments);
                                                bool hasLocation = argumentsJson.RootElement.TryGetProperty("songName", out JsonElement songName);

                                                GetMusicName(songName.GetString());
                                                break;
                                            }

                                        default:
                                            {
                                                // Handle other unexpected calls.
                                                throw new NotImplementedException();
                                            }
                                    }
                                }

                                requiresAction = true;
                                break;
                            }

                        case ChatFinishReason.Length:
                            throw new NotImplementedException("Incomplete model output due to MaxTokens parameter or token limit exceeded.");

                        case ChatFinishReason.ContentFilter:
                            throw new NotImplementedException("Omitted content due to a content filter flag.");

                        case ChatFinishReason.FunctionCall:
                            throw new NotImplementedException("Deprecated in favor of tool calls.");

                        default:
                            throw new NotImplementedException(completion.FinishReason.ToString());
                    }
                } while (requiresAction);



                foreach (ChatMessage message in chatMessages)
                {
                    switch (message)
                    {
                        case UserChatMessage userMessage:
                            Console.WriteLine($"[USER]:");
                            Console.WriteLine($"{userMessage.Content[0].Text}");
                            Console.WriteLine();
                            break;

                        case AssistantChatMessage assistantMessage when assistantMessage.Content.Count > 0:
                            Console.WriteLine($"[ASSISTANT]:");
                            Console.WriteLine($"{assistantMessage.Content[0].Text}");
                            Console.WriteLine();
                            break;

                        case ToolChatMessage:
                            // Do not print any tool messages; let the assistant summarize the tool results instead.
                            break;

                        default:
                            break;
                    }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

        }


        static void GetMusicName(string songName)
        {
            Console.WriteLine("songName: " + songName);
        }
    }
}
