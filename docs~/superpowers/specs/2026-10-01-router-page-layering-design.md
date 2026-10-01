# Router 层序 —— routed Page 按链路深度定 sortingOrder，清理同带平局

> 状态：**已定，实现中**（2026-10-01，作者同意评估里的推荐项；指示只补 spec、不写 plan，red 先行、按 §11 分步提交，
> 分支 `feat/router-page-layering`）。
> 需求来源：宿主工程（ssw_re_client）聊天框在 routed Page `Round` 里，星球面板是它的子 Page `Planet`；两块根画布
> `sortingOrder` 都是 0，HUD 偶尔翻到上面，左下角聊天框透到星球面板上。
> 相关：
> `2026-06-09-router-navigation-design.md` §5.1 / §5.2 / §11 / §12（§12 遗留「routed Modal 与 ad-hoc dialog 两套 sorting
> 计数如何不打架」—— 本文 §7 落地）、
> `2026-09-16-close-transition-design.md` §5.5 / §5.6（Overlap 下的叠放靠「新 root 后建 → 在上」—— 本文 §5 把它变成保证）、
> `2026-05-20-modal-layering-design.md` §2（层带）、
> `2026-05-14-messagebox-modal-design.md` §4.6（层管理器在 `UI.Open` 之后覆盖 configurator 的 sortingOrder）、
> `2026-08-29-tabmenu-design.md` 风险表末行（「Router 分层页面若用了大间隔 order，在实施期确认一次并写进 skill」）。

## 1. 问题

`UI.Router.Reconcile.cs` 的 `ActivatePage` 只给 Modal 设 `overrideSorting` + `SortingOrderBase + 链内 Modal 数`，Page 什么都不设：
所有 routed Page 的根画布都停在 `CanvasConfigurator` 给的值（默认 0）。

同 `sortingOrder` 的两块 Overlay 根画布谁在上面，Unity 没有规定；新建 / 销毁别的根画布（Toast、Loading）后可能翻转。
于是子 Page 会被父 Page 反过来盖住，而且：

- **点击也跟着错。** `GraphicRaycaster` 对 Overlay 画布按 `sortingOrder` 排，平局再按画布的实际渲染顺序 ——
  HUD 翻到上面时，聊天框不止透出来，还会接走那块区域的点击。
- **玻璃让它更难察觉。** 玻璃只采样相机画面（`reference/glass.md`「What the glass can see」），从不透出下层 UI。
  子 Page 的玻璃面板上出现了 HUD 的控件，看着像「玻璃是半透明的、透出了下面的 UI」，其实是 HUD 画在了玻璃上面。

`2026-09-16-close-transition-design.md` §5.5 写的「绘制先后只由层级顺序决定（新 root 后建 → 在上）」正是这条不成立的假设。

同一类问题还有三处：

1. **routed Modal 与 ad-hoc dialog 都从 1000 起。** `Reconcile.cs` 用 `SortingOrderBase + 链内前序 Modal 数`，
   `UI.Modal.cs` 用 `SortingOrderBase + 栈深 − 1` —— 在 routed Modal 里弹 `MessageBox`，两者都是 1000。
   router spec §12 定了原则「ad-hoc dialog 永远在 routed Modal 之上」，计数方案留给 plan，一直没落地。
2. **热重载把 router 的装饰丢了。** `UI.ReloadAsync` = `CloseImmediate` + `Open`：新 Screen、新 Canvas，只跑一遍
   configurator，router 不知情。routed Modal 今天就会掉出 modal 带（回到 0）并丢掉 `ModalEscapeListener`（ESC 不再 Back）。
3. **Page 挂在 Modal 下。** `ResolveChain` 不拦；这个 Page 落在 Page 带，被它自己的 Modal 祖先盖住。

## 2. 目标与非目标

**目标**

1. routed Page 的 `sortingOrder = PageSortingOrderBase + depth × PageSortingOrderStep`，`depth` = 链路里它前面的 Page 数。
   子 Page 严格高于父 Page，同级 Page 相同（不累加），整条 Page 带低于 Loading / Modal / Toast / Tutorial。
2. 留覆盖口子：两个静态旋钮，形状同 `UI.Modal.SortingOrderBase`。
3. Overlap 过渡期同深度的新旧两页有确定的上下（新页在上，延续 close-transition §5.5 的设计意图）。
4. 热重载重建的 routed Screen 重新拿到 router 的装饰（Page 层序；Modal 带 + ESC 监听）。
5. ad-hoc dialog 严格高于所有 routed Modal。
6. Page 挂在 Modal 下 = `RouteException`。
7. 文档：层带规则、与 configurator 的关系、玻璃「盖住即完全遮挡」的排查句。

