# 文档加载提速：Import 并行预取 + 跨文档源缓存 + `Theme.Changed` 只在真变时广播

> 状态：**已实现**（分支 `feat/document-load-parallel-prefetch-cache`，2026-09-14 分三步提交；实测与偏差见 §10）。
> 需求来源：宿主工程 ssw_re_client —— 对局里点母星打开星球面板（`Panels/Planet.ui.xml`，7 个 `<Import>`），
> 首次打开从点击到出现 **1.24 s**，其中 **0.95 s 花在按顺序取 8 个 xml**，再 0.30 s 单帧同步建面板；
> 同一批数据里 HUD（`Round.ui.xml` + 5 个 Import）进场也花 0.93 s 取源，只是藏在 Loading 后面。
> 三件事各自独立、都不改 XML 语义与公开 API 形状，放一份 spec 是因为它们共用一条加载路径、测试也互相牵连。
> 相关：
> 主 spec §7.6（Import）/ §9.1（`LoadDocumentAsync` / `LoadCommonLibraryAsync`）；
> `2026-08-26-theme-driven-style-design.md` §9（取源 / 合并的切线：`DocumentLoader` 异步预取 + `DocumentAssembler` 纯 C# 合并——本文只动**取**这一侧）
> 与 §5.2（`Theme.Changed → Screen.ReSolve` 链路——本文收窄它的触发条件）；
> `Runtime/Application/UI.cs` `ReloadAsync` 注释（"Register would idempotent-no-op on the same key"——这句已经不成立，见 §6.1）。

## 1. 问题（实测）

宿主工程用 Addressables 取源（`UI.UseAddressableResolver()`），编辑器 Play 时 Addressables 走 Fast Mode，
`AssetDatabaseProvider` 对每次 `LoadAssetAsync` 加 `SimulatedLoadDelay = 0.1 s`（宿主 `AddressableAssetSettings.asset`
`m_simulatedLoadDelay: 0.1`）。在 `UI.SourceResolver` 外包一层计时（不改库）量到：

```
[t=7.897] Router.Open(planet)
[t=8.012] xml Panels/Planet.ui.xml        114 ms / 7 帧
[t=8.129] xml Templates/GlassStyle.ui.xml 115 ms / 7 帧   ← Round / MapPlates / FactionSelect 各自都取过一遍
[t=8.244] xml Templates/DockPanel.ui.xml  113 ms
[t=8.360] xml Templates/SideTab.ui.xml    116 ms
[t=8.477] xml Templates/CountdownBar      116 ms
[t=8.610] xml Templates/BuildSlot         132 ms
[t=8.727] xml Templates/BuildOption       116 ms
[t=8.844] xml Templates/DockResRate       116 ms
[t=9.140] Router.Changed  round>planet          ← 最后一帧 337 ms
```

三个独立的原因：

**P1 取源是串行的。** `DocumentLoader.PrefetchAsync`（`Runtime/Application/DocumentLoader.cs:54`）深度优先、
一个 `await resolver(src)` 完了才发下一个。8 个文件 = 8 轮往返。真机没有 0.1 s 的模拟延迟，但每轮至少一帧，
远程包首轮还含下载——结构问题不随平台消失。宿主的 Import 图深度为 1（模板不再 Import），理论下限是 **2 轮**。

**P2 公共模板每个文档重取。** `GlassStyle.ui.xml`（皮肤词汇表）被 Round 场景 9 个文档 Import，进对局到面板打开为止已取 4 次；
`parsed` 表只活在一次 `LoadAsync` 里（`DocumentLoader.cs:30`）。热重载与 `ModalDocCache` 各有各的缓存，
唯独 `<Import>` 的源没有。

**P3 每次 `LoadDocumentAsync` 都让全部已开 Screen 整屏 ReSolve。** `RegisterThemesAndAutoSet`（`UI.cs:1055`）
末尾：只要 `Theme.Current` 非空就 `RaiseChangedIfCurrent`——注释写明 "fires regardless of whether the named theme is now
actually registered"。它的本意是「先 `Theme.Set` 后加载」的软失败→真色过渡（§6.2 的 (a)），但代价是**每次**加载
任何文档（哪怕它一个 `<Theme>` 都没有）都广播 `Theme.Changed`，HUD 这种大屏跟着整屏 `ReSolve`。
Deep Profile 里这一项占面板首开同步段的 14%（97 ms deep ≈ 40 ms 实际），并且随已开 Screen 的数量与体量线性增长。

