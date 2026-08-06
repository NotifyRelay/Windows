# 弹幕叠加层替换壁纸层显示 — 实施计划

## 总述

将原本委托给 `Notify-Relay-Gamebar` 的显示功能（通知/媒体/SuperIsland）由 PC 应用自身完成，采用 `example/danmuku-kano` 的 Direct2D 透明分层窗口技术。旧壁纸层（`WallpaperOverlayWindow`）完全删除，不做任何保留。

## 参考文件（danmuku-kano）

| 参考文件 | 用途 |
|---|---|
| `example/danmuku-kano/danmuku-kano/Services/Direct2DDanmakuRenderer.cs` | Direct2D 渲染引擎核心（窗口创建、渲染循环、轨道管理、阴影/描边） |
| `example/danmuku-kano/danmuku-kano/Services/DanmakuStyleSettings.cs` | 弹幕样式配置模型 |
| `example/danmuku-kano/danmuku-kano/Models/NotificationItem.cs` | 通知数据模型（图标加载模式） |
| `example/danmuku-kano/danmuku-kano/danmuku-kano.csproj` | 依赖引用参考（Vortice.Direct2D1 版本） |

## 显示层级优先级

```
覆盖层 (OverlayRenderService, Direct2D, WS_EX_TOPMOST)
  ↓ 关
Gamebar 转发 (LocalSocketRelayServer → TCP)
  ↓ 关
系统通知 (Windows Toast)
```

每种内容类型（通知/媒体/SuperIsland）独立走此优先级链。优先级高的开启后，低的自动跳过。

| 覆盖层 | Gamebar转发 | 系统通知 | 实际行为 |
|--------|-------------|---------|---------|
| ✅ 开 | 任意 | 任意 | 仅 Direct2D 叠加层 |
| ❌ 关 | ✅ 开 | 任意 | 仅 Gamebar TCP 转发 |
| ❌ 关 | ❌ 关 | ✅ 开 | 仅系统通知 |
| ❌ 关 | ❌ 关 | ❌ 关 | 不显示 |

若 `GamebarRelayEnabled` 为 true（设置页开关），则**强制**同时走 Gamebar 转发，不受优先级影响。

## 技术方案

### 渲染引擎

- **库**: `Vortice.Direct2D1` v3.6.0（NuGet）
- **窗口类型**: 全屏透明分层窗口
  - `WS_EX_LAYERED` — 每像素 Alpha 合成
  - `WS_EX_TRANSPARENT` — 鼠标点击穿透
  - `WS_EX_TOPMOST` — 始终置顶
  - `WS_EX_TOOLWINDOW` — 无任务栏条目
  - `WS_EX_NOACTIVATE` — 不窃取焦点
  - `WS_POPUP` — 无边框
- **渲染管道**: DCRenderTarget ← DIB Section ← CreateDIBSection ← UpdateLayeredWindow
- **线程模型**: 专用 STA 后台线程（消息泵 + 帧循环 + DwmFlush）
- **DPI 感知**: PerMonitorV2，每帧缩放

### 区域布局（单个全屏窗口）

```
┌──────────────────────────────────────────────────────────┐
│  ┌─── 顶部居中区域 (Y=0~260px) ────────────────────┐   │
│  │                                                    │   │
│  │  Media Card (max 1):                               │   │
│  │  ┌──────────┐                                      │   │
│  │  │ 封面图    │  歌名 - 艺术家                       │   │
│  │  │ 128x128  │  ═══════●══════ 1:30 / 3:00          │   │
│  │  │ 圆角     │      ◄⏸►                             │   │
│  │  └──────────┘  ∥∥∥∥∥ (频谱动画 6 条)               │   │
│  │                                                    │   │
│  │  SuperIsland Cards (max 3, 自底部向上堆叠):          │   │
│  │  ┌──────────────────────────────────────┐          │   │
│  │  │ [★图标32] 标题 副标题               │          │   │
│  │  │ [████████████░░░░░░] 60%  ⏱ 02:30   │          │   │
│  │  └──────────────────────────────────────┘          │   │
│  └────────────────────────────────────────────────────┘   │
│                                                           │
│  弹幕区域 (Y=260 ~ 屏幕高度)                               │
│  轨道 0: ← [★AppIcon24] AppName: Title - Body text...    │
│  轨道 1: ← [★AppIcon24] AppName: Title - Body text...    │
│  轨道 2:      ← [★AppIcon24] AppName: Title - Body...    │
│  轨道 3: ← [★AppIcon24] AppName: Title - Body text...    │
│                                                           │
└──────────────────────────────────────────────────────────┘
```

## 内容类型渲染细节

### 1. 通知弹幕

