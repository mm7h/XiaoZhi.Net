using Serilog;
using SherpaOnnx;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers.Punctuation
{
    internal class CtTransformer : BaseProvider, IPunctuation
    {
        private OfflinePunctuation? _offlinePunctuation;
        private readonly SemaphoreSlim _punctuationConvertSlim = new SemaphoreSlim(1, 1);
        public CtTransformer(XiaoZhiConfig config, ILogger logger) : base(config.PunctuationSetting, logger)
        {
        }
        public CtTransformer(ModelSetting punctuationSetting, ILogger logger) : base(punctuationSetting, logger)
        {
        }
        public override string ProviderType => "punctuation";

        public override bool Build()
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
                this.Logger.Information("Builded the {providerType} model: {modelName}", this.ProviderType, this.ModelName);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.Error(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
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
            catch (OperationCanceledException ex)
            {
                this.Logger.Warning("User canceled the job for {providerType}.", this.ProviderType);
                throw ex;
            }
            catch (Exception ex)
            {
                this.Logger.Error(ex, "Unexpected error(s) for {providerType}.", this.ProviderType);
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
