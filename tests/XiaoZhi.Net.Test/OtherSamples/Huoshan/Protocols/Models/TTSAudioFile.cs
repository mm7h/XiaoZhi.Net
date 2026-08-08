using System.IO;

namespace XiaoZhi.Net.Test.OtherSamples.Huoshan.Protocols.Models
{
    internal sealed class TTSAudioFile(string sessionId, FileStream stream, string tmpPath, string finalPath)
    {
        public string SessionId { get; } = sessionId;
        public FileStream Stream { get; } = stream;
        public string TmpPath { get; } = tmpPath;
        public string FinalPath { get; } = finalPath;
    }
}
