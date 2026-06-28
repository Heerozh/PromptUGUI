# 手柄/键盘导航 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 PromptUGUI 加上游戏手柄 / 键盘导航：焦点可见（复用 hover）、主机风移动手指光标、进屏自动聚焦 + 模态 trap + 关闭还原、显式导航覆盖（`nav*`）。

**Architecture:** 不造导航引擎，靠 uGUI EventSystem 的内置导航（控件已全是 `Selectable`）。库补四件事 + 一个中枢「导航模式」信号（Pointer↔Directional，按上次输入设备判定）。焦点态从被折叠成 `Normal` 解封成新的 `InteractState.Focused`（仅 Directional 模式生效，避免鼠标点完粘高亮），视觉复用 hover。光标是一段作者写的 XML（`<FocusCursor>`），复用 Tutorial 的定位三段式 + PixelSnap + LitMotion + InstantiateNode 动态子树。

**Tech Stack:** Unity 6 uGUI、新 Input System（`#if ENABLE_INPUT_SYSTEM` 门控）、R3、LitMotion、Unity `Awaitable`（**禁用 .NET Threading / Task / TCS**）。

## Global Constraints

- **语言**：所有面向作者/调用方的新文档（SKILL）用**英文**；spec/plan/代码注释中文 OK。
- **不提交 main**：本计划所有提交在 `feat/gamepad-keyboard-navigation` 分支（Task 0 建）。
- **WebGL 安全**：异步只用 `Awaitable`，TCS 用 `AwaitableCompletionSource`，禁 `System.Threading.Task`。
- **输入仅新系统**：所有导航代码包在 `#if ENABLE_INPUT_SYSTEM`；`#else` → no-op + 一次性 `Debug.LogWarning`。`Unity.InputSystem` 已在 `Runtime/PromptUGUI.Runtime.asmdef` 的 references 里（**无需改 asmdef**）。
- **严格 additive**：未启用 `UI.UseGamepadNavigation()` 时，所有新行为静默关闭，既有行为逐字不变（Pointer 模式下 `Focused` 折回 `Normal` = 今天）。
- **TDD**：每个改动先写红测、跑红、再实现、跑绿、提交。EditMode 测试 SetUp/TearDown 必须 `UI.ResetForTests()`。
- **测试协议（Testing Protocol，下文 RUN-TESTS 指此）**：改完源码后，依次：
  1. `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
  2. `mcp__UnityMCP__read_console(action="get", types=["error"])` —— 确认零编译错误再继续
  3. `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["<ClassName>"])` → 拿到 `job_id`
  4. 轮询 `mcp__UnityMCP__get_test_job(job_id=...)` 直到完成，读 pass/fail
  - PlayMode 同理换 `mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"]`。MCP 不可用时按 CLAUDE.md 重连或让用户重启。
- **Lint（LINT-CS 指此）**：`cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`（首次先 `dotnet restore PromptUGUI.Lint.slnx`）。**绝不**用 `dotnet format analyzers --severity info`（CLAUDE.md 列了会炸的自动修复）。
- **Lint（LINT-XML 指此）**：`dotnet run --project .lint/UIXmlLint -- <path>`。
- **提交信息**结尾加：`Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`。
- **InternalsVisibleTo**：`PromptUGUI.Tests.EditMode` / `.PlayMode` 已能访问 `internal`（含 `PuiButton`/`StateBroadcaster`/`UI.Navigation` 内部成员）。

---

## File Structure

**新建：**
- `Runtime/Application/UI.Navigation.cs` — `partial class UI` 内的 `public static class Navigation`（Mode 信号、`Enable`/`IsEnabled`、设备检测驱动、选区限制、默认光标缓存）+ `public static void UseGamepadNavigation()`。
- `Runtime/Application/Navigation/NavigationController.cs` — `MonoBehaviour`，挂在 EventSystem GO 上：每帧更新 Mode、mode-flip 重 poke 选中控件、模态选区限制守卫。`#if ENABLE_INPUT_SYSTEM`。
- `Runtime/Application/Navigation/FocusCursorView.cs` — `UIBehaviour`，挂光标 overlay 根：定位（复用 `TutorialOverlayView.WorldRectToLocal`）+ PixelSnap + LitMotion 滑动 + 按 Mode/选区显隐。
- `Runtime/Application/Navigation/ExplicitNavigationResolver.cs` — `internal static`：把 `nav*` 原始属性解析成 uGUI `Navigation`。
- `Runtime/Resources/PromptUGUI/Navigation/FocusCursor.ui.xml` — 内置默认光标文档（+ `.meta`）。
- `Runtime/Core/Lint/NavTargetRules.cs` — CLI lint `PUI-NAV-UNKNOWN-TARGET` / `PUI-NAV-ON-NON-SELECTABLE`。
- 测试：`Tests/EditMode/Navigation/*Tests.cs`、`Tests/PlayMode/Navigation/*PlayTests.cs`、`Tests/EditMode/Lint/NavTargetRulesTests.cs`。

**修改：**
- `Runtime/Controls/InteractState.cs` — 加 `Focused`。
- `Runtime/Controls/Internal/StateBroadcaster.cs` — `MapTransient` 3→Focused；`Recompute` 加 Mode 门控。
- `Runtime/Controls/Internal/IStateSource.cs` — 加 `void RefreshState();`。
- `Runtime/Controls/Internal/PuiButton.cs` / `PuiToggle.cs` — 实现 `RefreshState`。
- `Runtime/Controls/Internal/StateTintReactor.cs` — `OnState` 把 `Focused` 归一到 `Hover`。
- `Runtime/Application/UI.cs` — `ResetForTests` 加 `Navigation.ResetForTestsInternal()`。
- `Runtime/Core/IR/ScreenDef.cs` — 加 `FocusCursor` 属性。
- `Runtime/Core/Parser/UIDocumentParser.cs` — `ParseScreen` 抽出 `<FocusCursor>` 子节点。
- `Runtime/Application/Screen.cs` — `Open`/`ReSolve` 末尾：初始焦点、`ExplicitNavigationResolver`、光标搭建；加 `Focus(idPath)`。
- `Runtime/Application/UI.Modal.cs` — `Slot` 加 `PrevSelected`；`MaterializePump` 记录+设初始焦点；`RemoveSlot` 还原。
- `.claude/skills/authoring-promptugui-xml/SKILL.md`（+ 新 `reference/navigation.md`）、`.claude/skills/scripting-promptugui-csharp/SKILL.md`。

---

## Task 0: 建分支 + 落 spec/plan

**Files:** 无源码改动。

- [ ] **Step 1: 建分支并确认 spec/plan 在工作树**

```bash
cd /workspace-PromptUGUI
git checkout -b feat/gamepad-keyboard-navigation
git status --short    # 应看到两个未跟踪文件：specs/2026-06-29-...-design.md, plans/2026-06-29-...md
```

- [ ] **Step 2: 提交 spec + plan**

