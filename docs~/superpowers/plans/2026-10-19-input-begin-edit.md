# 输入框：代码进入编辑、浮层随编辑结束而关、编辑中的 Esc — Implementation Plan

> **For agentic workers:** 按 Task 顺序执行，步骤用 checkbox（`- [x]`）跟踪。每个 Task Red 先行。
> 设计依据：`docs~/superpowers/specs/2026-10-19-input-begin-edit-design.md`（下称 spec，§ 号都指它）。
> 2026-10-19 与作者对齐（spec §10 的 BE-D1…D8 按推荐）。

**Goal：** `InputField.BeginEdit()` / `IsEditing`；`<Popover closeOnEditEnd>` 与 `PopoverCloseReason.EditEnded`；Esc 链尾的「编辑中的输入框」；样例、文档。

**Architecture：**
- `InputField.BeginEdit` 经两级门的 `ExpectEdit` 与 `PuiInputField.RequestEdit` 走 TMP 的 `ActivateInputField`；`EditRequestPending` 跨到下一次 `LateUpdate`。
- 编辑边沿统一走 `InputField.OnEditEdge(bool)`：维护静态的「编辑中」表、启停输入框自己的 `ModalEscapeListener`，再发 `EditingChanged`。
- `EscapeChain.Decide` 末尾读 `InputField.AnyEditing`；`EscapeKey` 拆出 `StartButton`。
- `Popover` 的逐帧步加 `CheckEditEnd`（spec §5.2 的六步）。

**Tech Stack：** Unity 6 uGUI + TMP，R3，NUnit（EditMode / PlayMode，`InputTestFixture`）。

---

## Task 1：`BeginEdit` / `IsEditing`

**Files:** `Runtime/Controls/InputField.cs`、`Runtime/Controls/Internal/PuiInputField.cs`、`Runtime/Controls/Internal/InputFieldNavGate.cs`；
测试 `Tests/EditMode/Controls/InputFieldBeginEditTests.cs`、`Tests/PlayMode/Controls/InputFieldBeginEditPlayTests.cs`。

- [ ] Red（EditMode）：未激活 / 不可交互 / 只读返回 false 且无待生效请求；可编辑返回 true 且有请求；测试钩子钉着「不在编辑」时改钉成在编辑；方向导航下 `BeginEdit` 后 `SetSelectedGameObject` 不设撤回标记。
- [ ] Red（PlayMode）：提交后仍选中 → `screen.Focus` 不进编辑、`BeginEdit` 进；方向导航下已选中 / 未选中都进；`OnSubmit` 里 `BeginEdit` 后仍在编辑。
- [ ] 实现：`IsEditing` public；`BeginEdit`；`RequestEdit` / `EditRequestPending`（LateUpdate 只清开始前就有的请求，OnDisable 清）；门的 `ExpectEdit`。
- [ ] 刷新、看控制台、跑这两组测试，绿。

## Task 2：Esc 链尾的编辑中输入框

**Files:** `Runtime/Application/EscapeChain.cs`、`Runtime/Application/Modals/ModalEscapeListener.cs`、`Runtime/Application/UI.Escape.cs`、`Runtime/Controls/InputField.cs`；
测试 `Tests/EditMode/Modals/EscapeChainTests.cs`（追加）、`Tests/PlayMode/Controls/InputFieldEscapePlayTests.cs`。

- [ ] Red（EditMode）：普通页面编辑中 Esc / B 记为已处理、Start 不是；没在编辑不算；对话框照旧应答；浮层照旧先关；监听只在编辑时启用。
- [ ] Red（PlayMode 设备）：编辑中按 Esc 这一帧 `EscapeConsumedThisFrame` 为真且编辑结束；Start 不算；B 算。
- [ ] 实现：`EscapeKey.StartButton`；监听按控件分辨；`InputField.OnEditEdge` / 编辑中表 / `AnyEditing` / 监听启停；`Decide` 末尾；`EscapeChain.ResetForTests` 清表；`UI.EscapeConsumedThisFrame` 注释。
- [ ] 跑 `EscapeChainTests`、`PuiInputFieldTests`、`Suggestions*`、新 PlayMode 用例，绿。

## Task 3：`<Popover closeOnEditEnd>`

**Files:** `Runtime/Controls/Popover.cs`、`Runtime/Controls/PopoverCloseReason.cs`；
测试 `Tests/EditMode/Controls/PopoverCloseOnEditEndTests.cs`、`Tests/PlayMode/Controls/PopoverCloseOnEditEndPlayTests.cs`。

- [ ] Red（EditMode）：spec §7 的九条。
- [ ] Red（PlayMode）：`OnSubmit` 里 `BeginEdit` 不关；清掉选中下一帧以 `EditEnded` 关。
- [ ] 实现：属性、`EditEnded`、`Tick` = `Follow` + `CheckEditEnd`，打开时清零。
- [ ] 跑 Popover 全部测试，绿。

## Task 4：样例、文档、收尾

- [ ] 样例：`findPop` 加 `closeOnEditEnd="true"`；`OnOpened` 里 `BeginEdit`；无命中时 `BeginEdit`。
- [ ] XML skill：`controls-popover.md`、主 `SKILL.md` 目录行。C# skill：InputField、Popover、Esc 表、速查表。
- [ ] `dotnet format --verify-no-changes`；UIXmlLint 跑 `Runtime/Resources/` 与 `Samples~/`。
- [ ] 全量回归：EditMode / EditorOnly / PlayMode。
- [ ] spec 补实施记录；提交、推送、开 PR。
