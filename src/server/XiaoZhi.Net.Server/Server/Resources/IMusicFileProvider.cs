using System.Collections.Generic;

namespace XiaoZhi.Net.Server.Resources
{
    internal interface IMusicFileProvider : IResource<MusicProviderSetting>
    {
        bool HasMusicFiles { get; }
        string MusicFolderPath { get; }
        IReadOnlyDictionary<string, string> MusicFiles { get; }
        bool UpdateMusicFiles();
        bool UpdateMusicFiles(string newMusicFolderPath);
    }
}
