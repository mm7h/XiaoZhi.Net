namespace XiaoZhi.Net.Server.Abstractions.Common.Dtos
{
    public class ApiResponse<TData>
    {
        public ApiResponse() { }
        public int Code { get; set; }
        public string Msg { get; set; } = null!;
        public TData? Data { get; set; }

        public static ApiResponse<TData> Success(TData data)
        {
            return new ApiResponse<TData>
            {
                Code = 0,
                Msg = "success",
                Data = data
            };
        }

        public static ApiResponse<TData> Failure(int code, string msg, TData? data = default)
        {
            return new ApiResponse<TData>
            {
                Code = code,
                Msg = msg,
                Data = data
            };
        }
    }
}
