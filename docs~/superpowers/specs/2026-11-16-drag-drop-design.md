# 通用拖放 `drag=` / `drop=` —— 任意控件拿起，跨容器、跨 Screen 放下

> 状态：**决策已对齐**（2026-11-16：§13 的 DND-D1…D17 全部按推荐）。分支 `feat/drag-drop`，plan 见
> `docs~/superpowers/plans/2026-11-16-drag-drop.md`。
> 需求来源：一个九宫格阵形 —— 把一格里的单位拖到另一格（目标格有单位就交换）；之后是「道具栏 → 装备槽」
> （道具栏是会滚动的列表，装备槽可能在另一个 Screen 或模态里）。PC 宽屏（鼠标）与手机竖屏（触屏）同一份 XML。
> 相关：
> `2026-09-16-scrolllist-drag-reorder-design.md`（手势仲裁、出流 + placeholder、FLIP、取消规则 —— 本文的手势层直接从
> `ReorderDriver` 抽出来共用；REO-D5「先定型再补间」、REO-D12「结构变动即取消」原样沿用）、
> `2026-10-18-popup-core-and-input-suggestions-design.md`（`PopupCore`：面板留在宿主子树里，靠一个 overrideSorting 的
> 嵌套 Canvas 抬到页面之上、并脱离祖先 mask —— 本文「原地抬层」的出处）、
> `2026-09-15-raycast-target-design.md`（命中是声明出来的 —— `drag=` / `drop=` 本身就是声明）、
> `2026-10-01-router-page-layering-design.md`（各层 sortingOrder 分带 —— 拖动层要压过所有带）、
> `2026-09-14-scrolllist-row-reuse-design.md`（行按位置复用 —— 它决定了库无法替 C# 搬数据，也猜不出「交换」）。

## 1. 问题

今天能拖的只有 `<ScrollList reorder>` 的行，而且只能在本列表里插入排序（reorder spec §10 把「拖出列表 / 拖到别的
控件上」列为非目标）。作者要的是：

1. **任意控件可拿起**（格子里的单位卡、道具图标），**放到别的控件上**（另一格、装备槽、回收站）；
2. **落点有反馈**：手指下的目标高亮；最好一拿起就标出所有能放的地方，放不了的给出拒绝样式；
3. **C# 收到「从哪到哪」**，并且「能不能放」由 C# 判（同是道具，武器只能进武器槽）；
4. **跟手的图由库来画**，作者不为它写 C#；
5. 触屏**长按才拿起**，并且**不跟外层滚动打架**（道具栏本身是滚动列表）；
6. 拖动中结构变了（推送、ReSolve、关屏）要**干净地取消**；
7. **跨容器、跨 Screen**：道具栏在一个 Screen、装备面板在另一个（或在模态里）；
8. 一份 XML 两端，只靠 Variant 调参；
9. 补齐 XSD、lint、参考文档、测试。

## 2. 注意点（踩坑表）

设计里的每一条决定都是在绕下面这些坑；§7 是细节，§13 是决策表。

| # | 坑 | 不处理的后果 | 本文的处理 |
|---|---|---|---|
| P1 | **控件子树不能克隆**。`Object.Instantiate` 只复制序列化字段；`ProceduralPanel` / `FxImage` / `DecorPanel` 的参数全在非序列化私有字段里（`_fill`、`_radius`…），材质缓存按引用计数 | 克隆出来的「图」是透明的；材质缓存计数错乱；克隆里的 Selectable、静态注册表被复制一份 | **拿起本体**，原地抬层（§7.2）；不克隆、不快照 |
| P2 | **每个 Screen 是一个独立的根 Canvas**，sortingOrder 分带：页 0 起、Modal 1000、Toast 2000、Tutorial 3000 | 被拖的东西画在另一个 Screen 下面；被列表视口的 mask 裁掉 | 拿起时给本体**临时**加一个 `overrideSorting` 的嵌套 Canvas（`UI.Drag.SortingOrder`，默认 4000）：压过所有带；uGUI 的 stencil / `RectMask2D` 查找都停在 overrideSorting 的 Canvas 处 → 脱离祖先 mask。与 `PopupCore` 同一手法（§7.2） |
| P3 | **嵌套 Canvas 常驻会打断合批** | 背包 100 格 = 100 个 batch 根 | 拿起时加，结算飞行结束即摘；从不常驻 |
| P4 | **程序化面板只在「最近的 Canvas」上开 shader channel**（`EnsureCanvasChannels`：TexCoord1 / 2） | 新加的嵌套 Canvas 没开 → 拿起的瞬间面板塌成透明 | 抬层 Canvas 复制父画布的 `additionalShaderChannels` 与根的 `vertexColorAlwaysGammaSpace`；不动 `overridePixelPerfect`（继承根的像素模式） |
| P5 | **拖着的本体会挡住射线**，它下面的目标永远测不到 | 落点判定失效 | 抬层 Canvas **不加** `GraphicRaycaster` —— uGUI 按 Canvas 收集可命中的 Graphic，没有 raycaster 的嵌套 Canvas 整棵子树对射线透明 |
| P6 | **落点判定**：模态盖住的页面、滚出视口的格子、叠在一起的目标、另一个 Screen 在上面 | 几何测试（矩形含点）会让手指「穿过」模态，放进下面页面的格子 | `EventSystem.RaycastAll`，取最上层一个命中、向上找最近的 drop 目标 —— 与点击同一个答案（§7.4） |
| P7 | **库默认点击穿透**（`<Frame>` / `<Image>` 不进射线），目标多半是穿透的 Frame | 射线测不到目标 | 只在**武装期间**（有一个它会接受的拖动正在进行）让目标可命中，解除即还原 —— 平时零行为变化（§7.3） |
| P8 | **行按位置复用**（`BindItems`），库不知道数据的身份 | 库替 C# 搬数据、或自动播「交换」动画都会错：移动、交换、复制、列表收缩在库眼里一模一样 | 库不碰数据；先复位结构、同步发事件让 C# 推送，再在推送后的结构上做 FLIP 飞行。交换时「被换走的那个飞回去」由 C# 一句 `e.Fly(…)` 说明（§7.6） |
| P9 | **事件时机**：C# 在 `OnDrop` 里同步推送，若此时源还出流、placeholder 还在 | 推送撞上「推送即取消」（REO-D12），或把半截结构绑进去 | 先复位结构（placeholder 摘走、入流），再发事件，最后才开始飞行（§7.6） |
| P10 | **外层滚动**：uGUI 把整条 drag 序列交给最深的 `IDragHandler` | 道具栏要么滚不动、要么拖不起来 | 手势仲裁直接用 `ReorderDriver` 那一套（抽成共享件 `LiftGesture`）：不是拖放的手势逐事件转发给外层；触屏 hold；设备轮询判松手（Input System 模块三键共用一个 `PointerEventData` 的坑 —— reorder spec §14.4）（§7.1） |
| P11 | **触屏长按很烦**：不在任何滚动容器里的阵形格，长按没有意义；横向道具栏里往上拖出来，也不该先等 0.4s | 手感差 | `dragHold="auto"`：鼠标 0；触屏**只在有外层滚动时**才 hold 0.4s；在单轴滚动容器里**跨轴起手**（与滚动轴夹角 > 60°）立即拿起（§7.1） |
| P12 | **拖源同时是按钮**（点一下看详情，按住拖走） | 拖完松手又点了一下；或拖的一路都顶着 Pressed 的暗色 | 拿起时补做 uGUI 自己进入 drag 时做的事：给 `pointerPress` 发 `pointerUp`、清 `eligibleForClick`（§7.9） |
| P13 | **拖源是穿透的 Frame**，按下打不到它 | 拖不起来 | `drag=` 是意图（同订阅 `OnPointer*` 的规则）：拖源常驻可命中；点击仍沿层级向上找到外层的 `<Btn>`（§7.3） |
| P14 | **会话挂在源身上跑 Update**，源在拖动中被停用 | `Update` 停了，会话悬空 | 会话由全局 runner 驱动（隐藏的 DontDestroyOnLoad GO，同 `OrientationTracker`），每帧自检源是否还在 |
| P15 | **结构变动**：推送到源所在列表（位置复用 = 那一行现在是另一项）、ReSolve（Variant / 主题 / 转屏都是全局的）、源被销毁 / 隐藏 / 禁用、源的 Screen 关闭、热重载、新模态盖上来 | 拖着的是「别的东西」；或 `ApplyCommon` 把出流的源拽回排版 | 一律**取消**：瞬间复位、源发 `drop`、不发 `OnDrop`、本次手势余下的事件吞掉（§7.7） |
| P16 | **不同 Screen 的 CanvasScaler 系数不同**（像素模式 3× 的背包 → 自动缩放的装备面板） | 图在目标面板里显得忽大忽小 | 拖动中保持源的尺寸；落地飞行把 `localScale` 从「图 / 落点控件」的世界尺寸比补间回原值（§7.6） |
| P17 | **Camera 模式**：嵌套 Canvas 继承根的渲染模式；Overlay 永远画在 Camera 之上 | 源在 Camera 画布、目标在 Overlay 画布时，图画在目标下面 | v1 限制：同为 Overlay、或同一台相机都没问题；混用写进文档（§7.8） |
| P18 | **`PopupChain.DismissBelow`**：新的高层出现会关掉其下的弹层 | 拿起东西就把开着的 Popover / TabMenu 关掉了 | 抬层不走那条路 —— 它不是一个层，只是一个浮起来的控件 |
| P19 | **拖回原处** / 放到自己所在的容器上 | 发出一个 A→A 的事件 | 源的祖先目标不参与武装；放回原处 = 没有目标 = 飞回家、不发事件 |
| P20 | **拖动时内容在手指下移动**（自动滚动、动画） | 悬停目标不更新；源离开手指 | 会话每帧重新跟手、重测落点，不只在 `OnDrag` 里做 |
| P21 | **轮播卡根当拖源**：`CarouselView.Reposition` 每次滚动重写卡的几何；autoplay 在拖动中照转 | 卡被拽回去 | 拖源放进卡里面（卡根上写 `drag=` 报 lint）；拖动期间暂停所在 `<Carousel>` 的 autoplay |
| P22 | **reorder 列表的行里又写 `drag=`**：两套手势抢同一次拖动 | 行为不可预测 | v1 互斥，lint 报错（§8）；「列表内排序 + 拖出列表」是 v2 |
| P23 | **虚拟列表的行**会在滚动时被回收 | 拖着的源被回收成另一项 | v1 不支持（lint）；v2 让虚拟列表在会话期间钉住源行 |
| P24 | **暂停菜单**（`timeScale = 0`） | 长按、飞行、自动滚动全停 | 全部 unscaled time（同 REO-D14） |
| P25 | **多指** | 第二根手指改写会话 | 全局同时只有一个会话；其它指针的事件不进会话（同 reorder） |
| P26 | **开着 blocker 的弹层 / 引导遮罩** | 从弹层里往下面页面拖，目标在 blocker 之下 | 遵守射线的答案：被盖住的目标就是放不进去。引导步要允许「从 A 拖到 B」，镂空就得同时露出 A 和 B（文档写明） |