Deep Profile 对最后那一帧（703 ms deep ≈ 297 ms 实际，膨胀比 2.37）的分解，供衡量本文的收益边界：

| 项 | deep ms | 估实际 ms | 本文 |
|---|---|---|---|
| `UI.Open`（实例化 + 属性应用 + SetActive） | 352 | ~150 | 不动（另案：`ControlMeta.Apply` 走反射 `PropertyInfo.SetValue`） |
| 宿主 `OnEnter`（`ScrollList.Rebuild` 9 张卡） | 224 | ~95 | 不动（宿主侧） |
| **`RegisterThemesAndAutoSet → Theme.Changed → ReSolve`** | **97** | **~40** | **P3** |
| `TemplateExpander.Expand` | 30 | ~13 | 不动 |

## 2. 否决的方案

**A. 只把宿主的 `SimulatedLoadDelay` 调成 0。否决作为"修法"。** 它只掩盖编辑器里的症状；真机上 8 轮往返、
远程包首轮下载、每次加载的整屏 ReSolve 一样存在。宿主愿意调它是宿主的事，本文按 0.1 s 的基线衡量收益。

**B. 由宿主在场景 Loading 期预加载所有面板文档。** 有用但治标：每个新面板都得记得登记，且 Router 用私有
`_loadedSrcs` 判重，需要新公开 API（`UI.Router.PreloadAsync`）。**不在本文范围**，列入 §9 后续；本文先把库自身的三处浪费去掉，
预加载在此之上才是纯收益（预加载 N 个面板时若仍串行，Loading 会更长）。

**C. 缓存源文本而非解析结果。** 取源是大头、解析只有几 ms，缓存文本已能拿到 90% 收益，且天然没有 IR 别名风险。
否决理由：`DocumentAssembler.Assemble` 本来就以 `Func<string, UIDocument>` 查表（`Runtime/Core/Template/DocumentAssembler.cs:36`），
缓存解析结果是零改动的接法；而且 §6 的「主题块没变」判定恰好依赖**同一 src 得到同一个 `ThemeBlock` 实例**——缓存文本做不到。
IR 的不可变性有据可查：`TemplateExpander.ExpandTree` / `ExpandInvocation` 全程 `new ElementNode` + `DeepClone`，
`StyleMerger.Apply` 走 `CloneForMerge`，`StampInvokedAt` 只打在展开出来的 `instanceRoot` 上，
`BuildRuntimeTemplates` 明写 "Every entry is a copy the ScreenDef OWNS, never the shared TemplateDef"。§7 用一条测试钉住这一前提。

**D. 缓存在 `DocumentLoader` 内部按 src 全局 key。否决。** `LoadDocumentWithCommonsAsync`（`UI.cs:629`，模态框）传进来的
resolver 对入口 `label` 返回的是手上的 xml，`label` 未必是真 src；同一个 src 字符串在不同 resolver 下语义不同。
缓存必须只覆盖**经 `UI.SourceResolver` 取到的 src**，所以放在 `UI` 这一层、由 `UI` 组合成 fetch 函数再交给 loader（§5.1）。

**E. `ThemeStore.Register` 做结构相等比较（逐色比 `ColorSpec`、逐条比 `StyleDef`）来判"没变"。否决。**
`StyleDef` 没有结构相等，补一套等于再造一份 IR 比较器。用**解析产物的引用相等**（§6.1）：同 src 命中缓存 → 同一个 `ThemeBlock`
→ 判定为没变；缓存未命中（新会话 / 被失效）→ 新实例 → 保守地当作变了 → 行为等于今天。判错的方向只会是"多广播一次"，
不会是"该广播没广播"。

## 3. 方案总览

