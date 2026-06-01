# Btn `pressedSprite` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `pressedSprite` attribute to `<Btn>` that swaps the bg image while the button is held (revert on release) and auto-disables uGUI's default ColorTint darkening.

**Architecture:** `pressedSprite` resolves to a `Sprite` in its `[UIAttr]` setter and is applied through the existing `OnState` broadcast — a single subscription sets `_bg.overrideSprite` to the pressed sprite while `InteractState.Pressed`, `null` otherwise. Presence of a pressed sprite flips `Selectable.transition` to `None` (in `OnAfterApply`), matching the existing "any state visual → built-in ColorTint off" rule. No uGUI native `SpriteSwap` is used.

**Tech Stack:** Unity 6 uGUI, R3 (Cysharp) `Observable`/`Subscribe`, PromptUGUI `[UIAttr]` reflection, NUnit EditMode tests via Unity MCP.

**Spec:** `docs~/superpowers/specs/2026-06-01-btn-pressed-sprite-design.md`

---

## File Structure

- **Modify** `Runtime/Controls/Btn.cs` — add `_pressedSprite` field + `_pressedSpriteSub` subscription, the `PressedSprite` `[UIAttr]` setter, the `ApplyPressedSpriteForState` helper, the `OnAfterApply` transition flip, and dispose the subscription. (All feature logic lives here; the broadcast plumbing in `PuiButton`/`StateBroadcaster` is reused unchanged.)
- **Modify** `Editor/XsdGenerator.cs:92-101` — add `pressedSprite` to the hardcoded `<Btn>` attribute list (Btn's XSD is hand-written, not reflection-driven).
- **Modify (tests)** `Tests/EditMode/Controls/BtnStateTests.cs` — add the `pressedSprite` behaviour tests (reuses the file's `BuildBtn` / `BuildBtnXml` helpers, `SimulateState` ordinals, `UseInstantTint`, `AssertColorsEqual`).
- **Modify (tests)** `Tests/EditMode/Editor/XsdGeneratorTests.cs:776` — extend `Xsd_Btn_declares_state_color_attributes` with a `pressedSprite` substring assertion.
- **Modify (docs)** `.claude/skills/authoring-promptugui-xml/SKILL.md` — `<Btn>` attribute row + "Btn state visuals" note.

Per CLAUDE.md, all C# work is verified via Unity MCP (refresh → read_console → run_tests), and `dotnet format` lint runs after edits. Work happens on the existing `feat/btn-pressed-sprite` branch.

---

### Task 1: `pressedSprite` swaps bg `overrideSprite` on Pressed, reverts on release

**Files:**
- Modify: `Runtime/Controls/Btn.cs`
- Test: `Tests/EditMode/Controls/BtnStateTests.cs`

- [ ] **Step 1: Write the failing test**

Append this test inside the `BtnStateTests` class in `Tests/EditMode/Controls/BtnStateTests.cs` (just before the closing `AssertColorsEqual` helper):

```csharp
[Test]
public void PressedSprite_SwapsBgOverrideOnPressed_RevertsOnNormal()
{
    var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
    UI.SpriteResolver = _ => stub;

    var btn = BuildBtn("pressedSprite='ui:pressed'");
    var bg = btn.GameObject.GetComponent<UnityImage>();
    var authored = bg.sprite; // built-in 9-slice default, must stay untouched
    var puiBtn = btn.GameObject.GetComponent<PuiButton>();

    Assert.IsNull(bg.overrideSprite, "no swap before press");

    puiBtn.SimulateState(Pressed);
    Assert.AreEqual(stub, bg.overrideSprite, "Pressed shows pressedSprite via overrideSprite");
    Assert.AreEqual(authored, bg.sprite, "authored sprite is untouched during press");

    puiBtn.SimulateState(Normal);
    Assert.IsNull(bg.overrideSprite, "release reverts overrideSprite to null");
    Assert.AreEqual(authored, bg.sprite, "authored sprite still untouched after release");
}
```

- [ ] **Step 2: Refresh + run the test to verify it FAILS (red)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="PressedSprite_SwapsBgOverrideOnPressed_RevertsOnNormal")
```
Expected: FAIL — `PressedSprite` is not a known attribute, so `bg.overrideSprite` stays `null` on Pressed (assertion `Assert.AreEqual(stub, bg.overrideSprite)` fails). (Unknown `[UIAttr]` names are ignored at apply time, not a compile error.)

- [ ] **Step 3: Implement the setter + subscription in `Btn.cs`**

In `Runtime/Controls/Btn.cs`:

(a) Add `using System;` to the top of the file (for `IDisposable`), after the existing `using` lines:

```csharp
using System;
```

(b) Add two fields next to the other private fields (near `_pointerRelay`):

```csharp
private Sprite _pressedSprite;
private IDisposable _pressedSpriteSub;
```

(c) In `OnAttached`, after the `_btn.onClick.AddListener(...)` line, subscribe the swap to the state broadcast:

```csharp
_pressedSpriteSub = _btn.OnState.Subscribe(ApplyPressedSpriteForState);
```

(d) Add the helper method and the `PressedSprite` setter. Put them next to the existing `Sprite` setter:

```csharp
[UIAttr(IsSprite = true), Preserve]
public string PressedSprite
{
    set
    {
        // "" / "none" => no pressed swap (mirrors Tab.selectedSprite). Otherwise resolve
        // through the same path as `sprite`; a Variant ReSolve re-invokes this setter.
        _pressedSprite = string.IsNullOrEmpty(value) || value == "none"
            ? null
            : UI.ResolveSprite(value);
        // Re-evaluate for the live state so a Variant-driven change takes effect immediately.
        ApplyPressedSpriteForState(_btn.Current);
    }
}

// Swaps the bg's overrideSprite (never its authored `sprite`) so revert is overrideSprite=null.
private void ApplyPressedSpriteForState(InteractState state)
    => _bg.overrideSprite = state == InteractState.Pressed ? _pressedSprite : null;
```

(e) In `Dispose`, dispose the subscription (before `_click.Dispose()`):

```csharp
_pressedSpriteSub?.Dispose();
```

- [ ] **Step 4: Refresh + run the test to verify it PASSES (green)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="PressedSprite_SwapsBgOverrideOnPressed_RevertsOnNormal")
```
Expected: PASS.

- [ ] **Step 5: Lint + commit**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI && git add Runtime/Controls/Btn.cs Tests/EditMode/Controls/BtnStateTests.cs
git commit -m "feat: Btn pressedSprite swaps bg overrideSprite on press"
```

---

### Task 2: `pressedSprite` auto-disables the default ColorTint

**Files:**
- Modify: `Runtime/Controls/Btn.cs:68-73` (`OnAfterApply`)
- Test: `Tests/EditMode/Controls/BtnStateTests.cs`

(The "no pressedSprite → transition stays ColorTint" case is already covered by the existing `PlainBtn_BackCompat_TargetGraphicIsBgAndTransitionIsColorTint` and `NoStateColor_KeepsColorTintAndHasNoReactors` tests, so only the positive case is added here.)

- [ ] **Step 1: Write the failing test**

Append to `BtnStateTests`:

```csharp
[Test]
public void PressedSprite_DisablesDefaultColorTint()
{
    var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
    UI.SpriteResolver = _ => stub;

    var btn = BuildBtn("pressedSprite='ui:pressed'");
    var puiBtn = btn.GameObject.GetComponent<PuiButton>();
    Assert.AreEqual(Selectable.Transition.None, puiBtn.transition,
        "a pressedSprite must switch the Btn off uGUI's built-in ColorTint");
}
```

- [ ] **Step 2: Refresh + run to verify it FAILS (red)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="PressedSprite_DisablesDefaultColorTint")
```
Expected: FAIL — transition is still `ColorTint` (Task 1 only handled the swap, not the transition flip).

- [ ] **Step 3: Implement the transition flip in `OnAfterApply`**

In `Runtime/Controls/Btn.cs`, `OnAfterApply`, add the flip after the `StateTintInstaller.Install(...)` call:

```csharp
internal override void OnAfterApply()
{
    base.OnAfterApply();
    _btn.interactable = Interactable;
    StateTintInstaller.Install(GameObject, _btn, Children, _hoverColor, _pressedColor, null, _disabledColor);
    // A pressedSprite is itself a state visual: drop uGUI's built-in ColorTint so the
    // swapped pressed image isn't double-darkened. Set-only, matching the *Color path.
    if (_pressedSprite != null)
        _btn.transition = UnityEngine.UI.Selectable.Transition.None;
}
```

- [ ] **Step 4: Refresh + run to verify it PASSES (green)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="PressedSprite")
```
Expected: PASS (both Task 1 and Task 2 `PressedSprite*` tests).

- [ ] **Step 5: Lint + commit**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI && git add Runtime/Controls/Btn.cs Tests/EditMode/Controls/BtnStateTests.cs
git commit -m "feat: pressedSprite auto-disables default ColorTint"
```

