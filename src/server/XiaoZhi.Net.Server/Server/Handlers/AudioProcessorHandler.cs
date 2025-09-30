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

            OutAudioSegment outAudioSegment = workflow.Data;
            bool isContentNotEmpty = !string.IsNullOrEmpty(outAudioSegment.Content);

            try
            {
                if (isContentNotEmpty)
                {
                    // 注册字幕并提供该片段的样本数（按单声道样本数估算）。
                    // 这里假设输入数据已为目标通道数，若为多通道，字幕跟踪器会在 mixer 侧以实际消耗的单声道样本数推进。
                    int segmentMonoSamples = outAudioSegment.AudioData.Length;
                    session.PrivateProvider.AudioProcessor.RegisterSubtitle(outAudioSegment.AudioType, outAudioSegment.Content, segmentMonoSamples, outAudioSegment.IsFirstSegment, outAudioSegment.IsLastSegment);
                }

                // Directly feed the whole segment to the mixer; let mixer handle framing/clocking
                session.PrivateProvider.AudioProcessor!.AddAudioData(outAudioSegment.AudioType, outAudioSegment.AudioData);

                if (outAudioSegment.IsLastSegment)
                {
                    session.PrivateProvider.AudioProcessor!.StopAudioStream(outAudioSegment.AudioType);

                    if (session.CloseAfterChat)
                    {
                        session.PrivateProvider.AudioProcessor!.StopAudioStream(AudioType.Music);
                        session.PrivateProvider.AudioProcessor!.StopAudioStream(AudioType.SystemNotification);
                        session.PrivateProvider.AudioProcessor!.StopAudioStream(AudioType.Other);
                    }
                }
                if (isContentNotEmpty)
                {
                    //await this.SendOutter.SendTtsMessageAsync(TtsStatus.SentenceEnd, outAudioSegment.Content);
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

        public override void Dispose()
        {
            this.NextWriter.Complete();
        }
    }
}