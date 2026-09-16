# `on="close"` / `reverse-on="close"` —— 退场动画与两阶段 Close

> 状态：**已定，实现中**（2026-09-16，作者同意 §10 全部推荐项；指示跳过 plan 直接实现、按 §12 分步提交，分支 `feat/close-transition`）。
> 需求来源：宿主工程反馈「退场动画无入口」—— 路由反激活是 `Reconcile` 里同步的 `UI.Close`（`Deactivate`），
> GameObject 当场销毁，没有 onExit、没有过渡钩子。
> 相关：
> `2026-05-14-litmotion-animations-design.md` §1.3（明确把「B. 离场动画：Close 反向播放需要改 `UI.Close` 加 await
> 等待动画结束，独立 PR」列为不做 —— 本文就是那个独立 PR）、
> `2026-08-31-hug-reveal-flip-checked-design.md` §2.3 / §2.4.5（`reverse-on=` 的事件语法与「从当前值倒放」—— `close`
> 直接接进去）、
> `2026-09-16-scrolllist-drag-reorder-design.md` REO-D14（补间用 unscaled time，「Animation 维持现状不改」——
> 本文 CLS-D8 推翻后半句）、
> PR #143（`Screen.HoldFirstTick`：开屏帧内调度的 motion 停一帧 —— 本文的收集机制与它对称，共用同一个交接点）。

## 1. 问题

一个 `<Screen>` 今天只有一种消失方式：`Screen.Close()` → `DetachGlobals` → 逐个 `Dispose` 控件（取消所有
LitMotion handle）→ `Destroy(root)`，全程同步。所有关闭入口都汇到它：`UI.Close` / `CloseModalScreen`（Modal 弹栈、
Toast、LoadingOverlay、Tutorial）/ `UnloadAll` / `ResetForTests` / 热重载 `ReloadAsync` / `PromptUGUIDocumentHost.OnDisable`。

作者能写 `<Animation on="open" type="slidein-left">` 让页面滑进来，却没有任何办法让它滑出去；`Router.Back()`
一按，上一页在当帧消失。库自己也被这件事逼出过一套私货：`ToastView` 手写了「淡出 → 再 `CloseModalScreen`」。

要的是：

1. XML 里声明退场，和入场同一套语法、同一个 `<Animation>`；
2. 关闭时库**等退场播完再销毁**，中间用户不能再操作那个屏幕；
3. Router / Modal 走同一条路，不需要宿主额外写 C#；
4. 没写退场的屏幕行为**一字不变**（仍是当帧销毁）。

## 2. 否决的方案

**A. C# 钩子：`Router.OnExit(screen) → Awaitable` / `UI.Close(name, exit: Func<Screen, Awaitable>)`。否决。**
不声明式，与库「XML 描述、C# 只订阅」的分工相悖；每个页面都得写一段 C# 才有退场；而 `<Trigger on="close">`
的 `OnFire` 本来就能当 C# 钩子用，`CloseAsync` 又给了「等它销毁」的等待点 —— 钩子从 B 里免费长出来。

**B. `<Screen exit="slideout-left">` 屏级属性。否决。** 只能动整屏一个节点，做不到「标题栏上滑、列表下滑」；
和 `<Animation>` 是两套时间与缓动参数；`reverse-on=` 已经把「入场倒放」表达得很自然。

**C. `close@<id>`：关闭某个子部分（TabMenu 弹窗、Collapsible 面板）。否决。** 那些是 `collapse` / `collapse@id`，
已有。`close` 只描述 Screen，和 `open` 对称（`TriggerSpec` 里那句注释：Not "open@" / "close@"）。

**D. 让 `UI.Close` 变成 `Awaitable`（签名破坏）。否决。** 绝大多数调用方不关心什么时候销毁完；改签名逼所有人
`await`，且 `Dispose()` 无法返回 Awaitable。保留 `void Close` 作「开始关闭」，另加 `CloseAsync`。

**E. 退场期间把幽灵屏 reparent 到一个「过渡层」Canvas。否决。** 坐标系 / 缩放 / `_dynamicSubtrees` 全部错位；
`blocksRaycasts=false` 已经足够让它不吃输入，画在原地就行。

**F. 只在 Router 里做（`Deactivate` 自己播）。否决。** `UI.Close` / Modal 弹栈是更常见的关闭入口，作者不会
只在 Router 页上写退场；机制应在 `Screen` 上，Router / Modal 只是调用方。

## 3. 方案总览

三个部件：

