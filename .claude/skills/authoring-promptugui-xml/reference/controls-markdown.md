# `<Markdown>` — Markdown rendering

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md). The `<Markdown>` attribute table lives in the main doc's built-in primitives catalog; read this for the dynamic/static-CDATA usage patterns, block→control mapping, lossy notes, and lint rules.

`<Markdown>` parses a Markdown string and renders it block-by-block into a built-in vertical `ScrollRect`, reusing the existing `Text` / `RawImage` / `VStack` / `HStack` / `Frame` / `Grid` primitives. The parsed blocks are stacked vertically; the control's own `ScrollRect` lets the document scroll when it exceeds the available height.

Requires Markdig — see the [`using-promptugui-markdown`](../../using-promptugui-markdown/SKILL.md) skill. Without Markdig the control shows the raw Markdown text as a plain `<Text>`.

---

## Content: dynamic vs. static

### Dynamic (primary path)

Leave the element empty (or with a placeholder) in XML and set the document from C# at runtime:

```xml
<!-- Empty placeholder — C# fills it in -->
<Markdown id="patchNotes" anchor="stretch" margin="16"/>

<!-- Optional placeholder text while loading -->
<Markdown id="help" anchor="stretch" text="Loading…"/>
```

```csharp
screen.Get<Markdown>("patchNotes").Text = await Http.GetString(patchNotesUrl);
// or reactively:
screen.Get<Markdown>("patchNotes").BindText(localizedStream).AddTo(screen);
```

`Text` set triggers a full re-render of the subtree. Text appears immediately; block-level images stream in asynchronously (placeholder alt text shown until the texture arrives).

### Static CDATA (small / fixed documents)

Inline Markdown directly in the XML element body. Use `<![CDATA[...]]>` to protect Markdown characters (`<`, `&`) from the XML parser:

```xml
<Markdown id="about" anchor="stretch" margin="16"><![CDATA[
# About

A **pixel-art** game that scales from PC widescreen to mobile portrait.

- Handcrafted levels
- Responsive layout via PromptUGUI

More at [the homepage](https://example.com).
]]></Markdown>
```

The XML parser delivers the CDATA content as the element's text; the parser maps it through the `defaultTextAttr:"text"` registration, exactly as `<Text>Hello</Text>` works for `<Text>`.

### Combined style attributes

Frequently-used style items can be set on the tag itself; full styling lives in C# `MarkdownStyle` (`md.Style` / `UI.Markdown.DefaultStyle`):

```xml
<Markdown id="doc" anchor="stretch"
          bodyFont="default" codeFont="mono"
          linkColor="primary" spacing="8" wrap="true"/>
```

---

## Attributes

| Attribute | Type / values | Default | Effect |
|---|---|---|---|
| `text` | Markdown source string (CDATA for inline) | `""` | Document content. Registered as `defaultTextAttr`, so inline element text and CDATA flow into it automatically. A value set from C# at runtime survives resize / Variant / Theme ReSolve (the DefaultText lock — same mechanism as `<Text text>`). |
| `bodyFont` | font type name | `"default"` | Body text and heading font, resolved via `FontApplier`. |
| `codeFont` | font type name | `"default"` | Inline code and fenced code block font. Use a monospace font type. |
| `linkColor` | color token / hex / CSS name / `/alpha` suffix | theme link color | Link text color, applied as TMP `<color=…>` inside `<link>` spans. |
| `spacing` | float | `MarkdownStyle` default | Vertical spacing between top-level blocks (pixels). |
| `wrap` | bool | `true` | Whether paragraph text wraps. `false` makes long paragraphs clip horizontally (the internal `ScrollRect` is vertical-only). |

> Complete styling (heading size ladder, quote bar color, code block background, list indent, bullet glyph, task-list glyphs, HR color / thickness) is controlled via C# `MarkdownStyle`. See **scripting-promptugui-csharp → Markdown** for the full API.

**Color attribute resolution** follows the standard pipeline: token → `UI.Theme.Resolve` base chain → hex literal → CSS named color → `/alpha` suffix replaces alpha component. Example: `linkColor="primary/0.8"`.

---

## Supported Markdown subset

`<Markdown>` covers CommonMark + the GFM extensions activated by Markdig's `.UseAdvancedExtensions()` (which enables `PipeTables`, strikethrough, task lists, and others):

| Feature | Supported |
|---|---|
| ATX headings `# h1` … `###### h6` | Yes |
| Setext headings | Yes |
| Paragraphs | Yes |
| **Bold** `**` / `__` | Yes |
| *Italic* `*` / `_` | Yes |
| ~~Strikethrough~~ `~~` | Yes (GFM) |
| `Inline code` `` ` `` | Yes |
| [Links](url) | Yes (click → `OnLinkClicked`) |
| Unordered lists `-` / `*` / `+` | Yes (nested) |
| Ordered lists `1.` … | Yes (start offset respected) |
| Task lists `- [x]` / `- [ ]` | Yes (GFM, display-only — not interactive) |
| Blockquotes `>` | Yes (nested) |
| Fenced code blocks ` ``` ` | Yes (no syntax highlighting) |
| Horizontal rule `---` | Yes |
| Block-level images `![alt](url)` | Yes (async texture load) |
| GFM tables | Yes (equal-width columns, no cell borders) |
| HTML blocks / inline HTML | Stripped — plain text only |
| Footnotes, math, emoji shortcodes | Not supported (v1) |

---

## Block → control mapping (§7.1 of design spec)

`MarkdigRenderer` walks the Markdig AST and emits an `ElementNode` IR subtree that is then instantiated with the standard `InstantiateNode` pipeline. The mapping is:

| Markdown block | Generated ElementNode | Notes |
|---|---|---|
| Document root | `<VStack spacing=BlockSpacing>` (the `ScrollRect.content`) | Anchor `top-stretch`, pivot top, `ContentSizeFitter` vertical; width tracks viewport, height tracks content |
| Heading h1–h6 | `<Text>` with `fontSize=HeadingSizes[n-1]` and `<b>` inline wrap | font=BodyFont |
| Paragraph | `<Text wrap>` with inline rich-text string | See inline mapping below |
| Unordered list | `<VStack>` — each item is `<HStack>` (bullet `<Text>`) + (content `<Text>`) | Nested lists: content cell contains another `<VStack>`; indent = `ListIndent × depth` |
| Ordered list | Same as unordered; bullet is `"1."`, `"2."`, … (start offset from Markdig) | |
| Task list item | Bullet glyph replaced by `CheckedGlyph` (`"☑"`) or `UncheckedGlyph` (`"☐"`) | Display-only — not a live `<Toggle>` |
| Blockquote `>` | `<HStack>` — left `<Image width=HrThickness color=QuoteBarColor>` + right `<VStack>` (recursive blocks) | Can nest |
| Fenced code block | `<Frame color=CodeBackground>` + inner `<Text font=CodeFont wrap=false>` | Language tag ignored; no syntax highlighting |
| Horizontal rule `---` | `<Image height=HrThickness color=HrColor anchor=top-stretch>` | |
| Block-level image `![alt](url)` | `<RawImage type="contain">` + generated id → `ImageRequest` | Texture loaded async; alt text shown as placeholder until texture arrives |
| GFM table | `<VStack>` → each row `<HStack>` → each cell `<Text width="stretch" wrap>` | Header row is bold; equal-width columns (`width="stretch"`); no cell border lines (v1) |
| HTML block | Plain text (tags stripped) or discarded | MD-D17 |

### Inline → TMP rich-text mapping

Inline content within a single paragraph or heading is compiled into one TMP rich-text string placed in a single `<Text>` node:

| Markdown inline | TMP output |
|---|---|
| Literal text | Escaped: `&` → `&amp;`, `<` → `&lt;`, `>` → `&gt;` (prevents TMP from consuming raw `<` as a tag) |
| `**bold**` | `<b>…</b>` |
| `*italic*` | `<i>…</i>` |
| `~~strike~~` | `<s>…</s>` |
| `` `code` `` | `<mark=CodeBackground><font="CodeFont">…</font></mark>` |
| `[text](url)` | `<link="url"><color=LinkColor><u>text</u></color></link>` |
| `![alt](url)` inline (mid-text) | Named TMP sprite if url matches a known sprite name → `<sprite name=…>`; otherwise dropped + one-time `Debug.LogWarning` |

---

## Lossy / out of scope (v1)

- **HTML**: `HtmlBlock` / `HtmlInline` content is stripped; only the inner plain text (if any) is kept. Full HTML rendering is not planned.
- **Inline images in text**: Only images whose URL matches a TMP sprite name (from a SpriteSet with `generateTmpSpriteAsset` enabled) appear inline as `<sprite name=…>`. All other inline images are discarded with a one-time warning. Use block-level `![alt](url)` on its own line for reliable image display.
- **No syntax highlighting**: Fenced code blocks render with the `codeFont` monospace font and a `CodeBackground` color, but no language-specific token coloring.
- **No table cell borders**: GFM table cells are equal-width `<Text width="stretch" wrap>` columns; there are no border lines between cells (v1 limitation).
- **Vertical scroll only**: The built-in `ScrollRect` is vertical. Wide tables and `wrap=false` code lines are clipped horizontally by the `RectMask2D` viewport. Horizontal scrolling within code / tables is a v2 item.
- **No virtualization**: The entire document is instantiated as GameObjects at once. Very large documents (thousands of blocks) should be paginated by the author.
- **Task lists are display-only**: `[x]` / `[ ]` render as the `CheckedGlyph` / `UncheckedGlyph` characters; they are not interactive `<Toggle>` controls.
- **No footnotes / math / emoji shortcodes**: These Markdig extensions are not activated in v1.

---

## Lint

| Code | Trigger | Message | Level |
|---|---|---|---|
| `PUI-MARKDOWN-NO-CHILDREN` | `<Markdown>` element has child XML elements | "Markdown content comes from `text=` (or inline CDATA); child elements are ignored — remove them." | warning |

Content comes from `text=`; child XML elements are silently ignored at runtime (they are not rendered as cards or any other layout). The lint CLI and `ScreenInstantiator` both surface this warning. Use `text=` or CDATA instead:

```xml
<!-- Wrong: child elements are silently ignored -->
<Markdown id="doc" anchor="stretch">
  <Text>This child is ignored</Text>
</Markdown>

<!-- Right: CDATA for inline Markdown -->
<Markdown id="doc" anchor="stretch"><![CDATA[
**This** is the right way.
]]></Markdown>

<!-- Right: empty placeholder, fill from C# -->
<Markdown id="doc" anchor="stretch"/>
```

---

## Requires Markdig

Rendering requires the Markdig package, which is a soft dependency gated by the `PROMPTUGUI_HAS_MARKDIG` compile symbol. Without Markdig:

- `<Markdown>` is still a valid XML tag (the control class is always compiled).
- The raw Markdown source string is displayed as a single plain `<Text wrap>` node.
- A one-time `Debug.LogWarning` is emitted pointing to the install instructions.

See the [`using-promptugui-markdown`](../../using-promptugui-markdown/SKILL.md) skill for installation and the editor auto-detector.
