# Markdown `boldStyle` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `boldStyle` attribute to `<Markdown>` that redirects how "bold" (inline `**bold**`, headings, and table headers) renders — replacing the hardcoded TMP `<b>` with a configurable, combinable mix of style keywords + a color (so pixel fonts can use underline / a color instead of ugly faux-bold).

**Architecture:** One new string field `MarkdownStyle.BoldStyle` (default `"bold"`, backward-compatible). `MarkdigRenderer` parses it once per `Render` into an open/close tag pair (`ComputeBoldWrap`) and wraps the three faux-bold sites through a `WrapBold` helper. The XML `boldStyle` attribute maps to the field via a `[UIAttr]` setter on the `Markdown` control. Color tokens resolve through the existing `UI.Theme.Resolve` chokepoint (theme token / hex / CSS name / `/alpha` suffix), wrapped in try/catch so a bad token never breaks rendering.

**Tech Stack:** Unity 6 / C# 9, Markdig (gated behind `PROMPTUGUI_HAS_MARKDIG`), R3, NUnit (UnityMCP test runner), `dotnet format` lint.

**Spec:** [`docs~/superpowers/specs/2026-06-16-markdown-bold-style-design.md`](../specs/2026-06-16-markdown-bold-style-design.md)

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `Runtime/Markdown/MarkdownStyle.cs` | Modify | Add `public string BoldStyle = "bold";` field |
| `Runtime/MarkdigBackend/MarkdigRenderer.cs` | Modify | `ComputeBoldWrap()` + `WrapBold()`; route the 3 faux-bold sites through it (gated `PROMPTUGUI_HAS_MARKDIG` asmdef) |
| `Runtime/Controls/Markdown.cs` | Modify | `[UIAttr] BoldStyle` setter → `Style.BoldStyle` + `MarkDirty()` |
| `Tests/EditMode/Controls/MarkdownControlTests.cs` | Modify | Field default test (`MarkdownStyleTests`) + attribute-flow test (`MarkdownControlScaffoldTests`) — regular EditMode asmdef, no Markdig needed |
| `Tests/EditMode/Markdown/MarkdigRendererTests.cs` | Modify | Renderer behavior tests — gated `PromptUGUI.Tests.EditMode.Markdown` asmdef |
| `.claude/skills/authoring-promptugui-xml/reference/controls-markdown.md` | Modify | `boldStyle` attribute row + inline/block mapping notes |
| `.claude/skills/scripting-promptugui-csharp/SKILL.md` | Modify | `MarkdownStyle.BoldStyle` in the style example block |

**Test-runner note (every "Run" step):** Tests run through UnityMCP, not a shell. The canonical sequence after a source edit:

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])           # confirm 0 compile errors first
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=[...], group_names=[...])   # returns job_id
mcp__UnityMCP__get_test_job(job_id=...)                              # poll until done; read pass/fail
```

`group_names` filters to a test class. The two relevant assemblies: `PromptUGUI.Tests.EditMode` (field + control tests) and `PromptUGUI.Tests.EditMode.Markdown` (renderer tests, requires `PROMPTUGUI_HAS_MARKDIG`).

---

## Task 1: `MarkdownStyle.BoldStyle` field

**Files:**
- Modify: `Runtime/Markdown/MarkdownStyle.cs`
- Test: `Tests/EditMode/Controls/MarkdownControlTests.cs` (class `MarkdownStyleTests`)

- [ ] **Step 1: Write the failing test**

Add to the `MarkdownStyleTests` class in `Tests/EditMode/Controls/MarkdownControlTests.cs`:

```csharp
[Test]
public void CreateDefault_boldStyle_is_bold()
{
    Assert.AreEqual("bold", MarkdownStyle.CreateDefault().BoldStyle);
}
```

- [ ] **Step 2: Run test to verify it fails**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: compile error `CS1061: 'MarkdownStyle' does not contain a definition for 'BoldStyle'` (field doesn't exist yet).

- [ ] **Step 3: Add the field**

In `Runtime/Markdown/MarkdownStyle.cs`, add the field next to the other string style fields (after `BodyColor`, around line 15):

```csharp
        public string BodyColor = "";   // empty = inherit ProceduralBuilders.DefaultLabelColor (body text gets no color=)
        // How **bold** (and headings / table headers) render. Space-separated tokens: style keywords
        // {bold, underline, italic, strikethrough, none} + at most one color value (theme token / hex /
        // CSS name / "/alpha" suffix). Combinable, e.g. "underline #ffcc00". Default "bold" → TMP <b>
        // (unchanged). "none" → strip. A color token → <color=…>. Parsed by MarkdigRenderer.ComputeBoldWrap.
        public string BoldStyle = "bold";
