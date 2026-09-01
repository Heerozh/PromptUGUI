# UIXmlLint

Standalone `.NET` CLI that lints PromptUGUI `.ui.xml` files **without Unity**.

It compiles the Unity-agnostic Core subset of the PromptUGUI runtime (`Parser` /
`IR` / `Lint` / `Template`) and runs the same rule implementations that
`ScreenInstantiator` invokes at Unity runtime — single source of truth, no
duplicated rule logic. The CLI surfaces every rule violation as an **error**
(non-zero exit code); the Unity runtime path surfaces them as
`Debug.LogWarning` so `UI.Open()` is not interrupted.

## Two passes: raw + expanded

`PromptUGUI.Lint.DocumentLinter` walks each document **twice**, deduplicating
identical findings. Neither view is a superset of the other:

| Pass | Sees | Misses |
|---|---|---|
| **raw** (as written) | subtrees behind `if="false"`, templates nobody invokes | anything decided at expansion time |
| **expanded** (as `ScreenInstantiator` builds it) | a template invocation's real parent/child context, attributes arriving through `class=`, the resolved configuration rules like `GlassRules` need before they will speak at all | code the expander drops |

The expanded pass is why `<VStack class="boxed"/>` now reports
`PUI-CONTAINER-VISUAL-ATTR` when `boxed` supplies a `sprite` — the raw walk
reads `n.Attributes`, and before expansion the sprite lives in a `<Style>` the
node merely references.

Expansion failures (unknown template / style name, Import cycle) surface as
`PUI-EXPAND` instead of waiting until `UI.Open()` throws at runtime.

## `<Import>` resolution

To lint the expanded tree the CLI has to read the imported files. The runtime
maps `src` through a caller-supplied `SourceResolver`, so there is no on-disk
ground truth; the CLI approximates the shipped Resources resolver
(`UseResourcesResolver(root)` → `root/src`) by trying the importing file's own
directory first, then each ancestor up to and including a `Resources/` folder,
with and without a trailing `.xml`.

**An unresolvable `<Import>` is not an error.** A project may serve its commons
from Addressables or a custom resolver with no filesystem shape at all. Such a
document simply skips the expanded pass (a note on stdout says so) and keeps
exactly the pre-existing raw-IR behaviour — failing a today-clean file over an
environment gap would cost more than the missed coverage.

### Where a finding points

Output is `file:line: [CODE] message`, the shape editors and terminals turn into a jump:

```
$ UIXmlLint multi.ui.xml
multi.ui.xml:4: [PUI-MASK-FRAME-SELF] <Frame id='a'>: mask="self" … (via multi.ui.xml:7)
multi.ui.xml:4: [PUI-MASK-FRAME-SELF] <Frame id='b'>: mask="self" … (via multi.ui.xml:8)
```

The primary position is where the markup was **declared** — that is where the edit goes. `(via …)`
is the Template invocation that produced this instance, printed only when a template is involved:
without it, ten invocations of one template produce ten findings that read identically. Nested
invocations record the OUTERMOST site, since inner ones are the same for every instance.

Line numbers come from a `LineInfoXmlDocument` that overrides `CreateElement` while the reader is
still on the element — `XmlDocument` nodes carry no position of their own, and this was cheaper than
porting the parser to `XDocument` for the same information.

### Which file a finding is reported against

Once imports are followed, the node a rule complains about is often **not** in
the file being linted — it was written in a library and inlined here. Every
`ElementNode` therefore carries `OriginSrc` (stamped by
`UIDocumentParser.Parse(xml, src)`, propagated through every expansion copy),
and the CLI reports against it:

```
$ UIXmlLint uses-tmpl.ui.xml
tmpl.ui.xml: [PUI-MASK-FRAME-SELF] <Frame id='card'>: mask="self" requires ...
```

The mistake is in `tmpl.ui.xml`; `uses-tmpl.ui.xml` merely invoked the template.
Findings in the linted file itself are unaffected — they report against it as
before.