| 属性 | 说明 |
|---|---|
| 出现 | 从右侧外进入（`SpawnX = overlay.Width`） |
| 动画 | 线性向左移动，速度 = `Speed × 60` px/s |
| 消失 | `x < -TotalWidth - 50` 时移除 |
| 显示 | `[应用图标24x24] AppName: Title - Body` |
| 图标 | 从本地 png 文件加载 → ID2D1Bitmap（若不存在则不显示图标） |
| 轨道 | `trackHeight = fontSize + 24`，碰撞避免 |
| 密度 | Normal=100px / More=20px / Overlap=-300px 间距 |

### 2. 媒体卡片

| 属性 | 说明 |
|---|---|
| 出现 | 淡入 300ms（opacity 0→1） |
| 更新 | 内容变化时平滑过渡 |
| 消失 | 淡出 300ms |
| 超时 | 60s 无更新自动消失 |
| 布局 | 封面图(128x128 圆角) + 歌名(粗体) + 艺术家 + 进度条 + 控制按钮 |
| 频谱 | 6 条竖线，高度逐帧随机变化 |
| 交互 | 点击 ◄⏸► → 通过 `LocalSocketRelayServer.CommandReceived` 回传 media_control 指令 |
| 封面 | 从 data:URI base64 解码或从文件加载 |

### 3. SuperIsland 卡片

| 属性 | 说明 |
|---|---|
| 出现/更新/消失 | 同媒体卡片 |
| 超时 | 10s 无更新自动移除 |
| 布局 | 图标(32x32) + 标题(粗体) + 副标题 + 附加文本 + 进度条 + 计时器 |
| 进度条 | 圆角矩形 + 百分比文字 |
| 计时器 | 4 种类型（-2 相对计数 / -1 活跃倒计时 / 2 已过时间 / 1 活跃计时器） |
| 计时器逻辑 | 移植 `SuperIslandViewModel.cs` 的计时计算 + `SuperIslandParamV2Parser.cs` 的解析 |

## OverlayRenderService 接口设计

```csharp
public class OverlayRenderService : IDisposable
{
    // 生命周期
    void Start();
    void Stop();

    // 通知弹幕
    void ShowDanmaku(string appName, string title, string body,
                     byte[]? iconPng, string deviceName);

    // 媒体卡片
    void ShowMediaCard(string deviceId, string deviceName,
                       string title, string artist,
                       byte[]? coverPng, bool isPlaying);
    void RemoveMediaCard(string deviceId);

    // SuperIsland 卡片
    void ShowSuperIsland(string sourceId, string deviceName,
                         SuperIslandState state);
    void RemoveSuperIsland(string sourceId);

    // 样式更新
    void UpdateStyle(DanmakuStyleSettings settings);
}
```

## OverlayItem 类层次

```csharp
abstract class OverlayItem : IDisposable
{
    DanmakuType Type;       // Notification / Media / SuperIsland
    double StartTime;       // Stopwatch 时间戳
    bool Active;            // 是否还应渲染
    abstract void Render(ID2D1DCRenderTarget rt);
}

class DanmakuItem : OverlayItem  // 弹幕
{
    string Text; byte[]? IconPng; DanmakuStyleSettings Settings;
    IDWriteTextLayout? TextLayout; ID2D1Bitmap? IconBitmap;
    double SpawnX; int TrackIndex; float TrackY;
    float TextWidth, TextHeight, TotalWidth;
}

class MediaCardItem : OverlayItem  // 媒体卡片
{
    string Title, Artist; byte[]? CoverPng; bool IsPlaying;
    ID2D1Bitmap? CoverBitmap; IDWriteTextLayout? TitleLayout;
    // 淡入淡出动画
}

class SuperIslandItem : OverlayItem  // SuperIsland
{
    SuperIslandState State; byte[]? IconPng; ID2D1Bitmap? IconBitmap;
    IDWriteTextLayout? TitleLayout, SubTitleLayout;
    // 进度条、计时器
}
```

## DanmakuStyleSettings 配置模型

```csharp
class DanmakuStyleSettings
{
    double FontSizePercent;   // 50-300%, 基数 36pt
    double Speed;             // 1-20
    double OpacityPercent;    // 10-100%
    double DisplayAreaPercent;// 10-100%
    int Density;              // 0=normal, 1=more, 2=overlap
    string FontFamilyName;    // 默认 "Microsoft YaHei"
    bool Bold;                // 默认 true
    Color Color;              // 弹幕色, 默认 #FFFFFF
    bool BorderEnabled;       // 默认 false
    double BorderThickness;   // 0-8
    Color BorderColor;        // 默认 #000000
    bool ShadowEnabled;       // 默认 true
    double ShadowBlur;        // 0-24 (保留字段, 渲染仅用偏移)
    double ShadowDepth;       // 0-16
    double ShadowOpacity;     // 0-100%
    Color ShadowColor;        // 默认 #000000

    // 计算属性
    double FontSize => 36 * FontSizePercent / 100.0;
    double PixelsPerSecond => Math.Max(1, Speed) * 60.0;
}
```

