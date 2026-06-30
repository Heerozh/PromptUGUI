# Btn/Tab/Toggle 实体按钮按压位移（state-offset）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 `Btn`/`Tab`/`Toggle` 加 `pressedOffset`（+ Tab/Toggle 的 `selectedOffset`），按下/选中时子内容整体瞬移、背景框不动，三控件共享一套机制。

**Architecture:** 复用现有 `IStateSource.OnState` 广播 + installer/reactor 模式。新增纯数据 `StateOffsetSet`、挂在 content-holder 上的 `PressOffsetController`（订阅 `OnState` → 设 holder `anchoredPosition`，瞬移无补间）、以及懒建 holder 并把控件直接子节点扫入的 `StateOffsetInstaller`。三控件各加一个属性 + 一行 `OnAfterApply` 调用 + 一个 `ChildHostTransform` override。

**Tech Stack:** Unity 6 / C# (LangVersion 9.0)、R3 (`Observable`)、uGUI（`RectTransform.anchoredPosition`）。无新增包。

设计依据：`docs~/superpowers/specs/2026-06-24-btn-press-offset-design.md`。

## Global Constraints

- **分支**：全部工作在 `feat/btn-press-offset`（已建）。**绝不提交到 main。**
- **LangVersion 9.0**：不得用 C# 10+ 特性（无 primary constructor、无 collection expression `[]`、无 `[field: SerializeField]`）。`readonly struct` / switch 表达式 / target-typed `new()` 可用。
- **不用 System.Threading / Task**（WebGL）：本特性无异步，N/A。
- **float 解析**：`float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture)`（Mono 安全；**禁用** `AsSpan` 重载——会触发 lint CA1846 → CS1503）。
- **lint**：写完代码从仓库根跑 `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`。**不要** `dotnet format analyzers --severity info`。
- **同 PR 必改 SKILL**：功能性改动要同步 `authoring-promptugui-xml` 的 `reference/states.md` + 主 `SKILL.md`（英文）。见 Task 5。
- **测试只由 controller 经 Unity MCP 跑**（子 agent 到不了 Unity MCP，见 [[sdd-unity-mcp-controller-runs-tests]]；子 agent 只写代码）。
- **Sign 约定**：`pressedOffset="x,y"` 像素，Unity 符号——**负 y = 下**。
- **InternalsVisibleTo** 已对 `PromptUGUI.Tests.EditMode` 开放，测试可见所有 `internal` 类型。

**RUN(ClassName) = controller 跑 EditMode 测试的标准流程：**
1. `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
2. `mcp__UnityMCP__read_console(action="get", types=["error"])` —— 编译错误必须为空才继续
3. `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["ClassName"])` → 轮询 `mcp__UnityMCP__get_test_job(job_id=...)` 直到完成，读 pass/fail。

---

## File Structure

| 文件 | 责任 | 动作 |
|---|---|---|
| `Runtime/Controls/Internal/StateOffsetSet.cs` | per-state 位移数据 + `"x,y"` 解析 | Create |
| `Runtime/Controls/Internal/PressOffsetController.cs` | 订阅 `OnState` → 设 holder 位置（瞬移） | Create |
| `Runtime/Controls/Internal/StateOffsetInstaller.cs` | 懒建 holder + 扫子节点 + 挂 controller | Create |
| `Runtime/Controls/Btn.cs` | `pressedOffset` 接线 | Modify |
| `Runtime/Controls/Tab.cs` | `pressedOffset` + `selectedOffset` 接线 | Modify |
| `Runtime/Controls/Toggle.cs` | `pressedOffset` + `selectedOffset` 接线 | Modify |
| `Editor/XsdGenerator.cs` | Btn 手列属性加 `pressedOffset`（Tab/Toggle 反射自动覆盖） | Modify |
| `Tests/EditMode/Controls/StateOffsetSetTests.cs` | 纯数据/解析测试 | Create |
| `Tests/EditMode/Controls/BtnPressOffsetTests.cs` | Btn 集成测试 | Create |
| `Tests/EditMode/Controls/TabPressOffsetTests.cs` | Tab 集成测试 | Create |
| `Tests/EditMode/Controls/TogglePressOffsetTests.cs` | Toggle 集成测试 | Create |
| `.claude/skills/authoring-promptugui-xml/reference/states.md` | 新增「Press offset」节 | Modify |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` | Btn/Tab/Toggle 属性目录行 | Modify |

---

## Task 1: `StateOffsetSet` 纯数据 + 解析

**Files:**
- Create: `Runtime/Controls/Internal/StateOffsetSet.cs`
- Test: `Tests/EditMode/Controls/StateOffsetSetTests.cs`

**Interfaces:**
- Produces: `internal readonly struct StateOffsetSet`，构造 `new StateOffsetSet(Vector2? pressed, Vector2? selected)`；`bool HasAny`；`Vector2 For(InteractState)`；`static Vector2? Parse(string)`。

- [ ] **Step 1: 写失败测试**

