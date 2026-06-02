# State Colours: absolute `*Color` + relative `*Modulate` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `hoverColor`/`pressedColor`/`selectedColor`/`disabledColor` **absolute** per-state colours (bg only) and add a new `*Modulate` family for the relative multiplier (fanned out to the whole subtree), composing as `(absolute ?? color) × (modulate ?? white)`.

**Architecture:** Reuse `StateTintReactor` / `StateTintInstaller` / `InteractState`. Introduce one internal value type `StateColorSet` (four optional colours keyed by state) used twice per reactor — once as absolute base overrides (applied only to `Selectable.targetGraphic`), once as relative multipliers (applied to targetGraphic + every descendant Graphic). Breaking same-name flip; no back-compat (0 repo `.ui.xml` usages).

**Tech Stack:** Unity 6 uGUI, R3, LitMotion, NUnit (EditMode/PlayMode), Unity MCP for test runs.

**Spec:** `docs~/superpowers/specs/2026-06-02-state-color-absolute-modulate-design.md`

---

## File Structure

| File | Responsibility | Action |
|---|---|---|
| `Runtime/Controls/Internal/StateColorSet.cs` | 4 optional colours keyed by `InteractState`; `For` / `Any` / `Resolve` | **Create** |
| `Runtime/Controls/Internal/StateTintReactor.cs` | per-graphic tween to `(abs ?? base) × (mod ?? white)` | Modify |
| `Runtime/Controls/Internal/StateTintInstaller.cs` | install reactors; absolutes → `targetGraphic` only, modulates → fan-out | Modify |
| `Runtime/Controls/Btn.cs` | rename `*Color`→`*Modulate` fields/setters; add absolute `*Color`; new Install call | Modify |
| `Runtime/Controls/Tab.cs` | same (incl. `selected*`) | Modify |
| `Runtime/Controls/Toggle.cs` | same (incl. `selected*`) | Modify |
| `Tests/EditMode/Controls/StateColorSetTests.cs` | unit-test the struct | **Create** |
| `Tests/EditMode/Controls/StateAbsoluteColorTests.cs` | absolute / compose / fallback / no-fan-out behaviours | **Create** |
| `Tests/EditMode/Controls/BtnStateTests.cs` `TabStateTests.cs` `ToggleStateTests.cs` | migrate multiply assertions `*Color`→`*Modulate` | Modify |
| `Tests/EditMode/Editor/XsdGeneratorTests.cs` | assert `*Modulate` also emitted | Modify |
| `Tests/PlayMode/Controls/BtnStateVisualsPlayTests.cs` `TabStateVisualsPlayTests.cs` | migrate `pressedColor`→`pressedModulate` | Modify |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` | rewrite Btn-state-visuals semantics + 3 table rows + cheatsheet | Modify |

Unity MCP feedback loop after every source edit:
```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Run a class via `run_tests(test_names=["PromptUGUI.Tests.EditMode.Controls.<Class>"])` (full namespace; bare method names match 0 tests). Poll with `get_test_job(job_id, wait_timeout=60)`; reconnect with a `refresh_unity` if Unity disconnects mid-run.

---

## Task 1: `StateColorSet` value type