```bash
git add "docs~/superpowers/specs/2026-06-29-gamepad-keyboard-navigation-design.md" \
        "docs~/superpowers/plans/2026-06-29-gamepad-keyboard-navigation.md"
git commit -m "docs: gamepad/keyboard navigation spec + plan

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 1: 导航模式信号 + 解封 `InteractState.Focused`

**Files:**
- Create: `Runtime/Application/UI.Navigation.cs`
- Modify: `Runtime/Controls/InteractState.cs:14-21`
- Modify: `Runtime/Controls/Internal/StateBroadcaster.cs:54-78`（Recompute + MapTransient）
- Modify: `Runtime/Controls/Internal/IStateSource.cs:12-18`
- Modify: `Runtime/Controls/Internal/PuiButton.cs:20`、`PuiToggle.cs:20`（加 RefreshState）
- Modify: `Runtime/Application/UI.cs`（ResetForTests 加一行）
- Modify: 既有断言 `MapTransient(3)==Normal` 的测试（grep 定位）+ 任何实现 `IStateSource` 的测试 fake（如 `Tests/PlayMode/Controls/StateTintReactorPlayTests.cs` 的 `FakeSource`）
- Test: `Tests/EditMode/Navigation/FocusStateTests.cs`

**Interfaces:**
- Produces:
  - `enum UI.Navigation.NavMode { Pointer, Directional }`；`internal static NavMode UI.Navigation.Mode { get; set; }`（默认 Pointer）；`internal static bool UI.Navigation.IsDirectional`；`internal static void UI.Navigation.ResetForTestsInternal()`。
  - `InteractState.Focused`（枚举末尾，序数 5）。
  - `StateBroadcaster.MapTransient(3) => InteractState.Focused`。
  - `IStateSource.RefreshState()`。
- Consumes: `PuiButton.SimulateState(int)`（既有测试钩子）。

- [ ] **Step 1: 写红测 `FocusStateTests`**

```csharp
// Tests/EditMode/Navigation/FocusStateTests.cs
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class FocusStateTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PuiButton BuildBtn()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Btn id='b'>Hi</Btn></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            return screen.Get<Btn>("b").GameObject.GetComponent<PuiButton>();
        }

        [Test]
        public void MapTransient_NavigationSelected_IsFocused()
            => Assert.AreEqual(InteractState.Focused, StateBroadcaster.MapTransient(3));

        [Test]
        public void Focus_FoldsToNormal_InPointerMode()
        {
            var pui = BuildBtn();
            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            pui.SimulateState(3);                       // uGUI navigation-Selected
            Assert.AreEqual(InteractState.Normal, pui.Current);
        }

        [Test]
        public void Focus_IsVisible_InDirectionalMode()
        {
            var pui = BuildBtn();
            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            pui.SimulateState(3);
            Assert.AreEqual(InteractState.Focused, pui.Current);
        }

        [Test]
        public void RefreshState_RepaintsOnModeFlip()
        {
            var pui = BuildBtn();
            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            pui.SimulateState(3);
            Assert.AreEqual(InteractState.Normal, pui.Current);   // pointer: invisible
            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            pui.RefreshState();                                    // re-poke, no uGUI state change
            Assert.AreEqual(InteractState.Focused, pui.Current);
        }
    }
}
```

- [ ] **Step 2: 跑红** — RUN-TESTS（EditMode，group `FocusStateTests`）。预期：编译失败（`UI.Navigation` / `InteractState.Focused` / `RefreshState` 不存在）。

- [ ] **Step 3: 加 `InteractState.Focused`**

`Runtime/Controls/InteractState.cs`，枚举改为：
```csharp
    public enum InteractState
    {
        Normal,
        Hover,
        Pressed,
        Selected,
        Disabled,
        Focused,   // keyboard/gamepad navigation focus — visible only in Directional mode (spec §4)
    }
```

- [ ] **Step 4: 建 `UI.Navigation.cs`（本任务只放 Mode 信号；Enable/光标等后续任务补）**

```csharp
// Runtime/Application/UI.Navigation.cs
namespace PromptUGUI.Application
{
    public static partial class UI
    {
        public static partial class Navigation
        {
            public enum NavMode { Pointer, Directional }

            /// <summary>Pointer ↔ Directional 中枢信号（spec §3）。仅在 Enable 后由 NavigationController 驱动；
            /// 内部 settable 供控制器与 EditMode 测试设定。</summary>
            internal static NavMode Mode { get; set; } = NavMode.Pointer;
            internal static bool IsDirectional => Mode == NavMode.Directional;

            internal static void ResetForTestsInternal()
            {
                Mode = NavMode.Pointer;
            }
        }
    }
}
```
（`partial class Navigation`：后续任务在别处给同一嵌套类加成员；C# 允许嵌套类 partial。本文件与后续 `UI.Navigation` 成员都用 `public static partial class Navigation`。）

- [ ] **Step 5: `MapTransient` 3→Focused + `Recompute` 门控**

`StateBroadcaster.cs`：`MapTransient` 第 75 行 `3 => InteractState.Normal,` 改为：
```csharp
            3 => InteractState.Focused,
```
`Recompute()`（54-62 行）改为：
```csharp
        private void Recompute()
        {
            // Focus is visible only in Directional input mode; in Pointer mode it folds to Normal so a
            // mouse click doesn't leave the control stuck-highlighted (spec §3).
            var t = _transient;
            if (t == InteractState.Focused && !PromptUGUI.Application.UI.Navigation.IsDirectional)
                t = InteractState.Normal;
            var composite = t == InteractState.Normal
                ? (_isOn ? InteractState.Selected : InteractState.Normal)
                : t;
            _state.Value = composite;
            for (int i = 0; i < _showReevaluators.Count; i++)
                _showReevaluators[i].Invoke();
        }
```

- [ ] **Step 6: `IStateSource.RefreshState` + Pui* 实现**

`IStateSource.cs` 接口加一行（17 行后）：
```csharp
        /// <summary>Re-evaluate the transient state from the live Selectable SelectionState (used when
        /// the navigation Mode flips while this control stays selected — spec §3).</summary>
        public void RefreshState();
```
`PuiButton.cs` 第 20 行后、`PuiToggle.cs` 第 20 行后各加：
```csharp
        public void RefreshState() => _broadcaster.SetTransient(StateBroadcaster.MapTransient((int)currentSelectionState));
```

- [ ] **Step 7: `UI.ResetForTests` 接线 + 修既有断言**

`Runtime/Application/UI.cs` 的 `ResetForTests()` 体内（紧挨 `Tutorial.ResetForTestsInternal();` 后）加：
```csharp
        Navigation.ResetForTestsInternal();
```
然后 grep 既有断言并改：
```bash
grep -rn "MapTransient" Tests/   # 把断言 MapTransient(3)==Normal 的改成 ==Focused
grep -rln ": IStateSource" Tests/  # 每个实现 IStateSource 的 fake 加 RefreshState（空实现即可）
```
对每个 fake（如 `StateTintReactorPlayTests.FakeSource`）加：
```csharp
        public void RefreshState() { }
