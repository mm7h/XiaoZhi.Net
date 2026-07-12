using Microsoft.Extensions.Logging;
using System;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
                return input;

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

        public virtual void Dispose()
        { }
    }
}
