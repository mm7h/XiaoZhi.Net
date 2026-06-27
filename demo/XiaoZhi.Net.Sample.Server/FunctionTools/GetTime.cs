using System.ComponentModel;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.Common.Attributes;
using XiaoZhi.Net.Server.Abstractions.Common.Contexts;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Sample.Server.FunctionTools
{
    public class GetTime : FunctionTool
    {
        public override async ValueTask OnFunctionToolInitializedAsync()
        {
            await Console.Out.WriteLineAsync("GetTime 插件已初始化");
        }

        [Description("获取当前的日期和时间")]
        [ToolBehavior(ToolAction.DirectResponse)]
        public FunctionReturn<DateTime> GetNowTime()
        {
            DateTime now = DateTime.Now;
            return new FunctionReturn<DateTime>
            {
                Result = now,
                Response = $"现在时间是 {now:yyyy-MM-dd HH:mm:ss}"
            };
        }
    }
}