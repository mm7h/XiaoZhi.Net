using FFmpeg.AutoGen;
using XiaoZhi.Net.Server.Media.Common.Buffers;
using XiaoZhi.Net.Server.Media.Common.Models;
using XiaoZhi.Net.Server.Media.Common.Options;
using XiaoZhi.Net.Server.Media.Utilities.Extensions;

namespace XiaoZhi.Net.Server.Media.Decoders.FFmpeg;

/// <summary>
/// 表示使用 FFmpeg 解码和解复用指定音频源的类。
/// 此类不能被继承。
/// <para>实现：<see cref="IAudioDecoder"/>。</para>
/// </summary>
internal unsafe class FFmpegUrlDecoder : IAudioDecoder
{
    private const int StreamBufferSize = 4096;
    private const AVMediaType MediaType = AVMediaType.AVMEDIA_TYPE_AUDIO;
    private readonly object _syncLock = new();
    private readonly AVFormatContext* _formatCtx;
    private readonly AVIOInterruptCB_callback _interruptCallback;
    private readonly AVCodecContext* _codecCtx;
    private readonly AVPacket* _currentPacket;
    private readonly AVFrame* _currentFrame;
    private readonly FFmpegResampler _resampler;
    private readonly int _streamIndex;
    private readonly int _frameSampleCount;
    private readonly int _outputChannels;
    private readonly int _outputSampleRate;
    private readonly int _frameDurationMs;
    private readonly TimeSpan _openTimeout;
    private readonly TimeSpan _readInactivityTimeout;
    private readonly CancellationToken _initializationCancellationToken;
    private readonly PooledByteBufferWriter _sampleBuffer = new PooledByteBufferWriter();
    private int _disposed;
    private long _ioDeadlineTimestamp;
    private int _interruptionReason;
    private int _interruptRequested;
    private int _wasInterrupted;

