using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Common.Models;
using XiaoZhi.Net.Server.Media.Common.Options;
using XiaoZhi.Net.Server.Media.Decoders;
using XiaoZhi.Net.Server.Media.Decoders.FFmpeg;
using XiaoZhi.Net.Server.Media.Players.WorkPool;

namespace XiaoZhi.Net.Server.Media.Players;

/// <summary>
/// 表示提供音频加载和播放控制功能的类。
/// <para>实现：<see cref="IAudioPlayer"/>。</para>
/// </summary>
/// <remarks>
/// 通过提供 <see cref="FFmpegDecoderOptions"/> 实例初始化 <see cref="UrlAudioPlayer"/>。
/// 音频引擎将自动配置为与解码器输出格式匹配。
/// </remarks>
internal class UrlAudioPlayer(IAudioDecodeScheduler decodeScheduler, ILogger<UrlAudioPlayer> logger)
    : AudioPlayerBase<string, UrlAudioPlayer>(decodeScheduler, logger), IUrlAudioPlayer
{
    private FFmpegDecoderOptions? _decoderOptions;

    public override string AudioPlayerName => nameof(UrlAudioPlayer);

    protected override bool CanRecoverAfterInterrupt => !string.IsNullOrEmpty(this.CurrentUrl);

    protected override int ExpectedFrameBytes => this._decoderOptions is null
        ? 0
        : this._decoderOptions.SampleRate * this._decoderOptions.Channels * this._decoderOptions.FrameDuration / 1000 * sizeof(float);

    protected override TimeSpan FrameDuration => this._decoderOptions is null
        ? TimeSpan.Zero
        : TimeSpan.FromMilliseconds(this._decoderOptions.FrameDuration);

    /// <summary>
    /// 获取或设置当前指定的音频 URL。
    /// </summary>
    public string CurrentUrl { get; private set; } = string.Empty;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">指定的 URL 为 null 时引发。</exception>
    public async Task<bool> LoadAsync(string url, int outputSampleRate, int outputChannels, int frameDuration, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(url))
        {
            return false;
        }
        if (this.State != PlaybackState.Idle)
        {
            return false;
        }
        FFmpegDecoderOptions decoderOptions = new(outputSampleRate, outputChannels, frameDuration);
        this._decoderOptions = decoderOptions;

        bool loaded = await this.LoadInternalAsync(
            workerCancellationToken => this.CreateDecoder(url, workerCancellationToken),
            cancellationToken);

        if (loaded)
        {
            this.CurrentUrl = url;
        }

        return loaded;
    }

    /// <summary>
    /// 创建 <see cref="IAudioDecoder"/> 实例。
    /// 默认返回新的 <see cref="FFmpegDecoder"/> 实例。
    /// </summary>
    /// <param name="url">要加载的音频 URL 或路径。</param>
    /// <returns>新的 <see cref="FFmpegDecoder"/> 实例。</returns>
    protected override IAudioDecoder CreateDecoder(string url, CancellationToken cancellationToken)
    {
        if (this._decoderOptions is null)
        {
            throw new InvalidOperationException("Decoder options is not set.");
        }
        return new FFmpegUrlDecoder(
            url,
            this._decoderOptions,
            this.AudioPlayerOptions.UrlOpenTimeout,
            this.AudioPlayerOptions.UrlReadInactivityTimeout,
            cancellationToken);
    }

    /// <summary>
    /// 处理音频解码器错误。返回 <c>true</c> 时继续解码线程，返回 <c>false</c> 时中断该线程。
    /// 默认情况下会尝试重新初始化 <see cref="CurrentDecoder"/>，并定位到上次的位置。
    /// </summary>
    /// <param name="result">失败的音频解码器结果。</param>
    /// <returns>返回 <c>true</c> 时继续解码线程，返回 <c>false</c> 时中断该线程。</returns>
    protected override IAudioDecoder? CreateRecoveryDecoder(AudioDecoderResult result, CancellationToken cancellationToken)
    {
        this.Logger.LogDebug("Recreating interrupted audio decoder: {ResultErrorMessage}", result.ErrorMessage);
        cancellationToken.ThrowIfCancellationRequested();
        return string.IsNullOrEmpty(this.CurrentUrl) ? null : this.CreateDecoder(this.CurrentUrl, cancellationToken);
    }
}
