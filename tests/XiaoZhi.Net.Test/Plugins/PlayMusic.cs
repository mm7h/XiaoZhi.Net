using System.ComponentModel;

namespace XiaoZhi.Net.Test.Plugins
{
    [Description("唱歌、听歌、播放音乐的工具")]
    internal class PlayMusic
    {
        [Description("从本地目录项获取音乐文件路径")]
        public List<string> GetLocalMusicFiles()
        {
            return
            [
                "./music/你好 - 北京.mp3",
                "./music/两只老虎.mp3"
            ];
        }

        [Description("播放指定歌曲；未指定歌名时使用 random。")]
        public void Play(string songName)
        {
            Console.WriteLine("songName: " + songName);
        }
    }
}
