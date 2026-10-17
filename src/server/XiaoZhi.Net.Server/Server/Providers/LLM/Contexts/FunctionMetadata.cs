using System.Collections.Generic;
using System.ComponentModel;

namespace XiaoZhi.Net.Server.Providers.LLM.Contexts
{
    /// <summary>
    /// 函数工具的结构化元数据。
    /// </summary>
    public sealed class FunctionMetadata
    {
        [Description("函数名称，必须与可用函数列表中某个函数完全匹配；如果不需要调用任何函数，必须返回空字符串。")]
        public string Name { get; set; } = string.Empty;
        
        [Description("函数的描述信息，可选。")]
        public string? Description { get; set; }

        [Description("函数参数列表，只有在确定要调用函数且函数确实需要参数时才返回；否则应为 null 或空。")]
        public List<FunctionParameter>? Parameters { get; set; }

        [Description("函数输入的 JSON Schema，可选。")]
        public string? InputJsonSchema { get; set; }
    }
}