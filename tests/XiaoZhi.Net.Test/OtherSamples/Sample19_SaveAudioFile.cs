using XiaoZhi.Net.Server.Media;
using XiaoZhi.Net.Server.Media.Abstractions;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample19_SaveAudioFile
    {
        private const string InputPcmFilePath = @".\data\asr-cache\test.pcm";
        private const string OutputAudioFilePath = @".\data\asr-cache\test.wav";

        public static async Task RunAsync()
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

                byte[] pcmBytes = File.ReadAllBytes(InputPcmFilePath);
                bool result = await audioEditor.SaveAudioFileAsync(OutputAudioFilePath, pcmBytes, 16000, 1, 128000);
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
