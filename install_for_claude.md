# PromptUGUI Installation Guide (for AI Assistants)

> This document is written for AI coding assistants (Claude Code / Copilot / Cursor, etc.).
> When the user says "help me install PromptUGUI" or hands you this file, follow the steps below **in order**, verifying each step before moving on to the next.

All paths are relative to the **user's Unity project root** (`<project root>`, i.e. the level where `Packages/` and `Assets/` live).

## Prerequisites

1. A Unity 6000.0+ project
2. `<project root>/Packages/manifest.json` exists
3. R3 (Cysharp) is installed — the PromptUGUI runtime depends on it. It is usually installed as the `R3` package via NuGetForUnity; if it isn't installed, tell the user to install it before continuing.
4. .NET SDK 10 — this is important; it is needed to check lint outside of Unity. If it isn't installed, tell the user to install it before continuing:
    Windows: winget install Microsoft.DotNet.SDK.10
    macOS: brew install --cask dotnet-sdk
    linux: sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0
5. (Optional) Confirm that `xmllint` can be run on the system; if not, remind the user to install it in the final summary.

## Step 1: Add the package to the manifest

Read `<project root>/Packages/manifest.json` and add 2 lines to the `dependencies` object (skip either line if it already exists):

```json
"com.promptugui.core": "https://github.com/heerozh/PromptUGUI.git"
"com.annulusgames.lit-motion": "https://github.com/annulusgames/LitMotion.git?path=src/LitMotion/Assets/LitMotion"
```

Alternatively, have the user go to Unity → Window → Package Manager → "+" → "Add package from git URL", using the same URLs.

**Verification**: Wait for Unity to finish importing (you can call `mcp__UnityMCP__refresh_unity(compile="request", mode="standard", wait_for_ready=true)`), then confirm that the `<project root>/Library/PackageCache/com.promptugui.core@<hash>/` directory exists (the hash is a random value) and contains `Runtime/`, `Editor/`, and `package.json`.

## Step 2: Copy the LLM Skills into the user's project

The package ships four skills for LLMs to read, at these paths:

```
<project root>/Library/PackageCache/com.promptugui.core@<hash>/.claude/skills/authoring-promptugui-xml/SKILL.md      # XML authoring
<project root>/Library/PackageCache/com.promptugui.core@<hash>/.claude/skills/authoring-promptugui-pxl/SKILL.md      # .pxl pixel art (grid-text format for 9-slice borders / button skins / icons)
<project root>/Library/PackageCache/com.promptugui.core@<hash>/.claude/skills/scripting-promptugui-csharp/SKILL.md   # C# bridge (UI.Open / Get<T> / R3 / BindItems / custom Control)
<project root>/Library/PackageCache/com.promptugui.core@<hash>/.claude/skills/using-promptugui-addressables/SKILL.md # Addressables integration (.ui.xml / .po / IconSet)
```

Copy the entire skills directory into the user's project under `.claude/skills/` (**project scope**, tracked with the repo, shared by the team):

**Unix / macOS / WSL:**
```bash
mkdir -p .claude/skills
cp -r Library/PackageCache/com.promptugui.core@<hash>/.claude/skills/* .claude/skills/
```

