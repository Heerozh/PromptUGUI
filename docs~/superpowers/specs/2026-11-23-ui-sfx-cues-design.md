# UI 音效钩子：`sfx=` 与 `UI.Sfx.OnCue` —— 用户真实操作时发一个全局事件，库不播放

> 状态：**已实现**（分支 `feat/ui-sfx`，plan `docs~/superpowers/plans/2026-11-23-ui-sfx-cues.md`；实施记录见 §18）。决策已对齐（2026-11-23：§13 的 SFX-D1…D18 全部按推荐）；独立审查已过，修订见 §17。
> 需求来源：一个要给整套 UI 配音效的游戏 —— 让通用控件统一挂上默认音，不逐份改 `.ui.xml`。库只负责发事件：
> 不持有 AudioClip、不播放；节流、随机变体、混音都由游戏侧做。PC 宽屏与手机竖屏同一份 XML。
> 相关：
> `2026-05-14-litmotion-animations-design.md`（`<Trigger>` 的「只报真实翻转」—— 本文同一原则，并且更严一档：代码赋值也不算）、
> `2026-09-16-close-transition-design.md`（两段式关闭：「开始关闭」才是关闭的时刻；`CloseImmediate` 是拆除）、
> `2026-10-01-router-page-layering-design.md`（页 / Modal / Loading / Toast / Tutorial 的层带 —— 谁是 Modal 由打开方决定）、
> `2026-11-16-drag-drop-design.md`、`2026-09-16-scrolllist-drag-reorder-design.md`（拿起、落地、飞回、取消各在什么时刻）、
> `2026-10-19-popover-design.md`、`2026-10-18-popup-core-and-input-suggestions-design.md`（弹层的开合原因与级联）、
> `2026-09-15-raycast-target-design.md`（禁用的控件仍然吞指针 —— 「拒绝」音因此接得到）、
> `2026-09-17-common-attr-runtime-state-design.md`（通用属性每次重应用的写入与自愈边界）。

## 1. 问题

1. **全局事件**：控件在用户真实操作时发一个全局事件，带上控件种类、动作、音效键和来源元素；游戏侧注册一个处理器去播放。
2. **要覆盖的动作**：
   - `<Btn>` 点击；`<Tab>` / `<TabBar>` / `<TabMenu>` 切换；`<Pages>` / `<Carousel>` 翻页；`<Toggle>` 开、关；
   - `<Dropdown>` / `<Popover>` / `<Collapsible>` 展开、收起；
   - Screen 打开 / 关闭；Modal 弹出 / 关闭；
   - 拖放：拿起、放进目标、没放进去弹回；`<ScrollList>` 重排后落位；
   - 点了不可交互的控件：单独一个「拒绝」动作（游戏要一声「这个点不了」）。
3. **默认键**：按「控件种类 + 动作」生成默认键，游戏侧按键对应到音频 —— 不写任何 XML 就有一套完整的默认音。
4. **XML 覆盖**：任意控件可写 `sfx="confirm"` 覆盖默认、`sfx="none"` 静音；能写在模板 / 样式里被继承（主按钮、取消按钮靠
   样式区分）；Toggle 开 / 关这类多动作控件的写法由库定；加这个通用属性之前先查现有 `.ui.xml`，别和模板的 `<Param>` 重名。
5. **只认用户操作**：代码赋值、ReSolve、初始状态、Screen 打开时回放的状态都不发事件。
6. **UIXmlLint 认得 `sfx`**；键名由游戏定义，lint 不校验。

**重名检查（需求 4 的前置条件）**：扫过包内全部 `.ui.xml`（含 Samples、测试数据、内置 Modal）与宿主工程：没有名为 `sfx` 的
`<Param>`，没有任何以 `sfx` 开头的属性，也没有叫 `Sfx` 的 `[UIAttr]`。

## 2. 注意点（踩坑表）

设计里的每一条决定都是在绕下面这些坑；§7 是细节，§13 是决策表。

