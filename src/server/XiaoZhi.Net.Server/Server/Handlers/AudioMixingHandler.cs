using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class AudioMixingHandler : BaseHandler, IInHandler<OutAudioSegment, OutAudioSegment, OutAudioSegment>, IOutHandler<MixedAudioPacket>
    {
        private readonly ObjectPool<OutAudioSegment> _outAudioSegmentPool;
        private readonly ObjectPool<Workflow<OutAudioSegment>> _outAudioSegmentWorkflowPool;
        private readonly ObjectPool<MixedAudioPacket> _mixedAudioPacketPool;
        private readonly ObjectPool<Workflow<MixedAudioPacket>> _mixedAudioPacketWorkflowPool;

        private bool _privateAudioMixerInitialized = false;

        public AudioMixingHandler(ObjectPool<OutAudioSegment> outAudioSegmentPool, ObjectPool<Workflow<OutAudioSegment>> outAudioSegmentWorkflowPool,
            ObjectPool<MixedAudioPacket> mixedAudioPacketPool, ObjectPool<Workflow<MixedAudioPacket>> mixedAudioPacketWorkflowPool,
            XiaoZhiConfig config, ILogger<AudioMixingHandler> logger) : base(config, logger)
        {
            this._outAudioSegmentPool = outAudioSegmentPool;
            this._outAudioSegmentWorkflowPool = outAudioSegmentWorkflowPool;
            this._mixedAudioPacketPool = mixedAudioPacketPool;
            this._mixedAudioPacketWorkflowPool = mixedAudioPacketWorkflowPool;
        }

        public override string HandlerName => nameof(AudioMixingHandler);
        public IBizSendOutter SendOutter { get; set; } = null!;
        public ChannelReader<Workflow<OutAudioSegment>> PreviousReader { get; set; } = null!;
        public ChannelReader<Workflow<OutAudioSegment>> PreviousReader2 { get; set; } = null!;
        public ChannelReader<Workflow<OutAudioSegment>> PreviousReader3 { get; set; } = null!;
        public ChannelWriter<Workflow<MixedAudioPacket>> NextWriter { get; set; } = null!;

        public async Task Handle()
        {
            //tts
            await foreach (var workflow in this.PreviousReader.ReadAllAsync())
            {
                try
                {
                    await this.Handle(workflow);
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
            //music
            await foreach (var workflow in this.PreviousReader2.ReadAllAsync())
            {
                try
                {
                    await this.Handle(workflow);
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
            // notification
            await foreach (var workflow in this.PreviousReader3.ReadAllAsync())
            {
                try
                {
                    await this.Handle(workflow);
                }
                finally
                {
                    this._outAudioSegmentPool.Return(workflow.Data);
                    this._outAudioSegmentWorkflowPool.Return(workflow);
                }
            }
        }
        public async Task Handle(Workflow<OutAudioSegment> workflow)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            if (!this._privateAudioMixerInitialized && session.PrivateProvider.AudioMixer is not null)
            {
                session.PrivateProvider.AudioMixer.OnMixedAudioDataAvailable += this.OnMixedAudioDataAvailable;
                this._privateAudioMixerInitialized = true;
            }

            OutAudioSegment outAudioSegment = workflow.Data;
            bool isContentNotEmpty = !string.IsNullOrEmpty(outAudioSegment.Content);

            try
            {
                if (isContentNotEmpty)
                {
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.SentenceStart, outAudioSegment.Content);
                }

                // Directly feed the whole segment to the mixer; let mixer handle framing/clocking
                session.PrivateProvider.AudioMixer!.AddAudioData(outAudioSegment.AudioType, outAudioSegment.AudioData);

                if (outAudioSegment.IsLastSegment)
                {
                    session.PrivateProvider.AudioMixer!.StopAudioStream(outAudioSegment.AudioType);

                    if (session.CloseAfterChat)
                    {
                        session.PrivateProvider.AudioMixer!.StopAudioStream(AudioType.Music);
                        session.PrivateProvider.AudioMixer!.StopAudioStream(AudioType.SystemNotification);
                        session.PrivateProvider.AudioMixer!.StopAudioStream(AudioType.Other);
                    }
                }
                if (isContentNotEmpty)
                {
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.SentenceEnd, outAudioSegment.Content);
                }
            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "audio mixing");
            }
        }

        private void OnMixedAudioDataAvailable(float[] mixedPcmData, bool isFirst, bool isLast)
        {
            var mixedAudioPacket = this._mixedAudioPacketPool.Get();
            var workflow = this._mixedAudioPacketWorkflowPool.Get();

            mixedAudioPacket.Initialize(mixedPcmData, isFirst, isLast);
            workflow.Initialize(this.SendOutter.GetSession(), mixedAudioPacket);

            _ = this.NextWriter.WriteAsync(workflow);
        }

        public void Dispose()
        {
            this.NextWriter.Complete();
        }
    }
}