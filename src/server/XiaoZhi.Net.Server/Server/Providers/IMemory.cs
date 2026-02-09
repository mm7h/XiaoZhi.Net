using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Models;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IMemory : IProvider<ModelSetting>
    {
        Task<bool> AppendDialogue(string deviceId, string sessionId, Dialogue dialogue);
        Task<IEnumerable<Dialogue>> GetDialogues(string deviceId, string sessionId);
    }
}
