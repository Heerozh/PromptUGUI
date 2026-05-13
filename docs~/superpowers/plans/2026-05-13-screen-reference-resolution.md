# Screen Reference Resolution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add optional `reference="WxH"` attribute to `<Screen>` so authors can switch CanvasScaler to `ScaleWithScreenSize` declaratively, including `.variant` form. When unset, behavior stays identical to today (`ConstantPixelSize, scaleFactor=1`). Auto-infers `matchWidthOrHeight` from reference orientation (W ≥ H → 0, H > W → 1).

**Architecture:** Parser stores `reference=` / `reference.<variant>` on `ScreenDef.Root.Attributes` and `Root.VariantOverrides`, reusing the existing `VariantResolver`. `Screen.Open` reads the resolved value, parses to `Vector2`, applies `CanvasScaler` properties before invoking `UI.CanvasConfigurator`. `Screen.ReSolve` re-applies on every variant flip so `reference.mobile` etc. take effect immediately.

**Tech Stack:** Unity 6+, .NET Standard 2.1, NUnit (EditMode + PlayMode), UnityMCP for test execution.

**Spec reference:** `docs~/superpowers/specs/2026-05-13-screen-reference-resolution-design.md`

---

## Pre-flight

- [ ] **Step P1: Commit this plan**

  Spec is already at `95e83ee`. Plan needs its own commit before Task 1.

  ```bash
  git add "docs~/superpowers/plans/2026-05-13-screen-reference-resolution.md"
  git commit -m "$(cat <<'EOF'
  doc: plan for <Screen reference="..."> implementation

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

- [ ] **Step P2: Verify Unity test infra is reachable + baseline green**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: no compile errors. If errors exist, stop and report — don't start TDD on a red baseline.

  ```
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
  ```

  Expected: all green. Establishes baseline.

---

## Task 1: `ReferenceResolutionParser` helper + unit tests

**Files:**
- Create: `Runtime/Application/ReferenceResolutionParser.cs`
- Create: `Tests/EditMode/Application/ReferenceResolutionParserTests.cs`

The helper is pure C#: parses `"WxH"` → `Vector2?` (null on empty/null string), throws `ParseException` on malformed non-empty input. Used both by parser (validation at parse time) and runtime (re-parse in `ApplyCanvasScaler`).

- [ ] **Step 1.1: Write failing tests**

  Create `Tests/EditMode/Application/ReferenceResolutionParserTests.cs`:

  ```csharp
  using NUnit.Framework;
  using PromptUGUI.Application;
  using PromptUGUI.Core.Parser;
  using UnityEngine;

  namespace PromptUGUI.Tests.EditMode.Application
  {
      public class ReferenceResolutionParserTests
      {
          [Test]
          public void Parse_null_returns_null()
          {
              Assert.IsNull(ReferenceResolutionParser.Parse(null, "ctx"));
          }

          [Test]
          public void Parse_empty_returns_null()
          {
              Assert.IsNull(ReferenceResolutionParser.Parse("", "ctx"));
          }

          [Test]
          public void Parse_valid_WxH_returns_vector()
          {
              var v = ReferenceResolutionParser.Parse("1920x1080", "ctx");
              Assert.IsTrue(v.HasValue);
              Assert.AreEqual(new Vector2(1920, 1080), v.Value);
          }

          [Test]
          public void Parse_floats_allowed()
          {
              var v = ReferenceResolutionParser.Parse("960.5x540.25", "ctx");
              Assert.IsTrue(v.HasValue);
              Assert.AreEqual(new Vector2(960.5f, 540.25f), v.Value);
          }

          [Test]
          public void Parse_missing_x_throws()
          {
              var ex = Assert.Throws<ParseException>(
                  () => ReferenceResolutionParser.Parse("1920", "ctx"));
              StringAssert.Contains("WxH", ex.Message);
              StringAssert.Contains("ctx", ex.Message);
          }

          [Test]
          public void Parse_zero_width_throws()
          {
              var ex = Assert.Throws<ParseException>(
                  () => ReferenceResolutionParser.Parse("0x1080", "ctx"));
              StringAssert.Contains("positive", ex.Message);
          }

          [Test]
          public void Parse_zero_height_throws()
          {
              var ex = Assert.Throws<ParseException>(
                  () => ReferenceResolutionParser.Parse("1920x0", "ctx"));
              StringAssert.Contains("positive", ex.Message);
          }

          [Test]
          public void Parse_negative_throws()
          {
              Assert.Throws<ParseException>(
                  () => ReferenceResolutionParser.Parse("-1x100", "ctx"));
              Assert.Throws<ParseException>(
                  () => ReferenceResolutionParser.Parse("100x-1", "ctx"));
          }

          [Test]
          public void Parse_garbage_throws()
          {
              Assert.Throws<ParseException>(
                  () => ReferenceResolutionParser.Parse("abc", "ctx"));
              Assert.Throws<ParseException>(
                  () => ReferenceResolutionParser.Parse("axb", "ctx"));
              Assert.Throws<ParseException>(
                  () => ReferenceResolutionParser.Parse("100x200x300", "ctx"));
          }
      }
  }
  ```

- [ ] **Step 1.2: Run tests, verify they fail**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: compile error about `ReferenceResolutionParser` not found.

- [ ] **Step 1.3: Implement `ReferenceResolutionParser`**

  Create `Runtime/Application/ReferenceResolutionParser.cs`:

  ```csharp
  using System.Globalization;
  using PromptUGUI.Core.Parser;
  using UnityEngine;

  namespace PromptUGUI.Application
  {
      /// <summary>
      /// Parses <c>reference="WxH"</c> values on <c>&lt;Screen&gt;</c>. Empty / null
      /// input returns null (= "no reference, use ConstantPixelSize"). Malformed
      /// non-empty input throws <see cref="ParseException"/>.
      /// </summary>
      internal static class ReferenceResolutionParser
      {
          public static Vector2? Parse(string raw, string contextLabel)
          {
              if (string.IsNullOrEmpty(raw)) return null;

              var x = raw.IndexOf('x');
              if (x <= 0 || x >= raw.Length - 1 || raw.IndexOf('x', x + 1) >= 0)
                  throw new ParseException(
                      $"{contextLabel}: invalid reference '{raw}', expected WxH " +
                      $"(e.g. '1920x1080')");

              var wStr = raw.Substring(0, x);
              var hStr = raw.Substring(x + 1);

              if (!float.TryParse(wStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                  || !float.TryParse(hStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
                  throw new ParseException(
                      $"{contextLabel}: invalid reference '{raw}', both dimensions must be numeric");

              if (w <= 0f || h <= 0f)
                  throw new ParseException(
                      $"{contextLabel}: reference '{raw}' both dimensions must be positive");

              return new Vector2(w, h);
          }
      }
  }
  ```

- [ ] **Step 1.4: Run tests, verify they pass**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ReferenceResolutionParserTests")
  ```

  Expected: 9 tests pass.

- [ ] **Step 1.5: Commit**

  ```bash
  git add "Runtime/Application/ReferenceResolutionParser.cs" \
          "Tests/EditMode/Application/ReferenceResolutionParserTests.cs"
  git commit -m "$(cat <<'EOF'
  feat: add ReferenceResolutionParser helper

  Parses <Screen reference="WxH"> values into Vector2?, with empty=null and
  ParseException on malformed input. Used by parser and runtime.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 2: Parser stores `reference=` (base) on `ScreenDef.Root.Attributes`

**Files:**
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs` (`ParseScreen` at line 74)
- Create: `Tests/EditMode/Application/ScreenReferenceResolutionTests.cs`

- [ ] **Step 2.1: Write failing test**

  Create `Tests/EditMode/Application/ScreenReferenceResolutionTests.cs`:

  ```csharp
  using NUnit.Framework;
  using PromptUGUI.Application;
  using PromptUGUI.Core.Parser;

  namespace PromptUGUI.Tests.EditMode.Application
  {
      public class ScreenReferenceResolutionTests
      {
          [SetUp] public void SetUp() => UI.ResetForTests();
          [TearDown] public void TearDown() => UI.ResetForTests();

          [Test]
          public void Parser_stores_reference_attr_on_screen_root()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'>
    <Screen name='S' reference='1920x1080'>
      <Frame/>
    </Screen>
  </PromptUGUI>";
              var doc = UIDocumentParser.Parse(xml);
              var screen = doc.Screens[0];
              Assert.AreEqual("1920x1080", screen.Root.Attributes["reference"]);
          }

          [Test]
          public void Parser_screen_without_reference_has_no_attr()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'>
    <Screen name='S'><Frame/></Screen>
  </PromptUGUI>";
              var doc = UIDocumentParser.Parse(xml);
              var screen = doc.Screens[0];
              Assert.IsFalse(screen.Root.Attributes.ContainsKey("reference"));
          }
      }
  }
  ```

- [ ] **Step 2.2: Run, verify failure**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenReferenceResolutionTests")
  ```

  Expected: `Parser_stores_reference_attr_on_screen_root` fails because parser ignores `reference=` (KeyNotFoundException or similar). Second test passes (key truly not present).

- [ ] **Step 2.3: Extend `ParseScreen` to store `reference=`**

  Edit `Runtime/Core/Parser/UIDocumentParser.cs`. Insert this block right after the existing `canvas=` parsing block (after the closing brace at line 99, before the `var seenWhen = ...` line at line 101):

  ```csharp
              // <Screen reference="WxH"> stored on rootNode.Attributes so VariantResolver
              // can pick base + .variant overrides uniformly at runtime.
              var referenceAttr = el.GetAttribute("reference");
              if (!string.IsNullOrEmpty(referenceAttr))
              {
                  PromptUGUI.Application.ReferenceResolutionParser.Parse(
                      referenceAttr, $"<Screen name='{name}' reference>");
                  rootNode.Attributes["reference"] = referenceAttr;
              }
              else if (el.HasAttribute("reference"))
              {
                  // Explicit empty-string form (`reference=""`); preserve for variant-clear consistency.
                  rootNode.Attributes["reference"] = "";
              }
  ```

- [ ] **Step 2.4: Run, verify pass**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenReferenceResolutionTests")
  ```

  Expected: 2 tests pass.

- [ ] **Step 2.5: Commit**

  ```bash
  git add "Runtime/Core/Parser/UIDocumentParser.cs" \
          "Tests/EditMode/Application/ScreenReferenceResolutionTests.cs"
  git commit -m "$(cat <<'EOF'
  feat: parser stores <Screen reference="..."> on root node

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 3: Parser stores `reference.<variant>` on `Root.VariantOverrides`

**Files:**
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs` (`ParseScreen`)
- Modify: `Tests/EditMode/Application/ScreenReferenceResolutionTests.cs`

- [ ] **Step 3.1: Add failing test**

  Append to `ScreenReferenceResolutionTests.cs` inside the class:

  ```csharp
          [Test]
          public void Parser_stores_reference_variant_override()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'>
    <Screen name='S' reference='1920x1080' reference.mobile='1080x1920'>
      <Frame/>
    </Screen>
  </PromptUGUI>";
              var doc = UIDocumentParser.Parse(xml);
              var screen = doc.Screens[0];
              Assert.AreEqual("1920x1080", screen.Root.Attributes["reference"]);
              Assert.IsTrue(screen.Root.VariantOverrides.ContainsKey("reference"));
              var list = screen.Root.VariantOverrides["reference"];
              Assert.AreEqual(1, list.Count);
              Assert.AreEqual("mobile", list[0].Variant);
              Assert.AreEqual("1080x1920", list[0].Value);
          }

          [Test]
          public void Parser_stores_reference_variant_empty_clears_base()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'>
    <Screen name='S' reference='1920x1080' reference.tv=''>
      <Frame/>
    </Screen>
  </PromptUGUI>";
              var doc = UIDocumentParser.Parse(xml);
              var list = doc.Screens[0].Root.VariantOverrides["reference"];
              Assert.AreEqual("tv", list[0].Variant);
              Assert.AreEqual("", list[0].Value);
          }
  ```

- [ ] **Step 3.2: Run, verify failure**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenReferenceResolutionTests")
  ```

  Expected: 2 new tests fail (`VariantOverrides` is empty because parser ignores variant form).

- [ ] **Step 3.3: Extend `ParseScreen` to scan variant form**

  Edit `Runtime/Core/Parser/UIDocumentParser.cs`. Below the block added in Task 2.3, add:

  ```csharp
              // <Screen reference.<variant>="..."> — same shape as ElementNode VariantOverrides.
              foreach (System.Xml.XmlAttribute a in el.Attributes)
              {
                  if (!a.Name.StartsWith("reference.")) continue;
                  var variant = a.Name.Substring("reference.".Length);
                  if (string.IsNullOrEmpty(variant) || variant.Contains("."))
                      throw new ParseException(
                          $"<Screen name='{name}'>: malformed attribute '{a.Name}' " +
                          $"(variant suffix must be 'reference.variant' with no further dots)");
                  if (!string.IsNullOrEmpty(a.Value))
                      PromptUGUI.Application.ReferenceResolutionParser.Parse(
                          a.Value, $"<Screen name='{name}' {a.Name}>");
                  if (!rootNode.VariantOverrides.TryGetValue("reference", out var list))
                  {
                      list = new System.Collections.Generic.List<(string, string)>();
                      rootNode.VariantOverrides["reference"] = list;
                  }
                  list.Add((variant, a.Value));
              }
  ```

- [ ] **Step 3.4: Run, verify pass**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenReferenceResolutionTests")
  ```

  Expected: 4 tests pass.

- [ ] **Step 3.5: Commit**

  ```bash
  git add "Runtime/Core/Parser/UIDocumentParser.cs" \
          "Tests/EditMode/Application/ScreenReferenceResolutionTests.cs"
  git commit -m "$(cat <<'EOF'
  feat: parser stores reference.<variant> on screen root variant overrides

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 4: Parser-time validation errors for malformed `reference`

**Files:**
- Modify: `Tests/EditMode/Application/ScreenReferenceResolutionTests.cs`

This task adds tests that confirm `ParseException` already fires correctly from Task 2 + Task 3 wiring (which calls `ReferenceResolutionParser.Parse` for validation). No production-code change needed; existence of these tests is the deliverable.

- [ ] **Step 4.1: Add failing tests (expected to pass directly)**

  Append inside `ScreenReferenceResolutionTests`:

  ```csharp
          [Test]
          public void Parser_rejects_invalid_reference_base()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'>
    <Screen name='S' reference='1920x0'><Frame/></Screen>
  </PromptUGUI>";
              var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
              StringAssert.Contains("reference", ex.Message);
              StringAssert.Contains("positive", ex.Message);
          }

          [Test]
          public void Parser_rejects_invalid_reference_variant()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'>
    <Screen name='S' reference.mobile='-1x100'><Frame/></Screen>
  </PromptUGUI>";
              var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
              StringAssert.Contains("reference.mobile", ex.Message);
          }

          [Test]
          public void Parser_rejects_reference_without_x()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'>
    <Screen name='S' reference='1920'><Frame/></Screen>
  </PromptUGUI>";
              Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
          }
  ```

- [ ] **Step 4.2: Run, verify pass**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenReferenceResolutionTests")
  ```

  Expected: 7 tests pass (all four parser-validation tests now green from the existing impl).

