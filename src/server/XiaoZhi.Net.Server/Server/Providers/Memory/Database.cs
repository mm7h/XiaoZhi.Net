using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Dtos;

namespace XiaoZhi.Net.Server.Providers.Memory
{
    internal class Database : BaseProvider, IMemory
    {
        public Database(XiaoZhiConfig config, ILogger logger) : this(config.MemorySetting, logger)
        {
        }
        public Database(ModelSetting memorySetting, ILogger logger) : base(memorySetting, logger)
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
