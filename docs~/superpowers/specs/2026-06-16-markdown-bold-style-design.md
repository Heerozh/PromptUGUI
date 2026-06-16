# Markdown `boldStyle`:粗体渲染样式（关键字 + 颜色，可组合）

**日期**:2026-06-16
**状态**:设计阶段(待 review,未进入实施)
**作用域**:给 `<Markdown>` 控件新增一个 `boldStyle` 属性,让作者控制"粗体"如何渲染 —— 把硬编码的 TMP `<b>` 替换成可配置的标签组合。新增一个作者可写的 XML 属性 + 一个公开 C# 字段(`MarkdownStyle.BoldStyle`);`authoring-promptugui-xml`(reference/controls-markdown.md)与 `scripting-promptugui-csharp` 都必须更新。
**关联**:建立在 [`2026-06-09-markdown-control-design.md`](2026-06-09-markdown-control-design.md) 的 `<Markdown>` 控件之上;粗体走 scale-放大的标题渲染见该 spec。不改其它控件、不改模态。

---

## 1. 背景与目标

`<Markdown>` 当前在三处把内容包进 TMP 的 `<b>…</b>`:

1. 行内 `**bold**` / `__bold__`（`MarkdigRenderer.AppendInline` 的 `EmphasisInline` 分支,`DelimiterCount >= 2`）;
2. 标题 h1–h6（`RenderBlock` 的 `HeadingBlock` 分支 → `NewText(..., bold: true)`）;
3. GFM 表头单元格（`RenderTable` → `NewText(..., bold: row.IsHeader)`）。

TMP 的 `<b>` 对**没有真粗体字重的字体**（尤其位图/像素字体）只能做**合成描粗**(faux-bold):把字形外扩一圈,像素字体下边缘糊、发虚,非常难看。标题虽然已经靠 `RectTransform.localScale`（`HeadingScales`）放大、字号本身不变(像素清晰),但额外的 `<b>` 仍会把放大后的标题描粗。

**目标**:增加一个属性,让作者把"粗体"重定向成像素字体下更好看的表现 —— 例如下划线、换一个强调色,或者干脆去掉描粗只靠字号/scale 区分。要求**关键字与颜色可任意组合**(下划线 + 颜色同时生效)。

**非目标**:
- 不动斜体 `*italic*`(仍 `<i>`)与删除线 `~~~~`(仍 `<s>`)。本次只重定向粗体。将来若需要,可对称加 `italicStyle`,实现成本几乎为零(见 MDBS-D6)。
- 不支持渐变色作为粗体色(行内 `<color>` 标签本就只能单色;且逗号已被渐变语法占用,见 MDBS-D4)。

## 2. 接口

**XML**(`reference/controls-markdown.md` 属性表新增一行):

```xml
<Markdown id="md" text="..."
          boldStyle="underline #ffcc00"/>   <!-- 下划线 + 金色 -->
```

**C#**(`MarkdownStyle` 新增字段):

```csharp
public sealed class MarkdownStyle
{
    // ...
    /// <summary>How **bold** (and headings / table headers) render. Space-separated tokens:
    /// style keywords {bold, underline, italic, strikethrough, none} + at most one color value
    /// (theme token / hex / CSS name / "/alpha" suffix). Combinable, e.g. "underline #ffcc00".
    /// Default "bold" → TMP &lt;b&gt; (unchanged). "none" → strip. Color → &lt;color=…&gt;.</summary>
    public string BoldStyle = "bold";
    // ...
}
```

`Markdown` 控件新增 `[UIAttr] BoldStyle` setter（**非** `IsColor` —— 值不只是颜色,而是关键字+颜色的组合串）,设进 `Style.BoldStyle` 后 `MarkDirty()`,与现有 `BodyFont` / `LinkColor` 等 setter 同构。属性名由 `[UIAttr]` 自动从属性名派生为 `boldStyle`。

## 3. 取值语法

值 = 以**空格**分隔的若干 token,逐个识别后**组合嵌套**:

