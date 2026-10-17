using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.RegularExpressions;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.I18n;

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
                this.Logger.LogError(Lang.BaseOnnxModel_CheckModelExist_NotFound, modelFilePath);
            }
            return exist;
        }
        private string ConvertToKebabCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return input;
            }

            return Regex.Replace(input, "(?<!^)([A-Z])", "-$1").ToLower();
        }
    }
}
