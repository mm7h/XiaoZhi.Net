namespace XiaoZhi.Net.Server.Common.Dtos
{
    internal class ResamplerBuildConfig
    {
        public ResamplerBuildConfig(int channels, int inSampleRate, int outSampleRate)
        {
            this.Channels = channels;
            this.InSampleRate = inSampleRate;
            this.OutSampleRate = outSampleRate;
        }

        public int Channels { get; }
        public int InSampleRate { get; }
        public int OutSampleRate { get; }
    }
}
