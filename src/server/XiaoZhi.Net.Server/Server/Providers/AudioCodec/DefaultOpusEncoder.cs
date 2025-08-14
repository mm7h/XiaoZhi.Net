using Concentus;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers.AudioCodec
{
    internal class DefaultOpusEncoder : BaseProvider, IAudioEncoder
    {
        private const int DEFAULT_SAMPLE_RATE = 24_000;

        private IOpusEncoder? _encoder;
        private SemaphoreSlim _encodeSemaphoreSlim = new SemaphoreSlim(1, 1);

        public new string ModelName => "OpusEncoder";
        public override string ProviderType => "opus audio encoder";
        public int SampleRate { get; private set; }
        public int Channels { get; }
        public int FrameDuration { get; }
        public int FrameSize { get; private set; }
        public DefaultOpusEncoder(AudioSetting audioSetting, ILogger<DefaultOpusEncoder> logger) : base(logger)
        {
            this.SampleRate = DEFAULT_SAMPLE_RATE;
            this.Channels = audioSetting.Channels;
            this.FrameDuration = audioSetting.FrameDuration;
        }
        public DefaultOpusEncoder(int sampleRate, AudioSetting audioSetting, ILogger logger) : base(logger)
        {
            this.SampleRate = sampleRate;
            this.Channels = audioSetting.Channels;
            this.FrameDuration = audioSetting.FrameDuration;
        }
        public override bool Build()
        {
            try
            {
                this.FrameSize = this.SampleRate * this.FrameDuration * this.Channels / 1000;
                this._encoder = OpusCodecFactory.CreateEncoder(this.SampleRate, this.Channels, Concentus.Enums.OpusApplication.OPUS_APPLICATION_AUDIO);
                this.Logger.LogInformation("Builded the default {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return false;
            }
        }

        public async Task<byte[]> EncodeAsync(float[] pcmData, CancellationToken token)
        {
            if (this._encoder == null)
            {
                throw new ArgumentNullException("Please build opus provider first.");
            }
            byte[] byteData = ArrayPool<byte>.Shared.Rent(4000);
            try
            {
                await this._encodeSemaphoreSlim.WaitAsync(token);

                int encodedLength = this._encoder!.Encode(pcmData, pcmData.Length, byteData, byteData.Length);

                byte[] opusBytes = new byte[encodedLength];
                Array.Copy(byteData, opusBytes, encodedLength);

                return opusBytes;
            }
            finally
            {
                this._encodeSemaphoreSlim.Release();
                ArrayPool<byte>.Shared.Return(byteData);
            }
        }

        public override void Dispose()
        {
            this._encoder?.ResetState();
            this._encoder?.Dispose();
            this._encodeSemaphoreSlim.Dispose();
        }
    }
}
