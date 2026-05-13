# SafeArea Built-in Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `<SafeArea>` built-in control that wraps its children inside `Screen.safeArea`, auto-responding to screen rotation, window resize, Device Simulator, and Variant ReSolve.

**Architecture:** New `SafeArea : Control` mirrors `Frame` (no visual, no `[UIAttr]`). On `OnAttached` it attaches an internal `SafeAreaTracker : MonoBehaviour` that uses Unity's anchor-fraction technique (`anchorMin/Max = safeArea / Screen.size`, `offsetMin/Max = 0`). Tracker re-applies on Unity's `OnRectTransformDimensionsChange` magic method plus a new `Control.OnAfterApply` hook (called by `ControlAttributeApplier.Apply` after `ApplyCommon`) so Variant ReSolve doesn't leave SafeArea showing a stretch-default anchor for one frame.

**Tech Stack:** Unity 6+, .NET Standard 2.1, NUnit (EditMode + PlayMode), UnityMCP for test execution.

**Spec reference:** `docs~/superpowers/specs/2026-05-13-safearea-builtin-design.md`

---

## Pre-flight

- [ ] **Step P1: Confirm spec is committed (or commit it)**

  Per CLAUDE.md the user must explicitly approve commits. Ask once before starting Task 1:

  > "Ready to start. About to commit two doc files (`2026-05-13-safearea-builtin-design.md`, `2026-05-13-safearea-builtin.md`) as a single 'doc: …' commit before coding. OK?"

  If yes:

  ```bash
  git add docs~/superpowers/specs/2026-05-13-safearea-builtin-design.md \
          docs~/superpowers/plans/2026-05-13-safearea-builtin.md
  git commit -m "$(cat <<'EOF'
  doc: spec + plan for <SafeArea> built-in

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

- [ ] **Step P2: Verify Unity test infra is reachable**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: no compile errors before we touch anything. If errors exist, stop and report — don't start TDD on a red baseline.

---

## Task 1: Scaffold empty `SafeArea` control + register it

**Files:**
- Create: `Runtime/Controls/SafeArea.cs`
- Modify: `Runtime/Application/BuiltinPrimitives.cs:10` (insert after Frame registration)
- Create: `Tests/EditMode/Controls/SafeAreaTests.cs`

- [ ] **Step 1.1: Write the failing test**

  Create `Tests/EditMode/Controls/SafeAreaTests.cs`:

  ```csharp
  using NUnit.Framework;
  using PromptUGUI.Application;
  using PromptUGUI.Controls;

  namespace PromptUGUI.Tests.EditMode.Controls
  {
      public class SafeAreaTests
      {
          [SetUp] public void SetUp() => UI.ResetForTests();
          [TearDown] public void TearDown() => UI.ResetForTests();

          [Test]
          public void SafeArea_parses_and_instantiates()
          {
              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'><Screen name='S'>
    <SafeArea id='sa'/>
  </Screen></PromptUGUI>";
              UI.LoadDocument("test", xml);
              var screen = UI.Open("S");
              var sa = screen.Get<SafeArea>("sa");
              Assert.IsNotNull(sa);
              Assert.IsNotNull(sa.GameObject);
              Assert.IsNotNull(sa.RectTransform);
          }
      }
  }
  ```

- [ ] **Step 1.2: Run test, verify it fails**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: compile error about `SafeArea` not found. (Test won't even run yet.)

- [ ] **Step 1.3: Create the SafeArea class**

  Create `Runtime/Controls/SafeArea.cs`:

  ```csharp
  namespace PromptUGUI.Controls
  {
      public sealed class SafeArea : Control
      {
          // OnAttached / OnAfterApply 在后续 Task 中填充。
      }
  }
  ```

- [ ] **Step 1.4: Register SafeArea in BuiltinPrimitives**

  Edit `Runtime/Application/BuiltinPrimitives.cs`. After line 10 (`reg.Register<Frame>("Frame", null);`) insert:

  ```csharp
  reg.Register<SafeArea>("SafeArea", null);
  ```

  Final ordering: `Frame, SafeArea, Image, Icon, Text, ...`

- [ ] **Step 1.5: Refresh Unity and run test**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SafeAreaTests")
  ```

  Expected: PASS for `SafeArea_parses_and_instantiates`.

