# ScrollList 行虚拟化 + key 绑定 + 贴底（VIR）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

## Context

宿主 ssw_re_client 的聊天面板（`Round.ui.xml` 四个叠放的频道 `<ScrollList>`，行模板 `ChatLine` = 时间 + 会折行的正文）实测：
频道满了以后每来一条消息，所有行按位置重绑、N 个 TMP 全部重排，20 行 6.6 ms → 200 行 70 ms（桌面编辑器）；就算改成按 key 复用，
一次推送仍要碰全部 N 行（bind × N、VLG/CSF 遍历 N 个子节点、Canvas 合批 N 行），外推 1000 行约 50 ms。目标：**一个频道 1000 条
纯文字消息，来一条的成本与条数无关**。

设计见 spec `docs~/superpowers/specs/2026-09-29-scrolllist-virtualization-design.md`（下文 §x 均指它，已与作者对齐）。本计划分三步提交
（M0 key → M1 虚拟化核心 → M2 作者面收尾），全部在分支 `feat/scrolllist-virtualization`（已建，spec 尚未提交）。计划阶段经三路代码勘查
与一轮独立评审，修正了 spec 的几处（VIR-P1…P11），Task 0 先把 spec 改齐再提交。

**Goal:** `<ScrollList virtualize="true" stickToEnd="true">` + `BindItems(..., key: m => m.Id)` + `ScrollToStart/End/Index` / `IsAtEnd` /
`OnAtEndChanged` / `ItemCount`。验收：1000 与 10000 条下「裁头 + 追加一条」同量级（目标桌面编辑器 ≤ 2 ms），Content 下的行数只与视口有关。

**Architecture:** 虚拟模式下 Content 仍是 `VerticalLayoutGroup` 的子类 `WindowedVerticalLayoutGroup` + `ContentSizeFitter`，只放视口附近
一段行，前后用浮点 `Leading` / `Trailing` 撑出虚拟总高（§5.3）；行仍是 Content 的直接子节点，语义与今天相同。`VirtualLayoutModel`（纯数学）
存每项高度与状态，行被实现后 `ForceRebuildLayoutImmediate(Content)` 读实测高度，未实现的用均值估算（§5.1）。粘边是记住的状态，其余时候锚住
首个可见项；滚动路径只平移、推送路径才夹取（VIR-P5 / P6）。推送经 `ItemBinding` 产出 `newToOld` 下标映射，两种模式共用（§5.5）。滚动驱动挂在
`ScrollRect` 子类 `PuiScrollRect.LateUpdate`（`base` 之后），它也负责拖动中平移起点、刷新滚动条（§5.8）。

**Tech Stack:** Unity 6 / C# 9、uGUI（`ScrollRect` / `VerticalLayoutGroup` / `ContentSizeFitter` / `LayoutRebuilder`）、TMP、R3。无新包。

## 已对齐的决策

spec §12 的 VIR-D1…D13 沿用。计划阶段补充 / 修正（Task 0 写回 spec）：

| # | 决策 | 为什么 |
|---|---|---|
| VIR-P1 | 虚拟化组合规则放新的 `ScrollListRules.CheckVirtualize`，**只由 `IRWalker`（CLI）调用**；运行时由 `ScrollList` 自己 `UILog.Warn` 一次并降级 | `.lint/UIXmlLint/README.md`：「作者写了但被忽略」只在 CLI 报；控件看得到 `class=` 合并后的真值，不与镜像重复 |
| VIR-P2 | HUG 规则在 `HugRules` 里加 `IsOpenEndedHug`（裸 `hug` 或 `clamp(N, hug, _)`），复用它的 clamp 拆分 | `SizeSpec` 引用 UnityEngine、不在 CLI 编译集；不另写解析 |
| VIR-P3 | 非虚拟 `ScrollToIndex` 的上界是 `SlotCount`（含静态子节点），虚拟是 `ItemCount` | 纯静态列表 `ItemCount` 为 0 |
| VIR-P4 | `PositionalBinding<T,TSlot>` / `KeyedBinding<T,TSlot,TKey>` 共用非泛型 `IItemBinding`。上一次推送来自任一同 `TKey` 的 key 绑定（`IKeyIndex<TKey>`）→ **行**按 key 复用；但虚拟模式的**锚点**把「来自另一个绑定对象的推送」当首推 | 常见写法是每次推送都 `BindItems(Observable.Return(list), …)`，只认同一绑定对象的话 key 永不生效；而同一 `TKey` 类型不代表同一种 key（消息 id vs 用户 id），不能让锚点跟着巧合相等的 key 跳 |
| VIR-P5 | **粘边是记住的状态**（`_stuckToStart` / `_stuckToEnd`，两种模式共用）：同步、`ScrollTo*`、首推设置它；只有**用户造成的移动**之后才按几何重算——判据是 content 位置 ≠ 列表上次写入的位置（拖动、滚轮、拖滚动条、惯性、回弹都属于这类） | 内容第一次溢出时滚动条出现、视口变窄、行重新折行变高，这发生在 `LateUpdate` 之后的 Canvas 布局里；每次同步按几何判断会让聊天永远掉离底部 |
| VIR-P6 | **滚动路径的同步只平移 S**：锚项位移多少挪多少；粘起点 = 不动；粘终点 = 保持 `S − (total − V)`。**夹取只在推送时**，且没在拖动、`velocity == 0` | 否则顶端 / 底端的弹性回弹每帧被掐掉 |
| VIR-P7 | 同步期间来的推送与 `ScrollTo*` 入队（推送只留最新一个），同步结束后执行；`OnAtEndChanged` 在同步结束后才发 | bind 回调或 `OnAtEndChanged` 订阅者推送时不重入、不改动对账中的簿记 |
| VIR-P8 | 绑定对象把推送的列表拷进自己的可复用缓冲（稳态零分配，O(N) 拷贝） | 虚拟列表在滚动时惰性 bind；宿主原地改了推送过的列表也不会越界或绑错 |
| VIR-P9 | 模型只存高度与状态（Unknown / Stale / Measured / **Collapsed**：隐藏或 `flow="false"` 的行不占高、不占间距）；spacing / padding 每次同步从活的布局组读；N > 0 时窗口至少 1 行；外部改高按窗口内逐行比对检测 | `ApplyGroupMetrics` 每次 ReSolve 以 HashSet 顺序重写组参数；VLG 不给非激活 / `ignoreLayout` 子节点算间距；总高逐帧比对会被浮点误差与折叠行骗 |
| VIR-P10 | `PuiScrollRect` 的钩子只在 `Application.isPlaying` 时驱动同步；EditMode 测试显式调 `RefreshWindow()` / tick | 它和基类一样 `[ExecuteAlways]`，编辑器 tick 也会跑 `LateUpdate` |
| VIR-P11 | 演示做成 CommonControls 的第 5 个页签 | 列表页在横屏只剩约 100 高，放不下第二个列表；spec §10 同步改 |