| token（大小写不敏感） | 含义 | 产出标签 |
|---|---|---|
| `bold` | 描粗(当前默认行为) | `<b>` |
| `underline` | 下划线 | `<u>` |
| `italic` | 斜体 | `<i>` |
| `strikethrough`（别名 `strike`） | 删除线 | `<s>` |
| `none` | 去掉一切样式,纯文字 | （无包裹,且忽略其它 token） |
| 其它任意值 | 当作颜色,走标准颜色管线解析 | `<color=#RRGGBBAA>` |

**颜色解析**复用既有管线（与 `linkColor` / `bodyColor` 同：`UI.Theme.Resolve` → 主题 token 基链 → hex 字面量 → CSS 命名色 → `/alpha` 后缀替换 alpha 分量）。一个 `boldStyle` 串里**至多一个**颜色 token（多写时后者覆盖前者,不报错）。

**组合示例**:

```
boldStyle 未写 / "bold"     → <b>…</b>                         （默认,向后兼容）
boldStyle="underline #ffcc00" → <u><color=#ffcc00ff>…</color></u>   （下划线+金色）
boldStyle="bold accent"      → <b><color=…>…</color></b>          （保留粗体再加主题色）
boldStyle="#ffcc00"          → <color=#ffcc00ff>…</color>          （只换色,不描粗）
boldStyle="accent/0.8"       → <color=…>…</color>                 （主题色 + 0.8 alpha）
boldStyle="none"             → …                                  （纯文字）
```

**嵌套顺序**:按 token 出现顺序"先开先在外、后开后在内"地嵌套（开标签按序追加,闭标签逆序）。这些 TMP 标签互相独立,嵌套顺序不影响渲染结果,但产出确定、可测。

**边界**:
- 未写 / 空白串 → 视为 `"bold"`(默认)。控件 setter 对空字符串走"跳过"语义(同其它 setter),`MarkdownStyle.BoldStyle` 字段默认即 `"bold"`。
- `none` 一旦出现即清空全部包裹并忽略同串其它 token（`none` 与别的 token 混写无意义,以 `none` 为准）。
- 无法识别又无法解析成颜色的 token：交给颜色管线兜底(与现有 `bodyColor` 行为一致,不额外报错;后续如需要可加 lint,本次不做)。

## 4. 作用范围（MDBS-D2）

`boldStyle` 统一治理上述三处 faux-bold:行内 `**bold**`、标题、表头单元格。标题仍走 `HeadingScales` 的 scale 放大(字号不变),`boldStyle` 只替换它额外的 `<b>` 包裹 —— 即"标题 = scale 放大 + boldStyle 修饰"。

斜体 `*italic*`、删除线 `~~~~`、行内 `[link]`、代码 `` `code` `` 不受影响。注意:若 `boldStyle` 含颜色而粗体片段内又嵌了链接,链接自带的 `<color=LinkColor>` 在内层、TMP 内层颜色胜出,链接颜色不被粗体色覆盖(符合预期)。

## 5. 实现

仅改 `Runtime/MarkdigBackend/MarkdigRenderer.cs`(在 `PROMPTUGUI_HAS_MARKDIG` 门控的 `PromptUGUI.Markdown` asmdef 内)+ `Runtime/Markdown/MarkdownStyle.cs` + `Runtime/Controls/Markdown.cs`。

**MarkdownStyle.cs**:加 `public string BoldStyle = "bold";` 字段(`Clone()` 走 `MemberwiseClone`,string 自动复制,无需额外处理)。

**Markdown.cs**:加 `[UIAttr, Preserve] public string BoldStyle { set { ...; Style.BoldStyle = v; MarkDirty(); } }`,空串跳过,与 `BodyFont` 同构。

**MarkdigRenderer.cs**:
- 新增两个 per-render 字段 `_boldOpen` / `_boldClose`,在 `Render(...)` 开头(`_style` 赋值后)调用 `ComputeBoldWrap()` 解析一次。
- `ComputeBoldWrap()`:按空格切 `_style.BoldStyle`,逐 token 追加开标签 / 逆序拼闭标签;遇 `none` 清空两段并返回;非关键字 token 走 `ToHex(tok)`(已存在的颜色 helper)拼 `<color=…>`。
- 新增 `private string WrapBold(string inner) => _boldOpen.Length == 0 ? inner : _boldOpen + inner + _boldClose;`。
- 三处替换:
  - `NewText` 的 `bold ? $"<b>{richText}</b>" : richText` → `bold ? WrapBold(richText) : richText`（标题 + 表头自动覆盖,二者都走 `NewText`）。
  - `EmphasisInline` 的粗体分支(`DelimiterChar != '~' && DelimiterCount >= 2`)用 `_boldOpen` / `_boldClose` 包裹子内容,替换原 `<b>` / `</b>`;斜体/删除线分支不变。