- [ ] **Step 1.6: Commit**

  Ask the user before committing (CLAUDE.md). If approved:

  ```bash
  git add Runtime/Controls/SafeArea.cs \
          Runtime/Controls/SafeArea.cs.meta \
          Runtime/Application/BuiltinPrimitives.cs \
          Tests/EditMode/Controls/SafeAreaTests.cs \
          Tests/EditMode/Controls/SafeAreaTests.cs.meta
  git commit -m "$(cat <<'EOF'
  feat: scaffold empty <SafeArea> built-in

  Registers the new tag; tracker + parse validation land in
  follow-up commits.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

  Note: `.meta` files are created by Unity on refresh. If they don't exist yet, run refresh once more before committing.

---

## Task 2: Parse-time validation for forbidden attributes

**Files:**
- Create: `Tests/EditMode/Parser/SafeAreaParserTests.cs`
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs:340` (insert SafeArea validation block after the Icon validation at lines 321-340)

- [ ] **Step 2.1: Write the failing tests**

  Create `Tests/EditMode/Parser/SafeAreaParserTests.cs`:

  ```csharp
  using NUnit.Framework;
  using PromptUGUI.Core.Parser;

  namespace PromptUGUI.Tests.EditMode.Parser
  {
      public class SafeAreaParserTests
      {
          private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
              "<PromptUGUI version='1'><Screen name='S'>";
          private const string Footer = "</Screen></PromptUGUI>";

          [Test]
          public void SafeArea_no_attrs_parses()
          {
              var xml = Header + "<SafeArea id='sa'/>" + Footer;
              Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
          }

          [Test]
          public void SafeArea_with_anchor_throws()
          {
              var xml = Header + "<SafeArea anchor='stretch'/>" + Footer;
              var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
              StringAssert.Contains("anchor", ex.Message);
              StringAssert.Contains("SafeArea", ex.Message);
          }

          [Test]
          public void SafeArea_with_size_throws()
          {
              var xml = Header + "<SafeArea size='100x100'/>" + Footer;
              var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
              StringAssert.Contains("size", ex.Message);
          }

          [Test]
          public void SafeArea_with_width_throws()
          {
              var xml = Header + "<SafeArea width='100'/>" + Footer;
              var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
              StringAssert.Contains("width", ex.Message);
          }

          [Test]
          public void SafeArea_with_height_throws()
          {
              var xml = Header + "<SafeArea height='100'/>" + Footer;
              var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
              StringAssert.Contains("height", ex.Message);
          }

          [Test]
          public void SafeArea_with_margin_throws()
          {
              var xml = Header + "<SafeArea margin='10'/>" + Footer;
              var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
              StringAssert.Contains("margin", ex.Message);
          }

          [Test]
          public void SafeArea_with_pivot_throws()
          {
              var xml = Header + "<SafeArea pivot='0.5,0.5'/>" + Footer;
              var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
              StringAssert.Contains("pivot", ex.Message);
          }

          [Test]
          public void SafeArea_variant_override_on_anchor_throws()
          {
              var xml = Header + "<SafeArea anchor.mobile='stretch'/>" + Footer;
              var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
              StringAssert.Contains("anchor", ex.Message);
          }

          [Test]
          public void SafeArea_allows_id_hidden_interactable_if()
          {
              var xml = Header + "<SafeArea id='sa' hidden='true' interactable='false' if='mobile'/>" + Footer;
              Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
          }
      }
  }
  ```

  Note: confirm `using` namespace for `UIDocumentParser` matches an existing test file. If `PromptUGUI.Core.Parser` is wrong, grep one of the existing parser tests (e.g. `IconParserTests.cs`) and copy whatever it uses.

- [ ] **Step 2.2: Run tests, verify failure**

  ```
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SafeAreaParserTests")
  ```

  Expected: `SafeArea_no_attrs_parses` passes; the seven `_throws` tests FAIL because the parser currently accepts those attrs.

- [ ] **Step 2.3: Add validation block in UIDocumentParser**

  In `Runtime/Core/Parser/UIDocumentParser.cs`, after the Icon validation block (currently ending at line 340) and before the `native` size validation (currently at line 343), insert:

  ```csharp
  // <SafeArea> 校验：禁止 layout 类属性，几何完全由 Screen.safeArea 决定。
  // 要 padding 用 <Frame margin="..."> 嵌套；要不同形状用其他容器组合。
  if (tag == "SafeArea" && ns == null)
  {
      foreach (var key in new[] { "anchor", "size", "width", "height", "margin", "pivot" })
      {
          if (node.Attributes.ContainsKey(key))
              throw new ParseException(
                  $"<SafeArea> does not accept attribute '{key}'; " +
                  $"SafeArea is always stretched to Screen.safeArea. " +
                  $"To add inner padding, wrap content in <Frame margin=\"...\"/> inside the SafeArea.");
          if (node.VariantOverrides.ContainsKey(key))
              throw new ParseException(
                  $"<SafeArea> does not accept variant override for '{key}'; " +
                  $"SafeArea is always stretched to Screen.safeArea.");
      }
  }
  ```

