# Tab — merge the selected overlay into the single bg + selection-aware base

**Status**: Proposed (design approved; spec under review)
**Spec date**: 2026-06-04
**Branch**: `feat/tab-selected-skin`
**Depends on**: state-color-absolute-modulate (`2026-06-02-state-color-absolute-modulate-design.md`), clickable state-visuals (`2026-05-30-clickable-state-visuals-design.md`), btn-state-visuals (`2026-05-30-btn-state-visuals-design.md`), btn-pressed-sprite — `PuiToggle` / `InteractState` / `StateBroadcaster` / `StateTintReactor` / `StateTintInstaller` / `StateColorSet` / `<Btn pressedSprite>` (the `overrideSprite`-swap precedent).
**Skill impact**: `authoring-promptugui-xml` (the `<Tab>` row + `<Toggle>` row + the **Btn state visuals** section: `<Tab selectedSprite>` is now a bg `overrideSprite` swap, not an overlay child; `selectedColor` on `<Tab>` / `<Toggle>` is now the **bg base colour while selected**, not a Selected-state per-state absolute). `scripting-promptugui-csharp`: audit for any reference to the Tab "Overlay" node / `selectedSprite` semantics; `Tab.OnState` / `Toggle.OnState` / `OnValueChanged` / `OnSelected` are unchanged.

---

## 1. Motivation

A `<Tab>` today renders as **two** Graphics:

- `_bg` — the Image on the Tab's own GameObject; the toggle's `targetGraphic`; always visible; carries `sprite` / `color` and the `*Color` / `*Modulate` state reactors.
- `_overlay` — a lazily-created child Image, materialised only when `selectedSprite` is set, wired to `UnityToggle.graphic`; uGUI shows/hides it by `isOn` (CanvasRenderer alpha 0/1, instant). It carries the `selectedSprite` and is composited **on top of** `_bg`.

This two-layer split exists so the "selected artwork" rides the uGUI `Toggle.graphic` channel (free isOn-driven show/hide, decoupled from the transient Hover/Pressed state machine). That decoupling is genuinely useful **when you want compositing** — a normal background that stays visible with a highlight layered over it.

But the common tab shapes don't want compositing:

1. **Colour-only tab** — no sprite at all; states are pure colour. One Graphic (the bg) already covers this; no overlay is created.
2. **Sprite tab** — a normal/released sprite (a) and a pressed/selected sprite (b); the selected sprite **replaces** the normal one (they are alternatives, not layers); Hover / Disabled are colour shifts on top.

In shape 2 the selected sprite is a *replacement*, so the overlay's one unique capability (compositing) is unused, and the split causes two real problems:

- **The overlay can't be coloured by the language.** `selectedColor` / `tint` only touch `_bg`; the overlay renders at its raw (white) sprite colour. The only lever that reaches it is `selectedModulate`, which also fans out to the label/icon — not surgical. Authoring `<Tab selectedSprite="UI:Button-Solid" selectedColor="primary">` (the exact real use case that prompted this) does **not** colour the selected sprite; instead `selectedColor` paints the (sprite-less) bg into a stray solid `primary` rectangle behind a white sprite.
- **`selectedColor` as a per-state absolute flickers transparent under hover.** With a transparent normal bg (`color="#0000"`), `selectedColor` is the bg's Selected-state absolute. Hovering the *already-selected* tab makes the broadcaster emit `Hover` (the transient overrides `Selected`); the reactor then resolves `hoverColor ?? baseColor`, and `baseColor` is the transparent normal `color` → the selected look vanishes mid-hover. Setting `selectedColor` alone is enough to trigger this (it installs the reactor and flips `transition` to `None`, so the reactor owns *all* states).

The fix is to collapse `<Tab>` to a **single** bg Graphic and make the selected state a **skin** swapped onto that bg by `isOn`:

