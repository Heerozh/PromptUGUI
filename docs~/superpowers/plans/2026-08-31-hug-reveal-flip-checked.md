# 布局与动效基础四件套（hug / reveal / rotation-flip / checked）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 四个彼此独立的语言能力，各自可验收：(1) `width` / `height` 的 `hug` 关键字与 `clamp(min, hug, max)`——布局组容器与 `<ScrollList>` 贴合内容；(2) `<Animation>` 新增 D 族 `reveal="y|x"`（布局尺寸补间 + 裁剪）与通用 `reverse-on=`（从当前值反向）；(3) `<Image>` / `<Icon>` / `<RawImage>` 的网格级 `rotation` / `flip`；(4) `checked` / `unchecked` 持久态事件 + 所有 `@id` 改为词法三级查找。

**Architecture:** (1) `SizeSpec.Axis` 加 `IsHug`；自由定位复用 `ClampFitter` 加 `Hug` 模式（内容尺寸经 `Func<int,float>` 提供者读取），布局组内 ScrollList / clamp-hug 挂新的 `HugElement`（`ILayoutElement`，priority 1）。(2) `AnimationSpec` 加 `HasReveal` + `ReverseOn`；新 `RevealDriver`（量 / 写 box / 裁剪）被 `AnimationDriver` 与 COL 共用；`Trigger` 的订阅逻辑抽成按 `TriggerSpec` 订一次的方法，`Animation` 用它订第二个源。(3) `RotateFlipEffect : BaseMeshEffect` 绕 rect 中心改顶点，三控件共用 `RotateFlipApplier`。(4) `TriggerKind.Checked/Unchecked` + `IToggleSource`（Toggle / Tab）；`TriggerSourceResolver.ResolveId` 子树 → 祖先 `ScopedIds` → Screen，需要 `Control.Parent` 链与 `Screen.TryGet`。

**Tech Stack:** Unity 6 / C# (LangVersion 9.0)、uGUI（`ILayoutSelfController` / `ILayoutElement` / `LayoutRebuilder` / `RectMask2D` / `BaseMeshEffect`）、LitMotion、R3。无新增包。

设计依据：`docs~/superpowers/specs/2026-08-31-hug-reveal-flip-checked-design.md`（下文 §N 均指该 spec 的节号；决策 `FND-Dn`）。消费方 spec：`2026-08-31-collapsible-design.md`（COL），在本 plan 的 M3 之后开工。

## Global Constraints

- **分支**：全部工作在 `feat/hug-reveal-flip-checked`（Task 0 建）。**绝不提交到 main。**
- **LangVersion 9.0**：无 primary constructor、无 collection expression `[]`、无 `[field: SerializeField]`。switch 表达式 / target-typed `new()` / `??=` 可用。
- **不用 System.Threading / Task**：本特性无异步。
- **float 解析**：`float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)`（**禁用** `AsSpan` 重载）。
- **Core 纯 C# 子集**：`Runtime/Core/Lint/HugRules.cs` / `AnimationRules.cs` / `RotateFlipRules.cs` 与 `StateTriggerRules.cs` 的改动 **不得** `using UnityEngine`、不得引用 `SizeSpec` / `AnimationSpec`（CLI 编译集之外）—— 用字符串判定。
- **lint**：每个 Task 收尾从仓库根跑 `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`。**不要** `dotnet format analyzers --severity info`。
- **同 PR 必改 SKILL**（英文）：见 Task 14。
- **测试只经 Unity MCP 跑**。**禁止** `execute_menu_item("Assets/Reimport All")`；用 `refresh_unity(mode="force", scope="all")`。
- **InternalsVisibleTo** 已对 `PromptUGUI.Tests.EditMode` / `PromptUGUI.Tests.PlayMode` 开放。
- **Red first**：每个 Task 先写失败测试、跑一次看它**因正确的原因**失败，再写实现。
- **`.ui.xml` 改动后跑** `dotnet run --project .lint/UIXmlLint -- <file>`（本 plan 不改内置 `.ui.xml`）。
- **零回归守门**：`LayoutRebuildDirtyTests`（LGC-D18）、`SafeAreaTests`（回调不写 RT）、`TabMenu*Tests`、`Animation*Tests`、`ShowTests` 每个里程碑末尾必跑。

