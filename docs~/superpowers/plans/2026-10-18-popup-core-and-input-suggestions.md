# 浮层内核 + 统一 Esc 链 + `<InputField>` 候选列表 Implementation Plan

> **For agentic workers:** 按 Task 顺序执行，步骤用 checkbox（`- [ ]`）跟踪。每个 Task Red 先行。
> 设计依据：`docs~/superpowers/specs/2026-10-18-popup-core-and-input-suggestions-design.md`（下称 spec，§ 号都指它）。
> 2026-10-19 与作者对齐；计划期修正（P1–P21）已在 Task 0 写回 spec。

## Context

搜索框要「边打字边出候选」（↑↓ 选、Enter / 点行 / 提交钮提交，提交时带上是否选了候选），而浮层零件只锁在 `<TabMenu>` 的私有实现里，`<InputField>` 也没有候选列表。读码评估还发现现有浮层与 Esc 的四处漏洞（spec §1.2：路由 Modal 不问菜单、菜单的 Esc 不判教程、被盖住的菜单仍占着 Esc、Dropdown 不在 Esc 链上）。spec 已按评估的推荐方向写成，§13 决策按推荐执行。

**Goal：** 阶段 0 —— 从 TabMenu 抽出 `PopupCore` / `PopupChain`，Esc 的决定收进 `EscapeChain`，更高的层出现时收起下层浮层，修掉 §1.2 的 1–4，TabMenu 公开行为不变；阶段 1 —— `<InputField><Suggestions itemTemplate=…/></InputField>` + `PuiInputField` 按键接管 + `InputField.OnInput` + `submitIcon`。

**Architecture：**
- **`PopupCore`**（`Controls/Internal`，非 Control 的内核对象，`TabGroupCore` 先例）：管面板的 Canvas / GraphicRaycaster / CanvasGroup、开关与过渡、遮罩、焦点圈闭、Esc 监听、定位。
- **`PopupChain`**：开着的浮层栈。它基于内部接口 `IPopupEntry`，可以脱离 `PopupCore` 单测；负责祖先判定、层序、`DismissBelow` / `DismissUnder`。
- **`EscapeChain`**（`Application`）：Esc 的唯一决定点。各 `ModalEscapeListener` 只当按键来源，按 `pressId` 去重。
- **`Suggestions`**（阶段 1）：`ProceduralControl` + `IScrollbarHost`。面板就是它自己，是输入框 GameObject 的子节点。每行一个 `FocuslessPress` 外壳（不是 Selectable 的 `IStateSource`），行复用沿用 `ItemBinding`。
- **`PuiInputField : TMP_InputField`**：只在列表打开时接管 `OnUpdateSelected`，按键路由是纯函数 `SuggestionKeys.Route`。编辑态经 `InputField` 内部的 `EditingChanged` 进入 `Suggestions`，EditMode 用测试钩子驱动。

**Tech Stack：** Unity 6 / C# 9、uGUI（Canvas overrideSorting / GraphicRaycaster / ScrollRect / VerticalLayoutGroup）、TMP（`TMP_InputField` 子类化，ugui 2.0.0）、Input System（`ModalEscapeListener` 的 InputActionMap）、R3、LitMotion。无新包。

## 已对齐的决策

spec §13 的 PC-D1…D10、SG-D1…D15 按推荐执行（Task 0 把 spec 状态改为「已对齐」）。计划阶段补充：

| # | 决策 | 为什么 |
|---|---|---|
| P1 | `PopupChain` 管的是内部接口 `IPopupEntry`（宿主 GameObject、面板、宿主根序、`CloseFromChain(reason)`），`PopupCore` 实现它 | 链的规则（祖先、层序、`DismissBelow`）可以用假条目单测，不必先有内核或 TabMenu |
| P2 | M0 保持纯重构：内核的 Esc 监听暂时照旧走「同帧去重 + `TryConsumeEscape`」（搬进 `PopupChain`），M1 才换成 `EscapeChain` | M0 的护栏是 TabMenu 现有全部测试原样通过；行为变化集中在 M1，Red 测试能单独看到 |
| P3 | 退役的候选行先移进一个挂着**禁用** `VerticalLayoutGroup` 的 `Pool` 再销毁（照 ScrollList 的 `EnsurePool`） | `InstantiateNode` / ReSolve 按「父节点有 LayoutGroup」判断布局组子节点；停放中的行被重放时才不会走错分支 |
| P4 | 每行外壳 `Row` = `FocuslessPress` + `HitCatcher` + `VerticalLayoutGroup`（`childControlWidth/Height`、`childForceExpandWidth`），先建外壳再 `_factory(rowRt)` | `<Show>` 在工厂调用内的 `OnAfterApply` 里 `GetComponentInParent<IStateSource>`，源必须先在父链上；外壳带 LayoutGroup，模板根才按布局组子节点处理（同 ScrollList 行） |
| P5 | `FocuslessPress` 内部持有 `StateBroadcaster`：进出调 `SetPointerInside` + `SetTransient(Hover/Normal)`，按下 / 抬起调 `SetTransient(Pressed/…)`，高亮调 `SetOn` | 合成规则（hover / pressed 临时覆盖 `selected`）与 Show 的登记协调直接复用；照 `PuiButton` 的写法 |
| P6 | 安全边界复用 `TutorialOverlayView.ApplySafeInset` 的算法：在 `SafeAreaTracker` 加共享的 `internal static ApplySafeInset(Rect, float)`，教程那份保留为一行转发（`TutorialSafeAreaTests` 直接调它） | 同一套内缩算法与测试注入钩子（`SafeAreaOverride` / `ScreenSizeOverride` / `ScaleFactorOverride`）；控件不该反向依赖教程视图 |
| P7 | 键盘高度经新的测试注入 `PopupBounds.KeyboardHeightOverride`（`Func<float>`，屏幕像素）读取，生产读 `TouchScreenKeyboard.visible` + `area.height` | EditMode / 桌面拿不到软键盘 |
| P8 | `InputField.IsEditing` 已存在（`InputField.cs:159`，`=> _input.isFocused`，`InputFieldNavPlayTests` 在用）：改成 `_editingOverride ?? _input.isFocused`，旁边加 internal `EditingChanged` 与测试钩子 `SetEditingForTests(bool?)`。`Suggestions` 与 `PuiInputField` 的 `OnMove` / `OnSubmit` / `OnCancel` 都只看这个可钩的信号，不直接读 `isFocused` | EditMode 里 TMP 进不了编辑态（激活在 `LateUpdate`）；直接读 `isFocused` 的话 EditMode 测试永远过不了 |
| P9 | Dropdown 的复现用真按键：`InputTestFixture` + `InputSystemUIInputModule`（照 `SubmitWakePlayTests`），`Press(kb.escapeKey)`。今天应当看到「模态被关、Dropdown 也收起」，修复后模态留着 | 用 `FireForTests()` 复现不了：它不走 EventSystem 的 Cancel，今天无论真实行为如何都会红。读码已确认问题存在：`Show()` 会选中一项（`TMP_Dropdown.cs:903`），`DropdownItem.OnCancel` 调 `Hide()`（`:45-50`） |
| P10 | 阶段 0 与阶段 1 分两个 PR；spec + plan 随阶段 0 的第一个提交入库 | 阶段 0 碰到每个模态的 Esc，单独回归更清楚 |
| P11 | **v1 去掉 `<Suggestions width>`**：面板永远与输入框同宽（anchor x 0..1）；`width` 并入忽略的布局属性（与 `PUI-SCROLLBAR-LAYOUT-ATTR` 同一张表：`anchor` / `size` / `width` / `height` / `margin` / `pivot` / `flow` / `scale` / `hidden`）。Task 0 改 spec §5.2 / §6.8 / §7 / SG-D12 | `width` 是通用属性（`CommonAttributes.All`），由 `ApplyCommon` 写进 `sizeDelta`，控件分不清「作者写的宽」与「上次定位留下的宽」；候选列表与输入框同宽也是 AutoSuggestBox 的做法。需要时再加 `popupWidth`（TabMenu 同名） |
| P12 | `maxHeight` 的语义对齐 `<Collapsible maxHeight>`：`0` = 只受可用空间限制；默认仍 `320`；`PUI-SUGGEST-VALUE` 改为「`< 0` 或非数」 | 同一个属性名在两个控件里意思一致 |
| P13 | 候选面板的尺寸 / 侧算法做成纯函数（在 `PopupPlacer` 之上夹高度），EditMode 测纯函数，集成测试只查接线 | `TabMenuPlacementTests` 的先例：EditMode 给不了 Overlay Canvas 确定的尺寸 |
| P14 | `NavTargetRules` 新增 `internal static bool IsSelectableTag(string)`（集合仍 private）；`RaycastRules.InteractiveTags`、`FontFloorRules.RowHosts` 在各自文件里加 `Suggestions` | `PUI-SUGGEST-ROW-SELECTABLE` 要复用同一张可聚焦标签表 |
| P15 | `PUI-SUGGEST-ROW-SELECTABLE` 照 `FontFloorRules` 的接法：`DocumentLinter` 在展开遍之后调 `SuggestionsRules.CheckRows(expanded)`，读 `screen.Templates`（只有展开遍有）；运行期由控件首次建行时自己警告一次 | `IRWalker` 只拿得到原始 `doc.Templates`，不知道 `itemTemplate` 的调用点 |
| P16 | `EscapeChain` 第 6 步 `try { _ = Router.Back(); } catch (NavigationRejectedException) { }`，仍算消费 | `Back()` 被守卫拒绝时同步抛（教程 `_rejectAll` 等），今天会从 Input System 回调里漏出去 |
| P17 | 接管循环里 `eventData.Use()` 只在「确实消费了某个 KeyDown，或输入法正在组字」时调用（照 TMP 的 `TMP_InputField.cs:2406-2410`） | `InputSystemUIInputModule.ProcessNavigation`（`:801-812`）一旦看到 update-selected 被 Use，整帧都不发 move / submit / cancel；无条件 Use 会让列表打开期间手柄完全失灵 |
| P18 | 面板的滚动条只显示、不接指针：`WireScrollbar` 里把轨道与手柄的 `raycastTarget` 关掉（滚动靠拖内容与滚轮） | uGUI 的 `Scrollbar` 是 Selectable（`Scrollbar.cs:19`）；按到它时输入模块会因 `GetEventHandler<ISelectHandler>` 不是输入框而清掉选中，编辑结束、列表关闭，违反 spec §6.5 规则 2 |
| P19 | `<Suggestions>` 的面板就是控件自己的 GameObject：`Suggestions.OnAfterApply` 每次都把 active 拨回 `IsOpen`（`BeginRebuild` 期间除外），打开时重新定位；`Control` 的懒建 `CanvasGroup`（`Control.cs:76`）改成先 `GetComponent` 再 `AddComponent`，`PopupCore` 同样复用已有的 | 每次 ReSolve 的 `ApplyCommon` 都会改写 anchor / pivot / size，`hidden=` 还会 `SetActive`（`Control.cs:533`），能把关着的面板打开；作者写 `interactable=` 时会出现第二个 CanvasGroup |
| P20 | TabMenu 因 `Interactable = false` / 变体隐藏而收起时用原因 `Owner`（保持今天「焦点回把手」）；`HostGone` 只用于失活、Screen 开始关闭、`Dispose`。打开时分别记下全局 `ContainmentRoot` 与所在 Screen 的 `NavConfineRoot`，关闭时各自还原。Task 0 同步改 spec §4.3 | 今天的 `PopNavigation` 用全局值去还原 Screen 的圈闭（`TabMenu.cs:661, 677`）；M0 要保持纯重构 |
| P21 | `PuiInputField.OnMove` 在处理过 IMGUI 方向键后的几帧内忽略 move | 按住 ↓ 时，没有 IMGUI KeyDown 的重复帧不会 Use，输入模块的 Navigate 重复（含键盘方向键）也会到 `OnMove`，高亮会多跳 |

