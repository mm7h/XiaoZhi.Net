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
        public IAudioResampler? AudioResampler { get; private set; }
        public IAudioEncoder? AudioEncoder { get; private set; }

        public void SetVad(IVad vad)
        {
            this.Vad = vad;
        }
        public void SetAsr(IAsr asr)
        {
            this.Asr = asr;
        }
        public void SetLlm(string prompt, bool useStreaming, string? summaryMemory, string? llmModelName)
        {
            this.Prompt = prompt;
            this.UseStreaming = useStreaming;
            this.SummaryMemory = summaryMemory;
            this.LlmModelName = llmModelName;
        }
        public void SetTts(ITts tts)
        {
            this.Tts = tts;
        }
        public void SetAudioResampler(IAudioResampler audioResampler)
        {
            this.AudioResampler = audioResampler;
        }
        public void SetAudioEncoder(IAudioEncoder audioEncoder)
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
