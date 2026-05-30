# Btn State-Driven Visuals Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `<Btn>` broadcasts its uGUI `Selectable` state so descendants react — tint fan-out (`*Color`), per-state artwork switching (`<Show>`), and state-driven animation (`on="state-*"`) — plus `Btn.OnState` for C#.

**Architecture:** Replace `Btn`'s plain `Button` with an internal `PuiButton : Button` that overrides `DoStateTransition` to publish a `BtnState` over an R3 `ReactiveProperty`. Three reaction surfaces consume it: per-graphic `StateTintReactor` MonoBehaviours (fan-out, LitMotion fade), the existing `Trigger`/`Animation` `on=` system extended with `state-*` kinds (resolved **upward** to the nearest `<Btn>` ancestor), and a new `Show : Trigger` control that `SetActive`-toggles its subtree by state. Strictly additive — a `<Btn>` with none of the new attrs/children behaves as today.

**Tech Stack:** Unity 6 uGUI + TMP; C# 9; R3 (Cysharp); LitMotion; pure-C# lint (`Runtime/Core/Lint`); tests via UnityMCP (EditMode + PlayMode).

- **Date:** 2026-05-30
- **Spec:** `docs~/superpowers/specs/2026-05-30-btn-state-visuals-design.md`
- **Branch:** `feat/btn-state-visuals` (created; spec + this plan committed). **DO NOT commit to main.**

---

## MCP test-running conventions (every "run tests" step uses these)

Tests run **only** via UnityMCP, never batch-mode. After any source edit: **refresh → read console for compile errors → run tests**. Load tools first: `ToolSearch(query="select:refresh_unity,run_tests,read_console,get_test_job", max_results=4)`.

