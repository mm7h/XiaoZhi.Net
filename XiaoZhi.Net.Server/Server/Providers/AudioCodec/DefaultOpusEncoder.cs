using OpusSharp.Core;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers.AudioCodec
{
    internal class DefaultOpusEncoder : BaseProvider, IAudioEncoder
    {
        private OpusEncoder? _encoder;
        private SemaphoreSlim _encodesemaphoreSlim = new SemaphoreSlim(1, 1);
        private readonly ITts _tts;

        public new string ModelName => "OpusEncoder";
        public override string ProviderType => "opus audio encoder";
        public int SampleRate { get; private set; }
        public int Channels { get; }
        public int FrameDuration { get; }
        public int FrameSize { get; private set; }
        public DefaultOpusEncoder(ITts tts, AudioSetting audioSetting, ILogger logger) : base(logger)
        {
            this._tts = tts;
            this.Channels = audioSetting.Channels;
            this.FrameDuration = audioSetting.FrameDuration;
        }

        public override bool Initialize()
        {
            try
            {
                this.SampleRate = this._tts.GetTtsSampleRate();
                this.FrameSize = this.SampleRate * this.FrameDuration * this.Channels / 1000;
                this._encoder = new OpusEncoder(this.SampleRate, this.Channels, OpusPredefinedValues.OPUS_APPLICATION_AUDIO);
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

        public async Task<byte[]> EncodeAsync(float[] pcmData, CancellationToken token)
        {
            if (this._encoder == null)
            {
                throw new ArgumentNullException("Please initialize opus provider first.");
            }
            try
            {
                await this._encodesemaphoreSlim.WaitAsync(token);
                var byteData = new byte[4000];
                int encodedLength = _encoder!.Encode(pcmData, pcmData.Length, byteData, byteData.Length);

                byte[] opusBytes = new byte[encodedLength];
                Array.Copy(byteData, opusBytes, encodedLength);

                return opusBytes;
            }
            finally
            {
                this._encodesemaphoreSlim.Release();
            }
        }

        public override void Dispose()
        {
            this._encodesemaphoreSlim.Dispose();
        }
    }
}