**非目标**

- Modal 带内的退场重叠（routed Modal 换 routed Modal、ad-hoc 栈顶换栈顶时，幽灵与新模态同值）—— 维持
  close-transition §5.6「作者在 XML 里调」。Modal 带步长是 1，没有给幽灵让位的空隙。
- 非路由 Screen 的层序 —— 仍归 `CanvasConfigurator`；默认 0 与根 Page 平局，靠文档（RPL-D10）。
- 跨渲染模式定序 —— Overlay 画布永远画在所有相机之后，`sortingOrder` 管不到 Overlay 与 Camera/World 之间；只写文档，
  不加运行时警告（RPL-D11）。
- 热重载后重跑 `OnEnter` —— 与本文无关，维持现状。
- routed Modal 异步加载窗口里（`CloseAll` 之后、它 `UI.Open` 之前）新弹的 ad-hoc dialog 与它的先后 —— 接受。

## 3. 层带总表

```
非路由 Screen     CanvasConfigurator 给的值（默认 0）
routed Page       PageSortingOrderBase + depth × PageSortingOrderStep    默认 0, 10, 20 …
                    └ 每个深度占 [slot − 1, slot + 2]：退场幽灵 −1、TabMenu 遮罩 +1、TabMenu 面板 +2
Loading overlay   Loading.SortingOrder                                    500
routed Modal      UI.Modal.SortingOrderBase + 链内前序 Modal 数              1000, 1001 …
ad-hoc dialog     UI.Modal.SortingOrderBase + 链内 routed Modal 数 + 栈深 − 1
Toast             UI.Toast.SortingOrder                                   2000
Tutorial          UI.Tutorial.SortingOrder                                3000
```

## 4. Page 层序

### 4.1 深度

`depth` = 活动链路里排在该节点之前的 `RouteKind.Page` 节点数。Tab / Prompt 不计（Tab 寄生在宿主画布上，Prompt 没有画布）；
Modal 不会出现在 Page 之前（§8）。

激活是自底向上的：激活 `target[i]` 时 `_chain` 恰好是 `target[0..i-1]`，即它的静态祖先链。所以深度是注册表（`Parent` 链）
的纯函数，与怎么导航过来无关：

- 留在公共前缀里的 Page 祖先链没变，深度不变 —— `RefreshTarget` / `Back` / `SameChain` **不需要**重新赋值（RPL-D5）；
- 同级 Page 深度相同，「同级换 Page 不累加」自然成立；
- 唯一会让画布回到 configurator 值的是 Screen 被重建（§6）。

### 4.2 写入时机与 CanvasConfigurator

在 `UI.Open` 返回之后、`OnEnter` 之前写 —— 与现有 Modal 分支同一个位置。`CanvasConfigurator` 在 `Screen.Open` 里先跑，
所以 **routed Page 的 sortingOrder 覆盖 configurator 的值**；configurator 继续拥有其余一切（`worldCamera`、scaler、
`planeDistance` …）以及非路由 Screen 的 sortingOrder。

这与 Modal / Loading / Toast / Tutorial 四个层管理器的既有约定一致（messagebox spec §4.6；C# skill「Modal Canvas +
`UI.CanvasConfigurator`」）。不让 configurator 拍最后一板，是因为：

1. 它只拿到 `(canvas, screenName)`，不知道深度；
2. 常见的一刀切写法（C# skill 的示例就是 `canvas.sortingOrder = name == "Settings" ? 100 : 0`）会把平局悄悄带回来 ——
   和这次要修的问题一样难察觉。

覆盖口子：

| 口子 | 用途 |
|---|---|
| `UI.Router.PageSortingOrderBase`（`int`，默认 `0`） | 整条 Page 带上下平移，例如让非路由 HUD 稳定在所有 Page 之下 / 之上 |
| `UI.Router.PageSortingOrderStep`（`int`，默认 `10`） | 相邻深度的间隔 |
| `OnEnter` | 在写入之后运行，可以单独改某一页（热重载会还原成 router 的值，见 §6） |

两个旋钮是静态属性，`UI.ResetForTests` 复位。按路由逐个定值的委托（`Func<route, depth, int>`）等有需求再加。

### 4.3 步长下限

每个深度占 `[slot − 1, slot + 2]` 四个值（§3）。相邻深度不重叠要求 `step − 1 > 2`，即
`step ≥ TabMenu.PopupSortingOffset + 2`（今天是 4）。setter 对更小的值抛 `ArgumentOutOfRangeException`，消息说明原因。

