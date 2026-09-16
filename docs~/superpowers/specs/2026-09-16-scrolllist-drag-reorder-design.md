# `<ScrollList reorder>` —— 拖动排序：实时让位、FLIP 挤压、落位补间

> 状态：**已实现**（2026-09-16，分支 `feat/scrolllist-drag-reorder`；作者指示跳过 plan 直接实现、分步提交。实施记录见 §14）。
> 需求来源：宿主工程里一个「排优先级」的列表 —— 拖一行到别处，其余行实时让位，松手后被拖的行补间落进空位、被挤的行滑到新位置。PC 宽屏（鼠标）与手机竖屏（触屏）同一份 XML。
> 相关：
> `2026-09-14-scrolllist-row-reuse-design.md`（第 i 行 ↔ 第 i 项的位置复用契约 —— 本文的数据契约建在它上面；§2-B「按 key diff」仍然不做）、
> `2026-09-10-scrolllist-grid-and-static-children-requirements.md`（网格模式 / 静态子卡 —— 两者都要能拖）、
> `2026-06-04-carousel-design.md`（`CarouselView`：驱动器挂在容器上收 drag、`ForwardToParent` 把滚动让给外层、屏幕→本地坐标 1:1 跟手、LitMotion 吸附 —— 本文手势层的全部先例）、
> `2026-08-31-collapsible-design.md` §4.5（`expand` / `collapse` 用 `ExpandableMarker` 向上解析 —— `lift` / `drop` 照搬）、
> `2026-08-31-hug-reveal-flip-checked-design.md`（`reverse-on=`、`checked` / `unchecked` 持久对与 `<Show>` 的注册式可见性 —— `<Show on="lift">` 走同一条路）、
> `2026-09-15-raycast-target-design.md`（「hit-testing 是声明的，不是画出来的」—— 本文给 Content 加的 catcher 遵守它）。

## 1. 问题

`<ScrollList>` 今天没有「更新顺序」这个动作：`BindItems` 按位置重绑，宿主要改顺序只能改数据再推一遍，
行瞬移到新位置。作者要的是一个能**用手拖**的优先级列表：

1. 按住一行、拖到别处，其余行**实时让位**（不是松手才挤）；
2. 松手后被拖的行**补间落进空位**，不是瞬移；
3. 数据顺序跟着变，且宿主能拿到「从哪到哪」；
4. 鼠标和触屏都能用 —— 而触屏上「拖」本来就是滚动手势，两者得能分开；
5. 还是那份 XML：不同端只靠 Variant 调参。

要绕过的坑有两个：

- **`LayoutGroup` 一 rebuild 就瞬移**，它没有动画的概念；而 `ScrollList` 的 Content 是
  `Vertical` / `Horizontal` / `GridLayoutGroup` + `ContentSizeFitter`，`height="hug"`（`IHugContent.ContentSize`
  读 Content 的 preferred size）、静态子卡、行复用全建在这上面 —— 不能为了动画换掉排版。
- **uGUI 把整条 drag 序列交给最深的 `IDragHandler`**，今天那是根上的 `ScrollRect`。拖排要抢这条序列，
  又不能把滚动弄坏。

## 2. 否决的方案

**A. 松手才挤压（拖动中只显示一条插入线）。否决。** 作者选了实时让位（iOS / Android 列表的标准手感）；
两者是同一套代码，只差 placeholder 什么时候挪 —— 实时让位是「每次目标位变化就挪」，松手才挤是「只在
松手挪一次」。后者若有需求，是一个布尔开关的事，不另开设计。

**B. 拖动期间接管排版（像 `CarouselView` 自己排 strip 那样，全部行手动定位）。否决。** 要把每种
LayoutGroup 的量算复刻一遍（padding / spacing / childAlignment / 网格换行 / hug 行的变高），还要在
会话结束时交还 —— 交还那一帧的 rebuild 一定跳。且 Content 的 preferred size 会在接管期间变成 0
（子节点全 `ignoreLayout`），`height="hug"` 的列表当场塌掉。

**C. 被拖的行 reparent 到 Canvas 顶层（脱离 mask、可以拖出列表）。否决。** 排优先级不需要拖出列表；
reparent 会改坐标系、丢 `ScopedIds` 路径、让 `Screen.ReSolve` 的 `_dynamicSubtrees` 找不到根，
而且 `LayoutElement.ignoreLayout=true` 已经能让行留在 Content 里却不占排版位 —— `flow="false"`
用的就是它。留在 Content 里还顺带解决了「行跟着内容一起滚」。

**D. 驱动器与 `ScrollRect` 同挂根 GO，拖排时 `ScrollRect.enabled=false`。否决。** 同 GO 上两个
`IBeginDragHandler` 都会被调用，谁先谁后取决于组件顺序；`ScrollRect.OnBeginDrag` 已经跑过再禁用它，
`m_Dragging` 等内部状态半截；重新启用又触发 `OnEnable` 的 `SetDirty`。`CarouselView` 的做法干净得多：
驱动器挂在**行的祖先**（Content）上，`GetEventHandler<IDragHandler>` 先找到它；滚动模式把每个事件
`ExecuteHierarchy` 转发给父级，拖排模式不转发 —— `ScrollRect` 对这次手势一无所知。

**E. 只有 `<Btn>` 行能拖（依赖行自己是 raycast 目标）。否决。** 优先级列表的行多半是 `<Frame>` /
`<HStack>`，按库的规则它们 click-through —— 按下去打到的是 Viewport 或根 bg，事件向上找 handler
根本经不过 Content。Content 挂一个**无几何的 raycast catcher**（`ProceduralPanel` 已有「透明 catcher，
不出几何」的先例），按到行间 / padding 也能命中 Content，之后用几何测试决定按的是哪一行、还是空地。

**F. 维持现状：宿主用 `reuseItems="false"` 自己重排。否决。** 那只是让宿主能在推送时整表重建
—— 没有手势、没有动画。行复用 spec 那句「给宿主自己重排过行的兄弟序的场合」在本文之后不再是推荐路径。

**G. `<Reorder>` 部件子元素（像 `<Scrollbar>`）。否决。** `<Scrollbar>` 是**有外观的部件**，它做成子元素
是为了让一个 `class=` 包穿在多个宿主上（scrollbar spec §2）。拖排是列表的**行为**，没有自己的表面，
四个属性足够，且要能 `reorder.portrait="true"` 这样按 Variant 开关 —— 宿主属性是对的形态。

