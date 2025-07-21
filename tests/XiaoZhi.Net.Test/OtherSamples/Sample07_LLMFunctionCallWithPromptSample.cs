using Flurl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ModelContextProtocol.Client;
using OpenAI.Chat;
using System.ClientModel;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample07_LLMFunctionCallWithPromptSample
    {
        public static async Task Run()
        {
            await TestPromptSample();
        }

        static async Task TestPromptSample()
        {
            try
            {
                string endPoint = "https://open.bigmodel.cn/api/paas/v4/";
                string apiKey = Environment.GetEnvironmentVariable("OPEN_AI_API_KEY", EnvironmentVariableTarget.User)!;
                string chatModel = "glm-4-flash";

                IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
                IServiceCollection services = kernelBuilder.Services;
                services.AddOpenAIChatCompletion(chatModel, new Uri(endPoint), apiKey, orgId: "Xiao Zhi");

                var kernel = kernelBuilder.Build();
                var (command, arguments) = GetCommandAndArguments();

                var clientTransport = new StdioClientTransport(new()
                {
                    Name = "Demo Server",
                    Command = command,
                    Arguments = arguments,
                });

                await using var mcpClient = await McpClientFactory.CreateAsync(clientTransport);

                var tools = await mcpClient.ListToolsAsync();
                foreach (var tool in tools)
                {
                    Console.WriteLine($"Connected to server with tools: {tool.Name}");
                }
#pragma warning disable SKEXP0001
                var functions = tools.Select(aiFunction => aiFunction.AsKernelFunction()).ToList();
#pragma warning restore SKEXP0001
               // kernel.Plugins.AddFromFunctions("Tools", functions);

                Func<string, Task<string>> openUrlMethod = (url) =>
                {
                    Console.WriteLine("function调用：" + url);
                    return Task.FromResult("打开网站成功");
                };

                var parameters = new List<KernelParameterMetadata>
                {
                    new KernelParameterMetadata("url")
                    {
                        Description = "要打开的网址",
                        IsRequired = true,
                        ParameterType = typeof(string)
                    }
                };
                var openUrlFunction = KernelFunctionFactory.CreateFromMethod(openUrlMethod,
                       functionName: "open_url",
                       description: "打开网站",
                       parameters: parameters,
                       returnParameter: new KernelReturnParameterMetadata
                       {
                           Description = "打开网站的结果",
                           ParameterType = typeof(string)
                       }
                );

                kernel.ImportPluginFromFunctions("DeviceMcpFunctions", new List<KernelFunction> { openUrlFunction });

                var chatCompletionOptions = new OpenAIPromptExecutionSettings
                {
                    Temperature = 0.5f,
                    MaxTokens = 80,
                    //ResponseFormat = ChatResponseFormat.CreateTextFormat(),
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                };

                IChatCompletionService chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
                var clientResult = await chatCompletionService.GetChatMessageContentAsync("帮我打开百度网站", chatCompletionOptions, kernel);
                Console.WriteLine(clientResult.Content);


                //PromptExecutionSettings promptExecutionSettings = new PromptExecutionSettings
                //{
                //    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                //};

                //var result = await kernel.InvokePromptAsync("帮我打开百度网站", new(promptExecutionSettings));
                //Console.WriteLine(result);

            }
            catch (Exception ex)
            {

                throw;
            }
            finally
            {
                Console.WriteLine("Done");
                Console.ReadLine();
            }
        }

        static (string command, string[] arguments) GetCommandAndArguments()
        {
            return ("dotnet", ["run", "--project", Path.Combine("C:\\Visual_D_Drive\\Projects\\Github\\csharp-sdk-0.3.0-preview.2\\samples\\QuickstartWeatherServer\\../QuickstartWeatherServer")]);
        }
    }
}
