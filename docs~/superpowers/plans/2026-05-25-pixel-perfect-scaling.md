# Pixel-Perfect Scaling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `Pixel` scale mode to PromptUGUI Screens that locks the CanvasScaler to ConstantPixelSize with integer-only scaleFactor (plus 1/2^n snap below 1x), so pixel-art games render 1 design pixel to exactly N physical pixels.

**Architecture:** Project-level default `UI.DefaultScaleMode = ScaleMode.Pixel` with per-Screen XML override `scale-mode="auto|pixel"` (+ `.variant`). Pixel mode reuses the existing `reference="WxH"` as the design resolution. Algorithm: `factor = min(floor(screenW/dW), floor(screenH/dH))` for ≥1, else `1/2^ceil(log2(1/raw))`. Resize handled via `RectTransformDimensionsChanged` event Screen already exposes. Variant flips already trigger `Screen.ReSolve` → `ApplyCanvasScaler`.

**Tech Stack:** Unity 6+, C# 9, NUnit, Unity Test Framework (EditMode). Tests run via Unity MCP. Lint via `dotnet format` in `.lint/`.

**Spec:** [`docs~/superpowers/specs/2026-05-25-pixel-perfect-scaling-design.md`](../specs/2026-05-25-pixel-perfect-scaling-design.md)

---

## Task 0: Create feature branch

- [ ] **Step 1: Verify current branch is main and tree is clean**

Run: `git status && git branch --show-current`
Expected: `main`, no uncommitted changes besides the spec + plan files just written.

- [ ] **Step 2: Create + checkout feature branch**

Run: `git checkout -b feat/pixel-perfect-scaling`
Expected: `Switched to a new branch 'feat/pixel-perfect-scaling'`

- [ ] **Step 3: Commit spec + plan as the initial branch commit**

```bash
git add docs~/superpowers/specs/2026-05-25-pixel-perfect-scaling-design.md \
        docs~/superpowers/plans/2026-05-25-pixel-perfect-scaling.md
git commit -m "$(cat <<'EOF'
docs: spec + plan for pixel-perfect scaling

Adds <Screen scale-mode="auto|pixel"> + UI.DefaultScaleMode for pixel-art
projects. Closes the open question punted by 2026-05-13 reference resolution
spec (§10 "像素风游戏的整数倍 scaling").

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 1: Add `ScaleMode` enum

**Files:**
- Create: `Runtime/Application/ScaleMode.cs`

- [ ] **Step 1: Create the enum file**

```csharp
namespace PromptUGUI.Application
{
    public enum ScaleMode
    {
        // Existing behavior: when <Screen reference="..."> is set, CanvasScaler runs in
        // ScaleWithScreenSize (continuous fractional scaling). When unset, falls back to
        // ConstantPixelSize with scaleFactor=1 (XML numbers = device pixels).
        Auto = 0,

        // ConstantPixelSize + integer scaleFactor (with 1/2^n snap below 1x). Requires
        // <Screen reference="WxH"> as the design resolution. Use for pixel-art / iso-grid
        // projects where 1 design pixel must map to exactly N physical pixels.
        Pixel = 1,
    }
}
```

- [ ] **Step 2: Refresh Unity and verify compile**

Call: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`

Then: `mcp__UnityMCP__read_console(action="get", types=["error"])`
Expected: no errors related to `ScaleMode.cs`.

- [ ] **Step 3: Commit**

```bash
git add Runtime/Application/ScaleMode.cs Runtime/Application/ScaleMode.cs.meta
git commit -m "$(cat <<'EOF'
feat: add ScaleMode { Auto, Pixel } enum

Public surface for the pixel-perfect scaling feature. Default value Auto = 0
preserves zero-migration behavior.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: PixelScaleSolver (pure algorithm, TDD)

**Files:**
- Create: `Runtime/Application/PixelScaleSolver.cs`
- Test: `Tests/EditMode/Application/PixelScaleSolverTests.cs`

- [ ] **Step 1: Write the failing test file**

Create `Tests/EditMode/Application/PixelScaleSolverTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    public class PixelScaleSolverTests
    {
        [TestCase(1920f, 1080f, 1920f, 1080f, 1f)]
        [TestCase(3840f, 2160f, 1920f, 1080f, 2f)]
        [TestCase(5760f, 3240f, 1920f, 1080f, 3f)]
        [TestCase(7680f, 4320f, 1920f, 1080f, 4f)]
        // 21:9 ultrawide screen with 16:9 design: vertical axis is tighter -> 1x
        [TestCase(3840f, 1620f, 1920f, 1080f, 1f)]
        // 9:16 tall screen with 16:9 design: horizontal axis is tighter -> 1x
        [TestCase(1920f, 2160f, 1920f, 1080f, 1f)]
        // Sub-1x snaps to 1/2^n
        [TestCase(1366f, 768f, 1920f, 1080f, 0.5f)]
        [TestCase(1280f, 720f, 1920f, 1080f, 0.5f)]
        [TestCase(960f, 540f, 1920f, 1080f, 0.5f)]
        [TestCase(480f, 270f, 1920f, 1080f, 0.25f)]
        [TestCase(240f, 135f, 1920f, 1080f, 0.125f)]
        [TestCase(100f, 100f, 1920f, 1080f, 0.03125f)]
        // Degenerate inputs fall back to 1
        [TestCase(0f, 100f, 1920f, 1080f, 1f)]
        [TestCase(100f, 0f, 1920f, 1080f, 1f)]
        [TestCase(1920f, 1080f, 0f, 0f, 1f)]
        [TestCase(-1f, 100f, 1920f, 1080f, 1f)]
        public void Solve_returns_expected_factor(
            float sw, float sh, float dw, float dh, float expected)
        {
            var f = PixelScaleSolver.Solve(new Vector2(sw, sh), new Vector2(dw, dh));
            Assert.AreEqual(expected, f, 1e-6f);
        }
    }
}
```

- [ ] **Step 2: Refresh + run the test, verify it fails (PixelScaleSolver doesn't exist)**

Call: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
Then: `mcp__UnityMCP__read_console(action="get", types=["error"])`
Expected: error "The name 'PixelScaleSolver' does not exist".

- [ ] **Step 3: Create `PixelScaleSolver.cs` with minimal implementation**

Create `Runtime/Application/PixelScaleSolver.cs`:

```csharp
using UnityEngine;

