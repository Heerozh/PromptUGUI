# 隐藏焦点下 Submit 唤醒 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复「鼠标用过后焦点光标隐藏、但 EventSystem 选区仍在旧控件上时，按确认键会误触发那个看不见的旧焦点」——改为第一次确认只唤回光标、不触发。

**Architecture:** 在 `UI.Navigation` 加一个单一来源的唤醒门 `TryWakeOnSubmit()`；`PuiButton` / `PuiToggle` 各 override `OnSubmit` 一行调用它（Pointer 模式收到 Submit → 翻 Directional 唤回光标 + 吞掉点击）。同时把 Submit 类输入（`enterKey` / `buttonSouth`）从 `NavigationController` 的「翻 Directional」判定里移除，避免执行序竞争让门读到过期模式。

**Tech Stack:** Unity 6, uGUI `Button`/`Toggle`（`OnSubmit` 均 `public virtual`）+ EventSystem, New Input System（仅 `NavigationController` 改动在 `#if ENABLE_INPUT_SYSTEM` 内）, R3, NUnit + Unity Test Framework（EditMode 纯逻辑主测 + 一个 PlayMode 真实输入集成）。

设计源：`docs~/superpowers/specs/2026-06-30-nav-hidden-submit-wake-design.md`。
承接母特性：`docs~/superpowers/specs/2026-06-29-gamepad-keyboard-navigation-design.md`（修订其 §3 模式翻转触发器、§11 边界表）。

## Global Constraints

