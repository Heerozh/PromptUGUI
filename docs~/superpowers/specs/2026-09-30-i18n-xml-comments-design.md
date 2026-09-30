# XML 注释作为译者注进 `.po`

> 状态：**已定**（§9 全按推荐），实施中。计划：`docs~/superpowers/plans/2026-09-30-i18n-xml-comments.md`。
> 分支 `feat/i18n-xml-comments`。
> 相关：`2026-05-08-i18n-fonts-design.md` §8.2（XML 扫描——本 spec 修订它）/ §8.3（C# 侧的同类规则）；
> `2026-09-27-ui-xml-comment-stripping-design.md`（运行时的 TextAsset 已无注释）；PR #160（提取器按运行时组装展开 Screen）。

## 1. 问题

`UI.Tr("…")` 正上方的 `//` 注释会进 `.po` 的 `#.`（i18n spec §8.3，`CSharpStringScanner.CollectLeadingComments`），
XML 侧没有对应规则：§8.2 只列了 ambient（`Round screen, Text text`）、`sibling:`、TMP 富文本提示三种自动注释。
解析器用的 `XmlDocument` 其实保留了 `XmlComment` 节点，但 `UIDocumentParser` 只认 `XmlElement` / `XmlText` /
`XmlCDataSection`，注释在 IR 里无处可放。

实例：ssw_re_client `Round.ui.xml` 的旗帜页签，调用点上方写的就是给译者的话——

```xml
<!-- label是按钮位置，caption是装饰文字，CJK语言版本用英语装饰，其他语言用中文装饰 -->
<FlagTab id="flagDesign" label="战舰设计" caption="DESIGN" icon="FlagBtn:Design" … />
```

`DESIGN` 在 en 里要译成中文装饰字，这个意图只存在于注释里；而提取出的条目只有：

```
#. Round screen, Text text
#. sibling: 战舰设计
#: Assets/_Project/Round/UI/Round.ui.xml
msgid "DESIGN"
```

`2. AI Translate Locale...` 会把每条的 `.po` 注释作为 `comments` 发给模型（`TranslationClient.cs:78`）——注释进了
`.po` 就进了 prompt，没进就无从得知。

附带问题：同一 msgid 在一个分区出现多次时，`PoFileWriter.Merge` 把各次的注释 / 引用直接拼接，不去重。
ssw 的 `Round.po` 里 `世界` / `军团` / `势力` / `系统` 的 `#. Round screen, Text text` 与 `#:` 各重复两遍；
注释提取上线后，同一调用点注释也会跟着重复。

**目标**：写在文字元素正上方、模板调用点正上方、模板内部文字位置正上方的 XML 注释，作为 `#.` 译者注进入对应
msgid；同一 msgid 的注释与引用去重。

## 2. 现状调研（ssw_re_client，2026-09-30）

对全部 `.ui.xml` 统计（`Text` / `Btn` / `Toggle` / `Tab` 与模板调用点）：

| 项 | 数 |
|---|---|
| 候选元素（文字元素 237 + 模板调用点 91） | 328 |
| 其中紧贴上方有注释的 | 34（文字元素 26、调用点 8；其中 14 个在模板体内） |
| 注释与元素之间隔空行的 | **0** |
| 注释其实是上一行的**行尾注释**的 | **1**：`Planet.ui.xml:383` `<Frame width="stretch" />  <!-- spacer：吃掉中间剩余空间 -->`，下一行是 `<Btn id="applyJobs">` |

- 34 条里明确写给译者的是 6 条（FlagTab ×5、FlagBtn ×1）。其余是开发说明，但多数在说"这是什么"——
  `右上关闭（返回）图标按钮`、`提示文字：居中、自动换行、随内容长高`、`定宽 + 不折行：…96 = 最长的 "UTC+5:30 09-14 08:35:22"`
  ——对译者同样有用（后者就是长度约束）；纯实现向的如 `关闭：接 UI.Router.Back()（不是 ClearSelection …）` 多挂在
  只有图标、不产生 msgid 的 `<Btn>` 上，自然落空。