**RUN(ClassName) = 跑 EditMode 测试的标准流程：**
1. `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
2. `mcp__UnityMCP__read_console(action="get", types=["error"])` —— 编译错误必须为空才继续
3. `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["ClassName"])` → 轮询 `mcp__UnityMCP__get_test_job(job_id=...)` 直到完成，读 pass/fail；**核对 `summary.total` > 0**（PlayMode 连跑前先 force refresh，否则第二个 job 会 0 条执行还报 Passed）

**RUNPLAY(ClassName)** 同上，`mode="PlayMode"` + `assembly_names=["PromptUGUI.Tests.PlayMode"]`。

**Drain()** = EditMode 里驱动布局 pass：`Canvas.ForceUpdateCanvases()`（`LayoutRebuildDirtyTests.Drain` 的模式）。

---

## File Structure

| 文件 | 责任 | 动作 |
|---|---|---|
| `Runtime/Core/Layout/SizeSpec.cs` | `hug` / `clamp(min, hug, max)` 解析；`IsHugWidth/Height`；`LooksLikeKeyword` 加 `hug` | Modify |
| `Runtime/Controls/Internal/ClampFitter.cs` | `ClampMode { Fraction, Hug }`；`ContentSize` 提供者；`Apply` 分模式 | Modify |
| `Runtime/Controls/Internal/HugElement.cs` | 布局组内的 `ILayoutElement`（priority 1）：ScrollList hug / clamp-hug | Create |
| `Runtime/Controls/Internal/IHugContent.cs` | `float ContentSize(int axis)`；VStack/HStack/Grid 默认实现、ScrollList 覆盖 | Create |
| `Runtime/Controls/Control.cs` | `Parent` 链；`ApplyCommon` hug 路由（自由定位 → fitter Hug；布局组 → 哨兵 / HugElement）；`SyncClampFitter` 传模式 | Modify |
| `Runtime/Controls/ScrollList.cs` | 实现 `IHugContent`（`_content` preferred） | Modify |
| `Runtime/Controls/VStack.cs` `HStack.cs` `Grid.cs` | 实现 `IHugContent`（自身 LayoutGroup preferred） | Modify |
| `Runtime/Core/Lint/HugRules.cs` | `PUI-HUG-TAG` / `PUI-HUG-SCALE` / `PUI-HUG-STRETCH-CHILD` | Create |
| `Runtime/Controls/Internal/RotateFlipEffect.cs` | `BaseMeshEffect`，绕 rect 中心镜像 + 旋转 | Create |
| `Runtime/Controls/Internal/RotateFlipApplier.cs` | 三控件共用的属性落点（懒加 / 归一 / 恒等禁用） | Create |
| `Runtime/Controls/Image.cs` `Icon.cs` `RawImage.cs` | `[UIAttr] Rotation` / `Flip` | Modify |
| `Runtime/Core/Lint/RotateFlipRules.cs` | `PUI-FLIP-TAG`、值格式 | Create |
| `Editor/XsdGenerator.cs` | 手写清单的 `Image` / `Icon` 加 `rotation` / `flip`（RawImage 反射） | Modify |
| `Runtime/Controls/Internal/AnimationSpec.cs` | D 族字段、`ReverseOn`、校验、快照 | Modify |
| `Runtime/Controls/Internal/RevealDriver.cs` | `Measure` / `ApplyBox` / `SetClip`（COL 复用） | Create |
| `Runtime/Controls/Internal/AnimationDriver.cs` | `Play(spec, ctx, reverse)`；从当前值起；reveal 通道；循环块去重 | Modify |
| `Runtime/Controls/Animation.cs` | 四个新属性；`RectMask2D`；静止态；`OnAfterApply` 重断言；`Reverse()` / `OnReverse`；`GetNativeSize` | Modify |
| `Runtime/Controls/Trigger.cs` | `SubscribeSpec(spec, onFire)` 抽出；`OnTriggerFiredInitial` 虚方法；`SubscribeChecked` | Modify |
| `Runtime/Controls/Internal/TriggerSpec.cs` | `Checked` / `Unchecked`；`ParseReverseOn` | Modify |
| `Runtime/Controls/Internal/TriggerSourceResolver.cs` | `ResolveId` 词法三级；`FindToggleSource`；三处调用改用 | Modify |
| `Runtime/Controls/Internal/IToggleSource.cs` | `IsOn` / `OnValueChanged` / `RegisterCheckedShow` | Create |
| `Runtime/Controls/Toggle.cs` `Tab.cs` | 实现 `IToggleSource` | Modify |
| `Runtime/Controls/Show.cs` | 接受 `checked` / `unchecked` | Modify |
| `Runtime/Application/Screen.cs` | `internal bool TryGet(string id, out IControl)` | Modify |
| `Runtime/Core/Lint/AnimationRules.cs` | `PUI-REVEAL-*` / `PUI-REVERSE-*` | Create |
| `Runtime/Core/Lint/StateTriggerRules.cs` | `PUI-CHECKED-NO-SOURCE`；`IsToggleSourceTag` | Modify |
| `Runtime/Core/Lint/IRWalker.cs` | 分发 Hug / RotateFlip / Animation / Checked 规则；`hasToggleSourceAncestor` | Modify |
| `Runtime/Application/ScreenInstantiator.cs` | 运行时 warning 镜像分发 | Modify |
| `Runtime/Application/ControlAttributeApplier.cs` | `PUI-HUG-SCALE` / `PUI-REVEAL-SCALE` 硬错误 | Modify |
| `Tests/EditMode/Layout/SizeSpecTests.cs` | hug 解析用例 | Modify |
| `Tests/EditMode/Controls/ClampFitterTests.cs` | Hug 模式直驱 | Modify |
| `Tests/EditMode/Controls/HugSizingTests.cs` | 经 `UI.Open` 的自由定位 / 布局组 / Variant / 幂等 | Create |
| `Tests/EditMode/Lint/HugRulesTests.cs` | 三条 lint + 分发 | Create |
| `Tests/EditMode/Application/ControlAttributeApplierHugTests.cs` | `PUI-HUG-SCALE` 运行时硬错 | Create |
| `Tests/EditMode/Controls/RotateFlipEffectTests.cs` | 纯几何 | Create |
| `Tests/EditMode/Controls/ImageRotateFlipTests.cs` | 属性落点 / Variant / RT 不变 | Create |
| `Tests/EditMode/Lint/RotateFlipRulesTests.cs` | `PUI-FLIP-TAG` | Create |
| `Tests/EditMode/Editor/XsdGeneratorTests.cs`（既有） | `rotation` / `flip` 出现在 Image / Icon 元素上 | Modify |
| `Tests/EditMode/Controls/AnimationSpecTests.cs` | D 族 / `reverse-on` 解析与互斥 | Modify |
| `Tests/EditMode/Controls/AnimationRevealTests.cs` | 结构 / 静止态 / LE / 重断言 | Create |
| `Tests/PlayMode/Controls/AnimationRevealPlayTests.cs` | 补间中兄弟位移、hug 终点、fade 同步 | Create |
| `Tests/PlayMode/Controls/AnimationReversePlayTests.cs` | 反向从当前值、`manual` + `Reverse()`、B 族反向 | Create |
| `Tests/EditMode/Lint/AnimationRulesTests.cs` | `PUI-REVEAL-*` / `PUI-REVERSE-*` | Create |
| `Tests/EditMode/Controls/TriggerIdScopeTests.cs` | 三级查找 / 模板实例隔离 / 错误消息 | Create |
| `Tests/EditMode/Controls/CheckedTriggerTests.cs` | 开屏派发 / 翻转 / 组互斥 / Show / Animation 终态 | Create |
| `Tests/EditMode/Lint/StateTriggerRulesTests.cs`（既有） | `PUI-CHECKED-NO-SOURCE` | Modify |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` + `reference/animations.md` + `reference/states.md` | 语法 / 语义 / 错误表（英文） | Modify |
| `.claude/skills/scripting-promptugui-csharp/SKILL.md` | `Reverse()` / `OnReverse` / `Rotation` / `Flip` / hug 测试提示 | Modify |
| `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` | §6.2 末尾 FND 摘要段 | Modify |
| `.lint/UIXmlLint/README.md` | 代码表新增 8 条 | Modify |
| `docs~/superpowers/specs/2026-08-31-hug-reveal-flip-checked-design.md` | §11 实施记录 | Modify |

---

## Task 0: 建分支 + 落 plan

- [ ] `git checkout -b feat/hug-reveal-flip-checked`
- [ ] 本文件、COL plan 与两份 spec 一起 `git add docs~/` 后提交（分支上）。

---

# M1 —— `hug`

## Task 1: `SizeSpec` 解析 `hug` 与 `clamp(min, hug, max)`（spec §1.3）

**Files:**
- Modify: `Runtime/Core/Layout/SizeSpec.cs`
- Test: `Tests/EditMode/Layout/SizeSpecTests.cs`

**Interfaces（新增，公开面向 Controls）：**
```csharp
public bool IsHugWidth  { get; }   // width="hug" 或 clamp(min, hug, max)
public bool IsHugHeight { get; }
// hug 轴：Has=true、Numeric=0、IsNative/IsFlexible/IsFractional=false；clamp 形态叠 IsClamped + Min/Max
```

- [ ] **Step 1: 红测试** —— `SizeSpecTests` 加 `Hug_*`：
  - `width="hug"` ⇒ `HasWidth && IsHugWidth && !IsFlexibleWidth && !IsFractionalWidth && !IsClampedWidth`，`Width == 0`。
  - `height="clamp(_, hug, 200)"` ⇒ `IsHugHeight && IsClampedHeight && MinHeight = -∞ && MaxHeight = 200`；`clamp(100, hug, _)`、`clamp(100, hug, 200)`。
  - `clamp(_, hug, _)` ⇒ "both bounds open"（既有消息）；`clamp(300, hug, 250)` ⇒ min > max。
  - `size="hug"` / `size="hugxhug"` ⇒ 既有 "numeric-only" 那条（`StringAssert.Contains("numeric-only")`）。
  - `hug*2` / `Hug` / `hug%` ⇒ `ArgumentException`（`hug` 之外的拼写落到 `ParseFloat` 的 "is not a number"，够用；`Hug` 大写同）。
  - `ValidateAgainst`：`anchor="top-stretch"` + `width="hug"` ⇒ 既有 "cannot specify width/size on a horizontally-stretched axis"。
  - `WithFallbackForMissing` / `WithNativeResolved` 不覆盖 hug 轴（`Has` 已为 true）。