## IGeneralSettingsService 属性变更

### 删除（旧壁纸层）

```
WallpaperOverlayEnabled
WallpaperOverlayText
WallpaperOverlayFontSize
WallpaperOverlayTextColor
WallpaperOverlayTextAlignment
WallpaperOverlayShowControlPanel
WallpaperOverlayX / Y / Width / Height
```

### 新增（弹幕 + 覆盖层）

```csharp
// 启用开关
bool DanmakuNotificationEnabled { get; set; }     // default false
bool DanmakuMediaCardEnabled { get; set; }         // default false
bool DanmakuSuperIslandEnabled { get; set; }       // default false
bool GamebarRelayEnabled { get; set; }             // default false

// 弹幕样式
int DanmakuFontSizePercent { get; set; }           // default 100
int DanmakuSpeed { get; set; }                     // default 6
int DanmakuOpacityPercent { get; set; }            // default 100
int DanmakuDisplayAreaPercent { get; set; }        // default 100
int DanmakuDensity { get; set; }                   // default 0
string DanmakuFontFamily { get; set; }             // default "Microsoft YaHei"
bool DanmakuBold { get; set; }                     // default true
string DanmakuColor { get; set; }                  // default "#FFFFFF"

// 描边
bool DanmakuBorderEnabled { get; set; }            // default false
int DanmakuBorderThickness { get; set; }           // default 2
string DanmakuBorderColor { get; set; }            // default "#000000"

// 阴影
bool DanmakuShadowEnabled { get; set; }            // default true
int DanmakuShadowDepth { get; set; }               // default 2
int DanmakuShadowOpacity { get; set; }             // default 100
string DanmakuShadowColor { get; set; }            // default "#000000"
```

## 设置页 UI (DanmakuSettingsPage)

### 分组 1：启用

| 控件 | 绑定属性 |
|---|---|
| ToggleSwitch "通知弹幕" | `DanmakuNotificationEnabled` |
| ToggleSwitch "媒体卡片" | `DanmakuMediaCardEnabled` |
| ToggleSwitch "SuperIsland卡片" | `DanmakuSuperIslandEnabled` |
| ToggleSwitch "强制Gamebar转发" | `GamebarRelayEnabled` |

### 分组 2：弹幕样式

| 控件 | 绑定属性 | 默认 |
|---|---|---|
| 滑块 `字体大小` 50-300% | `DanmakuFontSizePercent` | 100 |
| 滑块 `速度` 1-20 | `DanmakuSpeed` | 6 |
| 滑块 `不透明度` 10-100% | `DanmakuOpacityPercent` | 100 |
| 滑块 `显示区域` 10-100% | `DanmakuDisplayAreaPercent` | 100 |
| 单选 `密度`: 正常/更多/重叠 | `DanmakuDensity` | 0 |
| ComboBox `字体` | `DanmakuFontFamily` | "Microsoft YaHei" |
| Toggle `加粗` | `DanmakuBold` | true |
| ColorPicker `弹幕颜色` | `DanmakuColor` | #FFFFFF |

### 分组 3：装饰

| 控件 | 绑定属性 | 默认 |
|---|---|---|
| Toggle `描边` + ColorPicker + 滑块厚度 0-8 | `DanmakuBorderEnabled/Color/Thickness` | false/#000000/2 |
| Toggle `阴影` + ColorPicker + 滑块深度 0-16 + 滑块不透明度 0-100% | `DanmakuShadowEnabled/Color/Depth/Opacity` | true/#000000/2/100 |

### 分组 4：操作

| 控件 | 行为 |
|---|---|
| Button "发送测试弹幕" | 调用 `ShowDanmaku("NotifyRelay", "测试", "这是一条测试弹幕通知", null, "本机")` |

## 修改文件清单（18 项）

### 依赖配置
| # | 操作 | 文件 | 说明 |
|---|---|---|---|
| 1 | 📦 改 | `src/Sefirah/NotifyRelay.csproj` | 加 Vortice.Direct2D1 v3.6.0 + WindowsDesktop.App.WindowsForms 框架引用 |

