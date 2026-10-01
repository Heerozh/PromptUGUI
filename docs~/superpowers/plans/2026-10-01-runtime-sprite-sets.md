# 运行时 SpriteSet + 运行时 .po 目录 Implementation Plan

> **For agentic workers:** Steps use checkbox (`- [ ]`) syntax for tracking. Red first: every task writes its tests,
> refreshes Unity and sees them fail for the right reason before implementing.

## Context

游戏要支持 UGC 资源包：图标与 .po 在运行时从服务器下载，要和内置 SpriteSet 一样用 `集名:键` 引用。

现状的问题：
- 只能在外面包一层 `UI.SpriteResolver`，resolver 重装或热重载时这层会被覆盖。
- 现有的全量 ReSolve 广播刷不到代码写入的图标（`<Icon name>` 是运行期独占属性），而且代价高。

设计见 spec `docs~/superpowers/specs/2026-10-01-runtime-sprite-sets-design.md`（下文 §x 均指它，决策编号 RSS-D1…D16）。
- 作者已说「开始写 plan」，所以 spec 中「新定」的 6 条按推荐执行。
- 本计划阶段做过一轮设计评审，采纳的修订见下面 P 表，由 Task 0 写回 spec。
- 分支 `feat/runtime-sprite-sets`。

**Goal:**
- 运行时集 API：`UI.RegisterRuntimeSpriteSet`（整包 / 按需两种形态）和 `UnregisterRuntimeSpriteSet`。
- `<Icon name>` / `<Image sprite>` 内置 slot：按需图晚到时自己刷新，不走 ReSolve。
- 其余 sprite 属性同步解析；遇到按需集时确定性报错。
- `UI.Locale.RegisterRuntimeCatalog` 以分层的方式叠在 `TranslationStore` 上。

验收：
- spec §10（按 Task 0 修订后）的测试全绿；
- EditMode / EditorOnly / PlayMode 零回归；
- `dotnet format --verify-no-changes --severity warn` 干净。

**Architecture:**
- `RuntimeSpriteSets`：中心，只管数据——注册表、key 状态、各张等待 / 绑定表、去重表。
- `AsyncSpriteSlot`：每个 Icon / Image 一个，只管自己——Set、图到达、注册 / 注销通知、静态 resolver 装好通知。
- `UI.ResolveSprite`：先走运行时分支，再走原样保留的 `ResolveSpriteStatic`。
- `TranslationStore`：内部加有序层，层用句柄表示。

**Tech Stack:** C# 9（LangVersion 9.0）、Unity `Awaitable` / `AwaitableCompletionSource`（Unity < 6 走 UniTask 垫片）、R3、NUnit EditMode / PlayMode。

## 已对齐的决策（plan 阶段，Task 0 写回 spec）

