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

The two-pass logic lives in `Runtime/Core/Lint/DocumentLinter.cs`, not in
`Program.cs`, so it is covered by `PromptUGUI.Tests.EditMode`
(`DocumentLinterTests`). The CLI owns only I/O and the src → path guess.

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
| `PUI-MARGIN-INERT-SIDE`       | A 4-component `margin` slot (order `top,right,bottom,left`) set to a non-zero value on a side the explicit `anchor` doesn't consume — e.g. `anchor="bottom-right" margin="60,_,_,_"` (top=60 is dropped; a `bottom` anchor only reads the `bottom` slot). A stretched axis consumes both its slots; a point anchor only its own side; a centered axis neither. Only the explicit-`anchor` + 4-component-`margin` form is checked (1-/2-component shorthands are symmetric and always land on the consumed side). | **CLI-only**             |
| `PUI-IMAGE-FIT-VARIANT`       | `type="cover"` / `"contain"` appearing in a `type.<variant>` override — a fit mode adds/removes an `AspectRatioFitter` that can't be torn down when the variant turns off (same lifetime issue as `PUI-MASK-VARIANT`). Use a fixed base `type=`, or split per-orientation Screens / `<Add into=...>`. | CLI + runtime warning    |
| `PUI-IMAGE-FIT-GEOMETRY`      | `anchor` / `size` / `width` / `height` / `margin` on a `type="cover"` / `"contain"` `<Image>` — `AspectRatioFitter` sizes the Image to its PARENT, so the Image's own geometry is inert. Put the size on the parent container. | **CLI-only**             |
| `PUI-VARIANT-NO-BASE`         | A control-specific attribute (`direction` / `spacing` / `itemTemplate` / `color` / `sprite` / `tint` / `hidden`, …) carrying a `attr.<variant>` override but **no base `attr=`**. Such setters are *set-only when a variant clears* (`ControlAttributeApplier` does `if (v == null) continue;`), so the value stays stuck after the variant deactivates — e.g. resized portrait→landscape keeps the portrait layout. Add a base value to revert to. **Built-in tags only** (the CLI can't see custom-control setters); self-healing geometry (`anchor` / `size` / `width` / `height` / `margin` / `pivot` / `interactable` / `flow` / `scale`) is exempt, as are a template-body root's invocation-mergeable CommonAttrs (`padding` / `spacing` / `hidden` / …), and `mask` / `type=cover\|contain` (owned by `PUI-MASK-VARIANT` / `PUI-IMAGE-FIT-VARIANT`). | **CLI-only**             |
| `PUI-GRID-CHILD-SIZE`         | `size` / `width` / `height` on a direct child of `<Grid>` — `GridLayoutGroup` gives every child a uniform `cellSize` set on the parent, so the child's own size is silently overridden. Set the cell size on the parent (`<Grid cellSize="WxH">`), or move the element out of the Grid for a non-uniform size. (`<VStack>` / `<HStack>` children are unaffected — there a child's size IS the main-axis size; a `flow="false"` child is exempt — out of flow, its size is meaningful again.) | **CLI-only**             |

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
