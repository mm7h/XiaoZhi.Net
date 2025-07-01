using ModelContextProtocol.Protocol;
using Serilog;
using System.Reflection;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Services
{
    internal class MCPClient2Xiaozhi
    {
        private readonly Session _session;
        private readonly ISendOutter _sendOutter;
        private readonly ILogger _logger;

        public MCPClient2Xiaozhi(Session session, ISendOutter sendOutter, ILogger logger)
        {
            this._session = session;
            this._sendOutter = sendOutter;
            this._logger = logger;
        }


        public async Task Initialize()
        {
            //vision


            var @params = new
            {
                ProtocolVersion = "2024-11-05",
                Capabilities = new
                {
                    Roots = new { ListChanged = true },
                    Sampling = new { },
                    //vision,
                },
                ClientInfo = new
                {
                    Name = "XiaoZhi.Net.Client",
                    Version = Assembly.GetEntryAssembly().GetName().Version.ToString(),
                }
            };

            JsonRpcRequest request = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Method = "initialize",
                Id = new RequestId(1),
                Params = @params.ToNode()
            };

            this._logger.Debug("Session {SessionId} send the initialize message.", this._session.SessionId);

            await this.SendMCPMessage(request);
        }

        public async Task RequestToolsList()
        {
            JsonRpcRequest request = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Method = "tools/list",
                Id = new RequestId(2)
            };

            this._logger.Debug("Session {SessionId} request tools list.", this._session.SessionId);

            await this.SendMCPMessage(request);
        }



        public async Task SendMCPMessage<TMessage>(TMessage message)
        {
            if (message is null)
            {
                this._logger.Error("Cannot send null message to MCP.");
                return;
            }
            if (!this._session.IsSupportMCP)
            {
                this._logger.Warning("Session {SessionId} does not support MCP, cannot send message.", this._session.SessionId);
                return;
            }

            var mcpMessage = new
            {
                Type = "mcp",
                payload = message
            };

            string jsonMessage = JsonHelper.Serialize(mcpMessage);
            await this._sendOutter.SendAsync(this._session.SessionId, jsonMessage);
        }
    }
}
