# Cooldown (`<Cooldown>`)

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md). The `<Cooldown>` attribute table lives in the main doc's `### <Cooldown>` section; read this for the recipes, the sweep geometry, the clock, the `ready` / `cooling` events and the lint rules.

`<Cooldown>` is the radial counterpart of `<Progress>`, with a clock of its own. It draws one of two things:

- a **veil** — the default: a dark sector over a skill icon that the sweep edge uncovers;
- an **arc** — with `thickness`: a ring along the shape's edge, cut to the same sector.

It is a **leaf** and **always click-through**. Put it **beside** what it covers — inside the `<Btn>`, after the icon — never around it.

## Recipes

```xml
<!-- 1. Skill button: icon + veil + countdown; the whole button pulses when it is ready -->
<Animation type="pulse" on="ready" duration="0.3s">
  <Btn id="skill1" size="64x64" radius="10">
    <Icon name="skill:fireball" anchor="stretch"/>
    <Cooldown id="cd" anchor="stretch" radius="10" countdown="true"/>
  </Btn>
</Animation>

<!-- 2. Buff ring: a track, a glowing arc, round ends -->
<Cooldown id="buff" size="48x48" radius="pill" thickness="4" cap="round"
          bgColor="white/0.15" fillColor="accent" glow="6"/>

<!-- 3. A highlight that lights up while the skill is ready (bare `ready` binds to the sibling) -->
<Btn id="skill2" size="64x64" radius="10">
  <Icon name="skill:heal" anchor="stretch"/>
  <Cooldown anchor="stretch" radius="10" countdown="true"/>
  <Show on="ready"><Frame anchor="stretch" radius="10" borderWidth="2" borderColor="accent" glow="6"/></Show>
</Btn>

<!-- 4. A charge-up arc that grows instead of a veil that shrinks -->
<Cooldown size="40x40" radius="pill" thickness="3" draw="elapsed" fillColor="accent"/>

<!-- 5. A "loading" spinner: a fixed quarter arc, rotated forever (rotate is counter-clockwise for positive degrees) -->
<Animation loop="true" easing="linear" rotate="0:-360" duration="1s">
  <Cooldown size="32x32" radius="pill" thickness="3" cap="round" value="0.25" fillColor="accent"/>
</Animation>

<!-- 6. Pixel / bitmap skin: the fill sprite is swept by Image.Filled + Radial360 -->
<Cooldown anchor="stretch" fill="px:veil"/>
```

```csharp
var cd = screen.Get<Cooldown>("cd");
cd.Start(8f);                            // a full 8 s cooldown
cd.Start(total: 8f, remaining: 3.2f);    // reconnect / server sync: pick it up half-way (re-times, not restarts)
cd.Stop();                               // ready now
cd.Value = 0.4f;                         // push a fraction instead (stops the clock)
cd.OnReady.Subscribe(_ => PlayReadySfx()).AddTo(screen);
```

## Sweep geometry

`value` is the **remaining** fraction `r` (1 = just started, 0 = ready). The sweep edge starts at `origin`, travels the way `clockwise` says, and has covered `s = 1 − r` of a turn.

| `draw` | Drawn part (measured from `origin`, the way the edge travels) | Looks like |
|---|---|---|
| `remaining` (default) | from the edge back to `origin` — `[s, 1]` | a veil that shrinks (the classic skill cooldown) |
| `elapsed` | from `origin` to the edge — `[0, s]` | an arc that grows (charge-up, buff timer) |

The defaults (`origin="top" clockwise="true" draw="remaining"`) are the classic veil: the edge leaves 12 o'clock clockwise and the dark part is what is still ahead of it. Nothing drawn = no geometry at all (not even the glow); the whole part drawn = the uncut shape.

## What it draws with

- **A colour fill is always the procedural (SDF) surface** — with or without `radius`. The whole shape is cut by the sector, so a square or rounded-square icon is swept **corners and all**, and the edge is anti-aliased. `fillColor` defaults to `black/0.6`, so a bare `<Cooldown anchor="stretch"/>` is already a usable veil.
- **Procedural attributes land on the fill**, as on `<Progress>`: `radius`, `borderWidth` / `borderColor`, `glow` / `glowColor`, `innerGlow*`, `intensity`, `haze*`. Border and glows follow the sector's cut edges and wrap the arc's ends; the glow escapes the rect.
- **`thickness`** turns the shape into a band that wide along its own edge — a ring on a circle, a rounded-square ring on a rounded square — then cuts it. A colour track (`bgColor`) becomes the same ring, uncut.
- **`cap="round"`** gives a ring half-circle ends. It is exact only on a **circular** ring (`radius="pill"` on a square rect, or a radius reaching half the side); any other shape is drawn with flat ends and warns once (`PUI-CD-ROUND-CAP`). A solid veil has no ends to cap.
- **`bg` / `bgColor`** is the uncut track under the fill. Off until one is written; `bg=""` / `bgColor=""` turns it off again.
- **A bitmap fill** (`fill="ui:veil"`) is swept by Unity's `Image.Filled` + `Radial360` — the bitmap is swept as drawn, so `thickness` / `cap` do nothing there (`PUI-CD-BITMAP-SHAPE`); use a ring sprite for an arc. Writing any procedural attribute with it makes the SDF win (`PUI-PROC-SPRITE-CONFLICT`, `radius` included).
- **No `glass`.** Glass samples the scene behind the canvas, not the icon under the veil; it is refused (`PUI-CD-GLASS`) and the fill is drawn opaque.

