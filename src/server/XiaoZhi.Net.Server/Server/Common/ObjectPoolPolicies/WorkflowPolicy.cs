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
            // 清理对象状态，为下次使用做准备
            obj.Reset();
            return true;
        }
    }
}