```

- [ ] **Step 8: 跑绿** — RUN-TESTS（EditMode，group `FocusStateTests`）；再跑全 EditMode（`assembly_names=["PromptUGUI.Tests.EditMode"]` 不带 group）确认没打破既有状态测试；`read_console types=["error"]` 零错误。预期：全绿。

- [ ] **Step 9: LINT-CS + 提交**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI && git add Runtime/Controls/InteractState.cs Runtime/Controls/Internal/StateBroadcaster.cs Runtime/Controls/Internal/IStateSource.cs Runtime/Controls/Internal/PuiButton.cs Runtime/Controls/Internal/PuiToggle.cs Runtime/Application/UI.Navigation.cs Runtime/Application/UI.cs Tests/
git commit -m "feat(nav): unfold InteractState.Focused gated by navigation Mode signal

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: 焦点视觉复用 hover（`StateTintReactor`）

**Files:**
- Modify: `Runtime/Controls/Internal/StateTintReactor.cs:128-131`
- Test: `Tests/EditMode/Navigation/FocusTintTests.cs`

**Interfaces:**
- Consumes: `InteractState.Focused`、`StateTintReactor`、`UI.Navigation.Mode`（Task 1）。
- Produces: 一个 Directional-focused 控件的 tint 等于它 hover 的 tint；无 hover 时不变色。

- [ ] **Step 1: 写红测 `FocusTintTests`**

```csharp
// Tests/EditMode/Navigation/FocusTintTests.cs
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class FocusTintTests
    {
        [SetUp] public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = true; }
        [TearDown] public void TearDown() { StateTintReactor.TestForceInstant = false; UI.ResetForTests(); }

        private static (Btn btn, Image bg) Build(string attrs)
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Btn id='b' {attrs}>Hi</Btn></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            return (btn, btn.GameObject.GetComponent<Image>());
        }

        [Test]
        public void DirectionalFocus_ReusesHoverColor()
        {
            var (btn, bg) = Build("hoverColor='#ff0000'");
            var pui = btn.GameObject.GetComponent<PuiButton>();
            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            pui.SimulateState(3);                       // focus
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), bg.color);   // == hover
        }

        [Test]
        public void Focus_NoHoverSet_LeavesBaseColor()
        {
            var (btn, bg) = Build("");
            var baseColor = bg.color;
            var pui = btn.GameObject.GetComponent<PuiButton>();
            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            pui.SimulateState(3);
            Assert.AreEqual(baseColor, bg.color);        // 手指兜底，控件不变色
        }
    }
}
```

- [ ] **Step 2: 跑红** — RUN-TESTS（group `FocusTintTests`）。预期：`DirectionalFocus_ReusesHoverColor` 失败（焦点态当前不映射到 hover 颜色）。

- [ ] **Step 3: 实现 Focused→Hover 归一**

`StateTintReactor.cs` 的 `OnState`（128 行起），在 `if (_graphic == null) return;` 之后加一行：
```csharp
        private void OnState(InteractState state)
        {
            if (_graphic == null) return;
            // Focus reuses the hover visual (spec §4.3). The composite already folds Focused→Normal in
            // Pointer mode, so this only fires for an actually-directional-focused control.
            if (state == InteractState.Focused) state = InteractState.Hover;
            var target = BaseFor(state).Multiply(MultiplierFor(state));
            // ...（其余不变）
```

- [ ] **Step 4: 跑绿** — RUN-TESTS（group `FocusTintTests`）。预期全绿。

- [ ] **Step 5: LINT-CS + 提交**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI && git add Runtime/Controls/Internal/StateTintReactor.cs Tests/
git commit -m "feat(nav): focus reuses hover tint in StateTintReactor

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: `UI.UseGamepadNavigation` + EventSystem + 设备检测控制器

**Files:**
- Modify: `Runtime/Application/UI.Navigation.cs`（加 `Enable`/`IsEnabled`/`Controller`/`UseGamepadNavigation` + 设备检测的内部钩子）
- Create: `Runtime/Application/Navigation/NavigationController.cs`
- Test: `Tests/EditMode/Navigation/NavEnableTests.cs`、`Tests/PlayMode/Navigation/NavModePlayTests.cs`

**Interfaces:**
- Produces:
  - `public static void UI.UseGamepadNavigation()`；`public static void UI.Navigation.Enable()`；`public static bool UI.Navigation.IsEnabled`。
  - `internal static NavigationController UI.Navigation.Controller`（单例，挂在 EventSystem GO 上）。
  - `internal static void UI.Navigation.NotePointerInput()` / `NoteDirectionalInput()`（控制器调用，翻 Mode 并在翻转时重 poke 选中控件）。
- Consumes: `UI.Navigation.Mode`、`IStateSource.RefreshState`（Task 1）、`EventSystem`、`InputSystemUIInputModule`。

- [ ] **Step 1: 写红测 `NavEnableTests`（EditMode，不依赖真实输入）**

```csharp
// Tests/EditMode/Navigation/NavEnableTests.cs
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class NavEnableTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Enable_IsIdempotent_AndCreatesEventSystem()
        {
            Assert.IsFalse(UI.Navigation.IsEnabled);
            UI.UseGamepadNavigation();
            UI.UseGamepadNavigation();                 // 第二次 no-op
            Assert.IsTrue(UI.Navigation.IsEnabled);
            Assert.IsNotNull(Object.FindAnyObjectByType<EventSystem>());
        }

        [Test]
        public void NoteInput_FlipsMode()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();
            Assert.AreEqual(UI.Navigation.NavMode.Directional, UI.Navigation.Mode);
            UI.Navigation.NotePointerInput();
            Assert.AreEqual(UI.Navigation.NavMode.Pointer, UI.Navigation.Mode);
        }
    }
}
```
（`using Object = UnityEngine.Object;` 视需要加。）

- [ ] **Step 2: 跑红** — RUN-TESTS（group `NavEnableTests`）。预期：编译失败（API 不存在）。

- [ ] **Step 3: 扩 `UI.Navigation` —— Enable / IsEnabled / Note* / 默认光标缓存占位**

在 `UI.Navigation.cs` 的 `Navigation` 类内追加（与 Task 1 的 Mode 同类）：
```csharp
            internal static bool IsEnabled { get; private set; }
            internal static NavigationController Controller { get; private set; }

            public static void Enable()
            {
#if ENABLE_INPUT_SYSTEM
                if (IsEnabled) return;
                IsEnabled = true;
                var es = UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
                if (es == null)
                {
                    var go = new UnityEngine.GameObject("EventSystem",
                        typeof(UnityEngine.EventSystems.EventSystem),
                        typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
                    es = go.GetComponent<UnityEngine.EventSystems.EventSystem>();
                }
                else if (es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null
                         && es.GetComponent<UnityEngine.EventSystems.BaseInputModule>() == null)
                {
                    es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                }
                Controller = es.gameObject.GetComponent<NavigationController>()
                             ?? es.gameObject.AddComponent<NavigationController>();
#else
                if (_warnedNoInputSystem) return;
                _warnedNoInputSystem = true;
                UnityEngine.Debug.LogWarning("[PromptUGUI] UI.UseGamepadNavigation() requires the New Input System package; gamepad/keyboard navigation is disabled.");
#endif
            }

#if !ENABLE_INPUT_SYSTEM
            private static bool _warnedNoInputSystem;
#endif

            /// <summary>控制器调用：上次输入来自指针 → 翻 Pointer，并在翻转时重绘选中控件焦点态。</summary>
            internal static void NotePointerInput() => SetMode(NavMode.Pointer);
            internal static void NoteDirectionalInput() => SetMode(NavMode.Directional);

            private static void SetMode(NavMode m)
            {
                if (Mode == m) return;
                Mode = m;
                RepokeSelected();
            }

            private static void RepokeSelected()
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                var go = es != null ? es.currentSelectedGameObject : null;
                if (go == null) return;
                var src = go.GetComponent<PromptUGUI.Controls.Internal.IStateSource>();
                src?.RefreshState();
            }
```
并把 `ResetForTestsInternal()` 扩为：
```csharp
            internal static void ResetForTestsInternal()
            {
                Mode = NavMode.Pointer;
                if (Controller != null) UnityEngine.Object.DestroyImmediate(Controller);
                Controller = null;
                IsEnabled = false;
#if !ENABLE_INPUT_SYSTEM
                _warnedNoInputSystem = false;
#endif
            }
```
`UseGamepadNavigation` 便捷别名（放在 `UI` 本体，`Navigation` 类外、`UI` 类内）：
```csharp
        public static void UseGamepadNavigation() => Navigation.Enable();
```

- [ ] **Step 4: 建 `NavigationController`（设备检测 + 每帧驱动）**

```csharp
// Runtime/Application/Navigation/NavigationController.cs
#if ENABLE_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;

namespace PromptUGUI.Application.Navigation
{
    /// <summary>挂在 EventSystem GO 上：每帧按"上次输入设备"翻 UI.Navigation.Mode（spec §3）。
    /// 鼠标移动/点击/触屏 → Pointer；手柄摇杆/方向键/按钮、键盘导航键 → Directional。</summary>
    internal sealed class NavigationController : MonoBehaviour
    {
        private const float MouseMoveThreshold = 1f;   // 屏幕像素