## 3. 否决的方案

**A. 把 `reorder` 扩成「拖出列表」。否决。** reorder 是**插入**语义（目标是缝隙，其余行让位），拖放是**槽位**语义
（目标是一个控件）。阵形是九个格子、装备槽是散落的 Frame，都不是列表。两者真正能共用的是手势层，不是语义 ——
所以抽共享件（§7.1），各自保留自己的判定。

**B. 跟手的图 = `Object.Instantiate` 克隆。否决。** 见 P1。

**C. 跟手的图 = 用源的 IR 节点（`Control.SourceNode`）重新实例化一份。否决作为默认。** bind 回调或 C# 写进去的运行时
内容（头像、名字、数量）全都没有，作者得为「图」再写一遍绑定，违背需求 4。保留为 v2 的 `dragGhost="Template"`：
「原物留在原处、另一张图跟手」的复制语义（商店 → 槽位那种），那时作者本来就要说清楚图长什么样。

**D. RenderTexture 快照。否决。** Overlay 画布要另搭相机才能渲进 RT；玻璃 / 雾采样的是背景；每次拿起一次离屏渲染。

**E. 把本体 reparent 到一个库自己的顶层拖动 Canvas。否决。** 换了 CanvasScaler 系数、相机、gamma、shader channel；
`UI.OwnerScreenOf` 找不到它的 Screen；`ApplyCommon` 的父级判断要临时假装 —— reorder spec §2-C 同一理由。原地 +
嵌套 Canvas（§7.2）得到同样的「浮在最上」，代价只是一个临时组件。

**F. 纯几何判定落点。否决。** 见 P6：要自己重算遮挡、mask、叠放顺序，而且会和点击给出不同的答案。

**G. 目标实现 uGUI 的 `IDropHandler` / `IPointerEnterHandler`。否决。** 事件只发给命中物向上第一个 handler，表达不了
armed / deny；内容在手指下滚动时没有事件；绑死指针输入，以后加手柄「搬运模式」接不上。会话自己判定落点，uGUI
只负责射线。

**H. 包装元素 `<Draggable>` / `<DropTarget>`（Flutter 式）。否决。** 多插一层 transform，破坏 LayoutGroup 的父子关系，
现有模板都得改结构。拖放是控件的**行为**，没有自己的外观 —— 同 reorder spec §2-G，属性是对的形态；做成 `Control`
基类上的 `[UIAttr]`（与 `stateReact` 同一处），任何标签都能写。

**I. 库替 C# 改数据 / 自动判断交换。否决。** 见 P8；数据归宿主（与 reorder 的契约同构）。

**J. 拿起就把事件交给 C#，让 C# 画图。否决。** 违背需求 4。

**K. 目标的默认悬停外观（缩放 / 着色）。否决（待确认，DND-D10）。** 缩放会挤到邻格，整块列表当落点时更难看；着色要和
`<Btn>` 的 state reactor 抢 CanvasRenderer 的颜色层。悬停反馈完全走挂钩（`<Show on="drop-over">`），skill 的每个
示例都带上。

## 4. 方案总览

```
按下（拖源上的 DragSource 收 OnInitializePotentialDrag；同时转发给外层，让惯性滚动停下）
  │  dragHandle？源 activeInHierarchy 且 Interactable？
  │  ── 否 → 本次手势全程转发（今天的行为）
  ▼
Pressed ── hold 计时（dragHold="auto"：鼠标 0；触屏有外层滚动时 0.4s，否则 0）
  │  ├─ OnBeginDrag 先到：沿外层滚动轴 → Scrolling（全程转发）；跨轴（> 60°）→ 立即拿起
  │  ├─ 松开 → Idle（点击照常）
  │  └─ 到点 / hold = 0 的第一帧拖动 →
  ▼
拿起（lift）
  │  源出流：父级是 LayoutGroup → ignoreLayout + 同尺寸 placeholder（ReorderDriver 那一套）
  │  源加抬层 Canvas：overrideSorting、sortingOrder = UI.Drag.SortingOrder、复制 shader channel / gamma、无 GraphicRaycaster
  │  pointerPress 补 pointerUp、eligibleForClick = false；源的 lift 挂钩（无挂钩 → 默认 1.03 缩放）
  │  武装：注册表里 kind 匹配、可交互、不是源祖先、CanDrop 通过的目标 → armed：开命中、发 drop-armed
  ▼
Dragging ── 每帧（全局 runner）：跟手（父级本地坐标 1:1）、RaycastAll → 悬停目标
  │  进入 armed 目标 → drop-over；进入 kind 匹配但被拒的目标 → drop-deny；离开 → drop-out
  │  指针贴近一个含 armed 目标的 ScrollList 视口边缘 → 自动滚动它
  ▼
松手
  │  ① 结构复位：placeholder 摘走、源入流（视觉仍停在手指处，抬层 Canvas 还在）
  │  ② 悬停在 armed 目标上 → DropEvent → target.OnDrop → UI.Drag.OnDrop（同步；C# 改模型、推送）
  │  ③ 飞行（FLIP 视觉补间，dragDuration）：
  │       被接受：目标里那一个同 kind 拖源（推送后它显示的正是落下的东西）从手指处飞进去；e.Land / e.Fly 追加
  │       没有目标 / e.Reject()：源从手指处飞回家
  │     目标发 receive；源发 drop；所有目标 drop-out / drop-disarmed、撤命中；UI.Drag.OnEnd
  │  ④ 飞行结束：摘抬层 Canvas、默认外观缩回
  ▼
Idle
```

