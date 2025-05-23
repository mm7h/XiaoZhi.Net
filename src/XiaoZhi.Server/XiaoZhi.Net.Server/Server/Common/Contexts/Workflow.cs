using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class Workflow<T>
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

        public string SessionId { get; private set; }
        public T Data { get; private set; }

        public Workflow<TNew> NextFlow<TNew>(TNew data)
        {
            return new Workflow<TNew>(this.SessionId, data);
        }

    }
}
