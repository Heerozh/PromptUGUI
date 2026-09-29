# `<ScrollList virtualize>` —— 行虚拟化、按 key 绑定、聊天式贴底

> 状态：**实现中**（2026-09-29，分支 `feat/scrolllist-virtualization`；方向已与作者对齐，见 §12；计划见
> `docs~/superpowers/plans/2026-09-29-scrolllist-virtualization.md`，计划阶段的修正 VIR-P1…P11 已写回本文，见 §12.1）。
> 需求来源：宿主工程 ssw_re_client 的聊天面板 —— `Round/UI/Round.ui.xml` 里 `chatWorld` / `chatFaction` /
> `chatAlliance` / `chatSystem` 四个叠放的频道列表（同时只显示一个），行模板 `Templates/ChatLine.ui.xml` 的
> `ChatLine`（`[时间]` + 一段会折行的富文本正文）。目标：**一个频道 1000 条纯文字消息，来一条新消息的成本与条数无关**。
> 相关：
> `2026-05-09-m5-common-controls-design.md` M5-D4（「v1 非虚拟化；`BindItems` API 不变即可后续升级」—— 本文兑现它）、
> `2026-09-14-scrolllist-row-reuse-design.md`（位置复用与 `bind` 契约；它的 §2-B「按 key diff」与 §9「inactive 行池」在本文落地）、
> `2026-09-16-scrolllist-drag-reorder-design.md`（§10 非目标「虚拟化列表」；虚拟模式下不能拖排，§4.3）、
> `2026-08-26-theme-driven-style-design.md` 文末遗留（500 行列表切一次 Variant ≈ 半秒 —— 虚拟化后只重放已实现的行）、
> `2026-06-23-centered-slidebox-reactive-items-design.md`（`Carousel.BindItems(..., key)` —— key 参数调用形状的先例）。

## 1. 问题

宿主在 UI Preview 里对 `chatWorld` 灌假消息实测（编辑器、桌面 CPU；手机与 WebGL 还要慢几倍）：

| 条数 | 来一条新消息 | 只有一行变（≈ 改成按 key 复用后） |
|---|---|---|
| 20 | 6.6 ms | 1.5 ms |
| 50 | 16.8 ms | 3.0 ms |
| 100 | 33 ms | 5.4 ms |
| 200 | 70 ms | 10.5 ms |

两列都随条数线性增长，外推到 1000 条约为 **350 ms / 50 ms**。成本来自两个 O(N) 项：

1. **位置绑定让所有行的内容挪一格。** 频道满了以后每来一条就裁掉最老的一条，而 `Rebuild` 把第 i 项绑到第 i 行，
   于是 N 行的正文全变，N 个 TMP 重新排版、重建网格。**按 key 绑定**能消掉这一项：行跟着消息走，文字不变，
   `TMP_Text.text` 的 setter 对同一字符串直接返回（uGUI 2.x `TMP_Text.cs`）—— 这就是第二列。
2. **即使只有一行变，一次推送仍要碰全部 N 行**：`Rebuild` 对 N 项各调一次 `bind`；Content 的
   `VerticalLayoutGroup` + `ContentSizeFitter` 遍历 N 个子节点；Canvas 给 N 行重新合批。
   这一项只有让「存在的行 = 视口附近的行」才能消掉。

另外两笔也跟着 N 走：打开面板要实例化 N 行（四个频道 × 1000 = 4000 行）；切主题 / Variant / 语言时
`Screen.ReSolve` 要重放 N 行（C# SKILL 记过 500 行 ≈ 500 ms）。

结论：1000 行需要**行虚拟化**。按 key 绑定是它的伴生件 —— 聊天在「裁掉最老的」「往上加载历史」时，
只有按 key 才能让画面不跳（§5.4）。

## 2. 否决的方案

**A. 只做按 key 复用。** 只消掉第 1 项；1000 行仍约 50 ms/条（桌面），手机上几倍。key 本文照做（§4.2），
但它是虚拟化的伴生件，不是替代。

**B. 固定行高（`itemHeight=`）。** 实现最简单（偏移是乘法、滚动条精确），但 `ChatLine` 的正文会折行。
作者选了「量已实现的行 + 估算未实现的行」（VIR-D1）。固定行高留作以后的快路径（§11）。

**C. 推送时把每一项都量一遍（一个隐藏的量高行）。** 高度与滚动条都精确，新消息只量一条。但**宽度一变
（转屏、滚动条出现）或一次 ReSolve 就得把 N 项全部重量**：1000 × 约 0.3 ms 在桌面约 300 ms，手机上以秒计；
首开 1000 条历史同样要 300 ms。估算法在这些情况下只重量可见的行。

**D. 新控件 `<VirtualList>`。** 代码边界更干净，但视口、遮罩、背景表面、`<Scrollbar>` 部件、静态占位、
`itemTemplate` 解析要么复制、要么再抽一层；作者面多出一个几乎一样的标签。选择 `<ScrollList>` 上的一个模式，
不兼容的组合交给 lint（§4.3）。

**E. 绝对定位 + 每行套一个「壳」。** Content 不挂 LayoutGroup，列表自己算每行坐标，每行套一个带 VLG + CSF 的壳来拿
hug 高度。可行，但行的父节点变了（布局子节点的 lint 语义、`LayoutHost`、`ScrollListContentMarker` 的向上找行都要跟着改），
行高变化要逐壳监听。选择**窗口化的 LayoutGroup**（§3）：Content 仍是 `VerticalLayoutGroup`，行仍是它的直接子节点，
只是只放视口附近那一段，前后用两段浮点空白撑住 —— 行的一切语义与今天相同。

**F. 行数超过阈值自动虚拟化。** 虚拟化改变了契约（行只在视口附近存在、行上不在数据里的状态滚出即丢、
`bind` 会在滚动时被调用），必须作者显式选择。

**G. 整段聊天拼进一个 TMP。** 1000 行是数万字符，每来一条整段重排，超长网格还会撞顶点上限。

