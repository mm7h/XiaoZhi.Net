using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using SherpaOnnx;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class Audio2TextHandler : BaseHandler, IInHandler<CircularBuffer>, IOutHandler<string>
    {
        private readonly ObjectPool<Workflow<CircularBuffer>> _circularBufferWorkflowPool;
        private readonly ObjectPool<Workflow<string>> _stringWorkflowPool;
        private IAsr _asr;

        public Audio2TextHandler([FromKeyedServices(GlobalProviderNames.GLOBAL_ASR)] IAsr asr,
            ObjectPool<Workflow<CircularBuffer>> circularBufferWorkflowPool, 
            ObjectPool<Workflow<string>> stringWorkflowPool,
            XiaoZhiConfig config,
            ILogger<Audio2TextHandler> logger) : base(config, logger)
        {
            this._asr = asr;
            this._circularBufferWorkflowPool = circularBufferWorkflowPool;
            this._stringWorkflowPool = stringWorkflowPool;
        }

        public override string HandlerName => nameof(Audio2TextHandler);
        public ChannelReader<Workflow<CircularBuffer>> PreviousReader { get; set; } = null!;
        public ChannelWriter<Workflow<string>> NextWriter { get; set; } = null!;

        public override bool Build(PrivateProvider privateProvider)
        {
            if (privateProvider.Asr is not null)
            {
                this._asr = privateProvider.Asr;
            }
            Session session = this.SendOutter.GetSession();
            this._asr.RegisterDevice(session.DeviceId, session.SessionId);
            return true;
        }

        public async Task Handle()
        {
            await foreach (var workflow in this.PreviousReader.ReadAllAsync())
            {
                try
                {
                    await this.Handle(workflow);
                }
                finally
                {
                    this._circularBufferWorkflowPool.Return(workflow);
                }
            }
        }

        public async Task Handle(Workflow<CircularBuffer> workflow)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            try
            {
                if (!session.IsDeviceBinded)
                {
                    var notBindWorkflow = this._stringWorkflowPool.Get();
                    notBindWorkflow.Initialize(workflow.SessionId, workflow.DeviceId, "NOT_BIND");
                    await this.NextWriter.WriteAsync(notBindWorkflow);
                    return;
                }

                string speechText = await this._asr.ConvertSpeechTextAsync(workflow, this.Config.AudioSetting.SampleRate, this.Config.AudioSetting.FrameSize, session.SessionCtsToken);

                if (string.IsNullOrEmpty(speechText) || string.IsNullOrEmpty(DialogueHelper.GetStringNoPunctuationOrEmoji(speechText)))
                {
                    session.Reset();
                    this.Logger.LogDebug("Device {deviceId} no speak.", session.DeviceId);
                    return;
                }

                await this.SendOutter.SendSttMessageAsync(speechText);
                this.Logger.LogDebug("Device {deviceId} speak the text: {speechText}", session.DeviceId, speechText);

                var nextWorkflow = this._stringWorkflowPool.Get();
                nextWorkflow.Initialize(workflow.SessionId, workflow.DeviceId, speechText);
                await this.NextWriter.WriteAsync(nextWorkflow);
            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "audio to text");
            }
        }

        public override void Dispose()
        {
            this.NextWriter.Complete();
        }
    }
}
