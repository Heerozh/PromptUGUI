# `<Decor>` — edge and corner decorations

Load this when you need brackets, indicator ticks, emphasis lines, or ornament art placed on a
host's corners and edges. For the host's own outline (radius / cut / notch / hexagon) see the
`radius` section of the main SKILL; for glass fills see [`glass.md`](glass.md).

`<Decor>` is a **leaf** decoration child. One authored node fans out into **one instance per
position** in `at=`, so all four corner brackets of a card are a single element.

```xml
<!-- selected card: four golden corner brackets, only while selected -->
<Btn class="skin-card">
  <Show on="state-selected">
    <Decor kind="bracket" extent="14" thickness="2" color="@accent" glow="6"/>
  </Show>
</Btn>

<!-- tab indicator triangle, hanging just below the tab -->
<Tab class="nav-tab">Skins
  <Show on="state-selected">
    <Decor kind="tick" at="bottom" extent="12x6" inset="-6" color="@accent"/>
  </Show>
</Tab>

<!-- underline under a panel header -->
<Frame class="panel-header">
  <Decor kind="line" at="bottom" extent="60%" thickness="1" color="@accent/0.6"/>
</Frame>

<!-- texture-themed ornament: draw the top-left corner once, the rest are mirrored -->
<Frame class="panel">
  <Decor kind="sprite" sprite="ui:corner-vine"/>
</Frame>
```

`<Decor>` only **draws and places**. Everything around it is machinery that already exists:

| Want | Use |
|---|---|
| Only while selected / hovered / pressed | wrap it in `<Show on="state-*">` |
| Swap colour / size / kind with the theme | `class=` attribute packs |
| Remove the decoration in one theme | that pack sets `kind="none"` |
| Per-platform difference | `at.mobile="top-left,bottom-right"` etc. |

## Attributes

| Attr | Kinds | Value | Default | Notes |
|---|---|---|---|---|
| `kind` | — | `bracket` / `tick` / `line` / `sprite` / `none` | — (`PUI-DECOR-KIND`) | `none` = every instance hidden; the theme channel for "no decoration here" |
| `at` | all | comma list of `anchor=` words | bracket & sprite: all four corners; tick & line: `bottom` | corners `top-left` `top-right` `bottom-right` `bottom-left`; edges `top` `bottom` `left` `right`. Bracket takes corners, tick / line take edges, sprite takes either |
| `extent` | all | `W` / `WxH`; `P%` (line); `native` (sprite) | bracket `12`, tick `10x6`, line `100%`, sprite `native` | bracket = arm lengths, tick = base × height, line = run along the edge, sprite = drawn size. **Not `size`** — see below |
| `thickness` | bracket, line | px | `2` | stroke width |
| `color` | all | token / hex / CSS name / `/alpha` / comma gradient / `A 70%,B` 色标 / `A,70%,B` 提示 | `white` | drawn by the SDF shader, so gradient stop positions and colour hints apply; on `sprite` it is a plain tint (no gradient) |
| `glow` / `glowColor` | SDF kinds | same as `<Frame>` | `0` / follows fill | inflates the drawn quad only, never the layout |
| `inset` | all | signed px | `0` | positive = inwards from flush, negative = outside the host |
| `offset` | tick, line | signed px | `0` | slides along the edge from its centre |
| `sprite` | sprite | sprite key | — (`PUI-DECOR-SPRITE`) | resolved like every other `sprite=` |
| `mirror` | sprite | `true` / `false` | `true` | automatic reflection / rotation, below |

**`extent`, not `size`.** `size` is a common layout attribute (it sizes the node itself) and is
consumed before any control's own setter sees it. A Decor node always covers its host, so sizing it
would be meaningless anyway — the decoration's own dimension is `extent`.

## Placement

Every instance sits **flush against its corner or edge, on the inside**; `inset` moves it inwards,
a negative `inset` pushes it out past the host's boundary. A `tick` always points **away** from the
host.

Placement is anchored to the host's **rectangle**, not to its drawn silhouette: on a `radius="cut
16"` or `radius="hexagon"` host, decorations still sit on the rect's corners and edges rather than
following the bevel.

**Drawing order is XML order** — uGUI paints by hierarchy, so a `<Decor>` written after the content
draws on top of it. Put brackets last.

## Automatic mirroring (`kind="sprite"`)

Draw **one** piece of art and let the library place the rest:

- **corner art** is drawn for `top-left`; the other three corners are reflections of it
- **edge art** is drawn for `bottom`; `top` is reflected, `left` / `right` are quarter turns

```
  drawn ┐                        ┌ mirrored
        │  ┌─────────────┐  │
           │             │
           │    host     │
           │             │
  mirrored │  └─────────────┘  │ mirrored
```

This is what `<Decor kind="sprite">` gives you over placing four `<Image>` nodes by hand: nothing
else in the control layer can flip a graphic, so four corners would otherwise mean four pre-flipped
assets. Ornament that only reads one way round (a signature, a crest with text) sets
`mirror="false"`.

Pixel-art decorations author well as `.pxl` — see the `authoring-promptugui-pxl` skill, and draw the
canonical corner / bottom edge only.

## What a decoration is not

- **Not in the layout.** A `<Decor>` never takes a slot in a `<VStack>` / `<HStack>` / `<Grid>` and
  never contributes a preferred size, so writing `anchor` / `size` / `width` / `height` / `margin` /
  `flow` on it is reported (`PUI-DECOR-LAYOUT-ATTR`). Placement is `at` / `inset` / `offset`.
- **Not clickable.** Instances never take raycasts; the host keeps every click.
- **Not a surface.** `radius` / `borderWidth` / `glass` / `weld` have nowhere to land — the shape is
  whatever `kind` names (`PUI-CONTAINER-VISUAL-ATTR`). Only `glow` / `glowColor` carry over.
- **Not a mask source.** Decorations are clipped by an ancestor mask, but cannot be one.
- **Not a container.** `<Decor>` is a leaf; child elements are a parse error.

## Lint

| Code | Level | Means |
|---|---|---|
| `PUI-DECOR-KIND` | error | no `kind=` after class merge — the decoration draws nothing |
| `PUI-DECOR-SPRITE` | error | `kind="sprite"` with no `sprite=` — likewise nothing |
| `PUI-DECOR-LAYOUT-ATTR` | error | a layout attribute on a tag that is not in the layout |
| `PUI-DECOR-ATTR` | warning | an attribute this `kind` has no use for (`tick` + `thickness`, `bracket` + `offset`, `sprite` + `glow`, a drawn kind + `mirror`) |
| `PUI-DECOR-VALUE` | error | bad value or an impossible combination (`bracket` on an edge, `%` outside `line`, unknown `kind`) |

## Cost

An unused `<Decor>` tag costs nothing. One that draws costs one GameObject per instance, plus one
node for the sprite layer if the document ever uses `kind="sprite"`. **Instances that share their
parameters share one material and batch into a single draw call** — the four corners of a bracket
are one draw, not four, because the corner an instance sits in rides the mesh rather than the
material. Hidden instances (`kind="none"`, a `<Show>` that is off) emit no geometry.
