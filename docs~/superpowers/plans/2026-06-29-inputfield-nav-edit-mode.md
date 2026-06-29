# InputField 方向导航两级编辑模式 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 `<InputField>` 在手柄/键盘方向导航下走"两级模型"——导航选中只聚焦不进编辑（方向键可移上/移出），按 Submit 才进编辑，Esc/Cancel 退出。

**Architecture:** 在 InputField 根 GO 上挂一个伴随组件 `InputFieldNavGate`（`ISelectHandler`/`ISubmitHandler`/`IDeselectHandler`）。导航选中（`UI.Navigation.IsEnabled && IsDirectional`）时把 TMP 的自动激活在下一 tick 撤销（`DeactivateInputField`，避开同帧 `m_AllowInput=false` 的竞态）；Submit 在未编辑时 `ActivateInputField`。指针选中（Pointer 模式）保持默认立即编辑。纯指针项目门控关闭、完全 inert。

**Tech Stack:** Unity 6, TMPro `TMP_InputField`, uGUI EventSystem (`ISelectHandler`/`ISubmitHandler`/`IDeselectHandler`), New Input System（仅设备检测在主特性里，本计划不碰 InputSystem 类型）, R3, NUnit + Unity Test Framework（PlayMode 主测 + EditMode 纯逻辑）。

设计源：`docs~/superpowers/specs/2026-06-29-inputfield-nav-edit-mode-design.md`。
承接主特性：`docs~/superpowers/specs/2026-06-29-gamepad-keyboard-navigation-design.md`。

## Global Constraints

- **严格附加**：不改 `<InputField>` 既有公共属性 / XML 语义；不新增 XML 属性（v1 行为自动）。
- **门控**：所有新行为仅当 `UI.Navigation.IsEnabled` 为真时生效；Pointer 模式选中保持默认（鼠标点 = 立即编辑）。
- **WebGL 安全**：同步逻辑，禁止 `System.Threading.Task` / `TaskCompletionSource`（本计划无异步）。
- **不需 `#if ENABLE_INPUT_SYSTEM`**：只用 EventSystem/Selectable/TMP，不引用 InputSystem 类型。门控走 `UI.Navigation.IsEnabled` / `IsDirectional`（已是 `internal`，InputField 在 Runtime 同程序集可见）。
- **Variant 不重建 GameObject**：gate 是 OnAttached 时一次性 AddComponent，不在 ReSolve 中增删。
- **测试经 Unity MCP**：改源后 `refresh_unity(compile=request, mode=force)` → `read_console(types=[error])` 零编译错误 → `run_tests` 轮询 `get_test_job`。PlayMode `init_timeout=120000`。
- **lint**：`dotnet format --verify-no-changes --severity warn .lint/PromptUGUI.Lint.slnx` exit 0；C# LangVersion 9（无 C#10+ 语法）。
- **SKILL 同步**：本特性改变 InputField 在导航下的行为 → 必须在同 PR 更新 C# SKILL + `reference/navigation.md`（英文）。

### ⚠️ TMP 内部经验性风险点（实现期用 PlayMode 实测校正，测试断言是判据）

1. **同帧 activate→deactivate 竞态**：TMP `OnSelect` 调 `ActivateInputField()` 只置 `m_ShouldActivateNextUpdate`，`m_AllowInput` 当帧仍 false；此时同步 `DeactivateInputField()` 会因 `m_AllowInput==false` 提前返回、**不能**取消激活。故撤销必须**延后到激活已生效之后**（gate 用一个 suppress 标志 + `LateUpdate`/`Update` tick，在 `_input.isFocused` 变 true 后再 `DeactivateInputField`）。可能有 1 帧 caret（渲染在 LateUpdate 后，通常不可见）——PlayMode 测试确认无残留编辑态即可。
2. **OnSubmit 在未聚焦时是否误发 submit 事件**：进编辑用的 Enter 不应触发 `OnSubmit` 业务事件。实测 TMP 未聚焦时 `OnSubmit` 行为；若会误发，gate 在"未编辑→激活"分支里需吞掉/不转发（Task 2 断言 `OnSubmit` 流在进编辑时不发）。
3. **Esc/Cancel 退出**：TMP 默认 `ICancelHandler`/Escape 已会 `DeactivateInputField`。Task 3 仅**断言**该默认成立；若某版本不成立再补 gate 的 `OnCancel`。

