# XML 注释作为译者注进 `.po` Implementation Plan

> **For agentic workers:** Steps use checkbox (`- [x]`) syntax for tracking. Red first: every task writes its tests, refreshes
> Unity and sees them fail for the right reason before implementing.

## Context

`UI.Tr()` 上方的 `//` 注释会进 `.po` 的 `#.`（i18n spec §8.3），XML 侧从无对应规则（§8.2）：`XmlDocument` 保留了
`XmlComment`，但 `UIDocumentParser` 只认元素 / 文本，注释在 IR 里无处可放。ssw_re_client `Round.ui.xml` 旗帜页签调用点上方
写给译者的说明因此进不了 `DESIGN` 等条目，`2. AI Translate Locale...` 也看不到（每条都把 `.po` 注释作为 `comments` 发给模型）。

设计见 spec `docs~/superpowers/specs/2026-09-30-i18n-xml-comments-design.md`（下文 §x 均指它，§9 全按推荐已定）。分支
`feat/i18n-xml-comments`。计划阶段经一轮对照代码的独立评审，修正已写回 spec（写时复制、`DeepClone` 复制
`InvocationComments`、`HasComments` 快路径、同一行多条注释、多行夹具、测试 32 / 40 的判别力）。

**Goal:** 文字元素、模板调用点、模板内部文字位置正上方的 XML 注释作为 `#.` 进入对应 msgid；`PoFileWriter` 去重。
验收：ssw `Round` 分区的 `DESIGN` 带 FlagTab 调用点注释，`Planet.ui.xml:383` 的行尾注释不挂到任何节点。

**Architecture:** 解析器把"前导注释"记到 `ElementNode.LeadingComments`；展开器随节点复制它，并把调用点的注释写时复制地
追加到实例根的 `InvocationComments`；扫描器沿展开树带一个不可变的调用点链（`CallSiteScope`），只给"值来自实参"的 msgid
附上链上注释。运行时不读这两个字段，且运行时 TextAsset 已去注释（2026-09-27 spec），零开销。

**Tech Stack:** C# 9、`System.Xml`（`XmlDocument` / `XmlReader`），Core 目录保持纯 C#（UIXmlLint CLI 编译集）。

## 已对齐的决策

spec §9 的 7 条全按推荐。计划阶段补充（均已写回 spec）：

| # | 决策 | 为什么 |
|---|---|---|
| P1 | 两字段 `IReadOnlyList<string>`，只整体替换；`AppendOuter` 每次新建列表 | 解析产物经 `ImportClosure.Cache` / commons 池共享，原地追加会越展开越长 |
| P2 | 文档级 `HasComments` 为假时不走 `PreviousSibling` | `XmlLinkedNode.PreviousSibling` 是 O(k) |
| P3 | 行首判定先剥掉前缀里完整的 `<!--…-->` | 同一行 `<!-- a --> <!-- b -->` 两条都该算 |
| P4 | 测试夹具一律多行 | 单行夹具里注释永远不算行首 |
| P5 | "值来自实参"沿用 `PickMsgid` 的判定，以 `out bool fromArgs` 带出 | 不另写一套判定，避免两处漂移 |

## Task 0：文档先行

- [x] spec 按评审修订、§9 改「已定」、§10 记入两处范围外缺陷（Variant Add 不提取；Slot 内容不代入外层参数）
- [x] 本计划落盘

## Task 1：IR 空字段（先让测试程序集能编译）

**Files:** `Runtime/Core/IR/ElementNode.cs`

- [x] 加 `IReadOnlyList<string> LeadingComments { get; set; }`、`IReadOnlyList<string> InvocationComments { get; set; }`，
  XML 注释写明语义（前者：解析器填、随复制带；后者：只在模板实例根上、由内到外；都只整体替换）
- [x] 刷新 Unity、console 无错

## Task 2：解析器（spec §5-1 ~ 16）

**Files:** 新 `Tests/EditMode/Parser/LeadingCommentParseTests.cs`；`Runtime/Core/Parser/LineInfoXmlDocument.cs`、`UIDocumentParser.cs`

