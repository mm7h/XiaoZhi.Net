using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Abstractions.Common.Attributes
{
    /// <summary>
    /// 声明函数工具的默认行为。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class ToolBehaviorAttribute : Attribute
    {
        public ToolBehaviorAttribute()
        {
        }

        public ToolBehaviorAttribute(ToolAction defaultAction)
        {
            DefaultAction = defaultAction;
        }

        /// <summary>
        /// 当函数返回未指定 Next 时的默认动作。
        /// </summary>
        public ToolAction DefaultAction { get; set; } = ToolAction.Continue;

        /// <summary>
        /// todo
        /// 是否允许该函数被意图识别模块调用。
        /// 默认为 true，表示允许；如果设置为 false，则该函数只能被显式调用，意图识别模块不会将其作为候选工具。
        /// </summary>
        public bool AllowIntentDetection { get; set; } = true;
    }
}