namespace PromptUGUI.Application
{
    internal static class PixelScaleSolver
    {
        // raw = min(screenW/designW, screenH/designH)  (fit-inside)
        // raw >= 1  -> floor(raw)                       (integer 1, 2, 3, ...)
        // raw <  1  -> 1 / 2^ceil(log2(1/raw))          (snap to 0.5, 0.25, 0.125, ...)
        // Degenerate input (any axis <= 0) -> 1 (safe fallback).
        public static float Solve(Vector2 screen, Vector2 design)
        {
            if (screen.x <= 0f || screen.y <= 0f || design.x <= 0f || design.y <= 0f)
                return 1f;
            float raw = Mathf.Min(screen.x / design.x, screen.y / design.y);
            if (raw >= 1f) return Mathf.Floor(raw);
            int n = Mathf.CeilToInt(Mathf.Log(1f / raw, 2f));
            return 1f / Mathf.Pow(2f, n);
        }
    }
}
```

- [ ] **Step 4: Refresh + run the tests, verify they pass**

Call: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
Then: `mcp__UnityMCP__read_console(action="get", types=["error"])`
Expected: no errors.
Then: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="PixelScaleSolverTests")`
Expected: all 15 test cases pass.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/PixelScaleSolver.cs \
        Runtime/Application/PixelScaleSolver.cs.meta \
        Tests/EditMode/Application/PixelScaleSolverTests.cs \
        Tests/EditMode/Application/PixelScaleSolverTests.cs.meta
git commit -m "$(cat <<'EOF'
feat: PixelScaleSolver fit-inside + 1/2^n snap

Pure-function helper used by the Pixel scale mode. Tested in isolation
with 15 parametrized cases covering 4K/1080/21:9/9:16/sub-1x/degenerate.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: `UI.DefaultScaleMode` + `UI.CanvasSizeOverride` + reset

**Files:**
- Modify: `Runtime/Application/UI.cs` (add 2 members; extend `ResetForTests`)

- [ ] **Step 1: Add `DefaultScaleMode` property near `CanvasConfigurator`**

In `Runtime/Application/UI.cs`, immediately after line 165 (`public static System.Action<UnityEngine.Canvas, string> CanvasConfigurator { get; set; }`), insert:

```csharp
        // Project-level default for <Screen> scale-mode. Per-Screen XML override
        // (scale-mode="auto|pixel") wins when present. See ScaleMode.cs for semantics.
        public static ScaleMode DefaultScaleMode { get; set; } = ScaleMode.Auto;

        // Test seam: when non-null, Screen.ApplyCanvasScaler (Pixel branch) reads canvas
        // size from this override instead of the Canvas RectTransform. Mirrors the pattern
        // used by Internal.OrientationTracker.ScreenSizeOverride.
        internal static System.Func<UnityEngine.Vector2> CanvasSizeOverride { get; set; }
```

- [ ] **Step 2: Reset both in `ResetForTests`**

In `Runtime/Application/UI.cs`, find the `ResetForTests` method (line ~707) and after the existing `CanvasConfigurator = null;` line, add:

```csharp
            DefaultScaleMode = ScaleMode.Auto;
            CanvasSizeOverride = null;
```

- [ ] **Step 3: Refresh + verify compile**

Call: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
Then: `mcp__UnityMCP__read_console(action="get", types=["error"])`
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add Runtime/Application/UI.cs
git commit -m "$(cat <<'EOF'
feat: UI.DefaultScaleMode + CanvasSizeOverride test seam

