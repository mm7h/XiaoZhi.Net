using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class PrivateProvider
    {

        public IVad? Vad { get; private set; }
        public IAsr? Asr { get; private set; }
        public string Prompt { get; private set; } = null!;
        public bool UseStreaming { get; private set; } = true;
        public string? SummaryMemory { get; private set; }
        public string? LlmModelName { get; private set; }
        public ITts? Tts { get; private set; }
        public IAudioEncoder? AudioEncoder { get; private set; }

        public void InitializeVad(IVad vad)
        {
            this.Vad = vad;
        }
        public void InitializeAsr(IAsr asr)
        {
            this.Asr = asr;
        }
        public void InitializeLlm(string prompt, bool useStreaming, string? summaryMemory, string? llmModelName)
        {
            this.Prompt = prompt;
            this.UseStreaming = useStreaming;
            this.SummaryMemory = summaryMemory;
            this.LlmModelName = llmModelName;
        }
        public void InitializeTts(ITts tts)
        {
            this.Tts = tts;
        }
        public void InitializeAudioEncoder(IAudioEncoder audioEncoder)
        {
            this.AudioEncoder = audioEncoder;
        }
        public void Release()
        {
            this.Vad?.Dispose();
            this.Asr?.Dispose();
            this.Tts?.Dispose();
            this.AudioEncoder?.Dispose();
        }
    }
}
