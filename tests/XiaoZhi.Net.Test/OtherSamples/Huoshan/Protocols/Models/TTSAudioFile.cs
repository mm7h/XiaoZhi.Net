using System.IO;

namespace XiaoZhi.Net.Test.OtherSamples.Huoshan.Protocols.Models
{
    internal sealed class TTSAudioFile
    {
        public TTSAudioFile(string sessionId, FileStream stream, string tmpPath, string finalPath)
        {
            SessionId = sessionId;
            Stream = stream;
            TmpPath = tmpPath;
            FinalPath = finalPath;
        }

        public string SessionId { get; }
        public FileStream Stream { get; }
        public string TmpPath { get; }
        public string FinalPath { get; }
    }
}