---

## Task 1: InputFieldNavGate — 导航选中抑制自动编辑

**Files:**
- Create: `Runtime/Controls/Internal/InputFieldNavGate.cs`
- Modify: `Runtime/Controls/InputField.cs`（OnAttached 末尾 AddComponent + 暴露 `internal bool IsEditing`）
- Test: `Tests/PlayMode/Navigation/InputFieldNavPlayTests.cs`

**Interfaces:**
- Consumes: `UI.Navigation.IsEnabled`（public）、`UI.Navigation.IsDirectional`（internal，同程序集可见）、`TMP_InputField.isFocused` / `ActivateInputField()` / `DeactivateInputField()`。
- Produces:
  - `InputFieldNavGate : MonoBehaviour, ISelectHandler, ISubmitHandler, IDeselectHandler`（internal），字段 `TMP_InputField _input`，方法 `internal void Init(TMP_InputField input)`。
  - `InputField.IsEditing`（`internal bool` → `_input != null && _input.isFocused`），供测试与后续任务断言编辑态。

- [ ] **Step 1: 写失败的 PlayMode 测试**

`Tests/PlayMode/Navigation/InputFieldNavPlayTests.cs`：
```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Navigation
{
    public class InputFieldNavPlayTests
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

        [UnityTest]
        public IEnumerator DirectionalSelect_DoesNotEnterEditMode()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();              // Mode = Directional
            var s = Open("<InputField id='f'/><Btn id='b'>B</Btn>");
            var field = s.Get<InputField>("f");
            var es = EventSystem.current;

            es.SetSelectedGameObject(field.GameObject);        // 模拟导航选中
            yield return null;                                  // 让 TMP 处理 activate-then-suppress
            yield return null;

            Assert.IsFalse(field.IsEditing,
                "directional-select must NOT activate edit mode (two-level nav)");
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])      # 须先有 InputField.IsEditing 才能编译——见 Step 3 先补桩
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], init_timeout=120000)
mcp__UnityMCP__get_test_job(job_id=..., wait_timeout=90)
```
Expected: `DirectionalSelect_DoesNotEnterEditMode` FAIL（默认 TMP 桌面会自动编辑 → `IsEditing==true`）。
（注：为能编译，Step 3 先加 `InputField.IsEditing` 桩 + 测试引用；红是因为行为未实现，不是编译失败。）

- [ ] **Step 3: 实现 gate + 接入 InputField**

`Runtime/Controls/Internal/InputFieldNavGate.cs`：
```csharp
using PromptUGUI.Application;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// 两级方向导航门：导航（Directional）选中 TMP_InputField 时只聚焦不进编辑，
    /// 让方向键经 Selectable.OnMove 正常导航；Submit 才进编辑。指针选中（Pointer 模式）
    /// 保持 TMP 默认立即编辑。仅当 UI.Navigation.IsEnabled 时生效。
    /// </summary>
    internal sealed class InputFieldNavGate : MonoBehaviour,
        ISelectHandler, ISubmitHandler, IDeselectHandler
    {
        private TMP_InputField _input;
        private bool _suppressUntilDeactivated;

        internal void Init(TMP_InputField input) => _input = input;

        public void OnSelect(BaseEventData eventData)
        {
            // 导航选中：标记下一 tick 撤销 TMP 的自动激活（同帧撤销有 m_AllowInput 竞态，见计划风险点 1）。
            if (UI.Navigation.IsEnabled && UI.Navigation.IsDirectional)
                _suppressUntilDeactivated = true;
        }

        // TMP 的激活延后到 LateUpdate 才令 isFocused=true；本组件 AddComponent 晚于 TMP，
        // LateUpdate 在其后运行 → 此刻 isFocused 已 true，可安全 DeactivateInputField。
        private void LateUpdate()
        {
            if (!_suppressUntilDeactivated) return;
            if (_input != null && _input.isFocused)
            {
                _input.DeactivateInputField();
                _suppressUntilDeactivated = false;
            }
            // Pointer 模式被点中（用户改用鼠标）→ 放弃抑制，保留编辑。
            else if (!UI.Navigation.IsDirectional)
            {
                _suppressUntilDeactivated = false;
            }
        }

        public void OnSubmit(BaseEventData eventData)
        {
            // Task 2 实现：未编辑 + 导航启用 → 进编辑。此处先留空，Task 1 只管抑制。
        }

        public void OnDeselect(BaseEventData eventData) => _suppressUntilDeactivated = false;
    }
}
```

