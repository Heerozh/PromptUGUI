# Noise fog — `haze` / `hazeColor` / `hazeDrift`

Load this when a design shows soft, cloud-like light patches on a surface — the "energy haze" that
sits along the bottom edge of a sci-fi HUD button, a nebula glow inside a card, a hollow frame with
a drifting patch of light in it. For the surface's outline see the main doc's **Corner treatments**;
for a lit core with a saturated halo see **Lighting it up** (`intensity`), which stacks with this.

```xml
<!-- a HUD button: dark fill, bright cyan edge, cyan fog seeping in from the bottom -->
<Btn radius="6" color="#0b1a33" borderWidth="1" borderColor="to right, cyan, cyan/0.3, cyan"
     haze="40" hazeColor="to top, cyan/0.8, cyan/0" intensity="1.6">建造</Btn>

<!-- gold variant: the fog concentrated in the bottom third, and flowing -->
<Btn radius="6" color="#1a1408" borderWidth="1" borderColor="to right, #f2c14e, #f2c14e/0.3, #f2c14e"
     haze="32" hazeColor="to top, orange/0.9, 30%, orange/0" hazeDrift="6">造船</Btn>

<!-- a hollow frame with a slow patch of light inside it -->
<Frame borderWidth="1" borderColor="white/0.3" haze="64" hazeColor="#7ec8ff/0.5" hazeDrift="12"/>

<!-- a theme changes the whole family at once -->
<Style name="hud-btn" radius="6" borderWidth="1" haze="40" hazeColor="to top, @accent/0.8, @accent/0"/>
```

Works on every tag that draws procedurally — `<Frame>`, `<Btn>`, `<Tab>`, `<TabMenu>`, `<Toggle>`,
`<Slider>` (the track), `<Dropdown>`, `<InputField>`, `<ScrollList>`, `<Scrollbar>`, `<Collapsible>`,
`<Progress>` — and through `<Style>` / `class=` like every other procedural attribute. Not on the
inner layers (`handle*`, a Progress / Slider fill), not on `<Decor>`, not on `<Image>` / `<Icon>`.

## Attributes

| Attribute | Value | Default | What it does |
|---|---|---|---|
| `haze` | px | `0` (no fog) | The **feature size** of the patches — roughly how wide one blob is. `> 0` switches the fog on; `""` switches it off (a Variant can only override a value, never remove it, so `haze.mobile=""` is the way back). `24`–`64` on buttons and cards |
| `hazeColor` | full colour grammar: token, `/alpha`, `Ndeg` / `to <side>`, 2–4 stops, `N%` positions and hints | `white` | The fog's colour **and its mask** (below). Deliberately *not* the fill: fog in the fill's own colour is invisible on an opaque fill, which is the common case. `/alpha` is the strength knob |
| `hazeDrift` | px/s, `≥ 0` | `0` (still) | Flow speed on an **unscaled** clock. Three layers of the noise move along different directions at 1 / 1.5 / 2× speed, so the pattern *deforms* — the fine detail "boils", the large shapes crawl — rather than sliding across as one sheet |

Bad values (`haze="cloudy"`, `haze="-4"`, `hazeDrift="NaN"`) are parse errors and `PUI-PROCEDURAL-VALUE`
in the CLI, with the same wording as `glow`.

## What the fog is

A low-frequency noise field (three octaves of value noise, smooth, tileable) decides **how much** fog
each pixel gets; `hazeColor` decides what colour it is. The field is mapped so that most of the surface
stays clear: about a quarter of the pixels carry any fog at all, a tenth are at half strength, a few
percent are fully lit — sparse soft patches with a bright core, not a uniform wash. Those constants are
calibrated against the reference art and are not attributes; `/alpha` on `hazeColor` is the one
strength knob, and the ramp (next section) is the shape knob.

This is **not** the glass `noise` parameter: that one is per-pixel grain (frosting, dithering); this one
is patches tens of pixels across.

## The colour is the mask

`hazeColor` is an ordinary colour slot — the same ramp grammar as `color` / `borderColor`, laid on the
same gradient line over the rect — and the noise only scales the ramp's **alpha**. So a directional
fade needs no extra attribute:

