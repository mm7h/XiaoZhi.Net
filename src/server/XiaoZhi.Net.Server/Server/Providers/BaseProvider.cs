using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace XiaoZhi.Net.Server.Providers
{
    internal abstract class BaseProvider<TSettings> : IProvider<TSettings> where TSettings : class
    {
        public BaseProvider(ILogger logger)
        {
            this.Logger = logger;
        }

        public abstract string ProviderType { get; }
        public abstract string ModelName { get; }
        protected string ModelFileFoler => Path.Combine(Environment.CurrentDirectory, "models", this.ProviderType, this.ModelName);

        protected ILogger Logger { get; }

        public abstract bool Build(TSettings settings);
        public abstract void Dispose();

        protected bool CheckModelExist()
        {
            string modelFilePath = Path.Combine(this.ModelFileFoler, "model.onnx");
            bool exist = File.Exists(modelFilePath);
            if (!exist)
            {
                this.Logger.LogError("Cannot found the model file in path: {modelFilePath}.", modelFilePath);
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
