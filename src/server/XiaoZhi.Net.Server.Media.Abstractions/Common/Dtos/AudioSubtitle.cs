using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Media.Abstractions.Dtos
{
    public struct AudioSubtitle
    {
        public AudioSubtitle(string sentenceId, AudioType audioType, string subtitleText, Emotion emotion, TtsStatus ttsStatus, DateTime registerTime)
        {
            this.SentenceId = sentenceId;
            this.AudioType = audioType;
            this.SubtitleText = subtitleText;
            this.Emotion = emotion;
            this.TtsStatus = ttsStatus;
            this.RegisterTime = registerTime;
        }

        public string SentenceId { get; } = string.Empty;
        public AudioType AudioType { get; }
        public string SubtitleText { get; } = string.Empty;
        public Emotion Emotion { get; }
        public DateTime RegisterTime { get; }
        public TtsStatus TtsStatus { get; }
    }
}


