using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace XiaoZhi.Net.Test.Plugins
{
    [Description("唱歌、听歌、播放音乐的插件")]
    internal class PlayMusic
    {
        [KernelFunction("GetLocalMusicFiles"), Description("从本地目录项获取音乐文件路径")]
        public List<string> GetLocalMusicFiles()
        {
            return new List<string>()
            { 
                "./music/你好 - 北京.mp3",
                "./music/两只老虎.mp3"
            };
        }


        [KernelFunction("PlayMusic"), Description($"歌曲名称，音乐文件路径列表优先从```{nameof(GetLocalMusicFiles)}```方法中获取，如果用户没有指定具体歌名则为'random', 明确指定的时返回音乐的名字 示例: ```用户:播放两只老虎\n参数：两只老虎``` ```用户:播放音乐 \n参数：random ``` ```用户:来点音乐 \n参数：random ```")]
        public void Play(string songName)
        {
            Console.WriteLine("songName: " + songName);
        }
    }
}