Project-level default surface for scale mode (defaults to Auto so existing
projects see no behavior change). CanvasSizeOverride is internal-only,
consumed by EditMode tests of the Pixel branch.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Parser — `scale-mode` and `scale-mode.<variant>` (TDD)

**Files:**
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs:130` (insert after the existing `reference.<variant>` block)
- Test: `Tests/EditMode/Application/ScreenScaleModeTests.cs` (new)

- [ ] **Step 1: Write the failing parser tests**

Create `Tests/EditMode/Application/ScreenScaleModeTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Application
{
    public class ScreenScaleModeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Parser_stores_scale_mode_attr_on_screen_root()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual("pixel", doc.Screens[0].Root.Attributes["scale-mode"]);
        }

        [Test]
        public void Parser_stores_scale_mode_auto()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto'><Frame/></Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual("auto", doc.Screens[0].Root.Attributes["scale-mode"]);
        }

        [Test]
        public void Parser_screen_without_scale_mode_has_no_attr()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame/></Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            Assert.IsFalse(doc.Screens[0].Root.Attributes.ContainsKey("scale-mode"));
        }

        [Test]
        public void Parser_rejects_invalid_scale_mode_value()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel-perfect'><Frame/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale-mode", ex.Message);
            StringAssert.Contains("'auto'", ex.Message);
            StringAssert.Contains("'pixel'", ex.Message);
        }

        [Test]
        public void Parser_accepts_empty_scale_mode_as_unset_semantics()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode=''><Frame/></Screen>
</PromptUGUI>";
            // Empty string is stored verbatim; runtime treats it as 'inherit DefaultScaleMode'.
            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual("", doc.Screens[0].Root.Attributes["scale-mode"]);
        }

        [Test]
        public void Parser_stores_scale_mode_variant_override()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'
          scale-mode='auto'
          scale-mode.portrait='pixel'
          scale-mode.landscape='auto'
          reference='1920x1080'>
    <Frame/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var list = doc.Screens[0].Root.VariantOverrides["scale-mode"];
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("portrait", list[0].Variant);
            Assert.AreEqual("pixel", list[0].Value);
            Assert.AreEqual("landscape", list[1].Variant);
            Assert.AreEqual("auto", list[1].Value);
        }

        [Test]
        public void Parser_rejects_invalid_scale_mode_variant_value()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode.mobile='nope'><Frame/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale-mode.mobile", ex.Message);
        }

        [Test]
        public void Parser_rejects_scale_mode_variant_with_extra_dot()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode.foo.bar='pixel'><Frame/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale-mode.foo.bar", ex.Message);
        }
    }
}
```

- [ ] **Step 2: Refresh + run the tests, verify they fail**

Call: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
Then: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenScaleModeTests")`
Expected: at least the parse-stage tests fail (the attribute is silently ignored, so stored value is missing).

- [ ] **Step 3: Implement parser branch in `UIDocumentParser.ParseScreen`**

In `Runtime/Core/Parser/UIDocumentParser.cs`, find the existing `reference.<variant>` loop ending around line 130 (just before `var seenWhen = ...`). Insert the new scale-mode parsing block:

```csharp
            // <Screen scale-mode="auto|pixel"> — parse-time validation only checks the
            // enum value. "Pixel requires reference=" is enforced at runtime instead,
            // because variant + DefaultScaleMode combinations can't be resolved here.
            if (el.HasAttribute("scale-mode"))
            {
                var scaleAttr = el.GetAttribute("scale-mode");
                ValidateScaleMode(scaleAttr, $"<Screen name='{name}' scale-mode>");
                rootNode.Attributes["scale-mode"] = scaleAttr;
            }

            // <Screen scale-mode.<variant>="..."> — same shape as ElementNode VariantOverrides.
            foreach (System.Xml.XmlAttribute a in el.Attributes)
            {
                if (!a.Name.StartsWith("scale-mode.")) continue;
                var variant = a.Name.Substring("scale-mode.".Length);
                if (string.IsNullOrEmpty(variant) || variant.Contains("."))
                    throw new ParseException(
                        $"<Screen name='{name}'>: malformed attribute '{a.Name}' " +
                        $"(variant suffix must be 'scale-mode.variant' with no further dots)");
                ValidateScaleMode(a.Value, $"<Screen name='{name}' {a.Name}>");
                if (!rootNode.VariantOverrides.TryGetValue("scale-mode", out var list))
                {
                    list = new System.Collections.Generic.List<(string, string)>();
                    rootNode.VariantOverrides["scale-mode"] = list;
                }
                list.Add((variant, a.Value));
            }
```

Then add the helper near the bottom of the class (search for `private static` methods; place adjacent to similar helpers — anywhere inside `class UIDocumentParser`):

