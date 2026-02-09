using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Models;

namespace XiaoZhi.Net.Server.Providers.Memory
{
    internal class Database : BaseProvider<Database, ModelSetting>, IMemory
    {
        public Database(ILogger<Database> logger) : base(logger)
        {
        }
        public override string ModelName => nameof(Database);
        public override string ProviderType => "memory";
        public override bool Build(ModelSetting modelSetting)
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
