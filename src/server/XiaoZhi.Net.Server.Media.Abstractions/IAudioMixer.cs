using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Media.Abstractions
{
    /// <summary>
    /// Audio mixer interface supporting multi-channel audio input real-time mixing
    /// </summary>
    public interface IAudioMixer : IDisposable
    {
        /// <summary>
        /// Mixer state change event
        /// </summary>
        event Action<AudioMixerState> StateChanged;

        /// <summary>
        /// Mixed audio data available event
        /// </summary>
        event Action<float[], bool, bool>? OnMixedAudioDataAvailable;

        /// <summary>
        /// Audio statistics information event
        /// </summary>
        event Action<AudioMixerStats> OnStatsUpdated;

        /// <summary>
        /// Gets whether the mixer has been initialized
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Output audio sample rate (Hz)
        /// </summary>
        int OutputSampleRate { get; }

        /// <summary>
        /// Output audio channel count
        /// </summary>
        int OutputChannels { get; }

        /// <summary>
        /// Frame duration in milliseconds
        /// </summary>
        int FrameDuration { get; }

        /// <summary>
        /// Initialize the audio mixer
        /// </summary>
        /// <param name="outputSampleRate">Output sample rate</param>
        /// <param name="outputChannels">Output channel count</param>
        /// <param name="frameDuration">Frame duration in milliseconds</param>
        /// <param name="config">Mixer configuration</param>
        /// <param name="subtitleSyncTracker">Optional subtitle synchronization tracker</param>
        /// <returns>Whether initialization was successful</returns>
        bool Initialize(int outputSampleRate, int outputChannels, int frameDuration, AudioMixerConfig? config = null, IAudioSubtitleSyncTracker? subtitleSyncTracker = null);

        /// <summary>
        /// Add audio data to the specified priority audio mixer
        /// </summary>
        /// <param name="audioType">Audio type with priority</param>
        /// <param name="audioData">Audio data</param>
        void AddAudioData(AudioType audioType, float[] audioData);

        /// <summary>
        /// Stop the specified type of audio stream
        /// </summary>
        /// <param name="audioType">Audio type</param>
        void StopAudioStream(AudioType audioType);

        /// <summary>
        /// Clear all audio buffers
        /// </summary>
        void ClearAllBuffers();

        /// <summary>
        /// Get current mixer statistics
        /// </summary>
        AudioMixerStats GetCurrentStats();
    }
}
