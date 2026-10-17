using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class Session
    {
        private long _audioProcessingTurnId = -1;
        private long _speechRecognitionTurnId = -1;
        private long _textProcessingTurnId = -1;
        private int _closeAfterChatRequested;
        private CancellationTokenSource _sessionCts = null!;
        private readonly IServiceScope _serviceScope;

        private readonly object _lock = new object();
        private volatile bool _isReseting = false;
        private int _isClosing;
        private long _turnId = 0;

        public Session(
            string sessionId,
            string deviceId,
            string authToken,
            IPEndPoint userEndPoint,
            IBizSendOutter sendOutter,
            IServiceScope serviceScope)
        {
            this._serviceScope = serviceScope;
            this.SessionId = sessionId;
            this.DeviceId = deviceId;
            this.AuthToken = authToken;
            this.EndPoint = userEndPoint;
            this.LocalEndPoint = new IPEndPoint(IPAddress.Any, 0);
            this.SendOutter = sendOutter;
            this.AudioSetting = new AudioSetting();
            this.OutputAudioSetting = new AudioSetting();
            this.AudioPacket = new AudioPacket();
            this.HandlerPipeline = new HandlerPipeline();
            this.PrivateProvider = new PrivateProvider(this);
            this.CreateCancellationTokenSource();
            this.PrivateProvider.RegisterCancellationToken();
            this.LoginTime = DateTime.Now;
            this.LastActivityTime = DateTime.Now;
        }

        public event Action<CancellationToken>? SessionCtsTokenChanged;
        public string SessionId { get; }
        public string DeviceId { get; }
        public string AuthToken { get; }
        public AudioSetting AudioSetting { get; }
        public AudioSetting OutputAudioSetting { get; }
        public string? ProtocolVersionHeader { get; set; }
        public TextFontCapability? TextFontCapability { get; set; }
        public IPEndPoint LocalEndPoint { get; private set; }
        public IPEndPoint EndPoint { get; }
        public ListenMode ListenMode { get; set; }
        public AudioPacket AudioPacket { get; set; }
        public CancellationToken SessionCtsToken => this._sessionCts.Token;
        public HandlerPipeline HandlerPipeline { get; }
        public IBizSendOutter SendOutter { get; }
        public IServiceProvider ServiceProvider => this._serviceScope.ServiceProvider;
        public PrivateProvider PrivateProvider { get; }
        public bool IsDeviceBinded { get; set; }
        public string? BindCode { get; set; }
        public DateTime LoginTime { get; }
        public DateTime LastActivityTime { get; private set; }
        public bool CloseAfterChat => Volatile.Read(ref this._closeAfterChatRequested) == 1;
        public long TurnId => Interlocked.Read(ref this._turnId);

        public bool IsAudioProcessing => Interlocked.Read(ref this._audioProcessingTurnId) >= 0;
        public bool IsSpeechRecognizing => Interlocked.Read(ref this._speechRecognitionTurnId) >= 0;
        public bool IsTextProcessing => Interlocked.Read(ref this._textProcessingTurnId) >= 0;

        public bool ShouldIgnore() => this._isReseting;

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
            // 重复 listen start 不能清空正在录制的音频或使在途 ASR 失效。
            if (this.IsSpeechRecognizing || this.IsTextProcessing || (this.AudioPacket.HaveVoice && !this.AudioPacket.VoiceStop))
            {
                return;
            }
            this.Reset();
            this.AudioPacket.HaveVoice = true;
            this.AudioPacket.VoiceStop = false;
        }

        public void ManualStop()
        {
            this.AudioPacket.HaveVoice = true;
            this.AudioPacket.VoiceStop = true;
        }

        public void RejectIncomingAudio()
        {
            this.RejectIncomingAudio(this.TurnId);
        }
        public void RejectIncomingAudio(long turnId)
        {
            Interlocked.Exchange(ref this._audioProcessingTurnId, turnId);
        }
        public void AcceptIncomingAudio()
        {
            Interlocked.Exchange(ref this._audioProcessingTurnId, -1);
        }
        public bool AcceptIncomingAudio(long turnId)
        {
            return Interlocked.CompareExchange(ref this._audioProcessingTurnId, -1, turnId) == turnId;
        }

        public void BeginSpeechRecognition(long turnId)
        {
            Interlocked.Exchange(ref this._speechRecognitionTurnId, turnId);
            this.RejectIncomingAudio(turnId);
        }

        public void CompleteSpeechRecognition(long turnId)
        {
            Interlocked.CompareExchange(ref this._speechRecognitionTurnId, -1, turnId);
        }

        public void BeginTextProcessing(long turnId)
        {
            Interlocked.Exchange(ref this._textProcessingTurnId, turnId);
            this.RejectIncomingAudio(turnId);
        }

        public void CompleteTextProcessing(long turnId)
        {
            Interlocked.CompareExchange(ref this._textProcessingTurnId, -1, turnId);
        }

        public void CompletePlayback()
        {
            // 上一段播放结束时，新输入可能已经进入识别，不能推进它的轮次。
            if (!this.IsSpeechRecognizing && !this.IsTextProcessing && this.AudioPacket.BufferSize == 0)
            {
                this.Reset();
            }
        }

        public bool TryRequestCloseAfterChat()
        {
            if (Interlocked.CompareExchange(ref this._closeAfterChatRequested, 1, 0) != 0)
            {
                return false;
            }

            this.RejectIncomingAudio();
            this.AudioPacket.Reset();
            return true;
        }

        public void Reset()
        {
            Interlocked.Increment(ref this._turnId);
            Interlocked.Exchange(ref this._speechRecognitionTurnId, -1);
            Interlocked.Exchange(ref this._textProcessingTurnId, -1);
            this.AcceptIncomingAudio();
            this.AudioPacket.Reset();
        }
        public void Abort()
        {
            lock (this._lock)
            {
                if (this._isReseting || Volatile.Read(ref this._isClosing) != 0)
                {
                    return;
                }
                this._isReseting = true;
            }
            try
            {
                this._sessionCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                lock (this._lock)
                {
                    if (this._isClosing == 0)
                    {
                        // 旧令牌的取消回调全部结束后再更新，后续文本不能使用已取消的令牌。
                        this._sessionCts.Dispose();
                        this.Reset();
                        this.CreateCancellationTokenSource();
                        try
                        {
                            this.SessionCtsTokenChanged?.Invoke(this._sessionCts.Token);
                        }
                        finally
                        {
                            this._isReseting = false;
                        }
                    }
                }
            }
        }

        public void RefreshLastActivityTime()
        {
            this.LastActivityTime = DateTime.Now;
        }

        public void SetLocalEndPoint(IPEndPoint localEndPoint)
        {
            this.LocalEndPoint = localEndPoint;
        }

        public async Task ReleaseAsync()
        {
            lock (this._lock)
            {
                if (this._isClosing != 0)
                {
                    return;
                }
                this._isClosing = 1;
            }
            try
            {
                this.Reset();
                this.AudioPacket.Release();
                this._sessionCts.Cancel();
                await this.HandlerPipeline.ReleaseAsync();
            }
            finally
            {
                try
                {
                    this.PrivateProvider.Release();
                }
                finally
                {
                    try
                    {
                        this._sessionCts.Dispose();
                    }
                    finally
                    {
                        this._serviceScope.Dispose();
                    }
                }
            }
        }

        private void CreateCancellationTokenSource()
        {
            this._sessionCts = new CancellationTokenSource();
        }

        public override string ToString()
        {
            return $"DeviceId: {this.DeviceId}, SessionId: {this.SessionId}";
        }
    }
}
