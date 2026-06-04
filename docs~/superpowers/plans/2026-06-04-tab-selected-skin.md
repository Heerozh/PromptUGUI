# Tab single-image + selection-aware base — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse `<Tab>` to a single bg Graphic — `selectedSprite` becomes an `isOn`-driven `overrideSprite` swap, `selectedColor` becomes the bg's selection-aware base colour — and share the selection-aware base with `<Toggle>`.

**Architecture:** Remove Tab's `_overlay` child. `selectedSprite` swaps `_bg.overrideSprite` while `isOn` (mirrors `<Btn pressedSprite>`). `StateTintReactor` gains a `selectedBase` + a `SetSelected(bool)` push so the bg's base colour is `selectedColor` while selected and `color` otherwise; Hover/Pressed/Disabled `*Color`/`*Modulate` layer on top. The control (Tab/Toggle) pushes `isOn` into the reactor on every value change and re-asserts on ReSolve. `StateBroadcaster` is untouched (the selection signal is a control push, not a broadcast change).

**Tech Stack:** Unity 6 uGUI, C# (LangVer 9), R3 (Cysharp), NUnit EditMode/PlayMode via Unity MCP. Spec: `docs~/superpowers/specs/2026-06-04-tab-selected-skin-single-image-design.md`.

**Branch:** `feat/tab-selected-skin` (already created; do NOT commit to `main`).

