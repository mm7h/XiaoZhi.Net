using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class Text2AudioHandler : BaseHandler, IInHandler<OutSegment>, IOutHandler<OutAudioSegment>
    {
        private readonly ITts _tts;
        private readonly ObjectPool<OutAudioSegment> _outAudioSegmentPool;
        private readonly ObjectPool<Workflow<OutAudioSegment>> _workflowPool;
        private readonly ObjectPool<Workflow<OutSegment>> _outSegmentWorkflowPool;
        private bool _privateTTSInitialized = false;
        private bool _privateAudioPlayerInitialized = false;
        private bool _privateAudioMixerInitialized = false;

        private IAudioPlayerClient? _audioPlayerClient;

        public Text2AudioHandler([FromKeyedServices(GlobalProviderNames.GLOBAL_TTS)] ITts tts,
            ObjectPool<OutAudioSegment> outAudioSegmentPool,
            ObjectPool<Workflow<OutAudioSegment>> workflowPool,
            ObjectPool<Workflow<OutSegment>> outSegmentWorkflowPool,
            XiaoZhiConfig config,
            ILogger<Text2AudioHandler> logger) : base(config, logger)
        {
            this._tts = tts;
            this._outAudioSegmentPool = outAudioSegmentPool;
            this._workflowPool = workflowPool;
            this._outSegmentWorkflowPool = outSegmentWorkflowPool;
            this._tts.OnBeforeProcessing += this.TTS_OnBeforeProcessing;
            this._tts.OnProcessed += this.TTS_OnProcessed;
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
                this._outSegmentWorkflowPool.Return(workflow);
                return;
            }

            if (!session.IsDeviceBinded)
            {
                await this.CheckBindDevice(session);
                this._outSegmentWorkflowPool.Return(workflow);
                return;
            }

            try
            {
                if (string.IsNullOrEmpty(workflow.Data.Content))
                {
                    this.Logger.LogInformation("No tts required, the query text is empty.");
                    return;
                }

                this.CheckInitialize(session);

                if (_privateTTSInitialized)
                {
                    await session.PrivateProvider.Tts!.SynthesisAsync(workflow, session, session.SessionCtsToken);
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
            finally
            {
                this._outSegmentWorkflowPool.Return(workflow);
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

        private void CheckInitialize(Session session)
        {
            if (!this._privateAudioPlayerInitialized && session.PrivateProvider.AudioPlayerClient is not null)
            {
                this._audioPlayerClient = session.PrivateProvider.AudioPlayerClient;
                this._audioPlayerClient.SystemNotification.OnAudioData += this.OnNotificationAudioDataAsync;
                this._audioPlayerClient.MusicPlayer.OnAudioData += this.OnMusicAudioDataAsync;
            }

            if (!this._privateAudioMixerInitialized && session.PrivateProvider.AudioMixer is not null)
            {
                session.PrivateProvider.AudioMixer.OnMixedAudioDataAvailable += this.OnMixedAudioDataAvailable;
                this._privateAudioMixerInitialized = true;
            }

            if (!this._privateTTSInitialized && session.PrivateProvider.Tts is not null)
            {
                session.PrivateProvider.Tts.OnBeforeProcessing += this.TTS_OnBeforeProcessing;
                session.PrivateProvider.Tts.OnProcessed += this.TTS_OnProcessed;
                this._privateTTSInitialized = true;
            }
        }

        private async void OnMixedAudioDataAvailable(float[] mixedPcmData, bool isFirst, bool isLast, Dictionary<AudioType, string?> contentMap)
        {
            var outAudioSegment = this._outAudioSegmentPool.Get();
            var workflow = this._workflowPool.Get();

            try
            {
                outAudioSegment.Initialize(mixedPcmData, isFirst, isLast, contentMap);
                workflow.Initialize(this.SendOutter.SessionId, outAudioSegment);

                await this.NextWriter.WriteAsync(workflow);
            }
            finally
            {
                this._outAudioSegmentPool.Return(outAudioSegment);
                this._workflowPool.Return(workflow);
            }
        }

        private void OnNotificationAudioDataAsync(float[] pcmData, bool isFirst, bool isLast)
        {
            Session session = this.SendOutter.GetSession();
            session.PrivateProvider.AudioMixer.AddAudioData(AudioType.SystemNotification, pcmData, isFirst, isLast, null);
        }

        private void OnMusicAudioDataAsync(float[] pcmData, bool isFirst, bool isLast)
        {
            Session session = this.SendOutter.GetSession();
            session.PrivateProvider.AudioMixer.AddAudioData(AudioType.Music, pcmData, isFirst, isLast, null);
        }

        private void TTS_OnBeforeProcessing(string sessionId, OutSegment segment)
        {
            // 避免使用全局的单例 TTS provider时，事件影响其他会话
            if (sessionId != this.SendOutter.SessionId)
                return;
            if (segment.IsFirst)
            {
                this.Logger.LogInformation("Send the first audio from segment: {content}", segment.Content);
            }
        }

        private async void TTS_OnProcessed(string sessionId, float[] audioData, OutSegment segment, double duration)
        {
            if (sessionId != this.SendOutter.SessionId)
                return;

            Session session = this.SendOutter.GetSession();
            if (session.PrivateProvider.AudioResampler is not null)
            {
                (float[] resampledAudioData, _) = await session.PrivateProvider.AudioResampler.ResampleAsync(audioData, session.SessionCtsToken);
                session.PrivateProvider.AudioMixer.AddAudioData(AudioType.TTS, resampledAudioData, segment.IsFirst, segment.IsLast, segment.Content);
            }
            else
            {
                session.PrivateProvider.AudioMixer.AddAudioData(AudioType.TTS, audioData, segment.IsFirst, segment.IsLast, segment.Content);
            }
        }

        public void Dispose()
        {
            this._tts.OnBeforeProcessing -= this.TTS_OnBeforeProcessing;
            this._tts.OnProcessed -= this.TTS_OnProcessed;

            Session session = this.SendOutter.GetSession();
            if (session.PrivateProvider.Tts is not null && this._privateTTSInitialized)
            {
                session.PrivateProvider.Tts.OnBeforeProcessing -= this.TTS_OnBeforeProcessing;
                session.PrivateProvider.Tts.OnProcessed -= this.TTS_OnProcessed;
                this._privateTTSInitialized = false;
            }
            if (this._audioPlayerClient is not null)
            {
                this._audioPlayerClient.SystemNotification.OnAudioData -= this.OnNotificationAudioDataAsync;
                this._audioPlayerClient.MusicPlayer.OnAudioData -= this.OnMusicAudioDataAsync;
                this._privateAudioPlayerInitialized = false;
            }
            if (session.PrivateProvider.AudioMixer is not null && this._privateAudioMixerInitialized)
            {
                session.PrivateProvider.AudioMixer.OnMixedAudioDataAvailable -= this.OnMixedAudioDataAvailable;
                this._privateAudioMixerInitialized = false;
            }
            this.NextWriter.Complete();
        }
    }
}
