namespace XiaoZhi.Net.Server.Abstractions.ConfigSettings;

public sealed class AudioSetting
{
    public AudioSetting()
    {
    }

    public AudioSetting(string format, int sampleRate, int channels, int frameDuration)
    {
        this.Format = format;
        this.SampleRate = sampleRate;
        this.Channels = channels;
        this.FrameDuration = frameDuration;
    }

    public string Format { get; set; } = "opus";
    public int SampleRate { get; set; } = 16000;
    public int Channels { get; set; } = 1;
    public int FrameDuration { get; set; } = 60;
    public int FrameSize => this.SampleRate * this.FrameDuration * this.Channels / 1000;
}