- **严格附加 / 纯 runtime**：不改任何公共 C# API、不新增/改 XML 元素或属性、不改 `id` 路径语义。
- **门控自洽**：唤醒门 + 两个 override **不需** `#if ENABLE_INPUT_SYSTEM`——它们只引用 `UI.Navigation`（`IsEnabled`/`IsDirectional`/`Mode`/`NoteDirectionalInput` 均定义在 `#if` 之外）与 `UnityEngine.EventSystems`，不碰 InputSystem 类型。导航未启用时 `IsEnabled==false` → `TryWakeOnSubmit` 恒返回 `false` → override 恒走 `base.OnSubmit` → 零行为变化。**只有 `NavigationController` 的删除动作在既有 `#if ENABLE_INPUT_SYSTEM` 块内。**
- **只拦 Submit，不拦指针**：鼠标点击走 `IPointerClickHandler.OnPointerClick`（不动），鼠标点 OK 永远照常工作。
- **InputField 排除在 v1 之外**：只 override `PuiButton` / `PuiToggle`（盖 Btn/Tab/Toggle）；`TMP_InputField` 的两级编辑不纳入。
- **统一「先唤回」规则**：Pointer 模式下任何 Submit（含开屏首次回车）都先唤回不触发；这是已 review 接受的有意行为变更（spec §4 边界表「模态刚打开首次回车」行）。
- **WebGL 安全**：纯同步逻辑，无 `System.Threading.Task` / `TaskCompletionSource`。
- **C# LangVersion 9**：无 C#10+ 语法（无 primary constructor、无 `[]` collection expression、无 `[field: SerializeField]`）。
- **测试经 Unity MCP**：改源后 `refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` → `read_console(types=["error"])` 零编译错误 → `run_tests` 轮询 `get_test_job`。PlayMode 加 `init_timeout=120000`。
- **lint**：`cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx` exit 0。
- **提交**：每个 commit 消息正文末尾加一行 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`（下文步骤只列 subject）。
- **分支**：已在 `fix/nav-hidden-submit-wake`，**禁止提交到 main**。

### ⚠️ 实现期注意点

1. **`Button.OnSubmit` 走协程**（`StartCoroutine(OnFinishSubmit())`）→ EditMode 调 `base.OnSubmit` 会有协程噪声/报错。故 PuiButton 的 EditMode 测试**只测「吞掉」分支**（提前 return，不调 base，干净）；「放行点击」分支交给 PlayMode 集成（Task 2）+ `TryWakeOnSubmit` 的 Directional→false 单测覆盖。
2. **`Toggle.OnSubmit` 无协程**（仅 `InternalToggle()` 翻 `isOn`）→ PuiToggle 的吞掉**和**放行两条分支都能在 EditMode 干净测。
3. **PlayMode 真实输入集成对执行序敏感**：若 `NavigationController.Update` 在该帧先于 input module 运行、且控制器仍把 enter 当方向输入，则门会读到 Directional 而放行——本计划删 enter/South 后该竞争消失。集成测试在「控制器先跑」的执行序下能抓到回归；在「module 先跑」的执行序下两断言对缺失控制器改动不敏感（诚实记录，非完美守卫）。控制器改动的必要性主要由 spec §3.3 推理保证。
4. **PlayMode runner 历史偶发不稳**（项目记忆）：失败先 `read_console` 排查环境（多余 EventSystem 泄漏等），再判真伪。

---

## Task 1: 唤醒门 `TryWakeOnSubmit` + `PuiButton`/`PuiToggle` 两个 override

**Files:**
- Modify: `Runtime/Application/UI.Navigation.cs`（在 `NoteDirectionalInput` 附近加 `TryWakeOnSubmit`）
- Modify: `Runtime/Controls/Internal/PuiButton.cs`（加 `OnSubmit` override + usings）
- Modify: `Runtime/Controls/Internal/PuiToggle.cs`（加 `OnSubmit` override + usings）
- Test: `Tests/EditMode/Navigation/SubmitWakeTests.cs`（新建）

**Interfaces:**
- Consumes: `UI.Navigation.IsEnabled`（public）、`UI.Navigation.IsDirectional`（internal）、`UI.Navigation.NoteDirectionalInput()`（internal）、uGUI `Button.OnSubmit`/`Toggle.OnSubmit`（`public virtual`）。
- Produces:
  - `internal static bool UI.Navigation.TryWakeOnSubmit()` —— `true` = 调用方应吞掉本次 Submit；`false` = 照常点击。返回 `true` 时已把 `Mode` 翻成 `Directional`。
  - `PuiButton.OnSubmit` / `PuiToggle.OnSubmit` override：`if (UI.Navigation.TryWakeOnSubmit()) return; base.OnSubmit(eventData);`

- [ ] **Step 1: 写失败的 EditMode 测试**

新建 `Tests/EditMode/Navigation/SubmitWakeTests.cs`：
```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class SubmitWakeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // ── TryWakeOnSubmit 三分支（纯逻辑，无控件） ──────────────────────────
        [Test]
        public void TryWake_NavDisabled_ReturnsFalse_NoFlip()
        {
            Assert.IsFalse(UI.Navigation.IsEnabled);
            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            Assert.IsFalse(UI.Navigation.TryWakeOnSubmit());
            Assert.AreEqual(UI.Navigation.NavMode.Pointer, UI.Navigation.Mode);
        }

        [Test]
        public void TryWake_Directional_ReturnsFalse_NoFlip()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            Assert.IsFalse(UI.Navigation.TryWakeOnSubmit());
            Assert.AreEqual(UI.Navigation.NavMode.Directional, UI.Navigation.Mode);
        }

        [Test]
        public void TryWake_PointerEnabled_ReturnsTrue_FlipsToDirectional()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            Assert.IsTrue(UI.Navigation.TryWakeOnSubmit());
            Assert.AreEqual(UI.Navigation.NavMode.Directional, UI.Navigation.Mode);
        }

        // ── PuiButton.OnSubmit 吞掉分支（EditMode 干净：提前 return，不调 base 协程） ──
        [Test]
        public void PuiButton_Submit_InPointerMode_SwallowsClick_AndWakes()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Btn id='b'>Hi</Btn></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var pui = btn.GameObject.GetComponent<PuiButton>();
            bool clicked = false;
            btn.OnClick.Subscribe(_ => clicked = true).AddTo(screen);

            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            var es = Object.FindAnyObjectByType<EventSystem>();
            pui.OnSubmit(new BaseEventData(es));

            Assert.IsFalse(clicked, "Submit while cursor hidden (Pointer) must NOT click");
            Assert.AreEqual(UI.Navigation.NavMode.Directional, UI.Navigation.Mode,
                "first Submit wakes the cursor instead of acting");
        }

        // ── PuiToggle.OnSubmit 两条分支（EditMode 干净：Toggle.OnSubmit 无协程） ──
        private static UnityEngine.UI.Toggle BuildToggle()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Toggle id='t'>On</Toggle></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            return screen.Get<Toggle>("t").GameObject.GetComponent<UnityEngine.UI.Toggle>();
        }

        [Test]
        public void PuiToggle_Submit_InPointerMode_DoesNotToggle_AndWakes()
        {
            UI.UseGamepadNavigation();
            var tog = BuildToggle();
            bool initial = tog.isOn;
            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            var es = Object.FindAnyObjectByType<EventSystem>();
            tog.OnSubmit(new BaseEventData(es));
            Assert.AreEqual(initial, tog.isOn, "Submit while hidden must not flip isOn");
            Assert.AreEqual(UI.Navigation.NavMode.Directional, UI.Navigation.Mode);
        }

        [Test]
        public void PuiToggle_Submit_InDirectionalMode_TogglesNormally()
        {
            UI.UseGamepadNavigation();
            var tog = BuildToggle();
            bool initial = tog.isOn;
            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            var es = Object.FindAnyObjectByType<EventSystem>();
            tog.OnSubmit(new BaseEventData(es));
            Assert.AreNotEqual(initial, tog.isOn, "Submit while cursor visible toggles normally");
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败（编译失败即红——`TryWakeOnSubmit` 尚不存在）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: 编译错误 `'UI.Navigation' does not contain a definition for 'TryWakeOnSubmit'`（红——证明测试在驱动实现）。

- [ ] **Step 3: 实现唤醒门 + 两个 override**

`Runtime/Application/UI.Navigation.cs`——在 `NoteDirectionalInput()`（约 55 行）之后追加：
```csharp
            /// <summary>
            /// 控件 <c>OnSubmit</c> 的唤醒门：导航启用且当前为 Pointer 模式（焦点光标隐藏）时，
            /// 把这次确认解释为「先唤回光标」而非「点击隐藏焦点」——翻 Directional 让光标重现，
            /// 返回 true 让调用方吞掉本次 Submit。其余情况返回 false（照常点击）。
            /// 见 spec 2026-06-30-nav-hidden-submit-wake。
            /// </summary>
            internal static bool TryWakeOnSubmit()
            {
                if (!IsEnabled) return false;
                if (IsDirectional) return false;
                NoteDirectionalInput();
                return true;
            }
```

`Runtime/Controls/Internal/PuiButton.cs`——顶部 usings 追加：
```csharp
using PromptUGUI.Application;
using UnityEngine.EventSystems;
```
在 `DoStateTransition` override 之后、`SimulateState` 之前追加：
```csharp
        /// <summary>
        /// 鼠标用过后焦点光标隐藏时，第一次 Submit 只唤回光标、不点击（见 nav-hidden-submit-wake）。
        /// 仅拦键盘/手柄确认；鼠标点击走 OnPointerClick 不受影响。
        /// </summary>
        public override void OnSubmit(BaseEventData eventData)
        {
            if (UI.Navigation.TryWakeOnSubmit()) return;
            base.OnSubmit(eventData);
        }
```

`Runtime/Controls/Internal/PuiToggle.cs`——顶部 usings 追加：
```csharp
using PromptUGUI.Application;
using UnityEngine.EventSystems;
```
在 `DoStateTransition` override 之后、`SimulateState` 之前追加：
```csharp
        /// <summary>
        /// 鼠标用过后焦点光标隐藏时，第一次 Submit 只唤回光标、不翻转 isOn（见 nav-hidden-submit-wake）。
        /// 仅拦键盘/手柄确认；鼠标点击走 OnPointerClick 不受影响。盖 Tab 与 Toggle。
        /// </summary>
        public override void OnSubmit(BaseEventData eventData)
        {
            if (UI.Navigation.TryWakeOnSubmit()) return;
            base.OnSubmit(eventData);
        }
```

- [ ] **Step 4: 跑测试确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["SubmitWakeTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: `SubmitWakeTests` 6 个全 PASS。

- [ ] **Step 5: 提交**

```bash
git add Runtime/Application/UI.Navigation.cs Runtime/Controls/Internal/PuiButton.cs Runtime/Controls/Internal/PuiToggle.cs Tests/EditMode/Navigation/SubmitWakeTests.cs
git commit -m "fix(nav): Submit wakes hidden cursor instead of triggering stale focus"
```

---

## Task 2: `NavigationController` 收窄 Submit 触发器 + PlayMode 真实输入集成

**Files:**
- Modify: `Runtime/Application/Navigation/NavigationController.cs:18-34`（删 `buttonSouth` + `enterKey`）
- Test: `Tests/PlayMode/Navigation/SubmitWakePlayTests.cs`（新建）

**Interfaces:**
- Consumes: Task 1 的 `PuiButton.OnSubmit` 唤醒行为；`UI.Navigation.NoteDirectionalInput()`/`NotePointerInput()`/`Mode`；`Btn.OnClick`。
- Produces: 无新公共面。`NavigationController` 不再把 Submit 类输入当方向输入——只有*移动*输入（摇杆/dpad/方向键/Tab）翻 `Directional`。

- [ ] **Step 1: 写失败的 PlayMode 集成测试**

新建 `Tests/PlayMode/Navigation/SubmitWakePlayTests.cs`：
```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PromptUGUI.Tests.PlayMode.Navigation
{
    // 端到端复现用户报告的 bug：键盘选中 Cancel → 鼠标移动隐藏光标（选区仍 Cancel）→
    // 真实回车不应触发 Cancel，而应唤回光标；第二次回车才触发。
    public class SubmitWakePlayTests : UnityEngine.InputSystem.InputTestFixture
    {
        private GameObject _es;

        public override void Setup()
        {
            base.Setup();
            UI.ResetForTests();
            DestroyAllEventSystems();   // 防前序 nav 测试泄漏的 EventSystem 抢 current
            _es = new GameObject("EventSystem", typeof(EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        }

        public override void TearDown()
        {
            UI.ResetForTests();
            DestroyAllEventSystems();
            _es = null;
            base.TearDown();
        }

        private static void DestroyAllEventSystems()
        {
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
        }

        [UnityTest]
        public IEnumerator EnterInPointerMode_WakesCursor_DoesNotTriggerStaleFocus()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = InputSystem.AddDevice<Keyboard>();
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='ok'>OK</Btn><Btn id='cancel'>Cancel</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            yield return null;
            var cancel = screen.Get<Btn>("cancel");
            bool cancelFired = false;
            cancel.OnClick.Subscribe(_ => cancelFired = true).AddTo(screen);

            // 1) 键盘导航到 Cancel（Directional）
            UI.Navigation.NoteDirectionalInput();
            EventSystem.current.SetSelectedGameObject(cancel.GameObject);
            yield return null;

            // 2) 鼠标移动 → Pointer 模式，光标隐藏（选区仍是 Cancel）
            UI.Navigation.NotePointerInput();
            yield return null;
            Assert.AreEqual(UI.Navigation.NavMode.Pointer, UI.Navigation.Mode);

            // 3) 真实回车
            Press(kb.enterKey);
            yield return null; yield return null;
            Release(kb.enterKey);
            yield return null;

            Assert.IsFalse(cancelFired,
                "Enter while cursor hidden must NOT trigger the stale focus (Cancel)");
            Assert.AreEqual(UI.Navigation.NavMode.Directional, UI.Navigation.Mode,
                "first Enter wakes the cursor → Directional");

            // 4) 现在 Directional + 选区 Cancel → 第二次回车正常触发 Cancel
            Press(kb.enterKey);
            yield return null; yield return null;
            Release(kb.enterKey);
            yield return null;
            Assert.IsTrue(cancelFired,
                "second Enter (cursor visible) triggers the focused button");
            UI.ResetForTests();
#else
            yield break;
#endif
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], group_names=["SubmitWakePlayTests"], init_timeout=120000)
mcp__UnityMCP__get_test_job(job_id=..., wait_timeout=90)
```
Expected: FAIL —— `enterKey` 仍在 `NavigationController` 方向输入列表里，按下回车那帧若控制器先于 module 运行则翻 Directional → 门放行 → `cancelFired==true`（在能抓到的执行序下红）。
> 若该 runner 执行序恰为「module 先跑」，此步可能不红（注意点 3）；遇此情况以 Step 3 改动后行为是否符合断言为准，并在 Step 4 确认全绿。

- [ ] **Step 3: 收窄 `NavigationController` 触发器**

`Runtime/Application/Navigation/NavigationController.cs`——把 gamepad 与 keyboard 两段判定改成（删 `buttonSouth`、删 `enterKey`，保留 `buttonEast` 取消语义）：
```csharp
            // Submit-class inputs (gamepad South / keyboard Enter) intentionally do NOT flip the
            // mode here. If they did, the same-frame race with InputSystemUIInputModule's Submit
            // dispatch could let the OnSubmit wake-gate read a stale Directional and act on a hidden
            // focus. Only genuine *movement* establishes Directional; Submit is woken via the gate
            // (PuiButton/PuiToggle.OnSubmit → UI.Navigation.TryWakeOnSubmit). buttonEast (Cancel/Back)
            // stays — it is orthogonal to focus and handled by the modal escape listener.
            var gp = Gamepad.current;
            if (gp != null && (gp.leftStick.ReadValue().sqrMagnitude > 0.25f
                               || gp.dpad.ReadValue().sqrMagnitude > 0.25f
                               || gp.buttonEast.wasPressedThisFrame))
            {
                UI.Navigation.NoteDirectionalInput();
            }
            else
            {
                var kb = Keyboard.current;
                if (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame
                                   || kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame
                                   || kb.tabKey.wasPressedThisFrame))
                {
                    UI.Navigation.NoteDirectionalInput();
                }
```

- [ ] **Step 4: 跑测试确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], group_names=["SubmitWakePlayTests"], init_timeout=120000)
mcp__UnityMCP__get_test_job(job_id=..., wait_timeout=90)
```
Expected: `EnterInPointerMode_WakesCursor_DoesNotTriggerStaleFocus` PASS。
> runner 不稳时先 `read_console(types=["error"])` 看是否多余 EventSystem / 设备未注入；排除环境后再判。

- [ ] **Step 5: 提交**

```bash
git add Runtime/Application/Navigation/NavigationController.cs Tests/PlayMode/Navigation/SubmitWakePlayTests.cs
git commit -m "fix(nav): stop Submit inputs from flipping nav mode (lets wake-gate read true mode)"
```

---

## Task 3: 文档（reference/navigation.md 一句）+ 全套件回归 + lint

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/reference/navigation.md`（模式切换 UX 处补一句）
- （核查：若该文件不存在，则本特性按「纯 runtime 默认行为」豁免 SKILL，跳过文档改动，仅做回归 + lint。）