---

### Task 3: Composition (`pressedColor`), Variant override, and `none` edge cases

**Files:**
- Test: `Tests/EditMode/Controls/BtnStateTests.cs`

These pin the composition and edge behaviour. The implementation from Tasks 1–2 should already satisfy them — if any fails, fix the relevant setter/flip logic rather than the test.

- [ ] **Step 1: Write the three failing/guard tests**

Append to `BtnStateTests`:

```csharp
[Test]
public void PressedSprite_ComposesWithPressedColor()
{
    UseInstantTint();
    var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
    UI.SpriteResolver = _ => stub;

    var btn = BuildBtnXml("pressedSprite='ui:pressed' pressedColor='#808080'", "<Image id='img'/>");
    var bg = btn.GameObject.GetComponent<UnityImage>();
    var bgBase = bg.color;
    var half = new Color(0.5019608f, 0.5019608f, 0.5019608f, 1f); // #808080
    var puiBtn = btn.GameObject.GetComponent<PuiButton>();

    puiBtn.SimulateState(Pressed);
    Assert.AreEqual(stub, bg.overrideSprite, "sprite swaps on press");
    AssertColorsEqual(bgBase * half, bg.color);  // and the tint reactor still multiplies
}

[Test]
public void PressedSprite_VariantOverride_ReResolves()
{
    var a = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
    var b = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
    UI.SpriteResolver = key => key == "ui:b" ? b : a;

    var btn = BuildBtn("pressedSprite='ui:a' pressedSprite.dark='ui:b'");
    var bg = btn.GameObject.GetComponent<UnityImage>();
    var puiBtn = btn.GameObject.GetComponent<PuiButton>();

    puiBtn.SimulateState(Pressed);
    Assert.AreEqual(a, bg.overrideSprite, "light variant uses 'ui:a'");

    puiBtn.SimulateState(Normal);
    UI.Variants.Set("dark", true); // ReSolve re-invokes the setter with the 'dark' override

    puiBtn.SimulateState(Pressed);
    Assert.AreEqual(b, bg.overrideSprite, "dark variant uses 'ui:b' after ReSolve");
}

[Test]
public void PressedSprite_None_NoSwapAndKeepsColorTint()
{
    var btn = BuildBtn("pressedSprite='none'");
    var bg = btn.GameObject.GetComponent<UnityImage>();
    var puiBtn = btn.GameObject.GetComponent<PuiButton>();

    Assert.AreEqual(Selectable.Transition.ColorTint, puiBtn.transition,
        "pressedSprite='none' must not disable the default ColorTint");

    puiBtn.SimulateState(Pressed);
    Assert.IsNull(bg.overrideSprite, "pressedSprite='none' => no swap on press");
}
```

