using XiaoZhi.Net.Server.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.TTS
{
    internal interface ITtsEventCallback
    {
        void OnBeforeProcessing(string sentence, bool isFirstSegment, bool isLastSegment);
        void OnProcessing(float[] audioData, bool isFirstFrame, bool isLastFrame);
        void OnPorcessed(string sentence, bool isFirstSegment, bool isLastSegment, TtsGenerateResult ttsGenerateResult);
        void OnSentenceStart(string sentence);
        void OnSentenceEnd(string sentence);
    }
}
