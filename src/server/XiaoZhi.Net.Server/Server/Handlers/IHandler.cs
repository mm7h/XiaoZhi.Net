using System;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Handlers
{
    internal interface IHandler : IDisposable
    {
        bool Builded { get; }
        bool Build(PrivateProvider privateProvider);
        IBizSendOutter SendOutter { get; set; }
    }
}
