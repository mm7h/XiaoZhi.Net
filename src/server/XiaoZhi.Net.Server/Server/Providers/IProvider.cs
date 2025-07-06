using System;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IProvider : IDisposable
    {
        string ProviderType { get; }
        string ModelName { get; }
        bool Build();
    }
}
