using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Configs;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan
{
    internal abstract class BaseHuoshanTTS<TLogger> : BaseProvider<TLogger, ModelSetting>
    {
        private const string LANG_ZH = "zh-CN";
        private const int SAMPLE_RATE = 24000;

        private readonly IAudioEditor _audioEditor;

        public BaseHuoshanTTS(IAudioEditor audioEditor, ILogger<TLogger> logger) : base(logger)
        {
            this._audioEditor = audioEditor;
        }

        public override string ProviderType => "tts";
        public string SpeakerId { get; protected set; } = string.Empty;
        public int SpeechRate { get; protected set; } = 0;
        public int LoudnessRate { get; protected set; } = 0;
        protected string AudioEncoding { get; set; } = "pcm";
        public AudioSavingConfig? AudioSavingConfig { get; protected set; }
        protected ITtsEventCallback? TTSEventCallback { get; set; }
        public int GetTtsSampleRate() => SAMPLE_RATE;

        public void RegisterDevice(string deviceId, string sessionId, ITtsEventCallback callback)
        {
            this.TTSEventCallback = callback;
            this.RegisterDevice(deviceId, sessionId);
        }

        protected void BuildAudioSavingConfig(ModelSetting modelSetting)
        {
            this.AudioSavingConfig = modelSetting.Config.GetConfigValueOrDefault("FileSavingOption", new AudioSavingConfig(false));
            if (this.AudioSavingConfig.SaveFile && !Directory.Exists(this.AudioSavingConfig.SavePath))
            {
                Directory.CreateDirectory(this.AudioSavingConfig.SavePath);
            }
        }

        protected async Task<bool> SaveAudioFileAsync(string deviceId, string fileName, float[] audioData)
        {
            if (this.AudioSavingConfig is not null && this.AudioSavingConfig.SaveFile)
            {
                fileName = $"{this.ProviderType}_{fileName}.{this.AudioSavingConfig.Format}";
                string savingPath = Path.Combine(this.AudioSavingConfig.SavePath, fileName);
                try
                {
                    bool saved = await this._audioEditor.SaveAudioFileAsync(savingPath, audioData, this.GetTtsSampleRate(), 1, 128000);

                    if (saved)
                    {
                        this.Logger.LogInformation(Lang.BaseHuoshanTTS_SaveAudioFile_FileSaved, fileName, deviceId);
                    }
                    else
                    {
                        this.Logger.LogWarning(Lang.BaseHuoshanTTS_SaveAudioFile_SaveFailed, fileName, deviceId);
                    }
                    return saved;
                }
                catch (Exception ex)
                {
                    this.Logger.LogError(ex, Lang.BaseHuoshanTTS_SaveAudioFile_SaveFailed, fileName, deviceId);
                    return false;
                }

            }
            else
            {
                return true;
            }
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
