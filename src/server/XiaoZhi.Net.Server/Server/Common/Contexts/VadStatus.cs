using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class VadStatus
    {
        public VadStatus()
        {

        }
        public long HaveVoiceLatestTime { get; set; }
        public bool HaveVoice { get; set; }
        public bool VoiceStop { get; set; }

        public void Reset()
        {
            VoiceStop = false;
            HaveVoice = false;
            HaveVoiceLatestTime = 0;
        }
    }
}