| # | 坑 | 不处理的后果 | 本文的处理 |
|---|---|---|---|
| P1 | **现有的事件流都不分来源**。`Btn.OnClick` 也收 `onClick.Invoke()`；`Toggle` / `Tab.OnValueChanged` 收代码 `IsOn`、XML `isOn`、ReSolve、`BindItems`、Router `MapTab`（开屏之后 `tab.IsOn = true`，跟点击一模一样）、uGUI `ToggleGroup.EnsureValidState`（下一帧把第一个强制打开）；`Carousel.OnCurrentChanged` 收自动播放 / `GoTo` / `BindItems` / 变体；`Pages.OnSelectedChanged` 只有代码会触发；`Collapsible.OnExpanded` 收 C# 与 ReSolve；`TabMenu.OnCollapsed` 不带原因；`UI.Drag.OnEnd` 与源一侧的 `drop` 挂钩在任何结束（含拆除）都发；`IScreen.OnClosing` 在拆除时也发 | 挂在这些流上，音效会在开屏、切变体、服务器推数据、自动轮播时乱响 | **不在状态流上过滤，在输入入口上挂**（§7.1）：指针点击、确认键、拖动手势、点外面、Esc —— 这些入口天然只有用户。现有的「这是初始化」标志（`Screen.IsOpening` / `InstantState.Active` / `Control.InApplyPass` / `UILog.Applying`）漏掉 Router `ActivateTab`、自动播放、开屏之后的代码调用、`EnsureValidState`，不能拿来当过滤器（§3 R1） |
| P2 | **库内部的 `PuiButton` 不止属于 `<Btn>`**：Carousel 指示点、CenteredSlideBox 的卡片、TabMenu 的把手、Collapsible 的头部都是它；弹层背板是普通 uGUI `Button` | 在 `PuiButton` 上统一发 `btn.click`，点指示点会响成按钮，展开 TabMenu 会响两声 | `PuiButton` / `PuiToggle` / `PuiDropdown` 只**报告**「用户激活了 / 用户点了但不可交互」，由**拥有它的控件**决定种类和动作（§7.1）；内部按钮各归其主，没有主人的不发 |
| P3 | **重点已选中的 Tab、radio 组里已开的 Toggle，uGUI 仍会再发一次 `onValueChanged(true)`**（`Toggle.Set` 的 "Always send event when toggle is clicked"）。仓库里 `PuiToggle.OnClicked` / `TabMenu` 的注释说它被吞了，现有测试只走 `SimulateClickForTests`，没覆盖真实 uGUI 路径（§15） | 重点当前页签也响切换音 | 比较点击前后的**实际状态**：没变就不发（`deny` 除外，见 P8） |
| P4 | **一次点击引起多次状态翻转**：radio 组关掉旧的、TabBar 关掉旧 Tab、手风琴收起兄弟、Dropdown 选中后关列表、TabMenu 选中后收起、弹层打开时先把无关弹层以 `Replaced` 关掉、点外面一次关掉一串嵌套弹层（子先父后，同一个原因） | 一下点击响一串 | **只发用户直接造成的那一个**；后果一律不发（§6.2 第 3 条、§7） |
| P5 | **`<Pages>` 没有任何用户手势**：翻页只有 `Show` / `Selected` 一条路，全部来自 C#（多半写在某个按钮的 `OnClick` 里）或变体 ReSolve；`<Tab bind=…>` 只 `SetActive`，绑到页上是不支持的写法 | 让 Pages 自己发 `flip` 就违背需求 5（代码赋值不发），而且和触发它的按钮叠成两声 | **Pages 不发**；要翻页音就把键写在触发翻页的控件上：`<Btn sfx="pages.flip">`（§7.9，决策 SFX-D5）。`<Pages sfx>` 报 `PUI-SFX-INERT` 并给出这条配方 |
| P6 | **Carousel 的所有来源都汇进 `GoTo`**；自动播放默认开（5s）；拖动不到卡宽 20% 会回弹，不算翻页 | 自动轮播每 5 秒响一次；拖一下没翻过去也响 | 只认三个入口：拖动结束且**真的换了页**、点指示点换了页、CenteredSlideBox 点侧边卡片；自动播放、`GoTo` / `Next` / `Previous` / `Current=`、XML `current`、`BindItems`、resize 都不发（§7.8） |
| P7 | **禁用控件被点击时 uGUI 静默吞掉**（`Button.Press` / `Toggle.InternalToggle` / TMP `Show` 都先查 `IsInteractable()`），今天没有任何挂点。好在禁用只关 `CanvasGroup.interactable`、不关 `blocksRaycasts`（`Control.Interactable` 的注释写明是有意的），点击**确实落到**控件的 GameObject 上 | 「这个点不了」没法实现 | 在 `PuiButton` / `PuiToggle` / `PuiDropdown` 的 `OnPointerClick` / `OnSubmit` 覆写里判 `IsActive() && !IsInteractable()` → 报告「拒绝」。只认左键、只认 click（按下和松开在同一个对象上；拖动、滚动之后 uGUI 本来就不算 click）；经祖先 `CanvasGroup` 禁用的也算（§7.13） |
| P8 | **导航能聚焦到禁用控件**：显式 `navUp` / `navDown`… 目标不查可交互，`Screen.Focus(id)` 不查，祖先 `CanvasGroup` 禁用后焦点留在原处。另一方面，鼠标用过之后的第一次 Submit 只唤回焦点光标（`TryWakeOnSubmit`，会切换导航模式），什么都不做 —— 但只有 `PuiButton` / `PuiToggle` 有这道门，`PuiDropdown` 没有覆写 `OnSubmit`（§15） | 确认键按在禁用控件上没有反馈；或者唤回光标那一下误响 | 在禁用控件上 Submit 也算「拒绝」，判断放在唤回门**之后**；被唤回门拦下的那次 Submit 什么都不发。Dropdown 照现状：第一次 Submit 就会打开它，发 `expand`（不为音效改它的导航行为） |
| P9 | **输入封锁全是挡射线，不是关 interactable**：关闭中的 Screen 把根 `CanvasGroup.blocksRaycasts` 置 false（CLS-D5）；Modal 背板、Loading 背板、引导 `SpotlightMask` 都是接住射线的图；Toast 自己 `blocksRaycasts=false`。`drag=` 的拖动在源 Screen 开始关闭时被取消（`SealInput` → `PopupChain.DismissUnder` → `DragSession`），但**已经捕获的重排**不经过 `DragSession`，在关闭中的 Screen 上会继续，松手仍能提交 | （如果封锁是关 interactable，退场动画期间每点一下都会响「拒绝」） | 「拒绝」不会在封锁下误发。另立一条：**所属 Screen 已开始关闭时，它的控件一律不再发 cue**（§7.14） |
| P10 | **Screen 的开关没有「只算真实开关」的单一出口**：`Screen.Open()` 也被热重载重开、`PromptUGUIDocumentHost`（`[ExecuteAlways]`）、Router 孤儿页（导航已过期但仍被打开、跑过 `OnEnter`，随后被 `CloseImmediate`）走；`OnClosing` 在 `UnloadAll` / `ResetForTests` / 热重载 / `Dispose` 时也发；场景卸载外部销毁则一次都不发 | 热重载、进出 Play 模式、换场景时响一串开关音 | **正常关闭 = 走进 `Screen.Close()`**（含 EditMode、`IsOpening` 时直接销毁的分支）；直接 `CloseImmediate` 的一律是拆除，不发。打开由**打开方**发（§7.10），热重载重开不发、沿用旧实例的角色；孤儿页不发 |
| P11 | **库里没有「Modal」这个类型或标志**：对话框（`UI.Modal.*`）和 Router 的 `RoutePresent.Modal` 都是普通 `Screen`，Modal 身份是打开**之后**由弹窗队列 / `ApplyRouteLayer` 套上 layer 才确定的；Loading、Toast、引导遮罩也走同一条 `OpenModalScreen` 路 | 在 `Screen.Open()` 里分不清 Modal；每条 Toast、每次 Loading 都响「弹出」 | 打开方在套完 layer 之后给 Screen 记一个**角色**（`screen` / `modal` / 静默）并发 `open`；关闭时按这个角色发 `close`。Loading、Toast、引导遮罩定为静默（§7.10） |
| P12 | **对话框按钮关闭自己时的顺序**：`ModalEntry` 先 `TrySetResult`、后 `onClose`，而 Awaitable 续延同步执行 —— 调用方 `await` 之后立刻打开的下一个对话框，会**先于**当前这个开始关闭 | `modal.open`（下一个）排在 `modal.close`（当前）前面，按顺序做去重的游戏会误判 | 记为已知顺序，写进 skill：同帧的 cue 不要假设「关在前、开在后」。不为音效改 Modal 的结果时序 |
| P13 | **一次操作本来就会有多个合法的 cue**：按钮点击 + 它打开的 Modal / Popover（`on="click@btn"`）/ 它关掉的 Screen；Router 一次导航 N 个 close + M 个 open（Sequential 模式隔着退场时长）；开机时 HUD 与背景两个 Screen 同帧打开；点外面关掉弹层（按下时）+ 下面按钮的点击（松开时，可能隔几帧） | 叠音；但哪个该响是游戏的口味 | 库**全部按发生顺序发出、不去重**（需求明确节流归游戏侧）；skill 给 `ChunkFrame(1)` 同帧挑一个的配方；常驻 Screen 用 `<Screen sfx="none">` |
| P14 | **拖放的结果晚于松手**：`Control.OnDrop` / `UI.Drag.OnDrop` 在结果定下来之前发（后面的订阅者还能 `Reject` / `Defer`）；`Defer` 时有两个时刻（松手时停在目标上方、服务器回复时落地或飞回）；`DragResult` 把用户与系统混在一起（`Cancelled` 含 Esc，`Returned` 含代码的 `Reject`）；超时、迟到 / 重复的回复、`UI.Drag.Cancel()`、结构变动、ReSolve、关屏、上面开了一层都会结束拖动 | 松手就响「放进」结果被拒；系统取消也响「弹回」 | `land` 在落地飞行**开始**时发（与 `receive` 同一时刻；等确认的在 `Accept` 时）；`return` 在用户松手没放进 / 被拒（同步或等确认之后）/ Esc 取消时发；等确认的松手那一刻、超时、代码与系统取消、迟到的回复都不发（§7.11） |
| P15 | **重排**：松手落回原位只有 `drop` 挂钩、没有 `OnReordered`；取消全部来自代码 / 系统（推送、ReSolve、`Reorder=false`、`Dispose`）；重排不受 Esc、Modal、关屏影响 | 原位松手也响「落位」；推数据时响 | 位置变了发 `land`（在 `OnReordered` 之前），落回原位发 `return`，取消不发（§7.12） |
| P16 | **`lift` / `drop` 挂钩依赖 `LiftMarker`**：只有 XML 里写了这两个挂钩，源或行上才有 marker；没写就一个事件都不发 | 借道挂钩的话，没写挂钩的拖动就没声音 | cue 直接从 `DragSession` / `ReorderDriver` 发，不依赖 marker |
| P17 | **按下到拿起之间变成不可交互的拖动源仍会被拿起**：`DragSource.TryPress` 在按下时就拒绝不可交互的源，但长按期间（`dragHold`）才被禁用的，`DragSession.Begin` 不再查，下一帧 `SourceAlive` 才把它取消（拿起一帧又放回） | 拿起音之后什么都没有 | `Begin` 时源实际不可交互（含祖先 `CanvasGroup`，复用 `DragSession.IsInteractable`）就不发 `lift`；拿起本身的修正不在本需求内（§15） |
| P18 | **模板调用上的通用属性会双路由**：`TemplateExpander.ExpandInvocation` 先把同名属性给 `<Param>`，再把 `CommonAttrs` 里的属性拷到实例根上 —— `sfx` 成了通用属性之后，若模板有 `<Param name="sfx">`，调用处的值会同时进 Param 和根 | 根和里面的按钮都被改；或者根上多一个无效的 `sfx` | `sfx` 进 `CommonAttrs`（调用处写的落到实例根，与 `hidden` / `drag` 一样）；`<Param name="sfx">` **解析期报错**并建议改名（如 `buySfx`）；实例根若不发声，lint 报 `PUI-SFX-INERT` 并给 Param 配方 —— 不照搬 `click@` 的「找里面唯一的 Btn」穿透（§3 R5） |
| P19 | **样式 / 主题 / 变体清空后，控件私有属性会停在旧值**：`ControlAttributeApplier` 对解算为空的 `[UIAttr]` 是 `continue`，不是复位 | 切回不带 `sfx` 的主题后，按钮还在用上个主题的键；`sfx.mobile` 退出 mobile 后不复原 | `sfx` 和 `interactable` 一样**每次重应用都解算并写入**，空 = 回默认（自愈）；lint 把它算进自愈集：不报 `PUI-VARIANT-NO-BASE`、`PUI-THEME-STYLE-SHAPE` 也豁免它 |
| P20 | **`<Screen>` 用属性白名单**，不认识的属性静默丢掉 | `<Screen sfx="none">` 无声无息地失效 | parser 显式收 `sfx` 与 `sfx.<variant>`；XSD 的 `<Screen>` 显式声明 `sfx` |
| P21 | **关闭 Domain Reload（Enter Play Mode Options）的工程里，静态 `Subject` 跨 Play 会话存活**；游戏在启动时订阅一次，第二次进 Play 就订阅两次 | 一下响两声，越玩越多 | `UI.Sfx.OnCue` 背后的 `Subject` 在 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 里重建（这个阶段的先例：`UI.RuntimeSprites` 的注册表清空、`EscapeChain.ResetForPlaySession`），`ResetForTests` 也重建（先例：`DragSession.ResetForTests` 重建它的四个 `Subject`）。游戏不要在 `SubsystemRegistration` 阶段订阅 |
| P22 | **cue 先于控件自己的事件发**（§6.2 第 4 条）。坏掉的音效处理器若把异常抛回发送方，`OnClick` 就不会触发 | 音效 bug 变成按钮失灵 | 每次 `OnNext` 都包 `try/catch` + `Debug.LogException`（与 `DragSession.Emit` 同一写法）—— 不依赖 R3 自己的异常分流（R3 只有 DLL，行为没法从源码钉死） |
| P23 | **弹层的系统关闭会在用户操作中途同步发生**：Esc、`Push`、`DismissOutside` 会先把失效的条目以 `HostGone` 修剪掉；`AnchorLost` 在 LateUpdate、`HostGone` 修剪在下一帧、`EditEnded` 在第二个 LateUpdate | 用「当前有用户操作在进行」这种标志去判，会把这些顺带的关闭也算成用户的 | 按**关闭原因**和**被手势直接指向的那一条**判，不按时间窗（§7.6） |
| P24 | **TMP Dropdown 没有开合事件**，所有关闭都汇进非虚的 `Hide()` → `DestroyBlocker`；淡出 0.15s 期间 `IsExpanded` 仍为 true；`SetOptions` 会 `Hide` + `Show` 重开一个开着的列表。**Esc / B 不经过 `PuiDropdown`**：`Show` 会选中当前条目，输入模块把 Cancel 交给这个条目的 `DropdownItem.OnCancel` → `Hide()`（`DropdownEscapePlayTests` 的注释写明了这一点）；Esc 链的 `HideExpanded` 只在有 Esc 监听者（弹层 / Modal / 拖动中）时才走，而且 Modal 的监听者不绑手柄 B | 无从区分用户关闭与系统关闭；普通页面上的 Dropdown 按 Esc / B 收起没有音 | 在用户入口上挂：`OnPointerClick` / `OnSubmit` 覆写、背板按钮、`HideExpanded`（Esc 链）、选项条目的点击；在 `CreateItem` 覆写里给每个条目加一个 `ICancelHandler`（`ExecuteEvents` 会调同一对象上的每个处理器）认 Esc / B。「开着」以背板存在为准，不看 `IsExpanded`；条目 `isOn` 的初始化写入发生在 `CreateBlocker` 之前，以「背板已存在」为门（§7.5） |
| P25 | **开屏的音比入场动画早约一帧**（入场 motion 停一帧再起） | — | 可接受，记录 |
| P26 | **调用处写的通用属性会被主题样式的回写盖掉（既有 bug）**：模板体根的 `class=` 把样式贡献的名字记进 `StyleAttrNames`；调用处随后覆写 `Attributes[name]`，却没有把它从 `StyleAttrNames` 里拿掉；而只要有任何主题带 `<Style>`，`StyleMerger.ReMerge` 就在每次 `Screen.Open` 与 `ReSolve` 开头运行，先删掉 `StyleAttrNames` 里的名字、再铺回样式值 | `<DangerButton sfx="none">` 不切主题也会丢掉覆盖（`width` / `hidden` 等所有通用属性今天都有这个问题） | `ExpandInvocation` 把调用处写到实例根上的通用属性（基础值与变体）记进新的 `ElementNode.InvocationAttrNames`，并从 `StyleAttrNames` 里删掉它们；所有通用属性一并修好，加测试 |
| P27 | **lint 没有 warning 级别**：每条规则的发现都是 `Kind.Issue`，CLI 以非零退出（`LintRun`，只有 `Note` 不计数） | 一条「提示」也会挡住 CI；误报不可接受（`ThemeStyleRules` 的注释也这么说） | 三条 sfx 规则都**只报确定的错**，范围见 §8 |
| P28 | **元素能做哪些动作，有一部分到运行期才知道**：`drag` 常由调用处带到模板根上；`itemTemplate` 的行是不是重排行要看外层列表；`DragKind` / `DropKinds` / `ScrollList.Reorder` 是公开 setter，可以在 C# 里打开；一个 `<Style>` 往往同时给 Btn、Toggle、Collapsible 用 | 按元素过滤动作项（lint 报错、运行时丢弃）会打断 §5.4 / §7.12 自己的配方 | 运行时**不按元素过滤**动作项，只校验语法；lint 的 `ACTION` 只查内置节点上的行内写法，`lift` / `land` / `return` 处处允许，模板体根与样式值只查语法（§8） |
| P29 | **处理器同步改状态**：游戏的处理器（或 `OnValueChanged` 订阅者）在同一次点击里把 `isOn` / 选中项改回去 | 同一次激活响两声（`checked` 再 `unchecked`） | 每次激活、每个控件至多发一个 cue（锁存到这次激活结束） |
| P30 | **`IsOpening` 时 `Close()`**：Screen 在构建途中被关掉，直接走 `CloseImmediate` | 一个 `close` 没有对应的 `open` | 没发过 `open` 的 Screen 不发 `close` |

