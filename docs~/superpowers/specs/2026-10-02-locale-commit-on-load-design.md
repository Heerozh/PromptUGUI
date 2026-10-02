# Locale 切换 —— 新语言到齐后一次性提交（commit-on-load）

> 状态：**已定，实现中**（2026-10-02 起草；作者已定 §3.1：`Current` = 已生效的语言，新增 `Pending`；其余按本文推荐，
> 2026-10-03 指示直接实施，按 §9 分步提交）。分支 `feat/locale-commit-on-load`。
> 需求来源：PR #164 的 follow-up。宿主工程（ssw_re_client）`UserConfig.SetLanguageAsync` 的注释写着「设置页推荐用这个，
> 避免下拉切换后短暂闪 msgid」；`LocaleAddressableResolverHelper` 的文档注释也直说「Set 返回后 UI 还看到 msgid……想避免闪烁用
> `await Locale.SetAsync`」。
> 相关：
> `2026-05-12-locale-addressable-resolver-design.md` LAR-D4 / LAR-D6（当年接受了「fire-and-forget 路径短暂闪烁回 msgid」；
> D6 的顺序是先卸旧表、先改 `Current`，加载失败时 `Current` 也就照样推进了 —— 本文取代这两条；LAR-D9 的「`Set` 记日志 /
> `SetAsync` 抛出」保留）、
> `2026-05-08-i18n-fonts-design.md`（locale 即 Variant；字体表按 `Locale.Current` 解析）、
> `2026-10-01-runtime-sprite-sets-design.md` §8（runtime .po catalogs：切换要等所有 catalog，单个失败记日志跳过）、
> PR #164（`VariantStore` 的双变体 `Set`：两个变体一步切完，只发一次 `Changed`）。

## 1. 问题

今天的 `UI.Locale.Set(x)`（`UI.cs` 的 `Locale.Set` / `LoadPoFilesAndApplyAsync` / `LoadPoFilesAsync`）按这个顺序走：

1. `VariantStore.Set(old, false)` → `Changed` → 每个打开的 Screen ReSolve 一遍。此时 `Current` 还是 old、旧表还在，
   文字仍是旧语言，但 locale 变体覆盖（`font.zh-Hans=` 之类）已经失效。
2. `TranslationStore.UnloadLocale(old)` —— 旧表没了。
3. `Current = x` —— `TrResolver`、`FontApplier`、宿主读 `Current` 的格式化代码从这一刻起都按 x 走。
4. 异步加载 x 的 built-in .po 与 runtime catalogs。
5. `VariantStore.Set(x, true)` → 第二遍 ReSolve → `Locale.Changed`。

第 3 步到第 5 步之间是一个窗口；用 Addressables 下载 .po 时可能长达秒级。窗口里任何重新渲染（resize、转屏、新开的 Screen、
`BindItems`、C# 的 `UI.Tr`）拿到的都是 **msgid**，配的却是 **x 的字体**（`FontApplier` 按 `Current` 查表）。
源文是 CJK、x 的字体不带 CJK 字形时更糟。此外：

- **每次切换 ReSolve 两遍**，第一遍对着「没有任何 locale 变体」的中间态。
- **加载失败停在半路**：`Current` 已经是 x、旧表已卸载、x 的变体永远不会打开 → 之后任何重新渲染都是 msgid。而 `Set(x)`
  重试会因为 `Current == x` 直接 return —— `Set_fire_and_forget_logs_error_on_resolver_throw` 注释里说的「caller can retry」
  其实做不到，只能 `ReloadCurrent`。
- **`ReloadCurrent` 同样先卸后载**：`UnloadLocale(Current)` 之后才 await 加载；加载器是异步的时候，窗口里的任何重新渲染都回落
  msgid（编辑器 .po 热重载、运行时补丁 .po 都走它）。

