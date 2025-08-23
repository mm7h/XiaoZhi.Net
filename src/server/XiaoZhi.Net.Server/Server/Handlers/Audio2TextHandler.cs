using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class Audio2TextHandler : BaseHandler, IInHandler<CircularBuffer>, IOutHandler<string>
    {
        private readonly IAsr _asr;
        private readonly IPunctuation _punctuation;
        private readonly int _sampleRate;
        private readonly int _frameSize;

        public Audio2TextHandler([FromKeyedServices(GlobalProviderNames.GLOBAL_ASR)] IAsr asr, [FromKeyedServices(GlobalProviderNames.GLOBAL_PUNCTUATION)] IPunctuation punctuation, [FromKeyedServices(GlobalProviderNames.GLOBAL_AUDIO_DECODER)] IAudioDecoder audioDecoder, XiaoZhiConfig config, ILogger<Audio2TextHandler> logger) : base(config, logger)
        {
            this._asr = asr;
            this._punctuation = punctuation;
            this._sampleRate = audioDecoder.SampleRate;
            this._frameSize = audioDecoder.FrameSize;
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
                string speechText;
                if (session.PrivateProvider is not null && session.PrivateProvider.Asr is not null)
                {
                    speechText = await session.PrivateProvider.Asr.ConvertSpeechText(workflow.Data, this._sampleRate, this._frameSize, session.SessionCtsToken);
                }
                else
                {
                    speechText = await this._asr.ConvertSpeechText(workflow.Data, this._sampleRate, this._frameSize, session.SessionCtsToken);
                }

                if (string.IsNullOrEmpty(DialogueHelper.GetStringNoPunctuationOrEmoji(speechText)))
                {
                    session.Reset();
                    this.Logger.LogDebug("Device {deviceId} no speak.", session.DeviceId);
                    return;
                }

                if (!session.IsDeviceBinded)
                {
                    await this.CheckBindDevice(session);
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

        private async Task CheckBindDevice(Session session)
        {
            if (!string.IsNullOrEmpty(session.BindCode) && session.BindCode.Length == 6)
            {
                if (session.BindCode.Length != 6)
                {
                    this.Logger.LogError("Invalid bind code {code} for the device: {deviceId}", session.BindCode, session.DeviceId);
                    string bindErrorMsg = "绑定码格式错误，请检查配置。";
                    await session.SendOutter.SendSttMessageAsync(bindErrorMsg);
                    return;
                }

                string text = $"请登录控制面板，输入{session.BindCode}，绑定设备。";
                await session.SendOutter.SendSttMessageAsync(text);

                string bindCodePromptFile = Path.Combine(Environment.CurrentDirectory, this.Config.DeviceBindSetting.BindCodePromptFilePath);

                List<string> audioFilePaths = new List<string>
                {
                    bindCodePromptFile
                };

                // 逐个获取需要播放的数字的音频文件
                for (int i = 0; i < session.BindCode.Length; i++)
                {
                    char digit = session.BindCode[i];
                    string numPath = Path.Combine(Environment.CurrentDirectory, this.Config.DeviceBindSetting.BindCodeDigitFolderPath, $"{digit}.wav");
                    audioFilePaths.Add(numPath);
                }
                await session.HandlerPipeline.PushAudioToSendAsync(text, Emotion.Happy, audioFilePaths.ToArray());
            }
            else
            {
                this.Logger.LogError("Invalid bind code {code} for the device: {deviceId}", session.BindCode, session.DeviceId);
                string text = "没有找到该设备的版本信息，请正确配置 OTA地址，然后重新编译固件。";
                await session.SendOutter.SendSttMessageAsync(text);

                string bindNotFoundFile = Path.Combine(Environment.CurrentDirectory, this.Config.DeviceBindSetting.BindNotFoundFilePath);
                await session.HandlerPipeline.PushAudioToSendAsync(text, Emotion.Neutral, bindNotFoundFile);
            }
        }

        public void Dispose()
        {
            this.NextWriter.Complete();
        }
    }
}
