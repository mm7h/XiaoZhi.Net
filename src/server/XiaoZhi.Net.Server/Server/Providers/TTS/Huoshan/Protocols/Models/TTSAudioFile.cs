using System.IO;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Models
{
    internal sealed class TTSAudioFile
    {
        public TTSAudioFile(string sessionId, FileStream stream, string tmpPath, string finalPath)
        {
            this.SessionId = sessionId;
            this.Stream = stream;
            this.TmpPath = tmpPath;
            this.FinalPath = finalPath;
        }

        public string SessionId { get; }
        public FileStream Stream { get; }
        public string TmpPath { get; }
        public string FinalPath { get; }
    }
}
