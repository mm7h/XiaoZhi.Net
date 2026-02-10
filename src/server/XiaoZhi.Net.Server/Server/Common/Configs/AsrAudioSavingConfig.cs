using System.Text.Json.Serialization;

namespace XiaoZhi.Net.Server.Common.Configs
{
    [method: JsonConstructor]
    internal record AudioSavingConfig(
        [property: JsonPropertyName("SaveFile")] bool SaveFile = false,
        [property: JsonPropertyName("SavePath")] string SavePath = "./data/asr-cache",
        [property: JsonPropertyName("Format")] string Format = "wav",
        [property: JsonPropertyName("SampleRate")] int SampleRate = 16000,
        [property: JsonPropertyName("Channels")] int Channels = 1,
        [property: JsonPropertyName("BitRate")] int BitRate = 128000)
    {
        public AudioSavingConfig(bool SaveFile)
            : this(SaveFile, string.Empty, string.Empty, -1, -1, -1)
        {
        }
    }
}