- [ ] **Step 4.3: Commit**

  ```bash
  git add "Tests/EditMode/Application/ScreenReferenceResolutionTests.cs"
  git commit -m "$(cat <<'EOF'
  test: parse-time validation coverage for malformed reference attrs

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 5: `Screen.Open` applies `CanvasScaler` from XML before CanvasConfigurator

**Files:**
- Modify: `Runtime/Application/Screen.cs`
- Modify: `Tests/EditMode/Application/ScreenReferenceResolutionTests.cs`

- [ ] **Step 5.1: Add failing tests**

  Append inside `ScreenReferenceResolutionTests`:

  ```csharp
          [Test]
          public void Open_unset_reference_keeps_constant_pixel_size()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'><Screen name='S'><Frame/></Screen></PromptUGUI>";
              UI.LoadDocument("t", xml);
              var screen = UI.Open("S");
              var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
              Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize,
                              scaler.uiScaleMode);
              Assert.AreEqual(1f, scaler.scaleFactor);
          }

          [Test]
          public void Open_landscape_reference_sets_match_zero()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'>
    <Screen name='S' reference='1920x1080'><Frame/></Screen>
  </PromptUGUI>";
              UI.LoadDocument("t", xml);
              var screen = UI.Open("S");
              var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
              Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize,
                              scaler.uiScaleMode);
              Assert.AreEqual(new UnityEngine.Vector2(1920, 1080), scaler.referenceResolution);
              Assert.AreEqual(0f, scaler.matchWidthOrHeight);
          }

          [Test]
          public void Open_portrait_reference_sets_match_one()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'>
    <Screen name='S' reference='1080x1920'><Frame/></Screen>
  </PromptUGUI>";
              UI.LoadDocument("t", xml);
              var screen = UI.Open("S");
              var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
              Assert.AreEqual(1f, scaler.matchWidthOrHeight);
          }

          [Test]
          public void Open_square_reference_sets_match_zero()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'>
    <Screen name='S' reference='1000x1000'><Frame/></Screen>
  </PromptUGUI>";
              UI.LoadDocument("t", xml);
              var screen = UI.Open("S");
              var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
              Assert.AreEqual(0f, scaler.matchWidthOrHeight);
          }

          [Test]
          public void Open_canvas_configurator_runs_after_xml_reference()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'>
    <Screen name='S' reference='1920x1080'><Frame/></Screen>
  </PromptUGUI>";
              UI.LoadDocument("t", xml);
              UI.CanvasConfigurator = (canvas, _) => {
                  var s = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                  s.referenceResolution = new UnityEngine.Vector2(2560, 1440);
              };
              try
              {
                  var screen = UI.Open("S");
                  var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
                  Assert.AreEqual(new UnityEngine.Vector2(2560, 1440), scaler.referenceResolution);
              }
              finally { UI.CanvasConfigurator = null; }
          }
  ```

- [ ] **Step 5.2: Run, verify failure**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenReferenceResolutionTests")
  ```

  Expected: 4 new tests fail (CanvasScaler still at Unity defaults — referenceResolution is `Vector2(800, 600)` or similar default, match is `0.5`, etc.). The `unset_reference_keeps_constant_pixel_size` test should already pass (Unity default IS ConstantPixelSize with scaleFactor=1).