```

`Clone()` uses `MemberwiseClone()`, which copies the string reference — strings are immutable, so no extra clone handling is needed.

- [ ] **Step 4: Run test to verify it passes**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["MarkdownStyleTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: PASS (all `MarkdownStyleTests`, including the existing 3).

- [ ] **Step 5: Commit**

```bash
git add Runtime/Markdown/MarkdownStyle.cs Tests/EditMode/Controls/MarkdownControlTests.cs
git commit -m "feat: add MarkdownStyle.BoldStyle field (default bold)"
```

---

## Task 2: Renderer keyword wrapping (`ComputeBoldWrap` / `WrapBold` + 3 sites)

Implements the style-keyword tokens (`bold`/`underline`/`italic`/`strikethrough`/`none`) and routes all three faux-bold sites through `WrapBold`. Color tokens are **not** handled yet (unknown tokens are ignored here; Task 3 adds color).

**Files:**
- Modify: `Runtime/MarkdigBackend/MarkdigRenderer.cs`
- Test: `Tests/EditMode/Markdown/MarkdigRendererTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `MarkdigRendererTests` in `Tests/EditMode/Markdown/MarkdigRendererTests.cs`. First add a style-aware render helper next to the existing `Render(string md)` (around line 11):

```csharp
        private static ElementNode Render(string md, string boldStyle)
        {
            var style = MarkdownStyle.CreateDefault();
            style.BoldStyle = boldStyle;
            return new MarkdigRenderer().Render(md, style).Root;
        }
```

Then add the tests (the `Count` helper already exists in this file):

```csharp
        [Test]
        public void BoldStyle_default_still_wraps_bold()
        {
            var root = Render("**x**", "bold");
            Assert.GreaterOrEqual(Count(root, n => n.Tag == "Text" && n.TextContent != null && n.TextContent.Contains("<b>")), 1);
        }

        [Test]
        public void BoldStyle_underline_uses_u_not_b()
        {
            var root = Render("**x**", "underline");
            Assert.GreaterOrEqual(Count(root, n => n.Tag == "Text" && n.TextContent != null && n.TextContent.Contains("<u>")), 1);
            Assert.AreEqual(0, Count(root, n => n.Tag == "Text" && n.TextContent != null && n.TextContent.Contains("<b>")));
        }

        [Test]
        public void BoldStyle_none_strips_bold()
        {
            var root = Render("**x**", "none");
            Assert.AreEqual(0, Count(root, n => n.Tag == "Text" && n.TextContent != null && n.TextContent.Contains("<b>")));
            Assert.AreEqual(0, Count(root, n => n.Tag == "Text" && n.TextContent != null && n.TextContent.Contains("<u>")));
        }

        [Test]
        public void BoldStyle_two_keywords_nest_in_order()
        {
            var t = Find(Render("**x**", "bold underline"), "Text", "<b>");
            Assert.IsNotNull(t);
            StringAssert.Contains("<b><u>x</u></b>", t.TextContent);   // open in order, close reversed
        }

        [Test]
        public void BoldStyle_applies_to_headings()
        {
            var root = Render("# Title", "underline");
            Assert.GreaterOrEqual(Count(root, n => n.Tag == "Text" && n.TextContent != null && n.TextContent.Contains("<u>")), 1);
            Assert.AreEqual(0, Count(root, n => n.Tag == "Text" && n.TextContent != null && n.TextContent.Contains("<b>")));
        }

        [Test]
        public void BoldStyle_applies_to_table_headers()
        {
            // header row is bold by default; "none" must strip it
            const string md = "| a | b |\n|---|---|\n| 1 | 2 |";
            Assert.GreaterOrEqual(Count(Render(md, "bold"), n => n.Tag == "Text" && n.TextContent != null && n.TextContent.Contains("<b>")), 1);
            Assert.AreEqual(0, Count(Render(md, "none"), n => n.Tag == "Text" && n.TextContent != null && n.TextContent.Contains("<b>")));
        }
```

