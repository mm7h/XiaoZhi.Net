using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal class GenericOpenAI : BaseProvider<GenericOpenAI, LLMBuildConfig>, ILlm
    {
        private readonly IChatAgent _chatAgent;
        private readonly IEmotionAgent _emotionAgent;

        private readonly ObjectPool<OutSegment> _outSegmentPool;
        private readonly Dictionary<string, IAgent> _subAgents = new Dictionary<string, IAgent>();
        private Kernel? _kernel;

        public GenericOpenAI(IChatAgent chatAgent,
            IEmotionAgent emotionAgent,
            ObjectPool<OutSegment> outSegmentPool,
            ILogger<GenericOpenAI> logger) : base(logger)
        {
            this._chatAgent = chatAgent;
            this._emotionAgent = emotionAgent;

            this._outSegmentPool = outSegmentPool;
            this._subAgents = new Dictionary<string, IAgent>();
            this.LLMChatHistory = new ChatHistory();
        }
        public override string ModelName => nameof(GenericOpenAI);
        public override string ProviderType => "llm";

        public bool UseStreaming { get; private set; }
        public ChatHistory LLMChatHistory { get; }

        public event Action? OnBeforeTokenGenerate;
        public event Action<OutSegment>? OnTokenGenerating;
        public event Action<IEnumerable<OutSegment>>? OnTokenGenerated;

        public override bool Build(LLMBuildConfig modelSetting)
        {
            try
            {
                this._kernel = modelSetting.Kernel;
                this.UseStreaming = modelSetting.UseStreaming;

                this._subAgents.Add(SubAgentNames.EmotionAgent, this._emotionAgent);
                this._subAgents.Add(SubAgentNames.ChatAgent, this._chatAgent);

                var buildResults = this._subAgents.Values
                    .AsParallel()
                    .Select(client => client.Build(modelSetting))
                    .ToArray();

                return buildResults.All(result => result);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return false;
            }
        }

        public override void RegisterDevice(string deviceId, string sessionId)
        {
            foreach (var agent in this._subAgents.Values)
            {
                agent.RegisterDevice(deviceId, sessionId);
            }
            base.RegisterDevice(deviceId, sessionId);
        }

        public async Task StartDialogueAsync(string userMessage, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered())
            {
                throw new SessionNotInitializedException();
            }
            if (!this._subAgents.Any() || this._kernel is null)
            {
                this.Logger.LogError("The {providerType} model: {modelName} is not built.", this.ProviderType, this.ModelName);
                return;
            }
            if (this._chatAgent.UseStreaming)
            {
                await this.ChatByStreamingAsync(userMessage, token);
            }
            else
            {
                await this.ChatAsync(userMessage, token);
            }
        }

        private async Task ChatAsync(string userMessage, CancellationToken token)
        {
            try
            {
                this.OnBeforeTokenGenerate?.Invoke();

                string assistantResponse = await this._chatAgent.GenerateChatResponseAsync(userMessage, token);
                token.ThrowIfCancellationRequested();

                string cleanContent = DialogueHelper.GetStringNoPunctuationOrEmoji(assistantResponse);
                IEnumerable<string> segments = DialogueHelper.SplitContentByPunctuations(cleanContent);
                List<OutSegment> allResponse = new List<OutSegment>();

                int index = 0;
                int count = segments.Count();

                foreach (string sentence in segments)
                {
                    token.ThrowIfCancellationRequested();
                    index++;
                    Emotion detectedEmotion = await this._emotionAgent.AnalyzeEmotionAsync(userMessage, sentence, token);
                    this.Logger.LogDebug("Detected emotion: {detectedEmotion} for segment: {segment}", detectedEmotion, sentence);

                    var outSegment = this._outSegmentPool.Get();
                    outSegment.Initialize(sentence, index == 1, index == count, detectedEmotion);

                    allResponse.Add(outSegment);
                    this.OnTokenGenerating?.Invoke(outSegment);
                }

                this.OnTokenGenerated?.Invoke(allResponse);
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
        }

        private async Task ChatByStreamingAsync(string userMessage, CancellationToken token)
        {
            try
            {
                this.OnBeforeTokenGenerate?.Invoke();

                List<OutSegment> allResponse = new List<OutSegment>();
                await foreach (string sentence in this._chatAgent.GenerateChatResponseStreamingAsync(userMessage, token))
                {
                    token.ThrowIfCancellationRequested();

                    Emotion detectedEmotion = await this._emotionAgent.AnalyzeEmotionAsync(userMessage, sentence, token);
                    this.Logger.LogDebug("Detected emotion: {detectedEmotion} for segment: {segment}", detectedEmotion, sentence);

                    var outSegment = this._outSegmentPool.Get();
                    outSegment.Initialize(sentence, detectedEmotion);

                    if (allResponse.Count == 0)
                    {
                        outSegment.IsFirstSegment = true;
                    }
                    allResponse.Add(outSegment);
                    if (allResponse.Count >= 2)
                    {
                        this.OnTokenGenerating?.Invoke(allResponse[^2]);
                    }

                }
                if (allResponse.Any())
                {
                    OutSegment lastOutSegment = allResponse.Last();
                    lastOutSegment.IsLastSegment = true;
                    this.OnTokenGenerating?.Invoke(lastOutSegment);
                }

                this.OnTokenGenerated?.Invoke(allResponse);
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
        }

        public override void Dispose()
        {
            foreach (var agent in this._subAgents.Values)
            {
                agent.Dispose();
            }
            this._subAgents.Clear();
        }
    }
}
