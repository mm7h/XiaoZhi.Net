using System;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IProvider<TSettings> : IDisposable where TSettings : class
    {
        string ProviderType { get; }
        string ModelName { get; }
        bool IsSherpaModel { get; }
        bool Build(TSettings settings);
        void RegisterDevice(string deviceId, string sessionId);
        void UnregisterDevice(string deviceId, string sessionId);
    }
}