## Global Constraints

- **分支**：阶段 0 在 `feat/popup-core`，阶段 1 在 `feat/input-suggestions`（阶段 0 合并后从 main 开）。**绝不提交到 main**。LangVersion 9.0（无 primary constructor / `[]` / `[field: SerializeField]` / `record`）。不用 `System.Threading` / `Task`（WebGL），异步用 `Awaitable`。
- **Core 纯 C#**：`Runtime/Core/Lint/*` 不得 `using UnityEngine`、不得依赖 `PromptUGUI.Application`。
- **`[UIAttr]` 必须同时 `[Preserve]`**；事件一律 `Observable<T>`（主 spec §9.4）。
- **R3 吞异常**：`OnNext` 里抛的异常记日志、不抛回推送方 —— 测试用 `LogAssert.Expect(LogType.Exception, regex)`，不用 `Assert.Throws`。
- **Red first**；每个 Task 收尾跑 lint：`cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`（**禁止** `--severity info`）。动过 `Core/Lint` 或任何 `.ui.xml` 后跑 `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/`（样例改动再加 `Samples~/CommonControls/Resources/`）。
- **测试只经 Unity MCP**；**禁止** `execute_menu_item("Assets/Reimport All")`。
- **EditMode 限制**：`LateUpdate` / 协程 / LitMotion 不跑（`TabMenu` 已是「非 Play 模式直接落终态」）；`Time.frameCount` 在 `[Test]` 内不前进；`Destroy` 即时；`TMP_InputField` 进不了编辑态（激活在 `LateUpdate`）；IMGUI 事件队列无注入口；没有 `[ExecuteAlways]` 的 MonoBehaviour（`ModalEscapeListener`、新的 `FocuslessPress`）不跑 `OnEnable` / `OnDisable` / `OnDestroy`。`Selectable` 带 `[ExecuteAlways]`（`Selectable.cs:9`），所以 `PuiButton` / `PuiInputField` 的 `OnEnable` / `OnDisable` 在 EditMode 里会跑，但 `[Test]` 内仍没有 `LateUpdate`。凡依赖这些的断言放 PlayMode 或用测试钩子。
- **开源仓库**：spec / plan / 提交信息 / PR 描述 / 代码注释 / 测试数据里不写任何宿主或下游工程的名字、路径、界面名、试跑结果；PR 前对分支 diff 与提交信息做一次 grep 自检。
- **测试程序集**：`PromptUGUI.Tests.EditMode` 不引用 Input System / LitMotion（要 `InputTestFixture` 或真过渡的放 PlayMode）；新测试文件不用登记。`Runtime/` 与 `Tests/` 下每个新文件都提交 Unity refresh 生成的 `.meta`；`Samples~` 不会被 Unity 导入，新资源的 `.meta` 要手写（见 Task 21）。
- **测试复位**：新增的静态状态都进 `UI.ResetForTests`：`PopupChain`、`EscapeChain`（含 `PopupConsumedFrame`）、`PuiDropdown` 的展开集合、`PopupBounds` 的各注入。另提供 `EscapeChain.ForgetPopupConsumedFrameForTests()`：`Time.frameCount` 在 `[Test]` 内不前进，不清掉的话，一次浮层 Esc 会让 `PuiInputField` 的接管与 `OnCancel` 吞键在本测试余下部分一直生效。
- **零回归守门**（阶段 0 每个里程碑末尾）：`TabMenuTests` `TabMenuPlacementTests` `TabMenuTrapTests` `TabMenuBindItemsTests` `TabMenuTriggerTests` `CollapsibleTriggerTests` `ModalStackTests` `MessageBoxRequestTests` `InputBoxTests` `MarkdownBoxTests` `CenteredSlideBoxTests` `LoadingOverlayTests` `RouterLayeringTests` `RouterModalTabTests` `TutorialRunTests` + PlayMode `TabMenuPlayTests` `CloseTriggerPlayTests` `ModalEscapePlayModeTests`。阶段 1 另加 `InputFieldTests` `ScrollbarTests` `DropdownTests` `ProceduralSurfaceRolloutTests` + PlayMode `InputFieldRuntimeTests` `InputFieldNavPlayTests`。

**RUN(Class)** = `refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` → `read_console(types=["error"])` 无 `error CS` → `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["Class"])` → 轮询 `get_test_job`，核对 `summary.total > 0`。
**RUNPLAY(Class)** 同上，`mode="PlayMode"`、`assembly_names=["PromptUGUI.Tests.PlayMode"]`。**RUNEDITOR(Class)** 同上，`assembly_names=["PromptUGUI.Tests.EditorOnly"]`。

---

## File Structure

| 文件 | 责任 | 动作 | 阶段 |
|---|---|---|---|
| `Runtime/Controls/Internal/PopupPlacer.cs` | `bounds` / `preferAbove` / `Room`（spec §4.6） | Modify | 0 |
| `Runtime/Controls/Internal/PopupChain.cs` | `IPopupEntry`、浮层栈、祖先、层序、`DismissBelow` / `DismissUnder`、`SortingOffset`（§4.2） | Create | 0 |
| `Runtime/Controls/Internal/PopupCore.cs` | 面板组件、开关、过渡、遮罩、圈闭与还原、监听、定位、`BeginRebuild`（§4.1 / §4.3） | Create | 0 |
| `Runtime/Controls/TabMenu.cs` | 委托给 `PopupCore`，保留 caption / 箭头 / `TabGroupCore`（§4.7） | Modify | 0 |
| `Runtime/Application/EscapeChain.cs` | `Dispatch`、按键去重、`PopupConsumedFrame`（§4.4） | Create | 0 |
| `Runtime/Application/Modals/ModalEscapeListener.cs` | 统一调 `Dispatch`、区分 East、InputActionMap 只建一次 | Modify | 0 |
| `Runtime/Application/UI.Modal.cs` · `UI.Router.Layering.cs` · `UI.Router.cs` · `Modals/LoadingOverlay.cs` · `Screen.cs` · `UI.cs` | `TryHandleEscape`、`DismissBelow` 钩子、`SealInput` → `DismissUnder`、复位 | Modify | 0 |
| `Runtime/Controls/Internal/PuiDropdown.cs` | 展开集合（Task 8） | Modify | 0 |
| `Runtime/Controls/Suggestions.cs` · `SuggestionSubmit.cs` | 部件元素本体与提交结构体（§5） | Create | 1 |
| `Runtime/Controls/Internal/FocuslessPress.cs` · `PointerSink.cs` · `SuggestionKeys.cs` · `PuiInputField.cs` | 非 Selectable 按压 + 状态源、面板吞点、纯按键路由、TMP 子类（§5.4 / §6.6） | Create | 1 |
| `Runtime/Controls/InputField.cs` | 挂 `PuiInputField`、`OnInput`、认领 `<Suggestions>`、`submitIcon*`、`EditingChanged` | Modify | 1 |
| `Runtime/Application/ScreenInstantiator.cs` · `BuiltinPrimitives.cs` | 认领分支、注册、运行期 lint 镜像 | Modify | 1 |
| `Runtime/Core/Lint/SuggestionsRules.cs` + `BuiltinTags` · `ProceduralSurfaceRules` · `PaddingRules` · `RaycastRules` · `FontFloorRules` · `ScrollbarRules` · `IRWalker` | `PUI-SUGGEST-*` 与扩展（§7） | Create / Modify | 1 |
| `Tests/EditMode/Controls/`：`PopupPlacerTests` `PopupChainTests` `PopupCoreTests` `FocuslessPressTests` `Suggestions*Tests` `SuggestionKeysTests` `PuiInputFieldTests` `SubmitIconTests`；`Tests/EditMode/Modals/`：`EscapeChainTests` `PopupCoverTests` | EditMode | Create | 0 / 1 |
| `Tests/PlayMode/Controls/InputFieldSuggestionsPlayTests.cs` · `Tests/PlayMode/Modals/DropdownEscapePlayTests.cs` | PlayMode | Create | 0 / 1 |
| `Tests/EditMode/Lint/SuggestionsRulesTests.cs` · `Tests/EditMode/Editor/XsdGeneratorTests.cs` | lint · XSD | Create · Modify | 1 |
| 既有测试：`TabMenuTests` `TabMenuTrapTests` `TabMenuPlayTests` `RouterLayeringTests` 等 | 跟随 internal 名字迁移 / Escape 一节改写 | Modify | 0 |
| 两份 SKILL · `reference/navigation.md` · `controls-tabs.md` · `states.md` · `controls-suggestions.md`（新）· `AGENTS.md` · 主 spec · RPL spec | 文档（英文 SKILL） | Modify / Create | 0 / 1 |
| `Samples~/CommonControls/…ui.xml` · `CommonControlsRunner.cs` · 两份 `CommonControls.po` · 两份 `_code.po`（如有代码文案）· `package.json` | 演示 | Modify | 1 |
| `docs~/superpowers/plans/2026-10-18-popup-core-and-input-suggestions.md` | 本计划落盘 | Create | 0 |