Create `Tests/EditMode/Controls/StateOffsetSetTests.cs`:

```csharp
using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class StateOffsetSetTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void For_MapsPressedAndSelected_OthersZero()
        {
            var set = new StateOffsetSet(new Vector2(0, -4), new Vector2(1, -2));
            Assert.AreEqual(new Vector2(0, -4), set.For(InteractState.Pressed));
            Assert.AreEqual(new Vector2(1, -2), set.For(InteractState.Selected));
            Assert.AreEqual(Vector2.zero, set.For(InteractState.Normal));
            Assert.AreEqual(Vector2.zero, set.For(InteractState.Hover));
            Assert.AreEqual(Vector2.zero, set.For(InteractState.Disabled));
        }

        [Test]
        public void For_UnsetState_ReturnsZero()
        {
            var set = new StateOffsetSet(new Vector2(0, -4), null);
            Assert.AreEqual(Vector2.zero, set.For(InteractState.Selected), "unset selected -> zero");
            Assert.AreEqual(new Vector2(0, -4), set.For(InteractState.Pressed));
        }

        [Test]
        public void HasAny_TrueWhenAnyPresent_FalseWhenDefault()
        {
            Assert.IsFalse(default(StateOffsetSet).HasAny);
            Assert.IsTrue(new StateOffsetSet(new Vector2(0, -1), null).HasAny);
            Assert.IsTrue(new StateOffsetSet(null, new Vector2(0, -1)).HasAny);
        }

        [Test]
        public void Parse_ValidPair_NegativeYIsDown()
        {
            var v = StateOffsetSet.Parse("0,-4");
            Assert.IsTrue(v.HasValue);
            Assert.AreEqual(new Vector2(0f, -4f), v.Value);
        }

        [Test]
        public void Parse_EmptyOrNone_ReturnsNull()
        {
            Assert.IsNull(StateOffsetSet.Parse(""));
            Assert.IsNull(StateOffsetSet.Parse("  "));
            Assert.IsNull(StateOffsetSet.Parse(null));
            Assert.IsNull(StateOffsetSet.Parse("none"));
        }

        [Test]
        public void Parse_BadFormat_Throws()
        {
            Assert.Throws<ArgumentException>(() => StateOffsetSet.Parse("5"));
            Assert.Throws<FormatException>(() => StateOffsetSet.Parse("a,b"));
        }
    }
}
```

- [ ] **Step 2: 跑测试确认红（编译错误 = 类型未定义）**

Run: **RUN(StateOffsetSetTests)**
Expected: `read_console` 报 `StateOffsetSet` 未定义的编译错误（红）。

- [ ] **Step 3: 写实现**

Create `Runtime/Controls/Internal/StateOffsetSet.cs`:

```csharp
using System;
using System.Globalization;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Per-state content offset (pixels) for a clickable control: how far the content-holder shifts
    /// while Pressed / Selected. Unset states — and Normal / Hover / Disabled — resolve to
    /// <see cref="Vector2.zero"/>. Pure data + parsing; mirrors <see cref="StateColorSet"/>.
    /// </summary>
    internal readonly struct StateOffsetSet
    {
        public readonly Vector2? Pressed;
        public readonly Vector2? Selected;

        public StateOffsetSet(Vector2? pressed, Vector2? selected)
        {
            Pressed = pressed;
            Selected = selected;
        }

        public bool HasAny => Pressed.HasValue || Selected.HasValue;

        /// <summary>Offset for a state; unset / Normal / Hover / Disabled → zero.</summary>
        public Vector2 For(InteractState state) => state switch
        {
            InteractState.Pressed => Pressed ?? Vector2.zero,
            InteractState.Selected => Selected ?? Vector2.zero,
            _ => Vector2.zero,
        };

        /// <summary>
        /// Parse an <c>"x,y"</c> pixel offset (Unity sign: negative y = down). <c>null</c> / empty /
        /// whitespace / <c>"none"</c> → <c>null</c> (state has no offset). Mirrors AnimationSpec's
        /// per-endpoint translate parse (kept local — AnimationSpec's is private).
        /// </summary>
        public static Vector2? Parse(string v)
        {
            if (string.IsNullOrWhiteSpace(v) || v == "none") return null;
            var parts = v.Split(',');
            if (parts.Length != 2)
                throw new ArgumentException($"Expected offset 'x,y', got '{v}'");
            return new Vector2(ParseFloat(parts[0]), ParseFloat(parts[1]));
        }

        private static float ParseFloat(string s)
            => float.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
```

- [ ] **Step 4: 跑测试确认绿**

Run: **RUN(StateOffsetSetTests)**
Expected: PASS（6 tests）。

- [ ] **Step 5: 提交**