`await SetAsync` 只能让调用方自己别在窗口里渲染，挡不住 resize、其它 Screen、其它代码触发的重新渲染；
而 C# skill 推荐给所有用户的是 fire-and-forget 的 `Set`。

## 2. 目标与非目标

**目标**

1. 切语言没有窗口：x 的全部翻译（built-in .po + runtime catalogs）到齐之前，界面完全停在旧语言 ——
   文字、字体、locale 变体、`Current` 都是旧的。
2. 到齐后一次提交：翻译表、`Current`、locale 变体（一次 `VariantStore.Changed` → 每个 Screen 一次 ReSolve）、旧表卸载、
   `Locale.Changed`，在同一个同步段里做完。
3. 新增只读 `UI.Locale.Pending`：正在加载、尚未提交的目标语言；没有在途切换时为 `null`。
4. 加载失败 = 不切换：留在旧语言；`Set` 记错误日志、`SetAsync` 抛出；`Set(x)` 可以直接重试。
5. `ReloadCurrent` 同样先取后换：新条目到齐之前旧条目一直有效。
6. 同步加载器（默认 Resources 路径、返回已完成 Awaitable 的 `PoResolver`）下对外行为不变：`Set` 返回时一切已切换完毕。
   唯一可见差别是 ReSolve 从两遍变成一遍。

**非目标**

- 不改 `TranslationStore` 的存储形状。它本来就按 `(locale, ctx, msgid)` 分键，新旧两种语言的表可以共存 —— 这正是
  「先取后换」不需要新存储的原因。只加两个 internal 的整表替换操作（§4.3）。
- 不给加载器加超时。沿用 runtime catalog 的约定「loader 自己负责超时」：一个永不完成的加载器让切换永远 pending
  （今天是永远停在 msgid）；再 `Set` 别的语言可以取代它。
- 不提供加载进度 API。
- 不改 `Locale.Changed` 的签名（仍是 `System.Action`）。
- 编辑器 UI（Play Mode 语言下拉框、UI Preview 状态行）不显示 `Pending`。
- 不处理主题（`UI.Theme`）里可能存在的类似时序 —— 另一个系统。

## 3. 语义

### 3.1 状态（作者已定）

| 属性 | 含义 | 何时改变 |
|---|---|---|
| `Current` | **已生效的语言**：`TrResolver`、`FontApplier`、locale 变体、`TranslationStore` 里的表都是它的 | 只在提交时（§3.4）|
| `Pending`（新增，只读） | 已请求、尚未提交的目标；无在途切换时为 `null` | 请求时设置；提交、失败、被取代、被取消时清空或改写 |

不变量：

- `Pending != Current`（请求回到 `Current` 会取消 pending，§3.2）。
- `Current != null` 时，locale 变体集合恰好是 `{Current}`：任何时刻都没有「两个都开」或「都不开」的中间态。
- gather（§3.3）不写 `TranslationStore`；`Pending` 的条目只在提交那一刻入库。

### 3.2 `Set(x)` / `SetAsync(x)`

按调用时的状态，自上而下取第一条匹配：

| # | 条件 | 效果 |
|---|---|---|
| 1 | 有 pending 且 `x == Pending` | 无操作（已在加载）。`SetAsync` 等这次在途请求结算 |
| 2 | `x == Current` | 有 pending 则取消它（结果到达时丢弃）；否则无操作。都不触发任何事件 |
| 3 | `x == null`（此时 `Current != null`） | 取消 pending，**立即**提交 `null`：`Current = null`、旧变体关（一次 `Changed`）、卸载旧表、`Locale.Changed` |
| 4 | 其它 | `Pending = x`，作废之前的 pending；开始 gather；gather 完成且仍是最新请求 → 提交 |

`SetAsync(x)` 返回的 Awaitable 在这次请求**结算**时完成：

