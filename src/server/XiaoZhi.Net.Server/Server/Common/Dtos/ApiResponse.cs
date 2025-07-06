namespace XiaoZhi.Net.Server.Common.Dtos
{
    internal class ApiResponse<TData>
    {
        public ApiResponse(){}
        public int Code { get; private set; }
        public string Msg { get; private set; } = null!;
        public TData? Data { get; private set; }
    }
}
