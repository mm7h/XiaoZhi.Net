using System.Collections.Generic;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server
{
    public interface IMusicProvider
    {
        /// <summary>
        /// 获取本地音乐文件路径列表
        /// </summary>
        /// <returns>文件名，文件路径</returns>
        Task<IReadOnlyDictionary<string, string>> GetLocalMusicFilesAsync();
    }
}
