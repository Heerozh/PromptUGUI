# 输入的正确性：被系统取消的触摸、按住期间的取消、重点不再发事件，以及下拉 / 菜单 / 轮播 / 引导的边角

> 状态：**已实现**（2026-11-23，实施记录见 §12）。决策已对齐：§6 的决策全部按推荐。分支 `fix/input-correctness`，叠在 `feat/ui-sfx`（PR #186）上；
> plan `docs~/superpowers/plans/2026-11-23-input-correctness.md`。
> 来源：`2026-11-23-ui-sfx-cues-design.md` §15 在调研中静态读到的既有问题 #1 / #2 / #3 / #4 / #5 / #9 / #10 / #11；之后逐条核实（静态
> 阅读 + 临时 PlayMode 探针），结论见 §1。共同点：**玩家的一次输入被解释错了** —— 取消被当成松手、按住期间该取消的没取消、什么都没
> 改变的点击被当作改变、禁用的控件仍然响应。
> 相关：`2026-11-16-drag-drop-design.md`（§7.7 取消、§7.10 等确认）、`2026-09-16-scrolllist-drag-reorder-design.md`、
> `2026-05-27-tabbar-design.md`、`2026-08-29-tabmenu-design.md`（TM-D10：选中某行收起）、`2026-06-04-carousel-design.md`、
> `2026-06-12-tutorial-system-design.md`（§3 输入拦截、§6 步骤流程）、`2026-06-30-nav-hidden-submit-wake-design.md`。

## 1. 问题（都已核实）

| # | 现象 | 核实 | 后果 |
|---|---|---|---|
| B1 | 拖动中触摸被**系统取消**（来电、通知栏、系统手势、多指超限、切后台）当作松手 | PlayMode 探针：`TouchPhase.Canceled` 与正常抬起结果完全相同 —— `Dropped`，`OnDrop` 1 次 | 东西被放进手指最后所在的目标；如果那是「出售 / 丢弃」格，数据变更不可逆。重排共用同一套手势，同样提交、发 `OnReordered`。违背拖放 spec §7.7「系统取消触摸要与松手区分开」|
| B2 | **按住期间**（`Pressed`，长按计时中，或 `hold = 0` 时还没拖动）拖放 spec §7.7 的取消条件都没生效 | 静态：`BeforeStructureChange` 在 `s_carry == null` 时直接返回，`OnLayerAbove` 只管 Dragging，`DragSession.Begin` 不再查可交互 | ① 期间被禁用：照样拿起，下一帧才被 `SourceAlive` 取消 —— 抬层、`<Show on="lift">`、目标的 `drop-armed` 闪一帧，C# 收到 `OnLift` 紧接 `OnEnd(Cancelled)`；② 期间源所在列表被推送：行是复用的，到点拿起的是**已经绑了新数据**的那一格 —— 拿错东西；③ Screen 开始关闭、上方开了模态：照样拿起 |
| B3 | 重点**已选中**的 Tab、**已开**的 radio Toggle 会重发选择事件 | 探针：`on="checked"` 的 `<Trigger>`、`Tab.OnSelected`、`TabBar.OnSelectionChanged`、`Toggle.OnValueChanged` 各多发 1 次，值没变 | 「选中弹一下」之类的 checked 动画重播；作者挂在这些事件上的刷新、请求、埋点、`Router` 跳转再跑一遍。重点当前页签是高频操作。与 TabBar spec、C# skill、`PuiToggle` 的注释写的都相反（uGUI `Toggle.Set` 在组里把值强制回 true 之后仍调 `onValueChanged(true)`）|
| B4 | 下拉列表淡出期间（约 0.15 s）推送选项 → 列表重新打开 | 探针 | 玩家刚关掉的列表又弹出来（排在 30000 层，盖住对话框），背板还吃掉下一次点击。语言下拉选完后异步换语言、刷新选项文字就是这个时机 |
| B5 | 引导的 Block 步结束后遮罩不关：整屏挡指针、没有洞 | 静态：`EndStep` 不动 `Mask.enabled` / `raycastTarget`，只在下一次 `BeginStep` 按下一步的模式改写 | 两步之间等玩家操作时（`await` 一个奖励弹窗、`OnClick.FirstAsync()`）纯触屏卡死：弹窗在遮罩之下点不到，v1 没有跳过按钮。只在 Block 步之后发生（Hint 步之后完全放开）—— 是上一步的残留，不是设计；违背 tutorial spec §3「Block 步骤**期间**生效」、§6「advance → 隐藏视觉」|
| B6 | TabMenu：点禁用的行，菜单收起 | 探针 | 玩家得重新打开；桌面系统的惯例是菜单保持打开 |
| B7 | TabMenu：`Open` 被拒（关闭回调里重入关掉了它）时仍发 `OnExpanded`、箭头朝上、`on="expand"` 触发 | 静态 | 罕见；事件顺序变成 `OnCollapsed` → `OnExpanded`，最终却是关着的 |
| B8 | 禁用的 Carousel 仍能滑动翻页（只有指示点失效）| 静态：`CarouselView` 的拖动处理不查可交互 | 选择器类用法（禁用期间等服务器）仍能换到别的项；同一个控件里指示点翻不了、手指翻得了 |
| B9 | 等确认（`Defer`）的停靠：被停靠的目标也被撤销武装 | 静态：`Park` → `Disarm(keepHover: true)` 仍对**每一个**已武装目标 `SetArmed(false)`，与 `Park` 自己的注释和 spec §7.10.1 都不符 | 纯视觉：停靠时目标多收一次 `drop-disarmed`，出现正常拖动里没有的「over 但没 armed」|
| B10 | 等确认期间源所在 Screen 开始关闭，等待要到 Screen 销毁才结束 | 静态：`SealInput` → `DismissUnder` → `BeforeStructureChange` 对 Pending 一律只「放开停靠」| 退场动画期间 `IsPending` 仍为 true（全局拿不起新东西），这段窗口里到达的 `Accept` 报 `Dropped`、在淡出的 Screen 上排飞行。违背 spec §7.10.5「源所在 Screen 开始关闭 → 等待结束」|
| B11 | 右键、中键也能打开 Dropdown | 静态：`TMP_Dropdown.OnPointerClick` 不看按键 | 与 Btn / Toggle / Tab 只认左键不一致；「右键 = 返回」的游戏里会误开列表 |

