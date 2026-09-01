# State visuals (Btn / Tab / Toggle)

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md). Read this before using any `*Color` / `*Modulate` / `selectedColor` / `<Show on="state-*">` / `pressedSprite` / `disabledSprite` / `selectedSprite` / `pressedOffset` / `selectedOffset`. For the `state-*` `on=` event values and `<Trigger>` / `<Animation>`, see [`animations.md`](animations.md).

`<Btn>`, `<Tab>`, and `<Toggle>` all broadcast their uGUI interaction state. `<Btn>` emits `Normal` / `Hover` / `Pressed` / `Disabled` (Selectable's `Selected` is folded into `Normal`). `<Tab>` and `<Toggle>` also emit `Selected` (= the active/`isOn` control at rest; transient Hover/Pressed/Disabled override it and it reverts on release). Three ways to react, in increasing power:

## 1. State colour — two families

**Absolute `*Color`** (`hoverColor` / `pressedColor` / `selectedColor` / `disabledColor`) sets the per-state colour of the control's base graphic (`targetGraphic` / bg) **only** — the same graphic that `color=` targets. It does **not** fan out to descendants. Normal has no `*Color` (use `color=`). Accepts the same value forms as `color` (hex / CSS named / theme token), **including gradients** (`hoverColor="#fff,#aaa"`). A state transition into or out of a gradient **snaps** (no ~0.1s fade); solid ↔ solid transitions still fade **after the first frame** (the state a control is *first shown in* always snaps — see [First-frame establishment](#first-frame-establishment) below). A gradient **stop position** or **hint** (`hoverColor="#fff 70%,#aaa"`) renders whether or not the control declares a procedural shape — the Image-backed surface gets it by having its mesh cut at the stop. See [Color Tokens → Gradients](../SKILL.md#gradients) for the full grammar.

**Relative `*Modulate`** (`hoverModulate` / `pressedModulate` / `selectedModulate` / `disabledModulate`) is a **relative multiplier** (Godot `modulate` semantics: white = identity, the normal uGUI ColorTint model). It fans out to the bg **and every descendant Graphic** (label, icons, nested images), switching the control off uGUI's built-in ColorTint, and the tint **fades** over ~0.1s (after the first frame; see [First-frame establishment](#first-frame-establishment)). `*Modulate` is **solid-only** — a gradient value is a parse error (lint `PUI-GRADIENT-MODULATE`); use `*Color` when you need a per-state gradient.

They compose: per state, `displayed = (absolute ?? color) × (modulate ?? white)`.

### First-frame establishment

The state a control is **first shown in** is applied **instantly**, never faded — mirroring uGUI's instant-at-`OnEnable` for serialized state. So a control that opens already in a non-Normal state shows it on frame 1, with no flash of the enabled look first: a `<Tab>` / `<Toggle>` authored `isOn`, or a `<Btn>` that a modal `Configure` hook disables right after `Open` (`okBtn.Interactable = false`). Only state *changes* made after the control is already on-screen — hover, press, a runtime `interactable` flip in response to a click — fade over ~0.1s.

`selectedColor` / `selectedModulate` are meaningful only on `<Tab>` / `<Toggle>`. `selectedColor` is the **selection-aware base** — the bg base colour while the control is the active/`isOn` one; hover/pressed/disabled compose on top (so a selected control with no `hoverColor` stays at `selectedColor`). A `<Btn>` has no selected state and ignores them.

```xml
<!-- relative multiplier — whole subtree dims on press (same fan-out multiplier the old pressedColor had) -->
<Btn color="primary" pressedModulate="#cccccc" disabledModulate="#888888">
  <Text anchor="center">Buy</Text>
</Btn>

<!-- absolute only — selected Tab bg flips to a brighter palette token -->
<Tab color="primary-darker" selectedColor="primary"/>

<!-- both — absolute sets the base, modulate dims the whole subtree while held -->
<Tab color="primary-darker" selectedColor="primary" pressedModulate="#cccccc"/>
```

- Distinct from `tint` (which picks the multiply-vs-linear-light **material**) and `color` (the base bg colour). All three compose.
- `interactable="false"` on the Btn also sets `Button.interactable=false`, so it enters the Disabled state — `disabledColor` / `disabledModulate` apply and `state-disabled` fires (on top of the existing CanvasGroup raycast block; the two compose).
- Variant-overridable like any `[UIAttr]` colour (the reactor re-resolves on ReSolve; the captured base colour is never re-captured).
- For per-state **absolute** recolouring of multiple graphics (not just the bg), use `<Show>` instead — `*Color` is bg-only by design (painting the label the same colour as bg makes it invisible).

### Default disabled appearance (grayscale)

When a `<Btn>` / `<Tab>` / `<Toggle>` enters the Disabled state and **no** `disabledColor`, `disabledModulate`, or `disabledSprite` (Btn only) is authored, the entire control — background sprite, labels, icons — is desaturated to **true grayscale** automatically. This is a shader-based luminance desaturation (not a colour multiply), so it produces a neutral grey regardless of the original colour. Authors write nothing on child nodes; the effect fans out to the whole control subtree with the same pruning as `*Modulate`: `stateReact="false"` subtrees are skipped, and nested `<Btn>` / `<Tab>` / `<Toggle>` controls are pruned (they manage their own disabled appearance).

**Overriding the default.** Writing any of the following replaces the grayscale default with the authored path instead:

- `disabledColor="…"` — sets an absolute bg colour for the Disabled state (bg only, no fan-out).
- `disabledModulate="…"` — applies a relative colour multiplier fanned out to the whole subtree (the normal `*Modulate` path).
- `disabledSprite="…"` — swaps the bg `overrideSprite` while disabled (`<Btn>` only).

**Opting out entirely.** `disabledModulate="none"` disables the default grayscale **and** bypasses the colour multiplier path — the control shows no disabled visual at all.

**Hover and press are unaffected.** The grayscale controller only activates on the Disabled state. uGUI's built-in ColorTint (hover darkening, pressed darkening) continues to run on top when the control is enabled; it does not interact with the grayscale effect.

**Opting out of `*Modulate` fan-out — `stateReact="false"`**: a **common attribute** (any element, default `true`) that opts a node **and its whole subtree** out of an ancestor Btn's `*Modulate` fan-out. Has no effect on `*Color` (absolute — never fanned out). The installer prunes that subtree, so those graphics keep their authored colour through hover / press / disable. (A nested `<Btn>` is auto-pruned — it owns its own graphics.)

```xml
<Btn pressedModulate="#aaaaaa">
  <Image sprite="ui:badge" stateReact="false"/>  <!-- stays full-colour on press -->
  <Text anchor="center">Claim</Text>             <!-- modulate-dimmed with the Btn -->
</Btn>
```

## 2. Artwork swap — `<Show on="state-...">`

`<Show>` is a no-visual wrapper whose subtree is visible **only while** the nearest ancestor `<Btn>` / `<Tab>` / `<Toggle>` is in that state (hidden otherwise, via `SetActive` — never destroyed). Sibling `<Show>` blocks under one source control are mutually exclusive; an unclaimed state falls back to the `state-normal` block. Only `state-*` `on=` values are valid (any other, e.g. `on="click"`, is an error). Wrap two `<Image>` siblings to swap artwork per state:

```xml
<Btn id="play">
  <Show on="state-normal"><Image anchor="stretch" sprite="ui:play-normal"/></Show>
  <Show on="state-pressed"><Image anchor="stretch" sprite="ui:play-pressed"/></Show>
  <Text anchor="center">Play</Text>
</Btn>
```

Here PC `state-hover` has no explicit block, so the `state-normal` artwork covers it too; add a `<Show on="state-hover">` to give hover its own art.

**Single-bg shorthand — `pressedSprite` / `disabledSprite`.** When the only per-state change is the button's own bg image, `<Btn pressedSprite="ui:play-pressed">` is the one-attribute form of a `state-normal`/`state-pressed` `<Show>` pair: it swaps the bg's `overrideSprite` while Pressed and reverts on release (the authored `sprite` is never touched). Setting it auto-switches the Btn off uGUI's built-in ColorTint (so the pressed art isn't additionally darkened), and it composes with `pressedColor` / `pressedModulate` (swap + tint/modulate stack). `""` / `none` = no swap. For swapping whole child subtrees (icon + label together, or more than two states), use `<Show>` instead. `<Tab selectedSprite>` is the same `overrideSprite`-swap mechanism keyed on `isOn` (the selected look) instead of `Pressed`.

