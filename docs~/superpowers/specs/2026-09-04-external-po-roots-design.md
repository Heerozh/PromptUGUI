# 外部 .po 根目录 —— `externalPoRoots`（EPR）

> 状态：**已对齐，待实施**（2026-09-04）。决策编号 `EPR-Dn`（§7）。
> 相关：`Editor/I18n/StringExtractor.cs`（提取与 orphan 报告）、
> `Editor/I18n/AddressablePoLabelSyncer.cs`（目录竞选与打标）、
> `Editor/I18n/PoFileWriter.cs`（合并语义）、
> `docs~/superpowers/plans/2026-05-08-i18n-m5b-extraction.md`（提取管线的原始计划）。
> 下游用例：ssw 游戏服务器把「服务器产的玩家可见文本」（星系名 / 势力名 / 对局展示名）导出成 .po
> 写进 Unity 工程，见 `ssw_re_server/docs/superpowers/specs/2026-09-04-server-text-i18n-export-design.md`。

## 0. 背景

PromptUGUI 的 i18n 管线假设**所有 msgid 都来自本工程的 `.ui.xml` / C# 字面量**：Extract 扫源、
按 partition 写 .po、msgstr 由翻译菜单或 CI 填。

真实项目里有第三类来源：**工程外的工具生成的 .po**。ssw 的服务器把星系名、势力名等玩家可见文本
导出成 .po 放进 Unity 工程——这些 msgid 客户端扫不到（它们在 C# 里长成 `UI.Tr(变量)`），却必须和
扫出来的那批一起被打包、被翻译、被 `TranslationStore` 加载。

运行时其实**已经支持**了：`UI.Locale.UseAddressableResolver()` 按 label `Locale:<locale>` 加载
所有 .po，`TranslationStore` 按 `(locale, ctx, msgid)` 并表，来源是谁无所谓。出问题的只有 Editor 侧
的三处「本工程假设」。

## 1. 现状：外部 .po 放进工程会撞上什么

| # | 机制 | 位置 | 撞法 |
|---|---|---|---|
| 1 | **条目级删除** | `PoFileWriter.Merge` | 「本轮提取里没有的条目丢弃」。外部串若写进客户端的 partition 文件（如 `_code.po`），下次 Extract 全部消失 |
| 2 | **文件级 orphan 报告** | `StringExtractor.ReportOrphanPoFiles` | localeDir 下每个对不上 partition 的 .po 都 `Debug.LogError` 一条「Orphan .po file … delete manually」。文件不会被删，但每次提取刷红 |
| 3 | **目录竞选** | `AddressablePoLabelSyncer.ResolveOutputDirForLocale` ← `CollectAddressablePoPathsByLocale` | 提取的输出目录 = 所有带 `Locale:` label 的 .po 的「最近的、名为 `<locale>` 的父目录」中 **Ordinal 第一个**。多于一个只 warning。外部 .po 一旦带 label 且落在名为 `<locale>` 的目录下，就参与竞选——**目录名排在前面时会把客户端自己的提取输出重定向过去**（`i18n-server/`、`ServerText/` 都排在 `i18n/` 前面） |

第 3 条是真陷阱：它不报错，只是把 Extract 的结果写去了别处。

## 2. 设计：一个「外部根」清单

`PromptUGUISettings` 增加一个字段：

```csharp
[Tooltip("Project-relative folders holding .po files produced by external tools " +
         "(e.g. a game server exporting runtime-provided strings). Extraction never " +
         "writes to or reports on them; labelling and translation still include them.")]
public List<string> externalPoRoots = new();   // e.g. "Assets/_Project/i18n_server"
```

语义一句话：**提取时排除，打标与翻译时包含。**

- 排除（Extract 的两处）：不参与目录竞选、不进 orphan 报告。
- 包含（其余全部）：Addressables 打标菜单照常给它们打 `Locale:<locale>`，翻译窗口/CI 照常填 msgstr，
  运行时照常加载。

外部根下的布局要求只有一条：`.po` 必须待在名为 `<locale>` 的目录下（`<root>/<locale>/xxx.po`），
这样 `AddressablePoLabelSyncer.ComputeDesiredLocale` 才能给它算出 label。这与工程内的布局同规，
不引入第二套约定。

## 3. 改动点

### 3.1 `PromptUGUISettings`