- [ ] **Step 2: RUN(SizeSpecTests)** —— 新用例红（先加空属性 `IsHugWidth => false` 让它编译）。
- [ ] **Step 3: 实现**
  - `Axis` 加 `IsHug` 字段（构造函数末尾加参数；`None` / `Fixed` / `WithNumeric` / `WithBounds` 透传，`WithNumeric` 保持 `IsHug` 原值）。
  - `ParseAxis`：在 `native` 之后加 `if (value == "hug") return new Axis(true, 0f, false, false, 1f, false, 0f, false, -∞, +∞, isHug: true);`。
  - `ParseClamp` middle 谓词改为 `if (!mid.IsFractional && !mid.IsFlexible && !mid.IsHug || mid.IsClamped)`，消息补 "or 'hug' (content-fit)"；"both bounds open" 消息里的 `write '{middle}' instead` 对 hug 自然成立。
  - `LooksLikeKeyword` 加 `s.Contains("hug")`。
  - 公开 `IsHugWidth` / `IsHugHeight`。
- [ ] **Step 4: RUN(SizeSpecTests)** 全绿；顺跑 `RUN(SizeSpecFromNumericTests)`、`RUN(SizeSpecNativeTests)`、`RUN(ClampRulesTests)`。
- [ ] **Step 5: lint**

---

## Task 2: `ClampFitter` 加 `Hug` 模式（spec §1.4.2 / §1.5）—— 先单测直驱

**Files:**
- Modify: `Runtime/Controls/Internal/ClampFitter.cs`
- Create: `Runtime/Controls/Internal/IHugContent.cs`
- Test: `Tests/EditMode/Controls/ClampFitterTests.cs`

**Interfaces：**
```csharp
internal enum ClampMode { Fraction, Hug }
internal interface IHugContent { float ContentSize(int axis); }   // 0 = X, 1 = Y

// ClampFitter
internal System.Func<int, float> ContentSize;   // Hug 模式读它；null → LayoutUtility.GetPreferredSize((RectTransform)transform, axis)
internal void SetAxis(int axis, bool on, ClampMode mode, float fraction, float min, float max,
                      float marginLow, float marginHigh, ClampAlign align);
```
`AxisSpec` 加 `Mode`；`Same()` 比较它；`Apply`：`var box = Mathf.Clamp(spec.Mode == ClampMode.Hug ? Content(axis) : spec.Fraction * p, spec.Min, spec.Max);` —— 其余（margin 内缩、`ClampAlign` 贴边、offset 写入、`_selfWriting`、`_delayedDirty`）一行不动。`parent == null` 早退**保留**（对齐 High / Center 仍要父尺寸）。

- [ ] **Step 1: 红测试** —— `ClampFitterTests` 加 `Hug_*`（沿用文件里既有的"手搭 Canvas + 父 RT + 子 RT"夹具）：
  - 子 RT 挂 `VerticalLayoutGroup` + 三个 44 高、spacing 4 的孙节点（`LayoutElement.preferredHeight=44`），`SetAxis(1, true, Hug, 0, -∞, +∞, 0, 0, High)` → `Drain()` → `rect.height == 140`、`offsetMax.y == 0`（贴上沿）。
  - 停用一个孙节点 → `Drain()` → 96。
  - `ClampAlign.Low` 贴下沿（`offsetMin.y == 0`）；`Center` 居中。
  - `Min=100, Max=120` 两端钳。
  - `ContentSize = axis => 77f` 提供者优先于 `LayoutUtility`。
  - `Same()`：同规格二次 `SetAxis` 不 `MarkLayoutForRebuild`（照 `LayoutRebuildDirtyTests.PendingLayoutRebuilds()` 探针）。
  - 既有 Fraction 用例全部不变（签名加参数后补 `ClampMode.Fraction`）。
- [ ] **Step 2: RUN(ClampFitterTests)** 红。
- [ ] **Step 3: 实现**（上面的 Interfaces）。
- [ ] **Step 4: RUN(ClampFitterTests)** 全绿；`RUN(ControlApplyCommonClampTests)` 零回归。
- [ ] **Step 5: lint**

---

## Task 3: `HugElement` + `ApplyCommon` 路由 + 四个容器实现 `IHugContent`（spec §1.4.2 / §1.4.3 / §1.4.5）

**Files:**
- Create: `Runtime/Controls/Internal/HugElement.cs`
- Modify: `Runtime/Controls/Control.cs`、`Runtime/Controls/VStack.cs`、`HStack.cs`、`Grid.cs`、`ScrollList.cs`
- Test: `Tests/EditMode/Controls/HugSizingTests.cs`

**Interfaces：**
```csharp
// HugElement : UIBehaviour, ILayoutElement — layoutPriority = 1；关轴全部返回 -1
internal void SetAxis(int axis, bool on, float min, float max, System.Func<int, float> content);
internal void ClearAxis(int axis);
// CalculateLayoutInput*：value = Mathf.Clamp(content(axis), min, max)；min = preferred = value；flexible = 0（刚性，LGC-D17）

// IHugContent 实现
// VStack/HStack : _layout.preferredWidth / preferredHeight（直接读同 GO 的 LayoutGroup 属性，不走 LayoutUtility —— 避免把 HugElement 自己算进去）
// Grid          : _grid.preferredWidth / preferredHeight
// ScrollList    : LayoutUtility.GetPreferredSize(_content, axis)
```
`Control.ApplyCommon`：
- 自由定位分支：hug 轴不做 native 兜底（`Has` 已 true）；`MarginResolver` 照数值 0 写点锚基线；`SyncClampFitter` 改为 `wantX = freePositioning && (IsClampedWidth || IsHugWidth)`、`mode = IsHugWidth ? Hug : Fraction`，并把 `fitter.ContentSize = axis => ((IHugContent)this).ContentSize(axis)`（控件实现了接口时；否则留 null）。
- 布局组分支 `ApplyLayoutElement`：`IsHugWidth && !IsClampedWidth && this is VStack/HStack/Grid` → 该轴留 `-1` 哨兵（不写 `prefW = 0`）；否则（ScrollList 裸 hug、任何 clamp-hug）→ 该轴 LE 留 `-1`，新 `SyncHugElement(sizeSpec)` 在 `LayoutHost` 上挂 / 更新 / 清 `HugElement`（模式同 `SyncClampFitter`：只 enabled 切换、`Same` 不标脏）。

- [ ] **Step 1: 红测试** —— `HugSizingTests`（`UI.LoadDocument` + `UI.Open` + `Drain()`，`[SetUp]/[TearDown] UI.ResetForTests()`）：
  - **自由定位**：`<VStack id='v' anchor='top-right' width='150' height='hug' spacing='4'>` 三个 `<Btn height='44'/>` ⇒ `rect.height == 140`、贴上沿（`anchoredPosition` / offset 与 `anchor="top-right" height="140"` 位相等）；`anchor='bottom-left'` 贴下沿。
  - `<Image anchor='stretch' flow='false'/>` 在 hug VStack 里 ⇒ 高 140（底图跟随）。
  - 隐藏一个 Btn（`Hidden = true`）→ `Drain()` ⇒ 96。
  - `<ScrollList height='hug'>` 五行 32（`itemTemplate` + `BindItems`）⇒ 视口 160 + padding；`height='clamp(_, hug, 100)'` ⇒ 100 且 `ScrollRect` 可滚（content 高 > viewport）。
  - `<Grid columns='3' cellSize='40x40' height='hug'>` 七个子节点 ⇒ 3 行 = 120（+spacing）。
  - `width='hug' height='200'` 两轴独立。
  - **布局组内**：`<VStack><VStack height='hug'>…</VStack></VStack>` 与不写 `height` 几何位相等；`<VStack><ScrollList height='hug'/></VStack>` 外层量到内容高；`<VStack><VStack height='clamp(_, hug, 100)'>` 三行 44 ⇒ 100、`LayoutElement` 该轴仍 -1 且 `HugElement` 报 min=preferred=100。
  - **Variant**：`height='200' height.portrait='hug'` 切换往返 ⇒ 几何位相等、fitter 只 enabled 切换、二次 ReSolve 不标脏。
  - **stretch 子节点**：hug 轴上 `<Btn height='stretch'/>` 得 0 高（行为断言，lint 在 Task 4）。