- [ ] **Step 2: Run tests to verify they fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Markdown"], group_names=["MarkdigRendererTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: the new keyword tests FAIL (current renderer always emits `<b>`; `underline`/`none` produce no `<u>` / still `<b>`). `BoldStyle_default_still_wraps_bold` passes already.

- [ ] **Step 3: Add `ComputeBoldWrap` + `WrapBold` and the per-render fields**

In `Runtime/MarkdigBackend/MarkdigRenderer.cs`, add two fields next to `_imageSeq` (after line 22):

```csharp
        // Bold wrapper open/close tags, recomputed once per Render from _style.BoldStyle.
        private string _boldOpen = "<b>";
        private string _boldClose = "</b>";
```

Add these two methods (e.g. just after `NewText`, around line 160):

```csharp
        // Parse _style.BoldStyle (space-separated keyword/color tokens) into an open/close tag pair.
        private void ComputeBoldWrap()
        {
            var spec = string.IsNullOrWhiteSpace(_style.BoldStyle) ? "bold" : _style.BoldStyle;
            var open = new StringBuilder();
            var close = new StringBuilder();
            foreach (var tok in spec.Split(new[] { ' ', '\t', '\n', '\r' },
                                           System.StringSplitOptions.RemoveEmptyEntries))
            {
                switch (tok.ToLowerInvariant())
                {
                    case "none":
                        _boldOpen = ""; _boldClose = "";
                        return;
                    case "bold":
                        open.Append("<b>"); close.Insert(0, "</b>"); break;
                    case "italic":
                        open.Append("<i>"); close.Insert(0, "</i>"); break;
                    case "underline":
                        open.Append("<u>"); close.Insert(0, "</u>"); break;
                    case "strikethrough":
                    case "strike":
                        open.Append("<s>"); close.Insert(0, "</s>"); break;
                    default:
                        // Unknown token: ignored here. Color-token handling added in Task 3.
                        break;
                }
            }
            _boldOpen = open.ToString();
            _boldClose = close.ToString();
        }

        private string WrapBold(string inner) =>
            _boldOpen.Length == 0 ? inner : _boldOpen + inner + _boldClose;
```

- [ ] **Step 4: Call `ComputeBoldWrap` in `Render` and route the 3 sites**

In `Render(...)`, after `_imageSeq = 0;` (line 28), add:

```csharp
            ComputeBoldWrap();
```

In `NewText` (line 158), change:

```csharp
            n.TextContent = bold ? $"<b>{richText}</b>" : richText;
```
to:
```csharp
            n.TextContent = bold ? WrapBold(richText) : richText;
```
(this covers **both** headings and table headers — both build their `<Text>` via `NewText`.)

In `AppendInline`, replace the `EmphasisInline` case (lines 104–111) with:

```csharp
                case EmphasisInline em:
                    {
                        bool isBold = em.DelimiterChar != '~' && em.DelimiterCount >= 2;
                        if (isBold)
                        {
                            sb.Append(_boldOpen);
                            foreach (var child in em) AppendInline(sb, child);
                            sb.Append(_boldClose);
                        }
                        else
                        {
                            string tag = em.DelimiterChar == '~' ? "s" : "i";
                            sb.Append('<').Append(tag).Append('>');
                            foreach (var child in em) AppendInline(sb, child);
                            sb.Append("</").Append(tag).Append('>');
                        }
                        break;
                    }
```

- [ ] **Step 5: Run tests to verify they pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Markdown"], group_names=["MarkdigRendererTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: PASS — all new keyword tests **and** the pre-existing ones (`Heading_becomes_bold_text`, `Paragraph_inline_maps_to_tmp_tags_and_escapes` stay green because default `BoldStyle="bold"` still yields `<b>`, and italic `<i>` / strike `<s>` are untouched).

- [ ] **Step 6: Commit**

