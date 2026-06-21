using System.ComponentModel;
using XiaoZhi.Net.Server.Abstractions;

namespace XiaoZhi.Net.Sample.Server.Plugins
{


    [Description("获取关于当前日期和时间插件")]
    public class GetTime
    {
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