**Interfaces:** 无代码产出；同步母 spec §14 规划的导航 UX 文档（spec §6 例外条款）。

- [ ] **Step 1: 确认 reference/navigation.md 现状并补一句**

```bash
sed -n '1,40p' .claude/skills/authoring-promptugui-xml/reference/navigation.md   # 找「auto show/hide on input device」模式切换段
```
在描述「手柄/键盘显示焦点与光标、鼠标/触屏隐藏」的那段之后，追加（英文）：
```markdown
When the cursor is hidden (the player last used the mouse), the **first** Submit (gamepad A /
keyboard Enter) only **wakes the cursor back onto the truly-focused control without activating it** —
press Submit again to act. This prevents a confirm from hitting an invisible, stale focus. (Pointer
clicks and Cancel/Back are unaffected.)
```
> 若 `reference/navigation.md` 不存在：本特性是纯 runtime 默认行为，按既有约定豁免 SKILL（spec §6）；跳过本步，直接 Step 2。

- [ ] **Step 2: 全套件回归 + lint**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__get_test_job(job_id=...)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__get_test_job(job_id=...)
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], init_timeout=120000)
mcp__UnityMCP__get_test_job(job_id=..., wait_timeout=120)
```
Expected: EditMode 全绿（含新 `SubmitWakeTests` 6 个）、EditorOnly 全绿、PlayMode 全绿（含新 `SubmitWakePlayTests`）。回归重点：既有 `FocusStateTests` / `NavEnableTests` / `InputFieldNavPlayTests` / `ModalNavRealInputPlayTests` 不受影响。

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx   # exit 0
```

