# 里程碑 3：接入 AI 识别大脑与违禁词过滤

本计划详细阐述了如何引入阿里云百炼（DashScope）实时语音识别，建立过滤内核，并与已有的系统麦克风及 OBS 联动。依照指示，当前优先完成“代码框架设计和 WebSocket 握手逻辑”。

## User Review Required

> [!IMPORTANT]
> - 本次重构会将 `MainWindow` 的界面改为**选项卡 (TabControl)** 结构，请确认是否接受这种排版风格？
> - 后续录音数据需要实时发送，为避免 UI 卡顿与网络积压，将放置在后台异步队列或单独的 Task 中进行推流。
> - 系统提示音“哔”声初步计划采用 `System.Media.SystemSounds.Beep`，如有特定短音频（如 `beep.wav`），可在后续阶段替换。

## Proposed Changes

---

### UI 与配置存取

#### [NEW] `ConfigManager.cs`
- 使用 `System.Text.Json` 读写本地的 `config.json` 文件。
- 保存的核心属性：`ApiKey`，`AppId`，`BannedWords` (字符串集合，用逗号分隔)。

#### [MODIFY] `MainWindow.xaml` & `MainWindow.xaml.cs`
- 引入 `<TabControl>` 控件。
- **Tab1 (主操作台)**：保留原有的麦克风选取、音量条、OBS 连接和开播等控件。
- **Tab2 (AI 设置)**：
  - TextBox: API Key, AppId.
  - TextBox: 违禁词列表 (多行输入).
  - Button: 保存配置.
- **关联逻辑**：在收到违禁指令时，触发本地 Beep 声并调用 OBS 闭麦 (`SetSourceMute(true)`)。

---

### 原生音频分发改造

#### [MODIFY] `AudioManager.cs`
- 现有的 `AudioManager` 只向外广播处理好的音量（`OnVolumeChanged`），需要增加一个新的事件：
  ```csharp
  // 广播抓取到的纯净 PCM 字节流
  public event EventHandler<byte[]> OnAudioDataAvailable;
  ```
- 在 `WaveIn_DataAvailable` 中，把 PCM 原生数据提取并经事件抛出。

---

### 过滤内核与 STT 识别服务

#### [NEW] `FilterEngine.cs`
- **高内聚类**，持有一个 `List<string>` 用于存储内存中的违禁词汇。
- 对外暴露 `bool Check(string recognizedText)` 方法，供上层验证。

#### [NEW] `STTService.cs`
- 封装 `ClientWebSocket` 来实现阿里的流式接口握手和数据推送。
- `ConnectAsync(string apiKey, string appId)`: 实现 Websocket 握手鉴权连接。
- `SendAudioDataAsync(byte[] pcmData)`: 用于向 WS 推送音频二进制。
- **事件推送**：建立一个长监听循环 `ReceiveLoopAsync()`，监听消息事件并触发 `OnTextRecognized`。

## Verification Plan

### Automated / Manual Verification
- 进行代码结构创建和 WebSocket 连通性测试。
- 确认音频事件分发到 STT 推流方法的完整性。
