using Serilog;
using System.Collections.Generic;
using System.Net;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class AuthHandler : BaseHandler
    {
        private readonly bool _authEnabled;
        private readonly IBasicVerify? _basicVerify;

        public AuthHandler(XiaoZhiConfig config, ILogger logger, IBasicVerify? basicVerify) : base(config, logger)
        {
            this._authEnabled = config.AuthEnabled;
            this._basicVerify = basicVerify;
        }
        public override string HandlerName => nameof(AuthHandler);

        public bool Handle(IDictionary<string, string> headers, string deviceId, IPEndPoint userEndPoint)
        {
            if (!this._authEnabled)
            {
                return true;
            }

            if (this._basicVerify != null && headers.TryGetValue("authorization", out string authToken))
            {
                this.Logger.Information("Authentication checking - Device: {deviceId}, Token: {token}, IP end point: {userEndPoint}", deviceId, authToken, userEndPoint.ToString());

                return this._basicVerify.Verify(deviceId, authToken, userEndPoint);
            }


            return false;
        }


    }
}