三条原则：

1. **结构在松手当帧复位，事件在结构正常时发出，动画是覆盖其上的视觉补间。** REO-D5 推广到跨容器：C# 在 `OnDrop`
   里同步推送永远撞不上半截结构；外来的 layout rebuild 最坏让飞行提前到位，永远不会错位。
2. **库不拥有数据。** 拖放是乐观 UI：C# 收到「谁拖到了谁上面」，改模型、推送；库只管手势、判定、视觉。
3. **不是拖放的手势一寸不让**（同 reorder）：点击、滚轮、滚动条、hold 未到就动手指、按在拖源之外，全是今天的行为。

## 5. 作者面（XML）

### 5.1 通用属性（任意标签）

| 属性 | 取值 | 默认 | 说明 |
|---|---|---|---|
| `drag` | kind（`[A-Za-z_][A-Za-z0-9_-]*`）\| `none` | — | 本控件可以被拿起来拖；kind 决定能放进哪些 `drop`。`none` 关掉（给 Variant 用：`drag.portrait="none"`）。写 `true` / `false` 是 `PUI-DRAG-VALUE`（要的是 kind，不是开关） |
| `drop` | kind 列表（空格或逗号分隔）\| `*` \| `none` | — | 本控件接收这些 kind 的拖放；`*` = 任何 kind |
| `dragHold` | `auto` \| 时长（`0.4s` / `400ms` / `0.4`） | `auto` | 拿起前要按住多久。`auto` = 鼠标 0；触屏：拖源在滚动容器（`<ScrollList>` / `<Carousel>`）里时 `0.4s`，否则 0；写了 `dragHandle` 时两端都 0。显式时长对两端一律生效 |
| `dragHandle` | 子节点 id | — | 只有从这个节点按下才能拿起；几何判定，规则同 `reorderHandle` |
| `dragDuration` | 时长 | `0.18s` | 结算飞行（飞进目标 / 飞回家）的时长，`OutCubic`；`0` = 瞬移 |

- 都是 `Control` 基类上的 `[UIAttr]`，按 Variant 切换，ReSolve 重放幂等。时长解析复用 `AnimationSpec.ParseSeconds`；
  坏值告警并保留旧值（同 `reorderHold`）。
- **模板调用能带上它们**：`<UnitCard drag="unit"/>` 落到实例根上。要把五个名字加进 `TemplateExpander.CommonAttrs`，
  否则今天会报 `unknown attribute 'drag'`。
- 一个控件可以既是拖源又是目标（把一张卡拖到另一张卡上合成）。
- 嵌套的拖源：按下时最深的那个拿到手势（uGUI 的 handler 查找规则）。

### 5.2 源一侧挂钩：`lift` / `drop`

沿用 reorder 的两个值，解析规则推广为：**从触发器自己开始向上，最近的「拖源」或「`<ScrollList>` 行」，近者胜。**

- `lift`：被拿起（边沿）；`<Show on="lift">` = 拿起期间可见。
- `drop`：被放下 —— 任何结局（放进目标、飞回家、被取消），在结算飞行**开始**时发；`reverse-on="drop"` 与飞行同时跑。
- `lift@<id>` / `drop@<id>`：源是含 `<id>` 的那个（词法作用域，可写路径）。
- 触发器得是拖源本身或在它里面：`<Animation on="lift" reverse-on="drop" drag="unit">…</Animation>`（拖源就是这个
  Animation），或 `<Frame drag="unit"><Animation on="lift" …>…`（向上找到 Frame）。写在格子上、指向格子里的卡：
  `<Show on="lift@unit">`（卡被拿走时格子里显示一个空位框）。
- 拖源在一个 `<ScrollList>` 行里时，写在行根上的裸 `lift` 解析到的是**行**（reorder 的源），不是里面的卡 —— 用
  `lift@<卡的 id>`。
- 裸 `lift` / `drop` 既不在拖源里、也不在列表行里 → 运行期错误 + `PUI-LIFT-NO-SOURCE`（规则扩展）。

### 5.3 目标一侧挂钩

向上解析到最近的 `drop=` 控件（`@<id>` 同其它 `@id` 规则）：

| 值 | 种类 | 什么时候 |
|---|---|---|
| `drop-armed` | 持久对之一 | 一次本目标**会接受**的拖动开始了（拿起时判定：kind 匹配、可交互、不是源的祖先、`CanDrop` 通过） |
| `drop-disarmed` | 持久对之二 | 那次拖动结束（放下、飞回、取消）；也是没有拖动时的常态 |
| `drop-over` | 持久对之一 | 那次拖动正悬停在本目标上（现在松手就放进来） |
| `drop-deny` | 状态 | 一个 kind 匹配、但本目标拒绝的拖动悬停在上面（`CanDrop` 为 false，或 `interactable="false"`） |
| `drop-out` | 持久对之二 | 没有拖动悬停在上面（移开、放下、取消）；也是常态。`drop-over` 与 `drop-deny` 都以它结束 |
| `receive` | 边沿 | 东西被放进本目标（已被接受）：结算飞行开始时，C# 的 `OnDrop` 之后 |

- `<Show>` 接受前五个：注册式（像 `checked`），没有 Normal 兜底，可以同时多块可见 —— 一个 armed 的格子被悬停时，
  `drop-armed` 与 `drop-over` 两块都亮，作者自己分层。`<Show on="receive">` 是错误（时刻，不是状态）。
- 常态的确立同 `ready` / `cooling`：Screen 打开时处于 `drop-disarmed` + `drop-out`，`<Animation>` 落在终态不播放，
  普通 `<Trigger>` 不为「打开时的状态」触发。
- `<Animation on="drop-over" reverse-on="drop-out" scale="1:1.06">`：悬停放大、离开（含松手、取消）缩回 ——
  `reverse-on` 只有一个出口，这是选持久对而不是 `state-*` 式互斥状态族的原因（DND-D9）。
- 裸值不在任何 `drop=` 控件里 → 运行期错误 + `PUI-DROP-NO-SOURCE`（模板体、`@id` 形式免检）。
- kind 不匹配的拖动对目标完全透明：不 armed、不 deny，悬停判定继续向上找外层目标。

### 5.4 默认外观

- **拖源**：没有任何 `on="lift"` 挂钩时，拿起 = 抬层 + `localScale` × 1.03（0.12s），放下缩回 —— 与 reorder 的默认外观
  同一份代码（REO-D10）。写了挂钩，外观全归作者。缩放以拿起时的 `localScale` 为基准（作者写的 `scale` 不被冲掉）。
- **目标**：没有默认外观（§3-K）。

### 5.5 示例

阵形 —— 九格是一个网格列表，`BindItems` 推九项（空格推 null），格子是目标、格子里的卡是拖源：

