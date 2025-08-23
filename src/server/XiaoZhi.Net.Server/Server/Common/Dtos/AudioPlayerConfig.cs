namespace XiaoZhi.Net.Server.Common.Dtos
{
    internal class AudioPlayerConfig
    {
        public AudioPlayerConfig(string sessionId, int frameDurationMs)
        {
            this.SessionId = sessionId;
            this.FrameDurationMs = frameDurationMs;
        }

        public string SessionId { get; set; }
        public int FrameDurationMs { get; set; }
    }
}