**H. 不要 key，只按下标虚拟化。** 追加照样是 O(1)，但裁头和前插时下标整体平移，锚点跟到了别的消息上，画面会跳。
key 保持可选（不给就按下标），但聊天应该传（§6）。

## 3. 方案总览

```
ScrollList 根 (PuiScrollRect)
├─ Viewport ─ Content  (WindowedVerticalLayoutGroup + ContentSizeFitter)
│               ·  Leading  = 第 first 项之前所有项的高度 + spacing   ← 一段浮点空白，不是 GameObject
│               ├─ row[first] … row[last]                            ← 只有这些行存在，仍是 VLG 的直接子节点
│               ·  Trailing = 第 last 项之后所有项的高度 + spacing
├─ Scrollbar
└─ Pool (inactive) ─ 停放的空闲行
```

- **窗口。** 按滚动位置算出与视口（上下各留一点余量）相交的项区间 `[first, last]`，只为它们实现行。
  行滚出窗口就回收，项滚进窗口就从回收行 / 池 / 工厂取一行再 `bind`。
- **行高。** 每项一个高度缓存。行被实现并 `bind` 之后，对 Content 做一次 `LayoutRebuilder.ForceRebuildLayoutImmediate`，
  读出各行的真实高度写回缓存；从没实现过的项用已测高度的均值估算。宽度变化和 ReSolve 把缓存降级为估算，只重量可见的行。
- **锚点。** 每次同步（推送、滚动、重量）之前，记下「第一条可见的行 + 它离视口顶的距离」，同步之后把它放回原处；
  视口贴着边时改为继续贴边（默认是起点，`stickToEnd` 时是终点）。估算误差、裁头、前插历史都不会让画面跳。
- **key。** `BindItems(..., key: m => m.Id)`。推送时按 key 算出新旧下标映射，高度缓存和已实现的行都跟着项走。
  两种模式都能用：非虚拟模式下直接得到报告第二列的收益。
- **滚动驱动。** `ScrollList` 的 `ScrollRect` 换成子类 `PuiScrollRect`，在 `base.LateUpdate()`（惯性 / 弹性已经挪完
  Content）之后同步窗口；推送则在推送当下同步执行。
- **成本。** 一次推送 = 已实现行数次 `bind`（没变的行写入同一字符串，TMP 直接返回，基本只剩回调本身）+ 新进窗口的行的
  TMP 排版 + 一次窗口内的布局。**与 N 无关。**

## 4. 作者面

### 4.1 `<ScrollList>` 新增属性

| 属性 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `virtualize` | bool | `false` | 只为视口附近的项实现行。实例化时定型，不随 Variant 切换（`PUI-SCROLL-VIRTUAL-VARIANT`）。v1 只支持纵向单列 |
| `stickToEnd` | bool | `false` | 视口在末端时，推送之后、视口尺寸变化之后仍停在末端（聊天）。两种模式都可用；可随 Variant 切换 |

`virtualize="true"` 的列表：

- 首次 `BindItems` 之前与今天完全一样：静态占位子节点照常排版，UI Preview 里看得见；首次 `BindItems` 照旧销毁占位并接管
  （2026-09-10 的规则不变）。
- 必须写 `itemTemplate`（`PUI-SCROLL-VIRTUAL-TEMPLATE`）。
- `spacing` / `padding` / `mask` / `frame` / 程序化表面 / `<Scrollbar>` 部件照旧。

### 4.2 C# 面

```csharp
// 新重载：带 key。调用处的写法与 Carousel 相同（key: …），但 key 是泛型的，值类型 id 不装箱
public IDisposable BindItems<T, TKey>(
    Observable<IReadOnlyList<T>> source, Action<IControl, T> bind, Func<T, TKey> key);
public IDisposable BindItems<T, TSlot, TKey>(
    Observable<IReadOnlyList<T>> source, Action<TSlot, T> bind, Func<T, TKey> key)
    where TSlot : class, IControl;

// 滚动：两种模式都可用，瞬时（先 StopMovement）
public void ScrollToStart();
public void ScrollToEnd();
public void ScrollToIndex(int index);   // 该项的起始边对齐视口的起始边，夹在可滚范围内；越界抛 ArgumentOutOfRangeException
                                        // （上界：虚拟模式 ItemCount，非虚拟 SlotCount —— 纯静态列表 ItemCount 为 0，VIR-P3）

public bool IsAtEnd { get; }                    // 视口在末端（内容不满一屏也算）
public Observable<bool> OnAtEndChanged { get; } // 订阅时回放当前值，之后只在变化时发（去重）
public int ItemCount { get; }                   // 最近一次推送的项数；首次推送前为 0
```

- 现有的 `BindItems<T>` / `BindItems<T, TSlot>` 不变，等于按下标确定身份。
- `SlotCount`：非虚拟模式 = 行数（同今天）；虚拟模式 = **已实现的行数**。
- 事件接口一律 `Observable<T>`（主 spec §9.4 的约束）。`OnAtEndChanged` 回放当前值，与 `Btn.OnState` 同一做法：
  「下面还有新消息」的提示从第一帧起就对。

### 4.3 lint

规则写在 `Core/Lint/ScrollListRules.cs` 的新方法 `CheckVirtualize`，**只由 `IRWalker`（CLI）调用**，报 error。
`ScreenInstantiator` 不镜像它（`.lint/UIXmlLint/README.md` 的约定：「作者写了但被忽略」只在 CLI 报）；运行时由
`ScrollList` 自己在发现冲突的那一刻 `UILog.Warn` 一次并降级 —— 它看得到 `class=` 合并后的真值，不让 Screen 打不开（VIR-P1）。