| Want | Write |
|---|---|
| fog everywhere, evenly | `hazeColor="cyan/0.6"` |
| seeping in from the bottom edge, gone at the top | `hazeColor="to top, cyan/0.8, cyan/0"` |
| concentrated in the bottom third, thinning out above (a hint bends the fade) | `hazeColor="to top, cyan/0.8, 30%, cyan/0"` |
| a hard cut-off at 60% (a stop position) | `hazeColor="to top, cyan/0.8, cyan/0 60%"` |
| from one corner | `hazeColor="to top right, orange/0.7, orange/0"` |
| two colours in one fog | `hazeColor="to right, cyan/0.7, magenta/0.7"` |

Everything the gradient doc says about direction, stops and hints applies unchanged. A `<Color>`
token works too (`hazeColor="@accent-fog"`).

## Where it is painted

```
fill → [fog] → inner glow → outer glow → border → exposure (intensity) → tint / fade → clip
```

- **Inside the shape only**, following the outline (`radius` / `cut` / `notch` / `pill`). It never
  reaches into an outer `glow` and never inflates the quad.
- **Under the border and the inner glow**: an opaque `borderColor` stays crisp over it; a thin
  translucent one lets the fog run underneath, which is the reference look ("light leaking out
  along the edge").
- **Before `intensity`**: `haze` + `intensity="1.6"`–`3` is neon fog — the brightest patches whiten,
  the rest keep their hue. The fog has no exposure knob of its own.
- **With no fill** it is a patch of light in an empty rect: `<Frame haze="48" hazeColor="cyan/0.5"/>`
  draws, the same way a border-only or inner-glow-only Frame does.
- **`mask="self"`** clips to the shape as always; the fog is inside it anyway.
- **`*Modulate`, CanvasGroup fades, `hoverColor` and friends** treat the fog as part of the surface: it
  darkens, fades and greys with everything else. A **disabled** control greys its fog and **freezes**
  the drift — grey but still flowing would read as alive.

## Position is the seed

The noise is sampled in **canvas space**, not in the panel's own rect. Three consequences, all
deliberate:

- **Two same-styled panels look different.** Twenty `class="hud-btn"` buttons in a column each get
  their own patches, while still sharing **one material** (position is not part of the material key).
  There is no `hazeSeed` to write, and nothing to forget.
- **Neighbouring panels read as one sky.** A HUD assembled from several panels shows different windows
  onto the same field of fog; patches line up across the gaps.
- **A moving panel sees the fog slide across it** — a `<Animation>` that slides a panel in makes the
  clouds pass behind the window rather than travel with it. A 300 ms entrance on still fog is barely
  visible; a panel that keeps moving (a carousel page, a dragged row) will show it. If that matters,
  keep the fog on the parts that stay put, or give the moving one `hazeDrift` so the motion reads as
  flow anyway.

Same position, same clock → same picture, every frame: nothing here depends on the instance or on
the frame count, so screenshots and probe tests are stable.

## Cost

Twelve hashes per pixel, only on surfaces with `haze > 0` (a surface without it takes exactly the path
it always took). Buttons, cards and panels are free; a full-screen fog background costs about as much
as a small-radius blur over the whole screen — for that, use a picture. `hazeDrift` adds nothing per
pixel; the whole project pays one shader global per frame, and only once something actually drifts.

## Not here

- **Glass** (`glass="true"` / a `weld` carrier): the fog is zeroed — a pane has no fill for it to lie
  on — and the CLI says so (`PUI-GLASS-HAZE`). `hazeColor` alone on glass is *not* flagged, so a
  theme can hand it to every surface.
- **Inner layers** (`handleHaze` etc.), `<Decor>`, `<Image>` / `<Icon>`: no fog. A `.pxl` / sprite
  aesthetic paints its haze into the art.
- **A coverage or contrast knob**, a **drift direction**: constants. Stack two Frames for two fogs.

## Lint / errors

| Code | Level | When |
|---|---|---|
| `PUI-PROCEDURAL-VALUE` | error | `haze` / `hazeDrift` not a non-negative finite number; also inside `<Style>` |
| `PUI-GLASS-HAZE` | warning | `haze` on `glass="true"` or a `weld` carrier — raw or arriving through `class=` |
| `PUI-CONTAINER-VISUAL-ATTR` | warning | any of the three on a layout-only container or a tag without a procedural surface |
| colour errors | error | a malformed `hazeColor` ramp — the same messages as every other colour slot |
