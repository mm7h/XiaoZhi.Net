using Microsoft.Extensions.Logging;

namespace XiaoZhi.Net.Server.Providers.ASR.Huoshan
{
    internal sealed class HuoshanBidirectionASR : BaseHuoshanASR<HuoshanBidirectionASR>
    {
        private const string Endpoint = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async";

        public HuoshanBidirectionASR(ILogger<HuoshanBidirectionASR> logger) : base(logger)
        {
        }

        public override string ModelName => nameof(HuoshanBidirectionASR);
        protected override string ServiceEndpoint => Endpoint;
    }
}
