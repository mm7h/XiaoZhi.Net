# 项目简介

（中文 | [English](https://translate.google.com/?hl=zh-cn&sl=auto&tl=en&op=translate)）

**XiaoZhi.Net.Server** 是基于 `.Net 8`开发的C# SDK，为 [XiaoZhi ESP32](https://github.com/78/xiaozhi-esp32) 项目提供后端服务支持。

## 快速开始 👋

### 自定义插件 👇️

插件需遵循 `SemanticKernel` 规范。
以下是一个获取当前时间的例子：

```csharp
using Microsoft.SemanticKernel;
using System.ComponentModel;

[Description("获取关于当前日期和时间插件")]
internal class GetTime
{
    [KernelFunction, Description("获取当前的日期和时间")]
    public DateTime GetNowTime()
    {
        return DateTime.Now;
    }
}
```

### 快速创建 Xiao Zhi 服务 👇️

```csharp
IHost? serverHost = null;
// 获取服务引擎构建器
IServerBuilder serverBuilder = EngineFactory.CreateServerBuilder();
try
{
    Console.WriteLine("Hello, Xiao Zhi!");

    string configJson = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "configs", "config.json"));

    // 快速从json文件中获取配置信息
    XiaoZhiConfig? config = Newtonsoft.Json.JsonConvert.DeserializeObject<XiaoZhiConfig>(configJson);
    if (config is not null)
    {
#if DEBUG
        // 为了方便调试，你可以在环境变量中设置 LLM 的 API Key
        string? apiKey = Environment.GetEnvironmentVariable("OPEN_AI_API_KEY", EnvironmentVariableTarget.User);
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.WriteLine("Please set the environment variable \"OPEN_AI_API_KEY\"");
            return;
        }
        config.LlmSettings.First().Config.ApiKey = apiKey;
#endif

        // 开始初始化服务
        serverHost = serverBuilder.Initialize(config)
            // 添加插件
            .WithPlugin<PlayMusic>(nameof(PlayMusic))
            .WithPlugin<GetTime>(nameof(GetTime))
            //构建服务引擎
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
}
```

## 目前已测试通过的小智客户端 🖥️

- [`python`](https://github.com/Huang-junsen/py-xiaozhi)
- [`C#`](https://github.com/zhulige/xiaozhi-sharp)


## 功能清单 ✨

### 已实现 ✅

- **通信协议**
  基于 [xiaozhi-esp32](https://ccnphfhqs21z.feishu.cn/wiki/M0XiwldO9iJwHikpXD5cEx71nKh) 协议，通过 WebSocket 实现数据交互。
- **对话交互**
  支持唤醒对话、手动对话及实时打断。长时间无对话时自动休眠
- **多语言识别**
  默认使用本地模型 `Sense Voice`，可支持国语、粤语、英语等。
- **LLM 模块**
  支持灵活切换 LLM 模块，目前只实现了 `ChatGLMLLM`，理论上支持Open AI Api规范的LLM都可以快速接入。
- **TTS 模块**
  默认使用本地模型 `Kokoro`。
- **记忆功能**
  以设备和当前连接为单位的记忆缓存。
- **自定义发送文字内容**
  可以向指定的设备发送文字内容，并让设备朗读出来。

### 后续开发 🚧

- [ ] 更多的本地模型支持
- [ ] 意图识别
- [ ] 音乐播放
- [ ] 对接更多第三方 LLM、TTS 服务
- [ ] IOT功能
- [ ] MCP功能
- [ ] 智控台管理（ [Abp](https://github.com/abpframework/abp) ）

## 本项目已接入的平台/组件列表 📋

### LLM 语言模型

| 类型  |   平台名称|  使用方式|收费模式|备注|
|:---:|:---------:|:----:|:----:|:--:|
| LLM |智谱（ChatGLMLLM）|openai 接口调用|免费|需要[申请密钥](https://bigmodel.cn/usercenter/proj-mgmt/apikeys)|

---

### TTS 语音合成

| 类型  |平台 / 模型 名称| 使用方式 |收费模式   |备注|
|:---:|:---------:|:----:|:----:|:--:|
| TTS |**[Kokoro (multi-lang-v1\_1)](https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-multi-lang-v1_1.tar.bz2)**| 本地调用 |    免费    ||

---

### VAD 语音活动检测

| 类型  |   模型名称    | 使用方式 | 收费模式 | 备注 |
|:---:|:---------:|:----:|:----:|:--:|
| VAD | **[SileroVAD (v5)](https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad_v5.onnx)**| 本地调用 |  免费  |    |

---

### ASR 语音识别

| 类型  |   平台 / 模型 名称    | 使用方式 | 收费模式 | 备注 |
|:---:|:---------:|:----:|:----:|:--:|
| ASR |**[Sense Voice (2024-07-17)](https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17.tar.bz2)**| 本地调用 |  免费  |    |

---

### Punctuation 标点符号添加

| 类型  |   平台 / 模型 名称    | 使用方式 | 收费模式 | 备注 |
|:---:|:---------:|:----:|:----:|:--:|
| PUN |**[CT Transformer (2024-04-12)]([https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17.tar.bz2](https://github.com/k2-fsa/sherpa-onnx/releases/download/punctuation-models/sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12.tar.bz2))**| 本地调用 |  免费  |  对ASR识别出的文字添加标点，便于LLM理解  |

---

### Memory 记忆存储

|   类型   |      平台名称       | 使用方式 |   收费模式   | 备注 |
|:------:|:---------------:|:----:|:--------:|:--:|
| Memory |本地缓存| 本地调用 | 免费 |  服务端停止后所有记忆将会丢失  |

---

## 注意事项⚠️

### 一、文件目录

```
.
├── configs
│   └── config.json 主配置文件
├── data
│   └── tts-cache # 当开启保存tts生成的文件后，生成的语音将会保存在这里
├── logs # 系统日志文件
├── models  # 所有模型存放的目录
│   ├── asr  # 模型类型
│   │   └── sense-voice  # 模型名称文件夹
│   │       ├── model.onnx  # 模型文件
│   │       └── tokens.txt  # 模型所需tokens文件
│   ├── punctuation
│   │   └── ct-transformer
│   │       └── model.onnx  # 模型文件
│   ├── tts
│   │   └── kokoro
│   │       ├── dict
│   │       ├── espeak-ng-data
│   │       ├── lexicon
│   │       │   └── lexicon-xxxx.txt  # 余下的3个txt文件
│   │       ├── model.onnx  # 模型文件
│   │       ├── tokens.txt  # 模型所需tokens文件
│   │       └── voices.bin  # 模型音色文件
│   └── vad
│       └── silero
│           └── model.onnx  # 模型文件
└── Demo.Server.exe # 测试主程序
```

### 二、模型下载

从上面的模型列表中下载好本地模型后，在models文件夹中，根据文件目录结构将模型放在对应的文件夹中。

*注意模型文件`.onnx`需要统一命名为`model.onnx`

### 三、程序运行

点击`Demo.Server.exe`运行后，将会在控制台中显示当前监听的`websocket`地址，将其复制到你的小智客户端中即可。
如果需要完整打印服务端日志，可以在`config.json`中将`LogSetting`项的`LogLevel`改为`DEBUG`。

## 贡献🙌

本项目初衷是为 `.Net` 生态贡献一份力，抛砖引玉。

由于目前只实现了基础功能，在项目使用中如果遇到任何问题，欢迎提交 Issues 和 Pull Requests！

## 特别鸣谢

| 项目名称|
|:---:|
|[xiaozhi esp32](https://github.com/78/xiaozhi-esp32) |
|[xiaozhi-esp32-server](https://github.com/xinnan-tech/xiaozhi-esp32-server)|
|[sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)|
|[SuperSocket](https://github.com/kerryjiang/SuperSocket)|

## 许可证📝

[MIT License](https://github.com/mm7h/XiaoZhi.Net/blob/main/LICENSE)