```bash
git add Runtime/Controls/Internal/StateOffsetSet.cs Tests/EditMode/Controls/StateOffsetSetTests.cs
git commit -m "$(printf 'feat: StateOffsetSet —— per-state 内容位移数据 + x,y 解析\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 2: `PressOffsetController` + `StateOffsetInstaller` + `Btn.pressedOffset`

整个 controller/installer 的纵切片在此通过 Btn 端到端验证。

**Files:**
- Create: `Runtime/Controls/Internal/PressOffsetController.cs`
- Create: `Runtime/Controls/Internal/StateOffsetInstaller.cs`
- Modify: `Runtime/Controls/Btn.cs`（字段 + `ChildHostTransform` override + `PressedOffset` 属性 + `OnAfterApply` 一行）
- Test: `Tests/EditMode/Controls/BtnPressOffsetTests.cs`

**Interfaces:**
- Consumes: `StateOffsetSet`（Task 1）；`IStateSource` / `InteractState`（既有）；`PuiButton`（既有，`SimulateState(int)`）。
- Produces:
  - `internal sealed class PressOffsetController : MonoBehaviour`，`void Configure(StateOffsetSet offsets)`。
  - `internal static class StateOffsetInstaller`，`static RectTransform Install(GameObject go, RectTransform existing, StateOffsetSet offsets)`。
  - `Btn`：XML 属性 `pressedOffset`；`protected internal override Transform ChildHostTransform`。

- [ ] **Step 1: 写失败测试**

Create `Tests/EditMode/Controls/BtnPressOffsetTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class BtnPressOffsetTests
    {
        private const int Normal = 0, Pressed = 2, Selected = 3, Disabled = 4;

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Btn BuildBtn(string attrs, string body = "Hi")
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' {attrs}>{body}</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Btn>("b");
        }

        private static RectTransform Holder(Control c)
            => (RectTransform)c.GameObject.GetComponentInChildren<PressOffsetController>(true).transform;

        [Test]
        public void PressedOffset_ShiftsHolderDown_RevertsOnNormal()
        {
            var btn = BuildBtn("pressedOffset='0,-4'");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            var holder = Holder(btn);

            Assert.AreEqual(Vector2.zero, holder.anchoredPosition, "rest at zero");
            puiBtn.SimulateState(Pressed);
            Assert.AreEqual(new Vector2(0, -4), holder.anchoredPosition, "pressed -> down 4px");
            puiBtn.SimulateState(Normal);
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition, "release -> zero");
        }

        [Test]
        public void NoOffset_CreatesNoHolder_LabelStaysDirectChild()
        {
            var btn = BuildBtn("");
            Assert.IsNull(btn.GameObject.GetComponentInChildren<PressOffsetController>(true),
                "plain Btn must not create an offset holder");
            var label = btn.GameObject.GetComponentInChildren<TMP_Text>(true);
            Assert.AreEqual(btn.GameObject.transform, label.transform.parent,
                "label is a direct child when no offset");
        }

        [Test]
        public void PressedOffset_ReparentsLabelIntoHolder()
        {
            var btn = BuildBtn("pressedOffset='0,-4'");
            var holder = Holder(btn);
            var label = btn.GameObject.GetComponentInChildren<TMP_Text>(true);
            Assert.AreEqual((Transform)holder, label.transform.parent, "label moved under the holder");
        }

        [Test]
        public void Disabled_StaysAtZero()
        {
            var btn = BuildBtn("pressedOffset='0,-4'");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            var holder = Holder(btn);
            puiBtn.SimulateState(Disabled);
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition, "disabled has no offset");
        }

        [Test]
        public void SelectedFolds_NoOffsetForMomentaryBtn()
        {
            // Btn has no isOn; Selected folds to Normal, so SimulateState(Selected) -> zero.
            var btn = BuildBtn("pressedOffset='0,-4'");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            var holder = Holder(btn);
            puiBtn.SimulateState(Selected);
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition);
        }

        [Test]
        public void VariantReSolve_NoDuplicateHolder_ReResolves()
        {
            var btn = BuildBtn("pressedOffset='0,-4' pressedOffset.dark='0,-8'");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            Assert.AreEqual(1, btn.GameObject.GetComponentsInChildren<PressOffsetController>(true).Length,
                "one holder after initial apply");

            UI.Variants.Set("dark", true);   // VariantStore.Changed -> Screen ReSolve -> OnAfterApply re-runs

            Assert.AreEqual(1, btn.GameObject.GetComponentsInChildren<PressOffsetController>(true).Length,
                "Variant ReSolve must not add a second holder");
            var holder = Holder(btn);
            puiBtn.SimulateState(Pressed);
            Assert.AreEqual(new Vector2(0, -8), holder.anchoredPosition, "dark override applies after ReSolve");
        }
    }
}
```

- [ ] **Step 2: 跑测试确认红**

Run: **RUN(BtnPressOffsetTests)**
Expected: 编译错误（`PressOffsetController` / `pressedOffset` 未定义）（红）。

- [ ] **Step 3: 写 `PressOffsetController`**

Create `Runtime/Controls/Internal/PressOffsetController.cs`:

```csharp
using System;
using R3;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Drives a content-holder's <see cref="RectTransform.anchoredPosition"/> from the owning
    /// <see cref="IStateSource"/>'s <see cref="InteractState"/> stream. On each state it snaps the
    /// holder to <c>offsets.For(state)</c> — instant, no tween (physical-button feel, pixel-perfect;
    /// authored offsets are integer pixels). One per control (lives on the holder), unlike the
    /// per-graphic <see cref="StateTintReactor"/>.
    /// </summary>
    internal sealed class PressOffsetController : MonoBehaviour
    {
        private RectTransform _holder;
        private StateOffsetSet _offsets;
        private IStateSource _source;
        private IDisposable _sub;

        /// <summary>
        /// (Re)set the per-state offsets. Safe to call repeatedly (Variant ReSolve). Assigns offsets
        /// BEFORE the first subscription so the synchronous replay of the source's current state sees
        /// them (mirrors <see cref="StateTintReactor.Configure"/>).
        /// </summary>
        public void Configure(StateOffsetSet offsets)
        {
            _offsets = offsets;
            var firstInit = _holder == null;
            EnsureInit();
            // On a re-Configure the subscription does NOT replay; repaint the current state explicitly.
            if (!firstInit && _source != null) OnState(_source.Current);
        }

        private void EnsureInit()
        {
            if (_holder != null) return;
            _holder = (RectTransform)transform;
            // includeInactive: the source control may sit on a hidden (SetActive(false)) page at Open.
            _source = GetComponentInParent<IStateSource>(true);
            if (_source != null)
                _sub = _source.OnState.Subscribe(OnState);   // replays Current synchronously
        }

        private void OnState(InteractState state)
        {
            if (_holder != null)
                _holder.anchoredPosition = _offsets.For(state);
        }

        private void OnDestroy()
        {
            _sub?.Dispose();
            _sub = null;
        }
    }
}
```

- [ ] **Step 4: 写 `StateOffsetInstaller`**

Create `Runtime/Controls/Internal/StateOffsetInstaller.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Lazily installs the press/select content-offset on a state-source control (Btn / Tab / Toggle):
    /// creates a full-stretch content-holder, sweeps the control's direct children into it, and wires a
    /// <see cref="PressOffsetController"/>. Idempotent: re-run on each Variant ReSolve, reusing the holder.
    /// Returns the holder (or null when no offset was ever authored).
    /// </summary>
    internal static class StateOffsetInstaller
    {
        internal static RectTransform Install(GameObject go, RectTransform existing, StateOffsetSet offsets)
        {
            if (!offsets.HasAny && existing == null) return null;   // never authored → no holder

            var holder = existing != null ? existing : CreateHolder(go);
            SweepDirectChildrenInto(go, holder);
            var ctrl = holder.GetComponent<PressOffsetController>()
                       ?? holder.gameObject.AddComponent<PressOffsetController>();
            ctrl.Configure(offsets);
            return holder;
        }

        // Full-stretch RectTransform (transparent to layout — same rect as the control), à la
        // Animation's _offsetProxy. The content lives under it so one anchoredPosition shift moves all.
        private static RectTransform CreateHolder(GameObject go)
        {
            var holderGo = new GameObject("_offsetHolder", typeof(RectTransform));
            var rt = (RectTransform)holderGo.transform;
            rt.SetParent(go.transform, worldPositionStays: false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        // Move every direct child of `go` (except the holder) into the holder, preserving sibling order.
        // Snapshot first: SetParent mutates the child list mid-iteration. On ReSolve the content is
        // already inside the holder → no-op; a child that first appears later (a Variant ReSolve) is
        // swept in then. The bg Image / PuiButton / CanvasGroup are components on `go`, not children.
        private static void SweepDirectChildrenInto(GameObject go, RectTransform holder)
        {
            var parent = go.transform;
            var count = parent.childCount;
            var moved = new List<Transform>(count);
            for (int i = 0; i < count; i++)
            {
                var child = parent.GetChild(i);
                if (child != holder) moved.Add(child);
            }
            foreach (var child in moved)
                child.SetParent(holder, worldPositionStays: false);
        }
    }
}
```

- [ ] **Step 5: 接线 `Btn.cs` —— 字段**

In `Runtime/Controls/Btn.cs`, find the relative-modulate fields (the `_hoverModulate` / `_pressedModulate` / `_disabledModulate` block) and add after `private string _disabledModulate;`:

```csharp
        // Press-offset: content-holder shift while Pressed. _offsetHolder lazily created by
        // StateOffsetInstaller (full-stretch wrapper around the content; bg stays on this GO).
        private Vector2? _pressedOffset;
        private RectTransform _offsetHolder;
```

- [ ] **Step 6: 接线 `Btn.cs` —— `ChildHostTransform` override**

In `Runtime/Controls/Btn.cs`, add immediately after the `OnState` property (the `public Observable<InteractState> OnState => _btn.OnState;` line):

```csharp

        // Children parent into the press-offset holder when it exists (so Add blocks land inside too),
        // else directly onto this GO (identical to the default Control behaviour — no holder, no cost).
        protected internal override Transform ChildHostTransform
            => _offsetHolder != null ? _offsetHolder : RectTransform;
```

- [ ] **Step 7: 接线 `Btn.cs` —— `OnAfterApply` 一行**

In `Runtime/Controls/Btn.cs` `OnAfterApply`, find `_btn.interactable = Interactable;` and add the install line right after it (before the `StateColorSet.ResolveAbsolutes` line):

```csharp
            _btn.interactable = Interactable;
            _offsetHolder = StateOffsetInstaller.Install(GameObject, _offsetHolder, new StateOffsetSet(_pressedOffset, null));
```

- [ ] **Step 8: 接线 `Btn.cs` —— `pressedOffset` 属性**

In `Runtime/Controls/Btn.cs`, find the `DisabledModulate` attribute one-liner (`[UIAttr(IsColor = true), Preserve] public string DisabledModulate { set => _disabledModulate = value; }`) and add after it:

```csharp

        /// <summary>Content offset (pixels, Unity sign: negative y = down) while Pressed. <c>""</c> / <c>none</c> = none.</summary>
        [UIAttr, Preserve] public string PressedOffset { set => _pressedOffset = StateOffsetSet.Parse(value); }
```

- [ ] **Step 9: 跑测试确认绿**

Run: **RUN(BtnPressOffsetTests)**
Expected: PASS（6 tests）。若 `read_console` 有编译错误先修。

- [ ] **Step 10: 回归 BtnStateTests（确保未破坏现有状态视觉）**

Run: **RUN(BtnStateTests)**
Expected: PASS（全部既有 Btn 状态测试不回归）。

- [ ] **Step 11: 提交**

```bash
git add Runtime/Controls/Internal/PressOffsetController.cs Runtime/Controls/Internal/StateOffsetInstaller.cs Runtime/Controls/Btn.cs Tests/EditMode/Controls/BtnPressOffsetTests.cs
git commit -m "$(printf 'feat: Btn.pressedOffset —— 按下子内容瞬移（content-holder + OnState reactor）\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 3: `Tab.pressedOffset` + `Tab.selectedOffset`

**Files:**
- Modify: `Runtime/Controls/Tab.cs`（字段 + `ChildHostTransform` override + 两个属性 + `OnAfterApply` 一行）
- Test: `Tests/EditMode/Controls/TabPressOffsetTests.cs`

**Interfaces:**
- Consumes: `StateOffsetInstaller` / `StateOffsetSet`（Task 1/2）；`PuiToggle.SimulateState(int)`、`Tab.IsOn`（既有）。
- Produces: `Tab` XML 属性 `pressedOffset` + `selectedOffset`；`Tab.ChildHostTransform` override。

- [ ] **Step 1: 写失败测试**

Create `Tests/EditMode/Controls/TabPressOffsetTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TabPressOffsetTests
    {
        private const int Normal = 0, Pressed = 2;

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // Two tabs so we can drive 'a' between Normal (select b) and Selected (select a).
        private static (Tab a, Tab b) TwoTabs(string aAttrs)
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='a' {aAttrs}/><Tab id='b'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            return (s.Get<Tab>("bar/a"), s.Get<Tab>("bar/b"));
        }

        private static RectTransform Holder(Control c)
            => (RectTransform)c.GameObject.GetComponentInChildren<PressOffsetController>(true).transform;

        [Test]
        public void SelectedOffset_HoldsWhileSelected()
        {
            var (a, b) = TwoTabs("selectedOffset='0,-3'");
            var holder = Holder(a);
            b.IsOn = true;   // a -> Normal
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition, "unselected -> zero");
            a.IsOn = true;   // a -> Selected
            Assert.AreEqual(new Vector2(0, -3), holder.anchoredPosition, "selected -> held offset");
        }

        [Test]
        public void Pressed_OverridesSelected_RevertsToSelected()
        {
            var (a, b) = TwoTabs("pressedOffset='0,-1' selectedOffset='0,-3'");
            var pt = a.GameObject.GetComponent<PuiToggle>();
            var holder = Holder(a);
            a.IsOn = true;                          // Selected -> -3
            Assert.AreEqual(new Vector2(0, -3), holder.anchoredPosition);
            pt.SimulateState(Pressed);              // Pressed -> -1
            Assert.AreEqual(new Vector2(0, -1), holder.anchoredPosition);
            pt.SimulateState(Normal);               // transient Normal + isOn -> Selected -> -3
            Assert.AreEqual(new Vector2(0, -3), holder.anchoredPosition);
        }

        [Test]
        public void PressedOffset_ShiftsOnPress()
        {
            var (a, b) = TwoTabs("pressedOffset='0,-2'");
            var pt = a.GameObject.GetComponent<PuiToggle>();
            var holder = Holder(a);
            pt.SimulateState(Pressed);
            Assert.AreEqual(new Vector2(0, -2), holder.anchoredPosition);
            pt.SimulateState(Normal);
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认红**

Run: **RUN(TabPressOffsetTests)**
Expected: 编译错误（`pressedOffset` / `selectedOffset` 在 Tab 上未定义）（红）。

- [ ] **Step 3: 接线 `Tab.cs` —— 字段**

In `Runtime/Controls/Tab.cs`, find the relative-modulate fields (ending with `private string _disabledModulate;`) and add after them:

```csharp
        // Press/select content-offset (see StateOffsetInstaller). _offsetHolder lazily created.
        private Vector2? _pressedOffset;
        private Vector2? _selectedOffset;
        private RectTransform _offsetHolder;
```

- [ ] **Step 4: 接线 `Tab.cs` —— `ChildHostTransform` override**

In `Runtime/Controls/Tab.cs`, add (place it near the other overrides, e.g. just before `internal override void OnAfterApply()`):

```csharp
        protected internal override Transform ChildHostTransform
            => _offsetHolder != null ? _offsetHolder : RectTransform;

```

- [ ] **Step 5: 接线 `Tab.cs` —— `OnAfterApply` 一行**

In `Runtime/Controls/Tab.cs` `OnAfterApply`, find `_toggle.interactable = Interactable;` and add right after it (before the `StateColorSet.ResolveAbsolutes` line):

```csharp
            _toggle.interactable = Interactable;
            _offsetHolder = StateOffsetInstaller.Install(GameObject, _offsetHolder, new StateOffsetSet(_pressedOffset, _selectedOffset));
```

- [ ] **Step 6: 接线 `Tab.cs` —— 两个属性**

In `Runtime/Controls/Tab.cs`, find the `DisabledModulate` one-liner attribute and add after it:

```csharp

        /// <summary>Content offset (px, Unity sign: negative y = down) while Pressed. <c>""</c>/<c>none</c>=none.</summary>
        [UIAttr, Preserve] public string PressedOffset { set => _pressedOffset = StateOffsetSet.Parse(value); }
        /// <summary>Content offset held while Selected (isOn). Composes with pressedOffset (Pressed wins).</summary>
        [UIAttr, Preserve] public string SelectedOffset { set => _selectedOffset = StateOffsetSet.Parse(value); }
```

- [ ] **Step 7: 跑测试确认绿 + 回归**

Run: **RUN(TabPressOffsetTests)** → Expected: PASS（3 tests）。
Run: **RUN(TabStateTests)** → Expected: PASS（无回归）。

- [ ] **Step 8: 提交**

```bash
git add Runtime/Controls/Tab.cs Tests/EditMode/Controls/TabPressOffsetTests.cs
git commit -m "$(printf 'feat: Tab.pressedOffset + selectedOffset —— 选中保持按入\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 4: `Toggle.pressedOffset` + `Toggle.selectedOffset`

**Files:**
- Modify: `Runtime/Controls/Toggle.cs`（字段 + `ChildHostTransform` override + 两个属性 + `OnAfterApply` 一行）
- Test: `Tests/EditMode/Controls/TogglePressOffsetTests.cs`

**Interfaces:**
- Consumes: `StateOffsetInstaller` / `StateOffsetSet`；`PuiToggle.SimulateState(int)`、`Toggle.IsOn`（既有）。
- Produces: `Toggle` XML 属性 `pressedOffset` + `selectedOffset`；`Toggle.ChildHostTransform` override。

- [ ] **Step 1: 写失败测试**

Create `Tests/EditMode/Controls/TogglePressOffsetTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TogglePressOffsetTests
    {
        private const int Normal = 0, Pressed = 2;

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Toggle BuildToggle(string attrs)
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='t' {attrs}>Opt</Toggle>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Toggle>("t");
        }

        private static RectTransform Holder(Control c)
            => (RectTransform)c.GameObject.GetComponentInChildren<PressOffsetController>(true).transform;

        [Test]
        public void SelectedOffset_HoldsWhenOn()
        {
            var tog = BuildToggle("selectedOffset='0,-3'");
            var holder = Holder(tog);
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition, "off -> zero");
            tog.IsOn = true;
            Assert.AreEqual(new Vector2(0, -3), holder.anchoredPosition, "on -> held offset");
            tog.IsOn = false;
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition, "off again -> zero");
        }

        [Test]
        public void AuthoredIsOn_ShowsSelectedOffsetOnFrameOne()
        {
            var tog = BuildToggle("isOn='true' selectedOffset='0,-3'");
            var holder = Holder(tog);
            Assert.AreEqual(new Vector2(0, -3), holder.anchoredPosition,
                "isOn at open -> selected offset established instantly (first-frame)");
        }

        [Test]
        public void PressedOffset_ShiftsOnPress()
        {
            var tog = BuildToggle("pressedOffset='0,-2'");
            var pt = tog.GameObject.GetComponent<PuiToggle>();
            var holder = Holder(tog);
            pt.SimulateState(Pressed);
            Assert.AreEqual(new Vector2(0, -2), holder.anchoredPosition);
            pt.SimulateState(Normal);
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认红**

Run: **RUN(TogglePressOffsetTests)**
Expected: 编译错误（Toggle 上 `pressedOffset`/`selectedOffset` 未定义）（红）。

- [ ] **Step 3: 接线 `Toggle.cs` —— 字段**

In `Runtime/Controls/Toggle.cs`, find the modulate fields (`private string _selectedModulate;` / `private string _disabledModulate;`) and add after `private string _disabledModulate;`:

```csharp
        // Press/select content-offset (see StateOffsetInstaller). _offsetHolder lazily created.
        private Vector2? _pressedOffset;
        private Vector2? _selectedOffset;
        private RectTransform _offsetHolder;
```

- [ ] **Step 4: 接线 `Toggle.cs` —— `ChildHostTransform` override**

In `Runtime/Controls/Toggle.cs`, add (place near the other overrides, e.g. just before `internal override void OnAfterApply()`):

```csharp
        protected internal override Transform ChildHostTransform
            => _offsetHolder != null ? _offsetHolder : RectTransform;

```

- [ ] **Step 5: 接线 `Toggle.cs` —— `OnAfterApply` 一行**

In `Runtime/Controls/Toggle.cs` `OnAfterApply`, find `_toggle.interactable = Interactable;` and add right after it (before the `StateColorSet.ResolveAbsolutes` line):

```csharp
            _toggle.interactable = Interactable;
            _offsetHolder = StateOffsetInstaller.Install(GameObject, _offsetHolder, new StateOffsetSet(_pressedOffset, _selectedOffset));
```

- [ ] **Step 6: 接线 `Toggle.cs` —— 两个属性**

In `Runtime/Controls/Toggle.cs`, find the `DisabledModulate` one-liner attribute and add after it:

```csharp

        /// <summary>Content offset (px, Unity sign: negative y = down) while Pressed. <c>""</c>/<c>none</c>=none.</summary>
        [UIAttr, Preserve] public string PressedOffset { set => _pressedOffset = StateOffsetSet.Parse(value); }
        /// <summary>Content offset held while Selected (isOn). Composes with pressedOffset (Pressed wins).</summary>
        [UIAttr, Preserve] public string SelectedOffset { set => _selectedOffset = StateOffsetSet.Parse(value); }
```

- [ ] **Step 7: 跑测试确认绿 + 回归**

Run: **RUN(TogglePressOffsetTests)** → Expected: PASS（3 tests）。
Run: **RUN(ToggleStateTests)** → Expected: PASS（无回归）。

- [ ] **Step 8: 提交**

```bash
git add Runtime/Controls/Toggle.cs Tests/EditMode/Controls/TogglePressOffsetTests.cs
git commit -m "$(printf 'feat: Toggle.pressedOffset + selectedOffset\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 5: 文档 + XSD + 全量回归

**Files:**
- Modify: `Editor/XsdGenerator.cs`（Btn 手列属性加 `pressedOffset`）
- Modify: `.claude/skills/authoring-promptugui-xml/reference/states.md`
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`

> Tab/Toggle 在 XSD 里是反射派生（`ReflectControlAttrs`），`pressedOffset`/`selectedOffset` 作为 `[UIAttr]` 自动覆盖——**无需**手改。只有 Btn 是手列。

- [ ] **Step 1: XSD —— Btn 手列加 `pressedOffset`**

In `Editor/XsdGenerator.cs`, find the Btn block's `("pressedSprite", "xs:string", (string)null),` line and add after it:

```csharp
                    ("pressedSprite", "xs:string", (string)null),
                    // Press-offset content shift ([UIAttr] Btn.PressedOffset). Tab/Toggle get theirs by reflection.
                    ("pressedOffset", "xs:string", (string)null),
```

- [ ] **Step 2: 编译确认无错**

Run: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` then `mcp__UnityMCP__read_console(action="get", types=["error"])`
Expected: 无编译错误。

- [ ] **Step 3: 回归 XSD 生成器测试**

Run: **RUN(XsdGeneratorTests)**（若类名不同，先 `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["XsdGeneratorTests"])`；substring 断言为加性，不应回归）
Expected: PASS。

- [ ] **Step 4: 文档 —— `reference/states.md` 新增 Press offset 节**

In `.claude/skills/authoring-promptugui-xml/reference/states.md`, append this section at the end of the file:

````markdown
## 4. Press offset — `pressedOffset` / `selectedOffset`

A **tactile / physical-button** effect: while Pressed, the control's child content (label, icons, nested content) shifts by a fixed pixel offset — the background frame (the control's own bg `Image`) stays put — giving a "depressed into the button" feel. Shared by `<Btn>` / `<Tab>` / `<Toggle>`.