---

## 阶段 0 —— `feat/popup-core`

### M0 — 内核抽取（纯重构）

#### Task 0：分支 + 文档入库

- [x] `git switch -c feat/popup-core`（从 main，工作区里的 spec 随之带过来）
- [x] spec 头部状态改「已对齐（§13 按推荐）」；§13 表头去掉「待作者确认」
- [x] spec 按计划阶段的修正改齐：§5.2 删 `width` 行、表下的布局属性忽略列表加 `width`（P11）；§6.8「宽」只留「与输入框同宽」（P11）；§7 `PUI-SUGGEST-LAYOUT-ATTR` 行去掉「`width` 合法」（P11）；§9「随输入框宽与固定 `width`」改为「与输入框同宽」（P11）；§5.2 / §6.8 / §7 的 `maxHeight` 改为「`0` = 只受可用空间限制」、`PUI-SUGGEST-VALUE` 改为 `< 0`（P12）；§7 `ROW-SELECTABLE` 注明接法（P15）；§4.4 第 6 步注明吞掉 `NavigationRejectedException`（P16）；SG-D12 同步
- [x] 再按计划审阅的结论改齐：§4.1 API 加 `OnHostAfterApply()`（宿主 apply 之后：被隐藏 / 不可交互就收起、否则重新定位、`DeferDuringOpen` 隐藏关着的面板）；§4.3 的 `HostGone` 收窄为「失活 / Screen 开始关闭 / Dispose」，不可交互与被隐藏归 `Owner`，并写明全局 `ContainmentRoot` 与 Screen 的 `NavConfineRoot` 分开还原（P20）；§1.2-4 把「按代码推断」改为读码确认的依据（P9）；§5.4 / §6.5 加「滚动条只显示、不接指针」（P18）；§6.2 加「按住方向键不多跳」（P21）；§6.6 改为「只在确实消费了 KeyDown 或正在组字时 `Use()`」（P17）
- [x] 本计划写到 `docs~/superpowers/plans/2026-10-18-popup-core-and-input-suggestions.md`（不含「本次执行备注」）
- [x] `git add` 两份 → `git commit -m "docs: popup core + input suggestions — spec + plan"`

#### Task 1：`PopupPlacer` 扩展

**Files:** Modify `Runtime/Controls/Internal/PopupPlacer.cs`；Create `Tests/EditMode/Controls/PopupPlacerTests.cs`

- [ ] **Red**（纯函数，夹具照 `TabMenuPlacementTests` 的 `Canvas800` / `Handle(x, y)`）：
  - `Prefers_above_when_asked_and_it_fits`（`preferAbove: true` → `FlippedUp`，anchor `(0,1)` / pivot `(0,0)` / `y = +gap`）
  - `Above_preference_flips_down_when_below_has_more_room`（对称于既有的 `Flips_up_…`）
  - `A_panel_taller_than_both_sides_flips_only_if_the_other_side_is_roomier`（两个偏好各一例；与既有的 `TabMenuPlacementTests.Flips_only_when_the_other_side_is_actually_roomier`（`:51-61`）同一条规则）
  - `Room_is_the_space_on_the_chosen_side_minus_the_gap`（下方 / 上方各一例，数值断言）
  - `Bounds_smaller_than_the_canvas_limit_room_and_the_right_clamp`（安全区内缩后的 bounds）
- [ ] **实现**：`Solve(Rect handle, Vector2 panel, Rect bounds, float gap, bool preferAbove = false)`（第三个形参改名 `bounds`，调用方不变）；`PopupPlacement` 加 `public readonly float Room`；翻面规则 `needed > roomPreferred && roomOther > roomPreferred`；横向不变；顺手改正 `PopupPlacer.cs:42-43` 那句与实际规则不符的注释（「a panel taller than both sides stays put」）
- [ ] RUN(PopupPlacerTests) 绿；RUN(TabMenuPlacementTests) 原样绿；lint

#### Task 2：`PopupChain` + `IPopupEntry`

**Files:** Create `Runtime/Controls/Internal/PopupChain.cs`、`Tests/EditMode/Controls/PopupChainTests.cs`

- [ ] **Red**（测试里的假条目：两三个挂 Canvas 的裸 GameObject 当宿主根 / 面板；`CloseFromChain` 记录调用顺序与原因）：
  - `Push_closes_entries_that_are_not_ancestors`（原因 `Owner`）
  - `Push_keeps_ancestors_when_the_host_is_inside_an_open_panel`
  - `Closing_a_parent_closes_its_children_top_down`
  - `Sorting_order_is_host_root_plus_offset_plus_two_per_depth`；`Blocker_order_is_panel_minus_one`
  - `DismissBelow_closes_entries_whose_host_root_sorts_lower`（`Covered`，自顶向下；序相等不关）
  - `DismissUnder_closes_entries_hosted_under_root`（`HostGone`）
  - `Remove_drops_an_entry_without_closing_others`；`ResetForTests_empties_the_chain`
- [ ] **实现**：
  ```csharp
  internal interface IPopupEntry
  {
      GameObject HostGameObject { get; }
      RectTransform Panel { get; }
      int HostRootOrder { get; }                      // 宿主根 Canvas 的 sortingOrder（读时现算）
      void CloseFromChain(PopupCloseReason reason);   // 只关自己；链负责顺序
  }
  internal static class PopupChain
  {
      internal const int SortingOffset = 2;
      internal static IPopupEntry Top { get; }  internal static bool IsEmpty { get; }
      internal static int Push(IPopupEntry e);       // 关掉非祖先后压栈，返回链深
      internal static void Close(IPopupEntry e, PopupCloseReason r);   // 先关其上各项
      internal static void Remove(IPopupEntry e);
      internal static int PanelOrder(IPopupEntry e); // 宿主根序 + 2 + 2·depth
      internal static void DismissBelow(int order);  internal static void DismissUnder(Transform root);
      internal static void ResetForTests();
  }
  ```
  `PopupCloseReason` 与 `PopupDismiss` 两个枚举放在同一文件。
  M0 的过渡件也在这里建（P2）：`NoteEscapeConsumed()` / `TryConsumeEscape()` / `ForgetEscapeFrameForTests()`，逻辑照搬 `TabMenu.cs:707-743`（今天是 TabMenu 私有的，Task 3 的内核监听要用）。Task 6 删除
- [ ] 补两条 Red：`TryConsumeEscape_closes_the_top_and_reports_consumed`；`An_escape_already_spent_this_frame_still_reads_as_consumed`
- [ ] RUN(PopupChainTests) 绿；lint

#### Task 3：`PopupCore`

**Files:** Create `Runtime/Controls/Internal/PopupCore.cs`、`Tests/EditMode/Controls/PopupCoreTests.cs`

- [ ] **Red**（宿主用 XML 开一个只有 `<Frame id='h' size='120x44'/>` 的 Screen；面板在测试里 `ProceduralBuilders.AddChild(h.RectTransform, "Panel")`）：
  - `Open_lifts_the_panel_above_the_host_root_canvas`（`overrideSorting`、序 = 根 + `SortingOffset`、面板激活）
  - `Blocker_mode_puts_a_catcher_under_the_root_canvas`（名字 `PopupCore.BlockerName`、序 = 面板 − 1、透明但 `raycastTarget`、Button 导航 `None`）；`Clicking_the_catcher_closes_with_OutsidePress`；`Owner_mode_has_no_catcher`
  - `Close_in_EditMode_deactivates_the_panel_and_drops_overrideSorting`
  - `Escape_listener_is_enabled_only_while_open`；`Cancel_button_binding_follows_the_constructor_flag`
  - `Cage_sets_ContainmentRoot_and_lands_on_FocusPick`（导航启用）；`Close_restores_ContainmentRoot_only_if_it_is_still_the_panel`；`Close_moves_selection_to_the_host_only_if_it_was_inside_the_panel`；`HostGone_clears_a_selection_inside_the_panel`
  - `BeginRebuild_activates_a_closed_panel_for_the_scope_only`
  - `OnHostAfterApply_closes_when_the_host_is_hidden_or_disabled_and_repositions_otherwise`；`A_closed_panel_is_hidden_after_the_open_pass`（`DeferDuringOpen`）
  - `Close_restores_the_screens_NavConfineRoot_and_the_global_ContainmentRoot_separately`（P20）
  - `Dispose_destroys_the_catcher_and_leaves_the_chain`
- [ ] **实现**：把 `TabMenu` 里的 `BuildPopup`（Canvas / Raycaster / CanvasGroup 部分）、`Expand/ExpandCore/Collapse`、`PlayTransition`（淡入 + 位移，不含箭头）、`SlideOffset`、`CancelMotions`、`DeactivatePopup`、`Push/PopNavigation`（改成 spec §4.3 的两条规则）、`EnableEscapeListener`、`RootCanvas`、`EnsureBlocker`、`PlacePopup` 的定位部分、`RectInCanvas`、`HidePopupIfCollapsed` + `DeferDuringOpen`、`BeforeRebuild/AfterRebuild` 搬进内核；尺寸与定位参数经 `Measure` 回调向宿主要（返回宽高、`gap`、`preferAbove`、`bounds`）；`Opened` / `Closed(reason)` 事件；实现 `IPopupEntry`；`internal void OnHostAfterApply()` 承接 `TabMenu.cs:851-864` 那段（被隐藏 / 不可交互就收起，否则 `Reposition`，关着时经 `DeferDuringOpen` 停用面板）；Esc 监听在 M0 调 `PopupChain.NoteEscapeConsumed()` + `Close(Escape)`。`AddComponent<ModalEscapeListener>` 之后立刻设 `AlsoCancelButton` 的顺序问题由 Task 9 修，这里照搬今天的写法
- [ ] RUN(PopupCoreTests) 绿；lint

#### Task 4：TabMenu 委托给内核 + internal 名字迁移

**Files:** Modify `TabMenu.cs`、`Screen.cs`（`SealInput` → `PopupChain.DismissUnder`）、`UI.cs`（`ResetForTests` → `PopupChain.ResetForTests`）、`UI.Router.Layering.cs`（步长下限与报错消息读 `PopupChain.SortingOffset`）。**测试文件一行不改**

