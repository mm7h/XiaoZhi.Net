using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers.Punctuation
{
    internal class CtTransformer : BaseProvider<CtTransformer, ModelSetting>, IPunctuation
    {
        private OfflinePunctuation? _offlinePunctuation;
        private readonly SemaphoreSlim _punctuationConvertSlim = new SemaphoreSlim(1, 1);
        public CtTransformer(ILogger<CtTransformer> logger) : base(logger)
        {
        }
        public override string ModelName => nameof(CtTransformer);
        public override string ProviderType => "punctuation";

        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelExist())
                {
                    return false;
                }
                OfflinePunctuationConfig config = new OfflinePunctuationConfig();
                config.Model.CtTransformer = Path.Combine(this.ModelFileFoler, "model.onnx");

                this._offlinePunctuation = new OfflinePunctuation(config);
                this.Logger.LogInformation("Builded the {providerType} model: {modelName}", this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return false;
            }
        }

        public async Task<string> AppendPunctuationAsync(string message, CancellationToken token)
        {
            if (this._offlinePunctuation == null)
            {
                throw new ArgumentNullException("Please build punctuation provider first.");
            }
            try
            {
                await this._punctuationConvertSlim.WaitAsync(token);
                string result = this._offlinePunctuation.AddPunct(message);
                return result;
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogWarning("User canceled the job for {providerType}.", this.ProviderType);
                throw;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Unexpected error(s) for {providerType}.", this.ProviderType);
                return string.Empty;
            }
            finally
            {
                this._punctuationConvertSlim.Release();
            }
        }

        public override void Dispose()
        {
            this._punctuationConvertSlim.Dispose();
            this._offlinePunctuation?.Dispose();
        }
    }
}
