using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.AudioPlayer.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class Text2AudioHandler : BaseHandler, IInHandler<OutSegment>, IOutHandler<OutAudioSegment>
    {
        private readonly ITts _tts;
        private bool _privateTTSInitialized = false;
        private readonly IDeviceBindingPlayer _deviceBindPlayer;

        public Text2AudioHandler([FromKeyedServices(GlobalProviderNames.GLOBAL_TTS)] ITts tts,
            [FromKeyedServices(GlobalProviderNames.GLOBAL_DEVICE_BINDING_PLAYER)] IDeviceBindingPlayer deviceBindPlayer, XiaoZhiConfig config, ILogger<Text2AudioHandler> logger) : base(config, logger)
        {
            this._tts = tts;
            this._tts.OnBeforeProcessing += this.TTS_OnBeforeProcessing;
            this._tts.OnProcessed += this.TTS_OnProcessed;

            this._deviceBindPlayer = deviceBindPlayer;
            this._deviceBindPlayer.OnPlayStateChanged += this.OnPlayerStateChanged;
            this._deviceBindPlayer.OnAudioData += this.OnPlayerAudioDataAsync;
        }


        public override string HandlerName => nameof(Text2AudioHandler);
        public IBizSendOutter SendOutter { get; set; } = null!;
        public ChannelReader<Workflow<OutSegment>> PreviousReader { get; set; } = null!;
        public ChannelWriter<Workflow<OutAudioSegment>> NextWriter { get; set; } = null!;

        public async Task Handle()
        {
            await foreach (var reader in this.PreviousReader.ReadAllAsync()) await this.Handle(reader);
        }

        public async Task Handle(Workflow<OutSegment> workflow)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            if (!session.IsDeviceBinded)
            {
                await this.CheckBindDevice(session);
                return;
            }
            try
            {
                if (string.IsNullOrEmpty(workflow.Data.Content))
                {
                    this.Logger.LogInformation("No tts required, the query text is empty.");
                    return;
                }

                if (session.PrivateProvider is not null && session.PrivateProvider.Tts is not null)
                {
                    if (!this._privateTTSInitialized)
                    {
                        session.PrivateProvider.Tts.OnBeforeProcessing += this.TTS_OnBeforeProcessing;
                        session.PrivateProvider.Tts.OnProcessed += this.TTS_OnProcessed;
                        this._privateTTSInitialized = true;
                    }
                    await session.PrivateProvider.Tts.SynthesisAsync(workflow, session, session.SessionCtsToken);
                }
                else
                {
                    await this._tts.SynthesisAsync(workflow, session, session.SessionCtsToken);
                }

            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "text to audio");
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

                await this._deviceBindPlayer.PlayBindCodeAsync(session.BindCode, session.AudioSetting);
            }
            else
            {
                this.Logger.LogError("Invalid bind code {code} for the device: {deviceId}", session.BindCode, session.DeviceId);
                string text = "没有找到该设备的版本信息，请正确配置 OTA地址，然后重新编译固件。";
                await session.SendOutter.SendSttMessageAsync(text);

                await this._deviceBindPlayer.PlayNotFoundAsync(session.AudioSetting);
            }
        }

        private async void OnPlayerAudioDataAsync(float[] pcmData)
        {
            //await this.NextWriter2.WriteAsync(this.SendOutter.GetSession().ToWorkflow(pcmData));
        }

        private void OnPlayerStateChanged(PlaybackState state)
        {

        }

        public void Dispose()
        {
            this._tts.OnBeforeProcessing -= this.TTS_OnBeforeProcessing;
            this._tts.OnProcessed -= this.TTS_OnProcessed;
            Session session = this.SendOutter.GetSession();
            if (session is not null && session.PrivateProvider is not null && session.PrivateProvider.Tts is not null && this._privateTTSInitialized)
            {
                session.PrivateProvider.Tts.OnBeforeProcessing -= this.TTS_OnBeforeProcessing;
                session.PrivateProvider.Tts.OnProcessed -= this.TTS_OnProcessed;
            }
            this.NextWriter.Complete();
        }

        private void TTS_OnBeforeProcessing(string sessionId, OutSegment segment)
        {
            if (segment.IsFirst)
            {
                this.Logger.LogInformation("Send the first audio from segment: {content}", segment.Content);
            }
        }

        private void TTS_OnProcessed(string sessionId, float[] audioData, OutSegment segment, double duration)
        {
            OutAudioSegment outAudioSegment = new OutAudioSegment(audioData, duration, segment);
            this.NextWriter.WriteAsync(new Workflow<OutAudioSegment>(sessionId, outAudioSegment));
        }
    }
}
