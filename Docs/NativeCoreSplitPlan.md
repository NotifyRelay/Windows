# NativeCore.cs 拆分计划

> 制定日期：2026-09-17
> 目标文件：`Win/src/NotifyRelay/Native/NativeCore.cs`
> 基线提交：`31ce450`（分支 `整理项目`）
> 当前规模：**644 物理行**，33+ 方法，1 个 `static class`
> 目标规模：主文件瘦身后 ≤ 200 行，共 6 个文件

---

## 一、拆分必要性（实测）

| 指标 | 实测值 |
|------|--------|
| 物理行数 | 644 |
| 类 | `public static class NativeCore`（**静态类，无 DI**） |
| 方法数 | 33+ |
| 外部引用 | **77 处**，分布在 17 个文件 |
| 最大引用方 | `NetworkService.cs`(22)、`DeviceManager.cs`(14)、`AppLifecycleHelper.cs`(10)、`AudioRelayService.cs`(8) |

### 1.1 职责分布（实测行号）

| # | 职责域 | 行号区间 | 行数 | 代表成员 |
|---|--------|----------|------|----------|
| A | 状态/初始化/上下文 | 12~70 | ~59 | `_ctx`、`_initialized`、`_gitHash`、`Initialize`、`GetGitHash`、`Context`、4 个回调分发目标属性 |
| B | 密钥管理 | 72~118 | ~47 | `MigrateSharedSecret`、`GenerateKeypair`、`GetPublicKey`、`HasKeypair`、`DeriveSharedSecret`、`ExportDeviceKey`、`GetLocalUuid`、`RenameDevice`、`RemoveDevice` |
| C | 配对协议 | 119~159 | ~41 | `PeriodicBroadcast`、`SendHandshake`、`SendPairingInit`、`SendPairingResp`、`SendAccept`、`SendReject`、`GeneratePairingCode`、`ClearPairingCode` |
| D | 核心生命周期 | 160~204 | ~45 | `StartCore`、`RemoveDeviceSession`、`ComputeDedupKey`、`ComputeFeatureId`、`ExportState`、`ImportState`、`EncryptLocalState`、`DecryptLocalState` |
| E | 回调注册/分发 | 205~500 | **~296** | `FindDevice`、`SetLogCallback`、`RegisterCallbacks`（**巨型方法 ~261 行**） |
| F | 心跳/发件队列/状态快照 | 501~535 | ~35 | `_senderQueueHandle`、`UpdateHeartbeatSchedulerParams`、`GetDeviceList`、`SenderQueueHandle`、`EnqueueMessage`、`PushSuperIslandState`、`PushMediaState` |
| G | 剪贴板 / 应用同步 | 536~573 | ~38 | `ClipboardOnChanged`、`ClipboardOnReceived`、`AppSyncPrepareIconRequest`、`AppSyncClearIconPending`、`AppSyncParseIconResponse`、`AppSyncBuildApplistRequest`、`AppSyncParseApplistResponse` |
| H | 网络/设备管理 | 574~607 | ~34 | `OnNetworkChanged`、`GetLocalIp`、`AddKnownDevice`、`RemoveKnownDevice` |
| I | 音频 FFI | 608~635 | ~28 | `AudioStart`、`AudioWriteFrame`、`AudioStop`、`AudioIsActive`、`RegisterAudioCallbacks` |
| J | 设备超时 | 636~644 | ~9 | `HandleDeviceTimeout` |

**判定**：🟡 警告级。作为静态类混合了 9 个底层桥接职责。

---

## 二、采用策略：`static partial` 分文件（**必须遵守**）

`NativeCore` 是 **public 静态类**，被 77 处调用。**这是本次拆分中最不能改签名的一个文件**。

**不采用**分析文档建议的 `NativeKeyManager` / `NativePairingBridge` 等子类方案，理由：

