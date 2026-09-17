# GeneralSettingsService.cs 拆分计划

> 制定日期：2026-09-17
> 目标文件：`Win/src/NotifyRelay/Services/Settings/GeneralSettingsService.cs`
> 基线提交：`31ce450`（分支 `整理项目`）
> 当前规模：**626 物理行**，~80 个配置属性，1 个类
> 目标规模：主类瘦身后 ≤ 130 行，共 8 个文件

---

## 一、拆分必要性

| 指标 | 实测值 |
|------|--------|
| 物理行数 | 626 |
| 属性数量 | ~80 |
| 方法数 | 5（`ApplyTheme` / `AddAction` / `UpdateAction` / `RemoveAction` / `SettingsKey`） |
| 实现接口 | `IGeneralSettingsService`（312 行契约）、`IOverlaySettings` |
| 外部引用 | `IGeneralSettingsService` 50 处；`GeneralSettingsService` 类型本身仅 `UserSettingsService.cs:27` 一处 `new` |

**判定**：属「属性膨胀型巨石类」。单一职责违反 8+ 个配置域。

### 1.1 配置域划分（实测行号）

| 配置域 | 行号区间 | 行数 | 属性数 |
|--------|----------|------|--------|
| 通用/启动/主题/路径/Actions/显示器 | 15~198（含 41~198） | ~120 | ~15 |
| DeepSeek | 199~255 | ~57 | ~7 |
| 弹幕叠加层 | 256~382 | ~127 | ~21 |
| 心率叠加层 | 383~473 | ~91 | ~15 |
| 动态灯效 | 474~509 | ~36 | ~6 |
| 键盘叠加层 | 517~528 | ~12 | ~2 |
| 罗技电池叠加层 | 529~572 | ~44 | ~7 |
| 时间浮窗叠加层 | 573~626 | ~54 | ~9 |

---

## 二、采用策略：`partial` 分文件（不做子服务提取）

**这是本计划的核心决策，必须遵守。**

分析文档建议拆为 `DanmakuSettingsAccessor` 等 7 个 Accessor 子类。**本项目不采用该路径**，理由：

1. `IGeneralSettingsService` 是 312 行的**单一接口**，被 50 处消费者依赖。若把属性拆到不同类，则每个消费者（设置页面、Overlay 服务、Worker 服务）都要改为注入多个新接口 —— 改动面从「1 个文件」膨胀到「50+ 处调用点」，超出「仅移动代码、不改逻辑」的拆分原则。
2. `UserSettingsService.cs:27` 直接 `new GeneralSettingsService(Configuration)`，且 DI 中以 `IGeneralSettingsService` / `IOverlaySettings` 双契约注册（`AppLifecycleHelper.cs:495-496`）。引入子服务需要改动 DI 注册与两个接口的转发实现。
3. 本类的 80 个属性是**同构的配置读写转发**（`_configuration.Get/Set(SettingsKey(nameof(X)))`），不存在逻辑耦合；行数问题纯粹来自「属性集中在一个文件」，`partial` 分文件即可完全解决，且**零行为变更**。
4. `SettingsKey()` 是 `private`，`partial` 各分文件共享同一类作用域，无需提升可见性；若拆为子类则必须提升为 `internal`/`public`，属 API 变更。

> 若后续确实需要真正解耦（按域注入），应作为独立任务另行评估，不在本次范围。

---

## 三、目标结构

```
Services/Settings/
├── GeneralSettingsService.cs                    ← 主文件（瘦身至 ~120 行）
│      类声明、_configuration/_uiSettings/_isApplyingTheme、
│      构造函数、SettingsKey、StartupOption、Theme、ApplyTheme、
│      ReceivedFilesPath、ScrcpyPath、AdbPath、MediaMessageReceiveMode、
│      Actions、AddAction/UpdateAction/RemoveAction、
│      ControlMyMonitorPath、EnableMonitorBrightnessSync、SelectedMonitors、
│      EnableSendMediaNotifications
├── GeneralSettingsService.DeepSeek.cs           ← ~60 行（DeepSeek 7 属性 + 余额叠加层 4 属性）
├── GeneralSettingsService.Danmaku.cs            ← ~130 行（弹幕 21 属性 + GamebarRelayEnabled）
├── GeneralSettingsService.HeartRate.cs          ← ~95 行（心率 15 属性）
├── GeneralSettingsService.DynamicLighting.cs    ← ~40 行（动态灯效 6 属性）
├── GeneralSettingsService.Keyboard.cs           ← ~15 行（键盘 2 属性）
├── GeneralSettingsService.LogiBattery.cs        ← ~48 行（罗技电池 7 属性）
└── GeneralSettingsService.Clock.cs              ← ~58 行（时间浮窗 9 属性）
```

---

## 四、执行步骤

