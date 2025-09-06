using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace XiaoZhi.Net.Server.Resources.Musics
{
    internal class LocalMusic : BaseResource<LocalMusic, LocalMusicSetting>, IMusics
    {
        private LocalMusicSetting? _setting;
        private readonly IDictionary<string, string> _musicFiles;

        public LocalMusic(ILogger<LocalMusic> logger) : base(logger)
        {
            this._musicFiles = new Dictionary<string, string>();
            this.MusicFiles = this._musicFiles.AsReadOnly();
        }
        public override string ResourceName => "LocalMusic";

        public bool HasMusicFiles { get; private set; }
        public IReadOnlyDictionary<string, string> MusicFiles { get; private set; }

        public override bool Load(LocalMusicSetting settings)
        {
            if (string.IsNullOrEmpty(settings.MusicFolderPath))
            {
                this.Logger.LogError("Music folder path is not set in the settings.");
                return false;
            }

            if (!Directory.Exists(settings.MusicFolderPath))
            {
                this.Logger.LogWarning("Music folder path '{MusicFolderPath}' does not exist.", settings.MusicFolderPath);
                return true;
            }

            string[] musicFiles = Directory.GetFiles(settings.MusicFolderPath);

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
                this.Logger.LogWarning("Cannot update music files because the settings are not initialized.");
                return false;
            }
            this._musicFiles.Clear();
            return this.Load(this._setting);
        }

        public override void Dispose()
        {
            this._musicFiles.Clear();
        }

    }
}