## Global Constraints

- **分支** `feat/scrolllist-virtualization`，**绝不提交到 main**。LangVersion 9.0（无 primary constructor / `[]` / `[field: SerializeField]`、不用 `record`）。不用 `System.Threading` / `Task`。
- **Core 纯 C#**：`Runtime/Core/Lint/*` 不得 `using UnityEngine`、不得依赖 `PromptUGUI.Application`。
- **`[UIAttr]` 必须同时 `[Preserve]`**；属性名 = 属性名首字母小写。事件接口一律 `Observable<T>`（主 spec §9.4）。
- **R3 吞异常**：`OnNext` 里抛的异常（推送校验失败、bind 异常）经 R3 未处理异常处理器记日志，**不会抛回推送方**。测试用 `LogAssert.Expect(LogType.Exception, regex)` 再断言列表未变（先例 `ScrollListStaticChildrenTests.cs:116`），不能 `Assert.Throws`；SKILL 里写「记日志」而不是「抛出」。
- **Red first**；每个 Task 收尾 lint：`cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`（**禁止** `--severity info`）。动过 `Core/Lint` 后跑 `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/`。
- **测试只经 Unity MCP**；**禁止** `execute_menu_item("Assets/Reimport All")`。
- **EditMode 限制**：`LateUpdate` 不跑（tick 显式调）；`ScrollRect.vScrollingNeeded` 恒为 true → 视口比列表窄「条厚 + 间距」，窗口与宽度断言一律读 `Viewport` / `Content` 的 rect；`Time.frameCount` 在 `[Test]` 内不前进（「非出生帧」只能 PlayMode 验）；`Destroy` 即时（延迟销毁只能 PlayMode 验）。
- **零回归守门**（每个里程碑末尾）：`ScrollListTests` `ScrollListReuseTests` `ScrollListStaticChildrenTests` `ScrollListContentSizingTests` `ScrollListGridTests` `ScrollListReorderTests` `ScrollListReorderTriggerTests` `ScrollListCrossAxisClipTests` `HugSizingTests` `DynamicSubtreeReSolveTests` `ScrollbarTests` `CarouselTests` `ScrollListRulesTests` `ScrollListReorderRulesTests` + PlayMode `ScrollListPlayTests` `ScrollListReorderPlayTests`。
- **可复用的现成件**：夹具 `ScrollListReuseTests.Open/Push/CountingDisposable`、`ScrollListReorderTests.Rt/ScreenOf/Offset/LabelsInSiblingOrder`、甩动写法 `PixelSnapPlayTests`（直接写 `velocity`）；运行时 `UILog.Warn/Error(Control, string)`、`BornFrame.Capture/IsCurrent`（先例 `PuiButton`）、`UI.OwnerScreenOf(control).RootGameObject`、`ExceptionDispatchInfo`（先例 `AwaitableHelpers.cs:50`）、`Screen.LiveDynamicSubtreeCount`、`ScrollListRules.DeclaresGrid` 的读法、`HugRules` 的 clamp 拆分。

**RUN(Class)** = `refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` → `read_console(types=["error"])` 必须无编译错误 → `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["Class"])` → 轮询 `get_test_job`，核对 `summary.total > 0`。
**RUNPLAY(Class)** 同上，`mode="PlayMode"`、`assembly_names=["PromptUGUI.Tests.PlayMode"]`（连跑前先 force refresh）。**RUNEDITOR(Class)** 同上，`assembly_names=["PromptUGUI.Tests.EditorOnly"]`。

---

## File Structure

| 文件 | 责任 | 动作 |
|---|---|---|
| `Runtime/Controls/Internal/ItemBinding.cs` | `IItemBinding` / `IKeyIndex<TKey>` / `PositionalBinding` / `KeyedBinding`：快照、校验、产出 `newToOld`、按下标 bind | Create |
| `Runtime/Controls/Internal/VirtualLayoutModel.cs` | 纯数学：高度与状态、估算、前缀和、区间、搬移、失效 | Create |
| `Runtime/Controls/Internal/WindowedVerticalLayoutGroup.cs` | 浮点 `Leading` / `Trailing` 的 `VerticalLayoutGroup` | Create |
| `Runtime/Controls/Internal/PuiScrollRect.cs` | `ScrollRect` 子类 + `IScrollTickHost`：钩子、`IsDragging`、`ShiftContentY`、用户移动判据 | Create |
| `Runtime/Controls/ScrollList.cs` | key 重载、映射重建、Pool、虚拟模式、粘边状态机、`virtualize` / `stickToEnd`、滚动 API、`IsAtEnd` / `OnAtEndChanged` / `ItemCount`、降级警告 | Modify |
| `Runtime/Application/ScreenInstantiator.cs` | `PreConfigureContent` 多传 `virtualize` | Modify |
| `Runtime/Core/Lint/ScrollListRules.cs` · `HugRules.cs` · `IRWalker.cs` | `PUI-SCROLL-VIRTUAL-*` 六码、`IsOpenEndedHug` | Modify |
| `Tests/EditMode/Controls/`：`ItemBindingTests` `ScrollListKeyedTests` `VirtualLayoutModelTests` `WindowedVerticalLayoutGroupTests` `PuiScrollRectTests` `ScrollListVirtualModeTests` `ScrollListVirtualTests` `ScrollListScrollApiTests` `ScrollListVirtualBenchmark` | EditMode | Create |
| `Tests/PlayMode/Controls/ScrollListVirtualPlayTests.cs` | PlayMode（Task 5 建，Task 10 补全） | Create |
| `Tests/EditMode/Lint/ScrollListVirtualRulesTests.cs` · `Tests/EditMode/Editor/XsdGeneratorTests.cs` | lint · XSD | Create · Modify |
| 两份 SKILL · `reference/virtualize.md`（新）· `reference/reorder.md` · `AGENTS.md` · `.lint/UIXmlLint/README.md` · 主 spec §9.5 | 文档 | Modify / Create |
| `Samples~/CommonControls/…ui.xml` · `CommonControlsRunner.cs` · 两份 `CommonControls.po` · `package.json` | 演示 | Modify |
| `docs~/superpowers/plans/2026-09-29-scrolllist-virtualization.md` | 本计划落盘 | Create |