只补文档的三条（已对齐）：#4b Dropdown 的 Submit 不先唤回光标（nav-hidden-submit-wake spec 有意推迟，但 `navigation.md` 写成了
通用规则）；#9d 重排与拖放可以两指同时进行（spec 没禁止，交叉点都有兜底）；#10 只装旧 Input Manager 时，一次 Esc 可能同时关掉
下拉列表和它下面的弹层 / 对话框（两个 `Update` 的先后由引擎决定）。

## 2. 拖放与重排（B1、B2、B9、B10）

### 2.1 被系统取消的触摸 = 取消（B1）

`LiftGesture` 在两处结束手势：Dragging 的 `OnEndDrag` 调 `IHost.Release()`；Armed（拿起后没动）松手放回原处。两处都先问一句
「这是系统取消吗」：

- Input System：`ExtendedPointerEventData` 且 `pointerType` 是 Touch → 在它的设备上按 `touchId` 找到那个触点，`phase` 为
  `Canceled`。Canceled 状态要到下一次触摸才复位，结束事件到达时仍读得到；设备重置（失去焦点时 Input System 的默认行为）也会把触点
  置为 Canceled，一并覆盖；
- 旧 Input Manager：`Input.touches` 里 `fingerId == pointerId` 的那个，`phase == TouchPhase.Canceled`（旧的 UI 模块自己也认 Canceled，
  只是我们的松手自己重新射线，绕过了它）。

是取消 → 走新的 `IHost.Cancel()`：`DragSource` 走 `CancelDrag`（§7.7：不飞、不发 `OnDrop`、`OnEnd(Cancelled)`、不发
`drag.return` 音效）；`ReorderDriver` 走现成的 `Cancel()`（行回原位，不发 `OnReordered`，不发 `reorder.*` 音效）。再加一个
测试缝，让 EditMode 能强制回答。

### 2.2 按住期间的取消（B2）

