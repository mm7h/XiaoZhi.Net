using Microsoft.Extensions.Logging;
using SherpaOnnx;
using System;
using System.IO;

namespace XiaoZhi.Net.Server.Providers.VAD.Sherpa
{
    internal class Silero : BaseSherpaVad<Silero>, IVad
    {
        public Silero(ILogger<Silero> logger) : base(logger)
        {
        }

        public override string ModelName => nameof(Silero);
        public override bool Build(ModelSetting modelSetting)
        {
            try
            {
                if (!this.CheckModelExist())
                {
                    return false;
                }
                VadModelConfig vadModelConfig = new VadModelConfig();
                vadModelConfig.SileroVad.Model = Path.Combine(this.ModelFileFoler, "model.onnx");

                if (this.Build(vadModelConfig, modelSetting))
                {
                    this.Logger.LogInformation("Builded the {providerType} model: {modelName}", this.ProviderType, this.ModelName);
                    return true;
                }
                else
                    return false;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Invalid model settings for {providerType}: {modelName}", this.ProviderType, this.ModelName);
                return false;
            }

        }

        
    }
}