| # | 决策 | 为什么 |
|---|---|---|
| P1 | 新增不带版本守卫的 internal 入口 `UI.ClearRuntimeRegistrations()`。它先把各注册标记为已作废，再清表（运行时集、各等待表、目录、层、去重表）。调用方：`ResetForTests`、新增的 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 钩子（同 `UI.ResetCommonsStatics`）、6000.5+ 的进出 Play 钩子。测试直接测这个入口 | dev 宿主是 6000.0.76f1，`UNITY_6000_5_OR_NEWER` 不成立；关掉 Domain Reload 时第二次进 Play 会重名抛错，并且还持有已销毁的 Sprite |
| P2 | `UILog` 新增 `Warn(string)`，同 `Error(string)`，用 `Applying` 定位 | 同步入口的缺图告警手里没有控件 |
| P3 | 派生状态刷新（Image：宽高比 / `DeriveType` / FX；Icon：FX）只在一种情况下调用：这次赋值来自运行时状态（Ready / Loading / Missing / Detached），且 `!owner.InApplyPass`。静态解析出的赋值一律不刷新 | 静态路径行为不变（spec §12）；运行时结果是同步命中还是晚到，刷新效果一样，与时序无关 |
| P4 | `Control.InApplyPass` 在 `ControlAttributeApplier.Apply` 里设置：先保存旧值再置 true，`finally` 恢复旧值 | P3 和尺寸检查都要知道「这次写入是否发生在自己的 apply 里」；可能有嵌套 apply，所以要恢复旧值而不是直接写 false |
| P5 | null 占位**什么都不画**：`FxImage` 加 internal `DrawNothingWhenEmpty`，`OnPopulateMesh` 开头 `if (DrawNothingWhenEmpty && sprite == null) { toFill.Clear(); return; }`，切换时 `SetVerticesDirty()`。只有运行时状态传入 `hideIfNull = true`；预置的是普通 Image（不是 FxImage）时退回 `enabled` | uGUI Image 的 sprite 为 null 时会画实心块（`FxImage.cs:211-213` 先调 base；库里 `CaptionBuilder.cs:176` 有注释）。不用 `enabled = false` 是为了不破坏 `mask="self"` 和 raycast |
| P6 | `EndSpriteResolverLoad` 计数归零时：先处理静态等待表，再发今天的广播。规则：<br>• 等待表只登记 apply **之外**写入的 slot，XML 写的值交给广播重放（避免报两次错）；<br>• 此时 `UI.SpriteResolver` 仍为 null（加载失败）→ 只清空等待表，不重解析、不打日志；<br>• 带 `:` 的值同时登记到「按集名等待表」 | 否则既有测试 `Icon_silent_when_load_in_flight` 会变红；也覆盖「静态加载期间注册了同名运行时集」的情况 |
| P7 | `Bound`、按集名等待表、key 等待者三张表统一走 `AddPruned`：插入时若 `count ≥ max(64, 2 × 上次清扫后的存活数)`，就扫一遍摘掉 `owner.GameObject == null` 的条目。遍历时也顺手摘。集名属于已加载的静态集时，不进按集名等待表 | 行内控件从不单独 Dispose，反复开关 UGC 浏览页会线性泄漏 |
| P8 | 重入安全：<br>• Request 先把 `KeyEntry(Loading)` 放进表，再调取图函数；<br>• slot 在 Request 返回、且条目仍是 Loading 时才挂上等待；<br>• slot 带代次 `_gen`，每次 Set / Release / Detach 都 `++`，凡是会跑用户代码的调用返回后都要比对代次；<br>• `Settle` 幂等，并检查 `reg.Alive`；<br>• 所有通知基于快照，每个 slot 单独 `try/catch` + `Debug.LogException` | 取图函数的同步部分和日志回调都会在 Set 进行到一半时运行用户代码 |
| P9 | `BuildLookup` 先预检（静态集互相重名、与运行时集重名），通过后才 `LoadedSpriteSetNames.Clear()` | 避免中途抛异常留下半张名单 |
| P10 | `SizeFromNative`：`ApplyCommon` 开头复位，然后：<br>• `size="native"` 分支置位；<br>• 自由定位缺轴回退：`!(parentIsGrid && flow)` 时置位；<br>• `ApplyLayoutElement` 算出 `fillCross*` 之后：某轴未写、不是 hug、也不是交叉轴 fill → 置位。<br>尺寸告警按 `SourceNode` 去重（没有 SourceNode 时按控件） | Grid 的格子尺寸由 cellSize 决定，交叉轴 fill 由父级决定，这两种不该告警；BindItems 的行共用同一个节点，500 行不能报 500 条 |
| P11 | 静态解析由宿主实现，返回三态 `Ok / Deferred / Failed`。`UI.ResolveSpriteStatic` 就是今天 `ResolveSprite` 的函数体，逐字不动。日志 overload 原样保留：Icon 用 `UILog.Error(this, "Icon '…'")`，Image 用 `UILog.Error(string)` | `RuntimeSourceAttributionTests` 钉住了这两种 at-line；Icon 不看 `:`、什么值都交给 resolver |
| P12 | 每个注册三张去重表：缺图 Warn、取图异常 Error、按需集用在同步入口的 Error。<br>• 发生过异常的 key 不再打缺图 Warn；<br>• 异步异常用第一个还活着的等待者的控件来打日志；<br>• 空 key（如代码写入 `"ugc:"`）直接按缺图处理，不调取图函数；<br>• `options` 在注册时做快照 | 测试与日志都可预期 |
| P13 | `TranslationStore` 的层用句柄（`AddLayer()` 返回 Layer）：<br>• `UnloadAll` 只清各层条目、保留层；<br>• `LoadLayer` 对已移除的层什么都不做；<br>• `ClearLayers` 由 P1 的入口显式调用 | 测试会直接调 `TranslationStore.Instance.UnloadAll()`；层的身份与顺序属于目录注册表 |
| P14 | 新代码不用 `GetInstanceID`，集合里直接存对象引用；新建 `.cs` 的 `.meta` 刷新后一起提交 | 6000.7 宿主上 `GetInstanceID` 是 obsolete-error；本仓库是 UPM 包 |

## Global Constraints

- **测试宏**
  - `RUN(Class)`：
    1. `refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`；
    2. `read_console(types=["error"])`，确认无编译错误；
    3. `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["Class"])`；
    4. 轮询 `get_test_job`，核对 `summary.total > 0`。
  - `RUNPLAY(Class)`：同上，改用 `mode="PlayMode"`、`PromptUGUI.Tests.PlayMode`。
  - `RUNEDITOR(Class)`：同上，改用 `PromptUGUI.Tests.EditorOnly`。
  - 工具先用 `ToolSearch("select:mcp__UnityMCP__run_tests,mcp__UnityMCP__get_test_job,mcp__UnityMCP__refresh_unity,mcp__UnityMCP__read_console")` 加载。
  - 先查 `mcpforunity://instances` 确认 MCP 连的是哪个实例：PromptUGUIDev 6000.0，或 ssw_re_client 6000.7。
