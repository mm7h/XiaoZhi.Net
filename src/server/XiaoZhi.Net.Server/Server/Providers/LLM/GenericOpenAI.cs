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
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Providers.LLM.Plugins;
using XiaoZhi.Net.Server.Server.Common.Configs;
using XiaoZhi.Net.Server.Server.Providers.LLM.Contexts;

namespace XiaoZhi.Net.Server.Providers.LLM
{
    internal class GenericOpenAI : BaseProvider<GenericOpenAI, LLMBuildConfig>, ILlm
    {
        private readonly IServiceProvider _serviceProvider;

        private readonly IIntentAgent _intentAgent;

        private readonly IChatAgent _chatAgent;

        private readonly ObjectPool<OutSegment> _outSegmentPool;
        private readonly Dictionary<string, IAgent> _subAgents = new Dictionary<string, IAgent>();
        private Workflow? _dialogueWorkflow;
        private int _seqParagraphId = 0;
        private int _seqSentenceId = 0;

        public GenericOpenAI(IServiceProvider serviceProvider,
            IIntentAgent intentAgent,
            IChatAgent chatAgent,
            ObjectPool<OutSegment> outSegmentPool,
            ILogger<GenericOpenAI> logger) : base(logger)
        {
            this._serviceProvider = serviceProvider;
            this._intentAgent = intentAgent;
            this._chatAgent = chatAgent;

            this._outSegmentPool = outSegmentPool;
            this._subAgents = new Dictionary<string, IAgent>();
        }
        public override string ModelName => nameof(GenericOpenAI);
        public override string ProviderType => "llm";

        /// <summary>当前对话历史，来自 ChatAgent（供保存记忆等扩展使用）</summary>
        public IReadOnlyList<ChatMessage> LLMChatHistory => this._chatAgent.ChatHistory;

        public event Action? OnBeforeTokenGenerate;
        public event Action<OutSegment>? OnTokenGenerating;
        public event Action<IEnumerable<OutSegment>>? OnTokenGenerated;

