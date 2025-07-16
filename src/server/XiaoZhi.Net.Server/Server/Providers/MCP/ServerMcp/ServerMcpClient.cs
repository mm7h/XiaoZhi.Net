using ModelContextProtocol.Client;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers.MCP.ServerMcp
{
    internal class ServerMcpClient : BaseMcpClient
    {
        private IMcpClient? _mcpClient;

        public ServerMcpClient(Session session, ModelSetting mcpSetting, ILogger logger) : base(session, mcpSetting, logger)
        {

        }
        public override string ProviderType => "server_mcp";

        public override bool Build()
        {
            try
            {
                if (this.ModelName.ToLower() == "stdio-client")
                {
                    string name = this.ModelSetting.Config.Name;
                    string command = this.ModelSetting.Config.Command;
                    List<string> arguments = this.ModelSetting.Config.Arguments?.ToObject<List<string>>() ?? new List<string>();

                    var transport = new StdioClientTransport(new()
                    {
                        Name = name,
                        Command = command,
                        Arguments = arguments
                    });
                    
                    this._mcpClient = McpClientFactory.CreateAsync(transport).GetAwaiter().GetResult();
                    var tools = this._mcpClient.ListToolsAsync().GetAwaiter().GetResult();
                    //this.AddTools(tools.ToList());
                }


                return true;
            }
            catch (Exception)
            {

                throw;
            }
        }

        public override void Dispose()
        {
            this._mcpClient?.DisposeAsync();
        }

        protected override async Task SendMCPMessageAsync<TMessage>(TMessage message)
        {

        }
    }
}
