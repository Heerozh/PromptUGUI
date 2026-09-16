# `<Scrollbar>` — the scrollbar as a part element

The scrollbar of a scrolling host is a **child element**, not a bundle of `scrollbar*` attributes on
the host. Write it as the direct child of a `<ScrollList>` or a `<Dropdown>`; leave it out and the
host builds the stock bar (the plain Scroll View bar, pixel for pixel).

```xml
<ScrollList columns="4" cellSize="63x86" spacing="4">
  <Scrollbar thickness="6" overlay="true" padding="1.5"
             radius="pill" color="glass-primary-darker/0.6" borderWidth="0.4" borderColor="hud-edge-cyan/0.6"
             handleRadius="pill" handleColor="hud-edge-cyan" handleGlow="3" handleGlowColor="hud-edge-cyan/0.6"/>
  <!-- every other child still goes into Content -->
</ScrollList>

<Dropdown …>
  <Scrollbar thickness="8" sprite="" color="bg-bottom/0.6" handle="" handleColor="ink-dim"/>   <!-- the popup's bar -->
</Dropdown>
```

Two layers, two vocabularies you already know:

- **The track is the bar's primary surface.** `sprite` / `color` skin it; `radius` / `borderWidth` /
  `borderColor` / `glow` / `glowColor` / `innerGlow` / `innerGlowColor` / `intensity` / `glass` (+ its
  parameters) shape it — every one exactly as on `<Frame>`. Writing any shape attribute retires the
  track's Image (sprite cleared, alpha zeroed, still catching the pointer) and draws an SDF face.
- **The handle is an inner layer with the `handle*` prefix**, the way `<Slider>` spells it —
  `handle` / `handleColor` / `handleRadius`, plus `handleBorderWidth` / `handleBorderColor` /
  `handleGlow` / `handleGlowColor`. This is the one inner layer in the library that takes border and
  glow, not just radius: a HUD bar's glowing knob is what the part element exists for. No `glass` on
  the handle (it would sample the same backdrop as the track and vanish).

## Attributes

| attribute | type | default | meaning |
|---|---|---|---|
| `thickness` | float ≥ 0 | `20` | The bar's only dimension: width of a vertical bar, height of a horizontal one. The stock 20 is most of a 66-wide grid column on a 640×360 canvas, so grid lists usually want `6`–`8`. `0` = no visible bar. (Not `width`/`height` — those are common layout attributes and never reach this tag.) |
| `overlay` | bool | `false` | `true` draws the bar **over** the content (`ScrollbarVisibility.AutoHide`); `false` lets the viewport shrink by `thickness + spacing` when the content overflows (`AutoHideAndExpandViewport`). Overlay is how a fixed column count stays fully visible. |
| `spacing` | float | `max(−thickness, −3)` | Distance between bar and viewport (`ScrollRect.*ScrollbarSpacing`): positive separates, negative pushes the bar into the viewport. The default is the stock 3-unit overlap, capped so a thin bar never widens the viewport. **Not read by uGUI when `overlay="true"`** (`PUI-SCROLLBAR-OVERLAY-SPACING`) — keep an overlaid bar off the content with the content's own `padding` (grid: `padding="0,R,0,0"`). |
| `padding` | `P` or `E,S` (≥ 0) | `0` | How far the handle sits inside the track. One value = all four sides; two = **along the bar (the ends), then across it (the sides)** — a vertical bar reads it as `V,H`, a horizontal one transposes. Across-inset makes the handle narrower than the track; along-inset keeps it off the ends. A handle thinner than 1 unit is clamped, with a warning (`PUI-SCROLLBAR-VALUE` statically). |
| `sprite` | sprite key | built-in inset 9-slice | Track bitmap. `""` / `none` = flat colour. |
| `color` | color | `white` | Track colour (token / `/alpha` / gradient); the SDF fill in procedural mode. |
| `radius` `borderWidth` `borderColor` `glow` `glowColor` `innerGlow` `innerGlowColor` `intensity` `glass` `frost` `depth` `dispersion` `lightAngle` `lightIntensity` `saturation` `noise` | as `<Frame>` | — | The track's procedural surface. |
| `handle` | sprite key | built-in round 9-slice | Handle bitmap. `""` / `none` = flat colour. |
| `handleColor` | color | `white` | Handle colour; the SDF fill once the handle is procedural. |
| `handleRadius` | as `radius` | — | Handle corners; `pill` = capsule. |
| `handleBorderWidth` · `handleBorderColor` | as `borderWidth` / `borderColor` (gradients included) | — | Handle inner border. `to right, a, a/0.3, a` = bright ends, dim middle. |
| `handleGlow` · `handleGlowColor` | as `glow` / `glowColor` (gradients included) | — | Handle outer glow. Inflates the drawn quad only; the bar sits beside the Viewport (outside its mask), so nothing clips it. `handleGlowColor` follows the whole `handleColor` ramp when unset. |

Common attributes: `id`, `class`, `if`, `interactable` and every `.variant` suffix are fine.
**Rejected** (`PUI-SCROLLBAR-LAYOUT-ATTR`): `anchor` / `size` / `width` / `height` / `margin` / `pivot` /
`flow` / `scale` / `hidden` — the bar writes its own rect from its orientation and `thickness`, and
`hidden` would fight uGUI's AutoHide (which re-activates the bar every frame); write `thickness="0"`
for "no bar". `<Scrollbar>` takes **no children** (`PUI-SCROLLBAR-CHILD`).

## Where it goes, how it is wired

