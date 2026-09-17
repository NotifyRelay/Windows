# SuperIslandParamV2Parser.cs 拆分计划

> 制定日期：2026-09-17
> 目标文件：`Win/src/NotifyRelay.Overlay/Models/Render/SuperIslandParamV2Parser.cs`
> 基线提交：`31ce450`（分支 `整理项目`）
> 当前规模：**1002 物理行**，43 个方法，1 个 `static partial class`
> 目标规模：主文件瘦身后 ≤ 200 行，共 4 个文件

---

## 一、拆分必要性（实测）

| 指标 | 实测值 |
|------|--------|
| 物理行数 | **1002**（全项目最大源文件，超过分析文档记载的 908） |
| 类 | `public static partial class SuperIslandParamV2Parser`（**已是 partial**） |
| 命名空间 | `NotifyRelay.Models.Render`（注意：在 **NotifyRelay.Overlay** 项目内） |
| 方法数 | 43 |
| 外部引用 | **仅 3 处，且全部只调 `ApplyToState`** |

### 1.1 外部引用（实测，全部走同一入口）

| 文件 | 行号 | 调用 |
|------|------|------|
| `ProtocolRouter.cs` | 292 | `Models.Render.SuperIslandParamV2Parser.ApplyToState(siState, paramV2Raw)` |
| `SuperIslandState.cs` | 77 | `SuperIslandParamV2Parser.ApplyToState(this, ParamV2Raw)` |
| `OverlayRenderService.Items.cs` | 169 | `SuperIslandParamV2Parser.ApplyToState(existing.State, state.ParamV2Raw)` |

> **`ParseParamV2` 与 `ApplyToState` 是本类的公开 API，其余 41 个方法全部 `private static`。** 只拆 `private` 部分，公开面完全不动。

### 1.2 方法分组（按前缀归纳，实测行号）

| # | 分组 | 方法 | 行号区间 | 行数 |
|---|------|------|----------|------|
| 1 | 公开入口 | `ParseParamV2`、`ApplyToState`、`ApplyTimerBase` | 14~153 | ~140 |
| 2 | Base/Chat/Highlight/Hint 解析 | `ParseBaseInfo`、`ParseChatInfo`、`ParseHighlightInfo`、`ParseHintInfo` | 154~276 | ~123 |
| 3 | Pic/Cover/Bg/IconText/AnimText | `ParsePicInfo`、`ParseCoverInfo`、`ParseBgInfo`、`ParseIconTextInfo`、`ParseAnimTextInfo`、`ParseTextButton` | 277~502 | ~226 |
| 4 | Progress / Timer / Action | `ParseProgressInfo`、`ParseMultiProgressInfo`、`ParseTimerInfo`、`ParseActionInfo`、`ParseActions`、`ToMultiProgressInfo` | 294~312, 313~335, 336~349, 350~376, 377~390, 891~913 | ~150 |
| 5 | 岛/组件布局 | `ParseHighlightInfoV3`、`ParseParamIsland`、`ParseSmallIslandArea`、`ParseBigIslandArea`、`ParseAComponent`、`ParseBComponent`、`ResolvePicKey` | 503~807 | **~305** |
| 6 | 文本提取工具 | 6 个 `s_*Keys` 数组、`ExtractFirstString`、`ExtractNestedFirstString`、`FirstTimerInfo`、`AppendInfoTexts` ×3、`TryAppend` | 808~890 | ~83 |
| 7 | JsonElement 扩展工具 | `GetPropertyOrNull` ×2、`GetString`、`GetInt32`、`GetInt32OrDefault` ×2、`GetInt64`、`GetInt64OrDefault`、`GetBool`、`TryParse<T>`、`TrimOrNull`、`GetPropertyOrNull` | 914~1001 | ~88 |

**判定**：🟡 警告级。单文件 1002 行、43 方法，是叠加层渲染的参数解析模块。

### 1.3 解析流程图（必须保持的调用链）

```mermaid
sequenceDiagram
    participant PR as ProtocolRouter
    participant Parser as SuperIslandParamV2Parser
    participant State as SuperIslandState
    participant Sub as ParseXxx 私有解析器

    PR->>Parser: ApplyToState(state, paramV2Raw)
    Parser->>Parser: ParseParamV2(paramV2Raw)
    Parser->>Parser: JsonDocument.Parse
    Parser->>Sub: ParseBaseInfo / ParseChatInfo / ...
    Sub-->>Parser: 各 XxxData（失败返回 null）
    Parser->>Parser: 兜底：从各域提取主/副文本
    Parser->>State: 逐字段赋值 + ApplyTimerBase
    Note over Parser,Sub: 每个 ParseXxx 均通过 TryParse&lt;T&gt; 包裹，<br/>JsonException/InvalidOperationException → null
```