`Runtime/Controls/InputField.cs`：OnAttached 末尾（`PromptUGUI.Application.UI.Locale.Changed += ApplyFont;` 之后）追加：
```csharp
            var navGate = GameObject.AddComponent<Internal.InputFieldNavGate>();
            navGate.Init(_input);
```
并新增（放在 `TextValue` 属性附近）：
```csharp
        // True when the underlying field is in edit mode (caret active). Used by the
        // two-level navigation gate and its tests.
        internal bool IsEditing => _input != null && _input.isFocused;
```

- [ ] **Step 4: 跑测试确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], init_timeout=120000)
mcp__UnityMCP__get_test_job(job_id=..., wait_timeout=90)
```
Expected: `DirectionalSelect_DoesNotEnterEditMode` PASS。
> 实测风险点 1：若 1 帧未撤销，加多一帧 yield 或把撤销逻辑也放进 `Update`（双保险）。撤销动作以"`isFocused` 由 true 变 false"为准。

- [ ] **Step 5: 提交**

```bash
git add Runtime/Controls/Internal/InputFieldNavGate.cs Runtime/Controls/InputField.cs Tests/PlayMode/Navigation/InputFieldNavPlayTests.cs
git commit -m "feat(nav): InputField two-level — directional-select doesn't enter edit mode"
```

---

## Task 2: Submit 进编辑（且不误发 submit 事件）

**Files:**
- Modify: `Runtime/Controls/Internal/InputFieldNavGate.cs:OnSubmit`
- Test: `Tests/PlayMode/Navigation/InputFieldNavPlayTests.cs`（新增 2 个 `[UnityTest]`）

**Interfaces:**
- Consumes: Task 1 的 `InputFieldNavGate` + `InputField.IsEditing` + `InputField.OnSubmit`（已存在 `Observable<string>`）。
- Produces: 无新公共面；`OnSubmit` 行为：未编辑 + 导航启用 → `ActivateInputField()`。

- [ ] **Step 1: 写失败测试**

追加到 `InputFieldNavPlayTests`：
```csharp
        [UnityTest]
        public IEnumerator Submit_OnFocusedNotEditingField_EntersEditMode()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();
            var s = Open("<InputField id='f'/>");
            var field = s.Get<InputField>("f");
            var es = EventSystem.current;
            es.SetSelectedGameObject(field.GameObject);
            yield return null; yield return null;
            Assert.IsFalse(field.IsEditing, "precondition: selected-not-editing");

            ExecuteEvents.Execute(field.GameObject, new BaseEventData(es), ExecuteEvents.submitHandler);
            yield return null; yield return null;

            Assert.IsTrue(field.IsEditing, "Submit must enter edit mode");
        }

        [UnityTest]
        public IEnumerator Submit_ToEnterEdit_DoesNotFireSubmitEvent()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();
            var s = Open("<InputField id='f'/>");
            var field = s.Get<InputField>("f");
            bool fired = false;
            field.OnSubmit.Subscribe(_ => fired = true).AddTo(s);
            var es = EventSystem.current;
            es.SetSelectedGameObject(field.GameObject);
            yield return null; yield return null;

            ExecuteEvents.Execute(field.GameObject, new BaseEventData(es), ExecuteEvents.submitHandler);
            yield return null;

            Assert.IsFalse(fired, "entering edit via Submit must not fire the OnSubmit business event");
        }