1. **事件**：`TriggerKind.Close`。`on="close"` 正向播（`fadeout` / `slideout-*` / `scaleout` 预设已在），
   `reverse-on="close"` 把入场倒放（§2.4.5：从当前值补到 from，入场半路被关也直接掉头）。
   `<Trigger on="close">` 是 C# 的 exit 钩子。
2. **两阶段 Close**：`Screen.Close()` = **Begin**（注销、断全局、封输入、发 `Closing`、收集退场 handle）→ 若无
   handle 则当帧走今天的销毁体；否则 **Finish**（等全部 handle 完成或取消 → 多等一帧 → 销毁体）。
   `CloseAsync()` 在销毁后完成。
3. **交接点**：`Animation` 在 fire / reverse 后把 `_current` 交给 `Screen.NotifyMotions(handles)`——opening 期
   hold 一帧（PR #143），closing 期收进等待集。一个入口，两种阶段。

时间线（`fade` 0.25s 的页面，`Router.Back()`）：

```
帧 N   Reconcile → Deactivate → UI.Close(key)
       ├ _open.Remove(key)；_closing.Add(screen)；IsClosing=true
       ├ DetachGlobals；root CanvasGroup.blocksRaycasts=false；选中若在幽灵内 → 清；TabMenu 展开着 → 收
       ├ Closing.OnNext → <Animation reverse-on="close"> Reverse() → 把 handle 交给 Screen
       └ UI.Close 返回；Reconcile 继续（Overlap：立刻 Activate 下一页）
帧 N+1…N+15   幽灵按 unscaled time 淡出；新页在它上面入场（HoldFirstTick 让入场也不吃建屏帧）
帧 N+16  最后一个 handle 完成 → await NextFrameAsync
帧 N+17  Finish：Dispose 控件 → Destroy(root) → CloseAsync 完成 → _closing.Remove
```

## 4. 作者面

### 4.1 XML

| 位置 | 新值 | 语义 |
|---|---|---|
| `<Trigger on=…>` / `<Animation on=…>` | `close` | Screen 开始关闭时触发一次。无 `@id` 形态 |
| `<Animation reverse-on=…>` | `close` | Screen 开始关闭时把动画倒放（从当前值到 from） |
| `<Show on=…>` | `close` | **报错**（同 `drop`：Show 是注册式可见性，关闭时改可见没有意义） |

```xml
<!-- 入场倒放：最常见 -->
<Animation on="open" reverse-on="close" translate="-32,0:0,0" fade="0:1" duration="0.25s">
  <VStack …/>
</Animation>

<!-- 独立退场：入场和退场不对称 -->
<Animation on="open" type="fadein" duration="0.2s"><Frame …/></Animation>
<Animation on="close" type="slideout-down" duration="0.3s"><Frame …/></Animation>

<!-- 模态整体淡出（backdrop 在模态 XML 里，包住即可） -->
<Animation on="open" reverse-on="close" fade="0:1" duration="0.15s">
  <Image id="backdrop" anchor="stretch" …/>
</Animation>
```

Screen 等的是 **`close` 事件触发的那些 `<Animation>`** 的 handle：`on="close"` 与 `reverse-on="close"`。
`on="loop"` 的循环、`Collapsible` / `TabMenu` 的内部过渡、`<Trigger on="close">` 都不在等待集里。

### 4.2 lint

| 代码 | 条件 | 级别 |
|---|---|---|
| `PUI-CLOSE-LOOP` | `on="close"` 且 `loop=` 是无限循环（`true` / `yoyo`，无次数） | error —— Screen 永远关不掉 |
| `PUI-REVERSE-LOOP`（已有） | `reverse-on=` 与 `loop=` 并用 | 不变，已覆盖 `reverse-on="close"` |
| `PUI-REVERSE-TEXT`（已有） | `reverse-on=` 与 `count` / `char-color` 并用 | 不变 |

`on="close"` + `count` / `char-color`（正向文本退场）合法，等待集照收。

### 4.3 C# 面

```csharp
UI.Close("Shop");                         // 开始关闭；有退场则幽灵多活 duration，无退场则当帧销毁（同今天）
await UI.CloseAsync("Shop");              // 销毁后完成；无退场 / 已关 → 已完成的 Awaitable
screen.IsClosing                          // Begin 之后、销毁之前为 true
screen.OnClosing                          // Observable<Unit>，Begin 时发一次（XML 之外的 exit 钩子）
screen.Dispose()                          // 立即销毁，不播退场（IDisposable 的语义）
UI.Get("Shop")                            // Begin 之后即 null
UI.Router.Transition = RouteTransition.Overlap;      // 默认：旧页退场与新页入场同时
UI.Router.Transition = RouteTransition.Sequential;   // 旧页销毁后再建新页
```

