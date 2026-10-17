namespace XiaoZhi.Net.PerformanceTest.Audio;

internal sealed record AudioFormat(int SampleRate, int Channels, int BitsPerSample, int FrameDurationMilliseconds)
{
    public override string ToString() => $"{this.SampleRate} Hz / {this.Channels} ch / {this.BitsPerSample}-bit PCM / {this.FrameDurationMilliseconds} ms";
}
