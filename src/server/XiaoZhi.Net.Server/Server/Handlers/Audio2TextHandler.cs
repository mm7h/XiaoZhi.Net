using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using SherpaOnnx;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal class Audio2TextHandler : BaseHandler, IInHandler<CircularBuffer>, IOutHandler<string>
    {
        private readonly ObjectPool<Workflow<CircularBuffer>> _circularBufferWorkflowPool;
        private readonly ObjectPool<Workflow<string>> _stringWorkflowPool;
        private IAsr? _asr;

        public Audio2TextHandler(ObjectPool<Workflow<CircularBuffer>> circularBufferWorkflowPool, 
            ObjectPool<Workflow<string>> stringWorkflowPool,
            XiaoZhiConfig config,
            ILogger<Audio2TextHandler> logger) : base(config, logger)
        {
            this._circularBufferWorkflowPool = circularBufferWorkflowPool;
            this._stringWorkflowPool = stringWorkflowPool;
        }

        public override string HandlerName => nameof(Audio2TextHandler);
        public ChannelReader<Workflow<CircularBuffer>> PreviousReader { get; set; } = null!;
        public ChannelWriter<Workflow<string>> NextWriter { get; set; } = null!;

        public override bool Build(PrivateProvider privateProvider)
        {
            Session session = this.SendOutter.GetSession();
            if (privateProvider.Asr is null)
            {
                this.Logger.LogError("ASR provider is not configured for the device: {deviceId}.", session.DeviceId);
                return false;
            }
            this._asr = privateProvider.Asr;
            this._asr.RegisterDevice(session.DeviceId, session.SessionId);
            this.RegisterCancellationToken();
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

            if (!this.CheckWorkflowValid(workflow))
            {
                return;
            }

            if (this._asr is null)
            {
                this.Logger.LogError("ASR provider is not configured for the device: {deviceId}.", session.DeviceId);
                return;
            }
            try
            {
                if (!session.IsDeviceBinded)
                {
                    var notBindWorkflow = this._stringWorkflowPool.Get();
                    notBindWorkflow.Initialize(session, "NOT_BIND");
                    await this.NextWriter.WriteAsync(notBindWorkflow);
                    return;
                }

                string speechText = await this._asr.ConvertSpeechTextAsync(workflow, this.Config.AudioSetting.SampleRate, this.Config.AudioSetting.FrameSize, this.HandlerToken);

                if (string.IsNullOrEmpty(speechText) || string.IsNullOrEmpty(DialogueHelper.GetStringNoPunctuationOrEmoji(speechText)))
                {
                    session.Reset();
                    this.Logger.LogDebug("Device {deviceId} no speak.", session.DeviceId);
                    return;
                }

                await this.SendOutter.SendSttMessageAsync(speechText);
                this.Logger.LogDebug("Device {deviceId} speak the text: {speechText}", session.DeviceId, speechText);

                var nextWorkflow = this._stringWorkflowPool.Get();
                nextWorkflow.Initialize(session, speechText);
                await this.NextWriter.WriteAsync(nextWorkflow);
            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "audio to text");
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to process the message packet from device: {deviceId}.", session.DeviceId);
            }
        }

        public override void Dispose()
        {
            this.NextWriter.Complete();
            base.Dispose();
        }
    }
}
