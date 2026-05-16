# Toggle Content Sizing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `<Toggle>` auto-size to its label content (mirroring the Btn pattern) so `<HStack><Toggle>静音</Toggle></HStack>` and `<Frame><Toggle>静音</Toggle></Frame>` render at sensible sizes without forcing the author to write `size=`.

**Architecture:** Override `Toggle.GetNativeSize()` to compute `(label.preferredWidth + 28, max(44, label.preferredHeight + 12))` for text-bearing Toggles and `(44, 44)` for checkbox-only. The horizontal constant `28` = `23` (Background 20px + 3px gap, the Label.offsetMin.x) + `5` (Label.offsetMax.x = -5, the right padding) — both inherited directly from `Toggle.OnAttached` layout values. `Control.ApplyLayoutElement` (LayoutGroup path) and `Control.ApplyCommon` (free-positioning fallback) infrastructure is already in place from the Btn precedent (BCS-D6 / BCS-D7) and requires no changes.

**Tech Stack:** Unity 6, TMPro (`_label.preferredWidth` after `ForceMeshUpdate`), Unity uGUI `LayoutElement`, NUnit EditMode tests, UnityMCP for compile + test runs.

**Spec:** `docs~/superpowers/specs/2026-05-16-toggle-content-sizing-design.md`

---

## File Structure

**Modify:**
- `Runtime/Controls/Toggle.cs` — add 5 private const fields + `GetNativeSize()` override at end of class
- `.claude/skills/authoring-promptugui-xml/SKILL.md` — three edits (Toggle table row + LayoutGroup paragraph + free-positioning paragraph)

**Create:**
- `Tests/EditMode/Controls/ToggleContentSizingTests.cs` — 7 EditMode tests mirroring `BtnContentSizingTests`

**No changes to:**
- `Runtime/Controls/Control.cs` (already routes `GetNativeSize()` via BCS-D6/D7)
- `Runtime/Core/Layout/SizeSpec.cs` (already has `FromNumeric` from BCS-D7)
- `Tests/EditMode/Controls/ControlApplyCommonLayoutGroupTests.cs` (Btn already established the "native!=null → LE auto-attached" branch; Toggle joins same branch, no new contract)

---

### Task 1: Write failing Toggle content sizing tests (RED)

**Files:**
- Create: `Tests/EditMode/Controls/ToggleContentSizingTests.cs`

- [ ] **Step 1: Create the test file with all 7 tests**

Write `Tests/EditMode/Controls/ToggleContentSizingTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ToggleContentSizingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Toggle_without_text_GetNativeSize_returns_icon_only_defaults()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='t'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var toggle = screen.Get<Toggle>("t");
            var native = toggle.GetNativeSize();
            Assert.IsTrue(native.HasValue, "Toggle must report a native size (checkbox-only fallback)");
            Assert.AreEqual(44f, native.Value.x);
            Assert.AreEqual(44f, native.Value.y);
        }

        [Test]
        public void Toggle_with_text_GetNativeSize_reports_label_preferred_plus_padding()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='t'>静音</Toggle>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var toggle = screen.Get<Toggle>("t");
            var label = toggle.GameObject.transform.Find("Label").GetComponent<TMP_Text>();
            label.ForceMeshUpdate();
            var textW = label.preferredWidth;

            var native = toggle.GetNativeSize();
            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(textW + 28f, native.Value.x, 0.5f,
                "preferredWidth = label.preferredWidth + 23 (left checkmark zone) + 5 (right padding)");
            Assert.AreEqual(44f, native.Value.y, 0.5f,
                "preferredHeight = max(44, label.preferredHeight + 6*2); fontSize 14 → max picks 44");
        }

        [Test]
        public void Toggle_in_Frame_no_size_sizeDelta_matches_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Toggle id='t'>静音</Toggle>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var toggle = screen.Get<Toggle>("t");
            var native = toggle.GetNativeSize().Value;
            Assert.AreEqual(native.x, toggle.RectTransform.sizeDelta.x, 0.5f,
                "BCS-D7 / TCS-D1: free-positioning + no size + has native → sizeDelta = native");
            Assert.AreEqual(native.y, toggle.RectTransform.sizeDelta.y, 0.5f);
            Assert.AreEqual(44f, toggle.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Toggle_in_Frame_anchor_stretch_skips_native_fallback()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Toggle id='t' anchor='stretch' margin='8'>静音</Toggle>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var toggle = screen.Get<Toggle>("t");
            Assert.AreEqual(-16f, toggle.RectTransform.sizeDelta.x, 0.5f,
                "anchor=stretch + margin=8: sizeDelta.x = -(l+r) = -16, native fallback skipped");
            Assert.AreEqual(-16f, toggle.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Toggle_in_VStack_no_size_gets_LayoutElement_with_native_preferred()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='200'>
    <Toggle id='t'>静音</Toggle>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var toggle = screen.Get<Toggle>("t");
            var le = toggle.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le,
                "BCS-D6 / TCS-D1: Toggle under LayoutGroup with no size should auto-attach LE reporting GetNativeSize");
            var native = toggle.GetNativeSize().Value;
            Assert.AreEqual(native.x, le.preferredWidth, 0.5f);
            Assert.AreEqual(native.y, le.preferredHeight, 0.5f);
            Assert.AreEqual(-1f, le.flexibleWidth);
            Assert.AreEqual(-1f, le.flexibleHeight);
        }

        [Test]
        public void Toggle_in_Frame_explicit_size_overrides_native()
        {
            // TCS-D6: explicit size wins; fallback only when no size attrs at all
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Toggle id='t' size='200x60'>静音</Toggle>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var toggle = screen.Get<Toggle>("t");
            Assert.AreEqual(new Vector2(200f, 60f), toggle.RectTransform.sizeDelta);
        }

        [Test]
        public void Toggle_in_VStack_variant_text_change_updates_preferred()
        {
            // TCS-D7: ApplyCommon re-runs on Variant switch → GetNativeSize re-evaluates → LE.preferred follows
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='200'>
    <Toggle id='t' text='短' text.long='长长长长长长'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            UI.Variants.Set("long", false);
            var screen = UI.Open("S");
            var toggle = screen.Get<Toggle>("t");
            var le = toggle.GameObject.GetComponent<LayoutElement>();
            var preferredShort = le.preferredWidth;
            Assert.Greater(preferredShort, 0f, "base text='短' preferred should be > 0");

            UI.Variants.Set("long", true);
            Assert.Greater(le.preferredWidth, preferredShort,
                "long variant has wider text → preferredWidth should grow");
        }
    }
}
```