- [ ] **Step 2: RUN(HugSizingTests)** 红。
- [ ] **Step 3: 实现**（`Parent` 链此 Task 顺手加：`internal Control Parent { get; private set; }`，`AddChild` 里设置 —— Task 11 要用）。
- [ ] **Step 4: RUN(HugSizingTests)** 全绿；回归 `RUN(ControlApplyCommonLayoutGroupTests)`、`RUN(ControlApplyCommonClampTests)`、`RUN(LayoutRebuildDirtyTests)`、`RUN(ScrollListTests)`。
- [ ] **Step 5: lint**

---

## Task 4: lint `HugRules` + 双分发 + 运行时硬错（spec §1.3 / §1.4.4 / §1.5）

**Files:**
- Create: `Runtime/Core/Lint/HugRules.cs`
- Modify: `Runtime/Core/Lint/IRWalker.cs`、`Runtime/Application/ScreenInstantiator.cs`、`Runtime/Application/ControlAttributeApplier.cs`
- Test: `Tests/EditMode/Lint/HugRulesTests.cs`、`Tests/EditMode/Application/ControlAttributeApplierHugTests.cs`

**Interfaces（纯 C#）：**
```csharp
public static class HugRules {
  public const string TagCode = "PUI-HUG-TAG", ScaleCode = "PUI-HUG-SCALE", StretchChildCode = "PUI-HUG-STRETCH-CHILD";
  public static readonly HashSet<string> HugTags = new() { "VStack", "HStack", "Grid", "ScrollList" };   // COL 加 "Collapsible"
  public static bool IsHugValue(string v);                 // "hug" 或 clamp(...) 中段 Trim() == "hug"
  public static bool HasHug(ElementNode n, string axis);   // base 或任一 variant（照 ClampRules.HasClamp）
  public static IEnumerable<LintIssue> CheckHugTag(ElementNode n);
  public static IEnumerable<LintIssue> CheckHugScale(ElementNode n);
  public static IEnumerable<LintIssue> CheckHugStretchChild(ElementNode parent, ElementNode child);
}
```
分发：`IRWalker.WalkNodeCore` 属性段（每个节点）`CheckHugTag` + `CheckHugScale`；子节点循环里 `isLayoutGroup` 分支加 `CheckHugStretchChild(node, child)`。`ScreenInstantiator` 镜像为 `Debug.LogWarning`（`CheckHugTag` 在运行时是 `ParseException` —— 由 `ControlAttributeApplier` 在 `CheckClampScale` 旁一起抛：`HugRules.CheckHugTag` + `CheckHugScale`）。

- [ ] **Step 1: 红测试** —— `HugRulesTests`：`<Frame height='hug'>` ⇒ `PUI-HUG-TAG` 且消息含 "VStack"；`<Image width='hug'>` 消息含 "native"；`<VStack height='hug' scale='0.5'>` ⇒ `PUI-HUG-SCALE`；`height.portrait='clamp(_, hug, 200)'` 也触发；`<VStack height='hug'><Btn height='stretch'/></VStack>` ⇒ `PUI-HUG-STRETCH-CHILD`，`width='stretch'` 不触发（交叉轴）；`IRWalker.Walk` 端到端各一例。`ControlAttributeApplierHugTests`：`UI.Open` 含 `<Frame height='hug'>` ⇒ `ParseException` 消息含 `[PUI-HUG-TAG]`；`PUI-HUG-SCALE` 同。
- [ ] **Step 2: RUN(HugRulesTests)** / **RUN(ControlAttributeApplierHugTests)** 红。
- [ ] **Step 3: 实现**。
- [ ] **Step 4: 两个类全绿**；`RUN(DocumentLinterTests)`、`RUN(IRWalkerMarginAnchorTests)` 回归。
- [ ] **Step 5: lint**；跑 `cd .lint && dotnet build UIXmlLint`（纯 C# 边界守门）。

---

# M2 —— `rotation` / `flip`

## Task 5: `RotateFlipEffect`（spec §3.4 / §3.5）—— 纯几何

**Files:**
- Create: `Runtime/Controls/Internal/RotateFlipEffect.cs`
- Test: `Tests/EditMode/Controls/RotateFlipEffectTests.cs`

**Interfaces：**
```csharp
[DisallowMultipleComponent]
internal sealed class RotateFlipEffect : BaseMeshEffect {
  public float Rotation { get; set; }   // 顺时针为正（CSS）；setter 归一到 [0, 360) 并 SetVerticesDirty
  public bool FlipX { get; set; }
  public bool FlipY { get; set; }
  public bool IsIdentity => Rotation == 0f && !FlipX && !FlipY;
  public override void ModifyMesh(VertexHelper vh);   // 先 flip 后 rotate，原点 = graphic.rectTransform.rect.center；恒等直接 return
  internal static void Transform(ref UIVertex v, Vector2 center, bool fx, bool fy, float cos, float sin);   // 供测试直接调
}
```
顺时针：Unity Z+ 为逆时针，故以 `-Rotation` 度构造 `cos/sin`。

- [ ] **Step 1: 红测试** —— 不建 Canvas：手搭 `VertexHelper` 四顶点 `(-1,-1)(-1,1)(1,1)(1,-1)`，center 0：`Rotation=90` ⇒ `(1,1)` → `(1,-1)`（顺时针）；`FlipX` ⇒ `(1,1)` → `(-1,1)` 且 UV 跟着顶点走（同一顶点的 uv0 不变、位置变）；`FlipX+FlipY` == `Rotation=180`（逐顶点近似相等）；恒等不改；`center=(10,0)` 时绕 (10,0) 转。
- [ ] **Step 2: RUN(RotateFlipEffectTests)** 红。
- [ ] **Step 3: 实现**。
- [ ] **Step 4: RUN(RotateFlipEffectTests)** 绿。
- [ ] **Step 5: lint**

---

## Task 6: 三控件属性 + XSD 手写清单 + lint（spec §3.3 / §3.5）

**Files:**
- Create: `Runtime/Controls/Internal/RotateFlipApplier.cs`、`Runtime/Core/Lint/RotateFlipRules.cs`
- Modify: `Runtime/Controls/Image.cs`、`Icon.cs`、`RawImage.cs`、`Editor/XsdGenerator.cs`、`Runtime/Core/Lint/IRWalker.cs`、`Runtime/Application/ScreenInstantiator.cs`
- Test: `Tests/EditMode/Controls/ImageRotateFlipTests.cs`、`Tests/EditMode/Lint/RotateFlipRulesTests.cs`、`Tests/EditMode/Editor/XsdGeneratorTests.cs`

**Interfaces：**
```csharp
// 三控件各加（Icon 同；RawImage 同）
[UIAttr, Preserve] public float  Rotation { get; set; }   // 读写；C# 可补间
[UIAttr, Preserve] public string Flip     { get; set; }   // "x" | "y" | "xy" | "none" | ""

internal static class RotateFlipApplier {
  public static void Apply(Graphic g, float rotation, string flip);   // 解析 flip（非法 → ArgumentException 列出合法值）；恒等 → 组件 enabled=false（不销毁，Variant 往返幂等）；非恒等 → 懒加并写值
}
public static class RotateFlipRules {   // 纯 C#
  public const string TagCode = "PUI-FLIP-TAG", ValueCode = "PUI-FLIP-VALUE";
  public static readonly HashSet<string> AllowedTags = new() { "Image", "Icon", "RawImage" };
  public static IEnumerable<LintIssue> Check(ElementNode n, StyleAttributeView styles);   // rotation/flip 出现在其它标签（含经 class= 到达）→ TagCode；flip 非枚举 / rotation 非数字 → ValueCode
}
```
XSD：`XsdGenerator.cs` 的 `WriteControl(writer, "Image", …)` 与 `"Icon"` 手写清单各加 `("rotation","xs:string",null)`、`("flip","xs:string",null)`（`RawImage` 走反射，自动）。

