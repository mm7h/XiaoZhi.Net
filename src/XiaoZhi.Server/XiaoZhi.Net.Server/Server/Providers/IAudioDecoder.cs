using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Providers
{
    internal interface IAudioDecoder : IProvider
    {
        int SampleRate { get; }
        int Channels { get; }
        int FrameDuration { get; }
        int FrameSize { get; }
        Task<float[]> DecodeAsync(byte[] opusData, CancellationToken token);
    }
}
