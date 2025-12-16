using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers.LLM.Plugins;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents
{
    internal class ChatAgent : BaseAgent<ChatAgent>, IChatAgent
    {

        private static readonly Dictionary<Emotion, string> EMOTION_STRATEGIES = new()
        {
            {
                Emotion.Neutral,
                "Current user sentiment: Neutral. Strategy: Respond in a calm, clear, and helpful tone. Keep replies concise and factual. Avoid excessive enthusiasm or emotional language. Use neutral phrasing like 'I see' or 'Understood'. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Happy,
                "Current user sentiment: Happy. Strategy: Match their positive energy with warm, upbeat language. Use phrases like 'That's wonderful!' or 'So glad to hear that!'. Keep it genuine, not exaggerated. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Laughing,
                "Current user sentiment: Laughing. Strategy: Reply in a light and pleasant tone, avoid lecturing and emoticons, and use short, cheerful sentences like 'Great!' or 'Haha, love that!' And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Funny,
                "Current user sentiment: Funny. Strategy: Acknowledge the humor with playful engagement—say 'That’s hilarious!' or 'You’ve got great comedic timing!'. Feel free to add a lighthearted follow-up joke or pun if appropriate. Don’t overexplain the joke. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Sad,
                "Current user sentiment: Sad. Strategy: First, empathize ('I can understand that you're feeling frustrated'), then provide specific support ('Would you like to talk about where you're stuck?'), and avoid vague words like 'don't be sad'. Use gentle, validating language and offer concrete help. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Angry,
                "Current user sentiment: Angry. Strategy: Acknowledge their frustration first ('This really sounds unfair'), stay calm and non-defensive, then pivot to problem-solving ('What part feels most urgent to fix?'). Avoid dismissive phrases like 'calm down' or 'it’s not a big deal'. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Crying,
                "Current user sentiment: Crying. Strategy: Respond with deep empathy and quiet support. Use soft, caring phrases like 'I’m here for you' or 'It’s okay to feel this way'. Offer space, not solutions—ask gently 'Would you like to share what’s hurting?' Avoid rushing them to 'feel better'. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Loving,
                "Current user sentiment: Loving. Strategy: Respond warmly and affectionately. Use kind, appreciative language like 'That’s so sweet of you' or 'You have such a caring heart'. You may keep it respectful and not overly romantic unless context suggests otherwise. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Embarrassed,
                "Current user sentiment: Embarrassed. Strategy: Normalize the feeling gently ('Everyone has those moments!'), reassure them ('It’s totally okay'), and shift focus away from shame. Avoid drawing more attention to the embarrassment. Use light reassurance like 'No worries at all'. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Surprised,
                "Current user sentiment: Surprised. Strategy: Mirror their sense of wonder with phrases like 'Wow, really?' or 'That’s unexpected!'. Show curiosity and engagement. You may avoid sounding skeptical or dismissive. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Shocked,
                "Current user sentiment: Shocked. Strategy: Validate the intensity ('That sounds truly shocking'), give them space to process, and respond with steady calm. Avoid dramatic reactions or pressing for details too quickly. Use supportive phrasing like 'Take your time—I’m listening.' And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Thinking,
                "Current user sentiment: Thinking. Strategy: Encourage reflection without interrupting. Use open-ended prompts like 'What are you pondering?' or 'That’s an interesting point to consider'. Keep tone patient and curious. You may avoid pushing for a quick answer. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Winking,
                "Current user sentiment: Winking. Strategy: Respond with playful subtlety—use light teasing, double meanings, or friendly conspiratorial tone. Phrases like 'I see what you did there' work well. Keep it classy and context-appropriate. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Cool,
                "Current user sentiment: Cool. Strategy: Keep replies smooth, confident, and effortlessly chill. Use laid-back phrases like 'All good', 'No sweat', or 'You’ve got this'. You may  avoid sounding aloof or disengaged. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Relaxed,
                "Current user sentiment: Relaxed. Strategy: Match their calm vibe with soothing, unhurried language. Use phrases like 'Nice and easy', 'Enjoy the moment', or 'Everything’s flowing well'. Avoid introducing urgency or stress. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Delicious,
                "Current user sentiment: Delicious. Strategy: Celebrate the sensory joy! Say things like 'Yum, that sounds amazing!' or 'Your description is making me hungry!'. Use enthusiastic but food-focused language. Don’t shift topic abruptly. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Kissy,
                "Current user sentiment: Kissy. Strategy: Respond with affectionate warmth but maintain appropriate boundaries. Use sweet, friendly phrases like 'Aww, you’re too kind' or 'Sending good vibes back!'. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Confident,
                "Current user sentiment: Confident. Strategy: Reinforce their self-assurance with affirming language like 'You’ve clearly got this under control' or 'Your confidence is inspiring!'. Use strong, positive phrasing. Avoid undermining or over-praising. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Sleepy,
                "Current user sentiment: Sleepy. Strategy: Keep responses short, soft, and low-energy. Use gentle phrasing like 'Time to rest', 'Sweet dreams', or 'Let yourself unwind'. Avoid long explanations or exciting topics. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Silly,
                "Current user sentiment: Silly. Strategy: Lean into the fun! Use exaggerated, playful language, nonsense words, or light absurdity. Phrases like 'Hehe, you goof!' or 'Silliness level: MAXIMUM!' work great. Don’t be serious. And you need to avoid use emoticons, reply in the same language as the user."
            },
            {
                Emotion.Confused,
                "Current user sentiment: Confused. Strategy: Clarify patiently without condescension. Use simple, step-by-step language like 'Let me break this down' or 'Which part feels unclear?'. Reassure them ('It’s totally normal to feel confused') and avoid jargon. And you need to avoid use emoticons, reply in the same language as the user."
            }
        };

        private Kernel? _kernel;
        private IChatCompletionService? _chatAgentService;
        private OpenAIPromptExecutionSettings? _chatExecutionSettings;

        public ChatAgent(IServiceProvider serviceProvider, ILogger<ChatAgent> logger) : base(serviceProvider, logger)
        {
        }
        public override string ModelName => nameof(ChatAgent);
        public override int Order => 10;

        public override bool Build(LLMBuildConfig modelSetting)
        {
            try
            {
                this._kernel = modelSetting.Kernel;
                this._chatAgentService = this.ServiceProvider.GetRequiredKeyedService<IChatCompletionService>($"LLM_{modelSetting.ChatLLMModelName}");

                this._chatExecutionSettings = new OpenAIPromptExecutionSettings
                {
                    Temperature = 0.5f,
                    MaxTokens = 40,
                    ResponseFormat = ChatResponseFormat.CreateTextFormat(),
                    FunctionChoiceBehavior = FunctionChoiceBehavior.None(),
                    ChatSystemPrompt = modelSetting.Prompt
                };
                if (!string.IsNullOrEmpty(modelSetting.SummaryMemory))
                {
                    this.ChatHistory.AddSystemMessage(modelSetting.SummaryMemory);
                }
                this.Prompt = modelSetting.Prompt;
                bool pluginsBuildResult = this.BuildPlugins(modelSetting.Kernel);

                if (pluginsBuildResult)
                {
                    this.Logger.LogInformation("Builded the {providerType} model {modelName}.", this.ProviderType, this.ModelName);
                    return true;
                }
                else
                {
                    this.Logger.LogError("Failed to build the plugins for {providerType} model {modelName}.", this.ProviderType, this.ModelName);
                    return false;
                }
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to build ChatAgent.");
                return false;
            }
        }

        public override void RegisterDevice(string deviceId, string sessionId)
        {
            if (this._kernel is not null)
            {
                foreach (var item in this._kernel.Plugins)
                {
                    if (item is ILLMPlugin llmPlugin)
                    {
                        llmPlugin.RegisterDevice(deviceId, sessionId);
                        this.Logger.LogInformation("LLM plugin {pluginName} registered for device: {deviceId}.", llmPlugin.ModelName, deviceId);
                    }
                }
            }
            base.RegisterDevice(deviceId, sessionId);
        }

        public async Task<string> GenerateChatResponseAsync(string userMessage, Emotion? emotion, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered())
            {
                throw new SessionNotInitializedException();
            }
            if (this._chatAgentService is null)
            {
                throw new InvalidOperationException("Chat agent is not builded yet.");
            }
            if (emotion.HasValue)
            {
                string emotionStrategy = this.ParseEmotion(emotion.Value);
                this.ChatHistory.AddSystemMessage($"Emotion Strategy: {emotionStrategy}");
            }
            this.ChatHistory.AddUserMessage(userMessage);

            var clientResult = await this._chatAgentService.GetChatMessageContentAsync(this.ChatHistory, this._chatExecutionSettings, this._kernel, token);

            string content = !string.IsNullOrEmpty(clientResult.Content) ? clientResult.Content : string.Empty;
            string assistantContent = MarkdownCleaner.CleanMarkdown(Regex.Replace(Regex.Unescape(content), @"<think>.*?</think>", string.Empty, RegexOptions.Singleline));

            this.ChatHistory.AddAssistantMessage(assistantContent);
            return assistantContent;
        }

        public async IAsyncEnumerable<string> GenerateChatResponseStreamingAsync(string userMessage, Emotion? emotion, [EnumeratorCancellation] CancellationToken token)
        {
            if (!this.CheckDeviceRegistered())
            {
                throw new SessionNotInitializedException();
            }
            if (this._chatAgentService is null)
            {
                throw new InvalidOperationException("Chat agent is not builded yet.");
            }
            if (emotion.HasValue)
            {
                string emotionStrategy = this.ParseEmotion(emotion.Value);
                this.ChatHistory.AddSystemMessage($"Emotion Strategy: {emotionStrategy}");
            }
            this.ChatHistory.AddUserMessage(userMessage);

            StringBuilder allResponse = new StringBuilder();
            StringBuilder segmentResponse = new StringBuilder();

            await foreach (var item in this._chatAgentService.GetStreamingChatMessageContentsAsync(this.ChatHistory, this._chatExecutionSettings, this._kernel, token))
            {
                string content = item.Content ?? string.Empty;
                string text = MarkdownCleaner.CleanMarkdown(Regex.Unescape(content));
                segmentResponse.Append(text);
                string currentSegment = segmentResponse.ToString();
                Match match = DialogueHelper.SENTENCE_SPLIT_REGEX.Match(currentSegment);
                while (match.Success)
                {
                    int splitPosition = match.Index + match.Length;
                    string sentence = currentSegment.Substring(0, splitPosition);

                    allResponse.Append(sentence);
                    yield return sentence;

                    string remaining = currentSegment.Substring(splitPosition);
                    segmentResponse.Clear();
                    segmentResponse.Append(remaining);
                    currentSegment = remaining;
                    match = DialogueHelper.SENTENCE_SPLIT_REGEX.Match(currentSegment);
                }
            }

            // 处理LLM回复的内容无法被句子分隔的问题
            if (segmentResponse.Length > 0)
            {
                string sentence = segmentResponse.ToString();
                allResponse.Append(sentence);
                yield return sentence;
            }

            string allContent = allResponse.ToString();
            this.ChatHistory.AddAssistantMessage(allContent);
        }
        private bool BuildPlugins(Kernel kernel)
        {
            #region LocalMusicPlayer
            MusicPlayer musicPlayerPlugin = this.ServiceProvider.GetRequiredService<MusicPlayer>();

            LLMPluginConfig llmPluginConfig = new LLMPluginConfig(kernel);

            if (musicPlayerPlugin.Build(llmPluginConfig))
            {
                string pluginName = musicPlayerPlugin.ModelName;
                kernel.ImportPluginFromObject(musicPlayerPlugin, pluginName);
            }
            else
            {
                return false;
            }
            #endregion

            return true;
        }

        private string ParseEmotion(Emotion emotion)
        {
            return EMOTION_STRATEGIES.TryGetValue(emotion, out string? strategy) ? strategy : EMOTION_STRATEGIES[Emotion.Neutral];
        }

        public override void Dispose()
        {
        }
    }
}
