using FFmpeg.AutoGen;

namespace XiaoZhi.Net.Server.Media.Utilities
{
    internal static class FFmpegStartup
    {
        private static readonly object _syncLock = new();
        private static Lazy<(bool Success, string Message)> _initializer = CreateInitializer();

        internal static string FFmpegRootPath = "./ffmpeg/";

        /// <summary>
        /// Gets a value indicating whether FFmpeg has been successfully initialized.
        /// </summary>
        internal static bool FFmpegInitialized
        {
            get
            {
                var lazy = Volatile.Read(ref _initializer);
                return lazy.IsValueCreated && lazy.Value.Success;
            }
        }

        public static void RegisterFFmpegBinaries(string ffmpegBinariesPath)
        {
            if (string.IsNullOrEmpty(ffmpegBinariesPath))
            {
                throw new ArgumentNullException(nameof(ffmpegBinariesPath), "FFmpeg binaries path must not be null or empty.");
            }

            lock (_syncLock)
            {
                ffmpeg.RootPath = FFmpegRootPath = ffmpegBinariesPath;
                Volatile.Write(ref _initializer, CreateInitializer());
            }
        }

        public static bool CheckFFmpegInstalled(out string message)
        {
            var lazy = Volatile.Read(ref _initializer);
            var result = lazy.Value;
            message = result.Message;
            return result.Success;
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