    /// <summary>
    /// 通过提供音频 URL 初始化 <see cref="FFmpegDecoder"/>。
    /// 音频 URL 可以是 URL 或本地音频文件路径。
    /// </summary>
    /// <param name="url">要解码的音频 URL 或音频文件路径。</param>
    /// <param name="options">可选的 FFmpeg 解码器选项。</param>
    /// <param name="openTimeout">打开 URL 和读取流信息的最长时间。</param>
    /// <param name="readInactivityTimeout">单次读取无进展的最长时间。</param>
    /// <exception cref="ArgumentNullException">指定的 URL 为 <c>null</c> 时引发。</exception>
    public FFmpegUrlDecoder(
        string url,
        FFmpegDecoderOptions options,
        TimeSpan openTimeout,
        TimeSpan readInactivityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(openTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(readInactivityTimeout, TimeSpan.Zero);

        this._initializationCancellationToken = cancellationToken;
        this._openTimeout = openTimeout;
        this._readInactivityTimeout = readInactivityTimeout;
        this._formatCtx = ffmpeg.avformat_alloc_context();
        this._interruptCallback = this.InterruptCallback;

        try
        {
            if (this._formatCtx is null)
            {
                throw new InvalidOperationException("Unable to allocate FFmpeg format context.");
            }

            this._formatCtx->interrupt_callback.callback = this._interruptCallback;

            // URL 打开和流信息探测都可能在原生 I/O 中阻塞。
            // AVIOInterruptCB 是跨协议的最终超时保障。
            this.BeginIoDeadline(this._openTimeout);
            AVDictionary* dictionary = null;
            try
            {
                // URLContext 的 rw_timeout 单位为微秒，作为协议内部等待的兜底；
                // 打开阶段仍由上面的配置化单调时钟截止点控制。
                ffmpeg.av_dict_set_int(
                    &dictionary,
                    "rw_timeout",
                    checked(this._readInactivityTimeout.Ticks / 10),
                    0);

                var formatCtx = this._formatCtx;
                int openResult = ffmpeg.avformat_open_input(&formatCtx, url, null, &dictionary);
                this._formatCtx = formatCtx;
                this.ThrowIfUrlReadTimedOut();
                openResult.FFGuard();

                int streamInfoResult = ffmpeg.avformat_find_stream_info(this._formatCtx, null);
                this.ThrowIfUrlReadTimedOut();
                streamInfoResult.FFGuard();
            }
            finally
            {
                ffmpeg.av_dict_free(&dictionary);
                this.ClearIoDeadline();
            }

            AVCodec* codec = null;
            this._streamIndex = ffmpeg.av_find_best_stream(this._formatCtx, MediaType, -1, -1, &codec, 0).FFGuard();

            // 指定的源可能是视频或包含多个流。
            // 由于只处理音频流，丢弃其他流。
            for (var i = 0; i < this._formatCtx->nb_streams; i++)
            {
                if (i != this._streamIndex)
                {
                    this._formatCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;
                }
            }

            this._codecCtx = ffmpeg.avcodec_alloc_context3(codec);

            ffmpeg.avcodec_parameters_to_context(this._codecCtx, this._formatCtx->streams[this._streamIndex]->codecpar).FFGuard();
            ffmpeg.avcodec_open2(this._codecCtx, codec, null).FFGuard();

            options ??= new FFmpegDecoderOptions();

            var srcChannelLayout = this._codecCtx->ch_layout;

            // 验证采样率。
            if (this._codecCtx->sample_rate <= 0)
            {
                throw new InvalidOperationException($"Invalid sample rate: {this._codecCtx->sample_rate}. Unable to decode audio stream from: {url}");
            }

            // 确保声道布局有效。
            if (srcChannelLayout.nb_channels == 0 || (srcChannelLayout.order == AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC && srcChannelLayout.u.mask == 0))
            {
                // 未指定声道布局时，尝试从编解码器上下文获取声道数。
                var channelCount = this._codecCtx->ch_layout.nb_channels;
                if (channelCount <= 0)
                {
                    throw new InvalidOperationException($"Unable to determine channel count for audio stream from: {url}. Channel layout is unspecified and channel count is {channelCount}.");
                }

                ffmpeg.av_channel_layout_default(&srcChannelLayout, channelCount);
            }

            this._resampler = new FFmpegResampler(
                srcChannelLayout,
                this._codecCtx->sample_rate,
                this._codecCtx->sample_fmt,
                options.Channels,
                options.SampleRate);

            this._outputChannels = options.Channels;
            this._outputSampleRate = options.SampleRate;
            this._frameDurationMs = options.FrameDuration;
            this._frameSampleCount = this._outputSampleRate * this._frameDurationMs / 1000 * this._outputChannels;

            var rational = ffmpeg.av_q2d(this._formatCtx->streams[this._streamIndex]->time_base);
            var duration = this._formatCtx->streams[this._streamIndex]->duration * rational * 1000.00;
            duration = duration > 0 ? duration : this._formatCtx->duration / 1000.00;

            this.StreamInfo = new AudioStreamInfo(srcChannelLayout.nb_channels, this._codecCtx->sample_rate, TimeSpan.FromMicroseconds(duration));

            this._currentPacket = ffmpeg.av_packet_alloc();
            this._currentFrame = ffmpeg.av_frame_alloc();
            this._initializationCancellationToken = default;
        }
        catch
        {
            this.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public AudioStreamInfo StreamInfo { get; }

    /// <inheritdoc />
    public bool WasInterrupted => Volatile.Read(ref this._wasInterrupted) != 0;

    /// <inheritdoc />
    public AudioDecoderInterruptionReason InterruptionReason => (AudioDecoderInterruptionReason)Volatile.Read(ref this._interruptionReason);

    /// <inheritdoc />
    public AudioDecoderResult DecodeNextFrame()
    {
        lock (this._syncLock)
        {
            if (Volatile.Read(ref this._disposed) != 0)
            {
                return new AudioDecoderResult(null, false, true, "Decoder has been disposed");
            }

            while (this._sampleBuffer.Count < this._frameSampleCount * sizeof(float))
            {
                ffmpeg.av_frame_unref(this._currentFrame);
                while (true)
                {
                    int code;
                    do
                    {
                        ffmpeg.av_packet_unref(this._currentPacket);
                        this.BeginIoDeadline(this._readInactivityTimeout);
                        try
                        {
                            code = ffmpeg.av_read_frame(this._formatCtx, this._currentPacket);
                        }
                        finally
                        {
                            this.ClearIoDeadline();
                        }

                        if (code.FFIsError())
                        {
                            ffmpeg.av_packet_unref(this._currentPacket);
                            this.MarkInterruptedIfRequested(code);
                            if (this._sampleBuffer.Count > 0)
                            {
                                byte[] lastData = this._sampleBuffer.ReadRemaining();
                                return new AudioDecoderResult(new AudioFrame(0, lastData), true, true);
                            }
                            return new AudioDecoderResult(null, false, code.FFIsEOF(), code.FFErrorToText());
                        }
                    } while (this._currentPacket->stream_index != this._streamIndex);

                    code = ffmpeg.avcodec_send_packet(this._codecCtx, this._currentPacket);
                    ffmpeg.av_packet_unref(this._currentPacket);
                    if (code.FFIsError())
                    {
                        return new AudioDecoderResult(null, false, code.FFIsEOF(), code.FFErrorToText());
                    }

                    code = ffmpeg.avcodec_receive_frame(this._codecCtx, this._currentFrame);
                    if (code == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        continue;
                    }

                    if (code.FFIsError())
                    {
                        return new AudioDecoderResult(null, false, code.FFIsEOF(), code.FFErrorToText());
                    }

                    if (!code.FFIsError())
                    {
                        break;
                    }
                }
                if (this._currentFrame->ch_layout.nb_channels <= 0 || (this._currentFrame->ch_layout.order == AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC && this._currentFrame->ch_layout.u.mask == 0))
                {
                    var channelCount = this._codecCtx->ch_layout.nb_channels;
                    if (channelCount <= 0)
                    {
                        return new AudioDecoderResult(null, false, false,
                            "Unable to determine channel count for current frame. Both frame and codec context have invalid channel information.");
                    }
                    ffmpeg.av_channel_layout_default(&this._currentFrame->ch_layout, channelCount);
                }
                if (!this._resampler.TryConvert(*this._currentFrame, this._sampleBuffer, out string? error))
                {
                    return new AudioDecoderResult(null, false, false, error);
                }
            }
            // 输出帧。
            byte[] frameData = this._sampleBuffer.Read(this._frameSampleCount * sizeof(float));

            // 获取最佳或最准确的呈现时间戳。
            long pts = this._currentFrame->best_effort_timestamp >= 0 ? this._currentFrame->best_effort_timestamp : this._currentFrame->pts >= 0 ? this._currentFrame->pts : 0;

            // 计算 FFmpeg 呈现时间戳的毫秒值。
            var rational = ffmpeg.av_q2d(this._formatCtx->streams[this._streamIndex]->time_base);
            var presentationTime = Math.Round(pts * rational * 1000.0, 2);
            return new AudioDecoderResult(new AudioFrame(presentationTime, frameData), true, false);
        }
    }

    /// <inheritdoc />
    public bool TrySeek(TimeSpan position, out string? error)
    {
        lock (this._syncLock)
        {
            if (Volatile.Read(ref this._disposed) != 0)
            {
                error = "Decoder has been disposed";
                return false;
            }

            var tb = this._formatCtx->streams[this._streamIndex]->time_base;
            var pos = (long)(position.TotalSeconds * ffmpeg.AV_TIME_BASE);
            var ts = ffmpeg.av_rescale_q(pos, ffmpeg.av_get_time_base_q(), tb);

            var code = ffmpeg.avformat_seek_file(this._formatCtx, this._streamIndex, 0, ts, long.MaxValue, 0);
            if (!code.FFIsError())
            {
                ffmpeg.avcodec_flush_buffers(this._codecCtx);
                this._sampleBuffer.Clear();
            }

            error = code.FFIsError() ? code.FFErrorToText() : null;
            return !code.FFIsError();
        }
    }

    /// <inheritdoc />
    public void RequestInterrupt()
    {
        Interlocked.Exchange(ref this._interruptRequested, 1);
    }

    /// <inheritdoc />
    public void ResetInterrupt()
    {
        if (Volatile.Read(ref this._disposed) == 0)
        {
            Interlocked.Exchange(ref this._interruptRequested, 0);
        }
    }

    /// <inheritdoc />
    public void FlushAfterInterrupt()
    {
        lock (this._syncLock)
        {
            if (Volatile.Read(ref this._disposed) == 0)
            {
                ffmpeg.avcodec_flush_buffers(this._codecCtx);
                this._sampleBuffer.Clear();
            }
        }
    }

    private void MarkInterruptedIfRequested(int code)
    {
        if (this.InterruptionReason == AudioDecoderInterruptionReason.UrlReadTimeout)
        {
            return;
        }

        if (code == ffmpeg.AVERROR_EXIT
            || Volatile.Read(ref this._interruptRequested) != 0
            || this._initializationCancellationToken.IsCancellationRequested)
        {
            Interlocked.Exchange(ref this._wasInterrupted, 1);
            Interlocked.CompareExchange(
                ref this._interruptionReason,
                (int)AudioDecoderInterruptionReason.Requested,
                (int)AudioDecoderInterruptionReason.None);
        }
    }

    private int InterruptCallback(void* opaque)
    {
        if (this.IsIoDeadlineExceeded())
        {
            Interlocked.CompareExchange(
                ref this._interruptionReason,
                (int)AudioDecoderInterruptionReason.UrlReadTimeout,
                (int)AudioDecoderInterruptionReason.None);
            Interlocked.Exchange(ref this._wasInterrupted, 1);
            return 1;
        }

        return Volatile.Read(ref this._disposed) != 0
            || Volatile.Read(ref this._interruptRequested) != 0
            || this._initializationCancellationToken.IsCancellationRequested
            ? 1
            : 0;
    }

    private void BeginIoDeadline(TimeSpan timeout)
    {
        long timeoutTicks = checked((long)Math.Ceiling(timeout.TotalSeconds * TimeProvider.System.TimestampFrequency));
        long deadline = checked(TimeProvider.System.GetTimestamp() + timeoutTicks);
        Volatile.Write(ref this._ioDeadlineTimestamp, deadline);
    }

    private void ClearIoDeadline()
    {
        Volatile.Write(ref this._ioDeadlineTimestamp, 0);
    }

    private bool IsIoDeadlineExceeded()
    {
        long deadline = Volatile.Read(ref this._ioDeadlineTimestamp);
        return deadline != 0 && TimeProvider.System.GetTimestamp() >= deadline;
    }

    private void ThrowIfUrlReadTimedOut()
    {
        if (this.InterruptionReason == AudioDecoderInterruptionReason.UrlReadTimeout)
        {
            throw new TimeoutException("URL 音频源读取超时。");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) != 0)
        {
            return;
        }

        this.RequestInterrupt();

        lock (this._syncLock)
        {
            var packet = this._currentPacket;
            ffmpeg.av_packet_free(&packet);

            var frame = this._currentFrame;
            ffmpeg.av_frame_free(&frame);

            var formatCtx = this._formatCtx;
            if (formatCtx is not null)
            {
                ffmpeg.avformat_close_input(&formatCtx);
            }
            var codecCtx = this._codecCtx;
            ffmpeg.avcodec_free_context(&codecCtx);

            this._resampler?.Dispose();
            this._sampleBuffer.Dispose();
        }
    }
}
