# UI 音效钩子 `sfx=` / `UI.Sfx.OnCue` Implementation Plan

> **For agentic workers:** 按 Task 顺序执行，步骤用 checkbox（`- [x]`）跟踪。每个 Task Red 先行：先写失败的测试、跑一遍确认红，再实现到绿。
> 设计依据：`docs~/superpowers/specs/2026-11-23-ui-sfx-cues-design.md`（下称 spec，§ / P / SFX-D 编号都指它）。
> 2026-11-23 与作者对齐：spec §13 的 SFX-D1…D18 全部按推荐；同日独立审查的修订已并入 spec（§17）。

## Context

给 UI 配音效：控件在**用户真实操作**时发一个全局事件（种类、动作、键、来源元素），库不播放。默认键 `kind.action`，XML `sfx=`
覆盖 / 静音，可经样式、模板、主题、变体继承；代码赋值、ReSolve、初始状态、开屏回放、拆除都不发。踩坑表见 spec §2。

**Goal：**
- 一个通用属性 `sfx`（含 `<Screen sfx>`）与它的迷你语法（spec §5.1）；
- 动作表与查找链（spec §5.2、§5.3）；
- C#：`UI.Sfx.OnCue : Observable<SfxCue>`、`SfxCue`、`Control.RaiseSfxCue`；
- 输入入口挂点：`PuiButton` / `PuiToggle` / `PuiDropdown`、弹层、Carousel、拖放、重排；Screen / Modal 的角色与开关；
- lint 三条（`PUI-SFX-VALUE` / `-ACTION` / `-INERT`）、XSD、自愈、`<Param name="sfx">` 报错；
- 顺带修 P26（调用处写的通用属性被主题样式回写盖掉）。

**Architecture：**
- **纯 C#（`Core/`，CLI 也编译）**：
  - `Core/Lint/SfxRules.cs`：`SfxSpec`（裸键 + `动作 → 键` 表 + 空 / 静音判断）、`TryParse(value, out spec, out error)`、内置
    词表、标签 → 自己的动作 / 被查的动作、`Resolve(chain, action)` 的纯函数版本、三条规则。运行时只用它的解析与查找。
  - `ElementNode.InvocationAttrNames`：调用处写到实例根上的通用属性名；展开期逐层复制。
  - `CommonAttributes.All` 加 `sfx`：`[UIAttr]` 循环跳过它，lint 自愈集自动包含它。
- **运行时**：
  - `Control`：`internal SfxSpec Sfx`（每次属性应用写入，空 = 没写）、`internal virtual string SfxKind`（内置控件 override；
    默认 = 注册标签小写）、`internal virtual Control SfxOwner`（查找链的上一级：Tab → 组、重排行 → 列表）、
    `protected void RaiseSfxCue(string action)`、发送门（所属 Screen 正在关闭 → 不发）、一次激活至多一个 cue 的锁存。
  - `UI.Sfx`（`Application/UI.Sfx.cs`）：静态 `Subject<SfxCue>`，`SubsystemRegistration` 与 `ResetForTests` 重建；内部
    `Raise(...)` 包 `try/catch` + `Debug.LogException`；`internal` 测试缝 `Recorded`（可选）。
  - `SfxCue`（`Application/SfxCue.cs`）：只读结构体。
  - `Pui*` 报告：`PuiButton` / `PuiToggle` / `PuiDropdown` 在 `OnPointerClick` / `OnSubmit` 覆写里向一个可选的
    `ISfxActivationSink`（所属控件实现）报告 `BeginActivation(interactableBefore)` / `EndActivation()` 与 `Deny()`；
    没有 sink 的内部按钮什么都不发（指示点、卡片由宿主自己发）。
  - Screen：`Screen.SfxRole`（`None` / `Screen` / `Modal`）+ `SfxOpened` 标志；打开方设置角色并调用 `Screen.RaiseOpenCue()`；
    `Screen.Close()` 过 guard 后、`OnClosing` 前若 `SfxOpened` 则发 `close`。
