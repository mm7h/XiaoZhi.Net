using Serilog;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class AudioSendHandler : BaseHandler, IInHandler<float[]>
    {
        private readonly IAudioEncoder _audioEncoder;
        private readonly IProtocolEngine _protocolEngine;

        public AudioSendHandler(IAudioEncoder audioEncoder,  IProtocolEngine protocolEngine, XiaoZhiConfig config, ILogger logger) : base(config, logger)
        {
            this._audioEncoder = audioEncoder;
            this._protocolEngine = protocolEngine;
        }

        public override string HandlerName => nameof(AudioSendHandler);
        public ChannelReader<Workflow<float[]>> PreviousReader { get; set; }

        public async Task Handle()
        {
            await foreach (var reader in this.PreviousReader.ReadAllAsync()) await this.Handle(reader);
        }

        public async Task Handle(Workflow<float[]> workflow)
        {
            Session session = this._protocolEngine.GetSessionContext(workflow.SessionId);
            if (session == null || session.ShouldIgnore())
            {
                return;
            }
            try
            {
                session.AudioPacketContext.SendOpusPacketFrame.Push(workflow.Data);

                int frameSize = this._audioEncoder.FrameSize;

                // 增加预缓冲的帧数
                int preBufferFrames = 50;
                float[] preBuffer = new float[preBufferFrames * frameSize];
                int preBufferCount = 0;

                // 预先收集足够的数据
                for (int i = 0; i < preBufferFrames && session.AudioPacketContext.SendOpusPacketFrame.GetFrames(frameSize, out float[] preChunk); i++)
                {
                    Array.Copy(preChunk, 0, preBuffer, i * frameSize, frameSize);
                    preBufferCount++;
                }
                using (CodeTimer timer = CodeTimer.Create(false))
                {
                    int frameDuration = this.Config.AudioSetting.FrameDuration;
                    double startTime = timer.ElapsedMilliseconds;
                    double playPosition = 0;// 已播放时长

                    // 如果收集到了足够的数据，一次性编码并播放
                    if (preBufferCount > 0)
                    {
                        for (int i = 0; i < preBufferCount; i++)
                        {
                            session.SessionCtsToken.ThrowIfCancellationRequested();

                            float[] chunk = new float[frameSize];
                            Array.Copy(preBuffer, i * frameSize, chunk, 0, frameSize);

                            byte[] opusData = await this._audioEncoder.EncodeAsync(chunk, session.SessionCtsToken);

                            double expectedTime = startTime + (playPosition / 1000);
                            double currentTime = timer.ElapsedMilliseconds;
                            int delay = (int)(expectedTime - currentTime);
                            if (delay > 0)
                            {
                                await Task.Delay(delay);
                            }

                            await this._protocolEngine.SendAsync(session.SessionId, opusData);

                            playPosition += frameDuration;
                        }

                        await Task.Delay(20);
                    }

                    session.SessionCtsToken.ThrowIfCancellationRequested();

                    // 当前缓冲区有数据
                    while (session.AudioPacketContext.SendOpusPacketFrame.GetFrames(frameSize, out float[] chunk))
                    {
                        session.SessionCtsToken.ThrowIfCancellationRequested();
                        byte[] opusData = await this._audioEncoder.EncodeAsync(chunk, session.SessionCtsToken);


                        double expectedTime = startTime + (playPosition / 1000);
                        double currentTime = timer.ElapsedMilliseconds;
                        int delay = (int)(expectedTime - currentTime);
                        if (delay > 0)
                        {
                            await Task.Delay(delay);
                        }

                        await this._protocolEngine.SendAsync(session.SessionId, opusData);

                        playPosition += frameDuration;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "audio sending");
            }
        }
    }
}