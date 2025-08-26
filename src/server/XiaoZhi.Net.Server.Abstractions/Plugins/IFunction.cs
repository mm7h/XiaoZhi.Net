namespace XiaoZhi.Net.Server.Abstractions
{
    public interface IFunction
    {
        string FunctionName { get; }
        string Description { get; }
        Delegate Method { get; }
    }
}