**H. `BindItems` 按 key diff、推送自带重排动画。本里程碑不做。** 行复用 spec §2-B 已经否决过一次；
本文落地的 FLIP 机制正是它的前提（有了「行从 A 位滑到 B 位」的原语，keyed diff 只剩 diff 本身），
但需求方今天要的是手势，不是推送动画。

## 3. 方案总览

一次拖排会话（`ReorderDriver`，挂在 Content 上）：

```
按下（OnInitializePotentialDrag，同时转发给 ScrollRect 让它清 velocity）
  │  几何测试：按在哪一行？有 reorderHandle 时是否按在 handle 上？行是否 active 且 Interactable？
  │  ── 没按在可拖的行上 → 本次手势全程转发 = 纯滚动（今天的行为）
  ▼
Pressed ── hold 计时（reorderHold；鼠标默认 0）
  │  ├─ OnBeginDrag 先到（手指先动了）→ Scrolling：begin / drag / end 全转发给 ScrollRect
  │  ├─ 松开（e.pointerDrag 变 null）→ Idle
  │  └─ 计时到点 / hold=0 且 OnBeginDrag 到 →
  ▼
提起（lift）
  │  placeholder 插到行的 siblingIndex 上（复制它的 sizeDelta + LayoutElement）
  │  行 LayoutElement.ignoreLayout=true，SetAsLastSibling（画在兄弟之上，仍在 mask 内）
  │  e.eligibleForClick=false（长按提起后松手不算点击）
  │  行的 ReorderRowMarker 发 OnLifted → <Animation on="lift"> / <Show on="lift">；没挂钩就用默认外观
  ▼
Dragging ── OnDrag：行的 anchoredPosition = 提起时位置 + (指针 Content 本地位移)；单列/单行锁跨轴
  │  目标位 = 以「上次 rebuild 后各行的排版位置」（不是补间中的视觉位置）算出的插入下标
  │  目标位变了 → placeholder.SetSiblingIndex → ForceRebuildLayoutImmediate → FLIP：
  │      被挤的行钉回 rebuild 前的位置，LitMotion 补间到新位置（reorderDuration）
  │  Update：指针贴近视口首尾 → 自动滚动 Content，并重算目标位
  ▼
松开（OnEndDrag；或 Armed 状态下 e.pointerDrag 变 null）
  │  结构**立即**定型：placeholder 摘走 → 行 SetSiblingIndex(to) → ignoreLayout=false → _slots 置换
  │  → ForceRebuild → 行从手指位置 FLIP 补间到最终位置（"放入"）
  │  发 OnDropped → <Animation reverse-on="drop">；from != to → OnReordered((from, to))
  ▼
Idle（补间在后台跑完；此时再按下可立刻开始下一次会话）
```

三条设计原则：

1. **结构在松手那一刻就定型，所有动画都是覆盖在定型结构上的视觉补间。** 补间只写 `anchoredPosition`，
   不会弄脏布局；布局若被别的东西 rebuild，写回的也是同一个终点 —— 最坏退化成瞬移，永远不会「错位」。
   这也让 `OnReordered` 能在松手当帧发出，宿主同步推送时 `_slots` 已经是新顺序，重绑零变化（§5.5）。
2. **滚动一寸不让。** 驱动器只在「按在可拖的行上、且提起了」之后才吞事件；其余一切转发。滚轮
   （`IScrollHandler`）、滚动条、空地拖动、hold 未到就动手指，全是今天的行为。
3. **列表只管视觉置换，数据顺序归宿主。** 与行复用契约同构：`OnReordered` 后宿主必须对数据做同一个
   `Move(from, to)`；没做（或服务端拒绝了）→ 下一次推送把行拉回数据顺序。模型是真相，拖动是乐观 UI。

## 4. 作者面

### 4.1 `<ScrollList>` 新增属性

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `reorder` | bool | `false` | 开启拖动排序。可按 Variant 开关（`reorder.portrait="true"`）。静态子卡与 `BindItems` 建的行都能拖 |
| `reorderHold` | `auto` \| 时长（`0.4s` / `400ms` / `0.4`） | `auto` | 提起前要按住多久。`auto` = 鼠标 `0`（一按即提）、触屏 `0.4s`；写了 `reorderHandle` 时 `auto` 在两端都是 `0`（从把手开始的拖动不会与滚动混淆）。显式数字对两端一律生效 |
| `reorderHandle` | 行内节点 id | — | 只有从这个节点上按下才能提起，行的其余区域照常滚动。按 `ScopedIds` 在每一行的模板实例里找，找不到再递归子树（同 `TabBar` 找 `<Tab>`）。命中是**几何**判定（`RectangleContainsScreenPoint`），把手可以是 `<Icon>` / `<Image>` / `<Frame>` / `<Text>`，不要求 `raycastTarget` |
| `reorderDuration` | 时长 | `0.15s` | 让位（被挤的行）与落位（被拖的行）补间的时长，`OutCubic`。`0` = 瞬移 |

时长解析复用 `AnimationSpec.ParseSeconds`（`<Animation duration=>` 的同一套写法）。格式错误的值告警并保留旧值
（同 `cellSize`）：一个笔误不该让 Screen 打不开。

### 4.2 行内挂钩：`lift` / `drop`

`<Trigger>` / `<Animation>` / `<Show>` 的 `on=` 新增两个值，**向上解析到最近的 ScrollList 行**
（`GetComponentInParent<ReorderRowMarker>(includeInactive: true)`，同 `expand` / `collapse` 的 `ExpandableMarker`）：

| 值 | 触发时机 |
|---|---|
| `lift` | 这一行被提起（hold 到点 / 一按即提的 OnBeginDrag）。**边沿**事件 |
| `drop` | 这一行被松开 —— 补间**开始**的那一刻，不是结束。这样 `reverse-on="drop"` 的缩回与落位补间同时跑 |
| `lift@<id>` · `drop@<id>` | 同上，源是 id 指向的那一行（词法作用域）。行模板里指自己的根用得上，其余场合少见 |

```xml
<Template name="TaskRow">
  <Animation on="lift" reverse-on="drop" scale="1:1.03" duration="0.12s">
    <Frame id="row" width="stretch" height="48" radius="8" color="@panel">
      <Icon id="grip" name="ui:grip" anchor="left" margin="0,0,0,12"/>
      <Text id="title" anchor="stretch" margin="0,12,0,40">{{title}}</Text>
      <Show on="lift">
        <Frame anchor="stretch" radius="8" glow="12" glowColor="@accent"/>
      </Show>
    </Frame>
  </Animation>
</Template>
```