`<Btn disabledSprite="ui:play-disabled">` is the identical mechanism for the **Disabled** state (greyed-out art instead of just a darkened tint). Same contract as `pressedSprite` — swaps `overrideSprite` while disabled (`interactable="false"`, or runtime `Btn.Interactable=false`), reverts when re-enabled, takes the Btn off ColorTint, `""` / `none` = no swap. Disabled and Pressed are mutually exclusive states, so the two attributes coexist (Disabled wins). Only `<Btn>` has `disabledSprite` (like `pressedSprite`); for Tab/Toggle use `disabledColor` / `disabledModulate` or a `<Show on="state-disabled">`.

```xml
<Btn sprite="ui:play-normal" pressedSprite="ui:play-pressed" disabledSprite="ui:play-disabled">Play</Btn>
```

**Default pressed fallback.** A `<Btn>` that carries no author-supplied `sprite=` and no explicit `pressedSprite=` automatically receives the built-in pressed 9-slice (`pugui_9slice_pressed`) as a fallback pressed skin. The fallback is a visual courtesy — it does **not** switch the Btn off uGUI's built-in ColorTint; `pressedColor` / `pressedModulate` compose on top of it as usual (unlike an explicit `pressedSprite=` or `disabledSprite=`, which flip the Btn off ColorTint to avoid double-darkening). The fallback yields as soon as any of the following is true:

