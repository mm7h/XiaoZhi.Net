using FFmpeg.AutoGen;

namespace XiaoZhi.Net.Server.Media.Utilities
{
    internal static class FFmpegStartup
    {
        static string _ffmpegVersion = string.Empty;

        internal static string FFmpegRootPath = "./ffmpeg/";

        /// <summary>
        /// Gets a value indicating whether FFmpeg has been successfully initialized.
        /// </summary>
        internal static bool FFmpegInitialized { get; set; }


        public static void RegisterFFmpegBinaries(string ffmpegBinariesPath)
        {
            if (string.IsNullOrEmpty(ffmpegBinariesPath))
            {
                throw new ArgumentNullException(nameof(ffmpegBinariesPath), "FFmpeg binaries path must not be null or empty.");
            }
            ffmpeg.RootPath = FFmpegRootPath = ffmpegBinariesPath;
        }

        public static bool CheckFFmpegInstalled(out string ffmpegVersion)
        {
            try
            {
                if (FFmpegStartup.FFmpegInitialized)
                {
                    ffmpegVersion = _ffmpegVersion;
                    return true;
                }
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_QUIET);
                ffmpegVersion = _ffmpegVersion = ffmpeg.av_version_info();
                FFmpegStartup.FFmpegInitialized = true;
                return true;
            }
            catch(Exception ex)
            {
                FFmpegStartup.FFmpegInitialized = false;
                ffmpegVersion = ex.Message;
                return false;
            }
        }
    }
}
