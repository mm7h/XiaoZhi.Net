using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace XiaoZhi.Net.Test.Plugins
{
    public sealed class TimePlugin
    {
        [KernelFunction, Description("获取当前时间.")]
        public string GetCurrentTime()
        {
            return DateTime.Now.ToString("R");
        }
    }
}
