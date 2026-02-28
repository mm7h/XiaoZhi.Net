using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Resources;
using XiaoZhi.Net.Server.Common.Configs;

namespace XiaoZhi.Net.Server.Providers.LLM.Plugins
{
    [Description("播放服务端音乐的插件")]
    internal class MusicPlayer : BasePlugin<MusicPlayer>, ILLMPlugin
    {
        private readonly IMusics _musicProvider;

        public MusicPlayer(IMusics musicProvider, ILogger<MusicPlayer> logger) : base(logger)
        {
            this._musicProvider = musicProvider;
        }

        public override string ModelName => nameof(MusicPlayer);

        public override bool Build(LLMPluginConfig config)
        {
            if (config.Kernel.Data.TryGetValue("session", out object? val) && val is Session session)
            {
                this.CurrentSession = session;
                return true;
            }
            else
            {
                return false;
            }
        }

        [KernelFunction, Description("获取服务端音乐文件列表，返回包含音乐文件名称的列表信息")]
        public string GetMusicFilesAsync()
        {
            if (this.CurrentSession is null)
            {
                return Lang.MusicPlayer_GetMusicFilesAsync_SessionNotInit;
            }
            if (this._musicProvider is not null)
            {
                IReadOnlyDictionary<string, string> musicFiles = this._musicProvider.MusicFiles;
                if (!this._musicProvider.HasMusicFiles)
                {
                    this.Logger.LogWarning(Lang.MusicPlayer_GetMusicFilesAsync_NoFilesLog, this.ProviderType, this.ModelName, this.DeviceId);
                    return Lang.MusicPlayer_GetMusicFilesAsync_NoFilesMsg;
                }

                IEnumerable<string> musicNames = musicFiles.Keys;
                this.Logger.LogInformation(Lang.MusicPlayer_GetMusicFilesAsync_SuccessLog, this.ProviderType, this.ModelName, musicNames.Count(), this.DeviceId);

                return string.Format(Lang.MusicPlayer_GetMusicFilesAsync_SuccessMsg, string.Join(", ", musicNames));
            }
            else
                return Lang.MusicPlayer_GetMusicFilesAsync_ProviderNotInit;
        }

        [KernelFunction, Description("播放服务端音乐文件（需要先调用方法 `" + nameof(GetMusicFilesAsync) + "` 来获取本地有哪些音乐文件；如果已经调用过该方法获取到了音乐文件列表，那么就不需要再调用了），返回播放结果的描述信息，你需要播报正在播放的音乐文件名称。")]
        public async ValueTask<string> PlayMusic([Description("是否为随机播放")] bool isRandom, [Description("音乐名称，如果是随机播放，那么不需要此参数")] string? musicName = null)
        {
            if (this.CurrentSession is null)
            {
                return Lang.MusicPlayer_PlayMusic_SessionNotInit;
            }

            if (this.CurrentSession.PrivateProvider.AudioPlayerClient is null)
            {
                return Lang.MusicPlayer_PlayMusic_PlayerNotInit;
            }

            if (this._musicProvider is not null)
            {
                IReadOnlyDictionary<string, string> musicFiles = this._musicProvider.MusicFiles;
                if (!this._musicProvider.HasMusicFiles)
                {
                    return Lang.MusicPlayer_PlayMusic_NoFiles;
                }

                string musicFilePath = string.Empty;
                string selectedMusicName = string.Empty;

                if (isRandom)
                {
                    int randomIndex = Random.Shared.Next(musicFiles.Count);
                    var selectedMusic = musicFiles.ElementAt(randomIndex);
                    musicFilePath = selectedMusic.Value;
                    selectedMusicName = selectedMusic.Key;
                }
                else
                {
                    if (string.IsNullOrEmpty(musicName))
                    {
                        return Lang.MusicPlayer_PlayMusic_MusicNameEmpty;
                    }
                    if (musicFiles.ContainsKey(musicName))
                    {
                        musicFilePath = musicFiles[musicName];
                        selectedMusicName = musicName;
                    }
                }

                if (string.IsNullOrEmpty(musicFilePath))
                {
                    return Lang.MusicPlayer_PlayMusic_FileNotFound;
                }

                try
                {
                    await this.CurrentSession.PrivateProvider.AudioPlayerClient.MusicPlayer.PlayAsync(this.CurrentSession.SessionCtsToken, musicFilePath);

                    this.Logger.LogInformation(Lang.MusicPlayer_PlayMusic_PlayingLog, this.ProviderType, this.ModelName, musicFilePath, this.DeviceId);

                    return string.Format(Lang.MusicPlayer_PlayMusic_SuccessMsg, selectedMusicName);
                }
                catch (Exception ex)
                {
                    this.Logger.LogError(ex, Lang.MusicPlayer_PlayMusic_FailedLog, musicFilePath, this.DeviceId);
                    return string.Format(Lang.MusicPlayer_PlayMusic_FailedMsg, selectedMusicName, ex.Message);
                }
            }
            else
                return Lang.MusicPlayer_PlayMusic_ProviderNotInit;
        }

        public override void Dispose() { }
    }
}