- **lint**：`cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`。**禁止**用 `--severity info`。
- **规矩**
  - 测试只经 Unity MCP 跑；禁止 `execute_menu_item("Assets/Reimport All")`；绝不提交到 main。
  - `[UIAttr]` 必须配 `[Preserve]`。
  - 不用 `Task` / 线程；每个 Awaitable 恰好取一次结果。
  - Core 的纯 C# 子集不动。
- **测试约定**
  - `[SetUp]` / `[TearDown]` 都调 `UI.ResetForTests()`。
  - 命名空间：Controls 用 `PromptUGUI.Tests.EditMode.Controls`，Application 用 `PromptUGUI.Tests.EditMode.Application`。
  - 按需取图函数：每个 key 一个 `AwaitableCompletionSource<RuntimeSprite>`，记录被调用次数。
  - 日志断言：`LogAssert.Expect(LogType.Warning|Error, new Regex(...))` 放在动作之前；断言没有日志用 `LogAssert.NoUnexpectedReceived()`。
  - 调用 `RegisterRuntimeSpriteSet(name, null)` 要显式 cast，否则 CS0121。
- **夹具来源**
  - 带 border 的 sprite：`BtnStateTests.cs:391-405`。
  - contain 宽高比：`ImageFitTests.cs:119-127`。
  - 行复用：`ScrollListReuseTests.cs:24-60`。
  - Probe 控件：`PagesTests.cs:264`。

---

## File Structure

| 文件 | 责任 | 动作 |
|---|---|---|
| `Runtime/Application/RuntimeSprite.cs` | `RuntimeSprite`、`RuntimeSpriteSetOptions` | Create |
| `Runtime/Application/RuntimeSpriteSets.cs` | 注册表、`KeyEntry`、Request / Settle、各等待表（P7）、去重表（P12）、`ResolveSync` | Create |
| `Runtime/Application/UI.RuntimeSprites.cs` | `RegisterRuntimeSpriteSet` ×2、`UnregisterRuntimeSpriteSet`、`ClearRuntimeRegistrations`、SubsystemRegistration 钩子 | Create |
| `Runtime/Controls/Internal/AsyncSpriteSlot.cs` | slot 状态机；宿主接口 `ISpriteSlotHost`（`ResolveStatic` 三态 / `Assign(sprite, hideIfNull)` / `RefreshDerived` / `SizeDependsOnSprite`） | Create |
| `Runtime/Application/UI.Locale.RuntimeCatalogs.cs` | `RegisterRuntimeCatalog(Async)`、`UnregisterRuntimeCatalog`、`LoadRuntimeCatalogsAsync` | Create |
| `Runtime/Application/UI.cs` | `ResolveSprite` 拆出 `ResolveSpriteStatic`、诊断名单、`EndSpriteResolverLoad`（P6）、`LoadPoFilesAsync` 接目录、重置与 Play 钩子 | Modify |
| `Runtime/Application/UILog.cs` / `SpriteResolverHelpers.cs` / `ControlAttributeApplier.cs` / `TranslationStore.cs` | 分别对应 P2 / P9 / P4 / P13 | Modify |
| `Runtime/Controls/Control.cs` | `InApplyPass`、`SizeFromNative`（P10） | Modify |
| `Runtime/Controls/Internal/FxImage.cs` | `DrawNothingWhenEmpty`（P5） | Modify |
| `Runtime/Controls/Icon.cs` / `Image.cs` | 实现 `ISpriteSlotHost`；Image 拆出 `RefreshSpriteDerivedState` | Modify |
| `Tests/EditMode/Application/RuntimeSpriteSetTests.cs`、`Tests/EditMode/Controls/AsyncSpriteSlotTests.cs`、`Tests/EditMode/Controls/ControlSizeFromNativeTests.cs`、`Tests/PlayMode/Controls/RuntimeSpriteSetPlayTests.cs`、`Tests/EditMode/Application/RuntimePoCatalogTests.cs`、`Tests/EditMode/I18n/TranslationStoreTests.cs`（追加） | 测试 | Create / Modify |
| `.claude/skills/**`、master spec §5.4、本 spec | 文档 | Modify |

---

## Task 0：spec 修订 + plan 落盘 + 提交

