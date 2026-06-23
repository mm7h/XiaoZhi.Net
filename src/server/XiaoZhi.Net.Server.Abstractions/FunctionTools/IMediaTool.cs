using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    public interface IMediaTool
    {
        string BasicPath { get; }

        float Volume { get; set; }

        ValueTask PlayAsync(string musicName);
        ValueTask PauseAsync();
        ValueTask ResumeAsync();
        ValueTask StopAsync();
        ValueTask SeekAsync(TimeSpan position);
    }
}
