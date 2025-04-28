using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Models;
using XiaoZhi.Net.Server.Handlers;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Management
{
    internal class AdvancedManager : IAdvanced
    {
        private readonly DialogueHandler _dialogueHandler;
        private readonly IProtocolEngine _protocolEngine;

        public AdvancedManager(DialogueHandler dialogueHandler, IProtocolEngine protocolEngine)
        {
            this._dialogueHandler = dialogueHandler;
            this._protocolEngine = protocolEngine;
        }

        public static void RegisterServices(IServiceCollection services, XiaoZhiConfig config)
        {
            services.AddSingleton<IAdvanced, AdvancedManager>();
        }

        public IDictionary<string, SessionDevice> GetAllSessions()
        {
            return this._protocolEngine.GetAllSessions();
        }

        public async Task SendCustomMessage(string sessionId, string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return;
            }
            await this._dialogueHandler.SendCustomMessage(sessionId, content);
        }
    }
}
