using Serilog;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace XiaoZhi.Net.Server.Providers
{
    internal abstract class BaseProvider : IProvider
    {
        public BaseProvider(ILogger logger)
        {
            this.ModelName = string.Empty;
            this.ModelFileFoler = string.Empty;
            this.ModelSetting = default!;
            this.Logger = logger;
        }
        public BaseProvider(ModelSetting modelSetting, ILogger logger)
        {
            this.ModelName = modelSetting.ModelName;
            this.ModelFileFoler = Path.Combine(Environment.CurrentDirectory, "models", this.ProviderType, this.ModelName);
            this.ModelSetting = modelSetting;
            this.Logger = logger;
        }

        public abstract string ProviderType { get; }
        public string ModelName { get; }
        protected string ModelFileFoler { get; }
        protected ModelSetting ModelSetting { get; }
        protected ILogger Logger { get; }

        public abstract bool Initialize();
        public abstract void Dispose();

        protected bool CheckModelExist()
        { 
            string modelFilePath = Path.Combine(this.ModelFileFoler, "model.onnx");
            bool exist = File.Exists(modelFilePath);
            if (!exist)
            {
                this.Logger.Error($"Cannot found the model file in path: {modelFilePath}.");
            }
            return exist;
        }

        protected string ReplaceMacDelimiters(string deviceId, string newDelimiter = "")
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new ArgumentException("Device id cannot be null or empty.", nameof(deviceId));
            }

            return Regex.Replace(deviceId, @"[^a-fA-F0-9]", newDelimiter);
        }
    }
}
