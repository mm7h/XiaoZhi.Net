namespace XiaoZhi.Net.Server.Providers.LLM.Contexts
{
    internal sealed class WorkflowOutputs
    {
        public string ResponseText { get; set; } = string.Empty;

        public bool HandledByIntent { get; set; }

        public string Source { get; set; } = string.Empty;
    }
}