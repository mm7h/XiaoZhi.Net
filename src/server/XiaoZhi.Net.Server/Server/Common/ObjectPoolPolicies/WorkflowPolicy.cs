using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.ObjectPoolPolicies
{
    internal class WorkflowPolicy<T> : PooledObjectPolicy<Workflow<T>> where T : class
    {
        public override Workflow<T> Create()
        {
            return new Workflow<T>();
        }

        public override bool Return(Workflow<T> obj)
        {
            obj.Reset();
            return true;
        }
    }
}