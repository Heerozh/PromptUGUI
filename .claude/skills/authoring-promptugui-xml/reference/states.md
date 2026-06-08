# State visuals (Btn / Tab / Toggle)

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md). Read this before using any `*Color` / `*Modulate` / `selectedColor` / `<Show on="state-*">` / `pressedSprite` / `disabledSprite` / `selectedSprite`. For the `state-*` `on=` event values and `<Trigger>` / `<Animation>`, see [`animations.md`](animations.md).

`<Btn>`, `<Tab>`, and `<Toggle>` all broadcast their uGUI interaction state. `<Btn>` emits `Normal` / `Hover` / `Pressed` / `Disabled` (Selectable's `Selected` is folded into `Normal`). `<Tab>` and `<Toggle>` also emit `Selected` (= the active/`isOn` control at rest; transient Hover/Pressed/Disabled override it and it reverts on release). Three ways to react, in increasing power:

## 1. State colour — two families

**Absolute `*Color`** (`hoverColor` / `pressedColor` / `selectedColor` / `disabledColor`) sets the per-state colour of the control's base graphic (`targetGraphic` / bg) **only** — the same graphic that `color=` targets. It does **not** fan out to descendants. Normal has no `*Color` (use `color=`). Accepts the same value forms as `color` (hex / CSS named / theme token).

**Relative `*Modulate`** (`hoverModulate` / `pressedModulate` / `selectedModulate` / `disabledModulate`) is a **relative multiplier** (Godot `modulate` semantics: white = identity, the normal uGUI ColorTint model). It fans out to the bg **and every descendant Graphic** (label, icons, nested images), switching the control off uGUI's built-in ColorTint, and the tint **fades** over ~0.1s.

They compose: per state, `displayed = (absolute ?? color) × (modulate ?? white)`.

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

## 3. State-triggered animation — `<Trigger>` / `<Animation on="state-...">`

`state-normal` / `state-hover` / `state-pressed` / `state-selected` / `state-disabled` (each also `@<id>`) are `on=` values on `<Trigger>` / `<Animation>` / `<Show>` — see the `on=` table in [`animations.md`](animations.md). Source resolution is **upward** to the nearest `<Btn>` / `<Tab>` / `<Toggle>` ancestor (opposite of `click` / `press`), and they fire **on entering** the state. `state-selected` is meaningful only with a `<Tab>` / `<Toggle>` source. Pair a press animation with its revert:

```xml
<Btn>
  <Animation scale="1:0.95" duration="0.08s" on="state-pressed"><Frame anchor="stretch"/></Animation>
  <Animation scale="0.95:1" duration="0.08s" on="state-normal"><Frame anchor="stretch"/></Animation>
  <Text anchor="center">Tap</Text>
</Btn>
```