**关键**：所有 `ParseXxx` 都经 `TryParse<T>` 做异常隔离。搬移时**不得**把 `TryParse` 的包裹去掉或改变异常类型捕获范围。

---

## 二、采用策略：`static partial` 分文件（**唯一可行方案，必须遵守**）

本类**已经是 `public static partial class`**，是本次 9 个文件中**最适合 partial 拆分**的一个。

**不采用**分析文档未明确、但常见的「拆成多个独立静态类」方案，理由：

1. 41 个私有方法存在**密集交叉调用**（`ParseBigIslandArea` 调 `ParseAComponent`/`ParseBComponent`/`ResolvePicKey`；各 `ParseXxx` 共用 `GetPropertyOrNull`/`GetString` 等扩展方法）。拆成独立类会使这些 `private` 方法必须提升为 `internal`/`public`，**属 API 变更**且暴露内部实现。
2. `s_primaryKeys` 等 6 个 `private static readonly` 数组被多个解析器共享；partial 分文件共享，零改动。
3. 三个外部调用点**只依赖 `ApplyToState`**，partial 分文件后公开面 100% 不变，**外部零改动**。
4. 该文件是「移植自 Android superislandui 的 AParser/BParser」——按解析器职责分文件后，与上游 Android 代码结构**一一对应**，便于后续比对同步。

---

## 三、目标结构

```
NotifyRelay.Overlay/Models/Render/
├── SuperIslandParamV2Parser.cs          ← 主文件（瘦身至 ~180 行）
│      using System.Text.Json、类声明、
│      ParseParamV2、ApplyToState、ApplyTimerBase、
│      （公开入口全部留此）
├── SuperIslandParamV2Parser.Text.cs     ← ~230 行（分组 2 + 3 中的文本/图片域解析）
│      ParseBaseInfo / ParseChatInfo / ParseHighlightInfo / ParseHintInfo /
│      ParsePicInfo / ParseCoverInfo / ParseBgInfo / ParseIconTextInfo /
│      ParseAnimTextInfo / ParseTextButton
├── SuperIslandParamV2Parser.Layout.cs   ← ~350 行（分组 5 + 4 中的布局与进度）
│      ParseProgressInfo / ParseMultiProgressInfo / ParseTimerInfo /
│      ParseActionInfo / ParseActions / ToMultiProgressInfo /
│      ParseHighlightInfoV3 / ParseParamIsland / ParseSmallIslandArea /
│      ParseBigIslandArea / ParseAComponent / ParseBComponent / ResolvePicKey
└── SuperIslandParamV2Parser.Json.cs     ← ~180 行（分组 6 + 7）
       s_*Keys 数组、ExtractFirstString、ExtractNestedFirstString、
       FirstTimerInfo、AppendInfoTexts ×3、TryAppend、
       GetPropertyOrNull ×2、GetString、GetInt32、GetInt32OrDefault ×2、
       GetInt64、GetInt64OrDefault、GetBool、TryParse<T>、TrimOrNull
```

> 若 `Layout.cs` 仍超 350 行，可把 `ParseAComponent`/`ParseBComponent`（632~797，~165 行）再拆为 `SuperIslandParamV2Parser.Components.cs`。

---

## 四、执行步骤

### 步骤 1：确认 `partial` 已存在
`SuperIslandParamV2Parser.cs:9` 已是 `public static partial class SuperIslandParamV2Parser`，**无需修改**。

### 步骤 2：分文件骨架
Overlay 项目有 `GlobalUsings.cs`（含 `System`、`Collections.Generic`、`Linq`、`Tasks` 等），因此分文件**通常只需**：
```csharp
using System.Text.Json;

namespace NotifyRelay.Models.Render;

public static partial class SuperIslandParamV2Parser
{
    // 从主文件整段剪切搬入
}
```
> 命名空间必须写 `NotifyRelay.Models.Render`（**不是** `NotifyRelay.Overlay.Models.Render`）。

### 步骤 3：逐组搬移（一次一组，每组构建一次）

| 顺序 | 分组 | 说明 |
|------|------|------|
| 1 | 分组 7 + 6 → `Json.cs` | **最先做**：这些是叶子工具，其他解析器都依赖它们，先搬走可验证 partial 作用域生效 |
| 2 | 分组 3 → `Text.cs` | 图片/文本域解析 |
| 3 | 分组 2 → `Text.cs` | Base/Chat/Highlight/Hint |
| 4 | 分组 4 + 5 → `Layout.cs` | 进度/动作/布局（最大块，最后做） |
| 5 | 主文件收尾 | 仅留 `ParseParamV2`/`ApplyToState`/`ApplyTimerBase` |