加 `externalPoRoots` 字段 + 一个纯函数（放 `AddressablePoLabelSyncer`，便于单测，与既有纯函数同居）：

```csharp
/// <summary>True if <paramref name="assetPath"/> sits under any of <paramref name="roots"/>.
/// Paths are compared with '/' separators, Ordinal, on folder boundaries — so
/// "Assets/i18n_server_old/x.po" is NOT under root "Assets/i18n_server".</summary>
public static bool IsUnderAnyRoot(string assetPath, IEnumerable<string> roots)
```

边界必须按 `/` 对齐（`root` 或 `root + "/"` 前缀），否则 `i18n_server` 会误吞 `i18n_server_old`。
空 / null 根忽略；反斜杠统一成 `/`。

配套第二个纯函数，给「提取排除」两处共用，同时把过滤逻辑从 `#if PROMPTUGUI_HAS_ADDRESSABLES`
里拽到无条件编译区（否则 `PromptUGUI.Tests.EditorOnly` 那个 asmdef 测不到它，见 §5）：

```csharp
/// <summary>The subset of <paramref name="assetPaths"/> that is NOT under any of
/// <paramref name="roots"/>. Null/empty roots ⇒ everything passes through.</summary>
public static IEnumerable<string> ExcludeExternalRoots(
    IEnumerable<string> assetPaths, IEnumerable<string> roots)
```

**Inspector 必须一并改。** `PromptUGUISettingsEditor` 是自定义 Inspector，只画 `fontTypes` +
`locales` 两个属性——不加 `PropertyField(serializedObject.FindProperty("externalPoRoots"))`
的话新字段在 Inspector 里根本不出现，作者只能改 YAML。放在 `fontTypes` 之后、`Locales` 之前。

### 3.2 `StringExtractor.ExtractAll`

两处过滤，都拿 `settings.externalPoRoots`：

1. `CollectAddressablePoPathsByLocale()` —— 收集带 label 的 .po 时跳过外部根下的路径。
   → 外部 .po 不参与目录竞选，"Multiple '<locale>' folders" 警告不再因外部根而出现。
   过滤走 `ExcludeExternalRoots`，`#if` 块内只负责调用。
2. `FindOrphanPoFiles(poFilePaths, localeDir, activePartitions, externalRoots)` —— 加一个
   参数，逐文件跳过外部根下的路径。**过滤放进这个既有纯函数**（而不是 `ReportOrphanPoFiles`
   这个读盘的调用方），否则 §5 的 orphan 测试打不到。
   → 即便作者把外部根设在 localeDir **内部**（`Assets/_Project/i18n/en/_server/`），也不刷红；
   `ReportOrphanPoFiles` 的 `Directory.GetFiles(..., AllDirectories)` 是递归的，这条路径真会发生。

其余一律不动：写出路径、partition 分组、`PoFileWriter.Merge` 全部保持原状。

顺带把 `CollectAddressablePoPathsByLocale` 从 `private` 提成 `internal`（§3.4 要复用），
并把 `StringExtractor.DefaultOutputRoot` 与 `TranslateLocaleWindow.I18nRoot` 这两份同值字面量
合成一处。

### 3.3 `AddressablePoMenu` / 运行时

**不改。** 打标菜单扫全工程 .po、按父目录名判 locale，外部根天然被包含；运行时只认 label。
这正是「排除只发生在提取」的体现。

### 3.4 `TranslateLocaleWindow`（顺带修，独立提交）

`TranslateLocaleWindow.I18nRoot` 目前硬编码 `"Assets/Resources/PromptUGUI/i18n"`——工程一旦把 .po
移到别处（ssw 就在 `Assets/_Project/i18n`），这个窗口就找不到任何文件、报 "Run Extract Strings first"。
这个窗口对 Addressables **一无所知**：不看 label、不读 `PromptUGUISettings`（除了取 locale 列表）、
也没有 fallback。

改成与 Extract 同一套解析：

1. `localeDir` = `ResolveOutputDirForLocale(locale, ExcludeExternalRoots(labelled), DefaultOutputRoot)`
   —— 与 Extract 逐字同源，所以「窗口找的地方」永远等于「Extract 写的地方」。
2. 再并上每个 `externalPoRoots/<locale>` 下的 `.po`。
3. 两处结果按归一化路径去重后一起扫。