- `selectedSprite` swaps the bg's `overrideSprite` while selected (the `<Btn pressedSprite>` mechanism, keyed on `isOn` instead of `Pressed`).
- `selectedColor` becomes the bg's **base colour while selected** (a selection-aware base), so Hover / Pressed / Disabled layer on top of it and a selected tab never falls back to the transparent normal base.

This makes `<Tab selectedSprite="UI:Button-Solid" selectedColor="primary">` mean exactly "show Button-Solid tinted `primary` while selected, transparent otherwise", with `selectedColor` colouring the bg consistently with `hoverColor` / `pressedColor` — the special-case "colour the overlay" semantics disappear because there is no overlay.

`<Toggle>` shares the selection-aware base half of this change (it has `isOn` and the same hover-flicker bug) but keeps its checkmark overlay (a genuine composited checkmark, not a full-tab replacement).

## 2. Goals

- `<Tab>` renders as a **single** Graphic (`_bg`). The `_overlay` child, `EnsureOverlay`, and the `_toggle.graphic` wiring are removed.
- `<Tab selectedSprite="…">` swaps `_bg.overrideSprite` to the resolved sprite **while `isOn`**, and clears it (back to the authored `sprite`) while off. Driven by the toggle's `OnValueChanged`, **not** the transient broadcast state, so the selected artwork is stable through hover / press of the selected tab. `""` / `"none"` = no swap (no-op), unchanged. Setting `selectedSprite` flips `Selectable.transition = None` (mirrors `<Btn pressedSprite>`) so uGUI ColorTint does not double-tint the swapped sprite.
- `selectedColor` on `<Tab>` and `<Toggle>` becomes the **selection-aware base colour** of the bg (`targetGraphic`): the reactor's base is `color` while not selected and `selectedColor ?? color` while selected. Hover / Pressed / Disabled `*Color` absolutes and all `*Modulate` multipliers compose on top of the *current* base. A selected control with no `hoverColor` stays at `selectedColor` on hover.
- `selectedModulate` is unchanged: still the Selected-state relative multiplier, still fanned out to the bg + descendants. Per state, the bg shows `(absolute[state] ?? selectionAwareBase) × (modulate[state] ?? white)`.
- `<Toggle>` keeps its checkmark overlay (`Toggle.graphic` / the `Checkmark` child) untouched; only its bg `selectedColor` semantics align with Tab's.
- `<Btn>` is unaffected (no `isOn` → never selected → selection-aware base collapses to the normal base).
- The `Selected` `InteractState`, `OnState`, `<Show on="state-selected">`, and `state-selected` triggers are unchanged — the broadcaster still emits `Selected`.

## 3. Non-goals

- **No backward-compatibility shim.** This is a deliberate semantic change to `<Tab selectedSprite>` (overlay → `overrideSprite` swap) and to `selectedColor` (Selected-state absolute → selection-aware base). The repo's `.ui.xml` uses **zero** `<Tab selectedSprite>` / `selectedColor` today (the project has no Tab in use yet — user-confirmed), so there is no migration target. Clean break. (User-approved.)
- **No compositing path for Tab.** A tab that genuinely needs "normal bg visible *under* a selected highlight" uses `<Show on="state-selected"><Image …/></Show>` (already supported; full `color` / `tint` control). The dedicated overlay layer is removed precisely because shape 2 wants replacement, not compositing.
- **No `selected*` on `<Btn>`** (unchanged — a momentary button has no `isOn`).
- **No new XML attributes.** `selectedSprite` / `selectedColor` / `selectedModulate` keep their names; only their wiring changes. The XSD surface is unchanged (reflection-driven; same `[UIAttr]` names).
- **No change to the `(absolute ?? base) × (modulate ?? white)` composition model** from `2026-06-02-state-color-absolute-modulate-design.md` — this spec only makes `base` selection-aware on the bg and moves `selectedColor` from the `absolute[Selected]` slot into that base.
- **No change to `StateBroadcaster`'s public surface.** `_isOn` stays private; we do not add an isOn stream or force a re-emit (that would re-fire `state-*` triggers). The selection signal reaches the reactor via an explicit control push (§4.4), not via the broadcaster.
- `tint` (multiply-vs-linear-light material) stays orthogonal and now naturally applies to the selected sprite, because the selected sprite is the bg (which `tint` already targets) — the old "overlay is not tinted" caveat is retired.

