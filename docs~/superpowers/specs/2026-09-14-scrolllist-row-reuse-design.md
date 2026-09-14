# `ScrollList.BindItems` 行复用 —— 每次推送不再整表销毁重建

> 状态：**待实现**（spec，2026-09-14）。
> 需求来源：宿主工程 ssw_re_client 星球面板（`Panels/Planet.ui.xml` 的 `slots` 网格，`BuildSlot` × 9）。
> Deep Profile 第二次打开面板的那一帧：`ScrollList.Rebuild` 9 张卡 = 205 ms（deep）/ `Router.Open` 368 ms，
> 每张卡 ≈ 25 个控件；而这块网格在**面板开着的时候还会反复重建**——`ColonyChanged` / `BuildingsChanged`
> 的每次推送、面板开着换球，都是整表 `Dispose` + 重新实例化。
> 相关：
> 主 spec §9.5（列表 = 代码侧推送，`BindItems` 的形状）；
> `2026-09-10-scrolllist-grid-and-static-children-requirements.md`（静态占位卡：首次 `BindItems` 销毁并接管——本文不改这条）；
> `2026-09-13-runtime-template-instantiate-design.md` §5.3（`.AddTo(instance)` 订阅袋归 Control、`Close` 兜底）与 §11-E（"包内提供池：本里程碑不做"——本文是它的第一步，但不是通用池）；
> `2026-09-14-document-load-parallel-prefetch-and-source-cache-design.md` §10（第二次打开 ~90 ms 的构成；本文与"路由 Page keep-alive"是把它压进一帧的两条腿）。

## 1. 问题

`ScrollList.Rebuild`（`Runtime/Controls/ScrollList.cs:514`）对每次 `OnNext` 做的事：

```
ClearSlots()            // 所有旧行 Dispose：退订阅袋 + Destroy 根 GO
for item in items:
    slot = _factory(_content)   // InstantiateNode：建 GO、AddComponent、Awake、应用模板全部属性
    bind(slot, item)
```

也就是说列表**没有"更新"这个概念，只有"重建"**。在宿主的星球面板上：

- 一次 `Rebuild`（9 张 `BuildSlot`）在 Deep Profile 里 205 ms，去掉 2.4 倍膨胀约 **30 ms 实际**——
  其中一半是建对象（`AddComponent` / `Awake` / `Destroy`），一半是把模板声明的属性应用一遍。
- 这不只发生在打开面板：`PlanetDockSection.RenderCards` 在殖民地行或建筑行的**每次**服务端推送时调用（建造开始 /
  完工 / 升级都会推），面板开着换一颗球也调。一次倒计时到点、完工推送 → 9 张卡全部销毁重建，
  只为把一张卡从「建造中」换成「Lv.1 升级」。
- 就算做了路由 Page 的 keep-alive（另案），面板再次打开时 `Bind` 仍会整表重建——keep-alive 省下的是
  `UI.Open`，`Rebuild` 那 30 ms 得靠本文。

**本文单独不能缩短"第二次打开"那一帧**：那一帧里 `ScrollList` 是新建的，没有旧行可复用。它缩短的是
面板开着期间的每次推送 / 换球，并且是 keep-alive 那条腿能兑现的前提。

## 2. 否决的方案

**A. 复用行时先自动"重置为模板声明态"再 `bind`。否决。** 重置 = 对行里每个节点重放一遍声明属性
（`ControlAttributeApplier.Apply(initial: true)`），而属性应用本来就占一张卡成本的一半——收益直接砍半。
更要紧的是它并不能兑现"和新实例一样干净"的承诺：C# 在上一次 `bind` 里写过、而模板**没声明**的属性
（例如没写 `name=` 的 `<Icon>` 被代码设了图标）重放不到，照样残留。既然做不到完全，就不做半个：
干净与否由 `bind` 回调负责（§6），这也是 RecyclerView / UI Toolkit `ListView` 这类回收列表的通行契约。

**B. 按 key 做 diff（增删中间项时行跟着数据走）。本里程碑不做。** 位置复用（第 i 行 ↔ 第 i 项）已经覆盖
宿主的全部场景：槽位表是定长有序的，推送只改内容不改顺序。keyed 复用是纯增量（`Carousel.BindItems`
已有 `key` 参数的先例），等有"行上带着动画 / 滚动位置身份"的需求再加。

