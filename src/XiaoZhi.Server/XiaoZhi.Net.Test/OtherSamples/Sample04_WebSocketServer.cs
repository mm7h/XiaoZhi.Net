using WebSocketSharp.Server;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample04_WebSocketServer
    {
        public static void Run()
        {
            TestWebSocketServer();
        }

        static void TestWebSocketServer()
        {
            // 创建 WebSocket 服务端实例
            WebSocketServer server = new WebSocketServer("ws://0.0.0.0:4530")
            {
                KeepClean = true
            };

            // 添加一个简单的 WebSocket 行为
            server.AddWebSocketService("/xiaozhi/v1/", () => new EchoBehavior());

            // 启动服务
            server.Start();
            Console.WriteLine($"WebSocket 服务器已启动，地址: ws://{server.Address.ToString()}:{server.Port}/xiaozhi/v1/");

            Console.WriteLine("按任意键停止服务器...");
            Console.ReadKey();

            // 停止服务
            server.Stop();
            Console.WriteLine("WebSocket 服务器已停止");
        }
    }


    // 定义 WebSocket 行为
    file class EchoBehavior : WebSocketBehavior
    {
        protected override void OnOpen()
        {
            IDictionary<string, string> headers = new Dictionary<string, string>();
            foreach (string key in this.Context.Headers.AllKeys)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    headers.Add(key.ToLower(), this.Context.Headers[key] ?? string.Empty);
                }
            }
            if (headers.TryGetValue("device-id", out string? deviceId))
            {
                Console.WriteLine(deviceId ?? "** no deviceId **");
            }
            Console.WriteLine("有连接上线...");
        }
        protected override void OnMessage(WebSocketSharp.MessageEventArgs e)
        {
            Console.WriteLine($"服务端收到: {e.Data}");
            // 将收到的消息原样返回
            Send($"服务端收到: {e.Data}");
        }
    }
}
