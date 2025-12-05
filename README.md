# 项目简介

（中文 | [English](https://translate.google.com/?hl=zh-cn&sl=auto&tl=en&op=translate)）

**XiaoZhi.Net.Server** 是使用 `.Net 8`开发的C# SDK，基于[Websocket](https://ccnphfhqs21z.feishu.cn/wiki/M0XiwldO9iJwHikpXD5cEx71nKh) 协议为 [XiaoZhi ESP32](https://github.com/78/xiaozhi-esp32) 项目提供后端服务支持。

## 快速开始 👋
### 快速创建 Xiao Zhi 服务 👇️

```csharp
IHost? serverHost = null;
// 获取服务引擎构建器
IServerBuilder serverBuilder = EngineFactory.CreateXiaoZhiServerBuilder();
try
{
    Console.WriteLine("Hello, Xiao Zhi!");

    string configJson = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "configs", "config.json"));

    // 快速从json文件中获取配置信息
    XiaoZhiConfig? config = Newtonsoft.Json.JsonConvert.DeserializeObject<XiaoZhiConfig>(configJson);
    if (config is not null)
    {
        // 开始初始化服务
        serverHost = serverBuilder.Initialize(config)
            // 添加插件
            .WithPlugin<GetTime>(nameof(GetTime))
            // ffmpeg音频支持
            .InitializeFFmpeg()
            // 多媒体文件格式支持
            .WithAllMedia(useFFmpeg: true)
            // 构建服务引擎
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

### 自定义插件 👇️

插件需遵循 `SemanticKernel` 规范。
以下是一个获取当前时间的例子：

```csharp
using Microsoft.SemanticKernel;
using System.ComponentModel;

[Description("获取关于当前日期和时间插件")]
public class GetTime
{
    [KernelFunction, Description("获取当前的日期和时间")]
    public DateTime GetNowTime()
    {
        return DateTime.Now;
    }
}
```

## 目前已测试通过的小智客户端 🖥️

- [`python`](https://github.com/Huang-junsen/py-xiaozhi)
- [`C#`](https://github.com/zhulige/xiaozhi-sharp)


## 功能清单 ✨

### 已实现 ✅
| 功能名称| 说明|
|:---:|:---------|
|语音对话|支持实时/手动语音对话，可随时打断，流式TTS返回，兼容多语种，长时间无对话时自动休眠|
|自定义插件|支持自定义插件函数，方便LLM调用|
|IOT/MCP|同时支持两种协议调用（IOT协议后续将会被移除）|
|短期记忆|以设备和单次连接为单位的短期记忆缓存|
|多媒体音频播放|得力于ffmpeg编码支持，可以播放常见格式的音频文件|
|音频重采样|服务端TTS输出会以小智设备提供采样率进行音频重采样|
|连接验证|可根据小智设备信息进行连接服务器前的登入验证|

### 正在开发 🚧

- [ ] 更多的本地模型支持
- [ ] 意图识别
- [ ] 对接更多第三方 LLM、TTS 服务
- [ ] 长期记忆存储
- [ ] RAG知识库
- [ ] 智控台管理（ [Abp](https://github.com/abpframework/abp) ）

## 已接入的平台/使用的模型列表 📋

### LLM 语言模型

|平台名称|使用方式|收费模式|备注|
|:-:|:-:|:-:|:-:|
|智谱（ChatGLMLLM）|API调用|免费|需要[申请密钥](https://bigmodel.cn/usercenter/proj-mgmt/apikeys)|

---

### TTS 语音合成

|平台 / 模型名称|使用方式|收费模式|备注|
|:-:|:-:|:-:|:-:|
|[Kokoro](#)|本地调用|免费||
|[火山双向流式](#)|API调用|付费||

---

### VAD 语音活动检测

|模型名称|使用方式|收费模式|备注|
|:-:|:-:|:-:|:-:|
|[SileroVAD](#)|本地调用|免费||

---

### ASR 语音识别

|平台 / 模型名称|使用方式|收费模式|备注|
|:-:|:-:|:-:|:-:|
|[Sense Voice](#)|本地调用|免费||


---

### Memory 记忆存储

|平台 / 模型名称|使用方式|收费模式| 备注 |
|:-:|:-:|:-:|:-:|
|内存缓存|本地调用|免费|服务端停止或连接断开后所有记忆将会丢失|

---

## 注意事项⚠️

### 一、文件目录

```
.
├── configs
│   ├── assets # 系统语音音频文件
│   │   ├── bind_codes # 数字播报音频文件
│   │   ├── bind_code.wav
│   │   ├── bind_not_found.wav
│   │   └── ...
│   └── config.json # 主配置文件
├── data
│   └── tts-cache # 当开启保存tts生成的文件后，生成的语音将会保存在这里
├── ffmpeg # 存放`ffmpeg v7.x` 二进制文件
├── logs # 系统日志文件
├── models  # 所有模型存放的目录
│   ├── asr  # 模型类型
│   │   └── sense-voice  # 模型名称文件夹
│   │       ├── model.onnx  # 模型文件
│   │       └── tokens.txt  # 模型所需tokens文件
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
├── musics  # 本地音乐文件目录
└── XiaoZhi.Net.Test.exe # 测试主程序

```

### 二、模型下载

从上面的模型列表中下载好本地模型后，在models文件夹中，根据文件目录结构将模型放在对应的文件夹中。

*注意模型文件`.onnx`需要统一命名为`model.onnx`

### 三、程序运行

点击`XiaoZhi.Net.Sample.Server.exe`运行后，将会在控制台中显示当前监听的`websocket`地址，将其复制到你的小智客户端中即可。
如果需要完整打印服务端日志，可以在`config.json`中将`LogSetting`项的`LogLevel`改为`DEBUG`。

## 贡献🙌

本项目初衷是为 `.Net` 生态贡献一份力，抛砖引玉。

由于目前只实现了基础功能，在项目使用中如果遇到任何问题，欢迎提交 Issues 和 Pull Requests！

At the same time, I will also do my best to develop / maintain this project :D

## 特别鸣谢

| 项目名称|
|:---:|
|[xiaozhi esp32](https://github.com/78/xiaozhi-esp32) |
|[xiaozhi-esp32-server](https://github.com/xinnan-tech/xiaozhi-esp32-server)|
|[sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)|
|[SuperSocket](https://github.com/kerryjiang/SuperSocket)|

## 许可证📝

[MIT License](https://github.com/mm7h/XiaoZhi.Net/blob/main/LICENSE)