        private void Update()
        {
            var gp = Gamepad.current;
            if (gp != null && (gp.leftStick.ReadValue().sqrMagnitude > 0.25f
                               || gp.dpad.ReadValue().sqrMagnitude > 0.25f
                               || gp.buttonSouth.wasPressedThisFrame
                               || gp.buttonEast.wasPressedThisFrame))
            {
                UI.Navigation.NoteDirectionalInput();
                return;
            }
            var kb = Keyboard.current;
            if (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame
                               || kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame
                               || kb.tabKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
            {
                UI.Navigation.NoteDirectionalInput();
                return;
            }
            var mouse = Mouse.current;
            if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > MouseMoveThreshold * MouseMoveThreshold
                                  || mouse.leftButton.wasPressedThisFrame))
            {
                UI.Navigation.NotePointerInput();
                return;
            }
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
                UI.Navigation.NotePointerInput();
        }
    }
}
#else
namespace PromptUGUI.Application.Navigation
{
    internal sealed class NavigationController : UnityEngine.MonoBehaviour { }
}
#endif
```
（`UI.Navigation.cs` 顶部 `using PromptUGUI.Application.Navigation;`。）

- [ ] **Step 5: 跑绿（EditMode）** — RUN-TESTS（group `NavEnableTests`）。预期全绿。`read_console types=["error"]` 零错误。

- [ ] **Step 6: 写 PlayMode 设备检测测试 `NavModePlayTests`**

```csharp
// Tests/PlayMode/Navigation/NavModePlayTests.cs
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PromptUGUI.Tests.PlayMode.Navigation
{
    public class NavModePlayTests : UnityEngine.InputSystem.InputTestFixture
    {
        [UnityTest]
        public IEnumerator GamepadInput_FlipsToDirectional()
        {
            UI.ResetForTests();
            var pad = InputSystem.AddDevice<Gamepad>();
            UI.UseGamepadNavigation();
            yield return null;
            Set(pad.leftStick, new Vector2(1f, 0f));   // InputTestFixture 注入
            yield return null;
            Assert.AreEqual(UI.Navigation.NavMode.Directional, UI.Navigation.Mode);
            UI.ResetForTests();
        }
    }
}
```
（`InputTestFixture` 来自 InputSystem TestFramework；测试工程须装。）

- [ ] **Step 7: 跑绿（PlayMode）** — RUN-TESTS（PlayMode，group `NavModePlayTests`）。MCP runner 不稳时按 CLAUDE.md 排查环境。

- [ ] **Step 8: LINT-CS + 提交**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI && git add Runtime/Application/UI.Navigation.cs Runtime/Application/Navigation/ Tests/
git commit -m "feat(nav): UI.UseGamepadNavigation + EventSystem ensure + device-mode controller

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: `Screen.Focus` + 初始焦点（`focus="true"`）

**Files:**
- Modify: `Runtime/Application/Screen.cs`（加 `Focus(idPath)`；`Open` 末尾设初始焦点）
- Test: `Tests/EditMode/Navigation/InitialFocusTests.cs`

**Interfaces:**
- Produces: `public void Screen.Focus(string idPath)`（经 `IScreen` 暴露）；`internal void Screen.ApplyInitialFocus()`（Open 调用，选 `focus="true"` 或文档序第一个可聚焦控件）。
- Consumes: `Screen.Get`/`_nodeMap`、`EventSystem.SetSelectedGameObject`、`UI.Navigation.IsEnabled`、`Selectable`。

- [ ] **Step 1: 写红测**

```csharp
// Tests/EditMode/Navigation/InitialFocusTests.cs
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class InitialFocusTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Open_SelectsControlMarkedFocus()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack><Btn id='a'>A</Btn><Btn id='b' focus='true'>B</Btn></VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            Assert.AreSame(screen.Get<Btn>("b").GameObject, EventSystem.current.currentSelectedGameObject);
        }

        [Test]
        public void Open_NoMarker_SelectsFirstFocusable()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack><Image id='img'/><Btn id='a'>A</Btn><Btn id='b'>B</Btn></VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            Assert.AreSame(screen.Get<Btn>("a").GameObject, EventSystem.current.currentSelectedGameObject);
        }

        [Test]
        public void Focus_ProgrammaticallySelects()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Btn id='a'>A</Btn></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            screen.Focus("a");
            Assert.AreSame(screen.Get<Btn>("a").GameObject, EventSystem.current.currentSelectedGameObject);
        }
    }
}
```

- [ ] **Step 2: 跑红** — RUN-TESTS（group `InitialFocusTests`）。预期：编译失败（`Focus` 不存在）/ 选区为空。

- [ ] **Step 3: 实现 `Focus` + `ApplyInitialFocus`**

`Screen.cs` 加（放在 `Get` 附近）：
```csharp
        public void Focus(string idPath)
        {
            var go = Get(idPath).GameObject;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(go);
        }

        internal void ApplyInitialFocus()
        {
            if (!UI.Navigation.IsEnabled) return;
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return;
            UnityEngine.GameObject pick = null;
            // 1) focus="true" 标记（原始属性，applier 静默跳过；文档序遍历 _nodeMap 的插入序）
            foreach (var kv in _nodeMap)
            {
                if (kv.Key.Attributes.TryGetValue("focus", out var f) && f == "true"
                    && IsFocusable(kv.Value)) { pick = kv.Value.GameObject; break; }
            }
            // 2) 否则文档序第一个可聚焦控件
            if (pick == null)
                foreach (var kv in _nodeMap)
                    if (IsFocusable(kv.Value)) { pick = kv.Value.GameObject; break; }
            if (pick != null) es.SetSelectedGameObject(pick);
        }

        private static bool IsFocusable(Controls.Control c)
        {
            if (c.GameObject == null) return false;
            var sel = c.GameObject.GetComponent<UnityEngine.UI.Selectable>();
            return sel != null && sel.IsActive() && sel.IsInteractable()
                   && sel.navigation.mode != UnityEngine.UI.Navigation.Mode.None;
        }
```
> 注：`_nodeMap` 是 `Dictionary<ElementNode, Control>`，C# 字典保留插入序近似文档序（实例化 DFS 顺序）；若需严格文档序可在实例化时另存有序 list，但 v1 用插入序足够。

在 `Open()` 末尾（`foreach (var hide in deferredHides) hide();` 之后、订阅 `Variants.Changed` 之前）加：
```csharp
            ApplyInitialFocus();
```

- [ ] **Step 4: 跑绿** — RUN-TESTS（group `InitialFocusTests`）+ 全 EditMode 回归。预期全绿。

- [ ] **Step 5: LINT-CS + 提交**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI && git add Runtime/Application/Screen.cs Tests/
git commit -m "feat(nav): Screen.Focus + initial focus (focus= marker / first focusable)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: 显式导航覆盖（`nav="none"` / `navUp..`）

**Files:**
- Create: `Runtime/Application/Navigation/ExplicitNavigationResolver.cs`
- Modify: `Runtime/Application/Screen.cs`（`Open`/`ReSolve` 末尾调用）
- Test: `Tests/EditMode/Navigation/ExplicitNavTests.cs`

**Interfaces:**
- Produces: `internal static void ExplicitNavigationResolver.Resolve(Screen screen, IReadOnlyDictionary<ElementNode, Control> nodeMap, VariantStore variants)`。
- 语义（原始属性，VariantResolver 解析）：`nav="none"` → `Selectable.navigation.mode = None`；`navUp/navDown/navLeft/navRight="id"` → `Explicit`，指定方向接目标 Selectable，未指定方向先用 `FindSelectableOnX` 几何邻居补齐。
- Consumes: `Screen.Get`、`VariantResolver.ResolveAttribute`、uGUI `Selectable.FindSelectableOnUp/Down/Left/Right`。

- [ ] **Step 1: 写红测**

```csharp
// Tests/EditMode/Navigation/ExplicitNavTests.cs
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class ExplicitNavTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Screen Open(string body)
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{body}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        [Test]
        public void NavNone_SetsModeNone()
        {
            var s = Open("<Btn id='a' nav='none'>A</Btn>");
            var sel = s.Get<Btn>("a").GameObject.GetComponent<Selectable>();
            Assert.AreEqual(Navigation.Mode.None, sel.navigation.mode);
        }

        [Test]
        public void NavUp_SetsExplicitToTarget()
        {
            var s = Open("<Btn id='a'>A</Btn><Btn id='b' navUp='a'>B</Btn>");
            var b = s.Get<Btn>("b").GameObject.GetComponent<Selectable>();
            var a = s.Get<Btn>("a").GameObject.GetComponent<Selectable>();
            Assert.AreEqual(Navigation.Mode.Explicit, b.navigation.mode);
            Assert.AreSame(a, b.navigation.selectOnUp);
        }
    }
}
```

- [ ] **Step 2: 跑红** — RUN-TESTS（group `ExplicitNavTests`）。预期失败（无解析器，nav 是默认 Automatic）。

- [ ] **Step 3: 实现 `ExplicitNavigationResolver`**

```csharp
// Runtime/Application/Navigation/ExplicitNavigationResolver.cs
using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using UnityEngine.UI;

namespace PromptUGUI.Application.Navigation
{
    internal static class ExplicitNavigationResolver
    {
        public static void Resolve(Screen screen, IReadOnlyDictionary<ElementNode, Control> nodeMap, VariantStore variants)
        {
            foreach (var kv in nodeMap)
            {
                var node = kv.Key; var control = kv.Value;
                if (control.GameObject == null) continue;
                var sel = control.GameObject.GetComponent<Selectable>();

                var navMode = VariantResolver.ResolveAttribute(node, "nav", variants);
                var up = VariantResolver.ResolveAttribute(node, "navUp", variants);
                var down = VariantResolver.ResolveAttribute(node, "navDown", variants);
                var left = VariantResolver.ResolveAttribute(node, "navLeft", variants);
                var right = VariantResolver.ResolveAttribute(node, "navRight", variants);

                bool hasExplicit = up != null || down != null || left != null || right != null;
                if (navMode == null && !hasExplicit) continue;
                if (sel == null) continue;   // 非 Selectable 控件：跳过（lint 另报，Task 10）

                if (navMode == "none")
                {
                    sel.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
                    continue;
                }
                if (!hasExplicit) continue;

                // 未指定方向先用几何邻居补齐，避免只写一向就把其余三向锁死（spec §7）。
                var nav = new UnityEngine.UI.Navigation
                {
                    mode = UnityEngine.UI.Navigation.Mode.Explicit,
                    selectOnUp = up != null ? Sel(screen, up) : sel.FindSelectableOnUp(),
                    selectOnDown = down != null ? Sel(screen, down) : sel.FindSelectableOnDown(),
                    selectOnLeft = left != null ? Sel(screen, left) : sel.FindSelectableOnLeft(),
                    selectOnRight = right != null ? Sel(screen, right) : sel.FindSelectableOnRight(),
                };
                sel.navigation = nav;
            }
        }

