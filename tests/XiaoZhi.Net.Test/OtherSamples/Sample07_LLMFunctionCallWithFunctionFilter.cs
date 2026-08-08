using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ModelContextProtocol.Client;
using OpenAI.Chat;
using XiaoZhi.Net.Test.Filter;
using XiaoZhi.Net.Test.Plugins;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample07_LLMFunctionCallWithFunctionFilter
    {
        public static async Task RunAsync()
        {
            await TestPromptSampleAsync();
        }

        private static async Task TestPromptSampleAsync()
        {
            try
            {
                string endPoint = "https://open.bigmodel.cn/api/paas/v4/";
                string apiKey = Environment.GetEnvironmentVariable("OPEN_AI_API_KEY", EnvironmentVariableTarget.User)!;
                string chatModel = "glm-4-flash";

                IKernelBuilder kernelBuilder = Kernel.CreateBuilder();

                IServiceCollection services = kernelBuilder.Services;
                services.AddOpenAIChatCompletion(chatModel, new Uri(endPoint), apiKey, orgId: "Xiao Zhi");

                services.AddSingleton<IFunctionInvocationFilter, PluginSelectionFilter>();

                var kernel = kernelBuilder.Build();

                kernel.ImportPluginFromType<PlayMusic>(nameof(PlayMusic));
                kernel.ImportPluginFromType<TimePlugin>(nameof(TimePlugin));
                kernel.ImportPluginFromType<WeatherPlugin>(nameof(WeatherPlugin));
                //kernel.Plugins.AddFromType<WeatherPlugin>(nameof(WeatherPlugin));

                //await InitMCPAsync();
                InitCustomFunctions(kernel);

                var chatCompletionOptions = new OpenAIPromptExecutionSettings
                {
                    Temperature = 0.5f,
                    MaxTokens = 80,
                    ResponseFormat = ChatResponseFormat.CreateTextFormat(),
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                };

                IChatCompletionService chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
                var clientResult = await chatCompletionService.GetChatMessageContentAsync("我本地目录下有哪些音乐文件", chatCompletionOptions, kernel);
                Console.WriteLine(clientResult.Content);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
            finally
            {
                Console.WriteLine("Done");
                Console.ReadLine();
            }
        }

        private static async Task InitMCPAsync()
        {
            //            var (command, arguments) = GetCommandAndArguments();

            //            var clientTransport = new StdioClientTransport(new()
            //            {
            //                Name = "Demo Server",
            //                Command = command,
            //                Arguments = arguments,
            //            });

            //            await using var mcpClient = await McpClientFactory.CreateAsync(clientTransport);

            //            var tools = await mcpClient.ListToolsAsync();
            //            foreach (var tool in tools)
            //            {
            //                Console.WriteLine($"Connected to server with tools: {tool.Name}");
            //            }
            //#pragma warning disable SKEXP0001
            //            var functions = tools.Select(aiFunction => aiFunction.AsKernelFunction()).ToList();
            //#pragma warning restore SKEXP0001

            //            kernel.Plugins.AddFromFunctions("Tools", functions);
        }

        private static void InitCustomFunctions(Kernel kernel)
        {
            //Func<string, Task<string>> openUrlMethod = (url) =>
            //{
            //    Console.WriteLine("function调用：" + url);
            //    return Task.FromResult("打开网站成功");
            //};

            Action tempMethod = () => { Console.WriteLine("调用了"); };

            KernelParameterMetadata kernelParameter = new KernelParameterMetadata("url")
            {
                Description = "要打开的网址",
                IsRequired = true,
                Schema = KernelJsonSchema.Parse("{\"description\": \"要打开的url地址\",\"type\": \"string\"}")
            };

            var parameters = new List<KernelParameterMetadata>
                {
                    //new KernelParameterMetadata("url")
                    //{
                    //    Description = "要打开的网址",
                    //    IsRequired = true,
                    //    ParameterType = typeof(string),
                    //    DefaultValue = "",
                    //}
                    kernelParameter
                };

            IDictionary<string, object?> additionalMetadataDic = new Dictionary<string, object?>
                {
                    { "sessionId", "my-test-id" }
                };

            KernelFunctionFromMethodOptions openUrlFunctionOptions = new KernelFunctionFromMethodOptions
            {
                FunctionName = "open_url",
                Description = "打开指定网址",
                Parameters = parameters,
                ReturnParameter = new KernelReturnParameterMetadata
                {
                    Description = "打开网站的结果",
                    ParameterType = typeof(string),
                },
                AdditionalMetadata = new ReadOnlyDictionary<string, object?>(additionalMetadataDic)
            };

            var openUrlFunction = KernelFunctionFactory.CreateFromMethod(tempMethod, openUrlFunctionOptions);

            var deviceMcpPlugins = kernel.ImportPluginFromFunctions("DeviceMcpFunctions", new[] { openUrlFunction });
            //if (kernel.Plugins.Remove(deviceMcpPlugins))
            //{
            //    Console.WriteLine("删除deviceMcpPlugins成功");
            //}
            //else
            //{
            //    Console.WriteLine("删除deviceMcpPlugins失败");
            //}
        }

        private static (string command, string[] arguments) GetCommandAndArguments()
        {
            return ("dotnet", ["run", "--project", Path.Combine("D:\\MyDotNet\\XiaoZhi AI\\model context protocol 0.3.0\\samples\\QuickstartWeatherServer\\../QuickstartWeatherServer")]);
        }
    }
}
