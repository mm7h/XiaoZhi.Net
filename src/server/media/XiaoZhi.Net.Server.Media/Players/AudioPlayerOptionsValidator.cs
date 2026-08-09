using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Media.Players;

internal static class AudioPlayerOptionsValidator
{
    public static void Validate(AudioPlayerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.DecoderWorkerCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxPlaybackContexts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxConcurrentLoads, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxFramesPerBatch, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MaxGlobalBufferedBytes, 0);

        if (options.LowBufferDuration <= TimeSpan.Zero
            || options.TargetBufferDuration <= TimeSpan.Zero
            || options.MaxBufferDuration <= TimeSpan.Zero
            || options.MaxDecodeBatchDuration <= TimeSpan.Zero
            || options.UrlOpenTimeout <= TimeSpan.Zero
            || options.UrlReadInactivityTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "音频缓冲、解码批次和 URL 超时时长必须大于零。");
        }

        if (options.LowBufferDuration >= options.TargetBufferDuration
            || options.TargetBufferDuration > options.MaxBufferDuration)
        {
            throw new ArgumentException("音频缓冲时长必须满足 LowBufferDuration < TargetBufferDuration <= MaxBufferDuration。", nameof(options));
        }
    }
}