- **测试缝**：EditMode 用 `ExecuteEvents.Execute(go, new PointerEventData(es) { button = Left }, pointerClickHandler)` 模拟真实
  点击（仓库已有先例），`OnSubmit` 直接调；cue 用 `UI.Sfx.OnCue.Subscribe(list.Add)` 收集。

**Tech Stack：** Unity 6 / C# 9；uGUI / TMP；R3；LitMotion（不涉及）。

---

## Task 0：spec 与 plan

- [ ] spec 状态已是「决策已对齐 + 审查已过」；本 plan 落盘；在 `feat/ui-sfx` 上提交 spec + plan。

## Task 1（M1，纯 C#）：语法、解析、模板、lint、XSD

**Files:** 新 `Runtime/Core/Lint/SfxRules.cs`；`Runtime/Core/IR/CommonAttributes.cs`、`ElementNode.cs`；
`Runtime/Core/Parser/UIDocumentParser.cs`；`Runtime/Core/Template/TemplateExpander.cs`；`Runtime/Core/Lint/VariantBaseRules.cs`、
`IRWalker.cs`、`StyleRules.cs`、`ThemeStyleRules.cs`；`Editor/XsdGenerator.cs`。

- [ ] Red：`SfxRulesTests`（语法）—— 裸键 / `none` / `动作:键` / 混写 / 逗号与空白分隔 / 空值 = 没写 / 两个裸键、重复动作、
  空键、空动作 → 错误；`Resolve`：动作项先于裸键、`deny` 不吃裸键与 `none`、链上逐个元素、链走完 = 默认键。
- [ ] Red：解析 —— `<Screen sfx="…">` 与 `sfx.<variant>` 进合成根；`<Screen sfx="open:">` 抛；`<Param name="sfx">` 抛
  `ParseException`（消息建议改名）。
- [ ] Red：模板 —— 调用处 `sfx` 落实例根、覆盖模板体根的值；调用处 `class=` 带的 `sfx` 落根；`InvocationAttrNames` 记下
  调用处写的名字；**P26**：模板体根 `class=` 贡献 `sfx` / `width`、调用处覆写，`StyleMerger.ReMerge` 之后调用处的值仍在。
- [ ] Red：lint —— `VALUE`（行内、`<Style>`、主题 `<Style>`、`<Screen>`）；`ACTION`（`<Btn sfx="checked:x">` 报、拼错报、样式贡献
  不报、模板体根不报、`lift` / `land` / `return` 不报）；`INERT` 三种情形与全部豁免（spec §8）；`PUI-VARIANT-NO-BASE` 不报
  `sfx.mobile`；`PUI-THEME-STYLE-SHAPE` 豁免 `sfx`；`InvocationMergeableMirror` 漂移测试照过；XSD 含 `sfx`（common 组与
  `<Screen>`）。
- [ ] Green：实现。`dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` 与 `Samples~/` 干净。
- [ ] 刷新编译、跑 `PromptUGUI.Tests.EditMode` 全量 + `PromptUGUI.Tests.EditorOnly`；`dotnet format` 干净；提交。

## Task 2（M2）：门面、应用、Btn / Toggle / Tab 与拒绝

**Files:** 新 `Runtime/Application/UI.Sfx.cs`、`SfxCue.cs`；`UI.cs`（`ResetForTests`）；`ControlAttributeApplier.cs`；`Control.cs`；
新 `Runtime/Controls/Internal/ISfxActivationSink.cs`；`PuiButton.cs`、`PuiToggle.cs`；`Btn.cs`、`Toggle.cs`、`Tab.cs`、`TabBar.cs`、
`TabMenu.cs`（只作为 Tab 的上一级）。

- [ ] Red：`SfxFacadeTests` —— `ResetForTests` 重建流；抛异常的订阅者不妨碍 `Btn.OnClick`；`RaiseSfxCue` 用注册标签小写作 kind。
- [ ] Red：`SfxApplyTests` —— 行内 / `class=` / 模板体 / 调用处 / `sfx.mobile` 无基础值退出变体回默认 / 切主题回新主题的值 /
  语法错只警告一次。
