using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Providers.LLM.Plugins
{
    [Description("播放音乐的插件")]
    internal class PlayMusic
    {
        private readonly Session _currentSession;
        private readonly IMusicProvider? _musicProvider;

        public PlayMusic(Session session, IMusicProvider? musicProvider)
        {
            this._currentSession = session;
            this._musicProvider = musicProvider;
        }

        [KernelFunction, Description("从本地目录项获取音乐文件路径")]
        public async ValueTask<bool> PlayLocalMusic([Description("是否为随机播放")] bool isRandom, [Description("音乐名称，如果是随机播放，那么不需要此参数")] string? musicName = null)
        {
            if (this._musicProvider is not null)
            {
                IList<string> localMusicFiles = this._musicProvider.GetLocalMusicFiles();
                if (localMusicFiles is null || !localMusicFiles.Any())
                {
                    return false;
                }
                if (isRandom)
                {
                    int randomIndex = Random.Shared.Next(localMusicFiles.Count);
                    musicName = localMusicFiles[randomIndex];
                }
                else
                {
                    musicName = localMusicFiles.FirstOrDefault(m => !string.IsNullOrEmpty(musicName) && m.Contains(musicName, StringComparison.OrdinalIgnoreCase));
                }

                if (string.IsNullOrEmpty(musicName))
                {
                    return false;
                }

                await this._currentSession.HandlerPipeline.HandlePlayAudioFile(musicName);

                return true;
            }
            else
                return false;
        }



    }
}
