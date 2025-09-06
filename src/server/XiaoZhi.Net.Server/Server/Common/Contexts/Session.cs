using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal sealed class Session
    {
        private long _isAudioProcessing;
        private CancellationTokenSource _sessionCts = null!;
        private Kernel? _kernel;
        private IIoTClient? _iotClient;
        private IMcpClient? _mcpClient;

        private readonly object _lock = new object();
        private bool _isCanceling = false;
        private DateTime _cancelCoolingTime = DateTime.Now;

        public Session(string sessionId, string deviceId, string authToken, IPEndPoint userEndPoint, IBizSendOutter sendOutter)
        {
            this.SessionId = sessionId;
            this.DeviceId = deviceId;
            this.AuthToken = authToken;
            this.EndPoint = userEndPoint;
            this.SendOutter = sendOutter;
            this.AudioSetting = new AudioSetting();
            this.AudioPacketContext = new AudioPacket();
            this.VadStatusContext = new VadStatus();
            this.HandlerPipeline = new HandlerPipeline(this);
            this.Dialogues = new LinkedList<Dialogue>();
            this.CreateCancellationTokenSource();
        }

        public string SessionId { get; }
        public string DeviceId { get; }
        public string AuthToken { get; }
        public AudioSetting AudioSetting { get; }
        public IPEndPoint EndPoint { get; }
        public ListenMode ListenMode { get; set; }
        public AudioPacket AudioPacketContext { get; }
        public VadStatus VadStatusContext { get; }
        public CancellationToken SessionCtsToken => this._sessionCts.Token;
        public HandlerPipeline HandlerPipeline { get; }
        public IBizSendOutter SendOutter { get; }
        [DisallowNull]
        public Kernel Kernel => this._kernel ?? throw new InvalidOperationException("Kernel is not set. Please set the kernel before using the session.");
        public ICollection<Dialogue> Dialogues { get; }
        public PrivateProvider? PrivateProvider { get; set; }
        public bool IsDeviceBinded { get; set; }
        public string? BindCode { get; set; }
        public DateTime LastActivityTime { get; private set; }
        public bool HasIoT { get; private set; }
        [DisallowNull]
        public IIoTClient IoTClient => this._iotClient ?? throw new InvalidOperationException("IoTClient is not set. Please set the iot client before using the session.");
        [DisallowNull]
        public IMcpClient McpClient => this._mcpClient ?? throw new InvalidOperationException("MCPClient is not set. Please set the mcp client before using the session.");
        public IAudioPlayerClient? AudioPlayerClient { get; private set; }
        public bool CloseAfterChat { get; set; }

        public bool IsIdle => Interlocked.Read(ref _isAudioProcessing) == 1;

        public bool ShouldIgnore()
        {
            lock (_lock)
            {
                return this._isCanceling && DateTime.Now < this._cancelCoolingTime;
            }
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
            this.AudioPlayerClient = audioPlayer;
        }

        public void SetListenMode(string mode)
        {
            switch (mode.ToLower())
            {
                default:
                case "auto":
                    this.ListenMode = ListenMode.Auto;
                    break;
                case "manual":
                    this.ListenMode = ListenMode.Manual;
                    break;
                case "realtime":
                    this.ListenMode = ListenMode.Realtime;
                    break;
            }
        }

        public void ManualStart()
        {
            this.Reset();
            this.VadStatusContext.HaveVoice = true;
            this.VadStatusContext.VoiceStop = false;
        }

        public void ManualStop()
        {
            this.VadStatusContext.HaveVoice = true;
            this.VadStatusContext.VoiceStop = true;
        }

        public void RejectIncomingAudio()
        {
            Interlocked.Exchange(ref _isAudioProcessing, 0);
        }
        public void AcceptIncomingAudio()
        {
            Interlocked.Exchange(ref _isAudioProcessing, 1);
        }

        public void Reset()
        {
            this.AcceptIncomingAudio();
            this.VadStatusContext.Reset();
            this.AudioPacketContext.Reset();
        }
        public void Abort()
        {
            this._sessionCts.Cancel();
            this._sessionCts.Dispose();
            lock (_lock)
            {
                this._isCanceling = true;
                this._cancelCoolingTime = DateTime.Now.AddSeconds(3);
            }
            this.CreateCancellationTokenSource();
        }

        public void RefreshLastActivityTime()
        {
            this.LastActivityTime = DateTime.Now;
        }

        public void Release()
        {
            this.Reset();
            this.AudioPacketContext.Release();
            this._sessionCts.Cancel();
            this.HandlerPipeline.Release();
            this._sessionCts.Dispose();
        }

        private void CreateCancellationTokenSource()
        {
            this._sessionCts = new CancellationTokenSource();
            this._sessionCts.Token.Register(async () =>
            {
                await Task.Delay(3000);
                lock (_lock)
                {
                    this._isCanceling = false;
                    this.Reset();
                }
            });
        }

        public Workflow<TData> ToWorkflow<TData>(TData data)
        {
            return new Workflow<TData>(this, data);
        }
        public override string ToString()
        {
            return $"DeviceId: {DeviceId}, SessionId: {SessionId}";
        }
    }
}
