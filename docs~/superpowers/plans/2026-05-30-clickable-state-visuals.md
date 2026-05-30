# Clickable State-Driven Visuals Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generalize the `Button`-only Btn state-visuals feature (broadcast interaction state → tint fan-out, `<Show>` swap, `state-*` triggers, `OnState`) so any uGUI `Selectable`-backed control can drive it, then wire `<Tab>` and `<Toggle>` (incl. a persistent `Selected`/`isOn` state).

**Architecture:** Extract the broadcast out of `PuiButton` into a held `StateBroadcaster` helper + an `IStateSource` interface (C# can't share a base across `Button`/`Toggle`). Add a `Selected` state computed as the *resting baseline* of an active control (`Current = transient==Normal ? (isOn?Selected:Normal) : transient`). Retarget the three consumers (resolver / tint reactor / `<Show>`) from `PuiButton` to `IStateSource`. Extract the tint fan-out into a shared `StateTintInstaller`. Add a `PuiToggle : Toggle` used by both Tab and Toggle, plus a `selectedColor` tint and a `state-selected` trigger.

**Tech Stack:** Unity 6 uGUI, C# (LangVersion 9), R3 (Cysharp `ReactiveProperty`/`Observable`), LitMotion (tint fade), NUnit (EditMode/PlayMode), Unity MCP for test runs.

**Spec:** `docs~/superpowers/specs/2026-05-30-clickable-state-visuals-design.md`

**Dependencies:** Branched off `feat/btn-state-visuals` (implemented but not yet merged to main — this work builds on its `PuiButton`/`StateTintReactor`/`<Show>`/`state-*` code). On this branch `<Tab>` **already accepts children** and `PUI-TAB-CHILDREN` is retired (verified in `Tests/EditMode/Lint/TabRulesTests.cs`), so `<Show>`-in-Tab needs **no** separate tab-container work. `<Toggle>` also instantiates children uniformly (`ScreenInstantiator:241`). No new package deps.

---

## Conventions for every task

- **Strict TDD:** write the failing test, run it (must fail for the stated reason), implement minimally, run it (must pass), commit.
- **Run tests via Unity MCP only** (never batch-mode). After each source edit:
  1. `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
  2. `mcp__UnityMCP__read_console(action="get", types=["error"])` — confirm **no compile errors** before running tests.
  3. `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` (add `filter="ClassName"` to scope to one class; the job is async — poll `get_test_job(job_id)` and read `result.summary`). PlayMode tasks use `mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"]`.
  - If MCP is unavailable: reconnect, or ask the user to restart it. Do not fall back to batch-mode.
- **Lint after C# edits:** from repo root, `cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx` then `dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`. Never run `dotnet format analyzers --severity info` (see CLAUDE.md table).
- **Lint after `.ui.xml` edits:** `dotnet run --project .lint/UIXmlLint -- <path>`.
- **Branch:** `feat/clickable-state-visuals` (already created). **Never commit to `main`.**
- Commit message footer: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

## File Structure

**Create:**
- `Runtime/Controls/Internal/IStateSource.cs` — the broadcast contract (interface).
- `Runtime/Controls/Internal/StateBroadcaster.cs` — shared state holder + composite rule + `MapTransient`.
- `Runtime/Controls/Internal/PuiToggle.cs` — `Toggle : IStateSource` used by Tab + Toggle.
- `Runtime/Controls/Internal/StateTintInstaller.cs` — shared tint fan-out (extracted from Btn).
- `Tests/EditMode/Controls/StateBroadcasterTests.cs`
- `Tests/EditMode/Controls/PuiToggleTests.cs`
- `Tests/EditMode/Controls/TabStateTests.cs`
- `Tests/EditMode/Controls/ToggleStateTests.cs`
- `Tests/PlayMode/Controls/TabStateVisualsPlayTests.cs`

**Rename:**
- `Runtime/Controls/BtnState.cs` → `Runtime/Controls/InteractState.cs` (enum `BtnState` → `InteractState`, add `Selected`).

**Modify:**
- `Runtime/Controls/Internal/PuiButton.cs` — delegate to `StateBroadcaster`.
- `Runtime/Controls/Internal/StateTintReactor.cs` — `IStateSource` source; add `_selected` multiplier.
- `Runtime/Controls/Internal/TriggerSourceResolver.cs` — `FindStateSource` returns `IStateSource`.
- `Runtime/Controls/Internal/TriggerSpec.cs` — `state-selected` kind.
- `Runtime/Controls/Trigger.cs` — `StateSelected` subscribe case.
- `Runtime/Controls/Show.cs` — `IStateSource` field; `StateSelected` map.
- `Runtime/Controls/Btn.cs` — delegate tint to `StateTintInstaller`.
- `Runtime/Controls/Tab.cs` — `PuiToggle`, `*Color`, `OnState`, `OnAfterApply`.
- `Runtime/Controls/Toggle.cs` — `PuiToggle`, `*Color`, `OnState`, `OnAfterApply`.
- `Runtime/Core/Lint/StateTriggerRules.cs` — `StateSourceTags` set + `state-selected`.
- `Runtime/Core/Lint/IRWalker.cs` — `hasStateSourceAncestor`.
- Test files referencing `BtnState`: `Tests/EditMode/Controls/BtnStateTests.cs`, `ShowTests.cs`, `StateTriggerTests.cs`, `TriggerSpecTests.cs`, `Tests/EditMode/Lint/StateTriggerRulesTests.cs`, `Tests/PlayMode/Controls/BtnStateVisualsPlayTests.cs`.
- `Tests/EditMode/Editor/XsdGeneratorTests.cs` — optional `selectedColor` assertion.
- `.claude/skills/authoring-promptugui-xml/SKILL.md`, `.claude/skills/scripting-promptugui-csharp/SKILL.md`.

---

## Task 1: Rename `BtnState` → `InteractState` (+ add `Selected`)

Pure rename + one new (inert) enum value. No behavior change — `Btn` still folds navigation-Selected → Normal and never emits `Selected`.

**Files:**
- Rename: `Runtime/Controls/BtnState.cs` → `Runtime/Controls/InteractState.cs`
- Modify: `Runtime/Controls/Internal/PuiButton.cs`, `Runtime/Controls/Internal/StateTintReactor.cs`, `Runtime/Controls/Show.cs`, `Runtime/Controls/Trigger.cs`, `Runtime/Controls/Btn.cs`
- Modify tests: `Tests/EditMode/Controls/BtnStateTests.cs`, `ShowTests.cs`, `StateTriggerTests.cs`, `Tests/PlayMode/Controls/BtnStateVisualsPlayTests.cs`

- [ ] **Step 1: Rename the enum file and type, add `Selected`**

Rename the file (`git mv Runtime/Controls/BtnState.cs Runtime/Controls/InteractState.cs` and rename `Runtime/Controls/BtnState.cs.meta` → `Runtime/Controls/InteractState.cs.meta`, keeping the `guid` line inside the `.meta` unchanged so references survive). New file content:

```csharp
namespace PromptUGUI.Controls
{
    /// <summary>
    /// The broadcast interaction state of a clickable control (Btn / Tab / Toggle / any future
    /// <see cref="UnityEngine.UI.Selectable"/>-backed control), derived from the uGUI Selectable
    /// state machine and — for toggle-family controls — the persistent <c>isOn</c> flag.
    /// </summary>
    /// <remarks>
    /// uGUI's navigation-<c>Selected</c> state folds to <see cref="Normal"/> (keyboard focus is not
    /// "checked"). <see cref="Selected"/> here is the resting baseline of an <c>isOn</c> control:
    /// emitted when the control is active and not currently Hover/Pressed/Disabled. A momentary
    /// <see cref="Btn"/> has no <c>isOn</c>, so it never emits <see cref="Selected"/>.
    /// </remarks>
    public enum InteractState
    {
        Normal,
        Hover,
        Pressed,
        Selected,
        Disabled,
    }
}
```

- [ ] **Step 2: Replace all `BtnState` references in runtime source**

In `PuiButton.cs`, `StateTintReactor.cs`, `Show.cs`, `Trigger.cs`, `Btn.cs`: replace every `BtnState` token with `InteractState`. (No other change yet — the `Map`/composite refactor is Task 3.)

- [ ] **Step 3: Replace all `BtnState` references in tests**

In `BtnStateTests.cs`, `ShowTests.cs`, `StateTriggerTests.cs`, `BtnStateVisualsPlayTests.cs`: replace `BtnState` → `InteractState`. (`BtnStateTests.cs` keeps `PuiButton.Map(...)` for now — that call changes in Task 3.)

- [ ] **Step 4: Refresh, verify no compile errors, run the affected EditMode classes**

Run the MCP refresh + `read_console(types=["error"])` (expect none), then `run_tests(mode="EditMode", filter="BtnStateTests")` and `filter="ShowTests"` and `filter="StateTriggerTests"`.
Expected: all PASS (rename is behavior-preserving; `Map(3)` still returns `InteractState.Normal`).

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add -A
git commit -m "refactor: rename BtnState -> InteractState; add inert Selected value"
```

---

## Task 2: `state-selected` in the trigger vocabulary

Add the fifth `state-*` kind end-to-end (parse → Trigger/Show mapping → lint bare-value set). At this point no source emits `Selected`, so a `state-selected` trigger simply never fires — that is correct and tested.

**Files:**
- Modify: `Runtime/Controls/Internal/TriggerSpec.cs:5-9,16-25,39-42`, `Runtime/Controls/Trigger.cs:110-117`, `Runtime/Controls/Show.cs:30-39`, `Runtime/Core/Lint/StateTriggerRules.cs:32-35`
- Test: `Tests/EditMode/Controls/TriggerSpecTests.cs`, `Tests/EditMode/Lint/StateTriggerRulesTests.cs`

- [ ] **Step 1: Write failing parse tests**

Append to `Tests/EditMode/Controls/TriggerSpecTests.cs` (inside the class; mirror existing cases):

```csharp
[Test]
public void Parse_StateSelected_NoId()
{
    var spec = TriggerSpec.Parse("state-selected");
    Assert.AreEqual(TriggerKind.StateSelected, spec.Kind);
    Assert.IsNull(spec.SourceId);
}

[Test]
public void Parse_StateSelected_WithId()
{
    var spec = TriggerSpec.Parse("state-selected@tab1");
    Assert.AreEqual(TriggerKind.StateSelected, spec.Kind);
    Assert.AreEqual("tab1", spec.SourceId);
}
```

- [ ] **Step 2: Run to verify failure**

`run_tests(mode="EditMode", filter="TriggerSpecTests")`.
Expected: FAIL — `TriggerKind.StateSelected` does not exist (compile error) / parse falls through to the throw.

- [ ] **Step 3: Implement the kind**

`TriggerSpec.cs` — add to the enum (line 5-9):

```csharp
internal enum TriggerKind
{
    Open, Loop, Click, Manual, HoverEnter, HoverExit, Press,
    StateNormal, StateHover, StatePressed, StateSelected, StateDisabled,
}
```

Add the prefixed entry to `s_prefixedKinds`:

```csharp
("state-selected@", TriggerKind.StateSelected),
```

Add the bare case in `Parse`'s switch (next to the other `state-*`):

```csharp
case "state-selected": return new TriggerSpec { Kind = TriggerKind.StateSelected };
```

And extend the final error string's enumerated list to include `state-selected`.

`Trigger.cs` `SubscribeState` switch — add:

```csharp
TriggerKind.StateSelected => InteractState.Selected,
```

`Show.cs` `InitTriggerSubscription` switch — add:

```csharp
TriggerKind.StateSelected => InteractState.Selected,
```

`StateTriggerRules.cs` — add `"state-selected"` to `BareStateValues`:

```csharp
private static readonly HashSet<string> BareStateValues = new HashSet<string>
{
    "state-normal", "state-hover", "state-pressed", "state-selected", "state-disabled"
};
```

- [ ] **Step 4: Add the lint test, run both**

Append to `Tests/EditMode/Lint/StateTriggerRulesTests.cs` a case that a bare `state-selected` with no state-source ancestor reports `PUI-STATE-NO-SOURCE` (mirror the existing `state-pressed` no-source test, swapping the `on` value). Then run `filter="TriggerSpecTests"` and `filter="StateTriggerRulesTests"`.
Expected: PASS.

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add -A
git commit -m "feat: add state-selected to the on= trigger vocabulary (parse + Trigger/Show map + lint)"
```

---

## Task 3: Extract `IStateSource` + `StateBroadcaster`; refactor `PuiButton`; composite rule

**Files:**
- Create: `Runtime/Controls/Internal/IStateSource.cs`, `Runtime/Controls/Internal/StateBroadcaster.cs`, `Tests/EditMode/Controls/StateBroadcasterTests.cs`
- Modify: `Runtime/Controls/Internal/PuiButton.cs`, `Tests/EditMode/Controls/BtnStateTests.cs`

- [ ] **Step 1: Write failing broadcaster tests**

Create `Tests/EditMode/Controls/StateBroadcasterTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class StateBroadcasterTests
    {
        // Selectable.SelectionState ordinals.
        private const int Normal = 0, Highlighted = 1, Pressed = 2, NavSelected = 3, Disabled = 4;

        [Test]
        public void MapTransient_FoldsNavSelectedToNormal()
        {
            Assert.AreEqual(InteractState.Normal, StateBroadcaster.MapTransient(Normal));
            Assert.AreEqual(InteractState.Hover, StateBroadcaster.MapTransient(Highlighted));
            Assert.AreEqual(InteractState.Pressed, StateBroadcaster.MapTransient(Pressed));
            Assert.AreEqual(InteractState.Normal, StateBroadcaster.MapTransient(NavSelected));
            Assert.AreEqual(InteractState.Disabled, StateBroadcaster.MapTransient(Disabled));
        }

        [Test]
        public void Composite_SelectedIsRestingBaselineOfActiveControl()
        {
            var b = new StateBroadcaster();
            Assert.AreEqual(InteractState.Normal, b.Current);

            b.SetOn(true);                                  // active at rest -> Selected
            Assert.AreEqual(InteractState.Selected, b.Current);

            b.SetTransient(InteractState.Hover);            // transient overrides Selected
            Assert.AreEqual(InteractState.Hover, b.Current);

            b.SetTransient(InteractState.Pressed);
            Assert.AreEqual(InteractState.Pressed, b.Current);

            b.SetTransient(InteractState.Normal);           // release -> back to Selected
            Assert.AreEqual(InteractState.Selected, b.Current);

            b.SetOn(false);                                 // deactivate -> Normal
            Assert.AreEqual(InteractState.Normal, b.Current);
        }

        [Test]
        public void Composite_DisabledWinsOverIsOn()
        {
            var b = new StateBroadcaster();
            b.SetOn(true);
            b.SetTransient(InteractState.Disabled);
            Assert.AreEqual(InteractState.Disabled, b.Current);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

`run_tests(mode="EditMode", filter="StateBroadcasterTests")`.
Expected: FAIL — `StateBroadcaster` / `IStateSource` do not exist (compile error).

- [ ] **Step 3: Create the interface**

Create `Runtime/Controls/Internal/IStateSource.cs`:

```csharp
using System;
using R3;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Implemented by any Selectable-backed control that broadcasts its interaction state
    /// (PuiButton, PuiToggle). Consumers (<see cref="StateTintReactor"/>, <see cref="Show"/>,
    /// <see cref="TriggerSourceResolver.FindStateSource"/>) resolve this, never a concrete type,
    /// so the feature is generic over Button / Toggle / future Selectables.
    /// </summary>
    internal interface IStateSource
    {
        Observable<InteractState> OnState { get; }
        InteractState Current { get; }
        void RegisterShow(InteractState state, Action reevaluate);
        bool IsShowStateClaimed(InteractState state);
    }
}
```

- [ ] **Step 4: Create the broadcaster**

Create `Runtime/Controls/Internal/StateBroadcaster.cs`:

```csharp
using System;
using System.Collections.Generic;
using R3;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Shared interaction-state holder for every <see cref="IStateSource"/>. Owns the R3 stream,
    /// the <c>&lt;Show&gt;</c> coordination set, and the composite rule that folds a transient
    /// Selectable state with a persistent checked (<c>isOn</c>) flag into one <see cref="InteractState"/>.
    /// A Selectable subclass holds one of these and forwards to it (C# cannot share a base across
    /// Button/Toggle).
    /// </summary>
    internal sealed class StateBroadcaster
    {
        private readonly ReactiveProperty<InteractState> _state = new(InteractState.Normal);
        private readonly HashSet<InteractState> _claimedShowStates = new();
        private readonly List<Action> _showReevaluators = new();

        private InteractState _transient = InteractState.Normal;
        private bool _isOn;

        /// <summary>Replays the current value to new subscribers, then emits on every change.</summary>
        public Observable<InteractState> OnState => _state;
        public InteractState Current => _state.Value;

        public bool IsShowStateClaimed(InteractState state) => _claimedShowStates.Contains(state);

        public void RegisterShow(InteractState state, Action reevaluate)
        {
            _claimedShowStates.Add(state);
            _showReevaluators.Add(reevaluate);
            for (int i = 0; i < _showReevaluators.Count; i++)
                _showReevaluators[i].Invoke();
        }

        /// <summary>Set the transient Selectable state (Normal/Hover/Pressed/Disabled).</summary>
        public void SetTransient(InteractState transient)
        {
            _transient = transient;
            Recompute();
        }

        /// <summary>Set the persistent checked flag (Toggle.isOn). Non-toggles never call this.</summary>
        public void SetOn(bool isOn)
        {
            _isOn = isOn;
            Recompute();
        }

        // Selected = resting baseline of an active control: transient Normal + isOn reads Selected;
        // any non-Normal transient overrides it (and reverts on release). The always-on "active"
        // indicator lives on the independent Toggle.graphic channel, so active-ness is never lost.
        private void Recompute()
        {
            var composite = _transient == InteractState.Normal
                ? (_isOn ? InteractState.Selected : InteractState.Normal)
                : _transient;
            _state.Value = composite;                 // drives OnState reactors (distinct-until-changed)
            for (int i = 0; i < _showReevaluators.Count; i++)
                _showReevaluators[i].Invoke();        // drives <Show> blocks
        }

        /// <summary>
        /// Maps a uGUI <see cref="UnityEngine.UI.Selectable.SelectionState"/> ordinal to a transient
        /// <see cref="InteractState"/> (navigation-Selected folds to Normal). Takes the int ordinal
        /// because the protected SelectionState type cannot appear in a non-Selectable class's
        /// accessible signature (CS0051). Ordinals: Normal=0 Highlighted=1 Pressed=2 Selected=3 Disabled=4.
        /// </summary>
        public static InteractState MapTransient(int selectionStateOrdinal) => selectionStateOrdinal switch
        {
            0 => InteractState.Normal,
            1 => InteractState.Hover,
            2 => InteractState.Pressed,
            3 => InteractState.Normal,
            4 => InteractState.Disabled,
            _ => InteractState.Normal,
        };

        public void Dispose() => _state.Dispose();
    }
}
```

- [ ] **Step 5: Refactor `PuiButton` to delegate**

Replace `Runtime/Controls/Internal/PuiButton.cs` body with:

```csharp
using System;
using R3;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// A uGUI <see cref="Button"/> that broadcasts its <see cref="Selectable"/> state through a
    /// held <see cref="StateBroadcaster"/>. <see cref="DoStateTransition"/> still calls
    /// <c>base</c> first, preserving the default <c>targetGraphic</c> ColorTint (back-compat).
    /// </summary>
    internal sealed class PuiButton : Button, IStateSource
    {
        private readonly StateBroadcaster _broadcaster = new();

        public Observable<InteractState> OnState => _broadcaster.OnState;
        public InteractState Current => _broadcaster.Current;
        public void RegisterShow(InteractState state, Action reevaluate) => _broadcaster.RegisterShow(state, reevaluate);
        public bool IsShowStateClaimed(InteractState state) => _broadcaster.IsShowStateClaimed(state);

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            _broadcaster.SetTransient(StateBroadcaster.MapTransient((int)state));
        }

        /// <summary>Test hook: drive a transition without a live EventSystem (ordinal — the test
        /// assembly cannot name the protected SelectionState type).</summary>
        internal void SimulateState(int selectionStateOrdinal)
            => DoStateTransition((SelectionState)selectionStateOrdinal, instant: true);

        protected override void OnDestroy()
        {
            _broadcaster.Dispose();
            base.OnDestroy();
        }
    }
}
```

- [ ] **Step 6: Update `BtnStateTests.Map_...` to call the broadcaster**

In `Tests/EditMode/Controls/BtnStateTests.cs`, the test `Map_TranslatesSelectionStatesToBtnState` calls `PuiButton.Map(...)`. Replace those calls with `StateBroadcaster.MapTransient(...)` (same ordinals/expected values; `InteractState` from Task 1). Rename the test method to `Map_TranslatesSelectionStatesToInteractState` for clarity.

- [ ] **Step 7: Refresh, verify no compile errors, run**

`read_console(types=["error"])` (none), then `run_tests(filter="StateBroadcasterTests")`, `filter="BtnStateTests"`, `filter="ShowTests"`, `filter="StateTriggerTests"`.
Expected: all PASS — Btn behavior is unchanged (it never calls `SetOn`, so never emits `Selected`).

- [ ] **Step 8: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add -A
git commit -m "refactor: extract IStateSource + StateBroadcaster (composite Selected rule); PuiButton delegates"
```

