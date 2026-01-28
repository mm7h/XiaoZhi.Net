using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace XiaoZhi.Net.Server.Resources.OnnxModels
{
    internal abstract class BaseOnnxModel<TLogger> : BaseResource<TLogger, ModelSetting>
    {
        public BaseOnnxModel(ILogger<TLogger> logger) : base(logger)
        {

        }
        public override string ResourceName => "onnx model";
        public abstract string ModelType { get; }
        public abstract string ModelName { get; }
        protected string ModelFileFoler => Path.Combine(Environment.CurrentDirectory, "models", this.ModelType, this.ConvertToKebabCase(this.ModelName));

        protected bool CheckModelExist()
        {
            string modelFilePath = Path.Combine(this.ModelFileFoler, "model.onnx");
            bool exist = File.Exists(modelFilePath);
            if (!exist)
            {
                this.Logger.LogError("Cannot found the onnx model file in path: {modelFilePath}.", modelFilePath);
            }
            return exist;
        }
        private string ConvertToKebabCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return Regex.Replace(input, "(?<!^)([A-Z])", "-$1").ToLower();
        }
    }
}