---

## M0 — key 绑定（两种模式都生效）

### Task 0：spec 对齐 + 计划落盘 + 提交文档

- [ ] 改 spec：§4.3 表头说明（VIR-P1）与 HUG 行（VIR-P2）；§5.1 加 Collapsed 状态、模型不缓存 spacing / padding、窗口至少 1 行（VIR-P9）；§5.4 改为「粘边是记住的状态」+「滚动路径只平移、推送才夹取」+「无相交行时从模型取 S 处的项」（VIR-P5 / P6）；§5.5 跨绑定规则（VIR-P4）与快照（VIR-P8）；§5.6 加重入入队（VIR-P7）；§5.8 `ShiftContentY` 刷新滚动条的方式与 isPlaying 门控（VIR-P10）；§10 演示改第 5 页（VIR-P11）
- [ ] 把本计划写到 `docs~/superpowers/plans/2026-09-29-scrolllist-virtualization.md`
- [ ] `git add` 这两份 → `git commit -m "docs: ScrollList virtualization — spec + plan"`

### Task 1：`ItemBinding`

**Files:** Create `Runtime/Controls/Internal/ItemBinding.cs`、`Tests/EditMode/Controls/ItemBindingTests.cs`

- [ ] **Red**（纯逻辑，假 `IControl` 当行；key 用 `string` 与 `int` 各一组）：
  - `Positional_maps_by_index`（3→5 `[0,1,2,-1,-1]`；5→2 `[0,1]`；首推全 -1）
  - `Keyed_append / trim_front / prepend / permute`（`[1,2,-1]`、`[-1,0,1]`、`[2,0,1]` …）
  - `Keyed_after_another_keyed_binding_with_the_same_key_type_maps_by_key`；`Keyed_after_a_positional_binding_is_positional`
  - `Duplicate_key_throws_and_keeps_the_last_accepted_push`（消息含两个下标与 key；之后合法推送仍对上次**被接受**的推送 diff）；`Null_key_throws`
  - `Items_are_snapshotted`（推送后改宿主列表，`TryBind` 仍绑原值）
  - `TryBind_returns_false_for_the_wrong_slot_type`
- [ ] **实现**：
  ```csharp
  internal interface IItemBinding { int Count { get; } Type SlotType { get; } bool TryBind(IControl row, int index); }
  internal interface IKeyIndex<in TKey> { bool TryGetIndex(TKey key, out int index); }   // 上一次被接受的推送
  internal abstract class ItemBinding<T, TSlot> : IItemBinding where TSlot : class, IControl
  {
      readonly Action<TSlot, T> _bind;
      protected readonly List<T> Items = new();                 // 快照（VIR-P8），Clear + AddRange 复用
      public int Count => Items.Count;
      public Type SlotType => typeof(TSlot);
      public bool TryBind(IControl row, int i) { if (row is not TSlot t) return false; _bind(t, Items[i]); return true; }
      /// 先校验、失败即抛且不改状态；成功后 newToOld[0..items.Count) = 相对「当前行」的来源下标（-1 = 新项）。
      public abstract void Accept(IReadOnlyList<T> items, IItemBinding previous, int previousCount, ref int[] newToOld);
  }
  // KeyedBinding<T,TSlot,TKey> : ItemBinding<T,TSlot>, IKeyIndex<TKey>：_keysBuf 只调一次 key 委托；_next 字典（ContainsKey + Add）
  // 校验 null / 重复；previous is IKeyIndex<TKey> → 逐项 TryGetIndex，否则按位置；成功后交换 _current / _next（两本字典复用）。
  ```
- [ ] RUN(ItemBindingTests) 绿；lint

### Task 2：`ScrollList` 带 key 的 `BindItems` + 按映射重建 + 退役行先移出 Content

**Files:** Modify `ScrollList.cs`；Create `Tests/EditMode/Controls/ScrollListKeyedTests.cs`

- [ ] **Red**（夹具照抄 `ScrollListReuseTests`；两种推送方式都覆盖：每次新 `BindItems(Observable.Return(...), …, key)` 与同一个 `Subject` 多次 `OnNext`）：
  1. `Keyed_append_keeps_every_row_and_adds_one`（旧行 `AreSame`，标签兄弟序 `a,b,c,d`）
  2. `Keyed_trim_front_destroys_only_the_first_row`（旧 a 行 `GameObject == null`）
  3. `Keyed_prepend_keeps_old_rows_behind_new_ones`
  4. `Keyed_permutation_moves_rows_with_their_keys`（每行文本不变；**`Slots[i]` 的宿主就是第 i 个兄弟**——`ReorderDriver` 依赖）
  5. `Duplicate_key_rejects_the_whole_push` / `Null_key_…`（`LogAssert.Expect(Exception, "share the key")`，行与标签不变）
  6. `Every_push_rebinds_every_row_and_releases_row_subscriptions`
  7. `ItemCount_is_the_last_push`；`Null_push_is_an_empty_list`；`A_push_after_Dispose_is_ignored`
  8. `Retired_rows_are_out_of_Content_when_bind_runs`（第二次推送的 bind 里 `ContentOf(list).childCount == 新项数`）
