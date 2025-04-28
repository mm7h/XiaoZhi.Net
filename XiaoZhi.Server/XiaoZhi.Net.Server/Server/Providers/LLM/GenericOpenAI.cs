using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Entities;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal sealed class GenericOpenAI : BaseProvider, ILlm
    {
        private readonly SemaphoreSlim _llmSlim = new SemaphoreSlim(1, 1);
        private readonly Kernel _kernel;
        private OpenAIPromptExecutionSettings _chatCompletionOptions;
        public const string SERVICE_ID = "generic";

        public GenericOpenAI(Kernel kernel, XiaoZhiConfig config, ILogger logger) : base(config.LlmSetting, logger)
        {
            this._kernel = kernel;
        }
        public override string ProviderType => "llm";

        public event Action<string> OnBeforeTokenGenerate;
        public event Action<string, OutSegment> OnTokenGenerating;
        public event Action<string, string> OnTokenGenerated;

        public override bool Initialize()
        {
            try
            {
                this._chatCompletionOptions = new OpenAIPromptExecutionSettings
                {
                    Temperature = 0.5f,
                    MaxTokens = 80,
                    ResponseFormat = ChatResponseFormat.CreateTextFormat(),
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                };

                this.Logger.Information($"Initialized the {this.ProviderType} model: {this.ModelName}");
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.Debug(ex, $"Invalid model settings for {this.ProviderType}: {ModelName}");
                this.Logger.Error($"Invalid model settings for {this.ProviderType}: {ModelName}");
                return false;
            }
        }

        public async Task ChatAsync(IEnumerable<Dialogue> dialogues, Workflow<string> workflow, CancellationToken token)
        {
            if (this._kernel == null || this._chatCompletionOptions == null)
            {
                throw new ArgumentNullException("Please initialize llm provider first.");
            }
            try
            {
                await this._llmSlim.WaitAsync(token);
                this.OnBeforeTokenGenerate?.Invoke(workflow.SessionId);
                ChatHistory chatHistory = this.Convert2ChatMessages(dialogues);

                IChatCompletionService chatCompletionService = this._kernel.GetRequiredService<IChatCompletionService>(GenericOpenAI.SERVICE_ID);

                var clientResult = await chatCompletionService.GetChatMessageContentAsync(chatHistory, this._chatCompletionOptions, this._kernel, token);

                this.OnTokenGenerated?.Invoke(workflow.SessionId, Regex.Replace(clientResult.Content, @"<think>.*?</think>", "", RegexOptions.Singleline));
            }
            catch (OperationCanceledException ex)
            {
                this.Logger.Warning($"User canceled the job for {this.ProviderType}.");
                throw ex;
            }
            catch (Exception ex)
            {
                this.Logger.Debug(ex, $"Unexpected error(s): {ex.Message}.");
                this.Logger.Error($"Unexpected error(s) for {this.ProviderType}.");
            }
            finally
            {
                this._llmSlim.Release();
            }
        }

        public async Task ChatByStreamingAsync(IEnumerable<Dialogue> dialogues, Workflow<string> workflow, CancellationToken token)
        {
            if (this._kernel == null || this._chatCompletionOptions == null)
            {
                throw new ArgumentNullException("Please initialize llm provider first.");
            }
            try
            {
                await this._llmSlim.WaitAsync(token);
                this.OnBeforeTokenGenerate?.Invoke(workflow.SessionId);

                ChatHistory chatHistory = this.Convert2ChatMessages(dialogues);
                IChatCompletionService chatCompletionService = this._kernel.GetRequiredService<IChatCompletionService>(GenericOpenAI.SERVICE_ID);

                StringBuilder segmentResponse = new StringBuilder();
                List<OutSegment> allResponse = new List<OutSegment>();

                await foreach (var item in chatCompletionService.GetStreamingChatMessageContentsAsync(chatHistory, this._chatCompletionOptions, this._kernel, token))
                {
                    string text = (item.Content ?? string.Empty).Replace(Environment.NewLine, string.Empty).Replace("\n", string.Empty);
                    segmentResponse.Append(text);

                    // 在累积的文本中查找分割点
                    string currentSegment = segmentResponse.ToString();
                    Match match = DialogueHelper.SENTENCE_SPLIT_REGEX.Match(currentSegment);

                    while (match.Success)
                    {
                        int splitPosition = match.Index + match.Length;
                        string sentence = currentSegment.Substring(0, splitPosition);
                        string remaining = currentSegment.Substring(splitPosition);

                        OutSegment outSegment = new OutSegment(sentence);
                        if (allResponse.Count == 0) outSegment.IsFirst = true;

                        allResponse.Add(outSegment);
                        this.OnTokenGenerating?.Invoke(workflow.SessionId, outSegment);

                        // 重置累积内容为剩余部分
                        segmentResponse.Clear();
                        segmentResponse.Append(remaining);
                        currentSegment = remaining;
                        match = DialogueHelper.SENTENCE_SPLIT_REGEX.Match(currentSegment);
                    }
                    //if (text.Contains("<think>"))
                    //{
                    //    isActive = false;
                    //    text = text.Split("<think>", StringSplitOptions.RemoveEmptyEntries)[0];
                    //}
                    //if (text.Contains("</think>"))
                    //{
                    //    isActive = true;
                    //    text = text.Split("</think>", StringSplitOptions.RemoveEmptyEntries)[-1];
                    //}

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
                        this.OnTokenGenerating?.Invoke(workflow.SessionId, segment);
                    }
                }
                segmentResponse.Clear();

                this.OnTokenGenerated?.Invoke(workflow.SessionId, string.Join(string.Empty, allResponse.Select(a => a.Content)));
            }
            catch (OperationCanceledException ex)
            {
                this.Logger.Warning($"User canceled the job for {this.ProviderType}.");
                throw ex;
            }
            catch (Exception ex)
            {
                this.Logger.Debug(ex, $"Unexpected error(s): {ex.Message}.");
                this.Logger.Error($"Unexpected error(s) for {this.ProviderType}.");
            }
            finally
            {
                this._llmSlim.Release();
            }

        }

        private ChatHistory Convert2ChatMessages(IEnumerable<Dialogue> dialogues)
        {
            ChatHistory chatHistory = new ChatHistory();

            foreach (Dialogue dialogue in dialogues)
            {
                if (dialogue.Role == AuthorRole.System)
                {
                    chatHistory.AddSystemMessage(dialogue.Content);
                }
                else if (dialogue.Role == AuthorRole.User)
                {
                    chatHistory.AddUserMessage(dialogue.Content);
                }
                else if (dialogue.Role == AuthorRole.Assistant)
                {
                    chatHistory.AddAssistantMessage(dialogue.Content);
                }
            }
            return chatHistory;
        }

        public override void Dispose()
        {

        }
    }
}
