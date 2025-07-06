using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers.TTS
{
    internal class HuoshanDoubleStream : BaseProvider, ITts
    {
        public HuoshanDoubleStream(XiaoZhiConfig config, ILogger logger) : this(config.TtsSetting, logger)
        {
        }
        public HuoshanDoubleStream(ModelSetting ttsSetting, ILogger logger) : base(ttsSetting, logger)
        {
        }
        public override string ProviderType => "tts";

        public event Action<string, OutSegment>? OnBeforeProcessing;
        public event Action<string, float[]>? OnProcessing;
        public event Action<string, OutSegment, int>? OnProcessed;

        public int GetTtsSampleRate()
        {
            return 16000;
        }

        public override bool Build()
        {
            return true;
        }

        public async Task SynthesisAsync(Workflow<OutSegment> sessionContext, Session session, CancellationToken token)
        {

        }

        public override void Dispose()
        {

        }
    }
}