```xml
<ScrollList id="formation" columns="3" cellSize="160x160" spacing="12"
            itemTemplate="FormationSlot" size="504x504"/>

<Template name="FormationSlot">
  <Frame id="slot" radius="12" color="@slot" drop="unit">
    <Show on="drop-armed"><Frame anchor="stretch" radius="12" borderWidth="2" borderColor="@accent"/></Show>
    <Show on="drop-over"><Frame anchor="stretch" radius="12" color="@accent/0.25"/></Show>
    <Show on="drop-deny"><Frame anchor="stretch" radius="12" color="@danger/0.25"/></Show>
    <Show on="lift@unit"><Frame anchor="stretch" margin="8" radius="8" borderWidth="1" borderColor="@muted"/></Show>
    <Frame id="unit" anchor="stretch" margin="8" radius="8" color="@card" drag="unit">
      <Image id="portrait" anchor="stretch" margin="4"/>
      <Text id="name" anchor="bottom-stretch" height="24"/>
    </Frame>
  </Frame>
</Template>
```

阵形不在滚动容器里，所以触屏上也是一动就拿起（`auto`）；要长按就写 `dragHold="0.4s"`。

道具栏 → 装备槽（两个 Screen）：

```xml
<!-- 背包 Screen：横向道具栏。触屏：横滑滚动；往上拖立即拿起；按住不动 0.4s 也拿起 -->
<ScrollList id="bag" direction="horizontal" itemTemplate="BagCell" anchor="bottom-stretch" height="96"/>
<Template name="BagCell">
  <Frame id="cell" width="88" radius="8" color="@cell">
    <Frame id="item" anchor="stretch" drag="item">
      <Image id="icon" anchor="stretch" margin="8"/>
      <Text id="count" anchor="bottom-right"/>
    </Frame>
  </Frame>
</Template>

<!-- 装备 Screen：调用处写 drop，落到模板实例根上 -->
<HStack spacing="16">
  <EquipSlot id="weapon" drop="item"/>
  <EquipSlot id="armor" drop="item"/>
</HStack>
<Template name="EquipSlot">
  <Frame size="96x96" radius="10" color="@slot">
    <Show on="drop-armed"><Frame anchor="stretch" radius="10" borderWidth="2" borderColor="@accent"/></Show>
    <Animation on="drop-over" reverse-on="drop-out" scale="1:1.08" duration="0.1s">
      <Frame anchor="stretch" radius="10" color="@accent/0.2"/>
    </Animation>
    <Frame id="equipped" anchor="stretch" drag="item">   <!-- 已装备的也能拖回背包 -->
      <Image id="icon" anchor="stretch" margin="8"/>
    </Frame>
  </Frame>
</Template>
```

## 6. C# 面

### 6.1 API

```csharp
public interface IControl
{
    // …现有成员…

    /// 这个控件在拖放里代表什么：作拖源时是携带的数据，作目标时是它的身份（格子下标、槽位枚举）。只由 C# 写。
    public object DragData { get; set; }

    /// 作为目标的额外条件，叠在 kind 匹配之上；每次拿起时对每个候选目标问一次。null = 接受。
    public Func<DragInfo, bool> CanDrop { get; set; }
}

public abstract class Control
{
    [UIAttr("drag")] public string DragKind { get; set; }    // null / "none" = 不可拖
    [UIAttr("drop")] public string DropKinds { get; set; }
    // dragHold / dragHandle / dragDuration：属性带 Attr 后缀（同 ReorderHoldAttr），解析后的值 internal

    /// 作为目标：一次被接受的放下落在这里（先于 UI.Drag.OnDrop）。
    public Observable<DropEvent> OnDrop { get; }
}

public static partial class UI
{
    public static class Drag
    {
        public static bool IsDragging { get; }                 // 拿起到松手（飞行期不算）
        public static DragInfo Current { get; }                // 同上期间非 null
        public static Observable<DragInfo> OnLift { get; }
        public static Observable<DragInfo> OnHover { get; }    // 悬停目标变了（Target 可为 null）
        public static Observable<DropEvent> OnDrop { get; }    // 任何 Screen 里被接受的放下
        public static Observable<DragInfo> OnEnd { get; }      // 每次会话恰好一次：Result = Dropped / Returned / Cancelled
        public static void Cancel();                           // 代码取消（§7.7）
        public static int SortingOrder { get; set; } = 4000;   // 抬层 Canvas 的 sortingOrder
    }
}

public class DragInfo
{
    public string Kind { get; }
    public IControl Source { get; }      public object Data { get; }        // = Source.DragData（拿起时取）
    public IScreen SourceScreen { get; } public int SourceIndex { get; }    // 源所在 <ScrollList> / <Carousel> 行的下标，否则 -1
    public IControl Target { get; }      public object TargetData { get; }  // 悬停 / 放下的目标；没有为 null
    public IScreen TargetScreen { get; } public int TargetIndex { get; }
    public Vector2 ScreenPosition { get; }                                  // 指针屏幕坐标（拖到 UI 之外时 C# 自己射 3D）
    public DragResult Result { get; }                                       // 只在 OnEnd 里有意义
}

public sealed class DropEvent : DragInfo
{
    /// 放下作废：不发 receive，源从手指处飞回家（C# 校验失败时用）。
    public void Reject();
    /// 从手指处飞进来的控件。默认 = 目标子树里唯一一个 active、同 kind 的拖源；null = 不飞。
    public void Land(IControl element);
    /// 追加一段飞行：element 从 from 的位置飞到它自己的排版位置（交换时被换走的那个）。
    public void Fly(IControl element, IControl from);
}
```

- `IControl` 加两个可写成员：bind 回调拿到的是 `IControl`，要在那里写。`IControl` 只由 `Control` 实现（自定义控件
  都继承 `Control`），实际不破坏兼容。
- `SourceIndex` / `TargetIndex`：最近的 `<ScrollList>` / `<Carousel>` 行的下标（拖源在行里面时也是行的下标）。
  静态布局（`<Grid>` 里九个模板调用）用 `DragData` 或 `Id`。

### 6.2 契约

- `OnDrop` 发出时：源已回到家（入流、placeholder 已摘），视觉还停在手指处；目标仍处于 `drop-over`；
  `UI.Drag.IsDragging` 已为 false。**在订阅里同步推送是安全的，也正是期望的写法。**
- 改模型并同步推送（乐观 UI）；服务端拒绝 → 把旧数据再推一次，格子自己回去（与 reorder 同构：模型是真相）。
- 要等异步确认、不想乐观的：`e.Reject()` 让源飞回，确认后再推。
- 先 `target.OnDrop`，后 `UI.Drag.OnDrop`；任何一个订阅者调 `Reject()` 都作数。`Land` / `Fly` 在所有订阅者跑完后统一结算；
  传入已销毁的控件被忽略。
- 被接受却什么都没推：默认的落地飞行会把目标里**原来的**内容从手指处飞进去 —— 这是在提示「你接了却没改」，按上面两条之一写。
- `OnEnd` 每次会话恰好一次，在 `OnDrop`（若有）之后；`Target == null && Result == Returned` 加 `ScreenPosition` 就是
  「拖到 UI 之外」（拖到 3D 世界的单位上，C# 自己射线）。

### 6.3 示例

```csharp
// 阵形：交换
var grid = screen.Get<ScrollList>("formation");
var formation = new ReactiveProperty<IReadOnlyList<UnitVm>>(units);      // 九项，空格为 null
grid.BindItems(formation, (IControl slot, UnitVm u) =>
{
    slot.Get<Frame>("unit").Hidden = u == null;
    slot.Get<Text>("name").TextValue = u?.Name ?? "";
    // 头像等同理
}).AddTo(screen);

UI.Drag.OnDrop.Where(e => e.Kind == "unit").Subscribe(e =>
{
    int a = e.SourceIndex, b = e.TargetIndex;
    var next = formation.Value.ToArray();
    var swapped = next[b] != null;
    (next[a], next[b]) = (next[b], next[a]);
    formation.Value = next;                       // 同步推送：A、B 两格重绑
    if (swapped) e.Fly(e.Source, from: e.Target); // A 格里的卡现在显示被换走的那个：让它从 B 飞回 A
    api.SaveFormation(next);
}).AddTo(screen);

// 背包 → 装备：类型过滤
foreach (var (id, type) in new[] { ("weapon", ItemType.Weapon), ("armor", ItemType.Armor) })
{
    var slot = equip.Get(id);
    slot.DragData = type;
    slot.CanDrop = d => d.Data is ItemVm it && it.Type == type;   // 不合的槽：不亮 drop-armed，悬停时 drop-deny
}
bag.BindItems(items, (IControl cell, ItemVm it) => cell.Get("item").DragData = it).AddTo(screen);
UI.Drag.OnDrop.Where(e => e.TargetScreen == equip)
  .Subscribe(e => inventory.Equip((ItemVm)e.Data, (ItemType)e.TargetData))  // 内部同步推送背包与槽位
  .AddTo(screen);
```

