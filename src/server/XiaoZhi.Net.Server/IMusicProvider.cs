using System.Collections.Generic;

namespace XiaoZhi.Net.Server
{
    public interface IMusicProvider
    {
        /// <summary>
        /// 获取本地音乐文件路径列表
        /// </summary>
        /// <returns></returns>
        IList<string> GetLocalMusicFiles();
    }
}
