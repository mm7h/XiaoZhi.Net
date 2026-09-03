using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.I18n;
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
                this.Logger.LogError(Lang.Text2AudioHandler_Build_TtsNotConfigured, session.DeviceId);
                return false;
            }

            if (privateProvider.AudioPlayerClient is null)
            {
                this.Logger.LogError(Lang.Text2AudioHandler_Build_PlayerNotConfigured, session.DeviceId);
                return false;
            }

            this._tts = privateProvider.Tts;
            this._tts.RegisterDevice(session.DeviceId, session.SessionId, this);

            this._audioPlayerClient = privateProvider.AudioPlayerClient;
            this._audioPlayerClient.SystemNotification.OnAudioData += this.OnNotificationAudioDataAsync;
            this._audioPlayerClient.MusicPlayer.OnAudioData += this.OnMusicAudioDataAsync;
            this._audioPlayerClient.RegisterDevice(session.DeviceId, session.SessionId);
            this.RegisterCancellationToken();
            this.Builded = true;
            return true;
        }

        protected override async void OnHandlerTokenChanged()
        {
            if (this._audioPlayerClient is not null)
            {
                await this._audioPlayerClient.SystemNotification.StopAsync();
            }
        }

        public override string HandlerName => nameof(Text2AudioHandler);
        public ChannelReader<Workflow<OutSegment>> PreviousReader { get; set; } = null!;
        public ChannelWriter<Workflow<OutAudioSegment>> NextWriter { get; set; } = null!;
        public ChannelWriter<Workflow<OutAudioSegment>> NextWriter2 { get; set; } = null!;
        public ChannelWriter<Workflow<OutAudioSegment>> NextWriter3 { get; set; } = null!;

        public async Task HandleAsync()
        {
            await foreach (var workflow in this.PreviousReader.ReadAllAsync())
            {
                try
                {
                    await this.HandleAsync(workflow);
                }
                finally
                {
                    this._outSegmentPool.Return(workflow.Data);
                    this._outSegmentWorkflowPool.Return(workflow);
                }
            }
        }

        public async Task HandleAsync(Workflow<OutSegment> workflow)
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
                this.Logger.LogError(Lang.Text2AudioHandler_Handle_TtsNotConfigured, session.DeviceId);
                return;
            }

            if (!session.IsDeviceBinded)
            {
                session.PrivateProvider.AudioProcessor?.ClearAllBuffers();
                await this.CheckBindDeviceAsync(session);
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(workflow.Data.Content))
                {
                    this.Logger.LogInformation(Lang.Text2AudioHandler_Handle_NoTtsRequired);
                    return;
                }
                this.HandlerToken.ThrowIfCancellationRequested();
                await this._tts.SynthesisAsync(workflow, this.HandlerToken);
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogDebug(Lang.Text2AudioHandler_Handle_Cancelled, session.DeviceId);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.Text2AudioHandler_Handle_ProcessFailed, session.DeviceId);
            }
        }

        private async Task CheckBindDeviceAsync(Session session)
        {
            if (this._audioPlayerClient is null)
            {
                this.Logger.LogError(Lang.Text2AudioHandler_CheckBindDevice_PlayerNotBuilt, session.DeviceId);
                return;
            }

            if (!string.IsNullOrWhiteSpace(session.BindCode) && session.BindCode.Length == 6)
            {
                if (session.BindCode.Length != 6)
                {
                    this.Logger.LogError(Lang.Text2AudioHandler_CheckBindDevice_InvalidBindCode, session.BindCode, session.DeviceId);
                    string bindErrorMsg = Lang.Text2AudioHandler_CheckBindDevice_BindCodeFormatError;
                    await session.SendOutter.SendSttMessageAsync(bindErrorMsg);
                    return;
                }

                string text = string.Format(Lang.Text2AudioHandler_CheckBindDevice_BindDevicePrompt, session.BindCode);
                await session.SendOutter.SendSttMessageAsync(text);

                await this._audioPlayerClient.SystemNotification.PlayBindCodeAsync(session.BindCode);
            }
            else
            {
                this.Logger.LogError(Lang.Text2AudioHandler_CheckBindDevice_InvalidBindCode, session.BindCode, session.DeviceId);
                string text = Lang.Text2AudioHandler_CheckBindDevice_VersionNotFound;
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

            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
            Workflow<OutAudioSegment> workflow = this._outAudioSegmentWorkflowPool.Get();

            outAudioSegment.Initialize(pcmData, AudioType.SystemNotification, isFirstFrame: isFirst, isLastFrame: isLast, isLastSegment: isLast);
            workflow.Initialize(session, outAudioSegment);

            try
            {
                await this.NextWriter3.WriteAsync(workflow, this.HandlerToken);
            }
            catch (OperationCanceledException)
            {
                this._outAudioSegmentPool.Return(outAudioSegment);
                this._outAudioSegmentWorkflowPool.Return(workflow);
            }
        }

        private async void OnMusicAudioDataAsync(float[] pcmData, bool isFirst, bool isLast)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }

            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
            Workflow<OutAudioSegment> workflow = this._outAudioSegmentWorkflowPool.Get();

            outAudioSegment.Initialize(pcmData, AudioType.Music, isFirstFrame: isFirst, isLastFrame: isLast, isLastSegment: isLast);
            workflow.Initialize(session, outAudioSegment);

            try
            {
                await this.NextWriter2.WriteAsync(workflow, this.HandlerToken);
            }
            catch (OperationCanceledException)
            {
                this._outAudioSegmentPool.Return(outAudioSegment);
                this._outAudioSegmentWorkflowPool.Return(workflow);
            }
        }

        #region ITtsEventCallback
        public async void OnBeforeProcessing(string sentence, bool isFirstSegment, bool isLastSegment)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            if (isFirstSegment)
            {
                OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
                Workflow<OutAudioSegment> nextWorkflow = this._outAudioSegmentWorkflowPool.Get();
                outAudioSegment.Initialize(audioType: AudioType.TTS, content: sentence, isFirstSegment: isFirstSegment, isLastSegment: isLastSegment);

                nextWorkflow.Initialize(session, outAudioSegment);
                await this.NextWriter.WriteAsync(nextWorkflow, this.HandlerToken);
            }
            this.Logger.LogDebug(Lang.Text2AudioHandler_OnBeforeProcessing_Started, session.DeviceId, sentence);
        }

        public async void OnProcessing(float[] audioData, bool isFirstFrame, bool isLastFrame)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
            Workflow<OutAudioSegment> nextWorkflow = this._outAudioSegmentWorkflowPool.Get();

            try
            {
                outAudioSegment.Initialize(audioType: AudioType.TTS, audioData: audioData, isFirstFrame: isFirstFrame, isLastFrame: isLastFrame);

                nextWorkflow.Initialize(session, outAudioSegment);
                await this.NextWriter.WriteAsync(nextWorkflow, this.HandlerToken);
            }
            catch (OperationCanceledException)
            {
                this._outAudioSegmentPool.Return(outAudioSegment);
                this._outAudioSegmentWorkflowPool.Return(nextWorkflow);
            }
        }

        public async void OnProcessed(string sentence, bool isFirstSegment, bool isLastSegment, TtsGenerateResult ttsGenerateResult)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            if (isLastSegment)
            {
                OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
                Workflow<OutAudioSegment> nextWorkflow = this._outAudioSegmentWorkflowPool.Get();
                try
                {
                    outAudioSegment.Initialize(audioType: AudioType.TTS, content: sentence, isFirstSegment: isFirstSegment, isLastSegment: isLastSegment);
                    nextWorkflow.Initialize(session, outAudioSegment);

                    await this.NextWriter.WriteAsync(nextWorkflow, this.HandlerToken);
                }
                catch (OperationCanceledException)
                {
                    this._outAudioSegmentPool.Return(outAudioSegment);
                    this._outAudioSegmentWorkflowPool.Return(nextWorkflow);
                }
            }
            this.Logger.LogDebug(Lang.Text2AudioHandler_OnProcessed_Completed, session.DeviceId);
        }

        public async void OnSentenceStart(string sentence, Emotion emotion, string sentenceId)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
            Workflow<OutAudioSegment> nextWorkflow = this._outAudioSegmentWorkflowPool.Get();
            try
            {
                outAudioSegment.Initialize(audioType: AudioType.TTS, content: sentence, isFirstFrame: true, emotion: emotion, sentenceId: "S_" + sentenceId);

                nextWorkflow.Initialize(session, outAudioSegment);
                await this.NextWriter.WriteAsync(nextWorkflow, this.HandlerToken);
            }
            catch (OperationCanceledException)
            {
                this._outAudioSegmentPool.Return(outAudioSegment);
                this._outAudioSegmentWorkflowPool.Return(nextWorkflow);
            }
        }

        public async void OnSentenceEnd(string sentence, Emotion emotion, string sentenceId)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            OutAudioSegment outAudioSegment = this._outAudioSegmentPool.Get();
            Workflow<OutAudioSegment> nextWorkflow = this._outAudioSegmentWorkflowPool.Get();
            try
            {

                outAudioSegment.Initialize(audioType: AudioType.TTS, content: sentence, isLastFrame: true, emotion: emotion, sentenceId: "E_" + sentenceId);

                nextWorkflow.Initialize(session, outAudioSegment);
                await this.NextWriter.WriteAsync(nextWorkflow, this.HandlerToken);
            }
            catch (OperationCanceledException)
            {
                this._outAudioSegmentPool.Return(outAudioSegment);
                this._outAudioSegmentWorkflowPool.Return(nextWorkflow);
            }
        }
        #endregion

        public override void Dispose()
        {
            if (this._tts is not null)
            {
                Session session = this.SendOutter.GetSession();
                if (session is not null)
                {
                    this._tts.UnregisterDevice(session.DeviceId, session.SessionId);
                }
                if (!this._tts.IsSherpaModel)
                {
                    this._tts.Dispose();
                }
            }
            if (this._audioPlayerClient is not null)
            {
                Session session = this.SendOutter.GetSession();
                if (session is not null)
                {
                    this._audioPlayerClient.UnregisterDevice(session.DeviceId, session.SessionId);
                }
                this._audioPlayerClient.SystemNotification.OnAudioData -= this.OnNotificationAudioDataAsync;
                this._audioPlayerClient.MusicPlayer.OnAudioData -= this.OnMusicAudioDataAsync;
                this._audioPlayerClient.Dispose();
            }
            this.NextWriter.Complete();
            this.NextWriter2.Complete();
            this.NextWriter3.Complete();
            base.Dispose();
        }
    }
}
