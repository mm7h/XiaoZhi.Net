using Concentus;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.AudioCodec
{
    internal class DefaultResampler : BaseProvider<DefaultResampler, ResamplerBuildConfig>, IAudioResampler
    {
        private IResampler? _resampler;
        private SemaphoreSlim _resamplerSemaphoreSlim = new SemaphoreSlim(1, 1);

        public DefaultResampler(ILogger<DefaultResampler> logger) : base(logger)
        {
        }

        public int Channels { get; private set; }
        public int InSampleRate { get; private set; }
        public int OutSampleRate { get; private set; }

        public override string ModelName => nameof(DefaultResampler);
        public override string ProviderType => "audio codec";

        public override bool Build(ResamplerBuildConfig config)
        {
            try
            {
                this.Channels = config.Channels;
                this.InSampleRate = config.InSampleRate;
                this.OutSampleRate = config.OutSampleRate;

                this._resampler = ResamplerFactory.CreateResampler(config.Channels, config.InSampleRate, config.OutSampleRate, 6);

                this.Logger.LogInformation(Lang.DefaultResampler_Build_Built, this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.DefaultResampler_Build_InvalidSettings, this.ProviderType, this.ModelName);
                return false;
            }
        }

        public async Task<(float[], int)> ResampleAsync(float[] inputData, CancellationToken token)
        {
            if (this._resampler == null)
            {
                throw new ArgumentNullException(Lang.DefaultResampler_ResampleAsync_NotBuilt);
            }

            if (this.InSampleRate == this.OutSampleRate)
            {
                return (inputData, inputData.Length);
            }

            // 计算输出缓冲区大小：输出采样率/输入采样率 * 输入长度，向上取整以确保足够空间
            int expectedOutputLength = (int)Math.Ceiling(inputData.Length * ((double)this.OutSampleRate / this.InSampleRate));
            float[] outputData = ArrayPool<float>.Shared.Rent(expectedOutputLength);

            bool acquired = false;
            try
            {
                await this._resamplerSemaphoreSlim.WaitAsync(token);
                acquired = true;

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
                if (acquired)
                {
                    this._resamplerSemaphoreSlim.Release();
                }
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