- 行尾注释那一例说明：只按"前一个兄弟节点是注释"判定会把 `spacer：…` 错挂到"应用"按钮上。§3.1-2 排除它。

## 3. 语义

### 3.1 前导注释（解析器）

XML 注释 C 是元素 E 的**前导注释**，当且仅当：

1. **紧邻**：C 与 E 是兄弟，二者之间只有其他注释（空白不计），没有元素、没有非空白文本 / CDATA。
2. **独占一行开头**：C 所在行 `<!--` 之前只有空白——前缀里完整的 `<!--…-->` 先剥掉再判，所以同一行的
   `<!-- a --> <!-- b -->` 两条都算。`<Frame/> <!-- … -->` 这种行尾注释属于前面的标记，不算；从 E 往前收集时
   遇到它即停止，它之前的注释也不再收。`<Frame> <!-- c -->` 换行后的首个子元素同理拿不到 `c`。
   （仍不算的边角：上一条多行注释的 `-->` 与 C 同处一行时，前缀里只有半条注释，C 按行尾注释处理。）
3. **不看空行**：注释块与 E 之间、块内各注释之间有空行也照收——与 C# 侧规则一致（C# 只以语句边界 `;` `{` `}`
   截断），调研中也没有空行的实例。

规范化：注释文本按行拆开，每行 `Trim()`，丢弃空行；剩下的每行是一条，块内按源码顺序。

存放：`ElementNode.LeadingComments`（`IReadOnlyList<string>`，无则 null；只整体替换、从不原地修改）。
`ParseElement` 构造的**每个**元素都填，不限标签——
Screen 子节点、模板体（含根）、`<Add>` 子节点、嵌套子节点走同一个入口。不产生 ElementNode 的标签（`<Screen>`、
`<Template>`、`<Param>`、`<Style>`、`<Variant>`、`<Add>`、`<Import>`）上方的注释不收；元素**内部**的注释
（`<Text>a<!-- x -->b</Text>`）也不收，`TextContent` 本就经 `InnerText` 跳过注释，行为不变。

行列号：`LineInfoXmlDocument` 已经靠重写 `CreateElement` 从 reader 读位置；`CreateComment` 用同一手法，产出
`LineInfoComment : XmlComment { Line, Column, StartsLine }`，`StartsLine`（§3.1-2）在创建时就地算好。Unity Mono 实测：
注释节点的 `LinePosition` 指向 `<!--` 之后的第一个字符，即 `<!--` 起于 `Column - 4`（1 基）；该处不是 `<!--` 时就近
`LastIndexOf`。行首偏移表按 XML 的换行（`\r\n` / `\r` / `\n`）在遇到第一个注释时才建。

收集：从 E 沿 `PreviousSibling` 往前，跳过空白节点（`xml:space="preserve"` 下的 `XmlWhitespace` /
`XmlSignificantWhitespace`），收 `LineInfoComment` 直到非注释节点或 `!StartsLine` 的注释（不含），再反转。
`XmlLinkedNode.PreviousSibling` 是 O(k)（从父节点 `FirstChild` 往后走），故文档级 `HasComments` 为假时直接跳过——
无注释的源码（运行时去注释后的 TextAsset）零开销。

**始终收集，不设开关**：`ImportClosure` / `DocumentLoader` 自己解析被 Import 的文件，开关一旦没传到，模板文件里的注释
（§3.3 ① 的来源）就静默丢失。运行时代价为零：`UiXmlImporter` 产出的 TextAsset 已去注释（2026-09-27 spec），只有
`UI.LoadDocument(label, xmlString)` 这种直接喂原文的调用会多几个字符串。

### 3.2 展开（`TemplateExpander`）

