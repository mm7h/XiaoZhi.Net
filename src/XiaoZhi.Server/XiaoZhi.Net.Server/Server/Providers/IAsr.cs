using SherpaOnnx;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAsr : IProvider
    {
        Task<string> ConvertSpeechText(CircularBuffer voicePackets, int sampleRate, int frameSize, CancellationToken token);
    }
}