### 步骤 1：加 `partial` 关键字
`GeneralSettingsService.cs:15` 改为：
```csharp
internal sealed partial class GeneralSettingsService : IGeneralSettingsService, IOverlaySettings
```
主文件保留原 `using` 中它自身用到的部分；**各分文件各自补齐自己需要的 `using`**（弹幕/心率等纯基础类型分文件可能只需 `NotifyRelay.Data.Contracts`）。

### 步骤 2：逐域搬移属性（一次一个域，每域构建一次）
按 §1.1 的**精确行号区间**整段剪切到对应分文件，属性体与 XML 注释**逐字不改**。建议顺序（从最独立到最耦合）：

1. `Clock`（573~626，文件末尾，最易搬）
2. `LogiBattery`（529~572）
3. `DynamicLighting`（474~509）
4. `Keyboard`（517~528）
5. `HeartRate`（383~473）
6. `Danmaku`（256~382，含 229 与 529/572 两处 `// ========` 分隔注释需一并搬）
7. `DeepSeek`（199~255）

> **注意**：256 行起的弹幕域与 517~528 的键盘域在原文件中**并非连续**（474~509 动态灯效夹在中间）。搬移时严格按行号逐段处理，不要整块剪切。

### 步骤 3：处理跨域注释
原文件 229 / 529 / 572 行的 `// ======== xxx 叠加层（实现 IGeneralSettingsService 与 IOverlaySettings 共有契约） ========` 分隔注释，跟随其所属域搬入对应分文件。

---

## 五、严格禁止事项

1. **不得修改 `IGeneralSettingsService.cs`**（312 行契约）——属性签名、默认值、XML 注释一律不动。
2. **不得修改 `IOverlaySettings`**。
3. **不得改动 `UserSettingsService.cs`**、`AppLifecycleHelper.cs` 的 DI 注册。
4. **不得新建 Accessor 子类或新接口**；不得把 `SettingsKey` 改为 `internal`/`public`。
5. **不得改任何属性的 getter/setter 实现**（含默认值、`ApplyTheme` 回调等）。
6. 不得改动 `ApplyTheme` 的 ~58 行实现（它涉及 `_isApplyingTheme` 重入保护与 UI 线程，风险高且不属于本拆分目标）。
7. 不得删除或重排文件内的 `#region`（原文件未使用 region，不要新增）。

---

## 六、验收标准

### 6.1 构建（每个步骤后都要跑）
```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' `
  'E:\GitHubCode\01Main\NotifyRelay\worktree\general-settings\src\NotifyRelay.sln' `
  -p:Platform=x64 -p:Configuration=Debug -v:m -nologo -nodeReuse:false -restore
```
**必须 `EXIT=0`。**

### 6.2 警告基线（由主代理在 `31ce450` 上实测，禁止自行重做基线）
基线构建 `EXIT=0`，耗时 41s。存量警告**恰好 8 条**，拆分后**不得新增**：

| # | 警告 | 位置 |
|---|------|------|
| 1 | `CS8604` | `Platforms/Windows/Services/KeyboardHookService.cs(85,13)` |
| 2~8 | `WMC1506` ×7 | `Views/Settings/OverlayLogiBatteryPage.xaml` 行 58,59,63,72,78,82,83 |

Rust 子模块另有 2 条 `dead_code` 警告 + 1 条汇总，属存量。
> 增量构建时 `CS8604` 可能被重复打印两次，比对时按**警告代码 + 文件 + 行号去重**后比较。

### 6.3 结构验收
- 主文件行数 ≤ 130。
- 所有 8 个文件均含 `partial class GeneralSettingsService`，且**只有一个** `class` 声明带基类/接口列表。
- `git diff --stat` 应显示：主文件大幅删除、7 个新文件新增，**删除行数 + 新增行数**大致相当（仅搬家，无逻辑改动）。
- 用 `git diff --color-moved` 或逐段比对确认**没有属性体被改写**。

### 6.4 提交
```powershell
cd E:\GitHubCode\01Main\NotifyRelay\worktree\general-settings
git add -A
git commit -m "refactor(settings): 按配置域将 GeneralSettingsService 拆为 partial 分文件"
```

---

## 七、风险清单

| 风险 | 说明 | 缓解 |
|------|------|------|
| 漏搬属性 | 80 个属性靠人工搬移易遗漏 | 构建后编译器会报「未实现接口成员」→ 属强校验，不会静默通过 |
| `using` 缺失 | 分文件各自需要 `NotifyRelay.Data.Enums`、`NotifyRelay.Data.Models.Actions` 等 | 每个分文件搬完立即构建 |
| 误改属性体 | 属性体高度相似，易复制错 | 使用「剪切」而非「重写」；`git diff` 复核 |
| XAML 绑定 | 属性名若被改写，`x:Bind` 编译期报错 | 属性名逐字保留 |