## 7. 语义细节

### 7.1 手势仲裁（从 `ReorderDriver` 抽出的 `LiftGesture`）

- **抽出**：phase 机（Idle / Pressed / Armed / Dragging / Scrolling / Swallow）、hold 计时、`Released()` 的设备轮询
  （含 `PressedProbeForTests`）、`IsTouch`、`IsAnotherPointer`、转发给父级、`eligibleForClick`。`ReorderDriver` 改为
  持有一个 `LiftGesture`，行为与测试不变（M0 的验收标准）。
- **`DragSource`** 挂在拖源的 `LayoutHost` GO 上，实现 `IInitializePotentialDragHandler` / `IBeginDragHandler` /
  `IDragHandler` / `IEndDragHandler`。uGUI 从命中物向上找 handler，拖源比外层 `ScrollRect`、Content 上的
  `ReorderDriver`、`CarouselView` 都深，先拿到；不是拖放的手势逐事件 `ExecuteHierarchy` 转发给 `transform.parent`。
- **外层滚动容器的识别**：向上找第一个实现 `IBeginDragHandler` 的组件（`PuiScrollRect` / `CarouselView` / …）。它的
  滚动轴：`ScrollRect.horizontal` / `vertical`，Carousel 恒为横向；两轴都滚 → 没有跨轴捷径，只靠 hold。没有外层 →
  `auto` 在触屏上也是 0。
- **跨轴捷径**（仅触屏、仅单轴外层）：Pressed 期间 `OnBeginDrag` 到来时，看 `e.position - e.pressPosition` 与滚动轴的
  夹角，> 60° → 立即拿起（Dragging，不转发）；否则 Scrolling。uGUI 的拖动阈值（`EventSystem.pixelDragThreshold`）保证
  此时位移已足够判方向。
- 只响应左键；`PeekInteractable == false` 或 inactive 的拖源按不起；`dragHandle` 的几何判定同 `reorderHandle`（找不到
  → warn 一次并整块可拿）。
- 全局同时只有一个会话；会话进行中，其它拖源的按下不进会话（照常转发）。

### 7.2 拿起：原地抬层

1. **出流**：父级有 `LayoutGroup` → `LayoutElement.ignoreLayout = true`，同尺寸 placeholder 插在源**之后**（复用 reorder
   的 placeholder 助手）。源**不改** sibling 顺序 —— 抬层 Canvas 已经让它画在最上，不需要 `SetAsLastSibling`。已经是
   `flow="false"` 的源不需要占位；结束时 `ignoreLayout` 还原成拿起前的值。自由定位的父级什么都不用做。
2. **抬层 Canvas**（`LiftLayer`）：`AddComponent<Canvas>`；`overrideSorting = true`、`sortingOrder = UI.Drag.SortingOrder`、
   `sortingLayerID` 取根的；`additionalShaderChannels` 取最近的父 Canvas 的、`vertexColorAlwaysGammaSpace` 取根的；
   **不加** `GraphicRaycaster`。源 GO 上本来就有 Canvas（极少：它是个弹层面板）→ 记下原值、改、结束还原。
   加 Canvas 会让子树的 Graphic 重新登记到它名下并重建一次 —— 只此一个控件，一次性。
3. **跟手**：`ScreenPointToLocalPointInRectangle(parent, e.position, pressEventCamera)`，`anchoredPosition = 拿起时位置 +
   本地位移`，不锁轴。1:1 跟手、隐式处理 CanvasScaler / 相机 / 祖先缩放旋转（同 reorder §5.9）。runner 每帧重跑一次。
4. **点击**：`pointerPress` 补 `pointerUp`，`eligibleForClick = false`，`pointerPress = null`（§7.9）。
5. 源的 `lift` 挂钩 / 默认外观；所在 `<Carousel>` 暂停 autoplay。
6. 武装（§7.3），`UI.Drag.OnLift`。

### 7.3 武装与命中

- **目标注册**：`DropKinds` 非空的控件在 apply pass 结束时进全局注册表（`DropTargetMarker` 挂在目标 GO 上，供向上查找），
  `none` / Dispose / GO 销毁时出表。
- **拿起时遍历注册表**：kind 匹配 → 排除 inactive、源的祖先、所在 Screen `IsClosing` 的 → `interactable == false` 或
  `CanDrop` 返回 false 的记为 **denied** → 其余 **armed**：发 `drop-armed`、开命中。`CanDrop` 每个目标每次会话只问
  一次，结果缓存。会话进行中新注册的目标（拖动中打开的弹层）在注册时补判。
- **开命中**不改作者的 `raycastTarget` 声明，是会话期的临时输入：
  - 目标 GO 上没有 Graphic → 挂 / 启用 `HitCatcher`（无几何的命中层，ReorderDriver 的同款；一个 GO 只能有一个 Graphic，
    所以只用在没有 Graphic 的节点上）；
  - 有 Graphic（`<Frame color>` 的面板、`<Image>`、`<Btn>`…）→ 临时 `raycastTarget = true`；
  - 解除时还原：`HitCatcher` 禁用；Graphic 交回控件自己的命中决策（`RaycastIntent` / Frame 的面板开关重算一次）。
  - denied 的目标也开命中 —— 否则手指在它上面时射线落到它后面，`drop-deny` 就显示不出来。
- **拖源常驻命中**：`drag=` 是意图，与订阅 `OnPointer*` 同一条规则 —— 同样三种实现，但不随会话开关。与作者显式的
  `raycastTarget="false"` 冲突 → 告警一次（拿不起来），作者的声明赢（同 `RaycastIntent` 的 contradiction）。
  点击照常：uGUI 的点击 handler 从命中物向上找，拖源里没有就找到外层的 `<Btn>`。

### 7.4 悬停判定

- **每帧**（指针没动也做 —— 自动滚动、动画会让目标在手指下移动）：`EventSystem.current.RaycastAll(pointer)`，只取
  **第一个**结果（最上层），从它向上走：
  - 遇到 kind 匹配的目标：armed → over；denied → deny；结束。
  - 遇到 kind 不匹配的目标：透明，继续向上。
  - 走到根都没有 → 无悬停目标。
- 只看第一个结果，所以被模态 backdrop、引导遮罩、弹层 blocker、另一个 Screen 盖住的目标，与点击一样够不着（P6 / P26）。
- 变化时：旧目标 `drop-out`，新目标 `drop-over` / `drop-deny`，`UI.Drag.OnHover`。
- 拖着的源在抬层 Canvas 里（无 raycaster），不会出现在结果里（P5）。
- `EventSystem` 也会把 3D `PhysicsRaycaster` 的命中排进来；它们身上没有目标标记，等同「无目标」。

### 7.5 自动滚动

- 指针在某个 `<ScrollList>` 视口主轴两端的边缘带内，且它的 Content 里有 armed / denied 目标 → 按 reorder §5.6 的同一
  公式滚它（抽成共享件 `EdgeAutoScroller`，`ReorderDriver` 也改用它）。
- 源所在的列表也可能被滚（往同一个背包里的另一格拖）：源随 Content 移动，runner 下一帧重新跟手，源仍在手指下。

### 7.6 松手：先复位、再发事件、后飞行

1. 记下源此刻的屏幕矩形（四角）与世界缩放 —— 「图」最后的样子。
2. **结构复位**：placeholder 摘走（先 `SetParent(null)` 再销毁，同 reorder §5.2）、`ignoreLayout` 还原、
   `ForceRebuildLayoutImmediate(parent)` → 源有了排版位置；视觉立刻钉回手指处（FLIP 的 First），抬层 Canvas 保留。
