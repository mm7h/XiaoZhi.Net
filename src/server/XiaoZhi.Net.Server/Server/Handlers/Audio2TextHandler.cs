using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class Audio2TextHandler : BaseHandler, IInHandler<CircularBuffer>, IOutHandler<string>
    {
        private readonly IAsr _asr;
        private readonly IPunctuation _punctuation;

        public Audio2TextHandler([FromKeyedServices(GlobalProviderNames.GLOBAL_ASR)] IAsr asr, 
            [FromKeyedServices(GlobalProviderNames.GLOBAL_PUNCTUATION)] IPunctuation punctuation,  
            XiaoZhiConfig config, ILogger<Audio2TextHandler> logger) : base(config, logger)
        {
            this._asr = asr;
            this._punctuation = punctuation;
        }

        public override string HandlerName => nameof(Audio2TextHandler);
        public IBizSendOutter SendOutter { get; set; } = null!;
        public ChannelReader<Workflow<CircularBuffer>> PreviousReader { get; set; } = null!;
        public ChannelWriter<Workflow<string>> NextWriter { get; set; } = null!;

        public async Task Handle()
        {
            await foreach (var reader in this.PreviousReader.ReadAllAsync()) await this.Handle(reader);
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
                    await this.NextWriter.WriteAsync(workflow.NextFlow("NOT_BIND"));
                    return;
                }

                string speechText;
                if (session.PrivateProvider is not null && session.PrivateProvider.Asr is not null)
                {
                    speechText = await session.PrivateProvider.Asr.ConvertSpeechText(workflow.Data, session.AudioSetting.SampleRate, session.AudioSetting.FrameSize, session.SessionCtsToken);
                }
                else
                {
                    speechText = await this._asr.ConvertSpeechText(workflow.Data, this.Config.AudioSetting.SampleRate, this.Config.AudioSetting.FrameSize, session.SessionCtsToken);
                }

                if (string.IsNullOrEmpty(DialogueHelper.GetStringNoPunctuationOrEmoji(speechText)))
                {
                    session.Reset();
                    this.Logger.LogDebug("Device {deviceId} no speak.", session.DeviceId);
                    return;
                }

                await this.SendOutter.SendSttMessageAsync(speechText);
                this.Logger.LogDebug("Device {deviceId} speak the text: {speechText}", session.DeviceId, speechText);
                speechText = await this._punctuation.AppendPunctuationAsync(speechText!, session.SessionCtsToken);

                await this.NextWriter.WriteAsync(workflow.NextFlow(speechText));
            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "audio to text");
            }
        }

        public void Dispose()
        {
            this.NextWriter.Complete();
        }
    }
}
