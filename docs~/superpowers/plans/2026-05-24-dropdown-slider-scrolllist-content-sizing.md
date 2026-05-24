# Dropdown / Slider / ScrollList Content Sizing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `<Dropdown>`、`<Slider>`、`<ScrollList>` auto-size to sensible defaults (mirroring the Btn / Toggle pattern via BCS-D6 / BCS-D7) so `<VStack><Dropdown/></VStack>` and `<Frame><Slider/></Frame>` render at usable sizes without forcing the author to write `size=`.

**Architecture:** Override `GetNativeSize()` on each of the three controls. Dropdown returns fixed `(160, 44)` (DSS-D2: not content-driven). Slider returns direction-aware `(160, 44)` (horizontal) or `(44, 160)` (vertical). ScrollList returns direction-aware `(160, 200)` (vertical scroll) or `(200, 160)` (horizontal scroll). All three plug into the existing `Control.ApplyLayoutElement` (BCS-D6) + `Control.ApplyCommon` free-positioning fallback (BCS-D7) without changes to `Control` / `SizeSpec`.

**Tech Stack:** Unity 6, Unity uGUI (`Image`, `Slider`, `TMP_Dropdown`, `ScrollRect`, `LayoutElement`), NUnit EditMode tests, UnityMCP for compile + test runs.

**Spec:** `docs~/superpowers/specs/2026-05-24-dropdown-slider-scrolllist-content-sizing-design.md`

---

## File Structure

**Modify:**
- `Runtime/Controls/Dropdown.cs` — add 2 private const fields + `GetNativeSize()` override
- `Runtime/Controls/Slider.cs` — add 2 private const fields + `GetNativeSize()` override
- `Runtime/Controls/ScrollList.cs` — add 2 private const fields + `GetNativeSize()` override
- `.claude/skills/authoring-promptugui-xml/SKILL.md` — 5 edits (3 table rows + LayoutGroup paragraph + free-positioning paragraph)

**Create:**
- `Tests/EditMode/Controls/DropdownContentSizingTests.cs` — 5 EditMode tests
- `Tests/EditMode/Controls/SliderContentSizingTests.cs` — 6 EditMode tests
- `Tests/EditMode/Controls/ScrollListContentSizingTests.cs` — 5 EditMode tests

**No changes to:**
- `Runtime/Controls/Control.cs` (already routes `GetNativeSize()` via BCS-D6/D7)
- `Runtime/Core/Layout/SizeSpec.cs` (already has `FromNumeric` from BCS-D7)
- `Tests/EditMode/Controls/ControlApplyCommonLayoutGroupTests.cs` (Btn already established the "native!=null → LE auto-attached" branch; three controls join same branch, no new contract)
- Existing `DropdownTests` / `SliderTests` / `ScrollListTests` (they all use explicit size, no overlap with native fallback path)

---

### Task 1: Write failing Dropdown content sizing tests (RED)

**Files:**
- Create: `Tests/EditMode/Controls/DropdownContentSizingTests.cs`

- [ ] **Step 1: Create the test file**

Write `Tests/EditMode/Controls/DropdownContentSizingTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class DropdownContentSizingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Dropdown_GetNativeSize_returns_default_size()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Dropdown id='d'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var dropdown = screen.Get<Dropdown>("d");
            var native = dropdown.GetNativeSize();
            Assert.IsTrue(native.HasValue, "Dropdown must report a default native size");
            Assert.AreEqual(160f, native.Value.x);
            Assert.AreEqual(44f, native.Value.y);
        }

        [Test]
        public void Dropdown_in_Frame_no_size_sizeDelta_matches_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Dropdown id='d'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var dropdown = screen.Get<Dropdown>("d");
            Assert.AreEqual(160f, dropdown.RectTransform.sizeDelta.x, 0.5f,
                "BCS-D7 / DSS-D2: free-positioning + no size + has native → sizeDelta = native");
            Assert.AreEqual(44f, dropdown.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Dropdown_in_Frame_anchor_stretch_skips_native_fallback()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Dropdown id='d' anchor='stretch' margin='8'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var dropdown = screen.Get<Dropdown>("d");
            Assert.AreEqual(-16f, dropdown.RectTransform.sizeDelta.x, 0.5f,
                "anchor=stretch + margin=8: sizeDelta.x = -(l+r) = -16, native fallback skipped");
            Assert.AreEqual(-16f, dropdown.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Dropdown_in_VStack_no_size_gets_LayoutElement_with_native_preferred()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='200'>
    <Dropdown id='d'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var dropdown = screen.Get<Dropdown>("d");
            var le = dropdown.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le,
                "BCS-D6 / DSS-D2: Dropdown under LayoutGroup with no size should auto-attach LE reporting GetNativeSize");
            Assert.AreEqual(160f, le.preferredWidth, 0.5f);
            Assert.AreEqual(44f, le.preferredHeight, 0.5f);
            Assert.AreEqual(-1f, le.flexibleWidth);
            Assert.AreEqual(-1f, le.flexibleHeight);
        }

        [Test]
        public void Dropdown_in_Frame_explicit_size_overrides_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Dropdown id='d' size='240x36'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var dropdown = screen.Get<Dropdown>("d");
            Assert.AreEqual(new Vector2(240f, 36f), dropdown.RectTransform.sizeDelta);
        }
    }
}
```