## 3. 否决的方案

- **R1 在状态流上加「是不是用户」的过滤**（复用 `IsOpening` / `InstantState` / `InApplyPass` / `UILog.Applying`）：见 P1，这几个标志只覆盖「构建与应用期间」，Router `ActivateTab`、自动播放、开屏后的代码调用、`EnsureValidState` 全漏。
- **R2 「用户手势作用域」**：手势分发期间发生的任何状态变化都算用户的。一个「全选」按钮翻十个 Toggle 就是十一声；Pages 翻页、弹层级联、radio 组关掉旧项也都会被算进来 —— 正好是 P4 要避开的。
- **R3 每个动作一个属性**（`sfxOn` / `sfxOff` / `sfxExpand`…）：属性面翻几倍，每个都要查 Param 重名、进 `CommonAttrs` / lint 镜像 / XSD 三处；样式里写两个、调用处改一个的「半合并」也没有实际用处。单属性 + 一句迷你语法（§5.1）就能表达同样的东西。
- **R4 库内去重 / 节流 / 优先级**：需求明确归游戏侧。库只保证每个 cue 都对应一次真实操作、顺序确定（§6.2）。
- **R5 模板实例照搬 `click@` 的「唯一 Btn」穿透**（`<ShopCard sfx="buy">` 自动作用到卡片里唯一的按钮）：给卡片加第二个按钮会悄悄改变 / 打断调用处的写法；而 lint 报 `PUI-SFX-INERT` + 一个改了名的 `<Param>` 更直白。
- **R6 `Action` 处理器**（`UI.Sfx.Handler = e => …`）：主 spec D18 禁止库暴露 `event` / `Action` 回调；`Observable<T>` 天然支持多订阅，还能直接用 R3 的帧算子做同帧去重。
- **R7 Pages 每次翻页都发 `flip`**：见 P5。
- **R8 内置对话框的按钮预设 `confirm` / `cancel` 这类键**：库只定义 `kind.action` 形式的默认键，不替游戏发明语义键；内置对话框的按钮是普通 `<Btn>`（`btn.click`），id 是 `ok` / `cancel` / `yes` / `no` / `close`，处理器可以按 `Source.Id` + `Screen.Name` 区分。

## 4. 方案总览

```
用户输入 ──► 输入入口（PuiButton / PuiToggle / PuiDropdown 的点击与确认、拖动手势结束、点外面、Esc）
              │  只报告「用户激活了 X」/「用户点了 X 但它不可交互」
              ▼
        拥有它的控件：判断真实发生了什么（状态真的变了？打开成功了？落进去了？）
              │  决定 kind（btn / toggle / tab …）与 action（click / checked / select …）
              ▼
        查键：元素自己的 sfx= → 规定的上一级（Tab → TabBar、行 → ScrollList、落地：目标 → 源）→ 默认键 kind.action
              │  none → 不发
              ▼
        UI.Sfx.OnCue : Observable<SfxCue>  ──►  游戏的处理器（映射到音频、节流、混音）
```

Screen / Modal 的开关不走输入入口：打开方在套完 layer 后发 `open`，`Screen.Close()` 开始关闭时发 `close`（§7.10）。

## 5. 作者面（XML）

### 5.1 `sfx` 属性与语法

任何元素都可以写 `sfx`（以及 `sfx.<variant>`），`<Screen>` 也可以。

```xml
<Btn sfx="confirm">…</Btn>                              <!-- 本元素自己的动作都用 confirm（deny 除外） -->
<Btn sfx="none">…</Btn>                                 <!-- 本元素自己的动作都静音（deny 除外） -->
<Toggle sfx="checked:switch_on unchecked:switch_off"/>  <!-- 按动作分别指定 -->
<Collapsible sfx="drawer collapse:none">…</Collapsible> <!-- 都用 drawer，只有收起静音 -->
<Btn sfx="deny:locked">…</Btn>                          <!-- 只改「拒绝」音 -->
<Btn sfx="none deny:none">…</Btn>                       <!-- 彻底静音 -->
<Btn sfx="{{clickSfx}}">…</Btn>                         <!-- 模板参数留空 = 等于没写 = 默认音 -->
```

- 值是若干**项**，以空白或逗号分隔；项是 `键` 或 `动作:键`。
- **键**：不含空白、`,`、`:` 的任意字符串，大小写敏感；`none` 是保留字，表示静音。键名由游戏定义，库与 lint 都不校验
  （`ui/confirm`、`Confirm`、`pages.flip` 都合法）。
- **动作**：小写的名字。内置控件的动作见 §5.2；自定义控件经 `RaiseSfxCue`（§6.1）可以有自己的动作名。
- **裸键**（不带 `动作:` 的项）至多一个；同一动作至多一项。
- **裸键与 `none` 不覆盖 `deny`**：`deny` 是库的拒绝反馈，不是控件自己的动作 —— 一个自己在 C# 里播音效、所以写了
  `sfx="none"` 的购买按钮，钱不够被禁用时仍应响「拒绝」。只有 `deny:键` / `deny:none` 能改它（SFX-D3）。
- 空值（或全是空白）= 没写。
- **运行时只校验语法**（与 lint 同一份解析）：语法错 → 每个节点、每个值警告一次，当作没写。运行时**不按元素过滤**动作项：
  写了一个这个元素不做的动作，只是永远查不到它 —— 拖放、重排可以由调用处、外层列表或 C# 在运行期才打开（P28）。
- 写错的检查在 lint：语法错是 `PUI-SFX-VALUE`，行内写了这个内置标签不可能用到的动作是 `PUI-SFX-ACTION`（§8）。

### 5.2 动作表

| 元素 | kind | 动作 | 用户操作 | 默认键 | 查找链 |
|---|---|---|---|---|---|
| `<Btn>` | `btn` | `click` | 左键点击 / 手柄·键盘确认 | `btn.click` | Btn |
| | | `deny` | 点了 / 确认了不可交互的 Btn | `btn.deny` | Btn |
| `<Toggle>` | `toggle` | `checked` · `unchecked` | 点击把它打开 · 关上 | `toggle.checked` · `toggle.unchecked` | Toggle |
| | | `deny` | | `toggle.deny` | Toggle |
| `<Tab>` | `tab` | `select` | 点了一个**没选中**的 Tab | `tab.select` | Tab → 所在 `<TabBar>` / `<TabMenu>` |
| | | `deselect` | `allowSwitchOff="true"` 时点掉当前 Tab | `tab.deselect` | 同上 |
| | | `deny` | | `tab.deny` | 同上 |
| `<TabMenu>` | `tabmenu` | `expand` · `collapse` | 点把手展开；点外面 / Esc / 重选当前项收起 | `tabmenu.expand` · `tabmenu.collapse` | TabMenu |
| | | `deny` | 点了不可交互的把手 | `tabmenu.deny` | TabMenu |
| `<Dropdown>` | `dropdown` | `expand` · `collapse` | 点开；点背板 / Esc / 重选当前项收起 | `dropdown.expand` · `dropdown.collapse` | Dropdown |
| | | `select` | 选了**另一个**选项（列表随之关闭，不再发 `collapse`） | `dropdown.select` | Dropdown |
| | | `deny` | | `dropdown.deny` | Dropdown |
| `<Popover>` | `popover` | `expand` · `collapse` | `on="click@…"` 的按钮打开 / 再点关闭；点外面、Esc 关闭 | `popover.expand` · `popover.collapse` | Popover |
| `<Collapsible>` | `collapsible` | `expand` · `collapse` | 点头部 | `collapsible.expand` · `collapsible.collapse` | Collapsible |
| | | `deny` | 点了不可交互的头部 | `collapsible.deny` | Collapsible |
| `<Carousel>` | `carousel` | `flip` | 拖动翻过一页 / 点指示点换页 | `carousel.flip` | Carousel |
| `<Screen>`（页、普通 Screen） | `screen` | `open` · `close` | 每次正常打开 · 开始关闭（不限来源，§7.10） | `screen.open` · `screen.close` | Screen |
| `<Screen>`（作为对话框打开） | `modal` | `open` · `close` | 同上 | `modal.open` · `modal.close` | Screen |
| `drag=` 的源 | `drag` | `lift` | 拿起 | `drag.lift` | 源 |
| | | `land` | 放进目标并被接受（落地飞行开始时；等确认的在 `Accept` 时） | `drag.land` | **目标 → 源** |
| | | `return` | 松手没放进 / 被拒（含等确认后被拒）/ Esc 取消，回到原处 | `drag.return` | 源 |
| `<ScrollList reorder>` 的行 | `reorder` | `lift` · `land` · `return` | 拿起一行 · 放到新位置 · 放回原位 | `reorder.lift` · `reorder.land` · `reorder.return` | 行的根 → ScrollList |

下面这些元素还会**替别人被查**：`<TabBar>`（只被查，自己不发）与 `<TabMenu>`（替它的 Tab 查 `select` / `deselect` /
`deny`）、写了 `reorder` 的 `<ScrollList>`（替它的行查）、写了 `drop=` 的目标（替落进来的东西查 `land`）。一个元素的
**有效动作** = 它自己的动作 ∪ 它被查的动作（例如写了 `drag=` 的 `<Btn>`：`click` / `deny` / `lift` / `land` / `return`）；
裸键覆盖其中除 `deny` 外的全部。有效动作只给 lint 用（§8），运行时不拿它过滤（§5.1）。

**身兼几职的元素写 `动作:`，不写裸键**：可点的重排行 `<Btn sfx="select">` 的裸键同时盖住它的 `lift` / `land` / `return`；
可点的放置槽 `<Btn drop="item" sfx="slot">` 的裸键会盖住每一次落进来的 `land`（目标先于源被查，源上的 `land:` 也让位）。
这种元素写 `动作:`：行写 `sfx="click:select"`，槽写 `sfx="click:slot"`。

`<Pages>` 没有动作（§7.9）。其余标签 v1 没有动作（§14）。

### 5.3 查找顺序

动作 A 发生在元素 E 上，按 E 的查找链从前往后，对每个元素：

1. 它的 `sfx` 有 `A:键` → 用这个键（`none` → 不发）；
2. 否则 A 不是 `deny` 且它有裸键 → 用裸键（`none` → 不发）；
3. 否则看链上的下一个。

链走完都没定 → 默认键 `kind.A`。例：

```xml
<TabBar sfx="select:page_turn">
  <Tab>背包</Tab>                     <!-- 切到它：page_turn -->
  <Tab sfx="special">商店</Tab>       <!-- 切到它：special（自己的裸键先于 TabBar） -->
  <Tab interactable="false">锻造</Tab><!-- 点它：tab.deny（TabBar 的 select: 管不到 deny） -->
</TabBar>
```

### 5.4 模板、样式、主题、变体

- **样式**：`sfx` 就是普通属性，`class=` 照常合并 —— 主按钮、取消按钮靠样式区分：

  ```xml
  <Style name="btn-primary" sfx="confirm" color="accent"/>
  <Style name="btn-cancel"  sfx="cancel"  color="muted"/>
  <Btn class="btn-primary">确定</Btn>
  ```