- The author writes a custom `sprite=` (the default bg skin is overridden, so the fallback pressed skin no longer pairs with it).
- The author writes an explicit `pressedSprite=` with any value, including `pressedSprite=""` or `pressedSprite="none"` (opt-out).

Note: an authored `disabledSprite=` also switches the Btn off ColorTint (same `OnAfterApply` rule), but it does **not** suppress the pressed fallback — a Btn with only `disabledSprite=` still swaps to the built-in pressed skin while pressed; write `pressedSprite=""` to opt out.

In short: write `pressedSprite=""` or `pressedSprite="none"` to explicitly disable the built-in fallback and keep the default skin unchanged on press.

**On a procedural surface, `*Color` and `*Modulate` land in two different places.** A panel keeps its
authored look in its **material** and treats `Graphic.color` as a **multiplier** (`col *= IN.color`)
— the split that lets every panel sharing a style share one material and keep batching. So:

- `*Color` (absolute) drives the panel's **fill**, and stays genuinely absolute. On glass this moves
  the pane's own tint, which is what "hover changes the colour" has to mean there.
- `*Modulate` (relative) stays on `Graphic.color`, exactly as it does on an Image.

One consequence worth knowing: **an absolute change snaps instead of fading** on a procedural
surface. The fill is a material parameter, and tweening it per frame would mint a material per frame
through the shared cache; state changes are discrete, so the cache sees one entry per state instead.
`*Modulate` still fades — it is pure vertex colour. If you want a fading hover on a glass control,
reach for `hoverModulate` rather than `hoverColor`.

**State SPRITES are not available on a procedural surface.** All three are `Image.overrideSprite` swaps, and a control
drawing procedurally (`<Btn radius=…>` / `glass=…`, see the main SKILL) has no Image showing — the
combination is a contradiction and reports `PUI-PROC-STATE-SPRITE-CONFLICT`. Use `pressedColor` /
`disabledColor` / `selectedColor`, their `*Modulate` counterparts, or `<Show on="state-*">`, all of
which work unchanged there because the state reactors drive `targetGraphic`, which follows the
surface. Automatic disabled greying works too: a procedural surface desaturates itself from the
inside (and glass also thins) rather than by a material swap, so the shape survives.

**Reversible.** The ColorTint switch is *computed* from what is currently in play, not latched: clearing `pressedSprite` / `disabledSprite` / `selectedSprite` (a Variant flip, a theme switch, a runtime assignment) hands interaction feedback back to uGUI's ColorTint — unless a `*Color` / `*Modulate` / `selectedColor` is still installed, in which case the reactors keep ownership and ColorTint stays off. Without this the control would be left with **no** feedback at all and no attribute the author could write to get it back.

**The base colour is reversible too.** A control that declares any `*Color` / `*Modulate` / `selectedColor` hands its bg to a state reactor, and the reactor's *base* — what it paints at rest — is read from the control's live `color=` declaration on every apply. So `color.mobile=` and a theme's `<Style color=>` reach a `<Btn hoverColor=>` / `<Tab selectedColor=>` / `<Toggle>` exactly like they reach a plain `<Image>`. What the reactor never does is read the base back off the graphic: mid-hover the graphic is showing the *tint*, and adopting that would bake the hover colour in permanently. A control that declares no `color=` at all keeps its built-in bg colour as the base.

## 3. State-triggered animation — `<Trigger>` / `<Animation on="state-...">`

`state-normal` / `state-hover` / `state-pressed` / `state-selected` / `state-disabled` (each also `@<id>`) are `on=` values on `<Trigger>` / `<Animation>` / `<Show>` — see the `on=` table in [`animations.md`](animations.md). Source resolution is **upward** to the nearest `<Btn>` / `<Tab>` / `<Toggle>` ancestor (opposite of `click` / `press`), and they fire **on entering** the state. `state-selected` is meaningful only with a `<Tab>` / `<Toggle>` source. Pair a press animation with its revert:

```xml
<Btn>
  <Animation scale="1:0.95" duration="0.08s" on="state-pressed"><Frame anchor="stretch"/></Animation>
  <Animation scale="0.95:1" duration="0.08s" on="state-normal"><Frame anchor="stretch"/></Animation>
  <Text anchor="center">Tap</Text>
</Btn>
```

## 4. Press offset — `pressedOffset` / `selectedOffset`