- [ ] **Step 2.4: Refresh and verify all parser tests pass**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SafeAreaParserTests")
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IconParserTests")
  ```

  Expected: all 9 SafeAreaParserTests pass; IconParserTests still all green (regression check on adjacent validation).

- [ ] **Step 2.5: Commit (ask user)**

  ```bash
  git add Runtime/Core/Parser/UIDocumentParser.cs \
          Tests/EditMode/Parser/SafeAreaParserTests.cs \
          Tests/EditMode/Parser/SafeAreaParserTests.cs.meta
  git commit -m "$(cat <<'EOF'
  feat: parse-time validation for <SafeArea> attributes

  Reject anchor/size/width/height/margin/pivot (base + variant
  override forms) — SafeArea geometry is fully driven by
  Screen.safeArea; user-set layout attrs would silently lose.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 3: `SafeAreaTracker` MonoBehaviour computes anchor fractions

**Files:**
- Create: `Runtime/Controls/Internal/SafeAreaTracker.cs`
- Add to: `Tests/EditMode/Controls/SafeAreaTests.cs`

- [ ] **Step 3.1: Write the failing test**

  Append to `Tests/EditMode/Controls/SafeAreaTests.cs` (inside the existing `SafeAreaTests` class):

  ```csharp
  [Test]
  public void Tracker_applies_safe_area_fractions()
  {
      try
      {
          PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
              () => new UnityEngine.Rect(0f, 100f, 1080f, 1820f);
          PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
              () => new UnityEngine.Vector2(1080f, 1920f);

          var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
          var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
          tracker.Apply();

          var rt = (UnityEngine.RectTransform)go.transform;
          Assert.AreEqual(0f, rt.anchorMin.x, 0.001f);
          Assert.AreEqual(100f / 1920f, rt.anchorMin.y, 0.001f);
          Assert.AreEqual(1f, rt.anchorMax.x, 0.001f);
          Assert.AreEqual(1f, rt.anchorMax.y, 0.001f);
          Assert.AreEqual(UnityEngine.Vector2.zero, rt.offsetMin);
          Assert.AreEqual(UnityEngine.Vector2.zero, rt.offsetMax);

          UnityEngine.Object.DestroyImmediate(go);
      }
      finally
      {
          PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
          PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
      }
  }

  [Test]
  public void Tracker_full_screen_safe_area_yields_identity_anchors()
  {
      try
      {
          PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
              () => new UnityEngine.Rect(0f, 0f, 1080f, 1920f);
          PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
              () => new UnityEngine.Vector2(1080f, 1920f);

          var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
          var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
          tracker.Apply();

          var rt = (UnityEngine.RectTransform)go.transform;
          Assert.AreEqual(UnityEngine.Vector2.zero, rt.anchorMin);
          Assert.AreEqual(UnityEngine.Vector2.one, rt.anchorMax);

          UnityEngine.Object.DestroyImmediate(go);
      }
      finally
      {
          PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
          PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
      }
  }

  [Test]
  public void Tracker_zero_screen_size_is_noop()
  {
      try
      {
          PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
              () => new UnityEngine.Rect(0f, 0f, 1080f, 1820f);
          PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
              () => UnityEngine.Vector2.zero;

          var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
          var rt = (UnityEngine.RectTransform)go.transform;
          rt.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
          rt.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);

          var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
          tracker.Apply();

          // Zero screen size → tracker bails; anchors unchanged.
          Assert.AreEqual(new UnityEngine.Vector2(0.5f, 0.5f), rt.anchorMin);
          Assert.AreEqual(new UnityEngine.Vector2(0.5f, 0.5f), rt.anchorMax);

          UnityEngine.Object.DestroyImmediate(go);
      }
      finally
      {
          PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
          PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
      }
  }
  ```

- [ ] **Step 3.2: Verify failure**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: compile error — `SafeAreaTracker` type doesn't exist.

