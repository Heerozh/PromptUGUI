# 关闭的可靠性 Implementation Plan

> **For agentic workers:** 按 Task 顺序执行，步骤用 checkbox（`- [x]`）跟踪。每个 Task Red 先行：先写失败的测试、跑一遍确认红，再实现到绿。
> 设计依据：`docs~/superpowers/specs/2026-11-23-close-reliability-design.md`（下称 spec，§ / A / D 编号都指它）。
> 2026-11-23 与作者对齐：spec §7 的 D1…D5 全部按推荐。

## Context

四条既有问题（sfx spec §15 的 #6 / #7 / #8 / #13）都让「关闭」在某条路径上不可靠：关了还登记着（A2 / A3）、关的时候订阅先没了
（A1）、在线程池里关（A4）、不该开的页先开再关（A5 / A6）。都已核实，A6 除外（Red 复现才修）。

**Architecture：**
- **A1**：`Screen.DetachGlobals()` 拆成「断开进程级事件」（Variant / `Theme.Changed` / `RectTransformDimensionsChanged`，Begin 时）
  与「释放 `_subscriptions`」（销毁体里、`FireClosing` 之后）。
- **A2 / A3**：`Screen.OnDetachedExternally` 换成统一的离开打开态钩子（`Close()` Begin、`CloseImmediate()`、根被外部销毁，各一次）；
  `UI.OpenCore` / `OpenModalScreen` 设置它：摘得掉 `_open` 就 `TrackClosing`；对话框再通知对话框栈结算（OCE）并移除槽位；Router 链里
  的页警告一次。
- **A4**：`UI.Modal.OpenAsync` 记下 `SynchronizationContext.Current`，回调不在它上面就 `Post`；registration 存进 entry，结算时 Dispose。
- **A5 / A6**：`Reconcile` 把 epoch 传给 `Activate` / `ActivatePage`，加载后过期就返回 null；A6 的去重 / 加载代数视 Red 结果而定。

**Tech Stack：** Unity 6 / C# 9；R3；LitMotion（测试里注入退场）。

---

## Task 0：spec 与 plan

- [x] spec 状态改为「决策已对齐」；本 plan 落盘；提交。

## Task 1（A1）：订阅活到销毁

**Files:** `Runtime/Application/Screen.cs`；新 `Tests/PlayMode/Lifecycle/CloseReliabilityPlayTests.cs`。

- [x] Red（PlayMode）：`OnClosing…AddTo(screen)` + `UI.Close`，无退场 / 有退场（`InjectExit` 的做法）各收到 1 次；
  `<Trigger id='t' on='close'/>` 的 `OnFire…AddTo(screen)` 收到 1 次；退场期间推给 `.AddTo(screen)` 订阅的 Subject 仍被处理；
  销毁之后不再被处理。
- [x] Green：拆 `DetachGlobals`；`CloseImmediate` 在 `FireClosing` 之后释放 `_subscriptions`；`OnRootDestroyedExternally` 两者都做。
- [x] 回归：`CloseTransitionPlayTests`、`ScreenLifecycleTests`、EditMode 全量；提交。

## Task 2（A2 / A3）：直接关闭也注销

**Files:** `Runtime/Application/Screen.cs`、`UI.cs`、`UI.Modal.cs`、`UI.Router*.cs`；新 `Tests/EditMode/Application/CloseReliabilityTests.cs`；
`CloseReliabilityPlayTests.cs`。

- [x] Red（EditMode）：`screen.Close()` / `CloseAsync()` / `Dispose()` 各一条 —— 之后 `UI.Get` 为 null，`UI.Open` 得到新的活实例；
  `configure` 里拿到的对话框 `IScreen` 直接 `Close()` / `Dispose()` → `await` 抛 OCE、`IsAnyOpen` 为 false、排队的下一个被提升；
  Router 链里的页被直接关掉 → 警告一次。
- [x] Red（PlayMode）：有退场的 `screen.Close()` → 退场期间 `UI.Get` 为 null、`ClosingCountForTests` 为 1、幽灵里的控件
  `OwnerScreenOf` 找得到它；结束后为 0。
- [x] Green：统一钩子；`UI` 侧摘 `_open` + `TrackClosing`；对话框栈的外部关闭收尾；Router 的 `HoldsScreen` + 一次性警告。
- [x] 回归 + `dotnet format`；提交。

## Task 3（A4）：token 在主线程结算

**Files:** `Runtime/Application/UI.Modal.cs`、`Runtime/Application/Modals/ModalEntry.cs`；`CloseReliabilityPlayTests.cs`；
`Tests/EditMode/Modals/ModalCancelTests.cs`。

- [x] Red（PlayMode）：`cts.CancelAfter(20)`，等几帧 → 根已销毁、`ClosingCountForTests` 为 0、`IsAnyOpen` 为 false、续体在主线程；
  `new Thread(cts.Cancel)` 一条。
- [x] Red（EditMode）：同一个长寿 CTS 开三个对话框、都结算之后，挂在它上面的 registration 为 0（内部计数缝）。
- [x] Green：记下同步上下文、跨线程 `Post`；registration 随 entry 结算 Dispose。`ModalCancelTests` 的同步用例保持绿。
- [x] 回归；提交。

## Task 4（A5 / A6）：过期的路由激活

**Files:** `Runtime/Application/UI.Router.Reconcile.cs`（A6：`UI.cs`、`UI.Router.Reconcile.cs` 的 `EnsureLoaded`）；
新 `Tests/EditMode/Router/RouterStaleActivationTests.cs`。

- [ ] Red：闸门 resolver（`DocumentCacheTests` 的做法）：`Router.Open` → `Router.Reset()`（以及 `UI.UnloadAll()`）→ 放闸 →
  `OnEnter` 0 次、`UI.Get` 为 null、新页上开着的弹层仍开着。
- [ ] Red（A6）：`Reset` → 再导航到同一条 → 放闸 → 第二次导航成功、只加载一次；`UnloadAll` → 放闸 → 文档没被写回，之后导航正常。
  **不红的旁支不修**，在 spec 实施记录里写明。
- [ ] Green：epoch 传参、过期返回 null；删 `ActivatePage` 里只管音效的 epoch 判断；A6 复现了的部分。
- [ ] 回归；提交。

## Task 5：文档与收尾

- [ ] C# skill：Open / Close / Get（直接关闭注销；路由页与对话框经主人关闭）、两段式关闭段（`OnClosing` 可 `.AddTo(screen)`）、
  CancellationToken 段（任意线程取消、主线程关闭与恢复）、Router（作废的导航不调 `OnEnter`）。
- [ ] close-transition spec §5.1 第 3 步的更正；spec 加实施记录。
- [ ] 全量回归：EditMode / EditorOnly / PlayMode；`dotnet format --verify-no-changes`。
- [ ] 推送，开 PR（base `feat/ui-sfx`），写明公开行为变化。
