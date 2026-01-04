namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class Workflow<T>
    {
        private string _sessionId = null!;
        private string _deviceId = null!;
        private long _turnId;
        private T _data = default!;

        public Workflow()
        {
        }

        public string SessionId => this._sessionId;
        public string DeviceId => this._deviceId;
        public long TurnId => this._turnId;
        public T Data => this._data;

        public void Initialize(string sessionId, string deviceId, T data, long turnId)
        {
            this._sessionId = sessionId;
            this._deviceId = deviceId;
            this._data = data;
            this._turnId = turnId;
        }

        public void Initialize(Session context, T data)
        {
            this._sessionId = context.SessionId;
            this._deviceId = context.DeviceId;
            this._data = data;
            this._turnId = context.TurnId;
        }

        public void Reset()
        {
            this._sessionId = null!;
            this._deviceId = null!;
            this._data = default!;
            this._turnId = 0;
        }
    }
}
