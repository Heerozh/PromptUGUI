# UIXmlLint

Standalone `.NET` CLI that lints PromptUGUI `.ui.xml` files **without Unity**.

It compiles the Unity-agnostic Core subset of the PromptUGUI runtime (`Parser` /
`IR` / `Lint`) and runs the same rule implementations that
`ScreenInstantiator` invokes at Unity runtime — single source of truth, no
duplicated rule logic. The CLI surfaces every rule violation as an **error**
(non-zero exit code); the Unity runtime path surfaces them as
`Debug.LogWarning` so `UI.Open()` is not interrupted.

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
