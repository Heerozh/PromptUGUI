# Commons 设置化 —— `PromptUGUISettings.commonLibraries` 成为公共库的唯一声明源

> 状态：**实施中**（§12 按推荐项已定，不出 plan；分支 `feat/commons-settings`，进度见 §14）。
> 相关：master spec §7.6（`as=` 消歧）与 M4.2–M4.4；`2026-05-08-m4-import-autoimport-hotreload-xsd-design.md`
> （M4-D1/D2 库不碰文件系统、M4-D3 `LoadCommonLibrary` = C# `global using`、M4-R8 reload 丢 `as=`）；
> `2026-08-26-theme-driven-style-design.md` §9（lint 的展开遍与 `DocumentAssembler` 单一实现）；
> `2026-09-14-document-load-parallel-prefetch-and-source-cache-design.md`（`DocumentCache`、`UnloadAll` 连源缓存一起清）；
> `2026-09-18-ui-preview-tool-design.md`（其 §4.6 / §5 的「commons 快照」方案被本文取代，改动清单见 §10）；
> 进行中的 `feat/lint-menu` 分支（`Editor/UIXmlLintMenu.cs` + `Core/Lint/LintRun.cs` / `ImportClosure.cs`）。

## 1. 问题

commons 池——`UI.LoadCommonLibraryAsync(src, as)` 装进去的全局 Template / Style / Theme——是**每个** Screen 的隐式依赖，
但它的地址只存在于宿主的 C# 里。库自己不知道，库的编辑器工具也不知道「这个工程的 commons 是哪几个 src」。三处后果：

**① lint 对用 commons 的工程是错的，不是"少个功能"。** 一个只写 `class="badge"`、没有 `<Import>` 的 Screen，`badge`
住在 commons 里，今天 CLI 的输出是：

```
Home.ui.xml: [PUI-EXPAND] <Frame>: unknown style 'badge' in class="badge" (no <Style> is declared in this document or its imports)
UIXmlLint: 1 issue(s)   → exit 1
```

根源在 `Runtime/Core/Lint/DocumentLinter.cs:41`：`Walk(doc, entrySrc, imports)` 只做 `DocumentAssembler.Assemble` + `Expand`，
没有运行时 `DocumentLoader.cs:76` 那一步 `MergeCommons`。于是 commons 里的样式（`class=`）与命名空间模板（`<ui.Card/>`）在展开遍一律
成了致命错误。宿主 ssw 已经被逼着改 XML 架构——`Assets/_Project/Round/UI/Templates/GlassStyle.ui.xml` 头部注释原话：
*「lint 对 Screen 文档跑展开校验时看不见 commons 池，于是 commons 里的样式（badge / badge-text）在这棵树上一律报 PUI-EXPAND……
所以想被 Round 这棵树 class= 到的样式，只能声明在本文件里」*。本该放 DefaultTheme 的主题样式包被复制进局部文件。

**② 每个宿主都要手写一遍库该做的幂等逻辑。** ssw 的 `UIBoot.cs` ≈ 60 行：`_loaded` / `_loading` 幂等 + `while (_loading) await
NextFrameAsync()` 并发等待、`[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 重置静态（关掉 Domain Reload 后
`UI.OnEnteringPlayMode` 清了池、宿主标志却还说"装好了"）、`ResetForSceneAsync` = `UnloadAll` + 重装。全部源于
`LoadCommonLibraryAsync` "重复调用即抛冲突" 这一条。

**③ preview 工具的 spec 为绕开这个缺口堆了三级机制。** §4.6 运行时快照 → 上一轮快照 → `UIPreviewSettings.commonLibraries` 兜底表；
宿主 boot 是 fire-and-forget 带来的竞态；§4.11 「等 1 秒」；§5 的 `LoadedCommonLibraries` / `CommonLibraryRef` / `CommonsSources`
改有序表；§11 决策 #3 #8；§7 测试 1–4；M0 整个里程碑。而且快照路线只解 Play 态的 preview——Edit 态的 lint 菜单没有任何运行时快照可用。

**只有一处声明能同时喂给运行时、lint（Edit 态与 CLI）和 preview（Play 态）：工程里已经存在的 `PromptUGUISettings` 资产。**

## 2. 否决的方案

| 方案 | 为什么不 |
|---|---|
| **A. 运行时快照 `UI.LoadedCommonLibraries`**（preview spec 现行） | Edit 态 lint 拿不到；有竞态与三级兜底；宿主仍要自己写 boot |
| **B. 约定扫描：所有无 `<Screen>` 的 `.ui.xml` 都当 commons** | ssw 有 14 个纯模板文件是 `<Import>` 目标而非 commons；全并进池会撞名，也不等于运行时真相 |
| **C. XML 里打标记 `<PromptUGUI common="true">`** | Edit 态能扫到，运行时没文件系统扫不到 → 仍要 settings 或代码指 src → 两个源必漂移 |
| **D. lint 软化：unknown style 时把 `PUI-EXPAND` 降级为 Note** | 假阳性没了，但用 commons 的工程几乎每个 Screen 都丢展开遍——glass / `class=` 带来的属性 / container-visual 这些最值钱的规则全瞎 |
| **E. `[CommonLibrary("…")]` 特性 + 反射** | Player 端启动反射扫全部类型 + IL2CPP 裁剪风险；Unity 惯例是 SO，而且 SO 已经在了 |
| **F. 两个源并存（公开 `LoadCommonLibraryAsync` + settings）** | 工具只能信 settings 那一半，「唯一源」落空——正是要消灭的歧义 |
| **G. 列表放 Editor-only 的 `ProjectSettings/PromptUGUI.asset`** | 运行时要读；那个文件是 `TranslationProvider` 的 `LoadSerializedFileAndForget` 存储，Player 不可见 |

## 3. 方案总览

```
Assets/…/PromptUGUI_Settings.asset            ← 唯一声明源：commonLibraries = [{src, as}, …]（resolver key，不是路径）
        │
        ├─ 运行时  UI.EnsureCommonLibrariesAsync()          幂等装载；LoadDocumentAsync / 模态装载 / ReloadAsync 入口自动先 await 它
        │            └─ LoadCommonLibraryAsyncInternal(src, as)   今天的实现，公开壳降 internal
        │                 └─ DocumentAssembler.AddCommonLibrary   as= 重命名 + 同名冲突 + OriginSrc，从 UI.cs 搬进 Core（纯 C#）
        ├─ Editor  Tools › PromptUGUI › Lint All UI XML     读 Instance.commonLibraries → 当每份文档的隐式 <Import> 解析、并入展开遍
        ├─ Editor  UI Preview（另一 spec）                    UnloadAll → LoadDocumentAsync（自动 Ensure，读盘最新版）
        └─ CLI     UIXmlLint                                 --settings / --commons / 自动发现 .asset → 同一份 Core 实现
