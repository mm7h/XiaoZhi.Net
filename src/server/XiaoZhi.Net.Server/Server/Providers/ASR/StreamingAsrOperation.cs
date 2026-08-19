namespace XiaoZhi.Net.Server.Providers.ASR
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