| host | the bar lives | default bar | orientation |
|---|---|---|---|
| `<ScrollList>` | on the list's root, beside `Viewport` (never in `Content`) | yes | follows `direction`: vertical / grid = a vertical bar on the right edge; `horizontal` = a horizontal bar on the bottom edge |
| `<Dropdown>` | in the popup `Template` (cloned into every opened popup) | yes | always vertical |

- It must be a **direct child** of the host (`PUI-SCROLLBAR-OUTSIDE`) — uGUI only shrinks the viewport
  for a bar that is a direct child of the ScrollRect's transform. Wrapping it in `<Show>` / `<Frame>`
  puts it outside; a template invocation whose body root is a `<Scrollbar>` is fine (see below).
- **One bar per host.** A second one is `PUI-SCROLLBAR-DUPLICATE`; at runtime the first in document
  order is used and the rest are parked inactive.
- The node is named `Scrollbar` (an `id` renames it, as on any control) and is reachable by id path:
  `screen.Get<Scrollbar>("list/bar")`. It is chrome, not a slot — `SlotCount` and `BindItems` ignore it.
- A `direction` variant on the list re-orients the **same node**; the bar's attributes, surfaces and
  subscriptions stay put. A `<Scrollbar>` directly inside a Variant `<Add>` block is not supported
  (`PUI-SCROLLBAR-IN-ADD`) — vary the one bar's attributes instead (`thickness.mobile="4"`).
- Dropdown: TMP clones the popup Template on every Show. Image state comes along on its own; the
  procedural panels are copied onto the clone by the library. Attribute changes while a popup is
  open reach the next open, not the one on screen (same rule as `itemColor` & co.).

## Geometry (what `padding` does)

With thickness `t`, padding `(E, S)` and handle thickness `h = t − 2S`: the bar is `t` across and
anchor-stretched along its host edge; the `Sliding Area` is inset `S` on the sides and `E + h/2` at
the ends; the `Handle` overhangs the Sliding Area by `h/2` at both ends. That last pair is uGUI's
own trick — the handle then reaches the bar's ends at value 0 / 1 and never gets shorter than `h`.
With `padding="0"` this is the stock Scroll View bar, rect for rect.

## Reuse: one bar, many hosts

Nothing new to learn — the three sharing mechanisms already work on it:

```xml
<!-- attributes: one pack, worn by the list and both dropdowns -->
<Style name="scrollbar" thickness="6" radius="pill" color="glass-primary-darker/0.6"
       borderWidth="0.4" borderColor="hud-edge-cyan/0.6"
       handleRadius="pill" handleColor="hud-edge-cyan" handleGlow="3" handleGlowColor="hud-edge-cyan/0.6"
       padding="1.5"/>

<!-- theme: the pixel skin swaps bitmaps in and shapes out — all attributes, class= is enough -->
<Theme name="pixel">
  <Style name="scrollbar" sprite="px:bar" handle="px:knob" radius="0" handleRadius="0"
         borderWidth="0" handleGlow="0" thickness="8" padding="0"/>
</Theme>

<!-- structure + parameters: an ordinary <Template> whose body root is the bar -->
<Template name="HudScrollbar">
  <Param name="accent" default="hud-edge-cyan"/>
  <Scrollbar class="scrollbar" handleColor="{{accent}}" handleGlowColor="{{accent}}/0.6"/>
</Template>

<ScrollList …><HudScrollbar/></ScrollList>
<ScrollList …><HudScrollbar accent="gold"/></ScrollList>
<Dropdown …><Scrollbar class="scrollbar"/></Dropdown>
```

Put the `class=` on the `<Scrollbar>` inside the template body, not on the `<HudScrollbar>` invocation
(`PUI-THEME-STYLE-ON-INVOCATION`). The `handle*` shape attributes count as one surface for the
theme / variant rules: a theme that sets `handleRadius` + `handleGlow` against a theme that sets
neither is fine (the surface toggles wholesale); a theme holding only half the set pins the surface
on and is reported (`PUI-THEME-STYLE-SHAPE`). Likewise `handleGlow.mobile` with no base `handle*`
shape self-heals, but with a base `handleRadius` it sticks (`PUI-VARIANT-NO-BASE`).

## Lint

| code | when |
|---|---|
| `PUI-SCROLLBAR-OUTSIDE` | not a direct child of `<ScrollList>` / `<Dropdown>` |
| `PUI-SCROLLBAR-DUPLICATE` | a second bar under one host |
| `PUI-SCROLLBAR-IN-ADD` | a bar as a direct child of a Variant `<Add>` |
| `PUI-SCROLLBAR-LAYOUT-ATTR` | `anchor` / `size` / `width` / `height` / `margin` / `pivot` / `flow` / `scale` / `hidden` on the bar |
| `PUI-SCROLLBAR-CHILD` | children under the bar |
| `PUI-SCROLLBAR-VALUE` | bad `thickness` / `spacing` / `padding` value; padding that swallows the thickness |
| `PUI-SCROLLBAR-OVERLAY-SPACING` | `overlay="true"` together with `spacing=` |
| `PUI-SCROLLBAR-RETIRED-ATTR` | the old host-side `scrollbar` / `scrollbarColor` / `scrollbarHandle` / `scrollbarHandleColor` / `scrollbarWidth` / `scrollbarOverlay` — on a host or in a `<Style>` pack. No tag takes them any more and the runtime drops unknown attributes silently |
| `PUI-PROC-SPRITE-CONFLICT` | `sprite=` with a track shape, or `handle=` with a handle shape (`""` / `none` do not count) |
| `PUI-PROCEDURAL-VALUE` | bad `handleRadius` / `handleBorderWidth` / `handleGlow` value |