1. **77 处调用点**全部形如 `NativeCore.GetPublicKey(...)`。若改为 `NativeKeyManager.GetPublicKey(...)`，需改动全部 77 处，并新增 5 个静态类的静态访问路径——这与「仅移动代码」原则严重冲突，且为本任务引入了与拆分无关的巨大回归面。
2. `NativeCore` 是 **FFI 边界**。`RegisterCallbacks` 里所有 lambda 都是传给 Rust 的 `[UnmanagedCallersOnly]` / 委托回调，静态上下文是**必需品**；改成实例类会改变回调生命周期与 GC 保持语义（`_callbackRefs` 就是为此存在）。
3. 各成员之间通过 `_ctx`、`_initialized`、`_callbackRefs`、`_deviceOnline`、`_senderQueueHandle` **静态字段强耦合**。`partial` 分文件共享全部静态字段，**零改动**；拆子类则需把这些字段改为 `internal` 并跨类访问，属 API 变更。
4. `partial` 分文件可**完全达成**「单文件 ≤ 阈值、按 FFI 域可读」的目标。

> 若后续要真正分层，应作为独立重构另行评估（需同时评估 FFI 回调生命周期）。

---

## 三、目标结构

```
Native/
├── NativeCore.cs                  ← 主文件（瘦身至 ~120 行）
│      类声明、_ctx/_initialized/_gitHash、4 个回调分发目标、
│      MediaSessionQueryHandler、_callbackRefs、_deviceOnline、
│      Context、Initialize、GetGitHash
├── NativeCore.Keys.cs             ← ~50 行（B：密钥管理）
├── NativeCore.Pairing.cs          ← ~45 行（C：配对协议）
├── NativeCore.Lifecycle.cs        ← ~48 行（D：核心生命周期）
├── NativeCore.Callbacks.cs        ← ~300 行（E：回调注册/分发，最大块）
├── NativeCore.State.cs            ← ~40 行（F：心跳/队列/状态快照）
├── NativeCore.AppSync.cs          ← ~42 行（G：剪贴板 + 应用同步）
├── NativeCore.Network.cs          ← ~38 行（H：网络/设备管理）
└── NativeCore.Audio.cs            ← ~32 行（I + J：音频 FFI + 设备超时）
```

> **命名参考现有先例**：`NotifyRelay.NativeCore` 子项目已采用 `NotifyRelayCore.Keys/Audio/Pairing/...` 分文件风格（见 `src/NotifyRelay.NativeCore/`），本计划沿用同一命名习惯。

---

## 四、执行步骤

### 步骤 1：加 `partial`
`NativeCore.cs:12` 改为：
```csharp
public static partial class NativeCore
```
> 注意：**不能**加 `sealed`（静态类本身隐含 sealed）；仅追加 `partial`。

### 步骤 2：分文件骨架
每个新文件模板（**必须逐字保留原命名空间**）：
```csharp
using System.Runtime.InteropServices;   // 按需
using NotifyRelay.Data.Contracts;       // 按需

namespace NotifyRelay.Native;

public static partial class NativeCore
{
    // 从主文件整段剪切搬入
}
```
> 各分文件**只需 `using` 自己用到的命名空间**，不要复制主文件全部 using（否则会触发「未使用的 using」类 IDE 提示，且掩盖真实依赖）。

### 步骤 3：逐域搬移（一次一个域，每域构建一次）

**搬移顺序**（从最独立到最耦合）：

| 顺序 | 域 | 行号 | 目标文件 |
|------|-----|------|----------|
| 1 | J 设备超时 | 636~644 | `NativeCore.Audio.cs` |
| 2 | I 音频 FFI | 608~635 | `NativeCore.Audio.cs` |
| 3 | G 剪贴板/应用同步 | 536~573 | `NativeCore.AppSync.cs` |
| 4 | H 网络/设备管理 | 574~607 | `NativeCore.Network.cs` |
| 5 | F 心跳/队列/快照 | 501~535 | `NativeCore.State.cs` |
| 6 | C 配对协议 | 119~159 | `NativeCore.Pairing.cs` |
| 7 | B 密钥管理 | 72~118 | `NativeCore.Keys.cs` |
| 8 | D 核心生命周期 | 160~204 | `NativeCore.Lifecycle.cs` |
| 9 | E 回调注册 | 205~500 | `NativeCore.Callbacks.cs` |

> 域 F 与 G 在原文件中**交错**（501~535 与 536~573 相邻但分属不同域），按行号逐段处理。
> **域 E 留到最后**（它最长、且与所有域都有交叉引用）。