```
（顶部按需 `using R3;`。）

- [ ] **Step 2: 跑确认失败**（命令同 Task 1 Step 2）。Expected: 两个新测试 FAIL（OnSubmit 仍空，不进编辑）。

- [ ] **Step 3: 实现 OnSubmit**

`InputFieldNavGate.OnSubmit` 改为：
```csharp
        public void OnSubmit(BaseEventData eventData)
        {
            if (!UI.Navigation.IsEnabled) return;
            if (_input == null || !_input.IsInteractable()) return;
            if (!_input.isFocused)
            {
                _input.ActivateInputField();   // 进编辑级
                _suppressUntilDeactivated = false;
            }
            // 已在编辑：交给 TMP 默认 OnSubmit（确认/换行），本 gate 不插手。
        }
```
> 实测风险点 2：若 TMP 未聚焦时其 `OnSubmit` 仍会触发业务 `onSubmit`（令 `DoesNotFireSubmitEvent` 失败），则在此用一个一次性标志吞掉那一次 `_submit` 转发——但优先实测确认 TMP 是否真的会发；多数版本未聚焦不发。

- [ ] **Step 4: 跑确认通过**（命令同上）。Expected: 两测试 PASS。

- [ ] **Step 5: 提交**

```bash
git add Runtime/Controls/Internal/InputFieldNavGate.cs Tests/PlayMode/Navigation/InputFieldNavPlayTests.cs
git commit -m "feat(nav): InputField two-level — Submit enters edit mode (no spurious submit)"
```

---

## Task 3: 回归——指针仍立即编辑 / 导航未启用不变 / Esc 退出

**Files:**
- Test: `Tests/PlayMode/Navigation/InputFieldNavPlayTests.cs`（新增 3 个 `[UnityTest]`）
- （预期无源改动；若某断言因 TMP 版本不成立，最小补 `InputFieldNavGate`。）

**Interfaces:** Consumes Task 1/2 成果；无新产出。

- [ ] **Step 1: 写测试**

```csharp
        [UnityTest]
        public IEnumerator PointerSelect_StillEntersEditImmediately()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NotePointerInput();                  // Mode = Pointer（鼠标）
            var s = Open("<InputField id='f'/>");
            var field = s.Get<InputField>("f");
            var es = EventSystem.current;
            // 指针点击路径：直接走 TMP 的 OnPointerClick → 默认激活
            ExecuteEvents.Execute(field.GameObject, new PointerEventData(es), ExecuteEvents.pointerClickHandler);
            yield return null; yield return null;
            Assert.IsTrue(field.IsEditing, "pointer click must enter edit immediately (mouse UX unchanged)");
        }

        [UnityTest]
        public IEnumerator NavDisabled_DefaultBehaviorUnchanged()
        {
            // 不调 UseGamepadNavigation：gate 仍挂着但 IsEnabled=false → 不抑制
            var s = Open("<InputField id='f'/>");
            var field = s.Get<InputField>("f");
            // 无 EventSystem 时建一个，模拟选中
            if (EventSystem.current == null)
                new GameObject("ES", typeof(EventSystem), typeof(StandaloneInputModule));
            EventSystem.current.SetSelectedGameObject(field.GameObject);
            yield return null; yield return null;
            // 桌面默认：选中即编辑；gate 因 IsEnabled=false 不撤销
            Assert.IsTrue(field.IsEditing, "with nav disabled, default TMP behavior must be untouched");
        }

        [UnityTest]
        public IEnumerator Cancel_ExitsEditMode()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();
            var s = Open("<InputField id='f'/>");
            var field = s.Get<InputField>("f");
            var es = EventSystem.current;
            es.SetSelectedGameObject(field.GameObject);
            yield return null; yield return null;
            ExecuteEvents.Execute(field.GameObject, new BaseEventData(es), ExecuteEvents.submitHandler);  // 进编辑
            yield return null; yield return null;
            Assert.IsTrue(field.IsEditing, "precondition: editing");

            ExecuteEvents.Execute(field.GameObject, new BaseEventData(es), ExecuteEvents.cancelHandler);  // Esc/B
            yield return null; yield return null;
            Assert.IsFalse(field.IsEditing, "Cancel/Esc must exit edit mode back to navigation");
        }
```

- [ ] **Step 2: 跑确认**（命令同上）。Expected: 三测试结果——`PointerSelect` / `NavDisabled` 应直接 PASS（验证门控正确）；`Cancel_ExitsEditMode` 验证 TMP 默认 Cancel→退出（风险点 3）。若 `Cancel` FAIL，进 Step 3。

- [ ] **Step 3: （仅当 Cancel 失败）补 gate.OnCancel**

让 gate 实现 `ICancelHandler`：
```csharp
        public void OnCancel(BaseEventData eventData)
        {
            if (_input != null && _input.isFocused) _input.DeactivateInputField();
        }
