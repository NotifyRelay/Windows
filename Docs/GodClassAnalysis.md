# Win PC 项目 — 巨石类/上帝类分析报告

> 生成时间：20260915  
> 分析范围：`Win/src/` 下所有 C# 源文件（排除 obj/bin/构建产物）  
> 分析方法：行数统计 + 方法数统计 + 职责领域识别

---

## 一、判定标准

| 等级 | 条件 |
|------|------|
| 🔴 严重（必须拆分） | 单类 > 800 行 且 职责 ≥ 4 个无关领域 |
| 🟡 警告（建议拆分） | 单类 > 500 行 且 职责 ≥ 3 个 |
| 🟢 建议优化 | 单文件包含多个类，或单类接近阈值 |

---

## 二、文件行数排行（Top 14，仅源码）

| 排名 | 文件 | 行数 | 方法数 | 类/结构数 |
|------|------|------|--------|-----------|
| 1 | `Overlay/Models/Render/SuperIslandParamV2Parser.cs` | **908** | 43 | 1 |
| 2 | `Platforms/Windows/Services/WindowsPlaybackService.cs` | **900** | 30 | 1 |
| 3 | `Services/ScreenMirrorService.cs` | **762** | 10 | 1 |
| 4 | `ViewModels/Settings/DeviceSettingsViewModel.cs` | **606** | 3 | 1 |
| 5 | `Native/NativeCore.cs` | **583** | 33 | 1（静态类） |
| 6 | `Services/FileTransferService.cs` | **565** | 19 | 1 |
| 7 | `Helpers/AppLifecycleHelper.cs` | **532** | 15 | 1（静态类） |
| 8 | `Services/Settings/GeneralSettingsService.cs` | **531** | 5 | 1 |
| 9 | `ViewModels/MainPageViewModel.cs` | **527** | 19 | 1 |
| 10 | `Platforms/Windows/Services/WindowsNotificationHandler.cs` | **491** | — | 1 |
| 11 | `Services/NetworkService.cs` | **430** | 15 | 1 |
| 12 | `Services/LocalNotificationListenerService.cs` | **418** | — | 1 |
| 13 | `Converters/Converters.cs` | **414** | 32 | **19** |
| 14 | `Services/HeartRate/HeartRateBleService.cs` | **379** | — | 1 |


---

## 三、🔴 严重级 — 上帝类分析

（原 3.1 `WindowsPlaybackService.cs` 已整体迁移至 WindowsPlaybackServiceSplitPlan.md）

---

### 3.4 `GeneralSettingsService.cs`（531行，~80个配置属性）

**单一职责违反：8+ 个配置域集中在一个类**

| 配置域 | 属性数量 | 示例 |
|--------|----------|------|
| 通用/启动/主题 | ~5 | `StartupOption`, `Theme`, `AdbPath`, `ScrcpyPath` |
| DeepSeek 相关 | ~7 | `DeepSeekApiToken`, `EnableDeepSeekBalanceMonitor`, ... |
| 弹幕叠加层 | ~15 | `DanmakuFontSizePercent`, `DanmakuSpeed`, `DanmakuOpacityPercent`, ... |
| 心率叠加层 | ~10 | `HeartRateOverlayEnabled`, `HeartRateStyle`, `HeartRateTargetScreen`, ... |
| 动态灯效 | ~4 | `EnableDynamicLighting`, `EnableAutoRGB`, ... |
| Logi 电池 | ~5 | `LogiBatteryEnabled`, `LogiBatteryTargetScreen`, ... |
| 时钟叠加层 | ~7 | `ClockOverlayEnabled`, `ClockTargetScreen`, ... |
| 键盘叠加层 | ~2 | `KeyboardOverlayEnabled`, `KeyboardMappings` |

**建议拆分：**
```
GeneralSettingsService（瘦身后~120行，保留通用/启动/主题）
├── DanmakuSettingsAccessor.cs          — 弹幕相关 ~15 属性
├── HeartRateSettingsAccessor.cs        — 心率叠加层 ~10 属性
├── DeepSeekSettingsAccessor.cs         — DeepSeek ~7 属性
├── ClockSettingsAccessor.cs            — 时钟叠加层 ~7 属性
├── LogiBatterySettingsAccessor.cs      — Logi 电池 ~5 属性
├── DynamicLightingSettingsAccessor.cs  — 动态灯效 ~4 属性
└── KeyboardSettingsAccessor.cs         — 键盘叠加层 ~2 属性
```

---

## 四、🟡 警告级 — 巨石类分析

### 4.1 `NativeCore.cs`（583行，33方法，静态类）

**问题**：作为静态类，混合了 Rust FFI 初始化、密钥管理、配对协议、音频 FFI、回调注册、网络状态管理等所有底层桥接逻辑。

**职责分布：**

