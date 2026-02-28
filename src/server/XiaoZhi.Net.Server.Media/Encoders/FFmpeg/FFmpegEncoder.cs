using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Media.Utilities.Extensions;

namespace XiaoZhi.Net.Server.Media.Encoders.FFmpeg
{
    internal class FFmpegEncoder : IAudioEncoder
    {
        private readonly ILogger _logger;

        public FFmpegEncoder(ILogger<FFmpegEncoder> logger)
        {
            this._logger = logger;
        }

        public Task<bool> EncodeAsync(string outputPath, float[] audioData, int sampleRate, int channels, int bitRate = 128000)
        {
            return Task.Run(() => this.EncodeAudio(outputPath, audioData, sampleRate, channels, bitRate));
        }

        private unsafe bool EncodeAudio(string outputPath, float[] audioData, int sampleRate, int channels, int bitRate)
        {
            AVFormatContext* formatContext = null;
            AVCodecContext* codecContext = null;
            AVStream* stream = null;
            SwrContext* swrContext = null;

            try
            {
                string extension = Path.GetExtension(outputPath).ToLowerInvariant();
                string formatName = this.GetFormatName(extension);
                AVCodecID codecId = this.GetCodecId(extension);

                int ret = ffmpeg.avformat_alloc_output_context2(&formatContext, null, formatName, outputPath);
                if (ret < 0)
                {
                    throw new InvalidOperationException($"avformat_alloc_output_context2 failed: {ret.FFErrorToText()}");
                }

                AVCodec* codec = ffmpeg.avcodec_find_encoder(codecId);
                if (codec == null)
                {
                    throw new InvalidOperationException($"Encoder not found for codec: {codecId}");
                }

                stream = ffmpeg.avformat_new_stream(formatContext, codec);
                if (stream == null)
                {
                    throw new InvalidOperationException("Failed to create new stream");
                }

                codecContext = ffmpeg.avcodec_alloc_context3(codec);
                if (codecContext == null)
                {
                    throw new InvalidOperationException("Failed to allocate codec context");
                }

                codecContext->bit_rate = bitRate;

                AVSampleFormat* supportedSampleFmt = null;
                ret = ffmpeg.avcodec_get_supported_config(null, codec, AVCodecConfig.AV_CODEC_CONFIG_SAMPLE_FORMAT, 0, (void**)&supportedSampleFmt, null);
                codecContext->sample_fmt = (ret >= 0 && supportedSampleFmt != null) ? *supportedSampleFmt : AVSampleFormat.AV_SAMPLE_FMT_FLTP;

                codecContext->sample_rate = sampleRate;

                if (channels == 1)
                {
                    ffmpeg.av_channel_layout_default(&codecContext->ch_layout, 1);
                }
                else
                {
                    ffmpeg.av_channel_layout_default(&codecContext->ch_layout, 2);
                }

                codecContext->time_base = new AVRational { num = 1, den = sampleRate };

                if ((formatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
                {
                    codecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
                }

                ret = ffmpeg.avcodec_open2(codecContext, codec, null);
                if (ret < 0)
                {
                    throw new InvalidOperationException($"avcodec_open2 failed: {ret.FFErrorToText()}");
                }

                ret = ffmpeg.avcodec_parameters_from_context(stream->codecpar, codecContext);
                if (ret < 0)
                {
                    throw new InvalidOperationException($"avcodec_parameters_from_context failed: {ret.FFErrorToText()}");
                }

                ret = ffmpeg.avio_open(&formatContext->pb, outputPath, ffmpeg.AVIO_FLAG_WRITE);
                if (ret < 0)
                {
                    throw new InvalidOperationException($"avio_open failed: {ret.FFErrorToText()}");
                }

                ret = ffmpeg.avformat_write_header(formatContext, null);
                if (ret < 0)
                {
                    throw new InvalidOperationException($"avformat_write_header failed: {ret.FFErrorToText()}");
                }

                swrContext = ffmpeg.swr_alloc();
                if (swrContext == null)
                {
                    throw new InvalidOperationException("Failed to allocate SwrContext");
                }

                AVChannelLayout srcLayout = new AVChannelLayout();
                AVChannelLayout dstLayout = new AVChannelLayout();

                ffmpeg.av_channel_layout_default(&srcLayout, channels);
                ffmpeg.av_channel_layout_copy(&dstLayout, &codecContext->ch_layout);

                ffmpeg.swr_alloc_set_opts2(&swrContext,
                    &dstLayout, codecContext->sample_fmt, codecContext->sample_rate,
                    &srcLayout, AVSampleFormat.AV_SAMPLE_FMT_FLT, sampleRate, 0, null);

                ret = ffmpeg.swr_init(swrContext);
                if (ret < 0)
                {
                    throw new InvalidOperationException($"swr_init failed: {ret.FFErrorToText()}");
                }

                int frameSize = codecContext->frame_size > 0 ? codecContext->frame_size : 1024;
                int totalSamples = audioData.Length / channels;
                int processedSamples = 0;
                long pts = 0;

                while (processedSamples < totalSamples)
                {
                    int currentFrameSize = Math.Min(frameSize, totalSamples - processedSamples);

                    AVFrame* frame = ffmpeg.av_frame_alloc();
                    if (frame == null)
                    {
                        throw new InvalidOperationException("Failed to allocate input frame");
                    }

                    frame->nb_samples = currentFrameSize;
                    frame->format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLT;
                    ffmpeg.av_channel_layout_copy(&frame->ch_layout, &srcLayout);
                    frame->sample_rate = sampleRate;

                    ret = ffmpeg.av_frame_get_buffer(frame, 0);
                    if (ret < 0)
                    {
                        ffmpeg.av_frame_free(&frame);
                        throw new InvalidOperationException($"av_frame_get_buffer (input) failed: {ret.FFErrorToText()}");
                    }

                    float* frameData = (float*)frame->data[0];
                    for (int i = 0; i < currentFrameSize * channels; i++)
                    {
                        frameData[i] = audioData[processedSamples * channels + i];
                    }

                    AVFrame* convertedFrame = ffmpeg.av_frame_alloc();
                    if (convertedFrame == null)
                    {
                        ffmpeg.av_frame_free(&frame);
                        throw new InvalidOperationException("Failed to allocate converted frame");
                    }

                    convertedFrame->nb_samples = currentFrameSize;
                    convertedFrame->format = (int)codecContext->sample_fmt;
                    ffmpeg.av_channel_layout_copy(&convertedFrame->ch_layout, &dstLayout);
                    convertedFrame->sample_rate = codecContext->sample_rate;

                    ret = ffmpeg.av_frame_get_buffer(convertedFrame, 0);
                    if (ret < 0)
                    {
                        ffmpeg.av_frame_free(&frame);
                        ffmpeg.av_frame_free(&convertedFrame);
                        throw new InvalidOperationException($"av_frame_get_buffer (converted) failed: {ret.FFErrorToText()}");
                    }

                    ret = ffmpeg.swr_convert_frame(swrContext, convertedFrame, frame);
                    ffmpeg.av_frame_free(&frame);

                    if (ret < 0)
                    {
                        ffmpeg.av_frame_free(&convertedFrame);
                        throw new InvalidOperationException($"swr_convert_frame failed: {ret.FFErrorToText()}");
                    }

                    convertedFrame->pts = pts;
                    pts += currentFrameSize;

                    ret = ffmpeg.avcodec_send_frame(codecContext, convertedFrame);
                    ffmpeg.av_frame_free(&convertedFrame);

                    if (ret < 0)
                    {
                        throw new InvalidOperationException($"avcodec_send_frame failed: {ret.FFErrorToText()}");
                    }

                    this.ReceiveAndWritePackets(codecContext, formatContext, stream);

                    processedSamples += currentFrameSize;
                }

                ffmpeg.avcodec_send_frame(codecContext, null);
                this.ReceiveAndWritePackets(codecContext, formatContext, stream);

                ffmpeg.av_write_trailer(formatContext);

                return true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Audio encoding failed: {ex.Message}", ex);
            }
            finally
            {
                if (swrContext != null)
                {
                    SwrContext* tempSwrContext = swrContext;
                    ffmpeg.swr_free(&tempSwrContext);
                }

                if (codecContext != null)
                {
                    AVCodecContext* tempCodecContext = codecContext;
                    ffmpeg.avcodec_free_context(&tempCodecContext);
                }

                if (formatContext != null)
                {
                    if (formatContext->pb != null)
                    {
                        ffmpeg.avio_closep(&formatContext->pb);
                    }
                    AVFormatContext* tempFormatContext = formatContext;
                    ffmpeg.avformat_free_context(tempFormatContext);
                }
            }
        }

        private unsafe void ReceiveAndWritePackets(AVCodecContext* codecContext, AVFormatContext* formatContext, AVStream* stream)
        {
            AVPacket* packet = ffmpeg.av_packet_alloc();
            if (packet == null)
            {
                return;
            }

            try
            {
                while (true)
                {
                    int ret = ffmpeg.avcodec_receive_packet(codecContext, packet);
                    if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                    {
                        break;
                    }
                    if (ret < 0)
                    {
                        break;
                    }

                    packet->stream_index = stream->index;
                    ffmpeg.av_packet_rescale_ts(packet, codecContext->time_base, stream->time_base);

                    ffmpeg.av_interleaved_write_frame(formatContext, packet);
                    ffmpeg.av_packet_unref(packet);
                }
            }
            finally
            {
                AVPacket* tempPacket = packet;
                ffmpeg.av_packet_free(&tempPacket);
            }
        }

        private string GetFormatName(string extension)
        {
            return extension switch
            {
                ".mp3" => "mp3",
                ".aac" => "adts",
                ".flac" => "flac",
                ".wav" => "wav",
                ".ogg" => "ogg",
                ".m4a" => "ipod",
                ".pcm" => "s16le",
                _ => "mp3"
            };
        }

        private AVCodecID GetCodecId(string extension)
        {
            return extension switch
            {
                ".mp3" => AVCodecID.AV_CODEC_ID_MP3,
                ".aac" => AVCodecID.AV_CODEC_ID_AAC,
                ".flac" => AVCodecID.AV_CODEC_ID_FLAC,
                ".wav" => AVCodecID.AV_CODEC_ID_PCM_S16LE,
                ".ogg" => AVCodecID.AV_CODEC_ID_VORBIS,
                ".m4a" => AVCodecID.AV_CODEC_ID_AAC,
                ".pcm" => AVCodecID.AV_CODEC_ID_PCM_S16LE,
                _ => AVCodecID.AV_CODEC_ID_MP3
            };
        }
    }
}