**Windows PowerShell:**
```powershell
New-Item -ItemType Directory -Force -Path .claude/skills | Out-Null
Copy-Item -Recurse -Force `
  Library/PackageCache/com.promptugui.core@<hash>/.claude/skills/* `
  .claude/skills/
```

If the target already exists, just overwrite it (**idempotent** — when the package is upgraded the skills must be upgraded too, so re-run this step).

> Alternative: if the user wants these skills available across all projects, copy them to `~/.claude/skills/` instead of the project-local `.claude/skills/`. Project scope is the recommended default.

**Verification**: Confirm that the four directories `authoring-promptugui-xml/`, `authoring-promptugui-pxl/`, `scripting-promptugui-csharp/`, and `using-promptugui-addressables/` all exist under `.claude/skills/`, and that line 1 of each directory's `SKILL.md` is `---` (the start of the YAML frontmatter).

## Step 3: Inject project-level CLAUDE.md conventions

In a project that uses PromptUGUI, all player-facing text should be wrapped with `Tr(...)` (an i18n convention; the project wires up its own translation table — the package itself does not enforce it). This convention should be known **by default in all AI sessions**, so write it into the project root's `CLAUDE.md`.

**Actions**:
- If `<project root>/CLAUDE.md` does not exist → create it and write the entire block below.
- If it exists → first grep for `Tr() Wrapping Convention`; if it's already present, skip this step (**idempotent**); if not, **append** it to the end of the file — **do not overwrite** existing content.

Content to append (or write):

```markdown
## i18n: Tr() Wrapping Convention

In this project, every string in C# code that appears on the UI and can be read by the player is wrapped with `UI.Tr(...)` (in the `PromptUGUI.Application` namespace).

**Wrap these**:
- Strings assigned to UI controls in C#: `label.Text = Tr("Start Game")`
- Error dialog messages, tooltip text

**Do not wrap these**:
- No wrapping needed in `.ui.xml`
- `Debug.Log`, exception messages, internal logs
- File paths, URLs, asset keys, format specifiers, JSON / SQL fragments
- `nameof(...)`, reflection identifiers
- Single-character / punctuation-only / numeric-only strings

**Rule of thumb**: if the player can see it on screen → wrap it; if it's only used internally by the engineering → don't wrap it.

We don't need to modify the i18n `po` files; translation is handled automatically by an LLM in CI later.
```

**Verification**: Read `CLAUDE.md` again and confirm it contains the text `Tr() Wrapping Convention` and that the existing content was not lost.

## Step 4: Final acceptance check

Confirm in order:

1. ✓ `Library/PackageCache/com.promptugui.core@<hash>/Runtime/` exists
2. ✓ The four SKILL.md files under `.claude/skills/` — `authoring-promptugui-xml/`, `authoring-promptugui-pxl/`, `scripting-promptugui-csharp/`, `using-promptugui-addressables/` — all exist, with complete frontmatter
3. ✓ `CLAUDE.md` contains the Tr() wrapping convention section, with the original content preserved
4. ✓ Check whether `dotnet --list-sdks` shows a version 10 installed.
5. ✓ (Optional) After `mcp__UnityMCP__refresh_unity(compile="request", mode="standard")`, `mcp__UnityMCP__read_console(action="get", types=["error"])` reports no compile errors
6. ✓ (Optional) Check whether `xmllint` is runnable, and remind the user to install it

Once all pass, installation is complete. In the next session Claude Code will load `CLAUDE.md` automatically; when the user edits `.ui.xml` the `authoring-promptugui-xml` skill triggers, when creating/editing `.pxl` pixel art (9-slice borders, button skins, icons) the `authoring-promptugui-pxl` skill triggers, when writing C# that calls `UI.*` the `scripting-promptugui-csharp` skill triggers, and when integrating Unity Addressables the `using-promptugui-addressables` skill triggers.

## Upgrade / Uninstall

**Upgrade the package** (equivalent to a `git pull` advancing the commit referenced in the manifest): re-run **Step 2** — overwrite `.claude/skills/` with the latest skills from the package. The CLAUDE.md section usually doesn't need to change, unless the release notes mention a convention change.

**Uninstall**:
1. Remove `com.promptugui.core` from `manifest.json`
2. Delete `.claude/skills/authoring-promptugui-xml/`, `.claude/skills/authoring-promptugui-pxl/`, `.claude/skills/scripting-promptugui-csharp/`, and `.claude/skills/using-promptugui-addressables/`
3. Remove the `## i18n: Tr() Wrapping Convention` section from `CLAUDE.md` (if another library in the project shares the same-named convention, keep it)
