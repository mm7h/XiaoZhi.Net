namespace XiaoZhi.Net.PerformanceTest.Audio;

internal sealed class CachedAudio
{
    public CachedAudio(string name, string sourcePath, AudioFormat format, IReadOnlyList<ReadOnlyMemory<byte>> opusFrames, int sampleCount)
    {
        this.Name = name;
        this.SourcePath = sourcePath;
        this.Format = format;
        this.OpusFrames = opusFrames;
        this.SampleCount = sampleCount;
    }

    public string Name { get; }
    public string SourcePath { get; }
    public AudioFormat Format { get; }
    public IReadOnlyList<ReadOnlyMemory<byte>> OpusFrames { get; }
    public int SampleCount { get; }
    public int FrameCount => this.OpusFrames.Count;
    public TimeSpan Duration => TimeSpan.FromSeconds(this.SampleCount / (double)this.Format.SampleRate);
}