副作用（可接受、且是 §2 语义的直接后果）：外部根下的服务器串会进 AI 翻译队列。ssw 用 CI 翻译，
不受影响；窗口里会多出条目。

另：`Path.Combine` 在 Windows 产出反斜杠，而 `FindLocaleFolder` / `FindOrphanPoFiles` 都按 `/`
归一化——这里统一改成 `/` 拼接。

这条与 EPR 无强依赖（ssw 用 CI 翻译），但既然动这块代码，顺手修掉；**单独一个提交**，便于回退。

## 4. 不做什么

- **不做「外部 msgid 参与 partition 分组」**：外部工具自己决定文件名与切分，PromptUGUI 不重排它们。
- **不做外部 .po 的格式校验**：解析失败已经由运行时 `PoParser` 报错（`LoadPoFromAddressablesAsync`
  里逐文件 try/catch 并 LogError），Editor 侧再来一遍没有增量价值。
- **不做「反向保护」**：外部工具写进客户端 partition 文件仍然会被 Merge 清掉——那是外部工具的错，
  文档里写清楚即可（EPR 的存在就是给它一个正确的落点）。
- **不动运行时**：`TranslationStore` / resolver / label 契约零改动。

## 5. 测试

`Tests/EditMode/Editor/`（asmdef `PromptUGUI.Tests.EditorOnly`，**没有** `PROMPTUGUI_HAS_ADDRESSABLES`
约束 —— 所以被测的必须是无条件编译的纯函数）：

- `IsUnderAnyRoot`：命中、未命中、目录边界（`i18n_server` vs `i18n_server_old`）、反斜杠、空根列表、
  根自身带尾斜杠、`null` 路径。
- `ExcludeExternalRoots`：空根列表原样放行、命中的被剔除、保序。
- `FindOrphanPoFiles` + 外部根：外部根下的 .po 不出现在 orphan 结果里；根设在 localeDir **内部**
  （`<localeDir>/_server/`）时同样。
- 目录竞选回归防线：`ExcludeExternalRoots` 过滤后喂给 `ResolveOutputDirForLocale`，即便外部根的
  目录名 Ordinal 排在工程自己那个前面（`.../i18n-server/en` < `.../i18n/en`，因为 `-` < `/`），
  输出目录仍是工程自己的那个，且 `detected.Count == 1` → 不 warning。

`Tests/EditMode/Addressables/`：打标集成测试补一条——外部根下的 .po 仍被 `MakeLocalePoFilesAddressable`
打上 `Locale:<locale>`（"排除只发生在提取" 的正面断言）。

## 6. 兼容性

`externalPoRoots` 默认空列表 = 现有行为逐字不变，无迁移动作。

## 7. 决策

- `EPR-D1`：外部 .po 用**设置里的根目录清单**标识，不用文件名约定、不用 label 区分。理由：根目录是
  作者的一次性决策，且与「谁写这些文件」一一对应；label 区分会牵动运行时契约（多一个 label 就要多
  一次 `LoadAssetsAsync`）。
- `EPR-D2`：语义固定为**提取排除、打标/翻译包含**。外部 .po 与工程内 .po 在打包与运行时**完全等价**，
  唯一区别是「谁生成它们」。
- `EPR-D3`：外部根仍要求 `<root>/<locale>/*.po` 布局，复用既有 locale 目录约定，不引入第二套。
- `EPR-D4`：`TranslateLocaleWindow` 的硬编码根一并修掉，但独立提交。
- `EPR-D5`：过滤逻辑落在 `AddressablePoLabelSyncer` 的**无条件编译纯函数**里
  （`IsUnderAnyRoot` / `ExcludeExternalRoots`），`#if PROMPTUGUI_HAS_ADDRESSABLES` 块内只调用不判断。
  理由：与既有纯函数同居、可被无 Addressables 的测试 asmdef 覆盖；`FindOrphanPoFiles` 同理靠加参数
  而非在读盘的调用方过滤。
- `EPR-D6`：`externalPoRoots` 虽是 Editor 期语义，仍住在运行时的 `PromptUGUISettings`。理由：它已是
  locale 配置的唯一落点（`locales[]` 就在那儿），另起一份 Editor-only 资产会让作者在两个地方配 i18n。
  代价是 Player 里多一个不用的 `List<string>`，可忽略。
