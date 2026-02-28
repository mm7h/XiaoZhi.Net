using XiaoZhi.Net.Server.Media;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample19_SaveAudioFile
    {
        const string INPUT_PCM_FILE_PATH = @".\data\asr-cache\test.pcm";
        const string OUTPUT_AUDIO_FILE_PATH = @".\data\asr-cache\test.wav";

        public static async Task Run()
        {
            MediaFactory.InitializeFFmpeg();
            bool checkResult = MediaFactory.CheckFFmpegInstalled(out string ffmpegVersion);
            if (!checkResult)
            {
                Console.WriteLine($"FFmpeg is not installed correctly: {ffmpegVersion}");
                return;
            }

            Console.WriteLine($"Installed the ffmpeg version: {ffmpegVersion}");

            try
            {
                IAudioEditor audioEditor = MediaFactory.CreateAudioEditor();

                byte[] pcmBytes = File.ReadAllBytes(INPUT_PCM_FILE_PATH);
                bool result = await audioEditor.SaveAudioFileAsync(OUTPUT_AUDIO_FILE_PATH, pcmBytes, 16000, 1, 128000);
                Console.WriteLine($"Audio file saved: {result}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Error: {ex.InnerException.Message}");
                }
            }
        }
    }
}
