using System;
using System.Collections.Generic;
using System.Text;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IProvider : IDisposable
    {
        string ProviderType { get; }
        string ModelName { get; }
        bool Initialize();
    }
}
