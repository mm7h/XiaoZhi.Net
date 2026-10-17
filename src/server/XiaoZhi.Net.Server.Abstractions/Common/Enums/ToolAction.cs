namespace XiaoZhi.Net.Server.Abstractions.Common.Enums
{
    /// <summary>
    /// 工具执行后的后续动作。
    /// </summary>
    public enum ToolAction
    {
        /// <summary>
        /// 继续交给 LLM 组织自然语言回复。
        /// </summary>
        Continue = 0,

        /// <summary>
        /// 直接把工具结果返回给用户。
        /// </summary>
        DirectResponse = 1,

        /// <summary>
        /// 静默执行，不向用户返回额外文本。
        /// </summary>
        Silent = 2,
    }
}