Also create the `.meta` file by letting Unity generate it on next refresh (Unity does this automatically when scanning new `.cs` files — no manual step).

- [ ] **Step 2: Refresh Unity and check for compile errors**

Run:
```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: zero compile errors. (`GetNativeSize` is a virtual method on `Control` with default returning null, so `toggle.GetNativeSize()` compiles even before override exists.)

If errors appear, fix imports / typos before proceeding.

- [ ] **Step 3: Run tests and verify they FAIL with expected reason**

Run:
```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ToggleContentSizingTests")
```

Expected failures:
- `Toggle_without_text_GetNativeSize_returns_icon_only_defaults` — `native.HasValue` is `false` (base class returns null) → assertion fails.
- `Toggle_with_text_GetNativeSize_reports_label_preferred_plus_padding` — same null → fails.
- `Toggle_in_Frame_no_size_sizeDelta_matches_native` — sizeDelta = (0, 0) (no fallback applied since GetNativeSize null) → fails on `AreEqual(native.x, sizeDelta.x)` because `native` is null and `.Value` throws.
- `Toggle_in_VStack_no_size_gets_LayoutElement_with_native_preferred` — LE not auto-attached (BCS-D6 takes the `native == null` branch) → `IsNotNull(le)` fails.
- `Toggle_in_VStack_variant_text_change_updates_preferred` — LE missing → NRE on `le.preferredWidth`.

Expected passes (already-correct behavior):
- `Toggle_in_Frame_anchor_stretch_skips_native_fallback` — anchor=stretch goes through MarginResolver directly; works today.
- `Toggle_in_Frame_explicit_size_overrides_native` — explicit size goes through ApplyCommon's existing numeric branch.

If a "should fail" test passes, stop — the test is missing its actual assertion or hitting a different code path than expected.

- [ ] **Step 4: Commit RED**

```bash
git add Tests/EditMode/Controls/ToggleContentSizingTests.cs Tests/EditMode/Controls/ToggleContentSizingTests.cs.meta
git commit -m "$(cat <<'EOF'
test: red — Toggle content sizing native fallback

5 of 7 tests fail (no Toggle.GetNativeSize override yet); 2 pre-existing
behaviors (anchor=stretch, explicit size) already pass.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Implement Toggle.GetNativeSize (GREEN)

**Files:**
- Modify: `Runtime/Controls/Toggle.cs`

- [ ] **Step 1: Add private const fields**

In `Toggle.cs`, insert these constants after the existing private fields (after the `_changed` Subject declaration around line 20):

```csharp
        // Bound to OnAttached layout — changing these without changing OnAttached breaks the formula.
        // CheckmarkZoneWidth = Background sizeDelta.x (20) + 3px gap = Label offsetMin.x (23)
        // RightPadding       = -Label offsetMax.x (5)
        private const float CheckmarkZoneWidth = 23f;
        private const float RightPadding = 5f;
        private const float VerticalPadding = 6f;
        private const float MinTapHeight = 44f;
        private const float DefaultIconOnlySize = 44f;
```

