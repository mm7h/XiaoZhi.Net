using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Providers;
using XiaoZhi.Net.Server.Providers.TTS;

namespace XiaoZhi.Net.Server.Handlers
{
    internal class Text2AudioHandler : BaseHandler, IInHandler<OutSegment>, IOutHandler<OutAudioSegment, OutAudioSegment, OutAudioSegment>, ITtsEventCallback
    {
        private readonly ObjectPool<OutSegment> _outSegmentPool;
        private readonly ObjectPool<OutAudioSegment> _outAudioSegmentPool;
        private readonly ObjectPool<Workflow<OutAudioSegment>> _outAudioSegmentWorkflowPool;
        private readonly ObjectPool<Workflow<OutSegment>> _outSegmentWorkflowPool;

        private ITts? _tts;
        private IAudioPlayerClient? _audioPlayerClient;

        public Text2AudioHandler(ObjectPool<OutSegment> outSegmentPool,

            ObjectPool<OutAudioSegment> outAudioSegmentPool,
            ObjectPool<Workflow<OutAudioSegment>> outAudioSegmentWorkflowPool,
            ObjectPool<Workflow<OutSegment>> outSegmentWorkflowPool,
            XiaoZhiConfig config,
            ILogger<Text2AudioHandler> logger) : base(config, logger)
        {
            this._outSegmentPool = outSegmentPool;
            this._outAudioSegmentPool = outAudioSegmentPool;
            this._outAudioSegmentWorkflowPool = outAudioSegmentWorkflowPool;
            this._outSegmentWorkflowPool = outSegmentWorkflowPool;
        }

        public override bool Build(PrivateProvider privateProvider)
        {
            Session session = this.SendOutter.GetSession();
            if (privateProvider.Tts is null)
            {
                this.Logger.LogError("TTS provider is not configured for the device: {deviceId}.", session.DeviceId);
                return false;
            }

            if (privateProvider.AudioPlayerClient is null)
            {
                this.Logger.LogError("AudioPlayerClient is not configured for the device: {deviceId}.", session.DeviceId);
                return false;
            }

            this._tts = privateProvider.Tts;
            this._tts.RegisterDevice(session.DeviceId, session.SessionId, this);

            this._audioPlayerClient = privateProvider.AudioPlayerClient;
            this._audioPlayerClient.SystemNotification.OnAudioData += this.OnNotificationAudioDataAsync;
            this._audioPlayerClient.MusicPlayer.OnAudioData += this.OnMusicAudioDataAsync;
            this._audioPlayerClient.RegisterDevice(session.DeviceId, session.SessionId);
            this.RegisterCancellationToken();

            return true;
        }

        protected override async void OnHandlerTokenChanged()
        {
            if (this._audioPlayerClient is not null)
            {
                await this._audioPlayerClient.SystemNotification.StopAsync();
                await this._audioPlayerClient.MusicPlayer.StopAsync();
            }
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

            if (!this.CheckWorkflowValid(workflow))
            {
                return;
            }

            if (this._tts is null)
            {
                this.Logger.LogError("TTS provider is not configured for the device: {deviceId}.", session.DeviceId);
                return;
            }

            if (!session.IsDeviceBinded)
            {
                session.PrivateProvider.AudioProcessor?.ClearAllBuffers();
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
                this.HandlerToken.ThrowIfCancellationRequested();
                await this._tts.SynthesisAsync(workflow, this.HandlerToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                this.Logger.LogError(ex, "Error occurred during text to audio processing for device: {deviceId}.", session.DeviceId);
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
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }
            OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
            Workflow<OutAudioSegment> workflow = this._outAudioSegmentWorkflowPool.Get();

            outAudioSegment.Initialize(pcmData, AudioType.SystemNotification, isFirstFrame: isFirst, isLastFrame: isLast);

            Session session = this.SendOutter.GetSession();
            workflow.Initialize(session, outAudioSegment);

            await this.NextWriter3.WriteAsync(workflow);
        }

        private async void OnMusicAudioDataAsync(float[] pcmData, bool isFirst, bool isLast)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }
            OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
            Workflow<OutAudioSegment> workflow = this._outAudioSegmentWorkflowPool.Get();

            outAudioSegment.Initialize(pcmData, AudioType.Music, isFirstFrame: isFirst, isLastFrame: isLast);