- [x] 把 P1–P14 写回 spec：
  - §5.1：null 占位什么都不画；
  - §5.3：取图契约补三条——在主线程完成、每次调用返回新的 Awaitable、空 key；
  - §5.4：注销后不再引用的措辞收紧——只指注册表和 slot；
  - §6.5：三张去重表；
  - §6.7：`ClearRuntimeRegistrations` 与 SubsystemRegistration 钩子；
  - §7.2–7.5：代次、三态静态解析、P3 刷新规则、清扫、P10 的放置与按节点去重；
  - §7.7：P6；
  - §8：层句柄、loader 必须自带超时、Async 版失败后注册保留；
  - §10：测试清单换成下文；
  - §13：补两条非目标——重绑或热重载期间代码写入的图标保持旧图；Detached 不会因同名静态集出现而恢复。
- [x] 本计划存为 `docs~/superpowers/plans/2026-10-01-runtime-sprite-sets.md`。
- [x] `git add` 这两份 → `git commit -m "docs(sprites): runtime sprite sets + runtime .po catalogs — spec + plan"`

## M1 — 运行时集注册表 + 同步入口（`RuntimeSpriteSetTests`）

### Task 1：骨架（不写 Red，只为让测试程序集能编译）

- [x] 新建 `RuntimeSprite.cs`：readonly struct，带隐式 `Sprite → RuntimeSprite` 转换；以及 `RuntimeSpriteSetOptions`。
- [x] 新建 `UI.RuntimeSprites.cs`：三个公开方法先 `throw new NotImplementedException()`。
- [x] 新建 `RuntimeSpriteSets.cs`：`Registration`、`KeyEntry`。
- [x] `UILog.Warn(string)`（P2）。
- [x] 刷新，console 无编译错误。

### Task 2：注册校验 + 整包集

- [x] **Red**：`Invalid_set_name_throws`、`Null_entries_or_loader_throws`、`Empty_key_in_entries_throws`、`Null_sprite_entries_are_skipped`、
      `Entries_and_options_are_snapshotted_at_register`、`Duplicate_runtime_name_throws`、`Name_colliding_with_static_set_throws_in_both_orders`、
      `Unregister_unknown_returns_false`、`Eager_set_resolves_through_ResolveSprite`、`Eager_missing_key_returns_Missing_and_warns_once`、
      `Eager_set_in_sync_attribute_works`（`<Btn sprite='pack:x'/>`）、`Works_without_any_SpriteResolver`、`Tiled_entry_registers_render_hint`
- [x] **实现**：
  - 集名校验 `[A-Za-z0-9_-]+`；
  - 整个字典校验通过后，才提交 Ordinal 快照；
  - 重名检查，消息注明对方是运行时集还是静态集；
  - tiled 条目登记 hint；
  - `ResolveSprite`：先 `RuntimeSpriteSets.TryResolveSync`，否则走 `ResolveSpriteStatic`（今天的函数体逐字搬过去）；
  - 静态侧在 `BuildLookup` 里查重（P9）。
- [x] RUN(RuntimeSpriteSetTests) 绿；RUN(ResolveSpriteTests / SpriteResolverTests / BtnStateTests) 不回归；lint

### Task 3：按需集的 key 状态机（注册表层，经 internal `Request` 测）

- [x] **Red**：`Request_completes_synchronously_via_raw_OnCompleted`（尽早验证：在 ACS 上 `SetResult` 时，裸的 `awaiter.OnCompleted` 会同步执行）、
      `OnDemand_set_in_sync_attribute_errors_once_even_when_cached`、`Request_invokes_provider_once_per_key`、
      `Sync_completed_request_is_ready_without_waiting`、`Pending_request_settles_on_completion`、`Provider_default_result_marks_key_missing`、
      `Provider_sync_throw_marks_missing_and_errors_once`、`Provider_async_fault_marks_missing_and_errors_once`、
      `Provider_returning_null_awaitable_marks_missing`、`Empty_key_is_missing_without_calling_provider`、
      `Reentrant_request_for_same_key_does_not_reinvoke_provider`、`Result_after_unregister_is_dropped`、
      `Result_for_old_registration_does_not_touch_reregistered_set`、`Tiled_result_registers_render_hint`
- [x] **实现**：
  ```csharp
  // 先入表再调取图函数（P8）；只取一个 awaiter，结果恰好取一次
  var entry = new KeyEntry { State = KeyState.Loading }; reg.Keys[key] = entry;
  Awaitable<RuntimeSprite> aw;
  try { aw = reg.Load(key); } catch (Exception e) { Settle(reg, key, entry, default, e); return entry; }
  if (aw == null) { Settle(reg, key, entry, default, null); return entry; }
  var awaiter = aw.GetAwaiter();
  if (awaiter.IsCompleted) SettleFrom(reg, key, entry, () => awaiter.GetResult());
  else awaiter.OnCompleted(() => SettleFrom(reg, key, entry, () => awaiter.GetResult()));
  ```
  - `Settle` 幂等：条目已不是 Loading 直接返回；`!reg.Alive` 时丢弃。
  - 有结果时先登记 tiled hint，再置 Ready / Missing，然后快照等待者逐个通知（各自 `try/catch`）。
  - 三张去重表按 P12。
