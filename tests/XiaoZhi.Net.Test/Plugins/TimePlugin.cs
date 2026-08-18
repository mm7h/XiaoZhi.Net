using System.ComponentModel;

namespace XiaoZhi.Net.Test.Plugins
{
    public sealed class TimePlugin
    {
        [Description("获取当前时间.")]
        public string GetCurrentTime()
        {
            return DateTime.Now.ToString("R");
        }
    }
}
