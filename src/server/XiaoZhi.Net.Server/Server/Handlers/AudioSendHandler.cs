using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using SherpaOnnx;
using System;
using System.Buffers;
using System.Linq;
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
        private readonly ObjectPool<Workflow<OutAudioSegment>> _workflowPool;
        private readonly CircularBuffer _sendOpusPacketFrame;

        public AudioSendHandler(ObjectPool<OutAudioSegment> outAudioSegmentPool, ObjectPool<Workflow<OutAudioSegment>> workflowPool, 
            XiaoZhiConfig config, ILogger<AudioSendHandler> logger) : base(config, logger)
        {
            this._outAudioSegmentPool = outAudioSegmentPool;
            this._workflowPool = workflowPool;
            this._sendOpusPacketFrame = new CircularBuffer(960 * 100);
        }

        public override string HandlerName => nameof(AudioSendHandler);
        public IBizSendOutter SendOutter { get; set; } = null!;
        public ChannelReader<Workflow<OutAudioSegment>> PreviousReader { get; set; } = null!;
        public ChannelReader<Workflow<float[]>> PreviousReader2 { get; set; } = null!;

        public async Task Handle()
        {
            await foreach (var reader in this.PreviousReader.ReadAllAsync()) await this.Handle(reader);
        }

        public async Task Handle(Workflow<OutAudioSegment> workflow)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                this._outAudioSegmentPool.Return(workflow.Data);
                this._workflowPool.Return(workflow);
                return;
            }

            int frameSize = session.PrivateProvider.AudioEncoder!.FrameSize;
            int frameDuration = session.AudioSetting.FrameDuration;

            float[] chunk = ArrayPool<float>.Shared.Rent(frameSize);

            OutAudioSegment outAudioSegment = workflow.Data;
            try
            {
                this._sendOpusPacketFrame.Push(outAudioSegment.AudioData);

                if (outAudioSegment.IsFirst)
                {
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.Start);
                    await this.SendOutter.SendLlmMessageAsync(Emotion.Cool);
                }

                foreach (var item in outAudioSegment.Contents.Where(i => !string.IsNullOrEmpty(i.Value)))
                {
                    if (outAudioSegment.IsFirst)
                    {
                        this.Logger.LogInformation("Send the first audio from the device: {deviceId}, the segment: {content}.", session.DeviceId, item.Value);
                    }
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.SentenceStart, item.Value);
                }

                while (this._sendOpusPacketFrame.GetFrames(frameSize, out chunk))
                {
                    session.SessionCtsToken.ThrowIfCancellationRequested();

                    byte[] opusData = await session.PrivateProvider.AudioEncoder.EncodeAsync(chunk, session.SessionCtsToken);

                    await Task.Delay(frameDuration);
                    await this.SendOutter.SendAsync(opusData);
                }

            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "audio sending");
            }
            finally
            {
                ArrayPool<float>.Shared.Return(chunk);
                this._sendOpusPacketFrame.Reset();

                foreach (var item in outAudioSegment.Contents.Where(i => !string.IsNullOrEmpty(i.Value)))
                {
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.SentenceEnd, item.Value);
                }
                if (outAudioSegment.IsLast)
                {
                    await this.SendOutter.SendTtsMessageAsync(TtsStatus.Stop);
                    await this.SendOutter.SendLlmMessageAsync(Emotion.Cool);
                }

                if (session.CloseAfterChat)
                {
                    await this.SendOutter.CloseSessionAsync("Close Chat");
                }

                this._outAudioSegmentPool.Return(workflow.Data);
                this._workflowPool.Return(workflow);
            }
        }
    }
}