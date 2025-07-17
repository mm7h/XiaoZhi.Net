using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
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

                //PromptTemplateConfig promptTemplateConfig = new PromptTemplateConfig("You are a helpful assistant. Answer the question using the provided tools.\n\nQuestion: {question}\n\nTools:\n{tools}\n\nAnswer: {answer}");
                //PromptTemplateConfig promptTemplateConfig = new PromptTemplateConfig("")
                //{
                //    Name = "OpenUrl",
                //    Description = "打开网站",
                //    InputVariables = new List<InputVariable>
                //    {
                //        new InputVariable
                //        {
                //            Name = "url",
                //            IsRequired = true,
                //            JsonSchema = "{\"type\":\"string\",\"format\":\"uri\"}"
                //        }
                //    },
                //};
                Func<string, Task<string>> openUrlMethod = (uri) =>
                {
                    Console.WriteLine("function调用：" + uri);
                    return Task.FromResult("打开网站成功");
                };

                var parameters = new List<KernelParameterMetadata>
                {
                    new KernelParameterMetadata("url")
                    {
                        Description = "要打开的网址",
                        IsRequired = true,
                        Schema = KernelJsonSchema.Parse("{\"type\":\"string\",\"format\":\"uri\"}")
                    }
                };
                var openUrlFunction = KernelFunctionFactory.CreateFromMethod(openUrlMethod,
                       functionName: "OpenUrl",
                       description: "打开网站",
                       parameters: parameters,
                       returnParameter: new KernelReturnParameterMetadata
                       {
                           Description = "打开网站的结果"
                       }
                );

                kernel.ImportPluginFromFunctions("DeviceMcpFunctions", new List<KernelFunction> { openUrlFunction });

                var chatCompletionOptions = new OpenAIPromptExecutionSettings
                {
                    Temperature = 0.5f,
                    MaxTokens = 80,
                    ResponseFormat = ChatResponseFormat.CreateTextFormat(),
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Required(new List<KernelFunction> { openUrlFunction })
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

        static void Test(string toolName, Dictionary<string, object> args, int timeout)
        {

        }
    }
}
