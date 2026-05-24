using System.ComponentModel;
using System.Text.Json.Serialization;

namespace XiaoZhi.Net.Server.Providers.LLM.Contexts
{
    internal sealed record IntentResult
    {
        [JsonPropertyName("intent_detected")]
        [Description("Whether an intent is detected, true if the user wants to trigger a built-in device function instead of normal conversation")]
        public bool IntentDetected { get; set; }

        [JsonPropertyName("feedback")]
        [Description("Specific feedback for improvements if not approved, empty if approved")]
        public string Feedback { get; set; } = string.Empty;

        [JsonIgnore]
        public string UserMessage { get; set; } = string.Empty;
    }
}