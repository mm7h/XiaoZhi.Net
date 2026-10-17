using Microsoft.Extensions.Logging;
using System.ComponentModel;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.Common.Attributes;
using XiaoZhi.Net.Server.Abstractions.Common.Contexts;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Sample.Server.FunctionTools
{
    public class GetTime : FunctionTool
    {
        public override ValueTask OnFunctionToolInitializedAsync()
        {
            this.Logger.LogInformation("GetTime function tool initialized.");
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnFunctionToolReleasedAsync()
        {
            this.Logger.LogInformation("GetTime function tool released.");
            return ValueTask.CompletedTask;
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