- [ ] **Step 2: Refresh Unity and check for compile errors**

Run:
```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: zero compile errors. (`GetNativeSize` is a virtual method on `Control` returning null by default, so `dropdown.GetNativeSize()` compiles even before override exists.)

- [ ] **Step 3: Run tests and verify they FAIL with expected reason**

Run:
```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="DropdownContentSizingTests")
```

Expected failures:
- `Dropdown_GetNativeSize_returns_default_size` — `native.HasValue` is `false` (base returns null) → assertion fails.
- `Dropdown_in_Frame_no_size_sizeDelta_matches_native` — sizeDelta != (160, 44); sliced Image preferred is ~16 minimum → fails.
- `Dropdown_in_VStack_no_size_gets_LayoutElement_with_native_preferred` — LE not auto-attached (BCS-D6 takes `native == null` branch) → `IsNotNull(le)` fails.

Expected passes (already-correct behavior):
- `Dropdown_in_Frame_anchor_stretch_skips_native_fallback` — anchor=stretch routes through MarginResolver directly; works today.
- `Dropdown_in_Frame_explicit_size_overrides_native` — explicit size routes through existing numeric branch.

- [ ] **Step 4: Commit RED**

```bash
git add Tests/EditMode/Controls/DropdownContentSizingTests.cs Tests/EditMode/Controls/DropdownContentSizingTests.cs.meta
git commit -m "$(cat <<'EOF'
test: red — Dropdown content sizing native fallback

3 of 5 tests fail (no Dropdown.GetNativeSize override yet); 2
pre-existing behaviors (anchor=stretch, explicit size) already pass.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Implement Dropdown.GetNativeSize (GREEN)

**Files:**
- Modify: `Runtime/Controls/Dropdown.cs`

- [ ] **Step 1: Add private const fields**

In `Dropdown.cs`, insert these constants right after the `_selected` Subject field (around line 20, before `OnAttached`):

```csharp
        // DSS-D2: Dropdown 不读 caption 来算 native（caption 会随用户选项变化，UX 会跳）。
        // 固定默认覆盖"作者忘写 size"的可见性问题；显式 size 仍胜出。
        private const float MinTapHeight = 44f;
        private const float DefaultDropdownWidth = 160f;
```

- [ ] **Step 2: Add `GetNativeSize()` override**

Add this method just before `public override void Dispose()` (around line 236):

```csharp
        public override Vector2? GetNativeSize()
            => new Vector2(DefaultDropdownWidth, MinTapHeight);
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
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="DropdownContentSizingTests")
```

Expected: all 5 tests pass.

- [ ] **Step 5: Run existing DropdownTests for regression check**

Run:
```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="DropdownTests")
```

