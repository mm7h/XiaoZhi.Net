using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Protocol.WebSocket;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Models;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan
{
    internal abstract class HuoshanTTS<TLogger> : BaseProvider<TLogger, ModelSetting>
    {
        public HuoshanTTS(ILogger<TLogger> logger) : base(logger)
        {
            
        }

        protected WebSocketClient? WebSocketClient { get; set; }

        public override string ProviderType => "tts";

        

        protected async Task SendMessage(Message message)
        {
            if (this.WebSocketClient is null)
            {
                return;
            }
            var data = message.Marshal();
            await this.WebSocketClient.SendAsync(data);
        }
    }
}