        private static Selectable Sel(Screen screen, string id)
            => screen.Get(id).GameObject.GetComponent<Selectable>();   // 不存在的 id 抛 KeyNotFoundException（spec §11）
    }
}
```
> `FindSelectableOnX()` 要几何邻居正确，须在布局稳定后调用——故在 `Open`/`ReSolve` 末尾运行（见 Step 4）。

- [ ] **Step 4: 在 `Screen.Open` / `ReSolve` 末尾接线**

`Open()` 末尾、`ApplyInitialFocus();` **之前**加：
```csharp
            Navigation.ExplicitNavigationResolver.Resolve(this, _nodeMap, Variants);
```
`ReSolve()` 末尾（`AttachPixelSnaps(RootGameObject);` 后）加同一行。

- [ ] **Step 5: 跑绿** — RUN-TESTS（group `ExplicitNavTests`）+ 全 EditMode 回归。预期全绿。

- [ ] **Step 6: LINT-CS + 提交**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI && git add Runtime/Application/Navigation/ExplicitNavigationResolver.cs Runtime/Application/Screen.cs Tests/
git commit -m "feat(nav): explicit nav overrides (nav=none / navUp..) with auto-neighbor fill

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: `<FocusCursor>` 解析 + 实例化进光标 overlay

**Files:**
- Modify: `Runtime/Core/IR/ScreenDef.cs:5-23`（加 `FocusCursor` 属性）
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs:131-142`（`ParseScreen` 抽出 `<FocusCursor>`）
- Modify: `Runtime/Application/Screen.cs`（`Open` 末尾搭建光标 overlay）
- Test: `Tests/EditMode/Navigation/FocusCursorParseTests.cs`

**Interfaces:**
- Produces:
  - `ScreenDef.FocusCursor`（`ElementNode`，null = 该屏未声明）。
  - `internal RectTransform Screen.SetupFocusCursor(ElementNode cursorNode)` — 建 overlay + InstantiateNode 子树 + 挂 `FocusCursorView`（Task 7 才让 view 动起来；本任务先把 overlay 实例化出来）。
- Consumes: `_instantiator.InstantiateNode(node, parent, owner)`、`UI.Navigation.IsEnabled`。

- [ ] **Step 1: 写红测**

```csharp
// Tests/EditMode/Navigation/FocusCursorParseTests.cs
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class FocusCursorParseTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void FocusCursor_NotInstantiatedAsLayoutChild_ButHoisted()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <FocusCursor side='left'><Image id='hand' size='16,16'/></FocusCursor>
  <VStack id='stack'><Btn id='a'>A</Btn></VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            // VStack 只有 1 个布局子（Btn a），光标不在其中
            Assert.AreEqual(1, screen.Get<VStack>("stack").GameObject.transform.childCount);
            // 光标 overlay 在 root 下存在，且含 hand
            var hand = screen.RootGameObject.GetComponentInChildren<UnityEngine.UI.Image>(true);
            Assert.IsNotNull(hand);
        }
    }
}
```

- [ ] **Step 2: 跑红** — RUN-TESTS（group `FocusCursorParseTests`）。预期：`<FocusCursor>` 当前会被当未知控件 tag → `_registry.Resolve` 抛异常（Open 失败）。

- [ ] **Step 3: `ScreenDef.FocusCursor` 属性**

`ScreenDef.cs` 在 `Templates` 后加：
```csharp
        /// <summary>该 Screen 声明的 &lt;FocusCursor&gt; 节点（解析时从 Root.Children 抽出）；null = 未声明，
        /// 运行时回退全局默认光标（spec §5.2）。</summary>
        public ElementNode FocusCursor { get; set; }
```

- [ ] **Step 4: `ParseScreen` 抽出 `<FocusCursor>`**

`UIDocumentParser.cs` 的 `ParseScreen` 内，在 `var screen = new ScreenDef(name, rootNode);`（142 行）之后、返回前加：
```csharp
            // <FocusCursor> 是 Screen 级声明，不进控件树（否则会被当未知控件实例化报错）。抽到 ScreenDef。
            for (int i = rootNode.Children.Count - 1; i >= 0; i--)
            {
                if (rootNode.Children[i].Tag != "FocusCursor") continue;
                if (screen.FocusCursor == null) screen.FocusCursor = rootNode.Children[i];
                rootNode.Children.RemoveAt(i);
            }
```
> 验证 `ElementNode.Children` 是可变 `List<ElementNode>`（实例化器 `foreach (var c in node.Children)` 表明是）。若为只读，改为重建过滤列表。

- [ ] **Step 5: `Screen.SetupFocusCursor` + Open 接线**

`Screen.cs` 加字段 `private RectTransform _cursorOverlay;` 和方法：
```csharp
        internal void SetupFocusCursor(ElementNode cursorNode)
        {
            if (cursorNode == null || cursorNode.Children == null || cursorNode.Children.Count == 0) return;
            var overlayGo = new UnityEngine.GameObject("__FocusCursor", typeof(RectTransform), typeof(UnityEngine.CanvasGroup));
            _cursorOverlay = (RectTransform)overlayGo.transform;
            _cursorOverlay.SetParent(RootGameObject.transform, worldPositionStays: false);
            _cursorOverlay.SetAsLastSibling();                       // 画在内容之上
            _cursorOverlay.anchorMin = _cursorOverlay.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
            _cursorOverlay.sizeDelta = UnityEngine.Vector2.zero;
            var le = overlayGo.AddComponent<UnityEngine.UI.LayoutElement>();
            le.ignoreLayout = true;
            // 光标视觉子树（取第一个子节点；多于一个时其余忽略——v1 单子约定）
            _instantiator.InstantiateNode(cursorNode.Children[0], _cursorOverlay, this);
            var view = overlayGo.AddComponent<Navigation.FocusCursorView>();
            view.Init(this, _cursorOverlay, cursorNode);             // Task 7 让它动
        }
```
`Open()` 末尾（`ApplyInitialFocus();` 之后）加：
```csharp
            if (UI.Navigation.IsEnabled)
                SetupFocusCursor(Def.FocusCursor ?? UI.Navigation.DefaultCursorNode);
```
> `UI.Navigation.DefaultCursorNode` 在 Task 8 实现；本任务先让它返回 `null`（加 `internal static ElementNode DefaultCursorNode => null;` 占位到 `UI.Navigation`，Task 8 替换）。本任务测试用屏内 `<FocusCursor>`，不依赖默认。
> `FocusCursorView.Init` 在 Task 7 实现；本任务先建一个最小 `FocusCursorView : UIBehaviour { internal void Init(Screen s, RectTransform rt, ElementNode n) {} }` 占位，Task 7 填充。

- [ ] **Step 6: 跑绿** — RUN-TESTS（group `FocusCursorParseTests`）+ 全 EditMode 回归（确认抽 `<FocusCursor>` 没影响普通解析）。预期全绿。

- [ ] **Step 7: LINT-CS + 提交**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI && git add Runtime/Core/IR/ScreenDef.cs Runtime/Core/Parser/UIDocumentParser.cs Runtime/Application/Screen.cs Runtime/Application/Navigation/ Tests/
git commit -m "feat(nav): parse <FocusCursor> + hoist into screen cursor overlay

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: `FocusCursorView` 控制器（追踪 + 像素吸附 + 滑动 + 显隐）

**Files:**
- Modify: `Runtime/Application/Navigation/FocusCursorView.cs`（填充 Task 6 的占位）
- Test: `Tests/EditMode/Navigation/FocusCursorPositionTests.cs`、`Tests/PlayMode/Navigation/FocusCursorPlayTests.cs`

**Interfaces:**
- Produces: `FocusCursorView.Init(Screen, RectTransform overlay, ElementNode cursorNode)`；逐帧 `LateUpdate` 按 `UI.Navigation.IsDirectional` + `EventSystem.currentSelectedGameObject ∈ 本屏` 显隐 + 定位。
- Consumes: `TutorialOverlayView.WorldRectToLocal(target, overlayRect)`（internal static，同程序集复用）、`PixelSnap.SnapToPixelGrid(rt, canvas, localRef)`、`LMotion`、`UI.Navigation.IsDirectional`。

- [ ] **Step 1: 写红测（EditMode 定位 —— 直接驱动，不靠输入）**