§7.7 本来就写着「**Pressed** / Dragging 期间以下任一发生 → 立即复位 … 本次手势余下的 drag / end 事件吞掉」。补上 Pressed 这一半：

- `DragSession` 登记正在按住的源（一个集合 —— 两根手指可以各按住一个）。按下成功时加入，拿起 / 手势结束 / 被取消时移出；
- §7.7 的信号对它们同样生效：`BeforeStructureChange(root)`（列表推送、`ClearSlots`、源所在 Screen 开始关闭都经这里）覆盖 root 之下
  按住着的源；`OnLayerAbove(order)` 覆盖在那一层之下的；任何 ReSolve；源 `Hidden` / `Interactable = false` / `drag` 变为 none —— 按住
  期间源每一帧的 `Tick` 自检一次（spec 写的「setter 入口 + 每帧自检」）；
- 效果：手势复位为 Swallow（余下的事件吞掉，同 §7.7 的理由：半途交给 ScrollRect 一个没有 begin 的 drag 会让它猛跳），不拿起、不发
  任何事件或音效；
- `DragSession.Begin` 开头再拒绝一次不可交互的源，作为兜底；拿起音效那里的 `IsInteractable` 条件随之删去（能走到那里的都可交互）。

`ReorderDriver` 的 Pressed 已经随 `Cancel()` 复位，不改。

### 2.3 停靠与等确认（B9、B10）

- **B9**：`Park` 把被停靠的目标排除在撤销武装之外 —— 它保持 `drop-over` 并进入 `drop-pending`，其余目标照常 `drop-out` /
  `drop-disarmed`。它自己的 `drop-disarmed` 在等待结束（`Finish`）时发。
- **B10**：`DismissUnder` 把关闭的原因带下去（Screen 开始关闭 / 一行换了绑定的项）。等确认期间，**源**所在的 Screen 开始关闭 →
  等待结束（`OnEnd(Cancelled)`，目标 `drop-out`，不再挡新的拿起）；目标所在的 Screen 关闭、列表推送仍然只「放开停靠」（§7.10.5 原文）。
  EditMode 的 `Close()` 直接走销毁体，那里也补一次。之后才到的 `Accept` / `Reject` 同超时之后一样被忽略。

## 3. 重点不再发事件（B3）

**规则**：`<Tab>` / `<Toggle>` 只在值与「这次操作之前」不同时宣布新值。「操作」是一次点击、一次 Submit、一次 `IsOn` 写入，或者
组替它做的调整。

**实现**：`PuiToggle` 在 `base.OnPointerClick` / `base.OnSubmit` 之前、`Tab.IsOn` / `Toggle.IsOn` 的 setter 在写入之前，记下当时的
值；`Tab` / `Toggle` 自己的 `onValueChanged` 监听在这段括号里收到与它相同的值就丢掉。组把**别的**成员关掉是它们自己的值真的变了，照常
宣布。`SetIsOnWithoutNotify` 本来就不回调，不受影响。只在 `TabGroupCore` 里过滤不够：盖不住 checked 触发器和 radio Toggle ——
`<Trigger on="checked">` 订阅的就是控件的 `OnValueChanged`，在这一层丢掉就全部盖住了。

**连带**：

- 重点之后不再发：`Tab.OnValueChanged` / `OnSelected`、`TabBar` / `TabMenu.OnSelectionChanged`、`Toggle.OnValueChanged`、
  `<Trigger>` / `<Animation on="checked">`。`state-*` 本来就去重，不受影响；`allowSwitchOff="true"` 时点掉已选中的 Tab 是真的变化，照常发；
- TabMenu 重点当前行仍然收起（TM-D10），改由已有的 `OnActivated` 路径完成，`collapse` 音效不变；PR #186 为绕开重发而加的
  `Tab.InSfxRepick` 与 `OnSelectionSettled` 里的重点分支成为死代码，删掉；
- `PuiToggle.OnClicked` 注释里「重点被吞掉、什么都不发」那句从此属实。

## 4. 下拉、菜单、轮播（B4、B6、B7、B8、B11）

