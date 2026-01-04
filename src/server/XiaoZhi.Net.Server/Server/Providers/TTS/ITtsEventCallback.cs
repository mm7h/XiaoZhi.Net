using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.TTS
{
    internal interface ITtsEventCallback
    {
        void OnBeforeProcessing(string sentence, bool isFirstSegment, bool isLastSegment);
        void OnProcessing(float[] audioData, bool isFirstFrame, bool isLastFrame);
        void OnProcessed(string sentence, bool isFirstSegment, bool isLastSegment, TtsGenerateResult ttsGenerateResult);
        void OnSentenceStart(string sentence, Emotion emotion, string sentenceId);
        void OnSentenceEnd(string sentence, Emotion emotion, string sentenceId);
    }
}
