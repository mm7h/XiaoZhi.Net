using Flurl;
using Flurl.Http;
using Flurl.Http.Configuration;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Services
{
    internal class ManageApiClient
    {
        private readonly IFlurlClient _manageApiClient;

        public ManageApiClient(IFlurlClientCache clientCache)
        {
            this._manageApiClient = clientCache.Get("ManageApi");
        }

        public async Task<PrivateModelsConfig?> LoadConfigFromApi(string deviceId, string sessionId)
        {
            var response = await this._manageApiClient
                .Request()
                .AppendPathSegment(ApiActions.GetPrivateConfig)
                .SetQueryParams(new
                {
                    deviceId,
                    sessionId
                })
                .GetJsonAsync<ApiResponse<PrivateModelsConfig>>();

            switch (response.Code)
            {
                case 0:
                    return response.Data;
                case 10041:
                    throw new DeviceNotFoundException();
                case 10042:
                    throw new DeviceBindException(response.Msg);
                default:
                    throw new Exception(string.Format(Lang.ManageApiClient_LoadConfigFromApi_UnknownException, response.Msg));
            }
        }

        public Task SaveMemoryAsync(string deviceId, string sessionId, ChatHistory chats)
        {
            throw new NotImplementedException();
        }
    }
}
