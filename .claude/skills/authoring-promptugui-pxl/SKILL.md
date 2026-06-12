---
name: authoring-promptugui-pxl
description: Use when creating or editing PromptUGUI .pxl pixel-grid sprite files — LLM-authored pixel art (9-slice borders, button skins, icons) that imports directly as Unity Sprites. For referencing the resulting sprites from XML see authoring-promptugui-xml.
---

# Authoring PromptUGUI `.pxl` pixel sprites

`.pxl` is a plain-text pixel-grid format: a palette plus character grid, one character per pixel. Drop a `.pxl` file into a SpriteSet `sourceFolder` and Unity's ScriptedImporter turns it into point-filtered Sprite(s) — with 9-slice border and PPU declared in-file — that the existing **Sync Atlases** pipeline packs exactly like PNGs. The result is referenced from `.ui.xml` as `set:key` via `<Icon name=>`, `<Image sprite=>`, `<Btn sprite=>`, etc.

Sweet spot: **UI chrome ≤48×48** — 9-slice frames, button skins, icons, badges, small decorations. NOT for large illustrations; a big grid is unmanageable text and pixel art quality drops with size anyway.

The text IS the image: you can re-read your own output row by row and fix individual pixels. Import errors carry line numbers, so the loop is write → check Console → revise.

## File format

A complete two-state 9-slice button (`Buttons/ok.pxl`):

```
# 12x8 button skin, two states sharing one palette
palette: @ui
ppu: 16
chars:
  K: night
  H: cloud
  M: steel
  S: slate
  D: #3a4466

[normal]
border: 3,3,3,3
grid:
  .KKKKKKKKKK.
  KHHHHHHHHHHK
  KHMMMMMMMMSK
  KHMMMMMMMMSK
  KHMMMMMMMMSK
  KHMMMMMMMMSK
  KSSSSSSSSSSK
  .KKKKKKKKKK.

[pressed]
border: 3,3,3,3
grid:
  .KKKKKKKKKK.
  KSSSSSSSSSSK
  KSDDDDDDDDHK
  KSDDDDDDDDHK
  KSDDDDDDDDHK
  KSDDDDDDDDHK
  KHHHHHHHHHHK
  .KKKKKKKKKK.
```

### Header directives (file-level, shared by all sections)

- `palette: @<name>` — optional. References a project `.gpl` palette by file basename (`@ui` → `ui.gpl`). Name charset: `[A-Za-z0-9_-]+`. Omit it for pure inline-hex mode.
- `ppu: <n>` — optional, pixels-per-unit for the created Sprites; positive number, **default 100**. For pixel art you usually want a small value (e.g. `ppu: 16`) or to rely on PromptUGUI's own scaling on the XML side.
- `chars:` — starts the character→color block. Each entry is one line: `X: value` (single character, colon, space, value). Values:
  - `transparent`
  - a palette color name (only valid in `palette: @...` mode)
  - `#RRGGBB` or `#RRGGBBAA` (exact 6 or 8 hex digits, no spaces)

  `.` is **always transparent and reserved** — you never need to declare it, and redefining it to anything but `transparent` is an error. Duplicate keys are errors. Don't use `[`, `]`, `#`, `.`, or whitespace as chars keys (`#` lines are comments; `[x]`-shaped grid rows would parse as section headers).

### Comments

A line whose **first non-whitespace character is `#`** is a comment, anywhere in the file (header, chars block, between sections, even between grid rows). There are **no trailing comments** — `K: night  # outline` would make the color name literally `night  # outline` and fail. Put comments on their own lines.

### Sections

- `[name]` starts a section; names match `[A-Za-z0-9_-]+`; duplicates are errors. Each section becomes one independent Sprite (sections may have different sizes).
- **Implicit single section**: a file with no `[name]` header at all may put `border:`/`grid:` directly after the header — one anonymous sprite. You **cannot mix** implicit content with explicit `[section]` headers in one file.

### Per-section directives

