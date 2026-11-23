# 关闭的可靠性：直接关闭、`OnClosing` 订阅、对话框的 token 取消、过期的路由激活

> 状态：**已实现**（2026-11-23，实施记录见 §11）。决策已对齐：§7 的 D1…D5 全部按推荐。分支 `fix/close-reliability`，叠在 `feat/ui-sfx`（PR #186）上；
> plan `docs~/superpowers/plans/2026-11-23-close-reliability.md`。
> 来源：`2026-11-23-ui-sfx-cues-design.md` §15 在调研中静态读到的既有问题 #6 / #7 / #8 / #13；之后逐条核实（静态阅读 +
> 临时 PlayMode 探针），结论见 §1。共同点：**「关闭」在某条路径上不可靠** —— 关了还登记着、关的时候订阅先没了、在错的线程上关、
> 不该开的页先开再关。
> 相关：`2026-09-16-close-transition-design.md`（两段式关闭：§4.3 C# 面、§5.1 Begin）、
> `2026-05-14-messagebox-modal-design.md` / `2026-05-20-modal-layering-design.md`（对话框栈）、
> `2026-06-09-router-navigation-design.md` / `2026-10-01-router-page-layering-design.md`（Reconcile、epoch、孤儿清理）。

## 1. 问题（都已核实）

| # | 现象 | 核实 | 后果 |
|---|---|---|---|
| A1 | Play 模式的两段式关闭里，`screen.OnClosing.Subscribe(…).AddTo(screen)` 永远收不到 | PlayMode 探针：有退场、无退场都是 **0 次**；同一时刻的裸订阅收到 1 次 | 存档、埋点、音效这类退场钩子在构建里静默失效，EditMode 测试全绿看不出来。同样失效：C# 对 `<Trigger on="close">` 写的 `OnFire…AddTo(screen)` |
| A2 | `screen.Close()` / `CloseAsync()` / `Dispose()` 不注销 `UI` 的登记 | 探针：之后 `UI.Open(name)` 返回**同一个死实例**（根已销毁、`UI.Get` 仍非 null），`Get<T>` 抛 `KeyNotFoundException` | 「第二次打开面板没反应」+ 一条误导性的异常；`UI.Get` 失真；之后的 `.AddTo(screen)` 进了已 detach 的列表，永不释放；热重载会把已经关掉的面板重新打开 |
| A3 | 对话框的 Screen 被直接关掉（`configure` 里拿到的 `IScreen` 上调 `Close()` / `Dispose()`） | 静态 | 对话框栈的槽位残留：`await` 永不结算，`IsAnyOpen` 仍为 true，导航围栏、Esc 监听还指着死掉的 Screen，排队的对话框不再被提升 |
| A4 | 对话框的 `CancellationToken` 在线程池里被取消（`CancelAfter`、带时长构造的 CTS、与网络库联动的 token） | 探针：`await` 之后的续体跑在**线程池线程**；对话框出了栈、从 `_open` 摘掉了，但根节点**一直活着**、卡在 `_closing`，没有任何报错 | 画面上留下一个关不掉的对话框；游戏代码在主线程之外继续跑，碰到 Unity API 就出错；registration 从不 Dispose，长寿 token 上越积越多 |
| A5 | Router 首次**异步**加载某条路由期间，发生 `Router.Reset()` / `UI.UnloadAll()`（skill 教的重连流程） | 静态 | 过期的续体照样 `OpenCore` → `ApplyRouteLayer` → `OnEnter`，回到 Reconcile 才被当孤儿 `CloseImmediate`：`OnEnter` 的副作用漏出（请求、埋点、弹窗、重定向，闭包可能还指向重连前的服务）；`DismissBelow` 关掉新页上玩家开着的弹层；手柄焦点被抢到孤儿页上、孤儿销毁后焦点丢失；`OnEnter` 抛异常时孤儿页**残留可见** |
| A6 | 同一场景的两个旁支：① `Reset` 后马上再导航到同一条仍在首次加载的路由 → 两次并发 `LoadDocumentAsync(src)`，后完成的抛 `already loaded`；② `UnloadAll` 期间还在跑的加载完成后，把文档写回 `_docs`、把 src 写回 Router 的 `_loadedSrcs` | 静态推断，**未验证** | 重连后的第一次导航失败 |