`IScreen` 增加 `bool IsClosing { get; }` / `Awaitable CloseAsync()` / `Observable<Unit> OnClosing { get; }`。

**契约变化（要进 SKILL）**：`UI.Close` 返回后，那个屏幕**仍可能回调你**（`OnClick` 不会——输入已封；但
`[Bind]` / `BindItems` 的数据订阅直到销毁才 `Dispose`）。屏幕引用的外部资源（RenderTexture、动态 Sprite、
自定义控件持有的句柄）要活过 `CloseAsync`。两者在没有退场动画的屏幕上与今天完全一样。

## 5. 语义细节

### 5.1 Begin

`Screen.Close()`：

1. `IsClosing` 已真 → 返回（幂等）。
2. 以下任一 → **直接走销毁体**（今天的 `Close`，改名 `CloseImmediate`）：`!Application.isPlaying`（EditMode 没有帧）、
   `IsOpening`（建屏帧内关闭：什么都还没画出来，播退场只是看不见的幽灵活 `duration`）、显式 `Dispose()` / 强制关闭（§5.3）。
3. `IsClosing = true`；`DetachGlobals()`（幽灵不再响应 theme / variant / resize）。
4. 封输入：root 上 `CanvasGroup`（无则加）`blocksRaycasts = false`。**不动 `interactable`**——那会让所有
   `Selectable` 进 Disabled，`StateTintReactor` 把正在淡出的按钮刷灰。
5. 焦点：`EventSystem.currentSelectedGameObject` 在 root 之下 → `SetSelectedGameObject(null)`；Navigation 启用时由
   其现有的补选逻辑接管。`TabMenu.s_expanded` 在 root 之下 → `Collapse()`（否则幽灵的菜单占着进程级静态，
   替它吃掉下一次 Escape）。
6. `Closing.OnNext(Unit)`。订阅者（`Trigger.SubscribeSpec` 的 `Close` 分支）同步 fire；`Animation` 在
   `OnTriggerFired` / `Reverse` 末尾 `NotifyMotions(_current)` → Screen 见 `IsClosing` 就把 handle 收进 `_exitMotions`。
7. `_exitMotions` 为空 → 销毁体（**零行为变化**的保证在这里）；否则 `_ = FinishAsync()`。

### 5.2 Finish

```
foreach h in _exitMotions: await h      // MotionAwaiter：complete 或 cancel 都放行；已死的 handle 直接跳过
await Awaitable.NextFrameAsync()        // 离开 LitMotion 的回调循环再销毁（见 §5.7）
if (_destroyed) return                   // 期间被强制关闭 / 外部销毁
CloseImmediate()                         // 今天的销毁体
```

`CloseImmediate` 末尾完成 `_closed`（`AwaitableCompletionSource`）并回调 `UI`（从 `_closing` 集合移除）。

### 5.3 立即路径（谁不播退场）

| 调用方 | 为什么 |
|---|---|
| `UI.UnloadAll` / `UI.ResetForTests` | 拆场；测试里幽灵会漏进下一个用例 |
| 热重载 `ReloadAsync`（`wasOpen → Close → Open` 同名） | 否则每次保存闪一段退场 + 入场，两个同名 root 并存 |
| `PromptUGUIDocumentHost.Clear`（`OnDisable` / `OnDestroy`） | 宿主自己正在没 |
| `Router.CancelAllForTeardown` / `Modal.CancelAllForTeardown` / Loading / Toast / Tutorial 的 teardown | 拆场 |
| `Reconcile` 里 epoch 变化后关掉的孤儿页 | teardown 的尾巴 |
| `Screen.Dispose()` | `IDisposable` = 立刻释放 |

其余全部走过渡：`UI.Close`、Modal 弹栈（按钮 / Escape）、`Modal.CloseAll`（导航时关掉 ad-hoc 模态 = 「用户先关弹窗
再导航」，该有退场）、Router `Deactivate`、Loading / Tutorial overlay 的正常关闭（它们的 XML 有没有写退场由作者定）。
Toast 保留自己的淡出（§9）。

强制关闭遇到进行中的 Finish：`CloseImmediate` **先置 `_destroyed`**，再取消 handle。LitMotion 的 `TryCancel`
**同步**调用 `OnCancelAction`，`MotionAwaiter` 的续体就挂在上面 —— 没有这个顺序，Finish 会在外层 `Dispose` 循环
里重入，把正在迭代的 `_nodeMap` 清掉。§5.2 的「多等一帧」把重入推到下一帧，`_destroyed` 检查让它成为 no-op。

### 5.4 `_open` / `_closing` / `OwnerScreenOf`

