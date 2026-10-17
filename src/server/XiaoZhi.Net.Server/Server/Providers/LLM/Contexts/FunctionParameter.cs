using System.ComponentModel;

namespace XiaoZhi.Net.Server.Providers.LLM.Contexts
{
    /// <summary>
    /// 函数参数的结构化描述。
    /// </summary>
    public sealed class FunctionParameter
    {
        [Description("参数名称，必须与函数定义中某个参数完全匹配。")]
        public string Name { get; set; } = string.Empty;

        [Description("参数类型，建议使用基本类型（string/integer/boolean/array/object）或可识别的自定义类型；如果无法明确判断参数类型，填 object。")]
        public string Type { get; set; } = string.Empty;

        [Description("参数的描述信息，可选。")]
        public string? Description { get; set; }

        [Description("是否为必填参数，默认为 false；只有在明确知道参数是否必填时才设置为 true，否则保持默认。")]
        public bool Required { get; set; }

        [Description("参数值，必须与参数类型一致；如果参数类型无法明确判断，可以直接填原始值（如字符串、数字、布尔值、数组或对象），但不要填字符串化的 JSON。")]
        public object? Value { get; set; }
    }
}