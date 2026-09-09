namespace XiaoZhi.Net.PerformanceTest.Execution;

internal enum ConversationPhase
{
    Initial,
    AwaitingDetectAudio,
    AwaitingDetectStop,
    StreamingInput,
    AwaitingInputAudio,
    AwaitingConversationStop,
    Completed
}