- [ ] **Step 3.3: Create `SafeAreaTracker.cs`**

  Create `Runtime/Controls/Internal/SafeAreaTracker.cs`:

  ```csharp
  using System;
  using UnityEngine;

  namespace PromptUGUI.Controls.Internal
  {
      [DisallowMultipleComponent]
      internal sealed class SafeAreaTracker : MonoBehaviour
      {
          // 仅测试注入：默认 null → 走真实 Screen.safeArea / Screen.width|height
          internal static Func<Rect> SafeAreaOverride;
          internal static Func<Vector2> ScreenSizeOverride;

          private RectTransform _rt;

          private void OnEnable()
          {
              _rt = transform as RectTransform;
              Apply();
          }

          private void OnRectTransformDimensionsChange()
          {
              if (_rt == null) return;
              Apply();
          }

          internal void Apply()
          {
              if (_rt == null) _rt = transform as RectTransform;
              if (_rt == null) return;

              var safe = SafeAreaOverride != null ? SafeAreaOverride() : Screen.safeArea;
              var screenSize = ScreenSizeOverride != null
                  ? ScreenSizeOverride()
                  : new Vector2(Screen.width, Screen.height);

              if (screenSize.x <= 0f || screenSize.y <= 0f) return;

              var aMin = new Vector2(safe.xMin / screenSize.x, safe.yMin / screenSize.y);
              var aMax = new Vector2(safe.xMax / screenSize.x, safe.yMax / screenSize.y);

              _rt.anchorMin = aMin;
              _rt.anchorMax = aMax;
              _rt.offsetMin = Vector2.zero;
              _rt.offsetMax = Vector2.zero;
          }
      }
  }
  ```

- [ ] **Step 3.4: Refresh and run tracker tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SafeAreaTests")
  ```

  Expected: 4 tests pass (`SafeArea_parses_and_instantiates` + 3 new tracker tests).

- [ ] **Step 3.5: Commit (ask user)**

  ```bash
  git add Runtime/Controls/Internal/SafeAreaTracker.cs \
          Runtime/Controls/Internal/SafeAreaTracker.cs.meta \
          Tests/EditMode/Controls/SafeAreaTests.cs
  git commit -m "$(cat <<'EOF'
  feat: SafeAreaTracker component computes anchor-fraction

  Standalone MonoBehaviour using Unity's anchor-fraction technique
  (anchorMin/Max = safeArea / Screen.size, offsetMin/Max = 0).
  Reacts to OnRectTransformDimensionsChange. Static
  SafeAreaOverride / ScreenSizeOverride hooks let tests inject
  deterministic values since Screen.safeArea is a static getter.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 4: Wire tracker into `SafeArea.OnAttached`

**Files:**
- Modify: `Runtime/Controls/SafeArea.cs`
- Add to: `Tests/EditMode/Controls/SafeAreaTests.cs`

- [ ] **Step 4.1: Write the failing test**

  Append to `SafeAreaTests`:

  ```csharp
  [Test]
  public void SafeArea_attaches_tracker_on_instantiation()
  {
      const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'><Screen name='S'>
    <SafeArea id='sa'/>
  </Screen></PromptUGUI>";
      UI.LoadDocument("test", xml);
      var screen = UI.Open("S");
      var sa = screen.Get<SafeArea>("sa");
      var tracker = sa.GameObject.GetComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
      Assert.IsNotNull(tracker, "SafeArea.OnAttached should add SafeAreaTracker");
  }
  ```

- [ ] **Step 4.2: Verify failure**

  ```
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SafeArea_attaches_tracker_on_instantiation")
  ```

  Expected: FAIL (no tracker on the GameObject yet).

- [ ] **Step 4.3: Add `OnAttached` to SafeArea**

  Replace `Runtime/Controls/SafeArea.cs` contents:

  ```csharp
  using PromptUGUI.Controls.Internal;

  namespace PromptUGUI.Controls
  {
      public sealed class SafeArea : Control
      {
          private SafeAreaTracker _tracker;

          public override void OnAttached()
          {
              _tracker = GameObject.AddComponent<SafeAreaTracker>();
          }
      }
  }
  ```

- [ ] **Step 4.4: Refresh, run test, verify pass**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SafeAreaTests")
  ```

  Expected: all 5 SafeAreaTests pass.

- [ ] **Step 4.5: Commit (ask user)**

  ```bash
  git add Runtime/Controls/SafeArea.cs \
          Tests/EditMode/Controls/SafeAreaTests.cs
  git commit -m "$(cat <<'EOF'
  feat: SafeArea attaches tracker on OnAttached

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 5: `Control.OnAfterApply` hook + SafeArea override

