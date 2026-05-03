using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.MCP.ServerMcp
{
    /// <summary>服务端 MCP 客户端 — 当前为占位实现，尚未接入主链路</summary>
    internal class ServerMcpClient : BaseMcpClient<ServerMcpClient>, ISubMcpClient
    {
        public ServerMcpClient(ILogger<ServerMcpClient> logger) : base(logger)
        {
        }

        public override string ModelName => SubMCPClientTypeNames.DeviceMcpClient;
        public override string ProviderType => "SubMcpClient";

        public override bool Build(MCPClientBuildConfig config)
        {
            this.InitSession(config);
            // todo: 服务端 MCP 客户端功能尚未实现
            return true;
        }

        public override void Dispose()
        {
        }

        protected override Task SendMCPMessageAsync<TMessage>(TMessage message)
        {
            // todo
            return Task.CompletedTask;
        }
    }
}