- [ ] **Step 2: Add `GetNativeSize()` override**

Add this method at the end of the `Toggle` class, just before `public override void Dispose()`:

```csharp
        public override Vector2? GetNativeSize()
        {
            if (_label != null && !string.IsNullOrEmpty(_label.text))
            {
                _label.ForceMeshUpdate();
                var w = _label.preferredWidth + CheckmarkZoneWidth + RightPadding;
                var h = Mathf.Max(MinTapHeight, _label.preferredHeight + VerticalPadding * 2f);
                return new Vector2(w, h);
            }
            return new Vector2(DefaultIconOnlySize, DefaultIconOnlySize);
        }
```

- [ ] **Step 3: Refresh Unity and check for compile errors**

Run:
```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: zero errors.

- [ ] **Step 4: Run new tests and verify GREEN**

Run:
```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ToggleContentSizingTests")
```

Expected: all 7 tests pass.

If `Toggle_with_text_GetNativeSize_reports_label_preferred_plus_padding` fails with a width off by ~3 or ~5, double-check the constants: `CheckmarkZoneWidth + RightPadding == 28` must hold.

- [ ] **Step 5: Run existing ToggleTests for regression check**

Run:
```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ToggleTests")
```

Expected: all pre-existing Toggle tests still pass (Geometry / Parses_isOn / Setter_triggers / Same_group / Default_text_attr / Visual_LabelRaycastTargetTrue).

If `Geometry_BackgroundIsTwentyByTwentyLeftMiddle` or `Geometry_LabelStretchesRightOfBackground` fails, the layout numbers our constants depend on have shifted — fix OnAttached or the constants in lockstep.

- [ ] **Step 6: Run BtnContentSizingTests for cross-control regression check**

Run:
```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="BtnContentSizingTests")
```

Expected: all Btn content sizing tests still pass (we only touched Toggle.cs and a new test file; no Control.cs / SizeSpec.cs changes).

- [ ] **Step 7: Run full EditMode suite for broader regression check**

Run:
```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

Expected: all green.

- [ ] **Step 8: Commit GREEN**

```bash
git add Runtime/Controls/Toggle.cs
git commit -m "$(cat <<'EOF'
feat: Toggle auto-sizes to label content (native fallback)

Toggle.GetNativeSize() returns label.preferredWidth + 28 horizontal
(23 checkmark zone + 5 right padding, mirroring OnAttached layout)
and max(44, label.preferredHeight + 12) vertical. Checkbox-only
Toggle defaults to 44x44. Plumbed into LayoutGroup auto-LE (BCS-D6)
and free-positioning sizeDelta fallback (BCS-D7) — no Control.cs
changes required.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Lint check

**Files:** none modified by lint (verifies only)

- [ ] **Step 1: Restore lint workspace**

Run:
```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
```

Expected: restore completes without errors.

- [ ] **Step 2: Run safe lint passes**

Run from repo root:
```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
cd .lint && dotnet format style PromptUGUI.Lint.slnx
cd .lint && dotnet format analyzers PromptUGUI.Lint.slnx
```

Expected: no errors. Some files may be auto-formatted (whitespace).

**Do NOT** run `dotnet format analyzers --severity info` — see CLAUDE.md for the list of Roslyn fixers that break Unity reflection / `var` semantics in this repo.

- [ ] **Step 3: Verify no remaining warnings**

Run:
```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: exit 0.

If non-zero, inspect the changes and either commit whitespace fixes or revert if the formatter touched code it shouldn't have.

- [ ] **Step 4: Commit whitespace fixes if any**

```bash
git status   # check whether step 2 modified any files
# If yes:
git add -u
git commit -m "$(cat <<'EOF'
chore: lint (dotnet format whitespace)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
# If no, skip this step.
```

---