```bash
git add Runtime/MarkdigBackend/MarkdigRenderer.cs Tests/EditMode/Markdown/MarkdigRendererTests.cs
git commit -m "feat: Markdown boldStyle keyword wrapping (bold/underline/italic/strike/none)"
```

---

## Task 3: Color token + alpha in `boldStyle`

Fills in the `default:` branch of `ComputeBoldWrap` so a non-keyword token resolves as a single solid color via the standard pipeline, wrapped in try/catch so an invalid token warns-and-skips instead of throwing.

**Files:**
- Modify: `Runtime/MarkdigBackend/MarkdigRenderer.cs`
- Test: `Tests/EditMode/Markdown/MarkdigRendererTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `MarkdigRendererTests`:

```csharp
        [Test]
        public void BoldStyle_color_hex_emits_color_tag()
        {
            var t = Find(Render("**x**", "#ffcc00"), "Text", "<color=");
            Assert.IsNotNull(t);
            StringAssert.Contains("<color=#FFCC00FF>", t.TextContent);   // ToHex uppercases, appends FF alpha
            StringAssert.DoesNotContain("<b>", t.TextContent);
        }

        [Test]
        public void BoldStyle_color_alpha_suffix_replaces_alpha()
        {
            var t = Find(Render("**x**", "#ff0000/0.4"), "Text", "<color=");
            Assert.IsNotNull(t);
            StringAssert.Contains("<color=#FF000066>", t.TextContent);   // 0.4*255 = 102 = 0x66
        }

        [Test]
        public void BoldStyle_underline_plus_color_nests()
        {
            var t = Find(Render("**x**", "underline #ffcc00"), "Text", "<u>");
            Assert.IsNotNull(t);
            StringAssert.Contains("<u><color=#FFCC00FF>x</color></u>", t.TextContent);
        }

        [Test]
        public void BoldStyle_invalid_token_does_not_throw_and_renders_plain()
        {
            // 'bogus' is neither a keyword nor a resolvable color. Render must NOT throw (the try/catch
            // in the color branch swallows UI.Theme.Resolve's exception) and must still emit the text.
            Assert.DoesNotThrow(() => Render("**x**", "bogus"));
            var t = Find(Render("**x**", "bogus"), "Text", "x");
            Assert.IsNotNull(t);
            StringAssert.DoesNotContain("<b>", t.TextContent);
        }
```

(No new `using` needed — these tests use only NUnit + the existing `Render`/`Find`/`Count` helpers.)

- [ ] **Step 2: Run tests to verify they fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Markdown"], group_names=["MarkdigRendererTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: the 3 color tests (`..._color_hex_...`, `..._color_alpha_...`, `..._underline_plus_color_...`) FAIL — Task 2's `default:` branch ignores color tokens, so no `<color=>` is emitted. The invalid-token guard already passes (Task 2 ignores unknown → plain text, no throw) and stays green; it guards the try/catch you add in Step 3 (without it, the color code below would throw on `'bogus'`).

- [ ] **Step 3: Implement color resolution in the `default:` branch**

In `ComputeBoldWrap`, replace the `default:` branch:

```csharp
                    default:
                        // Unknown token: ignored here. Color-token handling added in Task 3.
                        break;
```
with:
```csharp
                    default:
                        // Treat as a single solid color via the standard pipeline (theme token /
                        // hex / CSS name / "/alpha"). try/catch so a typo'd token warns + is skipped
                        // rather than throwing out of Render (UI.Theme.Resolve throws on unknown/gradient).
                        try
                        {
                            var hex = ToHex(tok);
                            open.Append("<color=").Append(hex).Append('>');
                            close.Insert(0, "</color>");
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogWarning($"<Markdown> boldStyle token '{tok}' is not a known keyword " +
                                $"or resolvable color; ignored. ({e.Message})");
                        }
                        break;