- [ ] **Step 5.3: Add `ApplyCanvasScaler` to `Screen.cs`**

  Edit `Runtime/Application/Screen.cs`. After the `Open()` method (around line 110-111, after `_variantSub = Variants.Changed.Subscribe(_ => ReSolve());`), add:

  ```csharp
          private void ApplyCanvasScaler(UnityEngine.UI.CanvasScaler scaler)
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
              scaler.uiScaleMode         = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
              scaler.referenceResolution = size;
              scaler.matchWidthOrHeight  = size.x >= size.y ? 0f : 1f;
          }
  ```

  Then call it inside `Open()`. Find the block (~ line 71-81):

  ```csharp
              var canvas = root.GetComponent<Canvas>();
              canvas.renderMode = Def.CanvasMode switch
              {
                  CanvasMode.Camera => RenderMode.ScreenSpaceCamera,
                  CanvasMode.World => RenderMode.WorldSpace,
                  _ => RenderMode.ScreenSpaceOverlay,
              };
              canvas.vertexColorAlwaysGammaSpace = true;
              UI.CanvasConfigurator?.Invoke(canvas, Def.Name);
  ```

  Insert one new line between the `canvas.vertexColorAlwaysGammaSpace = true;` line and the `UI.CanvasConfigurator?.Invoke(canvas, Def.Name);` line:

  ```csharp
              ApplyCanvasScaler(root.GetComponent<UnityEngine.UI.CanvasScaler>());
  ```

  Resulting block:

  ```csharp
              canvas.vertexColorAlwaysGammaSpace = true;
              ApplyCanvasScaler(root.GetComponent<UnityEngine.UI.CanvasScaler>());
              UI.CanvasConfigurator?.Invoke(canvas, Def.Name);
  ```