### 步骤 4：搬移注意事项
- **扩展方法**（`this JsonElement?`、`this string?`、`ToMultiProgressInfo`）搬入 `.Json.cs` 后仍需在**同一静态类**内——partial 保证了这一点，**不要**把它们移到独立的 `static class Extensions` 里（那会改变调用解析）。
- `s_primaryKeys` 等 6 个数组被 `.Text.cs` 与 `.Layout.cs` 共用 → 可留在 `.Json.cs`，partial 作用域内均可见。

---

## 五、严格禁止事项

1. **不得修改 `ParseParamV2` 与 `ApplyToState` 的签名与逻辑**（仅 3 个外部调用点依赖它们，但它们是公开契约）。
2. **不得把 `private static` 改为 `internal`/`public`**（partial 分文件无需提升可见性）。
3. **不得修改 `TryParse<T>` 的异常捕获类型**（`JsonException` + `InvalidOperationException`）。
4. **不得删除或改写 6 个 `s_*Keys` 数组的字面量内容与顺序**（顺序影响键匹配优先级）。
5. **不得修改命名空间**（`NotifyRelay.Models.Render`）。
6. **不得拆分 `ParseAComponent`/`ParseBComponent` 等单体方法内部逻辑**（仅整体搬移；单个方法内部再拆属逻辑改动）。
7. **不得修改 `NotifyRelay.Overlay.csproj`**（SDK 风格项目自动包含 `.cs`，新增文件无需登记）。
8. 不得改动 `SuperIslandModels.cs`（`ParamV2` 等模型定义）。
9. 不得修复任何「未使用键数组」类的提示——本文件所有数组成员均为声明式契约。

---

## 六、验收标准

### 6.1 构建
```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' `
  'E:\GitHubCode\01Main\NotifyRelay\worktree\param-v2-parser\src\NotifyRelay.sln' `
  -p:Platform=x64 -p:Configuration=Debug -v:m -nologo -nodeReuse:false -restore
```
**必须 `EXIT=0`。**

### 6.2 警告基线（由主代理在 `31ce450` 实测，**禁止自行重做基线**）
基线 `EXIT=0`，41s，存量警告**恰好 8 条**：

| # | 警告 | 位置 |
|---|------|------|
| 1 | `CS8604` | `Platforms/Windows/Services/KeyboardHookService.cs(85,13)` |
| 2~8 | `WMC1506` ×7 | `Views/Settings/OverlayLogiBatteryPage.xaml` 行 58,59,63,72,78,82,83 |

Rust 子模块另有 2 条 `dead_code` + 1 条汇总（存量）。
> **注意**：本文件所在项目为 `NotifyRelay.Overlay`，其构建日志在 `EXIT=0` 时只输出项目产出路径，通常无警告。若新增了 `CS0168`（未使用变量）或 `CS0414`（字段已赋值但未使用），说明搬移时漏搬了使用点。
> 增量构建时 `CS8604` 可能被打印两次，比对时按**警告代码 + 文件 + 行号去重**。

### 6.3 结构验收
- 主文件 ≤ 200 行。
- 全部 4 个文件均含 `public static partial class SuperIslandParamV2Parser`。
- **`git status` 显示只改动了 `Models/Render/` 下文件**——3 个外部调用点文件必须零改动。
- 方法总数仍为 43；`private static` 方法一个都没变成 `public`。

### 6.4 提交
```powershell
cd E:\GitHubCode\01Main\NotifyRelay\worktree\param-v2-parser
git add -A
git commit -m "refactor(overlay): 按解析职责将 SuperIslandParamV2Parser 拆为 static partial 分文件"
```

---

## 七、风险清单

| 风险 | 说明 | 缓解 |
|------|------|------|
| 扩展方法作用域 | `this JsonElement` 扩展方法搬错类会静默改变调用解析 | 保持在 `SuperIslandParamV2Parser` partial 内 |
| 键数组顺序 | 影响匹配优先级，改序会导致解析行为变化 | 逐字保留，含数组字面量顺序 |
| 可见性误改 | 为跨文件调用而提升 `private` → `internal` | partial 无需提升；复核 diff 无可见性变化 |
| 漏搬私有方法 | 编译器会报「未找到方法」，不会静默通过 | 构建即校验 |
| 项目未重新编译 | Overlay 项目在增量构建下可能跳过 | 用完整 sln 构建，确认输出含 `NotifyRelay.Overlay ->` 行 |
