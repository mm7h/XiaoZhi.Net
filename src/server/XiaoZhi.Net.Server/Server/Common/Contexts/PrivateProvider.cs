using Microsoft.SemanticKernel;
using System;
using System.Diagnostics.CodeAnalysis;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class PrivateProvider
    {
        private Kernel? _kernel;
        private IIoTClient? _iotClient;
        private IMcpClient? _mcpClient;
        private IAudioMixer? _audioMixer;
        private IAudioPlayerClient? _audioPlayerClient;

        public IVad? Vad { get; private set; }
        public IAsr? Asr { get; private set; }
        public string Prompt { get; private set; } = null!;
        public bool UseStreaming { get; private set; } = true;
        public string? SummaryMemory { get; private set; }
        public string? LlmModelName { get; private set; }
        public ITts? Tts { get; private set; }
        public IAudioResampler? AudioResampler { get; private set; }
        public IAudioEncoder? AudioEncoder { get; private set; }
        [DisallowNull]
        public Kernel Kernel => this._kernel ?? throw new InvalidOperationException("Kernel is not set. Please set the kernel before using the session.");
        public bool HasIoT { get; private set; }
        [DisallowNull]
        public IIoTClient IoTClient => this._iotClient ?? throw new InvalidOperationException("IoTClient is not set. Please set the iot client before using the session.");
        [DisallowNull]
        public IMcpClient McpClient => this._mcpClient ?? throw new InvalidOperationException("MCPClient is not set. Please set the mcp client before using the session.");
        [DisallowNull]
        public IAudioMixer AudioMixer => this._audioMixer ?? throw new InvalidOperationException("AudioMixer is not set. Please set the audio mixer before using the session.");
        [DisallowNull]
        public IAudioPlayerClient AudioPlayerClient => this._audioPlayerClient ?? throw new InvalidOperationException("AudioPlayerClient is not set. Please set the audio player client before using the session.");

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

        public void SetKernel(Kernel kernel)
        {
            this._kernel = kernel;
        }

        public void SetIoTClient(IIoTClient iotClient)
        {
            this._iotClient = iotClient;
            this.HasIoT = true;
        }

        public void SetMcpClient(IMcpClient mcpClient)
        {
            this._mcpClient = mcpClient;
        }

        public void SetAudioPlayerClient(IAudioPlayerClient audioPlayer)
        {
            this._audioPlayerClient = audioPlayer;
        }

        public void SetAudioMixer(IAudioMixer audioMixer)
        {
            this._audioMixer = audioMixer;
        }
        public void Release()
        {
            this.Vad?.Dispose();
            this.Asr?.Dispose();
            this.Tts?.Dispose();
            this.AudioResampler?.Dispose();
            this.AudioEncoder?.Dispose();
            this._iotClient?.Dispose();
            this._mcpClient?.Dispose();
            this._audioPlayerClient?.Dispose();
            this._audioMixer?.Dispose();
            this._kernel = null;
        }
    }
}
