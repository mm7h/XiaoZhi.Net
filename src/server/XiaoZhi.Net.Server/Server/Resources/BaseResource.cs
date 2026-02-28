using Microsoft.Extensions.Logging;

namespace XiaoZhi.Net.Server.Resources
{
    internal abstract class BaseResource<TLogger, TSettings> : IResource<TSettings> where TSettings : class
    {
        public BaseResource(ILogger<TLogger> logger)
        {
            this.Logger = logger;
        }
        public abstract string ResourceName { get; }
        protected ILogger<TLogger> Logger { get; }
        public abstract bool Load(TSettings settings);
        public abstract void Dispose();
    }
}
