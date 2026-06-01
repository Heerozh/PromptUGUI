# Btn `pressedSprite` — Design

**Status**: Proposed
**Spec date**: 2026-06-01
**Depends on**: master description-language spec (`2026-05-07-promptugui-description-language-design.md`), Btn state-visuals (`2026-05-30-btn-state-visuals-design.md`, `2026-05-30-clickable-state-visuals-design.md`) — `PuiButton` / `StateBroadcaster` / `OnState` / `StateTintInstaller`
**Skill impact**: `authoring-promptugui-xml` (new `<Btn>` `pressedSprite` attr + a note in the "Btn state visuals" section). No C# public-API change → `scripting-promptugui-csharp` untouched.

---

## 1. Problem

`<Btn>` can already swap per-state artwork with `<Show on="state-pressed">` (a whole alternative subtree) and tint per state with `pressedColor`. But the most common case — a single bg image that shows one sprite normally and another while held — currently needs a two-`<Show>`/two-`<Image>` dance for what is conceptually one extra attribute. Authors want the uGUI-`SpriteState.pressedSprite` ergonomics: one attribute, swap on press, revert on release.

There is also a default-ColorTint interaction. A plain `<Btn>` keeps uGUI's built-in ColorTint (`transition = ColorTint`), which darkens `_bg` on press. If a `pressedSprite` swapped the bg image while that darkening was still active, the pressed artwork would be **double-darkened** — almost never what the author wants, since the pressed sprite already encodes the pressed look.

## 2. Goals

- New `<Btn pressedSprite="...">` attribute: while the Btn is **Pressed**, its bg shows that sprite; on leaving Pressed (Normal / Hover / Disabled) it reverts to the authored `sprite`.
- Setting `pressedSprite` **auto-disables** the default ColorTint darkening (so the swap shows clean), consistent with the existing rule "any state visual present → uGUI built-in ColorTint off".
- Composes with `pressedColor` (swap + tint stack) and with `<Show>` (independent mechanisms).
- Strictly **additive**: a `<Btn>` without `pressedSprite` behaves exactly as today.

## 3. Non-goals

- No `hoverSprite` / `disabledSprite` / full `SpriteState` family — only the pressed swap was requested. The broadcast-driven design (§4.2) makes adding them later trivial, but YAGNI for now.
- No standalone "disable default ColorTint" switch (e.g. `colorTint="false"`). The existing `*Color` attributes already let an author kill the default darkening (set any one to white → `transition = None`, unspecified states = identity), and `pressedSprite` covers the swap case. Out of scope per alignment.
- Not extended to `<Tab>` / `<Toggle>` in this pass (Tab already has `selectedSprite`). Same pattern could follow later if needed.
- No animated cross-fade on the swap — instant `overrideSprite` change (matches uGUI SpriteSwap and the `<Show>` instant `SetActive`).

## 4. Design

### 4.1 Why broadcast-driven, not uGUI `SpriteSwap`

uGUI's `Selectable.transition` is a single enum (`None` / `ColorTint` / `SpriteSwap` / `Animation`). The Btn state-visuals work standardized on **`transition = None` + the `OnState` broadcast**: `PuiButton.DoStateTransition` calls `base` then `_broadcaster.SetTransient(...)`, and every visual (`StateTintReactor` colour fan-out) is a subscriber to `OnState` regardless of the transition value. `StateTintInstaller.Install` sets `transition = None` so uGUI's built-in ColorTint doesn't double-apply on top of the reactors.

Driving `pressedSprite` through uGUI's native `SpriteSwap` would re-introduce a dependency on `transition` and **fight** `StateTintInstaller`'s `transition = None` line (last-writer-wins, ordering-sensitive) whenever a `*Color` is also present. Instead, `pressedSprite` joins the same broadcast model: a small subscriber to `OnState` swaps `_bg.overrideSprite`, `transition` stays `None`. Colour tint and sprite swap are then orthogonal subscribers that stack cleanly.

### 4.2 `pressedSprite` attribute on `<Btn>`

New `[UIAttr(IsSprite = true)] string PressedSprite { set; }` on `Btn`. The setter:

- Treats `""` / `none` as **no pressed swap** (`_pressedSprite = null`), mirroring `Tab.selectedSprite` (`Tab.cs:190`); otherwise resolves via `UI.ResolveSprite(value)` into a `Sprite _pressedSprite` field. (Resolving in the setter matches the existing `Sprite` attr; a Variant ReSolve re-invokes the setter with the overridden value, so theme/Variant changes follow automatically.)
- Re-evaluates the swap for the **current** state immediately after assignment, so a live Variant change to `pressedSprite` takes effect without waiting for the next pointer transition.

### 4.3 The swap subscriber

