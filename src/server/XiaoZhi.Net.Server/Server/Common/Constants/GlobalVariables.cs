namespace XiaoZhi.Net.Server.Common.Constants
{
    internal static class GlobalVariables
    {
        public const string ServerName = "Xiao Zhi .Net Server";
        public const int MaxFunctionCallDepth = 5;

        // Client audio may use a different Opus rate. These are the canonical
        // PCM properties used after ingress resampling by VAD and every ASR provider.
        public const int AudioProcessingSampleRate = 16000;
        public const int AudioProcessingChannels = 1;
        public const int AudioProcessingBitsPerSample = 16;
        public const int StreamingAsrPreRollMilliseconds = 600;
        public const int StreamingAsrMaxQueuedAudioMilliseconds = 10000;

        public const string ChatAgentType = "chat";
        public const string RagAgentType = "rag";
        public const string VisionAgentType = "vision";
    }
}
