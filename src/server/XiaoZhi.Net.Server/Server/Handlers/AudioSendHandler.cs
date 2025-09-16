using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using SherpaOnnx;
using System;
using System.Buffers;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class AudioSendHandler : BaseHandler, IInHandler<OutAudioSegment>
    {
        private readonly ObjectPool<OutAudioSegment> _outAudioSegmentPool;
        private readonly ObjectPool<Workflow<OutAudioSegment>> _outAudioSegmentWorkflowPool;
        private readonly CircularBuffer _sendOpusPacketFrame;
        private bool _privateAudioMixerInitialized = false;

        public AudioSendHandler(ObjectPool<OutAudioSegment> outAudioSegmentPool, ObjectPool<Workflow<OutAudioSegment>> outAudioSegmentWorkflowPool,
            XiaoZhiConfig config, ILogger<AudioSendHandler> logger) : base(config, logger)
        {
            this._outAudioSegmentPool = outAudioSegmentPool;
            this._outAudioSegmentWorkflowPool = outAudioSegmentWorkflowPool;
            this._sendOpusPacketFrame = new CircularBuffer(960 * 100);
        }

        public override string HandlerName => nameof(AudioSendHandler);
        public IBizSendOutter SendOutter { get; set; } = null!;
        public ChannelReader<Workflow<OutAudioSegment>> PreviousReader { get; set; } = null!;
        public ChannelReader<Workflow<float[]>> PreviousReader2 { get; set; } = null!;

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
                this._outAudioSegmentPool.Return(workflow.Data);
                this._outAudioSegmentWorkflowPool.Return(workflow);
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

                this._sendOpusPacketFrame.Push(outAudioSegment.AudioData);
                while (this._sendOpusPacketFrame.GetFrames(frameSize, out chunk))
                {
                    session.SessionCtsToken.ThrowIfCancellationRequested();
                    
                    await Task.Delay(frameDuration);
                    session.PrivateProvider.AudioMixer!.AddAudioData(outAudioSegment.AudioType, chunk);
                }
                if (isContentNotEmpty)
                {
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.SentenceEnd, outAudioSegment.Content);
                }
            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "audio sending");
            }
            finally
            {
                ArrayPool<float>.Shared.Return(chunk);
                this._outAudioSegmentPool.Return(workflow.Data);
                this._outAudioSegmentWorkflowPool.Return(workflow);
                this._sendOpusPacketFrame.Reset();
            }
        }


        private async void OnMixedAudioDataAvailable(float[] mixedPcmData, bool isFirst, bool isLast)
        {
            Session session = this.SendOutter.GetSession();
            if (isFirst)
            {
                await this.SendOutter.SendTtsMessageAsync(TtsStatus.Start);
                await this.SendOutter.SendLlmMessageAsync(Emotion.Cool);
                this.Logger.LogInformation("Send the first audio from the device: {deviceId}.", session.DeviceId);
            }

            byte[] opusData = await session.PrivateProvider.AudioEncoder!.EncodeAsync(mixedPcmData, session.SessionCtsToken);
            await this.SendOutter.SendAsync(opusData);

            if (isLast)
            {
                await this.SendOutter.SendTtsMessageAsync(TtsStatus.Stop);
                await this.SendOutter.SendLlmMessageAsync(Emotion.Cool);
                this.Logger.LogInformation("Send the last audio from the device: {deviceId}.", session.DeviceId);
                if (session.CloseAfterChat)
                {
                    await this.SendOutter.CloseSessionAsync("Close Chat");
                }
            }
        }
    }
}