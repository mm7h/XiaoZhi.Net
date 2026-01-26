using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan
{
    internal abstract class BaseHuoshanTTS<TLogger> : BaseProvider<TLogger, ModelSetting>
    {
        private const string LANG_ZH = "zh-CN";
        private const int SAMPLE_RATE = 24000;

        public BaseHuoshanTTS(ILogger<TLogger> logger) : base(logger)
        {

        }

        public override string ProviderType => "tts";
        public string SavePath { get; protected set; } = string.Empty;
        public string SpeakerId { get; protected set; } = string.Empty;
        public int SpeechRate { get; protected set; } = 0;
        public int LoudnessRate { get; protected set; } = 0;
        protected string AudioEncoding { get; set; } = "pcm";
        public bool Save2File { get; protected set; }
        protected ITtsEventCallback? TTSEventCallback { get; set; }
        public int GetTtsSampleRate() => SAMPLE_RATE;

        public void RegisterDevice(string deviceId, string sessionId, ITtsEventCallback callback)
        {
            this.TTSEventCallback = callback;
            this.RegisterDevice(deviceId, sessionId);
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
