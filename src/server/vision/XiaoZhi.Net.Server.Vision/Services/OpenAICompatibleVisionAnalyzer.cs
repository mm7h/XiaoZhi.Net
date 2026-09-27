using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using XiaoZhi.Net.Server.Vision.Abstractions;
using XiaoZhi.Net.Server.Vision.Abstractions.Common;

namespace XiaoZhi.Net.Server.Vision.Services;

/// <summary>
/// 使用 OpenAI 兼容接口调用视觉语言模型。
/// </summary>
internal sealed class OpenAICompatibleVisionAnalyzer : IVisionAnalyzer
{
    private readonly VisionModelOptions _options;
    private readonly IChatClient _chatClient;

    public OpenAICompatibleVisionAnalyzer(VisionServerOptions options)
    {
        this._options = options.Model;
        OpenAIClient client = new(new ApiKeyCredential(this._options.ApiKey), new OpenAIClientOptions { Endpoint = new Uri(this._options.Endpoint) });
        this._chatClient = client.GetChatClient(this._options.ModelName).AsIChatClient();
    }

    public async Task<string> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken)
    {
        string question = string.IsNullOrWhiteSpace(this._options.ResponseInstruction)
            ? request.Question
            : string.Concat(request.Question, Environment.NewLine, this._options.ResponseInstruction);

        ChatMessage message = new(ChatRole.User,
            new List<AIContent>
            {
                new TextContent(question),
                new DataContent(request.Image, request.MediaType)
            });

        ChatResponse response = await this._chatClient.GetResponseAsync([message], cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            throw new InvalidOperationException("The vision model returned an empty response.");
        }

        return response.Text;
    }
}
