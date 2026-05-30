# Clickable State-Driven Visuals — Generalize Btn's state system to all `Selectable` controls

**Status**: Proposed (design approved; spec under review)
**Spec date**: 2026-05-30
**Branch**: `feat/clickable-state-visuals`
**Depends on**: `2026-05-30-btn-state-visuals-design.md` (the Btn state-visuals feature this generalizes — `PuiButton` / `BtnState` / `StateTintReactor` / `<Show>` / `state-*` triggers / `PUI-STATE-NO-SOURCE`)
**Skill impact**: `authoring-promptugui-xml` (`*Color` incl. `selectedColor` on `<Tab>` / `<Toggle>`; `state-selected` in the `on=` table; `PUI-STATE-NO-SOURCE` ancestor set extended to `<Tab>` / `<Toggle>`), `scripting-promptugui-csharp` (`InteractState` rename; `Tab.OnState` / `Toggle.OnState`)

---

## 1. Problem

The Btn state-visuals feature (broadcast interaction state → tint fan-out + `<Show>` swap + `state-*` triggers + `Btn.OnState`) is implemented **entirely against one concrete class, `PuiButton : Button`**:

- `PuiButton` (`Runtime/Controls/Internal/PuiButton.cs`) is the sole state source — it overrides `Selectable.DoStateTransition`, owns the `OnState` stream + `Current`, and owns the `<Show>` coordination (`RegisterShow` / `IsShowStateClaimed` / the claimed-state set + reevaluator list).
- All three consumers are hard-typed to `PuiButton`:
  - `TriggerSourceResolver.FindStateSource` does `GetComponentInParent<PuiButton>()` and returns `PuiButton` (`TriggerSourceResolver.cs:108-131`);
  - `StateTintReactor.EnsureInit` does `GetComponentInParent<PuiButton>()` (`StateTintReactor.cs:55`);
  - `Show._pui` is a `PuiButton` field (`Show.cs:26`).

So a `<Show>` / `<Trigger on="state-*">` / `*Color` placed on or inside any **other** clickable control finds no `PuiButton` and hard-throws (`InvalidOperationException`, plus the static `PUI-STATE-NO-SOURCE` lint). The "leaf vs. container" distinction does not exist in the engine here either — the only thing standing between Tab/Toggle/Slider and this feature is the hardcoded `Button` type.

Every clickable control in the library is a uGUI `Selectable` subclass — `Button` (Btn), `Toggle` (Tab, Toggle), `Slider`, `Dropdown` (TMP), `InputField` (TMP), `Scrollbar`. The transient state machine (Normal / Highlighted / Pressed / Disabled) is identical across all of them. The Toggle family additionally has a **persistent checked state** (`Toggle.isOn`) that drives an independent indicator graphic (`Toggle.graphic` — Tab's `selectedSprite` overlay, Toggle's checkmark).

## 2. Goals

- Decouple the broadcast from the concrete `Button`: extract an `IStateSource` contract + a shared `StateBroadcaster` helper that any `Selectable` subclass can forward to.
- Add the **persistent `Selected`** dimension (sourced from `Toggle.isOn`) to the state model, plus a `state-selected` `on=` event and a `selectedColor` tint — so a Tab/Toggle can tint or `<Show>`-swap its whole subtree while active, not only via the single overlay sprite.
- Wire **`Btn` (retarget), `Tab` (full), `Toggle` (full)** in this milestone.
- Leave **documented one-line extension points** so `Slider` / `Dropdown` / `InputField` are near-mechanical follow-ups (no abstraction re-open).
- Strictly **additive**: a `<Btn>` / `<Tab>` / `<Toggle>` with none of the new attributes/children behaves exactly as today.

## 3. Non-goals