**Files:**
- Modify: `Runtime/Controls/Control.cs` (add `OnAfterApply` virtual after `OnAttached` at line 39)
- Modify: `Runtime/Application/ControlAttributeApplier.cs:68` (call `OnAfterApply` after `ApplyCommon`)
- Modify: `Runtime/Controls/SafeArea.cs` (override `OnAfterApply` to call tracker.Apply)
- Add to: `Tests/EditMode/Controls/SafeAreaTests.cs`

- [ ] **Step 5.1: Write the failing test**

  Append to `SafeAreaTests`:

  ```csharp
  [Test]
  public void SafeArea_anchor_persists_after_ReSolve()
  {
      try
      {
          PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
              () => new UnityEngine.Rect(0f, 100f, 1080f, 1820f);
          PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
              () => new UnityEngine.Vector2(1080f, 1920f);

          const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'><Screen name='S'>
    <SafeArea id='sa'/>
  </Screen></PromptUGUI>";
          UI.LoadDocument("test", xml);
          var screen = UI.Open("S");
          var sa = screen.Get<SafeArea>("sa");

          // ReSolve clobbers anchorMin/Max via ApplyCommon (defaults to top-left).
          // OnAfterApply must restore the safe-area fractions in the same call.
          screen.ReSolve();

          var rt = sa.RectTransform;
          Assert.AreEqual(0f, rt.anchorMin.x, 0.001f);
          Assert.AreEqual(100f / 1920f, rt.anchorMin.y, 0.001f,
              "anchorMin.y should equal safeArea.y / Screen.height after ReSolve");
          Assert.AreEqual(1f, rt.anchorMax.x, 0.001f);
          Assert.AreEqual(1f, rt.anchorMax.y, 0.001f);
      }
      finally
      {
          PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
          PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
      }
  }
  ```

  > **Why no cast needed:** `UI.Open` returns the concrete public `Screen` class (verified at `Runtime/Application/UI.cs:398`), and `Screen.ReSolve()` is public.

- [ ] **Step 5.2: Verify failure**

  ```
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SafeArea_anchor_persists_after_ReSolve")
  ```

  Expected: FAIL. After ReSolve, `anchorMin.y` is 1.0 (top-left default) instead of 0.052.

- [ ] **Step 5.3: Add `OnAfterApply` to Control base**

  In `Runtime/Controls/Control.cs`, immediately after the `OnAttached` declaration (line 39 `public virtual void OnAttached() { }`), insert:

  ```csharp
          /// <summary>
          /// 在 <see cref="ControlAttributeApplier"/> 调用 <see cref="ApplyCommon"/> 之后再触发一次，
          /// 让一些控件在 Variant ReSolve / 初始 Apply 完成后做"恢复其它逻辑写入的 RectTransform / 组件状态"
          /// 这类收尾。默认实现为空；目前只有 SafeArea 重写。
          /// </summary>
          internal virtual void OnAfterApply() { }
  ```

- [ ] **Step 5.4: Wire `OnAfterApply` into ControlAttributeApplier**

  In `Runtime/Application/ControlAttributeApplier.cs`, at the end of the `Apply` method (immediately after `control.ApplyCommon(...)` on line 68), add:

  ```csharp
              control.OnAfterApply();
  ```

  The method body's final lines should read:

  ```csharp
              control.ApplyCommon(anchor, size, width, height, margin, pivot, hidden, interactable);
              control.OnAfterApply();
          }
  ```

- [ ] **Step 5.5: Override `OnAfterApply` in SafeArea**

  Replace `Runtime/Controls/SafeArea.cs`:

  ```csharp
  using PromptUGUI.Controls.Internal;

  namespace PromptUGUI.Controls
  {
      public sealed class SafeArea : Control
      {
          private SafeAreaTracker _tracker;

          public override void OnAttached()
          {
              _tracker = GameObject.AddComponent<SafeAreaTracker>();
          }

          internal override void OnAfterApply()
          {
              // ApplyCommon 在初次实例化和 Variant ReSolve 时都会重写 anchorMin/Max,
              // tracker 必须立刻补一次，否则 ReSolve 后会有一帧 stretch-default 闪烁。
              if (_tracker != null) _tracker.Apply();
          }
      }
  }
  ```