- [ ] TabMenu 只留 caption、箭头（在 `Opened` / `Closed` 上跑同一时长的翻转）、`TabGroupCore`、「选中即收起」与 `_expanding`；`IsExpanded` 镜像内核；`Interactable = false` / 变体隐藏收起用原因 `Owner`（焦点照旧回把手，P20），`Dispose` / Screen 关闭用 `HostGone`；`OnAfterApply` 里那段改调 `_core.OnHostAfterApply()`
- [ ] TabMenu 上保留 internal 别名，转发到新位置：`BlockerName`、`PopupSortingOffset`、`HasExpandedMenu`、`CollapseIfUnder`、`TryConsumeEscape`、`NotifyEscapeConsumedForTests`、`ForgetEscapeFrameForTests`、`ResetForTestsInternal`（P2：同帧去重这时搬进 `PopupChain`，语义不变）。`UI.Modal` 照旧调 `TabMenu.TryConsumeEscape()`。别名在 Task 6 删除，测试在那里一并迁移
- [ ] **零回归守门**：RUN(TabMenuTests) RUN(TabMenuPlacementTests) RUN(TabMenuTrapTests) RUN(TabMenuBindItemsTests) RUN(TabMenuTriggerTests) RUN(CollapsibleTriggerTests) RUN(RouterLayeringTests) RUN(RouterModalTabTests) RUNPLAY(TabMenuPlayTests) RUNPLAY(CloseTriggerPlayTests)；lint

#### M0 收尾

- [ ] EditMode / PlayMode 全量无新失败 → `git commit -m "refactor(popup): extract PopupCore / PopupChain from TabMenu"`

### M1 — Esc 链 + 被盖住即收

#### Task 5：先复现 spec §1.2 的四个漏洞（全部应当红）

**Files:** Create `Tests/EditMode/Modals/EscapeChainTests.cs`（先只放复现用例）、`Tests/EditMode/Modals/PopupCoverTests.cs`、`Tests/PlayMode/Modals/DropdownEscapePlayTests.cs`。两个 EditMode 类继承 `ModalTestFixture`（`Tests/EditMode/Modals/ModalTestFixture.cs`：假 resolver + 最小 MessageBox），页面用 `UI.LoadDocument` 直接开（同 `TabMenuTrapTests.Open`）

- [ ] `Routed_modal_with_an_open_menu_closes_only_the_menu`（§1.2-1）：照 `RouterModalTabTests` 的路由夹具开一个含 `<TabMenu>` 的路由 Modal，`Expand()`，对模态根的 `ModalEscapeListener.FireForTests()` → 菜单收起、路由链仍含该 Modal
- [ ] `Blocking_tutorial_leaves_an_open_menu_open`（§1.2-2）：照 `TutorialRunTests.cs:173` 起一个 Block 步骤（`UI.Tutorial.IsBlockingInput == true`），页上的菜单展开，触发菜单自己的监听 → 菜单仍展开
- [ ] `Opening_a_modal_by_code_collapses_an_open_menu`（§1.2-3）：菜单展开 → `MessageBox.Open(...)` → 菜单已收起；`UI.UseGamepadNavigation()` 下关掉模态后焦点回到菜单把手（`prevSelected` 是把手而不是失活的行）
- [ ] `Escape_on_an_expanded_dropdown_inside_a_modal_closes_only_the_dropdown`（§1.2-4，PlayMode，P9）：类继承 `InputTestFixture`，EventSystem 挂 `InputSystemUIInputModule`（照 `SubmitWakePlayTests.cs:17-72`，先清掉别的测试漏下的 EventSystem）；模态里放 `<Dropdown>`，`Show()` 后等一帧，`Press(kb.escapeKey)` 再等一帧 → 断言模态仍开、Dropdown 收起。今天应当红在「模态被关」上
- [ ] RUN(EscapeChainTests) RUN(PopupCoverTests) RUNPLAY(DropdownEscapePlayTests)：确认红在预期断言上

#### Task 6：`EscapeChain` + 监听改线

**Files:** Create `Runtime/Application/EscapeChain.cs`；Modify `ModalEscapeListener.cs`、`UI.Modal.cs`、`UI.Router.Layering.cs`（路由 Modal 的监听不再赋回调）、`UI.Router.Reconcile.cs`（`TryHandleEscape` 放在 `_chain` / `IsTop` 旁，`:343`）、`PopupCore.cs`、`PopupChain.cs`、`UI.cs`；改写 `TabMenuTrapTests` 的 Escape 一节

- [ ] **Red**（补进 `EscapeChainTests`）：
  - `Nothing_open_is_not_consumed`
  - `An_open_menu_takes_the_press_before_an_ad_hoc_modal`；`…_before_a_routed_modal`（即 Task 5 的复现）
  - `Cancel_button_closes_a_menu_but_never_a_modal`（`Dispatch(EscapeKey.CancelButton, …)`）
  - `The_same_press_heard_twice_acts_once`（同一 `pressId` 两次 → 第二次无动作、返回第一次的结果）；`FireForTests_presses_are_distinct`（连续两次 `FireForTests` 关掉栈上两个模态）
  - `Ad_hoc_modal_answers_before_a_routed_modal`；`Refused_escape_on_an_ok_only_box_is_still_consumed`
  - `A_popup_below_the_top_modal_does_not_take_the_press`（代码在模态下 `Expand()` 页上的菜单 → Esc 关模态）
  - `A_rejected_Back_is_consumed_and_does_not_throw`（路由守卫拒绝时 `Back()` 同步抛 `NavigationRejectedException`，链接住）
  - `Top_routed_node_that_is_not_a_Modal_does_nothing`（Tab / Prompt 压在 Modal 之上时，与今天一致）
- [ ] **实现**：
  - `EscapeChain.Dispatch(EscapeKey key, int pressId) → bool`，顺序照 spec §4.4 的表；`PopupConsumedFrame`；`NextTestPress()`（递减负数）；`ForgetPopupConsumedFrameForTests()`；`ResetForTests()`
  - `UI.Modal.TryHandleEscape()`（原 `OnEscapePressed` 去掉 TabMenu / 教程判断后的部分）；`UI.Router.TryHandleEscape()`：`_chain` 栈顶 `Def.Kind == RouteKind.Modal` 才 `Back()`，`try/catch (NavigationRejectedException)` 吞掉
  - `ModalEscapeListener`：删 `OnEscape` 字段；`OnPerformed(ctx)` → `Dispatch(ctx.control 是 buttonEast ? CancelButton : Escape, Time.frameCount)`；legacy `Update` → `Dispatch(Escape, Time.frameCount)`；`FireForTests()` → `Dispatch(Escape, EscapeChain.NextTestPress())`；各处 `AddComponent` 后不再赋回调
  - 删除 P2 的过渡件：`PopupChain` 里的同帧去重与 `TryConsumeEscape`，以及 Task 4 留在 TabMenu 上的全部 internal 别名
  - `UI.ResetForTests` 加 `EscapeChain.ResetForTests()`
- [ ] 测试迁移（spec §4.7 的表，语义不变）：`TabMenuTests`（`BlockerName` :235、`PopupSortingOffset` :252 / :267、`HasExpandedMenu` :400）、`TabMenuPlayTests`（`BlockerName` :91 / :104）、`CloseTriggerPlayTests`（`HasExpandedMenu` :186 / :190）、`RouterLayeringTests`（`PopupSortingOffset` :139-142）。`HasExpandedMenu` 对应的是 `!PopupChain.IsEmpty`，断言要翻转：`TabMenuTests.cs:400` 的 `IsFalse(HasExpandedMenu)` → `IsTrue(PopupChain.IsEmpty)`；`CloseTriggerPlayTests.cs:186` 的前提 → `Assume.That(!PopupChain.IsEmpty)`，`:190` → `IsTrue(PopupChain.IsEmpty)`
- [ ] `TabMenuTrapTests` Escape 一节（:130-173）的四个用例改写到 `EscapeChain.Dispatch` 上（场景与断言不变：无菜单不消费；有菜单收起并消费；两个监听听到同一下只动作一次；新的一下是新按键）
- [ ] RUN(EscapeChainTests) RUN(TabMenuTrapTests) RUN(ModalStackTests) RUN(MessageBoxRequestTests) RUN(InputBoxTests) RUN(MarkdownBoxTests) RUN(CenteredSlideBoxTests) RUN(LoadingOverlayTests) RUN(RouterModalTabTests) RUN(RouterLayeringTests) RUN(TutorialRunTests) RUNPLAY(ModalEscapePlayModeTests)；lint

#### Task 7：被盖住即收（`DismissBelow` 的三个钩子）

**Files:** Modify `UI.Modal.cs`、`UI.Router.Layering.cs`、`Modals/LoadingOverlay.cs`；补 `PopupCoverTests`

- [ ] **Red**（补进 `PopupCoverTests`）：
  - Task 5 的 `Opening_a_modal_by_code_collapses_an_open_menu`
  - `A_routed_child_page_collapses_the_parent_pages_menu`
  - `A_routed_modal_collapses_every_page_menu`
  - `Loading_collapses_page_menus_but_not_menus_inside_modals`
  - `Toast_and_tutorial_do_not_collapse_menus`
  - `A_covered_close_does_not_steal_focus_from_the_new_layer`（导航启用：新层先落焦点的场景下，选中项不被挪回把手）
- [ ] **实现**：
  - `UI.Modal` pump：在 `if (entry.Resolved) continue;`（`UI.Modal.cs:115`）之后、捕获 `prevSelected`（`:117`）**之前**调 `PopupChain.DismissBelow(SortingOrderBase + Router.RoutedModalCount + _stack.Count)`（它将得到的序）。同步调用，不破坏「pump 里唯一的 await 在 `_stack.Add` 之前」的不变量（`:110-113`）；收起回调里若有人再开模态，只会进 `_pending`（`_materializing` 已为真）。但收起会跑用户的 `OnCollapsed` 订阅：调用之后**再判一次** `entry.Resolved`（→ `continue`）与 `epoch != _materializeEpoch`（→ `return`），否则回调里一句 `CloseAll()` 之后 pump 仍会把已取消的模态建出来。补 Red：`CloseAll_from_an_OnCollapsed_callback_does_not_build_the_cancelled_modal`
  - `ApplyRouteLayer`：Modal 分支与 Page 分支各算出自己的序后调 `DismissBelow(order)`
  - `LoadingOverlay`：`canvas.sortingOrder = Loading.SortingOrder;`（`LoadingOverlay.cs:101`）之后、`BindText` 之前调 `DismissBelow(Loading.SortingOrder)`