**Files:**
- Create: `Runtime/Controls/Internal/StateColorSet.cs`
- Test: `Tests/EditMode/Controls/StateColorSetTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Controls/StateColorSetTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class StateColorSetTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void For_MapsEachState_NormalIsNull()
        {
            var set = new StateColorSet(Color.red, Color.green, Color.blue, Color.gray);
            Assert.AreEqual(Color.red, set.For(InteractState.Hover));
            Assert.AreEqual(Color.green, set.For(InteractState.Pressed));
            Assert.AreEqual(Color.blue, set.For(InteractState.Selected));
            Assert.AreEqual(Color.gray, set.For(InteractState.Disabled));
            Assert.IsNull(set.For(InteractState.Normal));
        }

        [Test]
        public void Any_TrueWhenAnyPresent_FalseWhenAllNull()
        {
            Assert.IsFalse(default(StateColorSet).Any);
            Assert.IsTrue(new StateColorSet(null, Color.red, null, null).Any);
        }

        [Test]
        public void Resolve_EmptyOrNull_BecomesNull_LiteralBecomesColor()
        {
            var set = StateColorSet.Resolve("", null, "#ff0000", "  ");
            Assert.IsNull(set.For(InteractState.Hover), "empty string -> null");
            Assert.IsNull(set.For(InteractState.Pressed), "null -> null");
            Assert.IsNull(set.For(InteractState.Disabled), "whitespace -> null");
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), set.For(InteractState.Selected));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run `StateColorSetTests`. Expected: COMPILE error / FAIL — `StateColorSet` does not exist.

- [ ] **Step 3: Create the struct**

Create `Runtime/Controls/Internal/StateColorSet.cs`:

```csharp
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Four optional colours keyed by <see cref="InteractState"/> (Hover / Pressed / Selected /
    /// Disabled; Normal has no entry). Used twice by <see cref="StateTintReactor"/>: as per-state
    /// ABSOLUTE base overrides (applied only to the control's <c>targetGraphic</c>) and as per-state
    /// relative MULTIPLIERS (white = identity, fanned out to the whole subtree). A null entry means
    /// "no override for that state".
    /// </summary>
    internal readonly struct StateColorSet
    {
        public readonly Color? Hover;
        public readonly Color? Pressed;
        public readonly Color? Selected;
        public readonly Color? Disabled;

        public StateColorSet(Color? hover, Color? pressed, Color? selected, Color? disabled)
        {
            Hover = hover;
            Pressed = pressed;
            Selected = selected;
            Disabled = disabled;
        }

        public Color? For(InteractState state) => state switch
        {
            InteractState.Hover => Hover,
            InteractState.Pressed => Pressed,
            InteractState.Selected => Selected,
            InteractState.Disabled => Disabled,
            _ => null,
        };

        public bool Any => Hover.HasValue || Pressed.HasValue || Selected.HasValue || Disabled.HasValue;

        /// <summary>Resolve four raw attribute strings (hex / CSS named / theme token; null / empty /
        /// whitespace ⇒ no override) into resolved colours via <see cref="UI.Theme"/>.</summary>
        public static StateColorSet Resolve(string hover, string pressed, string selected, string disabled)
            => new(R(hover), R(pressed), R(selected), R(disabled));

        private static Color? R(string v)
            => string.IsNullOrWhiteSpace(v) ? (Color?)null : UI.Theme.Resolve(v);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run `StateColorSetTests`. Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Internal/StateColorSet.cs Tests/EditMode/Controls/StateColorSetTests.cs
git commit -m "feat: StateColorSet — per-state optional colours (absolute/modulate carrier)"
```

---

## Task 2: Engine + controls — absolute `*Color` / relative `*Modulate`

This is one compile-unit (reactor + installer change the API the three controls call). Lead with the new RED behaviour tests, implement the engine + controls, migrate the existing multiply tests, then go green.

**Files:**
- Create: `Tests/EditMode/Controls/StateAbsoluteColorTests.cs`
- Modify: `Runtime/Controls/Internal/StateTintReactor.cs`, `Runtime/Controls/Internal/StateTintInstaller.cs`, `Runtime/Controls/Btn.cs`, `Runtime/Controls/Tab.cs`, `Runtime/Controls/Toggle.cs`
- Modify (test migration): `Tests/EditMode/Controls/BtnStateTests.cs`, `TabStateTests.cs`, `ToggleStateTests.cs`, `Tests/PlayMode/Controls/BtnStateVisualsPlayTests.cs`, `TabStateVisualsPlayTests.cs`

- [ ] **Step 1: Write the new RED behaviour tests**

Create `Tests/EditMode/Controls/StateAbsoluteColorTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class StateAbsoluteColorTests
    {
        [SetUp] public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = true; }
        [TearDown] public void TearDown() { UI.ResetForTests(); StateTintReactor.TestForceInstant = false; }

        // Two-tab bar so we can drive 'a' to Normal (select 'b') then to Selected (select 'a').
        private static (Tab a, Tab b) TwoTabs(string aAttrs, string aBody = "")
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='a' {aAttrs}>{aBody}</Tab><Tab id='b'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            return (s.Get<Tab>("bar/a"), s.Get<Tab>("bar/b"));
        }

        private static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString(h, out var c);
            return c;
        }

        [Test]
        public void SelectedColor_IsAbsolute_NotMultiplied()
        {
            var (a, b) = TwoTabs("color='#202020' selectedColor='#076DD7'");
            var bg = a.GameObject.GetComponent<UnityImage>();
            b.IsOn = true;            // a -> Normal (bg == #202020)
            a.IsOn = true;            // a -> Selected
            var expected = Hex("#076DD7");          // absolute, NOT #202020 × #076DD7
            Assert.That(bg.color.r, Is.EqualTo(expected.r).Within(0.001f));
            Assert.That(bg.color.g, Is.EqualTo(expected.g).Within(0.001f));
            Assert.That(bg.color.b, Is.EqualTo(expected.b).Within(0.001f));
        }

        [Test]
        public void SelectedColor_DoesNotFanOutToDescendants()
        {
            var (a, b) = TwoTabs("selectedColor='#ff0000'", "<Text id='lbl' color='#00ff00'>x</Text>");
            var label = a.Get<Text>("lbl").GameObject.GetComponent<TMP_Text>();
            var bg = a.GameObject.GetComponent<UnityImage>();
            b.IsOn = true;
            a.IsOn = true;            // a -> Selected
            Assert.That(bg.color.r, Is.EqualTo(1f).Within(0.001f), "bg becomes absolute red");
            Assert.That(label.color.g, Is.EqualTo(1f).Within(0.001f), "label keeps its own green (absolute does not fan out)");
            Assert.That(label.color.r, Is.EqualTo(0f).Within(0.001f), "label not painted red");
        }

        [Test]
        public void AbsoluteAndModulate_Compose()
        {
            var (a, b) = TwoTabs("color='#202020' selectedColor='#ffffff' selectedModulate='#808080'");
            var bg = a.GameObject.GetComponent<UnityImage>();
            b.IsOn = true;
            a.IsOn = true;            // a -> Selected: (#ffffff) × (#808080) ≈ 0.5 grey
            var half = 0.5019608f;    // 0x80 / 255
            Assert.That(bg.color.r, Is.EqualTo(half).Within(0.001f));
        }

        [Test]
        public void StateWithoutAnyAttr_FallsBackToColorBase()
        {
            // selectedColor present, but Pressed has neither attr → Pressed bg == color base.
            var (a, b) = TwoTabs("color='#202020' selectedColor='#076DD7'");
            var bg = a.GameObject.GetComponent<UnityImage>();
            var pt = a.GameObject.GetComponent<PuiToggle>();
            pt.SimulateState((int)InteractState.Pressed);
            var baseC = Hex("#202020");
            Assert.That(bg.color.r, Is.EqualTo(baseC.r).Within(0.001f));
            Assert.That(bg.color.g, Is.EqualTo(baseC.g).Within(0.001f));
        }
    }
}
```

- [ ] **Step 2: Run the new tests to verify they fail**

`refresh_unity` (expect compile errors are OK only if a referenced symbol is missing — here all symbols exist, so it compiles). Run `StateAbsoluteColorTests`.
Expected: `SelectedColor_IsAbsolute_NotMultiplied` FAILS (current multiply gives `#202020 × #076DD7`, far from `#076DD7`); `AbsoluteAndModulate_Compose` and `SelectedColor_DoesNotFanOutToDescendants` FAIL (`selectedColor` currently multiplies + fans out; `selectedModulate` is an unknown attr → ignored). `StateWithoutAnyAttr_FallsBackToColorBase` PASSES already (that behaviour is unchanged).

