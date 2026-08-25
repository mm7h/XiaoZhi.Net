using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Providers;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class PrivateProvider
    {
        private IIoTClient? _iotClient;
        private IMcpClient? _mcpClient;
        private IAudioProcessor? _audioProcessor;
        private IAudioPlayerClient? _audioPlayerClient;
        private CancellationTokenSource? _providerCts;
        private readonly Dictionary<string, FunctionToolRegistration> _functionToolRegistrations;
        private readonly List<PrivateFunctionTool> _privateFunctionTools;
        private readonly Session _session;
        /// <summary>MCP 客户端工具列表加载完成的异步信号，未启用 MCP 时为 null</summary>
        private TaskCompletionSource? _mcpClientReadyTcs;

        public PrivateProvider(Session session)
        {
            this._session = session;
            this.DeviceId = session.DeviceId;
            this.SessionId = session.SessionId;
            this.FunctionTools = new List<AITool>();
            this._functionToolRegistrations = new Dictionary<string, FunctionToolRegistration>(StringComparer.OrdinalIgnoreCase);
            this._privateFunctionTools = [];
        }
        public string DeviceId { get; }
        public string SessionId { get; }
        public IAudioDecoder? AudioDecoder { get; private set; }
        public IVad? Vad { get; private set; }
        public IAsr? Asr { get; private set; }
        public ILlm? Llm { get; private set; }
        public ITts? Tts { get; private set; }
        /// <summary>Resamples client input PCM to the internal VAD/ASR format.</summary>
        public IAudioResampler? InputAudioResampler { get; private set; }
        /// <summary>Resamples server output PCM to the device playback format.</summary>
        public IAudioResampler? OutputAudioResampler { get; private set; }
        public IAudioEncoder? AudioEncoder { get; private set; }
        public List<AITool> FunctionTools { get; private set; }

        public bool HasIoT { get; private set; }

        public IIoTClient? IoTClient => this._iotClient;

        public IMcpClient? McpClient => this._mcpClient;

        public IAudioProcessor? AudioProcessor => this._audioProcessor;

        public IAudioPlayerClient? AudioPlayerClient => this._audioPlayerClient;

        public List<PrivateFunctionTool> PrivateFunctionTools => this._privateFunctionTools;

        public CancellationToken Token { get; private set; }

        public void AddFunctionToolRegistration(PrivateFunctionTool privateFunctionTool, FunctionToolRegistration registration)
        {
            this._privateFunctionTools.Add(privateFunctionTool);
            this.AddFunctionToolRegistration(registration);
        }

        public void AddFunctionToolRegistration(FunctionToolRegistration registration)
        {
            this.FunctionTools.Add(registration.Function);
            this._functionToolRegistrations[registration.Function.Name] = registration;
        }

        public bool TryGetFunctionToolRegistration(string functionName, out FunctionToolRegistration? registration)
        {
            return this._functionToolRegistrations.TryGetValue(functionName, out registration);
        }

        public void RegisterCancellationToken()
        {
            this._providerCts = CancellationTokenSource.CreateLinkedTokenSource(this._session.SessionCtsToken);
            this.Token = this._providerCts.Token;
            this._session.SessionCtsTokenChanged += this.OnSessionCtsTokenChanged;
        }

        private void OnSessionCtsTokenChanged(CancellationToken newToken)
        {
            var oldCts = this._providerCts;
            try
            {
                oldCts?.Cancel();
                oldCts?.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
            this._providerCts = CancellationTokenSource.CreateLinkedTokenSource(newToken);
            this.Token = this._providerCts.Token;
        }

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
        public void SetOutputAudioResampler(IAudioResampler audioResampler)
        {
            this.OutputAudioResampler = audioResampler;
        }
        public void SetInputAudioResampler(IAudioResampler audioResampler)
        {
            this.InputAudioResampler = audioResampler;
        }
        public void SetAudioEncoder(IAudioEncoder audioEncoder)
        {
            this.AudioEncoder = audioEncoder;
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

        /// <summary>在 BuildMCP 启动前调用，标记 MCP 工具列表尚未就绪</summary>
        public void SetMcpClientPending()
        {
            this._mcpClientReadyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>MCP 工具列表全部加载完毕后调用，放行等待方</summary>
        public void SetMcpClientReady()
        {
            this._mcpClientReadyTcs?.TrySetResult();
        }

        /// <summary>用于 await 等待 MCP 工具就绪；未启用 MCP 时为 null</summary>
        public Task? McpClientReadyTask => this._mcpClientReadyTcs?.Task;

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
            if (this._session is not null)
            {
                this._session.SessionCtsTokenChanged -= this.OnSessionCtsTokenChanged;
            }
            try
            {
                this._providerCts?.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
            if (this.Vad is not null && !this.Vad.IsSherpaModel)
            {
                this.Vad.Dispose();
            }
            if (this.Asr is not null && !this.Asr.IsSherpaModel)
            {
                this.Asr.Dispose();
            }
            if (this.Tts is not null && !this.Tts.IsSherpaModel)
            {
                this.Tts.Dispose();
            }

            this.InputAudioResampler?.Dispose();
            this.OutputAudioResampler?.Dispose();
            this.AudioEncoder?.Dispose();
            this._iotClient?.Dispose();
            this._mcpClient?.Dispose();
            this._audioPlayerClient?.Dispose();
            this._audioProcessor?.Dispose();
            this.FunctionTools.Clear();
            this._functionToolRegistrations.Clear();
            this._privateFunctionTools.Clear();
        }
    }
}
