using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using XiaoZhi.Net.Test.Filter;
using XiaoZhi.Net.Test.Plugins;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample08_CloneKernelAndCheckTheMemory
    {
        public static async Task Run()
        {
            await TestTheMemory();
        }

        static async Task TestTheMemory()
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

                kernel.ImportPluginFromType<TimePlugin>();
                kernel.ImportPluginFromType<WeatherPlugin>();

                await InitMCP(kernel);

                List<Kernel> kernels = new List<Kernel>(100_000);
                for (int i = 0; i < 100_000; i++)
                {
                    // Clone the kernel
                    var clonedKernel = kernel.Clone();
                    kernels.Add(clonedKernel);
                }

                var anotherClonedKernel = kernel.Clone();
                anotherClonedKernel.ImportPluginFromType<TimePlugin>("TimePlugin2");
                anotherClonedKernel.ImportPluginFromType<WeatherPlugin>("WeatherPlugin2");

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
        static async Task InitMCP(Kernel kernel)
        {
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
        }
        static (string command, string[] arguments) GetCommandAndArguments()
        {
            return ("dotnet", ["run", "--project", Path.Combine("C:\\Visual_D_Drive\\Projects\\Github\\csharp-sdk-0.3.0-preview.2\\samples\\QuickstartWeatherServer\\../QuickstartWeatherServer")]);
        }
    }
}