```csharp
// Tests/EditMode/Navigation/FocusCursorPositionTests.cs
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class FocusCursorPositionTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Cursor_HiddenInPointerMode_VisibleAndTracksInDirectional()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <FocusCursor side='left'><Image id='hand' size='16,16'/></FocusCursor>
  <Btn id='a' anchor='center' size='100,40'>A</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            EventSystem.current.SetSelectedGameObject(screen.Get<Btn>("a").GameObject);

            var overlay = screen.RootGameObject.transform.Find("__FocusCursor").GetComponent<CanvasGroup>();
            var view = overlay.GetComponent<Navigation.FocusCursorView>();

            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            view.TickForTests();
            Assert.AreEqual(0f, overlay.alpha);                 // pointer：隐藏

            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            view.TickForTests();
            Assert.AreEqual(1f, overlay.alpha);                 // directional：显示
        }
    }
}
```

- [ ] **Step 2: 跑红** — RUN-TESTS（group `FocusCursorPositionTests`）。预期失败（view 是空占位，alpha 不变）。

- [ ] **Step 3: 实现 `FocusCursorView`**

```csharp
// Runtime/Application/Navigation/FocusCursorView.cs
using PromptUGUI.Application.Tutorial;        // WorldRectToLocal
using PromptUGUI.Controls.Internal;           // PixelSnap
using PromptUGUI.IR;
using LitMotion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PromptUGUI.Application.Navigation
{
    internal sealed class FocusCursorView : UIBehaviour
    {
        private Screen _owner;
        private RectTransform _rt;        // 光标 overlay 根（被移动）
        private CanvasGroup _cg;
        private Canvas _canvas;
        private string _side = "left";
        private Vector2 _offset;
        private MotionHandle _slide;
        private bool _hasLast;
        private Vector2 _lastTarget;

        internal void Init(Screen owner, RectTransform overlay, ElementNode cursorNode)
        {
            _owner = owner;
            _rt = overlay;
            _cg = overlay.GetComponent<CanvasGroup>();
            _canvas = overlay.GetComponentInParent<Canvas>();
            if (cursorNode.Attributes.TryGetValue("side", out var s)) _side = s;
            if (cursorNode.Attributes.TryGetValue("offset", out var o)) _offset = ParseVec(o);
            _cg.alpha = 0f;
        }

        private void LateUpdate() => Tick();

        internal void TickForTests() => Tick();

        private void Tick()
        {
            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            bool show = UI.Navigation.IsDirectional && sel != null && _owner.RootGameObject != null
                        && sel.transform.IsChildOf(_owner.RootGameObject.transform);
            if (!show) { _cg.alpha = 0f; _hasLast = false; return; }

            _cg.alpha = 1f;
            var targetRt = (RectTransform)sel.transform;
            var rect = TutorialOverlayView.WorldRectToLocal(targetRt, (RectTransform)_rt.parent);
            var p = EdgePoint(rect) + _offset;

            if (!_hasLast) { _rt.anchoredPosition = p; _hasLast = true; _lastTarget = p; }
            else if ((p - _lastTarget).sqrMagnitude > 0.01f)
            {
                _lastTarget = p;
                if (_slide.IsActive()) _slide.TryCancel();
                _slide = LMotion.Create(_rt.anchoredPosition, p, 0.12f).WithEase(Ease.OutCubic)
                    .Bind(_rt, static (v, rt) => { if (rt) rt.anchoredPosition = v; }).AddTo(_rt.gameObject);
            }
            if (_canvas != null) PixelSnap.SnapToPixelGrid(_rt, _canvas, Vector2.zero);
        }

        private Vector2 EdgePoint(Rect r) => _side switch
        {
            "right" => new Vector2(r.xMax, r.center.y),
            "top" => new Vector2(r.center.x, r.yMax),
            "bottom" => new Vector2(r.center.x, r.yMin),
            _ => new Vector2(r.xMin, r.center.y),       // left
        };

        private static Vector2 ParseVec(string s)
        {
            var p = s.Split(',');
            return p.Length == 2 && float.TryParse(p[0], out var x) && float.TryParse(p[1], out var y)
                ? new Vector2(x, y) : Vector2.zero;
        }

        protected override void OnDestroy()
        {
            if (_slide.IsActive()) _slide.TryCancel();
            base.OnDestroy();
        }
    }
}
```
> 注：滑动 tween `.AddTo(_rt.gameObject)` 绑定到光标 GO 生命周期（既有 motion-lifetime 坑）。像素吸附用 `SnapToPixelGrid(_rt, _canvas, Vector2.zero)`：以光标根原点为参考点吸到整数设备像素。

- [ ] **Step 4: 跑绿（EditMode）** — RUN-TESTS（group `FocusCursorPositionTests`）。预期全绿。

- [ ] **Step 5: PlayMode 跟随测试 `FocusCursorPlayTests`**

```csharp
// Tests/PlayMode/Navigation/FocusCursorPlayTests.cs
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Navigation
{
    public class FocusCursorPlayTests
    {
        [UnityTest]
        public IEnumerator Cursor_FollowsSelectionChange()
        {
            UI.ResetForTests();
            UI.UseGamepadNavigation();
            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <FocusCursor side='left'><Image id='hand' size='16,16'/></FocusCursor>
  <VStack spacing='40'><Btn id='a' size='100,40'>A</Btn><Btn id='b' size='100,40'>B</Btn></VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var overlay = (RectTransform)screen.RootGameObject.transform.Find("__FocusCursor");
            EventSystem.current.SetSelectedGameObject(screen.Get<Btn>("a").GameObject);
            yield return null; yield return null;
            var posA = overlay.anchoredPosition;
            EventSystem.current.SetSelectedGameObject(screen.Get<Btn>("b").GameObject);
            yield return null; yield return null; yield return null;
            Assert.AreNotEqual(posA.y, overlay.anchoredPosition.y);   // 光标移到了 B
            UI.ResetForTests();
        }
    }
}
```

- [ ] **Step 6: 跑绿（PlayMode）** — RUN-TESTS（PlayMode，group `FocusCursorPlayTests`）。

- [ ] **Step 7: LINT-CS + 提交**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI && git add Runtime/Application/Navigation/FocusCursorView.cs Tests/
git commit -m "feat(nav): FocusCursorView tracks selection (WorldRectToLocal + PixelSnap + LitMotion slide)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: 内置默认光标 + 全局默认

**Files:**
- Create: `Runtime/Resources/PromptUGUI/Navigation/FocusCursor.ui.xml`（+ `.meta`）
- Modify: `Runtime/Application/UI.Navigation.cs`（`DefaultCursorNode` 懒加载内置；可选 `DefaultCursorSrc`）
- Test: `Tests/EditMode/Navigation/DefaultCursorTests.cs`

**Interfaces:**
- Produces: `internal static ElementNode UI.Navigation.DefaultCursorNode { get; }`（懒加载内置文档的 `<FocusCursor>`，缓存）；`public static string UI.Navigation.DefaultCursorSrc { get; set; }`（默认 null = 内置；非 null 时由调用方 SourceResolver 懒加载——见说明）。
- Consumes: `Resources.Load<TextAsset>`、`UIDocumentParser.Parse`。

- [ ] **Step 1: 写内置默认光标 XML**

```xml
<!-- Runtime/Resources/PromptUGUI/Navigation/FocusCursor.ui.xml -->
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="PromptUGUI/Navigation/FocusCursor.ui">
    <FocusCursor side="left" offset="-4,0">
      <Image anchor="center" size="24x24" sprite="PromptUGUI/Defaults/pugui.png#pugui_caret"/>
    </FocusCursor>
  </Screen>
</PromptUGUI>
```
> 复用 Tutorial 手指同款内置子精灵 `pugui_caret`（已验证可从包 Resources 运行时加载）。

- [ ] **Step 2: 跑 LINT-XML（验证内置文档合法）**

```bash
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/PromptUGUI/Navigation/FocusCursor.ui.xml
```
预期 exit 0。

- [ ] **Step 3: 写红测**

```csharp
// Tests/EditMode/Navigation/DefaultCursorTests.cs
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class DefaultCursorTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void ScreenWithoutFocusCursor_UsesBuiltInDefault()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Btn id='a'>A</Btn></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            // 屏内没写 <FocusCursor>，但全局默认让 overlay 出现
            Assert.IsNotNull(screen.RootGameObject.transform.Find("__FocusCursor"));
        }
    }
}
```

- [ ] **Step 4: 跑红** — RUN-TESTS（group `DefaultCursorTests`）。预期失败（`DefaultCursorNode` 仍是 Task 6 的 `null` 占位）。

