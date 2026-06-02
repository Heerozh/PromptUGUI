# State Colours — split into absolute `*Color` + relative `*Modulate`

**Status**: Proposed (design approved; spec under review)
**Spec date**: 2026-06-02
**Branch**: `feat/state-color-absolute-modulate`
**Depends on**: Btn state-visuals (`2026-05-30-btn-state-visuals-design.md`), Clickable state-visuals (`2026-05-30-clickable-state-visuals-design.md`) — `PuiButton` / `PuiToggle` / `InteractState` / `StateTintReactor` / `StateTintInstaller` / `<Show>` / `state-*` triggers / `stateReact`
**Skill impact**: `authoring-promptugui-xml` (rewrite the **Btn state visuals** section + the `<Btn>` / `<Tab>` / `<Toggle>` attribute-table rows: `*Color` becomes **absolute**, new `*Modulate` family is the relative multiplier; `stateReact` opts out of `*Modulate` fan-out). `scripting-promptugui-csharp` only if it references `*Color` semantics (audit; `OnState` / `InteractState` unchanged).

---

## 1. Motivation

`<Btn>` / `<Tab>` / `<Toggle>` expose `hoverColor` / `pressedColor` / `selectedColor` / `disabledColor`. Today these are **colour multipliers** (uGUI ColorTint semantics: `graphic.color = base * stateColor`, Normal = white identity), fanned out to the bg + every descendant graphic. That was chosen to mirror uGUI and to make a single value tint a whole composed control coherently (see `2026-05-30-btn-state-visuals-design.md` §4.2).

The multiply model has two problems that surfaced in real use:

1. **The name lies.** Everywhere else in the language a `*Color` attribute (`color`, `fillColor`, `bgColor`, `char-color`…) is an **absolute set**. `selectedColor` looking like "set the colour when selected" but meaning "multiply by" is a genuine trap — an author writing `selectedColor="primary"` against a dark base gets `base × primary` (darker than either), never `primary`.
2. **Multiply can't brighten, so it can't express a theme palette.** Design systems define absolute tokens (`primary` / `primary-light` / `primary-lighter`). "Selected → `primary-light`" is an absolute pick. Multiply (hex factors ≤ 1) can only darken, so it structurally cannot reach a brighter token from a darker base. The palette/per-state-absolute model and the multiply model are different paradigms, and the multiply-only system left the absolute case with no first-class expression (only `<Show>` artwork swaps or C#).

Both models are legitimate and wanted:

- **Relative tint that fans out** — "dim the whole button 20 % while held" — naturally one factor over bg + label + icon. (The current behaviour; keep it.)
- **Absolute per-state colour** — "selected tab bg = `primary-light`" — a set, on the bg only. (New.)

This spec keeps both, but gives each the *honest* name: `*Color` = absolute (matching `color`), `*Modulate` = relative multiplier (matching Godot's `modulate`: a Color that multiplies a node and cascades to its children — exactly the fan-out semantics).

## 2. Goals

- `hoverColor` / `pressedColor` / `selectedColor` / `disabledColor` become **absolute** per-state colours, applied to the control's base graphic (`Selectable.targetGraphic`, the same graphic `color` writes to). `*Color` now means the same thing everywhere in the language.
- New `hoverModulate` / `pressedModulate` / `selectedModulate` / `disabledModulate` family carries the **relative multiplier**, fanned out to bg + every descendant graphic — i.e. exactly the current `*Color` behaviour, renamed.
- The two compose: per state, `displayed = (absolute ?? base) × (modulate ?? white)`.
- Values accept the same forms as `color` (hex / CSS named / theme token), resolved through `UI.Theme.Resolve`, re-resolved on Variant ReSolve.
- `stateReact="false"` continues to opt a subtree out of the **`*Modulate` fan-out** (absolutes are bg-only and never fanned out, so they are unaffected).
- Reuse `StateTintReactor` / `StateTintInstaller` / `InteractState` — no new components, no change to `OnState`, `<Show>`, or `state-*` triggers.

## 3. Non-goals

- **No backward compatibility.** This is a deliberate same-name semantic flip (`*Color` multiply → absolute) that cannot be detected or warned about (a hex string is valid under both). Repo `.ui.xml` uses **zero** of these attributes today; only the consumer's own game XML needs a one-time sweep. Clean break, no deprecation shim. (User-approved.)
- **No `selectedColor` / `selectedModulate` on `<Btn>`.** A momentary button has no `isOn` → never emits `Selected` (unchanged from today).
- **Absolute `*Color` does not fan out.** Setting bg + label + icon to one absolute colour would make the label/icon vanish (same colour as bg). Absolute is **bg-only** (`targetGraphic`). For per-state absolute recolouring of multiple graphics, `<Show>` swap remains the tool. (This asymmetry is intrinsic and is the reason multiply was the original whole-subtree choice.)
- **No compose-mode across states** (`base × selected × hover` simultaneously) — unchanged from `2026-05-30-clickable-state-visuals-design.md` §line36; `InteractState.Current` is still exactly one value.
- `tint` (multiply-vs-linear-light **material**) is untouched and orthogonal.

## 4. Design

### 4.1 Two attribute families

| Family | Meaning | Scope | Resolved value |
|---|---|---|---|
| `hoverColor` / `pressedColor` / `selectedColor` / `disabledColor` | **Absolute** colour for that state | `Selectable.targetGraphic` only (the bg `color` targets) | `UI.Theme.Resolve` |
| `hoverModulate` / `pressedModulate` / `selectedModulate` / `disabledModulate` | **Relative multiplier** (Godot `modulate`) | `targetGraphic` **+ every descendant Graphic** (fan-out), minus `stateReact="false"` subtrees and nested `IStateSource` subtrees | `UI.Theme.Resolve` |

`color` is unchanged: the absolute **Normal** base of `targetGraphic`. There is intentionally no `normalColor` / `normalModulate` — Normal is `color × white = color`.

### 4.2 Composition model

For the base graphic (`targetGraphic`), per state `s`:

```
displayed(s) = (absolute[s] ?? colorBase) × (modulate[s] ?? white)
```

- `colorBase` = the `color` attribute value (captured once as the reactor's base, as today).
- `absolute[s]` = the state's `*Color`, or null.
- `modulate[s]` = the state's `*Modulate`, or null.
- Normal: `absolute` and `modulate` are both absent ⇒ `displayed = colorBase`.

For every other (descendant) graphic, per state `s`:

```
displayed(s) = childBase × (modulate[s] ?? white)
```

Descendants never receive `absolute[s]`.

Worked examples:

```xml
<!-- relative tint only (today's behaviour, renamed): whole subtree dims on press -->
<Btn color="primary" pressedModulate="#cccccc">…</Btn>

<!-- absolute only: selected tab bg flips to the brighter palette token -->
<Tab color="primary-darker" selectedColor="primary"/>

<!-- both: selected bg becomes primary, then the whole subtree dims while held -->
<Tab color="primary-darker" selectedColor="primary" pressedModulate="#cccccc"/>
```

### 4.3 Reactor + installer changes

`StateTintReactor` currently holds four `Color` multipliers (`_hover/_pressed/_selected/_disabled`, white default) and computes `target = _baseColor * MultiplierFor(state)`. Extend it to also hold four **absolute** `Color?` per state and compute:

```csharp
Color BaseFor(InteractState s)  => Absolute(s) ?? _baseColor;   // null ⇒ keep captured base
Color target = BaseFor(state) * MultiplierFor(state);           // MultiplierFor: white default
```

- `Configure` takes both sets. To avoid an 9-parameter signature, introduce a tiny readonly struct `StateColorSet { Color? Hover, Pressed, Selected, Disabled; }` (internal, `Runtime/Controls/Internal/`) and call `Configure(StateColorSet absolutes, StateColorSet modulates, float fade)`.
- Base-capture-once invariant is unchanged: `_baseColor` is still captured from `graphic.color` on first init (= `color`). Absolutes/modulates are (re)passed via `Configure`, so a Variant ReSolve re-resolving a token never promotes a tinted colour into the base.

`StateTintInstaller.Install` signature changes to take both families (resolved strings → two `StateColorSet`s). New install rule:

- `hasAny` = any absolute **or** any modulate present (across the four states) → set `selectable.transition = None` (single source of truth, as today).
- The graphic equal to `selectable.targetGraphic` → `Configure(absolutes, modulates, fade)`.
- Every other non-blocked descendant Graphic → `Configure(default /*all null*/, modulates, fade)`.
- `blocked` set (— `stateReact="false"` subtrees + nested `IStateSource` subtrees) is computed exactly as today and applies to the modulate fan-out.

Note `targetGraphic` is **never** in `blocked` (it is the control's own bg; `stateReact` only prunes descendant subtrees), so the absolute always reaches it.

### 4.4 Per-control surface

| Control | Absolute `*Color` | `*Modulate` | Notes |
|---|---|---|---|
| `<Btn>` | `hoverColor` / `pressedColor` / `disabledColor` | `hoverModulate` / `pressedModulate` / `disabledModulate` | no `selected*` (no `isOn`) |
| `<Tab>` | + `selectedColor` | + `selectedModulate` | `targetGraphic = _bg` (root) |
| `<Toggle>` | + `selectedColor` | + `selectedModulate` | `targetGraphic = _bg` (the **Background child**, not root) — absolutes target it via `targetGraphic`, confirming the cross-control rule |

Each control: rename the four `[UIAttr(IsColor=true)]` setters that currently store the multiplier strings so they store the **modulate** strings (`HoverModulate` etc., XML `hoverModulate`), and add four new `[UIAttr(IsColor=true)]` absolute setters (`HoverColor` etc., XML `hoverColor`). `OnAfterApply` passes both sets to `StateTintInstaller.Install`.

### 4.5 Breaking rename + migration

- Mechanical rename in `Btn.cs` / `Tab.cs` / `Toggle.cs`: the old `_hoverColor/_pressedColor/_selectedColor/_disabledColor` fields + their `[UIAttr]` `*Color` setters become the `*Modulate` fields + setters; four fresh `*Color` (absolute) fields + setters are added.
- Tests that exercise the multiply math (`ImageTintTests` cross-control rows, `BtnStateTests`, `TabStateTests`, `ToggleStateTests`, PlayMode `*StateVisualsPlayTests`) currently write `pressedColor='#808080'` and assert `base*half`. Rename those literals to `pressedModulate=…` to keep the multiply assertions; add new tests for the absolute `*Color` path.
- XSD is reflection-driven → both families appear automatically; `XsdGeneratorTests` substring assertions for `selectedColor` still hold (attr still present, now absolute) — add an assertion that `selectedModulate` is also emitted.
- Consumer game XML: out of repo; offer a grep sweep (`*Color` wanting multiply → `*Modulate`; ones that wanted absolute stay as-is, now correct).

## 5. Testing (TDD, EditMode-first, via Unity MCP)

Red→green per behaviour:

1. **Absolute selected** — `<Tab color='#202020' selectedColor='#076DD7'>`, drive Selected, assert `targetGraphic.color == #076DD7` (not `#202020 × #076DD7`).
2. **Absolute does not fan out** — `<Tab selectedColor='#ff0000'><Text id='l'>x</Text></Tab>`: on Selected, `targetGraphic.color == #ff0000` but the label keeps its base colour.
3. **Modulate preserves old behaviour** — `<Btn color='#ffffff' pressedModulate='#808080'><Image id='c'/>`: on Pressed, bg and child both ≈ `base × 0.5` (the migrated current test).
4. **Compose** — `selectedColor='#076DD7' selectedModulate='#808080'` ⇒ Selected bg ≈ `#076DD7 × 0.5`.
5. **Fallback** — a state with neither attribute returns `targetGraphic` to `color` and descendants to their base.
6. **No attrs ⇒ stock ColorTint untouched** (regression: `transition` stays ColorTint).
7. **`stateReact='false'`** prunes a subtree from modulate fan-out; absolute on bg unaffected.
8. **Variant ReSolve** re-resolves a token in `selectedColor` / `selectedModulate` without re-capturing base.
9. **XSD**: `<Tab>` / `<Toggle>` emit both `selectedColor` and `selectedModulate`; `<Btn>` emits neither `selected*`.

Then full EditMode + EditorOnly suites; PlayMode for the pointer-driven hover/press/select fan-out + revert.

## 6. Skill impact (same PR)

`authoring-promptugui-xml`:
- **Btn state visuals** section §1: `*Color` = absolute per-state (bg/`targetGraphic` only); `*Modulate` = relative multiplier fanned out to bg + descendants; the `(absolute ?? base) × (modulate ?? white)` model; `stateReact="false"` opts out of `*Modulate` fan-out.
- `<Btn>` / `<Tab>` / `<Toggle>` attribute-table rows: replace the multiplier description of `*Color` with absolute; add the `*Modulate` family.
- Quick-reference cheatsheet `BTN STATE` / `TAB/TOGGLE` lines updated.

`scripting-promptugui-csharp`: audit for `*Color` multiplier wording; update if present. `OnState` / `InteractState` / `Tab.OnState` / `Toggle.OnState` unchanged.

## 7. Alternatives considered

- **Keep `*Color` = multiply, add `*ColorAbsolute` / `*ColorSet` for the new absolute.** Non-breaking, but perpetuates the misleading name the whole change is meant to fix; rejected (user).
- **Name the multiplier `*ColorMultiplier`.** Explicit, but collides verbally with uGUI `ColorBlock.colorMultiplier`; rejected (user).
- **Name it `*Shade` / `*Wash`.** `*Shade` reads as an absolute "darker colour" (re-introducing the set/multiply ambiguity); `*Wash` is obscure. `*Modulate` matches Godot's established term for *multiply-and-cascade-to-children*, which is exactly this semantic.
- **Make absolute fan out too.** Breaks composed controls (label/icon adopt the bg colour). Absolute is intrinsically single-graphic.

## 8. Risks / open questions

- **Silent breakage of consumer XML** (same-name flip). Mitigation: documented clean break + migration grep; repo itself is unaffected (0 usages).
- **Toggle `targetGraphic` is a child**, so the "absolute → `targetGraphic`" rule (not "→ root graphic") must be implemented carefully; covered by a Toggle-specific absolute test.
- **`StateColorSet` struct** adds a small internal type; acceptable vs. an 9-arg `Configure`.
- Open: should `disabledColor` absolute also drive the CanvasGroup-disabled path? No — `interactable=false` already routes to `InteractState.Disabled`; the reactor handles it like any other state. No extra wiring.