Begin 时 `UI` 把 Screen 从 `_open` 挪到 `_closing`（`HashSet<Screen>`）：

- `UI.Get(name)` 立即返回 null；`UI.Open(name)` 可立即建新实例（Router 快速来回、`Reset` 后再 `Open`），
  两个同名 root 短暂并存，`OwnerScreenOf` 按 root 身份区分。
- `UI.OwnerScreenOf` 先搜 `_open` 再搜 `_closing`：幽灵内的控件在退场期间仍能找到 owner（`count target=@id` 的
  `ResolveTextTarget`、`ToggleGroups`）。
- `CloseImmediate` 结束 / 外部销毁 → 从 `_closing` 移除。

### 5.5 Router

`Deactivate` 不变（调 `UI.Close`）。`Reconcile` 按 `UI.Router.Transition`：

- **Overlap（默认）**：`Deactivate` 全部返回后立刻 `Activate`。绘制先后只由层级顺序决定（新 root 后建 → 在上）：
  push（开子页）子页在父页上滑入 ✓；pop（`Back`）子页是幽灵、父页本来就开着且在下 → 子页在父页上滑出 ✓；
  平级切换 A→B：B 在 A 上入场、A 在下面淡出 —— push 感，可接受。库不改 `sortingOrder`（改了会压到模态上面）。
- **Sequential**：`Deactivate` 改为 `await UI.CloseAsync(key)`（逐个），全部销毁后再 `Activate`。导航慢一个
  `duration`，但没有两屏叠画的开销（移动端全屏 overdraw ×2 的场合）。
- `Router.Changed` 仍在 `Reconcile` 末尾发（Overlap 下幽灵可能还在）。
- `SameChain` 早退、Prompt / Tab 节点：与本文无关，不变。

### 5.6 Modal

- `RemoveSlot` → `CloseModalScreen(key)`（Begin）；`_stack` 立即弹、`PromoteWaiting` 立即开下一个（Overlap）。
  `Show*` 的结果在按钮回调里已经 resolve，**时机不变**（不让调用方等淡出）。
- 层序：`SortingOrderBase + CountModalsInChain()` 不计幽灵 → 新模态可能与退场中的旧模态同 `sortingOrder`，
  谁在上由层级顺序定（新的在上）。两层 `backdrop` 叠加 0.15s 会更暗一下 —— 作者在模态 XML 里调，库不介入。
- `PrevSelected` 还原已经把选中挪出幽灵；`ContainmentRoot` 已切到新栈顶。

### 5.7 时钟

`<Animation>` 的全部 motion 改用 `MotionScheduler.UpdateIgnoreTimeScale`（CLS-D8）。理由：暂停菜单是退场最典型
的场景 —— `timeScale = 0` 下点「继续」，若先 `Close` 再恢复 timeScale，scaled 时钟上的退场永远走不完，幽灵挂死。
Toast / Tutorial / Carousel / ReorderDriver 已经全是 unscaled，`<Animation>` 是唯一的例外；UI 本就不该随游戏
时间缩放。`HoldFirstTick` 基于 `PlaybackSpeed`，与时钟无关；仍在 `Update` 阶段，其「runner 先于脚本 tick」的前提
不变。`Collapsible` / `TabMenu` / `StateTintReactor` / `FocusCursorView` 的内部过渡本次不动（§11）。

「多等一帧」（§5.2）：LitMotion 在 runner 的回调循环里同步触发 `OnComplete`；销毁体会批量取消同一 storage 里的
其他 motion。跳出那个循环再销毁，一帧的代价在终态上看不见（`Destroy` 本来也延到帧末）。

### 5.8 外部销毁 / 退场中 Open 同名 / 重复关闭

- 退场中场景卸载：relay 的 `OnDestroy` → `OnRootDestroyedExternally`。今天它见 `_closing`（改名 `_destroying`）
  早退；改为：`IsClosing && !_destroyed` → 走外部销毁路径（只清引用、**不** `Dispose` —— 该方法注释里说明了原因）、
  置 `_destroyed`、完成 `_closed`、从 `_closing` 移除。Finish 醒来见 `_destroyed` 返回。
- 退场中 `UI.Open` 同名：新实例独立；幽灵继续到销毁。用户持有的旧 `Screen` 引用在销毁后 `Get` 抛
  `KeyNotFoundException`（与今天 Close 后相同）。
- 对同一个 Screen 再 `Close()`：no-op；`UI.Close(name)` 找不到（已不在 `_open`）：no-op。
- 在 `OnClosing` / `<Trigger on="close">` 回调里导航：重入 `Reconcile` 由现有 `_reconciling` / `_pending` 兜住。

### 5.9 与 `HoldFirstTick` 的关系