- [ ] **Step 1: 红测试** —— `ImageRotateFlipTests`：`<Icon rotation='90'>` ⇒ `RotateFlipEffect.Rotation == 90 && enabled`；`rotation='0' flip='none'` ⇒ 组件不存在或 disabled；`flip='xy'`；`rotation.portrait='180'` Variant 往返；RectTransform `sizeDelta` / `anchoredPosition` 与不写时位相等；父 `<HStack>` 里兄弟位置不变；`flip='z'` ⇒ `ParseException` 消息列出 `x / y / xy / none`。`RotateFlipRulesTests`：`<Btn rotation='90'>` ⇒ `PUI-FLIP-TAG` 消息含 "inner <Image>"；`<Frame class='r'>` + `<Style name='r' flip='x'/>` 也触发；`<Image flip='q'>` ⇒ `PUI-FLIP-VALUE`。`XsdGeneratorTests`：`StringAssert.Contains("name=\"rotation\"")` 在 `Image` 与 `Icon` 元素块内（照文件里既有的段落定位方式）。
- [ ] **Step 2: RUN** 三个类红。
- [ ] **Step 3: 实现**；`IRWalker` 每节点分发 `RotateFlipRules.Check`，`ScreenInstantiator` 镜像 warning。
- [ ] **Step 4: 三个类绿**；`RUN(ImageTests)`、`RUN(IconTests)`、`RUN(RawImageTests)`、EditorOnly 套件（`assembly_names=["PromptUGUI.Tests.EditorOnly"]`）回归。
- [ ] **Step 5: lint**

---

# M3 —— `reveal` / `reverse-on`

## Task 7: `AnimationSpec` D 族与 `reverse-on` 解析 / 校验（spec §2.3）

**Files:**
- Modify: `Runtime/Controls/Internal/AnimationSpec.cs`、`Runtime/Controls/Internal/TriggerSpec.cs`、`Runtime/Controls/Animation.cs`（四个 `[UIAttr]`）
- Test: `Tests/EditMode/Controls/AnimationSpecTests.cs`

**Interfaces：**
```csharp
internal readonly struct RevealValue { public readonly bool IsHug; public readonly float Px; public static RevealValue Parse(string s); }
// AnimationSpec
public bool HasReveal; public int RevealAxis;            // 0 = x, 1 = y
public RevealValue RevealFrom = px 0, RevealTo = hug;
public TriggerSpec ReverseOn;                            // null = 无
public void SetReveal(string v);  SetRevealFrom / SetRevealTo / SetReverseOn
// Validate(): HasReveal && (preset || text) → error；HasReveal && !lowLevel → Family = LowLevel（D 单独存在也走 LowLevel 播放路径）
//             ReverseOn != null && LoopMode != None → error("reverse-on cannot be combined with loop")
//             ReverseOn != null && text → error；RevealFrom 与 RevealTo 相等（同 IsHug 且同 Px）→ error
// TriggerSpec.ParseReverseOn(string): Parse 后拒绝 Open / Loop（消息 "reverse-on cannot be 'open' or 'loop'"）
// Snapshot / Clone 透传新字段
```
`Animation.cs`：`[UIAttr("reveal")] RevealAttr`、`[UIAttr("reveal-from")]`、`[UIAttr("reveal-to")]`、`[UIAttr("reverse-on")]`。

- [ ] **Step 1: 红测试** —— `AnimationSpecTests` 加：`reveal="y"` ⇒ `HasReveal && RevealAxis == 1 && RevealFrom.Px == 0 && RevealTo.IsHug`；`reveal="x"`；`reveal="z"` ⇒ error；`reveal-from="24" reveal-to="hug"`；`reveal-from="hug" reveal-to="hug"` ⇒ error；`reveal="y" fade="0:1"` ⇒ 合法且 `Family == LowLevel`；`reveal="y" type="fadein"` ⇒ error；`reveal="y" count="0:1"` ⇒ error；`reverse-on="collapse@x"` ⇒ `ReverseOn.Kind == Collapse && SourceId == "x"`；`reverse-on="open"` / `"loop"` ⇒ error；`reverse-on="click" loop="yoyo"` ⇒ error；`reverse-on="click" count="0:9"` ⇒ error；`Clone()` / `Snapshot()` 含新字段（改 `RevealTo` 后 snapshot 不等）。
- [ ] **Step 2: RUN(AnimationSpecTests)** 红。
- [ ] **Step 3: 实现**。
- [ ] **Step 4: RUN(AnimationSpecTests)** 绿；`RUN(AnimationTests)` 回归。
- [ ] **Step 5: lint**

---

## Task 8: `RevealDriver` + `Animation` 的结构与静止态（spec §2.4.1–§2.4.4 / §2.4.7）

**Files:**
- Create: `Runtime/Controls/Internal/RevealDriver.cs`
- Modify: `Runtime/Controls/Animation.cs`
- Test: `Tests/EditMode/Controls/AnimationRevealTests.cs`

**Interfaces：**
```csharp
internal static class RevealDriver {
  public static float Measure(RectTransform child, int axis);
  //   child inactive → 临时 SetActive(true)（TM BeforeRebuild 的套路）→ LayoutRebuilder.ForceRebuildLayoutImmediate(child)
  //   → LayoutUtility.GetPreferredSize(child, axis) → 还原激活态
  public static void ApplyBox(RectTransform host, int axis, float value, bool inLayoutGroup);
  //   inLayoutGroup: LayoutElement(min = preferred = value, flexible = 0) 于该轴；else: sizeDelta[axis] = value
  public static void SetClip(GameObject host, bool on);   // RectMask2D 懒加，只切 enabled
}
// Animation
private float _revealBox; private bool _revealInitialized;
private RectTransform RevealHost => LayoutHost;          // wrapper 模式下是 wrapper，否则 RectTransform
private float ResolveReveal(RevealValue v) => v.IsHug ? RevealDriver.Measure(ChildRt(), _animSpec.RevealAxis) : v.Px;
internal override void OnAfterApply() {
  … base.OnAfterApply();
  if (_animSpec.HasReveal) {
    if (!_revealInitialized) { _revealBox = ResolveReveal(_animSpec.RevealFrom); _revealInitialized = true; }   // FND-D6 静止态 = from
    RevealDriver.ApplyBox(RevealHost, axis, _revealBox, inLayoutGroup);                                          // 重断言（ApplyCommon 刚重置过）
    RevealDriver.SetClip(RevealHost.gameObject, _revealBox < ResolveReveal(hug));
  }
}
public override Vector2? GetNativeSize()   // HasReveal 时主轴 = _revealBox，交叉轴照 base
```
注意顺序：`OnAfterApply` 里 `base.OnAfterApply()` 会做 `on="open"` 的初始 `Fire()`，那会启动补间；静止态初始化必须在 `base` **之前**（先立 from，再让 open 的 Fire 从 from 起播）。