---

## Task 4: Retarget consumers (resolver / reactor / `<Show>`) to `IStateSource`

Type change only — no new behavior for Btn. Existing Btn Show/trigger/tint tests stay green.

**Files:**
- Modify: `Runtime/Controls/Internal/TriggerSourceResolver.cs:108-131`, `Runtime/Controls/Internal/StateTintReactor.cs:55-57`, `Runtime/Controls/Show.cs:24-26,41`

- [ ] **Step 1: Retarget `FindStateSource`**

In `TriggerSourceResolver.cs`, change `FindStateSource` to return `IStateSource`:

```csharp
public static IStateSource FindStateSource(Trigger trigger, string sourceId)
{
    if (string.IsNullOrEmpty(sourceId))
    {
        var ancestor = trigger.GameObject.GetComponentInParent<IStateSource>();
        if (ancestor == null)
            throw new InvalidOperationException(
                $"<Trigger on=\"state-...\"> in '{trigger.Id ?? trigger.GameObject.name}': " +
                "no <Btn>/<Tab>/<Toggle> ancestor found. Place it inside one, or use state-...@<id>.");
        return ancestor;
    }

    if (!trigger.ScopedIds.TryGetValue(sourceId, out var ctrl))
        throw new InvalidOperationException(
            $"<Trigger on=\"state-...@{sourceId}\"> in '{trigger.Id ?? trigger.GameObject.name}': " +
            $"id '{sourceId}' not found in trigger subtree scope");

    var src = ctrl.GameObject.GetComponent<IStateSource>();
    if (src == null)
        throw new InvalidOperationException(
            $"<Trigger on=\"state-...@{sourceId}\">: id '{sourceId}' is a " +
            $"{ctrl.GetType().Name}, not a state source. state-* triggers require a <Btn>/<Tab>/<Toggle>.");
    return src;
}
```

