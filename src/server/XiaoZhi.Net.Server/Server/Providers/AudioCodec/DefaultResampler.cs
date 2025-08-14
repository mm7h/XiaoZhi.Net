using Concentus;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers.AudioCodec
{
    internal class DefaultResampler : BaseProvider, IAudioResampler
    {
        private IResampler? _resampler;
        private SemaphoreSlim _resamplerSemaphoreSlim = new SemaphoreSlim(1, 1);

        public DefaultResampler(int channels, int inSampleRate, int outSampleRate, ILogger logger) : base(logger)
        {
            this.Channels = channels;
            this.InSampleRate = inSampleRate;
            this.OutSampleRate = outSampleRate;
        }

        public int Channels { get; }
        public int InSampleRate { get; }
        public int OutSampleRate { get; }

        public override string ProviderType => "default audio resampler";
        public override bool Build()
        {
            try
            {
                this._resampler = ResamplerFactory.CreateResampler(this.Channels, this.InSampleRate, this.OutSampleRate, 6);

                this.Logger.LogInformation("Built the default {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return false;
            }
        }

        public async Task<(float[], int)> ResampleAsync(float[] inputData, CancellationToken token)
        {
            if (this._resampler == null)
            {
                throw new ArgumentNullException("Please build resampler provider first.");
            }

            // 计算输出缓冲区大小：输出采样率/输入采样率 * 输入长度，向上取整以确保足够空间
            int expectedOutputLength = (int)Math.Ceiling(inputData.Length * ((double)this.OutSampleRate / this.InSampleRate));
            float[] outputData = ArrayPool<float>.Shared.Rent(expectedOutputLength);

            try
            {
                await this._resamplerSemaphoreSlim.WaitAsync(token);

                int inLen = inputData.Length / this.Channels;
                int outLen = expectedOutputLength;

                // 进行重采样
                this._resampler.ProcessInterleaved(inputData, ref inLen, outputData, ref outLen);

                float[] result = new float[outLen];
                Array.Copy(outputData, result, outLen);

                return (result, outLen);
            }
            finally
            {
                this._resamplerSemaphoreSlim.Release();
                ArrayPool<float>.Shared.Return(outputData);
            }
        }

        public override void Dispose()
        {
            this._resampler?.ResetMem();
            this._resampler?.Dispose();
            this._resamplerSemaphoreSlim.Dispose();
        }
    }
}
