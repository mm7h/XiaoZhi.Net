using Concentus;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace XiaoZhi.Net.Server.Providers.AudioCodec
{
    internal class DefaultOpusEncoder : BaseProvider<DefaultOpusEncoder, AudioSetting>, IAudioEncoder
    {

        private IOpusEncoder? _encoder;
        private SemaphoreSlim _encodeSemaphoreSlim = new SemaphoreSlim(1, 1);

        public override string ModelName => "OpusEncoder";
        public override string ProviderType => "opus audio encoder";
        public int SampleRate { get; private set; }
        public int Channels { get; private set; }
        public int FrameDuration { get; private set; }
        public int FrameSize { get; private set; }
        public DefaultOpusEncoder(ILogger<DefaultOpusEncoder> logger) : base(logger)
        { }

        public override bool Build(AudioSetting audioSetting)
        {
            try
            {
                this.SampleRate = audioSetting.SampleRate;
                this.Channels = audioSetting.Channels;
                this.FrameDuration = audioSetting.FrameDuration;
                this.FrameSize = audioSetting.FrameSize;

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

                if (pcmData.Length < this.FrameSize)
                {
                    float[] paddedData = new float[this.FrameSize];
                    Array.Copy(pcmData, paddedData, pcmData.Length);
                    for (int i = pcmData.Length; i < this.FrameSize; i++)
                    {
                        paddedData[i] = 0.0f;
                    }
                    pcmData = paddedData;
                }

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
