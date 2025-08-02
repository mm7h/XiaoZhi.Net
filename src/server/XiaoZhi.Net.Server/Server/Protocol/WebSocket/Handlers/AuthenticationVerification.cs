using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuperSocket.WebSocket;
using SuperSocket.WebSocket.Server;
using System.Net;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Protocol.WebSocket.Handlers
{
    internal class AuthenticationVerification
    {
        public static ValueTask<bool> VerifyAsync(WebSocketSession session, WebSocketPackage package)
        {
            string token = session.HttpHeader.Items.Get("authorization") ?? string.Empty;
            if (session.RemoteEndPoint is IPEndPoint ipEndPoint)
            {
                string ip = ipEndPoint.Address.ToString();
                int port = ipEndPoint.Port;

                string? deviceId = session.HttpHeader.Items.Get("device-id");

                if (string.IsNullOrEmpty(deviceId))
                {
                    session.Logger.LogError("Cannot get the device id from ip: {ip} authentication failed.", ip);
                    return ValueTask.FromResult(false);
                }

                bool verifyResult = true;

                XiaoZhiConfig config = session.Server.ServiceProvider.GetRequiredService<XiaoZhiConfig>();

                if (config.AuthEnabled)
                {
                    IBasicVerify? basicVerify = session.Server.ServiceProvider.GetService<IBasicVerify>();
                    if (basicVerify is not null)
                    {
                        verifyResult = basicVerify.Verify(deviceId, token, ipEndPoint);
                    }
                }

                if (verifyResult)
                {
                    session.Logger.LogInformation("New device: {deviceId} with ip {ip} connected", deviceId, ip);
                    return ValueTask.FromResult(true);
                }
                else
                {
                    session.Logger.LogError("The device {deviceId} from ip: {ip} authentication failed.", deviceId, ip);
                    return ValueTask.FromResult(false);
                }
            }
            else
            {
                session.Logger.LogError("Cannot get the ip info from the session: {sessionId}.", session.SessionID);
                return ValueTask.FromResult(false);
            }
        }
    }
}
