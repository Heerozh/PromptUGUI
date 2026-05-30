# Btn State-Driven Visuals — Design

**Status**: Proposed
**Spec date**: 2026-05-30
**Depends on**: master description-language spec (`2026-05-07-promptugui-description-language-design.md`), existing `<Btn>`, `<Trigger>` / `<Animation>` (`on=` system), `ImageTint` / `PointerEventRelay`
**Skill impact**: `authoring-promptugui-xml` (new `state-*` `on=` events, `<Show>` tag, `<Btn>` `*Color` / `interactable` / `stateReact` attrs), `scripting-promptugui-csharp` (`Btn.OnState`, `BtnState`)

---

## 1. Problem

`<Btn>` today wraps a uGUI `Button` with `targetGraphic = _bg` and the default ColorTint transition. uGUI's `Selectable` state machine (Normal / Highlighted / Pressed / Selected / Disabled) only ever drives that **one** graphic. So when an author composes a custom button — `<Btn><Icon/><Text/></Btn>` — the children do not react to press/hover at all, and there is no XML way to:

- tint the whole button (bg + label + icon) together on hover/press;
- show a different icon/artwork while pressed (uGUI SpriteSwap is likewise single-graphic);
- react to interaction state from C# or from the existing `<Animation>` system, which only has the one-shot, down-only `press` event — there is no release counterpart (skill: "release / long-press are v2").

The root cause is not that events fail to reach children — it is that uGUI deliberately confines the transition to a single `targetGraphic`.

## 2. Goals

- `<Btn>` **broadcasts** its `Selectable` state so any descendant can react.
- **Tint** the whole subtree per state via compact `<Btn>` attributes (fan-out to all descendant graphics + bg).
- **Switch** between per-state artwork/content via a new `<Show>` wrapper — covers "swap the icon on press" and any other per-state subtree.
- **Animate** on a state transition: `<Animation>` (and base `<Trigger>`) gain `state-*` events, which also fixes the missing press-release.
- C# access via `Btn.OnState`.
- Strictly **additive**: a `<Btn>` with none of the new attributes/children behaves exactly as today.

## 3. Non-goals

- No persistent toggle / checked state on `<Btn>` — that is `<Toggle>` / `<Tab selectedSprite>`. The scope here is **transient** state feedback only.
- No per-child `*Color` / `*Sprite` attributes. Sprite/artwork switching is done with `<Show>`, never a context-dependent child attribute.
- No new LitMotion family / `swap` action on `<Animation>` — `<Show>` replaces it.
- No animated cross-fade between `<Show>` alternatives (instant `SetActive`; a fade option can come later).

## 4. Design

Three verbs, one state machine. **Tint** modifies the colour of graphics already present (so it fans out as an attribute, no extra GameObjects). **Switch** picks one of several alternative subtrees (so it uses `<Show>` wrappers, one per alternative). **Animate** plays a one-shot motion on a transition (existing `<Animation>`). All three are driven by the same broadcast `BtnState`.

### 4.1 State source — `PuiButton` + `BtnState`

Replace the plain `Button` created in `Btn.OnAttached` with an internal `PuiButton : Button` (in `Runtime/Controls/Internal/`). It overrides the state-machine hook:

```csharp
protected override void DoStateTransition(SelectionState state, bool instant)
{
    base.DoStateTransition(state, instant);   // default targetGraphic ColorTint preserved (back-compat)
    _state.Value = Map(state);                // broadcast to reactors / Show blocks / C#
}
```

`BtnState { Normal, Hover, Pressed, Disabled }`. Mapping: `Normal→Normal`, `Highlighted→Hover`, `Pressed→Pressed`, `Disabled→Disabled`, and **`Selected→Normal`** (a click-button should not keep a sticky highlight after a touch tap). `_state` is an R3 `ReactiveProperty<BtnState>` seeded to `Normal` (or `Disabled` when `interactable=false`) and re-emitted once after the subtree is applied (see §4.4) so reactors and Show blocks get an initial value.