- 提交成功 → 正常完成，`Current == x`。
- 被取代（第 4 条）或被取消（第 2、3 条）→ **立即**正常完成，`Current != x`。今天被取代的 `SetAsync` 也是正常 return，
  只是要等那次过时的加载跑完；这里不再等一个与它无关的下载。
- built-in 加载失败 → 抛出（同 LAR-D9 的 async 半边）。
- 第 1 条：与在途请求同时结算；第 2 条：同步完成；第 3 条：同步完成。

`Set(x)` = 同一套逻辑的 fire-and-forget 外壳，失败时记 `[PromptUGUI] locale load failed for 'x': …`（文案不变）。

### 3.3 Gather（异步段，只读）

- **built-in .po**：`PoResolver(x)`；`PoResolver` 为 null → Resources 路径（同步解析 `PromptUGUI/i18n/x` 与
  `PromptUGUI/i18n-custom/x`）。只返回条目列表，不写 store。
- **runtime catalogs**：gather 开始时快照已注册的 catalog，全部同时启动、各 await 一次（沿用 `LoadRuntimeCatalogsAsync` 的形状；
  Awaitable 是池化的，只能 await 一次）。单个 catalog 失败：记日志、跳过，不挡切换（§8 约定不变）。
- gather 结束时已不是最新请求 → 丢弃全部结果，不提交。
- built-in 加载器抛出 → 请求失败（§3.5）。

### 3.4 提交（同步段，顺序固定）

1. 把 gather 到的条目**合并**写入 `TranslationStore`：base 表，以及**仍注册着**的 catalog 的 layer（同今天的 `Load` /
   `LoadLayer`）。是合并、不是替换：调用方在 `Set` 之前手动 `TranslationStore.Load` 进来的条目要保留 ——
   `LocaleSetTests.Tr_StaticMethod_DelegatesToTrResolver` 与宿主的格式化测试都这么用。
2. `old = Current; Current = x; Pending = null`。
3. 变体：`VariantStore.Set(old, false, x, true)`（PR #164 的双变体重载；`old == null` 时只开 x）→ 一次 `Changed` →
   每个打开的 Screen 一次 ReSolve。此刻 `Tr`、字体、变体全是 x。
4. `TranslationStore.UnloadLocale(old)` —— 在 ReSolve 之后，已没有人读它。
5. `Locale.Changed?.Invoke()` —— 控件的 `ApplyFont` 与宿主的监听。仍在 ReSolve 之后，与今天同序。
6. 补加载：gather 快照之后才注册的 catalog，对新的 `Current` 各走一次 `LoadCatalogForCurrent`（落地时再 ReSolve 一次）。
7. 结算这次请求的 `SetAsync` 等待者。

### 3.5 失败

- **built-in 加载器抛出**：不提交 —— `Current`、变体、旧表原封不动；若它仍是最新请求则 `Pending = null`。
  `Set` 记日志，`SetAsync` 抛出。重试就是再 `Set(x)`：`Current != x`，不再是无操作。
- **runtime catalog 抛出**：记日志、跳过，照常提交（不变）。
- **加载器永不完成**：切换永远 pending，界面停在旧语言；`Set` 别的语言或 `Set(Current)` 可以取代 / 取消它。

### 3.6 `ReloadCurrent` / `ReloadCurrentAsync`

- gather `Current` 的全部条目 → 提交时**逐表替换**：base 表与每个参与 gather 的 catalog layer，各自「清掉这个 locale 的条目、
  写入新条目」，同步完成 → `VariantStore.NotifyChangedInternal()`（一次 ReSolve）。不触发 `Locale.Changed`（同今天）。
- 不用 `UnloadLocale(Current)` 一刀切：reload 期间才注册的 catalog 已经为 `Current` 加载了自己的条目，它不在这次 gather 里，
  不能被抹掉。
- 失败：保留旧条目；`ReloadCurrent` 记日志（文案不变），`ReloadCurrentAsync` 抛出。
- gather 期间 `Current` 变了（某次切换提交了）→ 丢弃结果。
- 与在途切换并存：reload 只针对 `Current`。切换提交时照常卸载旧语言的表，被 reload 替换过的也一样。