### Task 4: Update authoring-promptugui-xml SKILL.md

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md` (lines 95, 237, 239)

- [ ] **Step 1: Update `<Toggle>` table row (line 95)**

Use the Edit tool to replace this exact text:

**Old:**
```
| `<Toggle>`     | Image + uGUI Toggle + auto label. R3 `OnValueChanged: bool`. `<Toggle>静音</Toggle>` shorthand sets the label. Same `group=` name → mutual exclusion. **不要给单个 Toggle 写 `group=`** — uGUI ToggleGroup 默认要求至少一个 active，单成员组一旦点上就锁死。                            | `text`, `isOn` (bool, default false), `group` (string, mutual-exclusion key), `color`, `sprite` (Resources path for checkmark sprite), `font`                                                                                                                                                                                                      |
```

**New:**
```
| `<Toggle>`     | Image + uGUI Toggle + auto label. R3 `OnValueChanged: bool`. `<Toggle>静音</Toggle>` shorthand sets the label. Same `group=` name → mutual exclusion. **不要给单个 Toggle 写 `group=`** — uGUI ToggleGroup 默认要求至少一个 active，单成员组一旦点上就锁死。**不写 size 时按文字宽 + 23 左 checkmark 区 + 5 右 padding、上下 max(44, 文字高+12) 自适应**；无 text（checkbox-only）回退到 44×44。 | `text`, `isOn` (bool, default false), `group` (string, mutual-exclusion key), `color`, `sprite` (Resources path for checkmark sprite), `font`                                                                                                                                                                                                      |
```

- [ ] **Step 2: Update LayoutGroup paragraph (line 237)**

**Old:**
```
**Inside `<VStack>` / `<HStack>`**, a child's `size` / `width` / `height` is written to `LayoutElement.preferredX` with `flexibleX=0` (not to `sizeDelta`). So `<Btn size="64x64"/>` inside a VStack is **strictly 64×64** — the layout group will not stretch it. Specifying only one axis (e.g. `width="100"`) leaves the other axis unconstrained, taking the child's intrinsic preferred size. Omitting all size attributes: controls that report an intrinsic content size (`<Btn>`、`<Icon>`) auto-attach a `LayoutElement` with that size as preferred (e.g. `<Btn>OK</Btn>` widens to fit text + padding, default height 44); controls without intrinsic size (e.g. `<Image>` 没 sprite 时) get no `LayoutElement` and fall back to whatever their components advertise (often 0 for an empty Frame), so write at least one axis when you need a visible footprint.
```

**New:**
```
**Inside `<VStack>` / `<HStack>`**, a child's `size` / `width` / `height` is written to `LayoutElement.preferredX` with `flexibleX=0` (not to `sizeDelta`). So `<Btn size="64x64"/>` inside a VStack is **strictly 64×64** — the layout group will not stretch it. Specifying only one axis (e.g. `width="100"`) leaves the other axis unconstrained, taking the child's intrinsic preferred size. Omitting all size attributes: controls that report an intrinsic content size (`<Btn>`、`<Toggle>`、`<Icon>`) auto-attach a `LayoutElement` with that size as preferred (e.g. `<Btn>OK</Btn>` widens to fit text + padding, default height 44; `<Toggle>静音</Toggle>` widens to fit text + 28 padding, default height 44); controls without intrinsic size (e.g. `<Image>` 没 sprite 时) get no `LayoutElement` and fall back to whatever their components advertise (often 0 for an empty Frame), so write at least one axis when you need a visible footprint.
```

- [ ] **Step 3: Update free-positioning paragraph (line 239)**

**Old:**
```
**Inside `<Frame>` / `<Screen>` / `<SafeArea>` (free-positioning)**, a child's `size` / `width` / `height` is written to `RectTransform.sizeDelta`. Omitting all size attributes + `anchor` 不 stretch + 控件有 intrinsic content size（`<Btn>`、`<Icon>`）→ `sizeDelta` 默认为 native content size（避免 0×0 不可见）。其他控件保持 `sizeDelta=(0,0)`，得自己写 `size` 或 `anchor="stretch"` + `margin`。
```

**New:**
```
**Inside `<Frame>` / `<Screen>` / `<SafeArea>` (free-positioning)**, a child's `size` / `width` / `height` is written to `RectTransform.sizeDelta`. Omitting all size attributes + `anchor` 不 stretch + 控件有 intrinsic content size（`<Btn>`、`<Toggle>`、`<Icon>`）→ `sizeDelta` 默认为 native content size（避免 0×0 不可见）。其他控件保持 `sizeDelta=(0,0)`，得自己写 `size` 或 `anchor="stretch"` + `margin`。
```

- [ ] **Step 4: Commit SKILL.md update**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "$(cat <<'EOF'
docs: SKILL.md — Toggle default content sizing

Add Toggle to the "controls with intrinsic content size" lists in both
LayoutGroup and free-positioning sections; document the 23+5 horizontal
formula and 44x44 checkbox-only default in the <Toggle> table row.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Done Criteria

- [ ] `ToggleContentSizingTests` 7 tests all pass.
- [ ] Existing `ToggleTests` (9 tests) still pass.
- [ ] Existing `BtnContentSizingTests` (7 tests) still pass.
- [ ] Full `PromptUGUI.Tests.EditMode` suite green.
- [ ] `dotnet format --verify-no-changes --severity warn` exits 0.
- [ ] SKILL.md mentions `<Toggle>` in both the LayoutGroup auto-LE list and the free-positioning fallback list.
- [ ] Three commits on the branch: test (RED), feat (GREEN), docs (SKILL.md). Optionally a 4th chore commit if lint touched whitespace.