- 节点被复制的每一处都带上 `LeadingComments` 与 `InvocationComments`（引用拷贝）：`SubstituteAttrs`、
  `StyleMerger.CloneForMerge`、`ExpandTree`、`ExpandNode`、`DeepClone`，以及 `Expand` 里的 Screen 根（恒无注释，
  只为统一）。漏任何一处都是**静默**丢注释：`DeepClone` 漏 `InvocationComments`，本身是模板实例的 Slot 内容就只剩
  外层调用点的注释。
- `ExpandInvocation`：把调用点的 `LeadingComments` 追加到实例根的 **`InvocationComments`**（新字段，
  `IReadOnlyList<string>`）。**写时复制**：每次新建列表，绝不对已有列表 `AddRange`——解析产物经
  `ImportClosure.Cache`（一次提取内跨入口）与 commons 池（运行时）共享，原地追加会让列表越展开越长。模板体的根本身
  又是一次调用时，内层先展开先追加、外层后追加到同一节点，所以顺序是**由内到外**。实例根自己的 `LeadingComments`
  仍是模板文件里写在根上方的注释，两者不混。
- 运行时（`ScreenInstantiator` 等）不读这两个字段。

### 3.3 提取（`XmlStringScanner`）

| msgid 从哪来 | 附带的作者注释 |
|---|---|
| Screen 里直写的文字（含传进 `<Slot>` 的内容） | 该元素自己的前导注释 |
| 模板体里的静态文字 / 格式串（模板体原样遍历，分区 = 模板文件） | 该元素在模板文件里的前导注释 |
| **模板实参**（纯 `{{x}}` 槽位被调用点的值替换，分区 = 调用方 Screen） | ① 槽位元素在模板文件里的前导注释（"模板内部文字的实际地点"）<br>② 展开树中包住它的每个模板调用点的前导注释，由内到外 |

- 调用点注释**只**挂到实参来的 msgid：模板的静态文字对所有实例是同一条 msgid，只从模板体提取一次，调用点描述的是
  "这一次传了什么"；Slot 里的静态文字写在调用点那边，有自己的前导注释。
- 容器上方的注释不下传：`<Frame>` / `<TabBar>` 上方的注释到不了其中的 `<Text>`（调研里这类几乎都是布局说明）。
- 条目内顺序：`X screen, Tag slot` → `sibling: …` → 作者注释（①，再 ② 由内到外）→ TMP 富文本提示。
- 实现要点：`WalkNode` 自上而下带一个不可变链表 `CallSiteScope { Comments, Outer }`——进入带 `InvocationComments`
  的节点时 `scope = new(ic, enclosing)`，该节点的文字与子树都用它，无需出栈，对惰性迭代器安全；从链表头走到尾即由内到外。
  **先算 scope 再收该节点自己的文字**（模板的根可能就是宿主文字的 `<Btn>{{label}}</Btn>`）。
- "值来自实参"沿用现有判定：原文是纯 `{{x}}` 且 `TextArgs` 非空（`PickMsgid` / `PickTextAttrMsgid` 以 `out` 带出）。
  于是 Param 默认值也算实参、带 ②；零 Param 的模板不设 `TextArgs`，其静态文字在 Screen 遍里按"直写"提取，带 ① 不带 ②。
- 边角：若实参来的文字本身位于另一个模板的 Slot 里，外层那个"被塞进去的"调用点注释也在栈上、会一并附上。按"包住它的
  调用点"的定义这是一致的，且只多出上下文，不做特判。

旗帜页签的结果（假设再在 `FlagTab.ui.xml` 的 caption `<Text>` 上方写一句 `<!-- 装饰小字 -->`）：

```
#. Round screen, Text text
#. sibling: 战舰设计
#. 装饰小字
#. label是按钮位置，caption是装饰文字，CJK语言版本用英语装饰，其他语言用中文装饰
#: Assets/_Project/Round/UI/Round.ui.xml
msgid "DESIGN"
```

