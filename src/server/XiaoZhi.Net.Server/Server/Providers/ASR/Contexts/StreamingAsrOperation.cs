namespace XiaoZhi.Net.Server.Providers.ASR.Contexts
{
    /// <summary>
    /// Describes the lifecycle operation of a streaming ASR utterance.
    /// </summary>
    internal enum StreamingAsrOperation
    {
        Start,
        Audio,
        Finish,
        Abort
    }
}
