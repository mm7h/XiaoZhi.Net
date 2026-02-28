using SherpaOnnx;
using System.Diagnostics;
using XiaoZhi.Net.Test.OtherSamples;

namespace XiaoZhi.Net.Test
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            //await Sample09_MP3Player.Run();
            //await Sample10_NumberAudioPlayer.Run();
            //await Sample11_AudioMixer.Run();
            //await Sample12_AudioMixerWithTTS.Run();
            //await Sample13_AudioSubtitleSyncTracker.Run();
            //await Sample14_HuoshanBidirection.Run();
            //await Sample15_BatchAsr.Run();
            //await Sample17_HuoshanUnidirectional.Run();
            //await Sample18_HuoshanHttp.Run();
            await Sample19_SaveAudioFile.Run();
            //ModelsInit();
        }


        static void ModelsInit()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            OfflineRecognizerConfig offlineRecognizerConfig = new OfflineRecognizerConfig();
            offlineRecognizerConfig.ModelConfig.SenseVoice.Model = Path.Combine("models", "asr", "sense-voice", "model.onnx");
            offlineRecognizerConfig.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
            offlineRecognizerConfig.ModelConfig.Tokens = Path.Combine("models", "asr", "sense-voice", "tokens.txt");
            //offlineRecognizerConfig.DecodingMethod = "modified_beam_search";
            //offlineRecognizerConfig.MaxActivePaths = 4;
            //offlineRecognizerConfig.HotwordsFile = Path.Combine("models", "asr", "sense-voice", "hotwords.txt");
            //offlineRecognizerConfig.HotwordsScore = 1.5F;

            VadModelConfig vadModelConfig = new VadModelConfig();
            vadModelConfig.SileroVad.Model = Path.Combine("models", "vad", "silero", "model.onnx");
            vadModelConfig.SampleRate = 16000;

            OfflinePunctuationConfig config = new OfflinePunctuationConfig();
            config.Model.CtTransformer = Path.Combine("models", "punctuation", "ct-transformer", "model.onnx");

            var offlineTtsConfig = new OfflineTtsConfig();
            offlineTtsConfig.Model.Kokoro.Model = Path.Combine("models", "tts", "kokoro", "model.onnx");
            offlineTtsConfig.Model.Kokoro.Voices = Path.Combine("models", "tts", "kokoro", "voices.bin");
            offlineTtsConfig.Model.Kokoro.Tokens = Path.Combine("models", "tts", "kokoro", "tokens.txt");
            offlineTtsConfig.Model.Kokoro.DataDir = Path.Combine("models", "tts", "kokoro", "espeak-ng-data");
            offlineTtsConfig.Model.Kokoro.DictDir = Path.Combine("models", "tts", "kokoro", "dict");

            offlineTtsConfig.Model.Kokoro.Lexicon = Path.Combine("models", "tts", "kokoro", "lexicon", "lexicon-zh.txt");
            offlineTtsConfig.Model.NumThreads = 2;
            offlineTtsConfig.Model.Provider = "cpu";

            Console.WriteLine(stopwatch.Elapsed);




            OfflineRecognizer _offlineRecognizer = new OfflineRecognizer(offlineRecognizerConfig);
            Console.WriteLine(stopwatch.Elapsed);
            VoiceActivityDetector _vad = new VoiceActivityDetector(vadModelConfig, 60);
            Console.WriteLine(stopwatch.Elapsed);
            OfflinePunctuation _offlinePunctuation = new OfflinePunctuation(config);
            Console.WriteLine(stopwatch.Elapsed);
            OfflineTts _offlineTts = new OfflineTts(offlineTtsConfig);

            Console.WriteLine(stopwatch.Elapsed);
            stopwatch.Stop();
        }
    }
}
