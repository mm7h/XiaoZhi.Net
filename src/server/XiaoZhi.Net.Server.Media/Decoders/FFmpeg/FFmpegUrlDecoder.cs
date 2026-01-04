using FFmpeg.AutoGen;
using XiaoZhi.Net.Server.Media.Common.Dtos;
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
    private readonly object _syncLock = new object();
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
    private List<byte> _sampleBuffer = new();
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
        _formatCtx = ffmpeg.avformat_alloc_context();

        // Open and read operations (like av_read_frame) are blocked by default.
        // We need to set http, udp and rstp read timeout, in case connection interrupted.
        AVDictionary* dict = null;
        ffmpeg.av_dict_set_int(&dict, "stimeout", 10, 0);
        ffmpeg.av_dict_set_int(&dict, "timeout", 10, 0);

        var formatCtx = _formatCtx;
        ffmpeg.avformat_open_input(&formatCtx, url, null, &dict).FFGuard();
        ffmpeg.av_dict_free(&dict);

        ffmpeg.avformat_find_stream_info(_formatCtx, null).FFGuard();

        AVCodec* codec = null;
        _streamIndex = ffmpeg.av_find_best_stream(_formatCtx, MediaType, -1, -1, &codec, 0).FFGuard();

        // The given source can be a video or contains multiple streams.
        // Since we will only work with audio stream, let's discard other streams.
        for (var i = 0; i < _formatCtx->nb_streams; i++)
        {
            if (i != _streamIndex)
            {
                _formatCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;
            }
        }

        _codecCtx = ffmpeg.avcodec_alloc_context3(codec);

        ffmpeg.avcodec_parameters_to_context(_codecCtx, _formatCtx->streams[_streamIndex]->codecpar).FFGuard();
        ffmpeg.avcodec_open2(_codecCtx, codec, null).FFGuard();

        options ??= new FFmpegDecoderOptions();

        var srcChannelLayout = _codecCtx->ch_layout;

        // Validate sample rate
        if (_codecCtx->sample_rate <= 0)
        {
            throw new InvalidOperationException($"Invalid sample rate: {_codecCtx->sample_rate}. Unable to decode audio stream from: {url}");
        }

        // Ensure we have a proper channel layout
        if (srcChannelLayout.nb_channels == 0 || (srcChannelLayout.order == AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC && srcChannelLayout.u.mask == 0))
        {
            // If channel layout is not specified, try to get channel count from codec context
            var channelCount = _codecCtx->ch_layout.nb_channels;
            if (channelCount <= 0)
            {
                throw new InvalidOperationException($"Unable to determine channel count for audio stream from: {url}. Channel layout is unspecified and channel count is {channelCount}.");
            }

            ffmpeg.av_channel_layout_default(&srcChannelLayout, channelCount);
        }

        _resampler = new FFmpegResampler(
            srcChannelLayout,
            _codecCtx->sample_rate,
            _codecCtx->sample_fmt,
            options.Channels,
            options.SampleRate);

        _outputChannels = options.Channels;
        _outputSampleRate = options.SampleRate;
        _frameDurationMs = options.FrameDuration;
        _frameSampleCount = _outputSampleRate * _frameDurationMs / 1000 * _outputChannels;

        var rational = ffmpeg.av_q2d(_formatCtx->streams[_streamIndex]->time_base);
        var duration = _formatCtx->streams[_streamIndex]->duration * rational * 1000.00;
        duration = duration > 0 ? duration : _formatCtx->duration / 1000.00;

        StreamInfo = new AudioStreamInfo(srcChannelLayout.nb_channels, _codecCtx->sample_rate, TimeSpan.FromMicroseconds(duration));

        _currentPacket = ffmpeg.av_packet_alloc();
        _currentFrame = ffmpeg.av_frame_alloc();
    }

    /// <inheritdoc />
    public AudioStreamInfo StreamInfo { get; }

    /// <inheritdoc />
    public AudioDecoderResult DecodeNextFrame()
    {
        lock (_syncLock)
        {
            while (_sampleBuffer.Count < _frameSampleCount * sizeof(float))
            {
                ffmpeg.av_frame_unref(_currentFrame);
                while (true)
                {
                    int code;
                    do
                    {
                        ffmpeg.av_packet_unref(_currentPacket);
                        code = ffmpeg.av_read_frame(_formatCtx, _currentPacket);
                        if (code.FFIsError())
                        {
                            ffmpeg.av_packet_unref(_currentPacket);
                            if (_sampleBuffer.Count > 0)
                            {
                                var lastData = _sampleBuffer.ToArray();
                                _sampleBuffer.Clear();
                                return new AudioDecoderResult(new AudioFrame(0, lastData), true, true);
                            }
                            return new AudioDecoderResult(null, false, code.FFIsEOF(), code.FFErrorToText());
                        }
                    } while (_currentPacket->stream_index != _streamIndex);

                    ffmpeg.avcodec_send_packet(_codecCtx, _currentPacket);
                    ffmpeg.av_packet_unref(_currentPacket);
                    code = ffmpeg.avcodec_receive_frame(_codecCtx, _currentFrame);
                    if (code != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        break;
                    }
                }
                if (_currentFrame->ch_layout.nb_channels <= 0 || (_currentFrame->ch_layout.order == AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC && _currentFrame->ch_layout.u.mask == 0))
                {
                    var channelCount = _codecCtx->ch_layout.nb_channels;
                    if (channelCount <= 0)
                    {
                        return new AudioDecoderResult(null, false, false,
                            "Unable to determine channel count for current frame. Both frame and codec context have invalid channel information.");
                    }
                    ffmpeg.av_channel_layout_default(&_currentFrame->ch_layout, channelCount);
                }
                if (!_resampler.TryConvert(*_currentFrame, out byte[]? data, out string? error))
                {
                    return new AudioDecoderResult(null, false, false, error);
                }
                if (data != null && data.Length > 0)
                {
                    _sampleBuffer.AddRange(data);
                }
            }
            // output frame
            var frameData = _sampleBuffer.GetRange(0, _frameSampleCount * sizeof(float)).ToArray();
            _sampleBuffer.RemoveRange(0, _frameSampleCount * sizeof(float));

            // Retrieve the best or most accurate presentation timestamp
            long pts = _currentFrame->best_effort_timestamp >= 0 ? _currentFrame->best_effort_timestamp : _currentFrame->pts >= 0 ? _currentFrame->pts : 0;
            
            // Calculate FFmpeg's presentation timestamp in milliseconds value
            var rational = ffmpeg.av_q2d(_formatCtx->streams[_streamIndex]->time_base);
            var presentationTime = Math.Round(pts * rational * 1000.0, 2);
            return new AudioDecoderResult(new AudioFrame(presentationTime, frameData), true, false);
        }
    }

    /// <inheritdoc />
    public bool TrySeek(TimeSpan position, out string? error)
    {
        lock (_syncLock)
        {
            var tb = _formatCtx->streams[_streamIndex]->time_base;
            var pos = (long)(position.TotalSeconds * ffmpeg.AV_TIME_BASE);
            var ts = ffmpeg.av_rescale_q(pos, ffmpeg.av_get_time_base_q(), tb);

            var code = ffmpeg.avformat_seek_file(_formatCtx, _streamIndex, 0, ts, long.MaxValue, 0);
            ffmpeg.avcodec_flush_buffers(_codecCtx);

            if (!code.FFIsError())
            {
                _sampleBuffer.Clear();
            }

            error = code.FFIsError() ? code.FFErrorToText() : null;
            return !code.FFIsError();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        var packet = _currentPacket;
        ffmpeg.av_packet_free(&packet);

        var frame = _currentFrame;
        ffmpeg.av_frame_free(&frame);

        var formatCtx = _formatCtx;

        ffmpeg.avformat_close_input(&formatCtx);
        var codecCtx = _codecCtx;
        ffmpeg.avcodec_free_context(&codecCtx);

        _resampler?.Dispose();
        _disposed = true;
    }
}
