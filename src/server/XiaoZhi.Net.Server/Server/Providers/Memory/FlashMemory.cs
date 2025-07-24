using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Services;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server.Providers.Memory
{
    internal sealed class FlashMemory : BaseProvider, IMemory
    {
        private readonly ManageApiClient _manageApiClient;
        private readonly IStore _store;
        public FlashMemory(ManageApiClient manageApiClient, XiaoZhiConfig config, ILogger<FlashMemory> logger) : this(manageApiClient, config.MemorySetting, logger)
        {
        }
        public FlashMemory(ManageApiClient manageApiClient, ModelSetting memorySetting, ILogger logger) : base(memorySetting, logger)
        {
            this._manageApiClient = manageApiClient;
            this._store = new DefaultMemoryStore();
        }
        public override string ProviderType => "memory";
        public override bool Build()
        {
            this.Logger.LogInformation("Builded the default {providerType}: flash memory", this.ProviderType);
            return true;
        }

        public async Task SaveMemoryAsync(string deviceId, string llmModelName, ICollection<Dialogue> dialogues)
        {


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