- [ ] RUN(PopupCoverTests) + Task 6 的守门列表；lint

#### Task 8：Dropdown 接进 Esc 与被盖住即收

**Files:** Modify `Controls/Internal/PuiDropdown.cs`、`EscapeChain.cs`、`PopupChain.cs`

- [ ] `PuiDropdown` 覆盖 `CreateBlocker`（入集合）/ `DestroyBlocker`（出集合）/ `OnDestroy`（出集合）；静态 `ExpandedCount` / `HideExpanded()` / `HideBelow(int order)`（按宿主根序）；`ResetForTests`
- [ ] `EscapeChain` 第 2 步；`PopupChain.DismissBelow` 顺带 `PuiDropdown.HideBelow(order)`
- [ ] RUNPLAY(DropdownEscapePlayTests) 转绿；补一条 `A_modal_opened_by_code_hides_an_expanded_dropdown_below_it`；RUN(DropdownTests) RUN(ScrollbarTests) 不回归；lint

#### Task 9：`ModalEscapeListener` 的 InputActionMap 只建一次 + 首次展开就绑上手柄 B

- [ ] **Red**（PlayMode，补进 `ModalEscapePlayModeTests`——`ModalEscapeListener` 没有 `[ExecuteAlways]`，EditMode 里 `OnEnable` 不跑、map 根本不建）：`Cancel_button_is_bound_on_the_first_open`。今天 `TabMenu.cs:699-701` 先 `AddComponent`（`OnEnable` 立刻按 `AlsoCancelButton == false` 建好 map）再设 `AlsoCancelButton = true`，所以菜单**第一次**展开时手柄 B 不生效，要关一次再开才有。断言用 internal 只读 `BindsCancelButton`（运行时代码在 `#if ENABLE_INPUT_SYSTEM` 里读 map 的绑定；EditMode 测试程序集不必引用 Input System）
- [ ] **实现**：`OnEnable` 首次建 map（绑定按 `AlsoCancelButton`）、之后只 `Enable()`；`OnDisable` 只 `Disable()`；`OnDestroy` 才 `Dispose()`；`AlsoCancelButton` 的 setter 在 map 已建好时重建绑定
- [ ] RUNPLAY(ModalEscapePlayModeTests) 转绿 + Task 6 的守门列表；lint

#### Task 10：阶段 0 文档（英文，同 PR）

- [ ] `reference/navigation.md`「Popup focus trap」：Esc 规则补全（路由 Modal 同 ad-hoc；Block 引导期间什么都不收；被模态 / 子页 / Loading 盖住的菜单自动收起）
- [ ] `reference/controls-tabs.md`「Opening and closing」：加「更高的层出现时收起」
- [ ] C# skill：Modal `### Behavior` 写 Esc 决定顺序（spec §4.4 的表，含「B 只关浮层」）；Layering 一节「an expanded `<TabMenu>`」泛化为「an open popup」
- [ ] `docs~/superpowers/specs/2026-10-01-router-page-layering-design.md` §3 / §4.3 加注：`PopupSortingOffset` 已移到 `PopupChain.SortingOffset`；层带里的「TabMenu 遮罩 / 面板」改称「浮层遮罩 / 面板」

#### M1 收尾（阶段 0 的 PR）

- [ ] 三个程序集全量（EditMode / EditorOnly / PlayMode）无新失败；`dotnet format --verify-no-changes --severity warn`；UIXmlLint 跑 `Runtime/Resources/`
- [ ] spec 加 §16「实施记录（阶段 0）」：与设计的偏差、Task 5 四条复现的结论
- [ ] `git commit -m "feat(popup): EscapeChain — one decision per press; covered popups close"`
- [ ] 隐私自检（见 Global Constraints）→ 征得同意后 `git push -u origin feat/popup-core` + `gh pr create`

---

## 阶段 1 —— `feat/input-suggestions`

阶段 0 的 PR 合并后：`git switch main && git pull && git switch -c feat/input-suggestions`。

### M2 — `<Suggestions>` 骨架

#### Task 11：`FocuslessPress`（非 Selectable 的按压 + 状态源）

**Files:** Create `Runtime/Controls/Internal/FocuslessPress.cs`、`Tests/EditMode/Controls/FocuslessPressTests.cs`

- [ ] **Red**（指针事件照 `CollapsibleTests.cs:235` / `TextLinkTests.cs:47-58` 的 `ExecuteEvents` 写法）：
  - `Is_not_a_Selectable`
  - `Pointer_enter_and_exit_drive_hover`；`Press_and_release_drive_pressed`
  - `SetOn_reads_selected_and_hover_overrides_it_while_inside`
  - `Click_fires_for_a_left_click_only`（右键不发）；`Not_interactable_reads_disabled_and_never_clicks`
  - `IsPressed_spans_down_to_up`（SG-D15 要用）
  - `A_Show_instantiated_under_it_resolves_to_it`：建一个带 `FocuslessPress` 的父节点，`UI.GetInstantiator().InstantiateNode(<Frame><Show on="state-selected"><Frame id="hl"/></Show></Frame>, parentRt, owner)`，`SetOn(true)` → `hl` 激活
- [ ] **实现**：`internal sealed class FocuslessPress : MonoBehaviour, IStateSource, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler`。内部持有 `StateBroadcaster`（照 `PuiButton.cs:47-57` 先 `SetPointerInside` 再 `SetTransient`）。对外：`Interactable`、`SetOn(bool)`、`IsPressed`、`Observable<Unit> OnClicked`；`OnDisable` 清指针状态，`OnDestroy` 调 `_broadcaster.Dispose()`。`RefreshState()` 是空实现（它不会成为 EventSystem 的选中项）
- [ ] RUN(FocuslessPressTests) 绿；lint

#### Task 12：`<Suggestions>` 注册、认领、结构

**Files:** Create `Runtime/Controls/Suggestions.cs`、`SuggestionSubmit.cs`、`Internal/PointerSink.cs`、`Runtime/Core/Lint/SuggestionsRules.cs`（骨架）；Modify `InputField.cs`（`AdoptSuggestions`、`Suggestions` 属性）、`ScreenInstantiator.cs`（认领分支）、`BuiltinPrimitives.cs`、`Core/Lint/BuiltinTags.cs`、`Core/Lint/ProceduralSurfaceRules.cs`（`SurfaceTags`）、`Tests/EditMode/Controls/ProceduralSurfaceRolloutTests.cs`（`SizeAttrsFor`）、`Runtime/Controls/Control.cs`（CanvasGroup 先找后建，P19）、`Tests/EditMode/Editor/XsdGeneratorTests.cs`；Create `Tests/EditMode/Controls/SuggestionsStructureTests.cs`、`Tests/EditMode/Lint/SuggestionsRulesTests.cs`

- [ ] **Red**：
  - `Adopted_by_its_InputField`（`field.Suggestions` 非空，面板父节点是输入框的 GameObject）；`A_field_without_one_has_null`
  - `Panel_carries_canvas_raycaster_canvasgroup_scrollrect_viewport_content_and_is_inactive_while_closed`
  - `Builds_a_default_scrollbar_when_none_is_authored`；`Adopts_an_authored_scrollbar`
  - `Outside_an_InputField_it_stays_inert_and_warns`（`LogAssert.Expect(Warning, new Regex(...))` 匹配规则消息的文案——`UILog.Warn(node, issue)` 只打 `issue.Message`，不带 code，`UILog.cs:55`）；`A_second_one_is_parked_inactive_and_warns`
  - `Layout_attributes_do_not_move_the_panel`（含 `hidden="false"` 与变体：都不能把关着的面板打开，P19）
  - `An_authored_interactable_does_not_add_a_second_CanvasGroup`（P19）
  - `The_scrollbar_is_display_only`（默认条与作者写的条，轨道和手柄的 `raycastTarget` 都是 false，P18）
  - `An_unknown_placement_throws`（`ArgumentException`，同 `contentType`）；`A_negative_maxHeight_or_gap_warns_and_falls_back`
  - XSD（EditorOnly）：`Suggestions_lists_its_attributes`（`itemTemplate` / `maxHeight` / `gap` / `placement` / `padding` / `spacing` / `transition`）
  - `BuiltinTagsTests` / `ProceduralAttrNamesTests` 自动钉住两张表
- [ ] **实现**：
  - `Suggestions : ProceduralControl, IScrollbarHost`。`OnAttached` 先挂 `Image`（`Surface` 首次访问时才捕获它，必须在前），再建 `ScrollRect` + `Viewport`（RectMask2D）+ `Content`（VLG + ContentSizeFitter）+ `PointerSink`。`ParticipatesInLayout => false`，`SurfaceHost => GameObject`，`ScrollbarHost => RectTransform`。`AdoptScrollbar` / `EnsureDefaultScrollbar` / `WireScrollbar` 照抄 `Dropdown.cs:328-381`，`WireScrollbar` 末尾把条的轨道与手柄 `raycastTarget` 关掉（P18）
  - `OnAfterApply`：`base` 之后把 `GameObject` 的 active 拨回 `IsOpen`（`BeginRebuild` 期间除外），打开着就重新定位（P19）；`Control.CanvasGroup`（`Control.cs:76`）改成 `GetComponent<CanvasGroup>() ?? AddComponent`，`PopupCore` 同样先找后建
  - 先建 `Runtime/Core/Lint/SuggestionsRules.cs` 的骨架：`Tag`、`OutsideCode`、`DuplicateCode`、`CheckOutside(parent, child)`、`CheckDuplicate(host)`（照 `ScrollbarRules.cs:148-174`，纯 C#），配两组 `SuggestionsRulesTests`；其余规则在 Task 20 补
  - 认领：`ScreenInstantiator` 子循环里照 `<Scrollbar>` 分支（`ScreenInstantiator.cs:385-401`）加一支：父控件是 `InputField` 且子标签是 `Suggestions` → 照常实例化到 `ChildHostTransform`，再 `field.AdoptSuggestions(s)`（重复的停用，并用 `CheckDuplicate` 警告）。不在 `<InputField>` 下时用 `CheckOutside` 的结果 `UILog.Warn`，面板保持停用
  - 认领后 `PopupCore(host: field, panel: RectTransform, PopupDismiss.Owner, cageFocus: false, cancelButtonCloses: true)`
  - `reg.Register<Suggestions>("Suggestions", null)`；`BuiltinTags.All` 与 `SurfaceTags` 加 `Suggestions`
  - `ProceduralSurfaceRolloutTests` 会把每个程序化标签裸放在 `<Screen>` 下（`:53-61`）。裸放的 `<Suggestions>` 是惰性的（面板停用），但表面照样在 `OnAfterApply` 里挂上；`PanelIn` 连未激活节点一起找，`IsPanelVisible` 只看填充 / 描边 / 发光（`ProceduralPanel.cs:682`），所以预期不需要特例，只在 `SizeAttrsFor` 里给它空串（`:37-44`，免得布局属性警告刷屏）。若仍有断言失败，再按 Collapsible / Scrollbar 的做法加特例
