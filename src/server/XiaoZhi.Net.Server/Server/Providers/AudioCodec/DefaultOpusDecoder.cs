using Concentus;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers.AudioCodec
{
    internal sealed class DefaultOpusDecoder : BaseProvider, IAudioDecoder
    {

        private IOpusDecoder? _decoder;

        private SemaphoreSlim _decodeSemaphoreSlim = new SemaphoreSlim(1, 1);
        public new string ModelName => "OpusDecoder";
        public override string ProviderType => "opus audio decoder";
        public int SampleRate { get; }
        public int Channels { get; }
        public int FrameDuration { get; }
        public int FrameSize { get; }
        public DefaultOpusDecoder(AudioSetting audioSetting, ILogger<DefaultOpusDecoder> logger) : base(logger)
        {
            this.SampleRate = audioSetting.SampleRate;
            this.Channels = audioSetting.Channels;
            this.FrameDuration = audioSetting.FrameDuration;
            this.FrameSize = audioSetting.FrameSize;
        }

        public override bool Build()
        {
            try
            {
                this._decoder = OpusCodecFactory.CreateDecoder(this.SampleRate, this.Channels);
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
