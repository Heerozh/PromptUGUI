# 输入的正确性 Implementation Plan

> **For agentic workers:** 按 Task 顺序执行，步骤用 checkbox（`- [x]`）跟踪。每个 Task Red 先行：先写失败的测试、跑一遍确认红，再实现到绿。
> 设计依据：`docs~/superpowers/specs/2026-11-23-input-correctness-design.md`（下称 spec，§ / B / D 编号都指它）。
> 2026-11-23 与作者对齐：spec §6 的决策全部按推荐（含 D-B4：B10 按拖放 spec 修）。

## Context

玩家的一次输入被解释错了：被系统取消的触摸当成松手（B1）、按住期间该取消的没取消（B2）、什么都没改变的点击被当作改变（B3）、
禁用的控件仍然响应（B6 / B8 / B11），以及几处边角（B4 / B5 / B7 / B9 / B10）。另有三条只补文档（#4b / #9d / #10）。

**Architecture：**
- **B1**：`LiftGesture` 在结束手势前问 `IHost` 之外的一个判定 —— 这次结束是不是系统取消（Input System：`ExtendedPointerEventData`
  的触点 `phase == Canceled`；旧输入：`Input.touches`），是就走新的 `IHost.Cancel()`；测试缝 `CanceledProbeForTests`。
- **B2**：`DragSession` 登记按住中的 `DragSource`；`BeforeStructureChange` / `OnLayerAbove` / ReSolve 取消信号覆盖它们；`DragSource`
  按住期间每帧自检；`Begin` 兜底拒绝不可交互的源。
- **B9 / B10**：`Park` 不撤被停靠目标的武装；`DismissUnder` 带原因，源所在 Screen 关闭时结束等待。
- **B3**：`PuiToggle` 与 `Tab.IsOn` / `Toggle.IsOn` 记下操作前的值，`Tab` / `Toggle` 的监听丢弃没变的回调；TabMenu 的重点收起走
  `OnActivated`，删掉 `InSfxRepick`。
- **B6 / B7**：TabMenu 的两个守卫。**B4 / B11**：`Dropdown.SetOptions` 以背板为准；`PuiDropdown` 只认左键。
- **B8**：`CarouselView.OnBeginDrag` 不可交互时整个手势转交外层。
- **B5**：`TutorialOverlayView.EndStep` 放开输入、暗幕推迟到下一次 `Tick`；`TutorialFlow.Navigate` 期间透明挡输入。

**Tech Stack：** Unity 6 / C# 9；uGUI / TMP；Input System（`InputTestFixture`）；R3。

---

## Task 0：spec 与 plan

- [x] spec 状态改为「决策已对齐」；本 plan 落盘；提交。

## Task 1（B1）：被系统取消的触摸

**Files:** `Runtime/Controls/Internal/LiftGesture.cs`、`DragSource.cs`、`ReorderDriver.cs`；新 `Tests/PlayMode/Controls/InputCorrectnessPlayTests.cs`；
EditMode 拖放 / 重排测试。

- [x] Red（PlayMode）：触屏拖到目标上 → `SetTouch(Canceled)` → `OnEnd(Cancelled)`、没有 `OnDrop`、没有落地音效；正常 `EndTouch` 仍是
  `Dropped`；重排版：顺序不变、没有 `OnReordered`。
- [x] Red（EditMode）：测试缝强制「系统取消」，Dragging 与 Armed 两个分支。
- [x] Green；回归拖放 / 重排；提交。

## Task 2（B2 / B9 / B10）：按住期间的取消；停靠与等确认

**Files:** `DragSession.cs`、`DragSource.cs`、`PopupChain.cs`（`DismissUnder` 的原因）、`Screen.cs`（EditMode 的关闭路径）；EditMode 拖放测试。

- [x] Red：按住期间被禁用 / 推送 / 开模态 / `UI.Close` / ReSolve → 不拿起、没有 `OnLift` / `OnEnd`；鼠标 `hold = 0` 版；停靠后目标仍
  armed；等确认中关闭源所在 Screen → 立即 `Cancelled`。
- [x] Green；回归；提交。

## Task 3（B3 / B6 / B7）：重点不发事件；TabMenu

**Files:** `PuiToggle.cs`、`Tab.cs`、`Toggle.cs`、`TabMenu.cs`；EditMode Tab / Toggle / TabMenu 测试；`SfxPlayTests`（日志改断言）。

- [x] Red：重点已选中的 Tab / 已开的 radio → 各事件与 `<Trigger on="checked">` 0 次；代码 `IsOn = false` 写给组里唯一开着的 → 不发；
  `allowSwitchOff` 点掉照发；TabMenu 重点当前行仍收起、仍发 `collapse`；点禁用行不收起；`Open` 被拒不发 `OnExpanded`。
- [x] Green（删 `InSfxRepick` 与 `OnSelectionSettled` 的重点分支）；回归 sfx 全部；提交。

## Task 4（B4 / B11）：下拉

**Files:** `Dropdown.cs`、`PuiDropdown.cs`；`InputCorrectnessPlayTests.cs`。

- [x] Red（PlayMode）：背板关闭后淡出期间推送 → 0.4 s 后仍关着；右键 → 不展开。
- [x] Green；回归 Dropdown；提交。

## Task 5（B8）：禁用的 Carousel

**Files:** `CarouselView.cs`、共用的可交互判定；EditMode Carousel 拖动测试。

- [x] Red：禁用自身 / 祖先 → 拖动不翻页、没有 `flip`、外层收到转发；自动播放照转。
- [x] Green；回归；提交。

## Task 6（B5）：引导的步与步之间

**Files:** `TutorialOverlayView.cs`、`TutorialFlow.cs`；EditMode 引导测试。

- [ ] Red：Block 步完成后遮罩不接射线；下一次 `Tick` 后暗幕隐藏；同一帧开始的下一步暗幕不隐藏；`Navigate` 期间挡输入、之后放开。
- [ ] Green；回归；提交。

## Task 7：文档与收尾

- [ ] XML skill（drag-drop / reorder / controls-tabs / controls-carousel / navigation / sfx 与主 SKILL 的相应句子）；C# skill（事件、
  `OnEnd(Cancelled)` 原因、Tutorial、旧输入下的 Esc）；sfx spec 三句；spec 实施记录。
- [ ] 全量回归：EditMode / EditorOnly / PlayMode；`dotnet format --verify-no-changes`；UIXmlLint。
- [ ] 推送，开 PR（base `feat/ui-sfx`），写明公开行为变化。
