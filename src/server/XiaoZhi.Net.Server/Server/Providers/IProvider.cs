using System;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IProvider<TSettings> : IDisposable where TSettings : class
    {
        string ProviderType { get; }
        string ModelName { get; }
        bool Build(TSettings settings);
        void RejsterDevice(string deviceId, string sessionId);
    }
}
