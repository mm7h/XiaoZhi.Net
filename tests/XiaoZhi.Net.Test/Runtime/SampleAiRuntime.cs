using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace XiaoZhi.Net.Test.Runtime
{
    /// <summary>
    /// Manually configured AI connections used by the standalone samples.
    /// Choose a provider in CreateChatClient and set its API key below before running a networked sample.
    /// </summary>
    internal sealed class SampleAiRuntime : IDisposable
    {
        private const string ZhipuApiKey = "YOUR_ZHIPU_API_KEY";
        private const string DeepSeekApiKey = "YOUR_DEEPSEEK_API_KEY";
        private const string DoubaoApiKey = "YOUR_DOUBAO_API_KEY";
        private const string QwenApiKey = "YOUR_QWEN_API_KEY";

        private SampleAiRuntime(
            IChatClient chatClient,
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
        {
            this.ChatClient = chatClient;
            this.EmbeddingGenerator = embeddingGenerator;
        }

        public IChatClient ChatClient { get; }

        public IEmbeddingGenerator<string, Embedding<float>> EmbeddingGenerator { get; }

        public static SampleAiRuntime Create()
        {
            return new SampleAiRuntime(CreateChatClient(), CreateQwenEmbeddingGenerator());
        }

        public void Dispose()
        {
            (this.ChatClient as IDisposable)?.Dispose();
            (this.EmbeddingGenerator as IDisposable)?.Dispose();
        }

        private static IChatClient CreateChatClient()
        {
            //return CreateZhipuChatClient();
            //return CreateDeepSeekChatClient();
            //return CreateDoubaoChatClient();
            return CreateQwenChatClient();
        }

        private static IChatClient CreateZhipuChatClient()
        {
            const string Endpoint = "https://open.bigmodel.cn/api/paas/v4/";
            const string ModelName = "glm-4-flash";
            return CreateOpenAiCompatibleChatClient(Endpoint, ModelName, ZhipuApiKey, nameof(ZhipuApiKey));
        }

        private static IChatClient CreateDeepSeekChatClient()
        {
            const string Endpoint = "https://api.deepseek.com";
            const string ModelName = "deepseek-v4-flash";
            return CreateOpenAiCompatibleChatClient(Endpoint, ModelName, DeepSeekApiKey, nameof(DeepSeekApiKey));
        }

        private static IChatClient CreateDoubaoChatClient()
        {
            const string Endpoint = "https://ark.cn-beijing.volces.com/api/v3";
            const string ModelName = "doubao-1-5-pro-32k-250115";
            return CreateOpenAiCompatibleChatClient(Endpoint, ModelName, DoubaoApiKey, nameof(DoubaoApiKey));
        }

        private static IChatClient CreateQwenChatClient()
        {
            const string Endpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1";
            const string ModelName = "qwen-flash";
            return CreateOpenAiCompatibleChatClient(Endpoint, ModelName, QwenApiKey, nameof(QwenApiKey));
        }

        private static IEmbeddingGenerator<string, Embedding<float>> CreateQwenEmbeddingGenerator()
        {
            const string Endpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1";
            const string ModelName = "qwen3.7-text-embedding";
            EnsureConfigured(QwenApiKey, nameof(QwenApiKey));

            OpenAIClient client = new(
                new ApiKeyCredential(QwenApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(Endpoint) });
            return client.GetEmbeddingClient(ModelName).AsIEmbeddingGenerator();
        }

        private static IChatClient CreateOpenAiCompatibleChatClient(string endpoint, string modelName, string apiKey, string apiKeyName)
        {
            EnsureConfigured(apiKey, apiKeyName);
            OpenAIClient client = new(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
            return client.GetChatClient(modelName).AsIChatClient();
        }

        private static void EnsureConfigured(string value, string settingName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("YOUR_", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"请在 {nameof(SampleAiRuntime)} 中设置 {settingName}。");
            }
        }
    }
}