| Attribute | Controls | Meaning |
| --- | --- | --- |
| `pressedOffset="x,y"` | Btn / Tab / Toggle | Content offset while **Pressed**. |
| `selectedOffset="x,y"` | Tab / Toggle | Content offset held while **Selected** (`isOn`). `<Btn>` has no selected state and does not have this attribute. |

```xml
<Btn pressedOffset="0,-4">Buy</Btn>                    <!-- press: content sinks 4px -->
<Tab pressedOffset="0,-2" selectedOffset="0,-3"/>      <!-- press sinks 2px; stays sunk 3px while selected -->
```

- **Sign is Unity's: negative `y` = down**, positive `x` = right (same convention as `<Animation translate>`). So "sink 4px" is `0,-4`, not `0,4`. This is the most common foot-gun.
- **Instant, never tweened.** The offset snaps on state enter and snaps back on release (physical-button semantics + pixel-art-friendly; authored offsets are integer pixels). This deliberately differs from `*Color`'s ~0.1s fade.
- `""` / `none` = no offset for that state.
- **First-frame:** a `<Tab>` / `<Toggle>` authored `isOn` shows its `selectedOffset` on frame 1 (no animate-in).
- **Selected being pressed:** pressing an already-selected `<Tab>` / `<Toggle>` shows `pressedOffset` while held, then reverts to `selectedOffset` on release. If you set `selectedOffset` but **not** `pressedOffset`, pressing a selected control momentarily pops the content back to zero — set both (often the same value) to avoid the pop.
- **Composition:** independent of `<Animation translate>` (which moves its own proxy — the two stack), of `*Color` / `*Modulate` / `pressedSprite` (different channels), and of `tint`. All compose.
- `stateReact="false"` does **not** exempt a child from the offset (it only governs `*Modulate` colour fan-out). The holder is a rigid translate — all content moves together.
- Variant-overridable like any `[UIAttr]` (`pressedOffset.dark="0,-8"`).
- **Disabled** has no offset (content rests at zero).

