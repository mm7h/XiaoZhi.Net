using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Concentus;
using Concentus.Structs;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Providers.AudioCodec
{
    internal class DefaultOpusDecoder : BaseProvider<DefaultOpusDecoder, AudioSetting>, IAudioDecoder
    {

        private IOpusDecoder? _decoder;

        public override string ModelName => nameof(DefaultOpusDecoder);
        public override string ProviderType => "audio codec";
        public int SampleRate { get; private set; }
        public int Channels { get; private set; }
        public int FrameDuration { get; private set; }
        public int FrameSize { get; private set; }
        public DefaultOpusDecoder(ILogger<DefaultOpusDecoder> logger) : base(logger)
        {
        }

        public override bool Build(AudioSetting audioSetting)
        {
            try
            {
                this.SampleRate = audioSetting.SampleRate;
                this.Channels = audioSetting.Channels;
                this.FrameDuration = audioSetting.FrameDuration;
                this.FrameSize = audioSetting.FrameSize;

                this._decoder = OpusCodecFactory.CreateDecoder(audioSetting.SampleRate, audioSetting.Channels);
                this.Logger.LogInformation(Lang.DefaultOpusDecoder_Build_Built, this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.DefaultOpusDecoder_Build_InvalidSettings, this.ProviderType, this.ModelName);
                return false;
            }
        }

        public ValueTask<float[]> DecodeAsync(byte[] opusData, CancellationToken token)
        {
            if (this._decoder == null)
            {
                throw new ArgumentNullException(Lang.DefaultOpusDecoder_DecodeAsync_NotBuilt);
            }
            token.ThrowIfCancellationRequested();
            int frameSizePerChannel = this.FrameSize / this.Channels;
            int decodedSamplesPerChannel = opusData.Length == 0
                ? frameSizePerChannel
                : OpusPacketInfo.GetNumSamples(opusData, this.SampleRate);
            float[] decoded = new float[checked(decodedSamplesPerChannel * this.Channels)];
            int decodedSamples = this._decoder.Decode(opusData, decoded, decodedSamplesPerChannel, false);
            if (decodedSamples != decodedSamplesPerChannel)
            {
                throw new InvalidDataException(string.Format(
                    Lang.DefaultOpusDecoder_DecodeAsync_SampleCountMismatch,
                    decodedSamplesPerChannel,
                    decodedSamples));
            }

            return ValueTask.FromResult(decoded);
        }

        public override void Dispose()
        {
            this._decoder?.ResetState();
            this._decoder?.Dispose();
        }
    }
}