| 职责领域 | 代表方法 |
|----------|----------|
| FFI 初始化/上下文 | `Initialize`, `Context`, `GetGitHash` |
| 密钥管理 | `GenerateKeypair`, `HasKeypair`, `DeriveSharedSecret`, `MigrateSharedSecret` |
| 配对协议 | `SendHandshake`, `SendPairingInit`, `SendPairingResp`, `SendAccept`, `SendReject`, `ClearPairingCode` |
| 核心生命周期 | `StartCore`, `RemoveDevice`, `RemoveDeviceSession`, `ImportState` |
| 回调注册 | `RegisterCallbacks`, `SetLogCallback` |
| 音频 FFI | `AudioStart`, `AudioWriteFrame`, `AudioStop`, `AudioIsActive`, `RegisterAudioCallbacks` |
| 网络/设备管理 | `OnNetworkChanged`, `AddKnownDevice`, `RemoveKnownDevice`, `PeriodicBroadcast` |
| 状态推送 | `PushSuperIslandState`, `PushMediaState`, `EnqueueMessage` |

**建议拆分：**
```
NativeCore（瘦身后~120行，保留初始化/上下文管理）
├── NativeKeyManager.cs        — 密钥生成/派生/迁移 FFI
├── NativePairingBridge.cs     — 配对协议 FFI 调用
├── NativeAudioBridge.cs       — 音频 FFI 调用
├── NativeCallbacks.cs         — 回调注册与分发
└── NativeDeviceBridge.cs      — 设备/网络状态 FFI 调用
```

---

### 4.2 `ScreenMirrorService.cs`（862行，10方法）

**问题**：单个 `StartScrcpy` 方法极其庞大（~330行），混合了进程管理、密码缓存、配置构建、对话框交互。

**建议拆分：**
```
ScreenMirrorService（瘦身后~250行）
├── ScrcpyProcessManager.cs   — 进程启动/监控/停止
├── ScrcpyConfigBuilder.cs    — scrcpy 命令行参数构建
└── ScrcpyPasswordCache.cs    — 密码缓存管理
```

---

### 4.3 `DeviceSettingsViewModel.cs`（606行，3方法）

**问题**：虽然方法少，但包含大量属性（剪贴板设置、通知设置、Scrcpy 设置、通用设置等），是属性膨胀型巨石类。

**建议拆分：**
```
DeviceSettingsViewModel（瘦身后~150行，保留核心设备状态）
├── ClipboardSettingsPart.cs      — 剪贴板同步相关属性
├── NotificationSettingsPart.cs   — 通知过滤相关属性
├── ScrcpySettingsPart.cs         — Scrcpy 偏好相关属性
└── DisplaySettingsPart.cs        — 显示/电池/铃声相关属性
```

---

### 4.4 `AppLifecycleHelper.cs`（532行，15方法，静态类）

**问题**：混合了应用初始化编排、DI 容器配置（`ConfigureServices` 方法 ~100行）、各子系统初始化、错误处理。

**建议拆分：**
```
AppLifecycleHelper（瘦身后~200行，保留生命周期管理）
├── ServiceCollectionConfigurator.cs  — DI 服务注册
├── AppInitializer.cs                 — 初始化步骤编排
└── ExceptionHandler.cs               — 未处理异常/启动任务处理
```

---

### 4.5 `FileTransferService.cs`（565行，19方法）

**问题**：混合了客户端发送逻辑、服务端接收逻辑、连接管理、进度追踪。

**建议拆分：**
```
FileTransferService（瘦身后~200行，保留协调逻辑）
├── FileTransferSender.cs     — 文件发送/批量发送
├── FileTransferReceiver.cs   — 文件接收/批量接收
└── FileTransferServer.cs     — TCP 服务端生命周期
```

---

## 五、🟢 建议优化

### 5.1 `Converters.cs`（414行，19个 Converter 类）

**问题**：单文件包含 19 个独立的 Converter 类。

**建议**：拆分为多个文件，按功能域分组：
```
Converters/
├── BoolConverters.cs          — BooleanToVisibility, BoolToOpacity, BoolToStatus
├── BatteryConverters.cs       — BatteryStatusToIcon, BatteryStatusToColor
├── MediaConverters.cs         — StringToImageSource, RingerModeToIcon
├── ColorConverters.cs         — ColorToStringConverter
├── NumberConverters.cs        — DoubleToPercent, CountToVisibility
└── LogiBatteryConverters.cs   — LogiBattery 相关 converters
```

---

## 六、拆分优先级路线图

```
阶段1: GeneralSettingsService 拆分（影响最广，所有设置页面都依赖它）
    ↓
阶段2: AdbService 拆分（原行数最多，职责最杂）→ 见 AdbServiceSplitPlan.md
    ↓
阶段3: SuperIslandParamV2Parser 拆分（叠加层解析，908行/43方法，仅本轮新增评估项）
    ↓
阶段4: NativeCore.cs 拆分（FFI 桥接层清理）
    ↓
阶段5: 其他文件优化（ScreenMirror, DeviceSettingsVM, FileTransfer, Converters）
```

---

## 七、拆分原则

1. **先提取后修改**：每次拆分仅移动代码，不改逻辑
2. **保持接口兼容**：`IGeneralSettingsService` 等现有接口不变
3. **编译验证**：每次拆分前后 `msbuild -p:Platform=x64` 确认无新增错误
4. **分步提交**：每个阶段独立 Git 提交
5. **DI 适配**：拆分后的新类通过 DI 注入原类，原类委托调用