约 30 行净增。运行时无新分配热路径(每次 Render 只解析一次)。

## 6. 测试（TDD,先红后绿）

测试加在 Markdown 测试程序集(`PROMPTUGUI_HAS_MARKDIG` 门控,与现有 Markdig 渲染测试同处)。直接断言 `MarkdigRenderer.Render(md, style)` 产出 IR 里 `<Text>` 节点的 `TextContent` 子串:

1. `BoldStyle` 默认 → 行内 `**x**` 产出含 `<b>x</b>`(回归,保持现状)。
2. `BoldStyle="underline"` → 含 `<u>`、不含 `<b>`。
3. `BoldStyle="underline #ffcc00"` → 同时含 `<u>` 与 `<color=#FFCC00FF>`(组合)。
4. `BoldStyle="none"` → 既不含 `<b>` 也不含 `<u>`/`<color>`(纯文字)。
5. 颜色单独：`BoldStyle="#ff0000"` → 含 `<color=#FF0000FF>`、不含 `<b>`。
6. 主题 token：`BoldStyle="<已注册的主题色名>"` → 解析成对应 hex 的 `<color=…>`。
7. 标题：`# H` 在 `BoldStyle="underline"` 下,标题 `<Text>` 的 `TextContent` 含 `<u>`、不含 `<b>`。
8. 表头：GFM 表的表头单元格在 `BoldStyle="none"` 下不含 `<b>`。

(XML→属性的打通路径已由现有 `[UIAttr]` 反射 + `MarkdownStyle` setter 测试模式覆盖;如 Markdown 控件已有 EditMode 属性测试则补一条 `boldStyle` 设值断言。)

## 7. 文档更新（同 PR）

- `reference/controls-markdown.md`:
  - 属性表新增 `boldStyle` 行(取值/默认/效果)。
  - "Inline → TMP rich-text mapping" 表的 `**bold**` 行:从写死 `<b>…</b>` 改为说明"受 `boldStyle` 控制(默认 `<b>`)"。
  - "Block → control mapping" 的 Heading 行与 GFM table 行:标注粗体包裹受 `boldStyle` 控制。
- `scripting-promptugui-csharp/SKILL.md` 的 `MarkdownStyle` 段:新增 `BoldStyle` 字段说明。

## 8. 决策记录

- **MDBS-D1 单属性、关键字+颜色组合串(空格分隔)**。备选"任意裸 TMP 标签(闭标签按名推导)"被否:对 LLM 作者不友好、颜色用不上主题 token/alpha 后缀、难 lint。关键字集覆盖像素字体的真实诉求(下划线/换色/去描粗),颜色复用全库一致的 token 管线。
- **MDBS-D2 治理全部三处 faux-bold(行内+标题+表头)**,而非只行内。动机本就是"像素字体下粗体难看",标题/表头同样是粗体重灾区;用户明确点名标题。
- **MDBS-D3 默认 `"bold"`,向后兼容**。不写属性 = 现状 `<b>`。去描粗用显式 `none`,不让空串承担"去掉"语义(空串=未设,同其它 setter)。
- **MDBS-D4 分隔符用空格、颜色仅单色**。逗号已被渐变色语法 `#fff,#000` 占用;且行内 `<color>` 标签只能单色,渐变无法表达。颜色 token 内部不含空格(主题名/hex/CSS 名/`/alpha` 后缀皆然),空格切分安全。
- **MDBS-D5 `none` 独占**。出现即清空、忽略其它 token,语义最不易误解。
- **MDBS-D6 斜体本次不动**。只重定向粗体;`italicStyle` 留作将来对称扩展(同一 `ComputeWrap` 机制),本次 YAGNI。