- [ ] RUN(SuggestionsStructureTests) RUN(BuiltinTagsTests) RUN(ProceduralAttrNamesTests) RUN(ProceduralSurfaceRolloutTests) RUN(ScrollbarTests) RUN(DropdownTests) RUNEDITOR(XsdGeneratorTests)；lint

#### Task 13：行、`BindItems` 复用与行状态

**Files:** Modify `Suggestions.cs`、`Internal/PointerSink.cs`；Create `Tests/EditMode/Controls/SuggestionsBindItemsTests.cs`、`SuggestionRowStateTests.cs`、`SuggestionsFocusTests.cs`

- [ ] **Red**（夹具照 `ScrollListReuseTests.cs:24-60` 的 `Open` / `Push`；推送用 `Observable.Return` 与 `ReactiveProperty` 各一组）：
  - `Positional_push_reuses_rows_by_position`（跨推送 `AreSame`，同一批 GameObject）；`Keyed_push_reuses_rows_by_key`（换序后每行内容不变、兄弟序 = 数据序）
  - `Reused_rows_release_subscriptions_before_rebind`；`Changing_itemTemplate_rebuilds_every_row`；`Retired_rows_leave_Content_before_bind`
  - `Count_is_the_last_push`；`Without_itemTemplate_the_push_logs_an_exception`（R3 吞异常 → `LogAssert.Expect(Exception, …)`）
  - `A_row_template_with_a_Btn_warns_once`
  - `Rows_pushed_while_closed_still_measure`（照 `TabMenuBindItemsTests.cs:79-91`）
  - 焦点（`SuggestionsFocusTests`，spec §6.5 的三条结构约束）：对行、面板空白处，`ExecuteEvents.GetEventHandler<ISelectHandler>` 都返回输入框；行上没有 `Selectable`；面板空白处的 `IPointerDownHandler` 与 `IPointerClickHandler` 都落在 `PointerSink`（click 是单独解析的，sink 不接 click 的话会落到 `TMP_InputField.OnPointerClick`）；行上的 `IPointerDownHandler` 是它的 `FocuslessPress`；行上的 `IDragHandler` / `IScrollHandler` 是面板的 ScrollRect
  - 行状态（断言写法照 `ShowTests.cs:33-34` 的多个 `<Show on="state-*">` 并列）：`Show_state_selected_follows_the_highlight`、`Show_state_hover_follows_the_pointer`、`Hover_overrides_selected_while_inside`、`Show_state_normal_hides_while_a_claimed_state_is_on`
- [ ] **实现**：
  - 工厂照 `ScrollList.ResolveFactory`（`TemplateFactoryResolver.TryResolve(owner, name, $"itemTemplate='{name}'", out f)`）
  - 每项先建外壳 `Row`（`FocuslessPress` + `HitCatcher` + VLG，P4），再 `_factory(rowRt)`
  - `PositionalBinding` / `KeyedBinding` 的四个 `BindItems` 重载，`Rebuild` 照 `ScrollList.Rebuild`（`ScrollList.cs:1433-1476`）：先定结构，复用前 `ReleaseSubscriptions`，没人要的旧行进 `Pool`（禁用 VLG，P3）再 `Dispose`，钉兄弟序，最后逐行 `TryBind`
  - 收起态推送包在 `_core.BeginRebuild()` 里；首次建出含 Selectable 的行时 `UILog.Warn` 一次
  - `TriggerSourceResolver.FindStateSource` 的两条报错文案（`:151-154`、`:160-163`）补上「或 `<Suggestions>` 的行」
- [ ] RUN(SuggestionsBindItemsTests) RUN(SuggestionRowStateTests) RUN(SuggestionsFocusTests)；lint

#### Task 14：显示状态机、内核接线与定位

**Files:** Modify `Suggestions.cs`、`InputField.cs`（既有的 `IsEditing` 改为可钩，新增 internal `EditingChanged` / `SetEditingForTests(bool?)` / `EndEditing()`，P8）、`Controls/Internal/SafeAreaTracker.cs`（新增共享的 `ApplySafeInset`，P6）、`Application/Tutorial/TutorialOverlayView.cs`（原方法改为转发）；Create `Controls/Internal/PopupBounds.cs`（安全区 + 键盘高度；测试注入 `KeyboardHeightOverride` 与 `BoundsOverride`（`Func<Rect>`，整块边界），P7）、`Tests/EditMode/Controls/SuggestionsVisibilityTests.cs`、`SuggestionsPlacementTests.cs`

- [ ] **Red**（可见性；编辑态用 `SetEditingForTests`，武装先用 internal `NotifyUserInput()`，Task 18 再接到 `OnInput`）：
  - `Editing_alone_does_not_open`；`Input_arms_and_a_non_empty_push_opens`；`Open_requires_editing`
  - `An_empty_push_closes_without_disarming`（再推非空就开）
  - `Escape_closes_and_disarms`（经 `EscapeChain.Dispatch`）；`Editing_end_closes_and_disarms`；`Close_disarms`
  - `Not_interactable_or_readOnly_never_opens`；`Setting_Interactable_false_from_code_closes`；`A_field_hidden_by_a_variant_closes`（走 ReSolve → `Suggestions.OnAfterApply` 检查 `activeInHierarchy`，同 `TabMenu.cs:853`）。代码里 `Hidden = true` 经 `PuiInputField.OnDisable` 关列表，这一例放到 Task 17（`PuiInputField` 建好之后；它继承 `Selectable` 的 `[ExecuteAlways]`，EditMode 里 `OnDisable` 照样跑）
  - `Covered_closes_and_ends_editing`（`PopupChain.DismissBelow` → `EndEditing` 被调用，用钩子记录）
  - `A_push_while_open_keeps_the_side_and_resets_the_highlight`
  - `TutorialSafeAreaTests` 照旧通过（P6：教程那份改为转发）
- [ ] **Red**（定位；纯函数为主，P13）：
  - `Below_by_default_and_as_wide_as_the_field`；`Above_when_preferred`；`Flips_when_the_preferred_side_lacks_room`
  - `Height_is_content_clamped_by_maxHeight_and_room`；`maxHeight_zero_means_room_only`（P12）
  - `Safe_area_reduces_the_room`（`SafeAreaTracker` 的三个注入）；`Keyboard_height_reduces_the_room_below`（`KeyboardHeightOverride`）
  - 接线（照 `TabMenuPlacementTests.cs:88-130` 的 wiring 段，`Canvas.ForceUpdateCanvases()`；边界一律用 `PopupBounds.BoundsOverride` 给定，EditMode 的画布尺寸与 `Screen.safeArea` 不确定）：面板与输入框同宽；两行内容时高 = 内容高；内容超过一个小的 `maxHeight` 时高 = `maxHeight`；边界很矮时高 = `Room`，并处在预期的一侧
- [ ] **实现**：spec §6.1 的状态机（`_armed`、`IsOpen`、`Open()`、`Close()`、`OnOpened` / `OnClosed`）。内核的 `Measure` 回调按 §6.8 给尺寸：先 `LayoutRebuilder.ForceRebuildLayoutImmediate(content)` 量内容高，再经 `PopupPlacer.Solve(..., bounds: PopupBounds.For(rootCanvas), preferAbove)` 定侧，按 `Room` 夹高。侧在打开时记下，打开期间的推送只重夹高度。`InputField.OnAfterApply` 里不可交互 / `readOnly` 就关；`InputField` 另外重写 `Interactable` 的 setter（照 `TabMenu.cs:877-886`；今天它没重写，只在 `OnAfterApply` 同步 `_input.interactable`，`InputField.cs:150-155`），代码里关掉交互时同步 `_input.interactable`、收起列表（这条路径不经过 `OnAfterApply`）
- [ ] RUN(SuggestionsVisibilityTests) RUN(SuggestionsPlacementTests) RUN(TutorialSafeAreaTests) RUN(TutorialStepTests) RUN(TutorialRunTests)；lint

#### Task 15：高亮 API 与点行提交

**Files:** Modify `Suggestions.cs`；Create `Tests/EditMode/Controls/SuggestionsSubmitTests.cs`（先放这几条，Task 18 补全）

- [ ] **Red**：
  - `HighlightedIndex_set_clamps_and_is_ignored_while_closed`；`OnHighlightChanged_fires_on_change_only`
  - `Clicking_row_i_submits_it_and_closes`（`Suggestions.OnSubmit == (text, i)`，`InputField.OnSubmit` 不发，`EndEditing` 被调用）
  - `Reveal_offset_brings_a_row_fully_into_view`（纯函数：行上下沿 × 视口高 × 当前滚动 → 新滚动，上下越界各一例、已可见不动）
  - `Highlighting_the_last_row_scrolls_the_content`（接线：用 `PopupBounds.BoundsOverride` 定边界、`maxHeight` 取小值；读 `Content.anchoredPosition.y > 0`）
- [ ] **实现**：`HighlightedIndex`（行 `FocuslessPress.SetOn`）、`OnHighlightChanged`、`MoveHighlight(int delta)`（按 §6.2 循环含「无」）、`CommitChosen(int i)`、`CommitTyped()`；行 `OnClicked` → `CommitChosen`；`EnsureVisible(row)`（在 viewport 坐标里比上下沿，挪 content）
- [ ] RUN(SuggestionsSubmitTests)；lint

#### M2 收尾

- [ ] RUN 本里程碑全部新类 + RUN(InputBoxTests)（模态里的 `<InputField>` 不受影响）→ `git commit -m "feat(suggestions): <Suggestions> part element — rows, binding, placement, visibility"`

