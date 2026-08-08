using System.Runtime.InteropServices;
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
internal unsafe class FFmpegStreamDecoder : IAudioDecoder
{
    private const int StreamBufferSize = 4096;
    private const AVMediaType MediaType = AVMediaType.AVMEDIA_TYPE_AUDIO;
    private readonly object _syncLock = new();
    private readonly AVFormatContext* _formatCtx;
    private readonly AVCodecContext* _codecCtx;
    private readonly AVPacket* _currentPacket;
    private readonly AVFrame* _currentFrame;
    private readonly FFmpegResampler _resampler;
    private AVIOContext* _avioContext;
    private avio_alloc_context_read_packet? _reads;
    private avio_alloc_context_seek? _seeks;
    private readonly int _streamIndex;
    private readonly Stream _inputStream;
    private readonly byte[] _inputStreamBuffer;
    private readonly int _frameSampleCount;
    private readonly int _outputChannels;
    private readonly int _outputSampleRate;
    private readonly int _frameDurationMs;
    private readonly CancellationToken _initializationCancellationToken;
    private readonly PooledByteBufferWriter _sampleBuffer = new PooledByteBufferWriter();
    private int _disposed;
    private int _interruptRequested;

    /// <summary>
    /// 通过提供源音频流初始化 <see cref="FFmpegDecoder"/>。
    /// </summary>
    /// <param name="stream">要解码的源音频流。</param>
    /// <param name="options">可选的 FFmpeg 解码器选项。</param>
    /// <exception cref="ArgumentNullException">指定的流为 <c>null</c> 时引发。</exception>
    public FFmpegStreamDecoder(Stream stream, FFmpegDecoderOptions options, CancellationToken cancellationToken = default)
    {
        this._initializationCancellationToken = cancellationToken;
        this._formatCtx = ffmpeg.avformat_alloc_context();

        this._reads = this.ReadsImpl;
        this._seeks = this.SeeksImpl;

        this._inputStream = stream;
        this._inputStreamBuffer = new byte[StreamBufferSize];

        try
        {
            if (this._formatCtx is null)
            {
                throw new InvalidOperationException("Unable to allocate FFmpeg format context.");
            }

            var buffer = (byte*)ffmpeg.av_malloc(StreamBufferSize);
            var avio = ffmpeg.avio_alloc_context(buffer, StreamBufferSize, 0, null, this._reads, null, this._seeks);

            if (avio is null)
            {
                ffmpeg.av_free(buffer);
                throw new ArgumentException("Unable to allocate avio context.");
            }

            this._avioContext = avio;
            this._formatCtx->pb = avio;

            // 打开和读取操作（如 av_read_frame）默认会阻塞。
            // 需要设置 HTTP、UDP 和 RTSP 的读取超时，以防连接中断。
            AVDictionary* dict = null;
            try
            {
                ffmpeg.av_dict_set_int(&dict, "stimeout", 10, 0);
                ffmpeg.av_dict_set_int(&dict, "timeout", 10, 0);

                var formatCtx = this._formatCtx;
                int openResult = ffmpeg.avformat_open_input(&formatCtx, null, null, &dict);
                this._formatCtx = formatCtx;
                openResult.FFGuard();
            }
            finally
            {
                ffmpeg.av_dict_free(&dict);
            }

            ffmpeg.avformat_find_stream_info(this._formatCtx, null).FFGuard();

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
            if (srcChannelLayout.nb_channels == 0)
            {
                ffmpeg.av_channel_layout_default(&srcChannelLayout, this._codecCtx->ch_layout.nb_channels);
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

            this.StreamInfo = new AudioStreamInfo(this._codecCtx->ch_layout.nb_channels, this._codecCtx->sample_rate, TimeSpan.FromMicroseconds(duration));

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
    public AudioDecoderResult DecodeNextFrame()
    {
        lock (this._syncLock)
        {
            if (Volatile.Read(ref this._disposed) != 0
                || Volatile.Read(ref this._interruptRequested) != 0
                || this._initializationCancellationToken.IsCancellationRequested)
            {
                return new AudioDecoderResult(null, false, true, "Decoder has been disposed or interrupted");
            }

            while (this._sampleBuffer.Count < this._frameSampleCount * sizeof(float))
            {
                if (Volatile.Read(ref this._disposed) != 0
                    || Volatile.Read(ref this._interruptRequested) != 0
                    || this._initializationCancellationToken.IsCancellationRequested)
                {
                    break;
                }

                ffmpeg.av_frame_unref(this._currentFrame);
                while (true)
                {
                    if (Volatile.Read(ref this._disposed) != 0
                        || Volatile.Read(ref this._interruptRequested) != 0
                        || this._initializationCancellationToken.IsCancellationRequested)
                    {
                        return new AudioDecoderResult(null, false, true, "Decoder has been disposed or interrupted");
                    }

                    int code;
                    do
                    {
                        ffmpeg.av_packet_unref(this._currentPacket);
                        code = ffmpeg.av_read_frame(this._formatCtx, this._currentPacket);
                        if (code.FFIsError())
                        {
                            ffmpeg.av_packet_unref(this._currentPacket);
                            if (this._sampleBuffer.Count > 0)
                            {
                                byte[] lastData = this._sampleBuffer.ReadRemaining();
                                return new AudioDecoderResult(new AudioFrame(0, lastData), true, true);
                            }
                            return new AudioDecoderResult(null, false, code.FFIsEOF(), code.FFErrorToText());
                        }
                    } while (this._currentPacket->stream_index != this._streamIndex);

                    ffmpeg.avcodec_send_packet(this._codecCtx, this._currentPacket);
                    ffmpeg.av_packet_unref(this._currentPacket);
                    code = ffmpeg.avcodec_receive_frame(this._codecCtx, this._currentFrame);
                    if (code != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        break;
                    }
                }
                if (this._currentFrame->ch_layout.nb_channels <= 0 || (this._currentFrame->ch_layout.order == AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC && this._currentFrame->ch_layout.u.mask == 0))
                {
                    var channelCount = this._codecCtx->ch_layout.nb_channels;
                    if (channelCount <= 0)
                    {
                        return new AudioDecoderResult(null, false, false, "Unable to determine channel count for current frame. Both frame and codec context have invalid channel information.");
                    }
                    ffmpeg.av_channel_layout_default(&this._currentFrame->ch_layout, channelCount);
                }
                if (!this._resampler.TryConvert(*this._currentFrame, this._sampleBuffer, out string? error))
                {
                    return new AudioDecoderResult(null, false, false, error);
                }
            }

            if (Volatile.Read(ref this._disposed) != 0
                || Volatile.Read(ref this._interruptRequested) != 0
                || this._initializationCancellationToken.IsCancellationRequested)
            {
                return new AudioDecoderResult(null, false, true, "Decoder has been disposed or interrupted");
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
            if (Volatile.Read(ref this._disposed) != 0
                || Volatile.Read(ref this._interruptRequested) != 0
                || this._initializationCancellationToken.IsCancellationRequested)
            {
                error = "Decoder has been disposed or interrupted";
                return false;
            }

            var tb = this._formatCtx->streams[this._streamIndex]->time_base;
            var pos = (long)(position.TotalSeconds * ffmpeg.AV_TIME_BASE);
            var ts = ffmpeg.av_rescale_q(pos, ffmpeg.av_get_time_base_q(), tb);

            var code = ffmpeg.avformat_seek_file(this._formatCtx, this._streamIndex, 0, ts, long.MaxValue, 0);
            ffmpeg.avcodec_flush_buffers(this._codecCtx);

            if (!code.FFIsError())
            {
                this._sampleBuffer.Clear();
            }

            error = code.FFIsError() ? code.FFErrorToText() : null;
            return !code.FFIsError();
        }
    }

    /// <summary>
    /// 请求中断解码操作。
    /// 这会使所有阻塞的 FFmpeg 操作以错误形式返回。
    /// </summary>
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

    private int ReadsImpl(void* opaque, byte* buf, int buf_size)
    {
        if (Volatile.Read(ref this._disposed) != 0
            || Volatile.Read(ref this._interruptRequested) != 0
            || this._initializationCancellationToken.IsCancellationRequested)
        {
            return ffmpeg.AVERROR_EXIT;
        }

        try
        {
            buf_size = Math.Min(buf_size, StreamBufferSize);

            // 尽可能使用较短的读取长度，以提高中断响应速度。
            // 对网络流而言，这有助于减少阻塞时间。
            var actualReadSize = Math.Min(buf_size, StreamBufferSize / 4);

            var length = this._inputStream.Read(this._inputStreamBuffer, 0, actualReadSize);

            if (length > 0)
            {
                Marshal.Copy(this._inputStreamBuffer, 0, (IntPtr)buf, length);
            }
            else if (length == 0)
            {
                // 流结束。
                return ffmpeg.AVERROR_EOF;
            }

            return length;
        }
        catch
        {
            // 发生任何错误时返回 EOF，以便正常结束。
            return ffmpeg.AVERROR_EOF;
        }
    }

    private long SeeksImpl(void* opaque, long offset, int whence)
    {
        if (Volatile.Read(ref this._disposed) != 0
            || Volatile.Read(ref this._interruptRequested) != 0
            || this._initializationCancellationToken.IsCancellationRequested)
        {
            return -1;
        }

        try
        {
            return whence switch
            {
                ffmpeg.AVSEEK_SIZE => this._inputStream.Length,
                < 3 => this._inputStream.Seek(offset, (SeekOrigin)whence),
                _ => -1
            };
        }
        catch
        {
            return -1;
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
            var avioContext = this._avioContext;
            if (avioContext is not null)
            {
                ffmpeg.av_freep(&avioContext->buffer);
                ffmpeg.avio_context_free(&avioContext);
                this._avioContext = null;

                if (formatCtx is not null)
                {
                    formatCtx->pb = null;
                }
            }

            if (formatCtx is not null)
            {
                ffmpeg.avformat_close_input(&formatCtx);
            }
            var codecCtx = this._codecCtx;
            ffmpeg.avcodec_free_context(&codecCtx);

            this._resampler?.Dispose();
            this._sampleBuffer.Dispose();
            this._reads = null;
            this._seeks = null;
        }
    }
}
