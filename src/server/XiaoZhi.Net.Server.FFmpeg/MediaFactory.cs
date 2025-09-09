using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XiaoZhi.Net.Server.FFmpeg.Abstractions;
using XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.FFmpeg.Mixers;
using XiaoZhi.Net.Server.FFmpeg.Players;

namespace XiaoZhi.Net.Server.FFmpeg
{
    /// <summary>
    /// Provides factory methods for creating instances of meadia providers.
    /// </summary>
    /// <remarks>This factory class includes methods to create audio players for different input sources,
    /// such as URLs and streams. 
    /// The created audio players are  pre-configured with default logging behavior.
    /// Thanks to https://github.com/luthfiampas/Bufdio for the FFmpeg integration approach.
    /// </remarks>
    public static class MediaFactory
    {
        /// <summary>
        /// Initializes the FFmpeg library with the specified root path.
        /// </summary>
        /// <remarks>This method sets the root path for FFmpeg and attempts to verify the library's
        /// availability by retrieving its version information. If the specified path is invalid or an error occurs
        /// during initialization, the method returns <see langword="false"/>.</remarks>
        /// <param name="ffmpegPath">The root path to the FFmpeg binaries. Defaults to "./ffmpeg/" if not specified. Must not be null or empty.</param>
        /// <returns><see langword="true"/> if the initialization is successful; otherwise, <see langword="false"/>.</returns>
        public static bool InitializeFFmpeg(string? ffmpegPath = "./ffmpeg/")
        {
            try
            {
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    return false;
                }
                ffmpeg.RootPath = AudioPlayerBase.FFmpegRootPath = ffmpegPath;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates a new instance of an audio player that plays audio from a URL.
        /// </summary>
        /// <returns>An <see cref="IUrlAudioPlayer"/> instance configured to play audio from URL sources.</returns>
        public static IUrlAudioPlayer CreateUrlAudioPlayer()
        {
            return new UrlAudioPlayer(NullLoggerFactory.Instance.CreateLogger<UrlAudioPlayer>());
        }

        /// <summary>
        /// Creates a new instance of an audio player designed for streaming audio.
        /// </summary>
        /// <remarks>The returned audio player is initialized with a default logger instance.  It is
        /// suitable for scenarios where audio needs to be streamed and played in real-time.</remarks>
        /// <returns>An <see cref="IStreamAudioPlayer"/> instance configured for streaming audio playback.</returns>
        public static IStreamAudioPlayer CreateStreamAudioPlayer()
        {
            return new StreamAudioPlayer(NullLoggerFactory.Instance.CreateLogger<StreamAudioPlayer>());
        }

        /// <summary>
        /// Creates a new instance of FFmpeg-native audio mixer for real-time multi-stream audio mixing.
        /// </summary>
        /// <param name="sampleRate">Output sample rate in Hz</param>
        /// <param name="channels">Number of output channels</param>
        /// <param name="frameDuration">Frame duration in milliseconds</param>
        /// <param name="config">Optional configuration for the audio mixer</param>
        /// <returns>An <see cref="IAudioMixer"/> instance configured for multi-stream audio mixing using FFmpeg filters</returns>
        public static IAudioMixer CreateAudioMixer(int sampleRate, int channels, int frameDuration, AudioMixerConfig? config = null)
        {
            IAudioMixer mixer = new FFmpegAudioMixer(NullLoggerFactory.Instance.CreateLogger<FFmpegAudioMixer>());
            
            if (!mixer.Initialize(sampleRate, channels, frameDuration, config))
            {
                mixer.Dispose();
                throw new InvalidOperationException($"Failed to initialize FFmpegAudioMixer with parameters: sampleRate={sampleRate}, channels={channels}, frameDuration={frameDuration}");
            }

            return mixer;
        }
    }
}