```

`ToHex` already exists in this file (`"#" + ColorUtility.ToHtmlStringRGBA(UI.Theme.Resolve(token))`) and handles the full token/hex/CSS/alpha pipeline.

- [ ] **Step 4: Run tests to verify they pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Markdown"], group_names=["MarkdigRendererTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: PASS — all color tests plus everything from Task 2.

- [ ] **Step 5: Commit**

```bash
git add Runtime/MarkdigBackend/MarkdigRenderer.cs Tests/EditMode/Markdown/MarkdigRendererTests.cs
git commit -m "feat: Markdown boldStyle color tokens (+alpha, invalid-token tolerant)"
```

---

## Task 4: XML `boldStyle` attribute on the `Markdown` control

**Files:**
- Modify: `Runtime/Controls/Markdown.cs`
- Test: `Tests/EditMode/Controls/MarkdownControlTests.cs` (class `MarkdownControlScaffoldTests`)

- [ ] **Step 1: Write the failing test**

Add to `MarkdownControlScaffoldTests` in `Tests/EditMode/Controls/MarkdownControlTests.cs` (this class has `UI.ResetForTests()` in SetUp/TearDown already; the test exercises only attribute application, so it does **not** need Markdig):

```csharp
        [Test]
        public void BoldStyle_attribute_flows_to_style()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='B'><Markdown id='md' anchor='stretch' boldStyle='underline #ffcc00'/></Screen></PromptUGUI>";
            UI.LoadDocument("b", xml);
            var screen = UI.Open("B");
            var md = screen.Get<Markdown>("md");
            Assert.AreEqual("underline #ffcc00", md.Style.BoldStyle);
        }
```

- [ ] **Step 2: Run test to verify it fails**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["MarkdownControlScaffoldTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: FAIL — `boldStyle` is an unknown attribute, so it's never applied; `md.Style.BoldStyle` stays `"bold"`, assert mismatch (`Expected "underline #ffcc00" but was "bold"`).

- [ ] **Step 3: Add the `[UIAttr]` setter**

In `Runtime/Controls/Markdown.cs`, add after the `BodyColor` property (after line 105), mirroring the existing string setters (empty string → skip, same as `BodyFont`/`LinkColor`):

```csharp
        [UIAttr, Preserve]
        public string BoldStyle
        {
            set
            {
                if (string.IsNullOrEmpty(value) || value == Style.BoldStyle) return;
                Style.BoldStyle = value;
                MarkDirty();
            }
        }
```

`[UIAttr]` with no explicit name derives the attribute name from the property → `boldStyle`. It is **not** `IsColor` — the value is a keyword+color combination string, not a pure color.

- [ ] **Step 4: Run test to verify it passes**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["MarkdownControlScaffoldTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Markdown.cs Tests/EditMode/Controls/MarkdownControlTests.cs
git commit -m "feat: <Markdown boldStyle> XML attribute"
```

---

## Task 5: Documentation (XML reference + C# SKILL)

Per CLAUDE.md, a new XML attribute + public C# field requires SKILL updates in the same PR.

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/reference/controls-markdown.md`
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`

- [ ] **Step 1: Add the `boldStyle` row to the XML attribute table**

In `controls-markdown.md`, in the `## Attributes` table (after the `wrap` row, line 78), add:

```markdown
| `boldStyle` | space-separated tokens | `bold` | How **bold** renders — governs inline `**bold**`, **headings**, and **table headers** (the three places that otherwise emit TMP `<b>`). Tokens combine: style keywords `bold` / `underline` / `italic` / `strikethrough` (and `none` = strip), plus at most one color value (theme token / hex / CSS name / `/alpha` suffix). E.g. `boldStyle="underline #ffcc00"` → underline + gold; `boldStyle="none"` → plain (headings still magnify via `scale`). Default `bold` = TMP `<b>` (faux-bold — looks poor on pixel fonts, hence this attribute). Maps to `MarkdownStyle.BoldStyle`. |
```

- [ ] **Step 2: Update the inline + block mapping notes**

In the "Inline → TMP rich-text mapping" table, change the `**bold**` row (line 141) from:

```markdown
| `**bold**` | `<b>…</b>` |
```
to:
```markdown
| `**bold**` | `boldStyle` wrap (default `<b>…</b>`; e.g. `<u>…</u>` / `<color=…>…</color>` / combined) |
```

In the "Block → control mapping" table, update the Heading row (line 122) — append to its Notes cell: ` Bold wrap governed by \`boldStyle\` (default \`<b>\`).` And the GFM table row (line 131) — change `Header row is bold;` to `Header row uses the \`boldStyle\` wrap (default bold);`.

- [ ] **Step 3: Add `BoldStyle` to the C# SKILL style example**