不是问题的：同一个 Pump 里的连点、`Back` 紧跟 `Navigate` 都不改 epoch —— 那是 router spec 写明的 latest-wins，前一个目标被完整
激活再正常撤下；`UseResourcesResolver` 是同步的，没有 A5 的窗口。

## 2. 订阅活到销毁（A1）

`Screen.Close()` 的 Begin 是 `_isClosing = true` → `DetachGlobals()` → `SealInput()` → `FireClosing()`。`DetachGlobals` 除了断开
Variant / 主题 / 尺寸这三个进程级事件，还释放了 `_subscriptions`（`.AddTo(screen)` 挂进去的全部订阅），于是 `OnClosing` 发出时它的
`.AddTo(screen)` 订阅者已经不在了。销毁体 `CloseImmediate()` 的顺序反过来（先 `FireClosing` 再 `DetachGlobals`），所以 EditMode、
`IsOpening`、`Dispose`、拆除都收得到 —— **正常关闭收不到、拆除反而收得到**。

设计意图与文档都站在「活到销毁」这一边：

- close-transition spec §5.1 第 3 步写的是「`DetachGlobals()`（幽灵不再响应 theme / variant / resize）」，没打算提前释放用户的订阅；
- C# skill 明写「Subscriptions inside the Screen stay alive until it is destroyed … disposed at the end of the exit, not when
  `UI.Close` returns; pointer input cannot reach the ghost, but a data source you push to will still update it」。

**改法**：拆开 `DetachGlobals`。

- Begin 只断开进程级事件（Variant 订阅、`Theme.Changed`、`RectTransformDimensionsChanged`）——幽灵照旧不再 ReSolve；
- `_subscriptions` 在销毁体里、`FireClosing()` 之后释放（拆除、EditMode 的顺序不变）。

效果：`OnClosing` 与 `<Trigger on="close">` 的 `OnFire` 的 `.AddTo(screen)` 订阅者都收得到；退场期间推送的数据照旧更新幽灵（文档
说的样子）；指针早已被封，按钮不会被点到。备选见 D1。

## 3. 直接关闭也注销（A2、A3）

**一个钩子，所有路径。** `Screen` 现有的 `OnDetachedExternally` 只在「根被外部销毁」时调用，换成一个统一的「离开打开态」钩子，每个
Screen 恰好调用一次：

- `Close()`：过了幂等检查之后、走 EditMode / `IsOpening` 捷径之前（Begin 的第一件事）；
- `CloseImmediate()`：置 `_destroyed` 之后、`CompleteClosed()` 之前；
- 根被外部销毁：原处。

`UI` 在 `OpenCore` / `OpenModalScreen` 里设置它：`RemoveFromOpenIfSame(key, s)` 摘得掉（说明这次关闭不是经 `UI` 发起的）就
`TrackClosing(s)`，让退场中的幽灵留在 `_closing` 里，`OwnerScreenOf` 照样找得到它（音效门、拖放判定靠这个）；销毁体结束时
`OnClosed` 把它摘出 `_closing`，和今天 `UI.Close` 的路径一致。`UI.Close` / `CloseAsync` / `CloseImmediate(name)` 自己先摘
`_open`，钩子对它们是空操作；拆除遍历的是快照，安全。

**对话框（A3）**：`OpenModalScreen` 的钩子在「摘得掉」时再通知对话框栈：找到持有这个 Screen 的槽位，按 `CloseAll` 的语义结算
（`entry.Cancel(OperationCanceledException)`，见 D2），移除槽位 —— 不再关一次 Screen —— 然后走 `RemoveSlot` 的其余收尾：恢复导航
围栏与先前的选中、`RefreshTopListener`、`PromoteWaiting`。对话框自己的关闭路径（按钮、Esc、`CancelEntry`、`CloseAll`）都先经
`UI.Close` 摘掉 `_open`，钩子摘不到，不会重复结算。