### M3 — `PuiInputField`

#### Task 16：`SuggestionKeys`（纯按键路由）

**Files:** Create `Runtime/Controls/Internal/SuggestionKeys.cs`、`Tests/EditMode/Controls/SuggestionKeysTests.cs`

- [ ] **Red**：用 `Event.KeyboardEvent("down" / "up" / "return" / "[enter]" / "escape" / "a" / "left" / "home" / "^v")` × 状态（列表开 / 关、有无高亮、组字中、组字刚结束、链本帧已消费 Esc），逐行断言 spec §6.2 表的动作与「是否交给 TMP」（`^` = Ctrl，`#` 是 Shift）
- [ ] **实现**：`internal enum KeyAction { PassToTmp, HighlightNext, HighlightPrev, CommitHighlighted, Swallow }`；`internal static KeyAction Route(Event e, in KeyState s)`
- [ ] RUN(SuggestionKeysTests)；lint

#### Task 17：`PuiInputField`

**Files:** Create `Runtime/Controls/Internal/PuiInputField.cs`；Modify `InputField.cs`（`AddComponent<PuiInputField>()`，把编辑边沿接到 `EditingChanged`，把列表状态与动作交给它）；Create `Tests/EditMode/Controls/PuiInputFieldTests.cs`、`Tests/PlayMode/Controls/InputFieldSuggestionsPlayTests.cs`

- [ ] **Red（EditMode，编辑态钩子 `SetEditingForTests(true)`）**：
  - 列表打开时 `ExecuteEvents` 的 `moveHandler`（上 / 下）移动高亮；有高亮时 `submitHandler` 是选中提交；`cancelHandler` 在 `EscapeChain.PopupConsumedFrame` 当帧被吞掉（测完调 `ForgetPopupConsumedFrameForTests()`）；列表关着时三者都走 `base`
  - `A_move_right_after_an_arrow_key_is_ignored`（P21：先 `HandleKeyDown("down")`，紧接着的 `moveHandler` 不再移高亮）
  - `Hiding_the_field_from_code_closes_the_list`（`field.Hidden = true` → `PuiInputField.OnDisable` → 关）
  - `InputField_OnAttached_toggling_enabled_before_adoption_is_harmless`（`InputField.cs:105-106` 的 enabled 开关会先触发一次 `OnDisable`，此时还没有 `<Suggestions>`）
- [ ] **Red（PlayMode，夹具照 `InputFieldNavPlayTests`）**：
  - 指针点击进入编辑（等两帧）→ 推送非空 → 面板打开
  - `EventSystem.SetSelectedGameObject(别处)` → 编辑结束 → 列表关
  - 手柄的 move / submit / cancel 驱动高亮、选中提交、关列表，且 East 不回滚文字
  - `InputTestFixture` 接手柄，列表打开、编辑中按十字键下 → 高亮移动（P17 的回归：接管时无条件 `Use()` 会让这一条永远不到 `OnMove`）
  - `InputTestFixture` 真按 Esc（照 `SubmitWakePlayTests.cs:17-72`）：链关掉列表、仍在编辑。真设备按键不会进 IMGUI 队列，TMP 那一侧「不回滚」由 `SuggestionKeys` 的路由单测与手测覆盖
  - 编辑中直接调 `HandleKeyDown(Event.KeyboardEvent(...))`（接管循环里每个 KeyDown 的处理体，提成 internal 方法）：`"a"` 照常进 TMP 且发 `OnValueChanged`；有高亮时 `"return"` 是选中提交、没有时是普通提交并结束编辑；`"down"` 移高亮且光标不动。只有外层的 `while (Event.PopEvent(...))` 一行靠手测
- [ ] **实现**：spec §6.6。重写 `OnUpdateSelected`：接管条件 + `Event.PopEvent` 循环，每个 KeyDown 交给 `HandleKeyDown`（路由 → 自己的动作，或 `KeyPressed` 转交 + `Finish` 处理）；`SelectAll` 命令；收尾 `UpdateLabel` + `textComponent.ForceMeshUpdate()`，**只在确实消费了某个 KeyDown 或正在组字时** `Use()`（P17）。另重写 `OnMove`（P21 的几帧抑制）/ `OnSubmit` / `OnCancel`，三者判断「编辑中」都读 `InputField.IsEditing`（可钩，P8），不直接读 `isFocused`；`LateUpdate`（`isFocused` 边沿）；`OnDisable`（发 `false`，对「还没有宿主 / 还没认领 `<Suggestions>`」要空安全）。组字判定照 TMP 的取法：`EventSystem.current?.currentInputModule?.input` 的 `compositionString`，取不到就 `Input.compositionString`；记上一帧状态，组字结束那一帧也不接管
- [ ] RUN(PuiInputFieldTests) RUNPLAY(InputFieldSuggestionsPlayTests) RUNPLAY(InputFieldNavPlayTests) RUN(InputBoxTests)；lint

#### Task 18：`OnInput`、提交语义、被盖住结束编辑、按下进行中推迟

**Files:** Modify `InputField.cs`、`Suggestions.cs`；补 `SuggestionsSubmitTests`、`InputFieldSuggestionsPlayTests`

- [ ] **Red**：
  - `OnInput`（全部 PlayMode：EditMode 里 `isFocused` 恒为假，「不发」的用例没有实现也会过；Esc 回滚在 EditMode 里根本发生不了，`DeactivateInputField` 在 `!m_AllowInput` 时直接返回，`TMP_InputField.cs:4426`）：进入编辑后 `ProcessEvent("a")` 会发；编辑中写 `TextValue` 不发；列表关着时 `cancelHandler` 回滚文字，发 `OnValueChanged` 但不发 `OnInput`；ReSolve 重放 `text=` 不发。`OnInput` 经 Task 14 的 internal `NotifyUserInput()` 武装列表（钩子保留，作为唯一入口）
  - spec §6.4 提交表逐行：Enter 无 / 有高亮（经 `SuggestionKeys` 动作）、点行、软键盘 Done（直接 `onSubmit.Invoke`）、两级导航下用 Submit 进入编辑不算提交；两事件的有无、参数与先后
  - `Editing_end_during_a_row_press_waits_for_the_release`（`pointerDown` 行 → `SetEditingForTests(false)` → 仍开 → 同行 `pointerUp` + click → 选中提交并关；另一例在别处抬起 → 关）
- [ ] **实现**：`OnInput` 判定（`isFocused` 且不在 `_programmaticWrite` 内，`TextValue` setter 包一层）；`InputField.OnSubmit` 收窄（选中提交不发）；`Suggestions.OnSubmit` 覆盖全部入口；`Covered` → `EndEditing()`（`DeactivateInputField`）；SG-D15 的推迟
- [ ] RUN(SuggestionsSubmitTests) RUNPLAY(InputFieldSuggestionsPlayTests)；lint

#### M3 收尾

- [ ] 三个程序集全量无新失败 → `git commit -m "feat(input): PuiInputField — keys belong to the suggestion list while it is open; OnInput"`

### M4 — `submitIcon`

#### Task 19：`<InputField submitIcon>`

**Files:** Modify `InputField.cs`；Create `Tests/EditMode/Controls/SubmitIconTests.cs`

- [ ] **Red**：
  - 写了 `submitIcon` 才建，`submitIconSize` / `submitIconColor` 生效
  - 文字区右侧内边距按点击区（输入框高度的正方形）加宽，`padding=` 显式值叠加其上
  - 点击：无高亮 → `InputField.OnSubmit(text)` + `Suggestions.OnSubmit(text, −1)`；有高亮 → 只发 `(text, i)`；没有 `<Suggestions>` 时只发 `InputField.OnSubmit`
  - 不可交互时显示禁用色、不提交
  - `GetEventHandler<ISelectHandler>(icon)` 是输入框；图标上没有 `Selectable`
  - XSD（EditorOnly）：`InputField_lists_submitIcon`（`submitIcon` / `submitIconSize` / `submitIconColor`）
- [ ] **实现**：内部节点 `SubmitIcon`（`Image` + `FocuslessPress`，状态 → 颜色用 uGUI `ColorBlock` 默认倍率）；`ApplyTextAreaPadding` 把点击区宽度加到右侧；点击走 Enter 同一条提交路径，编辑中则 `EndEditing()`
- [ ] RUN(SubmitIconTests) RUN(InputFieldTests) RUNEDITOR(XsdGeneratorTests) RUNPLAY(InputFieldRuntimeTests) RUNPLAY(InputFieldNavPlayTests)；lint → `git commit -m "feat(input): submitIcon — a focusless submit button inside the field"`

### M5 — lint

#### Task 20：`SuggestionsRules` + 扩展 + 运行期镜像 + XSD

**Files:** 补全 `Runtime/Core/Lint/SuggestionsRules.cs`、`Tests/EditMode/Lint/SuggestionsRulesTests.cs`（Task 12 建的骨架）；Modify `IRWalker.cs`、`DocumentLinter.cs`、`ScreenInstantiator.cs`、`NavTargetRules.cs`（P14）、`RaycastRules.cs`、`PaddingRules.cs`、`FontFloorRules.cs`、`ScrollbarRules.cs`、`.lint/UIXmlLint/README.md`（规则表）

- [ ] **Red**（写法照 `ScrollbarRulesTests.cs:19-55`：`IRWalker.Walk(UIDocumentParser.Parse(xml))` + `Has` / `First`；文档级规则用 `DocumentLinter.Walk`）：spec §7 每行一正一反；扩展规则各加一例，每例都要先红：`<Suggestions><Scrollbar/></Suggestions>` 不报 `PUI-SCROLLBAR-OUTSIDE`；一条 `PUI-RAYCAST-UNDECIDED` 用例（场景取放错位置、带 `color=` 的 `<Suggestions>`；写之前先读 `RaycastRules.cs:80-125`，确认加进 `InteractiveTags` 之前它确实会报——`InteractiveTags` 只影响这条规则；`<Suggestions raycastTarget>` 在标签进 `BuiltinTags` 后已经会报 `PUI-RAYCAST-TAG`，`RaycastRules.cs:58-75`，不能当 Red）；非整数 `padding` 报 `PUI-PADDING-INT`；行模板里的小字报 `PUI-FONT-FLOOR`。XSD 用例已在 Task 12 / 19
- [ ] **实现**：`SuggestionsRules`（`Tag`、各 `*Code` 常量、`Check(node, styles)`、`CheckOutside(parent, child)`、`CheckDuplicate(host)`、`CheckRows(UIDocument expanded)`）；`IRWalker` 照 `ScrollbarRules` 的三处挂法（`IRWalker.cs:226-233`、子循环 `:358`）；`DocumentLinter` 展开遍之后调 `CheckRows`（P15）；`ScreenInstantiator` 的逐节点分派加 `SuggestionsRules.Check`（照 `:245-247` 的 Scrollbar 一支）；各扩展表加 `Suggestions`
- [ ] RUN(SuggestionsRulesTests) RUN(ScrollbarRulesTests) RUN(RaycastRulesTests) RUN(PaddingRulesTests) RUN(FontFloorRulesTests) RUN(NavTargetRulesTests) RUN(BuiltinTagsTests) RUNEDITOR(XsdGeneratorTests)；`dotnet run --project .lint/UIXmlLint -- Runtime/Resources/`；scratchpad 里写一份故意出错的 `.ui.xml` 冒烟，各码都报
- [ ] lint → `git commit -m "feat(lint): PUI-SUGGEST-* rules"`

