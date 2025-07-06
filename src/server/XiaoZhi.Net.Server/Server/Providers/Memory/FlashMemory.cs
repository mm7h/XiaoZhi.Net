using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using Serilog;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Plugins;
using XiaoZhi.Net.Server.Services;
using XiaoZhi.Net.Server.Store;

namespace XiaoZhi.Net.Server.Providers.Memory
{
    internal sealed class FlashMemory : BaseProvider, IMemory
    {
        private readonly ManageApiClient _manageApiClient;
        private readonly Kernel _kernel;
        private readonly IStore _store;
        public FlashMemory(ManageApiClient manageApiClient, Kernel kernel, XiaoZhiConfig config, ILogger logger) : this(manageApiClient, kernel, config.MemorySetting, logger)
        {
        }
        public FlashMemory(ManageApiClient manageApiClient, Kernel kernel, ModelSetting memorySetting, ILogger logger) : base(memorySetting, logger)
        {
            this._manageApiClient = manageApiClient;
            this._kernel = kernel;
            this._store = new DefaultMemoryStore();
        }
        public override string ProviderType => "memory";
        public override bool Build()
        {
            this.Logger.Information("Builded the default {providerType}: flash memory", this.ProviderType);
            return true;
        }

        public async Task SaveMemoryAsync(string deviceId, string llmModelName, ICollection<Dialogue> dialogues)
        {
            var plugin = this._kernel.ImportPluginFromType<ConversationSummaryPlugin>("ConversationSummary");
            OpenAIPromptExecutionSettings chatCompletionOptions = new OpenAIPromptExecutionSettings
            {
                Temperature = 0.5f,
                MaxTokens = 80,
                ResponseFormat = ChatResponseFormat.CreateTextFormat(),
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(plugin)
            };

            IChatCompletionService chatCompletionService = this._kernel.GetRequiredService<IChatCompletionService>(llmModelName);
            ChatHistory chatHistory = dialogues.Convert2ChatMessages();

            var clientResult = await chatCompletionService.GetChatMessageContentAsync(chatHistory, chatCompletionOptions, this._kernel);



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
