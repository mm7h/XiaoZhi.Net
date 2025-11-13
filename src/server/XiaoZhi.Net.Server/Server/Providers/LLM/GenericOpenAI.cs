using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers.LLM.Plugins;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal sealed class GenericOpenAI : BaseProvider<GenericOpenAI, LLMBuildConfig>, ILlm
    {
        private readonly SemaphoreSlim _llmSlim = new SemaphoreSlim(1, 1);
        private readonly IServiceProvider _serviceProvider;
        private readonly ObjectPool<OutSegment> _outSegmentPool;

        private OpenAIPromptExecutionSettings _chatCompletionOptions;
        private Kernel? _kernel;

        private IChatCompletionService? _chatCompletionService;
        public GenericOpenAI(IServiceProvider serviceProvider,
            ObjectPool<OutSegment> outSegmentPool,
            ILogger<GenericOpenAI> logger) : base(logger)
        {
            this._serviceProvider = serviceProvider;
            this._outSegmentPool = outSegmentPool;
            this._chatCompletionOptions = new OpenAIPromptExecutionSettings
            {
                Temperature = 0.5f,
                MaxTokens = 80,
                ResponseFormat = ChatResponseFormat.CreateTextFormat(),
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };
            this.LLMModelName = string.Empty;
            this.LLMChatHistory = new ChatHistory();
        }
        public override string ModelName => nameof(GenericOpenAI);
        public override string ProviderType => "llm";

        public string LLMModelName { get; private set; }
        public bool UseStreaming { get; private set; }
        public ChatHistory LLMChatHistory { get; }

        public event Action? OnBeforeTokenGenerate;
        public event Action<OutSegment>? OnTokenGenerating;
        public event Action<string>? OnTokenGenerated;

        public override bool Build(LLMBuildConfig modelSetting)
        {
            try
            {
                this.LLMModelName = modelSetting.LlmModelName;
                this._kernel = modelSetting.Kernel;
                this.UseStreaming = modelSetting.UseStreaming;


                this._chatCompletionService = this._serviceProvider.GetRequiredKeyedService<IChatCompletionService>($"LLM_{modelSetting.LlmModelName}");

                this.LLMChatHistory.AddSystemMessage(modelSetting.Prompt);
                if (!string.IsNullOrEmpty(modelSetting.SummaryMemory))
                {
                    this.LLMChatHistory.AddSystemMessage(modelSetting.SummaryMemory);
                }

                bool pluginsBuildResult = this.BuildPlugins(modelSetting.Session, this._kernel);

                if (pluginsBuildResult)
                {
                    this.Logger.LogInformation("Builded the {providerType} model {modelName} to the device: {deviceId}.", this.ProviderType, this.ModelName, modelSetting.Session.DeviceId);
                    return true;
                }
                else
                { 
                    this.Logger.LogError("Failed to build the plugins for {providerType} model {modelName} to the device: {deviceId}.", this.ProviderType, this.ModelName, modelSetting.Session.DeviceId);
                    return false;
                }
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType} model {modelName} to the device: {deviceId}.", this.ProviderType, this.ModelName, modelSetting.Session.DeviceId);
                return false;
            }
        }

        public async Task ChatAsync(string userMessage, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered())
            {
                throw new SessionNotInitializedException();
            }
            if (this._chatCompletionService is null)
            {
                this.Logger.LogError("The {providerType} model: {modelName} is not built.", this.ProviderType, this.ModelName);
                return;
            }
            try
            {
                await this._llmSlim.WaitAsync(token);
                this.OnBeforeTokenGenerate?.Invoke();

                this.LLMChatHistory.AddUserMessage(userMessage);
                var clientResult = await this._chatCompletionService.GetChatMessageContentAsync(this.LLMChatHistory, this._chatCompletionOptions, this._kernel, token);

                string content = !string.IsNullOrEmpty(clientResult.Content) ? clientResult.Content : string.Empty;
                string assistantContent = MarkdownCleaner.CleanMarkdown(Regex.Replace(Regex.Unescape(content), @"<think>.*?</think>", string.Empty, RegexOptions.Singleline));

                this.LLMChatHistory.AddAssistantMessage(assistantContent);

                this.OnTokenGenerated?.Invoke(assistantContent);
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogWarning("User canceled the job for {providerType}.", this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Unexpected error(s) for {providerType}.", this.ProviderType);
            }
            finally
            {
                this._llmSlim.Release();
            }
        }

        public async Task ChatByStreamingAsync(string userMessage, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered())
            {
                throw new SessionNotInitializedException();
            }
            if (this._chatCompletionService is null)
            {
                this.Logger.LogError("The {providerType} model: {modelName} is not built.", this.ProviderType, this.ModelName);
                return;
            }

            List<OutSegment> allResponse = new List<OutSegment>();

            try
            {
                await this._llmSlim.WaitAsync(token);
                this.OnBeforeTokenGenerate?.Invoke();

                this.LLMChatHistory.AddUserMessage(userMessage);

                StringBuilder segmentResponse = new StringBuilder();

                await foreach (var item in this._chatCompletionService.GetStreamingChatMessageContentsAsync(this.LLMChatHistory, this._chatCompletionOptions, this._kernel, token))
                {
                    string content = !string.IsNullOrEmpty(item.Content) ? item.Content : string.Empty;
                    string text = MarkdownCleaner.CleanMarkdown(Regex.Unescape(content));
                    segmentResponse.Append(text);

                    // 在累积的文本中查找分割点
                    string currentSegment = segmentResponse.ToString();
                    Match match = DialogueHelper.SENTENCE_SPLIT_REGEX.Match(currentSegment);

                    while (match.Success)
                    {
                        int splitPosition = match.Index + match.Length;
                        string sentence = currentSegment.Substring(0, splitPosition);
                        string remaining = currentSegment.Substring(splitPosition);

                        var outSegment = this._outSegmentPool.Get();
                        outSegment.Initialize(sentence);
                        if (allResponse.Count == 0) outSegment.IsFirstSegment = true;

                        allResponse.Add(outSegment);
                        this.OnTokenGenerating?.Invoke(outSegment);

                        // 重置累积内容为剩余部分
                        segmentResponse.Clear();
                        segmentResponse.Append(remaining);
                        currentSegment = remaining;
                        match = DialogueHelper.SENTENCE_SPLIT_REGEX.Match(currentSegment);
                    }
                }

                // 处理流结束的情况
                if (allResponse.Any())
                {
                    OutSegment lastOutSegment = allResponse.Last();
                    lastOutSegment.IsLastSegment = true;
                }
                else
                {
                    // 处理LLM回复的内容无法被句子分隔的问题
                    if (segmentResponse.Length > 0)
                    {
                        var segment = this._outSegmentPool.Get();
                        segment.Initialize(segmentResponse.ToString(), true, true);
                        allResponse.Add(segment);
                        this.OnTokenGenerating?.Invoke(segment);
                    }
                }
                segmentResponse.Clear();

                string allContent = string.Join(string.Empty, allResponse.Select(a => a.Content));

                string assistantContent = MarkdownCleaner.CleanMarkdown(Regex.Replace(Regex.Unescape(allContent), @"<think>.*?</think>", "", RegexOptions.Singleline));

                this.LLMChatHistory.AddAssistantMessage(assistantContent);

                this.OnTokenGenerated?.Invoke(assistantContent);
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogWarning("User canceled the job for {providerType}.", this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Unexpected error(s) for {providerType}.", this.ProviderType);
            }
            finally
            {
                allResponse.Clear();

                this._llmSlim.Release();
            }
        }

        private bool BuildPlugins(Session session, Kernel kernel)
        {
            #region LocalMusicPlayer
            MusicPlayer musicPlayerPlugin = this._serviceProvider.GetRequiredService<MusicPlayer>();

            LLMPluginConfig llmPluginConfig = new LLMPluginConfig(session);

            if (musicPlayerPlugin.Build(llmPluginConfig))
            {
                string pluginName = musicPlayerPlugin.ModelName;
                kernel.ImportPluginFromObject(musicPlayerPlugin, pluginName);
                this.Logger.LogInformation("LLM plugin {pluginName} initialized for device: {deviceId}.", pluginName, session.DeviceId);
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
