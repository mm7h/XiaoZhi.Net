using Microsoft.Extensions.DependencyInjection;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Mixers;
using XiaoZhi.Net.Server.Media.Players;
using XiaoZhi.Net.Server.Media.Subtitle;
using XiaoZhi.Net.Server.Media.Utilities;

namespace XiaoZhi.Net.Server
{
    public static class MediaExtension
    {
        /// <summary>
        /// Initialize FFmpeg.
        /// </summary>
        /// <param name="builder">current builder</param>
        /// <param name="ffmpegPath">the root path of ffmpeg; Defaults to "./ffmpeg/" if not specified. Must not be null or empty.</param>
        /// <returns></returns>
        public static IServerBuilder InitializeFFmpeg(this IServerBuilder builder, string ffmpegPath = "./ffmpeg/")
        {
            FFmpegStartup.RegisterFFmpegBinaries(ffmpegPath);
            return builder;
        }

        /// <summary>
        /// Initialize audio player.
        /// </summary>
        /// <param name="builder">current builder</param>
        /// <returns></returns>
        public static IServerBuilder WithAudioPlayer(this IServerBuilder builder)
        {
            builder.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddTransient<IUrlAudioPlayer, UrlAudioPlayer>();
                services.AddTransient<IStreamAudioPlayer, StreamAudioPlayer>();
            });
            return builder;
        }

        /// <summary>
        /// Initialize audio mixer
        /// </summary>
        /// <param name="builder">current builder</param>
        /// <param name="useFFmpeg">use ffmpeg audio mixer support</param>
        /// <returns></returns>
        public static IServerBuilder WithAudioMixer(this IServerBuilder builder, bool useFFmpeg = true)
        {
            builder.HostBuilder.ConfigureServices((context, services) =>
            {
                if (useFFmpeg)
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
        /// Initialize audio subtitle sync tracker
        /// </summary>
        /// <param name="builder">current builder</param>
        /// <returns></returns>
        public static IServerBuilder WithAudioSubtitleSyncTracker(this IServerBuilder builder)
        {
            builder.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddTransient<IAudioSubtitleRegister, AudioSubtitleRegister>();
            });
            return builder;
        }

        /// <summary>
        /// Initialize all media services, including audio player, audio mixer and audio subtitle sync tracker.
        /// </summary>
        /// <param name="builder">current builder</param>
        /// <param name="useFFmpeg">use ffmpeg audio mixer support</param>
        /// <returns></returns>
        public static IServerBuilder WithAllMedia(this IServerBuilder builder, bool useFFmpeg = true)
        {
            builder.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddTransient<IUrlAudioPlayer, UrlAudioPlayer>();
                services.AddTransient<IStreamAudioPlayer, StreamAudioPlayer>();
                if (useFFmpeg)
                {
                    services.AddTransient<IAudioMixer, FFmpegAudioMixer>();
                }
                else
                {
                    services.AddTransient<IAudioMixer, AudioMixer>();
                }
                services.AddTransient<IAudioSubtitleRegister, AudioSubtitleRegister>();
            });
            return builder;
        }
    }
}
