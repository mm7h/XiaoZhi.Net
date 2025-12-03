using Microsoft.SemanticKernel;
using System;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class PrivateProvider
    {
        private Kernel? _kernel;
        private IIoTClient? _iotClient;
        private IMcpClient? _mcpClient;
        private IAudioProcessor? _audioProcessor;
        private IAudioPlayerClient? _audioPlayerClient;

        public IAudioDecoder? AudioDecoder { get; private set; }
        public IVad? Vad { get; private set; }
        public IAsr? Asr { get; private set; }
        public ILlm? Llm { get; private set; }
        public ITts? Tts { get; private set; }
        public IAudioResampler? AudioResampler { get; private set; }
        public IAudioEncoder? AudioEncoder { get; private set; }
        
        public Kernel Kernel => this._kernel ?? throw new InvalidOperationException("Kernel is not set. Please set the kernel before using the session.");
        public bool HasIoT { get; private set; }
        
        public IIoTClient IoTClient => this._iotClient ?? throw new InvalidOperationException("IoTClient is not set. Please set the iot client before using the session.");
        
        public IMcpClient McpClient => this._mcpClient ?? throw new InvalidOperationException("MCPClient is not set. Please set the mcp client before using the session.");
        
        public IAudioProcessor AudioProcessor => this._audioProcessor ?? throw new InvalidOperationException("AudioProcessor is not set. Please set the audio processor before using the session.");
        
        public IAudioPlayerClient AudioPlayerClient => this._audioPlayerClient ?? throw new InvalidOperationException("AudioPlayerClient is not set. Please set the audio player client before using the session.");

        public void SetAudioDecoder(IAudioDecoder audioDecoder)
        {
            this.AudioDecoder = audioDecoder;
        }
        public void SetVad(IVad vad)
        {
            this.Vad = vad;
        }
        public void SetAsr(IAsr asr)
        {
            this.Asr = asr;
        }
        public void SetLlm(ILlm llm)
        {
            this.Llm = llm;
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

        public void SetAudioProcessor(IAudioProcessor audioProcessor)
        {
            this._audioProcessor = audioProcessor;
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
            this._audioProcessor?.Dispose();
            this._kernel = null;
        }
    }
}
