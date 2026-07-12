namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    public interface IMediaTool
    {
        /// <summary>
        /// 是否存在音乐文件
        /// </summary>
        bool HasMusicFiles { get; }
        /// <summary>
        /// 当前音乐文件夹路径，若不存在音乐文件则为 null
        /// </summary>
        string MusicFolderPath { get; }
        /// <summary>
        /// 音量调节，范围为 0.0 到 1.0。
        /// 0.0 表示静音；
        /// 1.0 表示最大音量
        /// </summary>
        float Volume { get; set; }
        /// <summary>
        /// 音乐播放器是否正在播放（Buffering/Playing）
        /// </summary>
        bool IsPlaying { get; }
        /// <summary>
        /// 音乐播放器是否处于暂停状态
        /// </summary>
        bool IsPaused { get; }
        /// <summary>
        /// 当前正在播放的音乐文件名，未播放时为 null
        /// </summary>
        string? PlayingMusicName { get; }
        /// <summary>
        /// 当前服务端目录下的音乐文件列表
        /// key为音乐文件名，value为音乐文件的完整路径
        /// </summary>
        IReadOnlyDictionary<string, string> MusicFiles { get; }
        /// <summary>
        /// 更新音乐文件列表，若音乐文件夹路径发生变化，则需要调用此方法更新音乐文件列表
        /// </summary>
        /// <returns></returns>
        bool UpdateMusicFiles();
        /// <summary>
        /// 更新音乐文件列表，并指定新的音乐文件夹路径，若音乐文件夹路径发生变化，则需要调用此方法更新音乐文件列表
        /// </summary>
        /// <param name="newMusicFolderPath">新的目录</param>
        /// <returns></returns>
        bool UpdateMusicFiles(string newMusicFolderPath);
        /// <summary>
        /// 播放指定的音乐文件
        /// </summary>
        /// <param name="musicName">音乐文件的路径</param>
        /// <returns></returns>
        ValueTask PlayAsync(string musicName);
        /// <summary>
        /// 暂停播放
        /// </summary>
        /// <returns></returns>
        ValueTask PauseAsync();
        /// <summary>
        /// 恢复播放
        /// </summary>
        /// <returns></returns>
        ValueTask ResumeAsync();
        /// <summary>
        /// 停止播放
        /// </summary>
        /// <returns></returns>
        ValueTask StopAsync();
        /// <summary>
        /// 快进或快退到指定的时间位置
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        ValueTask SeekAsync(TimeSpan position);
    }
}