- **模板体**里写的 `sfx` 是该模板的默认；**调用处**写的 `sfx` 落到实例根上（`sfx` 在 `TemplateExpander.CommonAttrs` 里，
  与 `hidden` / `drag` 同类），覆盖模板体根上的值；调用处的 `class=` 也可以带 `sfx`：

  ```xml
  <Template name="DangerButton"><Btn sfx="danger" color="red">…</Btn></Template>
  <DangerButton sfx="none">删除草稿</DangerButton>     <!-- 这一个静音 -->
  ```

  模板体根的 `sfx` 来自样式（`<Btn class="btn-danger">`）时，调用处的覆盖同样要扛住主题样式的回写 —— 今天扛不住（P26），
  本需求一并修好。

- **实例根不发声**（例如根是 `<VStack>`、按钮在里面的卡片）时，调用处的 `sfx` 什么都不做 → `PUI-SFX-INERT`。要让调用处
  改里面按钮的音，模板暴露一个**别的名字**的参数：

  ```xml
  <Template name="ShopCard">
    <Param name="buySfx" default=""/>                  <!-- 空默认 = 默认音 -->
    <VStack> … <Btn id="buy" sfx="{{buySfx}}">购买</Btn> </VStack>
  </Template>
  <ShopCard buySfx="purchase"/>
  ```

  `<Param name="sfx">` 是**解析错误**（P18）。
- **主题**：主题里的 `<Style>` 也可以带 `sfx`（比如一套 8-bit 主题换一套键）；切主题时按新主题重算。
- **变体**：`sfx.mobile="none"` 合法，**不需要基础值** —— `sfx` 每次重应用都解算并写入（自愈，SFX-D13）。
- 各层之间是**整值替换**：调用处 / 行内的 `sfx` 整个盖掉样式或模板体的 `sfx`，不按动作逐项合并 —— 与其它所有属性同一条规则。

### 5.5 `<Screen sfx>`

```xml
<Screen name="Shop" sfx="open:shop_open close:none"> … </Screen>
<Screen name="Hud"  sfx="none"> … </Screen>                <!-- 常驻、开机就开的 Screen -->
```

- 动作 `open` / `close`；`sfx.<variant>` 合法。
- 种类不由 XML 决定，由**怎么打开的**决定：`screen` 或 `modal`（§7.10）。同一份 `sfx` 对两种都适用。
- 内置对话框（MessageBox / InputBox / MarkdownBox / CenteredSlideBox）不写 `sfx`，发默认的 `modal.open` / `modal.close`；
  它们的按钮是普通 `<Btn>`。Loading、Toast、引导遮罩是静默的（不用写 `sfx="none"`）。

### 5.6 示例

```xml
<PromptUGUI version="1">
  <Style name="btn-primary" sfx="confirm"/>
  <Style name="btn-cancel"  sfx="cancel"/>

  <Screen name="Settings" sfx="open:panel_open close:panel_close">
    <VStack anchor="center" width="480" height="hug" spacing="12">
      <Toggle id="music" sfx="checked:switch_on unchecked:switch_off">音乐</Toggle>
      <Dropdown id="quality"/>                               <!-- dropdown.expand / select / collapse -->
      <Collapsible id="advanced" sfx="collapse:none">        <!-- 展开 collapsible.expand，收起静音 -->
        <Header>高级</Header>
        <Toggle id="vsync">垂直同步</Toggle>
      </Collapsible>
      <HStack height="44" spacing="8">
        <Btn id="save"  class="btn-primary">保存</Btn>       <!-- confirm；禁用时 btn.deny -->
        <Btn id="close" class="btn-cancel">关闭</Btn>        <!-- cancel -->
      </HStack>
    </VStack>
  </Screen>
</PromptUGUI>
```

## 6. C# 面

### 6.1 API

```csharp
namespace PromptUGUI.Application
{
    public static partial class UI
    {
        /// <summary>UI sound cues: one per real user action (plus Screen / Modal open and close). The library never plays anything.</summary>
        public static class Sfx
        {
            /// <summary>Every cue, in the order they happen: synchronous, on the main thread, inside the input dispatch.</summary>
            public static Observable<SfxCue> OnCue { get; }
        }
    }

    public readonly struct SfxCue
    {
        public string Kind { get; }        // "btn" / "toggle" / "tab" / … / "screen" / "modal" / "drag" / "reorder"
        public string Action { get; }      // "click" / "checked" / "select" / "expand" / … / "deny"
        public string Key { get; }         // what to play: the key sfx= decided, else DefaultKey
        public string DefaultKey { get; }  // Kind + "." + Action — the fallback for a custom key the game has not mapped
        public IControl Source { get; }    // the control acted on (Btn / Toggle / Tab / Dropdown / …, the dragged control, the list row); null for screen / modal
        public IControl Target { get; }    // drag.land: the drop target; otherwise null
        public IScreen Screen { get; }     // the Screen the cue belongs to (screen / modal: the Screen itself; drag.land: the target's)
    }
}
```

给自定义控件（继承 `Control` 的游戏控件）一个入口，让它的用户操作也走同一套 `sfx=` 解析（SFX-D14）：

```csharp
public abstract class Control
{
    /// <summary>
    /// Raise a cue for a user action of this custom control, resolved against its sfx= like a built-in.
    /// Kind is the registered tag in lower case. Call it only for real user input.
    /// </summary>
    protected void RaiseSfxCue(string action);
}
```

v1 不提供控件上的 C# `Sfx` 读写属性（SFX-D14）：键在 XML 里定，运行时的特例在处理器里按 `Source` 处理。

### 6.2 契约

1. **只认用户**：每个控件 cue 都对应一次用户输入 —— 指针点击、确认键、拖动手势、点外面、Esc。代码调用、属性应用、
   ReSolve、初始状态、开屏时回放的状态、拆除都不发。Screen / Modal 的 `open` / `close` 是生命周期，不限来源（§7.10）。
2. **只认真的变化**：状态没变就不发 —— 重点已选中的 Tab、重选当前选项、拖动不足一页、打开被拒（`Popover.CanOpen`）。
   `deny` 例外：它报告的正是「什么都没发生」。
3. **一次操作只发它直接造成的那一个**：后果不发 —— radio 组关掉的旧项、关掉的旧 Tab、手风琴收起的兄弟、选中后关闭的
   列表 / 菜单、级联关闭的子弹层、被 `Replaced` 顶掉的弹层。
4. **顺序**：同步、在主线程、在输入分发之内。控件的 cue **先于它自己的 C# 事件**（`OnClick` / `OnValueChanged` /
   `OnSelected` / `OnSelectionChanged` / `OnExpanded` / `OnOpened` / `OnCurrentChanged` / `UI.Drag.OnLift` / `OnReordered`），
   所以由这些事件引起的 cue（处理器里打开的 Modal）排在后面。例外：拖放的 `land` / `return` 跟着结果走，在
   `OnDrop`（C# 在那里做决定）之后。Screen 的 `close` 在 `OnClosing` 之前。
5. **不去重**：一次手势可以有多个 cue（P13），全部按顺序发出。但**一次激活、一个控件至多一个 cue**：处理器在同一次
   点击里把状态改回去，不会再响第二声（P29）。
6. **处理器异常不影响 UI**（P22）。
7. **处理器里只播音，不要改 UI**：它同步运行在点击分发的中途。
8. **关闭中的 Screen 不再发控件 cue**（§7.14）；没发过 `open` 的 Screen 不发 `close`（P30）。
9. **每次进 Play 重建**：关闭 Domain Reload 时，上个会话的订阅不会留下来（P21）；游戏不要在 `SubsystemRegistration`
   阶段订阅。

### 6.3 示例

```csharp
// 启动时订阅一次
UI.Sfx.OnCue.Subscribe(Play).AddTo(this);

void Play(SfxCue c)
{
    // 自定义键没配就回落默认键；整个 *.deny 可以共用一个音
    if (!_bank.TryGetValue(c.Key, out var clip)) _bank.TryGetValue(c.DefaultKey, out clip);
    if (clip != null) _ui.PlayOneShot(clip);
}

// 同帧只响一个：按种类排优先级（modal / screen 先于 btn.click）
UI.Sfx.OnCue
    .ChunkFrame(1)
    .Subscribe(batch => Play(batch.OrderBy(Priority).First()))
    .AddTo(this);

// 运行时特例：内置 MessageBox 的确定键
if (c.Kind == "btn" && c.Source?.Id == "ok" && c.Screen?.Name == "PromptUGUI/Modals/MessageBox.ui") …
```

`ChunkFrame` 这类帧算子与 `AddTo(MonoBehaviour)` 来自 R3.Unity（帧提供者）；库本身只依赖 R3 核心，这两样是游戏侧代码的事。

## 7. 语义细节

### 7.1 「用户操作」从哪里来：输入入口

| 入口 | 位置 | 说明 |
|---|---|---|
| 指针点击 | `PuiButton` / `PuiToggle` / `PuiDropdown` 的 `OnPointerClick` 覆写 | 左键；uGUI 的 click（按下与松开在同一对象上，拖动 / 滚动之后不算） |
| 确认键 | 同上的 `OnSubmit` 覆写 | `PuiButton` / `PuiToggle`：被 `UI.Navigation.TryWakeOnSubmit` 拦下的那次（只唤回光标）什么都不发，拒绝判断在这道门之后。`PuiDropdown` 今天没有这道门（§15），照现状 |
| 拖动手势 | `CarouselView.OnEndDrag`、`DragSession.Begin` / `Release`、`ReorderDriver.Lift` / `Commit` | |
| 点外面 | `PopupChain.DismissOutside`（按下即关，任意键 / 笔 / 触摸）、`PopupCore` 背板 `Button`（左键松开）、TMP Dropdown 背板 | |
| Esc / Start / B | `EscapeChain` 关链顶、`PuiDropdown.HideExpanded`、Dropdown 当前选中条目上的 `ICancelHandler`（§7.5）、拖动中 Esc 取消 | 引导的阻断步吞掉 Esc，自然不发 |

三个 `Pui*` 子类**不决定**发什么：它们向所属控件报告「用户激活」（携带激活前是否可交互），由所属控件判断结果、
决定 kind 与动作（P2）。`onClick.Invoke()`、`isOn =`、`SimulateClickForTests` 都不经过这些覆写 → 不发 cue；测试用
`ExecuteEvents.Execute(go, pointerEventData, ExecuteEvents.pointerClickHandler)` 模拟真实点击（仓库里已有先例）。

`TextLinkClicker` 把不在链接上的点击转发给外层 `<Btn>` → 算这个 Btn 的点击，正确。

### 7.2 `<Btn>`

- `click`：在 `OnPointerClick` / `OnSubmit` 覆写里、`base` 之前发（因此先于 `onClick` → `OnClick`）。右键、中键不发。
- `deny`：见 §7.13。
- 作为拖动源被拿起时，拿起会结束这次按压（`DragSession.EndPress`），之后没有 click —— 只有 `drag.lift`。

### 7.3 `<Toggle>`

