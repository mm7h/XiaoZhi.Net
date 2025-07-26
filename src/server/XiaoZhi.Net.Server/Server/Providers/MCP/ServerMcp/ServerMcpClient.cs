using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers.MCP.ServerMcp
{
    internal class ServerMcpClient : BaseMcpClient, ISubMcpClient
    {
        private ModelContextProtocol.Client.IMcpClient? _mcpClient;

        public ServerMcpClient(Session session, ModelSetting mcpSetting, ILogger logger) : base(session, mcpSetting, logger)
        {

        }
        public override string ProviderType => SubMCPClientTypeNames.DeviceMcpClient;

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
                    IList<McpClientTool> tools = this._mcpClient.ListToolsAsync().GetAwaiter().GetResult();
                    foreach (McpClientTool tool in tools)
                    {
                        this.Logger.LogInformation($"Got mcp tools: {tool.Name}, Description: {tool.Description}");
#pragma warning disable SKEXP0001
                        this.AddTool(tool.Name,tool.AsKernelFunction());
#pragma warning restore SKEXP0001
                    }
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