### M6 — 演示、文档、收尾

#### Task 21：演示（`Samples~/CommonControls`）

- [ ] 放大镜图标：新建 `Samples~/CommonControls/Resources/UI/icons.pxl`（一个 `search` sprite，按 `authoring-promptugui-pxl` skill 写），`dotnet run --project .lint/PxlPreview -- <file> --scale 16` 渲染后**看图**确认；XML 里引用为 `UI/icons#search`。Unity 不导入 `Samples~`，`icons.pxl.meta` 要手写：照抄 `Samples~/CommonControls/Sprites/crops.pxl.meta` 的 `ScriptedImporter` 块（`script` guid `52005a060a4e54a40b77f1ca5c4deaa4`），换一个新生成的 `guid`
- [ ] `CommonControls.ui.xml`：「表单输入」页 `username` 下面加一个搜索框 —— `<InputField id="cropSearch" class="input" placeholder="搜索作物" submitIcon="UI/icons#search">` + `<Suggestions itemTemplate="CropRow" maxHeight="200" …/>`，再加一行 `<Text id="cropResult">` 显示最近一次提交；模板 `CropRow`（作物图标 `farm:<name>` + 名字 + 两个 `<Show>`：`on="state-hover"` 的浅底与 `on="state-selected"` 的高亮底）；农场皮与玻璃皮各给面板一个同名属性包
- [ ] `CommonControlsRunner.cs` 的 `BindFormPage`：一份固定作物名单，`OnInput` 本地过滤，`BindItems(..., key: c => c.Id)`，`Suggestions.OnSubmit` 把「选中第几项 / 输入的文字」写进 `cropResult`
- [ ] 两份 `CommonControls.po`（en 填英文，zh 的 `msgstr` 同 `msgid`）补新文案；代码里的新文案进两份 `_code.po`；`package.json` 样例描述把「Forms」改为提到搜索建议
- [ ] `dotnet run --project .lint/UIXmlLint -- Samples~/CommonControls/Resources/` 零 error；样例编译验证照 virtualization 计划 Task 15 的临时 csproj 做法
- [ ] `git commit -m "docs(sample): crop search with suggestions"`

#### Task 22：阶段 1 文档（英文，同 PR）

- [ ] 照 spec §11「阶段 1」逐条：新 `reference/controls-suggestions.md`；XML `SKILL.md`（目录行、`<InputField>` 表、程序化 / raycast 列表、`### <Suggestions>` 桩）；`reference/states.md`；C# `SKILL.md`（`OnInput`、Suggestions 小节、速查表、两级编辑模型）；`AGENTS.md` 路由一条 + 开头的 reference 清单补齐；主 spec §5 控件表一行
- [ ] `<Suggestions>` 也是滚动条宿主：`reference/controls-scrollbar.md` 的宿主表（`:59-60`）加一行（条只显示、不接指针，P18）；XML `SKILL.md` 目录里 `<Scrollbar>` 那行与换皮说明（`:116`、`:131`）带上 `<Suggestions>`；`AGENTS.md` 的 `<Scrollbar>` 路由句里宿主列表加 `<Suggestions>`
- [ ] `git commit -m "docs(skills): <Suggestions>, OnInput, submitIcon"`

#### Task 23：收尾（阶段 1 的 PR）

- [ ] 三个程序集全量；`dotnet format --verify-no-changes --severity warn`；UIXmlLint 跑 `Runtime/Resources/` 与样例
- [ ] spec 状态改「已实施」，§16 补「实施记录（阶段 1）」：与设计的偏差、手测结论
- [ ] 隐私自检 → 征得同意后 `git push -u origin feat/input-suggestions` + `gh pr create`
- [ ] 给作者的手测清单（PR 描述里也列）：Windows 与 macOS 下中文 / 日文输入法组字时 ↑↓ / Enter 不被抢；手机真机（Android / iOS）点候选不收键盘、键盘挡住时面板的高度与侧；手柄十字键高亮、A 选中、B 关列表；键盘按住 ↓ 时高亮一行一行走、不跳行（P21）

---

## Self-Review 注记（写计划时已核）

- `ControlRegistry.Register<T>` 有 `new()` 约束（`ControlRegistry.cs:27`）→ `Suggestions` 要有无参构造。
- `ProceduralControl.Surface` 首次访问时才捕获宿主 `Image`（`ProceduralSurface.cs:64-70`）→ `Suggestions.OnAttached` 第一件事挂 `Image`；`color` 属性要自己声明并调 `Surface.SetFill`（同 TabMenu / Dropdown）。
- `<Show>` / `<Animation on="state-*">` 只在第一次 `OnAfterApply` 解析一次源（`Trigger.cs:44-50` 的 `_subscribed`），`@id` 形式要求源在控件自己的 GameObject 上（`TriggerSourceResolver.cs:159`）→ 行外壳必须先于模板实例化存在；行模板里写 `state-*@id` 指向别的控件不受影响。
- `FocuslessPress` 的 `StateBroadcaster` 用字段初始化（同 `PuiButton`），不放在 `Awake` / `OnEnable` 里 —— EditMode 不跑这两个，`ExecuteEvents` 却照样能调到它的指针处理。
- `StateBroadcaster.Recompute` 每次调用都跑全部重评估（`StateBroadcaster.cs:67-87`）→ `FocuslessPress` 只在状态真的变化时调 `SetTransient` / `SetOn`。
- `IStateSource.RefreshState` 只对 EventSystem 的选中项调用（`UI.Navigation.cs:78-85`）→ 非 Selectable 的实现留空即可。
- `UILog.Warn(node, issue)` 只打印 `issue.Message`（`UILog.cs:55`）→ 运行期警告的测试匹配文案，不匹配 code。
- 根 Overlay Canvas 的 `overrideSorting` 读回来不是 true（`RouterModalTabTests.cs:46-48`）→ 层序断言读 `sortingOrder`；嵌套的面板 Canvas 不受影响。
- `Router.Back()` 被守卫拒绝时同步抛（`UI.Router.cs:30-35`）；`ApplyRouteLayer` 的 Modal / Page 两支各算层序（`UI.Router.Layering.cs:58-75`）→ P16、Task 7。
- `NavTargetRules.SelectableTags`、`RaycastRules.InteractiveTags` 是 private（P14）；`ScrollbarRules.HostTags`、`PaddingRules.Tags` 是 public。
- TMP：`KeyPressed` / `SendOnSubmit` / `UpdateLabel` / `SelectAll` 是 protected；`OnUpdateSelected` / `LateUpdate` / `OnSubmit` / `OnCancel` / `OnMove` 可重写；`m_ProcessingEvent`、`m_WasCanceled`（用公开的 `wasCanceled`）、组字状态、`MoveUp` / `MoveDown`、`m_IsTextComponentUpdateRequired` 是 private（后者用 `textComponent.ForceMeshUpdate()` 顶替）。
- `TutorialSafeAreaTests` 直接调 `TutorialOverlayView.ApplySafeInset` → P6 保留转发。
- `PromptUGUI.Tests.EditMode` 不引用 Input System；`ModalEscapeListener.OnPerformed` 里区分 East 的代码在 `#if ENABLE_INPUT_SYSTEM` 内，EditMode 测试只走 `FireForTests` / `Dispatch`。
- 既有 TabMenu 测试是 M0 的唯一护栏 —— Task 4 不改任何测试文件，先跑绿再进 M1。
- 计划审阅补充（已逐条对过源码）：`InputSystemUIInputModule.ProcessNavigation` 看到 update-selected 被 Use 就整帧不发 move / submit / cancel（`:801-812`，P17）；uGUI `Scrollbar` 是 Selectable（P18）；`Selectable` 带 `[ExecuteAlways]`，`PuiInputField` 的 `OnDisable` 在 EditMode 也会跑，且会被 `InputField.OnAttached` 的 enabled 开关提前触发一次；`InputField.IsEditing` 已存在（P8）；`TMP_Dropdown.Show()` 会选中当前项（`:903`），所以真按 Esc 时 Dropdown 走 EventSystem Cancel（P9）；`ApplyCommon` 会改写 `hidden` 与几何，面板是控件自身 GameObject 时要在 `OnAfterApply` 拨回（P19）。

## Verification（端到端）

1. **单测**：每个 Task 的新测试绿；每个里程碑末尾零回归守门全绿；两个 PR 前三个程序集（EditMode / EditorOnly / PlayMode）全量无新失败。
2. **漏洞已修**（阶段 0）：Task 5 的四条复现全部转绿；`TabMenuTrapTests` 改写后的 Escape 四例语义与改写前一致。
3. **lint / CLI**：`dotnet format --verify-no-changes --severity warn` 无改动；UIXmlLint 对 `Runtime/Resources/` 与样例零 error；scratchpad 冒烟 XML 能看到每个 `PUI-SUGGEST-*`。
4. **演示**（宿主里跑 CommonControls）：搜索框打字出候选；↑↓ 高亮、Enter / 点行 / 放大镜提交，`cropResult` 显示「选中第几项」或「输入的文字」；Esc 只关列表、仍在编辑、文字不变；农场皮与玻璃皮下面板换皮；把输入框挪到画面底部时面板向上翻。
5. **手测**（作者，Task 23 的清单）：输入法、手机真机、手柄。
