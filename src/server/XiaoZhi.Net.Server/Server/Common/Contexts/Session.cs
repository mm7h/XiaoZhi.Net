using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class Session
    {
        private long _isAudioProcessing;
        private CancellationTokenSource _sessionCts = null!;

        private readonly object _lock = new object();
        private volatile bool _isReseting = false;
        private long _turnId = 0;

        public Session(string sessionId, string deviceId, string authToken, IPEndPoint userEndPoint, IBizSendOutter sendOutter)
        {
            this.SessionId = sessionId;
            this.DeviceId = deviceId;
            this.AuthToken = authToken;
            this.EndPoint = userEndPoint;
            this.SendOutter = sendOutter;
            this.AudioSetting = new AudioSetting();
            this.AudioPacket = new AudioPacket();
            this.HandlerPipeline = new HandlerPipeline();
            this.PrivateProvider = new PrivateProvider();
            this.CreateCancellationTokenSource();
        }

        public event Action<CancellationToken>? SessionCtsTokenChanged;
        public string SessionId { get; }
        public string DeviceId { get; }
        public string AuthToken { get; }
        public AudioSetting AudioSetting { get; }
        public IPEndPoint EndPoint { get; }
        public ListenMode ListenMode { get; set; }
        public AudioPacket AudioPacket { get; set; }
        public CancellationToken SessionCtsToken => this._sessionCts.Token;
        public HandlerPipeline HandlerPipeline { get; }
        public IBizSendOutter SendOutter { get; }
        public PrivateProvider PrivateProvider { get; }
        public bool IsDeviceBinded { get; set; }
        public string? BindCode { get; set; }
        public DateTime LastActivityTime { get; private set; }
        public bool CloseAfterChat { get; set; }
        public long TurnId => Interlocked.Read(ref _turnId);

        public bool IsIdle => Interlocked.Read(ref _isAudioProcessing) == 1;

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
            Interlocked.Exchange(ref _isAudioProcessing, 0);
        }
        public void AcceptIncomingAudio()
        {
            Interlocked.Exchange(ref _isAudioProcessing, 1);
        }

        public void Reset()
        {
            Interlocked.Increment(ref _turnId);
            this.AcceptIncomingAudio();
            this.AudioPacket.Reset();
        }
        public void Abort()
        {
            lock (_lock)
            {
                if (this._isReseting)
                {
                    return;
                }
                this._isReseting = true;
            }
            this._sessionCts.Cancel();
        }

        public void RefreshLastActivityTime()
        {
            this.LastActivityTime = DateTime.Now;
        }

        public void Release()
        {
            this.Reset();
            this.AudioPacket.Release();
            this._sessionCts.Cancel();
            this.HandlerPipeline.Release();
            this.PrivateProvider.Release();
            this._sessionCts.Dispose();
        }

        private void CreateCancellationTokenSource()
        {
            this._sessionCts = new CancellationTokenSource();
            this._sessionCts.Token.Register(async () =>
            {
                await Task.Yield();
                this.Reset();
                this._isReseting = false;

                this._sessionCts.Dispose();
                this._sessionCts = new CancellationTokenSource();
                var newToken = this._sessionCts.Token;
                this.SessionCtsTokenChanged?.Invoke(newToken);
            });
        }

        public override string ToString()
        {
            return $"DeviceId: {DeviceId}, SessionId: {SessionId}";
        }
    }
}
