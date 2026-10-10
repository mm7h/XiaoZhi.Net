using System.Diagnostics;
using SherpaOnnx;
using XiaoZhi.Net.Test.Helpers;

namespace XiaoZhi.Net.Test.OtherSamples
{
    internal static class Sample24_SherpaModel
    {
        private const string TestWavFile = "./audioFile/max_output_size.wav";
        private const string TTSText = "你好，这是语音合成模型的测试";
        private const string ReferenceAudio = ""; // 16 位单声道 PCM WAV；ZipVoice 还需模型目录中的 reference.txt 对应转写。

        // 使用模型目录的 kebab-case 名称，每次运行一个模型。输入语言需符合模型支持的语言。
        private const string ModelName = "qwen3-asr";

        public static Task RunAsync()
        {
            string modelName = ModelName.ToLowerInvariant();
            string category = modelName switch
            {
                "ten-vad" => "vad",
                "kokoro" or "vits" or "matcha" or "kitten" or "zip-voice" or "pocket" or "supertonic" => "tts",
                "sense-voice" or "paraformer" or "offline-transducer" or "nemo-ctc" or "whisper" or "tdnn"
                    or "tele-speech-ctc" or "moonshine" or "fire-red-asr" or "dolphin" or "zipformer-ctc" or "canary"
                    or "wenet-ctc" or "omnilingual-asr-ctc" or "med-asr-ctc" or "fun-asr-nano" or "fire-red-asr-ctc"
                    or "qwen3-asr" or "cohere-transcribe" or "online-transducer" or "online-paraformer"
                    or "online-zipformer2-ctc" or "online-nemo-ctc" or "online-tone-ctc" => "asr",
                _ => throw new ArgumentException($"不支持的 Sherpa 模型：{ModelName}", nameof(ModelName))
            };
            string modelFolder = Path.Combine(AppContext.BaseDirectory, "models", category, modelName);
            Console.WriteLine($"测试模型：{modelName}\n模型目录：{modelFolder}");

            try
            {
                if (modelName == "ten-vad")
                {
                    VadModelConfig config = new VadModelConfig();
                    config.TenVad.Model = RequiredFile(modelFolder, "model.onnx");
                    using var vad = new VoiceActivityDetector(config, 60);
                    Console.WriteLine($"Sherpa 模型 {modelName} 已成功加载（仅 VAD 加载检查）。");
                    return Task.CompletedTask;
                }

                if (category == "tts")
                {
                    RunTts(modelName, modelFolder);
                }
                else
                {
                    WaveReader reader = ReadWave(TestWavFile);
                    if (modelName.StartsWith("online-", StringComparison.Ordinal))
                    {
                        RunOnlineAsr(CreateOnlineConfig(modelName, modelFolder), reader);
                    }
                    else
                    {
                        RunOfflineAsr(CreateOfflineConfig(modelName, modelFolder), reader);
                    }
                }

                Console.WriteLine($"Sherpa 模型 {modelName} 推理验证通过，请人工核验转写或试听 WAV。");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Sherpa 模型 {modelName} 推理验证失败：{ex.Message}", ex);
            }
        }

