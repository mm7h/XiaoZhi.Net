namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Enums
{
    /// <summary>
    /// Defines the event type which determines the event of the message.
    /// </summary>
    internal enum EventType : int
    {
        // Default event, applicable for scenarios not using events or not requiring event transmission,
        // or for scenarios using events, non-zero values can be used to validate event legitimacy
        None = 0,

        // 1 ~ 49 for upstream Connection events
        StartConnection = 1,
        StartTask = 1, // Alias of "StartConnection"
        FinishConnection = 2,
        FinishTask = 2, // Alias of "FinishConnection"

        // 50 ~ 99 for downstream Connection events
        // Connection established successfully
        ConnectionStarted = 50,
        TaskStarted = 50, // Alias of "ConnectionStarted"
        // Connection failed (possibly due to authentication failure)
        ConnectionFailed = 51,
        TaskFailed = 51, // Alias of "ConnectionFailed"
        // Connection ended
        ConnectionFinished = 52,
        TaskFinished = 52, // Alias of "ConnectionFinished"

        // 100 ~ 149 for upstream Session events
        StartSession = 100,
        CancelSession = 101,
        FinishSession = 102,

        // 150 ~ 199 for downstream Session events
        SessionStarted = 150,
        SessionCanceled = 151,
        SessionFinished = 152,
        SessionFailed = 153,

        // Usage events
        UsageResponse = 154,
        ChargeData = 154, // Alias of "UsageResponse"

        // 200 ~ 249 for upstream general events
        TaskRequest = 200,
        UpdateConfig = 201,

        // 250 ~ 299 for downstream general events
        AudioMuted = 250,

        // 300 ~ 349 for upstream TTS events
        SayHello = 300,

        // 350 ~ 399 for downstream TTS events
        TTSSentenceStart = 350,
        TTSSentenceEnd = 351,
        TTSResponse = 352,
        TTSEnded = 359,
        PodcastRoundStart = 360,
        PodcastRoundResponse = 361,
        PodcastRoundEnd = 362,

        // 450 ~ 499 for downstream ASR events
        ASRInfo = 450,
        ASRResponse = 451,
        ASREnded = 459,

        // 500 ~ 549 for upstream dialogue events
        // (Ground-Truth-Alignment) text for speech synthesis
        ChatTTSText = 500,

        // 550 ~ 599 for downstream dialogue events
        ChatResponse = 550,
        ChatEnded = 559,

        // 650 ~ 699 for downstream dialogue events
        // Events for source (original) language subtitle
        SourceSubtitleStart = 650,
        SourceSubtitleResponse = 651,
        SourceSubtitleEnd = 652,

        // Events for target (translation) language subtitle
        TranslationSubtitleStart = 653,
        TranslationSubtitleResponse = 654,
        TranslationSubtitleEnd = 655
    }
}