- [ ] **Step 1: 红测试** —— `AnimationRevealTests`（EditMode，不等补间）：
  - VStack 里 `<Animation id='a' on='manual' reveal='y'><VStack height='hug'>三个 44</VStack></Animation>` 后接 `<Btn id='below'/>`：`Drain()` ⇒ `a` 的 `LayoutElement.preferredHeight == 0 && minHeight == 0`，`below` 紧贴顶部；`RectMask2D` 存在且 enabled。
  - `reveal-from='hug'` ⇒ `preferredHeight == 140`、`RectMask2D` disabled。
  - `reveal-from='24'` ⇒ 24。
  - 自由定位父级：`sizeDelta.y == 0`；`anchor='bottom-left'` 下 pivot/anchor 与不写 reveal 时一致。
  - `reveal='x'` 写 `preferredWidth`。
  - ReSolve（`UI.Variants.Set("x", true)`）后 `preferredHeight` 仍是静止态值（重断言）。
  - `GetNativeSize()` 主轴返回当前 box。
  - 子节点 inactive（包在 `<Show>` 的未选态里）时 `Measure` 仍量到 140（临时激活）。
- [ ] **Step 2: RUN(AnimationRevealTests)** 红。
- [ ] **Step 3: 实现**。
- [ ] **Step 4: RUN(AnimationRevealTests)** 绿；`RUN(AnimationTests)`、`RUN(TriggerTests)`（如有）回归。
- [ ] **Step 5: lint**

---

## Task 9: `AnimationDriver` 反向 + 从当前值起 + `Trigger` 第二源 + `Reverse()`（spec §2.4.5 / §2.4.6）

**Files:**
- Modify: `Runtime/Controls/Internal/AnimationDriver.cs`、`Runtime/Controls/Animation.cs`、`Runtime/Controls/Trigger.cs`
- Test: `Tests/PlayMode/Controls/AnimationRevealPlayTests.cs`、`Tests/PlayMode/Controls/AnimationReversePlayTests.cs`

**Interfaces：**
```csharp
// AnimationDriver
internal struct AnimationContext { public RectTransform Proxy; public CanvasGroup Cg; public TMP_Text Text;
                                   public RectTransform RevealHost; public bool InLayoutGroup; public System.Func<float> RevealTarget; public System.Action<float> RevealWrite; }
public static MotionHandle[] Play(AnimationSpec spec, in AnimationContext ctx, bool reverse, bool fromCurrent);
//   fromCurrent = spec.ReverseOn != null || spec.HasReveal（FND-D7）；reverse 时 from/to 互换后再按 fromCurrent 取起点
//   每通道起点：Proxy.anchoredPosition / localScale / localEulerAngles.z / Cg.alpha / RevealTarget()
//   六段循环块合成一个 private static MotionBuilder<...> WithLoop(builder, spec)
// Trigger
protected IDisposable SubscribeSpec(TriggerSpec spec, System.Action onFire);   // 把 InitTriggerSubscription 的 switch 抽出来；Open/Loop 直接 onFire()
protected virtual void OnTriggerFiredInitial() => Fire();                       // Task 12 用
// Animation
private IDisposable _reverseSub;
private readonly Subject<Unit> _reverse = new(); public Observable<Unit> OnReverse => _reverse;
public void Reverse();   // CancelCurrent → Play(reverse: true) → _reverse.OnNext
protected override void InitTriggerSubscription() { base…; if (_animSpec.ReverseOn != null) _reverseSub = SubscribeSpec(_animSpec.ReverseOn, Reverse); }
// reveal 完成回调：正向到 hug → SetClip(false)；反向起步 → SetClip(true)
```

- [ ] **Step 1: 红测试** —— PlayMode（照 `TabMenuPlayTests` 的 Header/Footer + `UI.LoadDocument` + `UI.Open`）：
  - `AnimationRevealPlayTests`：`on='manual' reveal='y' duration='0.2s'` → `Fire()` → 逐帧采样 `below.anchoredPosition.y` 单调下移、0.3s 后 `preferredHeight == 140` 且 `RectMask2D` disabled；`reveal='y' fade='0:1'` 中途 `alpha` 与 `box/140` 同向；`on='open'` 开屏自动播放。
  - `AnimationReversePlayTests`：`on='manual' reverse-on='manual' reveal='y'`：`Fire()` 等 0.1s → `Reverse()` → 下一帧 box 与上一帧差 < 10（无跳变）→ 0.3s 后 box == 0 且 `RectMask2D` enabled；B 族 `rotate='0:180' reverse-on='click@b'`：点 `b` 后从当前角度回 0；`OnReverse` 触发计数 1；`translate` + `reverse-on` 正向也从当前值起（连点两次 `Fire()` 不回跳到 from）；无 `reverse-on` 的 `translate` 保持"先写 from"（既有行为回归断言）。
- [ ] **Step 2: RUNPLAY** 两个类红（`refresh_unity(mode="force")` 后再跑，核对 `total`）。
- [ ] **Step 3: 实现**。
- [ ] **Step 4: RUNPLAY** 两个类绿；`RUNPLAY(TabMenuPlayTests)`、`RUN(AnimationTests)` 回归。
- [ ] **Step 5: lint**

---

## Task 10: lint `AnimationRules`（spec §2.3 表）

**Files:**
- Create: `Runtime/Core/Lint/AnimationRules.cs`
- Modify: `Runtime/Core/Lint/IRWalker.cs`、`Runtime/Application/ScreenInstantiator.cs`、`Runtime/Application/ControlAttributeApplier.cs`（`PUI-REVEAL-SCALE` 硬错）
- Test: `Tests/EditMode/Lint/AnimationRulesTests.cs`

**Interfaces（纯 C#，字符串判定）：**
```csharp
public static class AnimationRules {
  // 代码：PUI-REVEAL-SINGLE-CHILD / PUI-REVEAL-SIZE-CONFLICT / PUI-REVEAL-SCALE / PUI-REVEAL-CHILD-STRETCH
  //       PUI-REVERSE-LOOP / PUI-REVERSE-TEXT / PUI-REVERSE-ON-TAG
  public static IEnumerable<LintIssue> CheckAnimation(ElementNode n);          // 上四条 + 前两条（属性组合）
  public static IEnumerable<LintIssue> CheckReverseOnTag(ElementNode n);       // Trigger / Show 上出现 reverse-on
}
```
主轴判定：`reveal="y"` → 检查 `height` / `size` / `height.*`；子节点 `anchor` 含 `stretch` 于该轴（`stretch` / `stretch-*` / `*-stretch` 按轴解析，照 `MarginAnchorRules` 里的 anchor 拆解）。

- [ ] **Step 1: 红测试** —— 每个码一正一反；`IRWalker.Walk` 端到端；`ControlAttributeApplier` 对 `reveal + scale` 抛 `ParseException`。
- [ ] **Step 2: RUN(AnimationRulesTests)** 红。
- [ ] **Step 3: 实现**（`IRWalker` `else if (node.Tag == "Animation")` 分支 + `Trigger` / `Show` 的 `CheckReverseOnTag`）。
- [ ] **Step 4: 绿**；`RUN(DocumentLinterTests)` 回归；`cd .lint && dotnet build UIXmlLint`。
- [ ] **Step 5: lint**

---

# M4 —— `checked` / `unchecked` 与 `@id` 作用域

## Task 11: `ResolveId` 词法三级查找（spec §4.3 后半）

**Files:**
- Modify: `Runtime/Controls/Internal/TriggerSourceResolver.cs`、`Runtime/Application/Screen.cs`、（`Control.Parent` 已在 Task 3）
- Test: `Tests/EditMode/Controls/TriggerIdScopeTests.cs`