- **B4**：`Dropdown.SetOptions` 用「背板存在」（`PuiDropdown` 的 `IsListUp`，改为 internal）判断列表是否开着，不再用 TMP 的
  `IsExpanded`（淡出期间仍为 true）。淡出中的推送就地换掉选项，列表照常淡出关闭。sfx spec §7.5 已经以背板为准，这里对齐。
- **B11**：`PuiDropdown.OnPointerClick` 只认左键，与 Btn / Toggle / Tab 一致。
- **B6**：TabMenu 的 `OnActivated` 遇到不接受激活的行（`!tab.AcceptsActivation`：它自己或外层组被禁用）直接返回，菜单保持打开；
  `tab.deny` 音效照旧。
- **B7**：`TabMenu.Expand` 在 `_popupCore.Open()` 之后若仍未展开就返回，不转箭头、不通知 `<Show on="expand">`、不发 `OnExpanded`
  （Popover 已有同样的守卫）。
- **B8**：`CarouselView.OnBeginDrag` 判断 Carousel 自己与外层 `CanvasGroup` 是否允许交互（与 `DragSession.IsInteractable` 共用一个
  助手）；不允许 → 整个手势转交外层（外层 ScrollList 照常滚），不翻页、不发 `flip`。**自动播放不受影响**：`interactable` 管的是用户
  输入，自动播放有自己的开关（`interval="0"` / `Playing = false`）。

## 5. 引导的步与步之间（B5）

- `EndStep` 立刻放开输入：遮罩不再接射线，没有洞。
- 暗幕：按 tutorial spec §6「advance → 隐藏视觉」隐藏，但推迟到 overlay 的下一次 `Tick` —— 下一步在同一帧里开始（`await` 的续体
  是同步执行的，紧挨着的两步就是这样）就直接沿用，两步之间不闪（D-T1）。
- `t.Navigate` 期间透明地挡住输入，直到路由切换完成（D-T2）：今天 Block 步之后的导航被残留的遮罩挡着、Hint 步之后不挡，修完
  统一为挡 —— 引导驱动的转场不该被玩家的点击打断。

## 6. 待确认的决策

| # | 问题 | 推荐 | 备选 |
|---|---|---|---|
| D-B1 | 拖动中失去焦点（Alt-Tab：Input System 重置鼠标，照样发松手）| **不在本 PR**：编辑器里跑测试时编辑器常常没有焦点，拿 `Application.isFocused` 判断会把测试里的松手都变成取消；触屏的设备重置已由 B1 覆盖 | 监听 Input System 的设备重置事件，把重置之后的那次松手当作取消 |
| D-B2 | B2：按住期间被取消之后，这根手指余下的手势 | **吞掉**（§7.7 原文）| 交给外层滚动（同按下那一刻就不可交互时 `TryPress` 拒绝的效果）|
| D-B3 | B3 的去重基准 | **这次操作之前的值**（括号）| 「上一次宣布的值」：更简单，但 `SetIsOnWithoutNotify` 之类的静默写入会让它与真实值脱节 |
| D-T1 | B5：步与步之间的暗幕 | **步结束就隐藏，推迟到下一次 `Tick`**，紧挨着的下一步沿用 | 立刻隐藏（紧挨着的两步之间若隔了一帧会闪）；或保留暗幕只放开输入（暗着的屏幕可以点，观感奇怪）|
| D-T2 | `t.Navigate` 期间挡输入 | **挡**（透明，不暗）| 不挡（修完之后 Block 步之后的导航也不再挡）|
| D-B4 | B10 | **按 spec 修**（源所在 Screen 开始关闭 → 等待结束）| 改 spec 为「源被销毁」，承认现状 |
| D-B5 | #4b / #10 | **只补文档**（已对齐）| #4b：`PuiDropdown.OnSubmit` 补一行唤回门（EditMode 可测）；#10：`ModalEscapeListener` 加 `[DefaultExecutionOrder]`（宿主工程用 Input System，测不到旧输入路径）|

## 7. 公开行为的变化（写进 PR 描述）