| 代码 | 触发条件 | 运行时 |
|---|---|---|
| `PUI-SCROLL-VIRTUAL-LAYOUT` | `virtualize` 同时有 `columns ≥ 1` 或 `direction="horizontal"`（基础值或任一变体） | 不虚拟化，按今天的方式跑 |
| `PUI-SCROLL-VIRTUAL-REORDER` | `virtualize` 同时有 `reorder="true"`（基础值或任一变体） | 拖排不开 |
| `PUI-SCROLL-VIRTUAL-REUSE` | `virtualize` 同时有 `reuseItems="false"` | 忽略 `reuseItems`（虚拟模式一定回收） |
| `PUI-SCROLL-VIRTUAL-HUG` | `virtualize` 且主轴尺寸是无上限的 `hug`：裸 `hug` 或 `clamp(N, hug, _)`（`clamp(_, hug, N)` 合法；判定用 `HugRules.IsOpenEndedHug`，VIR-P2） | 能跑，但所有行都会被实现 |
| `PUI-SCROLL-VIRTUAL-VARIANT` | 写了 `virtualize.<variant>=` | 以实例化时的值为准，之后的变化忽略并警告一次 |
| `PUI-SCROLL-VIRTUAL-TEMPLATE` | `virtualize` 但没有 `itemTemplate` | `BindItems` 照旧抛异常 |

规则都经过 `StyleAttributeView`（`class=` 带进来的也算），读法与 `DeclaresGrid` / `DeclaresHorizontal` 相同。
`CheckScrollList` 今天在非网格时提前返回，要拆成分段检查。`Core/Lint` 保持纯 C#。

## 5. 语义细节

### 5.1 数据模型

- `heights[i]`（float）+ 每项一个状态，与当前项序对齐。状态：**Unknown**（从未量过，用估算）、**Stale**（量过但已失效，
  保留旧值作为自己的估算 —— 比全局均值更接近）、**Measured**、**Collapsed**（行被隐藏或 `flow="false"`：VLG 不把它算进
  `rectChildren`，它既不占高也不占间距）。`estimate` = 当前 Measured 项的高度均值；还没有任何测量时，先实现一行、量它做种子；
  全部失效（§5.11）之后沿用失效前的均值，直到有新的测量。
- 每项的占位 `extent = Collapsed ? 0 : heights[i] + spacing`；`offset(i) = padding.top + Σ_{j<i} extent_j`；
  `total = padding.top + Σ extent − (有非折叠项 ? spacing : 0) + padding.bottom`（N = 0 时只有 padding）。
- **模型不缓存 spacing / padding 的来源**：`ApplyGroupMetrics` 每次 ReSolve 都按 HashSet 顺序重写布局组，所以每次同步开头从活的
  布局组读入模型（VIR-P9）。
- N > 0 时窗口**至少 1 行**：`Leading + Trailing` 按「窗口外各项的 extent 之和」算，只有窗口非空时才与 VLG 的 `(n − 1) × spacing` 对得上。
- 前缀和在需要时 O(N) 重算（1000 项是微秒级，10 万项也不到 0.1 ms）；可见区间用二分查找。Fenwick 树留给以后（§11）。
- 纯数学部分（`VirtualLayoutModel`）不碰 Unity 类型，可以直接单测。

### 5.2 窗口与余量

滚动位置 `S` = Content 顶到视口顶的距离（即 `content.anchoredPosition.y`）。窗口 = 与 `[S − m, S + V + m]` 相交的项，
`V` 是视口高，余量 `m` 默认一个估算行高（开放问题 1）。实现发生在内容挪动之后、渲染之前的同一帧里，所以快速甩动也不会
露出空白；余量只是为了来回小幅滚动时不反复回收。

### 5.3 布局：`WindowedVerticalLayoutGroup`

`VerticalLayoutGroup` 的子类，是虚拟模式下 Content 的组。`ApplyLayoutMode` 按 `virtualize` 选组型，
`PreConfigureContent` 多读一个 `virtualize`，所以组型在子节点实例化之前就定下来了。

- 两个浮点字段 `Leading` / `Trailing`，变化时 `SetDirty()`。
- `CalculateLayoutInputVertical`：`base` 之后，把 min 和 preferred 都加上 `Leading + Trailing`。`ContentSizeFitter`
  因此把 Content 撑到虚拟总高；min 与 preferred 同时加，基类 `SetChildrenAlongAxis` 里的 `surplusSpace` 就是 0，
  `childForceExpandHeight` 不会把这段空白分给行。
- `SetLayoutVertical`：`base` 之后，把每个子节点往下挪 `Leading`。
- 用浮点而不用 `padding`（`RectOffset` 是 int），不引入取整抖动。
- `IHugContent.ContentSize` 读的是 Content 的 preferred 尺寸，自然就是虚拟总高，所以 `clamp(_, hug, 200)` 照常工作。

### 5.4 锚点与贴边

**粘边是记住的状态，不是每次同步时的几何判断**（VIR-P5）。列表记着 `stuckToStart` / `stuckToEnd` 两个标记（两种模式共用）：
首次推送、每次同步、`ScrollTo*` 设置它们；只有**用户造成的移动**之后才按几何重算 —— 判据是 Content 的位置不等于列表上次写入的
位置（拖动、滚轮、拖滚动条、惯性、回弹都属于这一类）。反例说明为什么不能现算：内容第一次溢出时滚动条出现、视口变窄、行重新折行
变高，这一步发生在 `LateUpdate` 之后的 Canvas 布局里；下一次同步若按几何判断，会认为已不在底部，聊天就永远掉离底部了。

每次同步之前定锚：

1. **粘边** → 同步之后仍贴这条边。粘边默认是起点；`stickToEnd="true"` 时是终点（此时起点不粘）。几何重算时「贴边」指
   `S ≤ ε` 或 `S ≥ total − V − ε`（ε = 1；弹性拉过头也算）；内容不满一屏时同时贴两端。
2. **否则** → 锚 = **本次同步之前已经实现、并且与视口相交的第一行**（它在屏幕上的位置正是用户看到的），记下它的项身份和
   `delta = rowTop − S`。锚项被删了就取它后面第一条还在的，后面都没了就取前面的。**没有已实现的行与视口相交**（甩得比窗口还远、
   拖滚动条跳过去、未激活后第一次同步）→ 用模型找 S 处的项 `k`，`delta = offset(k) − S`。
