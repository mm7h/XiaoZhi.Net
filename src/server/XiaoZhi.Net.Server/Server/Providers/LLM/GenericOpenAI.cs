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
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal sealed class GenericOpenAI : BaseProvider<GenericOpenAI, ModelSetting>, ILlm
    {
        private readonly SemaphoreSlim _llmSlim = new SemaphoreSlim(1, 1);
        private readonly IServiceProvider _serviceProvider;
        private readonly ObjectPool<OutSegment> _outSegmentPool;
        private OpenAIPromptExecutionSettings _chatCompletionOptions;

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
        }
        public override string ModelName => nameof(GenericOpenAI);
        public override string ProviderType => "llm";

        public event Action<string>? OnBeforeTokenGenerate;
        public event Action<string, OutSegment>? OnTokenGenerating;
        public event Action<string, string>? OnTokenGenerated;

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                this.Logger.LogInformation("Builded the {providerType} model: {modelName}", this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return false;
            }
        }

        public async Task ChatAsync(Workflow<DialogueContext> workflow, CancellationToken token)
        {
            try
            {
                await this._llmSlim.WaitAsync(token);
                this.OnBeforeTokenGenerate?.Invoke(workflow.SessionId);
                ChatHistory chatHistory = workflow.Data.Dialogues.Convert2ChatMessages();

                IChatCompletionService chatCompletionService;

                if (!string.IsNullOrEmpty(workflow.Data.LlmModelName))
                {
                    chatCompletionService = this._serviceProvider.GetRequiredKeyedService<IChatCompletionService>($"LLM_{workflow.Data.LlmModelName}");
                }
                else
                {
                    chatCompletionService = this._serviceProvider.GetRequiredKeyedService<IChatCompletionService>($"LLM_{SystemLLMServiceNames.GENERIC_LLM_ID}");
                }


                var clientResult = await chatCompletionService.GetChatMessageContentAsync(chatHistory, this._chatCompletionOptions, workflow.Data.Kernel, token);

                string content = !string.IsNullOrEmpty(clientResult.Content) ? clientResult.Content : string.Empty;
                string text = MarkdownCleaner.CleanMarkdown(Regex.Unescape(content));
                this.OnTokenGenerated?.Invoke(workflow.SessionId, MarkdownCleaner.CleanMarkdown(Regex.Replace(Regex.Unescape(content), @"<think>.*?</think>", "", RegexOptions.Singleline)));
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

        public async Task ChatByStreamingAsync(Workflow<DialogueContext> workflow, CancellationToken token)
        {
            List<OutSegment> allResponse = new List<OutSegment>();
            
            try
            {
                await this._llmSlim.WaitAsync(token);
                this.OnBeforeTokenGenerate?.Invoke(workflow.SessionId);

                ChatHistory chatHistory = workflow.Data.Dialogues.Convert2ChatMessages();

                IChatCompletionService chatCompletionService;
                if (!string.IsNullOrEmpty(workflow.Data.LlmModelName))
                {
                    chatCompletionService = this._serviceProvider.GetRequiredKeyedService<IChatCompletionService>($"LLM_{workflow.Data.LlmModelName}");
                }
                else
                {
                    chatCompletionService = this._serviceProvider.GetRequiredKeyedService<IChatCompletionService>($"LLM_{SystemLLMServiceNames.GENERIC_LLM_ID}");
                }

                StringBuilder segmentResponse = new StringBuilder();

                await foreach (var item in chatCompletionService.GetStreamingChatMessageContentsAsync(chatHistory, this._chatCompletionOptions, workflow.Data.Kernel, token))
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

                        // 从对象池获取 OutSegment 对象
                        var outSegment = this._outSegmentPool.Get();
                        try
                        {
                            outSegment.Initialize(sentence);
                            if (allResponse.Count == 0) outSegment.IsFirst = true;

                            allResponse.Add(outSegment);
                            this.OnTokenGenerating?.Invoke(workflow.SessionId, outSegment);
                        }
                        catch
                        {
                            // 如果出错，归还对象到池中
                            this._outSegmentPool.Return(outSegment);
                            throw;
                        }

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
                    lastOutSegment.IsLast = true;
                }
                else
                {
                    // 处理LLM回复的内容无法被句子分隔的问题
                    if (segmentResponse.Length > 0)
                    {
                        var segment = this._outSegmentPool.Get();
                        try
                        {
                            segment.Initialize(segmentResponse.ToString(), true, true);
                            allResponse.Add(segment);
                            this.OnTokenGenerating?.Invoke(workflow.SessionId, segment);
                        }
                        catch
                        {
                            this._outSegmentPool.Return(segment);
                            throw;
                        }
                    }
                }
                segmentResponse.Clear();

                this.OnTokenGenerated?.Invoke(workflow.SessionId, string.Join(string.Empty, allResponse.Select(a => a.Content)));
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
                // 归还所有使用的 OutSegment 对象到池中
                foreach (var segment in allResponse)
                {
                    this._outSegmentPool.Return(segment);
                }
                allResponse.Clear();
                
                this._llmSlim.Release();
            }
        }
        
        public override void Dispose()
        {

        }
    }
}