Expected: all pre-existing Dropdown tests still pass (they use explicit size; native fallback path doesn't intersect).

- [ ] **Step 6: Commit GREEN**

```bash
git add Runtime/Controls/Dropdown.cs
git commit -m "$(cat <<'EOF'
feat: Dropdown reports default native size (160x44)

Dropdown.GetNativeSize() returns a fixed (DefaultDropdownWidth=160,
MinTapHeight=44). Plumbed into LayoutGroup auto-LE (BCS-D6) and
free-positioning sizeDelta fallback (BCS-D7) — no Control.cs changes.
Caption is intentionally not measured (DSS-D2): content-driven width
would jitter with user selection.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Write failing Slider content sizing tests (RED)

**Files:**
- Create: `Tests/EditMode/Controls/SliderContentSizingTests.cs`

- [ ] **Step 1: Create the test file**

Write `Tests/EditMode/Controls/SliderContentSizingTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class SliderContentSizingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Slider_horizontal_GetNativeSize_returns_horizontal_defaults()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Slider id='s'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slider = screen.Get<Slider>("s");
            var native = slider.GetNativeSize();
            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(160f, native.Value.x, "horizontal Slider: long axis x = 160");
            Assert.AreEqual(44f, native.Value.y, "horizontal Slider: short axis y = 44");
        }

        [Test]
        public void Slider_vertical_GetNativeSize_returns_vertical_defaults()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Slider id='s' direction='vertical'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slider = screen.Get<Slider>("s");
            var native = slider.GetNativeSize();
            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(44f, native.Value.x, "vertical Slider: short axis x = 44");
            Assert.AreEqual(160f, native.Value.y, "vertical Slider: long axis y = 160");
        }

        [Test]
        public void Slider_in_Frame_no_size_sizeDelta_matches_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Slider id='s'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slider = screen.Get<Slider>("s");
            Assert.AreEqual(160f, slider.RectTransform.sizeDelta.x, 0.5f);
            Assert.AreEqual(44f, slider.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Slider_in_VStack_no_size_gets_LayoutElement_with_native_preferred()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='200'>
    <Slider id='s'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slider = screen.Get<Slider>("s");
            var le = slider.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le,
                "BCS-D6 / DSS-D3: Slider under LayoutGroup with no size should auto-attach LE reporting GetNativeSize");
            Assert.AreEqual(160f, le.preferredWidth, 0.5f);
            Assert.AreEqual(44f, le.preferredHeight, 0.5f);
        }

        [Test]
        public void Slider_in_Frame_explicit_size_overrides_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Slider id='s' size='200x40'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slider = screen.Get<Slider>("s");
            Assert.AreEqual(new Vector2(200f, 40f), slider.RectTransform.sizeDelta);
        }

        [Test]
        public void Slider_direction_change_via_variant_updates_native()
        {
            // DSS-D6: ApplyCommon re-runs on Variant switch → GetNativeSize re-reads _slider.direction
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='400'>
    <Slider id='s' direction='horizontal' direction.tall='vertical'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            UI.Variants.Set("tall", false);
            var screen = UI.Open("S");
            var slider = screen.Get<Slider>("s");
            var le = slider.GameObject.GetComponent<LayoutElement>();
            Assert.AreEqual(160f, le.preferredWidth, 0.5f, "base: horizontal → preferredWidth=160");
            Assert.AreEqual(44f, le.preferredHeight, 0.5f, "base: horizontal → preferredHeight=44");

            UI.Variants.Set("tall", true);
            Assert.AreEqual(44f, le.preferredWidth, 0.5f, "tall variant: vertical → preferredWidth=44");
            Assert.AreEqual(160f, le.preferredHeight, 0.5f, "tall variant: vertical → preferredHeight=160");
        }
    }
}
```

- [ ] **Step 2: Refresh Unity and check for compile errors**

Run:
```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: zero compile errors.

- [ ] **Step 3: Run tests and verify they FAIL**

Run:
```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SliderContentSizingTests")
```

Expected failures: all 4 of `Slider_horizontal_GetNativeSize_returns_horizontal_defaults`, `Slider_vertical_GetNativeSize_returns_vertical_defaults`, `Slider_in_Frame_no_size_sizeDelta_matches_native`, `Slider_in_VStack_no_size_gets_LayoutElement_with_native_preferred`, `Slider_direction_change_via_variant_updates_native` fail because base `GetNativeSize` returns null.

Expected pass: `Slider_in_Frame_explicit_size_overrides_native`.

- [ ] **Step 4: Commit RED**

```bash
git add Tests/EditMode/Controls/SliderContentSizingTests.cs Tests/EditMode/Controls/SliderContentSizingTests.cs.meta
git commit -m "$(cat <<'EOF'
test: red — Slider content sizing native fallback

5 of 6 tests fail (no Slider.GetNativeSize override yet); 1
pre-existing behavior (explicit size) already passes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Implement Slider.GetNativeSize (GREEN)

**Files:**
- Modify: `Runtime/Controls/Slider.cs`

- [ ] **Step 1: Add private const fields**

In `Slider.cs`, insert these constants right after `_changed` Subject field (around line 17, before `OnAttached`):

```csharp
        // DSS-D3: Slider 无内容驱动自然尺寸；长边 160 + 短边 44 (tap target) 是常用默认。
        private const float MinTapHeight = 44f;
        private const float DefaultSliderLength = 160f;