> If `PuiToggle.SimulateState((int)...)` doesn't exist, use the same call form already in `TabStateTests` (`pt.SimulateState(Pressed)` with an `int` const). Confirm the signature in `Runtime/Controls/Internal/PuiToggle.cs` before running.

- [ ] **Step 3: Rewrite `StateTintReactor` to compose absolute × modulate**

In `Runtime/Controls/Internal/StateTintReactor.cs`, replace the four multiplier fields:

```csharp
        private Color _hover = Color.white;
        private Color _pressed = Color.white;
        private Color _selected = Color.white;
        private Color _disabled = Color.white;
        private float _fade = DefaultFade;
```

with:

```csharp
        private StateColorSet _absolutes;   // per-state ABSOLUTE base override (targetGraphic only)
        private StateColorSet _modulates;   // per-state relative MULTIPLIER (null entry = white identity)
        private float _fade = DefaultFade;
```

Replace `Configure(...)`:

```csharp
        /// <summary>
        /// (Re)set the per-state absolute overrides + relative multipliers + fade. Safe to call
        /// repeatedly (Variant ReSolve): the base colour stays captured from the first init.
        /// </summary>
        public void Configure(StateColorSet absolutes, StateColorSet modulates, float fade)
        {
            EnsureInit();
            _absolutes = absolutes;
            _modulates = modulates;
            _fade = fade;
        }
```

