using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Resources;

namespace XiaoZhi.Net.Server.Providers.LLM.Plugins
{
    [Description("播放音乐的插件")]
    internal class LocalMusicPlayer : ILLMPlugin
    {
        private readonly IMusics _musicProvider;
        private readonly ILogger<LocalMusicPlayer> _logger;
        private Session? _currentSession;

        public LocalMusicPlayer(IMusics musicProvider, ILogger<LocalMusicPlayer> logger)
        {
            this._musicProvider = musicProvider;
            this._logger = logger;
        }

        public string ProviderType => "llm plugin";

        public string ModelName => nameof(LocalMusicPlayer);

        public bool Build(LLMPluginConfig config)
        {
            this._currentSession = config.Session;
            return true;
        }

        [KernelFunction, Description("获取本地音乐文件列表，返回包含音乐文件名称的列表信息")]
        public string GetLocalMusicFilesAsync()
        {
            if (this._currentSession is null)
            {
                return "Failed to get local music files, the current session is not initialized.";
            }
            if (this._musicProvider is not null)
            {
                IReadOnlyDictionary<string, string> localMusicFiles = this._musicProvider.MusicFiles;
                if (localMusicFiles is null || !localMusicFiles.Any())
                {
                    this._logger.LogWarning("{ProviderType} - {ModelName}, Failed to get local music files due to no files existing for session {sessionId}.", this.ProviderType, this.ModelName, this._currentSession.SessionId);
                    return "Failed to get local music files due to no files existing.";
                }

                List<string> musicNames = localMusicFiles.Keys.ToList();
                this._logger.LogInformation("{ProviderType} - {ModelName}, Got {fileCount} local music files success for session {sessionId}.", this.ProviderType, this.ModelName, musicNames.Count, this._currentSession.SessionId);

                return $"Get the local music files success. Available music files: {string.Join(", ", musicNames)}";
            }
            else
                return "Failed to get local music files, the music provider is not initialized yet.";
        }

        [KernelFunction, Description("播放本地音乐文件（需要先调用方法 `" + nameof(GetLocalMusicFilesAsync) + "` 来获取本地有哪些音乐文件），返回播放结果的描述信息，你需要播报正在播放的音乐文件名称。")]
        public async ValueTask<string> PlayLocalMusic([Description("是否为随机播放")] bool isRandom, [Description("音乐名称，如果是随机播放，那么不需要此参数")] string? musicName = null)
        {
            if (this._currentSession is null)
            {
                return "Failed to play local music, the current session is not initialized.";
            }

            if (this._currentSession.AudioPlayerClient is null)
            {
                return "Failed to play local music, the player is not initialized.";
            }

            if (this._musicProvider is not null)
            {
                IReadOnlyDictionary<string, string> localMusicFiles = this._musicProvider.MusicFiles;
                if (localMusicFiles is null || !localMusicFiles.Any())
                {
                    return "Failed to play local music, there's no music files in local.";
                }

                string musicFilePath = string.Empty;
                string selectedMusicName = string.Empty;

                if (isRandom)
                {
                    int randomIndex = Random.Shared.Next(localMusicFiles.Count);
                    var selectedMusic = localMusicFiles.ElementAt(randomIndex);
                    musicFilePath = selectedMusic.Value;
                    selectedMusicName = selectedMusic.Key;
                }
                else
                {
                    if (string.IsNullOrEmpty(musicName))
                    {
                        return "Failed to play local music, the field musicName is empty.";
                    }
                    if (localMusicFiles.ContainsKey(musicName))
                    {
                        musicFilePath = localMusicFiles[musicName];
                        selectedMusicName = musicName;
                    }
                }

                if (string.IsNullOrEmpty(musicFilePath))
                {
                    return "Failed to play local music, the specified music file was not found.";
                }

                try
                {
                    await this._currentSession.AudioPlayerClient.MusicPlayer.PlayAsync(this._currentSession.SessionCtsToken, musicFilePath);

                    this._logger.LogInformation("{ProviderType} - {ModelName}, Playing the music file {musicFilePath} for session {sessionId}.", this.ProviderType, this.ModelName, musicFilePath, this._currentSession.SessionId);

                    return $"Successfully started playing music: {selectedMusicName}";
                }
                catch (Exception ex)
                {
                    this._logger.LogError(ex, "Failed to play music file {musicFilePath} for session {sessionId}.", musicFilePath, this._currentSession.SessionId);
                    return $"Failed to play music: {selectedMusicName}. Error: {ex.Message}";
                }
            }
            else
                return "Failed to play local music, the music provider is not initialized yet.";
        }

        public void Dispose() { }
    }
}
