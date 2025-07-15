using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.MCP.DeviceMcp
{
    internal class DeviceMcpClient : BaseMcpClient
    {
        public DeviceMcpClient(Session session, ILogger logger) : base(session, logger)
        {

        }

        public override string ProviderType => "device mcp";

        public override bool Build()
        {
            return true;
        }

        public override Task SendMCPMessage<TMessage>(TMessage message)
        {
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            
        }
    }
}