- [ ] **Step 2: Retarget `StateTintReactor.EnsureInit`**

In `StateTintReactor.cs`, change the source lookup type:

```csharp
var source = GetComponentInParent<IStateSource>();
if (source != null)
    _sub = source.OnState.Subscribe(OnState);
```

(The `OnState(InteractState state)` handler already uses `InteractState` after Task 1.)

- [ ] **Step 3: Retarget `Show`**

In `Show.cs`: change the field `private PuiButton _pui;` → `private IStateSource _src;`. In `InitTriggerSubscription`, `_src = TriggerSourceResolver.FindStateSource(this, _spec.SourceId); _src.RegisterShow(_myState, ReevaluateVisibility);`. In `ReevaluateVisibility`, use `_src.Current` and `_src.IsShowStateClaimed(...)`.

- [ ] **Step 4: Refresh, verify no compile errors, run the full state-feature classes**

`read_console(types=["error"])` (none), then `run_tests(filter="ShowTests")`, `filter="StateTriggerTests"`, `filter="BtnStateTests"`.
Expected: all PASS (a Btn still hosts a `PuiButton` which is now an `IStateSource`).

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add -A
git commit -m "refactor: retarget FindStateSource / StateTintReactor / Show from PuiButton to IStateSource"
```

---

## Task 5: `PuiToggle`

**Files:**
- Create: `Runtime/Controls/Internal/PuiToggle.cs`, `Tests/EditMode/Controls/PuiToggleTests.cs`

- [ ] **Step 1: Write failing tests**

Create `Tests/EditMode/Controls/PuiToggleTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class PuiToggleTests
    {
        private const int Normal = 0, Highlighted = 1, Pressed = 2;

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PuiToggle NewPuiToggle()
        {
            var go = new GameObject("t", typeof(RectTransform));
            var pt = go.AddComponent<PuiToggle>();
            pt.InitStateBroadcast();
            return pt;
        }

        [Test]
        public void Transient_PushesThroughBroadcaster()
        {
            var pt = NewPuiToggle();
            var seen = new List<InteractState>();
            using var _ = pt.OnState.Subscribe(s => seen.Add(s));

            pt.SimulateState(Highlighted);
            pt.SimulateState(Pressed);
            pt.SimulateState(Normal);

            CollectionAssert.AreEqual(
                new[] { InteractState.Normal, InteractState.Hover, InteractState.Pressed, InteractState.Normal },
                seen);
        }

        [Test]
        public void IsOn_ReadsSelectedAtRest()
        {
            var pt = NewPuiToggle();
            Assert.AreEqual(InteractState.Normal, pt.Current);
            pt.isOn = true;                              // fires onValueChanged -> SetOn(true)
            Assert.AreEqual(InteractState.Selected, pt.Current);
            pt.SimulateState(Pressed);
            Assert.AreEqual(InteractState.Pressed, pt.Current);
            pt.SimulateState(Normal);
            Assert.AreEqual(InteractState.Selected, pt.Current);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

`run_tests(filter="PuiToggleTests")`.
Expected: FAIL — `PuiToggle` does not exist.

- [ ] **Step 3: Create `PuiToggle`**

Create `Runtime/Controls/Internal/PuiToggle.cs`:

```csharp
using System;
using R3;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// A uGUI <see cref="Toggle"/> that broadcasts its interaction state (transient Selectable
    /// state + persistent <c>isOn</c>) through a held <see cref="StateBroadcaster"/>. Serves both
    /// <see cref="Tab"/> and <see cref="Toggle"/>.
    /// </summary>
    internal sealed class PuiToggle : Toggle, IStateSource
    {
        private readonly StateBroadcaster _broadcaster = new();

        public Observable<InteractState> OnState => _broadcaster.OnState;
        public InteractState Current => _broadcaster.Current;
        public void RegisterShow(InteractState state, Action reevaluate) => _broadcaster.RegisterShow(state, reevaluate);
        public bool IsShowStateClaimed(InteractState state) => _broadcaster.IsShowStateClaimed(state);

        /// <summary>
        /// Wires the checked dimension. Called by the owning control (Tab/Toggle) in OnAttached
        /// right after AddComponent — explicit timing, not a uGUI lifecycle hook.
        /// </summary>
        internal void InitStateBroadcast()
        {
            onValueChanged.AddListener(_broadcaster.SetOn);
            _broadcaster.SetOn(isOn);
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            _broadcaster.SetTransient(StateBroadcaster.MapTransient((int)state));
        }

        internal void SimulateState(int selectionStateOrdinal)
            => DoStateTransition((SelectionState)selectionStateOrdinal, instant: true);

        protected override void OnDestroy()
        {
            _broadcaster.Dispose();
            base.OnDestroy();
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

`read_console(types=["error"])` (none), then `run_tests(filter="PuiToggleTests")`.
Expected: PASS.

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add -A
git commit -m "feat: PuiToggle — Toggle broadcasting InteractState (transient + isOn) for Tab/Toggle"
```

---

## Task 6: Extract `StateTintInstaller`; add `selected` multiplier to the reactor; Btn delegates

**Files:**
- Create: `Runtime/Controls/Internal/StateTintInstaller.cs`
- Modify: `Runtime/Controls/Internal/StateTintReactor.cs:37-40,65-80`, `Runtime/Controls/Btn.cs:87-143`
- Test: `Tests/EditMode/Controls/BtnStateTests.cs` (add nested-source boundary test)

- [ ] **Step 1: Write a failing nested-source boundary test**

Append to `Tests/EditMode/Controls/BtnStateTests.cs`:

```csharp
[Test]
public void NestedStateSource_IsFanOutBoundary()
{
    UseInstantTint();
    // Outer Btn with pressedColor; an inner <Btn> (another IStateSource) must NOT receive the
    // outer's reactor on its own bg — the inner owns its subtree.
    var outer = BuildBtnXml("pressedColor='#808080'", "<Btn id='inner'>x</Btn>");
    var inner = outer.Get<Btn>("inner");
    var innerBg = inner.GameObject.GetComponent<UnityImage>();
    Assert.IsNull(innerBg.GetComponent<StateTintReactor>(),
        "nested state source must be a fan-out boundary (no reactor from the outer Btn)");
}
```

- [ ] **Step 2: Run to verify failure**

`run_tests(filter="BtnStateTests")`.
Expected: this test FAILS today only if the boundary regressed — with the current `control is Btn` check it should actually pass. Confirm it passes BEFORE refactor (it documents the behavior we must preserve), then proceed; it must still pass AFTER Step 3-4. If it errors to compile, fix the test first.

> Note: this is a characterization test guarding the refactor. If it already passes pre-refactor, that is expected — keep it; its job is to fail if the extraction breaks the boundary.

- [ ] **Step 3: Add `selected` to the reactor**

In `StateTintReactor.cs`, add the field next to the others:

```csharp
private Color _selected = Color.white;
```

Change `Configure` signature + body:

```csharp
public void Configure(Color? hover, Color? pressed, Color? selected, Color? disabled, float fade)
{
    EnsureInit();
    _hover = hover ?? Color.white;
    _pressed = pressed ?? Color.white;
    _selected = selected ?? Color.white;
    _disabled = disabled ?? Color.white;
    _fade = fade;
}
```

Add the `Selected` arm in `MultiplierFor`:

```csharp
private Color MultiplierFor(InteractState state) => state switch
{
    InteractState.Hover => _hover,
    InteractState.Pressed => _pressed,
    InteractState.Selected => _selected,
    InteractState.Disabled => _disabled,
    _ => Color.white,
};
```

- [ ] **Step 4: Create `StateTintInstaller` and delegate from Btn**

Create `Runtime/Controls/Internal/StateTintInstaller.cs`:

```csharp
using System.Collections.Generic;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Installs <see cref="StateTintReactor"/>s on a state-source control's bg + every descendant
    /// <see cref="Graphic"/>, skipping <c>stateReact="false"</c> children and any nested
    /// <see cref="IStateSource"/> subtree (a deeper Btn/Tab/Toggle owns its own graphics), then
    /// switches the Selectable transition to None so the reactors are the single source of truth.
    /// Shared by Btn / Tab / Toggle. Idempotent: re-runs on each Variant ReSolve, reusing reactors.
    /// </summary>
    internal static class StateTintInstaller
    {
        public static void Install(
            GameObject root,
            Selectable selectable,
            IReadOnlyList<IControl> children,
            string hoverColor, string pressedColor, string selectedColor, string disabledColor)
        {
            var hasAny = !string.IsNullOrEmpty(hoverColor)
                         || !string.IsNullOrEmpty(pressedColor)
                         || !string.IsNullOrEmpty(selectedColor)
                         || !string.IsNullOrEmpty(disabledColor);
            if (!hasAny) return;

            selectable.transition = Selectable.Transition.None;

            Color? hover = string.IsNullOrEmpty(hoverColor) ? null : UI.Theme.Resolve(hoverColor);
            Color? pressed = string.IsNullOrEmpty(pressedColor) ? null : UI.Theme.Resolve(pressedColor);
            Color? selected = string.IsNullOrEmpty(selectedColor) ? null : UI.Theme.Resolve(selectedColor);
            Color? disabled = string.IsNullOrEmpty(disabledColor) ? null : UI.Theme.Resolve(disabledColor);

            var blocked = new HashSet<GameObject>();
            foreach (var child in children)
                CollectBlocked(child as Control, blocked);

            foreach (var g in root.GetComponentsInChildren<Graphic>(includeInactive: true))
            {
                if (blocked.Contains(g.gameObject)) continue;
                InstallReactor(g, hover, pressed, selected, disabled);
            }
        }

        private static void CollectBlocked(Control control, HashSet<GameObject> blocked)
        {
            if (control == null) return;
            var optedOut = !control.StateReact;
            var nestedSource = control.GameObject != null
                               && control.GameObject.GetComponent<IStateSource>() != null;
            if (optedOut || nestedSource)
            {
                if (control.GameObject != null)
                {
                    foreach (var g in control.GameObject.GetComponentsInChildren<Graphic>(includeInactive: true))
                        blocked.Add(g.gameObject);
                    blocked.Add(control.GameObject);
                }
                return;
            }

            foreach (var child in control.Children)
                CollectBlocked(child as Control, blocked);
        }

        private static void InstallReactor(Graphic graphic, Color? hover, Color? pressed, Color? selected, Color? disabled)
        {
            if (graphic == null) return;
            var reactor = graphic.GetComponent<StateTintReactor>()
                          ?? graphic.gameObject.AddComponent<StateTintReactor>();
            reactor.Configure(hover, pressed, selected, disabled, StateTintReactor.DefaultFade);
        }
    }
}
```

In `Btn.cs`: delete the private `ApplyStateTint`, `CollectBlocked`, `InstallReactor` methods (lines ~87-143) and replace the `ApplyStateTint()` call in `OnAfterApply` with:

```csharp
StateTintInstaller.Install(GameObject, _btn, Children, _hoverColor, _pressedColor, null, _disabledColor);
```

(Btn keeps its `_hoverColor` / `_pressedColor` / `_disabledColor` fields; it has no `selectedColor`.)

- [ ] **Step 5: Refresh, verify no compile errors, run**

`read_console(types=["error"])` (none), then `run_tests(filter="BtnStateTests")`.
Expected: all PASS (fan-out, opt-out, transition=None, Variant-ReSolve idempotency, nested-source boundary).

- [ ] **Step 6: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add -A
git commit -m "refactor: extract StateTintInstaller (selected multiplier; nested-IStateSource boundary); Btn delegates"
```

---

## Task 7: Wire `<Tab>`

**Files:**
- Modify: `Runtime/Controls/Tab.cs`
- Test: `Tests/EditMode/Controls/TabStateTests.cs`

- [ ] **Step 1: Write failing Tab state tests**

Create `Tests/EditMode/Controls/TabStateTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TabStateTests
    {
        private const int Normal = 0, Pressed = 2;

        [SetUp] public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = true; }
        [TearDown] public void TearDown() { UI.ResetForTests(); StateTintReactor.TestForceInstant = false; }

        // A TabBar with a single Tab carrying the given attrs/body.
        private static Tab BuildTab(string tabAttrs, string body = "")
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='t' {tabAttrs}>{body}</Tab></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            return UI.Open("S").Get<Tab>("bar/t");
        }

        [Test]
        public void PressedColor_TintsBgAndDescendants_AndSwitchesTransitionNone()
        {
            var tab = BuildTab("pressedColor='#808080'", "<Image id='img'/>");
            var pt = tab.GameObject.GetComponent<PuiToggle>();
            Assert.IsNotNull(pt, "Tab should host a PuiToggle");
            Assert.AreEqual(Selectable.Transition.None, pt.transition);

            var bg = tab.GameObject.GetComponent<UnityImage>();
            Assert.IsNotNull(bg.GetComponent<StateTintReactor>());

            var half = new Color(0.5019608f, 0.5019608f, 0.5019608f, 1f);
            var bgBase = bg.color;
            pt.SimulateState(Pressed);
            Assert.That(bg.color.r, Is.EqualTo((bgBase * half).r).Within(0.001f));
            pt.SimulateState(Normal);
            Assert.That(bg.color.r, Is.EqualTo(bgBase.r).Within(0.001f));
        }

        [Test]
        public void SelectedColor_AppliesWhenIsOnAtRest()
        {
            var tab = BuildTab("selectedColor='#808080'");
            var pt = tab.GameObject.GetComponent<PuiToggle>();
            var bg = tab.GameObject.GetComponent<UnityImage>();
            var bgBase = bg.color;
            var half = new Color(0.5019608f, 0.5019608f, 0.5019608f, 1f);

            tab.IsOn = true;   // active at rest -> Selected
            Assert.That(bg.color.r, Is.EqualTo((bgBase * half).r).Within(0.001f));
        }

        [Test]
        public void InteractableFalse_BridgesToToggleAndEmitsDisabled()
        {
            var tab = BuildTab("interactable='false'");
            var pt = tab.GameObject.GetComponent<PuiToggle>();
            Assert.IsFalse(pt.interactable);
            Assert.AreEqual(InteractState.Disabled, pt.Current);
        }

        [Test]
        public void ShowInsideTab_ResolvesTabAsStateSource()
        {
            // Would throw "no <Btn>/<Tab>/<Toggle> ancestor" if Tab were not an IStateSource.
            var tab = BuildTab("", "<Show id='sn' on='state-normal'><Image/></Show>" +
                                   "<Show id='sp' on='state-pressed'><Image/></Show>");
            var sn = tab.Get<Show>("sn");
            var sp = tab.Get<Show>("sp");
            Assert.IsTrue(sn.GameObject.activeSelf);
            Assert.IsFalse(sp.GameObject.activeSelf);

            tab.GameObject.GetComponent<PuiToggle>().SimulateState(Pressed);
            Assert.IsFalse(sn.GameObject.activeSelf);
            Assert.IsTrue(sp.GameObject.activeSelf);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

`run_tests(filter="TabStateTests")`.
Expected: FAIL — Tab has no `PuiToggle` / `*Color` / `OnState`; `<Show>` inside a Tab throws no-ancestor.

- [ ] **Step 3: Implement Tab wiring**

In `Runtime/Controls/Tab.cs`:

1. Change the field type and using alias usage: `private PuiToggle _toggle;` (remove the `UnityToggle` alias dependency for the field type, or keep the alias and use `PuiToggle` for the field). In `OnAttached`, replace the toggle creation:

```csharp
_toggle = GameObject.GetComponent<PuiToggle>() ?? GameObject.AddComponent<PuiToggle>();
_toggle.targetGraphic = _bg;
_toggle.transition = Selectable.Transition.ColorTint;
_toggle.InitStateBroadcast();
```

(Keep the existing `_toggle.graphic`/`toggleTransition` overlay wiring in `EnsureOverlay`, the `FindAncestorToggleGroup` assignment, and the `_toggle.onValueChanged.AddListener(OnIsOnChanged)` — all unaffected; `PuiToggle` is a `Toggle`.)

2. Add the raw `*Color` fields + `[UIAttr]`s (resolved in `OnAfterApply`, like Btn):

```csharp
private string _hoverColor;
private string _pressedColor;
private string _selectedColor;
private string _disabledColor;

/// <summary>Tint multiplier applied to the Tab's bg + descendant graphics while Hover.</summary>
[UIAttr(IsColor = true), Preserve] public string HoverColor { set => _hoverColor = value; }
/// <summary>Tint multiplier applied while Pressed.</summary>
[UIAttr(IsColor = true), Preserve] public string PressedColor { set => _pressedColor = value; }
/// <summary>Tint multiplier applied while the Tab is the active (isOn) one, at rest.</summary>
[UIAttr(IsColor = true), Preserve] public string SelectedColor { set => _selectedColor = value; }
/// <summary>Tint multiplier applied while Disabled.</summary>
[UIAttr(IsColor = true), Preserve] public string DisabledColor { set => _disabledColor = value; }
```

3. Add `OnState` next to `OnValueChanged`/`OnSelected`:

```csharp
/// <summary>Broadcasts the Tab's interaction state (Normal/Hover/Pressed/Selected/Disabled).
/// Selected = this Tab is the active one (isOn) and at rest.</summary>
public Observable<InteractState> OnState => _toggle.OnState;
```

4. Add `OnAfterApply` (Tab has none today):

```csharp
internal override void OnAfterApply()
{
    base.OnAfterApply();
    _toggle.interactable = Interactable;
    StateTintInstaller.Install(GameObject, _toggle, Children,
        _hoverColor, _pressedColor, _selectedColor, _disabledColor);
}
```

5. Add `using R3;` and `using PromptUGUI.Controls.Internal;` if not already present (the file already imports `Internal` and `R3`).

- [ ] **Step 4: Run to verify pass**

`read_console(types=["error"])` (none), then `run_tests(filter="TabStateTests")` and `run_tests(filter="TabTests")` and `run_tests(filter="TabBarTests")`.
Expected: all PASS (new state tests + no Tab/TabBar regression).

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add -A
git commit -m "feat: <Tab> state visuals — PuiToggle + *Color (incl selectedColor) + OnState + interactable bridge"
```

---

## Task 8: Wire `<Toggle>`

Same pattern as Tab. Toggle's bg lives on a child "Background" GO and its `Selected` indicator is the checkmark (`Toggle.graphic`) — both handled transparently by `StateTintInstaller` (walks all descendant graphics) and the composite rule.

**Files:**
- Modify: `Runtime/Controls/Toggle.cs`
- Test: `Tests/EditMode/Controls/ToggleStateTests.cs`

- [ ] **Step 1: Write failing Toggle state tests**

Create `Tests/EditMode/Controls/ToggleStateTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ToggleStateTests
    {
        private const int Normal = 0, Pressed = 2;

        [SetUp] public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = true; }
        [TearDown] public void TearDown() { UI.ResetForTests(); StateTintReactor.TestForceInstant = false; }

        private static Toggle BuildToggle(string attrs, string body = "")
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='tg' {attrs}>{body}</Toggle>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            return UI.Open("S").Get<Toggle>("tg");
        }

        [Test]
        public void PressedColor_SwitchesTransitionNone_AndInstallsReactors()
        {
            var tg = BuildToggle("pressedColor='#808080'");
            var pt = tg.GameObject.GetComponent<PuiToggle>();
            Assert.IsNotNull(pt, "Toggle should host a PuiToggle");
            Assert.AreEqual(Selectable.Transition.None, pt.transition);
            // bg lives on the child "Background" GO; the installer walks all descendant graphics.
            var reactors = tg.GameObject.GetComponentsInChildren<StateTintReactor>(true);
            Assert.Greater(reactors.Length, 0);
        }

        [Test]
        public void Selected_ReadsWhenIsOnAtRest()
        {
            var tg = BuildToggle("selectedColor='#808080'");
            var pt = tg.GameObject.GetComponent<PuiToggle>();
            Assert.AreEqual(InteractState.Normal, pt.Current);
            tg.IsOn = true;
            Assert.AreEqual(InteractState.Selected, pt.Current);
            pt.SimulateState(Pressed);
            Assert.AreEqual(InteractState.Pressed, pt.Current);
            pt.SimulateState(Normal);
            Assert.AreEqual(InteractState.Selected, pt.Current);
        }

        [Test]
        public void NoStateColor_KeepsDefaultTransition_NoReactors()
        {
            var tg = BuildToggle("");
            var reactors = tg.GameObject.GetComponentsInChildren<StateTintReactor>(true);
            Assert.AreEqual(0, reactors.Length);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

`run_tests(filter="ToggleStateTests")`.
Expected: FAIL — Toggle has no `PuiToggle` / `*Color`.

- [ ] **Step 3: Implement Toggle wiring**

In `Runtime/Controls/Toggle.cs`:

1. Field: `private PuiToggle _toggle;`. In `OnAttached`, replace the toggle creation:

```csharp
_toggle = GameObject.GetComponent<PuiToggle>() ?? GameObject.AddComponent<PuiToggle>();
```

…and after the `_toggle.graphic = _checkmark;` + `_toggle.onValueChanged.AddListener(...)` lines, add the seed:

```csharp
_toggle.InitStateBroadcast();
```

2. Add the four raw `*Color` fields + `[UIAttr]`s (same block as Tab Step 3.2).

3. Add `OnState` next to `OnValueChanged`:

```csharp
public Observable<InteractState> OnState => _toggle.OnState;
```

4. Add `OnAfterApply`:

```csharp
internal override void OnAfterApply()
{
    base.OnAfterApply();
    _toggle.interactable = Interactable;
    StateTintInstaller.Install(GameObject, _toggle, Children,
        _hoverColor, _pressedColor, _selectedColor, _disabledColor);
}
```

5. Ensure `using R3;` (already present) and `using PromptUGUI.Controls.Internal;` (already present).

- [ ] **Step 4: Run to verify pass**

`read_console(types=["error"])` (none), then `run_tests(filter="ToggleStateTests")`, `run_tests(filter="ToggleTests")`, `run_tests(filter="ToggleContentSizingTests")`.
Expected: all PASS (new state tests + no Toggle regression).

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add -A
git commit -m "feat: <Toggle> state visuals — PuiToggle + *Color (incl selectedColor) + OnState + interactable bridge"
```

---

## Task 9: Generalize the `PUI-STATE-NO-SOURCE` lint to Tab/Toggle ancestors

The lint is CLI-only/static. Runtime already resolves `IStateSource` (Task 4), so this only fixes a static false-positive: a bare `state-*` inside a `<Tab>`/`<Toggle>` must not report "no source."

**Files:**
- Modify: `Runtime/Core/Lint/StateTriggerRules.cs`, `Runtime/Core/Lint/IRWalker.cs:16-22,33,38,71,74,93`
- Test: `Tests/EditMode/Lint/StateTriggerRulesTests.cs`

- [ ] **Step 1: Write failing lint tests**

Append to `Tests/EditMode/Lint/StateTriggerRulesTests.cs` (mirror the file's existing `Parse(...)` + `IRWalker.Walk` style — it never constructs `ElementNode` directly):

```csharp
[Test]
public void IsStateSourceTag_RecognisesClickables()
{
    Assert.IsFalse(StateTriggerRules.IsStateSourceTag("Frame"));
    Assert.IsTrue(StateTriggerRules.IsStateSourceTag("Btn"));
    Assert.IsTrue(StateTriggerRules.IsStateSourceTag("Tab"));
    Assert.IsTrue(StateTriggerRules.IsStateSourceTag("Toggle"));
}

[Test]
public void State_Show_Inside_Tab_No_Issue()
{
    var doc = Parse("<TabBar id='bar'><Tab id='t'><Show on='state-pressed'><Icon name='ui:gear'/></Show></Tab></TabBar>");
    var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
    CollectionAssert.DoesNotContain(codes, Code,
        "state-* inside a <Tab> resolves the Tab as a state source.");
}

[Test]
public void State_Show_Inside_Toggle_No_Issue()
{
    var doc = Parse("<Toggle id='tg'><Show on='state-selected'><Icon name='ui:check'/></Show></Toggle>");
    var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
    CollectionAssert.DoesNotContain(codes, Code,
        "state-* inside a <Toggle> resolves the Toggle as a state source.");
}

[Test]
public void NoSource_Message_Names_Btn_Tab_Toggle()
{
    var doc = Parse("<Frame><Show on='state-pressed'/></Frame>");
    var issue = IRWalker.Walk(doc).First(i => i.Code == Code);
    StringAssert.Contains("<Tab>", issue.Message);
    StringAssert.Contains("<Toggle>", issue.Message);
}
```

- [ ] **Step 2: Run to verify failure**

`run_tests(filter="StateTriggerRulesTests")`.
Expected: FAIL — `IsStateSourceTag` does not exist; the `hasStateSourceAncestor` parameter name differs; message lacks Tab/Toggle.

- [ ] **Step 3: Implement in `StateTriggerRules`**

```csharp
private static readonly HashSet<string> StateSourceTags =
    new HashSet<string> { "Btn", "Tab", "Toggle" };

/// <summary>True if <paramref name="tag"/> instantiates an IStateSource-backed control
/// (broadcasts InteractState). Extend this set when a new clickable opts in.</summary>
public static bool IsStateSourceTag(string tag) => StateSourceTags.Contains(tag);

public static IEnumerable<LintIssue> CheckStateSource(ElementNode n, bool hasStateSourceAncestor)
{
    if (hasStateSourceAncestor) yield break;
    if (!StateTriggerTags.Contains(n.Tag)) yield break;
    if (!n.Attributes.TryGetValue("on", out var on)) yield break;
    if (!BareStateValues.Contains(on)) yield break;

    yield return new LintIssue(
        NoSourceCode, n.Tag, n.Id,
        $"<{n.Tag} on=\"{on}\">: no <Btn>/<Tab>/<Toggle> ancestor. state-* resolves upward to the " +
        "nearest clickable — place it inside a <Btn>/<Tab>/<Toggle>, or use state-...@<id>.");
}
```

- [ ] **Step 4: Implement in `IRWalker`**

Rename the `hasBtnAncestor` parameter/arguments to `hasStateSourceAncestor` throughout `Walk`/`WalkNode` (lines 16, 22, 33, 38, 71). Change the descent computation (line 74):

```csharp
var childHasStateSourceAncestor = hasStateSourceAncestor || StateTriggerRules.IsStateSourceTag(node.Tag);
```

and pass `childHasStateSourceAncestor` to the recursive `WalkNode` (line 93) and `hasStateSourceAncestor` to `CheckStateSource` (line 71).

- [ ] **Step 5: Run to verify pass**

`read_console(types=["error"])` (none), then `run_tests(filter="StateTriggerRulesTests")` and `run_tests(filter="IRWalkerTests")` and `run_tests(filter="TabRulesTests")`.
Expected: all PASS.

- [ ] **Step 6: CLI smoke + lint + commit**

Run the CLI lint over the bundled XML to confirm no new errors:

```bash
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/
```

Then:

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add -A
git commit -m "feat: PUI-STATE-NO-SOURCE recognises <Tab>/<Toggle> as state-source ancestors"
```

---

## Task 10: Skill docs + XSD test coverage

CLAUDE.md mandates the SKILL update in the same PR. No `.ui.xml` semantics change beyond the new attrs/event.

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`, `.claude/skills/scripting-promptugui-csharp/SKILL.md`
- Modify (optional coverage): `Tests/EditMode/Editor/XsdGeneratorTests.cs`

- [ ] **Step 1: XSD coverage assertion (optional but cheap)**

In `Tests/EditMode/Editor/XsdGeneratorTests.cs`, find the test that asserts a reflected control's attributes (substring style, `StringAssert.Contains`). Add assertions that the generated schema contains `selectedColor` within the `<Tab>` and `<Toggle>` element definitions (mirror the closest existing assertion exactly). Run `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="XsdGeneratorTests")`.
Expected: PASS (attrs are reflected automatically).

- [ ] **Step 2: Update the XML authoring skill**

In `.claude/skills/authoring-promptugui-xml/SKILL.md`:
- In the `<Tab>` and `<Toggle>` attribute tables, add rows: `hoverColor`, `pressedColor`, `selectedColor`, `disabledColor` — "state tint multipliers (uGUI ColorTint semantics); `selectedColor` applies while the control is the active/`isOn` one at rest. Present ⇒ the control's Selectable transition switches to None and the tint fans out to bg + descendant graphics (opt a child out with `stateReact="false"`)."
- In the `on=` events table (Triggers/Animations/`<Show>`), add `state-selected` — "fires while the source control is the active/`isOn` one; meaningful only on `<Tab>`/`<Toggle>` sources (a `<Btn>` never emits it)."
- Update the `PUI-STATE-NO-SOURCE` lint row: the upward source can now be `<Btn>`, `<Tab>`, or `<Toggle>`.
- Where `<Show>`'s valid `on=` values are listed, change four → five (add `state-selected`).

- [ ] **Step 3: Update the C# scripting skill**

In `.claude/skills/scripting-promptugui-csharp/SKILL.md`:
- Rename `BtnState` → `InteractState` everywhere; add the `Selected` value and the one-line composite note ("Selected = active control at rest; Hover/Pressed/Disabled override transiently").
- Where `Btn.OnState` is documented, add `Tab.OnState` and `Toggle.OnState` (same `Observable<InteractState>` type).

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "doc: SKILL + XSD test updates for clickable state visuals (InteractState, *Color on Tab/Toggle, state-selected, Tab/Toggle.OnState)"
```

---

## Task 11: PlayMode integration — Tab tint fade over real frames + selected/overlay

Mirrors `BtnStateVisualsPlayTests.cs` exactly: drive via `PuiToggle.SimulateState(ordinal)` / `Tab.IsOn` (which route through the REAL `Selectable.DoStateTransition` / `Toggle.onValueChanged`) + real frame yields for the LitMotion fade. **No EventSystem/`ExecuteEvents`** — the existing Btn PlayMode test deliberately avoids that harness, and so do we.

**Files:**
- Create: `Tests/PlayMode/Controls/TabStateVisualsPlayTests.cs`

- [ ] **Step 1: Write the failing PlayMode test (full body)**

Create `Tests/PlayMode/Controls/TabStateVisualsPlayTests.cs`:

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    // PlayMode integration for <Tab>: exercises the StateTintReactor fade over REAL frames and
    // proves (1) pressed tint + <Show> swap revert, (2) the persistent Selected/isOn state drives
    // state-selected while the selectedSprite overlay (independent Toggle.graphic channel) stays
    // visible during a transient hover. Drives via SimulateState / IsOn — same path a pointer takes.
    public class TabStateVisualsPlayTests
    {
        private const int Normal = 0;
        private const int Highlighted = 1;
        private const int Pressed = 2;
        private static readonly Color Half = new Color(0.5019608f, 0.5019608f, 0.5019608f, 1f);

        [SetUp]
        public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = false; }

        [TearDown]
        public void TearDown() { UI.ResetForTests(); StateTintReactor.TestForceInstant = false; }

        [UnityTest]
        public IEnumerator Press_then_normal_fades_bg_tint_and_swaps_Show_over_real_frames()
        {
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'>
    <Tab id='t' pressedColor='#808080'>
      <Show id='sn' on='state-normal'><Image id='n'/></Show>
      <Show id='sp' on='state-pressed'><Image id='p'/></Show>
    </Tab>
  </TabBar>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");
            yield return null;

            var tab = screen.Get<Tab>("bar/t");
            var pt = tab.GameObject.GetComponent<PuiToggle>();
            Assert.IsNotNull(pt, "Tab should host a PuiToggle");
            var bg = tab.GameObject.GetComponent<UnityImage>();
            var bgBase = bg.color;

            var sn = screen.Get<Show>("bar/t/sn");
            var sp = screen.Get<Show>("bar/t/sp");
            Assert.IsTrue(sn.GameObject.activeSelf, "normal Show active at open");
            Assert.IsFalse(sp.GameObject.activeSelf, "pressed Show inactive at open");

            pt.SimulateState(Pressed);
            Assert.IsFalse(sn.GameObject.activeSelf, "normal Show hidden when pressed");
            Assert.IsTrue(sp.GameObject.activeSelf, "pressed Show shown when pressed");

            yield return new WaitForSeconds(0.2f);
            AssertColorsEqual(bgBase * Half, bg.color, "pressed bg tint settles at base * #808080");

            pt.SimulateState(Normal);
            Assert.IsTrue(sn.GameObject.activeSelf, "normal Show active again after release");
            Assert.IsFalse(sp.GameObject.activeSelf, "pressed Show hidden again after release");

            yield return new WaitForSeconds(0.2f);
            AssertColorsEqual(bgBase, bg.color, "bg tint reverts to base after returning to Normal");
        }

        [UnityTest]
        public IEnumerator IsOn_drives_state_selected_Show_while_overlay_persists_through_hover()
        {
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'>
    <Tab id='t' selectedSprite='ui:tab_on'>
      <Show id='ssel' on='state-selected'><Image id='sel'/></Show>
    </Tab>
  </TabBar>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");
            yield return null;

            var tab = screen.Get<Tab>("bar/t");
            var pt = tab.GameObject.GetComponent<PuiToggle>();
            var ssel = screen.Get<Show>("bar/t/ssel");

            // Not active yet: at open the (only) Tab may be auto-selected by TabBar; force a known
            // off-state first so the transition to on is unambiguous.
            tab.IsOn = false;
            Assert.IsFalse(ssel.GameObject.activeSelf, "state-selected Show hidden when off");

            tab.IsOn = true;
            Assert.IsTrue(ssel.GameObject.activeSelf, "state-selected Show shown when active at rest");
            Assert.IsTrue(pt.graphic != null && pt.graphic.enabled, "selectedSprite overlay enabled when on");

            // Transient hover overrides Selected in the broadcast, but the overlay is an independent
            // isOn-driven channel and must stay visible.
            pt.SimulateState(Highlighted);
            Assert.IsTrue(pt.graphic.enabled, "overlay stays visible during hover (independent channel)");
            Assert.IsFalse(ssel.GameObject.activeSelf, "state-selected Show yields to Hover transiently");

            pt.SimulateState(Normal);
            Assert.IsTrue(ssel.GameObject.activeSelf, "state-selected Show returns at rest while still on");
            yield return null;
        }

        private static void AssertColorsEqual(Color expected, Color actual, string msg)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.02f), $"{msg} (r)");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.02f), $"{msg} (g)");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.02f), $"{msg} (b)");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.02f), $"{msg} (a)");
        }
    }
}
```

> The second test references an icon key `ui:tab_on` — if the test icon set lacks it, swap to any sprite key the other PlayMode tests use, or assert overlay existence via `pt.graphic != null` only. `pt.graphic` is the `Toggle.graphic` (Tab's overlay), enabled/disabled by uGUI from `isOn`.

- [ ] **Step 2: Run to verify it passes (runtime already implemented by Tasks 1-9)**

`run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="TabStateVisualsPlayTests")`.
Expected: PASS. If a test fails, debug with `superpowers:systematic-debugging` — do not weaken assertions to force green.

- [ ] **Step 3: Full-suite regression**

Run all three suites to confirm nothing regressed:
- `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])`
- `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])`
- `run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])`
Expected: all green. Record the summary counts.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test: PlayMode integration — Tab press/hover tint + <Show> swap + selected overlay persistence"
```

---

## Final verification (before PR)

- [ ] All three EditMode/EditorOnly/PlayMode suites green (record counts).
- [ ] `dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx` clean.
- [ ] `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` exits 0.
- [ ] Both SKILL.md files updated; spec §6 (no XSD work) matches reality.
- [ ] `git log --oneline` shows the 11 focused commits on `feat/clickable-state-visuals`.
- [ ] Use `superpowers:finishing-a-development-branch` to choose merge/PR (note: depends on `feat/btn-state-visuals` being merged first, since this branched off it).