3. 悬停在 armed 目标上 → 建 `DropEvent`，依次发 `target.OnDrop`、`UI.Drag.OnDrop`（同步）。会话此时已不在 Dragging，
   订阅里的推送不会取消任何东西。没有目标 / 在 denied 上 → 结果 Returned。
4. **结算**：
   - 被接受且没有 `Reject()`：
     - 落点控件 = `e.Land(…)` 指定的；没指定 → 目标子树里**唯一一个** active、同 kind 的拖源（阵形格里的卡、装备槽里的
       已装备图标）；零个或多个 → 不飞。
     - 落点控件从步骤 1 的矩形飞到自己的排版位置：位置换算到它父级的本地坐标（它画布的相机），`localScale` 从
       「图宽 / 落点控件宽」× 原值补间到原值；飞行期间它也挂抬层 Canvas（压过一切、不被 mask 裁）。
     - 源已经在家，显示的是推送后的内容，不飞 —— 除非 C# `e.Fly(e.Source, from: e.Target)`（交换）。
     - 目标发 `receive`。
   - 未接受 / `Reject()`：源从步骤 1 的矩形飞回家（它的内容没变，飞的正是它自己）。
   - `e.Fly(x, from)`：x 从 `from` 的屏幕矩形飞到 x 自己的位置。
   - 源发 `drop`；所有目标 `drop-out`，armed 的再发 `drop-disarmed`，撤命中；`UI.Drag.OnEnd`。
5. 飞行只写 `anchoredPosition` / `localScale`（视觉层，同 REO-D5）。飞完摘抬层 Canvas、默认外观缩回。飞行中的控件被
   销毁 → 回调判空跳过。`dragDuration="0"` 或 `!Application.isPlaying` → 全部瞬移（EditMode 测试当帧断言）。
6. 飞行期不属于会话（同 reorder 的 Settling）：新会话可以立刻开始；正在飞的控件被再次拿起 → 先让它瞬移到位再拿起。

### 7.7 取消

Pressed / Dragging 期间以下任一发生 → 立即复位（placeholder 摘走、`ignoreLayout` 还原、摘抬层 Canvas、外观复位，
**不飞**），源发 `drop`，目标 `drop-out` / `drop-disarmed`，**不发** `OnDrop`，`OnEnd(Cancelled)`；本次手势余下的
drag / end 事件吞掉（不转发 —— 半途交给 ScrollRect 一个没有 begin 的 drag 会让它猛跳，同 reorder §5.7）：

- 源所在列表的推送 / `ClearSlots` / `itemTemplate` 变更 / Dispose（`ScrollList` / `Carousel` / `TabGroupCore` /
  `Suggestions` 的 Rebuild 入口，与 `_reorder?.Cancel()` 同一处）；
- **任何** Screen 的 ReSolve（Variant / 主题 / 转屏 / 语言切换本来就是全局的，一律取消，不去判断是否波及）；
- 源 Dispose / 被销毁 / `Hidden = true` / `Interactable = false` / `drag` 变为 none（setter 入口 + runner 每帧自检）；
- 源所在 Screen 开始关闭、热重载；
- 一个新层出现在源之上（`PopupChain.DismissBelow` 的同一个信号：模态、路由 Modal）；
- Esc / 手柄 East：`EscapeChain` 新增一环（引导之后、下拉列表之前），取消会话并消费这次按键；
- 系统取消触摸（`TouchPhase.Canceled`：设备轮询里与「松手」区分开）；
- `UI.Drag.Cancel()`。

目标侧的变化不取消会话：被悬停的目标销毁 / 隐藏 → 只是没有悬停目标了；目标所在 Screen 关闭 → 它的目标出表。

### 7.8 跨 Screen

- **层级**：抬层 Canvas 4000 压过页 / Modal / Toast / Tutorial（P2）；`UI.Drag.SortingOrder` 可调。
- **落点**：`RaycastAll` 本来就遍历所有 Screen 的 `GraphicRaycaster`；事件带 `SourceScreen` / `TargetScreen`。
- **坐标**：跟手只用源的父级与源画布的相机；落地飞行只用落点控件的父级与它画布的相机 —— 两个 Screen 的缩放系数、
  相机互不相干。
- **尺寸**：拖动中保持源的尺寸；落地时缩放过渡（P16）。
- **Camera / Overlay 混用**的限制见 P17。
- **订阅**：两边都可能想知道 —— `UI.Drag.OnDrop` 是全局流，`.AddTo(screen)` 照常。

### 7.9 与点击、导航、弹层、引导的关系

- **点击**：鼠标 —— 第一帧拖动才拿起，之前一直是点击；触屏 —— hold 到点前抬手 = 点击。拿起时补做 uGUI 进入 drag
  时自己会做的事（`pointerUp` 给 `pointerPress`、清点击资格），所以长按拿起的 `<Btn>` 不会一路顶着 Pressed 的暗色。
  这与 reorder §5.8「不干预」不同：那里行留在原地，这里它飞着（建议 reorder 也对齐，另开）。
- **导航**（手柄 / 键盘）：v1 不支持拖放（§14）；拖放期间切到方向模式不影响会话。
- **弹层**：拖源在 Popover / TabMenu 面板里 —— 嵌套 Canvas 里的嵌套 Canvas，照样浮在最上；面板的 blocker 挡住下面的
  目标（P26）。拿起不关弹层（P18）。
- **引导**：抬层 4000 > Tutorial 3000，图不会钻到遮罩下；落点受遮罩镂空约束（P26）。

## 8. lint

| 代码 | 条件 | 哪一遍 |
|---|---|---|
| `PUI-DRAG-VALUE` | `drag` 不是 kind（含写成 `true` / `false`）；`drop` 列表里有非法 kind；`dragHold` 不是 `auto` / 时长；`dragDuration` 不是时长。base 与 variant、经 `class=` 的都查 | raw + expanded |
| `PUI-DRAG-HANDLE-ID` | `dragHandle="x"`，但拖源子树里没有 `id="x"` | expanded |
| `PUI-DRAG-REORDER` | `drag=` 写在 `reorder="true"`（任何 variant）的 `<ScrollList>` 的行上或行里 | expanded |
| `PUI-DRAG-VIRTUAL` | `drag=` 在 `virtualize="true"` 的 `<ScrollList>` 的行里 | expanded |
| `PUI-DRAG-CAROUSEL-CARD` | `drag=` 写在 `<Carousel>` 的卡根上（应写在卡里面） | expanded |
| `PUI-DROP-NO-SOURCE` | 裸 `drop-armed` / `drop-disarmed` / `drop-over` / `drop-deny` / `drop-out` / `receive` 没有 `drop=` 祖先（或自身）；模板体、`@id` 形式免检 | raw |
| `PUI-LIFT-NO-SOURCE`（扩展） | 裸 `lift` / `drop` 既没有 `<ScrollList>` 祖先，也没有 `drag=` 祖先（或自身） | raw |

- 规则放 `Core/Lint/DragDropRules.cs`（纯 C#，不碰 Unity 类型）；后两条扩在 `StateTriggerRules.cs`。`IRWalker`（CLI error）
  与 `ScreenInstantiator`（运行期 `UILog.Warn`）共用；坏值在 setter 里已经 warn 的不再镜像（reorder §14.1-3 同理）。
- `<Show on="receive">` 与现有的 `<Show on="drop">` 同一条运行期错误路径。

## 9. XSD

`Editor/XsdGenerator.cs` 的 `_commonAttrNames` 加 `drag` / `drop` / `dragHold` / `dragHandle` / `dragDuration` —— 它们是
`Control` 基类的 `[UIAttr]`，与 `stateReact` 同理：放进公共属性组，逐控件反射时跳过，免得重复声明。`on=` 是
`xs:string`，不动。生成器测试加 `StringAssert.Contains` 断言（同现有约定）。

