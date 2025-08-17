using Concentus;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers.AudioCodec
{
    internal sealed class DefaultOpusDecoder : BaseProvider<DefaultOpusDecoder, AudioSetting>, IAudioDecoder
    {

        private IOpusDecoder? _decoder;

        private SemaphoreSlim _decodeSemaphoreSlim = new SemaphoreSlim(1, 1);
        public override string ModelName => "OpusDecoder";
        public override string ProviderType => "opus audio decoder";
        public int SampleRate { get; private set; }
        public int Channels { get; private set; }
        public int FrameDuration { get; private set; }
        public int FrameSize { get; private set; }
        public DefaultOpusDecoder(ILogger<DefaultOpusDecoder> logger) : base(logger)
        {
        }

        [MemberNotNullWhen(true, nameof(SampleRate), nameof(Channels), nameof(FrameDuration), nameof(FrameSize))]
        public override bool Build(AudioSetting audioSetting)
        {
            try
            {
                this.SampleRate = audioSetting.SampleRate;
                this.Channels = audioSetting.Channels;
                this.FrameDuration = audioSetting.FrameDuration;
                this.FrameSize = audioSetting.FrameSize;

                this._decoder = OpusCodecFactory.CreateDecoder(audioSetting.SampleRate, audioSetting.Channels);
                this.Logger.LogInformation("Builded the default {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return false;
            }
        }

        public async Task<float[]> DecodeAsync(byte[] opusData, CancellationToken token)
        {
            if (this._decoder == null)
            {
                throw new ArgumentNullException("Please build opus provider first.");
            }
            try
            {
                await this._decodeSemaphoreSlim.WaitAsync(token);
                var decoded = new float[this.FrameSize];
                var decodedSamples = _decoder.Decode(opusData, decoded, this.FrameSize, false);

                return decoded;
            }
            finally
            {
                this._decodeSemaphoreSlim.Release();
            }
        }

        public override void Dispose()
        {
            this._decoder?.ResetState();
            this._decoder?.Dispose();
            this._decodeSemaphoreSlim.Dispose();
        }
    }
}