- [ ] **Step 5.4: Run, verify pass**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenReferenceResolutionTests")
  ```

  Expected: 12 tests pass total in the class (7 prior + 5 new).

- [ ] **Step 5.5: Commit**

  ```bash
  git add "Runtime/Application/Screen.cs" \
          "Tests/EditMode/Application/ScreenReferenceResolutionTests.cs"
  git commit -m "$(cat <<'EOF'
  feat: Screen.Open applies CanvasScaler from XML reference attr

  Auto-infers matchWidthOrHeight from orientation (W>=H -> 0, H>W -> 1).
  Applied before UI.CanvasConfigurator so configurator can override.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 6: `Screen.ReSolve` re-applies `CanvasScaler` on variant flip

**Files:**
- Modify: `Runtime/Application/Screen.cs` (`ReSolve` at line 167)
- Modify: `Tests/EditMode/Application/ScreenReferenceResolutionTests.cs`

- [ ] **Step 6.1: Add failing tests**

  Append inside `ScreenReferenceResolutionTests`:

  ```csharp
          [Test]
          public void Variant_flip_reapplies_canvas_scaler()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'>
    <Screen name='S' reference='1920x1080' reference.mobile='1080x1920'>
      <Frame/>
    </Screen>
  </PromptUGUI>";
              UI.LoadDocument("t", xml);
              var screen = UI.Open("S");
              var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
              Assert.AreEqual(new UnityEngine.Vector2(1920, 1080), scaler.referenceResolution);
              Assert.AreEqual(0f, scaler.matchWidthOrHeight);

              UI.Variants.Set("mobile", true);
              try
              {
                  Assert.AreEqual(new UnityEngine.Vector2(1080, 1920), scaler.referenceResolution);
                  Assert.AreEqual(1f, scaler.matchWidthOrHeight);
              }
              finally { UI.Variants.Set("mobile", false); }
          }

          [Test]
          public void Variant_empty_value_clears_to_constant_pixel_size()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'>
    <Screen name='S' reference='1920x1080' reference.raw=''>
      <Frame/>
    </Screen>
  </PromptUGUI>";
              UI.LoadDocument("t", xml);
              var screen = UI.Open("S");
              var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();

              UI.Variants.Set("raw", true);
              try
              {
                  Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize,
                                  scaler.uiScaleMode);
                  Assert.AreEqual(1f, scaler.scaleFactor);
              }
              finally { UI.Variants.Set("raw", false); }
          }

          [Test]
          public void Variant_off_returns_to_base_reference()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'>
    <Screen name='S' reference='1920x1080' reference.mobile='1080x1920'>
      <Frame/>
    </Screen>
  </PromptUGUI>";
              UI.LoadDocument("t", xml);
              var screen = UI.Open("S");
              var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
              UI.Variants.Set("mobile", true);
              UI.Variants.Set("mobile", false);
              Assert.AreEqual(new UnityEngine.Vector2(1920, 1080), scaler.referenceResolution);
              Assert.AreEqual(0f, scaler.matchWidthOrHeight);
          }
  ```

