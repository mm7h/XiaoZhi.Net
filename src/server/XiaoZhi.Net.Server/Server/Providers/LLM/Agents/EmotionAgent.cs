using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents
{
    internal class EmotionAgent : BaseAgent<EmotionAgent>, IEmotionAgent
    {
        private Kernel? _kernel;
        private IChatCompletionService? _emotionAgentService;
        private OpenAIPromptExecutionSettings? _chatExecutionSettings;

        private const string EMOTION_AGENT_PROMPT = @"You are an expert emotional tone analyzer for conversational AI. Your task is to analyze ONLY the most recent user message and determine which single emotion from the predefined list best matches the sentiment that the AI assistant should use when replying.

Available emotions (choose exactly one):
Neutral, Happy, Laughing, Funny, Sad, Angry, Crying, Loving, Embarrassed, Surprised, Shocked, Thinking, Winking, Cool, Relaxed, Delicious, Kissy, Confident, Sleepy, Silly, Confused

Instructions:
1. Focus solely on the emotional intent or feeling expressed in the user's message.
2. Consider what emotional tone would make the AI's reply feel most empathetic, natural, and contextually appropriate.
3. Do NOT output explanations, notes, or extra text.
4. Output ONLY the exact emotion name as a single word on one line.

Example:
User: ""I just aced my exam!""
Output: Happy

User: ""This traffic is driving me insane.""
Output: Angry

User: ""My puppy passed away yesterday.""
Output: Sad

Now analyze the following user message:";

        public EmotionAgent(IServiceProvider serviceProvider, ILogger<EmotionAgent> logger) : base(serviceProvider, logger)
        {
        }
        public override string ModelName => nameof(EmotionAgent);
        public override int Order => 10;
        public override bool SupportsStreaming => false;

        public override bool Build(LLMBuildConfig modelSetting)
        {
            try
            {
                this._kernel = modelSetting.Kernel;
                this._emotionAgentService = this.ServiceProvider.GetRequiredKeyedService<IChatCompletionService>($"LLM_{modelSetting.EmotionLLMModelName}");

                this._chatExecutionSettings = new OpenAIPromptExecutionSettings
                {
                    Temperature = 0.5f,
                    MaxTokens = 40,
                    ResponseFormat = ChatResponseFormat.CreateTextFormat(),
                    FunctionChoiceBehavior = FunctionChoiceBehavior.None(),
                    ChatSystemPrompt = EMOTION_AGENT_PROMPT
                };

                this.Prompt = EMOTION_AGENT_PROMPT;

                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to build EmotionAgent.");
                return false;
            }
        }

        public async Task<Emotion> AnalyzeEmotionAsync(string userMessage, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered())
            {
                throw new SessionNotInitializedException();
            }
            if (this._emotionAgentService is null)
            {
                throw new InvalidOperationException("Emotion agent is not builded yet.");
            }
            try
            {
                var clientResult = await this._emotionAgentService.GetChatMessageContentAsync(userMessage, this._chatExecutionSettings, this._kernel, token);

                string content = !string.IsNullOrEmpty(clientResult.Content) ? clientResult.Content : string.Empty;
                string assistantContent = MarkdownCleaner.CleanMarkdown(Regex.Replace(Regex.Unescape(content), @"<think>.*?</think>", string.Empty, RegexOptions.Singleline));

                return this.ParseEmotion(assistantContent);
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogWarning("User canceled the job for {providerType}.", this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Unexpected error(s) for {providerType}.", this.ProviderType);
                throw;
            }
        }

        private Emotion ParseEmotion(string emotionText)
        {
            if (string.IsNullOrWhiteSpace(emotionText))
            {
                return Emotion.Neutral;
            }
            if (Enum.TryParse<Emotion>(emotionText, true, out var emotion))
            {
                return emotion;
            }
            return emotionText.Trim().ToLowerInvariant() switch
            {
                "neutral" => Emotion.Neutral,
                "happy" => Emotion.Happy,
                "laughing" => Emotion.Laughing,
                "funny" => Emotion.Funny,
                "sad" => Emotion.Sad,
                "angry" => Emotion.Angry,
                "crying" => Emotion.Crying,
                "loving" => Emotion.Loving,
                "embarrassed" => Emotion.Embarrassed,
                "surprised" => Emotion.Surprised,
                "shocked" => Emotion.Shocked,
                "thinking" => Emotion.Thinking,
                "winking" => Emotion.Winking,
                "cool" => Emotion.Cool,
                "relaxed" => Emotion.Relaxed,
                "delicious" => Emotion.Delicious,
                "kissy" => Emotion.Kissy,
                "confident" => Emotion.Confident,
                "sleepy" => Emotion.Sleepy,
                "silly" => Emotion.Silly,
                "confused" => Emotion.Confused,
                _ => Emotion.Neutral
            };
        }

        public override void Dispose()
        {
        }
    }
}
