# DeviceSettingsViewModel.cs 拆分计划

> 制定日期：2026-09-17
> 目标文件：`Win/src/NotifyRelay/ViewModels/Settings/DeviceSettingsViewModel.cs`
> 基线提交：`31ce450`（分支 `整理项目`）
> 当前规模：**678 物理行**，仅 3 个方法，~35 个属性
> 目标规模：主文件瘦身后 ≤ 180 行，共 5 个文件

---

## 一、拆分必要性（实测）

| 指标 | 实测值 |
|------|--------|
| 物理行数 | 678 |
| 方法数 | 3（`LoadApps` / `ChangeNotificationFilter` / `RemoveDevice`）+ 构造函数 |
| 类声明 | `public sealed partial class DeviceSettingsViewModel : BaseViewModel`（**已是 partial**） |
| 外部引用 | 15 处 |
| 服务依赖 | 7 个：`IAdbService`、`IDeviceSettingsService`、`RemoteAppRepository`、`ISessionManager`、`IftpService`、`IDeviceManager`，加 `Device` 属性 |

**判定**：典型「属性膨胀型巨石类」。方法极少，行数几乎全部来自属性 getter/setter。

### 1.1 属性域划分（实测行号，区域由 `#region` 标注）

| # | 区域 / 域 | 行号区间 | 行数 | 属性数 |
|---|-----------|----------|------|--------|
| 1 | `#region Display Properties` | 12~24 | ~13 | 1（`DisplayIpAddresses`，计算属性较长） |
| 2 | `#region Clipboard Settings` | 25~91 | ~67 | 5 |
| 3 | `#region Notification Settings` | 92~158 | ~67 | 5 |
| 4 | `#region Screen Mirror settings` | 159~162 | ~4 | 1 |
| 5 | `#region General Settings` | 163~176 | ~14 | 4（3 个转发 `AdbService` 选项 + 1 个字典） |
| 6 | Scrcpy 设备偏好 | 177~282 | ~106 | 9 |
| 7 | `#region Video Settings` | 283~429 | ~147 | 10 |
| 8 | `#region Audio Settings` | 430~533 | ~104 | 10 |
| 9 | `#region Media Session Settings` | 534~550 | ~17 | 1 |
| 10 | `#region ADB Settings` | 551~581 | ~31 | 2 |
| 11 | 字段 + 构造函数 + 3 方法 | 582~678 | ~97 | — |

---

## 二、采用策略：`partial` 分文件（**必须遵守**）

本类**已经是 `partial`**，且是 ViewModel（由 DI 注册、被 XAML `x:Bind` 绑定）。

**不采用**分析文档建议的 `ClipboardSettingsPart` 等「组合子对象」方案，理由：

1. 这些属性被 **XAML `x:Bind`** 直接绑定。若改为 `Settings.ClipboardSyncEnabled` 这类嵌套路径，**所有相关 XAML 绑定路径都必须改**，且 `x:Bind` 编译期检查会失败——这是纯粹的破坏性改动，不是拆文件。
2. `BaseViewModel` 提供 `SetProperty`/`OnPropertyChanged`；若拆为**子对象**，子对象需自己实现 `INotifyPropertyChanged` 并正确转发到 UI 线程，属**行为变更**，风险显著。
3. 属性体是「读取 `DeviceSettings.Xxx` / 写入 `DeviceSettings.Xxx`」的**同构转发**，不存在逻辑耦合。`partial` 分文件即可完全达成「单文件 ≤ 阈值、按域可读」的目标，且**零行为变更**。
4. `partial` 各分文件共享同一类作用域，可直接访问 `DeviceSettings`、`AdbService` 等 `private readonly` 字段，**无需提升可见性**。

---

## 三、目标结构

```
ViewModels/Settings/
├── DeviceSettingsViewModel.cs             ← 主文件（瘦身至 ~170 行）
│      类声明、7 个服务字段、Device 字段、RemoteApps 集合、
│      构造函数、LoadApps、ChangeNotificationFilter、RemoveDevice、
│      DisplayIpAddresses、MediaSessionSyncEnabled
├── DeviceSettingsViewModel.Clipboard.cs   ← ~70 行（剪贴板 5 属性）
├── DeviceSettingsViewModel.Notifications.cs ← ~70 行（通知 5 属性）
├── DeviceSettingsViewModel.Scrcpy.cs      ← ~270 行（General/Scrcpy 偏好 + Video + Audio + ADB 共 26 属性）
└── DeviceSettingsViewModel.Display.cs     ← ~25 行（Screen Mirror 1 + Media Session 1，如后续需要）
```

> **建议先把 §1.1 的 6/7/8 三个域（Scrcpy 偏好 + Video + Audio，合计 ~357 行）合并进 `Scrcpy.cs`**，因为它们同属 scrcpy 参数族，内聚度高；拆太碎反而降低可读性。

---

## 四、执行步骤

