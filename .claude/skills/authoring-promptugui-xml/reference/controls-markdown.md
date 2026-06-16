# `<Markdown>` — Markdown rendering

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md). The `<Markdown>` attribute table lives in the main doc's built-in primitives catalog; read this for the dynamic/static-CDATA usage patterns, block→control mapping, lossy notes, and lint rules.

`<Markdown>` parses a Markdown string and renders it block-by-block into a built-in vertical `ScrollRect`, reusing the existing `Text` / `RawImage` / `VStack` / `HStack` / `Frame` / `Grid` primitives. The parsed blocks are stacked vertically; the control's own `ScrollRect` lets the document scroll when it exceeds the available height.

Requires Markdig (a soft dependency gated by the `PROMPTUGUI_HAS_MARKDIG` compile symbol) — see [Setup — installing Markdig](#setup--installing-markdig) at the end of this doc. Without Markdig the control shows the raw Markdown text as a plain `<Text>`.

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

**Indentation is safe.** You may indent the CDATA body to line up with the surrounding XML — the parser strips the common leading indentation shared by all lines before the Markdown is parsed. Without this, uniformly-indented Markdown would be mis-read as an [indented code block](https://spec.commonmark.org/0.30/#indented-code-blocks) (4+ leading spaces), collapsing the whole document into one monospace block. Relative indentation is preserved, so an intentionally-deeper-indented fenced/code section still works.

### Combined style attributes

Frequently-used style items can be set on the tag itself; full styling lives in C# `MarkdownStyle` (`md.Style` / `UI.Markdown.DefaultStyle`):

```xml
<Markdown id="doc" anchor="stretch"
          fontSize="16" bodyFont="default" codeFont="mono"
          bodyColor="ink" linkColor="primary" spacing="8" padding="2" wrap="true"/>
```

---

## Attributes

| Attribute | Type / values | Default | Effect |
|---|---|---|---|
| `text` | Markdown source string (CDATA for inline) | `""` | Document content. Registered as `defaultTextAttr`, so inline element text and CDATA flow into it automatically. A value set from C# at runtime survives resize / Variant / Theme ReSolve (the DefaultText lock — same mechanism as `<Text text>`). |
| `fontSize` | float (px) | `16` | Base font size for **all** text. Headings render at this size and magnify via transform `scale` (see block mapping), so changing `fontSize` scales the whole document — headings included, proportions preserved. Maps to `MarkdownStyle.BodySize`. |
| `bodyFont` | font type name | `"default"` | Body text and heading font, resolved via `FontApplier`. |
| `codeFont` | font type name | `"default"` | Inline code and fenced code block font. Use a monospace font type. |
| `bodyColor` | color token / hex / CSS name / `/alpha` suffix | *(unset)* | Base text color for **all** generated body text (paragraphs, headings, lists, blockquotes, table cells, code). Applied as the TMP vertex color on each `<Text>` node. Unset → no `color=` is emitted, so body inherits the library-wide default ink color (`ProceduralBuilders.DefaultLabelColor`, a warm dark brown). `linkColor` still overrides per-link (inline `<color>` wins over the node color). |
| `linkColor` | color token / hex / CSS name / `/alpha` suffix | `#4EA1FF` | Link text color, applied as TMP `<color=…>` inside `<link>` spans. |
| `spacing` | float | `MarkdownStyle` default | Vertical spacing between top-level blocks (pixels). |
| `padding` | int (px) | `2` | Inner inset of the rendered content within the built-in scroll viewport (applied as the document-root `<VStack>`'s `padding`). The default `2` gives **outlined / stroked fonts** room so the viewport `RectMask2D` doesn't clip the first/last glyph outlines at the edges. Maps to `MarkdownStyle.Padding`. Set `0` for flush content. |
| `wrap` | bool | `true` | Whether paragraph text wraps. `false` makes long paragraphs clip horizontally (the internal `ScrollRect` is vertical-only). |

> Complete styling (heading **scale** ladder `HeadingScales`, quote bar color, code block background, list indent, bullet glyph, task-list glyphs, HR color / thickness) is controlled via C# `MarkdownStyle`. See **scripting-promptugui-csharp → Markdown** for the full API.

> **Headings magnify via transform scale, not font size.** Each heading renders at `BodySize` and is scaled up by `RectTransform.localScale` (`HeadingScales[level]`, default `{2, 1.75, 1.5, 1.25, 1.125, 1}` for h1–h6). Because the font is never re-sized, **bitmap / pixel fonts stay crisp** (the glyph is sampled at its native size and the quad is scaled). Integer scales (h1 = 2×, h6 = 1×) are pixel-perfect; non-integer levels are slightly soft on pixel fonts — set integer `HeadingScales` if you need every level pixel-perfect. This is the default for all fonts (the difference is negligible for SDF/vector fonts). At the default `BodySize 16` the visual ladder is the legacy `{32,28,24,20,18,16}`, unchanged.

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
| Document root | `<VStack spacing=BlockSpacing padding=Padding>` (the `ScrollRect.content`) | Anchor `top-stretch`, pivot top, `ContentSizeFitter` vertical; width tracks viewport, height tracks content. `padding` insets content off the viewport `RectMask2D` so outlined-font glyph edges aren't clipped (default 2) |
| Heading h1–h6 | `<Text fontSize=BodySize scale=HeadingScales[n-1]>` + `<b>` inline wrap | Magnified via RectTransform `localScale` (the V/HStack scale-wrapper + `ScaledTextLayoutBridge` reserve the visual size), **not** a larger font size → pixel fonts stay crisp. `scale 1` (h6 default) emits no `scale=`/wrapper. font=BodyFont |
| Paragraph | `<Text wrap>` with inline rich-text string | See inline mapping below |
| Unordered list | `<VStack>` — each item is `<HStack>` (bullet `<Text>`) + (content `<Text>`) | Nested lists: content cell contains another `<VStack>`; indent = `ListIndent × depth` |
| Ordered list | Same as unordered; bullet is `"1."`, `"2."`, … (start offset from Markdig) | |
| Task list item | Bullet glyph replaced by `CheckedGlyph` (`"☑"`) or `UncheckedGlyph` (`"☐"`) | Display-only — not a live `<Toggle>` |
| Blockquote `>` | `<HStack>` — left `<Image width=HrThickness color=QuoteBarColor>` + right `<VStack>` (recursive blocks) | Can nest |
| Fenced code block | `<Text font=CodeFont wrap=false>` with code background applied via inline TMP `<mark=…>` tag (no separate container) | Language tag ignored; no syntax highlighting |
| Horizontal rule `---` | `<Image width=stretch height=HrThickness color=HrColor>` | |
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

## Setup — installing Markdig

Rendering requires the **Markdig** package — a soft dependency gated by the `PROMPTUGUI_HAS_MARKDIG` compile symbol. The `<Markdown>` control class is always compiled and the XML tag is always valid; Markdig is only needed for actual rendering. **Without it**, `<Markdown>` shows the raw Markdown source as a single plain `<Text wrap>` node and emits a one-time `Debug.LogWarning`.

### Why a separate install step?

Markdig is not a UPM package, so it can't be declared in `Packages/manifest.json` and Unity's `versionDefines` can't auto-detect it. Instead: you add the Markdig DLL, and the editor auto-detector (`PromptUGUI.Editor.MarkdigDetector`) finds it in the loaded `AppDomain` and sets `PROMPTUGUI_HAS_MARKDIG`, which gates the `PromptUGUI.Markdown` asmdef that compiles `MarkdigRenderer` and auto-registers it with `UI.Markdown.Renderer`. Fully automatic once Markdig is in the project — no manual symbol editing required.

### Install

**Option A — NuGetForUnity (recommended):** Unity → **NuGet → Manage NuGet Packages → search "Markdig" → Install**.

- This project references **Markdig.Signed** (the signed NuGet package). Install **Markdig.Signed** to match; the namespace is `Markdig` either way, so the C# is identical.
- If you install the **unsigned Markdig** (DLL `Markdig.dll`), change `Runtime/MarkdigBackend/PromptUGUI.Markdown.asmdef`'s `precompiledReferences` from `["Markdig.Signed.dll"]` to `["Markdig.dll"]` — the asmdef ships referencing the signed DLL and won't compile against the unsigned one otherwise.

**Option B — manual DLL:** drop `Markdig.Signed.dll` (netstandard2.0 target) anywhere under `Assets/` (e.g. `Assets/Plugins/`). The auto-detector picks it up on the next domain reload. (Unsigned `Markdig.dll` → edit the asmdef as above.)

**Manual symbol (CI fallback):** if the auto-detector doesn't fire (unusual CI environment), define `PROMPTUGUI_HAS_MARKDIG` in **Project Settings → Player → Scripting Define Symbols** for each build target where Markdig is present.

### The auto-detector

`PromptUGUI.Editor.MarkdigDetector` — an `[InitializeOnLoad]` class with a static constructor — runs on every Editor domain reload, scans `AppDomain.CurrentDomain.GetAssemblies()` for an assembly named `Markdig` or `Markdig.Signed`, and adds/removes `PROMPTUGUI_HAS_MARKDIG` for the active build target accordingly (each change triggers a recompile). Installing or removing Markdig is picked up automatically.

### What the symbol lights up

| Component | Location | Behavior |
|---|---|---|
| `MarkdigRenderer` | `Runtime/MarkdigBackend/` (gated asmdef) | Walks Markdig AST → `ElementNode` IR subtree + `ImageRequest[]`. Registered with `UI.Markdown.Renderer` at domain load and after each `UI.ResetForTests()` (via `MarkdigBootstrap` + `UI.OnReset`). |
| `PromptUGUI.Markdown` asmdef | `Runtime/MarkdigBackend/PromptUGUI.Markdown.asmdef` | `defineConstraints: ["PROMPTUGUI_HAS_MARKDIG"]`, `precompiledReferences: ["Markdig.Signed.dll"]` (change to `"Markdig.dll"` for the unsigned variant), references `PromptUGUI.Runtime`. Compiled only when the symbol is defined. |
| `PromptUGUI.Tests.EditMode.Markdown` asmdef | `Tests/EditMode/Markdown/` | Same `defineConstraints`; renderer tree-shape + control integration tests. |

### IL2CPP / managed stripping

Markdig is pure managed code (no unsafe blocks, no P/Invoke); IL2CPP compiles it normally. If managed stripping (Medium/High) removes it and `<Markdown>` falls back to plain text in an IL2CPP build (the warning fires because `UI.Markdown.Renderer` is null), add a `link.xml` anywhere under `Assets/`:

```xml
<linker>
  <assembly fullname="Markdig" preserve="all"/>
  <assembly fullname="Markdig.Signed" preserve="all"/>
</linker>
```