```csharp
        private static void ValidateScaleMode(string raw, string contextLabel)
        {
            // Empty string means "inherit UI.DefaultScaleMode"; runtime semantics decide.
            if (string.IsNullOrEmpty(raw)) return;
            if (raw == "auto" || raw == "pixel") return;
            throw new ParseException(
                $"{contextLabel}: invalid value '{raw}' (expected 'auto' or 'pixel')");
        }
```

- [ ] **Step 4: Refresh + run the tests, verify they pass**

Call: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
Then: `mcp__UnityMCP__read_console(action="get", types=["error"])`
Expected: no errors.
Then: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenScaleModeTests")`
Expected: 8 tests pass (all the parse-stage tests in the file).

- [ ] **Step 5: Run the full ScreenReferenceResolutionTests too as regression**

Call: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenReferenceResolutionTests")`
Expected: still green.

- [ ] **Step 6: Commit**

```bash
git add Runtime/Core/Parser/UIDocumentParser.cs \
        Tests/EditMode/Application/ScreenScaleModeTests.cs \
        Tests/EditMode/Application/ScreenScaleModeTests.cs.meta
git commit -m "$(cat <<'EOF'
feat: parse <Screen scale-mode="auto|pixel"> (+ .variant)

Stores values on rootNode.Attributes / VariantOverrides mirroring reference=
plumbing. Validates enum at parse-time; defers "Pixel requires reference=" to
runtime since variant+default combinations can't be resolved at parse.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Refactor `Screen.ApplyCanvasScaler` — extract `ApplyAuto` (no behavior change)

**Files:**
- Modify: `Runtime/Application/Screen.cs:136-152`

This step is a pure refactor: extract the existing body into `ApplyAuto`. All existing tests must remain green.

- [ ] **Step 1: Replace `ApplyCanvasScaler` with dispatch + extracted `ApplyAuto`**

In `Runtime/Application/Screen.cs`, replace the entire current `ApplyCanvasScaler` method (lines 136-152) with:

```csharp
        private void ApplyCanvasScaler(UnityEngine.UI.CanvasScaler scaler)
        {
            var mode = ResolveScaleMode();
            if (mode == ScaleMode.Pixel) ApplyPixel(scaler);
            else                         ApplyAuto(scaler);
        }

        private ScaleMode ResolveScaleMode()
        {
            var raw = PromptUGUI.Variants.VariantResolver.ResolveAttribute(
                Def.Root, "scale-mode", Variants);
            if (string.IsNullOrEmpty(raw)) return UI.DefaultScaleMode;
            return raw == "pixel" ? ScaleMode.Pixel : ScaleMode.Auto;
        }

        private void ApplyAuto(UnityEngine.UI.CanvasScaler scaler)
        {
            var raw = PromptUGUI.Variants.VariantResolver.ResolveAttribute(
                Def.Root, "reference", Variants);
            var parsed = PromptUGUI.Application.ReferenceResolutionParser.Parse(
                raw, $"<Screen name='{Def.Name}' reference> (runtime)");
            if (!parsed.HasValue)
            {
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
                return;
            }
            var size = parsed.Value;
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = size;
            scaler.matchWidthOrHeight = size.x >= size.y ? 0f : 1f;
        }

        // Stub — implemented in Task 6. Falls through to Auto so all existing tests
        // remain green during the refactor.
        private void ApplyPixel(UnityEngine.UI.CanvasScaler scaler) => ApplyAuto(scaler);
```

- [ ] **Step 2: Refresh + run all EditMode tests, verify zero regressions**

Call: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
Then: `mcp__UnityMCP__read_console(action="get", types=["error"])`
Expected: no errors.
Then: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])`
Expected: every previously-passing test still passes.

- [ ] **Step 3: Commit**

