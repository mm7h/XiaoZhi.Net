using System.Text.Json.Serialization;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Abstractions.Common.Contexts
{
    /// <summary>
    /// 自定义函数工具的统一返回结构。
    /// </summary>
    /// <typeparam name="T">函数实际返回的数据类型。</typeparam>
    public sealed class FunctionReturn<T>
    {
        /// <summary>
        /// 后续动作。为空时回退到函数定义上的默认动作。
        /// </summary>
        [JsonIgnore]
        public ToolAction? Next { get; set; }

        /// <summary>
        /// 函数返回给 LLM 的实际结果。
        /// </summary>
        public T? Result { get; set; }

        /// <summary>
        /// 函数返回给客户端的播报文本。
        /// </summary>
        [JsonIgnore]
        public string? Response { get; set; }
    }
}