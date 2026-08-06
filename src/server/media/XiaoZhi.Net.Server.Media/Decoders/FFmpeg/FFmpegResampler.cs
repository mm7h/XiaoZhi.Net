using FFmpeg.AutoGen;
using XiaoZhi.Net.Server.Media.Utilities.Extensions;

namespace XiaoZhi.Net.Server.Media.Decoders.FFmpeg;

internal unsafe class FFmpegResampler : IDisposable
{
    public const AVSampleFormat FFmpegSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_FLT;
    private const int LogOffset = 0;
    private SwrContext* _swrCtx;
    private readonly AVFrame* _dstFrame;
    private readonly int _dstChannels;
    private readonly int _dstSampleRate;
    private readonly int _bytesPerSample;

    // Store the last input parameters to detect changes
    private int _lastSrcSampleRate;
    private AVSampleFormat _lastSrcSampleFormat;
    private int _lastSrcChannels;
    private ulong _lastSrcChannelMask;
    private AVChannelOrder _lastChannelOrder;
    private bool _isInitialized;
    private bool _disposed;

    public FFmpegResampler(
        AVChannelLayout srcChannelLayout,
        int srcSampleRate,
        AVSampleFormat srcSampleFormat,
        int dstChannels,
        int dstSampleRate)
    {
        this._dstChannels = dstChannels;
        this._dstSampleRate = dstSampleRate;

        this._bytesPerSample = ffmpeg.av_get_bytes_per_sample(FFmpegResampler.FFmpegSampleFormat);

        this._dstFrame = ffmpeg.av_frame_alloc();

        // Initialize with the first set of parameters
        this.InitializeSwrContext(srcChannelLayout, srcSampleRate, srcSampleFormat);
    }

    private void InitializeSwrContext(AVChannelLayout srcChannelLayout, int srcSampleRate, AVSampleFormat srcSampleFormat)
    {
        // Free existing context if it exists
        if (this._swrCtx != null)
        {
            var oldSwrCtx = this._swrCtx;
            ffmpeg.swr_free(&oldSwrCtx);
        }

        this._swrCtx = ffmpeg.swr_alloc();

        if (this._swrCtx is null)
        {
            throw new ArgumentException("Unable to allocate swr context.");
        }

        // Ensure we have a valid source channel layout
        var normalizedSrcChannelLayout = srcChannelLayout;
        if (normalizedSrcChannelLayout.nb_channels == 0 ||
            (normalizedSrcChannelLayout.order == AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC && normalizedSrcChannelLayout.u.mask == 0))
        {
            // If the channel layout is not properly set, create a default layout
            ffmpeg.av_channel_layout_default(&normalizedSrcChannelLayout, srcChannelLayout.nb_channels > 0 ? srcChannelLayout.nb_channels : 2);
        }

        var dstChannelLayout = new AVChannelLayout();
        ffmpeg.av_channel_layout_default(&dstChannelLayout, this._dstChannels);

        try
        {
            ffmpeg.av_opt_set_chlayout(this._swrCtx, "in_chlayout", &normalizedSrcChannelLayout, 0);
            ffmpeg.av_opt_set_chlayout(this._swrCtx, "out_chlayout", &dstChannelLayout, 0);
            ffmpeg.av_opt_set_int(this._swrCtx, "in_sample_rate", srcSampleRate, 0);
            ffmpeg.av_opt_set_int(this._swrCtx, "out_sample_rate", this._dstSampleRate, 0);
            ffmpeg.av_opt_set_sample_fmt(this._swrCtx, "in_sample_fmt", srcSampleFormat, 0);
            ffmpeg.av_opt_set_sample_fmt(this._swrCtx, "out_sample_fmt", FFmpegResampler.FFmpegSampleFormat, 0);

            var initResult = ffmpeg.swr_init(this._swrCtx);
            if (initResult < 0)
            {
                throw new InvalidOperationException($"Failed to initialize SwrContext: {initResult.FFErrorToText()}");
            }

            // Store the current parameters
            this._lastSrcSampleRate = srcSampleRate;
            this._lastSrcSampleFormat = srcSampleFormat;
            this._lastSrcChannels = normalizedSrcChannelLayout.nb_channels;
            this._lastSrcChannelMask = normalizedSrcChannelLayout.u.mask;
            this._lastChannelOrder = normalizedSrcChannelLayout.order;
            this._isInitialized = true;
        }
        finally
        {
            ffmpeg.av_channel_layout_uninit(&dstChannelLayout);
            // Clean up normalized layout if it was created
            if (normalizedSrcChannelLayout.nb_channels != srcChannelLayout.nb_channels ||
                normalizedSrcChannelLayout.u.mask != srcChannelLayout.u.mask)
            {
                ffmpeg.av_channel_layout_uninit(&normalizedSrcChannelLayout);
            }
        }
    }

    private bool HasInputChanged(AVFrame source)
    {
        if (!this._isInitialized)
        {
            return true;
        }

        var sourceChannelLayout = source.ch_layout;

        // Handle cases where channel layout is not properly set in the source frame
        if (sourceChannelLayout.nb_channels == 0 ||
            (sourceChannelLayout.order == AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC && sourceChannelLayout.u.mask == 0))
        {
            // If frame doesn't have proper channel layout but has the same number of channels as before, don't reinitialize
            if (source.ch_layout.nb_channels == this._lastSrcChannels)
            {
                return source.sample_rate != this._lastSrcSampleRate ||
                       source.format != (int)this._lastSrcSampleFormat;
            }
            return true;
        }

        return source.sample_rate != this._lastSrcSampleRate ||
               source.format != (int)this._lastSrcSampleFormat ||
               sourceChannelLayout.nb_channels != this._lastSrcChannels ||
               sourceChannelLayout.u.mask != this._lastSrcChannelMask ||
               sourceChannelLayout.order != this._lastChannelOrder;
    }