```

谁管什么：

| 事 | 谁 | 依据 |
|---|---|---|
| commons 是哪几个 src、各自的 `as` | **settings 资产**，唯一 | §4.1 |
| 什么时候装、装了没 | 库：`EnsureCommonLibrariesAsync`（以池状态为准，无"已装"标志） | §4.2 |
| 宿主要写什么 | 什么都不用写；想预热主题可显式调一次 Ensure | §4.3 |
| src → 内容 | 仍是宿主的 `SourceResolver`（M4-D1 不变） | §4.2 |
| src → 磁盘文件（Edit 态 / CLI） | lint 前端：Addressables 映射 / Resources 猜测 / `--src-root`，与 `<Import>` 同一条规则 | §4.7–4.9 |
| 合并语义 | `DocumentAssembler`（Core，纯 C#）——运行时、lint 菜单、CLI 三条路一份实现 | §4.6 |

## 4. 语义细节

### 4.1 settings 字段与校验

```csharp
// Runtime/Core/IR/CommonLibraryEntry.cs —— 纯 C#（System.SerializableAttribute），CLI 编译集内
[Serializable]
public sealed class CommonLibraryEntry
{
    public string src;   // resolver key，与 <Import src> 同形（Resources 式带 .ui、Addressables 式是地址）
    public string @as;   // 可空：命名空间，模板写 <ns.Name/>，样式写 class="ns:name"
    public ImportRef ToImportRef() => new ImportRef(src, string.IsNullOrWhiteSpace(@as) ? null : @as);
}

// Runtime/Application/PromptUGUISettings.cs
public List<CommonLibraryEntry> commonLibraries = new();
```

- **一条 commons 就是每份文档头部隐含的一行 `<Import src="{src}" as="{as}"/>`**，只是合并进 commons 池（同名硬冲突）而非文档自己的模板表。
  这也是 lint 的实现定义（§4.7）。
- 顺序有意义：按列表顺序装，等价于今天逐个 `await LoadCommonLibraryAsync(src, as)`（单主题自动选中等主题副作用按此顺序发生）。
- `OnValidate`（沿用现有查重风格）：重复 `src` → `LogError`（只装第一条）；`as` 含 `.` → `LogError`（与 parser 对 `<Import as>` 的规则一致，
  `UIDocumentParser.cs:123-131`）；`src` 空白的行保留在 Inspector 里但被 Ensure / lint 忽略；`as` 空白视为 null。
- Inspector：`PromptUGUISettingsEditor` 加一段 **Common Libraries**——列表 + HelpBox 说明 src 的形态
  （"Same key shape as `<Import src>`: `UI/Templates/Theme.ui` for a Resources resolver rooted at `UI/`… the Address for Addressables"）。
- 没有 settings 资产 / 表为空 = 该工程没有 commons，是正常态，不告警（§4.10 只在真的解析失败时给提示）。

### 4.2 `UI.EnsureCommonLibrariesAsync()`

```csharp
public static Awaitable EnsureCommonLibrariesAsync();
```

- **输入**：`CommonLibraryEntries`（§4.5 的测试缝）→ 默认 `PromptUGUISettings.Instance?.commonLibraries`，过滤空白 `src`。
- **幂等**：`_depGraph.IsCommons(src)` 已在池里 → 跳过。不比对 `as`：同一 src 只能装一次，改了 `as` 要 `UnloadAllCommonLibraries`
  或下次进 Play。全部已在 → 同步返回，不碰 resolver。
- **并发**：一次只有一个装载在跑；期间的调用者挂进 waiters（`AwaitableCompletionSource` 列表，同 `DocumentCache.s_inflight` 的手法），
  装载结束一起放行 / 一起收到同一个异常。**不用** `NextFrameAsync` 轮询——EditMode 无 PlayerLoop 会挂死，宿主 `UIBoot` 的 while 循环正是这个坑。
- **失败**：某条抛（`IOException` 取不到 / `ParseException` / `TemplateException` 同名冲突 / 含 `<Screen>`）→ 异常原样上抛；它之前的条目
  留在池里，之后的不装；下次调用从失败那条继续（幂等跳过已装的）。单条内部仍是今天的 all-or-nothing（staged 提交）。
- **resolver**：条目非空且 `SourceResolver == null` → `InvalidOperationException`（文案同今天）；**条目为空 → 无论 resolver 有无都直接返回**。
  没 commons 的工程、纯 `LoadDocument(label, xml)` 的工程、内置模态在 EditMode 无 PlayerLoop 下的同步路径全部不受影响。
- **主题**：同今天（`RegisterThemesAndAutoSet` / 单主题自动选中 / `WarnIfPendingThemeUnloaded`）。`UI.Theme.Set` 仍可先于 Ensure 调用。
- **域重载关闭**：以池状态为准，不存"已装"标志；`[OnEnteringPlayMode] UnloadAll` 清池后下一次自动 Ensure 重装。ssw `UIBoot.ResetStatics`
  那类补丁在库里不需要。
- **源缓存**：`UnloadAll` / `UnloadAllCommonLibraries` 都连 `DocumentCache` 一起摘，所以之后的 Ensure 经 resolver 重读——预览工具
  「每次都是磁盘最新版」靠这个，不需要额外机制。

### 4.3 自动 Ensure 的挂点

三个合并 `_commonsPool` 的入口在取源之前 `await EnsureCommonLibrariesAsync()`：

| 入口 | 覆盖 |
|---|---|
| `UI.LoadDocumentAsync(src)` | 普通 Screen |
| `UI.LoadDocumentWithCommonsAsync(label, xml)` | 模态 / LoadingOverlay（经 `ModalDocCache.EnsureLoaded`），自定义模态皮的 `class=` |
| `UI.ReloadAsync(name)` | 热重载；Play 中刚往 settings 加了库的情况下重载也看得见 |

`LoadDocument(label, xml)`（同步、raw）不变：它从来不合并 commons。`ReloadCommonLibraryAsync(src)` 是按 src 的重装，不 Ensure 其它条目。

显式调用的场景：boot 里 `_ = UI.EnsureCommonLibrariesAsync();` 预热，让 `Theme.Set("dark")` 在首屏加载前就能解析（不调也行，
首屏 `LoadDocumentAsync` 会带上，主题在那时注册）；`UnloadAll` 后想立刻重装；preview 工具的诊断行。

### 4.4 其余 API 的去留

- `UI.LoadCommonLibraryAsync(src, as)` → **internal**。45 处测试调用不动（四个测试程序集都在 `InternalsVisibleTo` 里）；`Ensure` 用它。
- `UI.ReloadCommonLibraryAsync(src)`：从条目表按 src 查 `as` 重装——修掉 M4-R8「reload 丢命名空间」；src 不在表里（internal 装的）→ `as: null`
  同今天。热重载 `NotifyAssetChanged`（`UI.cs:1339`）走它，不变。
- `UI.UnloadAllCommonLibraries()` / `UI.UnloadAll()`（`UI.cs:1042`）：语义不变。`UnloadAll` **仍清 commons**——预览工具依赖「整个重读」，
  而自动 Ensure 让「忘了装回」不再是坑。`UnloadAllCommonLibraries` + `Ensure` = 「重读重装」的显式写法。
- `UI.ResetForTests`：清池 + 清 in-flight / waiters + 缝置空表（§4.5）。

### 4.5 `Instance` 的 Editor 兜底与测试隔离

- `PromptUGUISettings.Instance`（`PromptUGUISettings.cs:90`）：扫描为空且 `FinderForTests == null` 时，`#if UNITY_EDITOR` 用
  `AssetDatabase.FindAssets("t:PromptUGUISettings")` 载入第一个（多个的告警由 `PromptUGUISettingsAutoMaintainer` 负责）。堵住
  「maintainer 的 `delayCall` 还没跑、宿主 `BeforeSceneLoad` 就调 Ensure」的窗口；`Locale.InitializeIfNeeded`（`UI.cs:1312`）
  同一时机读 `Instance`，一并受益。Player 端靠 `preloadedAssets`（§7-30 验证）。
