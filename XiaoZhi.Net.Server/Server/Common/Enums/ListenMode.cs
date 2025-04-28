using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace XiaoZhi.Net.Server.Common.Enums
{
    internal enum ListenMode
    {
        [Description("Auto")]
        Auto,
        [Description("Manual")]
        Manual,
        [Description("Realtime")]
        Realtime
    }
}
