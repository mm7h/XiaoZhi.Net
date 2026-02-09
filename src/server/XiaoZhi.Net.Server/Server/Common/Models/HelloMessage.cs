namespace XiaoZhi.Net.Server.Common.Dtos
{
    internal class HelloMessage
    {
        public HelloMessage(string sessionId, string transport, AudioParams audioParams)
        {
            this.SessionId = sessionId;
            this.Transport = transport;
            this.AudioParams = audioParams;
        }
        public string Type => "hello";
        public int Version { get; set; } = 1;
        public string Transport { get; set; }
        public AudioParams AudioParams { get; set; }
        public string SessionId { get; set; }
    }

    internal class AudioParams
    {
        public AudioParams()
        {
            
        }
        public AudioParams(int sampleRate, int channels, int frameDuration)
        {
            this.SampleRate = sampleRate;
            this.Channels = channels;
            this.FrameDuration = frameDuration;
        }
        public string Format { get; set; } = "opus";
        public int SampleRate { get; set; } = 16000;
        public int Channels { get; set; } = 1;
        public int FrameDuration { get; set; } = 60;
    }
}
