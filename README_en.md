# Project Introduction

([中文](README.md) | English)

**XiaoZhi.Net.Server** is a C# SDK developed using `.Net 8`, providing backend service support for the [XiaoZhi ESP32](https://github.com/78/xiaozhi-esp32) project based on the [Websocket](https://ccnphfhqs21z.feishu.cn/wiki/M0XiwldO9iJwHikpXD5cEx71nKh) protocol.

## Quick Start 👋
### Quickly Create Xiao Zhi Service 👇️

```csharp
IHost? serverHost = null;
// Get the server engine builder
IServerBuilder serverBuilder = EngineFactory.CreateXiaoZhiServerBuilder();
try
{
    Console.WriteLine("Hello, Xiao Zhi!");

    string configJson = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "configs", "config.json"));

    // Quickly get configuration information from the json file
    XiaoZhiConfig? config = JsonSerializer.Deserialize<XiaoZhiConfig>(configJson); // Some custom settings for System.Text.Json are omitted here
    if (config is not null)
    {
        // Start initializing the service
        serverHost = serverBuilder.Initialize(config)
            // Add plugin
            .WithPlugin<GetTime>(nameof(GetTime))
            // Multimedia file format support
            .WithMedia(useFFmpeg: true)
            //.WithManageApi("http://localhost:5118", "your-secret") // Please refer to the 'XiaoZhi.Net.Sample.OTA.Server' example
            // Set the language for log output
            .WithCulture("en-US") // optional, defaults to the current environment language
            // Build the server engine
            .Build();

        await serverHost.RunAsync();
    }
    else
    {
        Console.WriteLine("Cannot read the config settings.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Got an error: {ex.Message}");
}
finally
{
    if (serverHost is not null)
    {
        await serverHost.StopAsync();
    }
    Console.WriteLine("The server stopped.");
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
}
```

### Custom Plugins 👇️

Plugins must follow the `SemanticKernel` specification.
Here is an example of getting the current time:

```csharp
using Microsoft.SemanticKernel;
using System.ComponentModel;

[Description("Plugin for getting the current date and time")]
public class GetTime
{
    [KernelFunction, Description("Get the current date and time")]
    public DateTime GetNowTime()
    {
        return DateTime.Now;
    }
}
```

## Currently Tested XiaoZhi Clients 🖥️