### 3.4 合并（`PoFileWriter.Merge`）

同一 `(msgctxt, msgid)` 的多次出现合并时，`Comments`、`ExtractedComments`、`References` 各自**按首次出现顺序去重**
（同一次出现内部的重复也去掉）。注释照旧每次提取整体刷新（现有行为，`Merge_NewExtractionRefreshesComments`），
msgstr 不受影响。去重按行：两段不同的多行注释若有一行相同，那一行只留一份。

## 4. 迁移与影响

- 下次 Extract Strings：带注释的 msgid 多出 `#.` 行；重复的 `#.` / `#:` 行消失。msgstr 一律保留。
- 开发说明也会进 `.po` 和 AI prompt，token 略增。不提供过滤（§7）；真成问题时再加标记。
- UIXmlLint CLI：改动在 `Core/Parser` / `Core/IR` / `Core/Template`，只用 `System.Xml`，仍是纯 C#；lint 输出不变。
- 运行时：零（见 §3.1 末）。

## 5. 测试（Red first）

**夹具一律多行**（显式 `\n`）：现有 `Doc()` / `FakeFs.Add` 把内容拼成一行，注释前面永远是 `<Screen…>`，按 §3.1-2
永远不算行首——单行夹具会让注释用例假红，也会让"行尾注释不挂"假绿。

解析器（`Tests/EditMode/Parser/LeadingCommentParseTests.cs`，新）：

1. 元素正上方一条注释 → `LeadingComments == ["…"]`
2. 多行注释 → 每行一条、已 trim、空行丢弃
3. 连续两条注释 → 两条都收，源码顺序
4. 注释与元素之间隔一个兄弟元素 → 只归紧挨它的那个元素
5. `<A/> <!-- t -->` 换行 `<B/>` → B 无注释
6. `<A/> <!-- t -->` 换行 `<!-- o -->` 换行 `<B/>` → B 只有 `o`
7. 注释与元素间有空行 → 照收
8. 模板里 `<Param>` 之后、根元素之上的注释 → 归根元素；`<Param>` 上方的注释不归任何节点
9. `<Variant><Add>` 的子元素 → 照收
10. 无注释 → `null`
11. `<Text>a<!-- x -->b</Text>` → `TextContent == "ab"`，不产生前导注释（回归）
12. CRLF 源码、单独 `\r` 换行 → 与 LF 相同
13. 同一行 `<!-- a --> <!-- b -->` 换行 `<B/>` → B 两条都有
14. `<Frame> <!-- c -->` 换行 `<Text>x</Text>` → Text 无注释
15. 空注释 `<!-- -->` → 不产生条目（`null`）
16. `LineInfoComment` 直测：`Line` / `Column` / `StartsLine`

展开（`Tests/EditMode/Template/TemplateCommentPropagationTests.cs`，新）：

17. Screen 里普通节点的注释穿过展开；带 `class=` 的节点也是（`CloneForMerge`）
18. 模板体节点的注释到达展开后的节点
19. 调用点注释进实例根的 `InvocationComments`；实例根的 `LeadingComments` 是模板里根元素上方的注释
20. 模板体的根又是一次调用 → `InvocationComments` 内层在前、外层在后
21. Slot 内容保留自己的注释；Slot 内容本身是模板实例时 `InvocationComments` 也保留（`DeepClone`）
22. 调用点带 `class=` → 注释照样进实例根
23. 模板体内非根位置的嵌套调用 → 内层实例根拿到内层调用点注释
24. 同一模板被两个文档各展开一次 → 共享模板体上的列表不变、实例根各自的列表不累积

提取（`XmlStringScannerTests`，追加）：

