# Glass fill (`<Frame glass="true">`)

A glass Frame shows a blurred copy of the camera image inside its shape, with light catching its
edges. It is the same rounded-rect SDF as a normal procedural Frame — `radius`, `borderWidth`,
`glow` all behave identically — only the *fill* changes.

The look it aims at is a thin, flat frosted sheet (Figma-style glass), not a thick liquid lens. The
interior is deliberately flat: refraction and lighting happen only within `depth` pixels of the edge.
That is the whole difference between "thin pane" and "jelly".

```xml
<Style name="glass-card" glass="true" radius="16" frost="0.6"
       color="white/0.06" borderWidth="1" borderColor="white/0.25"/>

<Frame class="glass-card" anchor="top-stretch" height="220" margin="16,16,_,16">
  <Text anchor="center">Inventory</Text>
</Frame>
```

## Attributes

All live on `<Frame>` — **and only there**. A glass attribute on `<Btn>`, `<Toggle>`, `<Image>` or
any other tag is silently dropped (`PUI-CONTAINER-VISUAL-ATTR`); only Frame attaches the panel that
draws them. That is true of `class=` as well: a pack carrying `glass="true"` frosts a `<Frame>` and
does nothing to a `<Btn>` wearing the same class. For a glass-looking control today, put a stretched
glass `<Frame>` **inside** it — a Frame never blocks clicks.

On a Frame they work through `<Style>` / `class=`, Variant suffixes and ReSolve like any other
attribute. Writing any of them without `glass="true"` is a lint error
(`PUI-GLASS-PARAM-NO-GLASS`) — they never reach the shader in that state.

| 属性 | 取值 | 默认 | 说明 |
|---|---|---|---|
| `glass` | `true` / `false` | `false` | Turns the fill into glass. Anything other than `true`/`false` is a parse error |
| `frost` | `0`–`1` | `0.5` | Blur amount. `0` is the lightest frost available, not "clear" |
| `depth` | px | `4` | Thickness: the width of the refracting bevel at the edge. `0` = perfectly flat, no edge refraction and no edge lighting |
| `dispersion` | `0`–`1` | `0` | Chromatic fringing in the bevel. Costs three backdrop samples instead of one — leave at `0` unless you want it |
| `lightAngle` | 度 | `0` | Light direction. `0` = straight up, growing clockwise. Any value, including negatives and >360 |
| `lightIntensity` | `0`–`1` | `0.6` | Edge highlight strength. `0` turns the lighting layer off |
| `saturation` | `≥0` | `1.15` | Backdrop vibrancy. `1` = untouched, `0` = greyscale. This is what makes glass look lit rather than washed out — reach for it before reaching for `dispersion` |
| `noise` | `0`–`1` | `0.02` | Frosted grain. Doubles as dithering against banding on large blurred areas |

Reused unchanged: `color` (tint painted over the glass — comma gradients and `/alpha` work exactly as
elsewhere), `radius`, `borderWidth` / `borderColor`, `glow` / `glowColor`.

Keep the tint alpha low (`white/0.06`, `#39f/0.15`). A tint at high alpha stops reading as glass and
starts reading as a coloured panel with a blur behind it.

## What the glass can see — read this before debugging a "broken" panel

The backdrop is **the capture camera's finished image**: the game world plus every Screen
Space-Camera canvas on that camera. It is captured after post-processing, so glass shows the graded
picture the player sees.

**Overlay canvases are not in it.** uGUI has no grab pass, so a glass panel can never see its own
siblings on the same Overlay canvas.

That gives one rule and one trap:

- **Rule.** Leave the glass Screen on the default Overlay canvas. Put UI that should appear blurred
  *behind* the glass on a `CanvasMode.Camera` Screen. For the common case — a glass resource bar over
  the game world — the defaults are already correct and there is nothing to configure.
- **Trap.** A glass panel on a Camera-mode canvas rendered by the *capture* camera samples a picture
  that already contains itself, and smears over a few frames. The runtime warns once when it sees
  this.

Two glass panels also cannot see each other: both sample the same capture, taken before either drew.
Overlap them and the upper one shows the world, not the panel beneath. Fuse them with `weld` instead.

## Fusing panels: `weld`

A main bar with a smaller secondary block hanging off it looks wrong when a border or a gap divides
them. `weld` merges the shapes with an SDF smooth-min so they read as one continuous pane, and lets
their **thickness** — not a line — say which is primary.

```xml
<Frame weld="10" frost="0.5" lightAngle="-30" anchor="top-stretch" height="104">
  <Frame glass="true" anchor="top-stretch" height="64" radius="0,0,16,16" depth="6"
         color="white/0.06"/>
  <Frame glass="true" anchor="top-right" size="180,40" radius="12" depth="3"
         color="#39f/0.15"/>
</Frame>
```

- `weld` is the fusing radius in px: how far from the junction the two shapes blend together.
- Members are the **direct children** with `glass="true"`, from 2 to 8 of them. A group with fewer
  than two (or more than eight) is a lint error — at runtime the lone child simply draws itself and
  the ninth block onwards stays unfused, so nothing breaks, but a group that fuses nothing is a
  mistake worth naming.