- [ ] Red：`SfxBtnTests` —— spec §11「Btn」全部；`TextLinkClicker` 转发。
- [ ] Red：`SfxToggleTests`、`SfxTabTests` —— spec §11「Toggle」「Tab」全部；deny（Toggle / Tab）；`OnValueChanged` 订阅者改回去只
  一个 cue；Tab 查 TabBar / TabMenu 的 `sfx`。
- [ ] Green。
- [ ] 回归 + `dotnet format`；提交。

## Task 3（M3）：弹层 —— TabMenu、Popover、Collapsible、Dropdown 挂点

**Files:** `TabMenu.cs`、`Popover.cs`、`Collapsible.cs`、`Dropdown.cs`、`PuiDropdown.cs`；`PopupChain.cs`、`PopupCore.cs`、
`EscapeChain.cs`（把「被手势直接指向的那一层」与原因带到宿主）。

- [ ] Red：`SfxTabMenuTests`、`SfxPopoverTests`、`SfxCollapsibleTests` —— spec §11「TabMenu / Popover / Collapsible」全部；deny
  （TabMenu 把手 / Collapsible 头部）。
- [ ] Green（Dropdown 的挂点一并写，测试在 Task 7 的 PlayMode）。
- [ ] 回归 + `dotnet format`；提交。

## Task 4（M4）：Carousel 与 CenteredSlideBox

**Files:** `Carousel.cs`、`Internal/CarouselView.cs`、`Modals/CenteredSlideBoxRequest.cs`。

- [ ] Red：`SfxCarouselTests` —— spec §11「Carousel」全部。
- [ ] Green；回归；提交。

## Task 5（M5）：Screen / Modal

**Files:** `Screen.cs`、`UI.cs`、`UI.Modal.cs`、`UI.Router.Reconcile.cs`、`UI.Router.Layering.cs`、`Modals/LoadingOverlay.cs`、
`Toasts/ToastOverlay.cs`、`UI.Tutorial.cs`、`PromptUGUIDocumentHost.cs`。

- [ ] Red：`SfxScreenTests` —— spec §11「Screen / Modal」全部。
- [ ] Green；回归；提交。

## Task 6（M6）：拖放与重排

**Files:** `Internal/DragSession.cs`、`Internal/ReorderDriver.cs`、`ScrollList.cs`、`Control.cs`（`drop=` 目标进查找链）。

- [ ] Red：`SfxDragTests`、`SfxReorderTests` —— spec §11「拖放」「重排」全部。
- [ ] Green；回归；提交。

## Task 7（M7）：PlayMode

**Files:** 新 `Tests/PlayMode/Controls/SfxPlayTests.cs`（真 `EventSystem` + Input System 的 `InputTestFixture`，沿用
`DropdownEscapePlayTests` 的做法）。

- [ ] spec §11 PlayMode 段全部（含 Dropdown 全套、关闭中 Screen 上的重排、`dismiss="pass"`、真实点击已选中的 Tab）。
- [ ] 若「真实点击已选中的 Tab」证实 spec §15 第 1 条，记进 spec §18（实施记录），不在本 PR 修。

## Task 8（M8）：文档

- [ ] `authoring-promptugui-xml/SKILL.md`、新 `reference/sfx.md`、`controls-tabs.md` / `controls-collapsible.md` /
  `controls-carousel.md` / `controls-popover.md` / `drag-drop.md` / `reorder.md` / `animations.md` 指针；
  `scripting-promptugui-csharp/SKILL.md` 的 **UI sound cues** 小节与速查表；`AGENTS.md` 路由；主 spec §5.1。
- [ ] 全量回归：EditMode / EditorOnly / PlayMode；`dotnet format --verify-no-changes`；UIXmlLint 全目录。
- [ ] spec 加 §18 实施记录（偏差、发现）；提交。PR 描述写明两处行为变化（spec §12）。