**Interfaces：**
```csharp
// Screen
internal bool TryGet(string id, out IControl c);   // 只查 _byId 顶层
// TriggerSourceResolver
internal static IControl ResolveId(Trigger trigger, string id, string onLabel);
//   1) trigger.ScopedIds  2) for (var p = trigger.Parent; p != null; p = p.Parent) p.ScopedIds  3) UI.OwnerScreenOf(trigger)?.TryGet
//   全失败 → InvalidOperationException($"<Trigger on=\"{onLabel}@{id}\"> …: id '{id}' not found in the trigger's subtree, its enclosing template instance, or screen '{name}'")
// FindPointerSource / FindStateSource / FindTabMenu 的 @id 分支改调 ResolveId（类型校验保留）
// FindBtn 的 @id 分支：先 CollectBtns（子树，保持既有优先级），空则 ResolveId + `as Btn` 校验
```

- [ ] **Step 1: 红测试** —— `TriggerIdScopeTests`：
  - `<VStack><Toggle id='hdr'/><Trigger id='t' on='state-selected@hdr'><Frame/></Trigger></VStack>` 兄弟命中（今天抛 "subtree scope"）。
  - 模板 `<Template name='Row'><VStack><Btn id='b'/><Trigger id='t' on='click@b'><Frame/></Trigger></VStack></Template>` 用两次：各自的 `t` 指向各自的 `b`（点 A 的 `b` 只触发 A 的 `t`）。
  - Screen 顶层 id：`<Btn id='g'/>` 与深层 `<Trigger on='click@g'>`。
  - 最近优先：子树内 `b` 与 Screen 顶层同名 `b`，取子树内的。
  - 三级都没有 ⇒ 消息含 "subtree, its enclosing template instance, or screen"。
  - 既有子树引用（`TriggerTests` / `ShowTests` / `TabMenuTriggerTests`）零回归。
- [ ] **Step 2: RUN(TriggerIdScopeTests)** 红。
- [ ] **Step 3: 实现**。
- [ ] **Step 4: 绿**；`RUN(TriggerTests)`、`RUN(ShowTests)`、`RUN(TabMenuTriggerTests)`、`RUN(AnimationTests)` 回归。
- [ ] **Step 5: lint**

---

## Task 12: `checked` / `unchecked`：`TriggerKind` + `IToggleSource` + `Show` + Animation 首帧（spec §4.3 / §4.4 / §4.5）

**Files:**
- Create: `Runtime/Controls/Internal/IToggleSource.cs`
- Modify: `Runtime/Controls/Internal/TriggerSpec.cs`、`TriggerSourceResolver.cs`、`Runtime/Controls/Trigger.cs`、`Show.cs`、`Animation.cs`、`Toggle.cs`、`Tab.cs`
- Test: `Tests/EditMode/Controls/CheckedTriggerTests.cs`

**Interfaces：**
```csharp
internal interface IToggleSource {
  bool IsOn { get; }
  Observable<bool> OnValueChanged { get; }
  void RegisterCheckedShow(bool wantOn, System.Action reevaluate);   // 源持有列表；OnValueChanged 与注册时各调一次
}
// TriggerKind 加 Checked, Unchecked；s_prefixedKinds 加 ("checked@", …) ("unchecked@", …)；裸 case 两条；错误目录补
// TriggerSourceResolver.FindToggleSource(trigger, sourceId)：空 → GetComponentInParent 找 Toggle/Tab 的 Control（用 PuiToggle 组件 → UI.ControlOf? 若无此映射，则沿 trigger.Parent 链找 `is IToggleSource`）；@id → ResolveId + 类型校验
// Trigger.SubscribeChecked(kind)：var want = kind == Checked; if (src.IsOn == want) OnTriggerFiredInitial(); _sourceSub = src.OnValueChanged.Where(v => v == want).Subscribe(_ => Fire());
// Animation.OnTriggerFiredInitial()：HasReveal / LowLevel → 直接写终态（Proxy / Cg / reveal box）不建 handle，然后 _fire.OnNext（FND-D10）；Text 族 → 照常 Fire()
// Show：_spec.Kind 为 Checked/Unchecked → _toggleSrc.RegisterCheckedShow(want, () => GameObject.SetActive(_toggleSrc.IsOn == want))
```
`Toggle` / `Tab`：`private List<(bool want, Action act)> _checkedShows;` 在 `_changed.OnNext` 处遍历调用。

- [ ] **Step 1: 红测试** —— `CheckedTriggerTests`：
  - `<Toggle id='t' isOn='true'><Trigger id='k' on='checked'><Frame/></Trigger></Toggle>` 开屏 `OnFire` 计 1；`on='unchecked'` 计 0；`IsOn = false` 后 unchecked 计 1、checked 仍 1。
  - `<Show on='checked'>` / `<Show on='unchecked'>` 兄弟互补：开屏 `activeSelf` 正确；翻转后互换；只写 `checked` 时另一半无块（父下无其它子节点可见性变化）。
  - 悬停不影响：`Tab isOn=true` 上模拟 `PointerEnter`（`ExecuteEvents`）⇒ `state-hover` 块显示、`checked` 块仍显示。
  - `<Show on='checked@hdr'>` 兄弟形式（依赖 Task 11）。
  - `ToggleGroup` 互斥：同 `group` 两个 Toggle，点 B ⇒ A 的 `unchecked` 触发。
  - `<Animation on='checked' reverse-on='unchecked' rotate='0:180'>` 于 `isOn='true'`：开屏后 `_offsetProxy.localEulerAngles.z == 180` 且无活动 handle（`CancelCurrent` 前后无差）。
  - `<Btn><Trigger on='checked'>` ⇒ 运行时错误消息含 "<Toggle>/<Tab>"；`checked@b` 指到 Btn ⇒ "not a toggle source"。
  - `Show` 拒绝 `on='click'` 的既有消息仍列出全部合法值（含 checked）。
- [ ] **Step 2: RUN(CheckedTriggerTests)** 红。
- [ ] **Step 3: 实现**。
- [ ] **Step 4: 绿**；`RUN(ShowTests)`、`RUN(ToggleTests)`、`RUN(TabTests)`、`RUN(BtnStateTests)`、`RUN(TabBarTests)` 回归。
- [ ] **Step 5: lint**

---

## Task 13: lint `PUI-CHECKED-NO-SOURCE`（spec §4.3）

**Files:**
- Modify: `Runtime/Core/Lint/StateTriggerRules.cs`、`Runtime/Core/Lint/IRWalker.cs`
- Test: `Tests/EditMode/Lint/StateTriggerRulesTests.cs`

**Interfaces：**
```csharp
public const string NoToggleSourceCode = "PUI-CHECKED-NO-SOURCE";
public static bool IsToggleSourceTag(string tag) => tag is "Toggle" or "Tab";
public static IEnumerable<LintIssue> CheckCheckedSource(ElementNode n, bool hasToggleAncestor);   // 裸 checked/unchecked on Trigger/Animation/Show
```
`IRWalker.WalkNode` 加 `hasToggleSourceAncestor` 参数（与 `hasStateSourceAncestor` 并列传递；Template 体 / 实例根同样豁免）。

- [ ] **Step 1: 红测试** —— `<Frame><Show on='checked'>` ⇒ 触发；`<Toggle><Show on='checked'>` 不触发；`<Btn><Trigger on='checked'>` 触发（Btn 不是 toggle 源）；`checked@x` 不触发；Template 体内不触发。
- [ ] **Step 2: RUN(StateTriggerRulesTests)** 红。
- [ ] **Step 3: 实现**。
- [ ] **Step 4: 绿**；`RUN(DocumentLinterTests)`；`cd .lint && dotnet build UIXmlLint`。
- [ ] **Step 5: lint**

---

