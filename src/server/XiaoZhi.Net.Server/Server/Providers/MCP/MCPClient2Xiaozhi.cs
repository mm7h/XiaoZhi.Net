using ModelContextProtocol.Protocol;
using Serilog;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.MCP
{
    internal class MCPClient2Xiaozhi
    {
        private readonly Session _currentSession;
        private readonly ILogger _logger;
        private readonly object _locker = new object();

        private bool _isReady = false;
        private List<Tool> _mcpTools = new List<Tool>();

        public MCPClient2Xiaozhi(Session session, ILogger logger)
        {
            this._currentSession = session;
            this._logger = logger;
        }

        public ReadOnlyCollection<Tool> Tools => _mcpTools.AsReadOnly();

        // 将 IsReady 属性改为同步属性，并用 lock 保护
        public bool IsReady
        {
            get
            {
                lock (_locker)
                {
                    return _isReady;
                }
            }
            set
            {
                lock (_locker)
                {
                    _isReady = value;
                }
            }
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

            _logger.Debug("Session {SessionId} send the initialize message.", _currentSession.SessionId);

            await SendMCPMessage(request);
        }

        public async Task RequestToolsList()
        {
            JsonRpcRequest request = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Method = "tools/list",
                Id = new RequestId(2)
            };

            _logger.Debug("Session {SessionId} request tools list.", _currentSession.SessionId);

            await SendMCPMessage(request);
        }

        public async Task RequestToolsList(string cursor)
        {
            var @params = new { cursor };
            JsonRpcRequest request = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Method = "tools/list",
                Id = new RequestId(2),
                Params = @params.ToNode()
            };

            _logger.Debug("Session {SessionId} request tools list.", _currentSession.SessionId);

            await SendMCPMessage(request);
        }

        public async Task SendMCPMessage<TMessage>(TMessage message)
        {
            if (message is null)
            {
                _logger.Error("Cannot send null message to MCP.");
                return;
            }
            if (!_currentSession.IsSupportMCP)
            {
                _logger.Warning("Session {SessionId} does not support MCP, cannot send message.", _currentSession.SessionId);
                return;
            }

            var mcpMessage = new
            {
                Type = "mcp",
                payload = message
            };

            await this._currentSession.SendOutter.SendAsync(JsonHelper.Serialize(mcpMessage));
        }





        public void AddTool(Tool mcpTool)
        {
            lock (_locker)
            {
                _mcpTools.Add(mcpTool);
            }
        }


        public void AddTools(List<Tool> mcpTools)
        {
            lock (_locker)
            {
                _mcpTools.AddRange(mcpTools);
            }
        }
    }
}
