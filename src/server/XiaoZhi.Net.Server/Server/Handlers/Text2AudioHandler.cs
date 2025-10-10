using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class Text2AudioHandler : BaseHandler, IInHandler<OutSegment>, IOutHandler<OutAudioSegment, OutAudioSegment, OutAudioSegment>
    {
        private readonly ObjectPool<OutSegment> _outSegmentPool;
        private readonly ObjectPool<OutAudioSegment> _outAudioSegmentPool;
        private readonly ObjectPool<Workflow<OutAudioSegment>> _outAudioSegmentWorkflowPool;
        private readonly ObjectPool<Workflow<OutSegment>> _outSegmentWorkflowPool;

        private ITts _tts;

        private IAudioPlayerClient? _audioPlayerClient;

        public Text2AudioHandler([FromKeyedServices(GlobalProviderNames.GLOBAL_TTS)] ITts tts,
            ObjectPool<OutSegment> outSegmentPool,
            ObjectPool<OutAudioSegment> outAudioSegmentPool,
            ObjectPool<Workflow<OutAudioSegment>> outAudioSegmentWorkflowPool,
            ObjectPool<Workflow<OutSegment>> outSegmentWorkflowPool,
            XiaoZhiConfig config,
            ILogger<Text2AudioHandler> logger) : base(config, logger)
        {
            this._tts = tts;
            this._outSegmentPool = outSegmentPool;
            this._outAudioSegmentPool = outAudioSegmentPool;
            this._outAudioSegmentWorkflowPool = outAudioSegmentWorkflowPool;
            this._outSegmentWorkflowPool = outSegmentWorkflowPool;
        }

        public override bool Build(PrivateProvider privateProvider)
        {
            if (privateProvider.Tts is not null)
            {
                this._tts = privateProvider.Tts;

            }
            this._tts.OnBeforeProcessing += this.TTS_OnBeforeProcessing;
            this._tts.OnProcessed += this.TTS_OnProcessed;

            if (privateProvider.AudioPlayerClient is not null)
            {
                this._audioPlayerClient = privateProvider.AudioPlayerClient;
                this._audioPlayerClient.SystemNotification.OnAudioData += this.OnNotificationAudioDataAsync;
                this._audioPlayerClient.MusicPlayer.OnAudioData += this.OnMusicAudioDataAsync;
            }

            return true;
        }

        public override string HandlerName => nameof(Text2AudioHandler);
        public ChannelReader<Workflow<OutSegment>> PreviousReader { get; set; } = null!;
        public ChannelWriter<Workflow<OutAudioSegment>> NextWriter { get; set; } = null!;
        public ChannelWriter<Workflow<OutAudioSegment>> NextWriter2 { get; set; } = null!;
        public ChannelWriter<Workflow<OutAudioSegment>> NextWriter3 { get; set; } = null!;

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
                    this._outSegmentPool.Return(workflow.Data);
                    this._outSegmentWorkflowPool.Return(workflow);
                }
            }
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
                session.PrivateProvider.AudioProcessor.ClearAllBuffers();
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

                await this._tts.SynthesisAsync(workflow, session, session.SessionCtsToken);

            }
            catch (OperationCanceledException)
            {
                if (this._audioPlayerClient is not null)
                {
                    await this._audioPlayerClient.SystemNotification.StopAsync();
                    await this._audioPlayerClient.MusicPlayer.StopAsync();
                }
                this.FireAbort(session.DeviceId, session.SessionId, "text to audio");
            }
        }

        private async Task CheckBindDevice(Session session)
        {
            if (this._audioPlayerClient is null)
            {
                this.Logger.LogError("AudioPlayerClient is not built for the device {deviceId} yet", session.DeviceId);
                return;
            }

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

                await this._audioPlayerClient.SystemNotification.PlayBindCodeAsync(session.BindCode);
            }
            else
            {
                this.Logger.LogError("Invalid bind code {code} for the device: {deviceId}", session.BindCode, session.DeviceId);
                string text = "没有找到该设备的版本信息，请正确配置 OTA地址，然后重新编译固件。";
                await session.SendOutter.SendSttMessageAsync(text);

                await this._audioPlayerClient.SystemNotification.PlayNotFoundAsync();
            }
        }

        private async void OnNotificationAudioDataAsync(float[] pcmData, bool isFirst, bool isLast)
        {
            OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
            Workflow<OutAudioSegment> workflow = this._outAudioSegmentWorkflowPool.Get();

            outAudioSegment.Initialize(pcmData, AudioType.SystemNotification, string.Empty, isFirst, isLast);
            workflow.Initialize(this.SendOutter.SessionId, outAudioSegment);

            await this.NextWriter3.WriteAsync(workflow);
        }

        private async void OnMusicAudioDataAsync(float[] pcmData, bool isFirst, bool isLast)
        {
            OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
            Workflow<OutAudioSegment> workflow = this._outAudioSegmentWorkflowPool.Get();

            outAudioSegment.Initialize(pcmData, AudioType.Music, string.Empty, isFirst, isLast);
            workflow.Initialize(this.SendOutter.SessionId, outAudioSegment);

            await this.NextWriter2.WriteAsync(workflow);
        }

        private void TTS_OnBeforeProcessing(string sessionId, OutSegment segment)
        {
            // 避免使用全局的单例 TTS provider时，事件影响其他会话
            if (sessionId != this.SendOutter.SessionId)
                return;
            if (segment.IsFirstSegment)
            {
                this.Logger.LogInformation("Send the first audio from segment: {content}", segment.Content);
            }
        }

        private async void TTS_OnProcessed(string sessionId, float[] audioData, OutSegment segment, double duration)
        {
            if (sessionId != this.SendOutter.SessionId)
                return;

            Session session = this.SendOutter.GetSession();

            OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
            Workflow<OutAudioSegment> workflow = this._outAudioSegmentWorkflowPool.Get();

            if (session.PrivateProvider.AudioResampler is not null)
            {
                (float[] resampledAudioData, _) = await session.PrivateProvider.AudioResampler.ResampleAsync(audioData, session.SessionCtsToken);
                outAudioSegment.Initialize(resampledAudioData, AudioType.TTS, segment.Content, segment.IsFirstSegment, segment.IsLastSegment);
            }
            else
            {
                outAudioSegment.Initialize(audioData, AudioType.TTS, segment.Content, segment.IsFirstSegment, segment.IsLastSegment);
            }
            workflow.Initialize(this.SendOutter.SessionId, outAudioSegment);

            await this.NextWriter.WriteAsync(workflow);
        }

        public override void Dispose()
        {
            Session session = this.SendOutter.GetSession();
            this._tts.OnBeforeProcessing -= this.TTS_OnBeforeProcessing;
            this._tts.OnProcessed -= this.TTS_OnProcessed;
            if (this._audioPlayerClient is not null)
            {
                this._audioPlayerClient.SystemNotification.OnAudioData -= this.OnNotificationAudioDataAsync;
                this._audioPlayerClient.MusicPlayer.OnAudioData -= this.OnMusicAudioDataAsync;
            }
            this.NextWriter.Complete();
        }
    }
}
