using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Enums;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal sealed class Session
    {
        private int _isAudioProcessing;
        private CancellationTokenSource _sessionCts;

        private readonly object _lock = new object();
        private bool _isCanceling = false;
        private DateTime _cancelCoolingTime = DateTime.Now;

        public Session(string sessionId, string deviceId, IPEndPoint userEndPoint)
        {
            this.SessionId = sessionId;
            this.DeviceId = deviceId;
            this.EndPoint = userEndPoint;
            this.AudioPacketContext = new AudioPacket();
            this.VadStatusContext = new VadStatus();
            this.SentenceTimeAxisContext = new SentenceTimeAxis();
            this.CreateCancellationTokenSource();
        }

        public string SessionId { get; set; }
        public string DeviceId { get; set; }
        public IPEndPoint EndPoint { get; }
        public ListenMode ListenMode { get; set; }
        public AudioPacket AudioPacketContext { get; }
        public VadStatus VadStatusContext { get; }
        public SentenceTimeAxis SentenceTimeAxisContext { get; }
        public CancellationToken SessionCtsToken => this._sessionCts.Token;
        public bool CloseAfterChat { get; set; }

        public bool IsIdle
        {
            get => Volatile.Read(ref _isAudioProcessing) == 1;
            private set => Interlocked.Exchange(ref _isAudioProcessing, value ? 1 : 0);
        }

        public bool ShouldIgnore()
        {
            lock (_lock)
            {
                return this._isCanceling && DateTime.Now < this._cancelCoolingTime;
            }
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
        public bool CheckAsrData()
        {
            int asrPacketSize = AudioPacketContext.AsrPackets.Size;
            if (IsIdle && ListenMode == ListenMode.Manual && asrPacketSize > 15)
            {
                return true;
            }
            else
            {
                this.Reset();
                return false;
            }
        }

        public void Reset()
        {
            this.AcceptIncomingAudio();
            this.VadStatusContext.Reset();
            this.AudioPacketContext.Reset();
            this.SentenceTimeAxisContext.Reset();
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

        public void Release()
        {
            this.Reset();
            this.AudioPacketContext.Release();
            this.SentenceTimeAxisContext.Release();
            this._sessionCts.Cancel();
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