- [ ] **实现**：
  - 字段 `IItemBinding _binding; int _itemCount; int[] _remap = new int[16]; RectTransform _pool; readonly List<IControl> _prevSlots = new(); bool _disposed;`
  - `EnsurePool()`：列表根下 `"Pool"`，**挂一个 `enabled = false` 的 `VerticalLayoutGroup`**（`Control.ApplyCommon` 用 `GetComponent<LayoutGroup>()` 判父级，禁用的也算——停放 / 退役中的行被 ReSolve 重放时走布局组分支，`ChatLine` 那种 `width="stretch"` 的行根才不会抛），再 `SetActive(false)`。
  - 重载：原 `BindItems<T,TSlot>` → `PositionalBinding`；新增 `BindItems<T,TKey>(…, Func<T,TKey> key)` / `BindItems<T,TSlot,TKey>(…)` → `KeyedBinding`（`key` 为 null 抛 `ArgumentNullException`）；都走 `source.Subscribe(items => OnPush(binding, items ?? Array.Empty<T>()))`。
  - `OnPush`：`_disposed` → 忽略；`_factory == null` 照旧抛；`Accept(items, _binding, _itemCount, ref _remap)`；`_binding = binding; _itemCount = n`；非虚拟 → `Rebuild(binding, n)`。
  - `Rebuild`：`_reorder?.Cancel()`；`full` 条件同今天 → `ClearSlots()`；**先定结构**（按 `_remap` 从 `_prevSlots` 取活着的行并 `ReleaseSubscriptions()`，否则 `_factory(_content)`；没被取走的旧行 `Retire`；按需 `SetSiblingIndex`）**再逐行 bind**（`TryBind` 失败抛 `InvalidCastException`，消息与今天一致）。
  - `Retire(row)`：`HostOf(row).SetParent(EnsurePool(), false); row.Dispose();`——Play 下 `Destroy` 延到帧末，这一帧它已不占布局、不占兄弟位。`ClearSlots` 用 `Retire`；`ScrollList.Dispose` 的清表直接 `Dispose`，并置 `_disposed`。
  - `public int ItemCount => _itemCount;`
- [ ] RUN(ScrollListKeyedTests) 绿；RUN(ScrollListReuseTests) / RUN(ScrollListStaticChildrenTests) / RUN(ScrollListReorderTests) 不回归；lint

### M0 收尾

- [ ] 零回归守门全跑 → `git commit -m "feat(scrolllist): keyed BindItems — rows follow their items; retired rows leave Content before they are destroyed"`

---

## M1 — 虚拟化核心

### Task 3：`VirtualLayoutModel`

**Files:** Create `Runtime/Controls/Internal/VirtualLayoutModel.cs`、`Tests/EditMode/Controls/VirtualLayoutModelTests.cs`

- [ ] **Red**：`Offsets_and_total_follow_heights_spacing_and_padding`（10/20/30、spacing 2、pad 5/7 → offset 5/17/39、total 76）；`Unknown_items_use_the_mean_of_measured_ones`（测量前 `HasEstimate == false`）；`InvalidateAll_keeps_each_value_as_its_own_estimate_and_the_mean_as_fallback`；`Collapsed_items_take_no_height_and_no_gap`；`TryWindow_returns_the_items_that_intersect`（相切不算；N > 0 时至少返回离区间最近的 1 项）；`Remap_carries_heights_and_states`；`Extents_add_up_to_the_total`
- [ ] **实现**：每项 `extent = Collapsed ? 0 : h + spacing`，`OffsetOf(i) = padTop + Σ_{j<i} extent`，`Total = padTop + Σ extent − (有非折叠项 ? spacing : 0) + padBottom`；`ExtentBefore(f) = Σ_{j<f} extent`、`ExtentAfter(l) = Σ_{j>l} extent`（与 VLG 的 `(n−1)·spacing` 一致，前提是窗口非空）。**spacing / padTop / padBottom 由调用方每次 `SetMetrics(...)` 传入**，不自己缓存来源；`double` 前缀和按需 O(N) 重建；`TryWindow` 二分。
- [ ] RUN(VirtualLayoutModelTests) 绿；lint

### Task 4：`WindowedVerticalLayoutGroup`

**Files:** Create `…/WindowedVerticalLayoutGroup.cs`、`Tests/EditMode/Controls/WindowedVerticalLayoutGroupTests.cs`

- [ ] **Red**（裸层级，配置同 `ApplyLayoutMode`：`childControl*` / `childForceExpand*` 全 true + CSF vertical preferred；3 个 30 高子节点、spacing 4、padding 5/6）：`Leading_and_trailing_add_to_the_content_height`（= 5+100+90+8+200+6）；`Children_start_below_leading_and_keep_their_preferred_height`；`Zero_extents_match_a_plain_VerticalLayoutGroup`
- [ ] **实现**：
  ```csharp
  internal sealed class WindowedVerticalLayoutGroup : VerticalLayoutGroup
  {
      float _leading, _trailing;
      internal float Leading { get => _leading; set { if (_leading == value) return; _leading = value; SetDirty(); } }
      internal float Trailing { get => _trailing; set { if (_trailing == value) return; _trailing = value; SetDirty(); } }
      public override void CalculateLayoutInputVertical()
      {
          base.CalculateLayoutInputVertical();
          var extra = _leading + _trailing;   // min 与 preferred 同加：基类 surplusSpace = 0，childForceExpandHeight 不把空白分给行
          if (extra != 0f) SetLayoutInputForAxis(minHeight + extra, preferredHeight + extra, flexibleHeight, 1);
      }
      public override void SetLayoutVertical()
      {
          base.SetLayoutVertical();
          if (_leading == 0f) return;
          for (var i = 0; i < rectChildren.Count; i++) { var c = rectChildren[i]; var p = c.anchoredPosition; p.y -= _leading; c.anchoredPosition = p; }
      }
  }
  ```
- [ ] RUN(WindowedVerticalLayoutGroupTests) 绿；lint

### Task 5：`PuiScrollRect`

**Files:** Create `…/PuiScrollRect.cs`、`Tests/EditMode/Controls/PuiScrollRectTests.cs`、`Tests/PlayMode/Controls/ScrollListVirtualPlayTests.cs`（先放本任务的 PlayMode 用例）；Modify `ScrollList.cs`（`OnAttached` 直接 `AddComponent<PuiScrollRect>()`，`_scroll` 改类型，显式实现 `IScrollTickHost`，先空）