        public override bool Build(LLMBuildConfig modelSetting)
        {
            try
            {
                this._subAgents.Clear();

                Dictionary<string, ModelSetting> resolvedAgentSettings = this.ResolveAgentSettings(modelSetting.AgentSettings);

                var buildResults = this._subAgents.Values
                    .AsParallel()
                    .Select(client =>
                    {
                        bool subAgentBuildResult = client.Build(new LLMAgentBuildConfig(resolvedAgentSettings[client.AgentName], modelSetting.SessionPrivateProvider));
                        this._subAgents.Add(client.AgentName, client);
                        return subAgentBuildResult;
                    })
                    .ToArray();

                bool buildSuccess = buildResults.All(result => result);
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

        private Workflow BuildDialogueWorkflow(PrivateProvider sessionPrivateProvider)
        {
            IntentDetectionExecutor intentDetectionExecutor = new IntentDetectionExecutor(this._intentAgent);
            IntentActionExecutor intentActionExecutor = new IntentActionExecutor(this._serviceProvider, sessionPrivateProvider);
            ChatResponseExecutor chatResponseExecutor = new ChatResponseExecutor(this._chatAgent);
            WorkflowOutputExecutor workflowOutputExecutor = new WorkflowOutputExecutor();

            return new WorkflowBuilder(intentDetectionExecutor)
                .AddSwitch(intentDetectionExecutor, sw => sw
                    .AddCase<IntentResult>(result => result is not null && result.Matched, intentActionExecutor)
                    .AddCase<IntentResult>(result => result is null || !result.Matched, chatResponseExecutor))
                .AddEdge(intentActionExecutor, workflowOutputExecutor)
                .AddEdge(chatResponseExecutor, workflowOutputExecutor)
                .WithOutputFrom(workflowOutputExecutor)
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

            WorkflowOutputs workflowOutputs = await this.RunDialogueWorkflowAsync(userMessage, token);
            await this.EmitWorkflowOutputAsync(workflowOutputs, token);
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

        private async Task<WorkflowOutputs> RunDialogueWorkflowAsync(string userMessage, CancellationToken token)
        {
            await using StreamingRun run = await InProcessExecution.RunStreamingAsync(this._dialogueWorkflow!, userMessage, string.Empty, token);
            WorkflowOutputs? output = null;

            await foreach (WorkflowEvent workflowEvent in run.WatchStreamAsync())
            {
                switch (workflowEvent)
                {
                    case WorkflowOutputEvent workflowOutputEvent when workflowOutputEvent.Data is WorkflowOutputs workflowOutput:
                        output = workflowOutput;
                        break;
                    case WorkflowErrorEvent workflowErrorEvent:
                        throw workflowErrorEvent.Exception ?? new InvalidOperationException("Dialogue workflow failed.");
                    case ExecutorFailedEvent executorFailedEvent:
                        throw new InvalidOperationException($"Executor '{executorFailedEvent.ExecutorId}' failed with {(executorFailedEvent.Data is null ? "unknown error" : executorFailedEvent.Data)}.");
                }
            }

            return output ?? throw new InvalidOperationException("Dialogue workflow produced no output.");
        }

        private Task EmitWorkflowOutputAsync(WorkflowOutputs workflowOutputs, CancellationToken token)
        {
            List<OutSegment> allResponse = new List<OutSegment>();
            try
            {
                this.OnBeforeTokenGenerate?.Invoke();

                List<EmotionTagParser.ParsedEmotionSegment> segments = DialogueHelper.SplitContentByPunctuations(workflowOutputs.ResponseText)
                    .Select(EmotionTagParser.Parse)
                    .Select(segment => new EmotionTagParser.ParsedEmotionSegment(
                        DialogueHelper.GetStringNoPunctuationOrEmoji(segment.Content),
                        segment.Emotion))
                    .Where(segment => !string.IsNullOrWhiteSpace(segment.Content))
                    .ToList();

                string paragraphId = this.GenerateId();
                for (int index = 0; index < segments.Count; index++)
                {
                    token.ThrowIfCancellationRequested();

                    EmotionTagParser.ParsedEmotionSegment segment = segments[index];
                    this.Logger.LogDebug(Lang.GenericOpenAI_ChatAsync_EmotionDetected, segment.Emotion, segment.Content);

                    var outSegment = this._outSegmentPool.Get();
                    outSegment.Initialize(segment.Content, index == 0, index == segments.Count - 1, segment.Emotion, paragraphId, this.GenerateSentenceId(paragraphId));

                    allResponse.Add(outSegment);
                    this.OnTokenGenerating?.Invoke(outSegment);
                }

                this.OnTokenGenerated?.Invoke(allResponse);
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogDebug(Lang.GenericOpenAI_ChatAsync_Cancelled, allResponse.Count);
                this.OnTokenGenerated?.Invoke(allResponse);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.GenericOpenAI_ChatAsync_UnexpectedError, this.ProviderType);
                this.OnTokenGenerated?.Invoke(allResponse);
            }

            return Task.CompletedTask;
        }

        private Dictionary<string, ModelSetting> ResolveAgentSettings(Dictionary<string, ModelSetting> agentSettings)
        {
            if (!agentSettings.TryGetValue(SubAgentNames.ChatAgent, out ModelSetting? chatAgentSetting))
            {
                throw new InvalidOperationException("ChatAgent setting is required.");
            }

            Dictionary<string, ModelSetting> resolvedSettings = new Dictionary<string, ModelSetting>
            {
                { SubAgentNames.ChatAgent, CloneModelSetting(chatAgentSetting) },
                {
                    SubAgentNames.IntentAgent,
                    agentSettings.TryGetValue(SubAgentNames.IntentAgent, out ModelSetting? intentAgentSetting)
                        ? CloneModelSetting(intentAgentSetting)
                        : CloneModelSetting(chatAgentSetting)
                }
            };

            return resolvedSettings;
        }

        private static ModelSetting CloneModelSetting(ModelSetting modelSetting)
        {
            return new ModelSetting
            {
                ModelName = modelSetting.ModelName,
                Config = new Dictionary<string, string>(modelSetting.Config)
            };
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

    internal sealed class IntentDetectionExecutor : Executor<string, IntentResult>
    {
        private readonly IIntentAgent _intentAgent;

        public IntentDetectionExecutor(IIntentAgent intentAgent) : base("IntentDetection")
        {
            this._intentAgent = intentAgent;
        }

        public override async ValueTask<IntentResult> HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            return await this._intentAgent.DetectIntentAsync(message, cancellationToken);
        }
    }

    internal sealed class IntentActionExecutor : Executor<IntentResult, WorkflowOutputs>
    {
        private readonly IServiceProvider _serviceProvider;

        private readonly PrivateProvider _sessionPrivateProvider;

        public IntentActionExecutor(IServiceProvider serviceProvider, PrivateProvider sessionPrivateProvider) : base("IntentAction")
        {
            this._serviceProvider = serviceProvider;
            this._sessionPrivateProvider = sessionPrivateProvider;
        }

        public override async ValueTask<WorkflowOutputs> HandleAsync(IntentResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            if (message.IntentName.Equals("play_music", StringComparison.OrdinalIgnoreCase))
            {
                MusicPlayer musicPlayer = this._serviceProvider.GetRequiredService<MusicPlayer>();
                musicPlayer.Build(new LLMPluginConfig(this._sessionPrivateProvider));
                string responseText = await musicPlayer.PlayMusic(message.IsRandom, message.MusicName);
                return new WorkflowOutputs
                {
                    ResponseText = responseText,
                    HandledByIntent = true,
                    Source = message.IntentName
                };
            }

            return new WorkflowOutputs
            {
                ResponseText = message.Reply,
                HandledByIntent = true,
                Source = message.IntentName
            };
        }
    }

    internal sealed class ChatResponseExecutor : Executor<IntentResult, WorkflowOutputs>
    {
        private readonly IChatAgent _chatAgent;

        public ChatResponseExecutor(IChatAgent chatAgent) : base("ChatResponse")
        {
            this._chatAgent = chatAgent;
        }

        public override async ValueTask<WorkflowOutputs> HandleAsync(IntentResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            string responseText = await this._chatAgent.GenerateChatResponseAsync(message.UserMessage, cancellationToken);
            return new WorkflowOutputs
            {
                ResponseText = responseText,
                HandledByIntent = false,
                Source = SubAgentNames.ChatAgent
            };
        }
    }

    internal sealed class WorkflowOutputExecutor : Executor<WorkflowOutputs, WorkflowOutputs>
    {
        public WorkflowOutputExecutor() : base("WorkflowOutput")
        {
        }

        public override async ValueTask<WorkflowOutputs> HandleAsync(WorkflowOutputs message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            await context.YieldOutputAsync(message, cancellationToken);
            return message;
        }
    }
}