- Refresh: `refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
- Compile check: `read_console(action="get", types=["error"])` → expect empty before running tests.
- **The `run_tests` param shape varies by MCP-server version — load the live schema first** via `ToolSearch(query="select:run_tests,get_test_job", max_results=2)` and use exactly what it exposes. Two known shapes: (a) `mode` + `test_filter` (substring/regex) + `assembly_names`, **synchronous** (returns the result dict directly — this session); (b) `mode` + `assembly_names` + `group_names=[".*X.*"]` (regex), **async** (returns `job_id` → poll `get_test_job`). Where a task below writes `filter="X"` it is **shorthand for "narrow to that class"** — map it to whichever param the live schema has (`test_filter="X"` or `group_names=[".*X.*"]`). ⚠️ Never pass a bare class name to a `test_names`-style param — it can match 0 tests and look "passed" while running nothing. Never report pass/fail without the actual result payload.
- **EditMode requires the Editor NOT in Play Mode.** On `"Cannot start a test run while the Editor is in or entering Play Mode."` → `manage_editor(action="stop")`, then rerun.
- **First call `refresh_unity` to confirm MCP is actually connected.** If it errors/times out (Unity not open), STOP and report "MCP unavailable" + the raw error — **do not fabricate test results**. Retry-reconnect once per CLAUDE.md.
- **Unexpected `LogError` fails an EditMode test; `LogWarning` does not.** Tests that build a sourceless `state-*` (Task 5.1) or otherwise expect a warning must declare it with `LogAssert.Expect(LogType.Warning, new Regex(...))`, mirroring the existing Tab tests.
- After `.ui.xml`/lint changes: `dotnet run --project .lint/UIXmlLint -- <path>`. After C# edits: `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx` (never `analyzers --severity info` — see CLAUDE.md guardrails).

---

## Grounding facts (verified against current source)

- `Control` (`Runtime/Controls/Control.cs`) base members tasks rely on: `Id`, `GameObject`, `RectTransform`, `Children : IReadOnlyList<IControl>`, `ScopedIds`, `OnAttached()`, `internal virtual OnAfterApply()` (called by `ControlAttributeApplier` after `ApplyCommon`; `Trigger`/`Animation`/`SafeArea` already override it), `Dispose()`. There is **no** existing `stateReact` member — Task 2.4 adds it.
- `ProceduralBuilders` (`Runtime/Controls/Internal/`) helpers: `AddImage(RectTransform parent, string name, bool raycast=true)`, `AddText(RectTransform, string)`, `DefaultBtnColor`, `ApplyDefaultSlicedSprite`.
- Builtins are registered in `Runtime/Application/BuiltinPrimitives.cs` (`reg.Register<T>("Tag", null)`); `UI.ResetForTests()` rebuilds from there, so a new `<Show>` builtin (Task 4.4) is auto-visible to tests + XSD.
- Lint: rule classes in `Runtime/Core/Lint/` (e.g. `TabRules`, `MaskAttributeRules`) return `IEnumerable<LintIssue>` (`LintIssue(code, tag, id, message)`); dispatched per-tag from `IRWalker.WalkNode` (CLI, `yield`) and `ScreenInstantiator` (runtime, `Debug.LogWarning`). The new `state-*`-no-source rule needs **ancestor context** (is there a `<Btn>` ancestor?), so it threads a flag through `IRWalker.WalkNode` like the existing `inTemplateBody` / Tab-parent check.
- `Trigger` (`Runtime/Controls/Trigger.cs`): `on=` → `TriggerSpec.Parse`; `InitTriggerSubscription()` switches on `TriggerKind`; `Fire()`/`protected virtual OnTriggerFired()`; source resolution **downward** via `TriggerSourceResolver.FindBtn`/`FindPointerSource`. `Animation : Trigger` overrides `OnTriggerFired` to play LitMotion. `TriggerSpec` (`Internal/`): `enum TriggerKind { Open, Loop, Click, Manual, HoverEnter, HoverExit, Press }`, `@id` via `s_prefixedKinds`.

---

## Context & Constraints

- `Btn` lives in `Runtime/Controls/Btn.cs` — wraps `UnityImage _bg` + uGUI `Button` (`targetGraphic=_bg`, default ColorTint), lazy `Label`, `PointerEventRelay`, R3 `OnClick`. This plan makes it a **state broadcaster** so descendants can react to Normal/Hover/Pressed/Disabled.
- Trigger/Animation system: `Runtime/Controls/Trigger.cs` (base, `on=` → `TriggerSpec`, `Fire()`/`OnTriggerFired()`, source resolution **downward** via `TriggerSourceResolver`), `Runtime/Controls/Animation.cs` (`: Trigger`, overrides `OnTriggerFired` to play LitMotion), `Runtime/Controls/Internal/TriggerSpec.cs` (`TriggerKind` enum + `Parse`, `@id` via `s_prefixedKinds`).
- **TDD throughout.** Red test first, watch it fail via Unity MCP `run_tests`, then implement to green. Never batch-mode Unity. After each source edit: `refresh_unity(compile="request", mode="force")` then `read_console(types=["error"])` before running tests.
- EditMode test classes touching `UI` call `UI.ResetForTests()` in `[SetUp]`/`[TearDown]`; built-ins are pre-registered there — so a new `<Show>` builtin must be registered (Task 4.4) for tests to see it.
- **Do not use .NET threading** — `Awaitable` / R3 only. `Btn.OnState` is R3 `Observable<BtnState>`.
- Every functional change is mirrored in the SKILL files in the SAME PR, in English (CLAUDE.md rule) — Phase 7.
- After any `.ui.xml` or lint-rule change, run the UIXmlLint CLI; after C# edits, run `dotnet format --verify-no-changes --severity warn` (see CLAUDE.md guardrails — never `analyzers --severity info`).
- **Do NOT commit to main.** Work on `feat/btn-state-visuals`.
- Public API touched: `Btn.OnState`, `BtnState`, `<Show>`, `state-*` `on=` values, `<Btn>` `*Color`/`interactable`/`stateReact` attrs.

## Phases

### Phase 1: State source — `PuiButton` + `BtnState` broadcast

**Goal:** `<Btn>` drives an internal `Selectable` subclass that publishes its state as `Btn.OnState`; back-compat default visuals untouched.

#### Task 1.1: Red test — state mapping + broadcast
- **What:** In `Tests/EditMode/BtnTests.cs` (or new `BtnStateTests.cs`), add `Btn_DoStateTransition_MapsAndBroadcasts()`. Build a `<Btn>`, subscribe to `OnState`, drive the internal `PuiButton.DoStateTransition` through Normal/Highlighted/Pressed/Disabled/Selected, assert emitted `BtnState` = Normal/Hover/Pressed/Disabled/**Normal** (Selected→Normal).
- **How to verify:** `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="BtnState")` — fails to compile/red.
- **Dependencies:** none

#### Task 1.2: `BtnState` enum + `PuiButton`
- **What:** New `Runtime/Controls/BtnState.cs` (public `enum BtnState { Normal, Hover, Pressed, Disabled }`). New `Runtime/Controls/Internal/PuiButton.cs` (`internal sealed class PuiButton : UnityEngine.UI.Button`) overriding `DoStateTransition(SelectionState, bool)`: call `base`, then push `Map(state)` into an R3 `ReactiveProperty<BtnState>` it owns. Expose `Observable<BtnState> OnState` and `BtnState Current` on `PuiButton`. `Map`: Normal→Normal, Highlighted→Hover, Pressed→Pressed, Disabled→Disabled, Selected→Normal.
- **How to verify:** compiles (`refresh_unity` + `read_console`); test still red on the `Btn.OnState` member.
- **Dependencies:** 1.1

#### Task 1.3: Wire `Btn` → `PuiButton`, expose `OnState`
- **What:** In `Btn.cs` `OnAttached`, replace `GameObject.AddComponent<Button>()` with `AddComponent<PuiButton>()` (keep `_btn` typed as `Button` or `PuiButton`). Add `public Observable<BtnState> OnState => _puiBtn.OnState;`. Seed/re-emit the initial state in `OnAfterApply` (after subtree applied) so reactors/Show get a first value. Default `transition` stays ColorTint on `_bg` (back-compat).
- **How to verify:** Task 1.1 test passes (green).
- **Dependencies:** 1.2

#### Task 1.4: Red test + impl — `interactable` attribute
- **What:** Red test `Btn_Interactable_False_BroadcastsDisabled()`. Then add `[UIAttr] public bool Interactable { set => _btn.interactable = value; }` (default true) to `Btn.cs`. Setting false → `Selectable` enters Disabled → `OnState` emits `Disabled`.
- **How to verify:** `run_tests(... filter="BtnState")` green.
- **Dependencies:** 1.3

---

### Phase 2: Tint fan-out — `hoverColor` / `pressedColor` / `disabledColor`

**Goal:** State colour multipliers fan out to bg + all descendant graphics, with LitMotion fade and per-child opt-out.

#### Task 2.1: Red test — fan-out install + restore
- **What:** Red test `Btn_PressedColor_InstallsReactorsOnSubtree()`: build `<Btn pressedColor="#808080"><Image/><Text/></Btn>`, assert a `StateTintReactor` exists on bg + the Image + the Text's graphic; drive Pressed → each graphic's `color == base * #808080`; drive Normal → restored to base.
- **How to verify:** `run_tests(... filter="BtnState")` red.
- **Dependencies:** 1.3

