using Serilog;
using SherpaOnnx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
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

        public Audio2TextHandler(IAsr asr, IPunctuation punctuation, IAudioDecoder audioDecoder, XiaoZhiConfig config, ILogger logger) : base(config, logger)
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
                string speechText = await this._asr.ConvertSpeechText(workflow.Data, this._sampleRate, this._frameSize, session.SessionCtsToken);

                if (string.IsNullOrEmpty(DialogueHelper.GetStringNoPunctuationOrEmoji(speechText)))
                {
                    session.Reset();
                    this.Logger.Debug("Device {deviceId} no speak.", session.DeviceId);
                    return;
                }

                if (!session.IsDeviceBinded)
                {
                    await this.CheckBindDevice(session);
                    return;
                }

                await this.SendOutter.SendSttMessageAsync(speechText);
                this.Logger.Debug("Device {deviceId} speak the text: {speechText}", session.DeviceId, speechText);
                speechText = await this._punctuation.AppendPunctuationAsync(speechText!, session.SessionCtsToken);

                await this.NextWriter!.WriteAsync(workflow.NextFlow(speechText));
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
                    this.Logger.Error("Invalid bind code {code} for the device: {deviceId}", session.BindCode, session.DeviceId);
                    string bindErrorMsg = "绑定码格式错误，请检查配置。";
                    await session.SendOutter.SendSttMessageAsync(bindErrorMsg);
                    return;
                }

                string text = $"请登录控制面板，输入{session.BindCode}，绑定设备。";
                await session.SendOutter.SendSttMessageAsync(text);

                // 使用通用解码方法解码音频文件，现在返回元组(音频数据, 时长)
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
                await this.PlayAudioFile(session, text, Emotion.Thinking, audioFilePaths.ToArray());
            }
            else
            {
                this.Logger.Error("Invalid bind code {code} for the device: {deviceId}", session.BindCode, session.DeviceId);
                string text = "没有找到该设备的版本信息，请正确配置 OTA地址，然后重新编译固件。";
                await session.SendOutter.SendSttMessageAsync(text);

                string bindNotFoundFile = Path.Combine(Environment.CurrentDirectory, this.Config.DeviceBindSetting.BindNotFoundFilePath);
                await this.PlayAudioFile(session, text, Emotion.Neutral, bindNotFoundFile);
            }
        }

        private async Task PlayAudioFile(Session session, string text, Emotion emotion, params string[] audioFilePaths)
        {
            try
            {
                double totalDuration = 0;
                List<float> totalAudioData = new List<float>();

                foreach (string audioFilePath in audioFilePaths)
                {
                    if (!File.Exists(audioFilePath))
                    {
                        this.Logger.Error("The audio file does not exist: {filePath}", audioFilePath);
                        continue;
                    }

                    var (audioData, duration) = AudioFileHelper.DecodeAudioFile(audioFilePath, this._frameSize);
                    totalAudioData.AddRange(audioData);
                    totalDuration += duration;
                }

                Func<Task> sendSentenceAction = new Func<Task>(async () =>
                {
                    if (session.ShouldIgnore())
                    {
                        return;
                    }
                    await session.SendOutter.SendSttMessageAsync(text);
                    await this.SendOutter.SendTtsMessageAsync("start");
                    await Task.Delay((int)totalDuration, session.SessionCtsToken);
                    await this.SendOutter.SendLlmMessageAsync(emotion);
                    await this.SendOutter.SendTtsMessageAsync("stop");
                });
                await session.SentenceTimeAxisContext.AddSendSentenceActionAsync(sendSentenceAction, session.SessionCtsToken);
                await session.HandlerPipeline.PushAudioToSendAsync(totalAudioData.ToArray());
            }
            catch (Exception ex)
            {
                this.Logger.Error(ex, "播放音频文件失败");
            }
        }

        public void Dispose()
        {
            this.NextWriter.Complete();
        }
    }
}
