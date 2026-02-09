namespace XiaoZhi.Net.Server.Common.Configs
{
    internal record AudioSavingConfig(bool SaveFile, string SavePath, string Format, int SampleRate, int Channels, int BitRate)
    {
        public AudioSavingConfig(bool SaveFile)
            : this(SaveFile, string.Empty, string.Empty, -1, -1, -1)
        {
        }
    }
}