Implementation: the control lazily wraps its content in a full-stretch holder (only when an offset is authored) and a `PressOffsetController` drives that holder's `anchoredPosition` from the control's `OnState` stream — same broadcaster the `*Color` family uses.
````

- [ ] **Step 5: 文档 —— 主 `SKILL.md` Btn 行**

In `.claude/skills/authoring-promptugui-xml/SKILL.md`, find the Btn `disabledSprite` table row (line containing `` | `disabledSprite` | sprite key | — | ``) and add a new row right after it:

```markdown
| `pressedOffset` | `x,y` px | — | 按下时子内容整体位移（content-holder 平移；**Unity 符号 负 y=下**）；瞬移不补间；与 `<Animation>`/`*Color`/`*Sprite` 叠加；`""`/`none`=不动；见 states.md |
```

- [ ] **Step 6: 文档 —— 主 `SKILL.md` Toggle + Tab 行**

In `.claude/skills/authoring-promptugui-xml/SKILL.md`, find the Toggle `hoverModulate · pressedModulate · selectedModulate · disabledModulate` row (the one ending `子节点 stateReact="false" 退出；见 states.md`) and add after it:

```markdown
| `pressedOffset` · `selectedOffset` | `x,y` px | — | 子内容位移（content-holder 平移；**负 y=下**）；瞬移；`selectedOffset`=选中(`isOn`)保持按入；Pressed 优先；`""`/`none`=不动；见 states.md |
```