In `Btn.OnAttached` (after `_btn` exists), subscribe **once** to `_btn.OnState`:

```csharp
_pressedSpriteSub = _btn.OnState.Subscribe(ApplyPressedSpriteForState);
// ApplyPressedSpriteForState(state):
//   _bg.overrideSprite = (state == InteractState.Pressed) ? _pressedSprite : null;
```

- Uses `overrideSprite`, never `sprite` — the authored `sprite` is preserved and revert is `overrideSprite = null` (zero bookkeeping). When `_pressedSprite` is null, Pressed sets `overrideSprite = null` too, i.e. no-op — so a Btn without `pressedSprite` is unaffected.
- `OnState` replays the current value on subscribe; at `OnAttached` the Btn is Normal so `overrideSprite` stays null. Correct.
- Disposed in `Btn.Dispose` alongside `_click`.

### 4.4 Auto-disable default ColorTint

In `Btn.OnAfterApply`, after the existing `StateTintInstaller.Install(...)` call:

```csharp
if (_pressedSprite != null)
    _btn.transition = Selectable.Transition.None;
```

`StateTintInstaller.Install` only flips `transition` to `None` when a `*Color` is present (it early-returns otherwise), so the `pressedSprite`-only case needs this explicit flip. With `transition = None`, uGUI's built-in ColorTint no longer darkens `_bg` on press, so the swapped pressed sprite shows clean. The `OnState` broadcast still fires (it is independent of `transition`), so both the swap subscriber and any tint reactors keep working.

Consistent with the existing `*Color` behaviour, this is **set-only** — it does not restore `transition = ColorTint` if `pressedSprite` is later cleared via a Variant (same limitation the `*Color` path already has; out of scope to fix here).

### 4.5 Composition

- **`pressedSprite` + `pressedColor`**: bg swaps to the pressed sprite (`overrideSprite`) **and** the reactor multiplies its colour (`baseColor * pressedColor`). Orthogonal channels, both driven by `OnState`.
- **`pressedSprite` + `<Show on="state-pressed">`**: independent. `pressedSprite` swaps the single bg image; `<Show>` toggles whole child subtrees. `pressedSprite` is the lightweight shorthand for the single-bg case; `<Show>` remains the general mechanism. No conflict.

### 4.6 C# surface

None. `pressedSprite` is a pure XML `[UIAttr]` with no new public method/property exposed to callers. `Btn.OnState` (already public) is reused internally.

## 5. Errors & Lint

No new error or lint rule. `pressedSprite` accepts the same value forms as `sprite` and fails the same way (an unresolvable key logs the existing `UI.ResolveSprite` error and yields `null` → no swap). The generated XSD gains `pressedSprite` as an optional `<Btn>` attribute (sprite-key string), alongside `sprite`.

## 6. Testing (TDD — red first, then implement)

EditMode (`PromptUGUI.Tests.EditMode`), driving transitions with `PuiButton.SimulateState(ordinal)` (Pressed = 2, Normal = 0, Disabled = 4):

1. `pressedSprite` set → SimulateState(Pressed) makes `_bg.overrideSprite == resolved pressed sprite`; SimulateState(Normal) makes `_bg.overrideSprite == null`; the authored `_bg.sprite` is untouched throughout.
2. `pressedSprite` set → `_btn.transition == Selectable.Transition.None` after apply (default darkening disabled).
3. `pressedSprite` **not** set → `_btn.transition == Selectable.Transition.ColorTint` (default unchanged) and `_bg.overrideSprite == null` across all states.
4. `pressedSprite` + `pressedColor` both set → on Pressed, `overrideSprite == pressed sprite` **and** `_bg.color` reflects `base * pressedColor` (assert with `StateTintReactor.TestForceInstant = true`).
5. Variant override of `pressedSprite` → after ReSolve, the swap uses the overridden sprite.
6. `pressedSprite="none"` / `""` → `_pressedSprite == null`; no swap on Pressed; `transition` stays `ColorTint`.

(No PlayMode test required — the swap path is fully covered by the broadcast-driven EditMode `SimulateState` route, matching how the existing state-visual reactors are tested.)

## 7. Skill updates (same PR, English)

- `authoring-promptugui-xml`:
  - `<Btn>` attribute row: add `pressedSprite` (sprite key, same forms as `sprite`; swaps the bg on Pressed via `overrideSprite`, reverts on release; presence auto-disables the default ColorTint darkening; `""` / `none` = no swap).
  - "Btn state visuals" section: a short note under the artwork-swap material (§2 there) that `pressedSprite` is the single-bg shorthand for `<Show on="state-pressed">`, that it auto-switches off the default ColorTint, and that it composes with `pressedColor`.
- `scripting-promptugui-csharp`: no change (no new C# public API).