```bash
git add Runtime/Application/Screen.cs
git commit -m "$(cat <<'EOF'
refactor: extract Screen.ApplyCanvasScaler into Auto + Pixel branches

Pure refactor. ApplyAuto holds the existing ScaleWithScreenSize logic.
ApplyPixel is a stub that delegates to Auto; replaced with the real impl
in the next commit so this one stays a pure no-op.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Implement `ApplyPixel` (TDD)

**Files:**
- Modify: `Runtime/Application/Screen.cs` (replace `ApplyPixel` stub)
- Test: `Tests/EditMode/Application/ScreenScaleModeTests.cs` (extend)

- [ ] **Step 1: Append the apply-stage tests to `ScreenScaleModeTests.cs`**

In `Tests/EditMode/Application/ScreenScaleModeTests.cs`, add these tests inside the existing class:

```csharp
        private static PromptUGUI.Application.Screen OpenScreen(string xml)
        {
            UI.SourceResolver = _ => AwaitableHelpers.Completed("dummy");
            UI.LoadDocument("test", xml);
            return (PromptUGUI.Application.Screen)UI.Open("S");
        }

        [Test]
        public void Pixel_with_design_equal_to_canvas_yields_factor_1()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1920f, 1080f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize, scaler.uiScaleMode);
            Assert.AreEqual(1f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Pixel_with_4k_canvas_yields_factor_2()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(2f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Pixel_with_smaller_canvas_snaps_to_half()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1366f, 768f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(0.5f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Pixel_without_reference_logs_error_and_falls_back_to_1()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1920f, 1080f);
            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("requires.*reference"));
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize, scaler.uiScaleMode);
            Assert.AreEqual(1f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Default_scale_mode_pixel_applies_without_xml_attr()
        {
            UI.DefaultScaleMode = ScaleMode.Pixel;
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize, scaler.uiScaleMode);
            Assert.AreEqual(2f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Xml_auto_overrides_default_pixel()
        {
            UI.DefaultScaleMode = ScaleMode.Pixel;
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
        }

        [Test]
        public void Variant_flip_switches_to_pixel_mode()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'
          scale-mode='auto'
          scale-mode.portrait='pixel'
          reference='1920x1080'
          reference.portrait='1080x1920'>
    <Frame/>
  </Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);

            UI.Orientation.AutoTrack = false;
            UI.Orientation.Set(isPortrait: true);
            // ReSolve re-applies the scaler.
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize, scaler.uiScaleMode);
        }

        [Test]
        public void ResetForTests_clears_default_and_override()
        {
            UI.DefaultScaleMode = ScaleMode.Pixel;
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(99f, 99f);
            UI.ResetForTests();
            Assert.AreEqual(ScaleMode.Auto, UI.DefaultScaleMode);
            Assert.IsNull(UI.CanvasSizeOverride);
        }
```

> Note on `AwaitableHelpers`: it's an internal helper in the runtime asmdef, exposed to tests via `InternalsVisibleTo`. Existing tests like `DocumentLoaderTests` use the same pattern — if your IDE can't resolve it, add `using PromptUGUI.Application;` (where the helper lives).

- [ ] **Step 2: Refresh + run the new tests, verify they fail**

Call: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
Then: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenScaleModeTests")`
Expected: the new apply-stage tests fail (`ApplyPixel` is still a stub delegating to Auto).

- [ ] **Step 3: Replace `ApplyPixel` stub with the real implementation**

In `Runtime/Application/Screen.cs`, replace the `ApplyPixel` stub line with:

```csharp
        private void ApplyPixel(UnityEngine.UI.CanvasScaler scaler)
        {
            var refRaw = PromptUGUI.Variants.VariantResolver.ResolveAttribute(
                Def.Root, "reference", Variants);
            var design = PromptUGUI.Application.ReferenceResolutionParser.Parse(
                refRaw, $"<Screen name='{Def.Name}' reference> (pixel-mode runtime)");
            if (!design.HasValue)
            {
                UnityEngine.Debug.LogError(
                    $"[PromptUGUI] <Screen name='{Def.Name}' scale-mode='pixel'>: " +
                    $"requires a reference='WxH' to compute integer scale factor. " +
                    $"Falling back to ConstantPixelSize, scaleFactor=1.");
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
                return;
            }
            var canvasSize = UI.CanvasSizeOverride != null
                ? UI.CanvasSizeOverride()
                : ReadCanvasRectSize();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = PixelScaleSolver.Solve(canvasSize, design.Value);
        }

        private UnityEngine.Vector2 ReadCanvasRectSize()
        {
            var rt = RootGameObject.GetComponent<RectTransform>();
            var rect = rt.rect;
            return new UnityEngine.Vector2(rect.width, rect.height);
        }
```

- [ ] **Step 4: Refresh + run new tests, verify they pass**

Call: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
Then: `mcp__UnityMCP__read_console(action="get", types=["error"])`
Expected: no errors.
Then: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenScaleModeTests")`
Expected: all 16 tests in the file pass (8 parse + 8 apply).

- [ ] **Step 5: Run full EditMode suite for regressions**

