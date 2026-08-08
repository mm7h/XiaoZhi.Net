using SherpaOnnx;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal class Sample16_BatchTts
    {
        private const string ModelFileFoler = "./models/kokoro";
        private const float SpeakSpped = 1.0f;
        private const int SperakerId = 50;

        public static Task Run()
        {
            TestBatch();
            return Task.CompletedTask;
        }

        public static void TestBatch()
        {
            var config = new OfflineTtsConfig();
            config.Model.Kokoro.Model = Path.Combine(ModelFileFoler, "model.onnx");
            config.Model.Kokoro.Voices = Path.Combine(ModelFileFoler, "voices.bin");
            config.Model.Kokoro.Tokens = Path.Combine(ModelFileFoler, "tokens.txt");
            config.Model.Kokoro.DataDir = Path.Combine(ModelFileFoler, "espeak-ng-data");
            config.Model.Kokoro.DictDir = Path.Combine(ModelFileFoler, "dict");
            config.Model.Kokoro.Lexicon = Path.Combine(ModelFileFoler, "./lexicon/lexicon-zh.txt") + "," + Path.Combine(ModelFileFoler, "./lexicon/lexicon-us-en.txt");
            config.Model.NumThreads = 2;
            config.Model.Provider = "cpu";

            var tts = new OfflineTts(config);

            string text1 = "床前明月光，疑是地上霜。举头望明月，低头思故乡。";
            string text2 = "唧唧复唧唧，木兰当户织。不闻机杼声，唯闻女叹息。";
            string text3 = "鹅鹅鹅, 曲项向天歌。 白毛浮绿水, 红掌拨清波。";
            string text4 = "这首诗开篇先声夺人，\"鹅!鹅!鹅!\"写出鹅的声响美，又通过\"曲项\"与\"向天\"、\"白毛\"与\"绿水\"、\"红掌\"与\"清波\"的对比写出鹅的线条美与色彩美，同时，\"歌\"、\"浮\"、\"拨\"等字又写出鹅的动态美。";

            var t1 = Task.Run(() =>
            {
                Console.WriteLine(DateTime.Now + "task1开始");
                OfflineTtsGeneratedAudio audio = tts.Generate(text1, SpeakSpped, SperakerId);

                if (File.Exists("./models/output_tts1.wav"))
                {
                    File.Delete("./models/output_tts1.wav");
                }
                audio.SaveToWaveFile("./models/output_tts1.wav");
                Console.WriteLine(DateTime.Now + "task1结束");
            });

            var t2 = Task.Run(() =>
            {
                Console.WriteLine(DateTime.Now + "task2开始");
                OfflineTtsGeneratedAudio audio = tts.Generate(text2, SpeakSpped, SperakerId);

                if (File.Exists("./models/output_tts2.wav"))
                {
                    File.Delete("./models/output_tts2.wav");
                }
                audio.SaveToWaveFile("./models/output_tts2.wav");
                Console.WriteLine(DateTime.Now + "task2结束");
            });
            var t3 = Task.Run(() =>
            {
                Console.WriteLine(DateTime.Now + "task3开始");
                OfflineTtsGeneratedAudio audio = tts.Generate(text3, SpeakSpped, SperakerId);
                if (File.Exists("./models/output_tts3.wav"))
                {
                    File.Delete("./models/output_tts3.wav");
                }
                audio.SaveToWaveFile("./models/output_tts3.wav");
                Console.WriteLine(DateTime.Now + "task3结束");
            });
            var t4 = Task.Run(() =>
            {
                Console.WriteLine(DateTime.Now + "task4开始");
                OfflineTtsGeneratedAudio audio = tts.Generate(text4, SpeakSpped, SperakerId);
                if (File.Exists("./models/output_tts4.wav"))
                {
                    File.Delete("./models/output_tts4.wav");
                }
                audio.SaveToWaveFile("./models/output_tts4.wav");
                Console.WriteLine(DateTime.Now + "task4结束");
            });
            Task.WaitAll(t1, t2, t3, t4);
            Console.WriteLine("Done");
        }
    }
}