### 步骤 1：准备
1. 确认主文件 `class` 声明已含 `partial`（`line 10`，已满足）。
2. 为每个分文件**复制主文件的 `using` 区块**（`NotifyRelay.Data.AppDatabase.Repository`、`Data.Contracts`、`Data.Enums`、`Data.Items`、`Data.Models`、`Utils.Serialization`），再按需删减。
   > 分文件**必须重复写 `namespace NotifyRelay.ViewModels.Settings;`**。

### 步骤 2：逐域搬移（一次一个域，每域构建一次）
按下表顺序（**注意 `#region` 与其 `#endregion` 必须成对搬走，不要留下悬空的 `#endregion`**）：

| 顺序 | 域 | 行号 | 建议目标文件 |
|------|-----|------|--------------|
| 1 | Media Session + ADB Settings | 534~581 | `Scrcpy.cs` 或 `Display.cs` |
| 2 | Audio Settings | 430~533 | `Scrcpy.cs` |
| 3 | Video Settings | 283~429 | `Scrcpy.cs` |
| 4 | Scrcpy 设备偏好 | 177~282 | `Scrcpy.cs` |
| 5 | General Settings（`#region` 163~176） | 163~176 | `Scrcpy.cs` |
| 6 | Notification Settings | 92~158 | `Notifications.cs` |
| 7 | Clipboard Settings | 25~91 | `Clipboard.cs` |
| 8 | Screen Mirror settings | 159~162 | `Display.cs` |

> 159~162 的 `#region Screen Mirror settings` 只含 1 个属性（`IsGeneralScreenMirrorSettingsExpanded`），它夹在 92~158 通知域与 163 通用域之间。**搬移时按行号逐段处理**。

### 步骤 3：主文件收尾
搬完后主文件应只剩 §3 所列内容。确认没有多余的 `#region`/`#endregion` 残留。

---

## 五、严格禁止事项

1. **不得修改任何 XAML 文件**（本类的属性被 `DeviceSettingsPage.xaml` 等以 `x:Bind` 绑定）。
2. **不得修改属性名、类型、getter/setter 语义**（含 `SetProperty` 调用与 `DeviceSettings.Xxx` 的读写方向）。
3. **不得把属性改为嵌套子对象**（如 `Xxx.ClipboardSyncEnabled`）。
4. **不得修改构造函数**（593~606，含 7 个服务解析）与 3 个方法（`LoadApps` 607 / `ChangeNotificationFilter` 612 / `RemoveDevice` 622）。
5. **不得修改 `BaseViewModel`**；不得引入新的基类或接口。
6. **不得改动 `AdbService` 转发属性**（165~167：`DisplayOrientationOptions` 等三个 `=> AdbService.Xxx`）。
7. 不得新增/删除 `[ObservableProperty]` 特性。
8. 不得改动 `#region` 的名称文本（仅整体搬移）。

---

## 六、验收标准

### 6.1 构建
```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' `
  'E:\GitHubCode\01Main\NotifyRelay\worktree\device-settings-vm\src\NotifyRelay.sln' `
  -p:Platform=x64 -p:Configuration=Debug -v:m -nologo -nodeReuse:false -restore
```
**必须 `EXIT=0`。**

> 本类被 XAML `x:Bind` 绑定，因此**构建通过本身就是绑定正确性的强校验**：若属性被改名/删漏，XAML 编译器会直接报错。

### 6.2 警告基线（由主代理在 `31ce450` 实测，**禁止自行重做基线**）
基线 `EXIT=0`，41s，存量警告**恰好 8 条**：

| # | 警告 | 位置 |
|---|------|------|
| 1 | `CS8604` | `Platforms/Windows/Services/KeyboardHookService.cs(85,13)` |
| 2~8 | `WMC1506` ×7 | `Views/Settings/OverlayLogiBatteryPage.xaml` 行 58,59,63,72,78,82,83 |

> `WMC1506` 属 XAML 绑定警告，与本次拆分无关，**不得试图修复**。
> 增量构建时 `CS8604` 可能被打印两次，比对时按**警告代码 + 文件 + 行号去重**。

### 6.3 结构验收
- 主文件 ≤ 180 行；每个分文件 ≤ 280 行。
- 全部 5 个文件均含 `partial class DeviceSettingsViewModel`，且**只有一个**文件带 `: BaseViewModel` 基类列表。
- 属性总数不变（搬移前后各域属性计数一致）。

### 6.4 提交
```powershell
cd E:\GitHubCode\01Main\NotifyRelay\worktree\device-settings-vm
git add -A
git commit -m "refactor(settings-vm): 按设置域将 DeviceSettingsViewModel 拆为 partial 分文件"
```

---

## 七、风险清单

| 风险 | 说明 | 缓解 |
|------|------|------|
| `#region` 悬空 | 搬走内容却留下 `#endregion` → 编译错误 | 每域搬完立即构建 |
| `using` 缺失 | 各分文件需各自的 `using` | 搬完立即构建 |
| XAML 绑定断裂 | 属性改名会导致 `x:Bind` 失败 | 属性名逐字保留；构建即校验 |
| 漏搬属性 | 无接口强校验（不像 `GeneralSettingsService`） | 搬移前后用 `Select-String` 统计属性名数量对比 |