- [ ] **Step 5.6: Refresh, run all SafeArea tests + a quick regression on every other control**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SafeAreaTests")
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
  ```

  Expected: all SafeAreaTests pass; full EditMode suite stays green (no regression from the new `OnAfterApply` hook on the other 13 controls).

- [ ] **Step 5.7: Commit (ask user)**

  ```bash
  git add Runtime/Controls/Control.cs \
          Runtime/Application/ControlAttributeApplier.cs \
          Runtime/Controls/SafeArea.cs \
          Tests/EditMode/Controls/SafeAreaTests.cs
  git commit -m "$(cat <<'EOF'
  feat: OnAfterApply hook + SafeArea persistence across ReSolve

  Adds an internal virtual Control.OnAfterApply called at the end
  of ControlAttributeApplier.Apply. SafeArea overrides it to push
  the tracker's safe-area anchors back on top of the stretch
  default that ApplyCommon writes during Variant ReSolve. Other
  controls keep the empty default — zero behavioral change.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 6: PlayMode integration tests

**Files:**
- Create: `Tests/PlayMode/Controls/SafeAreaTests.cs`

- [ ] **Step 6.1: Write the PlayMode tests**

  Create `Tests/PlayMode/Controls/SafeAreaTests.cs`:

  ```csharp
  using System.Collections;
  using NUnit.Framework;
  using PromptUGUI.Application;
  using PromptUGUI.Controls;
  using PromptUGUI.Controls.Internal;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityEngine.UI;

  namespace PromptUGUI.Tests.Controls
  {
      public class SafeAreaTests
      {
          [SetUp] public void SetUp() => UI.ResetForTests();
          [TearDown] public void TearDown()
          {
              SafeAreaTracker.SafeAreaOverride = null;
              SafeAreaTracker.ScreenSizeOverride = null;
              UI.ResetForTests();
          }

          [UnityTest]
          public IEnumerator Children_inherit_safe_area_rect_after_layout()
          {
              SafeAreaTracker.SafeAreaOverride =
                  () => new Rect(0f, 100f, 1080f, 1820f);
              SafeAreaTracker.ScreenSizeOverride =
                  () => new Vector2(1080f, 1920f);

              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'><Screen name='S'>
    <SafeArea id='sa'>
      <Frame id='inner' anchor='stretch'/>
    </SafeArea>
  </Screen></PromptUGUI>";
              UI.LoadDocument("test", xml);
              var screen = UI.Open("S");
              var sa = screen.Get<SafeArea>("sa");
              var inner = screen.Get<Frame>("sa/inner");

              // Force the canvas to drive a known parent rect so anchor fractions resolve to a checkable size.
              var canvasRt = (RectTransform)sa.RectTransform.parent;
              canvasRt.anchorMin = Vector2.zero;
              canvasRt.anchorMax = Vector2.one;
              canvasRt.offsetMin = Vector2.zero;
              canvasRt.offsetMax = Vector2.zero;
              canvasRt.sizeDelta = new Vector2(1080f, 1920f);

              LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRt);
              yield return null;

              var innerRect = inner.RectTransform.rect;
              Assert.AreEqual(1080f, innerRect.width, 1f);
              Assert.AreEqual(1820f, innerRect.height, 1f);
          }

          [UnityTest]
          public IEnumerator Tracker_reapplies_when_provider_changes_via_dimensions_event()
          {
              SafeAreaTracker.SafeAreaOverride =
                  () => new Rect(0f, 100f, 1080f, 1820f);
              SafeAreaTracker.ScreenSizeOverride =
                  () => new Vector2(1080f, 1920f);

              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'><Screen name='S'>
    <SafeArea id='sa'/>
  </Screen></PromptUGUI>";
              UI.LoadDocument("test", xml);
              var screen = UI.Open("S");
              var sa = screen.Get<SafeArea>("sa");
              var rt = sa.RectTransform;
              yield return null;

              Assert.AreEqual(100f / 1920f, rt.anchorMin.y, 0.001f);

              // Swap the simulated device: notch moves to bottom (gesture bar style).
              SafeAreaTracker.SafeAreaOverride =
                  () => new Rect(0f, 0f, 1080f, 1830f);
              SafeAreaTracker.ScreenSizeOverride =
                  () => new Vector2(1080f, 1920f);

              // Trigger OnRectTransformDimensionsChange by mutating the parent canvas rect.
              var canvasRt = (RectTransform)rt.parent;
              canvasRt.sizeDelta = new Vector2(1080f, 1921f); // any change re-fires the magic method
              yield return null;
              canvasRt.sizeDelta = new Vector2(1080f, 1920f);
              yield return null;

              Assert.AreEqual(0f, rt.anchorMin.y, 0.001f, "new safe area starts at y=0");
              Assert.AreEqual(1830f / 1920f, rt.anchorMax.y, 0.001f);
          }

          [UnityTest]
          public IEnumerator SafeArea_inside_variant_add_block_works_after_toggle()
          {
              SafeAreaTracker.SafeAreaOverride =
                  () => new Rect(0f, 100f, 1080f, 1820f);
              SafeAreaTracker.ScreenSizeOverride =
                  () => new Vector2(1080f, 1920f);

              const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'><Screen name='S'>
    <Variant when='mobile'>
      <Add>
        <SafeArea id='sa'/>
      </Add>
    </Variant>
  </Screen></PromptUGUI>";
              UI.LoadDocument("test", xml);
              var screen = UI.Open("S");
              screen.Variants.SetActive("mobile", true);
              yield return null;

              var sa = screen.Get<SafeArea>("sa");
              Assert.IsNotNull(sa);
              Assert.AreEqual(100f / 1920f, sa.RectTransform.anchorMin.y, 0.001f);

              screen.Variants.SetActive("mobile", false);
              yield return null;
              Assert.IsFalse(sa.GameObject.activeSelf, "Add block goes inactive");

              screen.Variants.SetActive("mobile", true);
              yield return null;
              Assert.IsTrue(sa.GameObject.activeSelf);
              Assert.AreEqual(100f / 1920f, sa.RectTransform.anchorMin.y, 0.001f,
                  "tracker re-applies after reactivation");
          }
      }
  }
  ```

  > **Note on Variants API:** the third test calls `screen.Variants.SetActive(name, bool)`. If the actual method name differs (e.g. it returns or takes a different signature), check `Runtime/Application/VariantStore.cs` and adjust — but the activation semantics are spec'd in §8 of the master spec.