- `<Show on="lift">`：子树在提起到松开之间可见（`drop` 时隐藏）。注册式（`marker.RegisterLiftShow`），
  同 `checked` 的 `RegisterCheckedShow`；`<Show on="drop">` 是运行期错误（"drop 是时刻不是状态"），
  提示改用 `<Show on="lift">`。
- 挂钩**在任何 ScrollList 行里都合法**，列表没开 `reorder` 时只是永远不触发 —— 一份行模板可以同时给
  可排序与不可排序的列表用；`reorder.portrait` 一类 Variant 开关也因此不需要重新绑定。
- 在任何 ScrollList 行之外写裸 `lift` / `drop` 是运行期错误（CLI `PUI-LIFT-NO-SOURCE`，§4.5）。

### 4.3 默认提起外观

行里**没有任何** `on="lift"` 挂钩时（marker 在绑定期记 `HasAuthoredLiftHook`），驱动器给一个默认外观：
置顶（`SetAsLastSibling`，这条不论有没有挂钩都做，它是正确性不是外观）+ `LayoutHost.localScale` 1 → 1.03
（0.12s，`drop` 时缩回）。写了挂钩，外观全归作者 —— 两者不叠加，避免 `<Animation scale=>` 与默认缩放
同时写 `localScale` 打架。缩放绕行自身 pivot（排版子节点默认 0.5, 0.5）。

### 4.4 Variant / ReSolve

- 四个属性都是普通 `[UIAttr]`，ReSolve 重放幂等；`reorder` 从 true 翻到 false 只是不再开新会话。
- **任何触达 `<ScrollList>` 的 apply pass 都先取消进行中的会话**（§5.7）：`ApplyCommon` 会把行的
  `LayoutElement.ignoreLayout` 清回 false（`flow=` 的实现），提起中的行会被排版组抓回去 —— 与其在
  ReSolve 里保护会话，不如承认「结构变动期间没有手势」。转屏、切主题、切 Variant 期间正好按着一行的
  概率可以忽略；取消是还原到起点、不发 `OnReordered`，宿主看不到半次移动。
- 静态子卡（XML 里的占位行）与 `BindItems` 行同样可拖：`_slots` 里两者一视同仁；ReSolve 重放属性不改兄弟序，
  静态卡拖完不会被打回原位。

### 4.5 lint

| 代码 | 条件 | 哪一遍 |
|---|---|---|
| `PUI-REORDER-HANDLE-ID` | `reorderHandle="x"`，但 `itemTemplate` 的模板体里没有 `id="x"`；没有 `itemTemplate` 时检查每个静态子卡子树 | expanded（要看模板体） |
| `PUI-REORDER-VALUE` | `reorderHold` 不是 `auto` 也不是时长；`reorderDuration` 不是时长 | raw + expanded |
| `PUI-LIFT-NO-SOURCE` | 裸 `on="lift"` / `on="drop"` 的 `<Trigger>` / `<Animation>` / `<Show>` 没有 `<ScrollList>` 祖先；模板体免检、`@id` 形式免检（同 `PUI-EXPAND-NO-SOURCE`） | raw（CLI-only；运行期由 `FindReorderRow` 抛） |

前两条进 `Core/Lint/ScrollListRules.cs`，`IRWalker`（CLI error）与 `ScreenInstantiator`（运行期 `UILog.Warn`）
共用；第三条进 `StateTriggerRules.cs`。运行期 `reorderHandle` 找不到 → `UILog.Warn` 一次并**退回整行可拖**
（功能仍在，lint 负责让作者看见）。

### 4.6 C# 面

```csharp
public sealed class ScrollList
{
    [UIAttr] public bool Reorder { get; set; }
    [UIAttr] public string ReorderHold { set; }        // "auto" | 时长
    [UIAttr] public string ReorderHandle { set; }
    [UIAttr] public string ReorderDuration { set; }

    /// 松手且 from != to 时发出；下标是 _slots 下标（== 数据下标，见契约）。
    public Observable<(int From, int To)> OnReordered { get; }

    /// 从提起到松手之间为 true（补间期不算）。
    public bool IsReordering { get; }
}
```

```csharp
var list = screen.Get<ScrollList>("tasks");
var order = new ReactiveProperty<IReadOnlyList<Task>>(tasks);
list.BindItems(order, (IControl row, Task t) => { row.Get<Text>("title").TextValue = t.Title; });
list.OnReordered.Subscribe(e =>
{
    var next = order.Value.ToList();
    var moved = next[e.From]; next.RemoveAt(e.From); next.Insert(e.To, moved);
    order.Value = next;            // 同步推送：行已在新顺序上，重绑零变化
    api.SavePriority(next);        // 失败时把旧顺序推回去，行自己会拉回来
}).AddTo(screen);
```

**契约**：`OnReordered` 发出时 `_slots` 与兄弟序已按 `(From, To)` 置换完毕。宿主必须对数据做同一个 move
再推送（或不推 —— 不推也一致）。推一个**没**同步的列表 = 让行回到数据顺序，这是刻意保留的还原路径。
`To` 是**移除 `From` 之后**的插入下标（`List.RemoveAt(from); List.Insert(to, x)` 直接可用）。

## 5. 语义细节

### 5.1 手势判定与事件路由

- 驱动器 `ReorderDriver : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler,
  IEndDragHandler` 挂在 **Content** 上，随第一次 `reorder="true"` 懒加、之后常驻（`Behaviour` 禁用时
  `ExecuteEvents` 不投递，所以不靠 `enabled` 省 Update —— Update 空闲时首行返回）。
- Content 同时挂 `HitCatcher : Graphic`（`[RequireComponent(CanvasRenderer)]`，`OnPopulateMesh` 清空，
  `raycastTarget=true`），只在 `reorder` 为 true 时启用。它让「按在行间 / padding」也命中 Content；
  行内 `<Btn>` 等自己的 hit 层画在它之上，照常优先命中。Viewport 的 `RectMask2D` / `Mask` 实现
  `ICanvasRaycastFilter`，滚出视口的部分不可命中 —— 这一点不用另做。
- `OnInitializePotentialDrag` **总是**转发（`ScrollRect` 在这里清 velocity，正在惯性滑动的列表按下即停，
  与今天一致）。几何测试挑出被按的行：遍历 `_slots`，取 `LayoutHost` 的 rect 包含 `e.pressPosition`、
  `activeInHierarchy`、`Control.Interactable` 的那一个；有 `reorderHandle` 时再测把手 rect。
  没有 → 本次手势标记为 pass-through，后续 begin / drag / end 全转发。