| | 改哪里 | 结果 |
|---|---|---|
| **§4 并行预取** | `DocumentLoader.PrefetchAsync`：入口取完后，它的 Import 同层一起起飞，`WhenAll` 再递归 | 往返轮数 = Import 图深度 + 1；宿主 8 轮 → 2 轮 |
| **§5 源缓存** | 新 `Application/DocumentCache`（src → 已解析 `UIDocument`，带在飞去重）；`UI` 用它包 `SourceResolver`；`UnloadAll` / 热重载 / `ReloadAsync` 失效 | 同一 src 一个场景内只取一次、只解析一次 |
| **§6 Changed 收窄** | `RegisterThemesAndAutoSet` 只在「当前主题从不可解析变可解析」或「当前主题链上的块被新增 / 替换」时广播 | 加载不含主题变化的文档 → 零 ReSolve |

公开 API 形状不变：`LoadDocumentAsync` / `LoadCommonLibraryAsync` / `ReloadAsync` / `UnloadAll` / `Theme.Changed` 签名与用法照旧。
XML / XSD / lint 不动。`Core/Template` 不动（CLI 编译子集不受影响）。

预期（编辑器 0.1 s 基线，宿主）：面板首开取源 0.95 s → **~0.23 s**（2 轮），去掉 ~40 ms 的无效 ReSolve；
HUD 进场取源 0.93 s → ~0.23 s；之后五张旗帜面板各 2 轮（`GlassStyle` / `PanelShell` 命中缓存后是 1 轮 + 入口）。
真机：每个文档 8 帧 → 2 帧。

## 4. Import 并行预取

### 4.1 算法

`PrefetchAsync(src, fetch, parsed, started)`：

1. `src` 已在 `parsed` 或 `started` → 返回（`started` 是本次 Load 内"已起飞"的集合，替代今天的"记录后再递归"——
   循环 A→B→A 在这里终止，仍由 `DocumentAssembler.MergeInto` 报 `cyclic Import detected`，错误文案不变，
   `Cycle_detected_with_path_in_message` 不改）。
2. `started.Add(src)`；`doc = await fetch(src)`；`parsed[src] = doc`。
3. 对 `doc.Imports` 里每个尚未 `started` 的 `imp.Src`，**先全部调用** `PrefetchAsync(...)` 收集返回的 `Awaitable`，
   然后 `await AwaitableHelpers.WhenAll(list)`。递归里每层同样：取到自己就立刻放飞自己的 Import。

入口文档必须先取到才知道它 Import 什么，所以轮数 = 图深度 + 1，不可能再少；同一轮内的 N 个源在同一帧起飞、
在 Addressables 的同一次 `DelayedActionManager` tick 里一起完成，续体也落在同一帧的 `ExecuteTasks`。

### 4.2 `AwaitableHelpers.WhenAll`

`Runtime/Application/AwaitableHelpers.cs` 新增 `internal static async Awaitable WhenAll(IReadOnlyList<Awaitable> items)`：
**按下标顺序逐个 `await`**，中途有异常不中断——继续把余下的全部 await 完（每个 `Awaitable` 都必须被恰好 await 一次，
Unity 的 `Awaitable` 是池化对象、不能重复 await，也不能丢下不 await），最后抛**下标最小**的那个异常。
于是错误报告是确定的：与今天深度优先"第一个坏文件"按 Import 声明顺序报错一致（`Resolver_returns_null_throws_IOException`
这类测试的断言不变）。不用 `Task.WhenAll`——WebGL 没有线程池，且库的异步一律 `Awaitable`（`2026-07-06-awaitable-unitask-2022-design.md`）。

### 4.3 fetch 的形状

`DocumentLoader.LoadAsync` / `LoadAndMergeAsync` 的 `resolver` 参数从 `Func<string, Awaitable<string>>` 改为
`Func<string, Awaitable<UIDocument>> fetch`（取 + 解析一体，解析错误在 fetch 内包成 `ParseException`，文案照旧
`parsing src='…' failed: …`）。保留一个 `internal` 适配重载接受字符串 resolver（内部包一层 `Parse`），
`DocumentLoaderTests` 与 `LoadCommonLibraryAsync` 的空 resolver 校验（`Resolver_null_throws_InvalidOperation`）不动。

