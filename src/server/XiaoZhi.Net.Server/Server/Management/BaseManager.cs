using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Management
{
    internal abstract class BaseManager : IDisposable
    {

        protected BaseManager(IServiceProvider serviceProvider, XiaoZhiConfig config, ILogger logger)
        {
            this.ServiceProvider = serviceProvider;
            this.Logger = logger;
            this.Config = config;
        }

        public IServiceProvider ServiceProvider { get; }
        public ILogger Logger { get; }
        public XiaoZhiConfig Config { get; }

        public abstract bool BuildComponent();

        public static string ConvertToKebabCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return input;
            }

            return Regex.Replace(input, "(?<!^)([A-Z])", "-$1").ToLower();
        }

        public virtual Task OnSessionConnectedAsync(Session session)
        {
            return Task.CompletedTask;
        }

        public virtual Task OnSessionClosedAsync(Session session)
        {
            return Task.CompletedTask;
        }

        public virtual Task<bool> OnSessionPropertyInitializingAsync(Session session)
        {
            return Task.FromResult(true);
        }

        public virtual Task OnSessionPropertyInitializedAsync(Session session, JsonObject helloMessage)
        {
            return Task.CompletedTask;
        }

        protected ModelSetting GetSelectedSetting(string selectedModelType, XiaoZhiConfig config)
        {
            string selectedModel = config.SelectedSettings[selectedModelType];
            Dictionary<string, string> setting = config.ConfiguredSettings[selectedModelType][selectedModel];

            return new ModelSetting
            {
                ModelName = selectedModel,
                Config = setting
            };
        }

        public virtual void Dispose()
        { }
    }
}