Call: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])`
Expected: every test green.

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application/Screen.cs \
        Tests/EditMode/Application/ScreenScaleModeTests.cs
git commit -m "$(cat <<'EOF'
feat: implement Screen.ApplyPixel using PixelScaleSolver

Resolves reference= via VariantResolver (so reference.<variant> still
applies), reads canvas size from UI.CanvasSizeOverride or RT.rect, and
sets ConstantPixelSize + computed factor. Missing reference logs an
error and falls back to scaleFactor=1.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Auto-reapply on window resize (TDD)

**Files:**
- Modify: `Runtime/Application/Screen.cs` (`Open` + new private field/method)
- Test: `Tests/EditMode/Application/ScreenScaleModeTests.cs` (extend)

- [ ] **Step 1: Add the failing resize test**

Append inside the `ScreenScaleModeTests` class:

```csharp
        [Test]
        public void Resize_event_recomputes_pixel_factor()
        {
            UnityEngine.Vector2 size = new(1920f, 1080f);
            UI.CanvasSizeOverride = () => size;
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(1f, scaler.scaleFactor, 1e-6f);

            // Simulate window resize: change the override and fire the relay.
            size = new UnityEngine.Vector2(3840f, 2160f);
            var relay = screen.RootGameObject.GetComponent<PromptUGUI.Application.RectDimensionsRelay>();
            relay.OnDimensionsChanged?.Invoke();

            Assert.AreEqual(2f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Resize_does_not_recurse()
        {
            UnityEngine.Vector2 size = new(1920f, 1080f);
            UI.CanvasSizeOverride = () => size;
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var relay = screen.RootGameObject.GetComponent<PromptUGUI.Application.RectDimensionsRelay>();
            // Manually fire 5 times in a row; should not stack-overflow.
            for (int i = 0; i < 5; i++) relay.OnDimensionsChanged?.Invoke();
            Assert.Pass();
        }
```

- [ ] **Step 2: Refresh + run, verify the resize test fails**

Call: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
Then: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenScaleModeTests")`
Expected: `Resize_event_recomputes_pixel_factor` fails (still 1f because no subscription wires the event to ApplyCanvasScaler).

- [ ] **Step 3: Add subscription + reentrancy guard in `Screen`**

In `Runtime/Application/Screen.cs`, add the field next to the existing `_isApplyingScaler`-style state — put it near the top with the other private fields (right below `private IDisposable _variantSub;`):

```csharp
        private bool _isReapplyingScaler;
```

Then in `Open()`, find the line `relay.OnDimensionsChanged = () => RectTransformDimensionsChanged?.Invoke();` (around line 109). Replace it with:

```csharp
            relay.OnDimensionsChanged = OnCanvasDimensionsChanged;
```

And add the new method (place it adjacent to `ApplyCanvasScaler`):

```csharp
        private void OnCanvasDimensionsChanged()
        {
            // Forward to public subscribers first.
            RectTransformDimensionsChanged?.Invoke();
            // Pixel mode needs to recompute scaleFactor when canvas size changes;
            // Auto mode does its work via Unity's ScaleWithScreenSize internally, so
            // reapplying is idempotent and cheap. Guard against re-entry just in case
            // a subscriber happens to mutate the RectTransform during the callback.
            if (_isReapplyingScaler) return;
            _isReapplyingScaler = true;
            try
            {
                var scaler = RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler != null) ApplyCanvasScaler(scaler);
            }
            finally { _isReapplyingScaler = false; }
        }
```

- [ ] **Step 4: Refresh + run, verify both resize tests pass**

Call: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
Then: `mcp__UnityMCP__read_console(action="get", types=["error"])`
Expected: no errors.
Then: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenScaleModeTests")`
Expected: all tests in the file pass (now 18 total).

- [ ] **Step 5: Run full EditMode suite for regressions**

Call: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])`
Expected: every test green.

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application/Screen.cs \
        Tests/EditMode/Application/ScreenScaleModeTests.cs
git commit -m "$(cat <<'EOF'
feat: reapply CanvasScaler on RectTransformDimensionsChanged

Pixel mode needs to recompute scaleFactor whenever the window resizes;
this wires the existing RectDimensionsRelay callback to ApplyCanvasScaler
with a reentrancy guard. Auto mode is unaffected (ScaleWithScreenSize
handles resize internally).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: XSD generator — `scale-mode` enum attribute (TDD)

**Files:**
- Modify: `Editor/XsdGenerator.cs:307` (insert before `anyAttribute`)
- Test: `Tests/EditMode/Editor/XsdGeneratorTests.cs` (extend)

- [ ] **Step 1: Add a failing XSD assertion**

Append to `Tests/EditMode/Editor/XsdGeneratorTests.cs` inside the class:

```csharp
        [Test]
        public void Screen_element_declares_scale_mode_enum_attribute()
        {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            // The Screen element should carry an explicit attribute name="scale-mode"
            // restricted to {auto, pixel}.
            StringAssert.Contains("name=\"scale-mode\"", xsd);
            StringAssert.Contains("value=\"auto\"", xsd);
            StringAssert.Contains("value=\"pixel\"", xsd);
        }
```

- [ ] **Step 2: Refresh + run, verify it fails**

Call: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
Then: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="XsdGeneratorTests")`
Expected: `Screen_element_declares_scale_mode_enum_attribute` fails (string missing).

- [ ] **Step 3: Emit the `scale-mode` attribute in `WriteScreen`**

In `Editor/XsdGenerator.cs`, find the `// reference="WxH", optional` block (line ~302). Immediately after the `reference` attribute's `WriteEndElement();` and before the `anyAttribute` block (line ~311), insert:

```csharp
            // scale-mode="auto|pixel", optional
            w.WriteStartElement("xs", "attribute", null);
            w.WriteAttributeString("name", "scale-mode");
            w.WriteAttributeString("use", "optional");
            w.WriteStartElement("xs", "simpleType", null);
            w.WriteStartElement("xs", "restriction", null);
            w.WriteAttributeString("base", "xs:string");
            foreach (var v in new[] { "auto", "pixel" })
            {
                w.WriteStartElement("xs", "enumeration", null);
                w.WriteAttributeString("value", v);
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
```