## 5. 跨文档源缓存 `DocumentCache`

### 5.1 位置与形状

`Runtime/Application/DocumentCache.cs`，`internal static class`，状态两张表：

```csharp
Dictionary<string, UIDocument> _docs;                                   // src → 已解析
Dictionary<string, List<AwaitableCompletionSource<UIDocument>>> _inflight;  // src → 等待者
```

`Awaitable<UIDocument> GetOrFetchAsync(string src, Func<string, Awaitable<string>> resolver)`：

- 命中 `_docs` → `AwaitableHelpers.Completed(doc)`；
- `src` 在 `_inflight` → 新建一个 `AwaitableCompletionSource<UIDocument>` 挂进等待者列表，返回它的 `Awaitable`
  （**不能**把首个请求的 `Awaitable` 直接再给第二个人——只能 await 一次）；
- 否则登记 `_inflight[src]`（首个请求者自己也是一个等待者），发起 `resolver(src)` + `Parse`；完成后写 `_docs`、
  移出 `_inflight`、逐个 `SetResult`；失败则**不写缓存**、移出 `_inflight`、逐个 `SetException`（同一个异常对象），
  下次请求重新取——坏文件修好后不用清缓存就能重试。

`UI` 组合 fetch：

```csharp
// LoadDocumentAsync / LoadCommonLibraryAsync / ReloadAsync / ReloadCommonLibraryAsync
fetch = src => DocumentCache.GetOrFetchAsync(src, SourceResolver);
// LoadDocumentWithCommonsAsync(label, xml)：入口绕过缓存，Import 走缓存
fetch = src => src == label ? Parse(xml) : DocumentCache.GetOrFetchAsync(src, SourceResolver);
```

模态框（`ModalDocCache`）的入口 xml 不经 `SourceResolver`、也不进本缓存；它 Import 的 `ModalFrame.ui.xml` 这类模板从此只取一次。

在飞去重是必要的而不只是锦上添花：并行预取后，宿主同一帧起两个 `LoadDocumentAsync`（Round 进场就是 HUD + MapPlates + FactionSelect）
会同时要 `GlassStyle`；不去重就是两次取源 + 两个不同的 `ThemeBlock` 实例，§6.1 的引用判定随之失效、多一次无效 ReSolve。

### 5.2 失效（谁清、什么时候清）

| 时机 | 动作 | 理由 |
|---|---|---|
| `UI.UnloadAll()`（`UI.cs:984`） | `DocumentCache.Clear()` | 它的契约就是 "Clears all loaded state"；宿主每次切场景调它。`[OnEnteringPlayMode]` / `[OnExitingPlayMode]` 走的也是它，所以 **Domain Reload 关闭时的 Play→Stop→Play** 自动拿到新内容——`ThemeStore.Register` 注释里的场景 (2) 依旧成立 |
| `UI.ResetForTests()` | `DocumentCache.Clear()` | 测试隔离 |
| `HotReload.NotifyAssetChanged(assetPath)`（`UI.cs:1245`） | 算出 `src` 后**第一件事** `DocumentCache.Invalidate(src)`，再走既有的重载分发 | 文件变了就不能再从缓存给；无论有没有 Screen 依赖它（宿主的预览工具可能之后自己重新 Load） |
| `ReloadAsync(screenName)`（`UI.cs:684`）/ `ReloadCommonLibraryAsync(src)`（`UI.cs:791`） | 重取前 `Invalidate` **入口 + `_depGraph` 记录的整个闭包**（`ScreenDeps[name].AllDeps` / `SrcToDeps[src]`） | "reload" 的语义就是重读；`HotReloadTests` 直接改假文件表再调 `ReloadAsync`，不经 `NotifyAssetChanged`，必须能拿到新内容。热重载是编辑器专属，多取几个文件无所谓 |

不清的：`UnloadDocument(screenName)`（只释放 ScreenDef 槽位；`PromptUGUIDocumentHost` 的预览走同步 `LoadDocument(label, xml)`，
不碰缓存）、`Router` 的 teardown、`ModalDocCache.Clear()`。