Findings are deduplicated **across** entry files, not just within one: linting a directory walks
both a library and the document importing it, and the expanded pass attributes a finding to where it
was written, so the same defect would otherwise print once per entry file that reaches it.

The two-pass logic lives in `Runtime/Core/Lint/DocumentLinter.cs`, not in
`Program.cs`, so it is covered by `PromptUGUI.Tests.EditMode`
(`DocumentLinterTests`, `LintOriginTests`, `LintSourcePositionTests`). The CLI owns only I/O and the
src → path guess.

Some rules are **CLI-only** — they catch author confusion that runtime
silently absorbs (e.g. `sprite=` on `<Frame>` is dropped without a visible
defect). Those rules dispatch from `IRWalker` but not from
`ScreenInstantiator`, so they cost nothing at runtime. Each CLI-only rule
documents the choice in its source file.

## Why this exists

LLMs and humans regularly write structurally-valid but semantically-wrong
PromptUGUI markup — most commonly putting `anchor` or `margin` on a direct
child of `<VStack>` / `<HStack>` / `<Grid>`. Unity logs a warning, but warnings
are easy to miss in test loops or non-MCP environments. This tool turns the
same warning into a hard build failure so the mistake surfaces at write time.

## Usage

From the repo root:

```bash
# Lint a single file
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml

# Lint a directory (recurses for *.ui.xml)
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/

# Multiple paths
dotnet run --project .lint/UIXmlLint -- file1.ui.xml file2.ui.xml dir/
```

Exit codes:

| Code | Meaning                                              |
|------|------------------------------------------------------|
| 0    | All files parsed and passed all rules.               |
| 1    | At least one parse error or rule violation.          |
| 2    | No paths supplied or no `.ui.xml` matched.           |

## Downstream Unity projects

This package is published as UPM; the `.lint/` directory ships with it. From
your Unity project root:

```bash
dotnet run --project Packages/com.heerozh.promptugui/.lint/UIXmlLint -- Assets/UI/
```

You will need a local `dotnet` SDK (matching the `TargetFramework` in
`.lint/Directory.Build.props`). No Unity install required for the tool itself.

## Optional: auto-lint on save (Claude Code hooks)

