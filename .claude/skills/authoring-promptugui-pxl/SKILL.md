---
name: authoring-promptugui-pxl
description: Use when creating or editing PromptUGUI .pxl pixel-grid sprite files — LLM-authored pixel art (9-slice borders, button skins, icons) that imports directly as Unity Sprites. For referencing the resulting sprites from XML see authoring-promptugui-xml.
---

# Authoring PromptUGUI `.pxl` pixel sprites

`.pxl` is a plain-text pixel-grid format: a palette plus character grid, one character per pixel. Drop a `.pxl` file into a SpriteSet `sourceFolder` and Unity's ScriptedImporter turns it into point-filtered Sprite(s) — with 9-slice border and PPU declared in-file — that the existing **Sync Atlases** pipeline packs exactly like PNGs. The result is referenced from `.ui.xml` as `set:key` via `<Icon name=>`, `<Image sprite=>`, `<Btn sprite=>`, etc.

Sweet spot: **UI chrome ≤48×48** — 9-slice frames, button skins, icons, badges, small decorations. NOT for large illustrations; a big grid is unmanageable text and pixel art quality drops with size anyway.

The text IS the image: you can re-read your own output row by row and fix individual pixels. Import errors carry line numbers, and the `PxlPreview` CLI renders the grid to a PNG you can actually look at — so the loop is **write → render → look → revise**. Reading characters catches ragged rows; only looking at the render catches "the bevel points the wrong way".

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

  `.` is **always transparent and reserved** — you never need to declare it, and redefining it to anything but `transparent` is an error. Duplicate keys are errors. Don't use `[`, `]`, `#`, `:`, `.`, or whitespace as chars keys (`#` lines are comments; `[x]`-shaped grid rows would parse as section headers; a `:` key would let a grid row parse as a `layer:` header).

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

## Layers (optional)

By default a section's `grid:` *is* the sprite. You can instead build it up in layers — fill, then bevel, then detail — and let the importer composite them:

```
[normal]
border: 3,3,3,3
grid:              # the anonymous BOTTOM layer — its syntax doesn't change at all
  KKKKKK
  KMMMMK
  KMMMMK
  KKKKKK
layer: highlight   # stacked on top
  ......
  .HHH..
  ......
  ......
```

…which composites to:

```
  KKKKKK
  KHHHMK
  KMMMMK
  KKKKKK
```

**Why bother**: editing one layer never disturbs another. Move the outline without rewriting the fill it used to cover; darken the shadow without first working out which cells were shadow. That is the entire benefit — a sprite simple enough to write in one pass (most ≤16×16 icons) is *less* work flat, so don't reach for layers by default. Adding layers to an existing flat sprite is pure appending: keep `grid:` as-is and add `layer:` blocks under it.

For refined, fully-shaded art (~24×24 and up — build-button icons, portraits, decorated props) there is an established five-layer painting workflow — `silhouette → base → aa → shade → highlight`, with per-layer color budgets and a render-and-look check after every layer: see `reference/layered-painting.md`.

### Rules

- **Compositing is pure overwrite.** A non-`.` character on an upper layer replaces the character underneath. There is deliberately **no alpha blending**: it would composite colors that are not on the palette and make the `chars:` table grow without bound.
- **`.` means pass-through**, not transparent — it shows whatever is below.
- **A char declared `transparent` is an ERASER.** `X: transparent` is a non-`.` character, so it *overwrites* lower layers rather than passing through — that is how you punch a hole through everything below. (In a flat single-layer sprite it behaves like `.`, as before.)
- **`grid:`, if present, is the bottom layer and must come before every `layer:`.** A section may equally have only `layer:` blocks and no `grid:`.
- **Declaration order is bottom → top.**
- **Layer names are required**, match `[A-Za-z0-9_-]+`, and must be unique within their section. Different sections may reuse a name (`[normal]` and `[pressed]` can each have a `base`).
- **All layers in a section must be exactly the same size.** There are no offsets — a layer that only touches one corner still spells out the whole grid with `.`.
- `border:` / `tiled:` still come before the section's first pixel block.
- Layers and flat sections mix freely in one file: draw `[normal]` in layers and `[disabled]` flat.

### The composite is never written back to the file

The `.pxl` stores **only** layers; the importer composites in memory. You therefore **cannot read the result out of the file you just wrote** — the two CLI flags below exist for precisely that, and skipping them means shipping art you have never seen:

- `--layers` — renders every layer separately plus the composite
- `--emit-flat` — prints the composite as `.pxl` text, so you can check character by character which column that highlight actually landed on

Two more consequences:

- **`Sync from PNG` cannot touch a layered section.** A flattened PNG cannot be decomposed back into layers, so those sections are reported as `skipped (has layers)` and left untouched (flat sections in the same file still sync). `Export PNG...` still exports the composite normally.
- A layer that is entirely `.` contributes nothing; the CLI warns about it (usually a row of art you forgot to write).

## Sprite keys (how XML refers to the result)

Keys follow the same SpriteSet rules as PNGs (see the **authoring-promptugui-xml** skill, `reference/icons.md`):

| File | Section | Key |
|---|---|---|
| `Buttons/ok.pxl` (implicit single section) | — | `Buttons/ok` |
| `Buttons/ok.pxl` | `[pressed]` | `Buttons/ok/pressed` |
| `Buttons/ok.pxl` | `[ok]` (same as file basename) | `Buttons/ok` (collapses to the plain path key) |

So in XML: `<Btn sprite="ui:Buttons/ok" pressedSprite="ui:Buttons/ok/pressed"/>`.

**Decoration art** (corner ornament, edge filigree) is placed by `<Decor kind="sprite" sprite="ui:..."/>`, which mirrors one drawing into every slot — so draw **only** the canonical piece: the **top-left** corner for corner art, the **bottom** edge for edge art. See `reference/decor.md` in the authoring-promptugui-xml skill. The bare last segment (`pressed`) also works as an alias when it's unambiguous across the whole source folder — same shortcut rule as PNG basenames. Inline TMP sprites (`<sprite name=...>` in text) only ever see **bare names** (the section name, or the file basename for an implicit section).

## Palette workflow (`.gpl`)

Palettes use the **GIMP Palette** format — the community standard: Lospec palettes download as `.gpl`, Aseprite reads/writes it natively. Format: a `GIMP Palette` first line, then `R G B name` entries (name optional).

- `palette: @ui` finds `ui.gpl` by file basename **anywhere in the project**; zero matches or more than one is an import error (the error lists candidates when ambiguous).
- **Palette mode enforces project-wide color consistency**: every `#hex` chars value must exactly match some palette entry's RGB (alpha is free — `#1a1c2c80` is fine if `26 28 44` is on the palette). An off-palette hex fails the import with an "off-palette" error: pick a palette color or add it to the `.gpl`.
- Color **names are normalized** for lookup: case, spaces, hyphens and underscores are ignored (`Dark Blue` ≡ `dark-blue` ≡ `darkblue`).
- **Unnamed palette entries** (lines with only `R G B`) can only be referenced by hex.
- Editing a `.gpl` auto-reimports every `.pxl` that references it — a project-wide recolor is one file edit. Adding, deleting, or moving a `.gpl` also re-triggers `.pxl` imports (so a previously broken "palette not found" file heals itself when the palette appears).

## Pixel-art craft rules

You are drawing, not just encoding. Apply these when composing a grid:

- **1px outline** around the shape, usually the darkest palette color. It's what makes a small sprite read against any background. In refined layered art, additionally swap the outline pixels on the light-source side to a highlight color (**selective outlining / "selout"**, done in a top `highlight` layer) — a lit top edge is what lifts an icon off a dark button face. Recolor boundary pixels only; never move them.
- **Limited ramp**: 2–4 shades per material (highlight / base / shadow) for simple flat sprites. Refined layered art budgets differently — ≤5 *new* colors per layer, reuse-first, ~12–16 total at 32×32 (see `reference/layered-painting.md`).
- **9-slice design**: the **corners carry all the detail** (rounded corners, rivets, notches). The **edge strips between the borders must tile** — keep each edge uniform along its axis (a horizontal edge strip should have identical columns; a vertical strip identical rows). The **center must tile or be flat** — a flat fill is safest and lets the button stretch to any size.
- **Tiled edges** (`tiled: true`): design each edge strip as a seamless **repeating unit** — the pattern must loop cleanly across the strip's own length, and BOTH ends of the strip must return to the plain outline + base fill so the corners and the next repeat join invisibly. A strip that's busy at one end and empty at the other will show a visible seam every tile.
- **Button states**: pressed = swap the highlight and shadow edges (bevel inverts) and/or darken the face; optionally shift the content 1px down. Hover = lighter face. Keep the outline identical across states so the silhouette doesn't jump.
- **Do not paint a reflection into the sprite.** The XML side generates one — a second node with `flip="y"` and a stop gradient that fades to nothing — so a baked-in reflection would double up, and it would keep its own fade when the UI puts the icon somewhere with no floor. See the XML skill's *Reflection recipe*.
- **Do not paint a glow or a blur into the sprite either.** `glow=` / `blur=` on `<Image>` / `<Icon>` generate them at runtime, in whatever colour and radius the screen asks for — a baked halo would double up, and it inflates the sprite's bounds so `size="native"` and pixel snapping both go wrong. See **Blur & glow** in the XML skill.
- **Centered glyphs want odd dimensions** (e.g. 9×9, 13×13) so there's a true center pixel.
- **Design at the smallest size that reads**; let PPU / PromptUGUI scaling handle display size. A crisp 12×12 scaled up beats a fuzzy 48×48.
- Use `.` (transparent) for *outside the silhouette* only — don't fake glow/anti-aliasing with semi-transparent pixels inside the shape; pixel art stays hard-edged (the importer is point-filtered for a reason). Anti-aliasing with *opaque* mid-tone colors on **internal** color boundaries is fine (and expected in refined art) — but never anti-alias the outer edge against transparency: the runtime background is unknown, and those pixels become dirty fringe on any other background.