## The clock

- `Start(total)` / `Start(total, remaining)` record when the cooldown ends; the control advances itself every frame until it is ready, then sets `value` to 0. Calling `Start` on a running cooldown **re-times** it (no new `cooling` event) — call it on every server sync.
- It is timestamp-based and **keeps running while hidden**: a skill bar on a page a `<TabBar>` or the Router has hidden still goes ready on time and still raises `OnReady`.
- `time="scaled"` (default) follows `Time.timeScale` — a paused game pauses its cooldowns. `time="unscaled"` runs on real time. Switching mid-run keeps the seconds left.
- Writing `value` (XML or C#) stops the clock and shows that fraction. A running cooldown is runtime state: a Variant ReSolve never writes the XML `value` back over it.
- Closing the Screen takes the cooldown off the clock; it never raises `OnReady` afterwards.
- `OnReady` / `OnCooling` are **UI events** (sound, burst, highlight). Whether a skill may be used is your game's own state.
- Cost: a visible running cooldown re-emits four vertices a frame (its canvas re-batches); the material never changes. Put a HUD full of constantly running cooldowns on its own sub-canvas.

## Countdown

`countdown="true"` shows the seconds left in the middle while the clock runs — empty when ready and for a pushed `value` (it has no seconds). Always rounded **up**, so the label reaches 0 exactly when the cooldown does:

| Left | Shows |
|---|---|
| below `tenthsBelow` (default 1 s) | tenths: `0.43` → `0.5` |
| below a minute | whole seconds: `7.2` → `8` |
| a minute or more | `m:ss`: `89.4` → `1:30` |

The text is written only when it changes (once a second; ten times a second in the tenths range). Style it with `font` / `fontSize` (default 24) / `textColor` (default white). For another spelling ("2m"), set `CountdownFormatter` from C#. For a countdown somewhere else, place your own `<Text>` and read `Remaining`.

## `ready` / `cooling` events

| `on=` | Fires when | As a `<Show>` state |
|---|---|---|
| `ready` · `ready@<id>` | the remaining fraction goes from above 0 to 0 | shown while ready |
| `cooling` · `cooling@<id>` | the remaining fraction goes from 0 to above 0 | shown while cooling |

- A persistent pair like `checked` / `unchecked`: `<Animation on="ready" reverse-on="cooling">` is one pair; `reverse-on` takes both.
- Every path counts: the clock running out, `Stop()`, a pushed `value`. A re-time (`Start` while cooling) is not an edge.
- **At open**: an `<Animation>` lands on its end state without playing (so `on="ready"` does not pulse when the screen opens); `<Show>` follows the current state; a plain `<Trigger>` is **not** fired — a "skill ready" sound on its `OnFire` would otherwise play every time the screen opens. `OnFire` and C# `OnReady` therefore report exactly the same flips.
- **Resolution**: a bare `ready` / `cooling` binds to the **nearest** `<Cooldown>` — the trigger's own subtree first, then each enclosing element's whole subtree, stopping at the first level that has any. Exactly one there binds; two or more is an error asking for `@<id>`; none anywhere is an error. So both "Animation around the button" (recipe 1) and "Show beside the Cooldown" (recipe 3) work without an id. `ready@<id>` / `cooling@<id>` use the usual lexical scope (subtree → enclosing template instance → screen).

## Lint rules

| Code | Trigger | Level |
|---|---|---|
| `PUI-CD-VALUE-RANGE` | `value` literal outside `[0..1]` | warning |
| `PUI-CD-ENUM` | `origin` / `draw` / `cap` / `time` not one of its keywords (base or variant) — also a parse error at `UI.Open` | error |
| `PUI-CD-TENTHS` | `tenthsBelow` negative or not a number — also a parse error at `UI.Open` | error |
| `PUI-CD-CHILDREN` | `<Cooldown>` has child elements | error |
| `PUI-CD-ROUND-CAP` | `cap="round"` with no `thickness`, or on a ring that is not a circle (`radius` not `pill`, nor a number reaching half of a square `size`) | warning |
| `PUI-CD-BITMAP-SHAPE` | `thickness` / `cap` with a bitmap `fill=` | warning |
| `PUI-CD-GLASS` | `glass` (directly, via `class=`, or in a variant) | error |
| `PUI-CD-NO-SOURCE` | a bare `ready` / `cooling` with no `<Cooldown>` in reach (`@id` forms, template bodies and screens with `<Add>` blocks are left to the runtime) | error |
| `PUI-CD-AMBIGUOUS` | a bare `ready` / `cooling` whose nearest level holds two or more `<Cooldown>`s | error |
| `PUI-PROC-SPRITE-CONFLICT` | bitmap `fill=` with any procedural attribute, `radius` included | error |
| `PUI-RAYCAST-TAG` | `raycastTarget` on `<Cooldown>` — it is always click-through | error |
| `PUI-FONT-FLOOR` | the countdown (only with `countdown="true"`) below the project's font floor | error |
