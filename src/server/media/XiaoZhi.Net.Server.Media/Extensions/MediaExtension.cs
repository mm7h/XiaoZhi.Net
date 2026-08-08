using Microsoft.Extensions.DependencyInjection;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Editors;
using XiaoZhi.Net.Server.Media.Encoders;
using XiaoZhi.Net.Server.Media.Encoders.FFmpeg;
using XiaoZhi.Net.Server.Media.Mixers;
using XiaoZhi.Net.Server.Media.Players;
using XiaoZhi.Net.Server.Media.Players.WorkPool;
using XiaoZhi.Net.Server.Media.Subtitle;
using XiaoZhi.Net.Server.Media.Utilities;

#pragma warning disable IDE0130 // 保持 WithMedia 扩展方法的公开命名空间。
namespace XiaoZhi.Net.Server
{
    public static class MediaExtension
    {
        /// <summary>
        /// 初始化所有媒体服务，包括音频播放器、音频混音器和音频字幕同步跟踪器。
        /// </summary>
        /// <param name="builder">当前构建器。</param>
        /// <param name="useFFmpeg">是否启用 FFmpeg 音频混音器支持。</param>
        /// <returns>当前构建器。</returns>
        public static IServerBuilder WithMedia(this IServerBuilder builder, bool useFFmpegAudioMixer = true, string ffmpegPath = "./ffmpeg/")
        {
            builder.InitializeFFmpeg(ffmpegPath)
                .WithAudioPlayer()
                .WithAudioMixer(useFFmpegAudioMixer)
                .WithAudioSubtitleSyncTracker()
                .WithAudioEditor();

            return builder;
        }

        /// <summary>
        /// 初始化 FFmpeg。
        /// </summary>
        /// <param name="builder">当前构建器。</param>
        /// <param name="ffmpegPath">FFmpeg 根路径；未指定时默认为“./ffmpeg/”。不能为空。</param>
        /// <returns>当前构建器。</returns>
        private static IServerBuilder InitializeFFmpeg(this IServerBuilder builder, string ffmpegPath = "./ffmpeg/")
        {
            FFmpegStartup.RegisterFFmpegBinaries(ffmpegPath);
            return builder;
        }

        /// <summary>
        /// 初始化音频播放器。
        /// </summary>
        /// <param name="builder">当前构建器。</param>
        /// <returns>当前构建器。</returns>
        private static IServerBuilder WithAudioPlayer(this IServerBuilder builder)
        {
            builder.HostBuilder.ConfigureServices((context, services) =>
            {
                int workerCount = Math.Max(2, Environment.ProcessorCount);
                services.AddSingleton<IAudioDecoderWorkPool>(_ => new AudioDecoderWorkPool(workerCount));
                services.AddTransient<IUrlAudioPlayer, UrlAudioPlayer>();
                services.AddTransient<IStreamAudioPlayer, StreamAudioPlayer>();
            });
            return builder;
        }

        /// <summary>
        /// 初始化音频混音器。
        /// </summary>
        /// <param name="builder">当前构建器。</param>
        /// <param name="useFFmpegAudioMixer">是否启用 FFmpeg 音频混音器支持。</param>
        /// <returns>当前构建器。</returns>
        private static IServerBuilder WithAudioMixer(this IServerBuilder builder, bool useFFmpegAudioMixer = true)
        {
            builder.HostBuilder.ConfigureServices((context, services) =>
            {
                if (useFFmpegAudioMixer)
                {
                    services.AddTransient<IAudioMixer, FFmpegAudioMixer>();
                }
                else
                {
                    services.AddTransient<IAudioMixer, AudioMixer>();
                }

            });
            return builder;
        }

        /// <summary>
        /// 初始化音频字幕同步跟踪器。
        /// </summary>
        /// <param name="builder">当前构建器。</param>
        /// <returns>当前构建器。</returns>
        private static IServerBuilder WithAudioSubtitleSyncTracker(this IServerBuilder builder)
        {
            builder.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddTransient<IAudioSubtitleRegister, AudioSubtitleRegister>();
            });
            return builder;
        }

        /// <summary>
        /// 初始化音频编辑器。
        /// </summary>
        /// <param name="builder">当前构建器。</param>
        /// <returns>当前构建器。</returns>
        private static IServerBuilder WithAudioEditor(this IServerBuilder builder)
        {
            builder.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddTransient<IAudioEditor, AudioEditor>();
                services.AddTransient<IAudioEncoder, FFmpegEncoder>();
            });
            return builder;
        }
    }
}
#pragma warning restore IDE0130
