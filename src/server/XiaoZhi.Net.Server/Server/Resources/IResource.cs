using System;

namespace XiaoZhi.Net.Server.Resources
{
    internal interface IResource<TSettings> : IDisposable where TSettings : class
    {
        string ResourceName { get; }
        bool Load(TSettings settings);
    }
}
