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
        public override string ProviderType => "punctuation";

        public override bool Initialize()
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
                this.Logger.Information($"Initialized the {this.ProviderType} model: {this.ModelName}");
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.Debug(ex, $"Invalid model settings for {this.ProviderType}: {ModelName}");
                this.Logger.Error($"Invalid model settings for {this.ProviderType}: {ModelName}");
                return false;
            }
        }

        public async Task<string> AppendPunctuationAsync( string message, CancellationToken token)
        {
            if (this._offlinePunctuation == null)
            {
                throw new ArgumentNullException("Please initialize punctuation provider first.");
            }
            try
            {
                await this._punctuationConvertSlim.WaitAsync(token);
                string result = this._offlinePunctuation.AddPunct(message);
                return result;
            }
            catch (OperationCanceledException ex)
            {
                this.Logger.Warning($"User canceled the job for {this.ProviderType}.");
                throw ex;
            }
            catch (Exception ex)
            {
                this.Logger.Debug(ex, $"Unexpected error(s): {ex.Message}.");
                this.Logger.Error($"Unexpected error(s) for {this.ProviderType}.");
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