#### Task 2.2: `StateTintReactor`
- **What:** New `Runtime/Controls/Internal/StateTintReactor.cs` (`MonoBehaviour`): on enable, `GetComponentInParent<PuiButton>()`, cache its `Graphic.color`, subscribe to `OnState`, lerp `base → base * multiplier[state]` over `fadeDuration` (default 0.1f) via LitMotion. Multipliers settable by the installer; Normal multiplier defaults white. Dispose subscription on destroy.
- **How to verify:** compiles; 2.1 still red (installer not wired).
- **Dependencies:** 2.1

#### Task 2.3: `Btn` `*Color` attrs + fan-out installer + transition switch
- **What:** In `Btn.cs` add `[UIAttr(IsColor=true)] HoverColor / PressedColor / DisabledColor`. When any is set, in `OnAfterApply`: set `_btn.transition = Transition.None`, walk the subtree, add a `StateTintReactor` to `_bg` and every descendant `Graphic` (skip nodes with `stateReact="false"`), feed the multipliers. Resolve colours via the same path as `Color` (`UI.Theme.Resolve`).
- **How to verify:** 2.1 test green.
- **Dependencies:** 2.2

#### Task 2.4: Red test + impl — `stateReact="false"` opt-out
- **What:** Red test `Btn_StateReactFalse_SkipsChild()`: child with `stateReact="false"` gets no reactor and keeps its colour across states. Add a common `[UIAttr] StateReact` (on `Control` base or as a per-node marker the installer reads). Verify the back-compat case too: a `<Btn>` with NO `*Color` keeps `transition == ColorTint` and adds no reactors.
- **How to verify:** `run_tests(... filter="BtnState")` green; add assertion for the transition-unchanged case.
- **Dependencies:** 2.3

---

### Phase 3: `state-*` trigger events + upward source resolution

**Goal:** `on="state-normal|hover|pressed|disabled"` (+`@id`) parse, resolve to the nearest `<Btn>` ancestor, and fire-on-enter for `<Animation>`/`<Trigger>`.

#### Task 3.1: Red test + impl — `TriggerSpec.Parse`
- **What:** Red test in `Tests/EditMode/TriggerSpecTests.cs` (or existing trigger tests): `Parse("state-pressed")` → kind StatePressed; `Parse("state-hover@tabBtn")` → StateHover + SourceId; unknown rejected. Then extend `TriggerKind` with `StateNormal/StateHover/StatePressed/StateDisabled` and add the bare + `@`-prefixed entries to `Parse`/`s_prefixedKinds`. Update the error message’s allowed-list.
- **How to verify:** `run_tests(... filter="TriggerSpec")` green.
- **Dependencies:** none