Then find the **Tab** `hoverModulate · pressedModulate · selectedModulate · disabledModulate` row (under the `<Tab>` section, line ~342) and add the same row after it:

```markdown
| `pressedOffset` · `selectedOffset` | `x,y` px | — | 子内容位移（content-holder 平移；**负 y=下**）；瞬移；`selectedOffset`=选中(`isOn`)保持按入；Pressed 优先；`""`/`none`=不动；见 states.md |
```

- [ ] **Step 7: lint**

Run (from repo root):
```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: 无 diff（若首次需先 `dotnet restore PromptUGUI.Lint.slnx`）。

- [ ] **Step 8: 全量 EditMode 回归**

Run: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` → 轮询 `get_test_job`。
Expected: 全绿（含新 4 个测试类 + 所有既有）。

- [ ] **Step 9: 提交**

```bash
git add Editor/XsdGenerator.cs .claude/skills/authoring-promptugui-xml/reference/states.md .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "$(printf 'docs: state-offset —— states.md / SKILL.md / XSD（Btn pressedOffset）\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## 完成标准

- `pressedOffset`（Btn/Tab/Toggle）+ `selectedOffset`（Tab/Toggle）按 spec 工作：按下/选中瞬移子内容、背景不动、复位干净、ReSolve 幂等、无位移时零额外 GO。
- 4 个新测试类全绿，既有 Btn/Tab/Toggle 状态测试无回归，XSD 测试无回归。
- lint 干净。
- states.md + SKILL.md（Btn/Tab/Toggle 行）+ XSD 同步。
- 全程在 `feat/btn-press-offset`，未碰 main。