3. **首次推送**：`S = 0`；有 `stickToEnd` 时 `S = total − V`（聊天一打开就在最新处）。**来自另一个 `BindItems` 绑定对象的推送也按首次推送
   定锚**（同一种 `TKey` 类型不代表同一种 key，不能让锚点跟着巧合相等的 key 跳，VIR-P4）。

同步之后怎么把锚兑现（VIR-P6）：

- **滚动路径**（用户在滚、行高被重量）**只平移、从不直接设 S**：锚项的实测位置挪了多少，S 就挪多少；粘起点 = S 不动；粘终点 =
  保持 `S − (total − V)` 不变。这样顶端 / 底端的弹性回弹不会被每帧掐掉。
- **推送路径**：同上，然后在「没在拖动且 `velocity == 0`」时夹到 `[0, max(0, total − V)]`；首次推送直接取 0 或 `total − V`。

`delta` 用的是**实测**位置（布局之后），所以估算误差和浮点累积都不会造成可见的跳动。

这套策略覆盖的场景：

| 场景 | 结果 |
|---|---|
| 聊天停在底部，来新消息（带或不带裁头） | 仍在底部（规则 1，终点） |
| 聊天往上翻着看，来新消息 + 裁头 | 画面不动（规则 2，按 key 找回锚项） |
| 聊天翻到顶去加载更早的历史（前插） | 停在原来那条（有 `stickToEnd` 时起点不粘，走规则 2） |
| 排行榜 / 信息流停在顶部，新第一名插到最前 | 仍在顶部，看得见新的第一（规则 1，起点） |

**非虚拟模式不做规则 2**，保持今天「推送不动滚动偏移」的语义，避免改变现有列表的行为；规则 1 只对 `stickToEnd` 生效
（起点本来就不会动），用的是同一套粘边标记。

### 5.5 推送：两种模式共用一个下标映射

绑定对象分两种，共用非泛型接口 `IItemBinding`（列表本身只处理 `int` 下标，key 永不装箱）：`PositionalBinding<T, TSlot>`
与 `KeyedBinding<T, TSlot, TKey>`。后者额外实现 `IKeyIndex<TKey>`（上一次被接受的推送：key → 下标）。每次推送产出
`newToOld[j]`（-1 表示新项）：

- **有 key**：用 `EqualityComparer<TKey>.Default` 查表。**null key 或重复 key 会让整次推送被拒**：
  抛 `ArgumentException` 并点名下标和 key，列表保持上一次推送的样子。异常经 R3 的未处理异常处理器**记日志，不会抛回推送方**。
- **无 key**：`newToOld[j] = j < oldCount ? j : −1`，等于今天的位置复用。
- **上一次推送来自任一同 `TKey` 的 key 绑定**（不一定是同一个绑定对象）→ 行按 key 复用；否则按位置（VIR-P4）。常见写法是每次推送都
  `BindItems(Observable.Return(list), …)`，只认同一个绑定对象的话 key 就永远不生效。锚点在这种情况下按首次推送处理（§5.4）。
- **推送的列表会被拷进绑定对象自己的可复用缓冲**（稳态零分配，O(N) 拷贝，VIR-P8）：虚拟列表在滚动时才惰性 bind，宿主原地改了
  推送过的列表也不会越界或绑错。

非虚拟模式的 `Rebuild` 改为消费 `newToOld`：`newSlots[j] = oldSlots[newToOld[j]]`，否则新建一行；没被引用的旧行
**先停用并移出 Content，再 `Dispose`**。Play 模式下 `Destroy` 推迟到帧末：不先处理的话，本帧它还占着布局（裁头时整张表会
错位一帧），也还占着兄弟位（「第 j 行就是第 j 个兄弟」这条 `ReorderDriver` 依赖的不变式在这一帧被破坏）。今天的尾部销毁也顺带
受益。兄弟序只改相对顺序不对的行。无 key 时行为与今天完全一致。

虚拟模式：高度缓存按 `newToOld` 搬到新下标（新项先用估算）；已实现的行按映射换到新下标，还落在新窗口里的留用，
不在的回收；然后按 §5.6 同步。

两种模式都**每次推送重绑所有已实现的行**。契约同今天；没变的行写的是同一个字符串，TMP 直接返回，只剩回调本身的开销。

### 5.6 同步算法（虚拟模式）

```
推送:
  anchor = 保存的锚点?.按 newToOld 搬移 ?? 定锚(§5.4)                  // 用推送之前的实测位置
  应用 newToOld：搬移高度缓存，重映射已实现的行（被删项的行立刻停放） // 无论激活与否都立刻生效
  if 列表未激活: 保存 anchor; pending = true; return                 // §5.9
  Sync(anchor, 全部重绑)

Sync(anchor, 重绑范围):
  if (列表是本帧新建 或 视口尺寸为 0) 且本列表还没强制过: 先强制一次所属 Screen 根的布局   // §5.10
  从活的布局组读 spacing / padding 进模型                              // §5.1
  循环至多 3 次:
    用缓存 / 估算算出 offset、total；由 anchor 求目标 S；求窗口 [first, last]
    对账：窗口外的行 → 空闲表；窗口内缺行的项 → 从空闲表 / 池 / 工厂取行
          兄弟序 = 窗口序；需要 bind 的行：新进窗口的行（滚动时）/ 全部已实现的行（推送时）
    Leading / Trailing ← 缓存；ForceRebuildLayoutImmediate(Content)
    读窗口内各行的实测高度 → 写回缓存（measured = true），更新 estimate
    实测之后窗口不再盖满 [S − m, S + V + m]？ 是 → 再来一轮；否 → 结束
  空闲表里剩下的行 → 停放到池（§5.7）
  按 anchor 兑现 S（滚动路径只平移、推送路径才夹取，§5.4；PuiScrollRect.ShiftContentY，§5.8）
  执行同步期间入队的推送 / ScrollTo*；最后更新 IsAtEnd 并发 OnAtEndChanged
```