        #region ASR Test
        private static OfflineRecognizerConfig CreateOfflineConfig(string modelName, string folder)
        {
            OfflineRecognizerConfig config = new OfflineRecognizerConfig();
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.NumThreads = 1;
            switch (modelName)
            {
                case "sense-voice":
                    config.ModelConfig.SenseVoice.Model = RequiredFile(folder, "model.onnx");
                    config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
                    break;
                case "paraformer":
                    config.ModelConfig.Paraformer.Model = RequiredFile(folder, "model.onnx");
                    break;
                case "offline-transducer":
                    config.ModelConfig.Transducer.Encoder = RequiredFile(folder, "encoder.onnx");
                    config.ModelConfig.Transducer.Decoder = RequiredFile(folder, "decoder.onnx");
                    config.ModelConfig.Transducer.Joiner = RequiredFile(folder, "joiner.onnx");
                    break;
                case "nemo-ctc":
                    config.ModelConfig.NeMoCtc.Model = RequiredFile(folder, "model.onnx");
                    break;
                case "whisper":
                    config.ModelConfig.Whisper.Encoder = RequiredFile(folder, "encoder.onnx");
                    config.ModelConfig.Whisper.Decoder = RequiredFile(folder, "decoder.onnx");
                    config.ModelConfig.Whisper.Task = "transcribe";
                    break;
                case "tdnn":
                    config.ModelConfig.Tdnn.Model = RequiredFile(folder, "model.onnx");
                    config.FeatConfig.SampleRate = 8000;
                    config.FeatConfig.FeatureDim = 23;
                    break;
                case "tele-speech-ctc":
                    config.ModelConfig.TeleSpeechCtc = RequiredFile(folder, "model.onnx");
                    break;
                case "moonshine":
                    config.ModelConfig.Moonshine.Encoder = PrepareMoonshineFile(folder, "encoder.onnx");
                    if (File.Exists(Path.Combine(folder, "merged-decoder.onnx")))
                    {
                        config.ModelConfig.Moonshine.MergedDecoder = PrepareMoonshineFile(folder, "merged-decoder.onnx");
                    }
                    else
                    {
                        config.ModelConfig.Moonshine.Preprocessor = RequiredFile(folder, "preprocessor.onnx");
                        config.ModelConfig.Moonshine.UncachedDecoder = RequiredFile(folder, "uncached-decoder.onnx");
                        config.ModelConfig.Moonshine.CachedDecoder = RequiredFile(folder, "cached-decoder.onnx");
                    }
                    break;
                case "fire-red-asr":
                    config.ModelConfig.FireRedAsr.Encoder = RequiredFile(folder, "encoder.onnx");
                    config.ModelConfig.FireRedAsr.Decoder = RequiredFile(folder, "decoder.onnx");
                    break;
                case "dolphin":
                    config.ModelConfig.Dolphin.Model = RequiredFile(folder, "model.onnx");
                    break;
                case "zipformer-ctc":
                    config.ModelConfig.ZipformerCtc.Model = RequiredFile(folder, "model.onnx");
                    break;
                case "canary":
                    config.ModelConfig.Canary.Encoder = RequiredFile(folder, "encoder.onnx");
                    config.ModelConfig.Canary.Decoder = RequiredFile(folder, "decoder.onnx");
                    config.ModelConfig.Canary.SrcLang = "en"; // 当前下载模型支持 en/es/de/fr，按输入语言调整。
                    config.ModelConfig.Canary.TgtLang = "en";
                    config.ModelConfig.Canary.UsePnc = 1;
                    break;
                case "wenet-ctc":
                    config.ModelConfig.WenetCtc.Model = RequiredFile(folder, "model.onnx");
                    break;
                case "omnilingual-asr-ctc":
                    config.ModelConfig.Omnilingual.Model = RequiredFile(folder, "model.onnx");
                    break;
                case "med-asr-ctc":
                    config.ModelConfig.MedAsr.Model = RequiredFile(folder, "model.onnx");
                    break;
                case "fun-asr-nano":
                    config.ModelConfig.FunAsrNano.EncoderAdaptor = RequiredFile(folder, "encoder-adaptor.onnx");
                    config.ModelConfig.FunAsrNano.LLM = RequiredFile(folder, "llm.onnx");
                    config.ModelConfig.FunAsrNano.Embedding = RequiredFile(folder, "embedding.onnx");
                    config.ModelConfig.FunAsrNano.Tokenizer = RequiredDirectory(folder, "tokenizer");
                    config.ModelConfig.FunAsrNano.SystemPrompt = "You are a helpful assistant.";
                    config.ModelConfig.FunAsrNano.UserPrompt = ""; // 由原生库构造中文提示，避开 Windows LPStr 的 ANSI 编码。
                    config.ModelConfig.FunAsrNano.MaxNewTokens = 512;
                    config.ModelConfig.FunAsrNano.Temperature = 1e-6F;
                    config.ModelConfig.FunAsrNano.TopP = 0.8F;
                    config.ModelConfig.FunAsrNano.Seed = 42;
                    config.ModelConfig.FunAsrNano.Itn = 1;
                    break;
                case "fire-red-asr-ctc":
                    config.ModelConfig.FireRedAsrCtc.Model = RequiredFile(folder, "model.onnx");
                    break;
                case "qwen3-asr":
                    config.ModelConfig.Qwen3Asr.ConvFrontend = RequiredFile(folder, "conv-frontend.onnx");
                    config.ModelConfig.Qwen3Asr.Encoder = RequiredFile(folder, "encoder.onnx");
                    config.ModelConfig.Qwen3Asr.Decoder = RequiredFile(folder, "decoder.onnx");
                    config.ModelConfig.Qwen3Asr.Tokenizer = RequiredDirectory(folder, "tokenizer");
                    config.ModelConfig.Qwen3Asr.MaxTotalLen = 512;
                    config.ModelConfig.Qwen3Asr.MaxNewTokens = 128;
                    config.ModelConfig.Qwen3Asr.Temperature = 1e-6F;
                    config.ModelConfig.Qwen3Asr.TopP = 0.8F;
                    config.ModelConfig.Qwen3Asr.Seed = 42;
                    break;
                case "cohere-transcribe":
                    config.ModelConfig.CohereTranscribe.Encoder = RequiredFile(folder, "encoder.onnx");
                    config.ModelConfig.CohereTranscribe.Decoder = RequiredFile(folder, "decoder.onnx");
                    config.ModelConfig.CohereTranscribe.Language = "zh"; // 必须明确指定 TestWavFile 的语言。
                    config.ModelConfig.CohereTranscribe.UsePunct = 1;
                    config.ModelConfig.CohereTranscribe.UseItn = 1;
                    break;
                default:
                    throw new ArgumentException($"不支持的离线 ASR 模型：{modelName}", nameof(modelName));
            }
            config.ModelConfig.Tokens = modelName is "fun-asr-nano" or "qwen3-asr" ? "" : RequiredFile(folder, "tokens.txt");
            return config;
        }

