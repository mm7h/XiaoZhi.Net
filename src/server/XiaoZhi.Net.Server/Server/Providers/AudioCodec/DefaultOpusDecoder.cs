using Concentus;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Providers.AudioCodec
{
    internal class DefaultOpusDecoder : BaseProvider<DefaultOpusDecoder, AudioSetting>, IAudioDecoder
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

        public override void RegisterDevice(string deviceId, string sessionId)
        {
            // No device-specific registration needed for the default Opus decoder.
        }

        public async Task<float[]> DecodeAsync(byte[] opusData, CancellationToken token)
        {
            if (this._decoder == null)
            {
                throw new ArgumentNullException(Lang.DefaultOpusDecoder_DecodeAsync_NotBuilt);
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