- The welded Frame is a **carrier, not a shape**: `weld` plus `glass="true"` on one node is a lint
  error, and the carrier's own `color` / `radius` / `depth` are ignored — the fused outline comes
  from the children.
- Members stay ordinary nodes throughout: they lay out normally, hold children, and answer
  `Get<T>` as usual. Only their drawing moves to the group.
- A Variant may turn a block's `glass` on or off, or hide it: the group re-fuses in the same pass.

Where each parameter goes:

| 写在 | 参数 |
|---|---|
| 容器（带 `weld` 的 Frame） | `frost` `dispersion` `lightAngle` `lightIntensity` `saturation` `noise`, and `borderWidth` / `borderColor` / `glow` / `glowColor` for the fused outline |
| 每个玻璃子级 | `radius` `depth` `color` |

The split is physical, not arbitrary: two halves of one continuous pane cannot be frosted differently
or lit from different angles, while the thickness step between them is the entire point. A border on
one member would draw precisely the dividing line the weld exists to remove, which is why the outline
is the carrier's. Putting one on the wrong node is a lint error
(`PUI-GLASS-WELD-PARAM-PLACEMENT`) — the attribute is silently ignored at runtime.

## Clipping children to the glass shape

A Frame that draws — glass or plain SDF — can also be a stencil mask, so its children are clipped to
the same rounded shape:

```xml
<Frame glass="true" radius="20" mask="self">
  <Image type="cover" sprite="ui:banner"/>   <!-- corners follow the glass -->
</Frame>
```

The clip follows the **shape**, not the paint: an outer `glow` does not widen it, and it is correct
even before the first backdrop capture. `showMask="false"` keeps the clip and draws nothing.

Two placements do **not** work, both silently, so both are lint errors:

- a Frame with no visual attribute at all has no `Graphic` to mask with (`PUI-MASK-FRAME-SELF`);
- a `weld` carrier draws its fused pane on a `GlassWeld` child and suppresses its own panel while
  welding, so a mask there would clip every child away (`PUI-MASK-WELD-SELF`). Put `mask="self"` on
  a member, or wrap the group in a plain rounded `<Frame mask="self">`.

## Lint codes

The CLI (`dotnet run --project .lint/UIXmlLint -- <path>`) exits non-zero on any of these. Every one
of them is silent at runtime, which is why they exist.

| Code | Means |
|---|---|
| `PUI-GLASS-PARAM-NO-GLASS` | a glass parameter on a node that never enters glass mode |
| `PUI-GLASS-WELD-SELF` | `weld` and `glass="true"` on the same node |
| `PUI-GLASS-WELD-MEMBERS` | a weld group with fewer than 2 or more than 8 glass children |
| `PUI-GLASS-WELD-PARAM-PLACEMENT` | a group-level parameter on a member, or a per-block one on the carrier |
| `PUI-MASK-WELD-SELF` | `mask="self"` on a `weld` carrier — the fused pane is on a child |
| `PUI-PROCEDURAL-VALUE` | a glass value outside its range, or not a finite number |

These read attributes as they will be **after** `class=` is merged, so carrying `glass="true"` in a
`<Style>` works exactly like writing it inline. Where a class names a style the linted file does not
declare — the usual case for an imported skin library — nothing about that node can be proven, so
the structural codes above stay quiet rather than guess.

## When there is no backdrop

Glass degrades to a plain translucent panel — the shape, tint, border and glow still draw, the blur
does not — whenever:

- the project has no URP ≥ 17 (the capture pass does not even compile in);
- URP is installed but is not the active render pipeline;
- **URP is running in Compatibility Mode (Render Graph disabled)** — see below;
- there is no capture camera (no `MainCamera` tag and no `UI.Glass.Camera`);
- the capture camera stopped rendering — disabled for a cutscene, or its GameObject deactivated;
- `UI.Glass.Enabled` is off — the intended way to wire glass to a quality setting;
- you are in the Editor and not in Play mode. Glass does not preview outside play.

Whatever the cause, a backdrop that stops being refreshed is dropped within a frame or two rather
than left on screen as a frozen still — so the fallback is what you see, not the last thing the
camera happened to render.

**Render Graph is required.** The capture is a `ScriptableRenderPass` that only implements
`RecordRenderGraph`; under Compatibility Mode URP calls the deprecated `Execute` path instead, so
the pass is enqueued every frame and never runs. Nothing errors — the glass just quietly stays in
fallback, which is very easy to misread as "the blur is broken". This bites hardest on Unity
6000.0–6000.3, where *Project Settings → Graphics → Render Graph → Compatibility Mode* still exists
and may be on. After ~60 such frames the runtime logs a warning naming this cause.

Quickest way to tell the two apart: `UI.Glass.IsActive` is `false` whenever glass has degraded, for
any of the reasons above.

Nothing throws and nothing needs a fallback authored by hand. Design so the panel still reads at a
low tint alpha and the layout survives without the blur.

## Cost

The capture is one fixed cost per frame — three blits at a quarter resolution on each axis — shared
by every glass panel on screen, and it does not exist at all when no glass panel is visible. Glass
panels of identical style share one material and batch, same as ordinary procedural Frames. Per
panel, `dispersion > 0` is the only setting that meaningfully raises the fragment cost (three
backdrop samples instead of one).