        private static OnlineRecognizerConfig CreateOnlineConfig(string modelName, string folder)
        {
            OnlineRecognizerConfig config = new OnlineRecognizerConfig();
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.NumThreads = 1;
            config.EnableEndpoint = 0;
            switch (modelName)
            {
                case "online-transducer":
                    config.ModelConfig.Transducer.Encoder = RequiredFile(folder, "encoder.onnx");
                    config.ModelConfig.Transducer.Decoder = RequiredFile(folder, "decoder.onnx");
                    config.ModelConfig.Transducer.Joiner = RequiredFile(folder, "joiner.onnx");
                    break;
                case "online-paraformer":
                    config.ModelConfig.Paraformer.Encoder = RequiredFile(folder, "encoder.onnx");
                    config.ModelConfig.Paraformer.Decoder = RequiredFile(folder, "decoder.onnx");
                    break;
                case "online-zipformer2-ctc":
                    config.ModelConfig.Zipformer2Ctc.Model = RequiredFile(folder, "model.onnx");
                    break;
                case "online-nemo-ctc":
                    config.ModelConfig.NemoCtc.Model = RequiredFile(folder, "model.onnx");
                    break;
                case "online-tone-ctc":
                    config.ModelConfig.ToneCtc.Model = RequiredFile(folder, "model.onnx");
                    break;
                default:
                    throw new ArgumentException($"不支持的流式 ASR 模型：{modelName}", nameof(modelName));
            }
            config.ModelConfig.Tokens = RequiredFile(folder, "tokens.txt");
            return config;
        }

        private static void RunOfflineAsr(OfflineRecognizerConfig config, WaveReader reader)
        {
            Stopwatch timer = Stopwatch.StartNew();
            using var recognizer = new OfflineRecognizer(config);
            Console.WriteLine($"模型加载耗时：{timer.Elapsed.TotalSeconds:F2} 秒");
            using OfflineStream stream = recognizer.CreateStream();
            timer.Restart();
            stream.AcceptWaveform(reader.SampleRate, reader.Samples);
            recognizer.Decode(stream);
            ReportAsrResult(stream.Result.Text, timer.Elapsed);
        }