- 触屏判定：`#if ENABLE_INPUT_SYSTEM` 下 `e is ExtendedPointerEventData x && x.pointerType == UIPointerType.Touch`；
  否则 `e.pointerId >= 0`（uGUI 鼠标是负 id）。`auto` 只在这里用。
- hold 计时用 `Time.unscaledDeltaTime`（暂停菜单里的列表也得能拖）。Pressed 期间 `OnBeginDrag` 先到 →
  Scrolling（转发全程）；计时到点 → 提起进 Armed；Armed 期间 `OnBeginDrag` 到 → Dragging（不转发）。
- **Armed 而未动手指就松开**：uGUI 不给「按下后没拖就松开」的回调，驱动器在 Update 里轮询按下时拿到的那个
  `PointerEventData`：`e.pointerDrag == null` 即已松开（两种输入模块在释放时都会清它；触屏的事件对象
  随后被模块移除，但我们手里的引用已读到 null）。→ 原位落下：`to == from`，发 `drop`，不发 `OnReordered`。
- 提起时置 `e.eligibleForClick = false`：长按提起再松手，行里的 `<Btn>` 不点击。一按即提（hold=0）
  的路径不需要 —— uGUI 进入 drag 时自己清点击资格并给 `pointerPress` 补 `pointerUp`。
- 只响应 `e.button == Left`（同 `ScrollRect`）。行内有自己 `IDragHandler` 的控件（`<Slider>` / `<Carousel>` /
  嵌套 `<ScrollList>`）按今天的规则截走手势 —— 从它们上面开始的拖动不是拖排，也不滚外层，与现状一致。

### 5.2 placeholder 与出流

- placeholder 是一个每会话新建的空 `RectTransform`（无 Graphic），命名 `ReorderPlaceholder`，插在行的
  siblingIndex 上；复制行 `LayoutHost` 的 `sizeDelta` / `anchorMin` / `anchorMax` / `pivot` 与
  `LayoutElement` 的 min / preferred / flexible 六个值 —— 这样不论排版组是 `childControl*` 读 LayoutElement
  还是读 `sizeDelta`，它都与原行同尺寸；网格模式下 `cellSize` 说了算，复制无害。
- 会话结束：先 `SetParent(null, false)` 把它移出 Content（Play 下 `Destroy` 延后到帧末，不先摘走它会在
  同帧的 `ForceRebuildLayoutImmediate` 里继续占位），再 `Destroy` / `DestroyImmediate`（按
  `Application.isPlaying`，同 `Screen.Close`）。**不用池、不留 inactive 子节点**：`Rebuild` 的
  「第 i 行 ↔ siblingIndex i」假设不能被一个停用的常驻子节点打破。
- 行出流：`LayoutElement.ignoreLayout = true`，`SetAsLastSibling`。行不一定带 `LayoutElement`
  （`ApplyCommon.ApplyLayoutElement` 只在写了 size / native 兜底 / 跨轴 fill 时建，纯 hug 或全靠自身
  `ILayoutElement` 的行没有）→ 没有就 `AddComponent`，六个值留 -1：`LayoutUtility.GetLayoutProperty`
  跳过负值、落到行自己的 `ILayoutElement`，尺寸语义不变；ReSolve 的「残留 LE 清回 -1」路径也认它。
  Content 的 preferred size 不变（placeholder 同尺寸）→ `height="hug"` 的列表不抖、`ContentSizeFitter` 不重算。

### 5.3 目标位

- 参照系是**上次 rebuild 后各行的排版位置**（驱动器在每次 `ForceRebuildLayoutImmediate` 后快照
  `anchoredPosition`，`_layoutPos[row]`），不是当帧的视觉位置 —— 视觉位置正在被 FLIP 补间写着，用它判会抖。
- 单列 / 单行：目标可见下标 `k` = 排版中心在被拖行中心**之前**（竖向 = 上方，横向 = 左方）的可见行数。
  跨过某行中心的那一刻它让位、退到另一侧一整个行距，天然滞回，不需要额外阈值。
- 网格：以被拖行中心落在哪个格子算 `(r, c)` → `idx = r * columns + c`，夹到 `[0, 可见行数 - 1]`；
  两轴自由移动。格线上的抖动只在手指自己来回跨线时发生（iOS 主屏同款行为）。
- **隐藏行**（`hidden="true"` / `Hidden = true`，`LayoutHost` inactive）不参与命中，也不被计数；插入点是
  「第 `k + 1` 个可见行之前」（没有则 Content 末尾），隐藏行随其前面的可见行一起留在原地。
- 被拖行中心 = `anchoredPosition` + 尺寸的一半（按 pivot 折算），与其它行同一坐标系（排版组把所有子节点
  的锚点钉在 Content 左上角，`anchoredPosition` 跨行可比）。
- 单列 / 单行**锁跨轴**：竖向列表的行只随手指上下走，x 保持排版值；网格不锁。

### 5.4 FLIP 让位

目标位变化时：

```
old[row] = row.anchoredPosition                      // 所有在流可见行（补间中的取当前视觉值）
placeholder.SetSiblingIndex(newSlot)
LayoutRebuilder.ForceRebuildLayoutImmediate(content)
for row in 在流可见行:
    new = row.anchoredPosition; _layoutPos[row] = new
    if new == old[row]: continue
    cancel row 上正在跑的补间
    row.anchoredPosition = old[row]
    LMotion.Create(old[row], new, reorderDuration).WithEase(OutCubic)
          .WithScheduler(UpdateIgnoreTimeScale)
          .Bind(row, (v, rt) => { if (rt != null) rt.anchoredPosition = v; })
```

- 补间只写 `anchoredPosition`，`Graphic.OnRectTransformDimensionsChange` 不触发（位置不是尺寸），
  排版组不会因此 dirty；反过来排版组只在 rebuild 时写位置，两者不逐帧打架。
- 快速来回拖时同一行会被反复 FLIP：取消旧补间、从**当前视觉位置**起跳，不回到排版位再跳。
- `-k` 推送销毁了正在补间的尾行 → `Bind` 回调里 `rt == null` 直接跳过；会话已结束，无需更多处理。
- `reorderDuration="0"` 或 `!Application.isPlaying`：全部瞬移，`_layoutPos` 照常更新。EditMode 测试靠这条
  与 `CarouselDragTests` 一样当帧断言。

### 5.5 松手：先定型，再补间