- [ ] **Step 3: 提交**

```bash
git add .claude/skills/authoring-promptugui-xml/reference/navigation.md
git commit -m "docs(nav): document Submit-wakes-hidden-cursor in navigation reference"
```
> 若 Step 1 跳过（文件不存在），本步无文档改动 → 跳过提交。

---

## 自评（spec 覆盖检查）

- **spec §2/§3.1 唤醒门** → Task 1 `TryWakeOnSubmit`（3 分支单测）。✅
- **spec §3.2 两个 override** → Task 1 `PuiButton`/`PuiToggle.OnSubmit`（PuiButton 吞掉 + PuiToggle 双分支单测）。✅
- **spec §3.3 NavigationController 收窄** → Task 2（删 enter/South，保 buttonEast）+ PlayMode 集成。✅
- **spec §3.4 数据流（含开屏首次确认）** → Task 2 集成测试覆盖「首次回车唤醒不触发 + 二次触发」；开屏首次确认是同一码路（Pointer→门→唤醒），无需独立任务。✅
- **spec §4 边界**：鼠标点击不受影响（只 override Submit，注释明示）；buttonEast/Cancel 保留（Task 2 注释 + 保留判定）；InputField 排除（Global Constraints + 只 override Pui*）；空格走门（无需改 controller）。✅
- **spec §5 测试** → Task 1 EditMode 5 类断言 + Task 2 PlayMode 集成；Task 3 全套件回归。✅
- **spec §6 SKILL** → Task 3（reference/navigation.md 一句，带「文件不存在则豁免」分支）。✅
- **类型一致性**：`TryWakeOnSubmit`（无参，`bool`）在 UI.Navigation 定义、在两个 override 与 EditMode 单测中引用一致；`PuiButton`/`PuiToggle.OnSubmit(BaseEventData)` 签名与 uGUI `public virtual` 一致；测试取件 `GetComponent<PuiButton>()` / `GetComponent<UnityEngine.UI.Toggle>()` 与控件实际组件类型一致。✅
- **无占位符**：所有步骤含完整代码与确切命令。✅