不新增公开 API：宿主要"强制重读"就 `UnloadAll()`（Addressables 运行时更新目录的场合本来也得重载 UI）。
`InternalsVisibleTo` 已覆盖测试程序集，测试直接 `DocumentCache.Clear()`。

### 5.3 与 `ReLoad_Same_Src_With_New_Color_Values_Replaces_Old` 的关系

这条测试（`ThemeLoadingTests.cs`）模拟 "edit XML → re-Play with Domain Reload off"：同一进程里改假文件表、再 `LoadCommonLibraryAsync`
同一 src，断言新颜色生效。有缓存后第二次会命中旧解析——**它模拟的场景真正的边界是 `[OnEnteringPlayMode] → UnloadAll`**，
测试在两次加载之间插一句既有的测试缝 `UI.OnEnteringPlayModeForTests()` 即可（`UnloadAll` 不清 `ThemeStore`，
测试要验的"Register 必须替换而非跳过"仍然被验到）。

## 6. `Theme.Changed` 只在真变时广播

### 6.1 `ThemeStore.Register` 报告变化

`Entry` 增加 `public ThemeBlock Block;`——注册时的解析块引用。`Register(...)` 增加 `ThemeBlock block` 参数并返回：

```csharp
internal enum RegisterOutcome { Added, Replaced, Unchanged }
```

- 名字不存在 → 写入，`Added`；
- 同 (name, src) 且 `ReferenceEquals(existing.Block, block)` → **不改写**，`Unchanged`（缓存命中的情形：同一份 IR，值必然相同）；
- 同 (name, src)、不同实例 → 照今天替换，`Replaced`（缓存未命中：新会话、被失效、或测试直接喂新 xml——保守当作变了）；
- 不同 src → 照旧抛 `duplicate <Theme name>`。

`ReplaceFromSrc`（热重载）不变，仍无条件替换；`ReplaceThemesAndNotify` 的广播逻辑不变。

### 6.2 `RegisterThemesAndAutoSet` 的新规则

```csharp
var current = Theme.Current;
var wasResolvable = current != null && ThemeStore.Instance.Available.Contains(current);
var touched = new HashSet<string>();
foreach (var (theme, src) in loaded.Themes)
    if (ThemeStore.Instance.Register(..., theme, src) != RegisterOutcome.Unchanged) touched.Add(theme.Name);
ThemeStore.Instance.ResolveBases();

Theme.AutoSetIfSingleAvailable();                 // Current 为空的路径不变：单主题自动选中并广播
if (current == null || Theme.Current != current) return;

var raise = (!wasResolvable && Available.Contains(current))          // (a) 先 Set 后加载：软失败(白) → 真色
         || ThemeStore.Instance.ChainOf(current).Any(touched.Contains); // (b) 当前主题链上有块被新增 / 替换
if (raise) Theme.RaiseChangedIfCurrent(current);
```

`ChainOf(name)`：`ThemeStore` 新增 `internal IEnumerable<string> ChainOf(string name)`——沿 `ResolvedBase` 往上（含自身；
`ResolveBases` 之后调用）。(b) 必须看整条链而不只看 `current`：`dark base="light"` 时先注册 `dark` 再有文档带来 `light`，
`dark` 的解析结果变了但 `dark` 本身没被碰。

对照今天的行为，只少了一种广播：**加载没有触及当前主题链的文档**（没有 `<Theme>`，或带来的 `<Theme>` 与缓存里是同一份）。
SKILL 里写的三种触发（`Set` / "post-load registration of pre-Set theme" / hot reload）一条不少。

### 6.3 为什么不会漏

- 先 `Set("x")` 再加载注册 `x`：(a)。
- 加载注册了 `x` 的祖先：(b)。
- 不关 Domain Reload 的 re-Play 里 `ThemeStore` 残留、新 xml 值不同：`UnloadAll` 已清缓存 → 新实例 → `Replaced` → (b) 广播；
  即便此刻没有 Screen 开着，广播也无害。
- 热重载：不走本函数，`ReplaceThemesAndNotify` 照旧。
- `Theme.Set` 本身的广播不动。

## 7. 测试（Red 先行）