25. 普通 `<Text>` 的注释进 `ExtractedComments`
26. FlagTab 形：调用点注释同时挂到 label 与 caption 两条
27. 被 Import 的模板里、槽位 `<Text>` 上方的注释挂到实参 msgid；与调用点注释同在时 ① 在前 ② 在后
28. 模板静态文字：带自己的注释，不带调用点注释
29. Slot 里的静态文字：不带调用点注释
30. 嵌套调用链：内外两层调用点注释都在，内层在前
31. 容器上方的注释不下传
32. 行尾注释不挂到下一行元素：`<Frame/> <!-- spacer -->` 换行 `<Btn>应用</Btn>`（下一行必须自带文字，否则区分不出）
33. 模板根就是文字宿主（`<Btn>{{label}}</Btn>`）→ 带 ②
34. `text="{{label}}"` 形 → 同 `{{label}}` 正文
35. 零 Param 的模板 → 静态文字带 ① 不带 ②
36. 条目内完整顺序：ambient → sibling → ① → ②（内→外）→ TMP 提示
37. 展开失败回退原始树时，直写文字仍带 ①

合并（`PoFileWriterTests`，追加）：

38. 同一 msgid 两次出现、注释与引用相同 → 各只一份，顺序为首次出现顺序；同一次出现内的重复也去掉

端到端（`StringExtractorTests`，追加）：

39. 模板文件里的槽位注释 + Screen 文件里的调用点注释 → 同一条目上两者都在，分区为 Screen

手工（ssw_re_client，反射调 `ScanAllXml`，不写 `.po`）：

40. Round 分区的 `DESIGN` / `战舰设计` 带 FlagTab 调用点注释；解析 `Planet.ui.xml`，断言 `Btn#applyJobs` 的
    `LeadingComments` 为 null（它自己没有文字，只看 `.po` 区分不出对错）；无新增警告

## 6. 文档（同一 PR）

- `authoring-promptugui-xml/SKILL.md` 的 `## i18n & Fonts (XML markup)`：加 **Translator notes** 一段（英文）——
  哪些注释会进 `.po`、模板调用点与模板内部两处的规则、行尾注释不算。
- `2026-05-08-i18n-fonts-design.md` §8.2 末尾加一行指向本 spec。

## 7. 非目标

- **注释过滤 / 标记**（如只收 `<!-- tr: … -->`，或给开发说明加 opt-out 前缀）：用户意图是"注释就该给译者看"，C# 侧也不过滤；
  调研显示开发说明多半也在描述元素用途。真成问题时另加标记。
- **`<Param>` 上方的注释当槽位说明**：写在模板里槽位 `<Text>` 上方（§3.3 ①）已覆盖；一个参数流进多个文字元素时才更省事，
  暂无实例。
- **行尾注释归属前一个元素**：本 spec 只保证它不被错挂。
- **`#:` 引用带行号**：会让现有全部 `.po` 的引用行变动，另案。
- **`<Variant><Add>` 里的文字提取**：见 §10，另案。

## 8. 实现地图

| 文件 | 内容 |
|---|---|
| `Runtime/Core/Parser/LineInfoXmlDocument.cs` | `LineInfoComment` + `CreateComment` 重写 + 行首判定（§3.1） |
| `Runtime/Core/Parser/UIDocumentParser.cs` | `ParseElement` 收前导注释（§3.1） |
| `Runtime/Core/IR/ElementNode.cs` | `LeadingComments`、`InvocationComments` |
| `Runtime/Core/Template/TemplateExpander.cs` | 五处复制 + `ExpandInvocation` 追加（§3.2） |
| `Runtime/Core/Template/StyleMerger.cs` | `CloneForMerge` 复制 |
| `Editor/I18n/XmlStringScanner.cs` | 挂注释（§3.3） |
| `Editor/I18n/PoFileWriter.cs` | 去重（§3.4） |
| 测试 | §5 |
| skill、i18n spec | §6 |

顺序：IR 空字段（先让测试能编译）→ 解析器（§5-1 ~ 16）→ 展开（17 ~ 24）→ 提取（25 ~ 37）→ 合并（38）→
端到端（39）→ 文档 → ssw 手工验证（40）→ PR。