`NotifyMotions(handles)`：`IsOpening` → hold（PR #143 的 `HoldFirstTick` 改名并入）；`IsClosing` → 收集；否则
no-op。`Animation.OnTriggerFired` / `Reverse` 只调这一个。Open 期间关闭走立即路径（§5.1-2），两者不会同时为真。

## 6. 实现地图

| 文件 | 改动 |
|---|---|
| `Runtime/Controls/Internal/TriggerSpec.cs` | `TriggerKind.Close`；`case "close"`；错误信息清单补上；`ParseReverseOn` 不变（只排斥 open / loop） |
| `Runtime/Controls/Trigger.cs` | `SubscribeSpec` 加 `Close` 分支：`UI.OwnerScreenOf(this)?.OnClosing.Subscribe(_ => onFire())`（owner 为 null → 抛，同 `count target=@id` 的措辞） |
| `Runtime/Controls/Show.cs` | `close` 报错（同 `drop`） |
| `Runtime/Controls/Animation.cs` | `HoldFirstTick` 调用改 `NotifyMotions` |
| `Runtime/Controls/Internal/AnimationDriver.cs` | 每个 `LMotion.Create` 加 `.WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)`（收成一个 `Schedule()` 小 helper） |
| `Runtime/Application/Screen.cs` | `_closing` bool 改名 `_destroying`；新增 `IsClosing` / `OnClosing` / `CloseAsync` / `CloseImmediate` / `NotifyMotions` / `_exitMotions` / `_closed` / `_destroyed` / `FinishAsync`；`Close()` 重写为 Begin；`OnRootDestroyedExternally` 按 §5.8；`Dispose()` → `CloseImmediate()`；root CanvasGroup / 焦点 / TabMenu 收起 |
| `Runtime/Application/UI.cs` | `_closing` 集合；`Close` 挪集合；`CloseAsync`；内部 `CloseImmediate(name)`；`OwnerScreenOf` 两级搜索；`UnloadAll` / `ResetForTests` / `ReloadAsync` / `CloseModalScreen` 的立即变体 |
| `Runtime/Application/UI.Router*.cs` | `RouteTransition` 枚举与 `Transition` 属性；`Reconcile` 的 Sequential 分支；孤儿页立即关；`ResetForTestsInternal` 复位 `Transition` |
| `Runtime/Application/UI.Modal.cs` 等 | `CancelAllForTeardown` 用立即变体；其余不变 |
| `Runtime/Application/PromptUGUIDocumentHost.cs` | `Clear` 用立即变体 |
| `Runtime/Controls/TabMenu.cs` | `internal static void CollapseIfUnder(Transform root)` |
| `Runtime/Core/Lint/AnimationRules.cs` | `PUI-CLOSE-LOOP`；`ScreenInstantiator` 镜像 |
| `Editor/XsdGenerator.cs` | 无改动（`on=` 是自由字符串） |

`Core/Lint` 保持纯 C#。

## 7. 测试（Red 先行）

EditMode（`Tests/EditMode/Controls/CloseTriggerTests.cs` + `Tests/EditMode/Lint/AnimationRulesTests.cs`）：

1. `TriggerSpec.Parse("close")` → `Close`；`ParseReverseOn("close")` 合法；`"close@x"` 抛。
2. `<Show on="close">` 抛。
3. EditMode 下带 `reverse-on="close"` 的屏 `UI.Close` → `RootGameObject == null` 当帧（立即路径）。
4. `PUI-CLOSE-LOOP` 一正一反；`on="close" loop="3"` 不报。

PlayMode（`Tests/PlayMode/Lifecycle/CloseTransitionPlayTests.cs`；linear 1s fade，alpha 即秒数）：

5. `reverse-on="close"`：入场播完后 `UI.Close` → 当帧 `UI.Get == null`、`IsClosing`、root 仍在、`Close` 未 yield；
   之后 alpha 逐帧下降；`duration` + 2 帧后 root 为 null、`IsClosing == false`。