- [ ] **Step 6.2: Run, verify failure**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenReferenceResolutionTests")
  ```

  Expected: 3 new tests fail — variant flip doesn't trigger CanvasScaler re-apply yet.

- [ ] **Step 6.3: Extend `ReSolve` to re-apply CanvasScaler**

  Edit `Runtime/Application/Screen.cs`. In `ReSolve` (line 167), after the existing `foreach (var kv in _nodeMap) { ... }` loop (currently ends around line 196), add one line:

  ```csharp
              ApplyCanvasScaler(RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>());
  ```

  Resulting tail of `ReSolve`:

  ```csharp
              foreach (var kv in _nodeMap)
              {
                  var node = kv.Key;
                  if (inactiveNodes.Contains(node)) continue;
                  var control = kv.Value;
                  var entry = _registry.Resolve(node.Tag);
                  ControlAttributeApplier.Apply(node, control, entry, Variants);
              }
              ApplyCanvasScaler(RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>());
          }
  ```

- [ ] **Step 6.4: Run, verify pass**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenReferenceResolutionTests")
  ```

  Expected: 15 tests in class pass.

- [ ] **Step 6.5: Sanity check whole EditMode suite still green**

  ```
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
  ```

  Expected: full EditMode suite all green; no regression.

- [ ] **Step 6.6: Commit**

  ```bash
  git add "Runtime/Application/Screen.cs" \
          "Tests/EditMode/Application/ScreenReferenceResolutionTests.cs"
  git commit -m "$(cat <<'EOF'
  feat: Screen.ReSolve re-applies CanvasScaler on variant flip

  reference.mobile etc. now take effect immediately when variant toggles.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 7: XSD generator declares `reference` + `anyAttribute` on `<Screen>`

**Files:**
- Modify: `Editor/XsdGenerator.cs` (`WriteScreen` at line 257)
- Modify: `Tests/EditMode/Editor/XsdGeneratorTests.cs` (class `PromptUGUI.Tests.Editor.XsdGeneratorTests`, asmdef `PromptUGUI.Tests.EditorOnly`)

- [ ] **Step 7.1: Add failing tests**

  Append two new `[Test]` methods inside the existing `XsdGeneratorTests` class in `Tests/EditMode/Editor/XsdGeneratorTests.cs`. The fixture uses `new ControlRegistry()` (not `UI.Registry`) and calls the public `XsdGenerator.Generate(r)`:

  ```csharp
          [Test]
          public void Screen_element_declares_reference_attribute()
          {
              var r = new ControlRegistry();
              var xsd = XsdGenerator.Generate(r);
              StringAssert.Contains("name=\"reference\"", xsd);
          }

          [Test]
          public void Screen_element_allows_variant_form_via_any_attribute()
          {
              var r = new ControlRegistry();
              var xsd = XsdGenerator.Generate(r);
              StringAssert.Contains("anyAttribute", xsd);
          }
  ```

- [ ] **Step 7.2: Run, verify failure**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
  ```

  Expected: 2 new tests fail (no `reference` attr / no `anyAttribute` yet on Screen).