```
1. placeholder 摘走（§5.2）
2. row.LayoutHost.SetSiblingIndex(to)             // to = 插入点折算成的纯行下标
3. row.LayoutElement.ignoreLayout = false
4. _slots.RemoveAt(from); _slots.Insert(to, row)  // ScrollList.PermuteSlots(from, to)
5. ForceRebuildLayoutImmediate(content)
6. row 从手指位置 FLIP 补间到 anchoredPosition（"放入"）；默认外观 / reverse-on 缩回
7. marker.OnDropped；from != to → OnReordered((from, to))
```

- 顺序保证：第 7 步时 `_slots` 与兄弟序已一致，宿主在订阅里同步推送 → `Rebuild` 按位置重绑 → 每行拿到
  的正是它现在显示的那一项，`bind` 回调写回同样的值，视觉零变化；被拖行的落位补间继续跑完。
- 松手点在视口之外（手指拖出去了）：目标位保留最后一次在视口内算出的值，照常定型。
- `to` 的定义见 §4.6（移除后的插入下标）。

### 5.6 边缘自动滚动

Dragging 期间每帧：指针在视口主轴的首 / 尾 `edge` 范围内（`edge = clamp(视口主轴长 × 15%, 24, 80)`）
→ `content.anchoredPosition` 沿主轴推进 `800 × 渗透比例 × unscaledDeltaTime`，钳在 `ScrollRect` 的
内容边界内（不进 elastic 区，免得 `LateUpdate` 往回弹）。Content 动了、手指没动 → 被拖行的本地位置随
之变化（跟手计算读的是当帧的 Content 本地坐标，天然处理），目标位重算。`ScrollRect.LateUpdate` 检测到
`anchoredPosition` 变化会发 `onValueChanged`，滚动条跟着走 —— 不用另通知。

### 5.7 取消

以下任一发生在 Pressed / Armed / Dragging 期间 → **立即还原到 `from`**（placeholder 摘走、行入流、
`SetSiblingIndex(from)`、外观复位、无补间），发 `drop`，**不发** `OnReordered`：

- `<ScrollList>` 的任何 apply pass（`OnBeforeApply` 钩子；含 ReSolve、转屏、主题）—— §4.4 的理由
- `Rebuild`（`BindItems` 推送到达）、`ClearSlots`、`itemTemplate` 变更、`Dispose`
- 被拖的行被外部销毁（`GameObject == null`）
- `reorder` 被置回 false

被取消后手指仍按着：本次手势剩余的 drag / end 事件被吞掉（不转发 —— 半途转给 `ScrollRect` 一个没有
begin 的 drag 序列会让它从当前位置猛跳）。松手后一切如常。

补间期（Settling）不属于会话，不受取消影响 —— 结构早已定型。

### 5.8 与 `<Btn>` 行的交互

- 一按即提（鼠标）：uGUI 进入 drag 时给 `pointerPress` 补 `pointerUp` 并清 `eligibleForClick` → 松手不点击，
  Pressed 视觉随 `pointerUp` 退出。
- 长按提起（触屏）：提起时驱动器清 `eligibleForClick`；`<Btn>` 的 Pressed 视觉维持到手指离开
  （它没有收到 `pointerUp`），这与「按着」的事实一致，不干预。
- hold 期间手指先动 → 滚动 → uGUI 同样清点击资格，今天的行为。

### 5.9 坐标系

跟手计算全部在 **Content 本地坐标**里做：`RectTransformUtility.ScreenPointToLocalPointInRectangle(content,
e.position, e.pressEventCamera, out local)`，行的 `anchoredPosition = liftPos + (local - liftLocal)`。
同 `CarouselView.OnDrag` 的理由：隐式除掉 `CanvasScaler.scaleFactor` / 相机 / 旋转，被抓住的像素 1:1
跟手；Overlay 画布 `pressEventCamera` 为 null 也正确。

## 6. 实现地图

| 文件 | 改动 |
|---|---|
| `Runtime/Controls/Internal/ReorderDriver.cs`（新，≈450 行） | 状态机（Idle / Pressed / Armed / Dragging / Scrolling）、事件路由与转发、hold 计时、placeholder、跟手、目标位、FLIP、松手定型、边缘自动滚动、取消。持 `ScrollList` 引用读 `_slots` / 参数，回调 `PermuteSlots` |
| `Runtime/Controls/Internal/ReorderRowMarker.cs`（新） | 每行根 GO 上的组件：`Owner` / `Row` / `IsLifted` / `OnLifted` / `OnDropped`（`Subject<Unit>`）/ `RegisterLiftShow` / `HasAuthoredLiftHook`。`OnDestroy` 释放 Subject |
| `Runtime/Controls/Internal/HitCatcher.cs`（新，≈20 行） | 无几何 `Graphic`，`[RequireComponent(typeof(CanvasRenderer))]` |
| `Runtime/Controls/ScrollList.cs` | 四个 `[UIAttr]`、`OnReordered`、`IsReordering`；行进 `_slots` 时装 marker（`Rebuild` 新建行 / `OnAfterApply` 静态收集）；`OnBeforeApply` / `Rebuild` / `ClearSlots` / `Dispose` 先 `_reorder?.Cancel()`；`internal void PermuteSlots(int from, int to)`；`Reorder` setter 懒建驱动器 + catcher |
| `Runtime/Controls/Internal/TriggerSpec.cs` | `TriggerKind.Lift` / `Drop` + `lift@` / `drop@` 前缀；错误信息里的可用值清单补上 |
| `Runtime/Controls/Internal/TriggerSourceResolver.cs` | `FindReorderRow(trigger, sourceId)`：裸 → `GetComponentInParent<ReorderRowMarker>(true)`；`@id` → `ResolveId` + 取该控件根上的 marker |
| `Runtime/Controls/Trigger.cs` / `Show.cs` | Trigger 绑 marker 的两个流并置 `HasAuthoredLiftHook`；Show 接 `lift`（注册式），`drop` 报错 |
| `Runtime/Core/Lint/ScrollListRules.cs` | `PUI-REORDER-HANDLE-ID` / `PUI-REORDER-VALUE`；`IRWalker` 传模板体给 handle 检查；`ScreenInstantiator` 镜像 |
| `Runtime/Core/Lint/StateTriggerRules.cs` | `PUI-LIFT-NO-SOURCE`（裸值集合 + 祖先 tag `ScrollList`） |
| `Editor/XsdGenerator.cs` | 无改动（`[UIAttr]` 反射自动带上四个属性） |

`Core/Lint` 保持纯 C#：规则只看 `ElementNode` / `TemplateDef`，不碰任何 Unity 类型。

