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
/// 通过提供 <see cref="FFmpegDecoderOptions"/> 实例初始化 <see cref="StreamAudioPlayer"/>。
/// 音频引擎将自动配置为与解码器输出格式匹配。
/// </remarks>
internal class StreamAudioPlayer(IAudioDecodeScheduler decodeScheduler, ILogger<StreamAudioPlayer> logger)
    : AudioPlayerBase<Stream, StreamAudioPlayer>(decodeScheduler, logger), IStreamAudioPlayer
{
    private FFmpegDecoderOptions? _decoderOptions;

    public override string AudioPlayerName => nameof(StreamAudioPlayer);

    protected override bool CanRecoverAfterInterrupt => this.CurrentStream?.CanSeek == true;

    protected override int ExpectedFrameBytes => this._decoderOptions is null
        ? 0
        : this._decoderOptions.SampleRate * this._decoderOptions.Channels * this._decoderOptions.FrameDuration / 1000 * sizeof(float);

    protected override TimeSpan FrameDuration => this._decoderOptions is null
        ? TimeSpan.Zero
        : TimeSpan.FromMilliseconds(this._decoderOptions.FrameDuration);

    /// <summary>
    /// 获取或设置当前指定的音频流。
    /// </summary>
    protected Stream? CurrentStream { get; set; }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">指定的流为 null 时引发。</exception>
    public async Task<bool> LoadAsync(Stream stream, int outputSampleRate, int outputChannels, int frameDuration, CancellationToken cancellationToken = default)
    {
        if (stream is null)
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
            workerCancellationToken => this.CreateDecoder(stream, workerCancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (loaded)
        {
            this.CurrentStream = stream;
        }

        return loaded;
    }


    /// <summary>
    /// 创建 <see cref="IAudioDecoder"/> 实例。
    /// 默认返回新的 <see cref="FFmpegDecoder"/> 实例。
    /// </summary>
    /// <param name="stream">要加载的音频流。</param>
    /// <param name="decoderOptions"><see cref="FFmpegDecoderOptions"/> 实例。</param>
    /// <returns>新的 <see cref="FFmpegDecoder"/> 实例。</returns>
    protected override IAudioDecoder CreateDecoder(Stream stream, CancellationToken cancellationToken)
    {
        if (this._decoderOptions is null)
        {
            throw new InvalidOperationException("Decoder options is not set.");
        }

        return new FFmpegStreamDecoder(stream, this._decoderOptions, cancellationToken);
    }

    /// <summary>
    /// 处理音频解码器错误。返回 <c>true</c> 时继续解码线程，返回 <c>false</c> 时中断该线程。
    /// 默认情况下会尝试重新初始化 <see cref="CurrentDecoder"/>，并定位到上次的位置。
    /// </summary>
    /// <param name="result">失败的音频解码器结果。</param>
    /// <returns>返回 <c>true</c> 时继续解码线程，返回 <c>false</c> 时中断该线程。</returns>
    protected override IAudioDecoder? CreateRecoveryDecoder(AudioDecoderResult result, CancellationToken cancellationToken)
    {
        this.Logger.LogDebug("Recreating interrupted stream audio decoder: {ResultErrorMessage}", result.ErrorMessage);

        if (this.CurrentStream is not { CanSeek: true } stream)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        stream.Seek(0, SeekOrigin.Begin);
        return this.CreateDecoder(stream, cancellationToken);
    }
}