## 10. 实现地图

| 文件 | 改动 |
|---|---|
| `Runtime/Controls/Internal/LiftGesture.cs`（新，从 ReorderDriver 抽） | phase 机、hold、设备轮询判松手、IsTouch、转发；跨轴判定（新增，只有 DragSource 打开） |
| `Runtime/Controls/Internal/FlowPlaceholder.cs`（新，抽） | placeholder 建 / 复制几何 / 摘除 |
| `Runtime/Controls/Internal/EdgeAutoScroller.cs`（新，抽） | 视口边缘自动滚动 |
| `Runtime/Controls/Internal/LiftLook.cs`（新，抽） | 默认 1.03 外观（改为相对拿起时的 `localScale`） |
| `Runtime/Controls/Internal/ReorderDriver.cs` | 改用上面四个；行为不变 |
| `Runtime/Controls/Internal/DragSource.cs`（新） | 拖源 GO 上的组件：uGUI 事件 → LiftGesture → DragSession |
| `Runtime/Controls/Internal/LiftLayer.cs`（新） | 抬层 Canvas 的加 / 复制设置 / 摘 |
| `Runtime/Controls/Internal/DropTargetMarker.cs`（新） | 目标 GO 上的标记：Owner、kinds、armed / over / deny、挂钩注册（同 `ReorderRowMarker` / `ExpandableMarker`） |
| `Runtime/Controls/Internal/SettleFlight.cs`（新） | FLIP 飞行（位置 + 缩放），飞行期抬层 |
| `Runtime/Application/DragSession.cs`（新）、`UI.Drag.cs`（新） | 会话状态、注册表、武装、悬停、松手 / 结算 / 取消、事件流、全局 runner、测试缝 |
| `Runtime/Controls/Control.cs` / `IControl.cs` | 五个 `[UIAttr]`、`DragData`、`CanDrop`、`OnDrop`；命中意图钩子；Dispose / Hidden / Interactable 通知会话 |
| `Runtime/Controls/Frame.cs` / `Image.cs` / `RawImage.cs` / `Text.cs` | 命中意图：常驻（拖源）与会话期临时（目标）两个输入并入既有的 `raycastTarget` 决策 |
| `Runtime/Controls/ScrollList.cs` / `Carousel.cs` / `Internal/TabGroupCore.cs` / `Suggestions.cs` | Rebuild / Clear / Dispose 入口取消源在其内的会话；行下标查询；Carousel 拖动期间暂停 autoplay |
| `Runtime/Application/Screen.cs` | ReSolve 开头、Close 开头取消会话 |
| `Runtime/Controls/Internal/PopupChain.cs` | `DismissBelow` 顺带取消源在其下的会话 |
| `Runtime/Application/EscapeChain.cs` | 新一环：会话中的 Esc / East = 取消 |
| `Runtime/Controls/Internal/TriggerSpec.cs` | `DropArmed` / `DropDisarmed` / `DropOver` / `DropDeny` / `DropOut` / `Receive` 及 `@` 前缀 |
| `Runtime/Controls/Internal/TriggerSourceResolver.cs` | `FindReorderRow` → `FindLiftSource`（拖源或行，近者胜）；新 `FindDropTarget` |
| `Runtime/Controls/Trigger.cs` / `Show.cs` | 绑新流；Show 接五个状态值，`receive` 报错 |
| `Runtime/Core/Template/TemplateExpander.cs` | `CommonAttrs` 加五个属性 |
| `Runtime/Core/Lint/DragDropRules.cs`（新）、`StateTriggerRules.cs`、`IRWalker`、`ScreenInstantiator` | §8 |
| `Editor/XsdGenerator.cs` | §9 |

`Core/IR` / `Parser` / `Template` / `Lint` 的改动保持纯 C#（CLI 编译子集的约束）。

## 11. 测试（Red 先行）

EditMode（`Tests/EditMode/Controls/DragDropTests.cs`）：事件用手拼的 `PointerEventData` 直接打到 `DragSource`（同
`ScrollListReorderTests`），`LiftGesture.PressedProbeForTests` 顶替设备，`DragSession.TickForTests(dt)` 推时钟，
`DragSession.HitTestForTests` 顶替 `RaycastAll`；`!isPlaying` → 飞行瞬移，当帧断言。

1. **属性**：`drag="unit"` → 拖源 GO 上有 `DragSource`、可命中；`drop="unit"` → 进注册表；`none` / Variant 关掉 → 失效；
   模板调用 `<Card drag="unit"/>` 落到实例根；`drag="true"` 告警。
2. **鼠标拿起**：按下 + begin → 抬层 Canvas（overrideSorting、sortingOrder、channel / gamma 复制、无 GraphicRaycaster）；
   父为 LayoutGroup 时 placeholder 同尺寸、源 `ignoreLayout`、Content preferred size 不变、源 sibling 不变；
   `eligibleForClick == false`；`pointerPress` 收到 `pointerUp`。
3. **触屏**：ScrollList 里 —— hold 前沿轴 begin → 转发（根上挂计数 `IDragHandler` 替身）、不拿起；跨轴 begin → 立即拿起；
   hold 到点 → 拿起。不在滚动容器里 → 第一帧拿起。`dragHandle` 的三种情况。
4. **武装**：kind 匹配 + `CanDrop` true → armed（`drop-armed`、命中开）；`CanDrop` false / `interactable=false` → denied；
   kind 不匹配 → 都不是；源的祖先排除；`CanDrop` 每会话每目标一次；结束后全部 disarmed、命中还原 ——
   `<Frame color>` 面板、`<Image>`（`RaycastIntent`）、无 Graphic 的容器（`HitCatcher`）三种各一条。
5. **悬停**（测试缝）：over / deny / out 的边沿与 `<Show>` 可见性；kind 不匹配的内层目标透明、命中外层目标；`OnHover`。
6. **放下**：在 `OnDrop` 订阅里断言结构已复位（placeholder 不在、`ignoreLayout` 已还原、`IsDragging == false`）；
   `target.OnDrop` 先于 `UI.Drag.OnDrop`；`Source` / `Target` / `Data` / `TargetData` / 两个 Index / 两个 Screen 正确；
   `receive` 在 `OnDrop` 之后；`OnEnd(Dropped)` 恰好一次。
7. 订阅里同步 `BindItems` 推送 → 不取消、不抛；推送后各格内容正确。
8. **飞行**：默认落点 = 目标里唯一同 kind 拖源（两个时不飞）；`Land(x)` / `Land(null)`；`Fly(Source, from: Target)`；
   `Reject()` → 源飞回、无 `receive`；已销毁的控件被忽略。
9. 在空地 / denied 目标上松手 → 无 `OnDrop`、`OnEnd(Returned)`、源回家。
10. **取消矩阵**（每条都断言：复位、`drop` 发、无 `OnDrop`、`OnEnd(Cancelled)`、余下 drag / end 被吞）：推送到源列表、
    `Screen.ReSolve`（Variant 切换）、源 Dispose / `Hidden` / `Interactable = false` / Variant 关掉 `drag`、源 Screen Close、
    `DismissBelow`（打开模态）、Esc（EscapeChain 的测试按键入口）、`UI.Drag.Cancel()`。
11. 第二根指针的事件不进会话；会话中另一个拖源的按下照常转发。
12. **跨 Screen**：源在 Screen A、目标在 Screen B → 事件带两个 Screen；B 关闭 → 它的目标出表、悬停清空、会话继续。
13. **挂钩**：`lift` / `drop` 近者胜（拖源在列表行里）；`on="lift"` 挂钩关掉默认外观；目标外的裸 `drop-over` 抛；
    `<Show on="receive">` 抛；`drop-disarmed` / `drop-out` 在打开时确立、不触发普通 `<Trigger>`。
14. **自动滚动**：指针在含 armed 目标的列表边缘 → Content 移动、悬停重算、源仍在手指下。
15. **Carousel**：拖动期间 autoplay 暂停，结束恢复。
16. **回归**：M0 之后 `ScrollListReorderTests` 一行断言不改、全绿。