- [ ] **Red**：EditMode——`ScrollList_root_carries_a_PuiScrollRect`；`ShiftContentY_moves_the_content_and_the_scrollbar_value`；`ShiftContentY_during_a_drag_moves_the_drag_origin_too`（根上 `OnBeginDrag` → 平移 +50 → 原地 `OnDrag` → 不被拉回）；`IsDragging_tracks_begin_end_and_disable`。PlayMode——`Host_tick_runs_after_the_ScrollRect_moved_the_content`（写 `velocity` 后，钩子看到的 content 位置已是本帧移动后的）；`A_shift_while_dragging_does_not_turn_into_fling_velocity`；`Content_moved_by_the_user_is_reported_as_user_motion`（拖动 / 滚轮后 `ConsumeUserMotion()` 为 true，`ShiftContentY` 之后为 false）
- [ ] **实现**：
  ```csharp
  internal interface IScrollTickHost { void OnScrollLateUpdate(); void OnScrollEnabled(); }
  [ExecuteAlways, DisallowMultipleComponent]              // 与 ScrollRect 相同，不依赖特性继承
  internal sealed class PuiScrollRect : ScrollRect
  {
      internal IScrollTickHost Host;
      internal bool IsDragging { get; private set; }
      float _writtenY = float.NaN;                          // 列表最后写入的 content y
      protected override void LateUpdate() { base.LateUpdate(); if (UnityEngine.Application.isPlaying) Host?.OnScrollLateUpdate(); }   // VIR-P10
      protected override void OnEnable() { base.OnEnable(); Host?.OnScrollEnabled(); }
      protected override void OnDisable() { IsDragging = false; base.OnDisable(); }
      public override void OnBeginDrag(PointerEventData e) { base.OnBeginDrag(e); if (e.button == PointerEventData.InputButton.Left && IsActive()) IsDragging = true; }
      public override void OnEndDrag(PointerEventData e) { base.OnEndDrag(e); if (e.button == PointerEventData.InputButton.Left) IsDragging = false; }
      /// content 在列表上次写入之后被别人挪过（拖动 / 滚轮 / 滚动条 / 惯性 / 回弹）= 用户移动（VIR-P5）
      internal bool ConsumeUserMotion() { var moved = content != null && content.anchoredPosition.y != _writtenY; _writtenY = content != null ? content.anchoredPosition.y : 0f; return moved; }
      internal void MarkWritten() => _writtenY = content != null ? content.anchoredPosition.y : 0f;
      internal void ShiftContentY(float dy)
      {
          if (content == null) return;
          if (dy != 0f) { var p = content.anchoredPosition; p.y += dy; content.anchoredPosition = p; }
          UpdateBounds();                                      // bounds 依赖 content 位置，先更新再读 normalizedPosition
          if (IsDragging) { m_ContentStartPosition.y += dy; UpdatePrevData(); }   // 下一次 OnDrag / 速度估计看不到这次平移
          var bar = verticalScrollbar;                         // UpdateScrollbars 是 private；value 的 setter 会回调 SetNormalizedPosition 清速度
          if (bar != null) { bar.size = Mathf.Clamp01(viewRect.rect.height / Mathf.Max(1f, content.rect.height)); bar.SetValueWithoutNotify(verticalNormalizedPosition); }
          MarkWritten();
      }
  }
  ```
- [ ] RUN(PuiScrollRectTests) / RUNPLAY(ScrollListVirtualPlayTests) 绿；RUN(ScrollListTests) / RUN(ScrollbarTests) / RUN(ScrollListGridTests) 不回归；lint

### Task 6：`virtualize` 属性 + 组型 + 运行时降级

**Files:** Modify `ScrollList.cs`、`ScreenInstantiator.cs`（`PreConfigureContent` 多传 `VariantResolver.ResolveAttribute(node, "virtualize", _variants)`，已含 `class=` 合并值）、`ScrollListRules.cs`（先加六个 code 常量）；Create `Tests/EditMode/Controls/ScrollListVirtualModeTests.cs`

- [ ] **Red**：`Virtualize_swaps_Content_to_the_windowed_group`（不写时仍是**精确类型** `VerticalLayoutGroup`）；`Virtualize_through_a_class_style_counts`；`Virtualize_with_columns_or_horizontal_falls_back_and_warns`（`LogAssert.Expect(Warning, "PUI-SCROLL-VIRTUAL-LAYOUT")`）；`Virtualize_with_reorder_keeps_reorder_off_and_warns`；`Virtualize_with_reuseItems_false_warns`；`Virtualize_variant_flip_is_ignored_and_warns_once`；`Columns_or_direction_variant_on_a_virtual_list_is_ignored_and_warns`；`Static_children_lay_out_as_before_until_the_first_push`
- [ ] **实现**：`_virtualAsked` / `_virtual = asked && !grid && !horizontal`（按实例化时解析值定）；`IsGrid` / `IsHorizontal` 前加 `!_virtual`；`ApplyLayoutMode` 选 `WindowedVerticalLayoutGroup`；`[UIAttr, Preserve] public bool Virtualize { set { if (value != _virtualAsked) WarnOnce(VirtualVariantCode, …); } }`；`Reorder` setter 建驱动器前 `if (value && _virtual) { WarnOnce(…); value = false; }`；`ReuseItems` 改显式字段并在虚拟模式警告 `false`；`Columns` / `Direction` setter 在虚拟模式若请求网格 / 横向 → 警告且**不写字段**（`GetNativeSize` / `ApplyCrossAxisClip` / `WireScrollbar` 读它们）；`WarnOnce(code, msg)` = `HashSet<string>` 去重 + `UILog.Warn(this, $"[{code}] <ScrollList id='{Id}'>: {msg}")`。
- [ ] RUN(ScrollListVirtualModeTests) 绿；RUN(ScrollListGridTests) / RUN(ScrollListReorderTests) 不回归；lint

### Task 7：窗口同步核心

**Files:** Modify `ScrollList.cs`；Create `Tests/EditMode/Controls/ScrollListVirtualTests.cs`（第一组）

- [ ] **Red**（夹具：`box` Frame 里 `<ScrollList id='sl' width='150' height='200' itemTemplate='Row' virtualize='true' spacing='2' padding='4'/>`，`Row` = `<Frame height='30'><Text id='label'>x</Text></Frame>`；`TopOf(h) = -h.anchoredPosition.y - h.rect.height * (1 - h.pivot.y)`，`S = content.anchoredPosition.y`，`V = Viewport.rect.height`）：
  1. `Push_1000_realizes_only_the_window`（`SlotCount ≤ ⌈V/32⌉ + 3`，`== Content.childCount`，`ItemCount == 1000`）
  2. `Content_height_is_the_virtual_total`（`4 + 1000·30 + 999·2 + 4` ±0.5）；`Rows_sit_at_their_item_offsets`（`TopOf == 4 + 32·i`）
  3. `Scrolling_moves_the_window_and_reuses_rows`（`S = 32·500` → `RefreshWindow()` → 标签连续、从 i500 附近起；`LiveDynamicSubtreeCount` 不变）
  4. `Scroll_binds_only_rows_entering_the_window`；`Push_rebinds_every_realized_row`
  5. `Shrinking_the_list_parks_rows_and_growing_reuses_them`（停放的行在 `Pool` 下、`activeSelf` 仍 true）
  6. `First_push_destroys_static_placeholders`；`ItemTemplate_change_rebuilds_the_window`；`Dispose_releases_realized_and_parked_rows`
  7. `A_push_from_inside_bind_runs_after_the_current_sync`（窗口一致、最终显示第二次推送）；`Spacing_variant_moves_the_offsets`