### 3.7 Runtime catalogs

- pending 期间 `RegisterRuntimeCatalog`：照今天，对 `Current`（旧语言）加载；切换提交后由 §3.4 第 6 步再对新语言加载一次。
- pending 期间 `UnregisterRuntimeCatalog`：gather 里它的结果在提交时按「仍注册着」的检查丢弃（今天已有这条检查）。
- `RegisterRuntimeCatalogAsync` 完成于 `Current` 的条目到齐，不等在途的切换（写进 skill）。

### 3.8 其它入口

- `InitializeIfNeeded`：`Current != null || Pending != null` → return。启动期第一次切换还在下载时，不再重复发起。
- `SetToSystemDefault[Async]`：转发 `Set` / `SetAsync`，自然继承。
- `ResetForTests`：作废在途请求、`Pending = null`、结算所有等待中的 `SetAsync`（正常完成，免得测试挂住），其余同今天。
- `PromptUGUIDocumentHost`：`Current != locale` → `Set(locale)`；pending 期间重复调用落在第 1 条，无操作。
- `PlayModeLocaleMenu`：`Locale.Changed` 时把下拉框同步到 `Current`。现在只在提交时触发；加载失败时下拉框停在用户选的值 —— 接受。
- `UIAssetPostprocessor`：按 `Current` 判断 .po 改动 → `ReloadCurrent`，不变。

## 4. 实现要点

### 4.1 请求对象

每次第 4 条请求建一个 `LocaleRequest { Locale, Version, Waiters }`，静态持有最新的那个；`Version` 单调递增，
gather 的每个 await 之后用它判断是否过时。`Waiters` 是 `AwaitableCompletionSource` 的列表：**每个 `SetAsync` 调用各拿一个**，
因为 Unity 的 `Awaitable` 不能被 await 两次 —— 第 1 条的「共享结算」靠往同一个请求里再加一个 waiter 实现。
不用 .NET 的 `Task` / `TaskCompletionSource`（WebGL 约束）。

### 4.2 Gather / 提交拆分

`LoadPoFilesAsync`（现在边加载边 `TranslationStore.Load`）拆成：

- `GatherAsync(locale) → LocaleBundle`：base 条目 + 每个快照 catalog 的 `(catalog, entries)`；
- `Commit(request, bundle)`：§3.4 的同步段；
- `ReplaceForReload(bundle)`：§3.6 的同步段。

Resources 路径的 `LoadPoFromResourcesPath` 改成解析进列表、不直接入库。

### 4.3 `TranslationStore` 新增（internal）

- `ReplaceLocale(locale, entries)`：base 表里清掉该 locale 的条目、写入新条目；
- `ReplaceLayer(layer, locale, entries)`：同上，作用于一个 layer；layer 已被移除（catalog 已注销）时什么也不做（同 `LoadLayer`）。

`Load` / `LoadLayer` / `UnloadLocale` / `Lookup` 不变。

### 4.4 变体

提交用 PR #164 的 `VariantStore.Set(name1, active1, name2, active2)`；`Set(null)` 的立即提交用单变体 `Set(old, false)`。
两种情况都只发一次 `Changed`。

## 5. 对使用方的行为变化