- [ ] **Step 2: Refresh + run the three tests**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="BtnStateTests")
```
Expected: PASS for all three (and the whole `BtnStateTests` class stays green). If `PressedSprite_VariantOverride_ReResolves` fails because the setter didn't re-evaluate, confirm Step 3(d) of Task 1 calls `ApplyPressedSpriteForState(_btn.Current)`.

- [ ] **Step 3: Commit**

```bash
cd /workspace-PromptUGUI && git add Tests/EditMode/Controls/BtnStateTests.cs
git commit -m "test: pressedSprite composition / variant / none coverage"
```

---

### Task 4: Declare `pressedSprite` in the generated XSD

**Files:**
- Modify: `Editor/XsdGenerator.cs:92-101`
- Test: `Tests/EditMode/Editor/XsdGeneratorTests.cs:776`

- [ ] **Step 1: Extend the failing XSD test**

In `Tests/EditMode/Editor/XsdGeneratorTests.cs`, add a `pressedSprite` assertion to `Xsd_Btn_declares_state_color_attributes`:

```csharp
[Test]
public void Xsd_Btn_declares_state_color_attributes()
{
    // Btn is hardcoded in the generator (not reflection-driven), so the new
    // Btn-specific state-tint [UIAttr]s (hoverColor/pressedColor/disabledColor)
    // must be added to the hardcoded Btn attr list or IDEs flag valid authoring.
    var r = new ControlRegistry();
    var xsd = XsdGenerator.Generate(r);
    StringAssert.Contains("name=\"hoverColor\"", xsd);
    StringAssert.Contains("name=\"pressedColor\"", xsd);
    StringAssert.Contains("name=\"disabledColor\"", xsd);
    StringAssert.Contains("name=\"pressedSprite\"", xsd);
}
```

- [ ] **Step 2: Refresh + run to verify it FAILS (red)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="Xsd_Btn_declares_state_color_attributes")
```
Expected: FAIL — `name="pressedSprite"` not present in the generated XSD.