- **测试缝**：`internal static Func<IReadOnlyList<CommonLibraryEntry>> UI.CommonLibrariesForTests`（null = 读 settings）。
  `ResetForTests` 置为 `() => 空表`——否则在 ssw 里跑包测试时，宿主真实 settings 的条目会经测试的 fake resolver 装载而炸。
  `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 把缝复位为 null：关掉 Domain Reload 的工程跑完 EditMode 测试直接进 Play，
  静态残留不会让真 Play 看不到 settings。Editor 菜单不经这条缝（直接读 `Instance`），不受测试残留影响。
- 现有的 `PromptUGUISettings.FinderForTests` 不动：它是字体 / locale 测试的缝；`I18nFontSwapTests` 靠真扫描找到自己 `CreateInstance` 的资产。

### 4.6 Core：`DocumentAssembler.AddCommonLibrary`（单一实现）

```csharp
// Runtime/Core/Template/DocumentAssembler.cs —— 纯 C#
public static void AddCommonLibrary(
    LoadedDoc library, string ns, string originSrc,
    Dictionary<TemplateKey, TemplateDef> pool, Dictionary<StyleKey, StyleDef> styles,
    List<(ThemeBlock Theme, string Src)> themes = null)
```

- 今天 `UI.cs:735-770` 的逻辑原样搬家，语义不变：键重命名 `ns == null ? key : new (ns, key.Name)`（Style 同；库内部 `<Import as="x">`
  带来的内层命名空间被 `ns` 覆盖，M4 §4.2-6）；与池已有键同名 → `TemplateException("common library conflict: '{key}' already in commons pool")`，
  先 stage 后 commit，抛时池不变；`def.OriginSrc = originSrc`（reload 按它回收）。
- `themes` 非空时把 `library.Themes` 追加进去——lint 用（§4.7）；运行时传 null，主题照旧走 `ThemeStore`。
- 运行时 `LoadCommonLibraryAsyncInternal` = `LoadParsedAsync(src, allowScreens: false)` → `AddCommonLibrary` → 主题注册 → depGraph。
- **为什么必须搬**：这段今天在 `Application/UI.cs`，CLI 编译集（`Core/IR` / `Parser` / `Template` / `Lint`）够不着；不搬就得在 lint 里再写
  一遍重命名与冲突规则，CLAUDE.md 的「单一实现」约束破裂。

### 4.7 lint：commons 是每份文档的隐式 `<Import>`

运行时怎么合并，lint 就怎么合并——差别只在 src 怎么变成文件，而那本来就是两个前端各自的事。

> **分工**（§13）：本案只交付这里的 Core 原语——`AddCommonLibrary`、`CommonLibraryEntry`、`SettingsAssetReader` 和
> `DocumentLinter.Walk(..., commons)`；`ImportClosure` / `LintRun` / 菜单 / CLI 这些消费侧改动落在 `feat/lint-menu` 分支
> （它们本来就是那个分支正在写的文件），等本案合并后 rebase 接手。§4.7 的 `ImportClosure` / `LintRun` 两条、§4.8、§4.9 整节
> 是写给那个分支的规格，不是本案的交付。

- `ImportClosure.TryLoad(entryPath, entry, resolve, read, commons, out unresolved)`（`ImportClosure.cs:65`）：先按入口的 `<Import>` 走，再对每条
  commons `resolve(src, entryPath)`——importing path = **入口文件**：Resources 式短 key 从入口目录向上猜到 `Resources/`，与 `<Import>` 同一条
  规则——解析、递归它自己的 Import。任一 commons 解析不到 → 同今天的 Import 策略：返回 null、`unresolved` 记下 src → `LintRun` 出 Note
  （文案区分 `<Import src="…">` 与 `common library src="…"`），**跳过展开遍，raw 规则照跑**——宁可少看，不报假阳性。
- `DocumentLinter.Walk(doc, entrySrc, imports, commons)`：`Assemble(entry)` 之后，对每条 commons `Assemble(src, imports, allowScreens: false)`
  → `AddCommonLibrary(..., themes)` 进本次 Walk 的局部池 → `MergeCommons(loaded, pool, styles)` → 其余不变。`imports == null` 的早退条件改为
  「有 Import **或**有 commons」。
- commons 里的 `<Theme>` 进 `ThemesByName`：`CheckBaselines` / `CheckShape` / 逐主题再推导遍都看得见它——ssw 那条「只活在 `<Theme>` 里的样式包
  永远够不着」的限制随之消失。
- commons 自身含 `<Screen>` → `PUI-EXPAND`（运行时 `LoadCommonLibraryAsync` 同样抛）；入口与 commons 同名 →
  `PUI-EXPAND: template 'x' conflicts with commons pool`（同运行时文案）；条目 `as` 含 `.` → `LintRun` 一条 Error（settings 校验的 CLI 侧镜像）。
- 归属与去重不变：commons 模板体内的问题按 `ElementNode.OriginSrc` 报到 commons 文件的行号，`(via 入口:行)`；多个入口到达同一缺陷折叠一次
  （`LintRun._reported`）。目录递归扫到 commons 文件本身时它照常作入口跑 raw + 展开——不因它同时是 commons 而重复报。
- `LintRun.Lint(path, read, resolve, commons)`（`LintRun.cs:66`）：`commons` 是 `IReadOnlyList<ImportRef>`。commons 闭包**一次 run 只解析一次**、
  跨入口复用（几十个文件反复解析同一份 DefaultTheme 没意义）：`LintRun` 缓存 `src → UIDocument`；解析不到的 Note 也只出一次。

### 4.8 Editor 菜单（`Editor/UIXmlLintMenu.cs`）

- `LintAll()`：`PromptUGUISettings.Instance?.commonLibraries` → `ImportRef` 列表 → `Lint(paths, report, commons)`（`UIXmlLintMenu.cs:60` 多一个参数，
  测试直接传表）。
- 解析：`MakeResolver()`（`:163`）不变——Addressables 地址 / GUID → asset path；否则 `ResolveInResources` 从入口文件目录起猜。Console 里 commons 文件内的
  finding 以那个 TextAsset 为 context（`Report` 不变）。
- 末尾提示：本次运行出现了 `unknown style` / `unknown template` 的 `PUI-EXPAND` **且**表为空 → 一条 Info：
  `No common library is configured in PromptUGUISettings — if these names live in a shared library, list it under Common Libraries.`
  表非空时不提示（那就是真错）。

### 4.9 CLI（`.lint/UIXmlLint`）

- `--settings <file.asset>`：显式指定；`--commons <src>[@<as>]`：可重复，不读资产（CI、无 Unity 工程的场合）；两者都没给 → **自动发现**：
  从每个入口向上找最近的 `Assets/` 祖先，扫其下 `**/*.asset` 的头部（前 4 KB）`m_Script` 行 guid 是否为 `PromptUGUISettings.cs.meta` 的
  `ef72af4be0229a24cb2ab979147bdc01`；找到多个 → 取第一个并在 stderr 提示；找不到 → 视为无 commons（今天的行为）。
- `Runtime/Core/Lint/SettingsAssetReader.cs`（纯 C#、有测试）：`IsPromptUGUISettings(head)` + `ReadCommonLibraries(yaml) → IReadOnlyList<ImportRef>`。
  只认 Unity 文本序列化的固定形态（`commonLibraries:` 下 `- src:` / `as:` 两行、`[]` 空表、单 / 双引号字符串与 `''` 转义）；解析不出的行报
  stderr 并跳过。§7-26 用 `AssetDatabase.CreateAsset` 真序列化一份再读回，锁住字段名与形态。
- src → 文件：同 §4.7（从入口目录起 `ResolveInResources`）。**`--src-root <dir>`**（可选，Import 与 commons 都多试一次 `root/src(.xml)`）：
  `UseResourcesResolver(root)` 的 root，或 Addressables 地址的公共前缀目录——ssw 这类短地址工程给 `--src-root Assets/_Project/Common`
  就能跑到展开遍；今天它们只有 raw 遍。
- usage 文案、README 同步（§8）。

### 4.10 找不到样式 / 模板时的提示

`LoadDocumentAsync` / `LoadDocumentWithCommonsAsync` 捕到 `Expand` 抛的 `TemplateException`，消息含 `unknown style` / `unknown template`，
**且**本次条目数为 0 → 重新抛同类型异常，消息追加：
`— no common library is configured (PromptUGUISettings.commonLibraries is empty or there is no settings asset); if '{name}' lives in a shared library, list that library there.`
让「忘了配 settings」在第一次 Open 就能读懂。条目非空时不追加（那是真错，原文案已列出 known 名单）。

## 5. 公开 API 变化

| 项 | 今天 | 之后 |
|---|---|---|
| `PromptUGUISettings.commonLibraries` | — | 新增 `List<CommonLibraryEntry>`（`src` / `as`），Inspector 可编辑 |
| `PromptUGUI.IR.CommonLibraryEntry` | — | 新增（Core，纯 C#） |
| `UI.EnsureCommonLibrariesAsync()` | — | 新增，公开，幂等 |
| `UI.LoadCommonLibraryAsync(src, as)` | 公开 | **internal** |
| `UI.ReloadCommonLibraryAsync(src)` | 公开；reload 丢 `as` | 公开；按 settings 恢复 `as` |
| `UI.UnloadAllCommonLibraries()` / `UI.UnloadAll()` | 公开 | 不变 |
| `UI.LoadDocumentAsync` / 模态装载 / `ReloadAsync` | 只合并已在池里的 | 先自动 Ensure |
| `Tools › PromptUGUI › Lint All UI XML` | commons-blind | 读 settings |
| UIXmlLint CLI | 无 commons 概念 | `--settings` / `--commons` / 自动发现 / `--src-root` |

除此之外 Runtime **不加任何东西**（preview spec §5 的 `LoadedCommonLibraries` 不再需要）。

## 6. 实现地图

**本案（分支 `feat/commons-settings`，从 `main` 起）**——没有一个文件与 `feat/lint-menu` 的改动集重叠：

| 文件 | 内容 |
|---|---|
| `Runtime/Core/IR/CommonLibraryEntry.cs` | 新：`[Serializable]` 条目 + `ToImportRef()` |
| `Runtime/Core/Template/DocumentAssembler.cs` | `AddCommonLibrary(...)`（从 `UI.cs` 搬来） |
| `Runtime/Core/Lint/DocumentLinter.cs` | `Walk(..., commons = null)`：局部池 + 主题合并；早退条件。尾部可选参数，现有调用（含 lint-menu 的 `LintRun`）不改也能编译 |
| `Runtime/Core/Lint/SettingsAssetReader.cs` | 新：`.asset` YAML 里读 `commonLibraries`；guid 常量 |
| `Runtime/Application/PromptUGUISettings.cs` | `commonLibraries`；`OnValidate` 查重 / 查 `as`；`Instance` 的 Editor 兜底 |
| `Runtime/Application/UI.cs` | `EnsureCommonLibrariesAsync` + waiters + 缝 + `SubsystemRegistration` 复位；三个入口自动 Ensure；`LoadCommonLibraryAsync` internal；`ReloadCommonLibraryAsync` 取 `as`；§4.10；`ResetForTests` |
| `Editor/PromptUGUISettingsEditor.cs` | 「Common Libraries」段：列表 + HelpBox |
| `Tests/EditMode/Application/EnsureCommonLibrariesTests.cs`（新） | §7-1 ~ 15 |
| `Tests/EditMode/Lint/DocumentLinterTests.cs` / `DocumentAssemblerCommonsTests.cs`（新） / `SettingsAssetReaderTests.cs`（新） | §7-16 ~ 20、23、24 |
| `Tests/EditMode/Editor/SettingsAssetRoundTripTests.cs`（新） | §7-26 |

**`feat/lint-menu` 分支接手（本案合并后 rebase）**——全是它自己已经在改的文件：

| 文件 | 内容 |
|---|---|
| `Runtime/Core/Lint/ImportClosure.cs` | `TryLoad(..., commons, out unresolved)`：commons 按入口路径解析 |
| `Runtime/Core/Lint/LintRun.cs` | `Lint(path, read, resolve, commons)`；commons 闭包跨入口缓存；Note 文案；非法 `as` Error |
| `Editor/UIXmlLintMenu.cs` | 读 settings → `Lint(paths, report, commons)`；末尾提示 |
| `.lint/UIXmlLint/Program.cs` | `--settings` / `--commons` / 自动发现 / `--src-root`；usage |
| `.lint/UIXmlLint/README.md` | 「Common libraries」节；删过时的 Scope 条目 |
| `Tests/EditMode/Lint/LintRunTests.cs` / `ImportClosureTests.cs`、`Tests/EditMode/Editor/UIXmlLintMenuTests.cs` | §7-21、22、25 |

## 7. 测试（Red first）

EditMode（`PromptUGUI.Tests.EditMode`；条目经 `UI.CommonLibrariesForTests` 给，fake resolver 计数）：

1. 按顺序装两条，第二条 `as: "ui"` → `<ui.Card/>` 与 `class="ui:card"` 可用，第一条裸名可用。
2. 第二次 Ensure 不碰 resolver（计数不变）。
3. `UnloadAll` → Ensure → resolver 再次被调（源缓存已清），池重建。
4. 已用 internal API 装了 A → Ensure 只装 B。
5. 并发：resolver 返回挂起的 `AwaitableCompletionSource`（`DocumentCacheTests` 的手法）；两次 Ensure 同时挂起；完成后两者都结束，每个 src 只取一次。
6. 条目为空 + resolver 为 null → 不抛、不碰任何东西。
7. 条目非空 + resolver 为 null → `InvalidOperationException`。
8. 第二条失败（resolver 返回 null）→ `IOException`；第一条仍在池里；修好后再 Ensure 只装第二条。
9. `LoadDocumentAsync` 不显式 Ensure 也能用 commons 模板（自动 Ensure）。
10. `LoadDocumentWithCommonsAsync`（模态路径）同上。
11. `ReloadCommonLibraryAsync(B)` 后 `ui.Card` 仍在 `ui` 命名空间（今天掉到裸名——Red；即 preview spec §7-3）。
12. reload 失败回滚后池与失败前相同（含 `as`）。
13. 缝为 null 时从 `PromptUGUISettings.FinderForTests` 给的实例读 `commonLibraries`（空白 src 被过滤、空白 `as` 视为 null）。
14. `ResetForTests` 后缝返回空表（宿主真实 settings 不泄漏进测试）。
15. `PromptUGUISettings.OnValidate`：重复 src → `LogAssert.Expect(Error)`；`as` 含 `.` → Error；空 src 行保留、Ensure 忽略。

Lint——本案：`DocumentLinterTests`（`Walk` 直接喂内存 lookup，commons 以 `ImportRef` 列表给）、`DocumentAssemblerCommonsTests`、`SettingsAssetReaderTests`；
**21、22 归 lint-menu**（`LintRunTests` / `ImportClosureTests`）：

16. 入口 `class="badge"`、`badge` 只在 commons → 无 `PUI-EXPAND`（今天 Red，§1 ①）；且展开遍的规则能报（`class=` 带入 `sprite` 的 `PUI-CONTAINER-VISUAL-ATTR`）。
17. commons `as="ui"`、入口 `<ui.Card/>`、Card 体内 `<Frame mask="self">` → finding 的 `Origin` 是 commons 的 src、`Via` 是入口的调用行。
18. `<Theme>` 与基线 `<Style>` 都在 commons → 无 `PUI-THEME-STYLE-NO-BASELINE`；基线缺失时仍报；逐主题再推导遍在 commons 主题下也跑（一个只在该主题下触发的 `GlassRules` 用例）。
19. 入口重复声明 commons 里的 `badge` → `PUI-EXPAND … conflicts with commons pool`。
20. commons 含 `<Screen>` → `PUI-EXPAND`。
21. *(lint-menu)* commons src 解析不到 → 一条 Note（文案含 `common library`），跳过展开遍，raw 规则照报，**无** `PUI-EXPAND`。
22. *(lint-menu)* 两个入口同用一份有缺陷的 commons → 该缺陷只报一次；commons 闭包一次 run 只被 `read` 一次（读计数）。
23. `DocumentAssembler.AddCommonLibrary`：重命名 / 冲突抛后池不变 / `OriginSrc` 打戳 / `themes` 追加。
24. `SettingsAssetReader`：手写 YAML（空表 `[]`、两条、带引号、`as` 空）→ 正确读出；非 settings 的 `.asset` `IsPromptUGUISettings` 为假。

EditorOnly（`PromptUGUI.Tests.EditorOnly`）：

25. *(lint-menu)* `UIXmlLintMenu.Lint(paths, report, commons)`：临时目录里 commons + 入口，条目 src 用 `lib.ui` 短名 → 从入口目录猜到 `lib.ui.xml`，展开遍生效。
26. `SettingsAssetReader` 往返：`CreateInstance` + 填两条 → `InternalEditorUtility.SaveToSerializedFileAndForget`（文本序列化）到临时文件 → 读文件 → 读回相同两条；空表序列化成 `commonLibraries: []`。（不用 `AssetDatabase.CreateAsset`：工程里多出第二个 settings 资产会被 `PromptUGUISettingsAutoMaintainer` 报错。）

手工验证（ssw_re_client，Unity MCP）：

27. settings 加 `UI/Templates/DefaultTheme.ui.xml`，删 `UIBoot` 的装载代码 → 进 Play，Login / Lobby / Round 正常，`class="badge"` 不再 unknown。
28. *(lint-menu)* `Tools › PromptUGUI › Lint All UI XML`：Round 那棵树上 commons 样式不再 `PUI-EXPAND`；`<Theme name="round">` 的基线检查看得见 DefaultTheme。
29. 改 DefaultTheme.ui.xml 保存 → 热重载 → 打开的界面换色。
30. Player 构建（任一平台）：`BeforeSceneLoad` 里显式 `EnsureCommonLibrariesAsync()` 能找到 settings（`preloadedAssets`）——日志 `Instance != null` + 首屏正常。
31. 关掉 Domain Reload：跑一遍 EditMode 测试后直接进 Play，commons 仍装载（缝已复位）。
32. *(lint-menu)* CLI：`dotnet run --project .lint/UIXmlLint -- Assets/_Project/ --src-root Assets/_Project/Common` 在 ssw 跑到展开遍；不给 `--src-root` 时只出 Note，不出假阳性。

## 8. 文档更新（同一 PR，英文）

- `.claude/skills/scripting-promptugui-csharp/SKILL.md`：Boot 示例去掉 `LoadCommonLibraryAsync` 行、加一句 settings；「Commons pool」段改写
  （settings 声明、自动 Ensure、显式 Ensure 预热主题、`UnloadAll` 后自愈）；主题章节里三处 `LoadCommonLibraryAsync` 提法；Tooling 段加
  「lint 菜单 / CLI 读同一份 settings」。
- `.claude/skills/authoring-promptugui-xml/SKILL.md`：`PUI-EXPAND` 那句（lint 现在看得见 commons）、「commons pool populated C#-side」→
  「declared in PromptUGUISettings」、主题加载那行。
- `BEST_PRACTICES.md` / `.zh.md` §1 boot 片段与 §4 主题注册行。
- `README.md` / `README.zh.md`：Usage 里一句 settings 资产（Create → PromptUGUI → Settings → Common Libraries）。
- *(lint-menu)* `.lint/UIXmlLint/README.md`：新增「Common libraries」节（`--settings` / `--commons` / 自动发现 / `--src-root`）；删掉过时的
  「Scope：No cross-file resolution / No Template expansion」两条（展开遍早已存在）。
- `CLAUDE.md`：Critical Conventions 里 commons 那条改为 settings + Ensure；`LoadDocumentWithCommonsAsync` 注释里「模态 XML 用不了 commons」的旧说法
  一并修；UIXmlLint 段加 commons 一句。
- master spec §7.6 末尾加一句：commons 由 `PromptUGUISettings.commonLibraries` 声明、`EnsureCommonLibrariesAsync` 装载，指向本文；M4-R8 标记已修。
- preview spec：见 §10。

## 9. 宿主迁移（ssw_re_client，另案）

1. `Assets/Settings/PromptUGUI_Settings.asset` → Common Libraries 加 `UI/Templates/DefaultTheme.ui.xml`。
2. 删 `UIBoot.CommonLibrarySrc` / `EnsureCommonsAsync` / `ResetForSceneAsync` / `ResetStatics`；`GameBootstrap.cs:40` 改
   `_ = UI.EnsureCommonLibrariesAsync();`（保留预热，让 `Theme.Set("dark")` 首屏前就能解析）或直接删；`Lobby.cs:68` 删；场景切换处
   `UIBoot.ResetForSceneAsync()` → `UI.UnloadAll()`。
3. `Assets/Tools/UI Preview/UIPreview.cs:487` → `UI.EnsureCommonLibrariesAsync()`（库版预览工具落地后整个文件删）。
4. `GlassStyle.ui.xml` 头注释里的绕行说明作废：`badge` / `badge-text` 留在 DefaultTheme 即可，`<Theme name="round">` 的样式包可以搬回
   DefaultTheme（lint 现在看得见）——是否搬回由作者定。
5. 验 §7-27 ~ 32。

## 10. preview spec（`2026-09-18-ui-preview-tool-design.md`）改动清单

- §1 表「commons 地址」行 → 「settings 声明，工具不再需要知道」。
- §3 表「commons 地址」行 → `LoadDocumentAsync` 自动 Ensure。
- §4.6 整节 → 一句：`UnloadAll` 后 `LoadDocumentAsync` 自动重装 commons（读盘最新版靠工具 resolver + `DocumentCache.Clear`）；竞态段删除。
- §4.7 伪码：删 `snapshot` 行与 `foreach … LoadCommonLibraryAsync` 行。
- §4.9 诊断行「commons 快照为空」→ 「settings 未列 common library」（黄，指向 settings 资产；`UI.Theme.Available.Count == 0` 时才显示）。
- §4.11：不再等 commons 出现，注入后直接加载上次文件。
- §4.12：`UIPreviewSettings` 去掉 `commonLibraries` 兜底表。
- §5 整节删除；`as` 丢失的修复由本文 §4.4 承担。
- §6 实现地图：删 `DepGraph.cs` / `UI.cs` 两行。
- §7 测试 1–4 删除（本文 §7-11、12 覆盖 reload 保 `as`）。
- §9 宿主迁移第 2 步：删「commons 兜底表留空」。
- §11 决策 #3 → 「只问场景」成为唯一形态；#8 删除。
- §12 M0 删除。

## 11. 非目标

- 运行时按条件装不同 commons（按平台 / DLC）：settings 是静态表；真需要时另加显式 API，并明确工具看不见它。
- Play 中改 settings 列表即时生效（`OnValidate` → Ensure）：下次进 Play 生效（`[OnEnteringPlayMode] UnloadAll` + 自动 Ensure）。
- Inspector 里拖 `.ui.xml` 自动算 src：resolver 形态库不知道；v1 手填字符串，lint 菜单是它的校验器。
- commons 库自己 `<Import>` 的文件改动触发 commons 热重载（`NotifyAssetChanged` 只认顶层 commons src）：既有缺口，不在本案。
- `PromptUGUIDocumentHost`（Edit 态同步 `LoadDocument`）读 commons：不做。
- `UnloadAll` 改为保留 commons：不改（预览工具依赖「整个重读」）。

## 12. 已定的决策 / 待作者确认（推荐第一列）

| # | 项 | 推荐 | 备选 |
|---|---|---|---|
| 1 | 破坏公开 API | **已定**：接受（尚无正式使用者） | — |
| 2 | `LoadCommonLibraryAsync(src, as)` | 降 internal | 保留公开但文档写明工具看不见它装的 |
| 3 | 自动 Ensure | `LoadDocumentAsync` / 模态装载 / `ReloadAsync` 三处自动 | 只提供显式 Ensure |
| 4 | 名字 | `EnsureCommonLibrariesAsync`（与 `ReloadCommonLibraryAsync` / `UnloadAllCommonLibraries` 同族） | `EnsureCommonsAsync`（作者原提法） |
| 5 | CLI 读 settings | `--settings` / `--commons` + guid 自动发现一步到位 | 只做参数，自动发现后续 |
| 6 | `--src-root` | 做（几行，ssw 立刻从 raw 遍升到展开遍） | 不做 |
| 7 | 与 `feat/lint-menu` 的顺序 | **已定**：本案先行，从 `main` 起、不碰 lint-menu 的 13 个文件；lint-menu 合并 `main` 后接手消费侧（§6 第二张表）。可行的前提已核过：lint-menu 相对 `main` 的真实改动只有那 13 个文件（`git status` 里 `UI.cs` 等 4 个 ` M` 是 autocrlf 行尾噪音，`git diff` 为空），而本案改的 `UI.cs` / `PromptUGUISettings.cs` / `DocumentAssembler.cs` / `DocumentLinter.cs` 都不在其中；`Walk` 加的是尾部可选参数 | 先合 lint-menu 再做本案 / 并入 lint-menu 一起交 |
| 8 | §4.10 unknown style 提示 | 做 | 不做 |
| 9 | `CommonLibraryEntry` 放哪 | `Core/IR`（纯 C#，settings 与 CLI 共用一个类型） | `PromptUGUISettings` 嵌套类，Core 只认 `ImportRef` |

## 13. 里程碑

本案（`feat/commons-settings`，从 `main` 起）：

| # | 内容 | 验收 |
|---|---|---|
| M0 | Core：`CommonLibraryEntry`；`AddCommonLibrary` 搬家（纯重构，全绿）；`Walk(..., commons)`（§7-16~20、23 Red → Green）；`SettingsAssetReader`（§7-24、26） | EditMode / EditorOnly 全绿；`dotnet build .lint/UIXmlLint` 通过（编译集仍纯 C#，`main` 版 `Program.cs` 不改也能编译） |
| M1 | Runtime：settings 字段 + `OnValidate` + Editor 段；`EnsureCommonLibrariesAsync` + 缝 + 自动 Ensure + internal 化 + reload 保 `as` + `Instance` 兜底 + §4.10（§7-1~15） | EditMode / PlayMode 全绿；ssw §7-27、29、30、31 |
| M2 | 文档（§8，lint README 除外）+ preview spec 改动（§10）；宿主迁移（§9）另案 | lint 过、PR、合入 `main` |

`feat/lint-menu` 接手（合并 `main` 之后，追加到它自己的 plan 里）：

| # | 内容 | 验收 |
|---|---|---|
| L1 | `ImportClosure.TryLoad(..., commons)` + `LintRun.Lint(..., commons)`（§7-21、22 Red → Green） | EditMode 全绿 |
| L2 | 菜单读 `PromptUGUISettings.Instance.commonLibraries`（§7-25）+ 末尾提示（§4.8） | ssw §7-28 |
| L3 | CLI：`--settings` / `--commons` / 自动发现 / `--src-root`（§4.9）+ lint README | ssw §7-32 |

## 14. 实施记录（2026-09-18，分支 `feat/commons-settings`）

M0（`8cb6383`）、M1（`87f0f38`）与 M2 文档已落地；与上文的出入：

- **reload 的 `as` 不查 settings，记在 `DepGraph.CommonsSources`**（`HashSet<string>` → `Dictionary<string, string>`，src → 装载时的命名空间）。
  比 §4.4 写的「从条目表按 src 查」更稳：internal 路径装的库也不丢，失败回滚也按它还原。`DepGraphTests` 相应改两行。
- **`AddCommonLibrary` 多拦一种冲突**：同一个库里两条记录重命名后落到同一个键（库自带 `<Import as="x">` 的 `T` 与裸 `T` 在 `ns` 下都成 `ns.T`），
  以前静默后者覆盖前者，现在同样抛 `common library conflict`（M4-D4 fail loud）。
- **lint 多报一种**：`<Theme>` 同名同时出现在 commons 与入口闭包 → `PUI-EXPAND: duplicate <Theme name="x"> in 'a' and 'b'`，
  文案同运行时 `ThemeStore.Register`。
- **`SettingsAssetReader.ReadCommonLibraries` 返回 `List<CommonLibraryEntry>`**（与资产同型），调用方 `ToImportRef()`；§4.9 写的是 `ImportRef`。
- **`TemplateException` 加 `(message, inner)` 构造**（Core，纯 C#），§4.10 的提示用它包一层。
- **测试隔离缝**按 §4.5 落地；另外发现一个测试卫生坑并写进测试注释：测试里给 resolver 一个永不完成的 `AwaitableCompletionSource`
  后没完成就结束，会留在 `DocumentCache.s_inflight`（`Clear()` 不清 in-flight），同一轮里后面所有取同一 src 的测试都挂在它上面——
  症状是 `GetAwaiter().GetResult()` 返回 null / `Screen 'X' not loaded`，单跑却能过。
- **宿主 ssw 的 §9 迁移已做完**（PR #151 合并后，另一次改动）：`PromptUGUI_Settings.asset → Common Libraries` 列
  `UI/Templates/DefaultTheme.ui.xml`；`UIBoot` 只剩三个主题名常量（`CommonLibrarySrc` / `_loaded` / `_loading` / `ResetStatics` /
  `EnsureCommonsAsync` / `ResetForSceneAsync` 全删）；`GameBootstrap` 预热 `_ = UI.EnsureCommonLibrariesAsync()`；`AppFlow` 切场景 =
  `UI.UnloadAll()` + `await UI.EnsureCommonLibrariesAsync()`（显式装回，让它算进加载步）；`Lobby.cs` 的二次 Ensure 删掉；
  `Assets/Tools/UI Preview/UIPreview.cs` 改调 `EnsureCommonLibrariesAsync()`；`GlassStyle.ui.xml` 头注释把「lint 看不见 commons」的
  绕行理由改成「取舍」——样式没搬回 DefaultTheme（§9-4 由作者定）。验证：Login → `AppFlow` → Round 正常，`Theme.Available =
  dark,lobby,round`，Console 无 PromptUGUI 错误。
- 验证：EditMode 4417 / EditorOnly 全量 / PlayMode 244 全绿；`dotnet format --verify-no-changes` 与 `dotnet build .lint/UIXmlLint` 通过。
  §7-27（ssw 进 Play：`Theme.Available = dark,lobby,round` 来自 settings 声明的 DefaultTheme，Round 界面正常，Console 无 PromptUGUI 错误）与
  §7-31（ssw 关着 Domain Reload：跑完 EditMode 测试直接进 Play，settings 仍被读到）已在 Unity MCP 里验过；§7-29 的手工热重载、§7-30 的 Player
  构建未做；§7-28、32 归 lint-menu 分支。
- **（2026-09-18，`feat/ui-preview-tool` 补）waiter 放行顺序**：原实现先 `ReleaseCommonsWaiters` 再在 `finally` 放下 `_ensuringCommons`，
  而 waiter 续体同步执行——一个「等完再 `UnloadAll` + 再 Ensure」的调用者会在标志仍立着时把自己挂到没人放行的表上（预览工具
  的注入序列实测死锁）。现在先放标志再放行；回归测试 `Ensure_AWaiterThatUnloadsAndEnsuresAgain_StartsANewLoad_InsteadOfWaitingForever`。
