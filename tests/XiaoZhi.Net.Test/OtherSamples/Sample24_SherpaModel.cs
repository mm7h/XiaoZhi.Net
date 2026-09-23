using SherpaOnnx;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal static class Sample24_SherpaModel
    {
        private const string ModelName = "ten-vad"; // ten-vad、online-paraformer、qwen3-asr 或 zip-voice
        public static Task RunAsync()
        {
            string modelFolder = ModelName.ToLowerInvariant() switch
            {
                "ten-vad" => Path.Combine("models", "vad", "ten-vad"),
                "online-paraformer" => Path.Combine("models", "asr", "online-paraformer"),
                "qwen3-asr" => Path.Combine("models", "asr", "qwen3-asr"),
                "zip-voice" => Path.Combine("models", "tts", "zip-voice"),
                _ => throw new ArgumentException("支持 ten-vad、online-paraformer、qwen3-asr 或 zip-voice。", nameof(ModelName))
            };

            switch (ModelName.ToLowerInvariant())
            {
                case "ten-vad":
                    Require(modelFolder, "model.onnx");
                    VadModelConfig vadConfig = new VadModelConfig();
                    vadConfig.TenVad.Model = Path.Combine(modelFolder, "model.onnx");
                    using (var vad = new VoiceActivityDetector(vadConfig, 60))
                    {
                    }
                    break;
                case "online-paraformer":
                    Require(modelFolder, "encoder.onnx", "decoder.onnx", "tokens.txt");
                    OnlineRecognizerConfig onlineConfig = new OnlineRecognizerConfig();
                    onlineConfig.ModelConfig.Paraformer.Encoder = Path.Combine(modelFolder, "encoder.onnx");
                    onlineConfig.ModelConfig.Paraformer.Decoder = Path.Combine(modelFolder, "decoder.onnx");
                    onlineConfig.ModelConfig.Tokens = Path.Combine(modelFolder, "tokens.txt");
                    using (var recognizer = new OnlineRecognizer(onlineConfig))
                    {
                    }
                    break;
                case "qwen3-asr":
                    Require(modelFolder, "conv-frontend.onnx", "encoder.onnx", "decoder.onnx");
                    RequireDirectory(modelFolder, "tokenizer");
                    OfflineRecognizerConfig qwenConfig = new OfflineRecognizerConfig();
                    qwenConfig.ModelConfig.Qwen3Asr.ConvFrontend = Path.Combine(modelFolder, "conv-frontend.onnx");
                    qwenConfig.ModelConfig.Qwen3Asr.Encoder = Path.Combine(modelFolder, "encoder.onnx");
                    qwenConfig.ModelConfig.Qwen3Asr.Decoder = Path.Combine(modelFolder, "decoder.onnx");
                    qwenConfig.ModelConfig.Qwen3Asr.Tokenizer = Path.Combine(modelFolder, "tokenizer");
                    qwenConfig.ModelConfig.Tokens = "";
                    using (var recognizer = new OfflineRecognizer(qwenConfig))
                    {
                    }
                    break;
                case "zip-voice":
                    Require(modelFolder, "tokens.txt", "encoder.onnx", "decoder.onnx", "vocoder.onnx", "lexicon.txt", "reference.wav", "reference.txt");
                    RequireDirectory(modelFolder, "espeak-ng-data");
                    OfflineTtsConfig zipVoiceConfig = new OfflineTtsConfig();
                    zipVoiceConfig.Model.ZipVoice.Tokens = Path.Combine(modelFolder, "tokens.txt");
                    zipVoiceConfig.Model.ZipVoice.Encoder = Path.Combine(modelFolder, "encoder.onnx");
                    zipVoiceConfig.Model.ZipVoice.Decoder = Path.Combine(modelFolder, "decoder.onnx");
                    zipVoiceConfig.Model.ZipVoice.Vocoder = Path.Combine(modelFolder, "vocoder.onnx");
                    zipVoiceConfig.Model.ZipVoice.Lexicon = Path.Combine(modelFolder, "lexicon.txt");
                    zipVoiceConfig.Model.ZipVoice.DataDir = Path.Combine(modelFolder, "espeak-ng-data");
                    using (var tts = new OfflineTts(zipVoiceConfig))
                    {
                    }
                    break;
            }

            Console.WriteLine($"Sherpa 模型 {ModelName} 已成功加载。");
            return Task.CompletedTask;
        }

        private static void Require(string folder, params string[] files)
        {
            foreach (string file in files)
            {
                string path = Path.Combine(folder, file);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("缺少模型文件。", path);
                }
            }
        }

        private static void RequireDirectory(string folder, string name)
        {
            string path = Path.Combine(folder, name);
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"缺少模型目录：{path}");
            }
        }
    }
}