## 7. 测试（Red 先行）

EditMode（`Tests/EditMode/Controls/ScrollListReorderTests.cs`，事件用 `PointerEventData` 直接打到驱动器，
同 `CarouselDragTests`；`!isPlaying` → 全瞬移，当帧断言）：

1. `reorder=false` 时 Content 上没有驱动器 / catcher；`reorder=true` 有，且 catcher `raycastTarget=true`、不出几何。
2. 按在空地（padding）→ begin / drag / end 全部转发到根 `ScrollRect`（用一个计数 `IDragHandler` 替身挂在根上验证）。
3. `reorderHold=0`：按行 → begin → 行 `ignoreLayout=true`、是最后一个 sibling、placeholder 在原 index 且尺寸相同、Content preferred size 不变。
4. drag 跨过下一行中心 → placeholder 后移一位、被跨的行排版位置前移一个行距（瞬移）；再拖回 → 复原。
5. end → placeholder 消失、行 `ignoreLayout=false`、`SetSiblingIndex(to)`、`_slots` 顺序、`OnReordered` 恰好一次且 `(from, to)` 正确；`from == to` 不发。
6. 松手后同步 `BindItems` 推送同样置换过的列表 → 每行 `TextValue` 不变（重绑零变化）；推送**未**置换的列表 → 行回到数据顺序。
7. hold > 0（`TickForTests(dt)` 推时钟）：先 begin → 转发（滚动），行未出流；先到点 → 提起（`IsLifted`），此时 `e.eligibleForClick == false`；再 begin → 不转发。
8. Armed 未动松手（把 `e.pointerDrag` 置 null 后 `TickForTests`）→ 原位落下，`drop` 发、`OnReordered` 不发。
9. `reorderHandle`：按在把手外 → 转发；按在把手上 → 提起；id 不存在 → `UILog.Warn` 一次并整行可拖。
10. 网格模式：拖到第 2 行第 1 格 → `to` 正确；`direction="horizontal"`：主轴换成 x。
11. 隐藏行不计数、不被跨过；`Interactable=false` 的行按不起。
12. 取消：Dragging 中 `Screen.ReSolve()` / 推送到达 → 还原到 `from`、`drop` 发、`OnReordered` 不发，后续 drag 被吞。
13. 静态子卡可拖，拖完 ReSolve 不复位兄弟序。
14. `lift` / `drop` 触发：行模板里 `<Trigger on="lift">` 的 `OnFire` 计数；`<Show on="lift">` 提起显、松开隐；写了挂钩的行**不**被默认缩放；`<Show on="drop">` 抛；行外裸 `lift` 抛。
15. `<ScrollList>` 之外的列表（`Carousel` 卡）里 `on="lift"` → 抛「no ScrollList row」。

Lint（`Tests/EditMode/Lint/ScrollListReorderRulesTests.cs`）：三个代码各一正一反，`PUI-REORDER-HANDLE-ID`
分「有 itemTemplate」与「只有静态子卡」两支；模板体免检。

PlayMode（`Tests/PlayMode/Controls/ScrollListReorderPlayTests.cs`）：一条 —— 拖排后等 `reorderDuration`，
被挤的行与被拖的行都到达排版位置、Content 上没有残留 placeholder。

## 8. SKILL / 文档更新（同一 PR 内，英文）

| 文件 | 改动 |
|---|---|
| `authoring-promptugui-xml/SKILL.md` | `<ScrollList>` 属性表加四行；`reuseItems` 那行删掉「for a host that reorders rows itself」这半句（改指向本功能）；小节末加一个 3 行的 reorder 示例 + 指向 `reference/reorder.md` 的指针；速查表补 `reorder` |
| `authoring-promptugui-xml/reference/reorder.md`（新） | 手势规则（`auto` 两端差异、handle、hold 与滚动的分界）、`lift` / `drop` 挂钩与默认外观、网格 / 横向 / 隐藏行、取消规则、lint 三条、完整 XML + C# 示例 |
| `authoring-promptugui-xml/reference/animations.md` | `on=` 表加 `lift` / `drop` / `@id` 三行；「resolve upward」那段把 ScrollList 行加进去；`<Show>` 段说明只接 `lift` |
| `scripting-promptugui-csharp/SKILL.md` | **List / option push** 段加「Reorder」小节：`OnReordered` 契约、`To` 的定义、同步推送零变化、不同步即还原；速查表加 `ScrollList.OnReordered` |
| `CLAUDE.md` | 触发路由表加 `reorder` → `reference/reorder.md` |

## 9. 演示同步（与代码同一 PR）

| 文件 | 改动 |
|---|---|
| `Samples~/CommonControls/Resources/UI/CommonControls.ui.xml` | `id="list"` 的 `<ScrollList>` 加 `reorder="true"`；`OptionRow` 模板加 `<Icon id="grip">` 把手与 `<Animation on="lift" reverse-on="drop" scale="1:1.03" duration="0.12s">`；用 `reorderHandle="grip"` |
| `Samples~/CommonControls/CommonControlsRunner.cs` | 列表数据改为一个 `ReactiveProperty<IReadOnlyList<string>>`（键值列表），`LocaleTicks` 只在 bind 回调里 `UI.Tr(key)` —— 否则切语言的重发会把顺序打回去；订阅 `OnReordered` 做 move 并 Toast 一行 `"moved {from} → {to}"` |

改完跑 `dotnet run --project .lint/UIXmlLint -- Samples~/CommonControls/Resources/`，在宿主工程里鼠标 / 触屏模拟各拖一遍。

## 10. 非目标

- `list.Move(from, to, animated)` 编程接口（服务端改优先级的动画）—— FLIP 机制就位后约 20 行，另开需求。
- `BindItems` 按 key diff、推送自带增删移动画（§2-H）。
- 拖出列表 / 跨列表拖放、拖到别的控件上（drop target）。
- 松手才挤压的模式开关（§2-A）。
- 手柄式多选拖动、拖动时的 haptic 反馈。
- `<TabBar>` / `<Carousel>` 的拖排。
- 虚拟化列表（行数很多时先该做的是它，不是拖排）。
- placeholder 的可视化（虚线框等）—— 今天是空位；要做走 `<Template>` 引用，另开。

## 11. 已定的决策（2026-09-16 与作者对齐）

