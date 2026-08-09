namespace XiaoZhi.Net.Server.Media.Abstractions;

/// <summary>
/// 音频播放器的解码调度和资源限制配置。
/// </summary>
public sealed class AudioPlayerOptions
{
    private int? _maxPlaybackContexts;
    private int? _maxConcurrentLoads;

    /// <summary>
    /// 获取或设置专用解码工作线程数。
    /// </summary>
    public int DecoderWorkerCount { get; init; } = Math.Max(2, Environment.ProcessorCount);

    /// <summary>
    /// 获取或设置可同时保有解码器上下文的最大数量。
    /// </summary>
    public int MaxPlaybackContexts
    {
        get => this._maxPlaybackContexts ?? Math.Min(64, Math.Max(32, this.DecoderWorkerCount * 2));
        init => this._maxPlaybackContexts = value;
    }

    /// <summary>
    /// 获取或设置同时打开或重建解码器的最大数量。
    /// </summary>
    public int MaxConcurrentLoads
    {
        get => this._maxConcurrentLoads ?? Math.Max(1, this.DecoderWorkerCount / 4);
        init => this._maxConcurrentLoads = value;
    }

    /// <summary>
    /// 获取或设置触发补帧的低水位。
    /// </summary>
    public TimeSpan LowBufferDuration { get; init; } = TimeSpan.FromMilliseconds(360);

    /// <summary>
    /// 获取或设置单个播放上下文的目标预缓冲时长。
    /// </summary>
    public TimeSpan TargetBufferDuration { get; init; } = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// 获取或设置单个播放上下文允许的最大预缓冲时长。
    /// </summary>
    public TimeSpan MaxBufferDuration { get; init; } = TimeSpan.FromMilliseconds(1800);

    /// <summary>
    /// 获取或设置一次解码调度最多读取的音频帧数。
    /// </summary>
    public int MaxFramesPerBatch { get; init; } = 8;

    /// <summary>
    /// 获取或设置一次解码调度允许占用专用线程的最长时间。
    /// </summary>
    public TimeSpan MaxDecodeBatchDuration { get; init; } = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// 获取或设置所有播放上下文合计可缓存的最大 PCM 字节数。
    /// </summary>
    public long MaxGlobalBufferedBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>
    /// 获取或设置打开 URL 音频源并读取流信息的最长时间。
    /// </summary>
    public TimeSpan UrlOpenTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 获取或设置 URL 音频源单次读取无进展的最长时间。
    /// </summary>
    public TimeSpan UrlReadInactivityTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