**C. 多出来的行不销毁、留成 inactive 池。本里程碑不做。** 数量变少时销毁尾巴、变多时再实例化，
已经把"数量不变"这个最常见情形做到零实例化；池是下一步（§9）。

**D. 首次 `BindItems` 时把 XML 静态占位卡当作行复用。否决。** 静态卡是 `Screen._nodeMap` 里的静态节点，
带的是**作者写的占位值**（`index="1号槽位" state="升级中" time="01:26:14"`）；`ReSolve`（横竖屏 / 主题 /
resize）会按 `_nodeMap` 把这些占位值重放回去——文字有运行时接管锁挡着，但 `hidden` / 图标名 / 颜色没有，
一次转屏就把绑好的卡打回占位样子。要复用它们得先把节点从 `_nodeMap` 迁到 `_dynamicSubtrees` 并换成模板
默认体，收益（省首开 2 张卡）配不上复杂度。维持 2026-09-10 spec 的规则：首次 `BindItems` 销毁静态卡。

**E. 默认关闭、宿主显式 opt-in。否决。** 收益要默认到手；契约（§6）与现有文档"卡内控件句柄一律在 bind
回调里取"是同一条纪律，宿主 `PlanetDockSection` 已经天然满足。提供 opt-out（§5）给确实要新实例的场合。

## 3. 方案总览

`Rebuild(items, bind)` 改为**位置复用**：

```
if 行是静态占位卡 or 行的模板名 != 当前 itemTemplate:  ClearSlots()   // 与今天一致
n = min(旧行数, items.Count)
for i in [n, 旧行数):  旧行[i].Dispose()                              // 尾巴销毁（Destroy 延后到帧末，与今天同）
for i in [0, n):       旧行[i].ReleaseSubscriptions(); bind(旧行[i], items[i])   // 复用：退订阅袋，不动 GO
for i in [n, items.Count): slot = _factory(_content); bind(slot, items[i])      // 追加：与今天同
```

| | 今天 | 本文 |
|---|---|---|
| 数量不变的推送（宿主 90% 的情形） | 全部销毁 + 全部实例化 | **零实例化**，只跑 `bind` |
| 数量 +k | 全部销毁 + 全部实例化 | 实例化 k 张 |
| 数量 −k | 全部销毁 + 全部实例化 | 销毁 k 张 |
| 首次 `BindItems`（静态占位卡在场） | 销毁占位卡 + 全部实例化 | 不变 |
| 行上 `.AddTo(slot)` 的订阅 | 随 Dispose 释放 | **每次 `bind` 前释放**（§4.2） |

公开 API 形状不变：`BindItems<T>` / `BindItems<T, TSlot>` 签名照旧；新增 XML 属性 `reuseItems`（默认 `true`）与
同名 C# 属性作为 opt-out（§5）。`Carousel` / `TabBar` 不动（§9）。

## 4. 语义细则

### 4.1 位置复用

行按 `_slots` 的下标对应数据下标：第 i 项绑到第 i 行。行在 Content 里的兄弟顺序就是 `_slots` 顺序（追加永远在
尾、销毁只销毁尾巴），所以网格 / 单列的视觉顺序与数据顺序一致，`GridLayoutGroup` 按兄弟序排格。
**前提**：宿主没有自己调过行的 `SetSiblingIndex`——调过的宿主用 `reuseItems="false"`（§5）。

### 4.2 复用前释放订阅袋

`bind` 回调按 SKILL 的既有建议把行内的 R3 订阅 `.AddTo(slot)`（`Control.Track`，2026-09-13 spec §5.3）。
今天它们随行 `Dispose` 释放；复用后行不 Dispose，所以 **`Rebuild` 在再次 `bind` 之前调 `Control.ReleaseSubscriptions()`**
（= 现有私有 `DisposeSubscriptionsRecursive` 开成 internal：释放自身 + 子树订阅袋，不碰 GO）。
对宿主而言契约没变：「上一次 bind 挂在卡上的订阅，在下一次 bind 之前一定已释放」。
宿主自己管的 `CompositeDisposable`（`PlanetDockSection._cardSubs` 每次 publish 先 Dispose 再新建）不受影响。

### 4.3 尾巴销毁、追加实例化