```

- [ ] **Step 2: Add `GetNativeSize()` override**

Add this method just before `public override void Dispose()` (around line 116):

```csharp
        public override Vector2? GetNativeSize()
        {
            var horizontal = _slider == null
                          || _slider.direction == UnitySlider.Direction.LeftToRight
                          || _slider.direction == UnitySlider.Direction.RightToLeft;
            return horizontal
                ? new Vector2(DefaultSliderLength, MinTapHeight)
                : new Vector2(MinTapHeight, DefaultSliderLength);
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
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SliderContentSizingTests")
```

Expected: all 6 tests pass.

- [ ] **Step 5: Run existing SliderTests for regression check**

Run:
```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SliderTests")
```

Expected: all pre-existing Slider tests still pass.

- [ ] **Step 6: Commit GREEN**

```bash
git add Runtime/Controls/Slider.cs
git commit -m "$(cat <<'EOF'
feat: Slider reports direction-aware native size (160x44 / 44x160)

Slider.GetNativeSize() returns (160, 44) for horizontal directions
(LeftToRight / RightToLeft) and (44, 160) for vertical directions.
Plumbed into LayoutGroup auto-LE (BCS-D6) and free-positioning
sizeDelta fallback (BCS-D7) — no Control.cs changes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Write failing ScrollList content sizing tests (RED)

**Files:**
- Create: `Tests/EditMode/Controls/ScrollListContentSizingTests.cs`

- [ ] **Step 1: Create the test file**

Write `Tests/EditMode/Controls/ScrollListContentSizingTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ScrollListContentSizingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void ScrollList_vertical_GetNativeSize_returns_vertical_defaults()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <ScrollList id='l'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("l");
            var native = list.GetNativeSize();
            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(160f, native.Value.x, "vertical ScrollList: cross axis x = 160");
            Assert.AreEqual(200f, native.Value.y, "vertical ScrollList: main axis y = 200");
        }

        [Test]
        public void ScrollList_horizontal_GetNativeSize_returns_horizontal_defaults()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <ScrollList id='l' direction='horizontal'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("l");
            var native = list.GetNativeSize();
            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(200f, native.Value.x, "horizontal ScrollList: main axis x = 200");
            Assert.AreEqual(160f, native.Value.y, "horizontal ScrollList: cross axis y = 160");
        }

        [Test]
        public void ScrollList_in_Frame_no_size_sizeDelta_matches_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x400'>
    <ScrollList id='l'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("l");
            Assert.AreEqual(160f, list.RectTransform.sizeDelta.x, 0.5f);
            Assert.AreEqual(200f, list.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void ScrollList_in_VStack_no_size_gets_LayoutElement_with_native_preferred()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='400'>
    <ScrollList id='l'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("l");
            var le = list.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le,
                "BCS-D6 / DSS-D4: ScrollList under LayoutGroup with no size should auto-attach LE reporting GetNativeSize");
            Assert.AreEqual(160f, le.preferredWidth, 0.5f);
            Assert.AreEqual(200f, le.preferredHeight, 0.5f);
        }

        [Test]
        public void ScrollList_in_Frame_explicit_size_overrides_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x400'>
    <ScrollList id='l' size='300x250'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("l");
            Assert.AreEqual(new Vector2(300f, 250f), list.RectTransform.sizeDelta);
        }
    }
}
```

- [ ] **Step 2: Refresh Unity and check for compile errors**

Run:
```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: zero compile errors.

- [ ] **Step 3: Run tests and verify they FAIL**

Run:
```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScrollListContentSizingTests")
```

Expected failures: `ScrollList_vertical_GetNativeSize_returns_vertical_defaults`, `ScrollList_horizontal_GetNativeSize_returns_horizontal_defaults`, `ScrollList_in_Frame_no_size_sizeDelta_matches_native`, `ScrollList_in_VStack_no_size_gets_LayoutElement_with_native_preferred` all fail (base GetNativeSize returns null).

Expected pass: `ScrollList_in_Frame_explicit_size_overrides_native`.

- [ ] **Step 4: Commit RED**

```bash
git add Tests/EditMode/Controls/ScrollListContentSizingTests.cs Tests/EditMode/Controls/ScrollListContentSizingTests.cs.meta
git commit -m "$(cat <<'EOF'
test: red — ScrollList content sizing native fallback

4 of 5 tests fail (no ScrollList.GetNativeSize override yet); 1
pre-existing behavior (explicit size) already passes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Implement ScrollList.GetNativeSize (GREEN)

**Files:**
- Modify: `Runtime/Controls/ScrollList.cs`

- [ ] **Step 1: Add private const fields**

In `ScrollList.cs`, insert these constants right after the `_slots` field (around line 28, before `SlotCount`):

```csharp
        // DSS-D4: ScrollList 视口默认值（避免 0x0 不可见）；实际项目几乎都会显式写 size。
        private const float DefaultMainAxisLength = 200f;
        private const float DefaultCrossAxisLength = 160f;
```

- [ ] **Step 2: Add `GetNativeSize()` override**

Add this method just before `public override void Dispose()` (around line 317):

```csharp
        public override Vector2? GetNativeSize()
            => _direction == "horizontal"
                ? new Vector2(DefaultMainAxisLength, DefaultCrossAxisLength)
                : new Vector2(DefaultCrossAxisLength, DefaultMainAxisLength);
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
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScrollListContentSizingTests")
```

Expected: all 5 tests pass.

- [ ] **Step 5: Run existing ScrollListTests for regression check**

Run:
```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScrollListTests")
```

Expected: all pre-existing ScrollList tests still pass.

- [ ] **Step 6: Run all three new content-sizing test classes plus Btn/Toggle for cross-control regression**

Run:
```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ContentSizing")
```

Expected: all 5 ContentSizing test classes (Btn / Toggle / Dropdown / Slider / ScrollList) pass.

- [ ] **Step 7: Run full EditMode suite for broader regression check**

Run:
```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

Expected: all green.

- [ ] **Step 8: Commit GREEN**

```bash
git add Runtime/Controls/ScrollList.cs
git commit -m "$(cat <<'EOF'
feat: ScrollList reports direction-aware viewport default (160x200 / 200x160)

ScrollList.GetNativeSize() returns (160, 200) for vertical scrolling
and (200, 160) for horizontal. Conceptually no content-driven natural
size (it's a window over scrolled content), so this is a viewport
default to avoid 0x0 invisibility. Plumbed into LayoutGroup auto-LE
(BCS-D6) and free-positioning sizeDelta fallback (BCS-D7).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Lint check

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

### Task 8: Update authoring-promptugui-xml SKILL.md

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`

- [ ] **Step 1: Update `<Slider>` table row (line 94)**

Use Edit tool to replace:

**Old:**
```
| `<Slider>`     | Image + uGUI Slider. R3 `OnValueChanged: float`.                                                                                                                                                                                                                                                                                                                                                 | `min` (float), `max` (float), `value` (float), `wholeNumbers` (bool), `direction` (`horizontal` / `vertical` / `reverse-horizontal` / `reverse-vertical`), `color`, `sprite`                                                                                                                                                                       |
```

**New:**
```
| `<Slider>`     | Image + uGUI Slider. R3 `OnValueChanged: float`. **不写 size 时按方向给默认**：横向 160×44、纵向 44×160（长边视觉宽度、短边 tap target）。                                                                                                                                                                                                                                                          | `min` (float), `max` (float), `value` (float), `wholeNumbers` (bool), `direction` (`horizontal` / `vertical` / `reverse-horizontal` / `reverse-vertical`), `color`, `sprite`                                                                                                                                                                       |
```

- [ ] **Step 2: Update `<Dropdown>` table row (line 95)**

**Old:**
```
| `<Dropdown>`   | TMP_Dropdown. R3 `OnSelected: int`. Options pushed C#-side via `BindOptions(...)`.                                                                                                                                                                                                                                                                                                               | `value` (int initial index), `color`, `sprite`, `font`                                                                                                                                                                                                                                                                                             |
```

**New:**
```
| `<Dropdown>`   | TMP_Dropdown. R3 `OnSelected: int`. Options pushed C#-side via `BindOptions(...)`. **不写 size 时默认 160×44**（不读 caption 文字宽，避免每选一项就改宽度）。                                                                                                                                                                                                                                          | `value` (int initial index), `color`, `sprite`, `font`                                                                                                                                                                                                                                                                                             |
```

- [ ] **Step 3: Update `<ScrollList>` table row (line 96)**

**Old:**
```
| `<ScrollList>` | ScrollRect + Mask. Items pushed C#-side via `BindItems(...)`. `itemTemplate` references a `<Template name=...>` or registered Control class.                                                                                                                                                                                                                                                     | `itemTemplate` (required tag name), `direction` (`vertical` / `horizontal`), `spacing` (float), `padding`, `color`, `sprite`                                                                                                                                                                                                                       |
```

**New:**
```
| `<ScrollList>` | ScrollRect + Mask. Items pushed C#-side via `BindItems(...)`. `itemTemplate` references a `<Template name=...>` or registered Control class. **不写 size 时按方向给视口默认**：纵向滚动 160×200、横向滚动 200×160；实际项目通常显式写 size。                                                                                                                                                          | `itemTemplate` (required tag name), `direction` (`vertical` / `horizontal`), `spacing` (float), `padding`, `color`, `sprite`                                                                                                                                                                                                                       |
```

- [ ] **Step 4: Update LayoutGroup paragraph (line 235)**

**Old:**
```
**Inside `<VStack>` / `<HStack>`**, a child's `size` / `width` / `height` is written to `LayoutElement.preferredX` with `flexibleX=0` (not to `sizeDelta`). So `<Btn size="64x64"/>` inside a VStack is **strictly 64×64** — the layout group will not stretch it. Specifying only one axis (e.g. `width="100"`) leaves the other axis unconstrained, taking the child's intrinsic preferred size. Omitting all size attributes: controls that report an intrinsic content size (`<Btn>`、`<Toggle>`、`<Icon>`) auto-attach a `LayoutElement` with that size as preferred (e.g. `<Btn>OK</Btn>` widens to fit text + padding, default height 44; `<Toggle>静音</Toggle>` widens to fit text + 28 padding, default height 44); controls without intrinsic size (e.g. `<Image>` 没 sprite 时) get no `LayoutElement` and fall back to whatever their components advertise (often 0 for an empty Frame), so write at least one axis when you need a visible footprint.
```

**New:**
```
**Inside `<VStack>` / `<HStack>`**, a child's `size` / `width` / `height` is written to `LayoutElement.preferredX` with `flexibleX=0` (not to `sizeDelta`). So `<Btn size="64x64"/>` inside a VStack is **strictly 64×64** — the layout group will not stretch it. Specifying only one axis (e.g. `width="100"`) leaves the other axis unconstrained, taking the child's intrinsic preferred size. Omitting all size attributes: controls that report an intrinsic content size (`<Btn>`、`<Toggle>`、`<Icon>`、`<Dropdown>`、`<Slider>`、`<ScrollList>`) auto-attach a `LayoutElement` with that size as preferred (e.g. `<Btn>OK</Btn>` widens to fit text + padding, default height 44; `<Toggle>静音</Toggle>` widens to fit text + 28 padding, default height 44; `<Dropdown/>` defaults 160×44; `<Slider/>` defaults 160×44 horizontal or 44×160 vertical; `<ScrollList/>` defaults 160×200 vertical or 200×160 horizontal); controls without intrinsic size (e.g. `<Image>` 没 sprite 时) get no `LayoutElement` and fall back to whatever their components advertise (often 0 for an empty Frame), so write at least one axis when you need a visible footprint.
```

- [ ] **Step 5: Update free-positioning paragraph (line 237)**

**Old:**
```
**Inside `<Frame>` / `<Screen>` / `<SafeArea>` (free-positioning)**, a child's `size` / `width` / `height` is written to `RectTransform.sizeDelta`. Omitting all size attributes + `anchor` 不 stretch + 控件有 intrinsic content size（`<Btn>`、`<Toggle>`、`<Icon>`）→ `sizeDelta` 默认为 native content size（避免 0×0 不可见）。其他控件保持 `sizeDelta=(0,0)`，得自己写 `size` 或 `anchor="stretch"` + `margin`。
```

**New:**
```
**Inside `<Frame>` / `<Screen>` / `<SafeArea>` (free-positioning)**, a child's `size` / `width` / `height` is written to `RectTransform.sizeDelta`. Omitting all size attributes + `anchor` 不 stretch + 控件有 intrinsic content size（`<Btn>`、`<Toggle>`、`<Icon>`、`<Dropdown>`、`<Slider>`、`<ScrollList>`）→ `sizeDelta` 默认为 native content size（避免 0×0 不可见）。其他控件保持 `sizeDelta=(0,0)`，得自己写 `size` 或 `anchor="stretch"` + `margin`。
```

- [ ] **Step 6: Commit SKILL.md update**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "$(cat <<'EOF'
docs: SKILL.md — Dropdown/Slider/ScrollList default content sizing

Add three controls to the "intrinsic content size" lists in both
LayoutGroup and free-positioning sections; document each control's
default size in its table row.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

---

### Task 9: Frame default anchor "fill-or-fit" (DSS-D13/D14/D15) red→green

**Files:**
- Create: `Tests/EditMode/Controls/FrameDefaultAnchorTests.cs`
- Modify: `Runtime/Controls/Control.cs` (add virtual `GetDefaultAnchor`; reorder ApplyCommon to parse sizeSpec before preset)
- Modify: `Runtime/Controls/Frame.cs` (override `GetDefaultAnchor`)

- [ ] **Step 1: Create red tests**

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using Frame = PromptUGUI.Controls.Frame;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class FrameDefaultAnchorTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Frame_no_anchor_no_size_fills_parent()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame size='400x200'>
    <Frame id='inner'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var inner = screen.Get<Frame>("inner").RectTransform;
            Assert.AreEqual(Vector2.zero, inner.anchorMin, "stretch X+Y → anchorMin=(0,0)");
            Assert.AreEqual(Vector2.one, inner.anchorMax, "stretch X+Y → anchorMax=(1,1)");
            Assert.AreEqual(Vector2.zero, inner.sizeDelta, "no margin → sizeDelta=0 = match parent");
        }

        [Test]
        public void Frame_width_only_stretches_height()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame size='400x200'>
    <Frame id='inner' width='100'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var inner = screen.Get<Frame>("inner").RectTransform;
            Assert.AreEqual(0f, inner.anchorMin.x, 0.001f, "X axis Left → anchorMin.x=0");
            Assert.AreEqual(0f, inner.anchorMax.x, 0.001f, "X axis Left → anchorMax.x=0");
            Assert.AreEqual(0f, inner.anchorMin.y, 0.001f, "Y axis Stretch → anchorMin.y=0");
            Assert.AreEqual(1f, inner.anchorMax.y, 0.001f, "Y axis Stretch → anchorMax.y=1");
            Assert.AreEqual(100f, inner.sizeDelta.x, 0.5f, "X explicit 100");
            Assert.AreEqual(0f, inner.sizeDelta.y, 0.5f, "Y stretch + no margin → sizeDelta.y=0 (match parent)");
        }

        [Test]
        public void Frame_height_only_stretches_width()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame size='400x200'>
    <Frame id='inner' height='50'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var inner = screen.Get<Frame>("inner").RectTransform;
            Assert.AreEqual(0f, inner.anchorMin.x, 0.001f);
            Assert.AreEqual(1f, inner.anchorMax.x, 0.001f);
            Assert.AreEqual(1f, inner.anchorMin.y, 0.001f, "Y axis Top → anchorMin.y=1");
            Assert.AreEqual(1f, inner.anchorMax.y, 0.001f, "Y axis Top → anchorMax.y=1");
            Assert.AreEqual(0f, inner.sizeDelta.x, 0.5f, "X stretch → 0");
            Assert.AreEqual(50f, inner.sizeDelta.y, 0.5f, "Y explicit 50");
        }

        [Test]
        public void Frame_explicit_size_both_axes_uses_top_left_default()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame size='400x200'>
    <Frame id='inner' size='100x50'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var inner = screen.Get<Frame>("inner").RectTransform;
            Assert.AreEqual(new Vector2(0f, 1f), inner.anchorMin, "both fixed → top-left preset");
            Assert.AreEqual(new Vector2(0f, 1f), inner.anchorMax);
            Assert.AreEqual(new Vector2(100f, 50f), inner.sizeDelta);
        }

        [Test]
        public void Frame_explicit_anchor_skips_fill_or_fit_default()
        {
            // DSS-D15: 显式写 anchor 时按原规则，不走"按轴 fill-or-fit"
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame size='400x200'>
    <Frame id='inner' anchor='center' width='100'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var inner = screen.Get<Frame>("inner").RectTransform;
            Assert.AreEqual(new Vector2(0.5f, 0.5f), inner.anchorMin, "anchor=center 明文");
            Assert.AreEqual(new Vector2(0.5f, 0.5f), inner.anchorMax);
            Assert.AreEqual(100f, inner.sizeDelta.x, 0.5f);
            Assert.AreEqual(0f, inner.sizeDelta.y, 0.5f, "no height + center 不走 stretch default");
        }
    }
}
```

- [ ] **Step 2: Refresh + verify red**

Refresh Unity, check console, run filter `FrameDefaultAnchorTests`. Expect:
- 4 fails (`Frame_no_anchor_no_size_fills_parent`, `Frame_width_only_stretches_height`, `Frame_height_only_stretches_width` — all because current default is top-left + sizeDelta=0).
- 1 pass (`Frame_explicit_size_both_axes_uses_top_left_default`, `Frame_explicit_anchor_skips_fill_or_fit_default` — current behavior already matches).

Commit RED.

- [ ] **Step 3: Implement in Control.cs**

Add virtual method after `GetNativeSize`:
```csharp
protected virtual AnchorPreset GetDefaultAnchor(SizeSpec sizeSpec)
    => new AnchorPreset(AnchorVertical.Top, AnchorHorizontal.Left);
```

Reorder `ApplyCommon` so sizeSpec is parsed BEFORE preset:
```csharp
var sizeSpec = SizeSpec.Parse(size, width, height);

if (sizeSpec.IsNativeWidth || sizeSpec.IsNativeHeight)
{
    var native = GetNativeSize();
    if (native.HasValue)
        sizeSpec = sizeSpec.WithNativeResolved(native.Value);
}

var preset = string.IsNullOrEmpty(anchor)
    ? GetDefaultAnchor(sizeSpec)
    : AnchorPreset.Parse(anchor);

sizeSpec.ValidateAgainst(preset);
```

- [ ] **Step 4: Implement Frame.cs override**

Add `using PromptUGUI.IR;` if missing. Add inside Frame class:
```csharp
protected override AnchorPreset GetDefaultAnchor(SizeSpec sizeSpec)
    => new AnchorPreset(
        sizeSpec.HasHeight ? AnchorVertical.Top : AnchorVertical.Stretch,
        sizeSpec.HasWidth ? AnchorHorizontal.Left : AnchorHorizontal.Stretch);
```

- [ ] **Step 5: Refresh + verify all green**

Refresh, console clean, run full EditMode suite — expect all green including existing `FrameMaskTests` / Screen tests (Screen extends Frame? — check). If `Screen` extends `Frame` it inherits the new default; verify Screen tests still pass.

Commit GREEN.

---

### Task 10: SKILL.md addendum — Frame fill-or-fit default

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`

Add a paragraph after the LayoutGroup / free-positioning section explaining:
> **`<Frame>` 默认 anchor 按轴 fill-or-fit**：没写 anchor 时，作者写过 `size`/`width`/`height` 的轴默认 top/left + 用作者写的值；没写的轴默认 stretch（填满父）。镜像 CSS 块流：`<div style="width:100px">` 高度按 auto 撑开，`<div/>` 两轴都 100%。显式 `anchor=` 仍按原规则严格校验（`anchor='stretch'` + size 仍是 parse error）。

Commit docs.

---

## Done Criteria

- [ ] `DropdownContentSizingTests` (5 tests), `SliderContentSizingTests` (6 tests), `ScrollListContentSizingTests` (5 tests), `FrameDefaultAnchorTests` (5 tests) all pass.
- [ ] Existing `DropdownTests` / `SliderTests` / `ScrollListTests` / `BtnContentSizingTests` / `ToggleContentSizingTests` / `FrameMaskTests` still pass.
- [ ] Full `PromptUGUI.Tests.EditMode` suite green.
- [ ] `dotnet format --verify-no-changes --severity warn` exits 0.
- [ ] SKILL.md mentions `<Dropdown>`、`<Slider>`、`<ScrollList>` in both the LayoutGroup auto-LE list and the free-positioning fallback list, each table row documents its default size, and Frame fill-or-fit default is documented.
- [ ] ~10 commits on the branch: 3× (red, green) pairs for D/S/SL + 1 (red, green) pair for Frame + 2 docs (SKILL.md, addendum) + optional lint chore.