- 窗口内各行的高度与 `Leading` 无关（`Leading` 只由窗口之前的项决定），所以一轮布局就能定型。要多轮，只会发生在
  「实测比估算矮很多、窗口盖不满视口」的时候。
- `bind` 抛异常：推送路径上逐行收集，同步照常做完（簿记保持一致），最后重抛第一个异常（经 R3 记日志）；滚动路径上没有调用方可抛，
  用 `UILog.Error`（带行的源码位置）记录后继续。
- **重入**（VIR-P7）：同步期间来的推送（`bind` 回调里推送）与 `ScrollTo*` 入队 —— 推送只留最新一个，`Accept` 推迟到执行时 ——
  同步结束后再执行；`OnAtEndChanged` 也在同步结束后才发，所以它的订阅者推送不会重入。
- 行高是否被外部改了，按**窗口内逐行**比对「当前 rect 高、激活与 `ignoreLayout` 状态」与模型是否相符来判断，不比总高（总高会被浮点
  误差与折叠行骗，VIR-P9）。

### 5.7 行池

停放 = 把行的 `LayoutHost` 移到列表根下一个**非激活**的 `Pool` 容器里，行自己的 `activeSelf` 不变；取出 = 移回 Content
的对应兄弟位。不用「原地 `SetActive(false)`」：ReSolve 会给声明了 `hidden=` 的模板根重放 `Hidden`，把停放的行放回布局里。

- 池只在窗口变小时增长；窗口变大时先从池里取。上限是历史最大窗口，不主动销毁。
- 池里的行仍然是 Screen 的动态子树，ReSolve 照常重放（数量很少）。
- 模板名变了、`Dispose`、首次推送清表时，已实现的行和池里的行一起释放。

### 5.8 `PuiScrollRect`（`ScrollRect` 子类，所有 `ScrollList` 都换成它）

- `[ExecuteAlways, DisallowMultipleComponent]`，与基类相同（不依赖特性继承）。
- `LateUpdate()`：`base.LateUpdate()` 之后调用 `ScrollList` 的同步钩子，**只在 `Application.isPlaying` 时**（VIR-P10：编辑器 tick 也跑
  `LateUpdate`，宿主的 `bind` 不该在编辑模式里被调；EditMode 测试显式调 `RefreshWindow()` / tick）。虚拟模式下有位移、尺寸变化或
  pending 就 `Sync`；非虚拟模式下处理 `stickToEnd` 和 `IsAtEnd`。
- 视口尺寸与窗口内各行在这里轮询（几次 float 比较）：
  - 视口**宽**变了 → 全部降级为 Stale，然后同步；视口**高**变了 → 只重新求窗口。
  - 窗口内某行的高度或激活 / `ignoreLayout` 状态与模型不符（订阅改了字、行内动画、ReSolve）→ 同步并重量。
- **用户移动的判据**：`ConsumeUserMotion()` = Content 位置 ≠ 列表上次写入的位置（列表每次写位置后 `MarkWritten()`）；为真时重算粘边标记（§5.4）。
- `ShiftContentY(dy)`：挪 Content → `UpdateBounds()`（`protected`；bounds 依赖 Content 位置）→ 拖动中再平移 `m_ContentStartPosition`
  （`protected`）并 `UpdatePrevData()`（`protected`），这样下一个 `OnDrag` 不会把修正覆盖掉、`LateUpdate` 的速度估计也看不到这次平移 →
  **直接写滚动条**：`size` 与 `SetValueWithoutNotify(verticalNormalizedPosition)`（`UpdateScrollbars` 是 private；`value` 的 setter 会回调
  `SetNormalizedPosition` 把速度清零）。
- 覆盖 `OnBeginDrag` / `OnEndDrag`，记下 `IsDragging`（`m_Dragging` 是 private）。拖动中 `stickToEnd` 不抢位置。
- `OnEnable`：通知列表处理 pending。
- 对非虚拟、也没开 `stickToEnd` 的列表，行为与 `ScrollRect` 完全相同。

### 5.9 未激活的列表

四个频道叠放、同时只显示一个。隐藏的频道收到推送时只记数据（`pending = true`），**不调 `bind`、不实现行**；显示出来
（`OnEnable`）时再按锚点同步。这样顺带避开了 TMP 在未激活节点上量出高度 0 的坑（`InactiveMeasure`）。
未激活时调用 `ScrollToEnd` / `ScrollToIndex` 只记为意图，显示时兑现。

细节：推送的下标映射**立刻**应用到模型与已实现的行（被删项的行立刻停放、不 bind）；多次推送时，保存的锚点逐次按映射链式搬移；
粘边用的是记住的标记（§5.4）。显示之前 `Slots` 里的行还是旧数据。

### 5.10 首帧：视口还没有尺寸

宿主常在 `UI.Open` 之后同一帧就 `BindItems`。如果列表在 `<VStack>` 里靠 `height="stretch"` 拿高度，Canvas 布局之前它的
尺寸还不对：拿错误的宽度量行，折行文本会量错；拿 0 高求窗口，第一帧就是空的。所以：**列表在本帧新建（`BornFrame`）
或视口尺寸为 0 时，第一次同步前先对所属 Screen 的根 RectTransform 做一次 `ForceRebuildLayoutImmediate`**。这是本帧
Canvas 布局本来就要做的工作，只是提前了；之后 Canvas 那一轮只剩列表自己被标脏的部分。强制布局后仍为 0（祖先未激活）
→ 按 §5.9 挂起。

### 5.11 ReSolve / 宽度变化 / 行内高度变化

- `Screen.ReSolve` 先重放静态节点（`ScrollList` 自己的 `OnAfterApply` 在这一步），再重放动态行（`ReSolveDynamicSubtrees`）。
  所以 `OnAfterApply` 里量不到行的新高度：这里只把缓存标成未测并置 pending，到下一个 `LateUpdate` 再同步重量。