Replace `MultiplierFor(...)` with both helpers, and update `OnState`:

```csharp
        private Color MultiplierFor(InteractState state) => _modulates.For(state) ?? Color.white;
        private Color BaseFor(InteractState state) => _absolutes.For(state) ?? _baseColor;

        private void OnState(InteractState state)
        {
            if (_graphic == null) return;
            var target = BaseFor(state) * MultiplierFor(state);

            if (_handle.IsActive()) _handle.TryCancel();

            if (TestForceInstant || _fade <= 0f)
            {
                _graphic.color = target;
                return;
            }

            _handle = LMotion.Create(_graphic.color, target, _fade)
                .Bind(_graphic, static (c, g) => g.color = c);
        }
```

(`EnsureInit`, `_baseColor`, `_baseCaptured`, `OnDestroy` stay as-is.)

- [ ] **Step 4: Rewrite `StateTintInstaller` for two families + targetGraphic rule**

In `Runtime/Controls/Internal/StateTintInstaller.cs`, replace `Install` and `InstallReactor` (keep `CollectBlocked` unchanged):

```csharp
        internal static void Install(
            GameObject root,
            Selectable selectable,
            IReadOnlyList<IControl> children,
            StateColorSet absolutes,
            StateColorSet modulates)
        {
            if (!absolutes.Any && !modulates.Any) return;

            selectable.transition = Selectable.Transition.None;

            var blocked = new HashSet<GameObject>();
            foreach (var child in children)
                CollectBlocked(child as Control, blocked);

            var fade = StateTintReactor.DefaultFade;
            var target = selectable.targetGraphic;
            foreach (var g in root.GetComponentsInChildren<Graphic>(includeInactive: true))
            {
                if (blocked.Contains(g.gameObject)) continue;
                // Absolutes apply ONLY to the control's base graphic (targetGraphic) — fanning them
                // out would paint label/icon the same colour as bg. Descendants get the multiplier only.
                var abs = ReferenceEquals(g, target) ? absolutes : default;
                InstallReactor(g, abs, modulates, fade);
            }
        }
```

```csharp
        private static void InstallReactor(Graphic graphic, StateColorSet absolutes, StateColorSet modulates, float fade)
        {
            if (graphic == null) return;
            var reactor = graphic.GetComponent<StateTintReactor>()
                          ?? graphic.gameObject.AddComponent<StateTintReactor>();
            reactor.Configure(absolutes, modulates, fade);
        }
```

Remove the now-unused `using PromptUGUI.Application;` only if the resolver lines were the sole user (they were — string→Color resolution moved into `StateColorSet.Resolve`). Verify no other reference to `UI.` remains in the file before deleting the using.

- [ ] **Step 5: Update `Btn.cs`**