**路由的页**：Router 的链按 `ScreenKey` 记着它，直接关掉之后 `UI` 侧干净了，Router 仍以为这一页还在（再导航到同一条链会被
`SameChain` 早退）。本 PR 不去同步 Router 的状态，只在这种关闭时警告一次，指向 `UI.Router.Back()` / `Open()`（D3）。

## 4. 对话框的 token 一律在主线程结算（A4）

唯一的注册点是 `UI.Modal.OpenAsync` 的 `ct.Register(() => CancelEntry(…))`：MessageBox / InputBox / MarkdownBox /
CenteredSlideBox / 自定义请求都汇到这里。回调跑在取消它的那条线程上。

**改法**：

- `OpenAsync` 记下调用时的 `SynchronizationContext.Current`（主线程上是 Unity 的同步上下文）；
- 回调里：当前就在那个上下文上（或者记下的是 null）→ 原地 `CancelEntry`，主线程上的取消仍然**同步**关闭（`ModalCancelTests`
  钉住的现状不变）；否则 `Post` 过去，在下一帧的同步上下文泵里、主线程上结算。不阻塞取消它的那条线程；
- registration 存进 entry，entry 以任何方式结算时 Dispose，长寿 token 不再越积越多。`Post` 不阻塞，所以主线程上的 Dispose 不会
  等一个卡住的回调（`Register(cb, useSynchronizationContext: true)` 用的是会阻塞的 `Send`，与 Dispose 放在一起会死锁，见 D4）。

不用 `Task`、不开线程；WebGL 只有一条线程，永远走原地分支。`await` 对话框的游戏代码因此也在主线程上恢复（异常是在那里设置的）。
在主线程之外**调用** `Open` 本身仍不支持（和任何 Unity API 一样），不改。

## 5. 过期的路由激活什么都不开（A5、A6）

**A5**：Reconcile 把自己开始时的 epoch 传给 `Activate` / `ActivatePage`，而不是在 `ActivatePage` 里再读一次；
`await EnsureLoaded` 之后 epoch 变了就返回 null —— 不 `OpenCore`、不上层、不 `OnEnter`。Reconcile 见到 null 直接返回，不再需要
`CloseImmediate` 孤儿。保留现有的事后检查，应对 `OnEnter` 里同步调 `Reset` 的情况。`ActivatePage` 里只管音效的那个 epoch 判断随之
多余，删掉（开页 cue 照旧在上层之后、`OnEnter` 之前）。

- `RoutePresent.Modal` 走同一个 `ActivatePage`，一并覆盖；guards 在 `Open()` 里同步检查，不受影响；Sequential 模式的
  `await UI.CloseAsync` 之后已经自带检查。
- 公开行为的变化：被 `Reset` / `UnloadAll` 作废的导航，不再调用它的 `OnEnter`。

**A6（Red 测试复现了才修，D5）**：

- ① Router 的 `EnsureLoaded` 对**进行中**的同一 src 去重：后来者等前一个加载的结果，而不是再发一次 `LoadDocumentAsync`。
  `Awaitable` 只能被等一次，所以每个后来者一个 `AwaitableCompletionSource`；
- ② `UI.UnloadAll` / `ResetForTests` 推进一个加载代数；`LoadDocumentAsync` 开始时记下它，取源 / 合并之后发现代数变了 →
  抛 `OperationCanceledException`，不碰 `_docs` / `_depGraph` / 主题。Router 过期的 Reconcile 吞掉它（本来就过期了），`EnsureLoaded`
  也就不会把 src 写回 `_loadedSrcs`。

## 6. 测试（Red 先行）