- 用户点击 / 确认使 `isOn` 真的变了 → `checked` / `unchecked`；在 Toggle 自己的 `onValueChanged` 监听里、推给
  `OnValueChanged` 之前发（`PuiToggle` 在 `base` 调用前后标记「用户激活中」，监听里看这个标记并比较新旧值）。
- radio `group=`：只发被点的那个（`checked`）；被组关掉的旧项不发。重点组里已开的那个 → 状态没变，不发（P3）。
- `IsOn` / `SetIsOn` / XML `isOn` / ReSolve / `ToggleGroup` 在 `Start` 时强制打开第一个 → 不发。

### 7.4 `<Tab>` / `<TabBar>` / `<TabMenu>`

- `tab.select`：用户点了一个没选中的 Tab；同 §7.3 的机制，在 Tab 的监听里、`OnValueChanged` / `OnSelected` 之前发，
  因此也先于 `TabGroupCore` 的 `OnSelectionChanged`。被关掉的旧 Tab 不发。
- 重点当前 Tab：`allowSwitchOff="false"`（默认）→ 不发（P3）；`allowSwitchOff="true"` → 当前 Tab 被点掉 → `tab.deselect`。
- 代码与系统路径全部不发：`IsOn` / `SetIsOn` / `ClearSelection`、XML `isOn`、`SyncInitialSelection` 的自动选中、
  ReSolve、`BindItems`（含键控重宣告）、Router `MapTab`（`ActivateTab`）、`EnsureValidState`、`EnforceExclusive`。
- 查找链 Tab → 所在 TabBar / TabMenu（§5.3）。
- **TabMenu**：
  - `expand`：把手被点（包住把手的 `onClick` 监听），且菜单**真的打开了**（`OnExpanded` 在 `Open` 被拒时也会发，§15，
    不能拿它判）。
  - `collapse`：原因是 `Escape` 或 `OutsidePress`（背板 —— 背板压在把手上面，开着时点把手其实点的是背板）；导航
    未关进菜单时在把手上 Submit 收起；重选当前项（选择没变、菜单收起）。
  - 选了另一项：只发 `tab.select`（在 Tab 监听里，先于 TabMenu 自己订阅 `SelectionChanged` 触发的收起）；随之而来的收起不发。
  - 点了不可交互的行：`tab.deny`；菜单照现状收起（§15），不另发 `collapse`。
  - `Covered` / `Replaced` / `HostGone` / `AnchorLost` / 父弹层级联 / `Interactable=false` / `OnHostAfterApply` / C# `Collapse()` → 不发。

### 7.5 `<Dropdown>`

TMP 实现，不走 `PopupCore`，自己没有开合事件（P24）。

- 「开着」= 背板存在（`PuiDropdown` 的 `s_expanded` 判据），不看 `IsExpanded` —— 后者在淡出的 0.15s 里仍为 true。
- `expand`：`PuiDropdown.OnPointerClick` / `OnSubmit` 覆写，`base` 之后从「没背板」变成「有背板」才发。TMP 对任意鼠标键都会
  打开、`OnSubmit` 没有唤回门（§15）—— cue 跟着实际发生的事走。
- `select`：用户选了另一个选项。选项条目的监听以「背板已存在」为门（`Show` 里条目 `isOn` 的初始化写入发生在
  `CreateBlocker` 之前），据此在 Dropdown 的值变化监听里、`OnSelected` 之前发；选中后的关闭不发。
- `collapse`：点背板；Esc / B —— 输入模块把 Cancel 交给**当前选中的条目**（`DropdownItem.OnCancel` → `Hide()`），所以在
  `CreateItem` 覆写里给每个条目加一个 `ICancelHandler` 来认它；Esc 链的 `HideExpanded`；重选当前选项（值没变、列表关闭）。
- 不发：`HideBelow`（`Covered`）、`SetOptions` 的 `Hide` + `Show`、`OnDisable`、销毁、`Value=`、`value=` 的 ReSolve。
- `deny`：不可交互时左键点击 / 确认。
- TMP 的 `Hide` 走协程，开合要在 PlayMode 里用真实输入测（仓库已有 `DropdownEscapePlayTests` 的做法）。

### 7.6 `<Popover>`

- `expand`：只有 `on="click@id"` 的触发按钮能由用户打开（`ToggleFromTrigger`）；**真的打开了**才发（`CanOpen` 会静默
  拒绝），在 `OnOpened` 之前。触发按钮自己的 `btn.click` 照发 —— 一次点击两个 cue（P13）。
- `collapse`：关闭原因是 `Trigger` / `Escape` / `OutsidePress`，并且只发给**被手势直接指向的那一层**：Esc → 链顶那一层；
  再点触发按钮 → 这一个；点外面 → 被它关掉的最下面那一层（`DismissOutside` 的起点 / 背板的主人）。随它一起被关掉的
  子弹层、被 `Replaced` 顶掉的无关弹层不发（P4、P23）。
- 不发：`Code`（`Open` / `Close` / `Toggle` / `OpenAt` / `Hidden` / `Interactable`）、属性应用、`Covered`、`Replaced`、
  `HostGone`、`AnchorLost`、`EditEnded`（结束编辑的后果）。
- `dismiss="pass"`（默认）：按下时关闭（`popover.collapse`），下面的按钮在松开时才收到点击（`btn.click`），两个 cue 之间
  可能隔几帧。

### 7.7 `<Collapsible>`

- 包住头部 `PuiButton` 的 `onClick` 监听：头部被点、`IsExpanded` 真的翻转 → `expand` / `collapse`，在 `OnExpanded` /
  `OnCollapsed` / `OnToggled` 之前。
- 手风琴（同组）里被收起的兄弟不发；ReSolve 改 `expanded`（内部调的是公开的 `Expand()` / `Collapse()`）不发；
  C# `Expand` / `Collapse` / `Toggle` / `Expanded=`、初始状态与初始裁决都不发。
- `deny`：头部不可交互时被点。

### 7.8 `<Carousel>`

- `flip`：`OnEndDrag` 之后页真的换了（拖过卡宽的 20%）；点指示点换了页；CenteredSlideBox 点侧边卡片（库内部通过公开
  `GoTo` 实现，由它以用户身份发）。在 `OnCurrentChanged` 之前发。
- CenteredSlideBox 单按钮模式下点**居中**的卡片 = 按下那唯一的按钮：发那个 `<Btn>` 的 `btn.click`（按它的 `sfx` 解析），
  随后照常是 `modal.close` —— 两种确认方式听起来一样。多按钮模式下点居中卡什么都不做，也不发。
- 不发：自动播放、`GoTo` / `Next` / `Previous` / `Current=`、XML `current`、`BindItems` 改动下标、resize / 变体重排。
- 宿主自己接的左右箭头（`Btn.OnClick` → `car.Next()`）响的是按钮的 `btn.click`；想让它听起来像翻页，写
  `<Btn sfx="carousel.flip">`。
- 指示点是内部 `PuiButton`，永远不会发 `btn.click`（P2）。禁用的 Carousel 仍能拖动翻页（§15）—— v1 照实发 `flip`，不发 `deny`。

### 7.9 `<Pages>`：不发

`<Pages>` 没有用户手势（P5）：翻页只经 `Show` / `Selected`，全部来自 C# 或变体 ReSolve，而 C# 多半写在某个按钮的
`OnClick` 里 —— 那个按钮已经响过了。要「翻页音」就把键写在**触发翻页的控件**上：

```xml
<Btn id="toDetail" sfx="pages.flip">详情</Btn>   <!-- OnClick 里 pages.Selected = "detail" -->
```

`pages.flip` 只是一个键名，库不会主动发它；游戏照常把它映射到翻页音。`<Pages sfx>` → `PUI-SFX-INERT`，提示里给出这条配方
（SFX-D5）。

### 7.10 Screen 与 Modal

**角色由打开方决定**，记在 Screen 上，关闭时按它发：

| 打开方 | 角色 |
|---|---|
| `UI.Open(name)`（游戏代码、Router 的页） | `screen` |
| Router 的 `RoutePresent.Modal` | `modal`（在 `ApplyRouteLayer` 之后） |
| `UI.Modal.*`：MessageBox / InputBox / MarkdownBox / CenteredSlideBox / 自定义 `ModalRequest`、Router Prompt 弹出的对话框 | `modal`（弹窗队列套完 layer 之后） |
| Loading 遮罩、Toast、引导遮罩 | 静默 |
| `PromptUGUIDocumentHost` | 静默（SFX-D7） |
| 热重载重开 | 不发 `open`，沿用旧实例的角色 |

- **`open`**：打开方套完 layer 之后发一次。同名已开时 `UI.Open` 返回旧实例 → 不发。Router 孤儿页（打开时导航已过期）
  不发：Reconcile 今天要等 `OnEnter` 跑完才发现它过期（§15），所以 `ActivatePage` 在套完 layer、`OnEnter` 之前**自己比一次
  epoch**，过期就不发 —— 这样也不会让 `OnEnter` 里弹出的对话框排到页的 `screen.open` 前面。UI Preview 工具走普通的
  `UI.Open` / `UI.Close`，照常发 —— 在预览里也能听到音效。
- **`close`**：走进 `Screen.Close()` 时发，就在 `OnClosing` 之前 —— `UI.Close` / `UI.CloseAsync` / Router 撤下一页或一个
  Modal / 对话框关闭（按钮、Esc、`CancellationToken`、`Modal.CloseAll`）/ 直接 `screen.Close()`；EditMode 时直接销毁的分支
  也算。只对发过 `open` 的 Screen 发：构建途中（`IsOpening`）被关掉的不发（P30）。
- **不发**：直接 `CloseImmediate`（热重载、`UnloadAll`、`ResetForTests`、`DocumentHost.Clear`、Router 孤儿收尾）、
  `screen.Dispose()`、场景卸载导致的外部销毁。
- **已知顺序**：对话框按钮关闭自己时，`await` 之后同步打开的下一个对话框，其 `modal.open` 先于当前的 `modal.close`（P12）。
- Router：一次导航 N 个 `close` + M 个 `open`（Sequential 模式隔着退场时长）；`Back()` 就是 `Open(parent)`；同一条链只刷新
  `OnEnter` → 不发。同名先关后开是合法的：同帧一个 `close` + 一个 `open`。
- 开机时多个 Screen 同帧打开（HUD、背景）—— 常驻的写 `<Screen sfx="none">`，或在处理器里忽略就绪之前的 cue。

### 7.11 拖放

| 时刻 | cue | 说明 |
|---|---|---|
| 拿起（`DragSession.Begin`，唯一调用方是 `DragSource`） | `drag.lift` | 在 `lift` 挂钩、`UI.Drag.OnLift` 之前。按下时不可交互的源本来就拿不起来（`TryPress`）；长按期间才被禁用的不发（P17） |
| 同步接受：`Settle` 开始落地飞行（与 `receive` 同刻） | `drag.land` | 查找链：目标的 `sfx` → 源的 `sfx` → `drag.land` |
| 等确认（`Defer`）：松手、停在目标上方 | —— | 结果未定 |
| 等确认 → `Accept` | `drag.land` | 落地飞行开始时 |
| 等确认 → `Reject` | `drag.return` | 从停靠处飞回时 |
| 松手不在目标上 / 目标拒绝（`drop-deny`）/ 落回自己的容器 / 原地拿起又放下 / `OnDrop` 里同步 `Reject` | `drag.return` | 飞回开始时（没移动过的话飞行是瞬时的，照发） |
| Esc / Start / B 取消 | `drag.return` | 用户把它放回去了 |
| `UI.Drag.Cancel()`、源被销毁 / 隐藏 / 禁用 / `drag` 关掉 / 重新绑定、容器推送、ReSolve、源 Screen 开始关闭、上面开了一层、等待超时、迟到或重复的回复 | —— | 代码与系统 |