Replace the three multiplier fields (the `_hoverColor` / `_pressedColor` / `_disabledColor` block, currently commented "Raw (unresolved) *Color attribute values…"):

```csharp
        // Absolute per-state bg colours (set targetGraphic). Resolved in OnAfterApply.
        private string _hoverColor;
        private string _pressedColor;
        private string _disabledColor;
        // Relative per-state multipliers (fan out to bg + descendants). Resolved in OnAfterApply.
        private string _hoverModulate;
        private string _pressedModulate;
        private string _disabledModulate;
```

Replace the three `[UIAttr]` setters (currently `HoverColor`/`PressedColor`/`DisabledColor` with "Tint multiplier…" doc comments) with six:

```csharp
        /// <summary>Absolute bg colour while Hover.</summary>
        [UIAttr(IsColor = true), Preserve] public string HoverColor { set => _hoverColor = value; }
        /// <summary>Absolute bg colour while Pressed.</summary>
        [UIAttr(IsColor = true), Preserve] public string PressedColor { set => _pressedColor = value; }
        /// <summary>Absolute bg colour while Disabled.</summary>
        [UIAttr(IsColor = true), Preserve] public string DisabledColor { set => _disabledColor = value; }
        /// <summary>Relative colour multiplier (fans out to subtree) while Hover.</summary>
        [UIAttr(IsColor = true), Preserve] public string HoverModulate { set => _hoverModulate = value; }
        /// <summary>Relative colour multiplier while Pressed.</summary>
        [UIAttr(IsColor = true), Preserve] public string PressedModulate { set => _pressedModulate = value; }
        /// <summary>Relative colour multiplier while Disabled.</summary>
        [UIAttr(IsColor = true), Preserve] public string DisabledModulate { set => _disabledModulate = value; }
```

In `OnAfterApply`, replace the `StateTintInstaller.Install(...)` call (keep the `base.OnAfterApply()`, `_btn.interactable = Interactable;` before it and the `if (_pressedSprite != null) …` after it):

```csharp
            var abs = StateColorSet.Resolve(_hoverColor, _pressedColor, null, _disabledColor);
            var mod = StateColorSet.Resolve(_hoverModulate, _pressedModulate, null, _disabledModulate);
            StateTintInstaller.Install(GameObject, _btn, Children, abs, mod);
```

- [ ] **Step 6: Update `Tab.cs`**

Replace the four-field block (`_hoverColor`…`_disabledColor`, commented "Raw (unresolved) *Color…"):

```csharp
        // Absolute per-state bg colours (set _bg). Resolved in OnAfterApply.
        private string _hoverColor;
        private string _pressedColor;
        private string _selectedColor;
        private string _disabledColor;
        // Relative per-state multipliers (fan out to _bg + descendants). Resolved in OnAfterApply.
        private string _hoverModulate;
        private string _pressedModulate;
        private string _selectedModulate;
        private string _disabledModulate;
```

Replace the four `[UIAttr]` setters (the `HoverColor`/`PressedColor`/`SelectedColor`/`DisabledColor` "Tint multiplier…" block) with eight:

```csharp
        /// <summary>Absolute bg colour while Hover.</summary>
        [UIAttr(IsColor = true), Preserve] public string HoverColor { set => _hoverColor = value; }
        /// <summary>Absolute bg colour while Pressed.</summary>
        [UIAttr(IsColor = true), Preserve] public string PressedColor { set => _pressedColor = value; }
        /// <summary>Absolute bg colour while this Tab is the active (isOn) one at rest.</summary>
        [UIAttr(IsColor = true), Preserve] public string SelectedColor { set => _selectedColor = value; }
        /// <summary>Absolute bg colour while Disabled.</summary>
        [UIAttr(IsColor = true), Preserve] public string DisabledColor { set => _disabledColor = value; }
        /// <summary>Relative colour multiplier (fans out to subtree) while Hover.</summary>
        [UIAttr(IsColor = true), Preserve] public string HoverModulate { set => _hoverModulate = value; }
        /// <summary>Relative colour multiplier while Pressed.</summary>
        [UIAttr(IsColor = true), Preserve] public string PressedModulate { set => _pressedModulate = value; }
        /// <summary>Relative colour multiplier while active (isOn) at rest.</summary>
        [UIAttr(IsColor = true), Preserve] public string SelectedModulate { set => _selectedModulate = value; }
        /// <summary>Relative colour multiplier while Disabled.</summary>
        [UIAttr(IsColor = true), Preserve] public string DisabledModulate { set => _disabledModulate = value; }
```

