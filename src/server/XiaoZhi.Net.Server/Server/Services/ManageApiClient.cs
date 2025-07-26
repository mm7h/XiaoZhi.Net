using Flurl;
using Flurl.Http;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Common.Exceptions;

namespace XiaoZhi.Net.Server.Services
{
    internal class ManageApiClient
    {
        private readonly string _baseApiUrl;
        public ManageApiClient(XiaoZhiApiConfig apiConfig)
        {
            this._baseApiUrl = apiConfig.ManageApiUrl;
        }

        public async Task<PrivateModelsConfig?> LoadConfigFromApi(string deviceId, string clientId)
        {
            var postBody = new
            {
                MacAddress = deviceId,
                ClientId = clientId
            };
            var response = await _baseApiUrl.AppendPathSegment(ApiActions.GetDeviceConfig)
                       .PostJsonAsync(postBody).ReceiveJson<ApiResponse<PrivateModelsConfig>>();

            switch (response.Code)
            {
                case 0:
                    return response.Data;
                case 10041:
                    throw new DeviceNotFoundException();
                case 10042:
                    throw new DeviceBindException(response.Msg);
                default:
                    throw new Exception($"Unknown exception occurred while loading config from api: {response.Msg}");
            }
        }

        internal async Task SaveMemoryAsync(string deviceId, string sessionId, List<Dialogue> dialogues)
        {
            throw new NotImplementedException();
        }
    }
}
