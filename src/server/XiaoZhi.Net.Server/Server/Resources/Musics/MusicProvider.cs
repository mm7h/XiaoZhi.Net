using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Resources.Musics
{
    internal class MusicProvider : BaseResource<MusicProvider, MusicProviderSetting>, IMusicFileProvider
    {
        private MusicProviderSetting? _setting;
        private readonly IDictionary<string, string> _musicFiles;

        public MusicProvider(ILogger<MusicProvider> logger) : base(logger)
        {
            this._musicFiles = new Dictionary<string, string>();
            this.MusicFiles = this._musicFiles.AsReadOnly();
        }
        public override string ResourceName => "MusicProvider";

        public string MusicFolderPath => this._setting?.MusicFolderPath ?? string.Empty;
        public bool HasMusicFiles { get; private set; }
        public IReadOnlyDictionary<string, string> MusicFiles { get; private set; }

        public override bool Load(MusicProviderSetting settings)
        {
            this._setting = settings;
            return this.Load(settings.MusicFolderPath);
        }

        private bool Load(string musicFolderPath)
        {
            if (string.IsNullOrWhiteSpace(musicFolderPath))
            {
                this.Logger.LogError(Lang.MusicProvider_Load_PathNotSet);
                return false;
            }

            if (!Directory.Exists(musicFolderPath))
            {
                this.Logger.LogWarning(Lang.MusicProvider_Load_PathNotExist, musicFolderPath);
                return false;
            }
            
            string[] musicFiles = Directory.GetFiles(musicFolderPath);

            this.HasMusicFiles = musicFiles.Any();

            foreach (string filePath in musicFiles)
            {
                string fileName = Path.GetFileName(filePath);
                if (!this._musicFiles.ContainsKey(fileName))
                {
                    this._musicFiles.Add(fileName, filePath);
                }
            }
            this.MusicFiles = this._musicFiles.AsReadOnly();
            
            return true;
        }

        public bool UpdateMusicFiles()
        {
            if (this._setting is null)
            {
                this.Logger.LogWarning(Lang.MusicProvider_UpdateMusicFiles_SettingsNotInitialized);
                return false;
            }
            this._musicFiles.Clear();
            return this.Load(this._setting);
        }
        public bool UpdateMusicFiles(string newMusicFolderPath)
        {
            if (this._setting is null)
            {
                this.Logger.LogWarning(Lang.MusicProvider_UpdateMusicFiles_SettingsNotInitialized);
                return false;
            }
            this._setting.MusicFolderPath = newMusicFolderPath;
            this._musicFiles.Clear();
            return this.Load(newMusicFolderPath);
        }
        public override void Dispose()
        {
            this._musicFiles.Clear();
        }

    }
}
