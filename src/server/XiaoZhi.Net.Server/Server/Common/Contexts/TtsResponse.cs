namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class TtsResponse
    {
        public TtsResponse(string sessionId, string deviceId, float[] audioData)
        {
            this.SessionId = sessionId;
            this.DeviceId = deviceId;
            this.Segment = null!;
            this.AudioData = audioData;
        }
        public TtsResponse(string sessionId, string deviceId, OutSegment segment, float[] audioData)
        {
            this.SessionId = sessionId;
            this.DeviceId = deviceId;
            this.Segment = segment;
            this.AudioData = audioData;
        }

        public string SessionId { get; }
        public string DeviceId { get; }
        public OutSegment Segment { get; set; }
        public float[] AudioData { get; set; }
    }
}