`Tests/EditMode/Application/`：

**并行预取（`DocumentLoaderTests`）**
- `Imports_of_one_document_are_requested_before_any_completes`：resolver 用 `AwaitableCompletionSource<string>` 手动完成，
  记录请求顺序；入口完成后断言它的 3 个 Import **都已被请求**、再逐个放行；`AllSrcs` 与模板数与顺序版相同。
- `Nested_imports_fan_out_per_level`：深度 2 的图，断言第二层在其父完成后才起飞、同层并发。
- `Diamond_import_fetched_once`：A→B, A→C, B→D, C→D，D 只请求一次（`started` 去重）。
- `Failing_import_reports_first_in_declaration_order_after_all_settle`：两个 Import 都坏，晚声明的先完成，抛的是先声明那个的异常；
  所有请求都被完成（没有未 await 的 `Awaitable`）。
- 既有 `Cycle_detected_with_path_in_message` / `Same_src_imported_by_multiple_files_loaded_once` / `Resolver_returns_null_throws_IOException` 不改。

**源缓存（新 `DocumentCacheTests`）**
- `Second_document_importing_same_src_does_not_call_resolver_again`：计数 resolver，两个 Screen 文档 Import 同一模板，`GlassStyle` 计 1。
- `Concurrent_requests_for_same_src_share_one_fetch`：手动完成的 resolver，两次 `LoadDocumentAsync` 同帧发起，同一 src 请求 1 次、两边都拿到结果。
- `Failed_fetch_is_not_cached_and_retries`：第一次 resolver 抛，第二次成功。
- `UnloadAll_clears_cache` / `ResetForTests_clears_cache`。
- `NotifyAssetChanged_invalidates_src_even_without_dependents`。
- `ReloadAsync_rereads_entry_and_every_dep`：改假文件表里入口的一个 Import，`ReloadAsync(screen)` 后新模板生效。
- `Same_src_loaded_twice_yields_identical_expansion`（§2.C 的前提）：同一 src 两次 `LoadDocumentAsync`（中间 `UnloadDocument`），
  两次展开出的 `ScreenDef` 结构相等（序列化成文本比对），且第一次 `Open` 过不影响第二次。
- `ThemeLoadingTests.ReLoad_Same_Src_With_New_Color_Values_Replaces_Old` 按 §5.3 加一行 `UI.OnEnteringPlayModeForTests()`。
- `HotReloadTests` 全部不改、必须仍绿（它们是 §5.2 失效规则的既有 Red）。

**Changed 收窄（`ThemeLoadingTests` / `ThemeStoreTests`）**
- `Loading_document_without_theme_change_does_not_fire_Changed`：commons 注册 `light` 并 Set；再加载一个只含 Screen 的文档，计数不变。
- `Loading_second_document_importing_same_theme_src_does_not_fire_Changed`：缓存命中 → `Unchanged` → 不广播。
- `Loading_document_that_registers_base_of_current_fires_Changed`：`Set("dark")`，先注册 `dark base="light"`，再加载带 `light` 的文档，广播一次。
- `Register_same_block_instance_returns_Unchanged` / `Register_new_instance_same_src_returns_Replaced`。
- 既有 `PreSet_Theme_Then_Load_Fires_Changed_And_Resolves_To_Real_Color`（(a)）、`ThemeHotReloadTests` 不改。

Unity MCP 跑；`dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx` 过。

## 8. 影响面

- **SKILL**：不新增 / 不改公开 API。`scripting-promptugui-csharp/SKILL.md` 第 880 行 `Theme.Changed` 的触发描述已经是收窄后的语义
  ("fires on Set, post-load registration of pre-Set theme, and hot reload")，补半句 "— loading a document that doesn't touch the
  current theme chain does not fire it" 即可（同 PR，英文）。
- **CLI（UIXmlLint）**：不受影响——它自己做文件系统预取，只共享 `DocumentAssembler`。
- **Unity 2022 / UniTask 兼容层（`Runtime/Compat/`）**：`WhenAll` 只用普通 `await` 写在 `AwaitableHelpers` 里，两个后端自然都覆盖；
  `AwaitableCompletionSource<T>` shim 已有。不新增任何 `Awaitable` 静态 API 的使用（不碰 `NextFrameAsync` 之类）。
