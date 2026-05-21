using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Models;
using XiaoZhi.Net.Server.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents
{
    internal sealed class IntentAgent : BaseAgent<IntentAgent>, IIntentAgent
    {
        private const string INTENT_INSTRUCTIONS = """
You are an intent detector for a smart voice device.
Your job is to determine whether the user wants to trigger a built-in device function instead of normal conversation.

Currently supported intents:
- play_music: the user wants the device to play music, listen to songs, play one song, play some music, random music, or a named local music file.

Return JSON only with these fields:
- matched: boolean
- intent_name: string
- is_random: boolean
- music_name: string or null
- reply: string

Rules:
1. If the user only wants normal conversation, knowledge Q and A, storytelling, weather, translation, or anything that should continue to chat, set matched to false.
2. Set intent_name to play_music only when the user clearly wants music playback.
3. If the user asks for random music or does not provide a music name, set is_random to true and music_name to null.
4. If the user names a specific song, singer, or music title, set is_random to false and fill music_name with the best extracted text.
5. reply should be a short plain Chinese sentence that can be spoken after the function runs. Leave it empty when matched is false.
6. Never output markdown, explanations, or any text outside the JSON object.
""";

        private ChatClientAgent? _intentClientAgent;

        private AgentSession? _agentSession;

        public IntentAgent(IServiceProvider serviceProvider, ILogger<IntentAgent> logger) : base(SubAgentNames.IntentAgent, serviceProvider, logger)
        {
        }

        public override int Order => 5;

        public override bool SupportsStreaming => false;

        public override bool Build(LLMAgentBuildConfig agentBuildConfig)
        {
            try
            {
                this.Prompt = INTENT_INSTRUCTIONS;
                IChatClient chatClient = this.ServiceProvider.GetRequiredKeyedService<IChatClient>($"LLM_{agentBuildConfig.AgentSetting.ModelName}");

                ChatClientAgentOptions options = new ChatClientAgentOptions
                {
                    Name = SubAgentNames.IntentAgent,
                    Description = $"the agent of {SubAgentNames.IntentAgent}",
                    ChatOptions = new ChatOptions
                    {
                        Instructions = this.Prompt,
                        Temperature = 0.1f,
                        MaxOutputTokens = 160,
                        ResponseFormat = ChatResponseFormat.ForJsonSchema<IntentResult>()
                    }
                };

                this._intentClientAgent = new ChatClientAgent(
                    chatClient: chatClient,
                    options: options,
                    services: this.ServiceProvider);

                this._agentSession = this._intentClientAgent.CreateSessionAsync(agentBuildConfig.SessionPrivateProvider.Token).GetAwaiter().GetResult();
                //todo
                //this.Logger.LogInformation(Lang.ChatAgent_Build_Built, this.ProviderType, this.ModelName, agentBuildConfig.AgentSetting.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                //todo
                //this.Logger.LogError(ex, Lang.ChatAgent_Build_BuiltFailed, this.ProviderType, this.ModelName, agentBuildConfig.AgentSetting.ModelName);
                return false;
            }
        }

        protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
        {
            return protocolBuilder.ConfigureRoutes(routeBuilder =>
            {
                routeBuilder.AddHandler<string, ValueTask<IntentResult>>(this.DetectIntentAsync);
            });
        }

        [MessageHandler]
        public async ValueTask<IntentResult> DetectIntentAsync(string userMessage, IWorkflowContext workflowContext, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(this.DeviceId, this.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this._intentClientAgent is null || this._agentSession is null)
            {
                //todo
                throw new InvalidOperationException("");
            }
            IAsyncEnumerable<AgentResponseUpdate> updates = this._intentClientAgent.RunStreamingAsync(userMessage, session: this._agentSession, cancellationToken: token);
            AgentResponse agentResponse = await updates.ToAgentResponseAsync(token);

            string cleanContent = MarkdownCleaner.CleanMarkdown(
                Regex.Replace(Regex.Unescape(agentResponse.Text), @"<think>.*?</think>", string.Empty, RegexOptions.Singleline));

            string jsonPayload = ExtractJsonPayload(cleanContent);
            IntentResult? intentResult = JsonSerializer.Deserialize<IntentResult>(jsonPayload, JsonHelper.OPTIONS);
            if (intentResult is null)
            {
                throw new JsonException($"Failed to deserialize intent result: {cleanContent}");
            }

            intentResult.UserMessage = userMessage;
            return intentResult;
        }

        private static string ExtractJsonPayload(string content)
        {
            int start = content.IndexOf('{');
            int end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return content.Substring(start, end - start + 1);
            }

            return content;
        }

        public override void Dispose()
        {
        }
    }
}