- [`python`](https://github.com/Huang-junsen/py-xiaozhi)
- [`C#`](https://github.com/zhulige/xiaozhi-sharp)


## Feature List ✨

### Implemented ✅
| Feature Name | Description |
|:---:|:---------|
| Voice Conversation | Supports real-time/manual voice conversation, interruptible at any time, streaming TTS return, compatible with multiple languages, automatic sleep when there is no conversation for a long time |
| Custom Plugins | Supports custom plugin functions for easy LLM calling |
| IOT/MCP | Supports calling both protocols simultaneously (IOT protocol will be removed later) |
| Short-term Memory | Short-term memory cache based on device and single connection |
| Multimedia Audio Playback | Thanks to ffmpeg (v7.1.1) encoding support, it can play audio files in common formats |
| Audio Resampling | Customizable server TTS output sampling rate |
| Audio File Saving | Can be configured via configuration file to save user speech audio and TTS generated audio |
| Connection Verification | Login verification before connecting to the server based on XiaoZhi device information |

### Developing 🚧

- [ ] More local model support
- [ ] Intent recognition
- [ ] Docking with more third-party ASR, LLM, TTS services
- [ ] Long-term memory storage
- [ ] RAG knowledge base, Skills
- [ ] Intelligence Console Management ([Abp](https://github.com/abpframework/abp))

## Access Platforms / Model List 📋

### LLM Language Models
\* The following models are called using OpenAI's API specification

| Platform Name | Get Key |
|:-:|:-|
| Zhipu (ChatGLMLLM, Free) | [Request key](https://bigmodel.cn/usercenter/proj-mgmt/apikeys) |
| DeepSeek | [Request key](https://platform.deepseek.com/api_keys) |
| Doubao (Volcano Engine) | [Request key](https://console.volcengine.com/ark/region:ark+cn-beijing/apiKey?apikey=%7B%7D) |
| Qwen (Ali Bailian) | [Request key](https://bailian.console.aliyun.com/?apiKey=1#/api-key) |

---

### TTS Speech Synthesis

| Platform / Model Name | Remarks |
|:-:|:-|
| [Kokoro](https://github.com/mm7h/XiaoZhi.Net/releases/tag/resources) | Based on [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) implementation |
| Volcano Engine | Supports bidirectional websocket streaming, http calling |

---

### VAD Voice Activity Detection

| Model Name | Remarks |
|:-:|:-|
| [SileroVAD](https://github.com/mm7h/XiaoZhi.Net/releases/tag/resources) | Based on [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) implementation |
| SileroNative | Based on Microsoft.ML.OnnxRuntime implementation |

---

### ASR Speech Recognition

| Platform / Model Name | Remarks |
|:-:|:-|
| [Sense Voice](https://github.com/mm7h/XiaoZhi.Net/releases/tag/resources) | Based on [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) implementation |
| [Paraformer](https://github.com/mm7h/XiaoZhi.Net/releases/tag/resources) | Based on [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) implementation |


---

### Memory Storage

| Platform / Model Name | Remarks |
|:-:|:-|
| Memory Cache | All memories will be lost after the server stops or the connection is disconnected |

---

## Notes ⚠️

### 1. File Directory

```
.
├── configs
│   ├── assets # System voice audio files
│   │   ├── bind_codes # Number broadcast audio files
│   │   ├── bind_code.wav
│   │   ├── bind_not_found.wav
│   │   └── ...
│   └── config.json # Main configuration file
├── data
│   ├── asr-cache  # When saving audio files for asr recognition is enabled, user speech audio will be saved here
│   └── tts-cache # When saving tts generated files is enabled, generated voice will be saved here
├── ffmpeg # Store ffmpeg v7.1.1 binary files
├── logs # System log files
├── models  # Directory where all models are stored
│   ├── asr  # Model type
│   │   └── sense-voice  # Model name folder
│   │       ├── model.onnx  # Model file
│   │       └── tokens.txt  # Model required tokens file
│   ├── tts
│   │   └── kokoro
│   │       ├── dict
│   │       ├── espeak-ng-data
│   │       ├── lexicon
│   │       │   └── lexicon-xxxx.txt  # The remaining 3 txt files
│   │       ├── model.onnx  # Model file
│   │       ├── tokens.txt  # Model required tokens file
│   │       └── voices.bin  # Model voice file
│   └── vad
│       ├── silero
│       │   └── model.onnx  # Model file
│       └── silero-native
│           └── model.onnx  # Model file
├── musics  # Local music file directory
└── XiaoZhi.Net.Test.exe # Test main program


```

### 2. Model Usage

* Use packaged models

Download the packaged model resources directly in [Resource Files](https://github.com/mm7h/XiaoZhi.Net/releases/tag/resources), unzip them, and place the model files in the corresponding `models` folder.

* Use other models

If you need to access custom models, please refer to the [How to Extend Custom Models](docs/01.extend-custom-model.md) document.

\* Note that the model file `.onnx` needs to be uniformly named `model.onnx`

### 3. Program Running

After clicking `XiaoZhi.Net.Sample.Server.exe` to run, the currently listening `websocket` address will be displayed in the console. Copy it to your XiaoZhi client.

If you need to print the full server log, you can change `LogLevel` of `LogSetting` item to `DEBUG` in `config.json`.

## Contribution 🙌

The original intention of this project is to contribute to the `.Net` ecosystem and throw a brick to attract jade.

Since only basic functions are currently implemented, if you encounter any problems during the use of the project, Issues and Pull Requests are welcome!

At the same time, I will also do my best to develop / maintain this project :D

## Special Thanks

| Project Name |
|:---:|
| [xiaozhi esp32](https://github.com/78/xiaozhi-esp32) |
| [xiaozhi-esp32-server](https://github.com/xinnan-tech/xiaozhi-esp32-server)|
| [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)|
| [SuperSocket](https://github.com/kerryjiang/SuperSocket)|

## License 📝

[MIT License](https://github.com/mm7h/XiaoZhi.Net/blob/main/LICENSE)
