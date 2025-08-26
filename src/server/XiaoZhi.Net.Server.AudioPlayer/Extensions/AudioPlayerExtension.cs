using FFmpeg.AutoGen;
using Microsoft.Extensions.DependencyInjection;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.AudioPlayer;
using XiaoZhi.Net.Server.AudioPlayer.Players;

namespace XiaoZhi.Net.Server
{
    public static class AudioPlayerExtension
    {
        private const string FFmpegRootPath = "./ffmpeg/";

        /// <summary>
        /// Initialize audio player.
        /// </summary>
        /// <param name="builder">current builder</param>
        /// <param name="ffmpegPath">the root path of ffmpeg</param>
        /// <returns></returns>
        public static IServerBuilder WithAudioPlayer(this IServerBuilder builder, string? ffmpegPath = null)
        {
            ffmpeg.RootPath = ffmpegPath ?? FFmpegRootPath;

            //todo: validate the ffmpeg path, if not, throw the exception

            builder.HostBuilder.ConfigureServices((context, services) =>
            {
                services.AddTransient<IAudioPlayer, UrlAudioPlayer>();
                services.AddTransient<IAudioPlayer, StreamAudioPlayer>();
            });
            return builder;
        }
    }
}