| 项 | 层 | 用例 |
|---|---|---|
| A1 | PlayMode | `OnClosing…AddTo(screen)` 加 `UI.Close`：无退场、有退场各收到 1 次；`<Trigger id='t' on='close'/>` 的 `OnFire…AddTo(screen)` 收到 1 次；退场期间推送给 `.AddTo(screen)` 订阅的 Subject 仍被处理；销毁之后不再被处理（已释放）|
| A1 | PlayMode | 现有 `ScreenLifecycleTests.AddTo_screen_disposes_on_close` 保持绿（无退场：同一帧销毁，订阅随之释放）|
| A2 | EditMode | `screen.Close()` / `CloseAsync()` / `Dispose()` 各一条：之后 `UI.Get(name)` 为 null，`UI.Open(name)` 得到**新的**活实例 |
| A2 | PlayMode | 有退场的 `screen.Close()`：退场期间 `UI.Get` 为 null、`ClosingCountForTests` 为 1、幽灵里的控件 `OwnerScreenOf` 找得到它；结束后为 0 |
| A3 | EditMode | `configure` 里拿到对话框的 `IScreen`，直接 `Close()` / `Dispose()`：`await` 抛 `OperationCanceledException`，`IsAnyOpen` 为 false，排队的下一个对话框被提升 |
| A3 | EditMode | 路由的页被直接关掉：警告一次 |
| A4 | PlayMode | `cts.CancelAfter(20)`，等几帧：根已销毁、`ClosingCountForTests` 为 0、`IsAnyOpen` 为 false、续体的 `ManagedThreadId` 等于主线程；`new Thread(cts.Cancel)` 再来一条（线程只在测试代码里）|
| A4 | EditMode | 现有「主线程上取消同步关闭」的用例保持绿；同一个长寿 CTS 开三个对话框、都结算之后再 `Cancel`，不再有回调 |
| A5 | EditMode | `AwaitableCompletionSource` 做加载闸门（`DocumentCacheTests` 的做法）：`Router.Open("shop")` → `Router.Reset()` → 放闸：`OnEnter` 0 次、`UI.Get` 为 null、新页上开着的弹层仍开着；`UnloadAll` 版同样 |
| A6 | EditMode | 闸门：`Reset` → 再导航到同一条路由 → 放闸 → 第二次导航成功、只有一次加载；`UnloadAll` → 放闸 → `_docs` 里没有它，之后的导航正常加载 |

## 7. 待确认的决策

| # | 问题 | 推荐 | 备选 |
|---|---|---|---|
| D1 | A1：`.AddTo(screen)` 订阅什么时候释放 | **销毁体里、`FireClosing` 之后**：兑现 C# skill 与 close-transition §4.3 的契约，文档不用改 | Begin 里 `FireClosing` 之后立刻释放：时机变化最小，但要改 C# skill 的那段说法（推送不再更新幽灵）|
| D2 | A3：对话框被直接关掉，`await` 怎么结算 | **抛 `OperationCanceledException`**，同 `CloseAll` / token 取消 | 按「关闭按钮」的结果结算（每种对话框不同，且自定义请求未必有）；或只注销、不结算（栈修好了，`await` 仍挂着）|
| D3 | 路由的页被直接关掉 | **警告一次**，指向 `Router.Back()` / `Open()` | 什么都不做；或替用户 `Back()`（猜测意图，路由链变化出乎意料）|
| D4 | A4：怎么切回主线程 | **记下同步上下文，不在上面就 `Post`** | `Register(cb, useSynchronizationContext: true)`：1 行，但池线程上的 `Send` 会阻塞到下一帧，配合 Dispose 会死锁，只能放弃 Dispose；`Awaitable.MainThreadAsync()`：Unity 2022 的兼容层里没有 |
| D5 | A6 | **Red 测试复现了才修**，两个旁支分开判断 | 不进本 PR |

## 8. 文档同步（同一 PR，英文）

- `scripting-promptugui-csharp/SKILL.md`：
  - **Open / Close / Get**：`screen.Close()` / `Dispose()` 同 `UI.Close` 一样注销 —— 之后 `UI.Get` 为 null，下一次 `Open` 新建；路由
    的页和对话框请经它们的主人关闭（`Router.Back()`、对话框自己的按钮 / `close`），直接关掉对话框会让它的 `await` 抛
    `OperationCanceledException`；
  - 两段式关闭那段：补一句 `OnClosing` 与 close Trigger 的 `OnFire` 可以 `.AddTo(screen)`；
  - **CancellationToken on modal Open helpers**：token 可以在任何线程取消，对话框一律在主线程关闭（在别的线程取消时推迟到下一帧），
    `await` 它的代码在主线程恢复；
  - **Router**：被 `Reset` / `UnloadAll` 作废的导航不再调用 `OnEnter`。
