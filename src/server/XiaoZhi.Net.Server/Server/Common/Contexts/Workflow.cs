namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class Workflow<T>
    {
        private string _sessionId = null!;
        private T _data = default!;

        public Workflow()
        {
        }

        public string SessionId => this._sessionId;
        public T Data => this._data;

        public void Initialize(string sessionId, T data)
        {
            this._sessionId = sessionId;
            this._data = data;
        }

        public void Initialize(Session context, T data)
        {
            this._sessionId = context.SessionId;
            this._data = data;
        }

        public void Reset()
        {
            this._sessionId = null!;
            this._data = default!;
        }
    }
}