- 宽度变化（转屏、滚动条出现 / 消失）：走 §5.8 的轮询，处理同上。
- 已实现的行被外部改了高度：Canvas 布局先按新高度排（锚点下方的行自然往下推，与今天一样）。锚点上方的余量行变高，
  会让可见行在这一帧往下偏，下一个 `LateUpdate` 纠正。这是已知的一帧瑕疵（开放问题 2）。

### 5.12 `bind` 契约的变化（仅虚拟模式）

1. `bind` 只对**已实现**的行调用：推送时对全部已实现的行，滚动时对新进窗口的行。它可能在任何一帧的 `LateUpdate`
   里被调用，应当是 `(row, item)` 的纯函数。
2. 行滚出窗口后会被回收给别的项。行上不在数据里的状态（展开的 `<Collapsible>`、输入框的焦点与草稿、进行中的动画）
   随之丢失；要保留就放进数据里。
3. 不要在 `bind` 里隐藏整行来做过滤（`row.Hidden = true`），要过滤就过滤数据。被隐藏的行按高 0 计，列表警告一次。
4. `.AddTo(row)` 仍然是正确写法：行被重绑之前释放。
5. 手柄导航只能到达已实现的行（v1 不做「选中即滚入视口」）。

非虚拟模式的契约不变。

## 6. 聊天用法（写进 SKILL 的配方）

```xml
<ScrollList id="chatWorld" class="chat-list" anchor="stretch" itemTemplate="ChatLine"
            virtualize="true" stickToEnd="true">
  <Scrollbar class="chat-scrollbar" spacing="3"/>
  <ChatLine time="19:04" name="星痕" nameColor="#52B2F9" text="有没有盟收人？"/>  <!-- 预览占位，首次 BindItems 销毁 -->
</ScrollList>
```

```csharp
var list = screen.Get<ScrollList>("chatWorld");
list.BindItems(channel.Messages,                  // Observable<IReadOnlyList<Msg>>：上限 1000，满了裁最老的
    (IControl row, Msg m) =>
    {
        row.Get<Text>("time").TextValue = $"[{m.Time:HH:mm}]";
        row.Get<Text>("text").TextValue = m.RichText;
    },
    key: m => m.Id).AddTo(screen);

var more = screen.Get<Btn>("chatMore");           // 「下面还有新消息」
list.OnAtEndChanged.Subscribe(atEnd => more.Hidden = atEnd).AddTo(screen);
more.OnClick.Subscribe(_ => list.ScrollToEnd()).AddTo(screen);
```

- 停在底部：新消息进来仍在底部。往上翻着看：新消息和裁头都不动画面。往上加载历史（前插）：画面停在原来那条。
- key 必须稳定且唯一（消息 id）。不给 key 时追加照样便宜，但裁头和前插会跳。

## 7. 实现地图

| 文件 | 改动 |
|---|---|
| `Runtime/Controls/ScrollList.cs` | `virtualize` / `stickToEnd` 两个 `[UIAttr]`；`PreConfigureContent` 多读 `virtualize`；`ApplyLayoutMode` 选组型；两个带 key 的 `BindItems` 重载；`Rebuild` 改为消费 `newToOld`（先停用再销毁）；虚拟模式的窗口对账、池、`Sync`；`ScrollToStart` / `ScrollToEnd` / `ScrollToIndex`、`IsAtEnd`、`OnAtEndChanged`、`ItemCount`；`OnAfterApply` 置 pending；不兼容组合的运行时降级 + 一次性警告 |
| `Runtime/Controls/Internal/ItemBinding.cs`（新） | `ItemBinding<T, TSlot, TKey>` + 非泛型接口：`Count` / `Bind(row, i)` / 推送时产出 `newToOld`；key 校验 |
| `Runtime/Controls/Internal/VirtualLayoutModel.cs`（新，纯数学） | 高度缓存 / measured / estimate / offset / total / 二分查区间 / 按 `newToOld` 搬移 / 失效 |
| `Runtime/Controls/Internal/WindowedVerticalLayoutGroup.cs`（新） | §5.3 |
| `Runtime/Controls/Internal/PuiScrollRect.cs`（新） | §5.8 |
| `Runtime/Core/Lint/ScrollListRules.cs` | §4.3 六个代码；`CheckScrollList` 拆成分段检查。`IRWalker` / `ScreenInstantiator` 已经在调用它，不需要新接线 |
| `Editor/XsdGenerator.cs` | 无改动（反射自动带出 `virtualize` / `stickToEnd`） |

## 8. 测试（Red 先行）

**EditMode `Tests/EditMode/Controls/ScrollListKeyedTests.cs`**（非虚拟模式下的 key）：

1. 带 key 追加：旧行引用不变，只新建一行。带 key 裁头：第一行被销毁，其余行引用不变，兄弟序跟着前移。
2. 前插 k 项：旧行引用不变，新行在前面。
3. 换序：行跟着 key 走（`_slots[j]` 就是该 key 的旧行），每行文字不变。
4. 重复 key / null key：抛 `ArgumentException`，列表保持上一次推送。
5. 无 key 时与今天一致：`ScrollListReuseTests` 全部仍然通过。
6. 每次推送重绑全部行；`.AddTo(row)` 在重绑之前释放。

**EditMode `Tests/EditMode/Controls/ScrollListVirtualTests.cs`**（两种行模板：固定高 30 的 `Frame`，以及会折行的 `Text`）：

1. 推送 1000 项之后，Content 下的行数 ≤ ⌈V / 最小行高⌉ + 2 × 余量行；`SlotCount` = 已实现行数；`ItemCount` = 1000。
2. Content 高 = 虚拟总高；固定行高时精确等于 `padding + N × 30 + (N − 1) × spacing`。
3. 滚到中间（写 `anchoredPosition` 后调内部的 `RefreshWindow()`）：窗口内各项的行都在，兄弟序 = 项序，每行位于
   `offset(i)`；滚出去的行被复用（没有新实例）。