| 场景 | 今天 | 之后 |
|---|---|---|
| 同步加载器（默认 Resources） | `Set` 返回时已切换；两遍 ReSolve | `Set` 返回时已切换；**一遍** ReSolve |
| 异步加载器，加载期间读 `Current` | 新语言 | **旧语言**；`Pending` = 新语言 |
| 异步加载器，加载期间重新渲染 | msgid + 新语言字体 | 旧语言文字 + 旧字体 |
| 加载期间的 locale 变体 | 两个都不开 | 旧的开着 |
| 加载失败 | `Current` = 新语言、旧表已卸，之后的重新渲染都是 msgid；`Set(x)` 重试无效 | 保持旧语言；`Set(x)` 可重试 |
| `ReloadCurrent` 期间 | 当前语言的表被清空 → msgid | 旧条目保留到新条目到齐 |
| 快速连续 `Set(A)`、`Set(B)`（都异步） | `Current` 立刻 = B | `Current` 保持旧值直到 B 提交；`Pending` = B |
| 被取代的 `SetAsync(A)` | 等 A 的下载跑完才正常 return | 立即正常完成 |

迁移说明（写进 C# skill）：

- 想读「刚 `Set` 的那个语言」的代码：`await SetAsync(x)` 之后再读 `Current`；要「目标语言」读 `Pending ?? Current`；
  要在切换生效时刷新，订阅 `Locale.Changed`。
- 依赖「`Set` 同步改 `Current`」的测试：固定一个同步的 `PoResolver`（返回已完成的 Awaitable，或置 `null` 走 Resources），
  或者改成 `await SetAsync`。宿主 `NumberFormatTests.跟随当前UI语言不等po` 属于这一类，而且可能真的走到异步路径：
  它不设 `PoResolver`，用的是静态字段里现有的那个；如果同一个编辑器 domain 里先跑过 Play，`GameBootstrap` 设下的
  Addressables 加载器可能还留着。建议在它的 SetUp 里显式固定一个同步加载器。
- 宿主 `UserConfig.SetLanguageAsync` 注释里「避免闪 msgid」的理由不再成立；await 本身仍有用（等切换完成再保存 / 继续）。

## 6. 决策表

| 编号 | 决策 | 理由 / 取舍 |
|---|---|---|
| LCL-D1 | `Current` = 已生效的语言（**作者已定**） | 文字、字体、格式化、变体、`Changed` 同一刻切换；代价是异步加载器下 `Set` 之后不能立刻读到新值（§5 迁移说明） |
| LCL-D2 | 新增只读 `Pending` | 设置页显示目标、调用方去重；不另设 `IsSwitching`（`Pending != null` 即是） |
| LCL-D3 | 先取后换：gather 不写 store，提交同步完成 | `TranslationStore` 已按 locale 分键，新旧共存零成本 |
| LCL-D4 | 提交顺序：入库 → `Current` → 变体（一次 `Changed`）→ 卸旧表 → `Locale.Changed` | ReSolve 看到的全是新语言；`Locale.Changed` 仍在 ReSolve 之后，与今天同序 |
| LCL-D5 | 最新请求胜出；回到 `Current` = 取消；同 `Pending` = 无操作 | 设置页来回切、启动期重复初始化都安全 |
| LCL-D6 | built-in 加载失败不切换；`Set` 记日志 / `SetAsync` 抛出（LAR-D9 不变） | 取代 LAR-D6 顺序带来的「失败也推进 `Current`」；`Set(x)` 重试终于有效 |
| LCL-D7 | 被取代 / 取消的 `SetAsync` 立即正常完成 | 不引入 `OperationCanceledException`；与今天「正常 return」一致，只是不再等无关下载 |
| LCL-D8 | 同一目标的多个 `SetAsync` 一起结算；每个调用各持一个 completion source | Unity `Awaitable` 只能 await 一次 |
| LCL-D9 | `ReloadCurrent` 先取后换、逐表替换；失败保留旧条目 | 不抹掉 reload 期间新注册 catalog 的条目 |
| LCL-D10 | pending 期间注册的 catalog：提交后对新语言补加载 | 实现简单；代价是它落地时多一次 ReSolve |
| LCL-D11 | `InitializeIfNeeded` 同时看 `Pending` | 启动期第一次切换在途时不重复发起 |
| LCL-D12 | 不加超时 | 沿用 runtime catalog 约定：loader 自己负责 |
| LCL-D13 | 切换的提交**合并**入库；`ReloadCurrent` **逐表替换** | 保留调用方在 `Set` 前预载的条目；reload 的结果与今天（先 `UnloadLocale` 再加载）一致，只是没有窗口 |

