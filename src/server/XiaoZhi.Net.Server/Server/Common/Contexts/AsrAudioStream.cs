using SherpaOnnx;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class AsrAudioStream
    {
        private readonly OfflineStream _asrStream;

        public AsrAudioStream(string sessionId, OfflineStream stream)
        {
            this.SessionId = sessionId;
            this._asrStream = stream;
        }

        public string SessionId { get; }

        public void AcceptAudio(int sampleRate, float[] audioData)
        {
            this._asrStream.AcceptWaveform(sampleRate, audioData);
        }

        public void Release()
        {
            this._asrStream.Dispose();
        }
    }
}