### 4.4 Loading 带天花板

算出的 slot `≥ Loading.SortingOrder` 时 `Debug.LogWarning`（`[PromptUGUI]` 前缀，画布 GameObject 作 context）：
该页已不在 Loading 之下。默认值下要 50 层才会触发，实际只会在旋钮配错时出现。每次激活都报（导航不是每帧的事），不做去重。

不走 `UILog`：这条是关于路由配置的，不关于作者写的某个节点。

## 5. Overlap 下的同深度叠放：新页在上

close-transition §5.5 的设计是：push 子页在父页上入场；pop 子页幽灵在父页上退场；平级 A→B「B 在 A 上入场、A 在下面淡出 ——
push 感」。前两条现在由深度保证；第三条 A、B 同深度同值，仍是平局。

规则：router 反激活一个 Page 时（`Deactivate`：Overlap 路径与 `Reset`），**先把它的画布降一格（slot − 1）再 `UI.Close`**。

| 导航 | 结果 |
|---|---|
| push：`[A]` → `[A, A1]` | A1（10）在 A（0）上入场 |
| pop：`[A, A1]` → `[A]` | A1 幽灵（9）仍在 A（0）上退场 |
| 平级：`[A, A1]` → `[A, B1]` | B1（10）在 A1 幽灵（9）上入场 —— push 感 |
| 跨树：`[A, A1]` → `[B]` | A1 幽灵（9）在 B（0）上退场，A 幽灵（−1）在 B 下 —— 往浅处走像 pop |

`Sequential` 下旧页销毁之后才建新页，没有重叠，降一格是空操作。没写退场动画的页在 `UI.Close` 里当帧销毁，降一格同样无害。

**为什么不是「退出者在上」**（评估时我推荐过它：退场动画一定看得见）：对称模板（`on="open" reverse-on="close"`，最常见的写法）
下，旧页倒放滑出、新页滑入，旧页压在上面看起来像两页互换，不如 §5.5 的 push 感自然。§5.5 是作者早先的明确决定，这里只把它从
「碰运气」变成保证。

**为什么降旧页而不是抬新页**：新页始终停在自己的 slot 上 —— 值稳定、可测，热重载（§6）重算出的也是同一个值；幽灵是暂时的。

C# skill 里「a page opened later draws above one opened earlier」一句改写为深度优先、同深度新页在上。

## 6. 热重载重新装饰

把 router 对一块 routed 画布做的事收进一个内部函数 `ApplyRouteLayer(RouteNode def, Screen screen, int index)`
（`index` = 节点在 `_chain` 里的位置；激活时就是 `_chain.Count`）：

- Page：§4 的 slot + §4.4 的天花板检查；
- Modal：`overrideSorting` + `SortingOrderBase + 前序 Modal 数` + `ModalEscapeListener`（ESC → `Back()`，栈顶且无 ad-hoc 时）。

两处调用：

1. `ActivatePage`（替换现有的 Modal 分支）；
2. `UI.ReloadAsync` 在 `if (wasOpen) Open(screenName)` 之后调 internal 的 `Router.OnScreenReopened(screenName, screen)`：
   在 `_chain` 里找 `ScreenKey == screenName` 的 Page/Modal 节点，找到就 `ApplyRouteLayer`。

`ReloadCommonLibraryAsync` 也走 `ReloadAsync`，一并覆盖。`OnEnter` 不重跑（非目标）。

## 7. ad-hoc dialog 在 routed Modal 之上（router spec §12 遗留项）

`UI.Modal` 的层序改为 `SortingOrderBase + Router.RoutedModalCount + 栈深 − 1`。`RoutedModalCount`（internal）= `_chain` 里的
Modal 节点数。

- Reconcile 改动链路之前会先 `UI.Modal.CloseAll()`，所以 ad-hoc dialog 存在期间 routed Modal 数不变，两套计数不会交错。
- Prompt 节点弹的 `InputBox` / `MessageBox` 本身就是 ad-hoc dialog → 自然落在托管它的 routed Modal 之上。
- 链路里没有 routed Modal 时 `RoutedModalCount = 0`，数值与今天完全一样（`ModalStackTests` 的 1000 / 1001 不变）。

## 8. Page 不能挂在 Modal 之下

`ResolveChain` 组出根→叶链路后，若某个 Page 之前出现过 Modal → `RouteException`：

```
route '<name>': page '<page>' cannot sit under modal '<modal>' — pages sort in the page band, below every modal,
so it would be hidden behind its own ancestor. Map '<page>' with present: RoutePresent.Modal.
```