- [x] RUN(RuntimeSpriteSetTests) 绿；lint

### Task 4：诊断 + 与静态集共存

- [x] **Red**：`Failure_message_lists_runtime_sets_and_does_not_say_not_loaded`、`Runtime_set_survives_UseSpriteSetResolver_rebind`、
      `Runtime_set_survives_sprite_hot_reload_rebuild`、`LoadedSpriteSetNames_lists_static_sets_only`、`Static_collision_throws_before_touching_loaded_names`
- [x] **实现**：
  - 失败消息里的已加载名单 = 静态集 ∪ 运行时集，后者标注 `(runtime)` / `(runtime, on demand)`；
  - `SpriteResolver == null` 时的报错也列出运行时集；
  - P9 的预检。
- [x] RUN(RuntimeSpriteSetTests) 绿；RUN(SpriteSetTiledEntryTests / SpriteHotReloadTests / SpriteRenderHintsTests) 不回归；lint

### Task 5：广播 + 生命周期

- [x] **Red**：`Eager_register_after_open_refreshes_xml_declared_btn_sprite`、`Eager_unregister_broadcasts_once`、
      `OnDemand_register_and_unregister_do_not_broadcast`、`UnloadAll_keeps_runtime_sets`、`ResetForTests_clears_sets_and_drops_pending_results`、
      `ClearRuntimeRegistrations_clears_sets`；守卫 `#if UNITY_6000_5_OR_NEWER` 内另加 `Play_mode_entry_clears_runtime_sets`
- [x] **实现**：
  - 整包集在注册、注销时各调一次 `VariantStore.NotifyChangedInternal()`；
  - P1 的入口和三个钩子。
- [x] RUN(RuntimeSpriteSetTests) 绿；lint

### M1 收尾

- [x] 回归，全绿：
  - RUN：`ResolveSpriteTests`、`SpriteResolverTests`、`SpriteResolverLoadInFlightTests`、`SpriteHotReloadTests`、`SpriteRenderHintsTests`、
    `SpriteSetTiledEntryTests`、`RuntimeSourceAttributionTests`、`TabTests`、`DecorSpriteTests`、`BtnStateTests`、`ProceduralBuildersTests`、
    `UIResetEventTests`、`HotReloadTests`；
  - 装了 Addressables 时加 `AddressableSpriteResolverTests`；
  - 在 6000.5+ 宿主上加 `CommonLibraryTests`。

  lint
- [x] `git commit -m "feat(sprites): runtime sprite sets — register / unregister, resolved before UI.SpriteResolver"`（连同新 `.meta`）

## M2 — AsyncSpriteSlot + Icon / Image

### Task 6：Control 管道（`ControlSizeFromNativeTests`）

- [x] **Red**：`Native_keyword_sets_SizeFromNative`、`Omitted_size_in_free_positioning_sets_SizeFromNative`、
      `Explicit_size_clears_SizeFromNative_on_next_pass`、`Stretched_axis_does_not_set_SizeFromNative`、`Grid_cell_child_does_not_set_SizeFromNative`、
      `Stack_child_with_omitted_axis_sets_SizeFromNative`、`Stack_cross_fill_axis_does_not_set_SizeFromNative`、
      `Stack_child_with_both_axes_written_does_not_set_SizeFromNative`、`InApplyPass_is_true_only_inside_own_apply`（Probe 控件）
- [x] **实现**：P10 的 `SizeFromNative`；P4 的 `InApplyPass`
- [x] RUN(ControlSizeFromNativeTests) 绿；RUN(LayoutRebuildDirtyTests / ControlApplyCommonLayoutGroupTests / HugSizingTests) 不回归；lint

### Task 7：slot 核心 + Icon（`AsyncSpriteSlotTests`）

- [x] **Red**：`Pending_shows_Loading_then_result`、`Sync_completed_provider_never_shows_Loading`、`Same_key_requested_once_across_icons`、
      `Code_written_icon_name_refreshes_on_arrival`（核心）、`Stale_arrival_after_key_change_is_ignored`、`Arrival_after_destroy_is_silent`、
      `ReSolve_while_pending_does_not_rerequest_or_flash`、`Provider_null_shows_Missing_and_warns_once`、`Provider_exception_shows_Missing_and_errors_once`、
      `Variant_override_switches_ondemand_key`、`Pending_with_null_Loading_draws_nothing`、`Missing_with_null_placeholder_draws_nothing`、
      `Reentrant_set_from_provider_leaves_slot_consistent`、`Icon_static_value_reresolves_on_every_set`；
      守门测试保持绿：`IconRuntimeStateTests`、`SpriteResolverLoadInFlightTests`、`MessageBoxIconRuntimeStateTests`
