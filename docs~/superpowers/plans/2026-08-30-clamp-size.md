# 尺寸钳制 `clamp(min, N%, max)` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `width` / `height` 的值语法新增 `clamp(min, middle, max)`：`middle` 是 `N%`（自由定位，随父级连续拉伸、钳在 [min, max]）或 `stretch`（布局组，映射到 `LayoutElement` 的 min / preferred）。零新属性名；Screen / ReSolve / resize 路径不改。

**Architecture:** 三层。(1) `SizeSpec` 解析出 `IsClamped* / Min* / Max*`，中段复用既有的 `%` / `stretch` 标志。(2) 自由定位：`ApplyCommon` 照 `%` 写分数锚区基线，再把 spec 推进节点上的 `ClampFitter`（`UIBehaviour, ILayoutSelfController`，`AspectRatioFitter` 模式）——回调只标脏，写发生在 `LayoutRebuilder` pass 里，按父 rect 写 `offsetMin/Max`。(3) 布局组：`ApplyLayoutElement` 按映射表填三元组，无组件。lint `PUI-CLAMP-SCALE` 禁止 clamp 与 `scale` 同节点。

**Tech Stack:** Unity 6 / C# (LangVersion 9.0)、uGUI（`ILayoutSelfController` / `LayoutRebuilder` / `CanvasUpdateRegistry` / `LayoutElement`）。无新增包。

设计依据：`docs~/superpowers/specs/2026-08-30-clamp-size-design.md`（下文 §N 均指该 spec 的节号）。

## Global Constraints

- **分支**：全部工作在 `feat/clamp-size`（Task 0 建）。**绝不提交到 main。**
- **LangVersion 9.0**：无 primary constructor、无 collection expression `[]`、无 `[field: SerializeField]`。switch 表达式 / target-typed `new()` / `??=` 可用。
- **不用 System.Threading / Task**：本特性无异步。
- **float 解析**：`float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)`（**禁用** `AsSpan` 重载 —— CA1846 → Mono CS1503）。
- **Core 纯 C# 子集**：`Runtime/Core/Lint/ClampRules.cs` **不得** `using UnityEngine`、不得引用 `SizeSpec`（它在 `Core/Layout`，CLI 编译集之外）—— 用字符串前缀判定。
- **lint**：每个 Task 收尾从仓库根跑 `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`。**不要** `dotnet format analyzers --severity info`。
- **同 PR 必改 SKILL**（英文）：见 Task 7。
- **测试只经 Unity MCP 跑**。**禁止** `execute_menu_item("Assets/Reimport All")`；用 `refresh_unity(mode="force", scope="all")`。
- **InternalsVisibleTo** 已对 `PromptUGUI.Tests.EditMode` / `PromptUGUI.Tests.PlayMode` 开放。
- **Red first**：每个 Task 先写失败测试、跑一次看它**因正确的原因**失败，再写实现。
- **`.ui.xml` 改动后跑** `dotnet run --project .lint/UIXmlLint -- <file>`（本特性不改内置 `.ui.xml`；若 SKILL 示例落盘到 fixture 则跑）。