        private static void RunOnlineAsr(OnlineRecognizerConfig config, WaveReader reader)
        {
            Stopwatch timer = Stopwatch.StartNew();
            using var recognizer = new OnlineRecognizer(config);
            Console.WriteLine($"模型加载耗时：{timer.Elapsed.TotalSeconds:F2} 秒");
            using OnlineStream stream = recognizer.CreateStream();
            timer.Restart();
            int chunkSize = Math.Max(1, reader.SampleRate / 5);
            string previousText = "";
            for (int offset = 0; offset < reader.Samples.Length; offset += chunkSize)
            {
                int count = Math.Min(chunkSize, reader.Samples.Length - offset);
                stream.AcceptWaveform(reader.SampleRate, reader.Samples.AsSpan(offset, count).ToArray());
                DecodeAvailable(recognizer, stream);
                string text = recognizer.GetResult(stream).Text;
                if (!string.IsNullOrWhiteSpace(text) && text != previousText)
                {
                    Console.WriteLine($"中间结果：{text}");
                    previousText = text;
                }
            }
            // 补充尾部静音，确保最后一个解码窗口有足够上下文。
            stream.AcceptWaveform(reader.SampleRate, new float[reader.SampleRate]);
            stream.InputFinished();
            DecodeAvailable(recognizer, stream);
            ReportAsrResult(recognizer.GetResult(stream).Text, timer.Elapsed);
        }

        private static void DecodeAvailable(OnlineRecognizer recognizer, OnlineStream stream)
        {
            while (recognizer.IsReady(stream))
            {
                recognizer.Decode(stream);
            }
        }

        private static void ReportAsrResult(string text, TimeSpan elapsed)
        {
            if (string.IsNullOrWhiteSpace(text) || !text.Any(char.IsLetterOrDigit))
            {
                throw new InvalidDataException("ASR 未返回有效文字，请检查输入音频和模型支持的语言。");
            }
            Console.WriteLine($"最终识别结果：{text}\n推理耗时：{elapsed.TotalSeconds:F2} 秒");
        } 
        #endregion