与今天同一条路径：销毁 = `Control.Dispose()`（退订 + `Destroy` 根 GO，Play 下延后到帧末、EditMode 立即）；
追加 = `_factory(_content)` → `InstantiateNode`，新行登记为 owner Screen 的动态子树（`RegisterDynamicSubtree`），
`TSlot` 类型检查照旧（不匹配抛 `InvalidCastException`）。

### 4.4 什么时候退回整表重建

1. **静态占位卡在场**（`_bound == false`）：首次 `BindItems`，`ClearSlots()` 销毁占位卡（2026-09-10 spec 的规则，不变；否决 D）。
2. **模板换了**：`_slots` 记下建它们时的 `itemTemplate` 名（`_slotsTemplate`）；`Rebuild` 时与当前 `_itemTemplate` 不同
   → `ClearSlots()`。比较的是**名字**不是 `_factory` 委托——`ItemTemplate` 是 `[UIAttr]`，每次 `ReSolve` 重放都会
   `ResolveFactory` 出一个新委托，按委托比会次次误判。
3. **`reuseItems="false"`**（§5）。
4. **某一行被外部销毁**（`slot.GameObject == null`，例如宿主自己 `Dispose` 了一张卡）：只这一行按"追加"路径重建
   （从 `_slots` 里换掉），其余照常复用；新行 `SetSiblingIndex(i)` 钉回原位。

### 4.5 `bind` 抛异常

与今天一致：异常从 `Rebuild` 冒出（R3 送到未处理异常处理器），`_slots` 保持已处理到的状态——复用行已经绑到新数据、
未处理的行保持旧数据。不做回滚（今天也不做）。

### 4.6 与 `ReSolve` / 动态子树的关系

复用的行仍在 `Screen._dynamicSubtrees`（登记发生在实例化时，不因 `bind` 变），Variant / 主题 / resize 的重放照旧到达；
`PruneDeadDynamicSubtrees` 按 `Root.GameObject == null` 剔除被销毁的尾巴。运行时接管锁（`_lastAppliedDefaultText` /
`_lastAppliedRuntimeState`）是每个 Control 自己的状态，复用后由新一次 `bind` 写入的值接着受保护——行为等同一张
"被 bind 了两次的卡"，这在今天的"面板开着换球"里已经是常态（`PlanetDockSection.Bind` 换球时并不重建 ScrollList 控件本身）。

### 4.7 `Dispose` / `Close`

`ScrollList.Dispose` → `ClearSlots()` 照旧销毁所有行；`Screen.Close` 兜底 Dispose 动态子树（2026-09-13 spec §5.3）。
本文不改任何释放路径。

## 5. XML / C# 面

```xml
<ScrollList id="slots" itemTemplate="BuildSlot" reuseItems="false"/>   <!-- 默认 true -->
```

- `reuseItems`（bool，默认 `true`）：`false` = 每次推送整表重建（今天的行为）。给"宿主重排过行"（§4.1）、
  "行是自定义 Control、内部状态不经属性重置"这类场合。
- C# `public bool ReuseItems { get; set; }`，`[UIAttr]`，XSD 由生成器自动带上，lint 无新规则。
- `BindItems` 签名不变。不加 `unbind` 回调：订阅袋自动释放（§4.2）已覆盖需要它的主要理由；
  宿主要在换绑前做别的清理，就在自己的 `bind` 开头做（它拿到的正是那张行）。

## 6. 宿主契约（写进 SKILL）

行是**回收**的，不是每次新建：

1. `bind` 回调对它关心的每个属性都要**无条件写一遍**，不能只在某个分支写、另一个分支指望"新实例是模板默认值"。
   典型坑：`if (kind == Empty) btn.Hidden = false;` 而其它分支不写 `btn.Hidden`——复用后上一项留下的 `false` 会漏出来。
   两个分支都写，或者用互斥的 `built` / `empty` 两块 Frame 各自显隐（宿主 `BindSlotCard` 就是这么写的）。
2. 上一次 `bind` 拿到的控件句柄在下一次推送后**可能指着另一项**，不再是"已销毁 → 一用就报错"。
   句柄一律在本次 `bind` 回调里取（这条本来就在 SKILL 里）；要跨推送持有（倒计时 ticker 那类），推送时先清表再重取
   （宿主 `_countdowns` 已如此）。
3. `.AddTo(slot)` 仍是行内订阅的正确写法：下一次 `bind` 前自动释放。
4. 需要旧行为的写 `reuseItems="false"`。

