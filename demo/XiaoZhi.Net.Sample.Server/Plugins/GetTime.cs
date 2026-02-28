using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace XiaoZhi.Net.Sample.Server.Plugins
{


    [Description("获取关于当前日期和时间插件")]
    public class GetTime
    {
        [KernelFunction, Description("获取当前的日期和时间")]
        public DateTime GetNowTime()
        {
            return DateTime.Now;
        }
    }
}