- [ ] **Step 4: Refresh + run XSD tests, verify pass**

Call: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
Then: `mcp__UnityMCP__read_console(action="get", types=["error"])`
Expected: no errors.
Then: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="XsdGeneratorTests")`
Expected: all XsdGenerator tests green.

- [ ] **Step 5: Commit**

```bash
git add Editor/XsdGenerator.cs Tests/EditMode/Editor/XsdGeneratorTests.cs
git commit -m "$(cat <<'EOF'
feat(xsd): emit scale-mode enum attribute on <Screen>

Adds the explicit xs:attribute name='scale-mode' with restriction to
{auto, pixel}. The pre-existing xs:anyAttribute covers scale-mode.<variant>
forms.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: SKILL.md + master spec updates

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`

- [ ] **Step 1: Update XML skill — Canvas attributes section**

In `.claude/skills/authoring-promptugui-xml/SKILL.md`, find the "Canvas / scaler attributes on `<Screen>`" section (line ~494). After the bullet ending `**不要在两条路径同时改 CanvasScaler** —— variant flip 时 XML 路径会覆盖 configurator 的改动。`, insert a new bullet:

```markdown
- `scale-mode="auto|pixel"` (+ `.variant`)：默认 `auto` = 上面 `reference` 的连续缩放语义。`pixel` 切到 `ConstantPixelSize` + 整数倍 `scaleFactor`（fit-inside 取小；屏幕 < 设计时 snap 到 1/2、1/4、1/8 等保 2x2 干净降采样）。**必须配 `reference="WxH"`**，否则运行期 `Debug.LogError` 并降级 `scaleFactor=1`。像素美术 / 等距图项目用 —— sprite 永远整数倍渲染到屏幕像素。项目级默认走 C# `UI.DefaultScaleMode = ScaleMode.Pixel`；具体 Screen 想退回连续缩放写 `scale-mode="auto"`。
```

- [ ] **Step 2: Update XML skill — File anatomy row for `<Screen>`**

Find the table row starting with `` `<Screen name="..." [canvas="..."] [reference="..."] [reference.portrait="..."]>` `` (line ~71). Change the opening cell to:

```markdown
| `<Screen name="..." [canvas="..."] [reference="..."] [scale-mode="..."] [reference.portrait="..."]>` |
```

Then in the same row's Notes column, append `Optional scale-mode="auto\|pixel" (+ .variant); pixel = integer scaleFactor.` to the end.

- [ ] **Step 3: Update XML skill — Quick reference cheatsheet**

Find the cheatsheet block containing the `reference="WxH"` line (around line 608-610). Immediately after the `reference="WxH"` line, add:

```
              scale-mode="auto|pixel"          pixel = ConstantPixelSize + integer factor
                                               requires reference; project default via UI.DefaultScaleMode
```

- [ ] **Step 4: Update C# skill — `UI.DefaultScaleMode`**

In `.claude/skills/scripting-promptugui-csharp/SKILL.md`, find the section that documents `UI.CanvasConfigurator` (search for `CanvasConfigurator`). After that paragraph, add:

```markdown
**像素美术整数缩放**：`UI.DefaultScaleMode = ScaleMode.Pixel`（启动期一次性设置）让所有 `<Screen>` 默认走 `ConstantPixelSize` + 整数倍 `scaleFactor`。每个 Screen 必须配 `reference="WxH"` 作为设计分辨率。具体某个 Screen 想 opt-out 写 XML `scale-mode="auto"`。详见 [authoring-promptugui-xml](../authoring-promptugui-xml/SKILL.md) 的 Canvas 段。
```

- [ ] **Step 5: Update master spec §5**

In `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`, find §5 (顶层元素 / Screen). Locate the existing `reference=` note added by the 2026-05-13 spec. Immediately after that note, add:

```markdown
- `scale-mode="auto|pixel"`（可选，支持 `.variant`）：`pixel` 切 CanvasScaler 到 `ConstantPixelSize` + 整数倍 `scaleFactor`（用于像素艺术）。详见 [`2026-05-25-pixel-perfect-scaling-design.md`](2026-05-25-pixel-perfect-scaling-design.md)。
```

- [ ] **Step 6: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md \
        .claude/skills/scripting-promptugui-csharp/SKILL.md \
        docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md
git commit -m "$(cat <<'EOF'
docs: document scale-mode in SKILLs + master spec

XML skill: Canvas attribute bullet + File anatomy row + cheatsheet line.
C# skill: UI.DefaultScaleMode usage note.
Master spec §5: scale-mode pointer to new design doc.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: Lint pass + final verification

- [ ] **Step 1: Run `dotnet format` on the lint solution**

Run from repo root:

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format style PromptUGUI.Lint.slnx
dotnet format analyzers PromptUGUI.Lint.slnx
```

