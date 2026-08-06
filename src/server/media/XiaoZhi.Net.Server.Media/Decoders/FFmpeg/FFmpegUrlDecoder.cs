using FFmpeg.AutoGen;
using XiaoZhi.Net.Server.Media.Common.Models;
using XiaoZhi.Net.Server.Media.Common.Options;
using XiaoZhi.Net.Server.Media.Utilities.Extensions;

namespace XiaoZhi.Net.Server.Media.Decoders.FFmpeg;

/// <summary>
/// A class that uses FFmpeg for decoding and demuxing specified audio source.
/// This class cannot be inherited.
/// <para>Implements: <see cref="IAudioDecoder"/>.</para>
/// </summary>
internal unsafe class FFmpegUrlDecoder : IAudioDecoder
{
    private const int StreamBufferSize = 4096;
    private const AVMediaType MediaType = AVMediaType.AVMEDIA_TYPE_AUDIO;
    private readonly object _syncLock = new();
    private readonly AVFormatContext* _formatCtx;
    private readonly AVCodecContext* _codecCtx;
    private readonly AVPacket* _currentPacket;
    private readonly AVFrame* _currentFrame;
    private readonly FFmpegResampler _resampler;
    private readonly int _streamIndex;
    private readonly int _frameSampleCount;
    private readonly int _outputChannels;
    private readonly int _outputSampleRate;
    private readonly int _frameDurationMs;
    private readonly List<byte> _sampleBuffer = [];
    private bool _disposed;

    /// <summary>
    /// Initializes <see cref="FFmpegDecoder"/> by providing audio URL.
    /// The audio URL can be URL or path to local audio file.
    /// </summary>
    /// <param name="url">Audio URL or audio file path to decode.</param>
    /// <param name="options">An optional FFmpeg decoder options.</param>
    /// <exception cref="ArgumentNullException">Thrown when the given url is <c>null</c>.</exception>
    public FFmpegUrlDecoder(string url, FFmpegDecoderOptions options)
    {
        this._formatCtx = ffmpeg.avformat_alloc_context();

        // Open and read operations (like av_read_frame) are blocked by default.
        // We need to set http, udp and rstp read timeout, in case connection interrupted.
        AVDictionary* dict = null;
        ffmpeg.av_dict_set_int(&dict, "stimeout", 10, 0);
        ffmpeg.av_dict_set_int(&dict, "timeout", 10, 0);

        var formatCtx = this._formatCtx;
        ffmpeg.avformat_open_input(&formatCtx, url, null, &dict).FFGuard();
        ffmpeg.av_dict_free(&dict);

        ffmpeg.avformat_find_stream_info(this._formatCtx, null).FFGuard();

        AVCodec* codec = null;
        this._streamIndex = ffmpeg.av_find_best_stream(this._formatCtx, MediaType, -1, -1, &codec, 0).FFGuard();

        // The given source can be a video or contains multiple streams.
        // Since we will only work with audio stream, let's discard other streams.
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

        // Validate sample rate
        if (this._codecCtx->sample_rate <= 0)
        {
            throw new InvalidOperationException($"Invalid sample rate: {this._codecCtx->sample_rate}. Unable to decode audio stream from: {url}");
        }

        // Ensure we have a proper channel layout
        if (srcChannelLayout.nb_channels == 0 || (srcChannelLayout.order == AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC && srcChannelLayout.u.mask == 0))
        {
            // If channel layout is not specified, try to get channel count from codec context
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
    }

    /// <inheritdoc />
    public AudioStreamInfo StreamInfo { get; }

    /// <inheritdoc />
    public AudioDecoderResult DecodeNextFrame()
    {
        lock (this._syncLock)
        {
            while (this._sampleBuffer.Count < this._frameSampleCount * sizeof(float))
            {
                ffmpeg.av_frame_unref(this._currentFrame);
                while (true)
                {
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
                                var lastData = this._sampleBuffer.ToArray();
                                this._sampleBuffer.Clear();
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
                        return new AudioDecoderResult(null, false, false,
                            "Unable to determine channel count for current frame. Both frame and codec context have invalid channel information.");
                    }
                    ffmpeg.av_channel_layout_default(&this._currentFrame->ch_layout, channelCount);
                }
                if (!this._resampler.TryConvert(*this._currentFrame, out byte[]? data, out string? error))
                {
                    return new AudioDecoderResult(null, false, false, error);
                }
                if (data != null && data.Length > 0)
                {
                    this._sampleBuffer.AddRange(data);
                }
            }
            // output frame
            var frameData = this._sampleBuffer.GetRange(0, this._frameSampleCount * sizeof(float)).ToArray();
            this._sampleBuffer.RemoveRange(0, this._frameSampleCount * sizeof(float));

            // Retrieve the best or most accurate presentation timestamp
            long pts = this._currentFrame->best_effort_timestamp >= 0 ? this._currentFrame->best_effort_timestamp : this._currentFrame->pts >= 0 ? this._currentFrame->pts : 0;

            // Calculate FFmpeg's presentation timestamp in milliseconds value
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        var packet = this._currentPacket;
        ffmpeg.av_packet_free(&packet);

        var frame = this._currentFrame;
        ffmpeg.av_frame_free(&frame);

        var formatCtx = this._formatCtx;

        ffmpeg.avformat_close_input(&formatCtx);
        var codecCtx = this._codecCtx;
        ffmpeg.avcodec_free_context(&codecCtx);

        this._resampler?.Dispose();
        this._disposed = true;
    }
}
