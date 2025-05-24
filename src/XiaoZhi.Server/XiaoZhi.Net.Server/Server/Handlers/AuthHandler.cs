using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class AuthHandler : BaseHandler
    {
        private readonly AuthOption _authOption;
        private readonly IBasicVerify? _basicVerify;

        public AuthHandler(XiaoZhiConfig config, ILogger logger, IBasicVerify? basicVerify) : base(config, logger)
        {
            this._authOption = config.AuthOption;
            this._basicVerify = basicVerify;
        }
        public override string HandlerName => nameof(AuthHandler);

        public bool Handle(IDictionary<string, string> headers, IPEndPoint userEndPoint)
        {
            if (!this._authOption.Enabled)
            {
                return true;
            }

            if (headers.TryGetValue("device-id", out string deviceId) && (this._authOption.AllowedDevices?.Contains(deviceId) ?? false))
            {
                return true;
            }

            if (headers.TryGetValue("authorization", out string authHeader))
            {
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    this.Logger.Error($"Missing or invalid authorization header: {authHeader}");
                    return false;
                }

                string token = authHeader.Split(" ")[1];

                if (!this._authOption.Tokens.Select(t => t.Token).Contains(token))
                {
                    this.Logger.Error($"Invalid token: {token}");
                    return false;
                }
                this.Logger.Information($"Authentication successful - Device: {deviceId}, Token: {this._authOption.Tokens.FirstOrDefault(t => t.Token == token)}");

                if (this._basicVerify != null)
                {
                    return this._basicVerify.Verify(deviceId, token, userEndPoint);
                }

                return true;
            }


            return true;
        }


    }
}
