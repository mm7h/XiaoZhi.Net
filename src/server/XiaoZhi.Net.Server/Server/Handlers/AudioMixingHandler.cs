using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using SherpaOnnx;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class AudioMixingHandler : BaseHandler, IInHandler<OutAudioSegment, OutAudioSegment, OutAudioSegment>, IOutHandler<MixedAudioPacket>
    {
        private readonly ObjectPool<OutAudioSegment> _outAudioSegmentPool;
        private readonly ObjectPool<Workflow<OutAudioSegment>> _outAudioSegmentWorkflowPool;
        private readonly ObjectPool<MixedAudioPacket> _mixedAudioPacketPool;
        private readonly ObjectPool<Workflow<MixedAudioPacket>> _mixedAudioPacketWorkflowPool;

        private readonly ConcurrentDictionary<AudioType, CircularBuffer> _perTypeBuffers = new();

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
            int frameSize = session.PrivateProvider.AudioEncoder!.FrameSize;
            int frameDuration = session.AudioSetting.FrameDuration;

            bool isContentNotEmpty = !string.IsNullOrEmpty(workflow.Data.Content);

            float[] chunk = ArrayPool<float>.Shared.Rent(frameSize);

            OutAudioSegment outAudioSegment = workflow.Data;
            try
            {
                if (isContentNotEmpty)
                {
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.SentenceStart, outAudioSegment.Content);
                }

                var buffer = _perTypeBuffers.GetOrAdd(outAudioSegment.AudioType, _ => new CircularBuffer(960 * 100));
                buffer.Push(outAudioSegment.AudioData);
                while (buffer.GetFrames(frameSize, chunk))
                {
                    session.SessionCtsToken.ThrowIfCancellationRequested();

                    await Task.Delay(frameDuration, session.SessionCtsToken);
                    session.PrivateProvider.AudioMixer!.AddAudioData(outAudioSegment.AudioType, chunk);
                }
                if (outAudioSegment.IsLastSegment)
                {
                    session.PrivateProvider.AudioMixer!.StopAudioStream(outAudioSegment.AudioType);

                    // 清理该音频类型的缓冲，避免跨流残留
                    if (_perTypeBuffers.TryRemove(outAudioSegment.AudioType, out var buf))
                    {
                        buf.Reset();
                    }

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
            finally
            {
                ArrayPool<float>.Shared.Return(chunk);
            }
        }

        private void OnMixedAudioDataAvailable(float[] mixedPcmData, bool isFirst, bool isLast)
        {
            MixedAudioPacket mixedAudioPacket = this._mixedAudioPacketPool.Get();
            Workflow<MixedAudioPacket> workflow = this._mixedAudioPacketWorkflowPool.Get();
            mixedAudioPacket.Initialize(mixedPcmData, isFirst, isLast);
            workflow.Initialize(this.SendOutter.GetSession(), mixedAudioPacket);

            this.NextWriter.WriteAsync(workflow);
        }

        public void Dispose()
        {
            this.NextWriter.Complete();
        }
    }
}