A **tactile / physical-button** effect: while Pressed, the control's child content (label, icons, nested content) shifts by a fixed pixel offset — the background frame (the control's own bg `Image`) stays put — giving a "depressed into the button" feel. Shared by `<Btn>` / `<Tab>` / `<Toggle>`.

| Attribute | Controls | Meaning |
| --- | --- | --- |
| `pressedOffset="x,y"` | Btn / Tab / Toggle | Content offset while **Pressed**. |
| `selectedOffset="x,y"` | Tab / Toggle | Content offset held while **Selected** (`isOn`). `<Btn>` has no selected state and does not have this attribute. |

```xml
<Btn pressedOffset="0,-4">Buy</Btn>                    <!-- press: content sinks 4px -->
<Tab pressedOffset="0,-2" selectedOffset="0,-3"/>      <!-- press sinks 2px; stays sunk 3px while selected -->
```

- **Sign is Unity's: negative `y` = down**, positive `x` = right (same convention as `<Animation translate>`). So "sink 4px" is `0,-4`, not `0,4`. This is the most common foot-gun.
- **Instant, never tweened.** The offset snaps on state enter and snaps back on release (physical-button semantics + pixel-art-friendly; authored offsets are integer pixels). This deliberately differs from `*Color`'s ~0.1s fade.
- `""` / `none` = no offset for that state.
- **First-frame:** a `<Tab>` / `<Toggle>` authored `isOn` shows its `selectedOffset` on frame 1 (no animate-in).
- **Selected being pressed:** pressing an already-selected `<Tab>` / `<Toggle>` shows `pressedOffset` while held, then reverts to `selectedOffset` on release. If you set `selectedOffset` but **not** `pressedOffset`, pressing a selected control momentarily pops the content back to zero — set both (often the same value) to avoid the pop.
- **Composition:** independent of `<Animation translate>` (which moves its own proxy — the two stack), of `*Color` / `*Modulate` / `pressedSprite` (different channels), and of `tint`. All compose.
- `stateReact="false"` does **not** exempt a child from the offset (it only governs `*Modulate` colour fan-out). The holder is a rigid translate — all content moves together.
- Variant-overridable like any `[UIAttr]` (`pressedOffset.dark="0,-8"`).
- **Disabled** has no offset (content rests at zero).

Implementation: the control lazily wraps its content in a full-stretch holder (only when an offset is authored) and a `PressOffsetController` drives that holder's `anchoredPosition` from the control's `OnState` stream — same broadcaster the `*Color` family uses.

## 5. Persistent state — `checked` / `unchecked`

The four sections above are all about uGUI's **transient** interaction machine, where Hover and Pressed override Selected while they last. That is right for feedback and wrong for structure: a header whose panel is keyed on `state-selected` loses the panel the moment the pointer touches the header.

`checked` / `unchecked` follow `isOn` instead — the persistent question, which hovering does not change. They are `on=` values on `<Trigger>` / `<Animation>` / `<Show>` (each also `@<id>`), and resolve **upward** to the nearest `<Toggle>` / `<Tab>` exactly as `state-*` does. A `<Btn>` is not a source: it has no checked state (`PUI-CHECKED-NO-SOURCE`).

```xml
<!-- a header that shows a panel: the panel is a SIBLING, reached by @id -->
<VStack width="150">
  <Toggle id="hdr" isOn="true" width="stretch" height="24">任务</Toggle>
  <Show on="checked@hdr">
    <ScrollList itemTemplate="TaskRow" width="stretch" height="clamp(_, hug, 200)"/>
  </Show>
</VStack>

<!-- swapping two blocks in place -->
<Toggle id="mute" isOn="false">
  <Show on="checked"><Icon name="ui:speaker-off"/></Show>
  <Show on="unchecked"><Icon name="ui:speaker-on"/></Show>
</Toggle>
```

- **A second, independent claim family.** `<Show on="checked">` and `<Show on="unchecked">` are complementary on their own and have **no Normal fallback** — a persistent state has no "default block". They coexist with the `state-*` blocks on the same control and may nest inside them, and a checked block stays visible while the control is hovered or pressed.
- **Writing only one of the pair is fine**: the other half is simply "nothing shown".
- **Every path counts as a change**: a click, `IsOn = …` from C#, or a `ToggleGroup` mate taking the selection away all raise the matching edge.
- **`interactable="false"` does not enter into it.** A disabled control still has an `isOn`, and `checked` still reflects it — unlike `state-hover` / `state-pressed`, which a disabled control never emits.
- **First-frame establishment**: a control already in that state as the Screen opens dispatches once, and an `<Animation>` on that dispatch writes its **end state without playing** — an `isOn="true"` header shows its chevron already turned instead of spinning it on frame 1. Every later flip animates normally. See [`animations.md`](animations.md) → *First-frame establishment*.
