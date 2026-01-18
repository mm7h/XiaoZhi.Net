using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Protocol.WebSocket;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Models;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan
{
    internal abstract class HuoshanTTS<TLogger> : BaseProvider<TLogger, ModelSetting>
    {
        private const string LANG_ZH = "zh-CN";

        public HuoshanTTS(ILogger<TLogger> logger) : base(logger)
        {

        }

        protected WebSocketClient? WebSocketClient { get; set; }

        public override string ProviderType => "tts";

        public bool Save2File { get; protected set; }
        public string SavePath { get; protected set; } = string.Empty;
        public string SpeakerId { get; protected set; } = string.Empty;
        public int SpeechRate { get; protected set; } = 0;
        public int LoudnessRate { get; protected set; } = 0;

        protected async Task SendMessage(Message message)
        {
            if (this.WebSocketClient is null)
            {
                return;
            }
            var data = message.Marshal();
            await this.WebSocketClient.SendAsync(data);
        }

        protected string ConvertEmotion(Emotion emotion, string? lang = LANG_ZH)
        {
            bool isZh = !string.IsNullOrEmpty(lang) && lang == LANG_ZH;

            return (isZh, emotion) switch
            {
                (true, Emotion.Neutral) => "neutral",
                (true, Emotion.Happy) => "happy",
                (true, Emotion.Laughing) => "excited",
                (true, Emotion.Funny) => "happy",
                (true, Emotion.Sad) => "sad",
                (true, Emotion.Angry) => "angry",
                (true, Emotion.Crying) => "sad",
                (true, Emotion.Loving) => "lovey-dovey",
                (true, Emotion.Embarrassed) => "shy",
                (true, Emotion.Surprised) => "surprised",
                (true, Emotion.Shocked) => "surprised",
                (true, Emotion.Thinking) => "neutral",
                (true, Emotion.Winking) => "happy",
                (true, Emotion.Cool) => "coldness",
                (true, Emotion.Relaxed) => "tender",
                (true, Emotion.Delicious) => "happy",
                (true, Emotion.Kissy) => "lovey-dovey",
                (true, Emotion.Confident) => "magnetic",
                (true, Emotion.Sleepy) => "depressed",
                (true, Emotion.Silly) => "happy",
                (true, Emotion.Confused) => "neutral",

                (false, Emotion.Neutral) => "neutral",
                (false, Emotion.Happy) => "happy",
                (false, Emotion.Laughing) => "excited",
                (false, Emotion.Funny) => "chat",
                (false, Emotion.Sad) => "sad",
                (false, Emotion.Angry) => "angry",
                (false, Emotion.Crying) => "sad",
                (false, Emotion.Loving) => "affectionate",
                (false, Emotion.Embarrassed) => "chat",
                (false, Emotion.Surprised) => "excited",
                (false, Emotion.Shocked) => "excited",
                (false, Emotion.Thinking) => "chat",
                (false, Emotion.Winking) => "happy",
                (false, Emotion.Cool) => "authoritative",
                (false, Emotion.Relaxed) => "warm",
                (false, Emotion.Delicious) => "happy",
                (false, Emotion.Kissy) => "affectionate",
                (false, Emotion.Confident) => "authoritative",
                (false, Emotion.Sleepy) => "warm",
                (false, Emotion.Silly) => "chat",
                (false, Emotion.Confused) => "chat",

                _ => "neutral"
            };
        }
    }
}
