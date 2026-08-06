# XiaoZhi.Net Agent Guide

本文件约束在本仓库中工作的代码生成助手。目标是在不破坏实时会话链路、依赖注入生命周期和分层边界的前提下，以最小变更维护和扩展 XiaoZhi.Net。

## 项目背景

XiaoZhi.Net 是面向 XiaoZhi ESP32 生态的 .NET 8 / C# 服务端 SDK。核心服务通过 WebSocket 与客户端通信，提供实时或手动语音对话：

`客户端音频或文本 → VAD 断句 → ASR 转写 → LLM 与 Function Tool 调用 → TTS → 音频处理、混音、字幕 → Opus 音频与字幕发送`

主要能力包括设备绑定与远程私有配置、ASR/VAD/TTS/LLM Provider、音乐与通知播放、IoT/MCP、媒体处理、RAG 和示例应用。不要把尚未完整接入主链路的能力当作已上线功能；具体以当前代码和配置为准。

## 仓库地图

- `src/server/XiaoZhi.Net.Server`：核心服务端实现。
  - `Server/Management`：统一注册、构建和管理 Handler、Provider、Resource、协议、Function Tool 及其 session 生命周期。
  - `Server/Handlers`：处理管线和会话数据流；涵盖 AI Adapter 链路以及 SIP 相关处理（如存在）。
  - `Server/Providers`：按统一抽象提供具体能力，例如状态更新、音频编解码、ASR、TTS、LLM、缓存、音频处理和播放。
  - `Server/Resources`：支撑 Provider 的底层资源和全局组件，例如模型、设备绑定、音乐文件和 RAG 资源。
  - `Server/FunctionToolAdapters`：用受控的 Adapter 将 session 或 Provider 能力暴露给 Function Tool。
  - `Server/Protocol`：会话和传输协议；当前重点是 WebSocket。
- `src/server/XiaoZhi.Net.Server.Abstractions`：面向 SDK 使用方的公共配置、构建器、存储、鉴权和 Function Tool 抽象。
- `src/server/media/*`：媒体能力及其公共抽象，包含 ffmpeg 相关的重采样、混音、播放、编辑与字幕能力。
- `src/server/rag/*`：RAG 抽象、文档导入管线与 Qdrant 实现。
- `src/api/*`：管理/API 分层应用。
- `src/server/XiaoZhi.Net.Server.I18n`：日志与提示文本的多语言资源。
- `demo/*`：服务端与 OTA 服务示例；`tests/XiaoZhi.Net.Test`：测试和可运行样例。
- `docs/01.extend-custom-model.md`：新增 ASR、TTS、VAD 模型的既有扩展流程。

## 核心运行时流程

### 启动与连接

1. 使用 `EngineFactory` 获取 `IServerBuilder`，通过 `XiaoZhiConfig` 初始化并配置插件、媒体、鉴权、远程管理服务和语言。
2. `Build()` 后，由 `ResourceManager` 先构建全局资源，再由 `ProviderManager` 构建全局 Provider（例如本地模型与 ffmpeg 信息）。
3. WebSocket 建连时，`SocketSession` 创建 session 并初始化 `HelloMessageHandler`。
4. 客户端发送 `hello` 后，`ProviderManager` 按 session 初始化默认或远程私有 Provider 配置；`HandlerManager` 创建该 session 独立的 Handler 链路。
5. 初始化成功后回发 `hello`；如果客户端声明 `mcp`，再初始化客户端 MCP 能力。

### Handler 管线与中止

默认音频链路顺序为：

`AudioReceiveHandler → Audio2TextHandler → DialogueHandler → Text2AudioHandler → AudioProcessorHandler → AudioSendHandler`

`TextHandler` 可直接向 `DialogueHandler` 提交文本；`HelloMessageHandler` 负责握手与初始化。`abort` 必须同时停止当前 Handler 链路和对应 Provider 的在途处理（例如 VAD 与 TTS），而不是只停止对外发送。

不要随意调整上述顺序、绕过 `HandlerManager` 手动拼接管线，或绕过 `ProviderManager` 手动管理 session 级 Provider 生命周期。

## 分层与扩展规则

### Management

