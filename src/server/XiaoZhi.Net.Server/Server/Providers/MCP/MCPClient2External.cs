using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers.MCP
{
    internal class MCPClient2External : BaseProvider
    {
        private readonly Session _currentSession;
        private readonly ILogger _logger;
        private readonly object _locker = new object();

        private bool _isReady = false;
        private List<Tool> _mcpTools = new List<Tool>();

        private IMcpClient? mcpClient;

        public MCPClient2External(Session session, ModelSetting mcpSetting, ILogger logger) : base(mcpSetting, logger)
        {
            this._currentSession = session;
            this._logger = logger;
        }

        public override string ProviderType => "mcp";

        public override bool Build()
        {
            throw new NotImplementedException();
        }

        public override void Dispose()
        {
            this.mcpClient?.DisposeAsync();
        }

        public async Task Initialize()
        {

        }
    }
}