- [ ] **Step 3: Add `pressedSprite` to the hardcoded Btn attribute list**

In `Editor/XsdGenerator.cs`, the `WriteControl(writer, "Btn", new[] { ... })` block (around line 92), add the `pressedSprite` entry after `sprite`:

```csharp
WriteControl(writer, "Btn", new[]
{
    ("color", "xs:string", (string)null),
    ("sprite", "xs:string", (string)null),
    ("pressedSprite", "xs:string", (string)null),
    // State-driven tint [UIAttr]s (Btn.HoverColor/PressedColor/DisabledColor).
    // Btn is hardcoded here (not reflected), so list them explicitly.
    ("hoverColor", "xs:string", (string)null),
    ("pressedColor", "xs:string", (string)null),
    ("disabledColor", "xs:string", (string)null),
}, mixedContent: true);
```

- [ ] **Step 4: Refresh + run to verify it PASSES (green)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="XsdGeneratorTests")
```
Expected: PASS (the whole `XsdGeneratorTests` class stays green).

- [ ] **Step 5: Lint + commit**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI && git add Editor/XsdGenerator.cs Tests/EditMode/Editor/XsdGeneratorTests.cs
git commit -m "feat: declare Btn pressedSprite in generated XSD"
```

---

### Task 5: Update the XML authoring skill

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md` (Btn attribute row ~line 93; "Btn state visuals" section ~line 1087)

- [ ] **Step 1: Add `pressedSprite` to the `<Btn>` attribute list**

In the `<Btn>` row of the built-in controls table (the cell that currently lists `color`, `sprite`, `hoverColor` / `pressedColor` / `disabledColor`, …), add after `sprite`:

```
`pressedSprite` (sprite key, same forms as `sprite`; while the Btn is held it swaps the bg via `overrideSprite` and reverts on release — the authored `sprite` is untouched; setting it auto-switches the Btn off uGUI's built-in ColorTint so the pressed image isn't double-darkened; `""` / `none` = no swap; composes with `pressedColor`)
```

- [ ] **Step 2: Add a note in the "Btn state visuals" section**

In `.claude/skills/authoring-promptugui-xml/SKILL.md`, under **### 2. Artwork swap — `<Show on="state-...">`** (after the `<Show>` example block, ~line 1099), add:

````markdown
**Single-bg shorthand — `pressedSprite`.** When the only per-state change is the button's own bg image, `<Btn pressedSprite="ui:play-pressed">` is the one-attribute form of a `state-normal`/`state-pressed` `<Show>` pair: it swaps the bg's `overrideSprite` while Pressed and reverts on release (the authored `sprite` is never touched). Setting it auto-switches the Btn off uGUI's built-in ColorTint (so the pressed art isn't additionally darkened), and it composes with `pressedColor` (swap + tint stack). `""` / `none` = no swap. For swapping whole child subtrees (icon + label together, more than two states), use `<Show>`.

```xml
<Btn sprite="ui:play-normal" pressedSprite="ui:play-pressed">Play</Btn>
```
````

- [ ] **Step 3: Verify no broken markup**

Re-read the two edited regions to confirm table-cell pipes and the fenced code block are intact (no MCP/test run needed — docs only).

- [ ] **Step 4: Commit**

```bash
cd /workspace-PromptUGUI && git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "doc(skill): document Btn pressedSprite"
```

---

### Task 6: Full-suite regression + branch wrap-up

- [ ] **Step 1: Run the full affected suites**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
```
Expected: all green. Investigate any regression before proceeding (per `project_unity_mcp_test_gotchas`: a flaky "failed to initialize" usually means an unsaved scene — save and retry).

