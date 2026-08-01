using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions.Dtos;

namespace XiaoZhi.Net.Server.Media.Abstractions
{
    public interface IAudioSubtitleRegister : IDisposable
    {
        void Register(string sentenceId, AudioType audioType, TtsStatus ttsStatus, string subtitleText, Emotion emotion);
        bool GetSubtitle(string sentenceId, out AudioSubtitle subtitle);
        void ClearAll();
    }

}
