using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server.Common.Models
{
    internal class HelloMessage
    {
        public HelloMessage(string sessionId, string transport, AudioSetting audioParams)
        {
            this.SessionId = sessionId;
            this.Transport = transport;
            this.AudioParams = new
            {
                audioParams.Format,
                audioParams.SampleRate,
                audioParams.Channels,
                audioParams.FrameDuration
            };
        }
        public string Type => "hello";
        public int Version { get; set; } = 1;
        public string Transport { get; set; }
        public object AudioParams { get; }
        public string SessionId { get; set; }
    }
}