- [x] **实现**：
  - `AsyncSpriteSlot`：
    - 状态 `None / Static / WaitingStaticResolver / Ready / Loading / Missing / Detached`；
    - `Set`：值与当前相同且处于运行时状态 → 不做事；否则 `++_gen`、`Release()`、`Resolve()`；
    - 记下最近一次 Set 是否发生在 pass 内（供 P6 使用）；
    - 运行时状态的赋值传 `hideIfNull`，并按 P3 刷新派生状态；
    - 订阅不挂进 `Control.Track`。
  - `FxImage.DrawNothingWhenEmpty`（P5）。
  - Icon：显式实现 `ISpriteSlotHost`。
    - `ResolveStatic` 搬今天的逻辑，返回三态；
    - `RefreshDerived` 为 `Flush`；
    - `Name` 改为 `_name = value; _slot.Set(value)`；
    - `OnAfterApply` 改为 `Flush` 后调 `_slot.AfterPass()`。
- [x] RUN(AsyncSpriteSlotTests) 绿；RUN(IconRuntimeStateTests / SpriteResolverLoadInFlightTests / MessageBoxIconRuntimeStateTests / RuntimeSourceAttributionTests) 不回归；lint

### Task 8：注册 / 注销唤醒 + 清扫

- [x] **Red**：`Unregister_clears_then_reregister_refreshes`、`Detached_icon_draws_nothing`、`Unknown_set_errors_then_heals_on_register`、
      `Destroyed_slots_are_pruned_from_registry_tables`（断言 internal 计数）、`Register_from_inside_arrival_callback_is_safe`
- [x] **实现**：
  - 三张表都用 `AddPruned`（P7）；
  - 注册：先唤醒按集名等待表，再按需广播；
  - 注销：作废注册 → 快照 `Bound` → 各 slot 清空、转 Detached、进按集名等待 → 再按需广播；
  - 每个 slot 的处理单独 `try/catch`。
- [x] RUN(AsyncSpriteSlotTests) 绿；lint

### Task 9：Image

- [x] **Red**：`Image_pending_shows_Loading_then_result`、`Late_arrival_rederives_image_state`（contain 宽高比；带 border 的图变成 Sliced）、
      `Sync_hit_and_late_arrival_derive_same_image_type`、`Explicit_type_is_kept_on_late_arrival`、`Code_written_static_image_sprite_keeps_previous_type`
- [x] **实现**：
  - 拆出 `RefreshSpriteDerivedState()`。
  - `OnAfterApply` 顺序：刷新派生状态 → `_slot.AfterPass()` → `_raycast.EndPass()`。
  - `ResolveStatic` 的三态判定：
    - 值带 `:`、resolver 为 null、且正在加载 → `Deferred`；
    - 否则调 `ResolveSpriteStatic`，得到 sprite → `Ok`；值带 `:` 却解析为 null → `Failed`；
    - 不带 `:` 的 Resources 路径总是 `Ok`，sprite 可能为 null（与今天一样静默）。
- [x] RUN(AsyncSpriteSlotTests) 绿；RUN(ImageFitTests / FxImageTests / ImageFxRenderTests / ImageNativeSizeTests / ImageMaskTests / RaycastTargetTests) 不回归；lint

### Task 10：尺寸告警

- [x] **Red**：`Native_sized_icon_on_ondemand_set_warns_once`、`Explicit_size_does_not_warn`、`Eager_set_native_size_does_not_warn`、
      `Image_cover_without_size_does_not_warn`、`Grid_cell_icon_without_size_does_not_warn`、`Code_written_ondemand_name_on_native_icon_warns`、
      `Bound_rows_warn_once_per_template_node`
- [x] **实现**：
  - 两个检查点：`AfterPass`，以及 apply 外的 `Set`；
  - 按 `SourceNode` 去重（P10），去重表由 P1 清空；
  - Image 的 `SizeDependsOnSprite = SizeFromNative && !(fitter 已启用)`。
- [x] RUN(AsyncSpriteSlotTests) 绿；lint

### Task 11：行复用 + 真实帧调度

- [x] **Red**：
  - `Reused_row_same_key_still_receives_arrival`：用 `ScrollListReuseTests` 的 Push 写法，行模板里放 `<Icon id='icon' size='32'/>`，加载期间再推一次同样的数据。
  - PlayMode `RuntimeSpriteSetPlayTests.Ondemand_icon_arrives_after_real_frames`：取图函数里 `await Awaitable.NextFrameAsync()`，`[UnityTest]` 等几帧后断言。
