using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents
{
    internal class EmotionAgent : BaseAgent<EmotionAgent>, IEmotionAgent
    {
        private Kernel? _kernel;
        private KernelFunction? _emotionFunction;
        private OpenAIPromptExecutionSettings? _chatExecutionSettings;
        private bool _useEmotions = true;

        private const string EMOTION_PROMPT_TEMPLATE = @"<message role=""system"">You are an expert emotional tone analyzer for conversational AI. Your task is to analyze the sentiment of the provided text. If a conversation context is provided (e.g., User: ... Assistant: ...), analyze the sentiment of the Assistant's response to determine which single emotion from the predefined list matches the tone.

Available emotions (choose exactly one):
Neutral, Happy, Laughing, Funny, Sad, Angry, Crying, Loving, Embarrassed, Surprised, Shocked, Thinking, Winking, Cool, Relaxed, Delicious, Kissy, Confident, Sleepy, Silly, Confused

Instructions:
1. Focus solely on the emotional intent or feeling expressed in the message (or response).
2. Consider what emotional tone matches the text.
3. Do NOT output explanations, notes, or extra text.
4. Output ONLY the exact emotion name as a single word on one line.

Example:
User: ""I just aced my exam!""
Output: Happy

User: ""This traffic is driving me insane.""
Output: Angry

User: ""My puppy passed away yesterday.""
Output: Sad

User: ""Hello""
Assistant: ""I am so sorry to hear that.""
Output: Sad

User: ""What's up?""
Assistant: ""Nothing much, just chilling.""
Output: Relaxed

Now analyze the following text:</message>
<message role=""user"">User Message: ""{{$userMessage}}""
Assistant Sentence: ""{{$latestSentence}}""</message>";

        public EmotionAgent(IServiceProvider serviceProvider, ILogger<EmotionAgent> logger) : base(serviceProvider, logger)
        {
        }
        public override string ModelName => nameof(EmotionAgent);
        public override int Order => 11;
        public override bool SupportsStreaming => false;

        public override bool Build(LLMBuildConfig modelSetting)
        {
            try
            {
                this._useEmotions = modelSetting.UseEmotions;
                if (!this._useEmotions)
                {
                    return true;
                }
                this._kernel = modelSetting.Kernel.Clone();
                this._kernel.Plugins.Clear();

                string serviceId = $"LLM_{modelSetting.EmotionLLMModelName}";

                this._chatExecutionSettings = new OpenAIPromptExecutionSettings
                {
                    ServiceId = serviceId,
                    Temperature = 0.5f,
                    MaxTokens = 40,
                    ResponseFormat = ChatResponseFormat.CreateTextFormat(),
                    FunctionChoiceBehavior = FunctionChoiceBehavior.None()
                };


                this._emotionFunction = this._kernel.CreateFunctionFromPrompt(EMOTION_PROMPT_TEMPLATE, this._chatExecutionSettings);
                this.Prompt = EMOTION_PROMPT_TEMPLATE;

                this.Logger.LogInformation(Lang.EmotionAgent_Build_Built, this.ProviderType, this.ModelName, modelSetting.EmotionLLMModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.EmotionAgent_Build_BuildFailed);
                return false;
            }
        }

        public async Task<Emotion> AnalyzeEmotionAsync(string userMessage, string? latestSentence, CancellationToken token)
        {
            if (!this._useEmotions)
            {
                return Emotion.Neutral;
            }
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._kernel is null || this._emotionFunction is null)
            {
                throw new InvalidOperationException(Lang.EmotionAgent_AnalyzeEmotionAsync_AgentNotBuilt);
            }
            if (string.IsNullOrEmpty(latestSentence))
            { 
                return Emotion.Neutral;
            }
            try
            {
                KernelArguments arguments = new KernelArguments(this._chatExecutionSettings)
                {
                    { "userMessage", userMessage },
                    { "latestSentence", latestSentence }
                };

                var functionResult = await this._emotionFunction.InvokeAsync(this._kernel, arguments, token);

                string content = functionResult.GetValue<string>() ?? string.Empty;
                string assistantContent = MarkdownCleaner.CleanMarkdown(Regex.Replace(Regex.Unescape(content), @"<think>.*?</think>", string.Empty, RegexOptions.Singleline));

                return this.ParseEmotion(assistantContent);
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogWarning(Lang.EmotionAgent_AnalyzeEmotionAsync_UserCanceled, this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.EmotionAgent_AnalyzeEmotionAsync_UnexpectedError, this.ProviderType);
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
