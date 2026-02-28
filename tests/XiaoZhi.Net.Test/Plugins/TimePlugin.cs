using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace XiaoZhi.Net.Test.Plugins
{
    public sealed class TimePlugin
    {
        [KernelFunction, Description("获取当前时间.")]
        public string GetCurrentTime() => DateTime.Now.ToString("R");
    }
}