- [ ] **Step 5: 实现 `DefaultCursorNode` 懒加载**

`UI.Navigation.cs` 把 Task 6 的占位 `DefaultCursorNode => null;` 替换为：
```csharp
            private static IR.ElementNode _defaultCursorNode;
            private static bool _defaultCursorLoaded;
            public static string DefaultCursorSrc { get; set; }   // null = 内置；非 null 见说明

            internal static IR.ElementNode DefaultCursorNode
            {
                get
                {
                    if (_defaultCursorLoaded) return _defaultCursorNode;
                    _defaultCursorLoaded = true;
                    var ta = UnityEngine.Resources.Load<UnityEngine.TextAsset>("PromptUGUI/Navigation/FocusCursor.ui");
                    if (ta == null) return null;
                    var doc = Core.Parser.UIDocumentParser.Parse(ta.text);
                    foreach (var sc in doc.Screens)
                        if (sc.FocusCursor != null) { _defaultCursorNode = sc.FocusCursor; break; }
                    return _defaultCursorNode;
                }
            }
```
并在 `ResetForTestsInternal()` 加：
```csharp
                _defaultCursorNode = null; _defaultCursorLoaded = false; DefaultCursorSrc = null;
```
> `UIDocumentParser.Parse(string)` 与 `UIDocument.Screens` 的精确名以现状代码为准（解析入口；`doc.Screens` 为 `List<ScreenDef>`）。验证后调整。
> **自定义全局光标（`DefaultCursorSrc != null`）**：v1 仅占位该属性；若要支持，懒加载走 `SourceResolver`（异步 `Awaitable`，首屏打开时取一次缓存，失败 `LogWarning` + 回退内置）。本任务测试只覆盖内置路径；自定义 src 的异步加载留作小增量，勿引入 `Task`。

- [ ] **Step 6: 跑绿** — RUN-TESTS（group `DefaultCursorTests`、`FocusCursorParseTests`）+ 全 EditMode 回归。预期全绿。

- [ ] **Step 7: 提交（含 .meta）**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI && git add Runtime/Resources/PromptUGUI/Navigation/ Runtime/Application/UI.Navigation.cs Tests/
git status --short    # 确认 FocusCursor.ui.xml.meta 一并被加（Unity 生成）
git commit -m "feat(nav): built-in default focus cursor + global fallback

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: 模态焦点 trap + 关闭还原

**Files:**
- Modify: `Runtime/Application/UI.Modal.cs`（`Slot` 加 `PrevSelected`；`MaterializePump` 记录+设初始焦点；`RemoveSlot` 还原）
- Modify: `Runtime/Application/Navigation/NavigationController.cs`（选区限制守卫）
- Modify: `Runtime/Application/UI.Navigation.cs`（`internal static GameObject ContainmentRoot`）
- Test: `Tests/EditMode/Navigation/ModalTrapTests.cs`、`Tests/PlayMode/Navigation/ModalTrapPlayTests.cs`

**Interfaces:**
- Produces:
  - `Slot.PrevSelected`（`GameObject`，打开模态前的选区）。
  - `internal static GameObject UI.Navigation.ContainmentRoot { get; set; }`（非 null 时，选区须在其子树内，越界吸回）。
  - `internal static GameObject UI.Navigation.FirstFocusableUnder(GameObject root)`。
- Consumes: `EventSystem.currentSelectedGameObject`、`Screen.ApplyInitialFocus`、`RefreshTopListener` 同款栈顶逻辑。

- [ ] **Step 1: 写红测（EditMode —— 直接验证守卫逻辑）**

```csharp
// Tests/EditMode/Navigation/ModalTrapTests.cs
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class ModalTrapTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Containment_SnapsBack_WhenSelectionEscapes()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='outside'>O</Btn>
  <Frame id='trap'><Btn id='inside'>I</Btn></Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            UI.Navigation.ContainmentRoot = screen.Get<Frame>("trap").GameObject;
            EventSystem.current.SetSelectedGameObject(screen.Get<Btn>("outside").GameObject);  // 越界
            UI.Navigation.EnforceContainmentForTests();
            Assert.AreSame(screen.Get<Btn>("inside").GameObject, EventSystem.current.currentSelectedGameObject);
        }
    }
}
```

- [ ] **Step 2: 跑红** — RUN-TESTS（group `ModalTrapTests`）。预期：编译失败（`ContainmentRoot`/`EnforceContainmentForTests` 不存在）。

- [ ] **Step 3: `UI.Navigation` 选区限制**

`UI.Navigation.cs` 加：
```csharp
            internal static UnityEngine.GameObject ContainmentRoot { get; set; }

            internal static void EnforceContainment()
            {
                var root = ContainmentRoot;
                if (root == null) return;
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es == null) return;
                var sel = es.currentSelectedGameObject;
                if (sel != null && sel.transform.IsChildOf(root.transform)) return;
                var pick = FirstFocusableUnder(root);
                if (pick != null) es.SetSelectedGameObject(pick);
            }

            internal static void EnforceContainmentForTests() => EnforceContainment();

            internal static UnityEngine.GameObject FirstFocusableUnder(UnityEngine.GameObject root)
            {
                var all = root.GetComponentsInChildren<UnityEngine.UI.Selectable>(false);
                foreach (var s in all)
                    if (s.IsActive() && s.IsInteractable()
                        && s.navigation.mode != UnityEngine.UI.Navigation.Mode.None)
                        return s.gameObject;
                return null;
            }
```
并把 `ResetForTestsInternal()` 加：`ContainmentRoot = null;`。
`NavigationController.Update()` 末尾（`#if ENABLE_INPUT_SYSTEM` 分支内）加：`UI.Navigation.EnforceContainment();`。

- [ ] **Step 4: 模态接线（记录 / 设初始焦点 / 还原 / 守卫根）**

`UI.Modal.cs` 的 `Slot` 类加字段：
```csharp
        public UnityEngine.GameObject PrevSelected;
```
`MaterializePump` 内 `entry.RunBind(screen, ...)` 之后、`RefreshTopListener();` 之前加：
```csharp
                if (UI.Navigation.IsEnabled)
                {
                    var es = UnityEngine.EventSystems.EventSystem.current;
                    slot.PrevSelected = es != null ? es.currentSelectedGameObject : null;
                    screen.ApplyInitialFocus();                       // 焦点进模态
                    UI.Navigation.ContainmentRoot = screen.RootGameObject;  // trap 到本模态
                }
```
`RemoveSlot(Slot slot)` 体内 `CloseModalScreen(slot.Key);` 之后加：
```csharp
            if (UI.Navigation.IsEnabled)
            {
                UI.Navigation.ContainmentRoot = _stack.Count > 0
                    ? _stack[_stack.Count - 1].Screen.RootGameObject : null;   // 还原到新栈顶或解除
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es != null && slot.PrevSelected != null)
                    es.SetSelectedGameObject(slot.PrevSelected);
            }
```

- [ ] **Step 5: 跑绿（EditMode）** — RUN-TESTS（group `ModalTrapTests`）+ 全 EditMode 回归（确认模态既有测试不破）。预期全绿。

- [ ] **Step 6: PlayMode trap 测试 `ModalTrapPlayTests`**（打开 MessageBox → 选区在模态内；关闭 → 还原。仿 `Tests/PlayMode` 既有 Modal+EventSystem 范式，SetUp 建 EventSystem。略，结构同 Task 7 PlayMode。）

- [ ] **Step 7: LINT-CS + 提交**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI && git add Runtime/Application/UI.Modal.cs Runtime/Application/Navigation/ Runtime/Application/UI.Navigation.cs Tests/
git commit -m "feat(nav): modal focus trap (selection containment) + restore on close

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: CLI lint —— `nav*` 目标校验

**Files:**
- Create: `Runtime/Core/Lint/NavTargetRules.cs`
- Modify: 把规则接进 IRWalker dispatch（参考 `Runtime/Core/Lint/` 既有规则如 `MaskAttributeRules`/`ImageFitRules` 的注册处）
- Test: `Tests/EditMode/Lint/NavTargetRulesTests.cs`

**Interfaces:**
- Produces:
  - `PUI-NAV-ON-NON-SELECTABLE`：`nav*`/`focus` 落在非可交互控件（tag 不在 `{Btn,Tab,Toggle,Slider,Dropdown,InputField,ScrollList}`）。
  - `PUI-NAV-UNKNOWN-TARGET`：`navUp/navDown/navLeft/navRight` 的值不是同文档可解析 id（best-effort：同 Screen 内 id 集合）。
