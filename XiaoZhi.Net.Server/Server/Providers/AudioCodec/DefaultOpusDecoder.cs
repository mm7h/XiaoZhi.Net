using OpusSharp.Core;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers.AudioCodec
{
    internal sealed class DefaultOpusDecoder : BaseProvider, IAudioDecoder
    {

        private OpusDecoder? _decoder;

        private SemaphoreSlim _decodesemaphoreSlim = new SemaphoreSlim(1, 1);
        public new string ModelName => "OpusDecoder";
        public override string ProviderType => "opus audio decoder";
        public int SampleRate { get; }
        public int Channels { get; }
        public int FrameDuration { get; }
        public int FrameSize { get; }
        public DefaultOpusDecoder(AudioSetting audioSetting, ILogger logger) : base(logger)
        {
            this.SampleRate = audioSetting.SampleRate;
            this.Channels = audioSetting.Channels;
            this.FrameDuration = audioSetting.FrameDuration;
            this.FrameSize = this.SampleRate * this.FrameDuration * this.Channels / 1000;
        }

        public override bool Initialize()
        {
            try
            {
                this._decoder = new OpusDecoder(this.SampleRate, this.Channels);
                this.Logger.Information($"Initialized the default {this.ProviderType}: {this.ModelName}");
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.Debug(ex, $"Invalid model settings for {this.ProviderType}: {this.ModelName}");
                this.Logger.Error($"Invalid model settings for {this.ProviderType}: {this.ModelName}");
                return false;
            }
        }

        public async Task<float[]> DecodeAsync(byte[] opusData, CancellationToken token)
        {
            if (this._decoder == null)
            {
                throw new ArgumentNullException("Please initialize opus provider first.");
            }
            try
            {
                await this._decodesemaphoreSlim.WaitAsync(token);
                var decoded = new float[this.FrameSize];
                var decodedSamples = _decoder.Decode(opusData, opusData.Length, decoded, this.FrameSize, false);

                return decoded;
            }
            finally
            {
                this._decodesemaphoreSlim.Release();
            }
        }

        public override void Dispose()
        {
            this._decodesemaphoreSlim.Dispose();
        }
    }
}
