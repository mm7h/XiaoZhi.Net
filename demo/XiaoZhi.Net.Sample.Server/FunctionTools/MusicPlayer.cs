using Microsoft.Extensions.Logging;
using System.ComponentModel;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.Common.Attributes;
using XiaoZhi.Net.Server.Abstractions.Common.Contexts;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Sample.Server.FunctionTools
{
    internal class MusicPlayer : PrivateFunctionTool
    {
        public MusicPlayer()
        {
            
        }

        [ToolBehavior(ToolAction.DirectResponse)]
        [Description("获取服务端音乐文件列表，返回包含音乐文件名称的列表信息")]
        public FunctionReturn<string> GetMusicFiles()
        {
            if (!this.MediaTool.HasMusicFiles)
            {
                this.Logger.LogWarning($"No music files available in the media tool for current server at the path {this.MediaTool.MusicFolderPath}.");
                return new FunctionReturn<string>
                {
                    Next = ToolAction.DirectResponse,
                    Result = "No music files available.",
                    Response = "No music files available."
                };
            }

            // key: music file name, value: music file path
            IReadOnlyDictionary<string, string> musicFiles = this.MediaTool.MusicFiles;
            IEnumerable<string> musicNames = musicFiles.Keys;
            this.Logger.LogInformation($"Retrieved music files: {string.Join(", ", musicNames)}");

            string response = $"Available music files: {string.Join(", ", musicFiles.Keys)}";
            return new FunctionReturn<string>
            {
                Result = response,
                Response = response
            };
        }


        [ToolBehavior(ToolAction.DirectResponse)]
        [Description($"播放服务端音乐文件（需要先调用方法 `{nameof(GetMusicFiles)}` 获取音乐列表），返回播放结果描述，你需要播报正在播放的音乐文件名称。")]
        public async ValueTask<FunctionReturn<string>> PlayMusicAsync([Description("是否为随机播放")] bool isRandom, [Description("音乐名称，如果是随机播放，那么不需要此参数")] string? musicName = null)
        {
            if (!this.MediaTool.HasMusicFiles)
            {
                this.Logger.LogWarning($"No music files available in the media tool for current server at the path {this.MediaTool.MusicFolderPath}.");
                return new FunctionReturn<string>
                {
                    Next = ToolAction.DirectResponse,
                    Result = "No music files available.",
                    Response = "No music files available."
                };
            }
            if (isRandom)
            {
                // Play a random music file
                IReadOnlyDictionary<string, string> musicFiles = this.MediaTool.MusicFiles;
                int index = Random.Shared.Next(musicFiles.Count);
                musicName = musicFiles.Keys.ElementAt(index);
            }
            else
            {
                // Validate the provided music name
                if (string.IsNullOrWhiteSpace(musicName) || !this.MediaTool.MusicFiles.ContainsKey(musicName))
                {
                    this.Logger.LogWarning($"Invalid or missing music name: {musicName}. Available files: {string.Join(", ", this.MediaTool.MusicFiles.Keys)}");
                    return new FunctionReturn<string>
                    {
                        Next = ToolAction.DirectResponse,
                        Result = $"Invalid or missing music name. Available files: {string.Join(", ", this.MediaTool.MusicFiles.Keys)}",
                        Response = $"Invalid or missing music name. Available files: {string.Join(", ", this.MediaTool.MusicFiles.Keys)}"
                    };
                }
            }
            await this.MediaTool.PlayAsync(this.MediaTool.MusicFiles[musicName]);
            this.Logger.LogInformation($"Playing music: {musicName}");
            return new FunctionReturn<string>
            {
                Next = ToolAction.Continue,
                Result = $"Playing music: {musicName}",
                Response = $"Playing music: {musicName}"
            };
        }

        [ToolBehavior(ToolAction.DirectResponse)]
        [Description("暂停当前正在播放的音乐，可以通过 ResumeMusicAsync 继续播放")]
        public async ValueTask<FunctionReturn<string>> PauseMusicAsync()
        {
            if (!this.MediaTool.IsPlaying)
            {
                return new FunctionReturn<string>
                {
                    Next = ToolAction.DirectResponse,
                    Result = "No music is currently playing.",
                    Response = "No music is currently playing."
                };
            }
            await this.MediaTool.PauseAsync();
            this.Logger.LogInformation("Music paused.");
            return new FunctionReturn<string>
            {
                Next = ToolAction.DirectResponse,
                Result = "Music paused.",
                Response = "Music paused."
            };
        }

        [ToolBehavior(ToolAction.DirectResponse)]
        [Description("继续播放已暂停的音乐")]
        public async ValueTask<FunctionReturn<string>> ResumeMusicAsync()
        {
            if (!this.MediaTool.IsPaused)
            {
                return new FunctionReturn<string>
                {
                    Next = ToolAction.DirectResponse,
                    Result = "No music is currently paused.",
                    Response = "No music is currently paused."
                };
            }
            await this.MediaTool.ResumeAsync();
            string musicName = this.MediaTool.PlayingMusicName ?? string.Empty;
            this.Logger.LogInformation("Music resumed: {musicName}", musicName);
            return new FunctionReturn<string>
            {
                Next = ToolAction.DirectResponse,
                Result = $"Music resumed: {musicName}",
                Response = $"Music resumed: {musicName}"
            };
        }

        [ToolBehavior(ToolAction.DirectResponse)]
        [Description("停止当前正在播放或已暂停的音乐")]
        public async ValueTask<FunctionReturn<string>> StopMusicAsync()
        {
            await this.MediaTool.StopAsync();
            this.Logger.LogInformation("Music stopped.");
            return new FunctionReturn<string>
            {
                Next = ToolAction.DirectResponse,
                Result = "Music stopped.",
                Response = "Music stopped."
            };
        }

        public override ValueTask OnFunctionToolInitializedAsync()
        {
            if (this.MediaTool.HasMusicFiles)
            {
                string musicFileNames = string.Join(Environment.NewLine, this.MediaTool.MusicFiles.Keys);
                this.Logger.LogInformation("The music files are below: {newLine}{files}", Environment.NewLine, musicFileNames);
            }
            else
            { 
                this.Logger.LogWarning($"No music files available in the media tool for current server at the path {this.MediaTool.MusicFolderPath}.");
            }
            return ValueTask.CompletedTask;
        }
    }
}
