using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Helpers;

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
                    string? name = modelSetting.Config.GetConfigValueOrDefault("Name");
                    string command = modelSetting.Config.GetConfigValueOrDefault("Command", string.Empty);
                    List<string> arguments = modelSetting.Config.GetConfigValueOrDefault("Arguments", new List<string>());

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

        protected override Task SendMCPMessageAsync<TMessage>(TMessage message)
        {
            // todo
            return Task.CompletedTask;
        }

    }
}