- `border: L,B,R,T` — optional 9-slice border, four non-negative ints in **Unity sprite-border order: left, bottom, right, top**. Must appear **before** `grid:`. Constraints: `L+R ≤ width`, `B+T ≤ height`. Omit for a non-sliced sprite.
- `tiled: true` — optional render hint (default false). Every consumer (`<Image>`, `<Btn>`, `<Tab>`, default skins, Carousel cards) automatically renders this sprite with `Image.Type.Tiled` — corners stay fixed while edge strips and the center **repeat** instead of stretching. Use it for edges with a directional pattern that must not distort: vines, moss, wood grain, chains. Works with or without `border:` (borderless ⇒ the whole sprite tiles, e.g. a seamless grass fill). An explicit `type=` in the XML still overrides the hint. Must appear before `grid:`; like `border:`, a repeated declaration is last-wins.
- `grid:` — the pixel rows follow, one line per row, one character per pixel, **top-down**. Rules:
  - Every row must have **exactly the same width** as the first row (the #1 authoring error — count characters).
  - Every non-`.` character must already be declared in `chars:`.
  - Leading/trailing whitespace is trimmed, so uniform indentation is fine; spaces cannot be pixels.
  - A **blank line ENDS the grid**. Don't split a grid with an empty line — the rows after the blank become "unrecognized line" errors. One `grid:` per section.

## Sprite keys (how XML refers to the result)

Keys follow the same SpriteSet rules as PNGs (see the **authoring-promptugui-xml** skill, `reference/icons.md`):

| File | Section | Key |
|---|---|---|
| `Buttons/ok.pxl` (implicit single section) | — | `Buttons/ok` |
| `Buttons/ok.pxl` | `[pressed]` | `Buttons/ok/pressed` |
| `Buttons/ok.pxl` | `[ok]` (same as file basename) | `Buttons/ok` (collapses to the plain path key) |

So in XML: `<Btn sprite="ui:Buttons/ok" pressedSprite="ui:Buttons/ok/pressed"/>`. The bare last segment (`pressed`) also works as an alias when it's unambiguous across the whole source folder — same shortcut rule as PNG basenames. Inline TMP sprites (`<sprite name=...>` in text) only ever see **bare names** (the section name, or the file basename for an implicit section).

## Palette workflow (`.gpl`)

Palettes use the **GIMP Palette** format — the community standard: Lospec palettes download as `.gpl`, Aseprite reads/writes it natively. Format: a `GIMP Palette` first line, then `R G B name` entries (name optional).

- `palette: @ui` finds `ui.gpl` by file basename **anywhere in the project**; zero matches or more than one is an import error (the error lists candidates when ambiguous).
- **Palette mode enforces project-wide color consistency**: every `#hex` chars value must exactly match some palette entry's RGB (alpha is free — `#1a1c2c80` is fine if `26 28 44` is on the palette). An off-palette hex fails the import with an "off-palette" error: pick a palette color or add it to the `.gpl`.
- Color **names are normalized** for lookup: case, spaces, hyphens and underscores are ignored (`Dark Blue` ≡ `dark-blue` ≡ `darkblue`).
- **Unnamed palette entries** (lines with only `R G B`) can only be referenced by hex.
- Editing a `.gpl` auto-reimports every `.pxl` that references it — a project-wide recolor is one file edit. Adding, deleting, or moving a `.gpl` also re-triggers `.pxl` imports (so a previously broken "palette not found" file heals itself when the palette appears).

## Pixel-art craft rules

You are drawing, not just encoding. Apply these when composing a grid:

- **1px outline** around the shape, usually the darkest palette color. It's what makes a small sprite read against any background.
- **Limited ramp**: 2–4 shades per material (highlight / base / shadow). More shades at this size = mud.
- **9-slice design**: the **corners carry all the detail** (rounded corners, rivets, notches). The **edge strips between the borders must tile** — keep each edge uniform along its axis (a horizontal edge strip should have identical columns; a vertical strip identical rows). The **center must tile or be flat** — a flat fill is safest and lets the button stretch to any size.
- **Tiled edges** (`tiled: true`): design each edge strip as a seamless **repeating unit** — the pattern must loop cleanly across the strip's own length, and BOTH ends of the strip must return to the plain outline + base fill so the corners and the next repeat join invisibly. A strip that's busy at one end and empty at the other will show a visible seam every tile.
- **Button states**: pressed = swap the highlight and shadow edges (bevel inverts) and/or darken the face; optionally shift the content 1px down. Hover = lighter face. Keep the outline identical across states so the silhouette doesn't jump.
- **Centered glyphs want odd dimensions** (e.g. 9×9, 13×13) so there's a true center pixel.
- **Design at the smallest size that reads**; let PPU / PromptUGUI scaling handle display size. A crisp 12×12 scaled up beats a fuzzy 48×48.
- Use `.` (transparent) for *outside the silhouette* only — don't fake glow/anti-aliasing with semi-transparent pixels inside the shape; pixel art stays hard-edged (the importer is point-filtered for a reason).

## Round-trip with art tools

Selecting a `.pxl` asset in the Project window shows a custom Inspector — not the default ScriptedImporter settings panel. The importer has no editable settings (everything lives in the `.pxl` text), so the panel is read-only: it lists the palette reference, each section's name/dimensions/border, and a small sprite thumbnail per section. Two buttons appear below:

**Export PNG...** — opens a folder picker, then writes one PNG per section into that folder. Naming contract:

- Explicit sections → `<basename>.<section>.png` (e.g. `ok.normal.png`, `ok.pressed.png`)
- Implicit single section → `<basename>.png` (e.g. `ok.png`)

After export, Finder/Explorer opens on the folder. Edit the PNGs in Aseprite or any pixel editor. Aseprite reads `.gpl` palettes natively, so palette-mode round-trips are seamless.

**Do not export into a SpriteSet `sourceFolder`** — the Inspector warns you if you pick one. Exported PNGs would be ingested as new sprite sources, creating duplicate keys and packing conflicts. Use a scratch folder outside the sprite pipeline.

**Sync from PNG...** — opens a folder picker (defaults to the last export folder), then:

1. Matches each `.pxl` section to its PNG by the same naming contract above.
2. Maps PNG pixels back to `chars:` characters: full-transparent pixels → `.`; other pixels matched by RGBA to the existing `chars:` table (earliest declaration wins for duplicate colors); genuinely new colors get a freshly allocated character from the alphabet (`A-Z`, `a-z`, `0-9`, printable ASCII) appended to the `chars:` block.
3. Shows a summary dialog (sections updated, new chars, skipped sections, unmatched PNGs) before writing anything. Cancel keeps the file untouched.
4. Rewrites **only the grid rows** of matched sections in-place, plus any new `chars:` entries. Everything else — `ppu:`, `border:`, `palette:`, comments, unmatched sections, section order — survives unchanged. The `.pxl` remains the single source of truth for all metadata.

**What sync enforces (abort with a dialog, nothing written):**

| Condition | Fix |
|---|---|
| Off-palette color in palette mode (RGB not in the `.gpl`) | Add the color to the `.gpl`, or fix it in the art tool |
| Resize makes the existing `border:` exceed the new size | Edit the `border:` line in `.pxl` first |
| Too many distinct colors — alphabet exhausted | The art is not limited-palette pixel art; quantize first |
| New colors appear but the file has no `chars:` block | Add a `chars:` block (even empty) before syncing |

**Structural edits stay in text.** Adding, removing, or renaming sections is a `.pxl` text edit — sync only updates sections that already exist and have a matching PNG. Unmatched PNGs and sections with no matching PNG are reported in the summary but not auto-created.

**Gotcha — comments between grid rows are lost.** Comment lines sitting *inside* a grid span (between grid rows) fall within the replaced range and are dropped when that section is synced. Comments in the file header, in the `chars:` block, and between sections survive intact. Avoid placing comments inside grids if you plan to sync.



Import errors land in the **Unity Console with line numbers** and fail the asset — no Sprite is produced (and Sync Atlases / `<Icon>` resolution will then miss the key). After writing a file, check the Console; after fixing, the reimport is automatic on save.

| Error | Cause / fix |
|---|---|
| `row width N != first row width M` | Ragged grid row — count characters; every row in a section must be identical length. |
| `unknown grid char 'X' (not in chars:)` | Pixel char not declared — add it to `chars:` (before the grid). |
| `... is not on palette '@name' (off-palette color ...)` | Hex doesn't match any palette RGB — use a palette color or extend the `.gpl`. |
| `color name '...' not found in palette` / `requires a 'palette: @<name>' declaration` | Typo'd name, or used a name without declaring `palette:`. |
| `palette '@name' not found` / `is ambiguous` | No (or multiple) `<name>.gpl` in the project. |
| `border (...) exceeds grid size WxH` | `L+R > width` or `B+T > height` — shrink the border or grow the grid. |
| `unrecognized line '...' (note: a blank line ends a grid: block)` | Usually a blank line inside a grid — grid rows must be contiguous. |
| `cannot mix implicit (headerless) content with [section] headers` | Started drawing before the first `[section]` — add a header to the first sprite too. |
| `'.' is reserved for transparent` / `duplicate chars key` / `duplicate section name` | Self-explanatory — rename. |

**Self-verify before reporting done**: the grid is the image. Re-read your own output row by row — check the outline is closed, edge strips are uniform (9-slice), every row is the same width, and there are no stray pixels. Then confirm the Unity Console is clean and the sprite shows up under the expected `set:key`.