| # | 决策 | 理由 |
|---|---|---|
| REO-D1 | **实时让位**，不是松手才挤 | 作者选定；标准手感 |
| REO-D2 | 驱动器挂 **Content**，滚动模式**转发**给父级，拖排模式不转发 | 不碰 `ScrollRect` 的启用状态与内部 `m_Dragging`；`CarouselView` 先例 |
| REO-D3 | Content 挂**无几何 raycast catcher**，只在 `reorder` 开时启用 | 非 `<Btn>` 行也能拖；遵守「hit-testing 是声明的」 |
| REO-D4 | 被拖行 **`ignoreLayout=true` 留在 Content**，不 reparent | 坐标系 / `ScopedIds` / mask / 跟着滚 全部保持 |
| REO-D5 | **结构在松手当帧定型**，动画是覆盖其上的 `anchoredPosition` 补间 | `OnReordered` 同步可推送；布局被外部 rebuild 最坏瞬移不错位 |
| REO-D6 | 目标位以**排版快照**判定，不看视觉位置 | 补间中判视觉会抖 |
| REO-D7 | `reorderHold="auto"`：鼠标 0 / 触屏 0.4s；有 handle 两端都 0 | 鼠标滚动不靠拖；触屏拖即滚动；Android 长按阈值 400ms |
| REO-D8 | `reorderHandle` 命中是**几何**的，不要求 `raycastTarget` | 把手多半是 `<Icon>`（硬编码 click-through） |
| REO-D9 | `lift` / `drop` 走 marker **向上解析**，在任何 ScrollList 行里合法、没开 `reorder` 就不触发 | 行模板可跨列表复用；Variant 开关不用重绑 |
| REO-D10 | 默认提起外观 = 置顶 + 1.03 缩放，**行里有 `on="lift"` 挂钩即整体让位给作者** | 不与 `<Animation scale=>` 叠加 |
| REO-D11 | `drop` 在松手那一刻发（补间开始），`OnReordered` 同刻发、`from == to` 不发 | `reverse-on="drop"` 与落位同步；宿主拿到的是已定型的顺序 |
| REO-D12 | **任何 apply pass / 推送 / 清表都取消会话**，还原到起点、不发事件 | `ApplyCommon` 会清 `ignoreLayout`；结构变动期间不该有手势 |
| REO-D13 | placeholder 每会话新建、结束即摘走销毁，**不池化** | 不破坏 `Rebuild` 的 sibling == index 假设 |
| REO-D14 | 补间与 hold 用 **unscaled time** | 暂停菜单里的列表也要能拖；Carousel / Animation 维持现状不改 |
| REO-D15 | 单列 / 单行**锁跨轴**，网格两轴自由 | 行在列里不该左右飘 |
| REO-D16 | `To` = **移除后**的插入下标 | 与 `List.RemoveAt + Insert` 直接对应，宿主不做换算 |

## 12. 开放问题（留给 plan / 实现期）

1. `HitCatcher` 与 `Graphic.OnRectTransformDimensionsChange → SetLayoutDirty` 的关系：Content 每次变高都会多标一次
   `MarkLayoutForRebuild`，与 `ContentSizeFitter` 的标记重叠、幂等；实现时在 Deep Profile 里确认一次没有多余的 rebuild。
2. `IsReordering` 期间宿主主动 `Get<Text>().TextValue = …` 写行 —— 不受影响（只改内容），但要不要在 SKILL 里提一句「拖动中推送会取消手势」——提。
3. 触屏 hold 期间的「hold 进度」反馈（iOS 没有、Android 也没有）—— 不做，`lift` 事件是唯一反馈点。
4. `reorderHandle` 找到多个同 id（模板里写重了）→ 取第一个并 warn，还是报错？倾向 warn（同 `PUI-SCROLLBAR-DUPLICATE` 的态度）。

## 13. 里程碑拆分

一个 PR，两步提交：

- **M0 手势 + 结构 + 事件**：`ReorderDriver` / `HitCatcher` / `ScrollList` 接线 / `OnReordered` / 取消 / 自动滚动 / EditMode 测试 1–13 / lint 前两条。
- **M1 挂钩 + 外观 + 文档**：`ReorderRowMarker` / `lift` `drop` / `<Show on="lift">` / 默认外观 / 测试 14–15 / `PUI-LIFT-NO-SOURCE` / PlayMode 测试 / 三份 SKILL + `reference/reorder.md` / 演示同步。

## 14. 实施记录（2026-09-16）

分 6 步提交在 `feat/scrolllist-drag-reorder`：spec → M0（手势 / 结构 / 事件 + 27 条 EditMode）→ lint 两条 →
M1（挂钩 / `<Show on="lift">` / 默认外观 + `PUI-LIFT-NO-SOURCE`，13 + 3 条）→ PlayMode 1 条 → 三份 SKILL +
`reference/reorder.md` + 演示同步。EditMode 3993 / EditorOnly 346 / PlayMode 214 全绿；`dotnet format` 与
UIXmlLint（`Runtime/Resources/`、`Samples~/`）干净。

### 14.1 与设计的偏差

1. **行 marker 由第一个挂钩按需创建，不是列表装的。** §6 写的是「行进 `_slots` 时装 marker」，落地时发现顺序不对：
   挂钩在行**实例化过程中**（Trigger 的 `OnAfterApply`，DFS 后序）就要解析，那时列表还没拿到这一行。改为
   `ScrollListContentMarker` 在 `ScrollList.OnAttached` 打在 Content 上（永远先于任何行），`FindReorderRow` 从触发器
   向上走、遇到「父节点是 Content」即认出行，`ReorderRowMarker` 就地 `AddComponent`。没有挂钩的行就没有 marker ——
   `NotifyLifted` 拿不到 marker 或 `HasLiftHook == false` 即启用默认外观。`HasAuthoredLiftHook` 改名 `HasLiftHook`，
   只由 `on="lift"`（Trigger / Animation / Show）置位；只挂 `drop` 的行保留默认外观（§4.3 原意如此，测试钉住）。
2. **`TriggerSourceResolver.ResolveId` 多认一层：路上任何祖先自己的 id。** 动态行（itemTemplate / `Instantiate`）根的
   scope 要等整棵子树 apply 完才接上，`lift@row` 写在行根上时按表查不到自己。祖先的 id 对其后代本来就在作用域里，
   这条对所有 `@id` 形式通用生效（回归全绿）。
3. **`PUI-REORDER-VALUE` 不做运行期镜像。** setter 拒绝坏值时已经 `UILog.Warn`（带 src:line），再镜像一次是同一条
   告警出两遍。`PUI-REORDER-HANDLE-ID` 同 spec：CLI 查模板体，运行期由驱动器在首次按下找不到把手时 warn 一次。