    public bool TryConvert(AVFrame source, out byte[]? result, out string? error)
    {
        try
        {
            // Handle frames with invalid channel layouts
            var workingFrame = source;
            var tempChannelLayout = new AVChannelLayout();
            bool needsLayoutCleanup = false;

            if (workingFrame.ch_layout.nb_channels == 0 ||
                (workingFrame.ch_layout.order == AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC && workingFrame.ch_layout.u.mask == 0))
            {
                // Create a default channel layout based on the codec context or assume stereo
                var channelCount = workingFrame.ch_layout.nb_channels > 0 ? workingFrame.ch_layout.nb_channels : 2;
                ffmpeg.av_channel_layout_default(&tempChannelLayout, channelCount);
                workingFrame.ch_layout = tempChannelLayout;
                needsLayoutCleanup = true;
            }

            try
            {
                // Check if input parameters have changed
                if (this.HasInputChanged(workingFrame))
                {
                    this.InitializeSwrContext(workingFrame.ch_layout, workingFrame.sample_rate, (AVSampleFormat)workingFrame.format);
                }

                ffmpeg.av_frame_unref(this._dstFrame);

                int srcNbSamples = workingFrame.nb_samples;
                if (srcNbSamples <= 0)
                {
                    result = [];
                    error = null;
                    return true;
                }

                int expectedDstNbSamples = (int)ffmpeg.av_rescale_rnd(
                    srcNbSamples,
                    this._dstSampleRate,
                    this._lastSrcSampleRate,
                    AVRounding.AV_ROUND_UP);

                var delayedSamples = (int)ffmpeg.swr_get_delay(this._swrCtx, this._dstSampleRate);
                var maxDstSamples = expectedDstNbSamples + delayedSamples + 256; // add some buffer

                var dstChannelLayout = new AVChannelLayout();
                ffmpeg.av_channel_layout_default(&dstChannelLayout, this._dstChannels);

                try
                {
                    ffmpeg.av_channel_layout_copy(&this._dstFrame->ch_layout, &dstChannelLayout);
                    this._dstFrame->sample_rate = this._dstSampleRate;
                    this._dstFrame->format = (int)FFmpegResampler.FFmpegSampleFormat;
                    this._dstFrame->nb_samples = maxDstSamples;

                    var ret = ffmpeg.av_frame_get_buffer(this._dstFrame, LogOffset);
                    if (ret < 0)
                    {
                        result = null;
                        error = "Failed to allocate frame buffer: " + ret.FFErrorToText();
                        return false;
                    }

                    var code = ffmpeg.swr_convert_frame(this._swrCtx, this._dstFrame, &workingFrame);

                    if (code.FFIsError())
                    {
                        // If swr_convert_frame fails, try manual conversion
                        ffmpeg.av_frame_unref(this._dstFrame);

                        var outputSamples = (int)ffmpeg.av_rescale_rnd(
                            ffmpeg.swr_get_delay(this._swrCtx, this._lastSrcSampleRate) + srcNbSamples,
                            this._dstSampleRate,
                            this._lastSrcSampleRate,
                            AVRounding.AV_ROUND_UP);

                        if (outputSamples <= 0)
                        {
                            result = [];
                            error = null;
                            return true;
                        }

                        var bufferSize = outputSamples * this._bytesPerSample * this._dstChannels;
                        var outputBuffer = new byte[bufferSize];

                        fixed (byte* outputPtr = &outputBuffer[0])
                        {
                            byte*[] dstData = new byte*[this._dstChannels];
                            dstData[0] = outputPtr;

                            var sourceChannels = workingFrame.ch_layout.nb_channels;
                            byte*[] srcData = new byte*[sourceChannels];
                            for (uint i = 0; i < sourceChannels; i++)
                            {
                                srcData[i] = workingFrame.data[i];
                            }

                            fixed (byte** dstDataPtr = &dstData[0])
                            fixed (byte** srcDataPtr = &srcData[0])
                            {
                                var convertedSamples = ffmpeg.swr_convert(
                                    this._swrCtx,
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

                                var actualSize = convertedSamples * this._bytesPerSample * this._dstChannels;
                                var finalResult = new byte[actualSize];
                                Array.Copy(outputBuffer, 0, finalResult, 0, actualSize);

                                result = finalResult;
                                error = null;
                                return true;
                            }
                        }
                    }

                    // check if we got valid output samples
                    if (this._dstFrame->nb_samples <= 0)
                    {
                        result = [];
                        error = null;
                        return true;
                    }

                    var size = this._dstFrame->nb_samples * this._bytesPerSample * this._dstFrame->ch_layout.nb_channels;
                    var data = new byte[size];
                    fixed (byte* h = &data[0])
                    {
                        Buffer.MemoryCopy(this._dstFrame->data[0], h, size, size);
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
            finally
            {
                if (needsLayoutCleanup)
                {
                    ffmpeg.av_channel_layout_uninit(&tempChannelLayout);
                }
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
        if (this._disposed)
        {
            return;
        }

        var dstFrame = this._dstFrame;
        ffmpeg.av_frame_free(&dstFrame);

        var swrCtx = this._swrCtx;
        ffmpeg.swr_free(&swrCtx);

        this._disposed = true;
    }
}
