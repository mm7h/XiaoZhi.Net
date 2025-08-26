using FFmpeg.AutoGen;
using XiaoZhi.Net.Server.AudioPlayer.Utilities.Extensions;

namespace XiaoZhi.Net.Server.AudioPlayer.Decoders.FFmpeg;

internal sealed unsafe class FFmpegResampler : IDisposable
{
    public const AVSampleFormat FFmpegSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_FLT;
    private const int LogOffset = 0;
    private readonly SwrContext* _swrCtx;
    private readonly AVFrame* _dstFrame;
    private readonly int _dstChannels;
    private readonly int _dstSampleRate;
    private readonly int _bytesPerSample;
    private readonly int _srcSampleRate;
    private bool _disposed;

    public FFmpegResampler(
        AVChannelLayout srcChannelLayout,
        int srcSampleRate,
        AVSampleFormat srcSampleFormat,
        int dstChannels,
        int dstSampleRate)
    {
        _dstChannels = dstChannels;
        _dstSampleRate = dstSampleRate;
        _srcSampleRate = srcSampleRate;

        _bytesPerSample = ffmpeg.av_get_bytes_per_sample(FFmpegResampler.FFmpegSampleFormat);

        _swrCtx = ffmpeg.swr_alloc();

        if (_swrCtx is null)
        {
            throw new ArgumentException("Unable to allocate swr context.");
        }

        var dstChannelLayout = new AVChannelLayout();
        ffmpeg.av_channel_layout_default(&dstChannelLayout, dstChannels);

        try
        {
            ffmpeg.av_opt_set_chlayout(_swrCtx, "in_chlayout", &srcChannelLayout, 0);
            ffmpeg.av_opt_set_chlayout(_swrCtx, "out_chlayout", &dstChannelLayout, 0);
            ffmpeg.av_opt_set_int(_swrCtx, "in_sample_rate", srcSampleRate, 0);
            ffmpeg.av_opt_set_int(_swrCtx, "out_sample_rate", dstSampleRate, 0);
            ffmpeg.av_opt_set_sample_fmt(_swrCtx, "in_sample_fmt", srcSampleFormat, 0);
            ffmpeg.av_opt_set_sample_fmt(_swrCtx, "out_sample_fmt", FFmpegResampler.FFmpegSampleFormat, 0);

            ffmpeg.swr_init(_swrCtx).FFGuard();
        }
        finally
        {
            // clear up the temporary channel layout
            ffmpeg.av_channel_layout_uninit(&dstChannelLayout);
        }

        _dstFrame = ffmpeg.av_frame_alloc();
    }

    public bool TryConvert(AVFrame source, out byte[]? result, out string? error)
    {
        try
        {
            ffmpeg.av_frame_unref(_dstFrame);

            int srcNbSamples = source.nb_samples;
            int expectedDstNbSamples = (int)ffmpeg.av_rescale_rnd(
                srcNbSamples,
                _dstSampleRate,
                _srcSampleRate,
                AVRounding.AV_ROUND_UP);

            var delayedSamples = (int)ffmpeg.swr_get_delay(_swrCtx, _dstSampleRate);
            var maxDstSamples = expectedDstNbSamples + delayedSamples + 256; // add some buffer

            var dstChannelLayout = new AVChannelLayout();
            ffmpeg.av_channel_layout_default(&dstChannelLayout, _dstChannels);

            try
            {
                ffmpeg.av_channel_layout_copy(&_dstFrame->ch_layout, &dstChannelLayout);
                _dstFrame->sample_rate = _dstSampleRate;
                _dstFrame->format = (int)FFmpegResampler.FFmpegSampleFormat;
                _dstFrame->nb_samples = maxDstSamples;

                var ret = ffmpeg.av_frame_get_buffer(_dstFrame, LogOffset);
                if (ret < 0)
                {
                    result = null;
                    error = "Failed to allocate frame buffer" + ret.FFErrorToText();
                    return false;
                }

                var code = ffmpeg.swr_convert_frame(_swrCtx, _dstFrame, &source);

                if (code.FFIsError())
                {
                    result = null;
                    error = code.FFErrorToText();
                    return false;
                }

                // check if we got valid output samples
                if (_dstFrame->nb_samples <= 0)
                {
                    // if swr_convert_frame didn't work properly, fall back to manual conversion
                    ffmpeg.av_frame_unref(_dstFrame);

                    // calculate exact number of output samples
                    var outputSamples = (int)ffmpeg.av_rescale_rnd(
                        ffmpeg.swr_get_delay(_swrCtx, _srcSampleRate) + srcNbSamples,
                        _dstSampleRate,
                        _srcSampleRate,
                        AVRounding.AV_ROUND_UP);

                    if (outputSamples <= 0)
                    {
                        result = [];
                        error = null;
                        return true;
                    }

                    // allocate output buffer manually
                    var bufferSize = outputSamples * _bytesPerSample * _dstChannels;
                    var outputBuffer = new byte[bufferSize];

                    fixed (byte* outputPtr = &outputBuffer[0])
                    {
                        byte*[] dstData = new byte*[_dstChannels];
                        dstData[0] = outputPtr;

                        var sourceChannels = source.ch_layout.nb_channels;
                        byte*[] srcData = new byte*[sourceChannels];
                        for (uint i = 0; i < sourceChannels; i++)
                        {
                            srcData[i] = source.data[i];
                        }

                        fixed (byte** dstDataPtr = &dstData[0])
                        fixed (byte** srcDataPtr = &srcData[0])
                        {
                            var convertedSamples = ffmpeg.swr_convert(
                                _swrCtx,
                                dstDataPtr,
                                outputSamples,
                                srcDataPtr,
                                srcNbSamples);
                            if (convertedSamples < 0)
                            {
                                result = null;
                                error = "Error during manual resampling: " + convertedSamples.FFErrorToText();
                                return false;
                            }

                            if (convertedSamples == 0)
                            {
                                result = [];
                                error = null;
                                return true;
                            }

                            var actualSize = convertedSamples * _bytesPerSample * _dstChannels;
                            var finalResult = new byte[actualSize];
                            Array.Copy(outputBuffer, 0, finalResult, 0, actualSize);

                            result = finalResult;
                            error = null;
                            return true;
                        }

                    }
                }

                var size = _dstFrame->nb_samples * _bytesPerSample * _dstFrame->ch_layout.nb_channels;
                var data = new byte[size];
                fixed (byte* h = &data[0])
                {
                    Buffer.MemoryCopy(_dstFrame->data[0], h, size, size);
                }

                result = data;
                error = null;
                return true;
            }
            finally
            {
                ffmpeg.av_channel_layout_uninit(&dstChannelLayout);
            }

        }
        catch (Exception ex)
        {
            result = null;
            error = ex.Message;

            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        var dstFrame = _dstFrame;
        ffmpeg.av_frame_free(&dstFrame);

        var swrCtx = _swrCtx;
        ffmpeg.swr_free(&swrCtx);

        _disposed = true;
    }
}
