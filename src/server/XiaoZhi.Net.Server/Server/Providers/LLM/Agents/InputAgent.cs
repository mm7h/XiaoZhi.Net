using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;

namespace XiaoZhi.Net.Server.Providers.LLM.Agents
{
    internal class InputAgent : BaseAgent<InputAgent>
    {
        public InputAgent(IServiceProvider serviceProvider, ILogger<InputAgent> logger) : base(SubAgentNames.InputAgent, serviceProvider, logger)
        {
        }

        public override int Order => 1;

        private string _intentType = "None";

        public override bool Build(LLMAgentBuildConfig buildConfig)
        {
            this._intentType = buildConfig.AgentSetting.Config.GetValueOrDefault("IntentType", "None");
            return true;
        }

        protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
        {
            return protocolBuilder.ConfigureRoutes(routeBuilder =>
            {
                routeBuilder.AddHandler<string>(this.PreDialogueHandlerAsync);
            })
            .SendsMessage<WorkflowPreInputs>();
        }

        public async ValueTask PreDialogueHandlerAsync(string userMessage, IWorkflowContext context, CancellationToken token)
        {
            bool intentRequired = string.Compare("IntentLlm", this._intentType, StringComparison.OrdinalIgnoreCase) == 0;
            WorkflowPreInputs preInputs = new WorkflowPreInputs(intentRequired, userMessage);

            this.Logger.LogDebug("设备 {deviceId}发来消息: {userMessage}", this.DeviceId, userMessage);

            await context.SendMessageAsync(preInputs, token);
        }


        public override void Dispose()
        {
        }
    }
}