- [ ] **Step 2: Final lint gate**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: no changes reported.

- [ ] **Step 3: Hand off**

Stop here and report status. Do **not** merge to `main` (CLAUDE.md forbids committing to `main`); integration is a separate, user-driven step (PR or local merge) via the finishing-a-development-branch skill.

---

## Self-Review

**Spec coverage:**
- §2 swap on Pressed / revert on release → Task 1. ✓
- §2 + §4.4 auto-disable default ColorTint → Task 2. ✓
- §4.5 composes with `pressedColor` → Task 3 (`PressedSprite_ComposesWithPressedColor`). ✓
- §4.5 composes with `<Show>` → independent mechanisms; no code path to test (asserted by design, covered by existing `<Show>` tests). ✓
- §4.2 Variant re-resolve → Task 3 (`PressedSprite_VariantOverride_ReResolves`). ✓
- §4.2 `""` / `none` = no swap → Task 3 (`PressedSprite_None_NoSwapAndKeepsColorTint`). ✓
- §5 XSD gains `pressedSprite` → Task 4. ✓
- §7 XML skill update → Task 5; C# skill untouched (no public API) → matches spec. ✓
- §6 tests use `SimulateState` ordinals + `TestForceInstant` → all tasks follow the existing `BtnStateTests` conventions. ✓

**Placeholder scan:** No TBD/TODO; every code and test step shows full content. ✓

**Type consistency:** `_pressedSprite` (`Sprite`), `_pressedSpriteSub` (`IDisposable`), `PressedSprite` setter, and `ApplyPressedSpriteForState(InteractState)` are named identically across Tasks 1–3. `_btn.Current` (exists on `PuiButton` as `Current => _broadcaster.Current`) and `_btn.OnState` are the real members used by the existing code. The XSD entry name `pressedSprite` matches the `[UIAttr]`-derived attribute name (the property `PressedSprite` lower-camels to `pressedSprite`, same convention as `PressedColor` → `pressedColor`). ✓