- close-transition spec §5.1 第 3 步加一句更正，指向本文 §2。
- 本文 §11 实施记录。

## 9. 非目标

- 路由的页被直接关掉时同步 Router 的链（只警告）。
- 游戏代码自己在主线程之外调用 UI API。
- 改变 `Dispose()` / `CloseImmediate` 「立即销毁、不播退场」的语义。

## 10. 里程碑（一个 PR，分步提交）

1. docs：spec + plan。
2. A1：订阅活到销毁。
3. A2 + A3：统一的离开打开态钩子；对话框被直接关掉时结算；路由页的警告。
4. A4：token 在主线程结算；registration 随 entry 释放。
5. A5（+ A6 中复现了的部分）：过期的激活什么都不开。
6. docs：skill、close-transition 更正、实施记录。

## 11. 实施记录（2026-11-23）

按 §10 分步提交，一个 PR（Task 2、3 的改动落在同几个文件里，合成一个提交）。与正文的出入、实现中才看清的细节：

| 处 | 记录 |
|---|---|
| A1 | 拆出 `ReleaseSubscriptions()`；`DetachGlobals()` 只剩 Variant、`Theme.Changed`、`RectTransformDimensionsChanged`。另加一条：已销毁的 Screen 上再 `Track` / `.AddTo(screen)`，订阅当场释放 —— 持有旧引用的代码不会再往一个没人释放的列表里挂东西 |
| A2 | 钩子是 `Screen.OnLeftOpen(screen, rootDestroyed)`，`_leftOpen` 保证恰好一次。拆除（`UnloadAll` / `ResetForTests`）关 Screen 时它们还在 `_open` 里，钩子会把它们当成直接关闭（对话框收尾、路由页警告），所以拆除期间用 `_tearingDown` 让它静默。热重载、`PromptUGUIDocumentHost`、Router 清孤儿都先摘 `_open` 再关，不受影响 |
| A3 | 路由页的警告只在显式关闭时发，根被外部销毁（场景重载）时不发 —— 那是 Router 链自检（`AssertChainNotStale`）的地盘。直接关掉路由页之后，下一次导航本来就会被链自检拦下抛 `RouteException`（修前修后都如此），警告文字写明了这一点。根被外部销毁的**对话框**同样结算、出栈 —— 以前它的 `await` 会一直挂着 |
| A4 | 「是不是主线程」按**线程 ID** 判断，不按同步上下文判断：`CancellationTokenSource` 在注册时捕获的 `ExecutionContext` 里跑回调，那里的同步上下文是副本，按引用比较永远不相等，第一版因此把主线程上的取消也推迟了一帧（`ModalCancelTests` / `MarkdownBoxTests` 当场红了）。registration 由 `IModalEntry.HoldTokenRegistration` 持有，三条结算路径（按钮、Esc、取消）都释放；内部计数 `UI.Modal.TokenRegistrationsForTests` 是测试缝 |
| A5 | `ActivatePage(def, query, epoch)` 过期返回 null，Reconcile 见 null 直接返回；只管音效的那个 epoch 判断删了 |
| A6 | 两个旁支 Red 测试都复现了（都是 `already loaded`），都修：Router 的 `EnsureLoaded` 对进行中的同一 src 去重（`_loadingSrcs`，拆除时清空）；`UI.LoadDocumentAsync` 带加载代数，`UnloadAll` / `ResetForTests` 推进它 |
| 测试环境 | 在 PlayMode 之后不重载域就跑 EditMode，宿主工程进 Play 时设下的静态皮肤（`MessageBox.XmlSrc`）会留下来，让依赖内置皮肤的 EditMode 用例失败。重载域之后正常；本 PR 新加的对话框用例都自带皮肤 |

回归：EditMode / EditorOnly / PlayMode 全量，除 `main` 上同样失败的既有环境相关用例外全部通过；`dotnet format --verify-no-changes` 干净。

