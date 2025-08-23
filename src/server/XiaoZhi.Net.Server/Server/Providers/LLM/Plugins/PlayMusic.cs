using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.LLM.Plugins
{
    [Description("播放音乐的插件")]
    internal class PlayMusic : ILLMPlugin<IMusicProvider?>
    {
        private readonly ILogger<PlayMusic> _logger;
        private Session? _currentSession;
        private IMusicProvider? _musicProvider;

        public PlayMusic(ILogger<PlayMusic> logger)
        {
            this._logger = logger;
        }

        public string ProviderType => "llm plugin";

        public string ModelName => nameof(PlayMusic);

        public bool Build(LLMPluginConfig<IMusicProvider?> settings)
        {
            this._currentSession = settings.Session;

            return true;
        }


        [KernelFunction, Description("播放本地音乐文件，返回值为\"是否播放成功\"和\"播放结果的描述信息\"")]
        public async ValueTask<(bool, string)> PlayLocalMusic([Description("是否为随机播放")] bool isRandom, [Description("音乐名称，如果是随机播放，那么不需要此参数")] string? musicName = null)
        {
            if (this._currentSession is null)
            {
                return (false, "Failed to play local music, the current session is not initialized.");
            }

            if (this._musicProvider is not null)
            {
                IReadOnlyDictionary<string, string> localMusicFiles = this._musicProvider.GetLocalMusicFiles();
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
                await this._currentSession.HandlerPipeline.PushAudioToSendAsync(text, Emotion.Kissy, musicFilePath);

                return (true, $"Playing the music {musicName} success.");
            }
            else
                return (false, "Failed to play local music, cannot get the music file list from local.");
        }



        public void Dispose()
        {

        }
    }
}