## 4. Design

### 4.1 Tab structural change

`Tab.OnAttached` keeps creating `_bg` and wiring it as `targetGraphic` exactly as today. Remove:

- the `_overlay` field, `EnsureOverlay`, and the `_overlay.rectTransform` / `_toggle.graphic = _overlay` / `_toggle.toggleTransition = None` wiring.

`Tab` gains:

- `private string _selectedSpriteKey;` (the resolved sprite is cached so the `isOn` handler can swap without re-resolving) — store the resolved `UnityEngine.Sprite _selectedSprite;`.
- the `OnIsOnChanged` handler (already exists for `_changed` / bind) also performs the sprite swap and the selection-aware-base push (§4.2, §4.4).

### 4.2 `selectedSprite` → `overrideSprite` swap (isOn-driven)

```csharp
[UIAttr(IsSprite = true), Preserve]
public string SelectedSprite
{
    set
    {
        // "" / "none" → no selected sprite (no-op), unchanged.
        if (string.IsNullOrEmpty(value) || value == "none") { _selectedSprite = null; ApplySelectedSprite(); return; }
        _selectedSprite = UI.ResolveSprite(value);
        _toggle.transition = Selectable.Transition.None;   // mirror <Btn pressedSprite>: don't double-tint the swap
        ApplySelectedSprite();
    }
}

private void ApplySelectedSprite()
{
    _bg.overrideSprite = (IsOn && _selectedSprite != null) ? _selectedSprite : null;
}
```

- `_bg.overrideSprite` overrides the displayed sprite without touching the authored `_bg.sprite`, so toggling `isOn` flips between the normal sprite and the selected sprite with no state to restore — identical mechanism to `<Btn pressedSprite>`, keyed on `isOn` rather than `Pressed`.
- `OnIsOnChanged(bool)` calls `ApplySelectedSprite()` so user/code selection swaps the sprite. Because the swap is keyed on `isOn` (persistent), hover / press of the selected tab — which change only the transient state — never disturb it.
- `transition = None` is set when a `selectedSprite` is present so uGUI's ColorTint does not additionally darken the swapped sprite. When the state reactor is installed (§4.4) it also sets `transition = None`; the two agree.

### 4.3 `selectedColor` → selection-aware base

`selectedColor` stops being passed as the `Selected` entry of the **absolutes** `StateColorSet`. Instead it is resolved into a `Color?` **selected base** and handed to the bg's reactor. The Selected state then has no absolute, so it resolves to the (selection-aware) base = `selectedColor`.

Per state `s`, for the bg:

```
base(s)      = isSelected ? (selectedBase ?? colorBase) : colorBase
displayed(s) = (absolute[s] ?? base(s)) × (modulate[s] ?? white)
```