Lint（`Tests/EditMode/Lint/DragDropRulesTests.cs`）：§8 每个代码一正一反；模板体 / `@id` 免检；拖源里的裸 `lift` 不报
`PUI-LIFT-NO-SOURCE`。XSD：生成器测试断言五个属性在 `commonAttrs` 里。

PlayMode（`Tests/PlayMode/Controls/DragDropPlayTests.cs`，真 `EventSystem` + `GraphicRaycaster`）：

1. 页（0）+ 模态（1000）两个 Overlay Screen：从页拖到模态里的槽 → 悬停、放下成功；
2. 模态 backdrop 盖住的页面槽 → 放不进去；
3. 拿起的源画在模态之上，拖出 ScrollList 视口仍然完整可见（不被 mask）；
4. 源里的 `<Frame radius>` 拿起后仍出几何（抬层 Canvas 带 TexCoord1）；
5. 飞行结束：抬层 Canvas 摘除、位置到排版位置、Content 下无残留 placeholder。

## 12. SKILL / 文档 / 演示同步（同一 PR，英文）

| 文件 | 改动 |
|---|---|
| `authoring-promptugui-xml/reference/drag-drop.md`（新） | 属性；手势（`auto` 两端差异、跨轴、handle）；源 / 目标挂钩与默认外观；武装 / 悬停 / 结算规则；跨 Screen；取消；lint；限制（P17 / P26）；完整 XML + C# 示例（阵形交换、背包 → 装备） |
| `authoring-promptugui-xml/SKILL.md` | Common attributes 表加 `drag` / `drop` 两行 + 指针；Pointer hit-testing 段一句（`drag=` 让节点可命中，`drop=` 只在武装期）；速查表 |
| `authoring-promptugui-xml/reference/animations.md` | `on=` 表：`lift` / `drop` 的解析推广 + 六个目标值 + `@id`；「resolve upward」段补目标 |
| `authoring-promptugui-xml/reference/states.md` | `<Show>` 合法值清单 |
| `authoring-promptugui-xml/reference/reorder.md` | 「Not in v1」那行指向 `drag-drop.md`；`lift` / `drop` 的近者胜一句 |
| `scripting-promptugui-csharp/SKILL.md` | 新「Drag and drop」小节：`UI.Drag`、`DragData` / `CanDrop` / `OnDrop`、契约（同步推送、`Reject`、`Land` / `Fly` 交换配方、`SourceIndex`）；速查表 |
| `AGENTS.md` | 触发路由表加 `drag` / `drop` → `reference/drag-drop.md` |
| `Samples~/CommonControls` | 一个 3×3 小阵形（交换）+ 一个「列表 → 两个槽」演示；改完跑 UIXmlLint |

## 13. 待确认的决策

| # | 决策 | 理由 | 备选 |
|---|---|---|---|
| DND-D1 | 拖的是**本体**，原地抬层；源原来的位置在拖动中变空（可用 `<Show on="lift@x">` 画空位） | 零配置、像素级一致（P1）；阵形移动本来就是「拿走」 | 「原物留着、另一张图跟手」→ v2 `dragGhost="Template"` |
| DND-D2 | 抬层 = 嵌套 Canvas（overrideSorting、复制 channel / gamma、无 raycaster），拿起时加、飞完摘 | P2–P5 | reparent 到顶层 Canvas（§3-E） |
| DND-D3 | 落点 = `RaycastAll` 最上层命中向上找；目标只在武装期可命中 | P6 / P7 | 几何判定 |
| DND-D4 | 属性做在 `Control` 基类：`drag` / `drop` / `dragHold` / `dragHandle` / `dragDuration`；C# 名 `DragKind` / `DropKinds` | 行为不是部件；Variant 可切 | 包装元素 |
| DND-D5 | `dragHold="auto"`：鼠标 0；触屏只在有外层滚动时 0.4s；单轴滚动里跨轴起手立即拿起 | P11 | 触屏一律长按（需求原话「触屏长按才拿起」—— 请确认） |
| DND-D6 | 先复位结构、再同步发事件、再 FLIP 飞行 | P9；REO-D5 | 先飞后发（事件晚 0.18s，飞行中到来的推送进退两难） |
| DND-D7 | 库不搬数据、不猜交换；默认只飞「目标里唯一的同 kind 拖源」，交换用 `e.Fly(e.Source, from: e.Target)` | P8 | 自动交换动画（错的飞行比不飞更糟） |
| DND-D8 | 源一侧复用 `lift` / `drop`，解析为最近的拖源或列表行 | 一套词汇；一份模板两用 | 新词 `drag-start` / `drag-end` |
| DND-D9 | 目标一侧六个值：`drop-armed` / `drop-disarmed` / `drop-over` / `drop-deny` / `drop-out` / `receive`；持久对 + 一个边沿 | 与 expand / collapse、checked / unchecked 同形；`reverse-on` 有唯一出口 | `state-*` 式互斥状态族（over 直接跳回空闲时 `reverse-on` 找不到出口） |
| DND-D10 | 目标没有默认悬停外观 | §3-K | 默认 1.05 缩放 |
| DND-D11 | `UI.Drag.SortingOrder = 4000`（Tutorial 3000 之上） | 引导步里拖动，图不能钻到遮罩下 | 2500（引导之下） |
| DND-D12 | 任何 ReSolve 都取消会话 | Variant / 主题 / 转屏是全局的；REO-D12 | 只在波及源时取消 |
| DND-D13 | 全局单会话 | 同 ScrollRect；没有多指同拖的需求 | — |
| DND-D14 | 源的祖先目标不参与武装；放回原处 = 不发事件 | P19；同 reorder 的 from == to | — |
| DND-D15 | 时间一律 unscaled | P24 | — |
| DND-D16 | 拿起时给 `pointerPress` 补 `pointerUp` | P12 | reorder 的「不干预」 |
| DND-D17 | 手柄 / 键盘拖放不进 v1，但会话核心与输入无关 | 范围；v2 只需再加一个驱动 | v1 一起做 |

## 14. 非目标 / v2

- **手柄 / 键盘「搬运模式」**：Submit 拿起、方向键在 armed 目标间移动、Submit 放下、Cancel 放回。会话的拿起 / 悬停 /
  放下 / 取消是与输入无关的 API，指针只是其中一个驱动，v2 加一个方向驱动即可；还要设计目标不是 Selectable 时的焦点代理、
  `<FocusCursor>` 带着图走。
- `dragGhost="Template"`：原物留着，另一张图跟手（复制语义），C# 在 `OnLift` 里绑图。
- 列表内排序 + 拖出列表（reorder 行里的 `drag=`）。
- 虚拟列表的行作拖源（会话期间钉住源行）。
- 悬停在 `<Tab>` / 翻页按钮上一段时间自动切页（spring-loaded）。
- 源一侧的「当前落点是否有效」挂钩（图变红）—— v1 用 `UI.Drag.OnHover` 在 C# 做。
- 多会话 / 多指同时拖。
- 跨 Screen 时图按目标的缩放实时变大小（v1 只在落地时过渡）。
- Camera / Overlay 混用时的拖动层（P17）。
- 拖动中的 haptic。

## 15. 里程碑（一个 PR，分步提交）

- **M0 抽共享件**：`LiftGesture` / `FlowPlaceholder` / `EdgeAutoScroller` / `LiftLook`；`ReorderDriver` 改用它们；
  reorder 的全部测试原样绿。
- **M1 会话 + 事件**：属性、`DragSource`、`DragSession`、武装 / 悬停（测试缝）/ 松手 / 取消、`UI.Drag` 与 `OnDrop` 契约、
  EditMode 测试 1–12、16。
- **M2 视觉**：抬层 Canvas、结算飞行、源 / 目标挂钩、默认外观、自动滚动、Carousel；EditMode 13–15；PlayMode 1–5。
- **M3 lint + XSD + 文档 + 演示**。