        #region TTS Test
        private static void RunTts(string modelName, string folder)
        {
            if (string.IsNullOrWhiteSpace(TTSText))
            {
                throw new ArgumentException("TTSText 不能为空。", nameof(TTSText));
            }
            OfflineTtsConfig config = new OfflineTtsConfig();
            config.Model.Provider = "cpu";
            config.Model.NumThreads = 2;
            OfflineTtsGenerationConfig generation = new OfflineTtsGenerationConfig { Speed = 1.0F, Sid = 0 };
            switch (modelName)
            {
                case "kokoro":
                    config.Model.Kokoro.Model = RequiredFile(folder, "model.onnx");
                    config.Model.Kokoro.Voices = RequiredFile(folder, "voices.bin");
                    config.Model.Kokoro.Tokens = RequiredFile(folder, "tokens.txt");
                    config.Model.Kokoro.DataDir = RequiredDirectory(folder, "espeak-ng-data");
                    config.Model.Kokoro.DictDir = RequiredDirectory(folder, "dict");
                    config.Model.Kokoro.Lexicon = string.Join(",", RequiredFile(folder, "lexicon/lexicon-zh.txt"), RequiredFile(folder, "lexicon/lexicon-us-en.txt"));
                    generation.Sid = 50;
                    break;
                case "vits":
                    config.Model.Vits.Model = RequiredFile(folder, "model.onnx");
                    config.Model.Vits.Tokens = RequiredFile(folder, "tokens.txt");
                    config.Model.Vits.Lexicon = OptionalPath(folder, "lexicon.txt");
                    config.Model.Vits.DataDir = OptionalPath(folder, "espeak-ng-data");
                    config.Model.Vits.DictDir = OptionalPath(folder, "dict");
                    config.RuleFsts = OptionalPath(folder, "rule.fst");
                    config.RuleFars = OptionalPath(folder, "rule.far");
                    break;
                case "matcha":
                    config.Model.Matcha.AcousticModel = RequiredFile(folder, "acoustic-model.onnx");
                    config.Model.Matcha.Vocoder = RequiredFile(folder, "vocoder.onnx");
                    config.Model.Matcha.Tokens = RequiredFile(folder, "tokens.txt");
                    config.Model.Matcha.Lexicon = OptionalPath(folder, "lexicon.txt");
                    config.Model.Matcha.DataDir = OptionalPath(folder, "espeak-ng-data");
                    config.Model.Matcha.DictDir = OptionalPath(folder, "dict");
                    config.RuleFsts = OptionalPath(folder, "rule.fst");
                    config.RuleFars = OptionalPath(folder, "rule.far");
                    break;
                case "kitten":
                    config.Model.Kitten.Model = RequiredFile(folder, "model.onnx");
                    config.Model.Kitten.Voices = RequiredFile(folder, "voices.bin");
                    config.Model.Kitten.Tokens = RequiredFile(folder, "tokens.txt");
                    config.Model.Kitten.DataDir = RequiredDirectory(folder, "espeak-ng-data");
                    break;
                case "zip-voice":
                    config.Model.ZipVoice.Tokens = RequiredFile(folder, "tokens.txt");
                    config.Model.ZipVoice.Encoder = RequiredFile(folder, "encoder.onnx");
                    config.Model.ZipVoice.Decoder = RequiredFile(folder, "decoder.onnx");
                    config.Model.ZipVoice.Vocoder = RequiredFile(folder, "vocoder.onnx");
                    config.Model.ZipVoice.Lexicon = RequiredFile(folder, "lexicon.txt");
                    config.Model.ZipVoice.DataDir = RequiredDirectory(folder, "espeak-ng-data");
                    config.Model.ZipVoice.FeatScale = 0.1F;
                    config.Model.ZipVoice.Tshift = 0.5F;
                    config.Model.ZipVoice.TargetRms = 0.1F;
                    config.Model.ZipVoice.GuidanceScale = 1.0F;
                    SetReferenceAudio(generation);
                    generation.ReferenceText = File.ReadAllText(RequiredFile(folder, "reference.txt")).Trim();
                    if (string.IsNullOrWhiteSpace(generation.ReferenceText))
                    {
                        throw new InvalidDataException("ZipVoice 的 reference.txt 必须包含 ReferenceAudio 的准确转写。");
                    }
                    generation.NumSteps = 4;
                    generation.Extra["min_char_in_sentence"] = "10";
                    break;
                case "pocket":
                    config.Model.Pocket.LmFlow = RequiredFile(folder, "lm-flow.onnx");
                    config.Model.Pocket.LmMain = RequiredFile(folder, "lm-main.onnx");
                    config.Model.Pocket.Encoder = RequiredFile(folder, "encoder.onnx");
                    config.Model.Pocket.Decoder = RequiredFile(folder, "decoder.onnx");
                    config.Model.Pocket.TextConditioner = RequiredFile(folder, "text-conditioner.onnx");
                    config.Model.Pocket.VocabJson = RequiredFile(folder, "vocab.json");
                    config.Model.Pocket.TokenScoresJson = RequiredFile(folder, "token-scores.json");
                    config.Model.Pocket.VoiceEmbeddingCacheCapacity = 50;
                    SetReferenceAudio(generation);
                    generation.Extra["max_reference_audio_len"] = 12;
                    break;
                case "supertonic":
                    config.Model.Supertonic.DurationPredictor = RequiredFile(folder, "duration-predictor.onnx");
                    config.Model.Supertonic.TextEncoder = RequiredFile(folder, "text-encoder.onnx");
                    config.Model.Supertonic.VectorEstimator = RequiredFile(folder, "vector-estimator.onnx");
                    config.Model.Supertonic.Vocoder = RequiredFile(folder, "vocoder.onnx");
                    config.Model.Supertonic.TtsJson = RequiredFile(folder, "tts.json");
                    config.Model.Supertonic.UnicodeIndexer = RequiredFile(folder, "unicode-indexer.bin");
                    config.Model.Supertonic.VoiceStyle = RequiredFile(folder, "voice.bin");
                    generation.NumSteps = 8;
                    generation.Extra["lang"] = "en"; // 当前 Supertonic 模型不支持中文，测试英文时仍只使用 TTSText。
                    break;
                default:
                    throw new ArgumentException($"不支持的 TTS 模型：{modelName}", nameof(modelName));
            }

            Console.WriteLine($"合成文本：{TTSText}");
            Stopwatch timer = Stopwatch.StartNew();
            using var tts = new OfflineTts(config);
            Console.WriteLine($"模型加载耗时：{timer.Elapsed.TotalSeconds:F2} 秒");
            if (tts.NumSpeakers > 0 && generation.Sid >= tts.NumSpeakers)
            {
                throw new InvalidDataException($"说话人编号 {generation.Sid} 超出范围，模型共有 {tts.NumSpeakers} 个说话人。");
            }
            timer.Restart();
            OfflineTtsGeneratedAudio audio = tts.GenerateWithConfig(TTSText, generation, (_, _, _, _) => 1);
            try
            {
                TimeSpan inferenceTime = timer.Elapsed;
                if (audio.SampleRate <= 0 || audio.NumSamples <= 0)
                {
                    throw new InvalidDataException("TTS 没有返回有效音频，请检查 TTSText 的语言和生成参数。");
                }
                double rms = ValidateSamples(audio.Samples);
                string outputFolder = Path.Combine(AppContext.BaseDirectory, "test-output", "sherpa", modelName);
                Directory.CreateDirectory(outputFolder);
                string outputPath = Path.Combine(outputFolder, $"{DateTime.Now:yyyyMMdd-HHmmss-fffffff}.wav");
                if (!audio.SaveToWaveFile(outputPath))
                {
                    throw new IOException($"保存合成音频失败：{outputPath}");
                }
                WaveReader saved = new WaveReader(outputPath);
                if (saved.SampleRate != audio.SampleRate || saved.Samples.Length != audio.NumSamples)
                {
                    throw new InvalidDataException("保存的 WAV 与合成音频的采样率或样本数不一致。");
                }
                Console.WriteLine($"推理耗时：{inferenceTime.TotalSeconds:F2} 秒\n音频：{audio.SampleRate} Hz，{audio.NumSamples / (double)audio.SampleRate:F2} 秒，RMS={rms:F6}\n输出文件：{outputPath}");
            }
            finally
            {
                audio.Dispose();
            }
        } 
        #endregion