`Btn` exposes `public Observable<BtnState> OnState => _state;`. `OnClick` and the pointer streams are unchanged. `OnState` is always available even on a plain `<Btn>` — it is a cheap `ReactiveProperty` with no subscribers when unused.

**Back-compat switch.** With no fan-out tint attribute and no `<Show>` children, `transition` stays ColorTint on `_bg`, exactly as today. As soon as a `*Color` attribute is present, `Btn` sets `_btn.transition = Transition.None` and lets the tint reactors own all visuals — single source of truth, so `_bg` is never tinted twice.

### 4.2 Tint fan-out — `hoverColor` / `pressedColor` / `disabledColor`

New `[UIAttr]`s on `<Btn>`. Values are **multipliers** (uGUI ColorTint semantics), not absolute colours: a reactor applies `graphic.color = baseColor * stateMultiplier`, with the Normal multiplier = white. Theme tokens / hex / CSS names are accepted (same resolver path as `color`).

When any of them is present, `Btn` installs a `StateTintReactor` (new, `Runtime/Controls/Internal/`) on **its own bg and every descendant Graphic** — this is the agreed "whole subtree" range. Each reactor:

- caches its Graphic's base `color` at install time;
- subscribes to the source `OnState`;
- lerps `base → base * stateMultiplier` over `fadeDuration` (default `0.1s`, uGUI parity) via LitMotion.

Per-child opt-out: a child carrying `stateReact="false"` is skipped by the fan-out installer (its colour stays put across states).

Naming: deliberately distinct from the existing `tint` attribute (which switches the multiply vs linear-light **material** through `ImageTint`) and from `color` (the base bg colour). The `*Color` state attrs collide with neither.

### 4.3 `state-*` events in the `on=` vocabulary

Extend `TriggerKind` and `TriggerSpec.Parse` with `state-normal`, `state-hover`, `state-pressed`, `state-disabled`, each with the existing optional `@id` suffix.