In `scripting-promptugui-csharp/SKILL.md`, in the per-control style block, after the `md.Style.BodyColor` line (line 315), add:

```csharp
md.Style.BoldStyle = "underline accent";        // how **bold** + headings + table headers render (XML: boldStyle=). Space-separated: keywords bold/underline/italic/strikethrough/none + one color (token/hex/CSS/alpha). Default "bold" → TMP <b> (faux-bold; ugly on pixel fonts). "none" → strip
```

- [ ] **Step 4: Verify the docs (no test; visual read)**

Re-read the edited table rows to confirm no broken Markdown table syntax (pipe counts match the header) and the wording matches the implemented behavior.

- [ ] **Step 5: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/reference/controls-markdown.md .claude/skills/scripting-promptugui-csharp/SKILL.md
git commit -m "docs: document <Markdown boldStyle> in XML + C# skills"
```

---

## Task 6: Full verification & finalize

**Files:** none (verification only)

- [ ] **Step 1: Lint**

From the repo root:

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: no changes / no warnings. (Do **not** run `dotnet format analyzers --severity info` — see CLAUDE.md.) If `dotnet format whitespace` reports object-initializer or trailing-whitespace fixes, re-stage and amend the relevant commit.

- [ ] **Step 2: Full EditMode suite (both assemblies)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__get_test_job(job_id=...)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Markdown"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: 0 failures in both. (Don't trust per-group runs alone — run the whole assemblies. Pre-existing renderer tests `Heading_becomes_bold_text` / `Paragraph_inline_maps_to_tmp_tags_and_escapes` must remain green.)

- [ ] **Step 3: PlayMode suite (Markdown touch-point)**

```
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: 0 failures. (`MarkdownWebImageTests` etc. unaffected — sanity check only.)

- [ ] **Step 4: Confirm clean tree & branch**

```bash
git status --short          # expect clean
git log --oneline -7        # the 5 feature commits + spec, all on feat/markdown-bold-style
git branch --show-current   # feat/markdown-bold-style (NOT main)
```

- [ ] **Step 5: Hand back for visual QA**

Report the green test counts and lint status. Do not merge to `main` (CLAUDE.md: never commit to main). Surface the branch for the user's visual QA / PR decision.

---

## Self-Review

**1. Spec coverage** — every spec section maps to a task:
- §2 接口 (XML attr + C# field) → Task 1 (field), Task 4 (XML attr).
- §3 取值语法 (keywords, color, combination, default, `none`, separator) → Task 2 (keywords + default + combo + none), Task 3 (color + alpha + invalid).
- §4 作用范围 (inline + heading + table-header) → Task 2 (all 3 sites routed via `WrapBold`/`NewText`; heading + table tests).
- §5 实现 (ComputeBoldWrap/WrapBold, per-render parse, 3 sites, try/catch) → Task 2 + Task 3.
- §6 测试 → Tasks 2–4 (default, underline, combo, none, color, alpha, heading, table, invalid, attr-flow).
- §7 文档 → Task 5.
- §8 决策 — D1/D3 (default bold) Task 1+2; D2 (3 sites) Task 2; D4 (space sep, single color) Task 3 split logic; D5 (`none` independent → early return) Task 2; D6 (italic untouched) verified by keeping the `else` branch in Task 2's `EmphasisInline`.

**2. Placeholder scan** — no TBD/TODO; every code step shows full code; run steps give exact MCP calls + expected result. The one `job_id=...` is a runtime value the executor fills from the prior `run_tests` return, not a plan placeholder.

**3. Type/name consistency** — `MarkdownStyle.BoldStyle` (Task 1) used identically in renderer (Task 2/3), control setter (Task 4), tests, docs. `ComputeBoldWrap()` / `WrapBold(string)` / fields `_boldOpen` / `_boldClose` named consistently across Task 2 (declare) and Task 3 (edit `default:` only). `Render(string, string)` test helper added once in Task 2, reused in Task 3. `Count` / `Find` helpers pre-exist in the renderer test file. The `default:`-branch `Debug.LogWarning` is fire-and-forget (no test asserts its text — the invalid-token test only asserts no-throw + plain output, so it stays robust to UI theme state).