        #region Support Methods
        private static void SetReferenceAudio(OfflineTtsGenerationConfig generation)
        {
            if (string.IsNullOrWhiteSpace(ReferenceAudio))
            {
                throw new ArgumentException("请先设置 ReferenceAudio 参考音频文件路径。", nameof(ReferenceAudio));
            }
            WaveReader reader = ReadWave(ReferenceAudio);
            generation.ReferenceAudio = reader.Samples;
            generation.ReferenceSampleRate = reader.SampleRate;
        }

        private static WaveReader ReadWave(string filePath)
        {
            string path = Path.GetFullPath(filePath, AppContext.BaseDirectory);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("缺少测试音频文件。", path);
            }
            WaveReader reader = new WaveReader(path);
            if (reader.SampleRate <= 0)
            {
                throw new InvalidDataException("测试音频采样率必须大于零。");
            }
            ValidateSamples(reader.Samples);
            Console.WriteLine($"输入音频：{path}\n采样率：{reader.SampleRate} Hz，时长：{reader.Samples.Length / (double)reader.SampleRate:F2} 秒");
            return reader;
        }

        private static double ValidateSamples(float[] samples)
        {
            if (samples.Length == 0)
            {
                throw new InvalidDataException("音频样本不能为空。");
            }
            double energy = 0;
            foreach (float sample in samples)
            {
                if (!float.IsFinite(sample))
                {
                    throw new InvalidDataException("音频包含 NaN 或 Infinity。");
                }
                energy += (double)sample * sample;
            }
            double rms = Math.Sqrt(energy / samples.Length);
            if (rms <= 1e-5)
            {
                throw new InvalidDataException("音频为静音或音量过低。");
            }
            return rms;
        }

        private static string RequiredFile(string folder, string name)
        {
            string path = Path.Combine(folder, name);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("缺少模型文件。", path);
            }
            return path;
        }

        private static string PrepareMoonshineFile(string folder, string name)
        {
            string path = RequiredFile(folder, name);
            using var reader = new BinaryReader(File.OpenRead(path));
            byte[] header = reader.ReadBytes(8);
            if (header.Length != 8 || !header.AsSpan(4).SequenceEqual("ORTM"u8))
            {
                return path;
            }
            // 下载脚本将 ORT 文件重命名为 .onnx；ORT 按扩展名选择加载器，需要保留 .ort 副本。
            string outputFolder = Path.Combine(AppContext.BaseDirectory, "test-output", "sherpa", "moonshine");
            Directory.CreateDirectory(outputFolder);
            string ortPath = Path.Combine(outputFolder, name + ".ort");
            File.Copy(path, ortPath, true);
            return ortPath;
        }

        private static string RequiredDirectory(string folder, string name)
        {
            string path = Path.Combine(folder, name);
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"缺少模型目录：{path}");
            }
            return path;
        }

        private static string OptionalPath(string folder, string name)
        {
            string path = Path.Combine(folder, name);
            return Path.Exists(path) ? path : "";
        } 
        #endregion
    }
}
