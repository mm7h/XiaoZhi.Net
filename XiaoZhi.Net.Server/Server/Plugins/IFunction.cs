using System;

namespace XiaoZhi.Net.Server
{
    public interface IFunction
    {
        string FunctionName { get; }
        string Description { get; }
        Delegate Method { get; }
    }
}
