# PxlPreview

Standalone `.NET` CLI that renders PromptUGUI `.pxl` pixel-grid files to **PNG**
— without Unity.

`.pxl` is text, and the text *is* the image, but that only goes so far: row
widths and stray characters can be checked by reading, while shading ramps,
bevel direction, outline closure and whether a 9-slice edge strip actually
tiles cannot. This tool closes that gap — render, look, revise. It is
especially the missing feedback loop for an LLM author, which can view the
resulting PNG directly.

It doubles as the `.pxl` counterpart to [UIXmlLint](../UIXmlLint/README.md):
every parse / palette error the Unity importer would raise is reported here
first, with the same message and line number, and a non-zero exit code.

## How it stays honest

The CLI compiles the importer's own sources — `PxlParser`, `PxlFlattener`,
`GplPalette`, `PxlColorResolver` from `Editor/Pxl/` — rather than
reimplementing the format. A file this tool accepts is a file the
ScriptedImporter accepts, the colours are resolved through the same
palette-enforcement path, and layers composite through the same code that bakes
the texture.

Those sources touch exactly two UnityEngine value types (`Color32`,
`Vector4`); `UnityValueShims.cs` supplies them so the files compile verbatim
outside Unity. Everything else — renderer, PNG encoder, 3x5 label font — is
CLI-local and pure BCL, so the tool runs wherever `dotnet` runs (System.Drawing
would have pinned it to Windows).

## Usage

From the repo root:

```bash
# One file (writes to <temp>/pxlpreview, prints the path)
dotnet run --project .lint/PxlPreview -- Runtime/Resources/PromptUGUI/Defaults/pugui.pxl

# Bigger magnification + 9-slice split lines overlaid
dotnet run --project .lint/PxlPreview -- Buttons/ok.pxl --scale 16 --guides

# A whole directory (recurses for *.pxl) into a chosen folder
dotnet run --project .lint/PxlPreview -- Assets/UI/ --out-dir /tmp/pxl-check

# A layered file: every layer separately, plus the composite as text
dotnet run --project .lint/PxlPreview -- Buttons/ok.pxl --layers --emit-flat
```

| Option | Meaning |
|---|---|
| `--scale N` / `-s N` | Pixel magnification, 1..64 (default 8). |
| `--out-dir DIR` / `-o DIR` | Where PNGs are written (default `<temp>/pxlpreview`). |
| `--palette FILE` | Use this `.gpl` instead of searching the project for `@<name>`. |
| `--guides` | Overlay the 9-slice border split lines in magenta. |
| `--layers` | One row per section: every layer bottom-to-top, then the composite. |
| `--emit-flat` | Also print the composite as `.pxl` text on stdout. |

Output: **one PNG per `.pxl`**, named `<basename>.preview.png`, with every
section laid out left-to-right in file order, each on a transparency
checkerboard under a `name WxH bL,B,R,T` label. One image per file is what
makes state comparison (`normal` vs `pressed`) a single glance. The absolute
PNG path is printed to stdout, followed by one metadata line per section.

## Layers

A `.pxl` with `layer:` blocks stores **only** the layers — the composite is
built in memory at import time and written nowhere. That makes these two flags
the only way to inspect such a file:

- `--layers` gives each section its own row: every layer on its own, then the
  composite. A pixel that erases lower layers (a char declared `transparent`)
  is drawn as a magenta block, so an eraser layer can't be mistaken for an
  empty one.
- `--emit-flat` prints the composite as `.pxl`-shaped text, for checking the
  result character by character.

A layer that is entirely `.` is reported as a warning on stderr (it contributes
nothing to the composite) without affecting the exit code.

Exit codes:

| Code | Meaning |
|------|---------|
| 0 | Every file parsed, resolved and rendered. |
| 1 | At least one file failed to parse / resolve / write. |
| 2 | Bad arguments, or no `.pxl` matched. |

> **Never point `--out-dir` at a SpriteSet `sourceFolder`.** The PNGs would be
> ingested as new sprite sources and collide with the `.pxl`'s own keys — the
> same hazard the Inspector's *Export PNG...* warns about. The default temp
> folder is outside any sprite pipeline.

## Palette lookup outside Unity

There is no `AssetDatabase` here, so `palette: @ui` is resolved by scanning for
`ui.gpl` under a project root — the parent of the outermost `Assets` ancestor
if there is one (so embedded `Packages/` are covered), else the enclosing git
repo, else the `.pxl`'s own folder. `Library`, `Temp`, `Logs`, `obj`, `bin` and
`.git` are skipped. Exactly one match is required; zero or several produce the
same error the importer produces. `--palette FILE` bypasses the search.

## Downstream Unity projects

The `.lint/` directory ships with the UPM package:

```bash
dotnet run --project Packages/com.heerozh.promptugui/.lint/PxlPreview -- Assets/UI/
```

You need a local `dotnet` SDK matching `TargetFramework` in
`.lint/Directory.Build.props`. No Unity install required.

## What this tool does NOT do

- **No atlas / SpriteSet resolution.** It renders the `.pxl` in isolation; it
  does not tell you whether the resulting `set:key` is reachable from XML.
- **No PPU / layout simulation.** `ppu:` is parsed (and enforced) but the
  preview is magnified by `--scale`, not by the in-game size.
- **Not a round-trip editor.** Editing the PNG changes nothing; use the
  Inspector's *Export PNG...* / *Sync from PNG...* pair for that.
