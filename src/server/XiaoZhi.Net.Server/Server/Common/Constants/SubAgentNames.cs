namespace XiaoZhi.Net.Server.Common.Constants
{
    internal static class SubAgentNames
    {
        public const string InputAgent = "InputAgent";
        public const string IntentAgent = "IntentAgent";
        // IntentLlm 三步流水线
        public const string IntentDetectionAgent = "IntentDetectionAgent";
        public const string FunctionCallAgent = "FunctionCallAgent";
        public const string IntentResponseAgent = "IntentResponseAgent";
        public const string ChatAgent = "ChatAgent";
        public const string OutputAgent = "OutputAgent";
    }
}
