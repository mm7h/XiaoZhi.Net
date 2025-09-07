using System.Collections.Generic;

namespace XiaoZhi.Net.Server.Resources
{
    internal interface IMusics : IResource<MusicProviderSetting>
    {
        bool HasMusicFiles { get; }
        IReadOnlyDictionary<string, string> MusicFiles { get; }
        bool UpdateMusicFiles();
    }
}
