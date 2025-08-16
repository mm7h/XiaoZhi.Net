using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IPunctuation : IProvider<ModelSetting>
    {
        Task<string> AppendPunctuationAsync( string message, CancellationToken token);
    }
}