- **No implementation of Slider / Dropdown / InputField / Scrollbar in this spec.** The plumbing is built generically over `Selectable`, but only Btn/Tab/Toggle are wired here. Each deferred control carries its own structure + test surface (Slider's track/fill/handle, Dropdown's popup template, InputField's caret/placeholder/focus-as-selected) and its own "sensible default fan-out range" discussion, which belong in their own small specs (§7).
- **No compose-mode tint.** `Selected` is the *resting baseline* of an active control (§4.2), not a second multiplier layered on top of Hover/Pressed (`base × selected × hover` simultaneously is out of scope; revisit if a tab bar needs it). The persistent overlay/checkmark keeps showing "active" during hover, so nothing is lost.
- **No animated cross-fade for `<Show>`** (unchanged from the Btn spec — instant `SetActive`).
- **No back-compat alias for `BtnState`.** Project is early; the public enum is renamed (§4.2 / §8).
- **No `selectedColor` / `state-selected` semantics on `<Btn>`.** A momentary button has no `isOn`, so it never emits `Selected` (the value stays inert; Normal-fallback covers any `<Show on="state-selected">` placed under a Btn without error).

## 4. Design

### 4.1 State-source abstraction — `IStateSource` + `StateBroadcaster`

C# cannot share a base class between `Button` and `Toggle` (both already extend `Selectable`), so the broadcast machinery moves into a held helper, not a common base.

**`IStateSource`** (internal interface, `Runtime/Controls/Internal/`):

```csharp
internal interface IStateSource
{
    Observable<InteractState> OnState { get; }
    InteractState Current { get; }
    void RegisterShow(InteractState state, Action reevaluate);
    bool IsShowStateClaimed(InteractState state);
}
```

**`StateBroadcaster`** (internal plain class, `Runtime/Controls/Internal/`): owns the `ReactiveProperty<InteractState>`, the `_claimedShowStates` set + `_showReevaluators` list (verbatim from today's `PuiButton`), and the **composite** logic (§4.2). API the `Pui*` subclasses call:

- `SetTransient(InteractState transient)` — store the transient state (Normal/Hover/Pressed/Disabled), recompute composite, push + re-drive reevaluators.
- `SetOn(bool isOn)` — store the checked flag, recompute composite, push + re-drive reevaluators.
- plus the four `IStateSource` members the subclass delegates to.
- `static InteractState MapTransient(int selectionStateOrdinal)` — the `SelectionState → InteractState` switch (moved here from `PuiButton.Map(int)`; folds navigation-`Selected` → `Normal`). Lives here as a static taking an **int ordinal** so a non-`Selectable` class can hold it without the CS0051 protected-type problem; the subclass passes `(int)state`.

Both `SetTransient` and `SetOn` re-drive every registered reevaluator (today only `DoStateTransition` does), so an `isOn` change updates `<Show>` blocks the same way a hover does.

### 4.2 State model — `InteractState` and the composite rule

Rename `BtnState` → **`InteractState { Normal, Hover, Pressed, Selected, Disabled }`** (public; see §8).

Two inputs, one output stream:
- **Transient** from `DoStateTransition`: Normal / Hover / Pressed / Disabled. uGUI's navigation-`Selected` `SelectionState` folds to `Normal` (unchanged from Btn — that is keyboard focus, *not* checked).
- **Checked** from `Toggle.isOn` (Toggle family only).

**Composite** `Current` (in `StateBroadcaster`):

```
Current = (transient == Normal) ? (isOn ? Selected : Normal) : transient
```

In words: **`Selected` is the resting baseline of an active control.** An active Tab/Toggle at rest broadcasts `Selected`; hovering/pressing it transiently overrides to `Hover`/`Pressed` and reverts to `Selected` on release; `Disabled` (transient) wins over everything. `Btn` never sets `isOn`, so its broadcaster never emits `Selected` — Btn's observable behaviour is unchanged.

The always-visible "active" indicator stays on the **independent** `Toggle.graphic` channel (Tab's overlay, Toggle's checkmark), driven by `isOn` outside the broadcast — so active-ness is never lost during hover; only the base tint / `<Show>` subtree reverts transiently. `Current` is therefore exactly one value at any instant, which keeps `<Show>`'s mutual-exclusion + Normal-fallback logic unchanged (§4.7).

### 4.3 `Pui*` subclasses

**`PuiButton : Button, IStateSource`** — refactored to hold a `StateBroadcaster` and delegate the interface to it. Override shrinks to:

```csharp
protected override void DoStateTransition(SelectionState s, bool instant)
{
    base.DoStateTransition(s, instant);                       // default targetGraphic ColorTint preserved
    _broadcaster.SetTransient(StateBroadcaster.MapTransient((int)s));
}
```

**`PuiToggle : Toggle, IStateSource`** (new, `Runtime/Controls/Internal/`) — same `DoStateTransition` override, plus it feeds the broadcaster from its checked state via an explicit init the control calls in `OnAttached` right after `AddComponent<PuiToggle>()` (no reliance on a uGUI lifecycle override; mirrors how `Tab`/`Toggle` already wire `onValueChanged` there):

```csharp
internal void InitStateBroadcast()
{
    onValueChanged.AddListener(_broadcaster.SetOn);
    _broadcaster.SetOn(isOn);   // seed the checked dimension
}
```

`PuiToggle` serves **both** `Tab` and `Toggle`. `SimulateState(int)` stays on each `Pui*` subclass (it calls the protected `DoStateTransition`); the `MapTransient` switch moves to `StateBroadcaster`.

### 4.4 Consumers retargeted (type change only, no logic change)

- `TriggerSourceResolver.FindStateSource` → returns `IStateSource`; ancestor walk `GetComponentInParent<IStateSource>()`; `@id` form does `ctrl.GameObject.GetComponent<IStateSource>()` (Unity's `GetComponentInParent`/`GetComponent` resolve interfaces). Error text generalized from "`<Btn>`" to "`<Btn>`/`<Tab>`/`<Toggle>`" (state-source) wording.
- `StateTintReactor.EnsureInit` → `GetComponentInParent<IStateSource>()`.
- `Show._pui` → `IStateSource _src`; `RegisterShow` / `Current` / `IsShowStateClaimed` calls unchanged.
- `Trigger.SubscribeState` → resolves `IStateSource`, adds the `StateSelected → Selected` case (§4.7).

`<Show>`'s `ReevaluateVisibility` (exact-match OR Normal-fallback for an unclaimed `Current`) is **unchanged** — `Current` may now be `Selected`, and a `state-normal` block falls back for it when no `state-selected` sibling exists.

### 4.5 Tint installer extraction + `selectedColor`

Extract `Btn.ApplyStateTint` / `CollectBlocked` / `InstallReactor` into a shared **`StateTintInstaller`** (internal static, `Runtime/Controls/Internal/`) called by `Btn`, `Tab`, `Toggle` with `(rootGameObject, children, hover, pressed, selected, disabled)`:

- Fan-out range, opt-out (`stateReact="false"`), idempotent re-`Configure` on Variant ReSolve, "switch transition to `None` when any `*Color` present" — all verbatim from today's `Btn`.
- **Boundary generalized**: the nested-source skip changes from `control is Btn` to "the child's GameObject carries an `IStateSource`" (`child.GameObject.GetComponent<IStateSource>() != null`). A nested Btn/Tab/Toggle (any state source) owns its own subtree's fan-out — fully generic, no per-type list.

`StateTintReactor` gains a `_selected` multiplier: `Configure(hover, pressed, selected, disabled, fade)`; `MultiplierFor(Selected) => _selected`. Btn passes `selected: null` (white identity — and it never emits `Selected` anyway); Tab/Toggle pass their `selectedColor`.

### 4.6 Per-control wiring

| Control | Change |
|---|---|
| `Btn` | Retarget only: `_btn` is still `PuiButton` (now `IStateSource`); `OnState` returns `_btn.OnState` (`Observable<InteractState>`). Delegate tint install to `StateTintInstaller`. No new attrs (already has `hoverColor`/`pressedColor`/`disabledColor`; no `selectedColor`). |
| `Tab` | Swap `UnityToggle` → `PuiToggle`. Add `[UIAttr(IsColor=true)]` `HoverColor` / `PressedColor` / `DisabledColor` / `SelectedColor`. Add `public Observable<InteractState> OnState => _toggle.OnState;`. New `OnAfterApply`: bridge `_toggle.interactable = Interactable`, then `StateTintInstaller` install (sets `transition = None` when any `*Color`). `selectedSprite` overlay unchanged. |
| `Toggle` | Same as Tab: swap to `PuiToggle`; add the four `*Color` attrs + `OnState`; new `OnAfterApply` (interactable bridge + tint install). Checkmark (`Toggle.graphic`) unchanged — it is the independent `Selected` indicator. |

`interactable` and `stateReact` are already common `[UIAttr]`s on `Control` (`Control.cs:23,33`), so no per-control attribute declarations are needed for those — only the `OnAfterApply` bridge so `Control.Interactable` (CanvasGroup) also drives `Selectable.interactable` → `Disabled` broadcast (mirrors `Btn.OnAfterApply`).

### 4.7 `state-selected` trigger + `<Show>`

- `TriggerKind` += `StateSelected`; `TriggerSpec.Parse` += `state-selected` and `state-selected@<id>`.
- `Trigger.SubscribeState` and `Show.InitTriggerSubscription` += `StateSelected → InteractState.Selected`.
- `<Show on="state-selected">` is valid; the four-value valid-`on` set on `<Show>` becomes five.
- A `state-selected` trigger whose resolved source can never be `Selected` (e.g. a `<Btn>` source) simply never fires — no error; Normal-fallback covers `<Show>`.

## 5. Errors & Lint

- `StateTriggerRules`: the bare-state set `BareStateValues` += `state-selected`.
- The "state-source ancestor" notion in `IRWalker` / `StateTriggerRules` generalizes from `node.Tag == "Btn"` to a small static **`StateSourceTags`** set `{ "Btn", "Tab", "Toggle" }` (one-line to extend per future control — §7). `PUI-STATE-NO-SOURCE` message: "no `<Btn>`/`<Tab>`/`<Toggle>` ancestor."
- `<Show on="click">` (non-state) error unchanged.

## 6. XSD

- `Tab` / `Toggle` are **reflected** (`XsdGenerator.ReflectControlAttrs`), so their new `*Color` `[UIAttr]`s appear automatically — no manual edit for them.
- Manual edits: add `state-selected` (and `state-selected@…` pattern) to the `on=` enumeration; keep `*Color` off the hand-listed `<Btn>` block except the existing three (no `selectedColor` on Btn).

## 7. Extension points (for deferred controls)

A future `Slider` / `Dropdown` / `InputField` needs only:
1. a ~3-line `Pui<Control> : <Selectable>, IStateSource` subclass forwarding `DoStateTransition` to a `StateBroadcaster` (Toggle-family ones also `SetOn`; focus-based ones may map focus → `Selected`);
2. swap the control's plain Selectable for it, add the `*Color` `[UIAttr]`s (reflected → XSD-free) + `OnState`, and an `OnAfterApply` interactable bridge + `StateTintInstaller` call;
3. add the control's tag to `StateSourceTags` (lint, one line).

No change to the abstraction, resolver, reactor, installer, triggers, or `<Show>`. **Slider note**: its transient states fit a drag handle perfectly (uGUI holds `Pressed` for the whole drag → `pressedColor`/`state-pressed` cover the drag); `Selected` is inert (no `isOn`); the only authoring nuance — tint the whole track+fill+handle vs. just the handle — is already covered by the existing `stateReact="false"` opt-out.

## 8. Naming

`BtnState` (public enum) → **`InteractState`**. `Btn.OnState` keeps its name and now returns `Observable<InteractState>`; `Tab.OnState` / `Toggle.OnState` added with the same type. The `<Btn>`-centric strings in `TriggerSourceResolver` / `StateTriggerRules` generalize to "state source (`<Btn>`/`<Tab>`/`<Toggle>`)". No back-compat alias (project early).

## 9. Testing (TDD — red first)

EditMode (`PromptUGUI.Tests.EditMode`):
- `StateBroadcaster`: composite rule — `SetOn(true)` with transient Normal → `Selected`; transient Hover/Pressed/Disabled overrides Selected; `SetOn(false)` → Normal; `MapTransient` ordinals (incl. navigation-Selected→Normal).
- `PuiToggle`: `DoStateTransition` pushes transient; `onValueChanged` pushes `SetOn`; `Awake` seeds `isOn`.
- Consumers resolve via `IStateSource`: a `<Show>` / `<Trigger on="state-*">` inside a `<Tab>` and inside a `<Toggle>` finds the toggle as source (no throw); `@id` resolves a Tab/Toggle.
- Tint fan-out on Tab/Toggle: `pressedColor`/`selectedColor` install reactors on bg + descendants; `stateReact="false"` opts out; nested state source (Btn in Tab) is a boundary; `transition` → `None` only when a `*Color` is present; `selectedColor` applied when `isOn` true at rest.
- `interactable="false"` on Tab/Toggle → `Disabled` broadcast + `disabledColor`.
- `<Show on="state-selected">`: visible iff the source `Selected`; reverts to `state-normal`/`state-hover` block on hover; Normal-fallback covers Selected when no `state-selected` block.
- `state-selected` parse (with/without `@id`); lint `PUI-STATE-NO-SOURCE` ancestor set includes Tab/Toggle; `state-selected` in `BareStateValues`.
- Btn regression: enum rename compiles; Btn `OnState` unchanged; Btn never emits `Selected`.

PlayMode (`PromptUGUI.Tests.PlayMode`):
- Pointer-driven hover/press over a composed `<Tab>` flips child tint + `<Show>` artwork and reverts; toggling the Tab on flips the `state-selected` subtree / `selectedColor` while the `selectedSprite` overlay stays visible throughout.

## 10. Skill updates (same PR, English)

- `authoring-promptugui-xml`: `<Tab>` and `<Toggle>` attribute tables += `hoverColor` / `pressedColor` / `selectedColor` / `disabledColor`; `state-selected` added to the `on=` table (note: meaningful only on `isOn`-backed sources); `PUI-STATE-NO-SOURCE` ancestor list extended to `<Tab>` / `<Toggle>`; `<Show>` valid-`on` set += `state-selected`.
- `scripting-promptugui-csharp`: `BtnState` → `InteractState` (incl. new `Selected` value + composite-baseline note); `Tab.OnState` / `Toggle.OnState` added alongside `Btn.OnState`.

## 11. Risks & rollback

| Risk | Mitigation |
|---|---|
| `BtnState → InteractState` rename misses a reference | Compile-driven: rename is mechanical; EditMode/PlayMode + XSD generator all reference it and will fail to compile if missed. |
| `IStateSource` via interface `GetComponentInParent` perf/allocation | Same call shape as today's `GetComponentInParent<PuiButton>()`; interface resolution is supported and used elsewhere (no measurable change). |
| `isOn`-seed ordering vs. `<Show>` registration | `StateBroadcaster.SetOn` re-drives all reevaluators (like `DoStateTransition`); the control's `OnAfterApply` (post child-recursion) is the final seed, after every descendant `<Show>` has registered — mirrors the existing Btn ordering. |
| Tab/Toggle `transition` was ColorTint (Tab explicit, Toggle default); switching to `None` only when `*Color` present | Back-compat: with no `*Color`, transition is untouched (Tab keeps explicit ColorTint, Toggle keeps default). Covered by a no-`*Color` regression test. |
| Composite hides "active + hover simultaneously" in the tint stream | By design (§3 non-goal). The independent overlay/checkmark keeps showing active during hover; compose-mode deferred. |