**RUN(ClassName) = 跑 EditMode 测试的标准流程：**
1. `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
2. `mcp__UnityMCP__read_console(action="get", types=["error"])` —— 编译错误必须为空才继续
3. `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["ClassName"])` → 轮询 `mcp__UnityMCP__get_test_job(job_id=...)` 直到完成，读 pass/fail

**RUNPLAY(ClassName)** 同上，`mode="PlayMode"` + `assembly_names=["PromptUGUI.Tests.PlayMode"]`。

**Drain()** = EditMode 里驱动布局 pass：`Canvas.ForceUpdateCanvases()`（`LayoutRebuildDirtyTests.Drain` 的模式；`PendingLayoutRebuilds()` 那个反射探针可以照抄）。

---

## File Structure

| 文件 | 责任 | 动作 |
|---|---|---|
| `Runtime/Core/Layout/SizeSpec.cs` | `clamp(…)` 解析；`IsClampedWidth/Height`、`MinWidth/MaxWidth/MinHeight/MaxHeight`；`LooksLikeKeyword` 加 `clamp` | Modify |
| `Runtime/Core/Layout/MarginResolver.cs` | `ParseMargin` 升为 `internal static Parse(...)` 供 `ApplyCommon` 取四分量 | Modify |
| `Runtime/Controls/Internal/ClampFitter.cs` | `UIBehaviour, ILayoutSelfController`；`SetAxis` / `Apply` / 延迟标脏 | Create |
| `Runtime/Controls/Control.cs` | 自由定位分支挂 / 启停 `ClampFitter`；`ApplyLayoutElement` 三元组映射；两条既有报错补 clamp 指引 | Modify |
| `Runtime/Core/Lint/ClampRules.cs` | `HasClamp(node)`、`CheckClampScale(node)` → `PUI-CLAMP-SCALE` | Create |
| `Runtime/Core/Lint/IRWalker.cs` | 分发 `ClampRules.CheckClampScale` | Modify |
| `Runtime/Core/Lint/MarginAnchorRules.cs` | 分数轴（`%` / `clamp(`）视为消耗两侧槽（§11 第一条，已采纳） | Modify |
| `Runtime/Application/ControlAttributeApplier.cs` | `ApplyCommon` 前对 `PUI-CLAMP-SCALE` 抛 `ParseException` | Modify |
| `Tests/EditMode/Layout/SizeSpecTests.cs` | 解析用例 | Modify |
| `Tests/EditMode/Controls/ClampFitterTests.cs` | 直接驱动组件的单元测试 | Create |
| `Tests/EditMode/Controls/ControlApplyCommonClampTests.cs` | 经 `UI.Open` 的自由定位 / Variant / 幂等 | Create |
| `Tests/EditMode/Controls/ControlApplyCommonLayoutGroupTests.cs` | 布局组映射 + 真实布局 | Modify |
| `Tests/EditMode/Lint/ClampRulesTests.cs` | `PUI-CLAMP-SCALE` + IRWalker 分发 | Create |
| `Tests/EditMode/Lint/MarginAnchorRulesTests.cs` | 分数轴不再误报 | Modify |
| `Tests/EditMode/Application/ControlAttributeApplierClampTests.cs` | 运行时硬错 | Create |
| `Tests/PlayMode/Controls/ClampFitterPlayTests.cs` | 回调路径（改父 `sizeDelta` → yield → 子已重算） | Create |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` | 语法 / 语义 / 错误表 / FAQ / uGUI 对照 | Modify |
| `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` | §6.2 末尾 CLP 摘要段 | Modify |
| `.lint/UIXmlLint/README.md` | 代码表加 `PUI-CLAMP-SCALE`；`PUI-MARGIN-INERT-SIDE` 行补分数轴 | Modify |
| `docs~/superpowers/specs/2026-08-30-clamp-size-design.md` | §13 实施记录 | Modify |

---

## Task 0: 建分支 + 落 plan

- [ ] `git checkout -b feat/clamp-size`
- [ ] 本文件与 spec 一起 `git add docs~/` 后提交（分支上）。

---

## Task 1: `SizeSpec` 解析 `clamp(min, middle, max)`（spec §4 / §6.2）

**Files:**
- Modify: `Runtime/Core/Layout/SizeSpec.cs`
- Test: `Tests/EditMode/Layout/SizeSpecTests.cs`

**Interfaces（新增，公开面向 Controls）：**
```csharp
public bool  IsClampedWidth  { get; }   public bool  IsClampedHeight { get; }
public float MinWidth        { get; }   public float MaxWidth        { get; }   // 开放端 = float.NegativeInfinity / PositiveInfinity
public float MinHeight       { get; }   public float MaxHeight       { get; }
```
中段复用既有标志：`clamp(_, 46%, 250)` ⇒ `IsFractionalWidth=true, WidthFraction=0.46`；`clamp(167, stretch, _)` ⇒ `IsFlexibleWidth=true, WeightWidth=1`。

- [ ] **Step 1: 红测试** —— 在 `SizeSpecTests` 里加一组 `Clamp_*`：
  - `clamp(167, 46.4%, 250)` ⇒ `IsClampedWidth`、`IsFractionalWidth`、`WidthFraction≈0.464`、`MinWidth=167`、`MaxWidth=250`、`HasWidth`。
  - `clamp(_, 46%, 250)` ⇒ `MinWidth = NegativeInfinity`；`clamp(320, 60%, _)` ⇒ `MaxWidth = PositiveInfinity`。
  - `clamp( 167 , 46% , 250 )`（空白）合法。
  - `clamp(167, stretch, 250)` ⇒ `IsFlexibleWidth`、`WeightWidth=1`、min/max 如上；`clamp(167, stretch*2, _)` ⇒ `WeightWidth=2`。
  - `height="clamp(200, 55%, 400)"` 走 Height 字段。
  - 非 clamp 值 ⇒ `IsClamped*=false`、`Min*=-∞`、`Max*=+∞`（默认值，让 `Mathf.Clamp` 恒等）。
  - `Assert.Throws<ArgumentException>` 逐行覆盖 §4.1 第一张表：两端开放 / min>max / 负数 / NaN / 中段数字 / `stretch*2` + 有限上限 / 2 段 / 4 段 / `clamp 167 46% 250` / `Clamp(` / 缺右括号；`size="clamp(167,46%,250)"` 报 keyword 那条（`StringAssert.Contains("numeric-only")`）。
- [ ] **Step 2: RUN(SizeSpecTests)** —— 新用例全红（`IsClampedWidth` 不存在时是编译错，先用 `#if false` 圈住？**不要**：直接加字段的空实现让它编译、断言红）。
- [ ] **Step 3: 实现**
  - `ParseAxis` 开头：`value.StartsWith("clamp(", Ordinal)` ⇒ 进 `ParseClamp`；`value.StartsWith("clamp", OrdinalIgnoreCase)` 但不是这个前缀（`Clamp(` / `clamp 167`）⇒ 抛 "unknown … the only function form is `clamp(min, middle, max)`"。
  - `ParseClamp`：校验末尾 `)`；去壳后 `Split(',')` 必须 3 段（否则 "takes exactly 3 parts"）；两端 `Trim()` 后 `_` ⇒ ±∞，否则 `TryParse` 且 `>= 0` 且有限；中段 `Trim()` 后**递归 `ParseAxis`**，然后校验：既非 fractional 亦非 flexible ⇒ "middle must be `N%` or `stretch`"；flexible 且 weight≠1 且 max 有限 ⇒ "weighted stretch cannot be capped"；两端都开放 ⇒ "both bounds open"；min > max ⇒ "min > max"。
  - `LooksLikeKeyword` 加 `s.Contains("clamp")`。
  - 构造函数 / `WithNativeResolved` / `FromNumeric` / `WithFallbackForMissing` 透传新字段。可以顺手把 `ParseAxis` 的 6 个 `out` 收成一个私有 `readonly struct AxisSpec`，公开属性不变。
- [ ] **Step 4: RUN(SizeSpecTests)** 全绿；顺跑 `RUN(SizeSpecFromNumericTests)` 确认透传没漏。
- [ ] **Step 5: lint**

---

## Task 2: `ClampFitter` 组件（spec §5.1 / §6.4）—— 先单测直接驱动

**Files:**
- Create: `Runtime/Controls/Internal/ClampFitter.cs`
- Modify: `Runtime/Core/Layout/MarginResolver.cs`（`ParseMargin` → `internal static void Parse(string s, out float t, out float r, out float b, out float l)`，原私有方法改名即可，`Resolve` 内部调它）
- Test: `Tests/EditMode/Controls/ClampFitterTests.cs`

**Interfaces：**
```csharp
namespace PromptUGUI.Controls.Internal
{
    internal enum ClampAlign { Low, Center, High }   // Low = left/bottom, High = right/top

    [DisallowMultipleComponent, ExecuteAlways]
    internal sealed class ClampFitter : UIBehaviour, UnityEngine.UI.ILayoutSelfController
    {
        internal void SetAxis(int axis, bool on, float fraction, float min, float max,
                              float marginLow, float marginHigh, ClampAlign align);
        internal void ClearAxis(int axis);          // = SetAxis(axis, on:false, …)
        internal bool AxisEnabled(int axis);        // 测试探针
        public void SetLayoutHorizontal();          // Apply(0)
        public void SetLayoutVertical();            // Apply(1)
    }
}
```

`[ExecuteAlways]`：与 uGUI 自家 fitter 一致，也让 EditMode 测试与 `PromptUGUIDocumentHost` 编辑态预览都能收到回调（spec §11 第四条就此定案）。

- [ ] **Step 1: 红测试**（不经 `UI.Open`，手搭 RT：`parent` = 顶层 GO + RT 设 `sizeDelta=(300,200)`，`child` 挂 RT + `ClampFitter`，`anchorMin/Max` 按 `%` 规则手写，pivot 0.5）：
  - `SetAxis(0, true, 0.464, 167, 250, 0, 0, Low)`；`SetLayoutHorizontal()` ⇒ `rect.width=167`、`offsetMin.x=0`（贴左）。父改 500 ⇒ 232；父改 800 ⇒ 250，`rect.xMin=0`。
  - `High`（anchors `[1−f,1]`）：父 800 ⇒ `rect.xMax = 800`；`Center`：`rect.center.x = 400`。
  - margin：`marginLow=16, marginHigh=16`，父 800 ⇒ `rect.width = 250−32 = 218`、`rect.xMin = 16`。
  - 未钳（父 500，f=0.464 ⇒ 232 ∈ [167,250]）：`offsetMin.x == marginLow`、`offsetMax.x == −marginHigh`，与 `MarginResolver` stretch 分支等价（spec §5.1 第一条）。
  - 竖轴 `SetAxis(1, …)` + `SetLayoutVertical()`：`clamp(200, 55%, 400)`，父高 300/600/800 ⇒ 200/330/400。
  - 轴未启用 ⇒ `Apply` 不改 RT（先写个奇怪的 offset，调用后不变）。
  - 幂等：连续两次 `SetLayoutHorizontal()` 第二次不改任何值（比较 `offsetMin/Max` 位相等）。
  - 无父（顶层 RT）⇒ 不抛、不写。
- [ ] **Step 2: RUN(ClampFitterTests)** 红。
- [ ] **Step 3: 实现**
  - 字段：每轴 `bool on; float f, min, max, lo, hi; ClampAlign align`；`bool _delayedDirty; bool _selfWriting`。
  - `Apply(axis)`：`!on` ⇒ return；`parent = transform.parent as RectTransform`，null ⇒ return；`P = parent.rect.size[axis]`；`box = Mathf.Clamp(f*P, min, max)`；`W = box − (lo+hi)`；`low` 按 align（spec §5.1 公式）；`a0 = rt.anchorMin[axis]`、`a1 = rt.anchorMax[axis]`；`newMin = low − a0*P`、`newMax = low + W − a1*P`；与当前 `offsetMin[axis]` / `offsetMax[axis]` `Mathf.Approximately` 都相等 ⇒ return；否则 `_selfWriting = true` 包住两次写（只改该轴分量），finally 复位。
  - `SetDirty()`：`if (!IsActive()) return; LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);`
  - `OnRectTransformDimensionsChange()`：`if (_selfWriting) return; if (CanvasUpdateRegistry.IsRebuildingLayout()) _delayedDirty = true; else SetDirty();`
  - `Update()`：`if (!_delayedDirty) return; _delayedDirty = false; SetDirty();`（AspectRatioFitter 模式；spec §11 末条就此定案：Update 早退）。
  - `OnEnable` / `OnTransformParentChanged` ⇒ `SetDirty()`。`OnDisable` 不写 RT（ApplyCommon 已把该轴写成正确的非 clamp 几何）。
  - **回调里永远不写 RT** —— 文件头注释引用 `SafeAreaTests.cs:189` 说明为什么。
- [ ] **Step 4: RUN(ClampFitterTests)** 绿。
- [ ] **Step 5: lint**

---

## Task 3: `ApplyCommon` 接线（自由定位）+ Screen 级测试（spec §5.1 / §5.4 / §6.3）

**Files:**
- Modify: `Runtime/Controls/Control.cs`
- Test: `Tests/EditMode/Controls/ControlApplyCommonClampTests.cs`

- [ ] **Step 1: 红测试**（fixture 模式同 `ControlApplyCommonFractionalTests`：`UI.LoadDocument` + `UI.Open("S")`，父 `<Frame id='box' anchor='top-left' width='300' height='200'>` 用**数值**尺寸，与画布大小无关）：
  - `<Frame id='p' anchor='bottom-left' width='clamp(167, 46.4%, 250)' height='100'/>`，`Drain()` 后 `p.RectTransform.rect.width ≈ 167`、`rect.xMin ≈ 0`；把 `box` 换 500 / 800（三个 Screen 或三个 fixture 字符串）⇒ 232 / 250。
  - `anchor='center-right'` ⇒ 父 800 时 `rect.xMax ≈ 800`；`anchor='center'` ⇒ 居中。
  - `margin='0,16,0,16'` ⇒ 父 800 时 width 218、xMin 16。
  - `height='clamp(200, 55%, 400)'`，父高 300 / 600 / 800 ⇒ 200 / 330 / 400。
  - 孪生：同一父下 `<Frame id='a' width='46.4%'/>` 与 `<Frame id='b' width='clamp(_, 46.4%, _)'/>`… **注意**两端都 `_` 是 error，改用 `clamp(1, 46.4%, 9999)`；父 500 ⇒ `a` 与 `b` 的 `offsetMin/Max` 位相等。
  - `GetComponent<ClampFitter>()` 非 null、`AxisEnabled(0)` true、`AxisEnabled(1)` false（只钳 width 时）。
  - Variant：`width='clamp(167, 46.4%, 250)' width.wide='250'`；`UI.Variants.Set("wide", true)` ⇒ `AxisEnabled(0)` false、`rect.width == 250`（数值路径，`sizeDelta.x == 250`）；`Set("wide", false)` + `Drain()` ⇒ 恢复 clamp 几何、`AxisEnabled(0)` true。
  - 幂等：`screen.ReSolve()` 两次 + `Drain()` ⇒ `PendingLayoutRebuilds()==0`（照 `LayoutRebuildDirtyTests` 的探针），几何与首次一致。
  - `hidden='true'` 的 clamp 节点：`control.Hidden = false` + `Drain()` ⇒ 几何正确（OnEnable 标脏路径）。
  - 报错：`<VStack><Frame width='clamp(167, 46%, 250)'/></VStack>` ⇒ `ParseException`，消息含 `clamp(min, stretch, max)`；`<Frame><Frame width='clamp(167, stretch, 250)'/></Frame>` ⇒ 消息含 `clamp(min, N%, max)`；`anchor='bottom-stretch' width='clamp(…)'` ⇒ 既有的 stretched-axis 错。
  - 嵌套：clamp 节点的父自己也是 clamp（`box2` 在 `box` 里 `width='clamp(100, 50%, 400)'`，内层 `clamp(50, 50%, 120)`），父 800 ⇒ 外 400、内 120；父 300 ⇒ 外 150、内 75。**这条验证的是"父先子后"完全由 LayoutRebuilder 接管、与 apply 顺序无关。**
- [ ] **Step 2: RUN(ControlApplyCommonClampTests)** 红。
- [ ] **Step 3: 实现**（`Control.ApplyCommon` 自由定位分支末尾，`RectTransform.sizeDelta = lr.SizeDelta;` 之后）：
  ```csharp
  var fitter = RectTransform.GetComponent<Internal.ClampFitter>();
  if (sizeSpec.IsClampedWidth || sizeSpec.IsClampedHeight)
  {
      MarginResolver.Parse(margin, out var mt, out var mr, out var mb, out var ml);
      fitter ??= RectTransform.gameObject.AddComponent<Internal.ClampFitter>();
      fitter.SetAxis(0, sizeSpec.IsClampedWidth,  sizeSpec.WidthFraction,  sizeSpec.MinWidth,  sizeSpec.MaxWidth,  ml, mr, ToAlign(preset.H));
      fitter.SetAxis(1, sizeSpec.IsClampedHeight, sizeSpec.HeightFraction, sizeSpec.MinHeight, sizeSpec.MaxHeight, mb, mt, ToAlign(preset.V));
      fitter.enabled = true;
      UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(RectTransform);
  }
  else if (fitter != null) { fitter.ClearAxis(0); fitter.ClearAxis(1); fitter.enabled = false; }
  ```
  `ToAlign`：`Left/Bottom → Low`、`Right/Top → High`、`Center → Center`（Stretch 到不了这里，`ValidateAgainst` 已拒）。布局组分支也要走 `else if (fitter != null)` 的清理 —— 把这段抽成 `SyncClampFitter(sizeSpec, preset, margin, bool freePositioning)` 在两条分支各调一次。
  - 两条既有报错消息（`Control.cs:281` / `:313`）各补一句 clamp 指引（spec §4.1 第二表）。
- [ ] **Step 4: RUN(ControlApplyCommonClampTests)** 绿；回归 `RUN(ControlApplyCommonFractionalTests)`、`RUN(FlowAttributeTests)`、`RUN(LayoutRebuildDirtyTests)`（LGC-D18 的脏计数契约不能被新加的 `MarkLayoutForRebuild` 打破 —— 只有 clamp 节点才标脏）。
- [ ] **Step 5: lint**

---

## Task 4: 布局组映射 `clamp(min, stretch, max)`（spec §5.2）

**Files:**
- Modify: `Runtime/Controls/Control.cs`（`ApplyLayoutElement`）
- Test: `Tests/EditMode/Controls/ControlApplyCommonLayoutGroupTests.cs`

- [ ] **Step 1: 红测试**：
  - 三元组（§5.2 表五行）：`clamp(167, stretch, 250)` ⇒ `le.minWidth=167, preferredWidth=250, flexibleWidth=0`；`clamp(_, stretch, 250)` ⇒ `-1 / 250 / 0`；`clamp(167, stretch, _)` ⇒ `167 / 0 / 1`；`clamp(167, stretch*2, _)` ⇒ `167 / 0 / 2`。
  - 真实布局：`<HStack id='h' anchor='top-left' width='220' height='50'><Frame id='c' width='clamp(167, stretch, 250)' height='40'/></HStack>`，`LayoutRebuilder.ForceRebuildLayoutImmediate(h.RectTransform)` 后 `c.rect.width == 220`；HStack 宽 150 ⇒ 167（溢出）；300 ⇒ 250。
  - 配 spacer：`c` + `<Frame width='stretch'/>`，HStack 300 ⇒ `c` 250、spacer 50。
  - 交叉轴：`<VStack width='150'><Frame width='clamp(100, stretch, 200)' height='20'/></VStack>` ⇒ 150；VStack 80 ⇒ 100；300 ⇒ 200。
  - Grid 下 `clamp(167, stretch, 250)` ⇒ `ParseException`（沿用 `stretch` 在 Grid 的错）。
  - `flow='false'` + `clamp(167, stretch, 250)` ⇒ `ParseException`（出流禁 stretch，沿用）。
  - 组内 clamp 节点**没有** `ClampFitter`（或存在但 `enabled=false`）。
- [ ] **Step 2: RUN(ControlApplyCommonLayoutGroupTests)** 红。
- [ ] **Step 3: 实现**（`ApplyLayoutElement` 的 `IsFlexibleWidth` 分支）：
  ```csharp
  if (sizeSpec.IsClampedWidth)
  {
      var capped = !float.IsPositiveInfinity(sizeSpec.MaxWidth);
      prefW = capped ? sizeSpec.MaxWidth : 0f;
      flexW = capped ? 0f : sizeSpec.WeightWidth;
      minW  = float.IsNegativeInfinity(sizeSpec.MinWidth) ? -1f : sizeSpec.MinWidth;
  }
  else { prefW = 0f; flexW = sizeSpec.WeightWidth; }
  ```
  Height 同。LGC-D17 注释补一句：clamp 是唯一受支持的可收缩区间。
- [ ] **Step 4: RUN(ControlApplyCommonLayoutGroupTests)** 绿；回归 `RUN(LayoutRebuildDirtyTests)`。
- [ ] **Step 5: lint**

---

## Task 5: PlayMode 回调路径

**Files:**
- Create: `Tests/PlayMode/Controls/ClampFitterPlayTests.cs`

- [ ] **Step 1: 测试**（`[UnityTest]`，fixture 同 Task 3）：`UI.Open` 后 `yield return null`；断言子宽 167（父 300）；`box.RectTransform.sizeDelta = new Vector2(800, 200)`；`yield return null` × 2（回调 → 可能延迟一帧）⇒ 子宽 250、`rect.xMin == 0`。再改回 300 ⇒ 167。**这条 EditMode 跑不到**（`OnRectTransformDimensionsChange` → 标脏 → 下一次 canvas update）。
- [ ] **Step 2: RUNPLAY(ClampFitterPlayTests)** 绿（Task 2/3 已实现，这里直接验收；若红，先查 `_delayedDirty` 路径）。

---

## Task 6: lint `PUI-CLAMP-SCALE` + 运行时硬错 + `MarginAnchorRules` 分数轴（spec §5.5 / §6.5 / §11）

**Files:**
- Create: `Runtime/Core/Lint/ClampRules.cs`
- Modify: `Runtime/Core/Lint/IRWalker.cs`、`Runtime/Core/Lint/MarginAnchorRules.cs`、`Runtime/Application/ControlAttributeApplier.cs`
- Test: `Tests/EditMode/Lint/ClampRulesTests.cs`、`Tests/EditMode/Lint/MarginAnchorRulesTests.cs`、`Tests/EditMode/Application/ControlAttributeApplierClampTests.cs`

**Interfaces：**
```csharp
public static class ClampRules
{
    public const string ClampScaleCode = "PUI-CLAMP-SCALE";
    public static bool IsClampValue(string v);                 // v?.TrimStart().StartsWith("clamp(", Ordinal)
    public static bool HasClamp(ElementNode n);                // width/height 的 base 或任一 variant 值
    public static IEnumerable<LintIssue> CheckClampScale(ElementNode n);
}
```

- [ ] **Step 1: 红测试**
  - `ClampRulesTests`：clamp base + scale base ⇒ 1 issue；clamp base + `scale.mobile` ⇒ 1 issue；`width.wide="clamp(…)"` + scale ⇒ 1 issue；clamp 无 scale ⇒ 0；scale 无 clamp ⇒ 0；消息含 `PUI-CLAMP-SCALE`、节点 tag/id、"move scale to a child"。IRWalker 分发：解析一份含该节点的文档、`IRWalker.Walk`（照 `IRWalkerMarginAnchorTests` 的入口）⇒ 命中。
  - `MarginAnchorRulesTests`：`anchor="bottom-left" width="46%" margin="0,16,0,16"` ⇒ **0 issue**（右槽被分数轴消耗）；`width="clamp(167, 46%, 250)"` 同；`height="50%"` 让 top 槽不再误报；纯数值 `width="200"` 的既有误报用例不变。
  - `ControlAttributeApplierClampTests`：`UI.Open` 一个 `<Frame width='clamp(167, 46%, 250)' scale='2'/>` ⇒ `Assert.Throws<ParseException>`，消息含 `PUI-CLAMP-SCALE`。
- [ ] **Step 2: RUN(ClampRulesTests) / RUN(MarginAnchorRulesTests) / RUN(ControlAttributeApplierClampTests)** 红。
- [ ] **Step 3: 实现**
  - `ClampRules`：纯字符串；`HasClamp` 扫 `Attributes["width"/"height"]` + `VariantOverrides["width"/"height"]` 列表；`CheckClampScale`：`HasClamp && (Attributes.ContainsKey("scale") || VariantOverrides.ContainsKey("scale"))` ⇒ 一条 issue（措辞见 spec §6.5）。
  - `IRWalker`：在 `ColorLiteralRules.Check(node)` 旁（对所有 tag 的那一段）加分发。
  - `MarginAnchorRules.Check`：`fracX = IsFractionalAxis(n, "width")`、`fracY = … "height"`（值 `EndsWith("%")` 或 `ClampRules.IsClampValue`，base 属性），`consumedLeft/Right |= fracX`，`consumedTop/Bottom |= fracY`；类注释加一条 bullet。
  - `ControlAttributeApplier.Apply`：`var anchor = …` 之前：`foreach (var issue in ClampRules.CheckClampScale(node)) throw new ParseException(FormatNodeContext(node) + ": " + issue.Message);`（issue.Message 若已带节点上下文就不重复拼 —— 看 `LintIssue` 的构造）。
- [ ] **Step 4: 三个 RUN 绿；回归 `RUN(IRWalkerMarginAnchorTests)`、`RUN(IRWalkerTests)`。**
- [ ] **Step 5: CLI 冒烟**：把 Task 6 的 fixture 落到 scratchpad 一个 `.ui.xml`，`dotnet run --project .lint/UIXmlLint -- <file>` 看到 `[PUI-CLAMP-SCALE]` 且 exit code 非零；再跑 `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` 确认内置文档零新增 issue。
- [ ] **Step 6: lint**

---

## Task 7: 文档（英文 SKILL；同 PR）

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`、`docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`、`.lint/UIXmlLint/README.md`

- [ ] **SKILL.md**
  - Common attributes 表 `width` / `height` 行（第 657 行附近）：Format 列加 `clamp(min, N%, max)` / `clamp(min, stretch, max)`；Notes 指向 "Clamp"。
  - "Fractional %" 小节之后新增 **"Clamp — min / max bounds on a `%` or `stretch` axis"**：spec §3 的四个示例；`box = clamp(f·P, min, max)`，margin insets inside the box；alignment follows `anchor`（left / right / center 表）；`_` opens a bound；布局组的 LE 表 + "siblings shrink proportionally (flex-shrink)"；`stretch*N` only with an open max；Variant 示例；"clamp + scale on one node is rejected (`PUI-CLAMP-SCALE`) — wrap the content and scale the child"；EditMode/测试提示一句：geometry lands on the next canvas update (`Canvas.ForceUpdateCanvases()` in tests)。
  - "Stretch keyword" 段末尾：`Need a floor or a cap? clamp(min, stretch, max) is the only supported shrinkable range (LGC-D17 keeps plain numbers rigid).`
  - 错误表：新增 `PUI-CLAMP-SCALE` 行；`'%' … cannot be used inside` / `'stretch' … only valid inside` 两行 Fix 列补 clamp；新增一行合并写 clamp 语法错误（both bounds open / min > max / weighted stretch capped / not 3 parts）。
  - FAQ / 反模式表：`Panel too wide on desktop, too narrow on phones` → `width="clamp(167, 46%, 250)"`。
  - uGUI 对照表：free-positioning clamp ⇒ `ClampFitter (ILayoutSelfController, internal)`；group ⇒ `LayoutElement min/preferred`。
- [ ] **master spec §6.2** 末尾加段 **"`clamp(min, N%, max)` / `clamp(min, stretch, max)`（尺寸钳制，CLP）"**：三句摘要 + 指向 `2026-08-30-clamp-size-design.md`（照 §6.5 FLW 段）。
- [ ] **README 代码表**：`PUI-CLAMP-SCALE` 一行（CLI error + runtime **throw**，与其它 "CLI + runtime warning" 区分开写）；`PUI-MARGIN-INERT-SIDE` 行补 "a `%` / `clamp(` axis consumes both its slots"。
- [ ] 若 SKILL 里的示例被复制成 fixture 文件，跑 UIXmlLint。

---

## Task 8: 全套件 + 收尾

- [ ] `RUN` 全量：`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])`、`["PromptUGUI.Tests.EditorOnly"]`（XSD 生成器测试 —— `width` 是 `xs:string`，预期零改动）、`RUNPLAY` 全量。
- [ ] `read_console(types=["error"])` 为空；`read_console(types=["warning"])` 里没有 "Trying to add … layout rebuild while we are already inside a layout rebuild loop"（这是 `_delayedDirty` 路径的守门信号）。
- [ ] `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`。
- [ ] spec §13 写实施记录：零回归数字、`_delayedDirty` 是否真的触发过（PlayMode 里加个计数探针看一眼）、实现上的小结论。
- [ ] `git add -A && git commit`（分支上），推 `feat/clamp-size`，开 PR（正文引用 spec / plan，`--delete-branch` 合并）。

## Self-Review 注记（写计划时已核）

- `Control.cs:281` 的 `%` 禁令与 `:313` 的 `stretch` 禁令都在 `SizeSpec` 标志之上判断，clamp 复用标志后**自动**命中，Task 3 只补消息。
- `LayoutRebuildDirtyTests`（LGC-D18）钉的是 `LayoutElement` 写入的脏计数；Task 3 新增的 `MarkLayoutForRebuild` 只在 clamp 节点上发生，fixture 里没有 clamp 节点 ⇒ 计数不变。
- `ApplyBoxPreservingCompensation` 只对声明了 `scale` 的节点跑；`PUI-CLAMP-SCALE` 在 `ControlAttributeApplier` 里先抛，`Screen.ApplyScales` 永远看不到 clamp + scale 的组合。
- `Frame.GetDefaultAnchor` 用 `HasWidth` 判定 ⇒ `<Frame width="clamp(…)">` 省略 anchor 时默认 left/top，与 `%` 一致，无需改。
- `TemplateExpander.CommonAttrs` / `StyleMerger` 按属性名合并，值语法透明。
- `XsdGenerator` 的 `width` / `height` 是无 pattern 的 `xs:string` ⇒ EditorOnly 套件零改动。
