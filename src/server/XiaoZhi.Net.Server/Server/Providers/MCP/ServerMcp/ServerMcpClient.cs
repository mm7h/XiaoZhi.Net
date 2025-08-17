using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Dtos;

namespace XiaoZhi.Net.Server.Providers.MCP.ServerMcp
{
    internal class ServerMcpClient : BaseMcpClient<ServerMcpClient>, ISubMcpClient
    {
        private ModelContextProtocol.Client.IMcpClient? _mcpClient;

        public ServerMcpClient(ILogger<ServerMcpClient> logger) : base(logger)
        {
        }

        public override string ModelName => SubMCPClientTypeNames.DeviceMcpClient;
        public override string ProviderType => "SubMcpClient";

        public override bool Build(MCPClientBuildConfig config)
        {
            try
            {
                this.InitSession(config);
                ModelSetting modelSetting = config.ModelSetting;

                if (this.ModelName.ToLower() == "stdio-client")
                {
                    string name = modelSetting.Config.Name;
                    string command = modelSetting.Config.Command;
                    List<string> arguments = modelSetting.Config.Arguments?.ToObject<List<string>>() ?? new List<string>();

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
                        this.AddTool(tool.Name, tool.AsKernelFunction());
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