Add a `PostToolUse` hook to `.claude/settings.json` so Claude Code lints
automatically after writing or editing a `.ui.xml`:

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Edit|Write",
        "hooks": [
          {
            "type": "command",
            "command": "if [[ \"$CLAUDE_TOOL_FILE_PATH\" == *.ui.xml ]]; then dotnet run --project .lint/UIXmlLint -- \"$CLAUDE_TOOL_FILE_PATH\"; fi"
          }
        ]
      }
    ]
  }
}
```

(Hook env var names and exact JSON shape match the version of Claude Code
installed locally — check `/help` if unsure.)

## Current rule coverage

| Code                          | Rule                                                                                                              | Where dispatched         |
|-------------------------------|-------------------------------------------------------------------------------------------------------------------|--------------------------|
| `PUI-LAYOUT-ANCHOR`           | `anchor` on a direct child of `<VStack>` / `<HStack>` / `<Grid>` (LayoutGroup overrides it).                      | CLI + runtime warning    |
| `PUI-LAYOUT-MARGIN`           | `margin` on a direct child of `<VStack>` / `<HStack>` / `<Grid>` (use parent's `padding` / `spacing` instead).    | CLI + runtime warning    |
| `PUI-MASK-FRAME-SELF`         | `mask="self"` on `<Frame>` (Frame has no Image to act as mask source).                                            | CLI + runtime warning    |
| `PUI-MASK-VALUE`              | `mask=` on `<Frame>` / `<Image>` set to anything other than `rect` / `self`.                                      | CLI + runtime warning    |
| `PUI-MASK-PADDING-NO-RECT`    | `maskPadding=` without `mask="rect"` (RectMask2D-only knob).                                                      | CLI + runtime warning    |
| `PUI-MASK-SHOWMASK-NO-SELF`   | `showMask=` without `mask="self"` (stencil-Mask-only knob).                                                       | CLI + runtime warning    |
| `PUI-MASK-VARIANT`            | Variant override on `mask` / `showMask` / `maskPadding` (mode switch requires AddComponent / Destroy; not v1).    | CLI + runtime warning    |
| `PUI-MASK-SELF-NO-SPRITE`     | `mask="self"` on `<Image>` without `sprite=` (stencil mask needs a Graphic source).                               | CLI + runtime warning    |
| `PUI-CONTAINER-VISUAL-ATTR`   | `sprite=` / `color=` on pure containers (`<Frame>` / `<VStack>` / `<HStack>` / `<Grid>` / `<SafeArea>`) — they carry no `Graphic`, so the attribute is silently dropped. Nest an `<Image anchor="stretch" sprite=...>` for backgrounds. | **CLI-only**             |
| `PUI-MARGIN-INERT-SIDE`       | A 4-component `margin` slot (order `top,right,bottom,left`) set to a non-zero value on a side the explicit `anchor` doesn't consume — e.g. `anchor="bottom-right" margin="60,_,_,_"` (top=60 is dropped; a `bottom` anchor only reads the `bottom` slot). A stretched axis consumes both its slots, and so does a fractional one (`width="46%"` / `width="clamp(min, N%, max)"` — both margins inset into the anchor sub-range); a point anchor only its own side; a centered axis neither. Only the explicit-`anchor` + 4-component-`margin` form is checked (1-/2-component shorthands are symmetric and always land on the consumed side). | **CLI-only**             |
| `PUI-IMAGE-FIT-VARIANT`       | `type="cover"` / `"contain"` appearing in a `type.<variant>` override — a fit mode adds/removes an `AspectRatioFitter` that can't be torn down when the variant turns off (same lifetime issue as `PUI-MASK-VARIANT`). Use a fixed base `type=`, or split per-orientation Screens / `<Add into=...>`. | CLI + runtime warning    |
| `PUI-IMAGE-FIT-GEOMETRY`      | `anchor` / `size` / `width` / `height` / `margin` on a `type="cover"` / `"contain"` `<Image>` — `AspectRatioFitter` sizes the Image to its PARENT, so the Image's own geometry is inert. Put the size on the parent container. | **CLI-only**             |
| `PUI-VARIANT-NO-BASE`         | A control-specific attribute (`direction` / `spacing` / `itemTemplate` / `color` / `sprite` / `tint` / `hidden`, …) carrying a `attr.<variant>` override but **no base `attr=`**. Such setters are *set-only when a variant clears* (`ControlAttributeApplier` does `if (v == null) continue;`), so the value stays stuck after the variant deactivates — e.g. resized portrait→landscape keeps the portrait layout. Add a base value to revert to. **Built-in tags only** (the CLI can't see custom-control setters); self-healing geometry (`anchor` / `size` / `width` / `height` / `margin` / `pivot` / `interactable` / `flow` / `scale`) is exempt, as are a template-body root's invocation-mergeable CommonAttrs (`padding` / `spacing` / `hidden` / …), and `mask` / `type=cover\|contain` (owned by `PUI-MASK-VARIANT` / `PUI-IMAGE-FIT-VARIANT`). | **CLI-only**             |
| `PUI-GRID-CHILD-SIZE`         | `size` / `width` / `height` on a direct child of `<Grid>` — `GridLayoutGroup` gives every child a uniform `cellSize` set on the parent, so the child's own size is silently overridden. Set the cell size on the parent (`<Grid cellSize="WxH">`), or move the element out of the Grid for a non-uniform size. (`<VStack>` / `<HStack>` children are unaffected — there a child's size IS the main-axis size; a `flow="false"` child is exempt — out of flow, its size is meaningful again.) | **CLI-only**             |
| `PUI-CLAMP-SCALE`             | `width` / `height` = `clamp(min, N%, max)` (base or any variant) together with `scale=` (base or any variant) on the same node. The clamped axis is owned by the layout pass (`ClampFitter`), which would drop `scale`'s box-preserving inflation. Move `scale` to a child or drop the clamp. Declared, not resolved — the runtime uses the same predicate. | CLI error + **runtime throw** (`ParseException` at `UI.Open`) |

To add a new rule: implement it in `Runtime/Core/Lint/` and dispatch from
`IRWalker`. Also wire it into `ScreenInstantiator.InstantiateRecursive` if and
only if the failure mode is silent visible breakage (mask rules, layout-group
overrides). Pure "author wrote something we ignored" warnings should stay
CLI-only to keep `Debug.Log*` noise out of the editor / Player.

## Scope (what this CLI does NOT do)

- **No cross-file resolution.** `<Import src="..."/>` is not followed; each
  file is parsed in isolation. Violations inside an imported common library
  surface when you lint that common library directly.
- **No Template expansion.** Templates and their invocations are linted
  separately — a `<TitledPanel/>` invocation is treated as an opaque element,
  and the template's body is linted on its own.
- **No Variant resolution.** Variant `<Add>` subtrees ARE walked (so layout
  violations inside them are caught), but `attr.var` overrides are checked
  using the same rule that base attributes use.
| `PUI-HUG-TAG`                 | `width` / `height` = `hug` (or `clamp(min, hug, max)`) on a tag with no content size. Only `<VStack>` / `<HStack>` / `<Grid>` / `<ScrollList>` have one: a `<Frame>`'s children are free-positioned (wrap them in a `<VStack>` and hug that), and a leaf's content size is what `size="native"` means. Seen through `class=` too. | CLI error + **runtime throw** (`ParseException` at `UI.Open`) |
| `PUI-HUG-SCALE`               | A hug axis together with `scale=` on the same node — same last-writer conflict as `PUI-CLAMP-SCALE`. Move `scale` to a child. | CLI error + **runtime throw** |
| `PUI-HUG-STRETCH-CHILD`       | A `stretch` child on the parent's hugged main axis (`<VStack height="hug">` + `<Btn height="stretch">`). The parent sizes itself to its children, so there is no leftover space and the child renders at 0. Give the child a size, or drop the hug. | CLI error + runtime warning |
| `PUI-FLIP-TAG`                | `rotation=` / `flip=` on anything but `<Image>` / `<Icon>` / `<RawImage>` — they rewrite the generated mesh, and those are the tags that generate one. Rotate the inner `<Image>` / `<Icon>` instead of its container. Seen through `class=` too. | CLI + runtime warning |
| `PUI-FLIP-VALUE`              | `flip=` outside `x` / `y` / `xy` / `none`, or `rotation=` that is not a number (write plain clockwise degrees, e.g. `90` / `-45`). | CLI + runtime warning |
| `PUI-FX-TAG`                  | `blur=` on anything but `<Image>` / `<Icon>` — it resamples a sprite's own pixels, and those are the tags that draw one. `<RawImage>` is not wired up for it (its texture is not in an atlas, which the sampling relies on). Seen through `class=` too. | CLI                      |
| `PUI-FX-TYPE`                 | `blur=` / `glow=` on an `<Image>` / `<Icon>` whose `type=` is `sliced` / `tiled` / `filled` — those draw many quads and the effect samples one. Judged after `class=` is merged. | CLI + runtime warning    |
| `PUI-FX-ATTR`                 | `glowColor=` with no `glow=` — nothing is drawn. | **CLI-only**             |
| `PUI-FX-MASK`                 | `blur=` / `glow=` on the same node as `mask="self"` — the stencil is written by this graphic's own fragments, so the glow becomes part of the mask and children show through it. | **CLI-only**             |
| `PUI-FX-RADIUS`               | `blur=` / `glow=` over 12px — past what the sampling kernel covers smoothly, so wider radii band. Not clamped at runtime. | **CLI-only**             |
| `PUI-REVEAL-SINGLE-CHILD`     | `<Animation reveal=…>` with zero or several children — the child is what gets measured and clipped. Wrap the content in a single `<VStack>` / `<Frame>`. | CLI + runtime warning |
| `PUI-REVEAL-SIZE-CONFLICT`    | `height=` / `size=` (or a variant of them) on the axis a `reveal=` already owns — the reveal overwrites it on every pass. Drop it, or set the endpoints with `reveal-from` / `reveal-to`. The cross axis is fine. | CLI + runtime warning |
| `PUI-REVEAL-SCALE`            | `reveal=` together with `scale=` on the same node — the revealed axis is owned by the layout pass. Move `scale` to the revealed child. | CLI error + **runtime throw** |
| `PUI-REVEAL-CHILD-STRETCH`    | The revealed child's `anchor` stretches on the animated axis — the child would follow the box while the box measures the child. Give the child a fixed size on that axis. | CLI + runtime warning |
| `PUI-REVERSE-LOOP`            | `reverse-on=` together with `loop=` — a looping motion has no resting end state to reverse from. | CLI + runtime warning (runtime also throws from `AnimationSpec.Validate`) |
| `PUI-REVERSE-TEXT`            | `reverse-on=` together with `count=` / `char-color=` — a number counting backwards has no stable current value. | CLI + runtime warning (runtime also throws from `AnimationSpec.Validate`) |
| `PUI-REVERSE-ON-TAG`          | `reverse-on=` on anything but `<Animation>` — a `<Trigger>` / `<Show>` has no motion to play backwards. For a second event stream in C#, add another `<Trigger>`. | CLI + runtime warning |
| `PUI-CHECKED-NO-SOURCE`       | A bare (no-`@id`) `checked` / `unchecked` trigger with no `<Toggle>` / `<Tab>` ancestor. They follow a persistent on/off state, which only those two have — a `<Btn>` has none (use `state-pressed` for press feedback). Place it inside one, or use `checked@<id>`. `@id` forms and Template bodies are exempt. | CLI + runtime warning (runtime resolution then hard-throws) |
| `PUI-COLLAPSIBLE-HEIGHT`       | `height=` / `size=` on a `<Collapsible>` — its height IS its header plus its body, and folding it is what changes that. Cap the body with `maxHeight=` (it scrolls past the cap), size the bar with `headerHeight=`; `width=` is ordinary. Base, any variant, or through `class=`. | CLI error + **runtime throw** (`ParseException` at `UI.Open`) |
| `PUI-COLLAPSIBLE-HEADER-FIRST` | `<Header>` is not the first child of its `<Collapsible>` — everything after it is the body, in order. | CLI + runtime warning |
| `PUI-COLLAPSIBLE-HEADER-MULTI` | More than one `<Header>` in one `<Collapsible>`; there is one header bar. | CLI + runtime warning |
| `PUI-COLLAPSIBLE-HEADER-CONFLICT` | A `<Header>` together with a caption attribute (`text` / `icon` / `iconColor` / `font` / `fontSize` / `textColor`) — the slot replaces the built-in caption, so the attribute would never show. `arrow*` still applies: the caret is drawn either way. | CLI + runtime warning |
| `PUI-HEADER-OUTSIDE`           | A `<Header>` anywhere but directly inside a `<Collapsible>` — it is a structural marker, not a control, and will not be instantiated. | CLI + runtime warning |
| `PUI-COLLAPSIBLE-GROUP-MULTI-EXPANDED` | Several members of one `group=` authored open. An accordion shows one at a time: the first in document order wins and the rest open closed. Write `expanded='false'` on those. | CLI + runtime warning |