- [ ] **实现**：
  ```
  VirtualPush(binding, n):           // Accept 已成功
    anchor = _pendingAnchor?.Remap(_remap) ?? CaptureAnchor(sameBinding: binding == _anchorBinding)   // Task 8 细化；Task 7 先「保持 S」
    _model.Remap(_remap, n); RemapRealized(_remap)   // 已实现行换新下标；被删项的行立刻停放（不 bind）
    if (!GameObject.activeInHierarchy) { _pendingAnchor = anchor; _pendingBindAll = true; return; }   // Task 9
    Sync(bindAll: true, anchor)

  Sync(bindAll, anchor):
    _inSync = true
    _model.SetMetrics(_windowed.spacing, _windowed.padding.top, _windowed.padding.bottom)          // VIR-P9：每次从活的组读
    if (!_model.HasEstimate && n > 0) 先实现锚点处一行 → ForceRebuildLayoutImmediate(_content) → 量 → 种子
    for iter < 3:
      S* = Resolve(anchor)；m = _model.Estimate；_model.TryWindow(S* − m, S* + V + m, out f, out l)    // N > 0 至少 1 行
      if (iter > 0 && (f, l) 未变) break
      Reconcile(f, l, bindAll)       // 离窗行先顶给进窗项（不 reparent，只改兄弟序 + bind）；剩余 → Park；仍缺 → Unpark / _factory(_content)
      _windowed.Leading = _model.ExtentBefore(f); _windowed.Trailing = _model.ExtentAfter(l)
      LayoutRebuilder.ForceRebuildLayoutImmediate(_content)
      逐行量高：非激活 / ignoreLayout → Collapsed（+ WarnOnce）；否则 SetMeasured
    Apply(anchor)（Task 8：滚动路径平移、推送路径夹取）→ _scroll.ShiftContentY(…)
    _inSync = false；执行入队的推送 / ScrollTo*（VIR-P7）；最后写 _atEnd（发 OnAtEndChanged）
  ```
  - **新行永远 `_factory(_content)`**（`lift` / `drop` 挂钩在首次 apply 时向上找 `ScrollListContentMarker`），停放 = `SetParent(EnsurePool())`，取出 = 移回 `_content`。
  - `OnPush` 在 `_inSync` 时只把 `(binding, items)` 存为「待执行的最新推送」，`Accept` 推迟到执行时。
  - `ClearSlots` / `Dispose` 同时释放空闲表与池里的行；`internal void RefreshWindow() => Sync(false, CaptureAnchor(…))`。
- [ ] RUN(ScrollListVirtualTests) 绿；lint

### Task 8：粘边状态机 + 锚点 + key 推送 + `stickToEnd`

**Files:** Modify `ScrollList.cs`；`ScrollListVirtualTests.cs`（第二组，加折行模板 `Wrap` = `<HStack width='stretch'><Text id='label' width='stretch' wrap='true' fontSize='20'/></HStack>`，推长短不一的字符串）

- [ ] **Red**：
  8. `Scrolling_up_into_estimated_rows_keeps_the_anchor_row_in_place`（往上滚后 `RefreshWindow()`，首个可见行的 `TopOf − S` 不变 ±0.5）
  9. `Keyed_trim_front_in_the_middle_keeps_the_anchor`；`A_push_from_a_new_binding_object_is_anchored_like_a_first_push`（VIR-P4）
  10. `StickToEnd_starts_at_the_end_and_stays_there_on_push`
  11. `Stuck_to_end_survives_a_height_change_that_did_not_come_from_the_user`（首推在底 → 改列表宽使行变高 → tick → 仍在底；VIR-P5 的回归锚）
  12. `Keyed_prepend_at_the_start_with_stickToEnd_keeps_the_old_first_row`；`…without_stickToEnd_stays_at_the_start`；`First_push_without_stickToEnd_starts_at_the_top`
  13. `A_jump_past_the_window_anchors_on_the_item_at_S`（`S` 直接跳到 900 行处 → 无已实现行相交 → 从模型取项，行位置正确）
- [ ] **实现**：
  - 状态：`_stuckToStart = true`、`_stuckToEnd = StickToEnd`（首推时）；同步结束按结果维持；`ScrollToStart/End` 设置对应一个、清另一个；`ScrollToIndex` 两个都清。tick 里 `if (_scroll.ConsumeUserMotion())` → 按几何重算：`_stuckToStart = S ≤ 1`，`_stuckToEnd = StickToEnd && S ≥ total − V − 1`。
  - `CaptureAnchor`：`_stuckToEnd` → End；`!StickToEnd && _stuckToStart` → Start；否则首个与视口相交的已实现行 → `Item(i, TopOf − S)`；**没有相交行** → 模型里 S 处的项 `k`，`delta = OffsetOf(k) − S`（VIR-P6 / 评审 A3）。推送若来自不同绑定对象 → 首推规则（VIR-P4）。
  - `Apply`：**滚动路径**——Start 不动；End 保持 `S − (total − V)`；Item 平移 `TopOf'(k) − TopOf(k)`。**推送路径**——同上，再在「未拖动且 `velocity == 0`」时夹到 `[0, max(0, total − V)]`；首推直接 0 或 `total − V`。
  - `[UIAttr, Preserve] public bool StickToEnd { get; set; }`（Variant 可切；切成 false 时清 `_stuckToEnd`）。
- [ ] RUN(ScrollListVirtualTests) 绿；lint

### Task 9：失效与挂起

**Files:** Modify `ScrollList.cs`；`ScrollListVirtualTests.cs`（第三组；EditMode 用 `((IScrollTickHost)list).OnScrollLateUpdate()` / `OnScrollEnabled()`）

