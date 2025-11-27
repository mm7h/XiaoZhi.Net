using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;

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
                privateProvider.AudioProcessor.RegisterDevice(session.DeviceId, session.SessionId);
                privateProvider.AudioProcessor.OnMixedAudioDataAvailable += this.OnMixedAudioDataAvailable;
                return true;
            }
            this.Logger.LogError("Audio processor is not built for device {deviceId}.", session.DeviceId);
            return false;
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

            if (session.PrivateProvider.AudioProcessor is null)
            {
                this.Logger.LogError("Audio processor is not built for device {deviceId}.", session.DeviceId);
                return;
            }

            OutAudioSegment s = workflow.Data;
            try
            {
                session.PrivateProvider.AudioProcessor.ProcessAudio(s.AudioType, s.AudioData, s.Content, s.IsFirstFrame, s.IsLastFrame);

                if (s.IsLastSegment)
                {
                    session.PrivateProvider.AudioProcessor.CompleteStream(s.AudioType);
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
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to process the audio segment from device: {deviceId}.", session.DeviceId);
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