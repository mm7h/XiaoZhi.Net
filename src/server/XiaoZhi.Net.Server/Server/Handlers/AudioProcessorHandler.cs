using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class AudioProcessorHandler : BaseHandler, IInHandler<OutAudioSegment, OutAudioSegment, OutAudioSegment>, IOutHandler<MixedAudioPacket>
    {
        private readonly ObjectPool<OutAudioSegment> _outAudioSegmentPool;
        private readonly ObjectPool<Workflow<OutAudioSegment>> _outAudioSegmentWorkflowPool;
        private readonly ObjectPool<MixedAudioPacket> _mixedAudioPacketPool;
        private readonly ObjectPool<Workflow<MixedAudioPacket>> _mixedAudioPacketWorkflowPool;


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
            if (privateProvider.AudioProcessor is not null)
            {
                privateProvider.AudioProcessor.OnMixedAudioDataAvailable += this.OnMixedAudioDataAvailable;
                return true;
            }
            else
            {
                this.Logger.LogError("Audio processor is not built for device {deviceId}.", session.DeviceId);
                return false;
            }
        }
        public async Task Handle()
        {
            //tts
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
            //music
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
            // notification
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

            if (session.PrivateProvider.AudioProcessor is null)
            {
                this.Logger.LogError("Audio processor is not built for device {deviceId}.", session.DeviceId);
                return;
            }

            OutAudioSegment outAudioSegment = workflow.Data;

            try
            {
                session.PrivateProvider.AudioProcessor.ProcessAudio(outAudioSegment.AudioType, outAudioSegment.AudioData, outAudioSegment.Content, outAudioSegment.IsFirstSegment, outAudioSegment.IsLastSegment, outAudioSegment.AudioData.Length);

                if (outAudioSegment.IsLastSegment)
                {
                    session.PrivateProvider.AudioProcessor.CompleteStream(outAudioSegment.AudioType);

                    if (session.CloseAfterChat)
                    {
                        session.PrivateProvider.AudioProcessor.CompleteStream(AudioType.Music);
                        session.PrivateProvider.AudioProcessor.CompleteStream(AudioType.SystemNotification);
                        session.PrivateProvider.AudioProcessor.CompleteStream(AudioType.Other);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                session.PrivateProvider.AudioProcessor.ClearAllBuffers();
                this.FireAbort(session.DeviceId, session.SessionId, "audio process");
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

        public override void Dispose()
        {
            this.NextWriter.Complete();
        }
    }
}