- [x] **实现**：预期不需要改代码。若 Red 测试一上来就是绿的，只说明订阅不挂在 `Track` 上这一点成立，不算失败。
- [x] RUN(AsyncSpriteSlotTests) 绿；RUNPLAY(RuntimeSpriteSetPlayTests) 绿

### Task 12：静态 resolver 加载中（§1.3）

- [x] **Red**：`Code_written_icon_name_during_static_load_refreshes_on_End`（今天必红）、`Code_written_native_icon_gets_native_size_after_End`、
      `End_without_installed_resolver_stays_silent`、`Xml_declared_static_miss_after_load_logs_once`、
      `Waiting_slot_heals_when_runtime_set_registers_during_static_load`
- [x] **实现**：P6（先处理静态等待表，再广播；同时登记按集名等待表）。
- [x] RUN(AsyncSpriteSlotTests) 绿；RUN(SpriteResolverLoadInFlightTests) 不回归；lint

### M2 收尾

- [x] 回归，全绿：
  - RUN：`IconRuntimeStateTests`、`MessageBoxIconRuntimeStateTests`、`SpriteResolverLoadInFlightTests`、`RuntimeSourceAttributionTests`、
    `ColorTokenIntegrationTests`、`ImageTintTests`、`FxImageTests`、`ImageFxRenderTests`、`ImageFitTests`、`ImageNativeSizeTests`、`ImageMaskTests`、
    `ImageRotateFlipTests`、`RaycastTargetTests`、`GradientFlipOrderTests`、`IntensityRenderTests`、`ControlApplyCommonLayoutGroupTests`、
    `ControlApplyCommonFractionalTests`、`ControlApplyCommonClampTests`、`FlowAttributeTests`、`HugSizingTests`、`LayoutRebuildDirtyTests`、
    `CommonAttrRuntimeStateTests`、`ScrollListReuseTests`、`ScrollListKeyedTests`、`ScrollListVirtualTests`、`ScrollListGridTests`、
    `DynamicSubtreeReSolveTests`、`AnimationTests`；
  - RUNPLAY：`IconRuntimeTests`、`ImageFxPlayTests`、`ImageTests`、`GridTests`、`RuntimeSpriteSetPlayTests`。

  lint
- [x] `git commit -m "feat(sprites): Icon / Image refresh themselves when on-demand sprites arrive"`

## M3 — 运行时 .po 目录

### Task 13：分层的 `TranslationStore`（`TranslationStoreTests` 追加）

- [x] **Red**：`Layer_entry_overrides_base`、`Later_layer_overrides_earlier_layer`、`Removing_layer_restores_base`、`UnloadLocale_clears_base_and_layers`、
      `UnloadAll_clears_layer_entries_but_keeps_layers`、`Load_into_removed_layer_is_ignored`、`Empty_msgstr_in_layer_falls_through`
- [x] **实现**：P13。`Lookup` 从最后一层往前查，最后才查底层；公开签名不变；没有层时查表路径和今天一样。
- [x] RUN(TranslationStoreTests) 绿；RUN(TrResolverTests) 不回归；lint

### Task 14：目录注册（`RuntimePoCatalogTests`）

- [x] **Red**：`Duplicate_catalog_name_throws`、`Invalid_catalog_arguments_throw`、`Unregister_unknown_catalog_returns_false`、
      `Registered_before_Set_is_loaded_on_Set`、`Registered_after_Set_loads_current_and_retranslates_open_text`、`Unregister_restores_builtin_translation`、
      `Later_catalog_overrides_earlier_and_builtin`、`RegisterAsync_propagates_loader_exception`、`RegisterAsync_failure_keeps_registration_for_next_switch`、
      `Unregister_during_pending_load_drops_result`
- [x] **实现**：
  - `RegisterRuntimeCatalogAsync` 写成非 async 的包装：先同步校验、登记、`AddLayer`，再返回加载用的 Awaitable。重名会同步抛出。
  - `RegisterRuntimeCatalog` 是发出即不管的版本，配一个 `*Logged` 包装。
  - 加载完成后的守卫：`Current` 未变，且目录仍是同一个实例。通过后 `LoadLayer`，再 `NotifyChangedInternal`。
  - `UnregisterRuntimeCatalog`：移除登记 → `RemoveLayer` → 广播。
- [x] RUN(RuntimePoCatalogTests) 绿；lint

### Task 15：切语言集成 + 生命周期

- [x] **Red**：`Locale_switch_loads_catalog_for_new_locale`、`Catalog_failure_does_not_block_locale_switch`、`Stale_catalog_load_after_locale_switch_is_dropped`、
      `ReloadCurrent_reloads_catalogs`、`Set_with_sync_catalogs_completes_synchronously`、`Pending_catalog_delays_variant_flip_until_loaded`、
      `ResetForTests_clears_catalogs`、`ClearRuntimeRegistrations_clears_catalogs_and_layers`