## 7. 测试（Red 先行）

`Tests/EditMode/Controls/ScrollListReuseTests.cs`（夹具同 `ScrollListStaticChildrenTests`：`Open(xml, templates)` + `Bind(list, n)`）：

- `Same_count_rebind_reuses_every_row`：绑 3 → 再绑 3，三个 `IControl` 引用相同、`GameObject` 相同、Content `childCount` 不变、
  文本已是新值。
- `More_items_instantiate_only_the_extra`：3 → 5，前三引用相同，后两为新；`childCount` 5。
- `Fewer_items_dispose_only_the_tail`：5 → 2，前两引用相同，后三 `GameObject == null`（EditMode 立即销毁）。
- `Rebind_releases_row_subscriptions_before_bind`：`bind` 里 `.AddTo(slot)` 一个计数 Disposable；第二次推送时它已被 Dispose，
  且释放发生在第二次 `bind` 调用**之前**（回调里断言）。
- `First_bind_still_destroys_static_placeholders`：与 `The_first_BindItems_destroys_the_static_children` 同构，再推送一次时
  新行被复用（占位卡不复活）。
- `ItemTemplate_change_between_binds_rebuilds_all`：`list.ItemTemplate = "Row2"` 后推送，旧行全部销毁、新行全为新模板。
- `Externally_destroyed_row_is_rebuilt_in_place`：手动 `Dispose` 第 1 行，再推送同数量：第 1 行是新实例且兄弟序为 1，其余复用。
- `ReuseItems_false_rebuilds_all`：`reuseItems="false"`，同数量推送后所有引用不同。
- `Grid_mode_keeps_sibling_order_after_rebind`：`columns="2"`，3 → 5 → 4，每次 `_slots[i]` 的兄弟序 == i。
- `Reused_rows_still_follow_ReSolve`：复用后切 Variant，行内 `attr.var` 覆盖生效（动态子树登记未丢）。
- 既有 `ScrollListTests.BindItems_rebuild_replaces_slots`（只断 `SlotCount`）、`ScrollListStaticChildrenTests` 全部、
  `DynamicSubtreeReSolveTests` / `DynamicSubtreeScaleTests` 里经 `BindItems` 的用例必须仍绿；`ScreenInstantiateTests`
  不受影响（它不经 ScrollList）。

Unity MCP 跑；`dotnet format --verify-no-changes --severity warn` 过。

## 8. 影响面

- **SKILL（同 PR，英文）**：`scripting-promptugui-csharp/SKILL.md` "List / option push" 与 "Per-control subscription lifetime"
  两段改写成 §6 的契约（rows are recycled；`.AddTo(slot)` disposed before the next bind；handles from a previous push may now
  point at a recycled row；`reuseItems="false"` opts out）；`authoring-promptugui-xml/SKILL.md` `<ScrollList>` 属性表加 `reuseItems`。
- **XSD**：生成器按 `[UIAttr]` 自动带出 `reuseItems`，XSD 测试用 `StringAssert.Contains` 加一条。
- **`Carousel` / `TabBar`（`TabGroupCore`）**：不动。Carousel 的卡带居中 / 动画身份，TabBar 的 Tab 带选中互斥与
  `OnSelectionChanged`，复用语义要另议（§9）。
- **宿主**：`PlanetDockSection` 不用改——`_cardSubs` 每次 publish 重建、`_countdowns` 每次清空、`BindSlotCard` 每个分支都写显隐。
  收益：面板开着期间每次推送 / 换球 30 ms → ~2 ms（只剩 9 次 `bind` 回调）。

## 9. 不做的事 / 后续

- **keyed 复用**（否决 B）：`BindItems(source, bind, key)` 与 Carousel 同形，行跟数据走；有"行上带状态且列表会插删"的需求再做。
- **inactive 行池**（否决 C）：数量抖动的列表（聊天、日志）再做；配 `maxPooled`。
- **复用静态占位卡**（否决 D）。
- **`Carousel` / `TabBar` 复用**：等本文的契约在 ScrollList 上跑稳。
- **路由 Page keep-alive**（另案）：`UI.Router.Map(..., keepAlive: true)`，关面板 = 隐藏不销毁；与本文合起来才能把
  "第二次打开"从 ~90 ms 压到一帧内（`UI.Open` 省掉、`Rebuild` 零实例化）。