6. `on="close" type="fadeout"` 正向同上。
7. `CloseAsync` 在 root 销毁之后完成；无退场的屏 `CloseAsync` 立即完成。
8. **无退场的屏 `UI.Close` 当帧 `RootGameObject == null`**（零变化守护）。
9. `<Trigger on="close">` 的 `OnFire` 恰好一次、在 `Close` 返回前；不延长等待。
10. 幽灵不吃射线：`EventSystem.RaycastAll` 打在幽灵按钮上返回空 / 其下另一屏的 `Btn` 收到点击。
11. 入场半路 `Close` → alpha 从中间值往下走、不跳。
12. 建屏帧内 `Close` → 立即销毁。
13. 退场中 `UI.Open` 同名 → 新实例 `UI.Get` 可得、独立入场；幽灵按时销毁；无错误日志。
14. 退场中 `UI.ResetForTests` / `UnloadAll` → 当帧全销毁、`CloseAsync` 等待者完成、下一帧无错误（重入守卫）。
15. 退场中 `ReloadAsync`（热重载）→ 旧实例立即销毁、新实例正常。
16. 退场中场景内 `Destroy(root)`（外部销毁）→ 无错误、`CloseAsync` 完成、`_closing` 清空。
17. `Time.timeScale = 0` 下退场照常完成。
18. `Dispose()` 立即销毁。
19. 幽灵内展开的 `TabMenu` 在 Begin 时收起、`HasExpandedMenu == false`；幽灵内的选中被清。
20. Router：`Open(child)` → `Back()`：子页幽灵淡出、父页仍在、`Router.Changed` 在退场结束前已发；
    `Transition = Sequential` 时 `Open(sibling)` 的新页在旧页销毁后才 `Open`。
21. Modal：带退场的 MessageBox，点按钮 → `Show` 结果当帧 resolve、幽灵淡出、队列里下一个模态立即提升。
22. `on="close"` + `count`：文本退场被等待。

## 8. SKILL / 文档更新（同一 PR 内，英文）

| 文件 | 改动 |
|---|---|
| `authoring-promptugui-xml/reference/animations.md` | `on=` 表加 `close` 行；`reverse-on` 段加 `close`（推荐写法 + 模态 backdrop 例子）；Caveats：等待集定义、`PUI-CLOSE-LOOP`、`<Animation>` 走 unscaled time |
| `authoring-promptugui-xml/SKILL.md` | `<Trigger>` / `<Animation>` 速查处一行指向 `reference/animations.md` 的 close |
| `scripting-promptugui-csharp/SKILL.md` | `UI.Close` 那行改写（开始关闭 / 有退场则延迟销毁）；新增 `CloseAsync` / `IsClosing` / `OnClosing` / `Dispose` 立即；**契约**段（订阅与资源要活过 `CloseAsync`）；Router 段加 `Transition`；`<Trigger on="close">` 作 exit hook 的例子 |
| `CLAUDE.md` | Critical Conventions 加一条：Close 两阶段、哪些入口必须走 `CloseImmediate` |
| 主 spec `2026-05-07-…-design.md` | §9.6 附注：`Close` 语义与 `CloseAsync` |

## 9. 非目标

- Toast 迁到 `<Animation>` 退场（它有自己的 hold / 分组 reflow 时序，另开）。
- 「旧页在新页之上/之下」的显式控制（`<Screen exit-above>` 之类）；v1 只有层级顺序的默认行为（§5.5）。
- 取消已捕获的拖拽（幽灵内正在 reorder / 拖 Carousel 的手势会继续收到 `OnDrag` 直到松手）—— 记入 SKILL caveat。
- `Collapsible` / `TabMenu` / `StateTintReactor` / `FocusCursorView` 切 unscaled time（§5.7）。
- 退场期间响应 theme / variant / resize。
- 共享元素过渡（hero transition）。
- 退场的最长等待 / 超时 —— 由 `PUI-CLOSE-LOOP` 与 unscaled 时钟保证有限。

## 10. 已定的决策（2026-09-16 与作者对齐；★ 为采纳项）

| # | 决策 | 选项 | 理由 |
|---|---|---|---|
| CLS-D1 | `close` 是 Screen 事件，`on=` / `reverse-on=` 都收，无 `@id` | ★ 如此 | 与 `open` 对称；子部分用 `collapse` |
| CLS-D2 | `UI.Close` 保持 `void` = Begin；新增 `CloseAsync` | ★ 如此 | 不破坏签名；多数调用方不关心销毁时机 |
| CLS-D3 | 无退场 / EditMode / `IsOpening` → 当帧销毁 | ★ 如此 | 零回归 |
| CLS-D4 | Begin 即从 `_open` 注销，加 `_closing` 集合 | ★ 如此 | 同名可立即重开；幽灵内控件仍能找 owner |
| CLS-D5 | 封输入只用 `blocksRaycasts=false`，不动 `interactable` | ★ 如此 | 避免退场变灰 |
| CLS-D6 | `Dispose()` 立即，`Close()` 过渡 | ★ 如此 | `IDisposable` 语义 |
| CLS-D7 | Router 默认 **Overlap**，提供 `Sequential` | ★ Overlap / Sequential | 主流手感；层级顺序天然给出 push / pop 的正确叠放 |
| CLS-D8 | `<Animation>` 全部 motion 改 unscaled time（推翻 REO-D14 的「Animation 维持现状」） | ★ 改 / 只改 close 相关（做不到：一个 Animation 一套时钟）/ 不改 | 暂停菜单退场；库内其余早已 unscaled |
| CLS-D9 | Finish 在最后一个 handle 后多等一帧 | ★ 如此 | 离开 LitMotion 回调循环；重入变成 no-op |
| CLS-D10 | `Modal.CloseAll`（导航时）播退场；teardown 不播 | ★ 如此 | 「用户先关弹窗再导航」 |
| CLS-D11 | 层序不为幽灵调整 | ★ 如此 | 改了会压模态；默认顺序已覆盖 push / pop |
| CLS-D12 | 等待集只含 `close` 事件触发的 `<Animation>` | ★ 如此 | `loop` / 内部过渡不该拖住关闭 |