- [ ] **Red**：
  14. `Width_change_remeasures_the_visible_rows`
  15. `ReSolve_marks_heights_stale_and_the_tick_remeasures`（`fontSize.alt='40'` + `UI.Variants.Set("alt", true)`）
  16. `A_push_to_an_inactive_list_binds_nothing_until_it_shows`；`Several_pushes_while_inactive_then_showing_mid_list_keeps_the_anchor`（锚点逐次链式映射）
  17. `First_push_in_the_open_frame_measures_at_the_real_width`（`<VStack>` 里 `width/height='stretch'`；`UI.Open` 后不 `ForceUpdateCanvases` 直接推送）
  18. `Bind_exception_on_push_is_logged_and_the_window_stays_consistent`；`Bind_exception_on_scroll_is_logged_as_an_error`
  19. `Parked_rows_survive_a_ReSolve`（模板根 `width='stretch' hidden='false'`）
  20. `A_row_hidden_by_bind_collapses_and_does_not_resync_every_tick`（连 tick 10 次，同步只发生 1 次；警告 1 次）
- [ ] **实现**：
  - tick（虚拟 + 已绑定）：视口**宽**变 → `_model.InvalidateAll()` + pending；**高**变 → pending；窗口内逐行比对「当前 rect 高 / 激活与 `ignoreLayout` 状态」与模型不符 → pending；用户移动或 pending → `Sync(_pendingBindAll, …)`。
  - `OnAfterApply`（虚拟 + 已绑定）→ `InvalidateAll` + pending（`Screen.ReSolve` 先放静态节点、后放行）。`OnScrollEnabled` → pending。
  - 未激活时推送：映射立刻应用、被删项的行立刻停放，已有的 `_pendingAnchor` 链式映射；`Slots` 在显示前仍是旧数据（写进 SKILL）。
  - 首次真正同步：`!_settled && (BornFrame.IsCurrent(_born) || V ≤ 0)` → `LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)UI.OwnerScreenOf(this).RootGameObject.transform)` 一次（`_born` 于 `OnAttached` 取）。
  - bind 异常：推送路径收集第一个、同步做完后 `ExceptionDispatchInfo.Capture(e).Throw()`（经 R3 记日志）；滚动路径 `UILog.Error(this, …)`。
- [ ] RUN(ScrollListVirtualTests) 绿；lint

### Task 10：PlayMode 补全

**Files:** `Tests/PlayMode/Controls/ScrollListVirtualPlayTests.cs`（拖动照 `ScrollListReorderPlayTests` 直接调根上 `PuiScrollRect` 的 drag 接口；甩动照 `PixelSnapPlayTests` 写 `velocity`）

- [ ] `Fling_through_1000_items_never_shows_a_gap`（每帧首行 `TopOf ≤ S`、末行底 `≥ S + V`，到端除外）
- [ ] `A_push_during_a_drag_keeps_the_row_under_the_finger`（且松手后没有平移带来的速度尖峰）
- [ ] `StickToEnd_follows_pushes_until_the_user_drags_away`
- [ ] `Stuck_to_end_survives_the_scrollbar_appearing_and_rows_rewrapping`（`AutoHideAndExpandViewport` 场景：从不满一屏推到溢出，逐帧推送，始终在底）
- [ ] `The_elastic_bounce_at_the_top_plays_out_while_syncing`（往上甩到回弹：S 先 < 0 再平滑回 0，不被掐成 0）
- [ ] `Dragging_the_scrollbar_through_unmeasured_items_leaves_no_gap`（且甩动速度不被清零）
- [ ] `Keyed_trim_front_leaves_no_stale_row_in_Content_the_same_frame`（非虚拟；验证退役行已移出）
- [ ] RUNPLAY(ScrollListVirtualPlayTests) 绿

### M1 收尾

- [ ] 零回归守门全跑 → `git commit -m "feat(scrolllist): virtualize — windowed rows, measured heights, anchored scrolling"`

---

## M2 — 作者面收尾

### Task 11：滚动 API + `IsAtEnd` / `OnAtEndChanged` + 非虚拟 `stickToEnd`

**Files:** Modify `ScrollList.cs`；Create `Tests/EditMode/Controls/ScrollListScrollApiTests.cs`

- [ ] **Red**（两种模式各一遍）：`ScrollToEnd_and_ScrollToStart`；`ScrollToIndex_puts_the_item_at_the_top_clamped`（越界 `ArgumentOutOfRangeException`；上界见 VIR-P3）；`IsAtEnd_is_true_when_the_content_fits`；`OnAtEndChanged_replays_the_current_value_and_dedupes`；`NonVirtual_stickToEnd_keeps_the_end_after_a_push`；`NonVirtual_without_stickToEnd_keeps_the_offset`；`Horizontal_list_end_is_the_right_edge`；`ScrollToEnd_on_an_inactive_virtual_list_applies_when_shown`；`ScrollTo_from_inside_bind_runs_after_the_sync`
- [ ] **实现**：`ReactiveProperty<bool> _atEnd`（`OnAtEndChanged => _atEnd`，回放 + 去重，`Dispose` 释放；只在同步 / tick / 滚动 API 结束后写）；`IsAtEnd` 按当前 rect 现算。非虚拟模式共用粘边状态：推送标 `_stickPending`，tick 里 `_stickPending && _stuckToEnd` → `ForceRebuildLayoutImmediate(_content)` → 滚到底；视口 / 内容尺寸变了且 `_stuckToEnd` → 滚到底。滚动 API 先 `StopMovement()`；虚拟模式设锚点意图后同步（未激活则留作 pending，`_inSync` 则入队）；非虚拟模式 `ForceRebuildLayoutImmediate(_content)` 后写 `content.anchoredPosition`（横向写负 x）再 `MarkWritten()`。
- [ ] RUN(ScrollListScrollApiTests) 绿；lint

### Task 12：lint（六个代码，仅 CLI）

**Files:** Modify `ScrollListRules.cs`、`HugRules.cs`（加 `IsOpenEndedHug`）、`IRWalker.cs`、`.lint/UIXmlLint/README.md`（规则表六行）；Create `Tests/EditMode/Lint/ScrollListVirtualRulesTests.cs`（`IRWalker.Walk(Doc(...))` 写法）

- [ ] **Red**（每码一正一反 + 经 `<Style>`/`class=` + 经变体）：LAYOUT（`columns='2' cellSize='10x10'` / `direction='horizontal'` / `columns.portrait='3'` 报，`columns='0'` 不报）；REORDER；REUSE；HUG（`hug` / `clamp(40, hug, _)` 报，`clamp(_, hug, 200)` / `200` 不报）；VARIANT；TEMPLATE；不写 `virtualize` 的列表一个都不报
- [ ] **实现**：`DeclaresVirtualize(n, styles)`（照 `DeclaresGrid`）；`CheckVirtualize(n, styles)`；`IRWalker` ScrollList 分支调用；**不在** `ScreenInstantiator` 镜像（注释指向 Task 6 的运行时警告）。
- [ ] RUN(ScrollListVirtualRulesTests) / RUN(ScrollListRulesTests) 绿；`dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` 零 error；lint