## 7. 测试

EditMode，用 `AwaitableCompletionSource` 驱动的 `PoResolver` / catalog 模拟异步加载。

**改写的既有测试（行为变化）**

- `LocaleSetAsyncTests.Set_fire_and_forget_logs_error_on_resolver_throw`：失败后 `Current` 保持原值（null）、无 locale 变体；
  换一个正常的 resolver 再 `Set("en")` 成功。
- `LocaleSetAsyncTests.Set_rapid_consecutive_with_pending_resolver_discards_stale_load`：两次 `Set` 之后 `Current` 仍为 null、
  `Pending == "en"`；en 到齐后 `Current == "en"`，zh-Hans 的结果被丢弃（断言不变）。

**新增**

- 加载窗口停在旧语言：`Current`、`UI.Tr`、旧变体开着；窗口内 `screen.ReSolve()` 仍是旧文字，不是 msgid。
- 一次提交：恰好一次 `VariantStore.Changed`（回调里看到旧变体已关、新变体已开）、一次 `Locale.Changed`；之后旧表已卸载。
- 提交是合并：`Set` 之前手动 `TranslationStore.Load` 的新语言条目，异步提交之后仍可查到。
- 同步路径：每次切换恰好一次 `VariantStore.Changed`（今天是两次）。
- 取代 A → B（A 的结果丢弃、A 从未提交）；取消（pending 时 `Set(Current)`）；同目标重复 `Set` 只请求一次加载器；
  pending 时 `Set(null)` 立即提交 null。
- `SetAsync`：提交时完成；被取代时立即完成且 `Current != x`；两个 `SetAsync(x)` 一起结算；失败抛出且保留旧语言。
- 失败后 `Set(x)` 可重试。
- `ReloadCurrent`：异步 reload 期间旧条目仍有效；到齐后替换；失败保留旧条目；切换提交后到达的 reload 结果被丢弃；
  reload 期间新注册的 catalog 条目不被抹掉。
- pending 期间注册的 catalog：提交后对新语言可用。
- pending 期间 `InitializeIfNeeded` 不发起第二次请求。
- `ResetForTests` 清掉 `Pending`、丢弃在途结果、结算等待中的 `SetAsync`。
- `RuntimePoCatalogTests` 全部保持通过（`Pending_catalog_delays_variant_flip_until_loaded` 本来就符合新语义）。

## 8. 文档

- C# skill「Locale & i18n (C# side)」：`Current` = 已生效的语言、`Pending`、`Set` 不再闪 msgid、`SetAsync` 用于等切换完成、
  失败保持旧语言、每次切换一遍 ReSolve；§5 的迁移说明；`RegisterRuntimeCatalogAsync` 不等在途切换。
- Addressables skill：去掉「await 才不闪」的说法，改成「await 用于等切换完成」。
- `LocaleAddressableResolverHelper` 的文档注释（「Set 返回后 UI 还看到 msgid」两行）。
- `2026-05-12-locale-addressable-resolver-design.md`：LAR-D4 / LAR-D6 各加一行指向本文。

## 9. 实施步骤（每步 red → green → `dotnet format` → 提交）

1. 本 spec。
2. gather / 提交拆分 + `Pending` + §3.2 状态表 + 失败语义：改写两条既有测试、新增加载窗口 / 一次提交 / 取代 / 取消 /
   `SetAsync` 结算 / 重试的测试。
3. `ReloadCurrent` 先取后换（`TranslationStore.ReplaceLocale` / `ReplaceLayer`）。
4. runtime catalog 与 pending 的交互（§3.7）、`InitializeIfNeeded`、`ResetForTests`。
5. 文档与 skills（§8）。