- `colorBase` = the `color` attribute (the reactor's captured base, as today).
- `selectedBase` = resolved `selectedColor`, or null when `selectedColor` is unset (then selected base = `colorBase`, i.e. only the sprite differs).
- `absolute[Hover/Pressed/Disabled]` and `modulate[Hover/Pressed/Selected/Disabled]` are unchanged. There is no `absolute[Selected]` anymore.
- `isSelected` = the control's `isOn` (pushed to the reactor — §4.4).

Worked against the real use case `<Tab sprite="" color="#0000" selectedSprite="UI:Button-Solid" selectedColor="primary">`:

| Tab state | `overrideSprite` | bg displayed colour |
|---|---|---|
| not selected, idle | null (no `sprite`) | `#0000` (transparent) |
| not selected, hover | null | `hoverColor ?? #0000` (transparent unless set) |
| **selected, idle** | Button-Solid | `primary` |
| **selected, hover** | Button-Solid | `hoverColor ?? primary` → **stays `primary`** (no flicker) |
| selected, pressed | Button-Solid | `pressedColor ?? primary` |

### 4.4 `StateTintReactor` + `StateTintInstaller` + control wiring

`StateTintReactor` gains a selection-aware base:

```csharp
private Color  _baseColor;            // normal base, captured once from graphic.color (unchanged invariant)
private Color? _selectedBase;         // from Configure; null ⇒ no distinct selected base
private bool   _selected;             // pushed by the owning control via SetSelected

// Configure signature gains the selected base (default null keeps Btn/descendant call-sites unchanged):
public void Configure(StateColorSet absolutes, StateColorSet modulates, float fade, Color? selectedBase = null)

// New: the owning control pushes its isOn here; repaints the current state.
public void SetSelected(bool on)
{
    _selected = on;
    if (_source != null) OnState(_source.Current);   // repaint with the new base
}

private Color BaseFor(InteractState s)
    => _absolutes.For(s) ?? ((_selected && _selectedBase.HasValue) ? _selectedBase.Value : _baseColor);
```

- The "base captured once, never re-captured" invariant is intact: `_baseColor` is still captured from `graphic.color` on first init; `_selectedBase` arrives via `Configure` (re-passed on every ReSolve, never promoted from a tinted colour).
- `_selected` is pushed by the control, not read from the broadcaster — the broadcaster collapses `isOn` into `InteractState` and suppresses `Selected` under a transient state, and does not re-emit when `isOn` flips without a composite change. A control push is deterministic for both user-driven and programmatic `isOn` changes (uGUI `Toggle.onValueChanged` fires for both).

`StateTintInstaller.Install` gains the selected base for the targetGraphic only:

```csharp
internal static StateTintReactor Install(
    GameObject root, Selectable selectable, IReadOnlyList<IControl> children,
    StateColorSet absolutes, StateColorSet modulates, Color? selectedBase /* targetGraphic only */)
```

- Install gate becomes `absolutes.HasAny || modulates.HasAny || selectedBase.HasValue` (so a `selectedColor`-only tab still installs the bg reactor and flips `transition = None`).
- The targetGraphic's reactor gets `Configure(absolutes, modulates, fade, selectedBase)`; descendants get `Configure(default, modulates, fade, null)` (absolutes and the selected base are bg-only, exactly as absolutes are today).
- Install **returns** the targetGraphic's reactor (or null when nothing installed) so the control can push selection into it.

`Tab.OnAfterApply`:

```csharp
var abs = StateColorSet.Resolve(_hoverColor, _pressedColor, /* selected */ null, _disabledColor);
var mod = StateColorSet.Resolve(_hoverModulate, _pressedModulate, _selectedModulate, _disabledModulate);
Color? selectedBase = string.IsNullOrEmpty(_selectedColor) ? (Color?)null : UI.Theme.Resolve(_selectedColor);
_bgReactor = StateTintInstaller.Install(GameObject, _toggle, Children, abs, mod, selectedBase);
ApplySelectedSprite();                 // re-assert the overrideSprite swap after ReSolve
_bgReactor?.SetSelected(IsOn);         // re-assert the selection-aware base after ReSolve
```

`Tab.OnIsOnChanged(bool isOn)` (already wired to `_toggle.onValueChanged`) additionally:

```csharp
ApplySelectedSprite();
_bgReactor?.SetSelected(isOn);
```

### 4.5 `<Toggle>` — share the selection-aware base, keep the checkmark overlay

`<Toggle>` keeps its `Background` + `Checkmark` structure and `Toggle.graphic = Checkmark` wiring (a genuine composited checkmark, not a full-tab replacement — no `overrideSprite` swap). The only change mirrors Tab §4.3–§4.4 on its **bg** (`targetGraphic` = the `Background` child):

- `selectedColor` moves from the `absolute[Selected]` slot to the selected base passed to `Install`.
- `Toggle.OnAfterApply` resolves `selectedBase` and captures the returned reactor; its `onValueChanged` handler pushes `SetSelected(isOn)` (and `OnAfterApply` re-asserts on ReSolve).

This keeps `selectedColor` semantics identical across `<Tab>` and `<Toggle>` (no hover-flicker on a checked toggle) at the cost of one extra `SetSelected` wire — the reactor mechanism is already shared.

### 4.6 Broadcast / triggers unchanged

`StateBroadcaster.Recompute` still folds `isOn` into `Selected` and emits it; `<Show on="state-selected">`, `state-selected` triggers, `Tab.OnState`, `Toggle.OnState`, `OnValueChanged`, `OnSelected`, and the `bind=` Frame toggle are untouched.

## 5. Testing (TDD, EditMode-first, via Unity MCP)

Red→green per behaviour. Existing `TabStateTests` / `ToggleStateTests` rows that asserted `selectedColor` as a Selected-state absolute on the bg keep passing (the resting selected bg is still `selectedColor`); the new coverage is the swap + the hover-stability + the structural collapse.

1. **selectedSprite swaps overrideSprite on select** — `<Tab sprite="ui:n" selectedSprite="ui:s">`, drive `IsOn=true` ⇒ `_bg.overrideSprite == resolve("ui:s")`; `IsOn=false` ⇒ `_bg.overrideSprite == null` (and `_bg.sprite` still the authored `ui:n`).
2. **No overlay child** — a `<Tab selectedSprite="ui:s">` has **no** child GameObject named `Overlay`; `_toggle.graphic == null`.
3. **selectedColor is the selected base** — `<Tab color="#0000" selectedColor="#076DD7">`, drive Selected ⇒ `targetGraphic.color == #076DD7`.
4. **Hover on a selected tab does not flicker** — same tab, no `hoverColor`; with `IsOn=true`, push transient Hover ⇒ `targetGraphic.color == #076DD7` (NOT the transparent `#0000` normal base). This is the regression the change exists to fix.
5. **hoverColor over a selected tab composes on the selected base** — add `hoverColor="#ffffff"`; selected + Hover ⇒ `#ffffff`; selected + idle ⇒ `#076DD7`; not-selected + Hover ⇒ `#ffffff`; not-selected idle ⇒ `#0000`.
6. **selectedColor × selectedModulate** — `selectedColor="#076DD7" selectedModulate="#808080"`, Selected ⇒ bg ≈ `#076DD7 × 0.5`.
7. **selectedSprite forces transition None** — setting `selectedSprite` ⇒ `_toggle.transition == None`.
8. **selectedColor-only installs the reactor** — `<Tab color="#0000" selectedColor="#076DD7">` with no other state attrs ⇒ reactor installed, `transition == None`, behaviours 3–4 hold.
9. **Variant ReSolve** re-resolves a token in `selectedColor` and re-asserts the swap + selection without re-capturing the normal base; selected look survives a simulated resize/ReSolve (the §4.4 `OnAfterApply` re-assert).
10. **Toggle selection-aware base** — `<Toggle color="#0000" selectedColor="#076DD7">`: checked + Hover (no `hoverColor`) ⇒ Background `targetGraphic.color == #076DD7`; the `Checkmark` overlay still toggles via `Toggle.graphic` (structure intact).
11. **Btn unaffected** — `<Btn color="#202020" pressedModulate="#808080">`: Pressed ⇒ `base × 0.5` (migrated existing test); no `selected*`; reactor `selectedBase` is null.
12. **XSD** — `<Tab>` / `<Toggle>` still emit `selectedSprite` / `selectedColor` / `selectedModulate` (attr set unchanged); `XsdGeneratorTests` substring assertions hold.

Then full EditMode + EditorOnly suites; PlayMode for the pointer-driven select → hover → press → revert on Tab and Toggle (selected artwork stable, colour composes).

## 6. Skill impact (same PR)

`authoring-promptugui-xml`:
- `<Tab>` row: `selectedSprite` is now "while selected, swaps the bg's `overrideSprite` (no separate overlay child); reverts to `sprite` when deselected; `""`/`none` = no swap; auto-flips `transition` off ColorTint". `selectedColor` is "the bg base colour while this Tab is the active/`isOn` one — hover/pressed/disabled layer on top of it". Drop the "selectedSprite creates the overlay swap / bound to `UnityToggle.graphic`" wording and the "button mode … ColorTint on `_bg` still highlights" note that referenced the overlay.
- `tint` row / the "On `<Tab>` it applies to the bg only … the `selectedSprite` overlay is not tinted" caveat: retire it — the selected sprite **is** the bg now, so `tint` applies to it.
- `<Toggle>` row: `selectedColor` now "bg base colour while checked" (selection-aware), same wording as Tab; checkmark overlay unchanged.
- **Btn state visuals** section: update the Tab/Toggle `selectedColor` description (selection-aware base, not a Selected-state absolute) and note `<Tab selectedSprite>` joins `<Btn pressedSprite>` as a `overrideSprite`-swap shorthand (keyed on `isOn`).
- uGUI 对照表 `<Tab>` / `<Toggle>` rows: Tab no longer lists an `Overlay` auto-child or `graphic=Overlay`; Toggle's `graphic=Checkmark` stays.
- "Tabs" section prose + cheatsheet `TAB/TOGGLE` line: selectedSprite = overrideSprite swap; selectedColor = selected base.

`scripting-promptugui-csharp`: audit for any mention of the Tab "Overlay" node or `selectedSprite`/overlay; `OnState` / `InteractState` / `Tab.OnState` / `Toggle.OnState` / `OnValueChanged` / `OnSelected` unchanged.

## 7. Alternatives considered

- **Keep two layers; colour the overlay via `selectedColor` (route absolute → overlay when present).** Smaller, Tab-only change. Rejected (user): keeps an extra node, and makes `selectedColor`'s target context-dependent (overlay when `selectedSprite` set, else bg) — a wart the single-image model removes entirely.
- **Drop the overlay; keep `selectedColor` as the Selected-state absolute (no selection-aware base).** Simplest code. Rejected: reintroduces the hover-flicker (a selected tab with a transparent normal base goes transparent on hover unless the author also sets `hoverColor`/`pressedColor`/`disabledColor`) — the exact fragility this design removes.
- **Read `isOn` in the reactor from `StateBroadcaster`.** Would need to expose `_isOn` and force a re-emit on `isOn`-without-state-change; the re-emit re-fires `state-*` triggers (wrong). The explicit control push (§4.4) is contained and side-effect-free.
- **Drive the selected sprite off the broadcast `Selected` state instead of `isOn`.** Breaks under hover/press of the selected tab (transient overrides `Selected` → sprite reverts). `isOn` is the correct, stable key.

## 8. Risks / open questions

- **`StateTintReactor.Configure` signature change** (adds `Color? selectedBase = null`) and `StateTintInstaller.Install` now returns the targetGraphic reactor. Internal types; all call-sites are in `Btn` / `Tab` / `Toggle` + tests. Btn passes `selectedBase = null` and ignores the return.
- **Toggle's `targetGraphic` is the `Background` child**, not the root — the selected base must reach *that* graphic. Covered by the Toggle-specific test (behaviour 10); the existing absolute-on-`targetGraphic` rule already handles this graphic correctly.
- **Re-assert ordering on ReSolve**: `OnAfterApply` re-runs `Install` (which re-`Configure`s, repainting current state from the captured normal base), then `SetSelected(IsOn)` repaints again with the selected base. Two paints per ReSolve on a selected control — acceptable (ReSolve is not hot), and the second is the authoritative one.
- **`overrideSprite` + author children**: a `<Tab>` with both `selectedSprite` and nested author children still composites the children over the bg (unchanged); only the bg's own sprite is swapped. No interaction.