## Task 14: 文档（英文 SKILL；同 PR）

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`、`reference/animations.md`、`reference/states.md`、`.claude/skills/scripting-promptugui-csharp/SKILL.md`、`docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`、`.lint/UIXmlLint/README.md`

- [ ] **SKILL.md**
  - Common attributes 表 `width` / `height` 行：Format 列加 `hug` / `clamp(min, hug, max)`；Notes 指向 "Hug"。
  - "Clamp" 小节之后新增 **"Hug — size to content"**：允许标签表；自由定位 `box = clamp(content, min, max)` + 贴 anchor 边；布局组内 V/H/Grid 的 hug = 默认哨兵、ScrollList / clamp-hug 走 `HugElement`（刚性，对比 `clamp(min, stretch, max)` 可压缩）；`PUI-HUG-TAG` / `PUI-HUG-SCALE` / `PUI-HUG-STRETCH-CHILD`；EditMode 提示 `Canvas.ForceUpdateCanvases()`；三个示例（顶层面板、ScrollList 封顶、Variant）。
  - Common mistakes 表："HStack/VStack 子节点全挤在一起"那行 Fix 列补 `height="hug"`；新增 "`<Frame height="hug">` 报 PUI-HUG-TAG → wrap in a `<VStack>`"。
  - `<Image>` / `<Icon>` / `<RawImage>` 属性表各加 `rotation` / `flip` 两行（网格级、绕中心、顺时针为正、layout 不变）；`PUI-FLIP-TAG` / `PUI-FLIP-VALUE`。
  - uGUI 对照表：`<Animation>` 行补 `RectMask2D`（reveal 时）；`<Image>` 行补 `RotateFlipEffect (BaseMeshEffect, internal)`。
  - Quick reference：`SIZE` 块加 `"hug"`；`BUILT-INS` 附近加 `rotation= / flip=` 一行。
- [ ] **reference/animations.md**：`on=` 表加 `checked` / `unchecked`（+ `@id`）两行；"source resolution" 段改写为词法三级（`@id`：subtree → enclosing template instance → screen；最近优先）；新小节 **Family D — Reveal**（属性表、静止态 = from、裁剪、hug 测量、与 B 组合、错误码）与 **`reverse-on`**（语法、从当前值反向、完整 duration、`manual` + `Reverse()`、与 loop / text 互斥、`PUI-REVERSE-*`）；Patterns 里"Menu rows entering with the popup"改用 `reverse-on="collapse"`；新增"Toggle-driven chevron"与"Expandable details"两个 pattern。
- [ ] **reference/states.md**：新小节 **Persistent `checked` / `unchecked`**：与 `state-selected` 的区别（hover 不打断）、`<Show>` 的独立 claim 家族与互补块、开屏派发（Animation 写终态）、示例。
- [ ] **scripting SKILL**：`Animation.Reverse()` / `OnReverse`；`Image.Rotation` / `Flip` 可补间（LitMotion 绑 `Rotation` 示例一行）；hug / reveal 在 EditMode 测试要 `ForceUpdateCanvases`；`Screen.TryGet` 不公开、略。
- [ ] **master spec §6.2** 末尾加段 **"`hug` / `clamp(min, hug, max)`（贴合内容，FND）"**（照 CLP 段三句 + 链接）。
- [ ] **README 代码表**：`PUI-HUG-TAG` / `PUI-HUG-SCALE`（CLI error + runtime throw）/ `PUI-HUG-STRETCH-CHILD` / `PUI-FLIP-TAG` / `PUI-FLIP-VALUE` / `PUI-REVEAL-SINGLE-CHILD` / `PUI-REVEAL-SIZE-CONFLICT` / `PUI-REVEAL-SCALE`（runtime throw）/ `PUI-REVEAL-CHILD-STRETCH` / `PUI-REVERSE-LOOP` / `PUI-REVERSE-TEXT` / `PUI-REVERSE-ON-TAG` / `PUI-CHECKED-NO-SOURCE`。
- [ ] 若 SKILL 示例落成 fixture，跑 UIXmlLint。

---

## Task 15: 全套件 + 收尾

- [ ] `RUN` 全量：`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])`、`["PromptUGUI.Tests.EditorOnly"]`、`RUNPLAY` 全量（每次 job 前 force refresh，核对 `total`）。
- [ ] `read_console(types=["error"])` 为空；`types=["warning"]` 里没有 "layout rebuild while we are already inside a layout rebuild loop"（`HugElement` / `RevealDriver.ApplyBox` 的守门信号）。
- [ ] `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`；`dotnet build .lint/UIXmlLint`。
- [ ] spec §11 写实施记录：零回归数字、`HugElement` 排除自身的读取方式最终形态、reveal 反向中途的最大帧间跳变实测、`checked` 开屏 snap 是否需要覆盖 Text 族。
- [ ] `git add -A && git commit`（分支上），推 `feat/hug-reveal-flip-checked`，开 PR（正文引用 spec / plan；COL plan 在合并后建分支）。

## Self-Review 注记（写计划时已核）

- `SizeSpec.Axis` 是位置参数构造函数，加字段必须同步 `None` / `Fixed` / `WithNumeric` / `WithBounds` 四处，否则 `clamp(…)` 的 `WithBounds` 会把 `IsHug` 丢掉 —— Task 1 的 `clamp(_, hug, 200)` 用例专门钉它。
- `ClampFitter.Apply` 的 `parent == null` 早退保留：Hug 模式的 High / Center 对齐仍读 `p`；spec §1.5 写的"仅 Fraction 早退"改为"保留"，实施记录里注明。
- `HugElement` 与同 GO 的 `LayoutGroup` 都是 `ILayoutElement`：`LayoutRebuilder` 按 `GetComponents` 顺序调 `CalculateLayoutInput*`，后加的 `HugElement` 在 LayoutGroup 之后，读 `group.preferredHeight` 拿到的是本 pass 的值（`ContentSizeFitter` + `LayoutGroup` 同 GO 的官方组合依赖同一顺序）。若实测顺序不稳，退路是 `HugElement` 在 `CalculateLayoutInput*` 里自己调 `group.CalculateLayoutInput*()` 一次。
- `Trigger.GetNativeSize()` 只在 `Children.Count == 1` 时有值；reveal 要求单子节点（`PUI-REVEAL-SINGLE-CHILD`），两者一致。
- `Animation.OnAfterApply` 的静止态初始化必须在 `base.OnAfterApply()` **前**（`on="open"` 的初始 `Fire()` 在 base 里）。
- `ScreenInstantiator` 把 `ControlAttributeApplier.Apply` 放在子树递归之后（Trigger 注释已说明），`RevealDriver.Measure` 在 `OnAfterApply` 里量子节点是安全的；但 `Screen.ApplyScales` 在其后，`scale` 子节点的 box-preserving 膨胀量不到 —— `PUI-REVEAL-SCALE` 只禁 wrapper 自身，子节点 `scale` 属于开放问题，实施时若碰到记入 §11。
- `FindBtn` 的 `@id` 分支今天是"子树内按 id 收集"，不是 `ScopedIds`；Task 11 保留子树优先再退到 `ResolveId`，既有 `click@id` 语义不变。
- `Toggle._changed` 由 `_toggle.onValueChanged` 驱动（`Toggle.cs:97`），uGUI `isOn` setter 会触发它 —— C# `IsOn =` 与 `ToggleGroup` 顶掉都走同一条路，`checked` 不需要额外钩子；`Tab` 同（`Tab.cs:113`）。
- `XsdGenerator` 的 `Image` / `Icon` 是手写清单（`XsdGenerator.cs:113,142`），`rotation` / `flip` 必须手加；`RawImage` / `Animation` / `Collapsible` 走反射。
- `Screen.ApplyScales` 覆写 `localScale`（`Screen.cs:400-429`）—— 网格级实现不碰 transform，所以 `<Icon scale="0.5" flip="y">` 可以共存。