### 新建文件（4 个核心实现 + 1 个 VM）
| # | 操作 | 文件 | 说明 |
|---|---|---|---|
| 2 | ✨新 | `src/Sefirah/Services/OverlayRenderService.cs` | **核心**：Direct2D 渲染引擎，弹幕/媒体/SuperIsland 渲染 |
| 3 | ✨新 | `src/Sefirah/Models/Render/OverlayItem.cs` | OverlayItem 基类 + DanmakuItem / MediaCardItem / SuperIslandItem |
| 4 | ✨新 | `src/Sefirah/Models/Render/DanmakuStyleSettings.cs` | 弹幕样式配置 POCO |
| 5 | ✨新 | `src/Sefirah/Models/Render/SuperIslandState.cs` | SuperIsland 状态模型（移植自 Gamebar 的 SuperIslandParamV2Parser） |
| 6 | ✨新 | `src/Sefirah/ViewModels/Settings/DanmakuViewModel.cs` | 新设置页 ViewModel |

### 删除文件（3 个）
| # | 操作 | 文件 |
|---|---|---|
| 7 | 🗑删 | `src/Sefirah/Views/WallpaperOverlayWindow.xaml` |
| 8 | 🗑删 | `src/Sefirah/Views/WallpaperOverlayWindow.xaml.cs` |
| 9 | 🗑删 | `src/Sefirah/ViewModels/Settings/WallpaperOverlayViewModel.cs` |

### 修改文件（9 个）
| # | 操作 | 文件 | 说明 |
|---|---|---|---|
| 10 | 🔄改 | `src/Sefirah/Views/Settings/WallpaperOverlaySettingsPage.xaml` | 改为 DanmakuSettingsPage（内容全部替换） |
| 11 | 🔄改 | `src/Sefirah/Views/Settings/WallpaperOverlaySettingsPage.xaml.cs` | 改为 DanmakuSettingsPage.xaml.cs |
| 12 | 🔄改 | `src/Sefirah/Data/Contracts/IGeneralSettingsService.cs` | 删除壁纸层属性，新增 17 个弹幕/覆盖层属性 |
| 13 | 🔄改 | `src/Sefirah/Services/Settings/GeneralSettingsService.cs` | 实现新属性，删除旧属性 |
| 14 | 🔄改 | `src/Sefirah/Services/NotificationService.cs` | 通知流出时按优先级链分发（覆盖层→Gamebar→系统通知） |
| 15 | 🔄改 | `src/Sefirah/Services/ProtocolRouter.cs` | SuperIsland 按优先级分发 |
| 16 | 🔄改 | `src/Sefirah/Platforms/Windows/Services/WindowsPlaybackService.cs` | 媒体信息按优先级分发 |
| 17 | 🔄改 | `src/Sefirah/Helpers/AppLifecycleHelper.cs` | DI 注册 OverlayRenderService 单例，初始化调用 Start() |
| 18 | 🔄改 | `src/Sefirah/Strings/zh-CN/Resources.resw` | 删除壁纸层字符串，新增弹幕/覆盖层字符串 |

### 无需修改但涉及使用的文件
| 文件 | 说明 |
|---|---|
| `src/Sefirah/Services/LocalSocketRelayServer.cs` | 保留，通过 `GamebarRelayEnabled` 控制是否转发 |
| `src/Sefirah/Platforms/Windows/Interop/InteropHelpers.cs` | 保留现有函数（不删除旧壁纸层函数，仅不再调用） |
| `src/Sefirah/App.xaml.cs` / MainPage.xaml | 导航注册指向 DanmakuSettingsPage（由于类名变更需调整） |

## 实施步骤顺序

1. **修改 csproj** — 添加 NuGet 和框架引用
2. **建新文件** — `Models/Render/` 目录下 3 个模型文件
3. **建核心服务** — `Services/OverlayRenderService.cs`（移植 danmuku-kano 的 Direct2DDanmakuRenderer）
4. **建 ViewModel** — `DanmakuViewModel.cs`
5. **替换设置页** — 修改 WallpaperOverlaySettingsPage → DanmakuSettingsPage（XAML + CS）
6. **修改接口** — `IGeneralSettingsService.cs` 增删属性
7. **修改实现** — `GeneralSettingsService.cs`
8. **修改分发逻辑** — `NotificationService.cs` + `ProtocolRouter.cs` + `WindowsPlaybackService.cs`
9. **修改 DI** — `AppLifecycleHelper.cs`
10. **删除旧文件** — 3 个壁纸层文件
11. **更新资源** — `Resources.resw`
12. **编译验证** — dotnet build 确认无错误

## 编译后验证

- 启动应用，进入设置 → 弹幕叠加层设置页
- 开启"通知弹幕"，发送测试弹幕 → 确认 TOPMOST 窗口显示弹幕
- 从 Android 发一条通知 → 确认弹幕滚动
- 播放音乐 → 确认顶部媒体卡片出现
- 触发 SuperIsland → 确认顶部卡片出现
- 关闭覆盖层开关 → 确认走 Gamebar 或系统通知兜底
