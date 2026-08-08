using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.Media.Editors;
using XiaoZhi.Net.Server.Media.Encoders;
using XiaoZhi.Net.Server.Media.Encoders.FFmpeg;
using XiaoZhi.Net.Server.Media.Mixers;
using XiaoZhi.Net.Server.Media.Players;
using XiaoZhi.Net.Server.Media.Players.WorkPool;
using XiaoZhi.Net.Server.Media.Subtitle;
using XiaoZhi.Net.Server.Media.Utilities;

namespace XiaoZhi.Net.Server.Media
{
    /// <summary>
    /// 提供用于创建媒体服务实例的工厂方法。
    /// </summary>
    /// <remarks>此工厂类包含为不同输入源创建音频播放器的方法，例如 URL 和流。
    /// 创建的音频播放器已预先配置默认日志行为。
    /// FFmpeg 集成方案参考了 https://github.com/luthfiampas/Bufdio。
    /// </remarks>
    public static class MediaFactory
    {
        private static readonly IAudioDecoderWorkPool s_audioDecoderWorkPool = new AudioDecoderWorkPool(Math.Max(2, Environment.ProcessorCount));

        /// <summary>
        /// 从指定路径注册 FFmpeg 二进制文件。
        /// </summary>
        /// <param name="ffmpegPath">FFmpeg 二进制文件所在路径。</param>
        public static void InitializeFFmpeg(string ffmpegPath = "./ffmpeg/")
        {
            FFmpegStartup.RegisterFFmpegBinaries(ffmpegPath);
        }

        /// <summary>
        /// 检查系统是否已安装 FFmpeg，并获取已安装的版本。
        /// </summary>
        /// <param name="ffmpegVersion">此方法返回时包含系统已安装的 FFmpeg 版本；未安装时为空字符串。此参数未经初始化即传入。</param>
        /// <returns>如果已安装 FFmpeg，则为 <see langword="true"/>；否则为 <see langword="false"/>。</returns>
        public static bool CheckFFmpegInstalled(out string ffmpegVersion)
        {
            return FFmpegStartup.CheckFFmpegInstalled(out ffmpegVersion);
        }

        /// <summary>
        /// 创建从 URL 播放音频的音频播放器新实例。
        /// </summary>
        /// <returns>已配置为从 URL 源播放音频的 <see cref="IUrlAudioPlayer"/> 实例。</returns>
        public static IUrlAudioPlayer CreateUrlAudioPlayer()
        {
            return new UrlAudioPlayer(s_audioDecoderWorkPool, NullLoggerFactory.Instance.CreateLogger<UrlAudioPlayer>());
        }

        /// <summary>
        /// 创建专用于流式音频的音频播放器新实例。
        /// </summary>
        /// <remarks>返回的音频播放器已使用默认日志记录器实例初始化，适用于需要实时流式播放音频的场景。</remarks>
        /// <returns>已配置为流式播放音频的 <see cref="IStreamAudioPlayer"/> 实例。</returns>
        public static IStreamAudioPlayer CreateStreamAudioPlayer()
        {
            return new StreamAudioPlayer(s_audioDecoderWorkPool, NullLoggerFactory.Instance.CreateLogger<StreamAudioPlayer>());
        }

        /// <summary>
        /// 创建并返回音频与字幕同步跟踪器的新实例。
        /// </summary>
        /// <returns>用于跟踪和管理音频流与字幕流同步关系的 <see cref="IAudioSubtitleRegister"/> 实例。</returns>
        public static IAudioSubtitleRegister CreateAudioSubtitleSyncTracker()
        {
            return new AudioSubtitleRegister(NullLoggerFactory.Instance.CreateLogger<AudioSubtitleRegister>());
        }

        /// <summary>
        /// 创建用于实时多流音频混音的音频混音器新实例。
        /// </summary>
        /// <param name="sampleRate">以 Hz 为单位的输出采样率。</param>
        /// <param name="channels">输出声道数。</param>
        /// <param name="frameDuration">以毫秒为单位的帧时长。</param>
        /// <param name="config">可选的音频混音器配置。</param>
        /// <returns>已配置为多流音频混音的 <see cref="IAudioMixer"/> 实例。</returns>
        public static IAudioMixer CreateAudioMixer(int sampleRate, int channels, int frameDuration, AudioMixerConfig? config = null)
        {
            IAudioMixer mixer = new AudioMixer(NullLoggerFactory.Instance.CreateLogger<AudioMixer>());

            if (!mixer.Initialize(sampleRate, channels, frameDuration, config))
            {
                mixer.Dispose();
                throw new InvalidOperationException($"Failed to initialize FFmpegAudioMixer with parameters: sampleRate={sampleRate}, channels={channels}, frameDuration={frameDuration}");
            }

            return mixer;
        }

        /// <summary>
        /// 创建用于实时多流音频混音的音频混音器新实例。
        /// </summary>
        /// <param name="sampleRate">以 Hz 为单位的输出采样率。</param>
        /// <param name="channels">输出声道数。</param>
        /// <param name="frameDuration">以毫秒为单位的帧时长。</param>
        /// <param name="config">可选的音频混音器配置。</param>
        /// <returns>已配置为多流音频混音的 <see cref="IAudioMixer"/> 实例。</returns>
        public static IAudioMixer CreateFFmpegAudioMixer(int sampleRate, int channels, int frameDuration, AudioMixerConfig? config = null)
        {
            IAudioMixer mixer = new FFmpegAudioMixer(NullLoggerFactory.Instance.CreateLogger<FFmpegAudioMixer>());

            if (!mixer.Initialize(sampleRate, channels, frameDuration, config))
            {
                mixer.Dispose();
                throw new InvalidOperationException($"Failed to initialize FFmpegAudioMixer with parameters: sampleRate={sampleRate}, channels={channels}, frameDuration={frameDuration}");
            }

            return mixer;
        }

        /// <summary>
        /// 使用基于 FFmpeg 的音频编码器创建 <see cref="IAudioEditor"/> 新实例。
        /// </summary>
        /// <returns>使用 <see cref="FFmpegEncoder"/> 初始化的 <see cref="IAudioEditor"/> 实例。</returns>
        public static IAudioEditor CreateAudioEditor()
        {
            IAudioEncoder audioEncoder = new FFmpegEncoder(NullLoggerFactory.Instance.CreateLogger<FFmpegEncoder>());

            return new AudioEditor(audioEncoder);
        }
    }
}