#### Task 3.2: Red test + impl — upward source resolver
- **What:** Red test `StateSource_ResolvesNearestBtnAncestor()` and `_ByIdViaScopedIds()` and `_MissingThrows()`. Add `TriggerSourceResolver.FindStateSource(Trigger trigger, string sourceId) → PuiButton`: no id → `trigger.GameObject.GetComponentInParent<PuiButton>()` (nearest ancestor); with id → resolve `trigger.ScopedIds[id]` then its `GameObject.GetComponent<PuiButton>()` (type-check, mirror existing error wording); none found → `InvalidOperationException`.
- **How to verify:** `run_tests(... filter="StateSource")` green.
- **Dependencies:** 1.3

#### Task 3.3: Red test + impl — base `Trigger` fires on state enter
- **What:** Red test `Animation_OnStatePressed_FiresOnEnter()`: an `<Animation on="state-pressed">` inside a `<Btn>` plays when the Btn enters Pressed (assert `OnFire` emits / motion starts). In `Trigger.InitTriggerSubscription`, add a `SubscribeState(kind)` branch: resolve via `FindStateSource`, subscribe to `OnState`, `Fire()` when `state == kind`’s target.
- **How to verify:** `run_tests(... filter="Animation")` green.
- **Dependencies:** 3.1, 3.2

---

### Phase 4: `<Show>` — state-conditional visibility

**Goal:** `<Show on="state-...">` shows its subtree while the source Btn is in that state, with mutual exclusion + Normal fallback, via `SetActive` only.

#### Task 4.1: Red test — visibility + fallback + mutual exclusion
- **What:** Red test `Show_TogglesSubtreeByState()`: `<Btn><Show on="state-normal"><Image id=n/></Show><Show on="state-pressed"><Image id=p/></Show></Btn>`. Assert: at Normal n active / p inactive; at Pressed p active / n inactive; at **Hover** (no block) n active (Normal fallback); add a `state-hover` block → hover shows it instead. Assert `SetActive` toggled (GO not destroyed) and a single initial evaluation.
- **How to verify:** `run_tests(... filter="Show")` red.
- **Dependencies:** 3.2

#### Task 4.2: `PuiButton` Show-coordination surface
- **What:** On `PuiButton`: `RegisterShowState(BtnState)` accumulating a `HashSet<BtnState> ClaimedShowStates`, plus `Current` already present. (Used by Show for the fallback predicate; complete before Btn’s seed emit since Show registers in its `OnAfterApply`, Btn seeds in its later `OnAfterApply`.)
- **How to verify:** compiles; 4.1 still red.
- **Dependencies:** 1.2

#### Task 4.3: `Show : Trigger`
- **What:** New `Runtime/Controls/Show.cs` (`public sealed class Show : Trigger`). Override `InitTriggerSubscription`: require a `state-*` kind (else throw — see 5.2); resolve source via `FindStateSource`; `RegisterShowState(myState)`; subscribe to `OnState` and set subtree active = `current==myState || (myState==Normal && !Claimed.Contains(current))`. Use Strategy C (`SetActive` only, never Destroy). Reuse `Trigger.GetNativeSize` content-passthrough.
- **How to verify:** 4.1 test green.
- **Dependencies:** 4.1, 4.2, 3.2

#### Task 4.4: Register `<Show>` builtin
- **What:** Register `Show` in `Runtime/Application/BuiltinPrimitives.cs` alongside the other builtins so `ControlRegistry` (and `ResetForTests`) and the generated XSD pick it up.
- **How to verify:** `run_tests(... filter="Show")` still green; a `.ui.xml` using `<Show>` no longer reports unknown element.
- **Dependencies:** 4.3

---

### Phase 5: Lint rule + XSD

**Goal:** Authors get errors for a sourceless `state-*` and for `<Show on="click">`; XSD recognises the new surface.

#### Task 5.1: Red test + impl — `PUI-STATE-NO-SOURCE`
- **What:** Red test in the lint tests asserting a `state-*` `on=` with no `<Btn>` ancestor (and no `@id` match) yields rule `PUI-STATE-NO-SOURCE`. Implement the rule in `Runtime/Core/Lint/` (shared with `ScreenInstantiator` warning path — same source of truth, mirrors `PUI-LAYOUT-ANCHOR`).
- **How to verify:** `run_tests(... filter="Lint")` green; `dotnet run --project .lint/UIXmlLint -- <fixture>` exits non-zero on the bad file.
- **Dependencies:** 3.2