`Target` = 放进的目标；`Screen` = 目标所在的 Screen（跨 Screen 放下时）。没有手柄 / 键盘拖动（拖放 spec DND-D17），不涉及。

### 7.12 重排（`<ScrollList reorder>`）

| 时刻 | cue |
|---|---|
| 拿起一行（`ReorderDriver.Lift`） | `reorder.lift` |
| 松手、位置变了（`Commit`，`from != to`） | `reorder.land`，在 `OnReordered` 之前 |
| 松手、落回原位 / 长按后原地松开 | `reorder.return` |
| 取消（推送、ReSolve、`Reorder=false`、`ClearSlots`、`Dispose`） | —— |

查找链：行的根元素（`itemTemplate` 模板体的根）→ ScrollList。cue 不依赖 `lift` / `drop` 挂钩（P16）。

### 7.13 拒绝（`deny`）

- **哪些控件**：`<Btn>`、`<Toggle>`、`<Tab>`、`<TabMenu>`（把手）、`<Dropdown>`、`<Collapsible>`（头部）。
  `<Slider>` / `<InputField>` / `<Carousel>` / 拖动源 v1 不发（§14）。
- **什么算**：控件 `IsActive()`，但 `IsInteractable()` 为 false（自己的 `interactable="false"` / `Interactable = false`，
  或祖先 `CanvasGroup` 禁用了它），用户左键点击或在它上面确认。被唤回门拦下的 Submit 不算（P8）。
- **什么不算**：封锁（关闭中的 Screen、Modal 背板、引导遮罩）都挡住了射线，点击到不了控件（P9）；鼠标右键、中键；拖动或
  滚动之后的松开。
- 每次点击一个 cue —— 连点十下就是十个，节流归游戏侧。
- 裸键与 `none` 不覆盖它（§5.1）。

### 7.14 关闭中的 Screen

Screen 开始关闭后（`IScreen.IsClosing`），它的控件一律不再发 cue。正常情况下封锁已经挡住了新的点击，`drag=` 的拖动也已被
取消；这一条兜住**已经捕获**的重排在 ghost 上收尾的情况（拖着一行时 Screen 被关掉，松手仍会提交）。它自己的 `close` cue
照发（在这之前）。EditMode 里 `IsClosing` 永远为 false、关闭是立即的，所以这一条只能在 PlayMode 测。

### 7.15 重应用与自愈

`sfx` 在每次属性应用（初次与每次 ReSolve）里都解算并写给控件，解算为空 = 没写 = 默认音；与 `interactable` 的「未声明
也每次写」同一条路，不进 `[UIAttr]` 的 `if (v == null) continue;` 循环。字符串在应用时解析成一份小的 `SfxSpec`
（裸键 + 动作表）缓存在控件上；发 cue 时只走 1～2 个元素的查找链。`<Screen sfx>` 在发 `open` / `close` 时按当前变体解算。

## 8. lint

规则代码在 `Runtime/Core/Lint/SfxRules.cs`（纯 C#）：语法解析、内置标签 → 动作表、有效动作的并集（§5.2）—— 运行时的语法
校验用同一份实现。lint 没有 warning 级别（P27），三条规则都只报**确定**的错：

| 代码 | 查哪里 | 何时 |
|---|---|---|
| `PUI-SFX-VALUE` | 行内写法；`<Style>` 与主题 `<Style>` 的值（加进 `StyleRules.CheckStyle` —— 样式值不会走到 `WalkNode`）；`<Screen sfx>` | 语法错：`click:`（空键）、`:x`（空动作）、两个裸键、同一动作出现两次 |
| `PUI-SFX-ACTION` | 只查内置节点上的**行内**写法；样式贡献的（`StyleAttrNames` 里的）与模板体的根不查 | 动作名不在内置词表里（拼错：`clik:`）；或这个标签既不做、也不替别人查的动作（`<Btn sfx="checked:x">`、`<Toggle sfx="click:x">`）。`lift` / `land` / `return` 处处允许 —— 拖放、重排可以由调用处、外层列表或 C# 打开（P28） |
| `PUI-SFX-INERT` | 只查行内写法与调用处写法 | ① `<Pages sfx>`（附 §7.9 的配方）；② 内置标签上的行内 `sfx`，而这个标签既不发也不被查（`<Frame>` / `<Text>` / `<Image>` / `<Icon>` / `<VStack>` / `<HStack>` / `<Grid>` / `<Slider>` / `<InputField>` / `<Progress>` …、任何变体都没写 `reorder` 的 `<ScrollList>`），同时：任何变体都没写 `drag=` / `drop=`，不是模板体的根，不是写了 `reorder` 的 `<ScrollList>` 的直接子节点，值里也没有 `lift:` / `land:` / `return:`（写了就说明作者打算在 C# 里打开拖动）；③ 调用处写了 `sfx`（`ElementNode.InvocationAttrNames`，expanded 遍）、而实例根满足 ② 的模板调用，报在调用处，附 §5.4 的 Param 配方 |

不报的：样式贡献的 `sfx`（样式是广播的）、模板体的根（可能是列表行，也可能由调用处带上 `drag`）、自定义控件、合成的
Screen 根。

- `<Param name="sfx">` 是解析错误（共享 parser），lint 照常报出。
- `sfx` 计入 `VariantBaseRules` 的自愈集：`sfx.mobile` 无基础值不报 `PUI-VARIANT-NO-BASE`；`PUI-THEME-STYLE-SHAPE` 豁免它
  （今天没有通用的豁免机制，要新写一条）。
- 键名不校验（需求 6）。
- raw 遍与 expanded 遍都跑；`INERT` ③ 只在 expanded 遍（被导入的模板只在那一遍可见）。
- 运行时只有 `VALUE`：同一份解析，每个节点、每个值 `UILog.Warn` 一次（带 `src:line`）；`ACTION` / `INERT` 只在 CLI。

## 9. XSD

- `sfx` 加进 `_commonAttrNames`（`commonAttrs` 组）。
- `<Screen>` 显式声明可选的 `sfx`（`sfx.<variant>` 由已有的 `anyAttribute processContents="lax"` 接住）。

## 10. 实现地图

| 文件 | 改动 |
|---|---|
| `Runtime/Core/Lint/SfxRules.cs`（新） | `SfxSpec` 解析、动作表、有效动作、查找函数、三条 lint 规则 |
| `Runtime/Core/IR/CommonAttributes.cs` | 加 `sfx`（让 `[UIAttr]` 循环跳过它、lint 自愈集自动包含它）；更新注释里「layout geometry + visibility」的说法 |
| `Runtime/Core/Template/TemplateExpander.cs`、`Runtime/Core/Lint/VariantBaseRules.cs` | `CommonAttrs` 与镜像 `InvocationMergeableOntoTemplateRoot` 加 `sfx` |
| `Runtime/Core/IR/ElementNode.cs`、`Runtime/Core/Template/TemplateExpander.cs` | 新字段 `InvocationAttrNames`（调用处写到实例根上的通用属性，展开时逐层带上）；`ExpandInvocation` 填它，并从 `StyleAttrNames` 删掉这些名字（P26，所有通用属性一并修好） |
| `Runtime/Core/Parser/UIDocumentParser.cs` | `<Screen sfx>` / `sfx.<variant>` 进合成根；`<Param name="sfx">` 报错 |
| `Runtime/Core/Lint/IRWalker.cs`、`StyleRules.cs`、`ThemeStyleRules.cs` | 挂规则；样式值的 `VALUE`；主题 SHAPE 豁免 |
| `Runtime/Application/UI.Sfx.cs`（新）、`SfxCue.cs`（新） | 门面、`Subject`、`SubsystemRegistration` 重建、内部 `Raise`（每次 `OnNext` 包 `try/catch`）、`ResetForTests` |
| `Runtime/Application/ControlAttributeApplier.cs` | 每次应用解算 `sfx` 并写给控件 |
| `Runtime/Controls/Control.cs` | `SfxSpec` 字段、`RaiseSfxCue`、kind、查找链的「上一级」钩子、关闭中 Screen 的门 |
| `Runtime/Controls/Internal/PuiButton.cs`、`PuiToggle.cs`、`PuiDropdown.cs` | 点击 / 确认覆写：向所属控件报告用户激活与拒绝；`PuiDropdown` 的 `CreateItem` 覆写给每个条目加 `ICancelHandler`（Esc / B） |
| `Btn.cs`、`Toggle.cs`、`Tab.cs`、`TabMenu.cs`、`Dropdown.cs`、`Popover.cs`、`Collapsible.cs`、`Carousel.cs` + `Internal/CarouselView.cs` | §7.2–§7.8 的挂点 |
| `Runtime/Controls/Internal/PopupChain.cs`、`PopupCore.cs`、`Runtime/Application/EscapeChain.cs` | 「被手势直接指向的那一层」与关闭原因传到宿主 |
| `Runtime/Controls/Internal/DragSession.cs`、`ReorderDriver.cs`、`Runtime/Controls/ScrollList.cs` | §7.11、§7.12 的挂点 |
| `Runtime/Application/Screen.cs`、`UI.cs`、`UI.Modal.cs`、`UI.Router.Reconcile.cs`、`UI.Router.Layering.cs`、`Modals/LoadingOverlay.cs`、`Toasts/ToastOverlay.cs`、`UI.Tutorial.cs`、`PromptUGUIDocumentHost.cs`、`Modals/CenteredSlideBoxRequest.cs` | 角色、`open` / `close` 的发出点（Router 页在 `OnEnter` 之前比 epoch）、静默的打开方、热重载沿用角色；CenteredSlideBox 侧边卡片的 `flip`、单按钮居中卡的 `btn.click` |
| `Editor/XsdGenerator.cs` | §9 |

## 11. 测试（Red 先行）

EditMode（`PromptUGUI.Tests.EditMode`，每个类 `UI.ResetForTests()`；点击用 `ExecuteEvents` 模拟真实输入）：

- **语法**：`SfxRules` 的合法 / 非法写法、裸键与 `deny`、`none`、空值、逗号分隔、重复动作。
- **Btn**：点击发 `btn.click` 且先于 `OnClick`；`onClick.Invoke()` 不发；右键不发；禁用（自身 / 祖先 `CanvasGroup`）点击发
  `btn.deny`、不发 click；被唤回门拦下的 Submit 不发；Submit 发 click；`sfx` 覆盖、`none`、`deny:` 各自生效；
  `TextLinkClicker` 转发的非链接点击算这个 Btn 的 click。
