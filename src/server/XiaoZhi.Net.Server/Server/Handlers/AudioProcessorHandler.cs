using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal class AudioProcessorHandler : BaseHandler, IInHandler<OutAudioSegment, OutAudioSegment, OutAudioSegment>, IOutHandler<MixedAudioPacket>
    {
        private readonly ObjectPool<OutAudioSegment> _outAudioSegmentPool;
        private readonly ObjectPool<Workflow<OutAudioSegment>> _outAudioSegmentWorkflowPool;
        private readonly ObjectPool<MixedAudioPacket> _mixedAudioPacketPool;
        private readonly ObjectPool<Workflow<MixedAudioPacket>> _mixedAudioPacketWorkflowPool;

        private IAudioProcessor? _audioProcessor;

        public AudioProcessorHandler(ObjectPool<OutAudioSegment> outAudioSegmentPool, ObjectPool<Workflow<OutAudioSegment>> outAudioSegmentWorkflowPool,
            ObjectPool<MixedAudioPacket> mixedAudioPacketPool, ObjectPool<Workflow<MixedAudioPacket>> mixedAudioPacketWorkflowPool,
            XiaoZhiConfig config, ILogger<AudioProcessorHandler> logger) : base(config, logger)
        {
            this._outAudioSegmentPool = outAudioSegmentPool;
            this._outAudioSegmentWorkflowPool = outAudioSegmentWorkflowPool;
            this._mixedAudioPacketPool = mixedAudioPacketPool;
            this._mixedAudioPacketWorkflowPool = mixedAudioPacketWorkflowPool;
        }

        public override string HandlerName => nameof(AudioProcessorHandler);
        public ChannelReader<Workflow<OutAudioSegment>> PreviousReader { get; set; } = null!;
        public ChannelReader<Workflow<OutAudioSegment>> PreviousReader2 { get; set; } = null!;
        public ChannelReader<Workflow<OutAudioSegment>> PreviousReader3 { get; set; } = null!;
        public ChannelWriter<Workflow<MixedAudioPacket>> NextWriter { get; set; } = null!;

        public override bool Build(PrivateProvider privateProvider)
        {
            Session session = this.SendOutter.GetSession();
            if (privateProvider.AudioProcessor is null)
            {
                this.Logger.LogError(Lang.AudioProcessorHandler_Build_AudioProcessorNotConfigured, session.DeviceId);
                return false;
            }
            this._audioProcessor = privateProvider.AudioProcessor;
            this._audioProcessor.RegisterDevice(session.DeviceId, session.SessionId);
            this._audioProcessor.OnMixedAudioDataAvailable += this.OnMixedAudioDataAvailable;
            this.RegisterCancellationToken();
            this.Builded = true;
            return true;
        }

        protected override void OnHandlerTokenChanged()
        {
            this._audioProcessor?.ClearAllBuffers();
        }

        public async Task Handle()
        {
            await foreach (var workflow in this.PreviousReader.ReadAllAsync())
            {
                try
                {
                    this.Handle(workflow);
                }
                finally
                {
                    this._outAudioSegmentPool.Return(workflow.Data);
                    this._outAudioSegmentWorkflowPool.Return(workflow);
                }
            }
        }
        public async Task Handle2()
        {
            await foreach (var workflow in this.PreviousReader2.ReadAllAsync())
            {
                try
                {
                    this.Handle(workflow);
                }
                finally
                {
                    this._outAudioSegmentPool.Return(workflow.Data);
                    this._outAudioSegmentWorkflowPool.Return(workflow);
                }
            }
        }
        public async Task Handle3()
        {
            await foreach (var workflow in this.PreviousReader3.ReadAllAsync())
            {
                try
                {
                    this.Handle(workflow);
                }
                finally
                {
                    this._outAudioSegmentPool.Return(workflow.Data);
                    this._outAudioSegmentWorkflowPool.Return(workflow);
                }
            }
        }

        public void Handle(Workflow<OutAudioSegment> workflow)
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

            if (this._audioProcessor is null)
            {
                this.Logger.LogError(Lang.AudioProcessorHandler_Handle_AudioProcessorNotBuilt, session.DeviceId);
                return;
            }

            OutAudioSegment s = workflow.Data;
            try
            {
                this.HandlerToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(s.SentenceId))
                {
                    this._audioProcessor.RegisterSubtitle(s.SentenceId, s.AudioType, s.IsFirstFrame ? TtsStatus.SentenceStart : TtsStatus.SentenceEnd, s.Content, s.Emotion);
                }

                this._audioProcessor.ProcessAudio(s.AudioType, s.AudioData, s.Content, s.Emotion, s.IsFirstFrame, s.IsLastFrame, s.SentenceId);

                if (s.IsLastSegment)
                {
                    this._audioProcessor.CompleteStream(s.AudioType);
                    if (session.CloseAfterChat)
                    {
                        this._audioProcessor.CompleteStream(AudioType.Music);
                        this._audioProcessor.CompleteStream(AudioType.SystemNotification);
                        this._audioProcessor.CompleteStream(AudioType.Other);
                    }
                }

            }
            catch (OperationCanceledException)
            {
                this.Logger.LogDebug(Lang.AudioProcessorHandler_Handle_Cancelled, session.DeviceId);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.AudioProcessorHandler_Handle_ProcessFailed, session.DeviceId);
            }
        }

        private async void OnMixedAudioDataAvailable(float[] mixedPcmData, bool isFirst, bool isLast, string? sentenceId)
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
            
            var mixedAudioPacket = this._mixedAudioPacketPool.Get();
            var workflow = this._mixedAudioPacketWorkflowPool.Get();
            
            try
            {
                mixedAudioPacket.Initialize(mixedPcmData, isFirst, isLast, sentenceId);
                workflow.Initialize(session, mixedAudioPacket);
                await this.NextWriter.WriteAsync(workflow, this.HandlerToken);
            }
            catch (OperationCanceledException)
            {
                this._mixedAudioPacketPool.Return(mixedAudioPacket);
                this._mixedAudioPacketWorkflowPool.Return(workflow);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.AudioProcessorHandler_OnMixedAudioDataAvailable_WriteFailed, session.DeviceId);
                this._mixedAudioPacketPool.Return(mixedAudioPacket);
                this._mixedAudioPacketWorkflowPool.Return(workflow);
            }
        }


        public override void Dispose()
        {
            if (this._audioProcessor is not null)
            {
                Session session = this.SendOutter.GetSession();
                if (session is not null)
                {
                    this._audioProcessor.UnregisterDevice(session.DeviceId, session.SessionId);
                }
                this._audioProcessor.Dispose();
            }
            
            this.NextWriter.Complete();
            base.Dispose();
        }
    }
}