- [x] **实现**：`LoadPoFilesAsync`（`UI.cs:555-567`）在底层加载完之后：
  - 若 `Current != locale` 就返回；
  - 否则 `await LoadRuntimeCatalogsAsync(locale)`：
    - 先启动全部目录的 load，同步抛出的单独 catch；
    - 再按顺序各 await 一次；
    - 每个目录单独 `try/catch` + `Debug.LogError`；
    - 通过守卫后 `LoadLayer`；
    - 没有目录时同步返回。
  - P1 的入口里加入清空目录和 `ClearLayers`。
- [x] RUN(RuntimePoCatalogTests) 绿；lint

### M3 收尾

- [x] 回归，全绿：
  - RUN：`TranslationStoreTests`、`TrResolverTests`、`LocaleSetTests`、`LocaleSetAsyncTests`、`LocaleInitializeIfNeededTests`、
    `LocaleSetToSystemDefaultTests`、`LocaleFontCopierTests`；
  - RUNPLAY：`I18nHotReloadTests`、`TmpRichTextRoundtripTests`、`I18nFontSwapTests`；
  - 装了 Addressables 时加 `LocaleAddressableResolverTests`。

  lint
- [x] `git commit -m "feat(i18n): runtime .po catalogs layered over TranslationStore"`

## M4 — 文档 + 全量回归

### Task 16：Skills（英文）+ master spec

- [ ] `scripting-promptugui-csharp/SKILL.md` 新增一节 *Runtime sprite sets (downloaded / UGC packs)*，内容：
  - 两种形态与 API；
  - 取图契约表：主线程完成、每次返回新的 Awaitable、预期内失败返回 `default`、自带超时；
  - 所有权，以及注销措辞（P5 / spec §5.4）；
  - 占位与日志；
  - 哪些属性能用按需集；
  - 尺寸规则；
  - 注册 / 注销 / 重注册；
  - 重名规则，包括与进行中的 Addressables 静态加载撞名时的行为；
  - 不要在 SubsystemRegistration 阶段注册；
  - 取图函数示例：参照 `UI.Markdown.LoadWebTextureAsync`（`UnityWebRequestTexture` + ACS），并附 KTX2 建议；
  - `Awaitable<Sprite>` / `Dictionary<string, Sprite>` 不会隐式转换，要写成 `RuntimeSprite`。
- [ ] 同一文件的其它几处：
  - *`sprite=` dual-syntax* 补解析顺序；
  - *Error handling* 补 §1.3 的修复，以及「重绑 / 热重载期间不覆盖」；
  - *Locale & i18n* 新增 *Runtime .po catalogs*（loader 自带超时；fire-and-forget 用同步版）；
  - cheatsheet；
  - *Common mistakes* 加一行：UGC 图标看不见 → 没写定尺寸。
- [ ] `authoring-promptugui-xml/SKILL.md`：`<Icon>` 的 `name` / `size`、`<Image>` 的 `sprite` 各加一句。
- [ ] `reference/icons.md` 新增一节 *Runtime sprite sets*。
- [ ] `using-promptugui-addressables/SKILL.md` 加一行；master spec §5.4 加一行。

### Task 17：全量回归 + 实施记录

- [ ] 全量跑 `PromptUGUI.Tests.EditMode`、`PromptUGUI.Tests.EditorOnly`、`PromptUGUI.Tests.PlayMode`，核对 `summary.total`。
- [ ] `dotnet format --verify-no-changes --severity warn` 干净。
- [ ] 另一台宿主看 console 无编译错误。
- [ ] spec 末尾写「实施记录」（与设计的偏差、未做 / 另案），状态改为「已实现」；plan 打勾。
- [ ] `git commit -m "docs(skills): runtime sprite sets and runtime .po catalogs"`，然后汇报；push 与 PR 征得同意后再做。

## Verification（端到端）

1. 新测试类全绿：`RuntimeSpriteSetTests`、`ControlSizeFromNativeTests`、`AsyncSpriteSlotTests`、`RuntimePoCatalogTests`、
   `TranslationStoreTests`（追加的部分）、PlayMode 的 `RuntimeSpriteSetPlayTests`。
2. Red 证据：`Code_written_icon_name_refreshes_on_arrival` 和 `Code_written_icon_name_during_static_load_refreshes_on_End`
   改动前是红的、改动后是绿的；`Icon_silent_when_load_in_flight` 全程保持绿（P6）。
3. 三个测试程序集全量零回归；lint 干净；本次没有 `.ui.xml` 改动，不需要 UIXmlLint。
4. 6000.0（PromptUGUIDev）和 6000.7（ssw_re_client）都编译通过：MCP 连着的那台跑全量，另一台只看 console。
