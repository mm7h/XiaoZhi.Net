using Microsoft.Extensions.Logging;

namespace XiaoZhi.Net.Server.Providers.ASR.Huoshan
{
    internal sealed class HuoshanUnidirectionalASR : BaseHuoshanASR<HuoshanUnidirectionalASR>
    {
        private const string Endpoint = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_nostream";

        public HuoshanUnidirectionalASR(ILogger<HuoshanUnidirectionalASR> logger) : base(logger)
        {
        }

        public override string ModelName => nameof(HuoshanUnidirectionalASR);
        protected override string ServiceEndpoint => Endpoint;
        protected override bool SupportsLanguage => true;
    }
}