- Consumes: `Runtime/Core/Lint/` 既有 `LintIssue` + IRWalker 模式。

- [ ] **Step 1: 看齐既有 lint 规则形状**

```bash
ls Runtime/Core/Lint/
sed -n '1,60p' Runtime/Core/Lint/ImageFitRules.cs   # 看 CheckX(node) → IEnumerable<LintIssue> 与 code 常量写法
grep -rn "CheckImage\|CheckFrame\|CheckCarousel" Runtime/Application/ScreenInstantiator.cs   # 看运行时 dispatch
grep -rn "InteractableTags\|StateSourceTags\|Selectable" Runtime/Core/Lint/   # 看是否已有"可交互 tag 集合"
```

- [ ] **Step 2: 写红测 `NavTargetRulesTests`**

```csharp
// Tests/EditMode/Lint/NavTargetRulesTests.cs
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class NavTargetRulesTests
    {
        private static ElementNode Node(string tag, params (string, string)[] attrs)
        {
            var n = new ElementNode { Tag = tag };
            foreach (var (k, v) in attrs) n.Attributes[k] = v;
            return n;
        }

        [Test]
        public void NavOnFrame_Errors()
        {
            var issues = NavTargetRules.CheckNav(Node("Frame", ("navUp", "x"))).ToList();
            Assert.IsTrue(issues.Any(i => i.Code == "PUI-NAV-ON-NON-SELECTABLE"));
        }

        [Test]
        public void NavOnBtn_NoSelectableError()
        {
            var issues = NavTargetRules.CheckNav(Node("Btn", ("navUp", "x"))).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == "PUI-NAV-ON-NON-SELECTABLE"));
        }
    }
}
```
（`ElementNode`/`LintIssue` 构造与字段名以现状为准，按 Step 1 结果对齐。）

- [ ] **Step 3: 跑红** — RUN-TESTS（group `NavTargetRulesTests`）。预期编译失败（`NavTargetRules` 不存在）。

- [ ] **Step 4: 实现 `NavTargetRules`**（按 Step 1 看到的 `LintIssue`/规则签名落地；`CheckNav(ElementNode) → IEnumerable<LintIssue>`，可交互 tag 集合复用既有或新建 `{Btn,Tab,Toggle,Slider,Dropdown,InputField,ScrollList}`）。把 `CheckNav` 接进 `ScreenInstantiator.InstantiateRecursive` 的 tag-检查段（每节点都查，因 nav* 是通用原始属性）和 CLI walker。`PUI-NAV-UNKNOWN-TARGET` 在 CLI 侧做（需全 Screen id 集合，运行时 best-effort 可略）。

- [ ] **Step 5: 跑绿** — RUN-TESTS（group `NavTargetRulesTests`）+ 全 EditMode + `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/`（确认无新误报）。

- [ ] **Step 6: LINT-CS + 提交**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI && git add Runtime/Core/Lint/NavTargetRules.cs Runtime/Application/ScreenInstantiator.cs Tests/ .lint/
git commit -m "feat(nav): CLI lint PUI-NAV-ON-NON-SELECTABLE / PUI-NAV-UNKNOWN-TARGET

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 11: SKILL 更新（英文）+ `reference/navigation.md`

**Files:**
- Create: `.claude/skills/authoring-promptugui-xml/reference/navigation.md`
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`（主文档加 `<FocusCursor>` / `focus` / `nav*` 行 + 指针）
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`（`UI.UseGamepadNavigation` / `Screen.Focus` / `InteractState.Focused`）

**Interfaces:** 仅文档。无测试。

- [ ] **Step 1: 写 `reference/navigation.md`**（深水区：导航模式 Pointer↔Directional、仅新 Input System、`<FocusCursor>`（side/offset、子树即光标、可含 `<Animation>`、模板化、内置默认）、`focus="true"`、`nav="none"`、`navUp/navDown/navLeft/navRight="id"`（自动补几何邻居）、模态 trap/还原、`UI.UseGamepadNavigation()`/`Screen.Focus`。给 1-2 个完整 XML+C# 例子。）

- [ ] **Step 2: 主 XML SKILL 加行 + 指针**（通用可交互属性表加 `focus`/`nav`/`navUp..`；新元素表加 `<FocusCursor>` 一行 stub + `→ reference/navigation.md` 指针，照既有 `animations.md`/`states.md` 指针格式。）

- [ ] **Step 3: C# SKILL 加节**（「Gamepad / Keyboard Navigation」：`UI.UseGamepadNavigation()`、`Screen.Focus(idPath)`、`InteractState.Focused`（reuse-hover、Directional-only）、注明仅新 Input System、EventSystem 由 helper 兜底创建。）

- [ ] **Step 4: 用子代理验证 SKILL**（dispatch 一个 general-purpose 子代理：只读 SKILL，照着写一个"主菜单 + 手柄导航 + 自定义动画手指 + 一个模态"的 `.ui.xml` + C# 启动片段，回报是否信息自洽、有无缺口。按反馈补 SKILL。）

- [ ] **Step 5: 提交**

```bash
cd /workspace-PromptUGUI && git add .claude/skills/
git commit -m "docs(nav): XML + C# SKILL updates for gamepad/keyboard navigation

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 12: 全套件 + lint 终检 + 视觉 QA 清单

**Files:** 无源码改动（除非回归暴露 bug）。

- [ ] **Step 1: 刷新 + 零编译错误** — `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` → `read_console types=["error"]`。

- [ ] **Step 2: 全 EditMode** — `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` → 轮询 `get_test_job`。**全绿**（不靠 group 子集；CLAUDE.md/记忆：别只跑 targeted group 就宣称全绿）。

- [ ] **Step 3: 全 EditorOnly** — `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])`。

- [ ] **Step 4: 全 PlayMode** — `run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])`。runner 不稳时按 CLAUDE.md 排查；确认 `Navigation` + 既有 `StateTintReactorPlayTests`/`TabBar`/`Carousel` 全绿。

- [ ] **Step 5: lint 全绿** — `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx` + `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/`。

- [ ] **Step 6: grep 终检**（防漏改 switch / 折叠语义）

```bash
grep -rn "InteractState" Runtime/ | grep -i "switch\|=>"          # 确认 Focused 处处有兜底
grep -rn "MapTransient(3)\|navigation-Selected\|folds to" Runtime/ Tests/   # 注释/断言与新语义一致
```

- [ ] **Step 7: 输出视觉 QA 清单给用户**（库无法自验的）：
  1. PC 插手柄：拨摇杆/方向键，手指在控件间跳、被指控件复用 hover 提亮；按键确认触发。
  2. 鼠标一动：手指消失、焦点不粘高亮（点完按钮不卡亮）。
  3. 手机竖屏：全程无手指。
  4. 打开模态：焦点进模态、方向键到不了背后按钮；关闭：焦点还原。
  5. 自定义 `<FocusCursor>` 带 `<Animation>`：手指 idle 摆动 + 切目标平滑滑动 + 像素不抖。
  6. `navUp/navDown` 显式路由按预期；`nav="none"` 控件被跳过。
  7. Linear 色彩空间下焦点 hover 提亮观感正常。

- [ ] **Step 8: 收尾**——按 superpowers:finishing-a-development-branch 决定 merge/PR；更新自动记忆（feat/gamepad-keyboard-navigation 状态）。

---

## Self-Review 注记（写计划时已核）

- **Spec 覆盖**：§3 模式→Task1/3；§4 焦点→Task1/2；§5 光标→Task6/7/8；§6 初始焦点+trap→Task4/9；§7 显式→Task5；§8 API→Task3/4；§9 输入门控→Task3（`#if`）；§14 SKILL→Task11；§12 测试→各任务 + Task12。
- **类型一致**：`UI.Navigation.NavMode`/`Mode`/`IsDirectional`/`IsEnabled`/`Controller`/`ContainmentRoot`/`DefaultCursorNode`、`IStateSource.RefreshState`、`Screen.Focus`/`ApplyInitialFocus`/`SetupFocusCursor`、`FocusCursorView.Init`/`TickForTests`、`ExplicitNavigationResolver.Resolve` —— 跨任务引用名一致。
- **已知"需现状核对再微调"点**（实现时验证，非占位）：`ElementNode.Children` 可变性（Task6 Step4）、`UIDocumentParser.Parse`/`UIDocument.Screens` 名（Task8 Step5）、`LintIssue`/既有 lint dispatch 形状（Task10 Step1）、`VariantResolver.ResolveAttribute` 对未知属性返回 null 的行为（Task5）。每处都给了核对命令。
