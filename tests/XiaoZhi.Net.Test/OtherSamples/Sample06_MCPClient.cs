using System.IO.Pipelines;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample06_MCPClient
    {
        public static async Task RunAsync()
        {
            await TestStdioClientAsync();
            await TestSseClientAsync();
        }

        private static async Task TestStdioClientAsync()
        {
            //Console.WriteLine(nameof(TestStdioClient));
            //var transport = new StdioClientTransport(new()
            //{
            //    Name = "Demo Server",
            //    Command = "npx",
            //    Arguments = new List<string> {
            //        "-y",
            //        "@upstash/context7-mcp"
            //    }
            //});

            //await using var mcpClient = await McpClientFactory.CreateAsync(transport);

            //var tools = await mcpClient.ListToolsAsync();
            //foreach (var tool in tools)
            //{
            //    Console.WriteLine($"Connected to server with tools: {tool.Name}");
            //}
        }

        private static async Task TestSseClientAsync()
        {
            //Console.WriteLine(nameof(TestSseClient));
            //string serverUrl = " http://localhost:3000/mcp";

            //var sharedHandler = new SocketsHttpHandler
            //{
            //    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            //    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
            //};
            //var httpClient = new HttpClient(sharedHandler);

            //var transport = new SseClientTransport(new SseClientTransportOptions()
            //{
            //    Endpoint = new Uri(serverUrl),
            //    Name = "Local MCP"
            //}, httpClient);

            //var client = await McpClientFactory.CreateAsync(transport);

            //var tools = await client.ListToolsAsync();
            //foreach (var tool in tools)
            //{
            //    Console.WriteLine($"Connected to server with tools: {tool.Name}");
            //}
        }

        private static async Task Test2Async()
        {
            //var pipIn = new Pipe();
            //var pipOut = new Pipe();

            //var transport = new StreamClientTransport(pipIn.Reader.AsStream(), pipOut.Writer.AsStream());
            //await using var mcpClient = await McpClientFactory.CreateAsync(transport);
        }
    }
}