1. 重点已选中的 Tab / 已开的 radio Toggle 不再发任何事件（B3）。
2. 被系统取消的触摸结束的拖动是 `Cancelled`，不再放进目标；重排不再提交、不发 `OnReordered`（B1）。
3. 按住期间源被禁用 / 列表被推送 / Screen 开始关闭 / 上方开了新层 → 不再拿起（B2）。
4. 禁用的 Carousel 不能滑动翻页；自动播放照旧（B8）。
5. 引导的步与步之间不再挡输入；`t.Navigate` 期间挡输入（B5）。
6. TabMenu 点禁用的行不再收起（B6）。
7. 右键 / 中键不再打开 Dropdown（B11）。
8. 等确认期间源所在 Screen 开始关闭 → 等待立即结束（B10）。

## 8. 测试（Red 先行）

| 项 | 层 | 用例 |
|---|---|---|
| B1 | PlayMode | `InputTestFixture` 的触屏：拖到目标上 → `SetTouch(id, TouchPhase.Canceled, …)` → `OnEnd(Cancelled)`、没有 `OnDrop`、没有落地音效；正常 `EndTouch` 仍是 `Dropped`；重排版：顺序不变、没有 `OnReordered` |
| B1 | EditMode | 测试缝强制「系统取消」，覆盖 Dragging 与 Armed 两个分支 |
| B2 | EditMode | `dragHold='0.3s'` + 按住探针：按下 → `Interactable = false` → 跨过 hold → 没拿起、没有 `OnLift` / `OnEnd`、没有抬层；按下 → 推送 → 没拿起；按下 → 开模态 → 没拿起；按下 → `UI.Close` → 没拿起；鼠标 `hold = 0` 版：按住不动期间被禁用，第一次移动不拿起 |
| B3 | EditMode | 真实 `InternalToggle`（`SfxTestKit.Click` 经 `ExecuteEvents`）：重点已选中的 Tab → 各事件与 `<Trigger on="checked">` 计数为 0；已开的 radio 同样；`allowSwitchOff` 点掉仍发；代码 `IsOn = false` 写给组里唯一开着的那个 → 不发；TabMenu 重点当前行仍收起、仍发 `collapse` |
| B3 | PlayMode | `SfxPlayTests` 里「真实点击已选中的 Tab」那条日志改成断言 `OnSelectionChanged` 为 0 |
| B4 | PlayMode | 打开 → 点背板关闭 → 淡出期间推送 → 0.4 s 后仍关着、`ExpandedCount` 为 0 |
| B5 | EditMode | `TutorialStepTests` 的做法：Block 步完成后遮罩不接射线；下一次 `Tick` 后暗幕隐藏；同一帧开始的下一步暗幕从未隐藏；`Navigate` 期间挡输入、完成后放开；Hint → Block、Block → Hint 各一条 |
| B6 / B7 | EditMode | 展开后点禁用的行 → 仍展开；`Open` 被拒 → `OnExpanded` 0 次、箭头未转 |
| B8 | EditMode | `CarouselDragTests` 的做法：禁用自身 / 禁用祖先 → 拖动 → `Current` 不变、没有 `flip`、外层收到转发；自动播放照转 |
| B9 / B10 | EditMode | `DragDropDeferTests` 的做法：停靠后目标仍 armed、`drop-disarmed` 计数 0，`Accept` 后为 1；等确认中 `UI.Close` 源所在 Screen → 立即结束、`Cancelled` |
| B11 | PlayMode | 右键点 Dropdown → 不展开 |

## 9. 文档同步（同一 PR，英文）

- XML skill：`reference/drag-drop.md`（取消清单补「系统取消的触摸」「按住期间」；等确认的两处；与重排不互斥）、`reference/reorder.md`
  （系统取消的触摸；与拖放不互斥）、`reference/controls-tabs.md`（重点不发事件；TabMenu 禁用行不收起）、`reference/controls-carousel.md`
  （禁用 = 指示点、卡内控件、滑动都失效，自动播放不受影响）、`reference/navigation.md`（Dropdown 的 Submit 是唤回规则的例外）、
  `reference/sfx.md`（TabMenu 禁用行、Carousel 禁用、Dropdown 左键的相应句子）；主 `SKILL.md` 的 Btn / Toggle / Dropdown 小节涉及处。
- C# skill：`OnSelected` / `OnSelectionChanged` / `OnValueChanged` 不在重点时发；拖放的 `OnEnd(Cancelled)` 原因补「系统取消的触摸」
  「按住期间」；Tutorial 小节写清步与步之间的输入；旧 Input Manager 下 Esc 的限制。