**Per-task verification loop (Unity MCP):** after every source edit, before running tests:
```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Only when the console is error-free, run the focused test class:
```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="<ClassName>")
```
If MCP is unavailable: reconnect, or STOP and ask the user to open Unity + start the MCP server (do not fall back to batch-mode Unity). `run_tests` here is occasionally flaky — if a run hangs or reports "failed to initialize", the compile-clean console from `read_console` is the reliable gate; retry the run once, then surface the issue.

---

## File Structure

- **Modify** `Runtime/Controls/Internal/StateTintReactor.cs` — add `_selectedBase` (`Color?`) + `_selected` (`bool`); `Configure` gains a defaulted `selectedBase` param; `BaseFor` becomes selection-aware; new `SetSelected(bool)`.
- **Modify** `Runtime/Controls/Internal/StateTintInstaller.cs` — `Install` gains a defaulted `selectedBase` param (targetGraphic only), its install gate includes `selectedBase.HasValue`, and it **returns** the targetGraphic's reactor.
- **Modify** `Runtime/Controls/Tab.cs` — drop `_overlay`/`EnsureOverlay`/`_toggle.graphic` wiring; add `_selectedSprite` + `ApplySelectedSprite()`; `selectedSprite` setter swaps `overrideSprite` and flips `transition=None`; `OnAfterApply` resolves `selectedBase`, captures the bg reactor, re-asserts sprite + `SetSelected(IsOn)`; `OnIsOnChanged` swaps sprite + `SetSelected`.
- **Modify** `Runtime/Controls/Toggle.cs` — keep the `Checkmark` overlay; `selectedColor` moves from the Selected absolute slot to `selectedBase`; capture the bg reactor; push `SetSelected` on value change + ReSolve.
- **Modify** `Tests/EditMode/Controls/TabTests.cs` — rewrite the 4 "Overlay" tests to the `overrideSprite`-swap contract.
- **Modify** `Tests/EditMode/Controls/TabStateTests.cs` — add hover-on-selected stability + selectedColor-only-installs tests.
- **Modify** `Tests/EditMode/Controls/ToggleStateTests.cs` — add checked-toggle hover stability test.
- **Modify** `.claude/skills/authoring-promptugui-xml/SKILL.md` — Tab/Toggle rows, `tint` caveat, Btn-state-visuals section, uGUI table, Tabs section, cheatsheet.

`Runtime/Controls/Btn.cs` is **not** edited: its `Install(GameObject, _btn, Children, abs, mod)` call compiles unchanged against the new defaulted signature and ignores the returned reactor (`selectedBase` defaults to null → no selection-aware base, correct for a button with no `isOn`).

---

## Task 1: `<Tab selectedSprite>` → `overrideSprite` swap (remove overlay)

**Files:**
- Modify: `Runtime/Controls/Tab.cs` (fields `15-19`; `OnIsOnChanged` `75-80`; `SelectedSprite` setter `203-213`; `EnsureOverlay` `240-257`; `OnAfterApply` `282-289`)
- Test: `Tests/EditMode/Controls/TabTests.cs` (`166-198`, `232-238`)

- [ ] **Step 1: Rewrite the failing overlay test to the swap contract**

In `Tests/EditMode/Controls/TabTests.cs`, replace the method `Tab_SelectedSprite_Creates_Overlay_Wired_To_Toggle_Graphic` (lines ~165-181) with:

```csharp
[Test]
public void Tab_SelectedSprite_Swaps_OverrideSprite_When_IsOn()
{
    LogAssert.Expect(LogType.Warning,
        new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
    var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
    UI.SpriteResolver = key => key == "ui:tab_sel" ? stub : null;
    var t = OpenTab("<Tab id='t' selectedSprite='ui:tab_sel'/>");
    var bg = t.GameObject.GetComponent<UnityImage>();
    var toggle = t.GameObject.GetComponent<UnityToggle>();

    Assert.IsNull(t.GameObject.transform.Find("Overlay"), "no Overlay child in the single-image model");
    Assert.IsNull(toggle.graphic, "UnityToggle.graphic stays null (no overlay)");
    Assert.AreEqual(Selectable.Transition.None, toggle.transition, "selectedSprite flips transition off ColorTint");

    Assert.IsNull(bg.overrideSprite, "not selected -> no overrideSprite");
    t.IsOn = true;
    Assert.AreSame(stub, bg.overrideSprite, "selected -> bg shows selectedSprite via overrideSprite");
    Assert.AreSame(stub, t.GameObject.GetComponent<UnityImage>().overrideSprite);
    t.IsOn = false;
    Assert.IsNull(bg.overrideSprite, "deselected -> overrideSprite cleared, back to authored sprite");
}
```

Then replace the three no-overlay tests `Tab_Without_SelectedSprite_Has_No_Overlay` (~183-189), `Tab_Empty_SelectedSprite_Does_Not_Create_Overlay` (~192-199), and `Tab_None_SelectedSprite_Does_Not_Create_Overlay` (~232-239) with:

```csharp
[Test]
public void Tab_Without_SelectedSprite_No_Swap()
{
    LogAssert.Expect(LogType.Warning,
        new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
    var t = OpenTab("<Tab id='t'/>");
    var bg = t.GameObject.GetComponent<UnityImage>();
    t.IsOn = true;
    Assert.IsNull(bg.overrideSprite, "no selectedSprite -> no swap even when selected");
}

[Test]
public void Tab_Empty_SelectedSprite_No_Swap()
{
    LogAssert.Expect(LogType.Warning,
        new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
    var t = OpenTab("<Tab id='t' selectedSprite=''/>");
    var bg = t.GameObject.GetComponent<UnityImage>();
    t.IsOn = true;
    Assert.IsNull(bg.overrideSprite, "empty selectedSprite is a no-op even when selected");
}

[Test]
public void Tab_None_SelectedSprite_No_Swap()
{
    LogAssert.Expect(LogType.Warning,
        new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
    var t = OpenTab("<Tab id='t' selectedSprite='none'/>");
    var bg = t.GameObject.GetComponent<UnityImage>();
    t.IsOn = true;
    Assert.IsNull(bg.overrideSprite, "selectedSprite='none' is a no-op even when selected");
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabTests")
```
Expected: `Tab_SelectedSprite_Swaps_OverrideSprite_When_IsOn` FAILS (today `selectedSprite` still creates an Overlay and never sets `overrideSprite`; `transform.Find("Overlay")` is non-null). The three `_No_Swap` tests pass trivially (no `overrideSprite` is ever set today) — that's fine.

- [ ] **Step 3: Replace the overlay implementation with the overrideSprite swap**

In `Runtime/Controls/Tab.cs`:

(a) Remove the `_overlay` field. Change lines 15-16 from:
```csharp
        private UnityImage _bg;
        private UnityImage _overlay;
```
to:
```csharp
        private UnityImage _bg;
        private UnityEngine.Sprite _selectedSprite;   // resolved selectedSprite, swapped onto _bg.overrideSprite while IsOn
```

(b) Replace the `SelectedSprite` setter (lines 203-213) with the setter + helper:
```csharp
        [UIAttr(IsSprite = true), Preserve]
        public string SelectedSprite
        {
            set
            {
                // "" / "none" → no selected sprite (no swap). No default overlay to clear.
                if (string.IsNullOrEmpty(value) || value == "none")
                {
                    _selectedSprite = null;
                    ApplySelectedSprite();
                    return;
                }
                _selectedSprite = UI.ResolveSprite(value);
                // Mirror <Btn pressedSprite>: the swapped sprite IS the selected feedback, so take the
                // bg off uGUI's built-in ColorTint to avoid double-tinting it.
                _toggle.transition = Selectable.Transition.None;
                ApplySelectedSprite();
            }
        }

        // Show the selected sprite by overriding the bg's displayed sprite while IsOn — the authored
        // `sprite` (_bg.sprite) is never touched. Keyed on IsOn (persistent), so hover/press of the
        // selected tab (transient state only) never disturb it.
        private void ApplySelectedSprite()
        {
            if (_bg == null) return;
            _bg.overrideSprite = (IsOn && _selectedSprite != null) ? _selectedSprite : null;
        }
```

(c) Delete `EnsureOverlay` entirely (old lines 240-257, the whole `private void EnsureOverlay(...) { ... }` block).

(d) In `OnIsOnChanged` (lines 75-80), add the swap call:
```csharp
        private void OnIsOnChanged(bool isOn)
        {
            _changed.OnNext(isOn);
            if (isOn) _selected.OnNext(Unit.Default);
            ApplyBindFrame(isOn);
            ApplySelectedSprite();
        }
```

(e) In `OnAfterApply` (lines 282-289), add a final `ApplySelectedSprite();` so a declared `isOn="true"` (auto-select) swaps after attributes settle:
```csharp
        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            _toggle.interactable = Interactable;
            var abs = StateColorSet.Resolve(_hoverColor, _pressedColor, _selectedColor, _disabledColor);
            var mod = StateColorSet.Resolve(_hoverModulate, _pressedModulate, _selectedModulate, _disabledModulate);
            StateTintInstaller.Install(GameObject, _toggle, Children, abs, mod);
            ApplySelectedSprite();
        }
```
(Note: the `abs`/`mod`/`Install` lines stay as-is in this task — Task 2 changes them.)

- [ ] **Step 4: Run the tests to verify they pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabTests")
```
Expected: all `TabTests` PASS. Also run `filter="TabStateTests"` — the existing `PressedModulate_*` / `SelectedColor_*` / `DeclaredIsOn_*` tests must stay green (this task didn't touch the colour path).

- [ ] **Step 5: Lint check**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: no changes required (exit 0). If it reports formatting, run `dotnet format whitespace PromptUGUI.Lint.slnx` and re-verify.

- [ ] **Step 6: Commit**

```bash
git add Runtime/Controls/Tab.cs Tests/EditMode/Controls/TabTests.cs
git commit -m "$(cat <<'EOF'
feat: Tab selectedSprite swaps bg overrideSprite (drop overlay)

selectedSprite now swaps _bg.overrideSprite while IsOn (mirrors Btn.pressedSprite)
instead of creating an Overlay child wired to Toggle.graphic; flips transition=None.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Selection-aware base — reactor + installer infra + Tab `selectedColor`

**Files:**
- Modify: `Runtime/Controls/Internal/StateTintReactor.cs` (fields `33-39`; `Configure` `64-85`; `BaseFor` `88`)
- Modify: `Runtime/Controls/Internal/StateTintInstaller.cs` (`Install` `16-46`; `InstallReactor` `69-75`)
- Modify: `Runtime/Controls/Tab.cs` (fields; `OnIsOnChanged`; `OnAfterApply`)
- Test: `Tests/EditMode/Controls/TabStateTests.cs`

- [ ] **Step 1: Write the failing tests (hover-on-selected stability + selectedColor-only install)**

In `Tests/EditMode/Controls/TabStateTests.cs`, add `Hover = 1` to the const line and append these tests inside the class (before the closing braces). The class already declares `private const int Normal = 0, Pressed = 2;` — change it to:
```csharp
        private const int Normal = 0, Hover = 1, Pressed = 2;
```
Then add:
```csharp
        // The core regression: a transparent-normal tab with selectedColor must NOT fall back to the
        // transparent base when the SELECTED tab is hovered (broadcaster emits Hover, suppressing
        // Selected). With selection-aware base, no hoverColor => stays at selectedColor.
        [Test]
        public void HoverOnSelectedTab_StaysSelectedColor()
        {
            var tab = BuildTab("color='#00000000' selectedColor='#076DD7'");
            var pt = tab.GameObject.GetComponent<PuiToggle>();
            var bg = tab.GameObject.GetComponent<UnityImage>();
            var sel = new Color(0x07 / 255f, 0x6D / 255f, 0xD7 / 255f, 1f);

            tab.IsOn = true;                       // active at rest -> Selected
            Assert.That(bg.color.r, Is.EqualTo(sel.r).Within(0.001f), "selected idle = selectedColor");

            pt.SimulateState(Hover);               // hover the already-selected tab
            Assert.That(bg.color.r, Is.EqualTo(sel.r).Within(0.001f), "selected+hover stays selectedColor (r)");
            Assert.That(bg.color.b, Is.EqualTo(sel.b).Within(0.001f), "selected+hover stays selectedColor (b)");
            Assert.That(bg.color.a, Is.EqualTo(1f).Within(0.001f), "selected+hover does not fall back to transparent base");
        }

        // hoverColor (absolute) composes on top of the selection-aware base: present => wins; absent
        // => the current base (selectedColor when selected, color when not).
        [Test]
        public void HoverColorOverSelectedTab_Composes()
        {
            var tab = BuildTab("color='#00000000' selectedColor='#076DD7' hoverColor='#ffffff'");
            var pt = tab.GameObject.GetComponent<PuiToggle>();
            var bg = tab.GameObject.GetComponent<UnityImage>();

            tab.IsOn = true;
            pt.SimulateState(Hover);
            Assert.That(bg.color.r, Is.EqualTo(1f).Within(0.001f), "selected+hover uses hoverColor when set");
            pt.SimulateState(Normal);
            Assert.That(bg.color.r, Is.EqualTo(0x07 / 255f).Within(0.001f), "selected idle back to selectedColor");
        }

        // selectedColor alone (no hover/pressed/modulate) must still install the bg reactor and flip
        // transition=None — otherwise the selection-aware base never takes effect.
        [Test]
        public void SelectedColorOnly_InstallsReactor_AndFlipsTransitionNone()
        {
            var tab = BuildTab("color='#202020' selectedColor='#076DD7'");
            var pt = tab.GameObject.GetComponent<PuiToggle>();
            var bg = tab.GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(Selectable.Transition.None, pt.transition);
            Assert.IsNotNull(bg.GetComponent<StateTintReactor>(), "selectedColor installs the bg reactor");
        }
```

- [ ] **Step 2: Run to verify they fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabStateTests")
```
Expected: `HoverOnSelectedTab_StaysSelectedColor` FAILS (today selected+Hover resolves `hoverColor ?? baseColor` = transparent `#00000000`, so `bg.color.a ≈ 0` and `.r ≈ 0`). `HoverColorOverSelectedTab_Composes` should pass already (hoverColor is set). `SelectedColorOnly_*` passes already (selectedColor is currently a Selected absolute → installs). They fail/pass mixed now; all must pass after Step 3-5.

- [ ] **Step 3: Add the selection-aware base to `StateTintReactor`**

In `Runtime/Controls/Internal/StateTintReactor.cs`:

(a) After the `_baseColor` field (line 35), add:
```csharp
        private Color? _selectedBase;       // base while the source is selected (Tab/Toggle isOn); null ⇒ none
        private bool _selected;             // pushed by the owning control via SetSelected
```

(b) Change the `Configure` signature (line 64) and store the new arg. Replace:
```csharp
        public void Configure(StateColorSet absolutes, StateColorSet modulates, float fade)
        {
```
with:
```csharp
        public void Configure(StateColorSet absolutes, StateColorSet modulates, float fade, Color? selectedBase = null)
        {
```
and inside, alongside the existing `_absolutes = absolutes; _modulates = modulates; _fade = fade;` block (lines 71-73), add:
```csharp
            _selectedBase = selectedBase;
```
(Place it right after `_fade = fade;`. Do not reset `_selected` — the control owns it across re-Configure.)

(c) Change `BaseFor` (line 88) from:
```csharp
        private Color BaseFor(InteractState state) => _absolutes.For(state) ?? _baseColor;
```
to:
```csharp
        private Color BaseFor(InteractState state)
            => _absolutes.For(state)
               ?? ((_selected && _selectedBase.HasValue) ? _selectedBase.Value : _baseColor);
```

(d) Add the push method (e.g. right after `Configure`, before `MultiplierFor`):
```csharp
        /// <summary>
        /// Pushed by the owning Tab/Toggle on every isOn change (and re-asserted on ReSolve): selects
        /// the selection-aware base. Repaints the current state so a selected control at rest shows
        /// its selected base immediately. Read as a push (not from the broadcaster) because the
        /// broadcaster suppresses Selected under a transient state and does not re-emit on isOn-only
        /// changes.
        /// </summary>
        public void SetSelected(bool on)
        {
            _selected = on;
            if (_source != null) OnState(_source.Current);
        }
```

- [ ] **Step 4: Make `Install` take `selectedBase` and return the targetGraphic reactor**

In `Runtime/Controls/Internal/StateTintInstaller.cs`, replace the whole `Install` method (lines 16-46) and the `InstallReactor` helper (lines 69-75) with:

```csharp
        internal static StateTintReactor Install(
            GameObject root,
            Selectable selectable,
            IReadOnlyList<IControl> children,
            StateColorSet absolutes,
            StateColorSet modulates,
            Color? selectedBase = null)
        {
            if (!absolutes.HasAny && !modulates.HasAny && !selectedBase.HasValue) return null;

            selectable.transition = Selectable.Transition.None;

            var blocked = new HashSet<GameObject>();
            foreach (var child in children)
                CollectBlocked(child as Control, blocked);

            var fade = StateTintReactor.DefaultFade;
            var target = selectable.targetGraphic;
            StateTintReactor targetReactor = null;
            foreach (var g in root.GetComponentsInChildren<Graphic>(includeInactive: true))
            {
                if (blocked.Contains(g.gameObject)) continue;
                var isTarget = ReferenceEquals(g, target);
                // Descendants only matter for the fan-out multiplier: with no modulates, a descendant
                // reactor would be a no-op (base × white). Skip them so we don't add idle MonoBehaviours
                // + OnState subscriptions. The targetGraphic always installs (it carries the absolutes
                // and the selection-aware base).
                if (!isTarget && !modulates.HasAny) continue;
                // Absolutes + selectedBase apply ONLY to the control's base graphic (targetGraphic) —
                // fanning them out would paint label/icon the bg colour. Descendants get the multiplier only.
                var abs = isTarget ? absolutes : default;
                var selBase = isTarget ? selectedBase : null;
                var reactor = InstallReactor(g, abs, modulates, fade, selBase);
                if (isTarget) targetReactor = reactor;
            }
            return targetReactor;
        }
```

and:
```csharp
        private static StateTintReactor InstallReactor(Graphic graphic, StateColorSet absolutes, StateColorSet modulates, float fade, Color? selectedBase)
        {
            if (graphic == null) return null;
            var reactor = graphic.GetComponent<StateTintReactor>()
                          ?? graphic.gameObject.AddComponent<StateTintReactor>();
            reactor.Configure(absolutes, modulates, fade, selectedBase);
            return reactor;
        }
```

- [ ] **Step 5: Wire Tab's `selectedColor` as the selected base**

In `Runtime/Controls/Tab.cs`:

(a) Add a reactor field next to `_selectedSprite` (the field added in Task 1):
```csharp
        private StateTintReactor _bgReactor;
```

(b) Replace `OnAfterApply` with:
```csharp
        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            _toggle.interactable = Interactable;
            // selectedColor is the selection-aware BASE (not a Selected-state absolute), so pass null
            // for the Selected absolute; selectedModulate stays the Selected multiplier.
            var abs = StateColorSet.Resolve(_hoverColor, _pressedColor, null, _disabledColor);
            var mod = StateColorSet.Resolve(_hoverModulate, _pressedModulate, _selectedModulate, _disabledModulate);
            Color? selectedBase = string.IsNullOrWhiteSpace(_selectedColor)
                ? (Color?)null
                : UI.Theme.Resolve(_selectedColor);
            _bgReactor = StateTintInstaller.Install(GameObject, _toggle, Children, abs, mod, selectedBase);
            ApplySelectedSprite();
            _bgReactor?.SetSelected(IsOn);     // re-assert selection after ReSolve / first install
        }
```

(c) Add the `SetSelected` push to `OnIsOnChanged` (after `ApplySelectedSprite();`):
```csharp
        private void OnIsOnChanged(bool isOn)
        {
            _changed.OnNext(isOn);
            if (isOn) _selected.OnNext(Unit.Default);
            ApplyBindFrame(isOn);
            ApplySelectedSprite();
            _bgReactor?.SetSelected(isOn);
        }
```

- [ ] **Step 6: Run to verify pass + no regressions**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabStateTests")
```
Expected: all `TabStateTests` PASS — the new three plus the existing `DeclaredIsOn_TemplateWrappedTab_ShowsSelectedColorAtOpen`, `SelectedColor_SurvivesReSolve`, `SelectedModulate_AppliesToActiveTabAtRest`, `PressedModulate_*` (selectedColor is now the base; the resting-Selected bg is still `selectedColor`, so those assertions hold). Then run `filter="BtnStateTests"` — Btn must be unaffected (its `Install` call ignores the new defaulted param/return).

- [ ] **Step 7: Lint check**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: exit 0 (fix whitespace if flagged, re-verify).

- [ ] **Step 8: Commit**

```bash
git add Runtime/Controls/Internal/StateTintReactor.cs Runtime/Controls/Internal/StateTintInstaller.cs Runtime/Controls/Tab.cs Tests/EditMode/Controls/TabStateTests.cs
git commit -m "$(cat <<'EOF'
feat: selection-aware base; Tab selectedColor is the selected bg base

StateTintReactor gains a selectedBase + SetSelected(bool) push; Install takes
selectedBase (targetGraphic only) and returns the reactor. Tab.selectedColor is
now the bg base while selected, so hover/pressed/disabled layer on top and a
selected tab no longer flickers to the transparent normal base on hover.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: `<Toggle>` shares the selection-aware base (keeps checkmark overlay)

**Files:**
- Modify: `Runtime/Controls/Toggle.cs` (fields; `onValueChanged` listener `87`; `OnAfterApply` `178-184`)
- Test: `Tests/EditMode/Controls/ToggleStateTests.cs`

- [ ] **Step 1: Write the failing test (checked-toggle hover stability)**

In `Tests/EditMode/Controls/ToggleStateTests.cs`, add `Hover = 1` to the const line (`private const int Normal = 0, Pressed = 2;` → `private const int Normal = 0, Hover = 1, Pressed = 2;`) and add:
```csharp
        // Mirror of Tab's regression on the Toggle's bg (the Background child): a checked toggle
        // hovered with no hoverColor must stay at selectedColor, not fall back to the transparent base.
        [Test]
        public void HoverOnCheckedToggle_StaysSelectedColor()
        {
            var tg = BuildToggle("color='#00000000' selectedColor='#076DD7'");
            var pt = tg.GameObject.GetComponent<PuiToggle>();
            var bg = tg.GameObject.transform.Find("Background").GetComponent<UnityImage>();
            var sel = new Color(0x07 / 255f, 0x6D / 255f, 0xD7 / 255f, 1f);

            tg.IsOn = true;                        // checked at rest -> Selected
            Assert.That(bg.color.r, Is.EqualTo(sel.r).Within(0.001f), "checked idle = selectedColor");

            pt.SimulateState(Hover);
            Assert.That(bg.color.r, Is.EqualTo(sel.r).Within(0.001f), "checked+hover stays selectedColor (r)");
            Assert.That(bg.color.a, Is.EqualTo(1f).Within(0.001f), "checked+hover does not fall back to transparent base");

            // Checkmark overlay (Toggle.graphic) is intact — Toggle keeps its composited check.
            var checkmark = tg.GameObject.transform.Find("Background/Checkmark");
            Assert.IsNotNull(checkmark, "Toggle keeps its Checkmark overlay");
        }
```
Add `using UnityImage = UnityEngine.UI.Image;` at the top of the file if not already present (the file uses `UnityEngine.UI` — confirm; add the alias only if missing).

- [ ] **Step 2: Run to verify it fails**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ToggleStateTests")
```
Expected: `HoverOnCheckedToggle_StaysSelectedColor` FAILS (checked+Hover today resolves `hoverColor ?? base` = transparent).

- [ ] **Step 3: Wire Toggle's selectedColor as the selected base**

In `Runtime/Controls/Toggle.cs`:

(a) Add a reactor field near `_bg` (around line 13):
```csharp
        private StateTintReactor _bgReactor;
```
(Ensure `using PromptUGUI.Controls.Internal;` is present — `StateColorSet` is already used, so it is.)

(b) Change the `onValueChanged` listener (line 87) from:
```csharp
            _toggle.onValueChanged.AddListener(v => _changed.OnNext(v));
```
to:
```csharp
            _toggle.onValueChanged.AddListener(v => { _changed.OnNext(v); _bgReactor?.SetSelected(v); });
```

(c) Replace `OnAfterApply` (lines 178-184) with:
```csharp
        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            _toggle.interactable = Interactable;
            // selectedColor is the selection-aware BASE (not a Selected absolute); selectedModulate
            // stays the Selected multiplier. Toggle keeps its Checkmark overlay unchanged.
            var abs = StateColorSet.Resolve(_hoverColor, _pressedColor, null, _disabledColor);
            var mod = StateColorSet.Resolve(_hoverModulate, _pressedModulate, _selectedModulate, _disabledModulate);
            Color? selectedBase = string.IsNullOrWhiteSpace(_selectedColor)
                ? (Color?)null
                : UI.Theme.Resolve(_selectedColor);
            _bgReactor = StateTintInstaller.Install(GameObject, _toggle, Children, abs, mod, selectedBase);
            _bgReactor?.SetSelected(IsOn);
        }
```
(`UI` here is `PromptUGUI.Application.UI` — the file references it fully-qualified elsewhere; use `PromptUGUI.Application.UI.Theme.Resolve(...)` if `UI` is not imported unqualified. Check the existing `Color` setter at line 122: it uses `UI.Theme.Resolve(value)`, so `UI` resolves unqualified — use `UI.Theme.Resolve`.)

- [ ] **Step 4: Run to verify pass + no regressions**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ToggleStateTests")
```
Expected: all `ToggleStateTests` PASS — the new test plus existing `Selected_ReadsWhenIsOnAtRest`, `PressedModulate_*`, `NoStateColor_*` (selectedColor as base still shows selectedColor at rest-checked).

- [ ] **Step 5: Lint check**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: exit 0.

- [ ] **Step 6: Commit**

```bash
git add Runtime/Controls/Toggle.cs Tests/EditMode/Controls/ToggleStateTests.cs
git commit -m "$(cat <<'EOF'
feat: Toggle selectedColor is the selection-aware bg base

Toggle's bg (Background child) base colour now tracks isOn like Tab; a checked
toggle hovered with no hoverColor stays at selectedColor. Checkmark overlay
(Toggle.graphic) unchanged.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Docs (SKILL) + full-suite verification

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`
- (Audit only) `.claude/skills/scripting-promptugui-csharp/SKILL.md`

- [ ] **Step 1: Update the XML authoring skill**

In `.claude/skills/authoring-promptugui-xml/SKILL.md`, make these edits (search for the quoted anchors):

1. **`<Tab>` built-in row** — change the `selectedSprite` description from the overlay wording ("creates the overlay swap (bound to `UnityToggle.graphic`, instant transition); omit `selectedSprite` for 'button mode' …") to:
   > `selectedSprite` (sprite key; while this Tab is the active/`isOn` one, swaps the bg's `overrideSprite` to it and reverts to `sprite` when deselected — no separate overlay node; keyed on `isOn` so hover/press of the selected tab never disturb it; setting it flips `transition` off uGUI ColorTint, like `<Btn pressedSprite>`; `""` / `none` = no swap)
   And change `selectedColor` in the same row to: "**absolute** bg base colour **while selected** (`targetGraphic` only; hover/pressed/disabled `*Color`/`*Modulate` layer on top of it)".

2. **`<Tab>` prose / `tint` row** — find the sentence "On `<Tab>` it applies to the bg only (where `color` lands) — the `selectedSprite` overlay is not tinted." (Tint blend modes section) and the matching clause in the `<Tab>` row "applies to the bg only — the `selectedSprite` overlay is not tinted". Replace both with: "On `<Tab>` `tint` applies to the bg; since `selectedSprite` now swaps the bg's own sprite, the selected sprite is tinted too."

3. **`<Toggle>` row** — change `selectedColor` from "absolute per-state bg colour … `selectedColor` applies while the Toggle is checked" to "**absolute** bg base colour while checked (`targetGraphic`/Background only; hover/pressed/disabled layer on top — same selection-aware base as `<Tab>`)".

4. **"Tabs" section** (the `## Tabs` prose) — replace the paragraph describing `selectedSprite` as an overlay bound to `UnityToggle.graphic` ("每个 `<Tab>` 自带 `sprite`（常态底）和 `selectedSprite`（选中时 overlay，绑到 `UnityToggle.graphic`…）…`selectedSprite=""`（空字符串）按'未声明'处理，不会创建 overlay。") with:
   > 每个 `<Tab>` 自带 `sprite`（常态底）；`selectedSprite` 在选中时把 bg 自己的 `overrideSprite` 换成它、取消选中再换回 `sprite`（单图，无独立 overlay 节点；由 `isOn` 驱动，hover/按下选中 tab 不影响它；设了会把 `transition` 切到 None）。不写 `selectedSprite` 时 Tab 退化成纯色按钮视觉，互斥仍在。`selectedSprite=""` / `none` = 无 swap（no-op）。

5. **uGUI 对照表 `<Tab>` row** — remove the `Overlay` auto-child and `graphic=Overlay` / `toggleTransition=None` wording; the Tab root is now just `UnityImage`(bg, `targetGraphic`, supports `overrideSprite` swap while selected) + `UnityToggle` (`graphic` unset). Auto-children: optional `Label` / `Icon` + author children only (no `Overlay`).

6. **Btn state visuals section** — in the `*Color` / `*Modulate` description, update the Tab/Toggle `selectedColor` line to "**selection-aware base** — the bg base colour while the control is the active/`isOn` one; hover/pressed/disabled compose on top (so a selected control with no `hoverColor` stays at `selectedColor`)". In the `pressedSprite` paragraph, add a sentence: "`<Tab selectedSprite>` is the same `overrideSprite`-swap mechanism keyed on `isOn` (the selected look) instead of `Pressed`."

7. **Cheatsheet `TAB/TOGGLE` line** — change to: `+ selectedColor (selection-aware bg base while active/isOn) / selectedModulate; <Tab selectedSprite>=overrideSprite swap on isOn (no overlay)`.

- [ ] **Step 2: Validate the skill's own XML examples still lint**

The skill's Tabs section examples (`<Tab … selectedSprite="ui:tab_selected" …>`) are illustrative, not loaded `.ui.xml`. No `.ui.xml` in the repo uses `<Tab>` (verified during spec). Run the lint CLI over `Runtime/Resources/` to confirm nothing regressed:
```bash
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/
```
Expected: exit 0.

- [ ] **Step 3: Audit the C# scripting skill**

```bash
grep -n -i "overlay\|selectedSprite\|selectedColor" .claude/skills/scripting-promptugui-csharp/SKILL.md
```
If any hit describes the Tab Overlay node or `selectedSprite`/`selectedColor` semantics, update it to match (single-image swap; selection-aware base). `OnState` / `InteractState` / `OnValueChanged` / `OnSelected` are unchanged — leave those. If no hits, no edit needed.

- [ ] **Step 4: Full EditMode + EditorOnly + XSD + PlayMode runs**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```
Expected: all green. The XSD generator tests (in EditorOnly) assert `<Tab>`/`<Toggle>` emit `selectedSprite`/`selectedColor`/`selectedModulate` — the attribute set is unchanged (reflection-driven), so they pass without edits. If `run_tests` is flaky, the compile-clean console is the reliable gate; retry once and report any genuine failures (do not mark complete on a hung run).

- [ ] **Step 5: Final lint**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: exit 0.

- [ ] **Step 6: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md .claude/skills/scripting-promptugui-csharp/SKILL.md
git commit -m "$(cat <<'EOF'
doc: SKILL — Tab selectedSprite=overrideSprite swap, selectedColor=selection-aware base

Update XML skill (Tab/Toggle rows, tint caveat, Btn state visuals, uGUI table,
Tabs section, cheatsheet) for the single-image Tab + selection-aware base.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review (completed during planning)

**Spec coverage:** §4.1 (remove overlay) → Task 1. §4.2 (overrideSprite swap, transition=None) → Task 1. §4.3 (selection-aware base) → Task 2. §4.4 (reactor `SetSelected`/`selectedBase`, Install returns reactor, gate includes selectedBase, Tab wiring) → Task 2. §4.5 (Toggle) → Task 3. §4.6 (broadcaster/triggers untouched) → no code change (verified: `StateBroadcaster` not edited). §5 tests 1-2,7-8 → Task 1; 3-6,9 → Task 2; 10 → Task 3; 11 (Btn) → Task 2 Step 6 regression; 12 (XSD) → Task 4 Step 4. §6 (skills) → Task 4.

**Placeholder scan:** none — every code step shows the full replacement code and exact line anchors.

**Type consistency:** `Configure(StateColorSet, StateColorSet, float, Color? selectedBase = null)`, `Install(GameObject, Selectable, IReadOnlyList<IControl>, StateColorSet, StateColorSet, Color? selectedBase = null) → StateTintReactor`, `SetSelected(bool)`, `ApplySelectedSprite()`, fields `_selectedSprite` (`UnityEngine.Sprite`) / `_bgReactor` (`StateTintReactor`) — names match across Tasks 1-3. Btn's existing `Install(...)` 5-arg call compiles against the 6-arg defaulted signature.
