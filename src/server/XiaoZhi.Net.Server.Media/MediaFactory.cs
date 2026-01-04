using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.Media.Mixers;
using XiaoZhi.Net.Server.Media.Players;
using XiaoZhi.Net.Server.Media.Subtitle;
using XiaoZhi.Net.Server.Media.Utilities;

namespace XiaoZhi.Net.Server.Media
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
        /// Registers the FFmpeg binaries from the specified path.
        /// </summary>
        /// <param name="ffmpegPath">the path.</param>
        public static void InitializeFFmpeg(string ffmpegPath = "./ffmpeg/")
        {
            FFmpegStartup.RegisterFFmpegBinaries(ffmpegPath);
        }

        /// <summary>
        /// Checks whether FFmpeg is installed on the system and retrieves the installed version.
        /// </summary>
        /// <param name="ffmpegVersion">When this method returns, contains the version of FFmpeg installed on the system,  or an empty string if
        /// FFmpeg is not installed. This parameter is passed uninitialized.</param>
        /// <returns><see langword="true"/> if FFmpeg is installed; otherwise, <see langword="false"/>.</returns>
        public static bool CheckFFmpegInstalled(out string ffmpegVersion)
        {
            return FFmpegStartup.CheckFFmpegInstalled(out ffmpegVersion);
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
        /// Creates and returns a new instance of an audio-subtitle synchronization tracker.
        /// </summary>
        /// <returns>An instance of <see cref="IAudioSubtitleRegister"/> for tracking and managing synchronization between
        /// audio and subtitle streams.</returns>
        public static IAudioSubtitleRegister CreateAudioSubtitleSyncTracker()
        {
            return new AudioSubtitleRegister(NullLoggerFactory.Instance.CreateLogger<AudioSubtitleRegister>());
        }

        /// <summary>
        /// Creates a new instance of audio mixer for real-time multi-stream audio mixing.
        /// </summary>
        /// <param name="sampleRate">Output sample rate in Hz</param>
        /// <param name="channels">Number of output channels</param>
        /// <param name="frameDuration">Frame duration in milliseconds</param>
        /// <param name="config">Optional configuration for the audio mixer</param>
        /// <returns>An <see cref="IAudioMixer"/> instance configured for multi-stream audio mixing</returns>
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
        /// Creates a new instance of audio mixer for real-time multi-stream audio mixing.
        /// </summary>
        /// <param name="sampleRate">Output sample rate in Hz</param>
        /// <param name="channels">Number of output channels</param>
        /// <param name="frameDuration">Frame duration in milliseconds</param>
        /// <param name="config">Optional configuration for the audio mixer</param>
        /// <returns>An <see cref="IAudioMixer"/> instance configured for multi-stream audio mixing</returns>
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
    }
}