## 9. 决策（已定，均按推荐）

| # | 项 | 推荐 | 备选 |
|---|---|---|---|
| 1 | 收哪些注释 | 所有前导注释（与 C# 侧一致） | 只收带标记的 |
| 2 | 空行 | 不断开（与 C# 一致；调研 0 例） | 空行即断开 |
| 3 | 行尾注释 | 排除，不挂任何元素 | 归属前一个元素 |
| 4 | 调用点注释挂到哪 | 实参来的 msgid，包住它的所有调用点 | 实例内所有 msgid（含模板静态文字） |
| 5 | 条目内顺序 | 文字位置（①）在前，调用点（②）由内到外 | 调用点在前 |
| 6 | `PoFileWriter` 去重 | 本 spec 一并做 | 另案 |
| 7 | 解析器是否总收集 | 总收集（§3.1 末） | 加开关，仅提取器开启 |

## 10. 范围外发现

1. **提取器**：`XmlStringScanner` 只遍历 `screen.Root`，**从不遍历 `screen.Variants` 里 `<Add>` 块的子树**，写在
   `<Variant><Add>` 里的文字从未被提取。ssw_re_client 与本包 `Runtime/Resources` 目前都没有 `<Variant>` 块，暂无实际影响。
2. **展开器（运行时缺陷）**：模板体里传给另一个模板 `<Slot>` 的内容**不代入外层模板的参数**。

   ```xml
   <Template name='Panel'><Frame><Slot/></Frame></Template>
   <Template name='Card'>
     <Param name='y'/>
     <Panel><Text>{{y}}</Text></Panel>
   </Template>
   <Screen name='S'><Card y='你好'/></Screen>
   ```

   Unity 实测展开结果：`Frame > Text`，`TextContent == "{{y}}"`、`TextArgs == null`——界面上显示字面 `{{y}}`，提取器也
   收不到。原因：`ExpandNode` 遇到模板调用时经 `SubstituteAttrs`（子节点按引用原样带过）转 `ExpandTree` →
   `ExpandInvocation`，后者对调用的子节点（Slot 内容）走 `ExpandTree`，不带外层 args。ssw_re_client 0 处使用此写法。

两者均建议另开小修。

## 11. 实施记录（2026-09-30，分支 `feat/i18n-xml-comments`）

- 按计划 Task 0 ~ 7 完成。Red → Green：解析器 19 条（13 红；另 6 条是"不应挂注释"的防护用例，本就绿）；展开 10 条全红；
  提取 13 条（11 红，2 条防护用例本就绿）；合并 1 条红；端到端 1 条直接绿（前几层已实现，符合预期）。
- `CreateComment` 重写在 Unity Mono 下确实由 `XmlDocument.Load` 在 reader 停在注释上时调用（§5-16 直测行列号通过），
  未用兜底预扫。
- 回归：`PromptUGUI.Tests.EditMode` 4666/4666、`PromptUGUI.Tests.EditorOnly` 453/453；改动文件 `dotnet format` 干净；
  UIXmlLint CLI 编译 0 警告；对本包 `Runtime/Resources/` 与 ssw `Assets/_Project` 的 lint 输出与改动前逐字一致。
- ssw 实测（反射调 `ScanAllXml`，未写 `.po`）：117 条中 21 条带作者注释。旗帜页签 12 条（label / caption × 6，含 FlagBtn 的
  `母星` / `HOME`）；其余 9 条是开发说明：PanelShell 模板标题槽位上方的「标题：左对齐……右边给关闭钮让出 30」经实参到达
  5 个面板标题（§3.3 ①），BuildSlot 底部钮说明 × 2，FactionSelect × 2。`Planet.ui.xml` 的 `Btn#applyJobs`（384 行）无前导
  注释，上一行的行尾注释 `spacer：…` 未被错挂；无新增警告。
