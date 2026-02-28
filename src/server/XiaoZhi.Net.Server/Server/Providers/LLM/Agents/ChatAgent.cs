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
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.LLM.Plugins;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents
{
    internal class ChatAgent : BaseAgent<ChatAgent>, IChatAgent
    {
        private Kernel? _kernel;
        private IChatCompletionService? _chatAgentService;
        private OpenAIPromptExecutionSettings? _chatExecutionSettings;

        public ChatAgent(IServiceProvider serviceProvider, ILogger<ChatAgent> logger) : base(serviceProvider, logger)
        {
        }

        public bool UseStreaming { get; private set; }
        public override string ModelName => nameof(ChatAgent);
        public override int Order => 10;

        public override bool Build(LLMBuildConfig modelSetting)
        {
            try
            {
                this._kernel = modelSetting.Kernel;
                this.UseStreaming = modelSetting.UseStreaming;
                this._chatAgentService = this.ServiceProvider.GetRequiredKeyedService<IChatCompletionService>($"LLM_{modelSetting.ChatLLMModelName}");
                this.Prompt = modelSetting.Prompt;

                this._chatExecutionSettings = new OpenAIPromptExecutionSettings
                {
                    Temperature = 0.5f,
                    MaxTokens = 40,
                    ResponseFormat = ChatResponseFormat.CreateTextFormat(),
                    FunctionChoiceBehavior = FunctionChoiceBehavior.None(),
                    ChatSystemPrompt = this.Prompt
                };
                if (!string.IsNullOrEmpty(modelSetting.SummaryMemory))
                {
                    this.ChatHistory.AddSystemMessage(modelSetting.SummaryMemory);
                }
                bool pluginsBuildResult = this.BuildPlugins(modelSetting.Kernel);

                if (pluginsBuildResult)
                {
                    this.Logger.LogInformation(Lang.ChatAgent_Build_BuildPluginsBuilt, this.ProviderType, this.ModelName);
                    this.Logger.LogInformation(Lang.ChatAgent_Build_Built, this.ProviderType, this.ModelName, modelSetting.ChatLLMModelName);
                    return true;
                }
                else
                {
                    this.Logger.LogError(Lang.ChatAgent_Build_BuiltFailed, this.ProviderType, this.ModelName, modelSetting.ChatLLMModelName);
                    this.Logger.LogError(Lang.ChatAgent_Build_BuildPluginsFailed, this.ProviderType, this.ModelName, modelSetting.ChatLLMModelName);
                    return false;
                }
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.ChatAgent_Build_BuiltFailed, this.ProviderType, this.ModelName, modelSetting.ChatLLMModelName);
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
                        this.Logger.LogInformation(Lang.ChatAgent_RegisterDevice_PluginRegistered, llmPlugin.ModelName, deviceId);
                    }
                }
            }
            base.RegisterDevice(deviceId, sessionId);
        }

        public async Task<string> GenerateChatResponseAsync(string userMessage, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._chatAgentService is null)
            {
                throw new InvalidOperationException(Lang.ChatAgent_GenerateChatResponseAsync_AgentNotBuilt);
            }
            this.ChatHistory.AddUserMessage(userMessage);

            var clientResult = await this._chatAgentService.GetChatMessageContentAsync(this.ChatHistory, this._chatExecutionSettings, this._kernel, token);

            string content = !string.IsNullOrEmpty(clientResult.Content) ? clientResult.Content : string.Empty;
            string assistantContent = MarkdownCleaner.CleanMarkdown(Regex.Replace(Regex.Unescape(content), @"<think>.*?</think>", string.Empty, RegexOptions.Singleline));

            this.ChatHistory.AddAssistantMessage(assistantContent);
            return assistantContent;
        }

        public async IAsyncEnumerable<string> GenerateChatResponseStreamingAsync(string userMessage, [EnumeratorCancellation] CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._chatAgentService is null)
            {
                throw new InvalidOperationException(Lang.ChatAgent_GenerateChatResponseAsync_AgentNotBuilt);
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

        public override void Dispose()
        {
        }
    }
}
