using Serilog;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Entities;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server.Providers.Memory
{
    internal sealed class FlashMemory : BaseProvider, IMemory
    {
        private readonly IStore _store;
        public FlashMemory(XiaoZhiConfig config, ILogger logger) : base(config.MemorySetting, logger)
        {
            this._store = new DefaultMemoryStore();
        }
        public override string ProviderType => "memory";
        public override bool Build()
        {
            this.Logger.Information($"Builded the default {this.ProviderType}: flash memory");
            return true;
        }

        public async Task<bool> AppendDialogue(string deviceId, string sessionId, Dialogue dialogue)
        {
            string key = this.GetStoreKey(deviceId, sessionId);
            if (this._store.Contains(key))
            {
                LinkedList<Dialogue> dialogues = this._store.Get<LinkedList<Dialogue>>(key);
                dialogues.AddLast(dialogue);
                return await Task.FromResult(this._store.Update(key, dialogues));
            }
            else
            {
                LinkedList<Dialogue> dialogues = new LinkedList<Dialogue>();
                dialogues.AddLast(dialogue);
                return await Task.FromResult(this._store.Add(key, dialogues));
            }
        }

        public async Task<IEnumerable<Dialogue>> GetDialogues(string deviceId, string sessionId)
        {
            string key = this.GetStoreKey(deviceId, sessionId);
            return await Task.FromResult(this._store.Get<LinkedList<Dialogue>>(key).OrderBy(d => d.CreateTime));
        }

        public override void Dispose()
        {
            this._store.Dispose();
        }

        private string GetStoreKey(string deviceId, string sessionId)
        {
            return $"{deviceId}_{sessionId}";
        }
    }
}
