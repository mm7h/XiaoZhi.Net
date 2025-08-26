using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;

namespace XiaoZhi.Net.Server.Providers.LLM.Plugins
{
    [Description("播放音乐的插件")]
    internal class LocalMusicPlayer : ILLMPlugin<IMusicProvider?>
    {
        private readonly ILogger<LocalMusicPlayer> _logger;
        private Session? _currentSession;
        private IAudioPlayer? _audioPlayer;
        private IMusicProvider? _musicProvider;

        public LocalMusicPlayer(ILogger<LocalMusicPlayer> logger)
        {
            this._logger = logger;
        }

        public string ProviderType => "llm plugin";

        public string ModelName => nameof(LocalMusicPlayer);

        public bool Build(LLMPluginConfig<IMusicProvider?> settings)
        {
            this._currentSession = settings.Session;
            this._audioPlayer = settings.Session.AudioPlayer;
            return true;
        }

        [KernelFunction, Description("获取本地音乐文件列表，返回值为\"是否获取成功\"、\"获取结果是否成功的描述信息\"和\"音乐文件名称列表\"")]
        public async ValueTask<(bool, string, IList<string>?)> GetLocalMusicFilesAsync()
        {
            if (this._currentSession is null)
            {
                return (false, "Failed to get local music files, the current session is not initialized.", null);
            }
            if (this._musicProvider is not null)
            {
                IReadOnlyDictionary<string, string> localMusicFiles = await this._musicProvider.GetLocalMusicFilesAsync();
                if (localMusicFiles is null || !localMusicFiles.Any())
                {
                    this._logger.LogWarning("{ProviderType} - {ModelName}, Failed to get local music files due to no files existing for session {sessionId}.", this.ProviderType, this.ModelName, this._currentSession.SessionId);
                    return (false, "Failed to get local music files due to no files existing.", null);
                }

                List<string> musicNames = localMusicFiles.Keys.ToList();
                this._logger.LogInformation("{ProviderType} - {ModelName}, Got {fileCount} local music files success for session {sessionId}.", this.ProviderType, this.ModelName, musicNames.Count, this._currentSession.SessionId);

                return (true, "Get the local music files success", musicNames);
            }
            else
                return (false, "Failed to play local music, the music provider is not initialized yet.", null);
        }

        [KernelFunction, Description("播放本地音乐文件（需要先调用方法 `" + nameof(GetLocalMusicFilesAsync) + "` 来获取本地有哪些音乐文件），返回值为\"是否播放成功\"和\"播放结果是否成功的描述信息\"")]
        public async ValueTask<(bool, string)> PlayLocalMusic([Description("是否为随机播放")] bool isRandom, [Description("音乐名称，如果是随机播放，那么不需要此参数")] string? musicName = null)
        {
            if (this._currentSession is null)
            {
                return (false, "Failed to play local music, the current session is not initialized.");
            }

            if (this._audioPlayer is null)
            {
                return (false, "Failed to play local music, the audio player for current session is not initialized yet.");
            }

            if (this._musicProvider is not null)
            {
                IReadOnlyDictionary<string, string> localMusicFiles = await this._musicProvider.GetLocalMusicFilesAsync();
                if (localMusicFiles is null || !localMusicFiles.Any())
                {
                    return (false, "Failed to play local music, there's no music files in local.");
                }

                string musicFilePath = string.Empty;

                if (isRandom)
                {
                    int randomIndex = Random.Shared.Next(localMusicFiles.Count);
                    musicFilePath = localMusicFiles.ElementAt(randomIndex).Value;
                }
                else
                {
                    if (string.IsNullOrEmpty(musicName))
                    {
                        return (false, "Failed to play local music, the field musicName is empty.");
                    }
                    if (localMusicFiles.ContainsKey(musicName))
                    {
                        musicFilePath = localMusicFiles[musicName];
                    }
                }

                if (string.IsNullOrEmpty(musicFilePath))
                {
                    return (false, "Failed to play local music, there's no music files in local.");
                }

                string text = $"正在播放音乐：{musicName}";
                await this._audioPlayer.PlayAsync(musicFilePath);

                this._logger.LogInformation("{ProviderType} - {ModelName}, Playing the music file {musicFilePath} for session {sessionId}.", this.ProviderType, this.ModelName, musicFilePath, this._currentSession.SessionId);

                return (true, $"Playing the music {musicName} success.");
            }
            else
                return (false, "Failed to play local music, the music provider is not initialized yet.");
        }



        public void Dispose(){}
    }
}
