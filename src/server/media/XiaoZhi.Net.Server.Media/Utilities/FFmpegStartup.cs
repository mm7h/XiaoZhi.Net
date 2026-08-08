using FFmpeg.AutoGen;

namespace XiaoZhi.Net.Server.Media.Utilities
{
    internal static class FFmpegStartup
    {
        private static readonly object s_syncLock = new();
        private static Lazy<(bool Success, string Message)> s_initializer = CreateInitializer();

        internal static string FFmpegRootPath = "./ffmpeg/";

        /// <summary>
        /// 获取一个值，该值指示 FFmpeg 是否已成功初始化。
        /// </summary>
        internal static bool FFmpegInitialized
        {
            get
            {
                var lazy = Volatile.Read(ref s_initializer);
                return lazy.IsValueCreated && lazy.Value.Success;
            }
        }

        public static void RegisterFFmpegBinaries(string ffmpegBinariesPath)
        {
            if (string.IsNullOrEmpty(ffmpegBinariesPath))
            {
                throw new ArgumentNullException(nameof(ffmpegBinariesPath), "FFmpeg binaries path must not be null or empty.");
            }

            lock (s_syncLock)
            {
                ffmpeg.RootPath = FFmpegRootPath = ffmpegBinariesPath;
                Volatile.Write(ref s_initializer, CreateInitializer());
            }
        }

        public static bool CheckFFmpegInstalled(out string message)
        {
            var lazy = Volatile.Read(ref s_initializer);
            var (Success, Message) = lazy.Value;
            message = Message;
            return Success;
        }

        private static Lazy<(bool Success, string Message)> CreateInitializer()
        {
            return new Lazy<(bool, string)>(() =>
            {
                try
                {
                    ffmpeg.av_log_set_level(ffmpeg.AV_LOG_QUIET);
                    string version = ffmpeg.av_version_info();
                    return (true, version);
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }
    }
}