#### Task 5.2: `<Show on="click">` rejection
- **What:** Make `Show` reject any non-`state-*` `on=` (parser/runtime error with the message from spec §5). Add a red test first.
- **How to verify:** `run_tests(... filter="Show")` green.
- **Dependencies:** 4.3

#### Task 5.3: XSD regen + spot-check
- **What:** Confirm the XSD generator (Editor) emits `<Show>` (auto via registry) and the new `[UIAttr]`s. Add/adjust the substring assertion in the XSD generator tests (`PromptUGUI.Tests.EditorOnly`) for `Show` + `pressedColor` + `state-` if those tests enumerate the surface.
- **How to verify:** `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])` green; `xmllint --noout --schema` on a fixture using `<Show>`/`pressedColor` passes.
- **Dependencies:** 4.4

---

### Phase 6: PlayMode integration

**Goal:** End-to-end pointer interaction flips tint + Show artwork and reverts.

#### Task 6.1: PlayMode test
- **What:** In `Tests/PlayMode/`, build a composed `<Btn pressedColor="#808080"><Show on="state-normal">…</Show><Show on="state-pressed">…</Show></Btn>`, simulate pointer down/up + enter/exit (EventSystem or direct `DoStateTransition` via the Selectable), assert child tint and `<Show>` artwork change on press/hover and revert on release/exit.
- **How to verify:** `run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="BtnState")` green.
- **Dependencies:** Phases 2 & 4

---

### Phase 7: SKILL documentation (same PR, English)

**Goal:** Authoring + scripting skills reflect the new surface.

#### Task 7.1: `authoring-promptugui-xml/SKILL.md`
- **What:** Add to the `<Btn>` attribute table: `hoverColor`/`pressedColor`/`disabledColor` (multipliers, fan-out to subtree), `interactable`, `stateReact`. Add a `<Show>` row to the controls + internal-structure tables with its visible-while-state + Normal-fallback semantics. Add `state-normal/hover/pressed/disabled` (+`@id`) to the Triggers/Animations `on=` table, noting **upward** source resolution and the `hover-enter` vs `state-hover` distinction. Add `PUI-STATE-NO-SOURCE` to the lint list.
- **How to verify:** Re-read for accuracy against shipped behavior; examples lint-clean via UIXmlLint CLI.
- **Dependencies:** Phases 1–5

#### Task 7.2: `scripting-promptugui-csharp/SKILL.md`
- **What:** Document `Btn.OnState : Observable<BtnState>` and the `BtnState` enum (R3 subscription example).
- **How to verify:** Re-read for accuracy.
- **Dependencies:** Phase 1

---

## Verification (end-to-end)

1. `refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` then `read_console(action="get", types=["error"])` → no compile errors.
2. `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` → all green (incl. BtnState/TriggerSpec/StateSource/Show/Lint).
3. `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])` → XSD generator green.
4. `run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])` → integration green.
5. `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx` → clean.
6. `dotnet run --project .lint/UIXmlLint -- <fixtures>` → exit 0 on good fixtures, non-zero on the `state-*`-without-source fixture.
7. A back-compat `<Btn>` with none of the new attrs/children renders identically to pre-change (transition still ColorTint on `_bg`; no reactors) — covered by Task 2.4 assertion.

## Risks & Rollback

- **Risk:** `transition = None` when `*Color` present removes uGUI’s built-in `_bg` tint, so the bg reactor must cover it → **Mitigation:** Task 2.3 installs a reactor on `_bg` too; Task 2.4 asserts bg still tints on press.
- **Risk:** Show registration vs Btn seed-emit ordering race (claimed set incomplete at first eval) → **Mitigation:** rely on the documented "apply after subtree recursion" order (Show.OnAfterApply before Btn.OnAfterApply); Task 4.1 asserts correct initial state.
- **Risk:** `GetComponentInParent<PuiButton>()` crossing a nested `<Btn>` boundary picks the wrong source → **Mitigation:** nearest-ancestor is the intended semantic; `@id` escape hatch + Task 3.2 test for nested case.
- **Risk:** Unity MCP test runs flaky/hang (known) → **Mitigation:** compile-check via `read_console` first; restart Unity if "failed to initialize"; prefer single-class `filter=`.
- **Rollback:** All work is additive on `feat/btn-state-visuals`; revert the branch. No migration of existing `.ui.xml` is required (additive attributes/elements).