### 步骤 4：验证主文件
搬完后主文件只剩 A 域（初始化/上下文）。确认 `_callbackRefs`、`_deviceOnline` 等**共享静态字段仍在主文件**（供各分文件访问），**不要**搬到 `.Callbacks.cs`。

---

## 五、严格禁止事项

1. **不得修改任何成员的签名、可见性、返回类型**——77 处调用点一处都不能改。
2. **不得修改任何 `[UnmanagedCallersOnly]`、委托类型、`Marshal` 调用**。
3. **不得把静态方法改为实例方法**；不得引入实例类或 DI。
4. **不得移动共享静态字段**（`_ctx`、`_initialized`、`_gitHash`、`_callbackRefs`、`_deviceOnline`、`_senderQueueHandle` 留在主文件；`_callbackRefs.Add(...)` 在 `Callbacks.cs` 中通过 partial 作用域访问即可）。
5. **不得改动 `RegisterCallbacks` 内部任何一行逻辑**——它是 FFI 回调注册，含 GC 保持语义；仅允许整段搬移到 `.Callbacks.cs`。
6. **不得修改 `Initialize` 的 DLL 探测顺序**（`AppContext.BaseDirectory` → 父目录 → 程序集目录）。
7. **不得改 `NotifyRelayCore`（自动生成的 FFI 封装）**。
8. **不得改 `NotifyRelay.NativeCore` 子项目**（Rust 侧）。
9. 不得改动命名空间 `NotifyRelay.Native`。

---

## 六、验收标准

### 6.1 构建
```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' `
  'E:\GitHubCode\01Main\NotifyRelay\worktree\native-core\src\NotifyRelay.sln' `
  -p:Platform=x64 -p:Configuration=Debug -v:m -nologo -nodeReuse:false -restore
```
**必须 `EXIT=0`。**

### 6.2 警告基线（由主代理在 `31ce450` 实测，**禁止自行重做基线**）
基线 `EXIT=0`，41s，存量警告**恰好 8 条**：

| # | 警告 | 位置 |
|---|------|------|
| 1 | `CS8604` | `Platforms/Windows/Services/KeyboardHookService.cs(85,13)` |
| 2~8 | `WMC1506` ×7 | `Views/Settings/OverlayLogiBatteryPage.xaml` 行 58,59,63,72,78,82,83 |

Rust 子模块另有 2 条 `dead_code` 警告 + 1 条汇总（存量，**不是本次引入**）：
```
warning : associated items `from_str` and `to_str` are never used  --> src\protocol\header.rs:46
warning : associated items `parse`, `is_discovery_message`, and `is_data` are never used --> src\protocol\header.rs:120
warning : `notify-relay-core` (lib) generated 2 warnings
```
> 增量构建时 `CS8604` 可能被打印两次，比对时按**警告代码 + 文件 + 行号去重**。

### 6.3 结构验收
- 主文件 ≤ 200 行；`.Callbacks.cs` 允许 ~300 行（单方法 `RegisterCallbacks` 本身 ~261 行，**不得进一步拆分逻辑**）。
- 所有分文件均含 `public static partial class NativeCore`。
- **`git diff` 中不得出现任何调用点文件的改动**（只允许 `Native/` 下新增文件 + 主文件行数减少）。这是本任务最重要的验收项。

### 6.4 提交
```powershell
cd E:\GitHubCode\01Main\NotifyRelay\worktree\native-core
git add -A
git commit -m "refactor(native): 按 FFI 职责将 NativeCore 拆为 static partial 分文件"
```

---

## 七、风险清单

| 风险 | 说明 | 缓解 |
|------|------|------|
| 误改 FFI 语义 | `RegisterCallbacks` 逻辑密集 | 整段剪切，禁止重写；复核 diff 无逻辑改动 |
| 共享静态字段搬错 | 搬走后各分文件编译失败 | 共享字段一律留主文件 |
| `using` 遗漏 | 各分文件 `Marshal`/`Concurrent` 等 | 每域搬完立即构建 |
| GC 保持失效 | `_callbackRefs` 若被误删会导致回调被回收 | 确认该字段仍在主文件且未被改动 |
| 隐藏调用点 | `Models.Render.SuperIslandParamV2Parser` 等跨项目引用 | 构建 + `git status` 确认无其他文件被改 |
