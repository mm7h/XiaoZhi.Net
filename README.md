<div align="center">

<img src="docs/assets/logo.png" alt="XiaoZhi.Net Logo" width="128" height="128" />

# XiaoZhi.Net

**为小智 ESP32 搭建属于自己的 C# 语音服务**

[![.NET](https://img.shields.io/badge/.NET-8.0-7355dd?logo=dotnet)](https://dotnet.microsoft.com/)
[![Releases](https://img.shields.io/github/v/release/mm7h/XiaoZhi.Net?logo=github)](https://github.com/mm7h/XiaoZhi.Net/releases/latest)
[![Issues](https://img.shields.io/github/issues/mm7h/XiaoZhi.Net)](https://github.com/mm7h/XiaoZhi.Net/issues)
[![License](https://img.shields.io/github/license/mm7h/XiaoZhi.Net?logo=mit)](LICENSE)

中文 | [English](README_en.md)

</div>

## 项目简介 📖

**XiaoZhi.Net.Server** 是面向 [小智 ESP32](https://github.com/78/xiaozhi-esp32) 的 C# 服务端 SDK，核心语音服务基于 .NET 8，通过 [WebSocket 协议](https://ccnphfhqs21z.feishu.cn/wiki/M0XiwldO9iJwHikpXD5cEx71nKh) 接收设备输入，完成语音识别、模型对话、工具调用与语音合成。


## 文档导航 📚

| 我想要了解… | 从这里开始 |
| --- | --- |
| 配置文件、模型选择与参数 | [⚙️ 配置文件参考](docs/00.configuration-file.md) |
| 接入自己的模型实现 | [🧩 如何扩展自定义模型](docs/01.extend-custom-model.md) |
| 下载本地模型、确认目录和文件名 | [🎙️ Sherpa-onnx 本地模型](docs/02.sherpa-models.md) |
| 运行包、Docker、源码发布与 GitHub Actions | [🚀 服务部署](docs/03.server-development.md) |
| 导入资料与接入向量存储 | [🔎 RAG 文档检索](docs/04.rag.md) |
| 设备拍照、图片上传与视觉模型 | [👁️ 视觉分析](docs/05.vision.md) |
| 保存聊天记录与替换记忆存储 | [🧠 聊天记忆保存](docs/06.chat-memory.md) |
| 新增工具、会话控制与调用模式 | [🔌 Function Tool 扩展](docs/07.function-tools.md) |

## 功能清单 ✨

### 已实现 ✅

| 功能名称 | 当前能力 |
| --- | --- |
| 🗣️ 语音对话 | 实时／手动语音输入、对话打断、流式 TTS 播报；无语音超时可自动断开连接，多语种能力取决于所选模型 |
| 🔌 自定义工具与意图识别 | 注册全局或会话 Function Tool；支持主模型 `FunctionCall` 和独立意图模型 `IntentLlm` |
| 🌐 IoT / MCP | 接入设备 IoT、设备 MCP 和配置的外部 MCP 工具；可用能力取决于设备及工具服务 |
| 🧠 聊天上下文与会话保存 | SDK 维护会话内历史；示例启用 SQLite，按设备保存最近一次成功写入的非空会话 |
| 🎵 多媒体音频播放 | 通过媒体模块和 FFmpeg 解码常见音频格式；示例提供音乐列表、播放、暂停、恢复和停止工具 |
| 🎚️ 音频重采样与 TTS 设置 | 配置输出采样率；示例工具可调整部分云端 TTS 的会话音色、语速和音调，支持范围以预设为准 |
| 💾 音频文件保存 | 根据所选 ASR / TTS 实现的 `FileSavingOption` 保存输入或生成的音频 |
| 🔐 连接验证 | 支持连接鉴权、自定义验证与设备配置接入，按部署配置启用 |
| 📦 部署与发布 | 提供主服务、OTA 示例、Docker 部署入口，以及 Windows / Linux 的 x64 / ARM64 发布配置和工作流 |

### 可选扩展 🧩

| 功能名称 | 备注 |
| --- | --- |
| 🔎 RAG 知识库 | 已有检索、导入及向量存储接口；需配置 Embedding、注册存储并导入资料。示例默认未启用，可接入内存或 Qdrant |
| 👁️ 视觉分析 | 通过 `WithVision(...)` 启用独立 HTTP 图片分析接口；需兼容的设备 MCP、JPEG 上传和视觉模型，示例默认未启用 |

> [!TIP]
> 当前示例还注册了时间、模拟天气、音乐、结束对话和 TTS 设置工具。工具是否被调用取决于所选模式和模型能力。

## 快速开始 👋

### 选择部署方式 📦

| 方式 | 适用场景 | 准备内容 |
| --- | --- | --- |
| 运行包直接运行 | 体验示例服务 | 对应平台程序、配置、所选本地模型与 FFmpeg 动态库 |
| Docker 部署 | 使用容器管理主服务和 OTA | 当前脚本面向 Linux x64 Docker 引擎；可使用运行包或源码构建 |
| 源码开发与 SDK 接入 | 修改工具或自定义宿主 | .NET 8 SDK、Git，以及所选模型与媒体资源 |

- 当前发布配置覆盖 `win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`。
- 从 [Releases](https://github.com/mm7h/XiaoZhi.Net/releases) 选择版本时，以该版本实际附件和说明为准。
- 运行包布局也可能随版本不同。
- Docker 操作、Linux 系统依赖、源码发布和生成 Release 草稿的步骤见 [服务部署](docs/03.server-development.md)。

### 准备配置与资源 ⚙️

1. 解压与操作系统和 CPU 架构对应的运行包。以下命令使用包含 `server/` 和 `ota/` 的布局。
2. 编辑 `server/Configs` 中的 `config.json` 与 `config_*.json`，选择 VAD、ASR、LLM、TTS，填写所用云服务凭据；独立意图模型还需配置对应的 LLM。
3. 下载选中的本地模型到 `server/models`。当前 VAD 模板选择 `SileroNative`，需要 `models/vad/silero-native/model.onnx`；即使 ASR / TTS 使用云服务，也要准备 VAD 模型。
4. 将对应平台的 FFmpeg 7.1 动态库放入 `server/ffmpeg`，保留提示音 `Configs/assets`，按需向 `server/musics` 放入音乐。

> [!TIP]
> 模型可从 [Resource Files](https://github.com/mm7h/XiaoZhi.Net/releases/tag/resources) 下载，或使用仓库提供的下载脚本。<br />
> 各模型的固定文件名、脚本用法和自定义模型接入见 [本地模型](docs/02.sherpa-models.md) 与 [模型扩展](docs/01.extend-custom-model.md)。

主服务会加载指定主配置、同目录按文件名排序的 `config_*.json`，最后加载可选的 `config.<环境名>.json`。默认 RAG 选择为空；启用时还需完成存储注册和资料导入，不能只填写 API Key。

### 启动服务 🚀

在两个终端中分别从解压目录启动。将示例 IP `192.168.1.100` 替换为设备能够访问的服务器 IP 或域名。

<details>
<summary> 🗔 Windows</summary>

终端一：主服务。

```powershell
Set-Location .\server
$env:DOTNET_ENVIRONMENT = 'Production'
.\XiaoZhi.Net.Sample.Server.exe --config .\Configs\config.json
```

终端二：需要 OTA 接入时启动。

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

先在解压目录恢复可执行权限：

```bash
chmod +x server/XiaoZhi.Net.Sample.Server ota/XiaoZhi.Net.Sample.OTA.Server
```

终端一：主服务。

```bash
cd server
DOTNET_ENVIRONMENT=Production ./XiaoZhi.Net.Sample.Server --config ./Configs/config.json
```

终端二：需要 OTA 接入时启动。

```bash
cd ota
ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_URLS=http://0.0.0.0:4531 \
XiaoZhi__ConfigDirectory="$PWD/../server/Configs" \
XiaoZhi__Websocket__Url=ws://192.168.1.100:4530/xiaozhi/v1/ \
./XiaoZhi.Net.Sample.OTA.Server
```

</details>

### 连接设备 🖥️

| 接入方式 | 设备填写的地址 |
| --- | --- |
| 直接 WebSocket | `ws://192.168.1.100:4530/xiaozhi/v1/` |
| OTA 示例 | `http://192.168.1.100:4531/xiaozhi/ota` |

OTA 示例用于返回连接地址和时间等信息，没有提供固件下载流程。设备能连接后，实际发起一次语音对话，检查录音、转写、模型回复与播报；端口监听或健康检查成功不代表完整对话链路已验证。

## 开发者接入与自定义工具 🛠️

### 创建 XiaoZhi 服务 👇️

下面是 `XiaoZhi.Net.Sample.Server` 项目中的简化宿主代码，复用其配置加载器、SQLite 存储和时间工具。用于替换示例的 `Program.cs` 时，保留现有项目引用及 `Configs`；自己的宿主需自行接入配置加载与存储实现。

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

当前完整示例还通过 `WithPrivateFunctionTools<T>()` 注册会话工具，并预留 RAG、视觉接入位置，见 [Program.cs](demo/XiaoZhi.Net.Sample.Server/Program.cs)。

### 声明 Function Tool 🔌

继承 `FunctionTool`，为公开实例方法添加 `Description`，再通过 `WithFunctionTools<T>()` 注册。

以下时间工具可替换示例项目中的 `GetTime`；读取的是服务器本地时间：

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
    [Description("获取当前的日期和时间")]
    [ToolBehavior(ToolAction.DirectResponse)]
    public FunctionReturn<DateTime> GetNowTime()
    {
        DateTime now = DateTime.Now;
        return new FunctionReturn<DateTime>
        {
            Result = now,
            Response = $"现在时间是 {now:yyyy-MM-dd HH:mm:ss}"
        };
    }
}
```

> [!TIP]
> 如果要声明全局工具，继承 `FunctionTool` 并通过 `WithFunctionTools<T>()` 注册，可用于声明一些全局可用的工具方法的场景，例如查询时间、天气等不依赖于会话状态的功能。<br />
> 如果要声明会话工具，继承 `PrivateFunctionTool` 并通过 `WithPrivateFunctionTools<T>()` 注册，可用于声明一些依赖于会话状态的工具方法的场景，例如操作当前设备的播放器、TTS 或连接等功能。


**更多示例见 [Function Tool 扩展](docs/07.function-tools.md)。**

## 已有测试记录的小智客户端 🖥️

- [py-xiaozhi（Python）](https://github.com/Huang-junsen/py-xiaozhi)
- [xiaozhi-sharp（C#）](https://github.com/zhulige/xiaozhi-sharp)

保留项目已有的兼容记录；客户端和固件版本变化后，仍需验证当前设备的协议、音频参数与工具能力。

## 平台与模型支持 📋


### LLM 语言模型 💬

聊天模型通过 OpenAI 兼容接口接入，当前配置提供以下平台示例；模型名称、地址和密钥见 [配置文件参考](docs/00.configuration-file.md)。

| 平台 | 密钥入口 |
| --- | --- |
| 智谱 / ChatGLM | [申请密钥](https://bigmodel.cn/usercenter/proj-mgmt/apikeys) |
| DeepSeek | [申请密钥](https://platform.deepseek.com/api_keys) |
| Doubao / 火山引擎 | [申请密钥](https://console.volcengine.com/ark/region:ark+cn-beijing/apiKey?apikey=%7B%7D) |
| Qwen / 阿里百炼 | [申请密钥](https://bailian.console.aliyun.com/?apiKey=1#/api-key) |

### ASR 语音识别 🎙️

| 平台 / 模型 | 实现范围 |
| --- | --- |
| Sherpa-onnx 离线模型 | 19 类，包含 SenseVoice、Paraformer、Whisper、Qwen3Asr 等 |
| Sherpa-onnx 流式模型 | 5 类：OnlineTransducer、OnlineParaformer、OnlineZipformer2Ctc、OnlineNemoCtc、OnlineToneCtc |
| 火山引擎 | `HuoshanUnidirectional`、`HuoshanBidirection` |
| 阿里云 | `AliyunRealtime` |

### TTS 语音合成 🔊

| 平台 / 模型 | 实现范围 |
| --- | --- |
| Sherpa-onnx | Kokoro、Vits、Matcha、Kitten、ZipVoice、Pocket、Supertonic |
| 火山引擎 | `HuoshanBidirection`、`HuoshanHttp`、`HuoshanHttpV3` |
| 阿里云 | `AliyunRealtimeTTS`、`AliyunHttp` |

### VAD 语音活动检测 🎧

| 模型 | 实现 |
| --- | --- |
| Silero | Sherpa-onnx |
| TenVad | Sherpa-onnx，默认 256 样本窗口 |
| SileroNative | Microsoft.ML.OnnxRuntime |

Sherpa 模型完整列表、固定目录及下载方式见 [本地模型文档](docs/02.sherpa-models.md)。文件名并非都为 `model.onnx`，请按所选模型准备。

### Memory 记忆存储 🧠

| 类型 | 当前行为 |
| --- | --- |
| 会话内存 | 同一次连接的聊天历史；未注册 `IAgentMemory` 时，不通过该接口跨会话保存 |
| SQLite 示例 | 已在示例注册 `SqliteAgentMemory`，按设备保存最近一次成功写入的非空会话，下次连接作为提示词参考 |
| 自定义存储 | 实现 `IAgentMemory` 并通过 `WithAgentMemory<T>()` 注册，自行决定保存、累积或摘要策略 |

> [!IMPORTANT]
> SQLite 示例不累积全部历史，也不自动提取长期偏好；详见 [聊天记忆保存](docs/06.chat-memory.md)。

## 目录与注意事项 ⚠️

### 运行目录 📁

以下为当前运行包的简化布局，模型和 FFmpeg 可从 [Resource Files](https://github.com/mm7h/XiaoZhi.Net/releases/tag/resources) 下载，缓存和数据库会在使用时生成：

```text
XiaoZhi.Net-<版本>-<RID>/
├── server/
│   ├── XiaoZhi.Net.Sample.Server[.exe] # Windows 平台
│   ├── Configs/
│   │   ├── config.json
│   │   ├── config_{vad,asr,llm,rag,tts,intent}.json
│   │   ├── config.<Environment>.json       # 可选环境覆盖
│   │   └── assets/                        # 提示音与数字播报
│   ├── models/{vad,asr,tts}/               # 所选本地模型
│   ├── ffmpeg/                            # 对应平台的动态库
│   ├── musics/                            # 本地音乐
│   ├── data/
│   │   ├── asr-cache/                     # 按保存配置生成
│   │   ├── tts-cache/                     # 按保存配置生成
│   │   └── memory/agent-memory.db          # SQLite 示例
│   └── logs/
├── ota/
│   ├── XiaoZhi.Net.Sample.OTA.Server[.exe]
│   └── appsettings.json
├── docker/
├── scripts/
└── docs/
```

配置行中的 `config_{...}.json` 表示六个独立分片文件。完整布局与升级步骤见 [服务部署](docs/03.server-development.md)。

### 运行前检查 🔍

- **地址**：`0.0.0.0` 是监听地址；设备上的 `localhost` 指向设备自身。OTA 返回地址、WebSocket 路径和防火墙端口必须匹配。
- **配置**：Release 主服务启动时传入 `--config <文件路径>`；环境覆盖与分片从主配置所在目录读取，修改后重启。当前 Debug 加载器固定读取工作目录的 `Configs/config.json`。
- **资源**：媒体模块需要 FFmpeg 7.1 动态库，不能仅提供 `ffmpeg` 可执行文件；本地模型、架构和固定文件名也需匹配。
- **路径与数据**：模型、音乐及音频缓存按各自路径配置读取或写入；SQLite 示例固定写入 `AppContext.BaseDirectory/data/memory/agent-memory.db`，升级时保留该数据，不能只依赖旧工作目录。
- **日志与录音**：在 `config.json` 设置 `LogSetting.LogLevel = DEBUG` 可增加日志细节。录音保存取决于所选 Provider 的配置，公开日志和反馈前移除密钥及个人数据。

## 后续规划 🚧

以下为后续方向，不代表已启用的功能或交付时间承诺：

- [ ] 持续扩展本地模型及第三方 ASR、LLM、TTS 服务。
- [ ] 在现有会话保存基础上，增加累积、摘要及长期偏好记忆。
- [ ] 完善 RAG 使用体验并探索 Skills 接入。
- [ ] 开发基于 [ABP](https://github.com/abpframework/abp) 的管理台。

## 贡献 🙌

项目希望为 .NET 生态提供可扩展的小智服务端。欢迎通过 [Issues](https://github.com/mm7h/XiaoZhi.Net/issues) 和 Pull Requests 反馈问题、完善文档或贡献实现。

## 特别鸣谢 ❤️

| 项目 | 用途 |
| --- | --- |
| [xiaozhi-esp32](https://github.com/78/xiaozhi-esp32) | 设备端与协议生态 |
| [xiaozhi-esp32-server](https://github.com/xinnan-tech/xiaozhi-esp32-server) | 服务端实现参考 |
| [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) | 本地语音识别、合成与 VAD |
| [SuperSocket](https://github.com/kerryjiang/SuperSocket) | WebSocket 服务基础 |
| [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) / [Microsoft.Extensions.AI](https://github.com/dotnet/extensions) | 对话工作流与 AI 工具抽象 |
| [FFmpeg](https://ffmpeg.org/) | 媒体解码、转换与混音基础 |

## 许可证 📝

本项目采用 [MIT License](LICENSE)。
