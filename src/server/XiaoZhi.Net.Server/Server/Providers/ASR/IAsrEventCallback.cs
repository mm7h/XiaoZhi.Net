namespace XiaoZhi.Net.Server.Providers.ASR
{
    internal interface IAsrEventCallback
    {
        void OnSpeechTextConverted(long turnId, bool success, string text);
    }
}