## 11. 开放问题（留给 plan / 实现期）

1. `UI.Navigation` 在选中被清后的补选时机：是否需要在 Begin 里显式触发一次，还是它的 tick 已经足够 —— 实现时看
   `UI.Navigation.cs` 第 130–146 行那段的触发条件。
2. Sequential 模式下 `Reconcile` 的 `epoch` 检查要不要在每个 `await CloseAsync` 之后重做（teardown 发生在退场
   等待中）—— 倾向做，成本一行。
3. `OnClosing` 放在 `IScreen` 还是只在 `Screen`：`IScreen` 今天没有任何 Observable 成员，`R3` 类型出现在接口上
   算不算扩大依赖面 —— 倾向放 `IScreen`，`UI.Get` 返回的就是 `Screen`，接口对齐只是为了 mock。
4. 幽灵内 `BindItems` 的数据推送是否要在 Begin 后静默丢弃（省掉退场期间的行重建）—— 倾向不做，退场只有十几帧。

## 12. 里程碑拆分

一个 PR，三步提交：

- **M0 两阶段 Close + 立即路径**：`Screen` / `UI` 的状态机、`CloseAsync`、`_closing` 集合、所有立即调用方、
  外部销毁、重入守卫；测试 7 / 8 / 12 / 14 / 15 / 16 / 18（此时还没有任何触发能产生退场，用一个测试专用的
  `NotifyMotions` 直灌 handle 做红绿）。
- **M1 `close` 事件 + 时钟**：`TriggerKind.Close` / `Trigger` / `Show` / `Animation.NotifyMotions` / unscaled /
  封输入 / 焦点 / TabMenu / lint；测试 1–6 / 9–11 / 13 / 17 / 19 / 22。
- **M2 Router / Modal + 文档**：`Transition` / Sequential / Modal 路径；测试 20 / 21；三份 SKILL + CLAUDE.md +
  主 spec 附注；`Samples~` 里给一个页面加 `reverse-on="close"`。

## 13. 风险与缓解

| 风险 | 缓解 | 落点 |
|---|---|---|
| 调用方在 `Close` 后立刻 `Open` 同名（热重载、刷新逻辑） | 热重载走立即；用户侧写进 SKILL；`_closing` 集合让两实例并存不串 | §5.3 / §5.4 |
| `Close` 后立刻释放屏幕引用的资源 / Dispose 数据源 | SKILL 契约：活过 `CloseAsync`；无退场的屏不受影响 | §4.3 |
| `CloseImmediate` 取消 handle 时同步重入 Finish | `_destroyed` 先置位 + 多等一帧 | §5.2 / §5.3 |
| 幽灵仍能被键盘 / 手柄操作 | 清选中、TabMenu 收起 | §5.1 |
| 幽灵按钮退场变灰 | 不动 `interactable` | §5.1 |
| 已捕获的拖拽继续投递到幽灵 | 接受；SKILL caveat | §9 |
| `timeScale = 0` 下退场挂死 | unscaled 时钟 | §5.7 |
| 两模态同 `sortingOrder`、双 backdrop | 层级顺序决定；作者在 XML 里调 | §5.6 |
| 全屏叠画的移动端开销 | `Sequential` 选项 | §5.5 |
| 测试间幽灵泄漏 | `ResetForTests` 立即路径 + 测试 14 | §5.3 |
| 退场中场景卸载 | `OnRootDestroyedExternally` 分支 + 测试 16 | §5.8 |
| Router 的 `await CloseAsync` 被 teardown 挂死 | 立即路径也完成 `_closed` | §5.3 |

## 14. 实施记录（2026-09-16，分支 `feat/close-transition`，三步提交 M0 / M1 / M2）

### 14.1 与设计的偏差

