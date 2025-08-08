namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal record Workflow<T>
    {
        public Workflow(string sessionId, T data)
        {
            SessionId = sessionId;
            Data = data;
        }
        public Workflow(Session context, T data)
        {
            SessionId = context.SessionId;
            Data = data;
        }

        public string SessionId { get; }
        public T Data { get; }

        public Workflow<TNew> NextFlow<TNew>(TNew data)
        {
            return new Workflow<TNew>(this.SessionId, data);
        }

    }
}