```
（类签名加 `, ICancelHandler`。）否则本步无源改动。

- [ ] **Step 4: 跑确认全绿**（命令同上）。

- [ ] **Step 5: 提交**

```bash
git add Tests/PlayMode/Navigation/InputFieldNavPlayTests.cs Runtime/Controls/Internal/InputFieldNavGate.cs
git commit -m "test(nav): InputField two-level regressions — pointer/disabled/cancel"
```

---

## Task 4: SKILL 文档（英文）

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`（导航小节）
- Modify: `.claude/skills/authoring-promptugui-xml/reference/navigation.md`（InputField 行为说明）

**Interfaces:** 无代码；文档同步本特性行为（CLAUDE.md SKILL-sync 硬规矩）。

- [ ] **Step 1: C# SKILL 导航小节追加**

在 `scripting-promptugui-csharp/SKILL.md` 的 Gamepad/Keyboard Navigation 小节追加一段：
```markdown
**InputField uses a two-level model under directional navigation.** When `UI.UseGamepadNavigation()`
is active, navigating (d-pad / arrows) onto a text field **focuses** it without entering edit mode, so
directional input keeps moving between controls. Press **Submit** (gamepad A / keyboard Enter) on the
focused field to **enter edit mode** (caret active); press **Cancel** (gamepad B / keyboard Esc) — or
Enter on a single-line field — to confirm and return to navigation. A **pointer click** still enters
edit immediately (mouse UX unchanged). No markup is required; the behavior is automatic when navigation
is enabled.
```

- [ ] **Step 2: navigation.md 补 InputField 说明**

在 `reference/navigation.md` 合适处（焦点视觉 / selectable-tag 列表附近）加：
```markdown
### Text fields (two-level edit)

A `<InputField>` participates in directional navigation like any selectable, but with a two-level model:
directional input **navigates onto and off** a focused field without typing into it; press **Submit**
(A / Enter) to **enter edit mode**, and **Cancel** (B / Esc), or Enter on a single-line field, to leave it.
This mirrors console UIs and keeps arrow keys from getting trapped in the field. Pointer click still edits
immediately. (Active only when `UI.UseGamepadNavigation()` is enabled.)
```

- [ ] **Step 3: 跑 lint + 整套件终检**

```bash
dotnet format --verify-no-changes --severity warn .lint/PromptUGUI.Lint.slnx   # exit 0
```
```
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], init_timeout=120000)   # 全绿（含新 InputFieldNavPlayTests）
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])                        # 回归全绿
```

- [ ] **Step 4: 提交**

```bash
git add .claude/skills/scripting-promptugui-csharp/SKILL.md .claude/skills/authoring-promptugui-xml/reference/navigation.md
git commit -m "docs(nav): document InputField two-level edit mode in skills"
```

---

## 自评（spec 覆盖检查）

- spec §2 两级模型 → Task 1（抑制自动编辑）+ Task 2（Submit 进编辑）+ Task 3（Cancel 退出）。✅
- spec §3 门控（仅启用时 / 指针仍立即编辑 / 全平台一致）→ Task 3 `PointerSelect` + `NavDisabled` 回归。✅
- spec §5 边界（disabled 不参与）→ gate `OnSubmit` 检 `IsInteractable`；disabled 字段 TMP 本就不可选。✅
- spec §4 单行 Up/Down 退出"可选增强" → **明确移出 v1**（与 TMP 抢键、收益小），留作后续；spec §4/§6 已标"可选"。计划不含。
- spec §6 机制 A/B → 选 A（伴随组件，无 Selectable 内部复刻脆弱性）；风险点 1-3 已在 Global Constraints 下标注、由 PlayMode 测试敲定。✅
- spec §9 SKILL → Task 4。✅
- 类型一致性：`InputFieldNavGate`（Init/OnSelect/OnSubmit/OnDeselect[/OnCancel]）、`InputField.IsEditing` 在各任务引用一致。✅
