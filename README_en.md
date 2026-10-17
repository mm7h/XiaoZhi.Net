<div align="center">

<img src="docs/assets/logo.png" alt="XiaoZhi.Net Logo" width="128" height="128" />

# XiaoZhi.Net

**Build your own C# voice service for Xiaozhi ESP32**

[![.NET](https://img.shields.io/badge/.NET-8.0-7355dd?logo=dotnet)](https://dotnet.microsoft.com/)
[![Releases](https://img.shields.io/github/v/release/mm7h/XiaoZhi.Net?logo=github)](https://github.com/mm7h/XiaoZhi.Net/releases/latest)
[![Issues](https://img.shields.io/github/issues/mm7h/XiaoZhi.Net)](https://github.com/mm7h/XiaoZhi.Net/issues)
[![License](https://img.shields.io/github/license/mm7h/XiaoZhi.Net?logo=mit)](LICENSE)

[中文](README.md) | English

</div>

## Project Introduction 📖

**XiaoZhi.Net.Server** is a C# server SDK for [Xiaozhi ESP32](https://github.com/78/xiaozhi-esp32). The core voice service targets .NET 8 and receives device input through the [WebSocket protocol](https://ccnphfhqs21z.feishu.cn/wiki/M0XiwldO9iJwHikpXD5cEx71nKh), handling speech recognition, model conversations, tool calls, and speech synthesis.

## Documentation 📚

The detailed guides below are currently available in Chinese.

| I want to learn about… | Start here |
| --- | --- |
| Configuration files, model selection, and parameters | [⚙️ Configuration reference (Chinese)](docs/00.configuration-file.md) |
| Integrating a custom model implementation | [🧩 Custom model extensions (Chinese)](docs/01.extend-custom-model.md) |
| Downloading local models and checking layouts | [🎙️ Sherpa-onnx local models (Chinese)](docs/02.sherpa-models.md) |
| Runtime packages, Docker, source publishing, and GitHub Actions | [🚀 Server deployment (Chinese)](docs/03.server-development.md) |
| Importing documents and integrating vector stores | [🔎 RAG document retrieval (Chinese)](docs/04.rag.md) |
| Device cameras, image uploads, and vision models | [👁️ Vision analysis (Chinese)](docs/05.vision.md) |
| Saving conversations and replacing memory storage | [🧠 Chat memory (Chinese)](docs/06.chat-memory.md) |
| Adding tools, session control, and invocation modes | [🔌 Function Tool extensions (Chinese)](docs/07.function-tools.md) |
| Missing characters on the device, font downloads, and setup | [✍️ Glyph push (Chinese)](docs/08.glyph-push.md) |

## Features ✨

### Implemented ✅

| Feature | Current capabilities |
| --- | --- |
| 🗣️ Voice conversations | Real-time/manual voice input, interruption, and streaming TTS playback; configurable disconnection after voice inactivity. Language support depends on the selected models |
| ✍️ Missing character support | Send missing characters to devices that support Glyph Push; requires a server font bundle matching the device font. See the [setup guide (Chinese)](docs/08.glyph-push.md) |
| 🔌 Custom tools and intent detection | Global or session Function Tools; supports the main model's `FunctionCall` mode and a separate model in `IntentLlm` mode |
| 🌐 IoT / MCP | Device IoT, device MCP, and configured external MCP tools; available capabilities depend on the device and tool service |
| 🧠 Chat context and saved sessions | The SDK maintains session history; the sample enables SQLite to keep each device's most recent successfully saved nonempty conversation |
| 🎵 Media audio playback | The media module and FFmpeg decode common audio formats; sample tools list, play, pause, resume, and stop music |
| 🎚️ Resampling and TTS settings | Configurable output sample rate; sample tools adjust session voice, speech rate, and pitch for some cloud TTS providers, within the configured presets |
| 💾 Audio file saving | Save input or generated audio through the selected ASR / TTS implementation's `FileSavingOption` |
| 🔐 Connection verification | Connection authentication, custom verification, and device configuration integration, enabled through deployment configuration |
| 📦 Deployment and publishing | Main server, OTA sample, Docker deployment entry points, and Windows / Linux x64 / ARM64 publishing profiles and workflow |

### Optional Extensions 🧩

| Feature | Notes |
| --- | --- |
| 🔎 RAG knowledge base | Retrieval, import, and vector store interfaces are implemented; configure embeddings, register storage, and import documents. Disabled by default in the sample; supports in-memory or Qdrant integration |
| 👁️ Vision analysis | Enable a separate HTTP image analysis endpoint through `WithVision(...)`; requires compatible device MCP support, JPEG uploads, and a vision model. Disabled by default in the sample |

> [!TIP]
> The current sample also registers time, mock weather, music, conversation exit, and TTS settings tools. Tool invocation depends on the selected mode and model capabilities.

## Quick Start 👋

### Choose a Deployment Method 📦

| Method | Use case | Requirements |
| --- | --- | --- |
| Run a runtime package | Try the sample service | Matching platform binaries, configuration, selected local models, and FFmpeg shared libraries |
| Docker deployment | Manage the main server and OTA in containers | Current scripts target Linux x64 Docker engines; use a runtime package or build from source |
| Source development and SDK integration | Change tools or customize the host | .NET 8 SDK, Git, and the selected model and media resources |

- Current publishing profiles cover `win-x64`, `win-arm64`, `linux-x64`, and `linux-arm64`.
- When choosing a version from [Releases](https://github.com/mm7h/XiaoZhi.Net/releases), check its actual assets and instructions.
- Package layouts may differ between versions.
- See [server deployment (Chinese)](docs/03.server-development.md) for Docker operations, Linux system dependencies, source publishing, and creating Release drafts.

### Prepare Configuration and Resources ⚙️

1. Extract a runtime package matching your operating system and CPU architecture. The commands below use a layout with `server/` and `ota/`.
2. Edit `config.json` and `config_*.json` in `server/Configs`. Select VAD, ASR, LLM, and TTS, and supply credentials for the cloud services you use. A separate intent model also needs its referenced LLM configured.
3. Download the selected local models into `server/models`. The current VAD template selects `SileroNative`, which requires `models/vad/silero-native/model.onnx`; a VAD model is still needed when ASR / TTS use cloud services.
4. Place FFmpeg 7.1 shared libraries for the matching platform in `server/ffmpeg`. Keep the prompt audio in `Configs/assets`, and add music to `server/musics` if needed.
5. Check that `server/fonts/noto-v1-full/manifest.json` exists. New runtime packages containing the font update include these files. For older packages or source runs, download [fonts.zip](https://github.com/mm7h/XiaoZhi.Net/releases/download/resources/fonts.zip) and follow the [glyph push guide (Chinese)](docs/08.glyph-push.md) to place the files and check the configuration. To disable this feature, set `GlyphPushSetting.ManifestPath` to `null`.

> [!TIP]
> Download models from [Resource Files](https://github.com/mm7h/XiaoZhi.Net/releases/tag/resources) or use the repository's download scripts.<br />
> Fixed filenames, script usage, and custom model integration are covered in [local models (Chinese)](docs/02.sherpa-models.md) and [model extensions (Chinese)](docs/01.extend-custom-model.md).

The main server loads the specified main configuration, `config_*.json` files in the same directory sorted by filename, and then an optional `config.<environment>.json` override. The default RAG selection is empty; enabling it also requires storage registration and document import, beyond supplying an API key.

### Start the Services 🚀

Open two terminals at the extracted package directory. Replace `192.168.1.100` with a server IP address or hostname reachable from the device.

<details>
<summary> 🗔 Windows</summary>

Terminal one: main server.

```powershell
Set-Location .\server
$env:DOTNET_ENVIRONMENT = 'Production'
.\XiaoZhi.Net.Sample.Server.exe --config .\Configs\config.json
```

Terminal two: start this when OTA integration is needed.

```powershell
Set-Location .\ota
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = 'http://0.0.0.0:4531'
$env:XiaoZhi__ConfigDirectory = (Resolve-Path ..\server\Configs).Path
$env:XiaoZhi__Websocket__Url = 'ws://192.168.1.100:4530/xiaozhi/v1/'
.\XiaoZhi.Net.Sample.OTA.Server.exe
```

</details>

<details>
<summary>🐧 Linux</summary>

First restore executable permissions from the extracted directory:

```bash
chmod +x server/XiaoZhi.Net.Sample.Server ota/XiaoZhi.Net.Sample.OTA.Server
```

Terminal one: main server.

```bash
cd server
DOTNET_ENVIRONMENT=Production ./XiaoZhi.Net.Sample.Server --config ./Configs/config.json
```

Terminal two: start this when OTA integration is needed.

```bash
cd ota
ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_URLS=http://0.0.0.0:4531 \
XiaoZhi__ConfigDirectory="$PWD/../server/Configs" \
XiaoZhi__Websocket__Url=ws://192.168.1.100:4530/xiaozhi/v1/ \
./XiaoZhi.Net.Sample.OTA.Server
```

</details>

### Connect a Device 🖥️

| Connection method | Address to enter on the device |
| --- | --- |
| Direct WebSocket | `ws://192.168.1.100:4530/xiaozhi/v1/` |
| OTA sample | `http://192.168.1.100:4531/xiaozhi/ota` |

The OTA sample returns connection addresses, time, and related information; it does not provide a firmware download flow. Once connected, make a real voice request to check recording, transcription, model responses, and playback. A listening port or successful health check does not verify the entire conversation pipeline.

## Developer Integration and Custom Tools 🛠️

### Create a XiaoZhi Service 👇️

This simplified host runs within the `XiaoZhi.Net.Sample.Server` project and reuses its configuration loader, SQLite storage, and time tool. To replace the sample's `Program.cs`, keep the existing project references and `Configs` directory. A separate host needs its own configuration loading and storage integration.

```csharp
using Microsoft.Extensions.Hosting;
using XiaoZhi.Net.Sample.Server.Configs;
using XiaoZhi.Net.Sample.Server.FunctionTools;
using XiaoZhi.Net.Sample.Server.MemoryStore;
using XiaoZhi.Net.Server;
using XiaoZhi.Net.Server.Abstractions;

XiaoZhiConfig config = new(); // to your own config loader

IServerBuilder serverBuilder = EngineFactory.CreateXiaoZhiServerBuilder();
serverBuilder.Initialize(config)
    .WithAgentMemory<SqliteAgentMemory>()
    .WithFunctionTools<GetTime>()
    .WithMedia(useFFmpegAudioMixer: true)
    .WithCulture("zh-CN");

serverBuilder.HostBuilder.UseEnvironment(environmentName)
    .ConfigureAppConfiguration((_, builder) =>
    {
        builder.Sources.Clear();
        builder.AddConfiguration(configuration, shouldDisposeConfiguration: false);
    });

using IHost serverHost = serverBuilder.Build();
await serverHost.RunAsync();
```

The full sample also registers session tools through `WithPrivateFunctionTools<T>()` and includes integration points for RAG and vision. See [Program.cs](demo/XiaoZhi.Net.Sample.Server/Program.cs).

### Declare a Function Tool 🔌

Inherit from `FunctionTool`, add `Description` to public instance methods, and register the class through `WithFunctionTools<T>()`.

The time tool below can replace `GetTime` in the sample project. It reads the server's local time:

```csharp
using System;
using System.ComponentModel;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.Common.Attributes;
using XiaoZhi.Net.Server.Abstractions.Common.Contexts;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Sample.Server.FunctionTools;

public class GetTime : FunctionTool
{
    [Description("Get the current date and time")]
    [ToolBehavior(ToolAction.DirectResponse)]
    public FunctionReturn<DateTime> GetNowTime()
    {
        DateTime now = DateTime.Now;
        return new FunctionReturn<DateTime>
        {
            Result = now,
            Response = $"The current time is {now:yyyy-MM-dd HH:mm:ss}"
        };
    }
}
```

> [!TIP]
> For global tools, inherit from `FunctionTool` and register through `WithFunctionTools<T>()`. Use them for functions that are available across sessions and do not depend on session state, such as querying the time or weather.<br /><br />
> For session tools, inherit from `PrivateFunctionTool` and register through `WithPrivateFunctionTools<T>()`. Use them for functions that depend on session state, such as operating the current device's player, TTS, or connection.

**For more examples, see [Function Tool extensions (Chinese)](docs/07.function-tools.md).**

## XiaoZhi Clients with Existing Test Records 🖥️

- [py-xiaozhi (Python)](https://github.com/Huang-junsen/py-xiaozhi)
- [xiaozhi-sharp (C#)](https://github.com/zhulige/xiaozhi-sharp)

These retain the project's existing compatibility records. After client or firmware version changes, verify the current device's protocol, audio parameters, and tool capabilities.

## Platforms and Models 📋

### LLM Language Models 💬

Chat models use OpenAI-compatible APIs. The configuration currently includes the platform examples below; see the [configuration reference (Chinese)](docs/00.configuration-file.md) for model names, endpoints, and credentials.

| Platform | API key portal |
| --- | --- |
| Zhipu / ChatGLM | [Request a key](https://bigmodel.cn/usercenter/proj-mgmt/apikeys) |
| DeepSeek | [Request a key](https://platform.deepseek.com/api_keys) |
| Doubao / Volcano Engine | [Request a key](https://console.volcengine.com/ark/region:ark+cn-beijing/apiKey?apikey=%7B%7D) |
| Qwen / Alibaba Bailian | [Request a key](https://bailian.console.aliyun.com/?apiKey=1#/api-key) |

### ASR Speech Recognition 🎙️

| Platform / model | Implementations |
| --- | --- |
| Sherpa-onnx offline models | 19 types, including SenseVoice, Paraformer, Whisper, and Qwen3Asr |
| Sherpa-onnx streaming models | 5 types: OnlineTransducer, OnlineParaformer, OnlineZipformer2Ctc, OnlineNemoCtc, and OnlineToneCtc |
| Volcano Engine | `HuoshanUnidirectional`, `HuoshanBidirection` |
| Alibaba Cloud | `AliyunRealtime` |

### TTS Speech Synthesis 🔊

| Platform / model | Implementations |
| --- | --- |
| Sherpa-onnx | Kokoro, Vits, Matcha, Kitten, ZipVoice, Pocket, and Supertonic |
| Volcano Engine | `HuoshanBidirection`, `HuoshanHttp`, `HuoshanHttpV3` |
| Alibaba Cloud | `AliyunRealtimeTTS`, `AliyunHttp` |

### VAD Voice Activity Detection 🎧

| Model | Implementation |
| --- | --- |
| Silero | Sherpa-onnx |
| TenVad | Sherpa-onnx, with a default 256-sample window |
| SileroNative | Microsoft.ML.OnnxRuntime |

See [local models (Chinese)](docs/02.sherpa-models.md) for the complete Sherpa model list, fixed layouts, and download instructions. Not every model uses `model.onnx`; prepare files for the selected model.

### Memory Storage 🧠

| Type | Current behavior |
| --- | --- |
| Session memory | Chat history within one connection; without `IAgentMemory` registration, this interface does not save history across sessions |
| SQLite sample | The sample registers `SqliteAgentMemory`, keeping each device's most recent successfully saved nonempty conversation as prompt context for the next connection |
| Custom storage | Implement `IAgentMemory` and register through `WithAgentMemory<T>()`; define your own saving, accumulation, or summarization policy |

> [!IMPORTANT]
> The SQLite sample does not accumulate all history or automatically extract long-term preferences. See [chat memory (Chinese)](docs/06.chat-memory.md).

## Directory Layout and Notes ⚠️

### Runtime Directory 📁

This is a simplified layout of the current runtime package. Models and FFmpeg can be downloaded from [Resource Files](https://github.com/mm7h/XiaoZhi.Net/releases/tag/resources); caches and the database are generated when used:

```text
XiaoZhi.Net-<version>-<RID>/
├── server/
│   ├── XiaoZhi.Net.Sample.Server[.exe] # Windows platform
│   ├── Configs/
│   │   ├── config.json
│   │   ├── config_{vad,asr,llm,rag,tts,intent}.json
│   │   ├── config.<Environment>.json       # Optional environment override
│   │   └── assets/                        # Prompt audio and spoken digits
│   ├── models/{vad,asr,tts}/               # Selected local models
│   ├── ffmpeg/                            # Platform-specific shared libraries
│   ├── fonts/noto-v1-full/                 # Fonts included in new packages, with manifest.json
│   ├── musics/                            # Local music
│   ├── data/
│   │   ├── asr-cache/                     # Generated according to saving options
│   │   ├── tts-cache/                     # Generated according to saving options
│   │   └── memory/agent-memory.db          # SQLite sample
│   └── logs/
├── ota/
│   ├── XiaoZhi.Net.Sample.OTA.Server[.exe]
│   └── appsettings.json
├── docker/
├── scripts/
└── docs/
```

The `config_{...}.json` line represents six separate configuration files. See [server deployment (Chinese)](docs/03.server-development.md) for the full layout and upgrade steps.

### Before Running 🔍

- **Addresses**: `0.0.0.0` is a listening address; `localhost` on a device refers to that device. Check the OTA response address, WebSocket path, and firewall ports.
- **Configuration**: Pass `--config <file path>` when starting the Release main server. Fragments and environment overrides are read from the main configuration directory; restart after changes. The current Debug loader always reads `Configs/config.json` in the working directory.
- **Resources**: The media module needs FFmpeg 7.1 shared libraries, not just an `ffmpeg` executable. Local model files, architecture, and fixed filenames must also match.
- **Paths and data**: Models, music, and audio caches follow their configured paths. The SQLite sample writes to `AppContext.BaseDirectory/data/memory/agent-memory.db`; preserve that data during upgrades instead of relying only on the old working directory.
- **Logs and recordings**: Set `LogSetting.LogLevel = DEBUG` in `config.json` for more detail. Recording behavior depends on the selected provider's configuration. Remove credentials and personal data before sharing logs or reports.

## Roadmap 🚧

These are future directions, not enabled features or delivery commitments:

- [ ] Continue expanding local models and third-party ASR, LLM, and TTS services.
- [ ] Extend session saving with accumulated history, summaries, and long-term preferences.
- [ ] Improve the RAG experience and explore Skills integration.
- [ ] Develop a management console based on [ABP](https://github.com/abpframework/abp).

## Contributing 🙌

This project aims to provide an extensible Xiaozhi server for the .NET ecosystem, as a starting point for the community to build on.

[Issues](https://github.com/mm7h/XiaoZhi.Net/issues) and Pull Requests are welcome for bug reports, documentation improvements, and implementations.

If this project helps you, please give it a ⭐️ on GitHub.

## Special Thanks ❤️

| Project | Role |
| --- | --- |
| [xiaozhi-esp32](https://github.com/78/xiaozhi-esp32) | Device implementation and protocol ecosystem |
| [xiaozhi-esp32-server](https://github.com/xinnan-tech/xiaozhi-esp32-server) | Server implementation reference |
| [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) | Local speech recognition, synthesis, and VAD |
| [SuperSocket](https://github.com/kerryjiang/SuperSocket) | WebSocket server infrastructure |
| [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) / [Microsoft.Extensions.AI](https://github.com/dotnet/extensions) | Conversation workflows and AI tool abstractions |
| [FFmpeg](https://ffmpeg.org/) | Media decoding, conversion, and mixing infrastructure |

## License 📝

This project is licensed under the [MIT License](LICENSE).