- [x] 写 §5-1 ~ 16（多行夹具），刷新看红
- [x] `LineInfoComment : XmlComment { Line, Column, StartsLine }`；`LineInfoXmlDocument.Parse` 在 `Load` 期间持有源文本；
  `override CreateComment(string data)` 读 reader 位置、置 `HasComments`、就地算 `StartsLine`（首个注释时懒建行首表，
  换行 `\r\n` / `\r` / `\n`；`<!--` 预期在 `Column-5`（0 基），不符则 `LastIndexOf`；前缀剥掉完整注释后全空白）
- [x] ~~兜底预案~~ 未启用：§5-16 直测通过（仅当 §5-16 显示 `CreateComment` 时 reader 不在注释上）：`XmlReader` 预扫记录注释位置，按文档顺序对齐
- [x] `UIDocumentParser.CollectLeadingComments(XmlElement)`：`!HasComments` → null；沿 `PreviousSibling` 往前，跳过
  `XmlWhitespace` / `XmlSignificantWhitespace`，收 `LineInfoComment`，遇非注释或 `!StartsLine` 停；反转、按行拆 / trim / 丢空；
  空则 null。在 `ParseElement` 建节点处赋值
- [x] 看绿；`Tests/EditMode/Parser` 全组回归

## Task 3：展开（spec §5-17 ~ 24）

**Files:** 新 `Tests/EditMode/Template/TemplateCommentPropagationTests.cs`；`Runtime/Core/Template/TemplateExpander.cs`、`StyleMerger.cs`

- [x] 写 §5-17 ~ 24，刷新看红
- [x] `TemplateExpander`：Screen 根 / `ExpandTree` / `ExpandNode` / `SubstituteAttrs` / `DeepClone` 复制两字段；
  `ExpandInvocation` 建好实例根后 `instanceRoot.InvocationComments = AppendOuter(instanceRoot.InvocationComments, invocation.LeadingComments)`
- [x] `StyleMerger.CloneForMerge` 复制两字段
- [x] 看绿；`Tests/EditMode/Template` 全组回归

## Task 4：提取（spec §5-25 ~ 37）

**Files:** `Tests/EditMode/Editor/XmlStringScannerTests.cs`（追加）；`Editor/I18n/XmlStringScanner.cs`

- [x] 写 §5-25 ~ 37（多行夹具），刷新看红
- [x] `CallSiteScope { Comments; Outer }`（不可变链表）；`WalkNode(…, CallSiteScope enclosing)` 进入节点先算 scope 再收文字；
  `PickMsgid` / `PickTextAttrMsgid` 加 `out bool fromArgs`；`Build` 追加 ①（节点 `LeadingComments`）与仅 `fromArgs` 时的
  ②（scope 由内到外），顺序 ambient → sibling → ① → ② → TMP 提示；模板体原样遍历传空 scope
- [x] 看绿

## Task 5：合并（spec §5-38）

**Files:** `Tests/EditMode/Editor/PoFileWriterTests.cs`（追加）；`Editor/I18n/PoFileWriter.cs`

- [x] 写 §5-38，看红
- [x] `Merge`：`Comments` / `ExtractedComments` / `References` 按首次出现顺序去重（含同一次出现内部）
- [x] 看绿

## Task 6：端到端（spec §5-39）

**Files:** `Tests/EditMode/Editor/StringExtractorTests.cs`（追加，`FakeFs` 内容写多行）

- [x] 写 §5-39，看绿（前面各层已实现，此条应直接绿；红则说明组装链上还有缺口）

## Task 7：文档与收尾

- [x] `.claude/skills/authoring-promptugui-xml/SKILL.md` 的 `## i18n & Fonts (XML markup)` 加 **Translator notes**（英文）
- [x] `docs~/superpowers/specs/2026-05-08-i18n-fonts-design.md` §8.2 末尾指向本 spec
- [x] 全量：`PromptUGUI.Tests.EditMode`、`PromptUGUI.Tests.EditorOnly`（核对 `summary.total`）
- [x] `.lint`：改动文件 `dotnet format --verify-no-changes --severity warn`；`dotnet build .lint/UIXmlLint`；
  `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` 与 ssw `Assets/_Project` 的输出与改动前基线一致
- [x] ssw 手工（spec §5-40）：反射调 `StringExtractor.ScanAllXml`（不写 `.po`）+ 解析 `Planet.ui.xml` 断言
- [x] spec 末尾写「实施记录」；汇报，提交与 PR 等作者确认