4. **三个属性的 C# 属性名带 `Attr` 后缀**（`ReorderHoldAttr` 等，`[UIAttr("reorderHold")]` 显式命名，同 `Animation`
   的做法），因为驱动器要读的解析后值占了 `ReorderHold` / `ReorderHandle` / `ReorderDuration` 这三个 internal 名。
5. **驱动器的 `Update` 常驻。** `Behaviour` 禁用时 `ExecuteEvents` 不投递（`ShouldSendToComponent` 查
   `isActiveAndEnabled`），所以不能靠 `enabled` 省帧；`Tick` 在 Idle 首行返回。
6. **演示里的把手是 `<Frame>` 不是 `<Icon>`**：CommonControls 的 SpriteSet 没有 grip 图，且农场侧刻意不写形状属性。

### 14.2 测试里发现并记录的既有事实

`ScrollList` 的 `Vertical` / `HorizontalLayoutGroup` 保持 uGUI 默认 `childControl* = false`，行用自己的
`sizeDelta`（新 RectTransform 默认 100×100）排版，`<Frame height='30'>` 的 30 只进 `LayoutElement`、不进
`sizeDelta`。`HugSizingTests` / `ScrollListStaticChildrenTests` 早已记着这条；本文测试因此**量测**行距
（`StrideY` / `StrideX`）而不假设 30。这不是本文的范围，但值得另立需求看一眼：列表行的声明高度在
单列模式下今天并不生效。

### 14.3 开放问题的落地

- §12-1：`HitCatcher` 的 `OnRectTransformDimensionsChange → SetLayoutDirty` 只对 Content 自身多标一次
  `MarkLayoutForRebuild`，幂等；未 Deep Profile。
- §12-4：`reorderHandle` 同 id 写重 → `ScopedIds` 只存一份、递归遍历取第一个，静默；CLI 没有为此加规则。

### 14.4 上线后修正（2026-09-16，宿主工程接入时发现）

**`Released()` 不能读 `pointerDrag`（Input System 模块下每次会话都在第一帧被判成松手）。** §5.1 假设「两种输入模块在
释放时都会清 `pointerDrag`」——对，但 `InputSystemUIInputModule` 还有一条没料到的：它**三个键共用一个 `PointerEventData`**
（`ProcessPointer` 里 `eventData.button = Left; leftButton.CopyPressStateTo(eventData)` … 处理完左键再把右键、中键的
`ButtonState` 拷进同一个对象），所以指针有变化的帧末，`pointerDrag` 恒是中键的 `m_DragObject`——null。驱动器的 `Update`
跑在其后，`Pressed` 一进 `Tick` 就 `Released()` → Idle，下一帧 `OnBeginDrag` 走 `default` 分支转给 ScrollRect：
**鼠标按住拖动只会滚动，永远提不起来**（宿主工程 ssw_re_client 的岗位页首次接入即复现）。EditMode 测试没抓到是因为
它们手拼的是裸 `PointerEventData`（每键一个对象的旧模块语义）。

改法：`ExtendedPointerEventData`（Input System）→ 问**设备**：`Touchscreen` 按 `touchId` 找那根手指的 `isInProgress`，
其余 `Pointer.press.isPressed`（鼠标左键 / 笔尖）；裸 `PointerEventData`（旧模块、测试）仍看 `pointerDrag`。测试用
`ReorderDriver.PressedProbeForTests` 顶替设备查询，钉住「`pointerDrag` 为 null 但设备仍按着 = hold 照常走 / hold 0
的第一帧拖动照常提起」两条。

### 14.5 跨轴不裁（2026-09-16，宿主工程首次接入后）

**现象**：提起的行放大 1.03 + 自带边框 / 发光，越出视口左右两侧的部分被视口 mask 切掉（截图：第二行的边框两侧被截）。
视口 mask 是滚动裁剪本身（`Viewport` 上的 stencil `Mask` 或 `RectMask2D`），不能关。

**决定（REO-D17）：列表只沿滚动轴裁剪。** `ScrollList` 是单轴滚动（`direction` 二选一，网格也只竖向滚），跨轴永远明确：
竖向列表 / 网格左右不裁，横向列表上下不裁。这不只解提起那一刻——静止时行的 glow / 阴影被视口边切掉也是同一个问题。
写得过宽的行从"被藏起来"变成"露出来"，作者错误更显眼，接受。

两种 mask 模式两套机制，都在 `ScrollList.ApplyCrossAxisClip`（mask 模式或 `direction` 变了就重放，幂等）：

- **`RectMask2D`**（`sprite=""` / `mask=""` 的直角列表）：`padding` 跨轴两侧 = `-100000`。查过 uGUI 源码
  `Clipping.FindCullAndClipWorldRect` 是 `xMin + padding.x` / `xMax - padding.z`，负值即外扩；剔除用同一个矩形，
  滚出主轴的行照常被剔。
- **stencil sprite mask**（默认 `pugui_9slice_mask` 圆角、`mask="x#slice"`）：视口的 `Image` 换成子类
  `CrossAxisMaskImage`（`ApplyViewportMask` 复用节点上已有的 Image，所以在它之前 `AddComponent` 即可，共享函数不改），
  `OnPopulateMesh` 先画 9-slice 原形，再补一条沿滚动轴、跨轴无限宽（±100000）的直边带，带只覆盖 9-slice 上下（横向列表：
  左右）边框之间的区域，UV 采 `DataUtility.GetInnerUV` 的中心实心像素。stencil 写入 = 圆角矩形 ∪ 直边带：圆角处照旧裁、
  直边段两侧敞开。**没有 border 的自定义 mask（六边形之类）不加带**——它不是 9-slice，无从知道直边在哪，作者选的形状不该被撑破。

**否决的替代**：LiftLayer（提起时把行 reparent 到 ScrollList 根下的一层，浮在边框之上）——效果更"抬起"，但要在会话里两套坐标系、
且只救提起的行不救静止时的 glow；`maskable=false`（提起时关掉行子树的 MaskableGraphic 遮罩参与）——会连行内自己的
`<Image mask="self">` 一起失效，且行仍画在滚动条 / frame 之下。

**顺手修的**：§14.2 的 EditMode 行尺寸问题。`ApplyLayoutMode` 给 V/H 组显式写 `childControl* = true`、
`childForceExpand* = true`（与 Play 模式实测默认值一致），UIPreview 与 EditMode 测试的行尺寸从此与运行时一致；
`HugSizingTests` / `ScrollListStaticChildrenTests` 里绕着走的断言收紧成精确数字。
