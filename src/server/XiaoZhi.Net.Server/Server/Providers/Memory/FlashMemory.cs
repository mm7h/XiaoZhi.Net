using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Store;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Common.Models;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server.Providers.Memory
{
    internal class FlashMemory : BaseProvider<FlashMemory, ModelSetting>, IMemory
    {
        private readonly IStore _store;
        public FlashMemory(ILogger<FlashMemory> logger) : base(logger)
        {
            this._store = new DefaultMemoryStore();
        }
        public override string ModelName => nameof(FlashMemory);
        public override string ProviderType => "memory";
        public override bool Build(ModelSetting modelSetting)
        {
            this.Logger.LogInformation(Lang.FlashMemory_Build_Built, this.ProviderType, this.ModelName);
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