Tab / Prompt / Modal 挂在 Modal 下照旧允许。行为变化：这种注册以前能「导航成功」但页面被盖住，现在 `Open` / `Navigate` 直接失败。
宿主工程（ssw_re_client 的 `RoundRoutes` / `LobbyRoutes`）没有 Modal 路由，不受影响。

## 9. 决策表

| ID | 决策 | 理由 |
|---|---|---|
| RPL-D1 | slot = Base + depth × Step，depth = 前序 Page 数（Tab/Prompt 不计） | 深度是注册表的纯函数：确定、有界、幂等；同级不累加 |
| RPL-D2 | `UI.Open` 之后、`OnEnter` 之前写，覆盖 configurator | 与四个层管理器一致；configurator 不知道深度，一刀切写法会静默复发 |
| RPL-D3 | 旋钮 `PageSortingOrderBase = 0` / `PageSortingOrderStep = 10`，`ResetForTests` 复位 | 形状同 `UI.Modal.SortingOrderBase`；Base 0 让根 Page 保持今天的值 |
| RPL-D4 | Step < `PopupSortingOffset + 2` → `ArgumentOutOfRangeException` | 每个深度占 4 个值，相邻深度不能重叠 |
| RPL-D5 | `RefreshTarget` / `Back` / `SameChain` 不重新赋值 | 公共前缀深度不变；重写只会冲掉 `OnEnter` 的调整 |
| RPL-D6 | 反激活 Page 先降一格 → 同深度新页在上 | 延续 close-transition §5.5 的 push 感，并使之成为保证 |
| RPL-D7 | `ApplyRouteLayer` 由激活与热重载共用 | 热重载是 router 之外唯一会重建 routed 画布的路径；顺带修好 routed Modal |
| RPL-D8 | ad-hoc = Base + routed Modal 数 + 栈深 − 1 | 落地 router spec §12 的原则；无 routed Modal 时数值不变 |
| RPL-D9 | Page 挂 Modal 下 → `RouteException` | 这种页一定被祖先盖住；fail-fast 好过静默 |
| RPL-D10 | Base 默认 0；非路由 Screen 与根 Page 的平局只写文档 | 改成正数会和 skill 示例里 `Settings = 100` 的写法撞上 |
| RPL-D11 | 跨渲染模式只写文档，不警告 | Overlay 玻璃 HUD + Camera 子页「出现在玻璃后面」正是 glass.md 推荐的合法配方 |

## 10. 测试

EditMode，新文件 `Tests/EditMode/Router/RouterLayeringTests.cs`：

- 根 → 子 → 孙：0 / 10 / 20，严格递增；
- 同级来回切换：始终 10，不累加；
- Page → Tab → Page：Tab 不计入深度（20，不是 30）；
- 旋钮：Base 100 / Step 5 → 100 / 105 / 110；`ResetForTests` 复位为 0 / 10；Step 3 抛异常、4 可以；
- configurator 设 7：routed Page 被覆盖，非路由 Screen 保持 7；`OnEnter` 里读到的已是 router 的值，且可以改；
- Base 495：深度 0 不警告，深度 1（505）警告一次；
- Page 带全部低于 `Loading.SortingOrder`，routed Modal 高于所有 Page；
- 热重载：routed 子 Page 重建后仍是 10；routed Modal 重建后仍在 modal 带、有 `ModalEscapeListener` 且 ESC → Back；
- ad-hoc：routed Modal 上弹的 dialog 严格更高；无路由时仍是 1000 / 1001；
- Page 挂 Modal 下 → `RouteException`；Tab / Prompt 挂 Modal 下不受影响。

PlayMode，`Tests/PlayMode/RouterCloseTransitionPlayTests.cs` 追加：

- Overlap 平级切换：新页 > 旧页幽灵 > 父页；
- `Back`：子页幽灵 > 父页。

## 11. 实施步骤（每步 red → green → `dotnet format` → 提交）

1. 本 spec；router spec §12、close-transition §5.5 各加一行指向本文。
2. Page 深度层序 + 旋钮 + 步长下限 + Loading 天花板警告（§4）。
3. Overlap 同深度新页在上（§5）。
4. 热重载重新装饰（§6）。
5. ad-hoc dialog 在 routed Modal 之上（§7）。
6. Page 挂 Modal 下报错（§8）。
7. 文档：C# skill（Router 层序小节、Canvas configuration 示例、「Modal Canvas + `UI.CanvasConfigurator`」、Exit animations、
   cheatsheet）与 `authoring-promptugui-xml/reference/glass.md`（玻璃盖住下层 Overlay UI 即完全遮挡 → 看到 UI 就是层序问题）。
