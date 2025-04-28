using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IPunctuation : IProvider
    {
        Task<string> AppendPunctuationAsync( string message, CancellationToken token);
    }
}
