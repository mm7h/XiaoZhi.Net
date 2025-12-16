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
    internal sealed class GenericOpenAI : BaseProvider<GenericOpenAI, LLMBuildConfig>, ILlm
    {

        private readonly IEmotionAgent _emotionAgent;
        private readonly IChatAgent _chatAgent;
        private readonly ObjectPool<OutSegment> _outSegmentPool;
        private readonly Dictionary<string, IAgent> _subAgents = new Dictionary<string, IAgent>();
        private Kernel? _kernel;

        public GenericOpenAI(IEmotionAgent emotionAgent,
            IChatAgent chatAgent,
            ObjectPool<OutSegment> outSegmentPool,
            ILogger<GenericOpenAI> logger) : base(logger)
        {
            this._emotionAgent = emotionAgent;
            this._chatAgent = chatAgent;
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

        public async Task ChatAsync(string userMessage, CancellationToken token)
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

            try
            {
                this.OnBeforeTokenGenerate?.Invoke();

                Emotion detectedEmotion = await this._emotionAgent.AnalyzeEmotionAsync(userMessage, token);
                this.Logger.LogDebug("Detected emotion: {detectedEmotion} with the message: \"{userMessage}\" for the device: {deviceId}", detectedEmotion, userMessage, this.DeviceId);
                string assistantResponse = await this._chatAgent.GenerateChatResponseAsync(userMessage, detectedEmotion, token);

                IEnumerable<OutSegment> allResponse = this.ParseContentToSegments(assistantResponse, detectedEmotion);

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

        public async Task ChatByStreamingAsync(string userMessage, CancellationToken token)
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

            List<OutSegment> allResponse = new List<OutSegment>();
            try
            {
                this.OnBeforeTokenGenerate?.Invoke();

                Emotion detectedEmotion = await this._emotionAgent.AnalyzeEmotionAsync(userMessage, token);
                this.Logger.LogDebug("Detected emotion: {detectedEmotion} with the message: \"{userMessage}\" for the device: {deviceId}", detectedEmotion, userMessage, this.DeviceId);
                await foreach (string sentence in this._chatAgent.GenerateChatResponseStreamingAsync(userMessage, detectedEmotion, token))
                {
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
            finally
            {
                allResponse.Clear();
            }
        }

        private IEnumerable<OutSegment> ParseContentToSegments(string content, Emotion emotion)
        {
            content = DialogueHelper.GetStringNoPunctuationOrEmoji(content);

            IEnumerable<string> segments = DialogueHelper.SplitContentByPunctuations(content);
            int segmentsCount = segments.Count();
            int segmentIndex = 0;

            foreach (string segment in segments)
            {
                segmentIndex++;
                bool isFirst = segmentIndex == 1;
                bool isLast = segmentIndex == segmentsCount;

                var outSegment = this._outSegmentPool.Get();

                outSegment.Initialize(segment, isFirst, isLast, emotion);
                yield return outSegment;
            }
        }

        public override void Dispose()
        {

        }
    }
}