4. 不等高的行（折行文本）：未测项用估算，实现后实测；往上滚、实现新行时，锚点行的屏幕位置不变。
5. 带 key 裁头、视口在中间：锚项屏幕位置不变。在末端且 `stickToEnd`：仍在末端。
6. 带 key 前插 50 项、视口在起点：有 `stickToEnd` → 停在原来那条；没有 → 仍贴起点。
7. `ScrollToEnd` / `ScrollToIndex(500)` / `ScrollToStart`：目标项位置正确；`IsAtEnd` 与 `OnAtEndChanged`（回放 + 去重）正确。
8. 列表未激活时推送：不调 `bind`；激活后窗口正确。
9. 视口变宽：measured 全部失效，可见行重量。ReSolve（切 Variant 改 `fontSize`）：可见行重量。
10. 池：窗口缩小时行停放到 `Pool`（非激活容器，行自己的 `activeSelf` 不变），窗口变大时优先从池取；模板根声明了
    `hidden="false"` 时，ReSolve 不会把停放的行放回 Content。
11. 首帧：列表在 `<VStack height="stretch">` 里，`UI.Open` 之后同帧推送 → 窗口盖满视口、行高按正确宽度量出。
12. 静态占位：首次推送前照常排版（`Leading` / `Trailing` 为 0），首次推送时销毁。
13. `bind` 抛异常：推送路径重抛，且簿记一致；滚动路径走 `UILog.Error`。
14. 不兼容组合的运行时降级：`columns` / `horizontal` → 非虚拟；`reorder` 不开；各警告一次。

**EditMode `Tests/EditMode/Controls/VirtualLayoutModelTests.cs`**：offset / total / 二分查区间 / 搬移 / 估算均值 / 失效，纯数学。

**Lint `Tests/EditMode/Lint/ScrollListVirtualRulesTests.cs`**：六个代码各一正一反，另各加一条经 `class=` 带入、一条经变体带入。

**PlayMode `Tests/PlayMode/Controls/ScrollListVirtualPlayTests.cs`**：

1. 惯性甩过 1000 项：每一帧窗口都盖满视口（没有空白帧）。
2. 拖动中推送（裁头）：手指下那条不跳，松手后没有多出来的速度。
3. `stickToEnd`：每帧推一条，始终在末端；用户往上拖离末端后不再被拽回。
4. 非虚拟模式带 key 裁头：推送返回时 Content 的子节点恰好是新的行、顺序 = 数据顺序（被移除的行已经停用并移出 Content，
   不等帧末的延迟销毁）。

**性能基准**（`[Explicit]`，不进常规测试）：用 ChatLine 形状的模板，N = 20 / 200 / 1000 / 10000，测「裁头 + 追加一条」
的推送 + `Canvas.ForceUpdateCanvases()`。期望：虚拟模式各个 N 在同一量级（目标：桌面编辑器 ≤ 2 ms）；非虚拟 + key 复现
报告第二列。数字写进实施记录。合入后在宿主用同一套 UI Preview 方法重测 `chatWorld`。

用 Unity MCP 跑；`dotnet format --verify-no-changes --severity warn` 通过；改过的 `.ui.xml` 过 UIXmlLint。

## 9. SKILL / 文档更新（同一 PR，英文）

| 文件 | 改动 |
|---|---|
| `authoring-promptugui-xml/SKILL.md` | `<ScrollList>` 属性表加 `virtualize` / `stickToEnd` 两行，并指向 `reference/virtualize.md`；速查表加一行；lint 代码表加 `PUI-SCROLL-VIRTUAL-*` |
| `authoring-promptugui-xml/reference/virtualize.md`（新） | 何时用；什么变了（§5.12）；锚点与粘边（§5.4 的场景表）；不兼容组合与 lint；聊天配方（§6） |
| `authoring-promptugui-xml/reference/reorder.md` | 「keyed `BindItems` diffs」那句改为：key 已经有了（没有动画）；虚拟列表不能拖排 |
| `scripting-promptugui-csharp/SKILL.md` | **List / option push**：key 重载（两种模式）、虚拟模式的契约、滚动 API、`OnAtEndChanged`；速查表；Theme switching 一段里「行数很多时列表本身就该考虑虚拟化」改为指向 `virtualize` |
| `AGENTS.md` | 路由表加一条：`virtualize` / `stickToEnd` / 带 key 的 `BindItems` → `reference/virtualize.md` + C# skill |
| 主 spec §9.5 | 补一句：key 与虚拟化见本文 |

## 10. 演示同步（同一 PR）

`Samples~/CommonControls` 加**第 5 个页签「聊天」**（VIR-P11：列表页在横屏只剩约 100 高，放不下第二个列表），页里是一个
`virtualize="true" stickToEnd="true"` 的聊天列表：生成 1000 条消息，每秒来一条，超过 1000 条裁掉最老的；一个「↓ 新消息」按钮接
`OnAtEndChanged` / `ScrollToEnd`，一个「来 50 条」按钮。五个页签在竖屏的高度从 120 收到 108 才放得下。改完跑 UIXmlLint。
宿主工程（ssw_re_client）的接入不在本 PR 里。

## 11. 非目标

- 网格 / 横向的虚拟化。网格格子等高，最容易；横向是同一套数学换个轴。
- 虚拟模式下拖排。
- 固定行高的快路径 `itemHeight=`。
- 动画滚动（`ScrollToEnd(animated)`）；内容不满一屏时贴底排列（`align="end"`）。
- 手柄导航滚入未实现的行；`TryGetRow(index)`。
- 非虚拟模式下按项锚定（§5.4 规则 2）。
- key diff 的增删 / 移动动画（reorder spec §2-H）。
- `<Carousel>` / `<TabBar>` / `<Markdown>` 的虚拟化或 key 复用。
- Fenwick 树前缀和（十万项级别再说）。
- 分帧 bind（甩动很快时占位行先出、内容后填）。