### Task 13：XSD

- [ ] `XsdGeneratorTests.ScrollList_lists_its_grid_attributes_and_not_the_retired_scrollbar_ones` 加 `virtualize` / `stickToEnd` 的 `type="xs:boolean"` 断言 → RUNEDITOR(XsdGeneratorTests)

### Task 14：文档（英文，同 PR）

- [ ] XML SKILL：`<ScrollList>` 属性表加两行（不兼容组合 + lint 代码）；reorder 示例后 3 行聊天示例 + 指向 `reference/virtualize.md`；BUILT-INS 速查在 `reorder` 那行后加一行
- [ ] 新建 `reference/virtualize.md`：何时用；什么变了（bind 只对已实现行、滚动时也会调、行上非数据状态随回收丢失、别用 `Hidden` 过滤、停放的行仍活着——`FindAll` 会返回、未激活时 `Slots` 是旧数据、推送的列表会被拷贝）；粘边与锚点（§5.4 场景表 + 「粘边是记住的状态」）；lint 表；聊天配方；错误经 R3 记日志而不抛回
- [ ] `reference/reorder.md`：「keyed `BindItems` diffs」改为 key 已有（无动画）；虚拟列表不能拖排
- [ ] C# SKILL：**List / option push** 加 key 重载（行跟 key 走、跨 `BindItems` 调用同 `TKey` 也认、重复 / null key 整次推送被拒并记日志）、虚拟模式契约、滚动 API、`IsAtEnd` / `OnAtEndChanged`、`ItemCount` vs `SlotCount`、「绑定一次、经 observable 推送」；DATA PUSH 速查；Theme switching 段指向 `virtualize`
- [ ] `AGENTS.md` 触发路由表加一条；主 spec §9.5 补一句

### Task 15：演示（`Samples~/CommonControls`，VIR-P11）

- [ ] `CommonControls.ui.xml`：模板 `ChatRow`（`<HStack width="stretch" spacing="4" childAlign="upper-left">` + `<Text width="44">` 时间 + `<Text width="stretch" wrap="true">` 正文）；`<TabBar>` 加 `<Tab id="tabChat" … text="聊天" bind="pageChat"/>`，五个 Tab 的 `height.portrait` 120 → 108（竖屏轨道 640 − 44 − 8 = 588；5 × 108 + 4 × 4 = 556；四字标签 fontSize 22 竖排约 106 不裁）；新页 `pageChat`（同其他页的 margin / `<Skin/>` / VStack）：一行「来 50 条」按钮 + 说明，下面 `<Frame height="stretch">` 里 `<ScrollList id="chat" anchor="stretch" itemTemplate="ChatRow" virtualize="true" stickToEnd="true" spacing="4" padding="8">` + `<Btn id="chatMore" anchor="bottom-center" margin="_,_,8,_">↓ 新消息</Btn>`
- [ ] `CommonControlsRunner.cs`：`BindChatPage(screen)`——只读字段的 `sealed class ChatMsg`（`Id` 递增，正文长短不一）；`ReactiveProperty<IReadOnlyList<ChatMsg>>` 预生成 1000 条，**只 `BindItems` 一次**（`key: m => m.Id`）；`Update()` 每秒追加一条、超 1000 裁最老（Start 未完成前判空）；「来 50 条」；`OnAtEndChanged` 驱动 `chatMore.Hidden`，点击 `ScrollToEnd()`；类注释「四页」→「五页」；`package.json` 样例描述改为 `Five-Page …` 并加 `Virtualized Chat`
- [ ] 两份 `CommonControls.po`（en 填英文译文；zh 的 `msgstr` 同 `msgid`）按现有条目格式补新增文案
- [ ] `dotnet run --project .lint/UIXmlLint -- Samples~/CommonControls/Resources/` 零 error
- [ ] 编译验证（`Samples~` 不在任何编译集里、宿主里那份是 6 月的旧拷贝不动）：scratchpad 建临时 csproj，`Compile Include` 样例 `*.cs` + `ProjectReference` `.lint/Runtime.csproj`（`Local.props` 已配 Unity 与宿主路径，R3 走 NuGet），`dotnet build` 零 error

### Task 16：性能基准

- [ ] `ScrollListVirtualBenchmark.cs`，`[Explicit, Category("Perf")]`：ChatLine 形状模板，N = 20 / 200 / 1000 / 10000 × {虚拟 + key、非虚拟 + key（N ≤ 1000）、非虚拟无 key（N ≤ 1000）}，各 20 次「裁头 + 追加一条」推送 + `Canvas.ForceUpdateCanvases()`，`Stopwatch` 均值，`Debug.Log` 一张表；用 `run_tests(test_names=[…])` 显式跑

### Task 17：收尾

- [ ] 三个程序集全量（EditMode / EditorOnly / PlayMode）；`dotnet format --verify-no-changes --severity warn`；UIXmlLint 跑 `Runtime/Resources/` 与示例
- [ ] spec 状态改「已实现」，加 §15 实施记录（实现期新发现的偏差、基准数字表、报告第二列的复现）
- [ ] `git commit -m "feat(scrolllist): stickToEnd, scroll API, lint, docs, demo"`
- [ ] 征得同意后 `git push -u origin feat/scrolllist-virtualization` + `gh pr create`（正文附基准表）

---

## Verification（端到端）

1. **单测**：各 Task 新测试全绿；零回归守门全绿；三个程序集全量无新失败。
2. **性能**：Task 16 基准里虚拟模式 1000 与 10000 同量级、目标 ≤ 2 ms（桌面编辑器）；Content 下行数与 N 无关；非虚拟 + key 复现报告第二列。
3. **CLI**：UIXmlLint 对 `Runtime/Resources/` 与示例零 error；临时写一份 `virtualize` + `reorder` 的 XML 能看到 `PUI-SCROLL-VIRTUAL-REORDER`。
4. **手感**（作者在宿主验）：合入后给 ssw_re_client 的 `chatWorld` 加 `virtualize="true" stickToEnd="true"` + key，用报告同样的 UI Preview 灌 1000 条重测「来一条新消息」，并试往上翻看时来消息、滚动条出现瞬间是否仍贴底。
