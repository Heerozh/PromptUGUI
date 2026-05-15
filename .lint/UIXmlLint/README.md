# UIXmlLint

Standalone `.NET` CLI that lints PromptUGUI `.ui.xml` files **without Unity**.

It compiles the Unity-agnostic Core subset of the PromptUGUI runtime (`Parser` /
`IR` / `Lint`) and runs the same rule implementations that
`ScreenInstantiator` invokes at Unity runtime — single source of truth, no
duplicated rule logic. The CLI surfaces every rule violation as an **error**
(non-zero exit code); the Unity runtime path surfaces them as
`Debug.LogWarning` so `UI.Open()` is not interrupted.

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

| Code                | Rule                                                                                   |
|---------------------|----------------------------------------------------------------------------------------|
| `PUI-LAYOUT-ANCHOR` | `anchor` on a direct child of `<VStack>` / `<HStack>` / `<Grid>` (LayoutGroup overrides it). |
| `PUI-LAYOUT-MARGIN` | `margin` on a direct child of `<VStack>` / `<HStack>` / `<Grid>` (use parent's `padding` / `spacing` instead). |

To add a new rule: implement it in `Runtime/Core/Lint/` so both the runtime
warning path (`ScreenInstantiator`) and the CLI rule walker (`IRWalker`) pick
it up automatically.

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