- [ ] **Step 7.3: Extend `WriteScreen`**

  Edit `Editor/XsdGenerator.cs`. In `WriteScreen` (line 257-297), after the existing `canvas` attribute block (ends at line 293) and before the trailing two `WriteEndElement()` calls, insert:

  ```csharp
              // reference="WxH", optional
              w.WriteStartElement("xs", "attribute", null);
              w.WriteAttributeString("name", "reference");
              w.WriteAttributeString("use", "optional");
              w.WriteAttributeString("type", "xs:string");
              w.WriteEndElement();

              // Accept reference.<variant>="..." (open variant namespace).
              w.WriteStartElement("xs", "anyAttribute", null);
              w.WriteAttributeString("processContents", "lax");
              w.WriteEndElement();
  ```

  Note: `anyAttribute` MUST come after all `<xs:attribute>` declarations per XSD schema ordering rules. Place it right before the closing `WriteEndElement()` pairs.

- [ ] **Step 7.4: Run, verify pass**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
  ```

  Expected: 2 new tests pass; no XSD test regressions.

- [ ] **Step 7.5: Trigger XSD regeneration so an `xmllint` smoke-test passes**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
  ```

  Then run xmllint against an example XML that uses `reference.mobile`:

  ```bash
  cat > /tmp/ref-smoke.ui.xml <<'EOF'
  <?xml version="1.0" encoding="utf-8"?>
  <PromptUGUI version="1">
    <Screen name="S" reference="1920x1080" reference.mobile="1080x1920">
      <Frame anchor="stretch"/>
    </Screen>
  </PromptUGUI>
  EOF
  xmllint --noout --schema Assets/PromptUGUI.gen.xsd /tmp/ref-smoke.ui.xml
  ```

  Expected: `validates` — no schema error on `reference` or `reference.mobile`.

