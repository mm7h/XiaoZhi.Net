namespace XiaoZhi.Net.Server.Providers.ASR
{
    internal interface IAsrEventCallback
    {
        void OnSpeechTextConverted(bool success, string text);
    }
}