## Look at what you drew (`PxlPreview` CLI)

Run this after writing or editing any `.pxl`, before reporting done. It needs no Unity instance and no Unity install — just a `dotnet` SDK:

```bash
dotnet run --project .lint/PxlPreview -- Buttons/ok.pxl            # -> <temp>/pxlpreview/ok.preview.png
dotnet run --project .lint/PxlPreview -- Buttons/ok.pxl --scale 16 --guides
dotnet run --project .lint/PxlPreview -- Assets/UI/ --out-dir /tmp/pxl-check   # whole tree
```

(Paths are relative to the PromptUGUI repo root. From a Unity project that consumes the UPM package, the project is at `Packages/com.heerozh.promptugui/.lint/PxlPreview`.)

It prints the PNG's absolute path — **then read that PNG as an image**. That is the point of the tool: judging shading ramps, bevel direction, outline closure, silhouette consistency across states, or whether an edge strip really tiles is impossible from the character grid, and those are exactly the mistakes that survive a text re-read.

One PNG per `.pxl`: every section side by side in file order, on a transparency checkerboard, labelled `name WxH bL,B,R,T`. So `normal` vs `pressed` is one glance — check the outline is identical and only the bevel/face changed.

| Option | Use it for |
|---|---|
| `--scale N` (1..64, default 8) | Small glyphs need 16+; a 48×48 frame reads fine at 6. |
| `--guides` | Overlays the 9-slice split lines — the fastest way to confirm the corners fall *inside* the border box and the edge strips are uniform between the lines. |
| `--layers` | One row per section: every layer bottom-to-top, then the composite. **Mandatory for any file with `layer:` blocks** — the composite exists nowhere else. Eraser pixels (a `transparent` char) show as magenta so an eraser layer can't be mistaken for an empty one. |
| `--emit-flat` | Also prints the composite as `.pxl` text on stdout. The render answers "does it look right"; this answers "which column did that pixel land on". |
| `--out-dir DIR` | Default is a temp folder. **Never aim it at a SpriteSet `sourceFolder`** — the PNGs would be ingested as sprite sources and collide with the `.pxl`'s own keys. |
| `--palette FILE` | Skip the `@name` project search and name the `.gpl` directly. |

It is also the `.pxl` linter: it compiles the importer's own parser / palette / colour-resolution sources, so every error below surfaces here first, with the same message and line number, and exit code 1 — no Unity Console round trip. Details: `.lint/PxlPreview/README.md`.

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
| `grid: must come before any layer: block` | `grid:` is the bottom layer — move it above the `layer:` blocks (or rename it to a `layer:` of its own). |
| `layer 'X' has N rows but grid: has M` | Layers in one section must all be the same size — pad the short layer with `.` rows. |
| `layer name '...' must be non-empty and match [A-Za-z0-9_-]` | `layer:` needs a name; no spaces or punctuation. |
| `duplicate layer name 'X' in section` | Layer names are unique per section (reuse across sections is fine). |
| `':' cannot be a chars key` | Pick another character — `:` is reserved so grid rows can't be mistaken for `layer:` headers. |
| `section '[x]' has no grid: or layer: block` | The section declares metadata but no pixels. |

**Self-verify before reporting done**, in this order:

1. **Re-read the grid** row by row — same width everywhere, outline closed, no stray pixels, edge strips uniform along their axis. For a layered file this only checks each layer in isolation; the composite lives nowhere in the text, so add `--emit-flat` and read that too.
2. **Render it and look at it** — `dotnet run --project .lint/PxlPreview -- <file>.pxl --scale 16 --guides`, then read the PNG it prints. Add `--layers` for any file with `layer:` blocks. A clean parse says nothing about whether the art reads; step 1 cannot catch an inverted bevel, a muddy ramp, or corners bleeding past the 9-slice split. Do not claim a sprite looks right without having looked at it.
3. **Confirm in Unity** — Console clean, and the sprite shows up under the expected `set:key`.