- **deny 逐个控件**：`<Toggle>`、`<Tab>`、`<TabMenu>` 把手、`<Collapsible>` 头部各自禁用后点击 / 确认发各自的 `*.deny`、
  不发别的（Dropdown 在 PlayMode）。
- **Toggle**：开 / 关各一个；`IsOn` / XML `isOn` / ReSolve 不发；radio 组点 B 只发 B 的 `checked`；重点已开的不发；
  `OnValueChanged` 订阅者在同一次点击里把它改回去 → 只有一个 cue。
- **Tab**：切换只发一个 `tab.select` 且先于 `OnSelectionChanged`；重点当前不发；`allowSwitchOff` 点掉发 `deselect`；
  `IsOn` / `ClearSelection` / `BindItems` / Router `MapTab` 不发；TabBar 的 `sfx` 被查、Tab 自己的优先；TabMenu 里的 Tab
  查 TabMenu 的 `sfx`。
- **TabMenu / Popover / Collapsible**：用户开合各发一个；C# 开合、属性应用、`Covered`、`Replaced`、级联不发；TabMenu 选中
  另一项只发 `tab.select`、重选当前项发 `tabmenu.collapse`；点外面关掉嵌套弹层只发一个；手风琴兄弟不发；`CanOpen` 拒绝不发。
- **Carousel**：拖过一页发 `flip`、不到 20% 不发；指示点发 `flip`、不发 `btn.click`；自动播放 / `GoTo` 不发；
  CenteredSlideBox 侧边卡片发 `flip`、单按钮模式点居中卡发那个按钮的 `btn.click`。
- **Pages**：`Selected=` / ReSolve 不发。
- **Screen / Modal**：`UI.Open` / `UI.Close` 发 `screen.*`；对话框发 `modal.*`；Router 页发 `screen.*`、Router Modal 发
  `modal.*`；Loading / Toast / 引导遮罩 / `PromptUGUIDocumentHost` 不发；热重载、`UnloadAll`、`Dispose`、`CloseImmediate` 不发；
  热重载过的 Router Modal 关闭时仍发 `modal.close`（角色沿用）；`<Screen sfx>` 与 `sfx.<variant>` 生效；同名已开的
  `UI.Open` 不发；Router 孤儿不发；构建途中被关的不发 `close`。
- **拖放**：拿起；同步接受 `land`（目标的 `sfx` 先于源）；`Reject` → `return`；松在空处 → `return`；Esc → `return`；
  `UI.Drag.Cancel()` / ReSolve / 关屏 / 超时不发；`Defer` → 松手不发、`Accept` 发 `land`、`Reject` 发 `return`、迟到的回复不发；
  按住期间才被禁用的源不发 `lift`（按下时就不可交互的走 `TryPress`，不在此列）。
- **重排**：拿起；换位 `land` 且先于 `OnReordered`；原位 `return`；推送取消不发；没写挂钩照样发；行根上的
  `sfx="lift:x"` 生效（运行时不按元素过滤）。
- **继承与自愈**：`class=` 带 `sfx`；模板体默认 + 调用处覆盖；模板体根的 `sfx` 来自样式时，调用处覆盖在有主题样式的工程里
  经 `Open` / ReSolve 后仍在（P26，顺带对 `width` 断言同样成立）；`<Param name="sfx">` 抛 `ParseException`；`sfx.mobile` 无
  基础值、退出变体后回默认；切主题后回到新主题的值；语法错只警告一次。
- **门面**：`ResetForTests` 重建流；抛异常的订阅者不妨碍 `OnClick`。

EditorOnly / Lint（`Tests/EditMode/Lint`、`Tests/EditMode/Editor`）：`VALUE` 覆盖行内、`<Style>`、主题 `<Style>`、`<Screen>`；
`ACTION` 不报样式贡献的值、不报模板体的根、不报 `lift` / `land` / `return`；`INERT` 的三种情形与每一条豁免（`drag=` /
`drop=` 任一变体、模板体的根、重排列表的直接子节点、值里写了拖放动作、样式贡献、调用处写法经 `InvocationAttrNames` 报在
调用处）；`PUI-VARIANT-NO-BASE` 不报 `sfx`；主题 SHAPE 豁免；XSD 含 `sfx`（`StringAssert.Contains`）。

PlayMode（真实 `EventSystem`、射线与输入）：禁用 Btn 被真实点击发 `deny`；关闭中的 Screen 上点击什么都不发；带退场动画的
Screen 关闭途中，已捕获的重排松手提交不发 cue；`dismiss="pass"` 的弹层：按下发 `collapse`、松开时下面按钮发 `click`；
**Dropdown**：点开发 `expand`、选另一项只发 `select`、重选当前项发 `collapse`、点背板发 `collapse`、真实 Esc 与手柄 B
（普通页面上与对话框里）各发一个 `collapse`、`BindOptions` 推送时的 `Hide` + `Show` 不发、禁用时点击发 `deny`；
真实点击已选中的 Tab 不发（同时验证 §15 第 1 条）。

## 12. SKILL / 文档同步（同一 PR，英文）

- `authoring-promptugui-xml/SKILL.md`：通用属性表加 `sfx` 一行；`<Screen>` 属性小节加 `sfx`；Templates 小节注明
  `<Param name="sfx">` 是错误、给 Param 配方；错误码表加三条。
- 新建 `authoring-promptugui-xml/reference/sfx.md`：语法、动作表、查找链、身兼几职的元素写 `动作:`、模板 / 样式 / 主题 /
  变体、Pages 与 Carousel 箭头的配方、什么时候不发。
- 在各控件的 reference 里加一行指向 `sfx.md`：`controls-tabs.md`、`controls-collapsible.md`、`controls-carousel.md`、
  `controls-popover.md`、`drag-drop.md`、`reorder.md`；主文档 `<Btn>` / `<Toggle>` / `<Dropdown>` / `<Pages>` 各一句。
  `animations.md` 的 `on="drop"` 一行注明它与 `drag.return` / `drag.land` 的区别（前者任何结束都发）。
- `scripting-promptugui-csharp/SKILL.md`：新增 **UI sound cues** 小节（`UI.Sfx.OnCue`、`SfxCue`、契约、同帧去重（帧算子来自
  R3.Unity）、`DefaultKey` 回落、Modal 的已知顺序、Domain Reload 与不要在 `SubsystemRegistration` 阶段订阅）与速查表一行；
  `RaiseSfxCue`。
- `AGENTS.md`：路由表加 `reference/sfx.md`。
- 主 spec §5.1 通用属性表加 `sfx`，指向本文。
- PR 描述写明两处行为变化（仓库没有 CHANGELOG）：`<Param name="sfx">` 现在是解析错误（改名，如 `clickSfx`）；调用处写到
  模板实例根上的通用属性不再被主题样式的回写盖掉（P26）。

## 13. 待确认的决策

| # | 决策 | 推荐 | 备选 |
|---|---|---|---|
| SFX-D1 | 多动作控件的写法 | 一个 `sfx` 属性 + `动作:键` 迷你语法（§5.1） | 每个动作一个属性（R3） |
| SFX-D2 | 默认键与动作名 | `kind.action`，小写，点分；动作名与 `<Trigger on=>` 同时刻时同名（`click` / `checked` / `unchecked` / `expand` / `collapse` / `open` / `close` / `lift`），新增 `select` / `deselect` / `flip` / `land` / `return` / `deny`；拖放的「放进」叫 `land` 而不叫 `drop`，因为 `on="drop"` 在任何结束都发 | Toggle 用 `on` / `off` |
| SFX-D3 | 裸键与 `none` 管不管 `deny` | 不管；只有 `deny:` 能改它 | 裸键 / `none` 也覆盖 `deny` |
| SFX-D4 | 查找链 | Tab → TabBar / TabMenu；行 → ScrollList；`land`：目标 → 源；其余只查自己 | 只查自己 |
| SFX-D5 | `<Pages>` 翻页 | 不发（没有用户手势），配方：把键写在触发翻页的按钮上 | 在用户手势里调用的 `Show` 也发 `pages.flip`（与按钮叠两声） |
| SFX-D6 | Carousel | 只认拖动换页、指示点、CenteredSlideBox 侧边卡片 | —— |
| SFX-D7 | Screen / Modal | 按打开方定角色；Loading / Toast / 引导遮罩 / DocumentHost 静默；热重载、拆除、`Dispose` 不发 | DocumentHost 也发 |
| SFX-D8 | 拖放结果 | `land` / `return` 跟结果走；等确认的松手不发；Esc 取消算 `return`；代码与系统取消、超时不发 | 松手时立即发（乐观） |
| SFX-D9 | 重排 | 换位 `land`、原位 `return`、取消不发 | 原位也发 `land` |
| SFX-D10 | 一次手势多个 cue | 全部发、不去重；cue 先于控件自己的事件（拖放结果除外） | 库内按优先级只发一个 |
| SFX-D11 | 关闭中的 Screen | 它的控件不再发 cue | —— |
| SFX-D12 | 模板调用 | `sfx` 进 `CommonAttrs`；`<Param name="sfx">` 解析错误；不做「唯一控件」穿透；能确定无效时 lint 报 `PUI-SFX-INERT`（§8） | 照搬 `click@` 穿透（R5） |
| SFX-D13 | 变体 / 主题清空 | 自愈：每次重应用都写，空 = 默认 | 与普通 `[UIAttr]` 一样粘住（需要基础值） |
| SFX-D14 | C# 面 | `UI.Sfx.OnCue` + `SfxCue`；给自定义控件 `RaiseSfxCue`；不提供控件上的 `Sfx` 读写属性 | 加 `IControl.Sfx` 读写（要处理运行期独占） |
| SFX-D15 | 不可交互的拖动源 | `TryPress` 已在按下时拒绝；长按期间才被禁用的，只是不发 `lift`，拿起本身的修正另开（§15） | 本 PR 顺手让 `DragSession.Begin` 也查可交互 |
| SFX-D16 | TabMenu 选中 | 只发 `tab.select`；重选当前项发 `tabmenu.collapse` | 另发一个 `collapse` |
| SFX-D17 | Dropdown 选中 | 只发 `dropdown.select`；重选当前项发 `collapse` | 另发一个 `collapse` |
| SFX-D18 | 弹层级联 | 一次手势只给被它直接指向的那一层发 `collapse` | 每关一层发一个 |

## 14. 非目标 / v2

- 播放、AudioClip、节流 / 去重、随机变体、混音、空间化 —— 全部归游戏侧。
- 悬停、获得焦点、按下（未松开）的声音 —— 已有 `OnPointerEnter` / `OnPointerDown` 与导航焦点流可订阅。
- `<Slider>` 拖动刻度、`<InputField>` 输入与提交、`<Suggestions>` 选行、`<ScrollList>` 滚动 / 回弹、`<Markdown>` 链接、
  `<Cooldown>` 就绪（已有 `OnReady`，且它不是用户操作）、Toast 出现、引导步骤。
- `<Slider>` / `<InputField>` / `<Carousel>` / 拖动源的 `deny`。
- 控件上的 C# `Sfx` 读写属性。
- 手柄 / 键盘拖放（库本身就没有）。

## 15. 调研中发现的既有问题（静态阅读，未在运行时验证；不在本需求内）