**Resolution is upward** — a new direction. The source is the nearest `<Btn>` ancestor of the `<Trigger>` / `<Animation>` / `<Show>`, found by a transform-ancestor walk (`GetComponentInParent<PuiButton>()`, the same idiom `<Tab>` uses to find its `<TabBar>`'s `ToggleGroup`). `state-pressed@id` resolves a specific `<Btn>` by id through `ScopedIds`. No `<Btn>` ancestor and no matching `@id` → error (§5). This contrasts with the existing `click` / `hover-enter` / `press` resolution, which walks **downward** to find a Btn/Image source in the subtree.

Base `Trigger` behaviour for a `state-*` kind: `Fire()` on **enter** of that state. This gives `<Animation on="state-pressed">` (play a tween when pressed) with `<Animation on="state-normal">` as its natural release counterpart, and exposes `OnFire` to C# for a generic `<Trigger on="state-*">`.

Distinction from the pointer events: `hover-enter` / `press` are raw `PointerEventRelay` events on a Btn/Image found downward; `state-hover` / `state-pressed` come from the enclosing Btn's `Selectable` state machine, so they are disabled-aware and drag-cancel-aware.

### 4.4 `<Show>` — state-conditional visibility

New control `Show : Trigger` (`Runtime/Controls/Show.cs`). A no-visual wrapper (like `Trigger`) whose subtree is visible **while** the source Btn is in the `on=` state and hidden otherwise.

- Only `state-*` `on=` values are valid on `<Show>`; one-shot kinds (`click`, `open`, …) make no sense for a sustained binding → error (§5).
- It overrides the base fire path: instead of `Fire()`, it subscribes to the source's state and toggles its subtree's active flag. **Strategy C** — the subtree is instantiated once and only `SetActive`-toggled, never `Destroy`ed (consistent with Add-block lifetime rules).
- **Mutual exclusion + Normal fallback.** Sibling `<Show>` blocks under the same Btn are mutually exclusive. Each Show registers its target state with the source. A Show is visible iff `current == myState`, **or** (`myState == Normal` **and** no sibling Show claims `current`). Consequence: declaring just `state-normal` + `state-pressed` automatically shows the Normal artwork during PC `Hover` (because no `state-hover` block claims it); add an explicit `state-hover` block when distinct hover art is wanted. If neither an exact-match nor a Normal block exists for `current`, that group shows nothing.
- Initial visibility is evaluated after the subtree is applied: the Btn emits its seed state in its `OnAfterApply`, which (per the existing "apply after subtree recursion" ordering) runs after every descendant Show has registered.

```xml
<Btn>
  <Show on="state-normal"><Image sprite="ui:play"/></Show>
  <Show on="state-pressed"><Image sprite="ui:play_dn"/></Show>
</Btn>
```

Like `<Trigger>` / `<Animation>`, `<Show>` passes through its single child's native size (`GetNativeSize`) so it does not collapse to a 0×0 slot in a layout group.

### 4.5 `interactable` attribute on `<Btn>`

So `disabledColor` and `state-disabled` are reachable from XML, `<Btn>` gains `interactable` (bool, default `true`) mapped to `Button.interactable`. Setting it `false` drives the `Selectable` into Disabled, which broadcasts `BtnState.Disabled`.

### 4.6 C# surface

- `Btn.OnState : Observable<BtnState>` (R3) — public, for code that prefers to react in C#.
- `BtnState` enum — public.

No change to `Get<Btn>`, `OnClick`, or the existing pointer streams.

## 5. Errors & Lint

- `<Show on="click">` (or any non-`state-*` value) → parse error: `<Show> only accepts state-* events (state-normal / state-hover / state-pressed / state-disabled).`
- `<Trigger>` / `<Animation>` / `<Show>` with a `state-*` `on=` and no `<Btn>` ancestor (and no matching `@id`) → error via a new lint rule **PUI-STATE-NO-SOURCE** in `Runtime/Core/Lint/`, shared with the `ScreenInstantiator` warning path and promoted to a non-zero exit by the CLI (mirrors `PUI-LAYOUT-ANCHOR`).
- `*Color` / `interactable` / `stateReact` are restricted to their valid elements by the generated XSD (`*Color` / `interactable` on `<Btn>`; `stateReact` on any graphic-bearing element).

## 6. Testing (TDD — red first, then implement)

EditMode (`PromptUGUI.Tests.EditMode`):

- `PuiButton.DoStateTransition` maps `SelectionState → BtnState` (including `Selected → Normal`) and pushes `OnState`.
- Fan-out: a `pressedColor` install adds a tint reactor to bg + each descendant graphic; `stateReact="false"` opts a child out; the reactor restores base colour on `Normal`.
- `transition` becomes `None` only when a `*Color` attr is present; otherwise unchanged.
- `TriggerSpec.Parse` accepts the four `state-*` kinds with/without `@id` and rejects unknown kinds.
- Upward source resolution finds the nearest Btn ancestor; `@id` resolves via `ScopedIds`; missing source throws.
- `<Show>`: exact-match visibility, Normal fallback for an unclaimed state, mutual exclusion, instant `SetActive`, single initial evaluation.
- `interactable="false"` → `BtnState.Disabled` and `disabledColor` applied.
- Lint: `PUI-STATE-NO-SOURCE` fires for a sourceless `state-*`; `<Show on="click">` errors.

PlayMode (`PromptUGUI.Tests.PlayMode`):

- A pointer-driven press/hover over a composed `<Btn>` flips child tint and `<Show>` artwork, and reverts on release / pointer-exit.

## 7. Skill updates (same PR, English)

- `authoring-promptugui-xml`: `<Btn>` attribute table (`hoverColor` / `pressedColor` / `disabledColor`, `interactable`, `stateReact`); a new `<Show>` control row + its container/visibility semantics; `state-*` added to the Triggers/Animations `on=` table with the upward-resolution rule and the `hover-enter` vs `state-hover` distinction; `PUI-STATE-NO-SOURCE` in the lint list.
- `scripting-promptugui-csharp`: `Btn.OnState` / `BtnState`.