            Session session = this.SendOutter.GetSession();
            workflow.Initialize(session, outAudioSegment);

            await this.NextWriter2.WriteAsync(workflow);
        }

        public override void Dispose()
        {
            Session session = this.SendOutter.GetSession();
            if (this._audioPlayerClient is not null)
            {
                this._audioPlayerClient.SystemNotification.OnAudioData -= this.OnNotificationAudioDataAsync;
                this._audioPlayerClient.MusicPlayer.OnAudioData -= this.OnMusicAudioDataAsync;
            }
            this.NextWriter.Complete();
            this.NextWriter2.Complete();
            this.NextWriter3.Complete();
            base.Dispose();
        }

        #region ITtsEventCallback
        public void OnBeforeProcessing(string sentence, bool isFirstSegment, bool isLastSegment)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }
            Session session = this.SendOutter.GetSession();
            if (isFirstSegment)
            {
                OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
                Workflow<OutAudioSegment> nextWorkflow = this._outAudioSegmentWorkflowPool.Get();
                outAudioSegment.Initialize(audioType: AudioType.TTS, content: sentence, isFirstSegment: isFirstSegment, isLastSegment: isLastSegment);

                nextWorkflow.Initialize(session, outAudioSegment);
                this.NextWriter.WriteAsync(nextWorkflow);
            }
            this.Logger.LogDebug("TTS processing started for device: {deviceId}, sentence: {sentence}.", session.DeviceId, sentence);
        }

        public async void OnProcessing(float[] audioData, bool isFirstFrame, bool isLastFrame)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }
            Session session = this.SendOutter.GetSession();
            OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
            Workflow<OutAudioSegment> nextWorkflow = this._outAudioSegmentWorkflowPool.Get();
            if (session.PrivateProvider.AudioResampler is not null && audioData.Length > 0)
            {
                (float[] resampledAudioData, _) = await session.PrivateProvider.AudioResampler.ResampleAsync(audioData, this.HandlerToken);
                outAudioSegment.Initialize(audioType: AudioType.TTS, audioData: resampledAudioData, isFirstFrame: isFirstFrame, isLastFrame: isLastFrame);
            }
            else
            {
                outAudioSegment.Initialize(audioType: AudioType.TTS, audioData: audioData, isFirstFrame: isFirstFrame, isLastFrame: isLastFrame);
            }


            nextWorkflow.Initialize(session, outAudioSegment);
            await this.NextWriter.WriteAsync(nextWorkflow);
        }

        public void OnProcessed(string sentence, bool isFirstSegment, bool isLastSegment, TtsGenerateResult ttsGenerateResult)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }
            Session session = this.SendOutter.GetSession();
            if (isLastSegment)
            {
                OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
                Workflow<OutAudioSegment> nextWorkflow = this._outAudioSegmentWorkflowPool.Get();
                outAudioSegment.Initialize(audioType: AudioType.TTS, content: sentence, isFirstSegment: isFirstSegment, isLastSegment: isLastSegment);

                nextWorkflow.Initialize(session, outAudioSegment);
                this.NextWriter.WriteAsync(nextWorkflow);
            }
            this.Logger.LogDebug("TTS processing completed for device: {deviceId}.", session.DeviceId);
        }

        public void OnSentenceStart(string sentence, Emotion emotion, string sentenceId)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }
            Session session = this.SendOutter.GetSession();
            OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
            Workflow<OutAudioSegment> nextWorkflow = this._outAudioSegmentWorkflowPool.Get();
            outAudioSegment.Initialize(audioType: AudioType.TTS, content: sentence, isFirstFrame: true, emotion: emotion, sentenceId: "S_" + sentenceId);

            nextWorkflow.Initialize(session, outAudioSegment);
            this.NextWriter.WriteAsync(nextWorkflow);
        }

        public void OnSentenceEnd(string sentence, Emotion emotion, string sentenceId)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }
            Session session = this.SendOutter.GetSession();
            OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
            Workflow<OutAudioSegment> nextWorkflow = this._outAudioSegmentWorkflowPool.Get();
            outAudioSegment.Initialize(audioType: AudioType.TTS, content: sentence, isLastFrame: true, emotion: emotion, sentenceId: "E_" + sentenceId);

            nextWorkflow.Initialize(session, outAudioSegment);
            this.NextWriter.WriteAsync(nextWorkflow);
        }
        #endregion
    }
}