- [ ] **Step 6.2: Run PlayMode tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="SafeAreaTests")
  ```

  Expected: 3 PlayMode tests pass. If `Children_inherit_safe_area_rect_after_layout` fails because the canvas parent isn't what we expected, inspect via:

  ```
  mcp__UnityMCP__read_console(action="get", types=["error", "warning"])
  ```

  Most common failure modes:
  - `sa.RectTransform.parent` isn't the canvas RectTransform — Screen may wrap in another container. Adjust by walking up `transform.parent` until you hit the `Canvas` component, then resize THAT RectTransform.
  - Variant `Add` block in test 3 doesn't activate — confirm the `SetActive` signature against `VariantStore.cs`.

- [ ] **Step 6.3: Commit (ask user)**

  ```bash
  git add Tests/PlayMode/Controls/SafeAreaTests.cs \
          Tests/PlayMode/Controls/SafeAreaTests.cs.meta
  git commit -m "$(cat <<'EOF'
  test(playmode): SafeArea integration coverage

  Real-canvas layout, dimensions-change reapply, and variant Add
  block round-trip.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 7: Author-facing documentation

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md` (add `## Safe area` section)
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` (append note in §5 referencing the new design doc)

- [ ] **Step 7.1: Locate insertion point in SKILL.md**

  Open `.claude/skills/authoring-promptugui-xml/SKILL.md` and find the existing "Built-in primitives" / "Controls overview" section (whichever lists `Frame`, `Image`, etc.). The new `## Safe area` section should land immediately after the built-in primitives list — readers see the catalog first, then learn about SafeArea as a special-purpose container.