- **可逆入场静息在 `from`（新增，§5 未预见）。** M1 的 PlayMode 测试暴露：`<Animation on="open" reverse-on="close" fade="0:1">`
  在 open 时**根本不淡入**——§2.4.5 让 `reverse-on=` 动画的正向从「当前值」起，而 open 时 CanvasGroup 的当前值是 1，
  于是 1 → 1。这正是 §13 之外、上一轮调研标为「独立跟进」的隐患，但它打在本特性的招牌写法上，必须一起修。
  收窄修法：`Animation.OnAfterApply` **只对 `on="open"` 的可逆动画**用 `WriteEndState(reverse: true)` 建立 `from`
  静息态（一次，ReSolve 不重建——open 不会重发，重写会把已定的元素弹回 from）。不推广到所有可逆动画：
  `<Collapsible>` 里 `on="expand" reverse-on="collapse" translate="-12,0:0,0"` 的行在默认展开的面板里会永远卡在 -12
  （open 时不派发 expand）。
- **`OnClosing` 在所有关闭路径都发一次**（含 `Dispose` / teardown），不只是过渡路径：C# exit 钩子的契约是「Screen 开始
  关闭时通知一次」，与走哪条销毁路径无关。`UnloadAll` / `ResetForTests` 因此改为迭代快照——钩子里开关 Screen 不会撞
  正在迭代的字典。
- **`_closing` 布尔改名为 `_destroyed`** 并统一成一个标志：`CloseImmediate` 置位后再销毁 root，relay 的 `OnDestroy`
  据此早退；外部销毁路径也置位并完成 `CloseAsync` 等待者。
- **Finish 按本地引用逐次检查 `_destroyed`**，销毁路径不再置空 `_exitMotions`：测试里一条没 `.AddTo(root)` 的 motion
  在外部销毁后继续跑到 0.5s，唤醒时列表已空 → NRE。真实 `<Animation>` 都挂 `.AddTo(go)`，但 Finish 不该依赖这一点。
- **`Screen.IsOpening` 期间 Close → 立即** 的分支没有测试：没有一个内建控件能在 apply pass 里调用 `UI.Close`，
  写测试要注册带 prefab 的自定义控件，收益不值。代码是一行 `if (!isPlaying || IsOpening) CloseImmediate()`。
- `UI.CloseAsync` 对同一 Screen 的多次调用各自拿到独立的 `AwaitableCompletionSource`：Unity 的 `Awaitable` 是池化对象，
  只能被 await 一次，不能共享。

### 14.2 测试里发现并记录的既有事实

- Unity 的 `Object.Destroy` 延到帧末：`CloseImmediate` 返回后 `screen.RootGameObject` 已为 null，但测试持有的 root
  引用要到下一帧才 fake-null。断言应看 `screen.RootGameObject`。
- PlayMode 测试 asmdef 现在引用 `LitMotion`（直接造 handle 灌进 `NotifyMotions`）和 `Unity.Addressables`
  （`UI.ReloadAsync` 有 `AssetReferenceT<>` 重载，缺引用时编译器无法决议）——与 EditMode asmdef 对齐。
- `MessageBox.Open` 默认 `ModalMode.Popup` 是**叠放**，`Queued` 才排队；「队列里下一个模态立即提升」的测试要显式 `Queued`。

### 14.3 开放问题的落地

1. `UI.Navigation` 补选：Begin 只清幽灵内的选中；Navigation 现有逻辑接管，没有额外触发。测试 19 只断言「选中被清」。
2. Sequential 的 `epoch` 检查：每个 `await UI.CloseAsync` 之后都做（一行）。
3. `OnClosing` 放 `IScreen`（`Observable<Unit>`），`IsClosing` / `CloseAsync` 同。
4. 幽灵内 `BindItems` 推送不拦截。

### 14.4 内建模态（2026-09-16，作者决定）

五个内建模态 XML（`MessageBox` / `InputBox` / `MarkdownBox` / `CenteredSlideBox` / `Loading`）各包一层
`<Animation anchor="stretch" on="open" reverse-on="close" fade="0:1" duration="0.15s" easing="out-quad">`——
作为最基础的演示：整体淡入，关闭时倒放。结果仍在点击时 resolve、栈立即弹出（§5.6），只是对话框多活 0.15s。
Toast 保留自己的淡出（§9）。`Samples~/CommonControls` 另有 `ExitDemo` 屏演示滑入 + 淡入的倒放。
既有 PlayMode 模态测试全部不受影响（它们看的是 `TopScreen` / 结果 / `UI.Get`，都在 Begin 时就变了）。

### 14.5 未做

- §9 的非目标全部维持。
