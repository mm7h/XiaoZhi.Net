using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Entities;

namespace XiaoZhi.Net.Server.Providers.Memory
{
    internal class Database : BaseProvider, IMemory
    {
        public Database(XiaoZhiConfig config, ILogger logger) : base(config.MemorySetting, logger)
        {
        }
        public override string ProviderType => "memory";
        public override bool Build()
        {
            throw new NotImplementedException();
        }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }

        public Task<bool> AppendDialogue(string deviceId, string sessionId, Dialogue dialogue)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Dialogue>> GetDialogues(string deviceId, string sessionId)
        {
            throw new NotImplementedException();
        }
    }
}
