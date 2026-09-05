using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.LLM.AIContextProviders;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal class GenericOpenAI : BaseProvider<GenericOpenAI, LLMBuildConfig>, ILlm
    {
        private readonly IServiceProvider _serviceProvider;

        private readonly ObjectPool<OutSegment> _outSegmentPool;
        private readonly Dictionary<string, IAgent> _subAgents = new Dictionary<string, IAgent>();
        private readonly SessionChatHistoryProvider _chatHistoryProvider;

        private Workflow? _dialogueWorkflow;
        private int _seqParagraphId = 0;
        private int _seqSentenceId = 0;

        public GenericOpenAI(IServiceProvider serviceProvider,
            SessionChatHistoryProvider chatHistoryProvider,
            ObjectPool<OutSegment> outSegmentPool,
            ILogger<GenericOpenAI> logger) : base(logger)
        {
            this._serviceProvider = serviceProvider;
            this._chatHistoryProvider = chatHistoryProvider;

            this._outSegmentPool = outSegmentPool;
            this._subAgents = new Dictionary<string, IAgent>();
        }
        public override string ModelName => nameof(GenericOpenAI);
        public override string ProviderType => "llm";

        public event Action? OnBeforeTokenGenerate;
        public event Action<OutSegment>? OnTokenGenerating;
        public event Action<IEnumerable<OutSegment>>? OnTokenGenerated;

        public override bool Build(LLMBuildConfig modelSetting)
        {
            try
            {
                this._subAgents.Clear();
                this._dialogueWorkflow = null;

                IAgent inputAgent = this._serviceProvider.GetRequiredKeyedService<IAgent>(SubAgentNames.InputAgent);
                IAgent intentDetectionAgent = this._serviceProvider.GetRequiredKeyedService<IAgent>(SubAgentNames.IntentDetectionAgent);
                IAgent functionCallAgent = this._serviceProvider.GetRequiredKeyedService<IAgent>(SubAgentNames.FunctionCallAgent);
                IAgent intentResponseAgent = this._serviceProvider.GetRequiredKeyedService<IAgent>(SubAgentNames.IntentResponseAgent);
                IAgent chatAgent = this._serviceProvider.GetRequiredKeyedService<IAgent>(SubAgentNames.ChatAgent);
                IAgent outputAgent = this._serviceProvider.GetRequiredKeyedService<IAgent>(SubAgentNames.OutputAgent);

                this._subAgents[inputAgent.AgentName] = inputAgent;
                this._subAgents[intentDetectionAgent.AgentName] = intentDetectionAgent;
                this._subAgents[functionCallAgent.AgentName] = functionCallAgent;
                this._subAgents[intentResponseAgent.AgentName] = intentResponseAgent;
                this._subAgents[chatAgent.AgentName] = chatAgent;
                this._subAgents[outputAgent.AgentName] = outputAgent;

                bool buildSuccess = this._subAgents.Values
                    .AsParallel()
                    .Select(agent =>
                    {
                        if (modelSetting.AgentSettings.TryGetValue(agent.AgentName, out ModelSetting? agentSetting))
                        {
                            return agent.Build(new LLMAgentBuildConfig(
                                agentSetting,
                                modelSetting.SessionPrivateProvider,
                                this._chatHistoryProvider,
                                modelSetting.MemoryInstruction));
                        }
                        else
                        {
                            this.Logger.LogError(Lang.GenericOpenAI_Build_AgentSettingMissing, agent.AgentName);
                            return false;
                        }
                    })
                    .All(r => r);

                if (buildSuccess)
                {
                    this._dialogueWorkflow = this.BuildDialogueWorkflow(modelSetting.SessionPrivateProvider);
                }
                return buildSuccess;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.GenericOpenAI_Build_InvalidSettings, this.ProviderType, this.ModelName);
                return false;
            }
        }

        private Workflow BuildDialogueWorkflow(PrivateProvider privateProvider)
        {
            ExecutorBinding inputExecutor = this._subAgents[SubAgentNames.InputAgent].AsExecutor();
            ExecutorBinding chatExecutor = this._subAgents[SubAgentNames.ChatAgent].AsExecutor();
            ExecutorBinding outputExecutor = this._subAgents[SubAgentNames.OutputAgent].AsExecutor();
            ExecutorBinding intentSubWorkflow = this.BuildIntentSubWorkflow(privateProvider)
                .BindAsExecutor($"{this.ReplaceMacDelimiters(privateProvider.DeviceId, "_")}_{privateProvider.SessionId.Replace("-", string.Empty)}_intent_binding");

            return new WorkflowBuilder(inputExecutor)
                .AddEdge<WorkflowPreInputs>(inputExecutor, intentSubWorkflow, iw => iw is not null && iw.IntentRequired)
                .AddEdge<WorkflowPreInputs>(inputExecutor, chatExecutor, iw => iw is not null && !iw.IntentRequired)
                .AddEdge<IntentDetectionResult>(intentSubWorkflow, chatExecutor, id => id is not null && !id.Detected)
                .AddEdge(intentSubWorkflow, outputExecutor)
                .AddEdge(chatExecutor, outputExecutor)
                .WithOutputFrom(outputExecutor)
                .WithName(privateProvider.DeviceId)
                .WithDescription($"Dialogue workflow for device {privateProvider.DeviceId} and session {privateProvider.SessionId}")
                .Build();
        }

        private Workflow BuildIntentSubWorkflow(PrivateProvider privateProvider)
        {
            ExecutorBinding intentDetectionExecutor = this._subAgents[SubAgentNames.IntentDetectionAgent].AsExecutor();
            ExecutorBinding functionCallExecutor = this._subAgents[SubAgentNames.FunctionCallAgent].AsExecutor();
            ExecutorBinding intentResponseExecutor = this._subAgents[SubAgentNames.IntentResponseAgent].AsExecutor();

            return new WorkflowBuilder(intentDetectionExecutor)
                .AddEdge<IntentDetectionResult>(intentDetectionExecutor, functionCallExecutor, dr => dr is not null && dr.Detected)
                .AddEdge<IntentDetectionResult>(intentDetectionExecutor, intentResponseExecutor, dr => dr is not null && !dr.Detected)
                .AddEdge(functionCallExecutor, intentResponseExecutor)
                .WithOutputFrom(intentResponseExecutor)
                .WithName($"{privateProvider.DeviceId}_intent")
                .WithDescription($"Intent workflow for device {privateProvider.DeviceId} and session {privateProvider.SessionId}")
                .Build();
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
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (!this._subAgents.Any())
            {
                this.Logger.LogError(Lang.GenericOpenAI_StartDialogueAsync_NotBuilt, this.ProviderType, this.ModelName);
                return;
            }
            if (this._dialogueWorkflow is null)
            {
                throw new InvalidOperationException("Dialogue workflow is not initialized.");
            }

            this.OnBeforeTokenGenerate?.Invoke();
            await this.RunAndEmitWorkflowStreamingAsync(this._dialogueWorkflow, userMessage, token);
        }

        public IReadOnlyList<ChatMessage> GetChatHistory()
        {
            return this._chatHistoryProvider.GetMessages();
        }

        protected override string GenerateId()
        {
            string devicePart = this.ReplaceMacDelimiters(this.DeviceId, "_");
            string sessionPart = this.SessionId.Replace("-", string.Empty);
            if (sessionPart.Length > 7)
            {
                sessionPart = sessionPart.Substring(0, 7);
            }
            int sequence = Interlocked.Increment(ref this._seqParagraphId);
            return $"{devicePart}_{sessionPart}_{sequence}";
        }

        private string GenerateSentenceId(string paragraphId)
        {
            return $"{paragraphId}_{Interlocked.Increment(ref this._seqSentenceId)}";
        }

        private async Task RunAndEmitWorkflowStreamingAsync(Workflow dialogueWorkflow, string userMessage, CancellationToken token)
        {
            await using StreamingRun run = await InProcessExecution.Concurrent.RunStreamingAsync(dialogueWorkflow, userMessage, this.DeviceId, token);

            List<OutSegment> allSegments = new List<OutSegment>();
            string paragraphId = this.GenerateId();
            int segmentCount = 0;
            OutSegment? pendingSegment = null; // 缓冲上一句，等待确认是否为最后一段

            try
            {
                await foreach (WorkflowEvent workflowEvent in run.WatchStreamAsync())
                {
                    token.ThrowIfCancellationRequested();

                    switch (workflowEvent)
                    {
                        case WorkflowOutputEvent workflowOutputEvent when workflowOutputEvent.Data is WorkflowOutputs output:
                            foreach (ChatMessageItemResult item in output.Results)
                            {
                                this.Logger.LogDebug(Lang.GenericOpenAI_ChatAsync_EmotionDetected, item.Emotion, item.Content);

                                // 将前一个pending segment以isLast=false立即发出，无需等待LLM全部完成
                                if (pendingSegment is not null)
                                {
                                    allSegments.Add(pendingSegment);
                                    this.OnTokenGenerating?.Invoke(pendingSegment);
                                }

                                // 当前item暂存为pending，等待下一句到来或流结束以确定IsLastSegment
                                OutSegment seg = this._outSegmentPool.Get();
                                seg.Initialize(item.Content, segmentCount == 0, false, item.Emotion, paragraphId, this.GenerateSentenceId(paragraphId));
                                pendingSegment = seg;
                                segmentCount++;
                            }
                            break;

                        case WorkflowErrorEvent workflowErrorEvent:
                            throw workflowErrorEvent.Exception ?? new InvalidOperationException("Dialogue workflow failed.");
                        case ExecutorFailedEvent executorFailedEvent:
                            throw new InvalidOperationException($"Executor '{executorFailedEvent.ExecutorId}' failed with {(executorFailedEvent.Data is null ? "unknown error" : executorFailedEvent.Data)}.");
                    }
                }

                // 流结束，将最后一句标记为IsLastSegment后发出
                if (pendingSegment is not null)
                {
                    pendingSegment.IsLastSegment = true;
                    allSegments.Add(pendingSegment);
                    this.OnTokenGenerating?.Invoke(pendingSegment);
                }

                this.OnTokenGenerated?.Invoke(allSegments);
            }
            catch (OperationCanceledException)
            {
                // 确保pending segment归入allSegments以便池回收
                if (pendingSegment is not null)
                {
                    allSegments.Add(pendingSegment);
                }
                this.Logger.LogDebug(Lang.GenericOpenAI_ChatAsync_Cancelled, allSegments.Count);
                this.OnTokenGenerated?.Invoke(allSegments);
                throw;
            }
            catch (Exception ex)
            {
                if (pendingSegment is not null)
                {
                    allSegments.Add(pendingSegment);
                }
                this.Logger.LogError(ex, Lang.GenericOpenAI_ChatAsync_UnexpectedError, this.ProviderType);
                this.OnTokenGenerated?.Invoke(allSegments);
            }
        }


        public override void Dispose()
        {
            foreach (var agent in this._subAgents.Values)
            {
                agent.Dispose();
            }
            this._subAgents.Clear();
            this._dialogueWorkflow = null;
        }
    }
}
