using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.RegularExpressions;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Providers
{
    internal abstract class BaseProvider<TLogger, TSettings> : IProvider<TSettings> where TSettings : class
    {
        public BaseProvider(ILogger<TLogger> logger)
        {
            this.Logger = logger;
        }

        public abstract string ProviderType { get; }
        public abstract string ModelName { get; }
        public bool IsSherpaModel => this.CheckIsSherpaModel();
        protected string ModelFileFoler => Path.Combine(Environment.CurrentDirectory, "models", this.ProviderType, this.ConvertToKebabCase(this.ModelName));

        protected ILogger<TLogger> Logger { get; }

        protected string SessionId { get; set; } = string.Empty;
        protected string DeviceId { get; set; } = string.Empty;
        public abstract bool Build(TSettings settings);
        /// <summary>
        /// Updates a provider's supported runtime settings without rebuilding its
        /// model or transport. Providers opt in explicitly.
        /// </summary>
        public virtual bool Rebuild(TSettings settings) => false;
        public abstract void Dispose();

        public virtual void RegisterDevice(string deviceId, string sessionId)
        {
            this.DeviceId = deviceId;
            this.SessionId = sessionId;
            this.Logger.LogInformation(Lang.BaseProvider_RegisterDevice_Registered, this.DeviceId, this.ProviderType, this.SessionId);
        }

        public virtual void UnregisterDevice(string deviceId, string sessionId)
        {
            this.Logger.LogInformation(Lang.BaseProvider_UnregisterDevice_Unregistered, this.DeviceId, this.ProviderType, this.SessionId);
            this.DeviceId = string.Empty;
            this.SessionId = string.Empty;
        }

        public virtual bool CheckDeviceRegistered(string deviceId, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(this.DeviceId) || string.IsNullOrWhiteSpace(this.SessionId))
            {
                this.Logger.LogError(Lang.BaseProvider_CheckDeviceRegistered_NotRegistered, string.IsNullOrWhiteSpace(this.DeviceId) ? "unkonwn" : this.DeviceId, string.IsNullOrWhiteSpace(this.SessionId) ? "unkonwn" : this.SessionId, this.ProviderType);
                return false;
            }
            return true;
        }

        protected bool CheckModelExist()
        {
            return this.CheckModelFiles("model.onnx");
        }

        protected bool CheckModelFiles(params string[] fileNames)
        {
            bool exist = true;
            foreach (string fileName in fileNames)
            {
                string modelFilePath = Path.Combine(this.ModelFileFoler, fileName);
                if (!File.Exists(modelFilePath))
                {
                    this.Logger.LogError(Lang.BaseProvider_CheckModelExist_NotFound, modelFilePath);
                    exist = false;
                }
            }
            return exist;
        }

        protected bool CheckModelDirectories(params string[] directoryNames)
        {
            bool exist = true;
            foreach (string directoryName in directoryNames)
            {
                string directoryPath = Path.Combine(this.ModelFileFoler, directoryName);
                if (!Directory.Exists(directoryPath))
                {
                    this.Logger.LogError(Lang.BaseProvider_CheckModelExist_NotFound, directoryPath);
                    exist = false;
                }
            }
            return exist;
        }

        protected static string GetSessionKey(string deviceId, string sessionId)
        {
            return string.Concat(deviceId, "\u001f", sessionId);
        }

        protected virtual string GenerateId()
        {
            return Guid.NewGuid().ToString("N");
        }

        protected string ReplaceMacDelimiters(string deviceId, string newDelimiter = "")
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new ArgumentException(Lang.BaseProvider_ReplaceMacDelimiters_DeviceIdNull, nameof(deviceId));
            }

            return Regex.Replace(deviceId, @"[^a-fA-F0-9]", newDelimiter);
        }


        private bool CheckIsSherpaModel()
        {
            return SherpaModels.IsSherpaModel(this.ProviderType, this.ModelName);
        }

        private string ConvertToKebabCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            return Regex.Replace(input, "(?<!^)([A-Z])", "-$1").ToLower();
        }
    }
}