- [ ] **Step 7.6: Commit**

  ```bash
  git add "Editor/XsdGenerator.cs" "Tests/EditMode/Editor/XsdGeneratorTests.cs"
  git commit -m "$(cat <<'EOF'
  feat(xsd): declare <Screen reference> + anyAttribute for variant form

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 8: SKILL.md + master spec author-facing documentation

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` (§5 tail)

- [ ] **Step 8.1: Update SKILL.md — Canvas configuration section**

  Open `.claude/skills/authoring-promptugui-xml/SKILL.md`, find the "Canvas configuration (optional):" section. After the final paragraph (the one explaining `worldCamera` is the user's job; "With no configurator and no `canvas=` attribute, every Screen is `ScreenSpaceOverlay`, `sortingOrder=0`."), insert this block:

  ````markdown
  **Pixel units & scaling.** 默认情况下 `<Screen>` 创建的 `CanvasScaler` 是
  `ConstantPixelSize, scaleFactor=1`，所以 `width="240"` ≡ 240 个**设备像素** ——
  同一 XML 在 1080p / 4K / 不同手机上视觉大小不一致。要按"设计分辨率"自动缩放
  （业内默认配法），在 `<Screen>` 上声明 `reference="WxH"`：

  ```xml
  <Screen name="MainMenu" reference="1920x1080">...</Screen>

  <!-- 横屏 PC + 竖屏手机一份 XML -->
  <Screen name="MainMenu"
          reference="1920x1080"
          reference.mobile="1080x1920">...</Screen>
  ```

  - `reference="WxH"` → CanvasScaler 切到 `ScaleWithScreenSize`，referenceResolution
    即该值。`matchWidthOrHeight` 按朝向自动推断：W ≥ H 锁宽（0），H > W 锁高（1）。
  - 未设 / `reference=""` → 保留默认 ConstantPixelSize 行为。
  - `.variant` 形态：`reference.mobile="..."` 同其他属性 variant 规则；变体切换时
    CanvasScaler 立即重应用。
  - 要 `match=0.5` 折中或改 `referencePixelsPerUnit`：走 `UI.CanvasConfigurator`
    手改。**不要在两条路径同时改 CanvasScaler** —— variant flip 时 XML 路径会覆盖
    configurator 的改动。
  ````

- [ ] **Step 8.2: Update SKILL.md — Common mistakes table**

  In the "Common mistakes" table, append a new row at the end (after the `'150%' must be in (0%, 100%]` row):

  ```markdown
  | UI 在不同屏上视觉大小不一（4K 上变邮票、手机上变巨人） | `<Screen>` 没设 `reference=`，走默认 `ConstantPixelSize, scaleFactor=1`，XML 数字直接 = 设备像素 | 在 `<Screen>` 上加 `reference="1920x1080"`（或你的设计分辨率），切到 `ScaleWithScreenSize` |
  ```

- [ ] **Step 8.3: Update SKILL.md — File anatomy table**

  In the "File anatomy" table near the top, the `<Screen name="..." [canvas="..."]>` row. Update its Notes column to mention `reference=`:

  Current text fragment to replace:
  > `One Screen = one Canvas. Names unique across all loaded files. `canvas="overlay\|camera\|world"`, default `overlay`.`

  New text:
  > `One Screen = one Canvas. Names unique across all loaded files. `canvas="overlay\|camera\|world"`, default `overlay`. Optional `reference="WxH"` (+ `.variant`) switches CanvasScaler to ScaleWithScreenSize.`

- [ ] **Step 8.4: Update SKILL.md — Quick reference cheatsheet**

  In the Quick reference cheatsheet section, find the `XML CANVAS` line:

  ```
  XML CANVAS    <Screen name="X" canvas="overlay|camera|world">   default overlay; renderMode only
  ```

  Replace with two lines:

  ```
  XML CANVAS    <Screen name="X" canvas="overlay|camera|world">   default overlay; renderMode only
  XML SCALER    <Screen name="X" reference="WxH">                 ScaleWithScreenSize; unset = ConstantPixelSize
                                                                    .variant overrides supported (reference.mobile=...)
  ```

- [ ] **Step 8.5: Update master spec §5 tail**

  Open `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`. Find §5 (the section describing `<Screen>` / `<Template>` / `<Import>` top-level elements). At the section's end (before §6 begins), append:

  ```markdown

  ### 5.x `<Screen reference="WxH">` (since 2026-05-13)

  Optional attribute on `<Screen>` switches CanvasScaler to `ScaleWithScreenSize`
  with the given reference resolution. Unset = `ConstantPixelSize, scaleFactor=1`
  (zero-migration default). `matchWidthOrHeight` auto-inferred from orientation
  (W ≥ H → 0, H > W → 1). `.variant` form supported; variant flips re-apply
  immediately via `ReSolve`. Full design: see
  [`2026-05-13-screen-reference-resolution-design.md`](2026-05-13-screen-reference-resolution-design.md).
  ```

  (Renumber `5.x` to the next free subsection number based on the actual §5 contents at edit time.)

- [ ] **Step 8.6: Validate SKILL XML examples with xmllint**

  ```bash
  cat > /tmp/skill-examples.ui.xml <<'EOF'
  <?xml version="1.0" encoding="utf-8"?>
  <PromptUGUI version="1">
    <Screen name="A" reference="1920x1080"><Frame/></Screen>
    <Screen name="B" reference="1920x1080" reference.mobile="1080x1920"><Frame/></Screen>
  </PromptUGUI>
  EOF
  xmllint --noout --schema Assets/PromptUGUI.gen.xsd /tmp/skill-examples.ui.xml
  ```

  Expected: validates.

- [ ] **Step 8.7: Commit**

  ```bash
  git add ".claude/skills/authoring-promptugui-xml/SKILL.md" \
          "docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md"
  git commit -m "$(cat <<'EOF'
  doc: SKILL + master spec for <Screen reference="..."> attribute

  Author guide section + common-mistake row + master spec pointer.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Final verification

- [ ] **Step F1: Full test suite green**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error","warning"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
  ```

  Expected: full suite green, no compile errors, no warnings introduced.

- [ ] **Step F2: Lint pass**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  dotnet format style PromptUGUI.Lint.slnx
  dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  Expected: clean exit.

- [ ] **Step F3: Optional PlayMode smoke (manual Unity)**

  Open Unity, open a scene that hosts a Screen with `reference="1920x1080" reference.mobile="1080x1920"`. In Device Simulator, switch between a desktop layout and iPhone 14 Pro; verify UI scales coherently and `UI.Variants.Set("mobile", true)` re-scales without re-opening the Screen.

  This step is **manual** — for autonomous executors, mark this checkbox as N/A and report.

- [ ] **Step F4: Push branch and prepare PR description**

  ```bash
  git push -u origin feat/screen-reference-resolution
  ```

  PR body should reference the spec and plan files and summarize the visible API change (one new optional attribute on `<Screen>`).