- [ ] **Step 7.2: Append the SKILL section**

  Insert:

  ````markdown
  ## Safe area

  Mobile devices have unsafe insets — notch, status bar, home indicator, gesture
  bar. Wrap your UI in `<SafeArea>` to stay inside `Screen.safeArea`. Backgrounds
  that should bleed to the device edges stay outside, as siblings of `<SafeArea>`:

  ```xml
  <Screen name="Login">
    <Image id="bg" anchor="stretch" color="#0B1828"/>
    <SafeArea>
      <HStack id="brandBar" anchor="top-left" width="320" height="56" margin="24,_,_,24">
        ...
      </HStack>
    </SafeArea>
  </Screen>
  ```

  Rules:

  - `<SafeArea>` is always stretched to the safe area; it does **not** accept
    `anchor`, `size`, `width`, `height`, `margin`, or `pivot` (including their
    `.variant` override forms). Writing any of those is a parse error.
  - To add inner padding inside the safe area, wrap content in
    `<Frame anchor="stretch" margin="..."/>` *inside* the `<SafeArea>`.
  - Place `<SafeArea>` as a direct child of `<Screen>`. Nesting another
    `<SafeArea>` inside one is harmless but redundant (the inner one collapses
    to the outer one's rect).
  - Don't put `<SafeArea>` inside `<VStack>` / `<HStack>` / `<Grid>` — the
    layout group will override its anchor math.
  - Reacts automatically to screen rotation, window resize, and Unity 6's
    Device Simulator. No code-side wiring needed.
  ````

- [ ] **Step 7.3: Update master spec §5**

  Open `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`. Find the end of §5 (the last subsection before §6). Append a brief subsection or note:

  ```markdown
  ### 5.5 `<SafeArea>` 安全区容器

  作为显式安全区包裹层；语义、运行时和测试约定详见
  [`2026-05-13-safearea-builtin-design.md`](2026-05-13-safearea-builtin-design.md)。
  禁止 `anchor` / `size` / `width` / `height` / `margin` / `pivot`（含 `.variant` 覆盖），
  几何完全由 `Screen.safeArea` 驱动。
  ```

  > **If §5 already has a 5.4 final subsection**, append as §5.5. If the numbering pattern looks different in the latest master spec, follow that pattern instead — the goal is a one-paragraph pointer from the master spec into this design doc.

- [ ] **Step 7.4: Commit (ask user)**

  ```bash
  git add .claude/skills/authoring-promptugui-xml/SKILL.md \
          docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md
  git commit -m "$(cat <<'EOF'
  doc: SafeArea author guide + master spec pointer

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 8: Lint + full test sweep + Editor manual verify

- [ ] **Step 8.1: Run lint**

  ```bash
  cd /workspace-PromptUGUI/.lint
  dotnet format whitespace PromptUGUI.Lint.slnx
  dotnet format style PromptUGUI.Lint.slnx
  dotnet format analyzers PromptUGUI.Lint.slnx
  dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  Expected: exit 0 on the verify step. If a `style`/`analyzers` pass *changes* files, review the diff carefully (per CLAUDE.md, `info`-severity analyzer fixes can break Unity reflection contracts — but `warn`-level should be safe).

- [ ] **Step 8.2: Run all EditMode assemblies**

  ```
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
  ```

  Expected: all green. Pay attention to `XsdGeneratorTests` — the reflection-driven generator should now include `SafeArea`. If a snapshot/substring assertion needs updating (per CLAUDE.md these use `StringAssert.Contains` not byte-exact), do it as the smallest possible follow-up commit.

- [ ] **Step 8.3: Run all PlayMode tests**

  ```
  mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
  ```

  Expected: all green.

- [ ] **Step 8.4: Manual Device Simulator check (host project)**

  Tell the user to open the host project (`C:\xsoft\PromptUGUIDev`), pick a Screen with a `<SafeArea>` content (suggest editing a demo XML to add one if none exists), and toggle between iPhone 14 Pro / iPad / Pixel in Window → Game → Device Simulator. Confirm SafeArea content avoids notch and home indicator.

  This is a **manual** step — log a confirmation comment in the PR description rather than an automated assertion.

- [ ] **Step 8.5: Lint commit if needed**

  If `dotnet format` made changes:

  ```bash
  git add -A
  git commit -m "$(cat <<'EOF'
  chore: dotnet format pass after SafeArea landing

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

- [ ] **Step 8.6: Final summary to user**

  Report:
  - Files added/modified (count)
  - Tests added (EditMode + PlayMode counts)
  - Console clean / all suites green
  - Any deferred items (e.g., XSD generator per-tag deny-list — see spec §6 risk row; if generator works fine with the existing wide-attr schema, this remains "not needed for v1")

---

## Notes / Reminders

- **No commits without explicit user OK.** CLAUDE.md governs; ask each task's commit step.
- **UnityMCP only for tests.** Don't shell out to `Unity -batchmode`. If MCP loses connection, ask the user to reconnect (per CLAUDE.md).
- **Refresh before reading console.** After any source edit run `refresh_unity` then `read_console action=get types=["error"]` before running tests.
- **Don't run `Assets/Reimport All`** via `execute_menu_item` — it pops a modal that blocks MCP (CLAUDE.md forbidden list).
- **Strategy C for Add blocks** (spec'd in CLAUDE.md): the third PlayMode test in Task 6 relies on Add-block instantiate-once + SetActive toggling. If reactivation drops the tracker, that's an Add-block bug to triage separately, not a SafeArea bug.