1. **重点已选中的 Tab、radio 组里已开的 Toggle 会再发一轮选择事件**：uGUI `Toggle.Set` 在组里强制回 true 之后仍调用
   `onValueChanged(true)`，于是 `Tab.OnValueChanged(true)` / `OnSelected` / `TabBar.OnSelectionChanged(同一个)` 重发；与
   `PuiToggle.OnClicked`、`TabMenu` 的注释以及 TabBar spec「`OnSelected` 只在 false→true 发」不符。现有测试只走
   `SimulateClickForTests`，没跑真实的 `InternalToggle`。本需求的 PlayMode 测试顺带验证。
2. **禁用的 Carousel 仍能拖动翻页、自动播放照转**（只有指示点失效）。
3. **按下到拿起之间（长按期间）才被禁用的拖动源会被拿起一帧**，下一帧才被 `SourceAlive` 取消（P17；按下时就不可交互的，
   `TryPress` 已经拒绝）。
4. **TMP Dropdown**：右键 / 中键也能打开；`OnSubmit` 没有唤回光标的门；淡出期间 `BindOptions` 推送会把列表重新打开。
5. **TabMenu**：点不可交互的行仍会收起菜单；`OnExpanded` 在 `Open` 被拒时仍然发。
6. **直接 `screen.Close()` / `CloseAsync()` / `Dispose()` 不经 `UI` 的登记**，之后 `UI.Open(name)` 会返回已经死掉的实例。
7. **`screen.OnClosing.Subscribe(…).AddTo(screen)` 在 Play 模式的两段式关闭里永远不触发**（`DetachGlobals` 先于 `FireClosing`）。
8. **Router `ActivatePage` 在 `await` 之后不查 epoch**：过期的页仍会被打开、跑过 `OnEnter`，再被 `CloseImmediate`。
9. **拖放**：触屏的 cancel 被当作松手；等确认期间源 Screen 关闭只放开停靠、没有结束等待（拖放 spec §7.10.5 说应结束）；
   停靠时被停靠的目标也被撤销武装（spec 说只撤其他的）；重排与拖放可以两指同时进行。
10. **只有旧 Input Manager 时**，一次 Esc 可能同时关掉 TMP 列表与链顶弹层。
11. **引导的阻断步之间**（`EndStep` 之后）遮罩不关，整屏挡点击且没有洞。
12. **调用处写的通用属性会被主题样式的回写盖掉**（P26）—— 本需求依赖它，**一并修复**，是这一节里唯一在本 PR 里改的。
13. **`CancellationToken` 关闭对话框**：token 若由 `CancelAfter` 之类在线程池里触发，关闭（以及本文的 `close` cue）会在主线程
    之外执行。§6.2「在主线程」的承诺以库自己运行在主线程为前提。

## 16. 里程碑（一个 PR，分步提交）

1. `SfxRules` + parser（`<Screen sfx>`、`<Param name="sfx">`）+ 模板（`InvocationAttrNames`、P26 修复）/ 自愈 / lint / XSD
   —— 纯 C#，先过 CLI。
2. 门面 `UI.Sfx` + `SfxCue` + 应用时写入；`<Btn>` / `<Toggle>` / `<Tab>` / `deny`（`Pui*` 覆写）。
3. 弹层：TabMenu、Dropdown、Popover、Collapsible。
4. Carousel（含 CenteredSlideBox）。
5. Screen / Modal 角色与开关。
6. 拖放与重排。
7. SKILL / reference / AGENTS.md / 主 spec。
8. PlayMode 覆盖。

## 17. 独立审查修订（2026-11-23）

一次不看作者调研、只对照需求与源码的独立审查，结论逐条核实后改进了正文：

| 审查意见 | 改动 |
|---|---|
| 按元素过滤动作项会打断自己的配方：重排行的根、由调用处带上 `drag` 的模板根、C# 打开的拖放 / 重排、一个样式给几种控件用 | 运行时不按元素过滤、只校验语法；`ACTION` 只查内置节点的行内写法，`lift` / `land` / `return` 处处允许（P28、§5.1、§8） |
| Dropdown 的 Esc / B 走当前选中条目的 `DropdownItem.OnCancel`，不经过 `PuiDropdown`；Esc 链只在有监听者时才走 | 每个条目加 `ICancelHandler`；PlayMode 用真实输入测普通页面与对话框里的 Esc 与 B（P24、§7.5、§11） |
| 模板体根的 `sfx` 来自样式时，调用处的覆盖会被主题样式的回写盖掉（既有，所有通用属性都有） | 新字段 `InvocationAttrNames`，`ExpandInvocation` 从 `StyleAttrNames` 里删掉调用处写的名字；本 PR 一并修复（P26、§10、§15） |
| lint 没有 warning 级别，「提示」也会挡住 CI | 三条规则都只报确定的错，`INERT` 的范围与豁免写清（P27、§8） |
| 关闭中 Screen 上的重排、Dropdown 开合放在 EditMode 测不出来 | 挪到 PlayMode（§11） |
| `DragSource.TryPress` 本来就在按下时拒绝不可交互的源 | P17 / §15 / SFX-D15 改成只管「长按期间才被禁用」 |
| `drag=` 的拖动在源 Screen 关闭时会被取消，继续的只有重排 | P9、§7.14 改正 |
| `PuiDropdown` 没有唤回门 | 照现状，第一次 Submit 就打开并发 `expand`；拒绝判断放在唤回门之后（P8、§7.1、§7.5） |
| CenteredSlideBox 单按钮模式下点居中卡确认，没有定义 | 发那个按钮的 `btn.click`（§7.8） |
| 语法警告会在每次重应用、每一行刷屏 | 每个节点、每个值只警告一次（§5.1、§8） |
| 身兼几职的元素，裸键会盖住它被查的动作 | 写 `动作:`，不写裸键（§5.2、§12） |
| 调用处 `INERT` 没说哪一遍、怎么认 | expanded 遍，靠 `InvocationAttrNames`（§8） |
| 测试缺口：五种控件的 `deny`、DocumentHost、Tab → TabMenu、热重载后的 Router Modal、`TextLinkClicker` | 补进 §11 |
| Router 孤儿要等 `OnEnter` 之后才被发现 | `ActivatePage` 在 `OnEnter` 之前自己比 epoch（§7.10） |
| 细节：§5.2 措辞；「开着」以背板为准；帧算子来自 R3.Unity；`SubsystemRegistration` 的先例与「不要在这个阶段订阅」；`OnNext` 一律 `try/catch`；`<Param name="sfx">` 要写进发布说明；样式值的语法检查走 `StyleRules`；同一次激活至多一个 cue；构建途中被关的不发 `close`；线程池里触发的 `CancellationToken` | 各处改正（P21、P22、P29、P30、§5.2、§6.2、§6.3、§7.5、§7.10、§8、§12、§15） |

没有发现泄露私有工程的内容。

## 18. 实施记录（2026-11-23）

按 §16 分步提交，一个 PR。与正文的偏差、实现中才看清的细节：

| 处 | 记录 |
|---|---|
| §7.4 TabMenu「重点当前行 = 用户的收起」 | uGUI 在收起之前会把同一个选中再宣布一遍，所以「这次点击是不是重点」不能在弹层关闭的回调里现查。`Tab` 记下自己这次激活是不是重点（`InSfxRepick`：激活窗口开着、却没发出 cue），`TabMenu` 在选择落定时（`OnSelectionSettled(tab)`）取走它，关闭回调只读这个标记 |
| §7.5 Dropdown 的挂点 | 全在 `PuiDropdown`：背板上加一个点击监听（点背板 = 用户收起），`DestroyBlocker` 里认「重点当前项」，每个条目挂一个 `SfxCancelRelay`（`ICancelHandler`，Esc / B 走选中条目，P24）。`TMP_Dropdown` 自己的淡出协程不经过这些入口，所以 `BindOptions` 在打开时推送（`Hide` + `Show`）不发声 |
| §7.6 弹层级联的「一次手势一个 cue」 | `PopupChain.CloseFrom` 记下这次 Esc / 外部按下瞄准的是哪一层（`IsAimedAt`），被连带关掉的子层与上层不发 |
| §7.10 角色 | `ScreenSfxRole` 默认 `Silent`，只有 `UI.Open` / Router（`RoutePresent.Modal` → `Modal`）/ Modal 泵会改；Loading、Toast、引导都走 `OpenModalScreen`，天然静音，没有加任何标记 |
| §11 测试文件 | 按控件族合并：`SfxBtnTests`（含门面：`ResetForTests` 重建流、抛异常的订阅者、自定义控件 `RaiseSfxCue`）、`SfxToggleTabTests`、`SfxPopupTests`（TabMenu / Popover / Collapsible）、`SfxCarouselTests`、`SfxApplyTests`、`SfxTemplateTests`、`SfxRulesTests`、`Application/SfxScreenTests`、`SfxDragTests`（含重排）；真实输入的在 `PlayMode/Controls/SfxPlayTests`。控件测试的记录器默认滤掉 `screen` / `modal`，只有 Screen 的测试要它们 |
| 测试的隔离 | 改了 `MessageBox.XmlSrc` / `Loading.XmlSrc` / `CenteredSlideBox.XmlSrc` 的测试都在 `finally` / `TearDown` 里还原 —— 这些静态皮肤不在 `ResetForTests` 里复位，漏还原会让后面不相干的测试读到测试用的皮肤 |

实现中顺带验证的 §15 条目（仍不在本 PR 修）：

- **§15-1 属实**：PlayMode 真实点击已选中的 Tab，`TabBar.OnSelectionChanged` 又发了一次（同一个 Tab）。音效不受影响 —— `select` 只在
  false → true 时发，该用例断言没有 cue。
- **§15-6 属实**：`screen.Dispose()` 之后 `UI` 的登记还在；`SfxScreenTests` 的拆除用例因此在 `Dispose` 之后再调 `UI.CloseImmediate`。

另一处观察（未查证是否有意）：同步的 `UI.LoadDocument(label, xml)` 不登记文档里的 `<Theme>`，主题相关的用例改走 resolver +
`LoadDocumentAsync`。

回归：EditMode / EditorOnly / PlayMode 全量，除 `main` 上同样失败的既有用例外全部通过 —— 那几条与本分支无关，都是环境相关：
一条着色器渲染、一条依赖平台路径格式的编辑器用例、四条按真实帧时长断言的 PlayMode 动画 / 冷却用例（编辑器在后台被限帧时帧长
超过断言的阈值）。`dotnet format --verify-no-changes` 与 UIXmlLint（`Runtime/Resources/`、`Samples~/`）干净。

发布说明要写的两处行为变化（§12）：

1. `<Param name="sfx">` 现在是解析错误 —— `sfx` 成了通用属性，调用处写的 `sfx` 落到实例根；要给模板里面的控件传音效键，换个名字
   （`<Param name="buySfx" default=""/>`）。
2. 修复 P26：模板体根从 `class=` 拿到某个通用属性、调用处又写了同名属性时（`width` / `hidden` / `drag` … / `sfx`），只要有任何主题
   带 `<Style>`，每次 `Screen.Open` 与 `ReSolve` 都会把样式值铺回去、盖掉调用处的值。现在调用处的值保留。