- Management 负责 DI 注册、全局构建、session 初始化、连接/关闭事件和生命周期协调；不承载具体音频或模型业务。
- Handler 是按 session 使用的数据流编排层。新的业务处理节点应放在最直接控制该数据的 Handler，而不是在转发层做表面修补。
- Resource 是底层支撑能力。全局且可复用的模型、设备绑定或文件索引应优先在此层管理。

### Provider

- 同一类 Provider 的职责必须一致，差异仅存在于内部实现。Handler 和 Function Tool 只依赖该类型的抽象接口（例如 `IAsr`、`ITts`、`IVad`），不得依赖某个具体厂商或模型类。
- 新 Provider 先定义或复用适当的接口，再实现具体类并在 `ProviderManager` 中按既有生命周期注册；不要在 Handler 中直接 `new` 具体 Provider。
- Provider 的实现可在其目录下创建子目录来承载协议、模型、辅助类和厂商实现。TTS 的 `Huoshan` 目录是可参考的组织方式。
- 对 ASR/TTS/VAD 自定义模型，遵循 `docs/01.extend-custom-model.md`：实现对应接口或 Sherpa 基类，在 `ProviderManager` 注册；Sherpa 模型还需更新 `SherpaModels`。

### Function Tool 与 Adapter

- Function Tool 不得直接接触具体 Provider 或 session 内部状态。需要调用 Provider 时，新增一个 Adapter，由其持有 Provider 抽象接口的属性，并只暴露 Tool 实际需要的方法。
- 新增的 `DefaultCallController` 必须先抽象为接口（例如 `ICallController`），再让 Handler 或 Function Tool 依赖该接口；不得让 Tool 直接依赖 `DefaultCallController`。
- Adapter 是 Function Tool 的边界：封装 session、生命周期检查和可暴露的 Provider 方法；保持接口小而专用，不把整个 Provider 或 `IServiceProvider` 泄露给 Tool。
- 按现有模式在 `FunctionToolManager` 中完成 session 级 Adapter 注入和 Function Tool 注册，确保其随 session 创建和释放。

## 代码生成约束

1. 先定位需求所属层和真正控制行为的类、方法或测试；说明关键假设。若需求存在会改变协议、生命周期、公开 API 或数据模型的歧义，先提问，不要猜测。
2. 只做与请求直接相关的最小修改。不要顺手重构、格式化无关文件、引入单次使用的抽象、臆造协议或增加未经要求的配置项。
3. 遵循 `.editorconfig` 和现有局部风格：C# 使用 4 空格、UTF-8 BOM；实例字段、属性、方法和事件访问显式使用 `this.`；私有字段使用 `_camelCase`，私有静态字段使用 `s_camelCase`；异步方法以 `Async` 结尾。
4. 新增代码注释、XML 注释和面向项目使用者的补充说明使用中文，且只在有助于理解时添加。日志与本地化文本优先复用或补充 `XiaoZhi.Net.Server.I18n` 资源，不要散落重复文案。
5. 尊重访问边界。只有确实需要 SDK 使用方扩展的契约才放入 `*.Abstractions`；服务端内部 Provider、Manager 和 Handler 的实现细节保持内部。
6. 不假设 `Memory`、`ServerMcpClient`、`McpEndpointClient` 或任何占位能力已经完整接入主流程。改动它们前先确认调用点与测试覆盖。
7. 不读取、输出、提交或硬编码密钥、令牌、远程服务密文或真实用户音频数据。示例与测试使用安全的占位配置。

## 验证与交付

每项改动都应有可检验的成功条件。优先级如下：

1. 运行或补充最贴近改动的测试；修复缺陷时优先先写出能复现它的测试。
2. 构建受影响的项目或解决方案。
3. 若无法自动测试实时音频或会话流程，执行最小可行的针对性检查，并明确说明未覆盖的外部依赖或人工验证步骤。

交付时简要说明：改动归属的模块、关键设计取舍、修改内容、已执行的验证及结果，以及仍需用户决定或未验证的事项。

## 文件与 Git 安全

- 保留用户已有的工作区改动；只编辑任务相关文件。
- 禁止批量删除文件或目录，不使用 `del /s`、`rd /s`、`rmdir /s`、`Remove-Item -Recurse` 或 `rm -rf`。
- 如确需删除，只能删除一个已核实的明确文件，并在交付中说明；批量删除请求须由用户自行执行。
- 不执行 `git reset --hard`、强制覆盖或其他会丢失用户工作的命令，除非用户明确授权。