## 12. 已定的决策（2026-09-29 与作者对齐）

| # | 决策 | 理由 |
|---|---|---|
| VIR-D1 | 行高：**量已实现的行 + 估算未实现的行** | 作者选定；`ChatLine` 正文会折行；宽度变化只重量可见行（§2-B / §2-C） |
| VIR-D2 | v1 范围：纵向单列 + key + `stickToEnd` / `ScrollTo*` / `IsAtEnd`；网格、横向、拖排另开 | 作者选定 |
| VIR-D3 | 做成 `<ScrollList>` 的 `virtualize` 模式，不新开控件 | §2-D |
| VIR-D4 | 窗口化 LayoutGroup（浮点 `Leading` / `Trailing`），行仍是 Content 的直接子节点 | §2-E；行的语义与 lint 与今天一致 |
| VIR-D5 | key 为泛型 `Func<T, TKey>`，两种模式都生效；调用写法同 Carousel | 不装箱；非虚拟模式直接拿到报告第二列 |
| VIR-D6 | 每次推送重绑全部已实现的行 | 契约同今天；TMP 同串早退，所以不贵 |
| VIR-D7 | 锚点：先看粘边（默认起点，`stickToEnd` 时终点），否则锚住第一条可见的行（仅虚拟模式） | 聊天前插 / 裁头不跳；排行榜在顶部仍能看到新第一；非虚拟列表行为不变 |
| VIR-D8 | 停放 = 移入非激活的 `Pool` 容器 | 不受 ReSolve 重放 `hidden` 影响 |
| VIR-D9 | 滚动同步挂在 `PuiScrollRect.LateUpdate`（`base` 之后）；推送时同步执行 | 执行顺序确定；推送之后立即可断言 |
| VIR-D10 | `virtualize` 在实例化时定型 | 切模式要整体重建组型与窗口，收益不值 |
| VIR-D11 | 未激活的列表只记数据 | 四频道叠放；TMP 在未激活节点上量出 0 |
| VIR-D12 | 不兼容组合：lint 报 error，运行时降级、不崩 | 一个笔误不该让 Screen 打不开（同 `cellSize` 的态度） |
| VIR-D13 | 重复 / null key 拒绝整次推送 | key 就是身份；出错是宿主 bug，保留上一次的画面比半更新好 |

### 12.1 计划阶段的修正（2026-09-29，已写进上文各节）

写计划时经三路代码勘查与一轮独立评审，改了以下几处；细节与测试见 plan。

| # | 修正 | 落在 |
|---|---|---|
| VIR-P1 | 组合规则只在 CLI（`CheckVirtualize`），运行时由控件自己警告一次 | §4.3 |
| VIR-P2 | HUG 规则用 `HugRules.IsOpenEndedHug`，拦裸 `hug` 与 `clamp(N, hug, _)` | §4.3 |
| VIR-P3 | 非虚拟 `ScrollToIndex` 的上界是 `SlotCount`，虚拟是 `ItemCount` | §4.2 |
| VIR-P4 | 同 `TKey` 的 key 绑定之间按 key 复用行；锚点把另一个绑定对象的推送当首推 | §5.4 / §5.5 |
| VIR-P5 | 粘边是记住的状态，只在用户造成的移动之后按几何重算 | §5.4 |
| VIR-P6 | 滚动路径只平移 S，推送路径才夹取；无相交行时从模型取 S 处的项 | §5.4 |
| VIR-P7 | 同步期间的推送 / `ScrollTo*` 入队，`OnAtEndChanged` 在同步结束后才发 | §5.6 |
| VIR-P8 | 推送的列表拷进绑定对象的可复用缓冲 | §5.5 |
| VIR-P9 | 模型只存高度与状态（含 Collapsed）、spacing / padding 每次从活的组读、窗口至少 1 行、外部改高按行比对 | §5.1 / §5.6 |
| VIR-P10 | `PuiScrollRect` 的钩子只在 Play 模式驱动同步；`ShiftContentY` 直接写滚动条 | §5.8 |
| VIR-P11 | 演示做成第 5 个页签 | §10 |

开放问题 6（非虚拟 `stickToEnd` 在有推送的帧强制一次 O(N) 的 Content 布局）按「接受」处理：只影响开了该属性的列表。

## 13. 开放问题（留给 plan / 实现）

1. 余量大小：默认一个估算行高；PlayMode 甩动实测后再定，也可能改成视口高的一个比例。
2. 外部改高的即时纠正：v1 在下一帧纠正；如果看得出抖动，改为在 `Canvas.willRenderCanvases` 里纠正（`PixelSnap` 的先例）。
3. 每次同步 `ForceRebuildLayoutImmediate(Content)` 与「只重建新行」的开销对比：Profiler 里看一次。
4. `ScrollToIndex` 的对齐方式：v1 只有起始边对齐；居中 / 最小滚动另议。
5. 甩动时每帧新进窗口的行数 × TMP 排版（桌面约 0.3 ms/行）：手机上超预算的话考虑分帧 bind（§11）。
6. ~~非虚拟模式的 `stickToEnd` 要在有推送的帧强制一次 Content 布局（O(N)）~~ —— 已定：接受（§12.1）。

## 14. 里程碑拆分

一个 PR，三步提交：

- **M0 key**：`ItemBinding` + `Rebuild` 消费 `newToOld` + 先停用再销毁 + `ScrollListKeyedTests`。单独也有价值：报告第二列。
- **M1 虚拟化核心**：`VirtualLayoutModel` / `WindowedVerticalLayoutGroup` / `PuiScrollRect` / 同步 / 池 / 锚点 / 未激活 / 首帧
  + `VirtualLayoutModelTests` + `ScrollListVirtualTests` + PlayMode 测试。
- **M2 作者面收尾**：两种模式的 `stickToEnd` / 滚动 API / `IsAtEnd` + 六条 lint + 两份 SKILL / `reference/virtualize.md` /
  `AGENTS.md` / 主 spec + 演示 + 性能基准。
