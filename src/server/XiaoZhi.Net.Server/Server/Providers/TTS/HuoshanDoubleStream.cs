using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers.TTS
{
    internal class HuoshanDoubleStream : BaseProvider<HuoshanDoubleStream, ModelSetting>, ITts
    {

        public HuoshanDoubleStream(ILogger<HuoshanDoubleStream> logger) : base(logger)
        {
        }

        public override string ModelName => nameof(HuoshanDoubleStream);
        public override string ProviderType => "tts";

        public event Action<string, OutSegment>? OnBeforeProcessing;
        public event Action<string, float[]>? OnProcessing;
        public event Action<string, float[], OutSegment, double>? OnProcessed;

        public int GetTtsSampleRate()
        {
            return 16000;
        }

        public override bool Build(ModelSetting modelSetting)
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
