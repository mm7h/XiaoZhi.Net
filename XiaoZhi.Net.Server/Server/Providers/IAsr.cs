using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using SherpaOnnx;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAsr : IProvider
    {
        Task<string> ConvertSpeechText( CircularBuffer voicePackets, int sampleRate, int frameSize, CancellationToken token);
    }
}
