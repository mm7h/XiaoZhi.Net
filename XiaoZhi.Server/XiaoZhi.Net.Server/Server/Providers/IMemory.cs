using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Entities;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IMemory : IProvider
    {
        Task<bool> AppendDialogue(string deviceId, string sessionId, Dialogue dialogue);
        Task<IEnumerable<Dialogue>> GetDialogues(string deviceId, string sessionId);
    }
}
