using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Handlers;

namespace XiaoZhi.Net.Server.Management
{
    internal class AdvancedManager : IAdvanced
    {
        private readonly DialogueHandler _dialogueHandler;
        private readonly SessionManager _sessionManager;

        public AdvancedManager(DialogueHandler dialogueHandler, SessionManager sessionManager)
        {
            this._dialogueHandler = dialogueHandler;
            this._sessionManager = sessionManager;
        }

        public static void RegisterServices(IServiceCollection services, XiaoZhiConfig config)
        {
            services.AddSingleton<IAdvanced, AdvancedManager>();
        }

        public IDictionary<string, SessionDevice> GetAllSessions()
        {
            return this._sessionManager.GetAllSessions();
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
