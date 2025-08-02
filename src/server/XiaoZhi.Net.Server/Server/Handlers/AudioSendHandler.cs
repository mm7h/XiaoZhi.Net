using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class AudioSendHandler : BaseHandler, IInHandler<float[]>
    {
        private readonly IAudioEncoder _audioEncoder;

        public AudioSendHandler([FromKeyedServices(GlobalProviderNames.GLOBAL_AUDIO_ENCODER)] IAudioEncoder audioEncoder, XiaoZhiConfig config, ILogger<AudioSendHandler> logger) : base(config, logger)
        {
            this._audioEncoder = audioEncoder;
        }

        public override string HandlerName => nameof(AudioSendHandler);
        public IBizSendOutter SendOutter { get; set; } = null!;
        public ChannelReader<Workflow<float[]>> PreviousReader { get; set; } = null!;

        public async Task Handle()
        {
            await foreach (var reader in this.PreviousReader.ReadAllAsync()) await this.Handle(reader);
        }

        public async Task Handle(Workflow<float[]> workflow)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            try
            {
                session.AudioPacketContext.SendOpusPacketFrame.Push(workflow.Data);

                int frameSize = session.PrivateProvider is not null && session.PrivateProvider.AudioEncoder is not null ? session.PrivateProvider.AudioEncoder.FrameSize : this._audioEncoder.FrameSize;

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

                Stopwatch timer = Stopwatch.StartNew();

                int frameDuration = this.Config.AudioSetting.FrameDuration;
                //double startTime = timer.ElapsedMilliseconds;
                //double playPosition = 0;// 已播放时长

                // 如果收集到了足够的数据，一次性编码并播放
                if (preBufferCount > 0)
                {
                    for (int i = 0; i < preBufferCount; i++)
                    {
                        session.SessionCtsToken.ThrowIfCancellationRequested();

                        float[] chunk = new float[frameSize];
                        Array.Copy(preBuffer, i * frameSize, chunk, 0, frameSize);

                        byte[] opusData = await this._audioEncoder.EncodeAsync(chunk, session.SessionCtsToken);

                        //double expectedTime = startTime + (playPosition / 1000);
                        //double currentTime = timer.ElapsedMilliseconds;
                        //int delay = (int)(expectedTime - currentTime);
                        //if (delay > 0)
                        //{
                        //    await Task.Delay(delay);
                        //}
                        await Task.Delay(frameDuration);
                        await this.SendOutter.SendAsync(opusData);

                        //playPosition += frameDuration;
                    }

                    await Task.Delay(20);
                }

                session.SessionCtsToken.ThrowIfCancellationRequested();

                // 当前缓冲区有数据
                while (session.AudioPacketContext.SendOpusPacketFrame.GetFrames(frameSize, out float[] chunk))
                {
                    session.SessionCtsToken.ThrowIfCancellationRequested();

                    byte[] opusData;
                    if (session.PrivateProvider is not null && session.PrivateProvider.AudioEncoder is not null)
                    {
                        opusData = await session.PrivateProvider.AudioEncoder.EncodeAsync(chunk, session.SessionCtsToken);
                    }
                    else
                    {
                        opusData = await this._audioEncoder.EncodeAsync(chunk, session.SessionCtsToken);
                    }

                    //double expectedTime = startTime + (playPosition / 1000);
                    //double currentTime = timer.ElapsedMilliseconds;
                    //int delay = (int)(expectedTime - currentTime);
                    //if (delay > 0)
                    //{
                    //    await Task.Delay(delay);
                    //}
                    await Task.Delay(frameDuration);
                    await this.SendOutter.SendAsync(opusData);

                    //playPosition += frameDuration;
                }

                timer.Stop();
            }
            catch (OperationCanceledException)
            {
                this.FireAbort(session.DeviceId, session.SessionId, "audio sending");
            }
        }
    }
}