In `OnAfterApply`, replace the `StateTintInstaller.Install(...)` call:

```csharp
            var abs = StateColorSet.Resolve(_hoverColor, _pressedColor, _selectedColor, _disabledColor);
            var mod = StateColorSet.Resolve(_hoverModulate, _pressedModulate, _selectedModulate, _disabledModulate);
            StateTintInstaller.Install(GameObject, _toggle, Children, abs, mod);
```

- [ ] **Step 7: Update `Toggle.cs`**

Identical shape to Tab. Replace the four-field block with the eight-field block (same as Tab Step 6). Replace the four `[UIAttr]` setters with the eight (same as Tab, but the doc comment for `SelectedColor` reads "while checked (isOn) at rest"). In `OnAfterApply` replace the install call:

```csharp
            var abs = StateColorSet.Resolve(_hoverColor, _pressedColor, _selectedColor, _disabledColor);
            var mod = StateColorSet.Resolve(_hoverModulate, _pressedModulate, _selectedModulate, _disabledModulate);
            StateTintInstaller.Install(GameObject, _toggle, Children, abs, mod);
```

- [ ] **Step 8: Migrate existing multiply tests `*Color` → `*Modulate` (EditMode)**

These assert `base × multiplier`, which is now the `*Modulate` behaviour. Rename the XML literals (NOT the C# property names in assertions — they stay, those test the renamed behaviour):

- `Tests/EditMode/Controls/BtnStateTests.cs`: in every XML string, `pressedColor=` → `pressedModulate=` and `pressedColor.dark=` → `pressedModulate.dark=` (lines ~179, 194, 218, 250, 279, 290, 341). Line 335 test `PressedSprite_ComposesWithPressedColor` → rename method to `PressedSprite_ComposesWithPressedModulate` and its XML `pressedColor='#808080'` → `pressedModulate='#808080'`.
- `Tests/EditMode/Controls/TabStateTests.cs`: `BuildTab("pressedColor='#808080'", …)` → `pressedModulate='#808080'`; the `SelectedColor_AppliesToActiveTabAtRest` test (lines 51–72) `selectedColor='#808080'` → `selectedModulate='#808080'`, rename method to `SelectedModulate_AppliesToActiveTabAtRest`, update the message string.
- `Tests/EditMode/Controls/ToggleStateTests.cs`: `BuildToggle("pressedColor='#808080'")` → `pressedModulate=…` (line 32); `BuildToggle("selectedColor='#808080'")` → `selectedModulate=…` (line 44); rename the affected test methods to `…Modulate…` if their names say `Color`.

- [ ] **Step 9: Migrate PlayMode tests**

- `Tests/PlayMode/Controls/BtnStateVisualsPlayTests.cs`: `pressedColor='#808080'` → `pressedModulate='#808080'` (lines ~51, 107).
- `Tests/PlayMode/Controls/TabStateVisualsPlayTests.cs`: `pressedColor='#808080'` → `pressedModulate='#808080'` (line ~34).

- [ ] **Step 10: Refresh + verify compile clean**

`refresh_unity(compile="request", mode="force")` then `read_console(types=["error"])`. Expected: 0 errors.

- [ ] **Step 11: Run the new + migrated EditMode suites — all green**

Run, in turn (full namespaces):
`PromptUGUI.Tests.EditMode.Controls.StateAbsoluteColorTests`,
`…StateColorSetTests`,
`…BtnStateTests`, `…TabStateTests`, `…ToggleStateTests`, `…ImageTintTests`.
Expected: all PASS. (`StateAbsoluteColorTests` now green; the migrated multiply tests still green under their new attr name.)

- [ ] **Step 12: Commit**

```bash
git add Runtime/Controls/Internal/StateTintReactor.cs Runtime/Controls/Internal/StateTintInstaller.cs \
        Runtime/Controls/Btn.cs Runtime/Controls/Tab.cs Runtime/Controls/Toggle.cs \
        Tests/EditMode/Controls/StateAbsoluteColorTests.cs \
        Tests/EditMode/Controls/BtnStateTests.cs Tests/EditMode/Controls/TabStateTests.cs \
        Tests/EditMode/Controls/ToggleStateTests.cs \
        Tests/PlayMode/Controls/BtnStateVisualsPlayTests.cs Tests/PlayMode/Controls/TabStateVisualsPlayTests.cs
git commit -m "feat: *Color now absolute (bg) + new *Modulate relative multiplier on Btn/Tab/Toggle"
```

---

## Task 3: XSD coverage, docs, full verification

**Files:**
- Modify: `Tests/EditMode/Editor/XsdGeneratorTests.cs`
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`

- [ ] **Step 1: Write the failing XSD assertion**

In `Tests/EditMode/Editor/XsdGeneratorTests.cs`, the existing tests already assert `hoverColor`/`pressedColor`/`disabledColor` (Btn) and `selectedColor` (Tab/Toggle) — those attrs still exist (now absolute), so leave them. Add a new assertion in the Tab/Toggle test (`Xsd_Tab_and_Toggle_declare_selectedColor_attribute`, after the `selectedColor` assert at line ~799):

```csharp
            StringAssert.Contains("name=\"selectedModulate\"", xsd);
```

And in the Btn state-attr test (around lines 783–785) add:

```csharp
            StringAssert.Contains("name=\"pressedModulate\"", xsd);
```

- [ ] **Step 2: Run to verify it fails**

Run `PromptUGUI.Tests.Editor.XsdGeneratorTests` (assembly `PromptUGUI.Tests.EditorOnly`). Expected: the two new asserts FAIL only if the controls weren't rebuilt — but Task 2 added the `*Modulate` `[UIAttr]`s, so the reflection-driven XSD already emits them ⇒ these likely PASS immediately. That is acceptable here (guard test for an already-shipped attr); if it fails, the `[UIAttr]` was missed in Task 2 — go fix the control.

- [ ] **Step 3: Update the XML SKILL**

In `.claude/skills/authoring-promptugui-xml/SKILL.md`:

(a) **Btn state visuals** section, item "1. State colour multipliers" — rewrite the heading + body so:
- `*Color` (`hoverColor`/`pressedColor`/`selectedColor`/`disabledColor`) = **absolute** per-state colour applied to the control's base graphic (bg / `targetGraphic`) only; does NOT fan out.
- New `*Modulate` (`hoverModulate`/`pressedModulate`/`selectedModulate`/`disabledModulate`) = **relative multiplier** (uGUI ColorTint / Godot `modulate` semantics, Normal = white) fanned out to bg + every descendant Graphic.
- Compose: `displayed = (absolute ?? color) × (modulate ?? white)`.
- `stateReact="false"` opts a subtree out of the **`*Modulate` fan-out** (absolutes are bg-only, unaffected).
- For per-state absolute recolouring of multiple graphics, use `<Show>`.
- Update the inline examples (the `pressedColor="#cccccc"` example becomes either `pressedModulate="#cccccc"` for a dim, or `selectedColor="primary"` for an absolute pick).

(b) **`<Btn>` table row** (built-in primitives table): replace the `hoverColor / pressedColor / disabledColor (… colour multipliers …)` description with: `hoverColor / pressedColor / disabledColor` (absolute per-state bg colour) and `hoverModulate / pressedModulate / disabledModulate` (relative multiplier, fans out). Same wording pattern.

(c) **`<Tab>` and `<Toggle>` table rows**: same edit, including `selectedColor` (absolute) + `selectedModulate` (multiplier).

(d) **Quick reference cheatsheet**, `BTN STATE` and `TAB/TOGGLE` lines: update to name both families and their one-line semantics (`*Color` absolute bg; `*Modulate` relative multiplier fan-out; `state-selected` only on Tab/Toggle).

Audit `.claude/skills/scripting-promptugui-csharp/SKILL.md` for any "`*Color` multiplier" wording; update if present (the `[UIAttr]` setters `HoverColor`/`HoverModulate` are public). `OnState` / `InteractState` text is unchanged.

- [ ] **Step 4: Validate the SKILL examples + run the full suites**

Run the UIXmlLint CLI on any `.ui.xml` you changed in the skill examples (none expected). Then:
- `refresh_unity` → `read_console(types=["error"])` → 0 errors.
- Full `PromptUGUI.Tests.EditMode` → expect all PASS.
- Full `PromptUGUI.Tests.EditorOnly` (or just `XsdGeneratorTests` if the full run disconnects) → PASS.
- `PromptUGUI.Tests.PlayMode` (`BtnStateVisualsPlayTests`, `TabStateVisualsPlayTests`) → PASS.

- [ ] **Step 5: Run .NET lint**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: exit 0 (workspace-load warning about Local.props is benign).

- [ ] **Step 6: Commit**

```bash
git add Tests/EditMode/Editor/XsdGeneratorTests.cs .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "feat: XSD asserts *Modulate; SKILL documents absolute *Color vs relative *Modulate"
```

---

## Self-Review

**Spec coverage:**
- §4.1 two families → Task 2 Steps 5–7 (per control) + §4.4 surface table. ✓
- §4.2 composition `(abs ?? base) × (mod ?? white)` → Task 2 Step 3 (reactor `BaseFor`/`MultiplierFor`/`OnState`) + tests Step 1 (`AbsoluteAndModulate_Compose`, `StateWithoutAnyAttr_FallsBackToColorBase`). ✓
- §4.3 reactor/installer + `StateColorSet` → Task 1 + Task 2 Steps 3–4; targetGraphic-only absolute → Step 4 `ReferenceEquals(g, target)` + test `SelectedColor_DoesNotFanOutToDescendants`. ✓
- §4.4 Btn has no `selected*` → Btn passes `null` for selected in `Resolve` (Step 5); XSD asserts neither (existing test untouched). ✓
- §4.5 breaking rename + test migration → Task 2 Steps 8–9. ✓
- §5 testing list items 1–9 → StateAbsoluteColorTests (1,2,4,5) + migrated multiply tests (3) + no-attrs regression (existing `transition` regression test in Btn/Tab/ToggleStateTests, untouched, still green = item 6) + `stateReact` (existing BtnStateTests stateReact test, now under `pressedModulate`, item 7) + Variant (existing `PressedColor_VariantReSolve…`, migrated to `pressedModulate`, item 8) + XSD (Task 3 item 9). ✓
- §6 skill impact → Task 3 Step 3. ✓

**Placeholder scan:** none — every code/edit step shows concrete content or exact line targets.

**Type consistency:** `StateColorSet(hover,pressed,selected,disabled)`, `.For(InteractState)`, `.Any`, `.Resolve(string×4)` used identically in Task 1 (def), Task 2 reactor (`_absolutes`/`_modulates`, `BaseFor`/`MultiplierFor`), installer (`Install(…, StateColorSet, StateColorSet)`, `InstallReactor(…, StateColorSet, StateColorSet, float)`), and all three controls' `OnAfterApply` (`StateColorSet.Resolve(...)` → `Install(GameObject, selectable, Children, abs, mod)`). `Configure(StateColorSet, StateColorSet, float)` matches between reactor def and installer call. ✓

**Note for executor:** Tab.cs on this branch is the `main` version (no `tint` attr — that lives in the separate open PR #44 `feat/tab-tint`). If #44 merges before this lands, rebase; the changes touch different attributes and should not conflict beyond adjacent lines.