- sfx spec：§7.4（禁用行「照现状收起」）、§7.5（「任意鼠标键都会打开」）、§7.8（「禁用的 Carousel 仍能拖动翻页」）三句改为新行为；
  §18 里 `InSfxRepick` 的那段注明已随 B3 删除。
- 本文 §12 实施记录。

## 10. 非目标

- 拖动中失去焦点（D-B1）。
- 重排行在按住期间的可交互自检（重排的取消路径另有一套，未发现问题）。
- 重排与拖放的两指互斥（只写文档）。
- Carousel 的 `deny` 音效。

## 11. 里程碑（一个 PR，分步提交）

1. docs：spec + plan。
2. B1：被系统取消的触摸。
3. B2 + B9 + B10：按住期间的取消；停靠保留目标；源所在 Screen 关闭结束等待。
4. B3 + B6 + B7：重点不发事件；TabMenu 的两处。
5. B4 + B11：下拉。
6. B8：禁用的 Carousel。
7. B5：引导的步与步之间。
8. docs：skill、sfx spec 的相应句子、实施记录。

## 12. 实施记录（2026-11-23）

按 §11 分步提交，一个 PR。与正文的出入、实现中才看清的细节：

| 处 | 记录 |
|---|---|
| B1 | 判定是 `LiftGesture.IsSystemCancel`：Input System 下读触点的 `phase`（`touchId` 对上的那个），旧输入下读 `Input.touches`（`#if ENABLE_LEGACY_INPUT_MANAGER`，只装 Input System 时不碰 `Input`）。宿主的取消要在手势还是 Dragging / Armed 时调用，否则 `ReorderDriver.Cancel()` 会因为手势已不活跃而提前返回、什么都不恢复；所以顺序是「先 `IHost.Cancel()`，后置 Idle」。测试缝 `LiftGesture.CanceledProbeForTests` |
| B2 | `DragSession` 的 `s_pressed` 在 `TryPress` 通过时登记、按 `Gesture.Current` 懒清理；`BeforeStructureChange` / `OnLayerAbove` / `OnReSolve` / `OnReleased`（行被重新绑定）/ `OnSourceTurnedOff` 都覆盖它们，`DragSource` 按住期间每帧自检可交互。按住被取消 → `LiftGesture.CancelPress()` → Swallow（D-B2）。`Hidden` 的源 GameObject 不活跃，既不 Tick 也收不到拖动事件，自然不会拿起；`Begin` 兜底拒绝不可交互的源。可交互判定抽成 `Interaction.GroupsAllow`，与 B8 共用 |
| B10 | 没有改 `Screen`：停靠时订阅源所在 Screen 的 `OnClosing`（两段式关闭与立即销毁两条路径都会发它），等待结束时（`Finish`）释放 —— 也避开了与 close-reliability PR 在 `Screen.cs` 上的冲突 |
| B3 | 括号在 `PuiToggle`（`BeginOperation` / `EndOperation` / `IsUnchanged`）：点击、Submit 与 `Tab.IsOn` / `Toggle.IsOn` 的 setter 开括号，`Tab` / `Toggle` 的监听丢弃没变的回调。`SetIsOn(on, animate)` 都经 `IsOn`，自然覆盖 |
| B5 | D-T1 的「下一次 `Tick`」落成「一整帧没有步用过它」（两个 Tick）：`await t.Step(a); await Awaitable.NextFrameAsync(); await t.Step(b);` 这种隔一帧的写法也不闪。`t.Navigate` 用一块独立的透明挡板，不动遮罩的暗幕状态；挡的是指针，Esc 照旧（`IsBlockingInput` 不变）|
| 测试 | B1 的 PlayMode 用例是实现之后写的；它们的红态由调研时的临时探针在真实触屏上确认过（系统取消与正常抬起结果都是 `Dropped`）|

回归：EditMode / EditorOnly / PlayMode 全量，除 `main` 上同样失败的既有环境相关用例外全部通过；`dotnet format --verify-no-changes` 与
UIXmlLint 干净。