- 内存：一个场景内所有 `.ui.xml` 的解析 IR 常驻到 `UnloadAll`——KB 量级。

## 9. 不做的事 / 后续

- **宿主预加载 API**（`UI.Router.PreloadAsync(name)`，走 `EnsureLoaded`）：本文之后再看是否还需要——2 轮 ≈ 0.23 s（编辑器）/ 2 帧（真机）
  可能已够；要做也是纯增量。
- **属性应用的反射开销**（`ControlMeta.Apply → PropertyInfo.SetValue`，每控件 ~0.35 ms 实际）：`UI.Open` 与 `ScrollList.Rebuild`
  的大头，另案（编译委托 / 表达式树）。
- **`Screen.ReSolve` 增量化**：本文只减少无效触发，不改它的整屏成本。
- 跨 `UnloadAll` 的持久缓存：切场景本来就要过 Loading，且要处理 Addressables 目录更新，不值。

## 10. 实施记录（2026-09-14）

三步各一提交，每步 Red 先行：`perf(loader)` 并行预取 → `perf(loader)` `DocumentCache` → `perf(theme)` `Theme.Changed` 收窄。
EditMode 3818 / PlayMode 201 / Addressables + EditorOnly 370 全绿，`dotnet format --verify-no-changes` 干净。

**宿主实测**（同一台机、同一 0.1 s 模拟延迟基线，同一套 `SourceResolver` 计时钩子；宿主同时把 `Planet.ui.xml` 的静态占位卡 9 → 2）：

| | 改前 | 改后 |
|---|---|---|
| 星球面板：点击 → `Router.Changed` | **1.24 s** | **0.40 s** |
| 　其中取源 | 0.95 s（8 轮串行） | 0.23 s（2 轮：入口 115 ms + 6 个 Import 并行 116 ms；`GlassStyle` 命中缓存没再取） |
| 　其中最后一帧同步段（`ExecuteTasks`） | 297 ms | 164 ms（无 `Theme.Changed` 广播；静态卡少 7 张） |
| HUD 进场（`Round.ui.xml` + 4 Import） | 1.03 s | 0.53 s（`GlassStyle` 命中；两轮各 ~220 ms 是因为进场那几帧本身 70 ms/帧） |

整局 `Theme.Changed` 只剩宿主自己 `Theme.Set` 的那几次，加载文档不再触发。

**与 spec 的偏差**：

- 并行预取的测试放在独立文件 `DocumentLoaderPrefetchTests`（手动完成的 resolver 夹具），没塞进 `DocumentLoaderTests`；
  `WhenAll` 单独 `AwaitableHelpersWhenAllTests`。
- `Load*Async` 保留了接字符串 resolver 的签名，新加 `LoadParsedAsync` / `LoadAndMergeParsedAsync` 接 fetch——
  同名重载会让 `LoadAsync("x", null, …)`（既有测试）二义。
- `ReLoad_Same_Src_With_New_Color_Values_Replaces_Old` 在两次加载间插的是 `UI.UnloadAll()` 而不是
  `OnEnteringPlayModeForTests()`（后者带 `UNITY_6000_5_OR_NEWER` 守卫）；语义相同。
- `UnloadAllCommonLibraries` 也失效被卸载的 commons 闭包（spec §5.2 没列；"unload = 忘掉"）。
- §2.C「展开不改写输入 IR」有一处例外要知道：`LoadCommonLibraryAsyncInternal` 把 `TemplateDef.OriginSrc` /
  `StyleDef.OriginSrc` 写成 commons 入口 src（`ReloadCommonLibraryAsync` 靠它按来源 stash）。这是登记而非内容，
  且同一 src 不会既当 commons 又被文档直接 Import（会撞 commons 冲突），缓存下无影响；`Same_src_loaded_twice_yields_identical_expansion` 钉的是内容。
- `RegisterOutcome` 枚举在 `internal sealed class ThemeStore` 内声明为 `public`（有效可见性仍是 internal）。