Expected: no errors. Whitespace / style fixers may rewrite files; review diff.

- [ ] **Step 2: Run `--verify-no-changes` check**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: exit code 0.

- [ ] **Step 3: Run UIXmlLint on `Runtime/Resources/`**

```bash
cd .lint && dotnet run --project UIXmlLint -- ../Runtime/Resources/
```

Expected: exit code 0 (no `.ui.xml` regressed because we didn't touch any).

- [ ] **Step 4: Full EditMode test suite via Unity MCP**

Call: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
Then: `mcp__UnityMCP__read_console(action="get", types=["error"])`
Expected: no errors.
Then: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])`
Then: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])`
Expected: all tests green in both assemblies.

- [ ] **Step 5: Commit lint fixes if any**

If lint modified files:

```bash
git add -u
git commit -m "$(cat <<'EOF'
chore: dotnet format whitespace/style/analyzer fixes

Auto-applied by .lint/ dotnet format on the new files added in this branch.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

If lint produced no changes, skip this step.

- [ ] **Step 6: Push branch + open PR**

```bash
git push -u origin feat/pixel-perfect-scaling
gh pr create --title "feat: <Screen scale-mode=\"pixel\"> integer scaling for pixel-art" --body "$(cat <<'EOF'
## Summary
- Adds `<Screen scale-mode="auto|pixel">` (+ `.variant`) and `UI.DefaultScaleMode` for project-level default
- `Pixel` mode uses `ConstantPixelSize` + integer `scaleFactor` (`1/2^n` snap below 1x) so pixel-art sprites stay integer-aligned at every screen size
- Re-applies on window resize via existing `RectTransformDimensionsChanged`; variant flips already trigger `ApplyCanvasScaler` via `ReSolve`

Closes the open question punted by `docs~/superpowers/specs/2026-05-13-screen-reference-resolution-design.md` §10 ("像素风游戏的整数倍 scaling").

Spec: `docs~/superpowers/specs/2026-05-25-pixel-perfect-scaling-design.md`
Plan: `docs~/superpowers/plans/2026-05-25-pixel-perfect-scaling.md`

## Test plan
- [ ] EditMode `PixelScaleSolverTests` — 15 parametrized algorithm cases
- [ ] EditMode `ScreenScaleModeTests` — 18 parse / apply / variant / resize cases
- [ ] EditMode `XsdGeneratorTests.Screen_element_declares_scale_mode_enum_attribute`
- [ ] Full EditMode + EditorOnly suites green
- [ ] `dotnet format --verify-no-changes` green
- [ ] Manual: host project Device Simulator → switch 1080p / 4K / iPhone SE; confirm `<Screen scale-mode="pixel">` factor jumps in integer steps

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Expected: PR URL returned.

---

## Self-Review

After writing this plan, verifying against the spec:

**1. Spec coverage:**
- §1.3 goal 1 (ScaleMode enum + DefaultScaleMode) → Tasks 1, 3
- §1.3 goal 2 (XML scale-mode + variants) → Task 4
- §1.3 goal 3 (Pixel algorithm) → Tasks 2, 6
- §1.3 goal 4 (reference required, fallback LogError) → Task 6 step 3
- §1.3 goal 5 (resize via RectTransformDimensionsChanged) → Task 7
- §1.3 goal 6 (variant flip via ReSolve) → covered automatically by Task 5 refactor (ReSolve already calls ApplyCanvasScaler) — verified in Task 6 step 1 `Variant_flip_switches_to_pixel_mode`
- §1.3 goal 7 (CanvasConfigurator still runs after) → unchanged; verified implicitly by Auto-mode regression tests in Task 5
- §3.1-3.5 (file structure) → Tasks 1, 2, 3, 4, 5, 6, 7 each map 1:1 to a §3.x section
- §3.6 (XsdGenerator) → Task 8
- §3.7 (SKILL.md) → Task 9
- §3.8 (master spec) → Task 9 step 5
- §6 test matrix (EditMode rows) → all covered by Tasks 2, 4, 6, 7
- §6 PlayMode "optional" → intentionally skipped (spec §9 marks as plan-stage decision; EditMode + CanvasSizeOverride covers the same logic)

**2. Placeholder scan:** No "TBD"/"TODO"/"implement later"; every step has either an exact code block or an exact command with expected output. Lint commit step has a conditional that's spelled out ("if lint produced no changes, skip").

**3. Type consistency:** `ScaleMode { Auto, Pixel }` used identically in Tasks 1/3/4/5/6/8/9. `ApplyAuto` / `ApplyPixel` / `ResolveScaleMode` signatures match between Task 5 (refactor) and Task 6 (real impl). `UI.CanvasSizeOverride` signature `Func<Vector2>` consistent across Tasks 3, 6, 7. `RectDimensionsRelay.OnDimensionsChanged` field name verified against `Screen.cs:108-109` source.
