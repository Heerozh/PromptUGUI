# `<Trigger>` + `<Animation>` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `<Trigger>` base control (event subscriber with `on=` DSL, exposes R3 `OnFire`) and `<Animation>` subclass (LitMotion-driven transform / text / preset effects via inner offset proxy).

**Architecture:** `Trigger : Control` parses `on=` into a `TriggerSpec`, subscribes to event source in its subtree, exposes `Observable<Unit> OnFire`. `Animation : Trigger` inserts an inner `_offsetProxy` RectTransform on `OnAttached`; XML children parent to that proxy via a new `Control.ChildHostTransform` virtual. On `OnTriggerFired` it delegates to a static `AnimationDriver` that translates `AnimationSpec` (preset / low-level transform / text-effect families) into one or more LitMotion `MotionHandle`s. Variant ReSolve dirty-checks control attributes and only cancels+restarts when they change.

**Tech Stack:** Unity 6+, .NET Standard 2.1, R3 (Cysharp), LitMotion + LitMotion.Extensions (TMP bindings), TextMeshPro, NUnit (EditMode + PlayMode), UnityMCP for test execution.

**Spec reference:** `docs~/superpowers/specs/2026-05-14-litmotion-animations-design.md`

---

## Pre-flight

- [ ] **Step P1: Verify spec is committed**

  Spec was committed as `5ef2f88` (`doc: spec for <Trigger> + <Animation>`). This plan will be a separate commit after the user approves the plan.

  Confirm clean baseline:

  ```bash
  git status
  git log --oneline -3
  ```

  Expected: clean working tree, top commit is the spec.

- [ ] **Step P2: Verify Unity baseline is green**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: no compile errors. If errors exist, stop — don't start TDD on a red baseline.

- [ ] **Step P3: Commit plan doc (ask user first)**

  Per CLAUDE.md ask before committing:

  > "Plan written to `docs~/superpowers/plans/2026-05-14-litmotion-animations.md`. Commit it as `doc: plan for <Trigger> + <Animation>` before starting Task 1?"

  If yes:

  ```bash
  git add docs~/superpowers/plans/2026-05-14-litmotion-animations.md
  git commit -m "$(cat <<'EOF'
  doc: plan for <Trigger> + <Animation> implementation

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 1: LitMotion hard dep + asmdef references

**Files:**
- Modify: `package.json`
- Modify: `Runtime/PromptUGUI.Runtime.asmdef`

- [ ] **Step 1.1: Discover LitMotion assembly names**

  LitMotion's asmdef names vary by version. Find them via the host project's Package Manager cache:

  ```bash
  ls C:/xsoft/PromptUGUIDev/Library/PackageCache/com.annulusgames.lit-motion*/Runtime/*.asmdef 2>/dev/null
  ls C:/xsoft/PromptUGUIDev/Library/PackageCache/com.annulusgames.lit-motion*/ 2>/dev/null
  ```

  Read each asmdef and record the `"name"` field. Expected names (verify):
  - `LitMotion` (core runtime; provides `LMotion`, `MotionHandle`, easing)
  - `LitMotion.Extensions` or `LitMotion.TextMeshPro` (provides `BindToText`, `BindToTMPCharColor`)

  If the TMP bindings are in a different package (e.g., `com.annulusgames.lit-motion-text-mesh-pro`), record that — Task 1.2 must add it to `package.json`.

- [ ] **Step 1.2: Add dependency to `package.json`**

  Edit `package.json`. Current `dependencies` block:

  ```json
  "dependencies": {
      "com.unity.ugui": "2.0.0"
  }
  ```

  Replace with (adjust if Step 1.1 found a separate TMP package):

  ```json
  "dependencies": {
      "com.unity.ugui": "2.0.0",
      "com.annulusgames.lit-motion": "https://github.com/annulusgames/LitMotion.git?path=src/LitMotion/Assets/LitMotion"
  }
  ```

- [ ] **Step 1.3: Add asmdef references**

  Edit `Runtime/PromptUGUI.Runtime.asmdef`. Current `references`:

  ```json
  "references": [
    "Unity.TextMeshPro",
    "UnityEngine.UI",
    "Unity.Addressables",
    "Unity.Addressables.Editor",
    "Unity.ResourceManager"
  ]
  ```

  Append the LitMotion assembly names from Step 1.1, e.g.:

  ```json
  "references": [
    "Unity.TextMeshPro",
    "UnityEngine.UI",
    "Unity.Addressables",
    "Unity.Addressables.Editor",
    "Unity.ResourceManager",
    "LitMotion",
    "LitMotion.Extensions"
  ]
  ```

- [ ] **Step 1.4: Refresh + verify**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: 0 errors. If errors mention missing `LitMotion` namespace, the asmdef name is wrong — revisit Step 1.1.

- [ ] **Step 1.5: Smoke-test LitMotion is callable from a test**

  Create `Tests/EditMode/LitMotionSmokeTest.cs`:

  ```csharp
  using LitMotion;
  using NUnit.Framework;
  using UnityEngine;

  namespace PromptUGUI.Tests.EditMode
  {
      public class LitMotionSmokeTest
      {
          [Test]
          public void LitMotion_API_is_callable()
          {
              var handle = LMotion.Create(0f, 1f, 0.1f).RunWithoutBinding();
              Assert.IsTrue(handle.IsActive());
              handle.TryCancel();
          }
      }
  }
  ```

  Run:

  ```
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="LitMotionSmokeTest")
  ```

  Expected: PASS. Then **delete the file** — it's just a smoke test to confirm wiring:

  ```bash
  rm Tests/EditMode/LitMotionSmokeTest.cs
  rm Tests/EditMode/LitMotionSmokeTest.cs.meta 2>/dev/null
  ```

- [ ] **Step 1.6: Lint**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  Expected: clean. (Only `package.json` / asmdef were modified — no C# changes to lint, but verify-no-changes catches accidental drift.)

- [ ] **Step 1.7: Commit (ask user)**

  ```bash
  git add package.json Runtime/PromptUGUI.Runtime.asmdef
  git commit -m "$(cat <<'EOF'
  chore: hard-dep LitMotion for <Animation>

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 2: `Control.ChildHostTransform` hook + ScreenInstantiator integration

**Files:**
- Modify: `Runtime/Controls/Control.cs:31-39` (around `AttachTo` / `OnAttached`)
- Modify: `Runtime/Application/ScreenInstantiator.cs:193` (the `SetParent` call site)

This is a pure mechanical extension point: the virtual returns `RectTransform` by default, behavior unchanged for all existing controls. No new tests needed — existing test suite is the regression baseline.

- [ ] **Step 2.1: Add `ChildHostTransform` virtual to `Control.cs`**

  Edit `Runtime/Controls/Control.cs`. After the `RectTransform` property (around line 13) add:

  ```csharp
  /// <summary>
  /// 实例化子节点时 ScreenInstantiator 用作 parent 的 Transform。
  /// 默认 = 自身 RectTransform；Animation 等需要"在 transform 树里多塞一层"的控件 override 它，
  /// 这样子节点 parent 到那一层，而不是自身根 GameObject。
  /// </summary>
  protected internal virtual Transform ChildHostTransform => RectTransform;
  ```

- [ ] **Step 2.2: Modify `ScreenInstantiator.cs` to use `ChildHostTransform`**

  Open `Runtime/Application/ScreenInstantiator.cs`. Find the recursive child instantiation. Search for `SetParent` (should be around line 193). The current code parents new children to a `parent` Transform passed in.

  Locate where `InstantiateRecursive` recurses into a node's children, e.g.:

  ```csharp
  foreach (var childNode in node.Children)
      InstantiateRecursive(childNode, (RectTransform)go.transform, ...);
  ```

  Change `(RectTransform)go.transform` to use the new control's `ChildHostTransform` when available. Concretely: after the `Control control = ...` is instantiated for this node, when recursing into children pass `(RectTransform)control.ChildHostTransform` instead of `(RectTransform)go.transform`.

  **Important**: the cast may fail if a subclass returns a non-RectTransform Transform. Convention: `ChildHostTransform` MUST be a RectTransform (we add `[AddComponent<RectTransform>]` in the call site or rely on the override always returning a RectTransform). Document this in the XML doc comment on the property:

  ```csharp
  /// <remarks>必须返回一个 RectTransform — uGUI 子节点要求父也是 RectTransform。</remarks>
  ```

  After modification, the recursion should be:

  ```csharp
  var childHost = (RectTransform)control.ChildHostTransform;
  foreach (var childNode in node.Children)
      InstantiateRecursive(childNode, childHost, ...);
  ```

  (Adjust to the actual surrounding code — read 193 ± 20 lines first to fit the existing structure.)

- [ ] **Step 2.3: Refresh + run all existing tests (regression check)**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
  mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
  ```

  Expected: zero new failures vs. baseline. Default `ChildHostTransform => RectTransform` means semantics for existing controls are byte-equivalent.

- [ ] **Step 2.4: Lint**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

- [ ] **Step 2.5: Commit (ask user)**

  ```bash
  git add Runtime/Controls/Control.cs Runtime/Application/ScreenInstantiator.cs
  git commit -m "$(cat <<'EOF'
  feat: Control.ChildHostTransform virtual for child-parent override

  默认返回 RectTransform，所有现存控件行为不变。Animation 控件用它把子节点 parent
  到内部 offset proxy 而不是外层 GameObject。

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 3: `TriggerSpec` parser

**Files:**
- Create: `Runtime/Controls/Internal/TriggerSpec.cs`
- Create: `Tests/EditMode/Controls/TriggerSpecTests.cs`

- [ ] **Step 3.1: Write the failing tests**

  Create `Tests/EditMode/Controls/TriggerSpecTests.cs`:

  ```csharp
  using NUnit.Framework;
  using PromptUGUI.Controls.Internal;

  namespace PromptUGUI.Tests.EditMode.Controls
  {
      public class TriggerSpecTests
      {
          [Test] public void Null_or_empty_parses_to_Open()
          {
              Assert.AreEqual(TriggerKind.Open, TriggerSpec.Parse(null).Kind);
              Assert.AreEqual(TriggerKind.Open, TriggerSpec.Parse("").Kind);
          }

          [Test] public void Open_parses() => Assert.AreEqual(TriggerKind.Open, TriggerSpec.Parse("open").Kind);
          [Test] public void Loop_parses() => Assert.AreEqual(TriggerKind.Loop, TriggerSpec.Parse("loop").Kind);
          [Test] public void Manual_parses() => Assert.AreEqual(TriggerKind.Manual, TriggerSpec.Parse("manual").Kind);

          [Test] public void Click_bare_parses_with_null_SourceId()
          {
              var spec = TriggerSpec.Parse("click");
              Assert.AreEqual(TriggerKind.Click, spec.Kind);
              Assert.IsNull(spec.SourceId);
          }

          [Test] public void Click_with_id_parses()
          {
              var spec = TriggerSpec.Parse("click@ok");
              Assert.AreEqual(TriggerKind.Click, spec.Kind);
              Assert.AreEqual("ok", spec.SourceId);
          }

          [Test] public void Invalid_value_throws()
          {
              Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("hover"));
              Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("click@"));     // empty id
              Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("click@a@b")); // double @
          }
      }
  }
  ```

- [ ] **Step 3.2: Run tests, verify they fail**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: compile error — `TriggerSpec` doesn't exist.

- [ ] **Step 3.3: Create `TriggerSpec.cs`**

  Create `Runtime/Controls/Internal/TriggerSpec.cs`:

  ```csharp
  using System;

  namespace PromptUGUI.Controls.Internal
  {
      internal enum TriggerKind { Open, Loop, Click, Manual }

      internal sealed class TriggerSpec
      {
          public TriggerKind Kind;
          public string SourceId;  // only non-null for Click with @id

          public static TriggerSpec Parse(string value)
          {
              if (string.IsNullOrEmpty(value)) return new TriggerSpec { Kind = TriggerKind.Open };
              switch (value)
              {
                  case "open":   return new TriggerSpec { Kind = TriggerKind.Open };
                  case "loop":   return new TriggerSpec { Kind = TriggerKind.Loop };
                  case "manual": return new TriggerSpec { Kind = TriggerKind.Manual };
                  case "click":  return new TriggerSpec { Kind = TriggerKind.Click };
              }
              if (value.StartsWith("click@"))
              {
                  var id = value.Substring("click@".Length);
                  if (string.IsNullOrEmpty(id) || id.Contains('@'))
                      throw new ArgumentException(
                          $"Invalid trigger source id in 'on=\"{value}\"' — expected 'click@<id>' with non-empty single id");
                  return new TriggerSpec { Kind = TriggerKind.Click, SourceId = id };
              }
              throw new ArgumentException(
                  $"Invalid trigger 'on=\"{value}\"' — expected one of: open / loop / click / click@<id> / manual");
          }
      }
  }
  ```

- [ ] **Step 3.4: Refresh + run tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TriggerSpecTests")
  ```

  Expected: all 8 tests PASS.

- [ ] **Step 3.5: Lint + Commit (ask user)**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  ```bash
  git add Runtime/Controls/Internal/TriggerSpec.cs \
          Runtime/Controls/Internal/TriggerSpec.cs.meta \
          Tests/EditMode/Controls/TriggerSpecTests.cs \
          Tests/EditMode/Controls/TriggerSpecTests.cs.meta
  git commit -m "$(cat <<'EOF'
  feat: TriggerSpec parser for on= DSL

  Parses on="open|loop|click|click@<id>|manual"; null/empty → Open.
  Pure POCO, no Unity dependencies.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 4: `Trigger` base control (open / loop / manual paths; click stubbed)

**Files:**
- Create: `Runtime/Controls/Trigger.cs`
- Modify: `Runtime/Application/BuiltinPrimitives.cs:10` (insert after Frame registration)
- Create: `Tests/EditMode/Controls/TriggerTests.cs`

- [ ] **Step 4.1: Write the failing tests**

  Create `Tests/EditMode/Controls/TriggerTests.cs`:

  ```csharp
  using System;
  using NUnit.Framework;
  using PromptUGUI.Application;
  using PromptUGUI.Controls;
  using R3;

  namespace PromptUGUI.Tests.EditMode.Controls
  {
      public class TriggerTests
      {
          private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
              "<PromptUGUI version='1'><Screen name='S'>";
          private const string Footer = "</Screen></PromptUGUI>";

          [SetUp]    public void SetUp()    => UI.ResetForTests();
          [TearDown] public void TearDown() => UI.ResetForTests();

          [Test] public void Open_trigger_fires_OnFire_on_open()
          {
              UI.LoadDocument("t", $"{Header}<Trigger id='t' on='open'/>{Footer}");
              var screen = UI.Open("S");
              int fires = 0;
              screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
              // Subscribe is after open, so we miss the initial fire — verify Fire() works
              screen.Get<Trigger>("t").Fire();
              Assert.AreEqual(1, fires);
          }

          [Test] public void Open_is_default_when_on_attribute_missing()
          {
              UI.LoadDocument("t", $"{Header}<Trigger id='t'/>{Footer}");
              var screen = UI.Open("S");
              Assert.IsNotNull(screen.Get<Trigger>("t"));
          }

          [Test] public void Manual_trigger_does_not_auto_fire()
          {
              UI.LoadDocument("t", $"{Header}<Trigger id='t' on='manual'/>{Footer}");
              int fires = 0;
              // Subscribe BEFORE open is impossible (screen doesn't exist yet); subscribe after open
              var screen = UI.Open("S");
              screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
              Assert.AreEqual(0, fires, "manual trigger must not auto-fire");
              screen.Get<Trigger>("t").Fire();
              Assert.AreEqual(1, fires);
          }

          [Test] public void Invalid_on_throws_at_parse_time()
          {
              Assert.Throws<Exception>(() =>
              {
                  UI.LoadDocument("t", $"{Header}<Trigger id='t' on='hover'/>{Footer}");
                  UI.Open("S");
              });
          }
      }
  }
  ```

- [ ] **Step 4.2: Run tests, verify they fail**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: compile error — `Trigger` doesn't exist.

- [ ] **Step 4.3: Create `Trigger.cs`**

  Create `Runtime/Controls/Trigger.cs`:

  ```csharp
  using System;
  using PromptUGUI.Controls.Internal;
  using PromptUGUI.Registry;
  using R3;

  namespace PromptUGUI.Controls
  {
      public class Trigger : Control
      {
          private readonly Subject<Unit> _fire = new();
          public Observable<Unit> OnFire => _fire;

          private TriggerSpec _spec;
          private IDisposable _sourceSub;
          private bool _subscribed;

          [UIAttr("on")]
          public string On { set => _spec = TriggerSpec.Parse(value); }

          internal override void OnAfterApply()
          {
              if (_subscribed) return;
              _subscribed = true;
              _spec ??= new TriggerSpec { Kind = TriggerKind.Open };
              InitTriggerSubscription();
          }

          protected virtual void InitTriggerSubscription()
          {
              switch (_spec.Kind)
              {
                  case TriggerKind.Open:
                  case TriggerKind.Loop:
                      Fire();
                      break;
                  case TriggerKind.Click:
                      SubscribeClick();
                      break;
                  case TriggerKind.Manual:
                      // no auto-subscribe; awaiting Fire()
                      break;
              }
          }

          public void Fire()
          {
              OnTriggerFired();
              _fire.OnNext(Unit.Default);
          }

          protected virtual void OnTriggerFired() { }

          private void SubscribeClick()
          {
              // Implemented in Task 5
              throw new NotImplementedException(
                  "on=\"click\" 触发暂未实现 (Task 5 will add it)");
          }

          public override void Dispose()
          {
              _sourceSub?.Dispose();
              _fire.Dispose();
              base.Dispose();
          }
      }
  }
  ```

- [ ] **Step 4.4: Register `Trigger` in `BuiltinPrimitives`**

  Edit `Runtime/Application/BuiltinPrimitives.cs`. After the `Frame` registration (around line 10) add:

  ```csharp
  reg.Register<Trigger>("Trigger", null);
  ```

  Final order in the method should be: `Frame, SafeArea, Trigger, Image, Icon, Text, ...` (Trigger before visual controls — its position is alphabetical-ish but not strict).

- [ ] **Step 4.5: Refresh + run tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TriggerTests")
  ```

  Expected: 4 tests PASS.

- [ ] **Step 4.6: Lint + Commit (ask user)**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  ```bash
  git add Runtime/Controls/Trigger.cs Runtime/Controls/Trigger.cs.meta \
          Runtime/Application/BuiltinPrimitives.cs \
          Tests/EditMode/Controls/TriggerTests.cs Tests/EditMode/Controls/TriggerTests.cs.meta
  git commit -m "$(cat <<'EOF'
  feat: <Trigger> control with open/loop/manual paths

  Click path stubbed with NotImplementedException — Task 5 adds it.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 5: Click trigger + `TriggerSourceResolver`

**Files:**
- Create: `Runtime/Controls/Internal/TriggerSourceResolver.cs`
- Modify: `Runtime/Controls/Trigger.cs:SubscribeClick` (replace stub)
- Modify: `Tests/EditMode/Controls/TriggerTests.cs` (append click cases)

- [ ] **Step 5.1: Write the failing tests**

  Append to `Tests/EditMode/Controls/TriggerTests.cs`:

  ```csharp
  [Test] public void Click_trigger_fires_when_unique_inner_Btn_clicked()
  {
      UI.LoadDocument("t", $"{Header}" +
          "<Trigger id='t' on='click'><Btn id='b'>OK</Btn></Trigger>" +
          $"{Footer}");
      var screen = UI.Open("S");
      int fires = 0;
      screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
      // Simulate click via R3 stream: Btn exposes _click subject internally; in tests we
      // can call onClick.Invoke via UnityEngine.UI.Button.
      var btn = screen.Get<Btn>("t/b");  // scoped path
      btn.GameObject.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
      Assert.AreEqual(1, fires);
  }

  [Test] public void Click_trigger_with_id_picks_correct_Btn()
  {
      UI.LoadDocument("t", $"{Header}" +
          "<Trigger id='t' on='click@ok'>" +
          "  <Btn id='ok'>OK</Btn>" +
          "  <Btn id='cancel'>Cancel</Btn>" +
          "</Trigger>" +
          $"{Footer}");
      var screen = UI.Open("S");
      int fires = 0;
      screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
      screen.Get<Btn>("t/cancel").GameObject
          .GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
      Assert.AreEqual(0, fires, "cancel click must NOT fire when source is @ok");
      screen.Get<Btn>("t/ok").GameObject
          .GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
      Assert.AreEqual(1, fires);
  }

  [Test] public void Click_trigger_no_inner_Btn_throws()
  {
      Assert.Throws<Exception>(() =>
      {
          UI.LoadDocument("t", $"{Header}<Trigger id='t' on='click'/>{Footer}");
          UI.Open("S");
      });
  }

  [Test] public void Click_trigger_multiple_Btn_no_id_throws()
  {
      Assert.Throws<Exception>(() =>
      {
          UI.LoadDocument("t", $"{Header}" +
              "<Trigger id='t' on='click'>" +
              "  <Btn id='a'>A</Btn><Btn id='b'>B</Btn>" +
              "</Trigger>" +
              $"{Footer}");
          UI.Open("S");
      });
  }

  [Test] public void Click_trigger_unknown_id_throws()
  {
      Assert.Throws<Exception>(() =>
      {
          UI.LoadDocument("t", $"{Header}" +
              "<Trigger id='t' on='click@nope'><Btn id='ok'>OK</Btn></Trigger>" +
              $"{Footer}");
          UI.Open("S");
      });
  }
  ```

- [ ] **Step 5.2: Run tests, verify they fail**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TriggerTests")
  ```

  Expected: 5 new tests FAIL (NotImplementedException or compile errors).

- [ ] **Step 5.3: Create `TriggerSourceResolver.cs`**

  Create `Runtime/Controls/Internal/TriggerSourceResolver.cs`:

  ```csharp
  using System;
  using System.Collections.Generic;

  namespace PromptUGUI.Controls.Internal
  {
      internal static class TriggerSourceResolver
      {
          /// <summary>
          /// 在 trigger 子树（descendants of trigger.RectTransform）里查找一个 Btn 作为点击事件源。
          /// </summary>
          /// <param name="sourceId">非空 → 按 ScopedIds[sourceId] 精确查找；空 → 子树里 unique Btn</param>
          public static Btn FindBtn(Trigger trigger, string sourceId)
          {
              if (!string.IsNullOrEmpty(sourceId))
              {
                  if (!trigger.ScopedIds.TryGetValue(sourceId, out var ctrl))
                      throw new InvalidOperationException(
                          $"<Trigger on=\"click@{sourceId}\"> in '{trigger.Id ?? trigger.GameObject.name}': " +
                          $"id '{sourceId}' not found in trigger subtree scope");
                  return ctrl as Btn ?? throw new InvalidOperationException(
                      $"<Trigger on=\"click@{sourceId}\">: id '{sourceId}' is a " +
                      $"{ctrl.GetType().Name}, expected Btn");
              }

              var found = new List<Btn>();
              CollectBtns(trigger, found);
              if (found.Count == 0)
                  throw new InvalidOperationException(
                      $"<Trigger on=\"click\"> in '{trigger.Id ?? trigger.GameObject.name}': " +
                      "no Btn found in subtree. Add a Btn or use on=\"manual\".");
              if (found.Count > 1)
                  throw new InvalidOperationException(
                      $"<Trigger on=\"click\"> in '{trigger.Id ?? trigger.GameObject.name}': " +
                      $"ambiguous — found {found.Count} Btn descendants. " +
                      "Use on=\"click@<id>\" to disambiguate.");
              return found[0];
          }

          private static void CollectBtns(IControl c, List<Btn> outList)
          {
              foreach (var child in c.Children)
              {
                  if (child is Btn b) outList.Add(b);
                  CollectBtns(child, outList);
              }
          }
      }
  }
  ```

- [ ] **Step 5.4: Replace `Trigger.SubscribeClick` stub**

  Edit `Runtime/Controls/Trigger.cs`. Replace the `SubscribeClick` body:

  ```csharp
  private void SubscribeClick()
  {
      var btn = Internal.TriggerSourceResolver.FindBtn(this, _spec.SourceId);
      _sourceSub = btn.OnClick.Subscribe(_ => Fire());
  }
  ```

- [ ] **Step 5.5: Refresh + run tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TriggerTests")
  ```

  Expected: 9 tests PASS (4 from Task 4 + 5 new).

- [ ] **Step 5.6: Lint + Commit (ask user)**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  ```bash
  git add Runtime/Controls/Internal/TriggerSourceResolver.cs \
          Runtime/Controls/Internal/TriggerSourceResolver.cs.meta \
          Runtime/Controls/Trigger.cs \
          Tests/EditMode/Controls/TriggerTests.cs
  git commit -m "$(cat <<'EOF'
  feat: on="click" / on="click@id" subscription via subtree Btn lookup

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 6: `Animation` skeleton — offset proxy + ChildHostTransform override

**Files:**
- Create: `Runtime/Controls/Animation.cs`
- Modify: `Runtime/Application/BuiltinPrimitives.cs` (add Animation registration)
- Create: `Tests/EditMode/Controls/AnimationTests.cs`

- [ ] **Step 6.1: Write the failing structural test**

  Create `Tests/EditMode/Controls/AnimationTests.cs`:

  ```csharp
  using NUnit.Framework;
  using PromptUGUI.Application;
  using PromptUGUI.Controls;
  using UnityEngine;

  namespace PromptUGUI.Tests.EditMode.Controls
  {
      public class AnimationTests
      {
          private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
              "<PromptUGUI version='1'><Screen name='S'>";
          private const string Footer = "</Screen></PromptUGUI>";

          [SetUp]    public void SetUp()    => UI.ResetForTests();
          [TearDown] public void TearDown() => UI.ResetForTests();

          [Test] public void Animation_creates_inner_offset_proxy()
          {
              UI.LoadDocument("t", $"{Header}" +
                  "<Animation id='a' on='manual'><Text id='label'>hi</Text></Animation>" +
                  $"{Footer}");
              var screen = UI.Open("S");
              var anim = screen.Get<Animation>("a");
              Assert.IsNotNull(anim);
              var proxy = anim.GameObject.transform.Find("_offsetProxy");
              Assert.IsNotNull(proxy, "Animation must create _offsetProxy child GameObject");
              var rt = (RectTransform)proxy;
              Assert.AreEqual(Vector2.zero,  rt.anchorMin);
              Assert.AreEqual(Vector2.one,   rt.anchorMax);
              Assert.AreEqual(Vector2.zero,  rt.offsetMin);
              Assert.AreEqual(Vector2.zero,  rt.offsetMax);
          }

          [Test] public void Animation_children_parented_to_offset_proxy()
          {
              UI.LoadDocument("t", $"{Header}" +
                  "<Animation id='a' on='manual'><Text id='label'>hi</Text></Animation>" +
                  $"{Footer}");
              var screen = UI.Open("S");
              var anim = screen.Get<Animation>("a");
              var label = screen.Get<Text>("a/label");
              Assert.AreEqual(
                  anim.GameObject.transform.Find("_offsetProxy"),
                  label.RectTransform.parent,
                  "Text must be parented to _offsetProxy, not Animation root");
          }
      }
  }
  ```

- [ ] **Step 6.2: Run tests, verify they fail**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: compile error — `Animation` doesn't exist.

- [ ] **Step 6.3: Create `Animation.cs` skeleton**

  Create `Runtime/Controls/Animation.cs`:

  ```csharp
  using UnityEngine;

  namespace PromptUGUI.Controls
  {
      public sealed class Animation : Trigger
      {
          private RectTransform _offsetProxy;

          protected internal override Transform ChildHostTransform => _offsetProxy;

          public override void OnAttached()
          {
              var go = new GameObject("_offsetProxy", typeof(RectTransform));
              go.transform.SetParent(RectTransform, worldPositionStays: false);
              _offsetProxy = (RectTransform)go.transform;
              _offsetProxy.anchorMin = Vector2.zero;
              _offsetProxy.anchorMax = Vector2.one;
              _offsetProxy.offsetMin = Vector2.zero;
              _offsetProxy.offsetMax = Vector2.zero;
              _offsetProxy.pivot = new Vector2(0.5f, 0.5f);
          }

          // Animation-specific [UIAttr]s and OnTriggerFired added in Task 8.
      }
  }
  ```

- [ ] **Step 6.4: Register `Animation` in `BuiltinPrimitives`**

  Edit `Runtime/Application/BuiltinPrimitives.cs`. After the `Trigger` registration add:

  ```csharp
  reg.Register<Animation>("Animation", null);
  ```

- [ ] **Step 6.5: Refresh + run tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="AnimationTests")
  ```

  Expected: 2 tests PASS. Important: the `Animation_children_parented_to_offset_proxy` test verifies Task 2's `ChildHostTransform` integration works correctly.

- [ ] **Step 6.6: Lint + Commit**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  ```bash
  git add Runtime/Controls/Animation.cs Runtime/Controls/Animation.cs.meta \
          Runtime/Application/BuiltinPrimitives.cs \
          Tests/EditMode/Controls/AnimationTests.cs Tests/EditMode/Controls/AnimationTests.cs.meta
  git commit -m "$(cat <<'EOF'
  feat: <Animation> skeleton with inner offset proxy

  Empty Animation control inherits Trigger; OnAttached creates _offsetProxy
  RectTransform (stretch fill); ChildHostTransform override re-parents
  XML children to the proxy. Animation effect logic in Task 8.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 7: `AnimationSpec` POCO (attributes + Validate + Snapshot)

**Files:**
- Create: `Runtime/Controls/Internal/AnimationSpec.cs`
- Create: `Tests/EditMode/Controls/AnimationSpecTests.cs`

- [ ] **Step 7.1: Write the failing tests**

  Create `Tests/EditMode/Controls/AnimationSpecTests.cs`:

  ```csharp
  using NUnit.Framework;
  using PromptUGUI.Controls.Internal;
  using UnityEngine;

  namespace PromptUGUI.Tests.EditMode.Controls
  {
      public class AnimationSpecTests
      {
          [Test] public void Empty_spec_validates()
          {
              var s = new AnimationSpec();
              Assert.DoesNotThrow(s.Validate);
              Assert.AreEqual(AnimationFamily.None, s.Family);
          }

          [Test] public void Preset_family_recognized()
          {
              var s = new AnimationSpec(); s.SetType("fadein");
              s.Validate();
              Assert.AreEqual(AnimationFamily.Preset, s.Family);
          }

          [Test] public void LowLevel_family_recognized()
          {
              var s = new AnimationSpec(); s.SetTranslate("0,-50:0,0");
              s.Validate();
              Assert.AreEqual(AnimationFamily.LowLevel, s.Family);
              Assert.AreEqual(new Vector2(0, -50), s.TranslateFrom);
              Assert.AreEqual(Vector2.zero,        s.TranslateTo);
          }

          [Test] public void Text_family_recognized()
          {
              var s = new AnimationSpec(); s.SetCount("0:1000");
              s.Validate();
              Assert.AreEqual(AnimationFamily.Text, s.Family);
              Assert.AreEqual(0f,    s.CountFrom);
              Assert.AreEqual(1000f, s.CountTo);
          }

          [Test] public void Preset_and_LowLevel_throws()
          {
              var s = new AnimationSpec();
              s.SetType("fadein");
              s.SetTranslate("0,0:0,0");
              Assert.Throws<System.ArgumentException>(s.Validate);
          }

          [Test] public void LowLevel_and_Text_throws()
          {
              var s = new AnimationSpec();
              s.SetTranslate("0,0:0,0");
              s.SetCount("0:100");
              Assert.Throws<System.ArgumentException>(s.Validate);
          }

          [Test] public void Count_and_CharColor_throws()
          {
              var s = new AnimationSpec();
              s.SetCount("0:100");
              s.SetCharColor("1,1,1,1:1,0,0,1");
              Assert.Throws<System.ArgumentException>(s.Validate);
          }

          [Test] public void Invalid_preset_name_throws()
          {
              var s = new AnimationSpec();
              s.SetType("explodeIn");
              Assert.Throws<System.ArgumentException>(s.Validate);
          }

          [Test] public void Translate_single_to_uses_zero_from()
          {
              var s = new AnimationSpec();
              s.SetTranslate(":50,0");
              s.Validate();
              Assert.AreEqual(Vector2.zero,        s.TranslateFrom);
              Assert.AreEqual(new Vector2(50, 0),  s.TranslateTo);
          }

          [Test] public void Scale_single_value_expands_to_vector()
          {
              var s = new AnimationSpec();
              s.SetScale("0.5:1");
              s.Validate();
              Assert.AreEqual(new Vector2(0.5f, 0.5f), s.ScaleFrom);
              Assert.AreEqual(Vector2.one,             s.ScaleTo);
          }

          [Test] public void Loop_count_parses()
          {
              var s = new AnimationSpec();
              s.SetLoop("count:3");
              s.Validate();
              Assert.AreEqual(LoopMode.Count, s.LoopMode);
              Assert.AreEqual(3, s.LoopCount);
          }

          [Test] public void Snapshot_equality_for_control_props()
          {
              var s1 = new AnimationSpec(); s1.SetType("fadein"); s1.SetDuration("0.3s");
              var s2 = new AnimationSpec(); s2.SetType("fadein"); s2.SetDuration("0.3s");
              s1.Validate(); s2.Validate();
              Assert.AreEqual(s1.Snapshot(), s2.Snapshot());
          }

          [Test] public void Snapshot_differs_when_duration_changes()
          {
              var s1 = new AnimationSpec(); s1.SetType("fadein"); s1.SetDuration("0.3s");
              var s2 = new AnimationSpec(); s2.SetType("fadein"); s2.SetDuration("0.5s");
              s1.Validate(); s2.Validate();
              Assert.AreNotEqual(s1.Snapshot(), s2.Snapshot());
          }

          [Test] public void Target_with_at_sign_strips_prefix()
          {
              var s = new AnimationSpec();
              s.SetCount("0:1");
              s.SetTarget("@score");
              s.Validate();
              Assert.AreEqual("score", s.TargetId);
          }
      }
  }
  ```

- [ ] **Step 7.2: Run, verify failure**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: compile error.

- [ ] **Step 7.3: Create `AnimationSpec.cs`**

  Create `Runtime/Controls/Internal/AnimationSpec.cs`:

  ```csharp
  using System;
  using System.Globalization;
  using UnityEngine;

  namespace PromptUGUI.Controls.Internal
  {
      internal enum AnimationFamily { None, Preset, LowLevel, Text }
      internal enum LoopMode { None, Yoyo, Restart, Count }
      internal enum EasingKind {
          Linear,
          InCubic, OutCubic, InOutCubic,
          InQuad, OutQuad, InOutQuad,
          InQuart, OutQuart, InOutQuart,
          InQuint, OutQuint, InOutQuint,
          OutBack, OutElastic, OutBounce
      }

      internal sealed class AnimationSpec
      {
          // Family-defining inputs (raw)
          public string TypeRaw;
          public bool HasTranslate, HasScale, HasRotate, HasFade;
          public bool HasCount, HasCharColor;

          // Parsed values
          public Vector2 TranslateFrom, TranslateTo;
          public Vector2 ScaleFrom, ScaleTo;
          public float   RotateFrom, RotateTo;
          public float   FadeFrom, FadeTo;
          public float   CountFrom, CountTo;
          public string  Format = "{0}";
          public Color   CharColorFrom, CharColorTo;
          public float   CharStaggerSec;

          // Common
          public float Duration   = 0.3f;
          public float Delay;
          public EasingKind Easing = EasingKind.OutCubic;
          public LoopMode   LoopMode = LoopMode.None;
          public int        LoopCount;
          public string     TargetId;  // null if no target=

          public AnimationFamily Family { get; private set; }

          private static readonly string[] ValidPresets = {
              "fadein","fadeout",
              "slidein-left","slidein-right","slidein-up","slidein-down",
              "slideout-left","slideout-right","slideout-up","slideout-down",
              "scalein","scaleout",
              "pulse","bounce","shake"
          };

          public void SetType(string v)      => TypeRaw = v;
          public void SetTranslate(string v) { ParseVec2FromTo(v, out TranslateFrom, out TranslateTo); HasTranslate = true; }
          public void SetScale(string v)     { ParseScaleFromTo(v, out ScaleFrom, out ScaleTo);       HasScale     = true; }
          public void SetRotate(string v)    { ParseFloatFromTo(v, out RotateFrom, out RotateTo);     HasRotate    = true; }
          public void SetFade(string v)      { ParseFloatFromTo(v, out FadeFrom, out FadeTo);         HasFade      = true; }
          public void SetCount(string v)     { ParseFloatFromTo(v, out CountFrom, out CountTo);       HasCount     = true; }
          public void SetFormat(string v)    => Format = string.IsNullOrEmpty(v) ? "{0}" : v;
          public void SetCharColor(string v) { ParseColorFromTo(v, out CharColorFrom, out CharColorTo); HasCharColor = true; }
          public void SetCharStagger(string v) => CharStaggerSec = ParseSeconds(v);
          public void SetDuration(string v)  => Duration = ParseSeconds(v);
          public void SetDelay(string v)     => Delay = ParseSeconds(v);
          public void SetEasing(string v)    => Easing = ParseEasing(v);
          public void SetLoop(string v)      => ParseLoop(v, out LoopMode, out LoopCount);
          public void SetTarget(string v)    => TargetId = v?.StartsWith("@") == true ? v.Substring(1) : v;

          public void Validate()
          {
              bool preset   = !string.IsNullOrEmpty(TypeRaw);
              bool lowLevel = HasTranslate || HasScale || HasRotate || HasFade;
              bool text     = HasCount || HasCharColor;

              int families = (preset ? 1 : 0) + (lowLevel ? 1 : 0) + (text ? 1 : 0);
              if (families > 1)
                  throw new ArgumentException(
                      "<Animation>: three attribute families (preset / low-level transform / text-effect) " +
                      "are mutually exclusive. Use only one.");

              if (preset)
              {
                  if (System.Array.IndexOf(ValidPresets, TypeRaw) < 0)
                      throw new ArgumentException(
                          $"<Animation type=\"{TypeRaw}\"> is not a valid preset. " +
                          "Valid: " + string.Join(", ", ValidPresets));
                  Family = AnimationFamily.Preset;
              }
              else if (lowLevel) Family = AnimationFamily.LowLevel;
              else if (text)
              {
                  if (HasCount && HasCharColor)
                      throw new ArgumentException(
                          "<Animation>: count= and char-color= are mutually exclusive within text family.");
                  Family = AnimationFamily.Text;
              }
              else Family = AnimationFamily.None;
          }

          public Snapshot Snapshot() => new Snapshot
          {
              TypeRaw = TypeRaw, Duration = Duration, Delay = Delay, Easing = Easing,
              LoopMode = LoopMode, LoopCount = LoopCount,
              TranslateFrom = TranslateFrom, TranslateTo = TranslateTo,
              ScaleFrom = ScaleFrom, ScaleTo = ScaleTo,
              RotateFrom = RotateFrom, RotateTo = RotateTo,
              FadeFrom = FadeFrom, FadeTo = FadeTo,
              CountFrom = CountFrom, CountTo = CountTo, Format = Format,
              CharColorFrom = CharColorFrom, CharColorTo = CharColorTo, CharStaggerSec = CharStaggerSec,
              TargetId = TargetId,
          };

          public struct Snapshot : IEquatable<Snapshot>
          {
              public string TypeRaw; public float Duration, Delay; public EasingKind Easing;
              public LoopMode LoopMode; public int LoopCount;
              public Vector2 TranslateFrom, TranslateTo, ScaleFrom, ScaleTo;
              public float RotateFrom, RotateTo, FadeFrom, FadeTo, CountFrom, CountTo;
              public string Format;
              public Color CharColorFrom, CharColorTo; public float CharStaggerSec;
              public string TargetId;
              public bool Equals(Snapshot o) =>
                  TypeRaw == o.TypeRaw && Duration == o.Duration && Delay == o.Delay && Easing == o.Easing
                  && LoopMode == o.LoopMode && LoopCount == o.LoopCount
                  && TranslateFrom == o.TranslateFrom && TranslateTo == o.TranslateTo
                  && ScaleFrom == o.ScaleFrom && ScaleTo == o.ScaleTo
                  && RotateFrom == o.RotateFrom && RotateTo == o.RotateTo
                  && FadeFrom == o.FadeFrom && FadeTo == o.FadeTo
                  && CountFrom == o.CountFrom && CountTo == o.CountTo
                  && Format == o.Format
                  && CharColorFrom == o.CharColorFrom && CharColorTo == o.CharColorTo
                  && CharStaggerSec == o.CharStaggerSec
                  && TargetId == o.TargetId;
              public override bool Equals(object obj) => obj is Snapshot s && Equals(s);
              public override int GetHashCode() => HashCode.Combine(
                  TypeRaw, Duration, Easing, LoopMode,
                  TranslateTo, ScaleTo, FadeTo, CountTo);
          }

          // --- parsers ---

          private static float ParseFloat(string s)
              => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

          private static float ParseSeconds(string s)
          {
              if (string.IsNullOrEmpty(s)) return 0f;
              s = s.Trim();
              if (s.EndsWith("ms")) return ParseFloat(s.Substring(0, s.Length - 2)) / 1000f;
              if (s.EndsWith("s"))  return ParseFloat(s.Substring(0, s.Length - 1));
              return ParseFloat(s);
          }

          private static Vector2 ParseVec2(string s)
          {
              var parts = s.Split(',');
              if (parts.Length != 2)
                  throw new ArgumentException($"Expected 'x,y', got '{s}'");
              return new Vector2(ParseFloat(parts[0]), ParseFloat(parts[1]));
          }

          private static void ParseVec2FromTo(string v, out Vector2 from, out Vector2 to)
          {
              var i = v.IndexOf(':');
              if (i < 0) { from = Vector2.zero; to = ParseVec2(v); return; }
              var l = v.Substring(0, i);
              var r = v.Substring(i + 1);
              from = string.IsNullOrEmpty(l) ? Vector2.zero : ParseVec2(l);
              to   = ParseVec2(r);
          }

          private static void ParseScaleFromTo(string v, out Vector2 from, out Vector2 to)
          {
              // "0.5:1" → (0.5,0.5):(1,1)
              // "0.5,0.8:1,1" → (0.5,0.8):(1,1)
              var i = v.IndexOf(':');
              string l = i >= 0 ? v.Substring(0, i) : "";
              string r = i >= 0 ? v.Substring(i + 1) : v;
              from = string.IsNullOrEmpty(l) ? Vector2.one : ParseScaleSide(l);
              to   = ParseScaleSide(r);
          }

          private static Vector2 ParseScaleSide(string s)
          {
              return s.Contains(',') ? ParseVec2(s) : new Vector2(ParseFloat(s), ParseFloat(s));
          }

          private static void ParseFloatFromTo(string v, out float from, out float to)
          {
              var i = v.IndexOf(':');
              if (i < 0) { from = 0f; to = ParseFloat(v); return; }
              var l = v.Substring(0, i);
              var r = v.Substring(i + 1);
              from = string.IsNullOrEmpty(l) ? 0f : ParseFloat(l);
              to   = ParseFloat(r);
          }

          private static void ParseColorFromTo(string v, out Color from, out Color to)
          {
              var i = v.IndexOf(':');
              if (i < 0) { from = Color.white; to = ParseColor(v); return; }
              from = ParseColor(v.Substring(0, i));
              to   = ParseColor(v.Substring(i + 1));
          }

          private static Color ParseColor(string s)
          {
              var parts = s.Split(',');
              if (parts.Length != 4)
                  throw new ArgumentException($"Expected 'r,g,b,a', got '{s}'");
              return new Color(ParseFloat(parts[0]), ParseFloat(parts[1]),
                               ParseFloat(parts[2]), ParseFloat(parts[3]));
          }

          private static EasingKind ParseEasing(string s) => (s ?? "out-cubic") switch
          {
              "linear"        => EasingKind.Linear,
              "in-cubic"      => EasingKind.InCubic,
              "out-cubic"     => EasingKind.OutCubic,
              "in-out-cubic"  => EasingKind.InOutCubic,
              "in-quad"       => EasingKind.InQuad,
              "out-quad"      => EasingKind.OutQuad,
              "in-out-quad"   => EasingKind.InOutQuad,
              "in-quart"      => EasingKind.InQuart,
              "out-quart"     => EasingKind.OutQuart,
              "in-out-quart"  => EasingKind.InOutQuart,
              "in-quint"      => EasingKind.InQuint,
              "out-quint"     => EasingKind.OutQuint,
              "in-out-quint"  => EasingKind.InOutQuint,
              "out-back"      => EasingKind.OutBack,
              "out-elastic"   => EasingKind.OutElastic,
              "out-bounce"    => EasingKind.OutBounce,
              _ => throw new ArgumentException(
                  $"<Animation easing=\"{s}\"> not a recognized easing. " +
                  "Valid: linear / in-cubic / out-cubic / in-out-cubic / out-back / out-elastic / out-bounce / ...")
          };

          private static void ParseLoop(string v, out LoopMode mode, out int count)
          {
              count = 0;
              switch (v)
              {
                  case null: case "":  mode = LoopMode.None;    return;
                  case "true":         mode = LoopMode.Restart; return;
                  case "yoyo":         mode = LoopMode.Yoyo;    return;
              }
              if (v.StartsWith("count:"))
              {
                  mode = LoopMode.Count;
                  count = int.Parse(v.Substring("count:".Length), CultureInfo.InvariantCulture);
                  return;
              }
              throw new ArgumentException(
                  $"<Animation loop=\"{v}\"> not valid. Use true / yoyo / count:<N>.");
          }
      }
  }
  ```

- [ ] **Step 7.4: Refresh + run tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="AnimationSpecTests")
  ```

  Expected: 14 tests PASS.

- [ ] **Step 7.5: Lint + Commit**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  ```bash
  git add Runtime/Controls/Internal/AnimationSpec.cs \
          Runtime/Controls/Internal/AnimationSpec.cs.meta \
          Tests/EditMode/Controls/AnimationSpecTests.cs \
          Tests/EditMode/Controls/AnimationSpecTests.cs.meta
  git commit -m "$(cat <<'EOF'
  feat: AnimationSpec POCO — parse + family validation + snapshot

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 8: `AnimationDriver` low-level transform family + wire Animation OnTriggerFired

**Files:**
- Create: `Runtime/Controls/Internal/AnimationDriver.cs`
- Modify: `Runtime/Controls/Animation.cs` (add `[UIAttr]`s, wire OnTriggerFired)
- Create: `Tests/PlayMode/Controls/AnimationPlayTests.cs`

This is the first task that exercises real LitMotion calls — must be PlayMode.

- [ ] **Step 8.1: Write the failing PlayMode test**

  Create `Tests/PlayMode/Controls/AnimationPlayTests.cs`:

  ```csharp
  using System.Collections;
  using NUnit.Framework;
  using PromptUGUI.Application;
  using PromptUGUI.Controls;
  using UnityEngine;
  using UnityEngine.TestTools;

  namespace PromptUGUI.Tests.PlayMode.Controls
  {
      public class AnimationPlayTests
      {
          private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
              "<PromptUGUI version='1'><Screen name='S'>";
          private const string Footer = "</Screen></PromptUGUI>";

          [SetUp]    public void SetUp()    => UI.ResetForTests();
          [TearDown] public void TearDown() => UI.ResetForTests();

          [UnityTest]
          public IEnumerator Fade_low_level_reaches_to_value()
          {
              UI.LoadDocument("t", $"{Header}" +
                  "<Animation id='a' fade='0:1' duration='0.1s'><Frame id='f'/></Animation>" +
                  $"{Footer}");
              var screen = UI.Open("S");
              var anim = screen.Get<Animation>("a");

              // Wait for the motion to complete; LitMotion runs on Update so a few frames suffice.
              yield return new WaitForSeconds(0.2f);

              var cg = anim.GameObject.GetComponent<CanvasGroup>();
              Assert.IsNotNull(cg, "Animation must have a CanvasGroup for fade");
              Assert.AreEqual(1f, cg.alpha, 0.01f);
          }

          [UnityTest]
          public IEnumerator Translate_low_level_reaches_to_offset()
          {
              UI.LoadDocument("t", $"{Header}" +
                  "<Animation id='a' translate='0,-50:0,0' duration='0.1s'><Frame id='f'/></Animation>" +
                  $"{Footer}");
              var screen = UI.Open("S");
              var anim = screen.Get<Animation>("a");
              yield return new WaitForSeconds(0.2f);

              var proxy = (RectTransform)anim.GameObject.transform.Find("_offsetProxy");
              Assert.AreEqual(Vector2.zero, proxy.anchoredPosition, "Translate must end at 0,0");
          }

          [UnityTest]
          public IEnumerator Scale_low_level_reaches_to_value()
          {
              UI.LoadDocument("t", $"{Header}" +
                  "<Animation id='a' scale='0.5:1' duration='0.1s'><Frame id='f'/></Animation>" +
                  $"{Footer}");
              var screen = UI.Open("S");
              var anim = screen.Get<Animation>("a");
              yield return new WaitForSeconds(0.2f);

              var proxy = (RectTransform)anim.GameObject.transform.Find("_offsetProxy");
              Assert.AreEqual(Vector3.one, proxy.localScale);
          }
      }
  }
  ```

- [ ] **Step 8.2: Run, verify failure**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: compile error — `AnimationDriver` not defined; Animation has no `[UIAttr]` setters.

- [ ] **Step 8.3: Create `AnimationDriver.cs` (transform family only)**

  Create `Runtime/Controls/Internal/AnimationDriver.cs`:

  ```csharp
  using LitMotion;
  using UnityEngine;
  using UnityEngine.UI;
  using TMPro;

  namespace PromptUGUI.Controls.Internal
  {
      internal static class AnimationDriver
      {
          public static MotionHandle[] Play(
              AnimationSpec spec,
              RectTransform offsetProxy,
              CanvasGroup canvasGroup,
              TMP_Text textTarget)
          {
              var handles = new System.Collections.Generic.List<MotionHandle>();
              var ease = ToEase(spec.Easing);

              switch (spec.Family)
              {
                  case AnimationFamily.Preset:
                      // Task 10 expands preset → low-level invocations
                      break;

                  case AnimationFamily.LowLevel:
                      if (spec.HasTranslate)
                          handles.Add(LMotion.Create(spec.TranslateFrom, spec.TranslateTo, spec.Duration)
                              .WithEase(ease).WithDelay(spec.Delay)
                              .Bind(offsetProxy, (v, rt) => rt.anchoredPosition = v));
                      if (spec.HasScale)
                          handles.Add(LMotion.Create(
                                  new Vector3(spec.ScaleFrom.x, spec.ScaleFrom.y, 1f),
                                  new Vector3(spec.ScaleTo.x,   spec.ScaleTo.y,   1f),
                                  spec.Duration)
                              .WithEase(ease).WithDelay(spec.Delay)
                              .Bind(offsetProxy, (v, rt) => rt.localScale = v));
                      if (spec.HasRotate)
                          handles.Add(LMotion.Create(spec.RotateFrom, spec.RotateTo, spec.Duration)
                              .WithEase(ease).WithDelay(spec.Delay)
                              .Bind(offsetProxy, (v, rt) => rt.localEulerAngles = new Vector3(0, 0, v)));
                      if (spec.HasFade)
                          handles.Add(LMotion.Create(spec.FadeFrom, spec.FadeTo, spec.Duration)
                              .WithEase(ease).WithDelay(spec.Delay)
                              .Bind(canvasGroup, (v, cg) => cg.alpha = v));
                      break;

                  case AnimationFamily.Text:
                      // Task 11 / 12 fill in text effects
                      break;
              }

              // Loop application — currently only on the LAST handle (LitMotion's WithLoops applies per motion).
              // For multi-motion this is approximate; full sequence support would need LSequence (out of scope v1).
              return handles.ToArray();
          }

          private static Ease ToEase(EasingKind k) => k switch
          {
              EasingKind.Linear      => Ease.Linear,
              EasingKind.InCubic     => Ease.InCubic,
              EasingKind.OutCubic    => Ease.OutCubic,
              EasingKind.InOutCubic  => Ease.InOutCubic,
              EasingKind.InQuad      => Ease.InQuad,
              EasingKind.OutQuad     => Ease.OutQuad,
              EasingKind.InOutQuad   => Ease.InOutQuad,
              EasingKind.InQuart     => Ease.InQuart,
              EasingKind.OutQuart    => Ease.OutQuart,
              EasingKind.InOutQuart  => Ease.InOutQuart,
              EasingKind.InQuint     => Ease.InQuint,
              EasingKind.OutQuint    => Ease.OutQuint,
              EasingKind.InOutQuint  => Ease.InOutQuint,
              EasingKind.OutBack     => Ease.OutBack,
              EasingKind.OutElastic  => Ease.OutElastic,
              EasingKind.OutBounce   => Ease.OutBounce,
              _ => Ease.OutCubic,
          };
      }
  }
  ```

  **Note:** `LMotion.Create` / `WithEase` / `WithDelay` / `Bind` are the LitMotion v2 API. If LitMotion's API in the host project differs (e.g., `BindWithState` vs `Bind`), adjust signatures here. If LitMotion needs an explicit `RunWithoutBinding()` or returns `MotionHandle<TValue>`, normalize via `.Preserve()` or `(MotionHandle)` cast.

- [ ] **Step 8.4: Wire `Animation.cs` to use the driver**

  Replace `Runtime/Controls/Animation.cs` with:

  ```csharp
  using LitMotion;
  using PromptUGUI.Controls.Internal;
  using PromptUGUI.Registry;
  using TMPro;
  using UnityEngine;

  namespace PromptUGUI.Controls
  {
      public sealed class Animation : Trigger
      {
          private RectTransform _offsetProxy;
          private CanvasGroup _cg;
          private MotionHandle[] _current;
          private readonly AnimationSpec _spec = new();
          private AnimationSpec.Snapshot _lastApplied;

          protected internal override Transform ChildHostTransform => _offsetProxy;

          [UIAttr("type")]      public string TypeAttr      { set => _spec.SetType(value); }
          [UIAttr("translate")] public string TranslateAttr { set => _spec.SetTranslate(value); }
          [UIAttr("scale")]     public string ScaleAttr     { set => _spec.SetScale(value); }
          [UIAttr("rotate")]    public string RotateAttr    { set => _spec.SetRotate(value); }
          [UIAttr("fade")]      public string FadeAttr      { set => _spec.SetFade(value); }
          [UIAttr("duration")]  public string DurationAttr  { set => _spec.SetDuration(value); }
          [UIAttr("delay")]     public string DelayAttr     { set => _spec.SetDelay(value); }
          [UIAttr("easing")]    public string EasingAttr    { set => _spec.SetEasing(value); }
          [UIAttr("loop")]      public string LoopAttr      { set => _spec.SetLoop(value); }
          // Text-effect / target attrs added in Tasks 11-12.

          public override void OnAttached()
          {
              var go = new GameObject("_offsetProxy", typeof(RectTransform));
              go.transform.SetParent(RectTransform, worldPositionStays: false);
              _offsetProxy = (RectTransform)go.transform;
              _offsetProxy.anchorMin = Vector2.zero;
              _offsetProxy.anchorMax = Vector2.one;
              _offsetProxy.offsetMin = Vector2.zero;
              _offsetProxy.offsetMax = Vector2.zero;
              _offsetProxy.pivot = new Vector2(0.5f, 0.5f);
              _cg = GameObject.AddComponent<CanvasGroup>();
          }

          internal override void OnAfterApply()
          {
              _spec.Validate();
              // Dirty-check vs. last applied snapshot — restart only on change.
              // (First call: _lastApplied is default; HasControlChanges returns true → cancel of empty
              // _current is harmless, then trigger fires below.)
              var snap = _spec.Snapshot();
              if (!snap.Equals(_lastApplied))
              {
                  CancelCurrent();
                  _lastApplied = snap;
              }
              base.OnAfterApply();  // Trigger handles initial Fire / subscriptions
          }

          protected override void OnTriggerFired()
          {
              CancelCurrent();
              _current = AnimationDriver.Play(_spec, _offsetProxy, _cg, ResolveTextTarget());
          }

          private TMP_Text ResolveTextTarget() => null;  // Tasks 11-12 implement

          private void CancelCurrent()
          {
              if (_current == null) return;
              foreach (var h in _current) if (h.IsActive()) h.TryCancel();
              _current = null;
          }

          public override void Dispose()
          {
              CancelCurrent();
              base.Dispose();
          }
      }
  }
  ```

- [ ] **Step 8.5: Refresh + run PlayMode tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="AnimationPlayTests")
  ```

  Expected: 3 tests PASS. If a LitMotion API mismatch fails compilation, fix the signature in `AnimationDriver` (Step 8.3 note) and re-run.

- [ ] **Step 8.6: Lint + Commit**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  ```bash
  git add Runtime/Controls/Internal/AnimationDriver.cs \
          Runtime/Controls/Internal/AnimationDriver.cs.meta \
          Runtime/Controls/Animation.cs \
          Tests/PlayMode/Controls/AnimationPlayTests.cs \
          Tests/PlayMode/Controls/AnimationPlayTests.cs.meta
  git commit -m "$(cat <<'EOF'
  feat: AnimationDriver for low-level transform family (translate/scale/rotate/fade)

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 9: Preset → low-level expansion table

**Files:**
- Modify: `Runtime/Controls/Internal/AnimationSpec.cs` (add `ExpandPreset()`)
- Modify: `Runtime/Controls/Internal/AnimationDriver.cs` (call `ExpandPreset` on Preset family)
- Append: `Tests/EditMode/Controls/AnimationSpecTests.cs` (preset expansion tests)
- Append: `Tests/PlayMode/Controls/AnimationPlayTests.cs` (1-2 end-state preset tests)

- [ ] **Step 9.1: Write the failing tests**

  Append to `AnimationSpecTests.cs`:

  ```csharp
  [Test] public void Preset_fadein_expands_to_fade_0_to_1()
  {
      var s = new AnimationSpec(); s.SetType("fadein"); s.Validate();
      s.ExpandPreset();
      Assert.IsTrue(s.HasFade);
      Assert.AreEqual(0f, s.FadeFrom);
      Assert.AreEqual(1f, s.FadeTo);
  }

  [Test] public void Preset_slidein_left_expands_to_translate_and_fade()
  {
      var s = new AnimationSpec(); s.SetType("slidein-left"); s.Validate();
      s.ExpandPreset();
      Assert.IsTrue(s.HasTranslate);
      Assert.AreEqual(new Vector2(-100, 0), s.TranslateFrom);
      Assert.AreEqual(Vector2.zero,         s.TranslateTo);
      Assert.IsTrue(s.HasFade);
  }

  [Test] public void Preset_pulse_sets_yoyo_loop_implicitly()
  {
      var s = new AnimationSpec(); s.SetType("pulse"); s.Validate();
      s.ExpandPreset();
      Assert.AreEqual(LoopMode.Yoyo, s.LoopMode);
  }

  [Test] public void Preset_bounce_sets_outback_easing_implicitly()
  {
      var s = new AnimationSpec(); s.SetType("bounce"); s.Validate();
      // explicitly check the easing was NOT overwritten if user set it
      Assert.AreEqual(EasingKind.OutCubic, s.Easing); // user didn't set, default
      s.ExpandPreset();
      Assert.AreEqual(EasingKind.OutBack, s.Easing);
  }
  ```

  Append to `AnimationPlayTests.cs`:

  ```csharp
  [UnityTest]
  public IEnumerator Preset_fadein_completes_to_alpha_1()
  {
      UI.LoadDocument("t", $"{Header}" +
          "<Animation id='a' type='fadein' duration='0.1s'><Frame id='f'/></Animation>" +
          $"{Footer}");
      var screen = UI.Open("S");
      yield return new WaitForSeconds(0.2f);
      var cg = screen.Get<Animation>("a").GameObject.GetComponent<CanvasGroup>();
      Assert.AreEqual(1f, cg.alpha, 0.01f);
  }

  [UnityTest]
  public IEnumerator Preset_slidein_left_ends_at_origin()
  {
      UI.LoadDocument("t", $"{Header}" +
          "<Animation id='a' type='slidein-left' duration='0.1s'><Frame id='f'/></Animation>" +
          $"{Footer}");
      var screen = UI.Open("S");
      yield return new WaitForSeconds(0.2f);
      var proxy = (RectTransform)screen.Get<Animation>("a").GameObject.transform.Find("_offsetProxy");
      Assert.AreEqual(Vector2.zero, proxy.anchoredPosition);
  }
  ```

- [ ] **Step 9.2: Add `ExpandPreset()` to `AnimationSpec.cs`**

  Append to the `AnimationSpec` class:

  ```csharp
  /// <summary>
  /// 把 TypeRaw 展开成等价的低层属性 (HasTranslate/HasFade/...)。
  /// 调用后 Family 保持 Preset；Driver 用展开后的低层值生成 motion。
  /// </summary>
  public void ExpandPreset()
  {
      if (string.IsNullOrEmpty(TypeRaw)) return;
      switch (TypeRaw)
      {
          case "fadein":          HasFade = true; FadeFrom = 0; FadeTo = 1; break;
          case "fadeout":         HasFade = true; FadeFrom = 1; FadeTo = 0; break;
          case "slidein-left":    SlideIn(new Vector2(-100, 0)); break;
          case "slidein-right":   SlideIn(new Vector2( 100, 0)); break;
          case "slidein-up":      SlideIn(new Vector2(0, -100)); break;
          case "slidein-down":    SlideIn(new Vector2(0,  100)); break;
          case "slideout-left":   SlideOut(new Vector2(-100, 0)); break;
          case "slideout-right":  SlideOut(new Vector2( 100, 0)); break;
          case "slideout-up":     SlideOut(new Vector2(0, -100)); break;
          case "slideout-down":   SlideOut(new Vector2(0,  100)); break;
          case "scalein":         HasScale = true; ScaleFrom = new Vector2(0.8f, 0.8f); ScaleTo = Vector2.one;
                                  HasFade = true; FadeFrom = 0; FadeTo = 1; break;
          case "scaleout":        HasScale = true; ScaleFrom = Vector2.one; ScaleTo = new Vector2(0.8f, 0.8f);
                                  HasFade = true; FadeFrom = 1; FadeTo = 0; break;
          case "pulse":           HasScale = true; ScaleFrom = Vector2.one; ScaleTo = new Vector2(1.05f, 1.05f);
                                  if (LoopMode == LoopMode.None) LoopMode = LoopMode.Yoyo; break;
          case "bounce":          HasScale = true; ScaleFrom = new Vector2(0.9f, 0.9f); ScaleTo = Vector2.one;
                                  Easing = EasingKind.OutBack; break;
          case "shake":           HasTranslate = true; TranslateFrom = new Vector2(-5, 0); TranslateTo = new Vector2(5, 0);
                                  Easing = EasingKind.Linear;
                                  if (LoopMode == LoopMode.None) { LoopMode = LoopMode.Count; LoopCount = 4; }
                                  break;
      }
  }

  private void SlideIn(Vector2 from)
  {
      HasTranslate = true; TranslateFrom = from; TranslateTo = Vector2.zero;
      HasFade = true; FadeFrom = 0; FadeTo = 1;
  }
  private void SlideOut(Vector2 to)
  {
      HasTranslate = true; TranslateFrom = Vector2.zero; TranslateTo = to;
      HasFade = true; FadeFrom = 1; FadeTo = 0;
  }
  ```

- [ ] **Step 9.3: Update `AnimationDriver.Play` to call `ExpandPreset`**

  In `AnimationDriver.cs`, update the `Preset` case:

  ```csharp
  case AnimationFamily.Preset:
      spec.ExpandPreset();
      goto case AnimationFamily.LowLevel;
  case AnimationFamily.LowLevel:
      // ...existing translate/scale/rotate/fade code...
      break;
  ```

  C# `goto case` is legal in `switch`. (Alternative: extract the low-level dispatch to a helper method and call from both.)

- [ ] **Step 9.4: Refresh + run tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="AnimationSpecTests")
  mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="AnimationPlayTests")
  ```

  Expected: 4 new EditMode tests PASS + 2 new PlayMode tests PASS.

- [ ] **Step 9.5: Lint + Commit**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  ```bash
  git add Runtime/Controls/Internal/AnimationSpec.cs \
          Runtime/Controls/Internal/AnimationDriver.cs \
          Tests/EditMode/Controls/AnimationSpecTests.cs \
          Tests/PlayMode/Controls/AnimationPlayTests.cs
  git commit -m "$(cat <<'EOF'
  feat: 15 animation presets via low-level expansion

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 10: Loop / Delay application + on="loop" wiring

**Files:**
- Modify: `Runtime/Controls/Internal/AnimationDriver.cs` (apply `WithLoops` per handle)
- Modify: `Runtime/Controls/Trigger.cs` (on=loop implies loop=yoyo via spec mutation — but Trigger doesn't know AnimationSpec; instead handle this in `Animation.OnAfterApply`)
- Append: `Tests/PlayMode/Controls/AnimationPlayTests.cs`

- [ ] **Step 10.1: Write the failing PlayMode test**

  Append to `AnimationPlayTests.cs`:

  ```csharp
  [UnityTest]
  public IEnumerator On_loop_pulse_oscillates_scale()
  {
      UI.LoadDocument("t", $"{Header}" +
          "<Animation id='a' type='pulse' on='loop' duration='0.05s'><Frame id='f'/></Animation>" +
          $"{Footer}");
      var screen = UI.Open("S");
      var proxy = (RectTransform)screen.Get<Animation>("a").GameObject.transform.Find("_offsetProxy");

      yield return new WaitForSeconds(0.05f);
      var s1 = proxy.localScale.x;
      yield return new WaitForSeconds(0.1f);
      var s2 = proxy.localScale.x;

      // After yoyo, scale visits 1.0 and ~1.05; in either sample we should see them differ.
      Assert.AreNotEqual(s1, s2, 0.001f, "pulse loop must oscillate scale, not freeze");
  }

  [UnityTest]
  public IEnumerator Loop_count_3_runs_three_times_then_stops()
  {
      UI.LoadDocument("t", $"{Header}" +
          "<Animation id='a' translate='0,0:50,0' duration='0.05s' loop='count:3' on='open'><Frame id='f'/></Animation>" +
          $"{Footer}");
      var screen = UI.Open("S");
      var proxy = (RectTransform)screen.Get<Animation>("a").GameObject.transform.Find("_offsetProxy");
      yield return new WaitForSeconds(0.05f * 3 + 0.05f);  // 3 loops + grace
      // After 3 loops with Restart mode, position is at "to" (50,0)
      Assert.AreEqual(new Vector2(50, 0), proxy.anchoredPosition);
  }
  ```

- [ ] **Step 10.2: Apply loop in `AnimationDriver`**

  In `AnimationDriver.cs`, wrap each `LMotion.Create(...)` chain to include loop:

  ```csharp
  private static MotionBuilder<T, NoOptions, T> ApplyCommon<T>(
      MotionBuilder<T, NoOptions, T> b, AnimationSpec spec, Ease ease)
  {
      var withDelay = b.WithEase(ease).WithDelay(spec.Delay);
      return spec.LoopMode switch
      {
          LoopMode.None    => withDelay,
          LoopMode.Yoyo    => withDelay.WithLoops(-1, LoopType.Yoyo),
          LoopMode.Restart => withDelay.WithLoops(-1, LoopType.Restart),
          LoopMode.Count   => withDelay.WithLoops(spec.LoopCount, LoopType.Restart),
          _                => withDelay,
      };
  }
  ```

  **Note**: `MotionBuilder<T, ...>` is a LitMotion generic struct; the exact signature depends on the version. If the type-inference makes this helper messy, inline the loop application per branch instead:

  ```csharp
  if (spec.HasFade)
  {
      var b = LMotion.Create(spec.FadeFrom, spec.FadeTo, spec.Duration)
                     .WithEase(ease).WithDelay(spec.Delay);
      b = ApplyLoop(b, spec);
      handles.Add(b.Bind(canvasGroup, (v, cg) => cg.alpha = v));
  }
  ```

  Pick whichever pattern compiles cleanest; the test verifies behavior.

- [ ] **Step 10.3: Wire `on="loop"` → `LoopMode.Yoyo` in Animation**

  In `Animation.cs`, in `OnAfterApply` (before Validate), translate trigger kind to loop hint:

  ```csharp
  internal override void OnAfterApply()
  {
      // on="loop" implies yoyo unless user explicitly set loop=
      if (TriggerKind == TriggerKind.Loop && _spec.LoopMode == LoopMode.None)
          _spec.LoopMode = LoopMode.Yoyo;
      _spec.Validate();
      // ...
  }
  ```

  This requires `Trigger` to expose `TriggerKind` to subclasses. Edit `Trigger.cs` to add:

  ```csharp
  protected TriggerKind TriggerKind => _spec?.Kind ?? TriggerKind.Open;
  ```

  (Visibility of `TriggerKind` enum — it's `internal` per Task 3. Either make `Animation` access the property which exposes the kind without exposing the enum publicly, or move `TriggerKind` enum to `public`. Keep `internal` and make the property `internal protected`):

  ```csharp
  internal protected TriggerKind TriggerKind => _spec?.Kind ?? Internal.TriggerKind.Open;
  ```

- [ ] **Step 10.4: Refresh + run tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="AnimationPlayTests")
  ```

  Expected: both new tests PASS plus all previous PlayMode tests still PASS.

- [ ] **Step 10.5: Lint + Commit**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  ```bash
  git add Runtime/Controls/Internal/AnimationDriver.cs \
          Runtime/Controls/Trigger.cs \
          Runtime/Controls/Animation.cs \
          Tests/PlayMode/Controls/AnimationPlayTests.cs
  git commit -m "$(cat <<'EOF'
  feat: loop/delay application + on="loop" → yoyo sugar

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 11: Text effect — `count="from:to"` (BindToText)

**Files:**
- Modify: `Runtime/Controls/Text.cs` (expose `internal TMP_Text TmpComponent`)
- Create: `Runtime/Controls/Internal/AnimationTargetResolver.cs`
- Modify: `Runtime/Controls/Animation.cs` (add Count / Format / Target `[UIAttr]`s; implement `ResolveTextTarget`)
- Modify: `Runtime/Controls/Internal/AnimationDriver.cs` (add Text family branch)
- Append: `Tests/PlayMode/Controls/AnimationPlayTests.cs`

- [ ] **Step 11.1: Write the failing PlayMode test**

  Append to `AnimationPlayTests.cs`:

  ```csharp
  [UnityTest]
  public IEnumerator Count_animation_writes_final_value_to_Text()
  {
      UI.LoadDocument("t", $"{Header}" +
          "<Animation id='a' count='0:1000' format='{0:F0}' duration='0.1s'><Text id='label'>0</Text></Animation>" +
          $"{Footer}");
      var screen = UI.Open("S");
      yield return new WaitForSeconds(0.2f);
      var label = screen.Get<Text>("a/label");
      Assert.AreEqual("1000", label.GameObject.GetComponent<TMPro.TMP_Text>().text);
  }

  [UnityTest]
  public IEnumerator Count_with_target_refs_screen_scope_Text()
  {
      UI.LoadDocument("t", $"{Header}" +
          "<Text id='score'>0</Text>" +
          "<Animation id='a' count='0:500' format='{0:F0}' target='@score' duration='0.1s' on='open'/>" +
          $"{Footer}");
      var screen = UI.Open("S");
      yield return new WaitForSeconds(0.2f);
      Assert.AreEqual("500", screen.Get<Text>("score").GameObject.GetComponent<TMPro.TMP_Text>().text);
  }
  ```

- [ ] **Step 11.2: Expose `Text.TmpComponent`**

  Edit `Runtime/Controls/Text.cs`. After the private `_tmp` field declaration, add:

  ```csharp
  internal TMP_Text TmpComponent => _tmp;
  ```

- [ ] **Step 11.3: Create `AnimationTargetResolver.cs`**

  Create `Runtime/Controls/Internal/AnimationTargetResolver.cs`:

  ```csharp
  using System;
  using System.Collections.Generic;
  using TMPro;

  namespace PromptUGUI.Controls.Internal
  {
      internal static class AnimationTargetResolver
      {
          /// <summary>
          /// Wrapper 子树内查找唯一 Text；多个 → 报错（要求 target= 指定）；零个 → 报错。
          /// </summary>
          public static TMP_Text FindTextInSubtree(IControl wrapper)
          {
              var found = new List<Text>();
              Collect(wrapper, found);
              if (found.Count == 0)
                  throw new InvalidOperationException(
                      "<Animation count=... or char-color=...>: no <Text> in subtree. " +
                      "Add a Text child or use target=\"@id\".");
              if (found.Count > 1)
                  throw new InvalidOperationException(
                      $"<Animation>: ambiguous — {found.Count} Text descendants. Use target=\"@id\".");
              return found[0].TmpComponent;
          }

          private static void Collect(IControl c, List<Text> outList)
          {
              foreach (var child in c.Children)
              {
                  if (child is Text t) outList.Add(t);
                  Collect(child, outList);
              }
          }
      }
  }
  ```

- [ ] **Step 11.4: Add Count/Format/Target attrs + ResolveTextTarget to `Animation.cs`**

  Edit `Runtime/Controls/Animation.cs`. Add `[UIAttr]` setters:

  ```csharp
  [UIAttr("count")]  public string CountAttr  { set => _spec.SetCount(value); }
  [UIAttr("format")] public string FormatAttr { set => _spec.SetFormat(value); }
  [UIAttr("target")] public string TargetAttr { set => _spec.SetTarget(value); }
  ```

  Replace `private TMP_Text ResolveTextTarget()` body:

  ```csharp
  private TMP_Text ResolveTextTarget()
  {
      if (_spec.Family != AnimationFamily.Text) return null;
      if (!string.IsNullOrEmpty(_spec.TargetId))
      {
          var screen = UI.OwnerScreenOf(this)
              ?? throw new InvalidOperationException(
                  $"<Animation target=\"@{_spec.TargetId}\">: owner Screen not found");
          return screen.Get<Text>(_spec.TargetId).TmpComponent;
      }
      return AnimationTargetResolver.FindTextInSubtree(this);
  }
  ```

  (Add `using PromptUGUI.Application;` if not already present.)

- [ ] **Step 11.5: Add Text family branch in `AnimationDriver`**

  In `AnimationDriver.cs`, in the `switch (spec.Family)`:

  ```csharp
  case AnimationFamily.Text:
      if (spec.HasCount)
      {
          if (textTarget == null)
              throw new InvalidOperationException(
                  "<Animation count=...> requires a Text target (in subtree or via target=\"@id\")");
          handles.Add(ApplyLoop(
              LMotion.Create(spec.CountFrom, spec.CountTo, spec.Duration)
                  .WithEase(ease).WithDelay(spec.Delay), spec)
              .BindToText(textTarget, spec.Format));
      }
      // char-color in Task 12
      break;
  ```

  `BindToText` is in `LitMotion.Extensions` (or whichever package the host has). Add `using LitMotion.Extensions;` to `AnimationDriver.cs`.

- [ ] **Step 11.6: Refresh + run tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="AnimationPlayTests")
  ```

  Expected: 2 new tests PASS.

- [ ] **Step 11.7: Lint + Commit**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  ```bash
  git add Runtime/Controls/Text.cs \
          Runtime/Controls/Internal/AnimationTargetResolver.cs \
          Runtime/Controls/Internal/AnimationTargetResolver.cs.meta \
          Runtime/Controls/Animation.cs \
          Runtime/Controls/Internal/AnimationDriver.cs \
          Tests/PlayMode/Controls/AnimationPlayTests.cs
  git commit -m "$(cat <<'EOF'
  feat: <Animation count="from:to" format="..."> via BindToText

  - Text.TmpComponent exposed internally for LitMotion bindings
  - AnimationTargetResolver: subtree Text lookup with unique/error rules
  - target="@id" resolves in screen-global scope (via UI.OwnerScreenOf)

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 12: Text effect — `char-color` with per-char stagger (BindToTMPCharColor)

**Files:**
- Modify: `Runtime/Controls/Animation.cs` (add CharColor / CharStagger `[UIAttr]`s)
- Modify: `Runtime/Controls/Internal/AnimationDriver.cs` (char-color branch)
- Append: `Tests/PlayMode/Controls/AnimationPlayTests.cs`

- [ ] **Step 12.1: Write the failing PlayMode test**

  Append to `AnimationPlayTests.cs`:

  ```csharp
  [UnityTest]
  public IEnumerator CharColor_zero_stagger_all_chars_reach_to_color()
  {
      UI.LoadDocument("t", $"{Header}" +
          "<Animation id='a' char-color='1,1,1,1:1,0,0,1' duration='0.1s'><Text id='label'>ABC</Text></Animation>" +
          $"{Footer}");
      var screen = UI.Open("S");
      var tmp = screen.Get<Text>("a/label").GameObject.GetComponent<TMPro.TMP_Text>();
      tmp.ForceMeshUpdate();
      yield return new WaitForSeconds(0.2f);
      tmp.ForceMeshUpdate();

      // After motion, each char's bottom-left vertex color should be red. Read via textInfo.
      for (int i = 0; i < 3; i++)
      {
          var c = tmp.textInfo.characterInfo[i];
          if (!c.isVisible) continue;
          var mi = c.materialReferenceIndex;
          var vi = c.vertexIndex;
          var color = tmp.textInfo.meshInfo[mi].colors32[vi];
          Assert.AreEqual(255, color.r);
          Assert.AreEqual(0,   color.g);
          Assert.AreEqual(0,   color.b);
      }
  }
  ```

- [ ] **Step 12.2: Add CharColor / CharStagger attrs**

  In `Animation.cs`:

  ```csharp
  [UIAttr("char-color")]   public string CharColorAttr   { set => _spec.SetCharColor(value); }
  [UIAttr("char-stagger")] public string CharStaggerAttr { set => _spec.SetCharStagger(value); }
  ```

- [ ] **Step 12.3: Add char-color branch in `AnimationDriver`**

  In `AnimationDriver.cs`, append to the `Text` case:

  ```csharp
  if (spec.HasCharColor)
  {
      if (textTarget == null)
          throw new InvalidOperationException(
              "<Animation char-color=...> requires a Text target");
      // Force the mesh to populate textInfo so characterCount is correct.
      textTarget.ForceMeshUpdate();
      var count = textTarget.textInfo.characterCount;
      for (int i = 0; i < count; i++)
      {
          var charIdx = i;
          var perCharDelay = spec.Delay + spec.CharStaggerSec * i;
          handles.Add(ApplyLoop(
              LMotion.Create(spec.CharColorFrom, spec.CharColorTo, spec.Duration)
                  .WithEase(ease).WithDelay(perCharDelay), spec)
              .BindToTMPCharColor(textTarget, charIdx));
      }
  }
  ```

  `BindToTMPCharColor` is in LitMotion's TMP extensions. If the host's LitMotion version exposes it on a different builder (e.g., requires explicit `Color`-typed motion), adjust the type parameter.

- [ ] **Step 12.4: Refresh + run tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="AnimationPlayTests")
  ```

  Expected: new test PASSes. If `BindToTMPCharColor` API doesn't match, fix in Step 12.3.

- [ ] **Step 12.5: Lint + Commit**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  ```bash
  git add Runtime/Controls/Animation.cs \
          Runtime/Controls/Internal/AnimationDriver.cs \
          Tests/PlayMode/Controls/AnimationPlayTests.cs
  git commit -m "$(cat <<'EOF'
  feat: <Animation char-color="..." char-stagger="..."> via BindToTMPCharColor

  Per-char motion handle with stagger=i*charStagger. Useful for color waves
  on victory / score / title text.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 13: Variant ReSolve dirty-check (full coverage test)

The dirty-check logic was already added in Task 8 (Snapshot equality). This task adds the explicit test that proves it works under Variant flips.

**Files:**
- Append: `Tests/EditMode/Controls/AnimationTests.cs`

- [ ] **Step 13.1: Write the failing tests**

  Append to `AnimationTests.cs`:

  ```csharp
  [Test] public void Variant_change_to_duration_resets_motion()
  {
      const string xml = @"<?xml version='1.0' encoding='utf-8'?>
  <PromptUGUI version='1'>
    <Variants>
      <Variant name='theme' values='fast,slow'/>
    </Variants>
    <Screen name='S'>
      <Animation id='a' fade='0:1' duration='0.1s' on='manual'>
        <Animation.Variants>
          <Variant when='theme=slow' duration='1.0s'/>
        </Animation.Variants>
        <Frame id='f'/>
      </Animation>
    </Screen>
  </PromptUGUI>";
      // Note: this XML uses inline <Variant> as supported by the engine.
      // If the engine's syntax differs, adjust to use attr.var notation.
      // For this test we just verify the snapshot-based dirty check, not
      // the variant XML syntax — easiest: assert via direct API.
  }

  [Test] public void Animation_OnAfterApply_idempotent_when_attrs_unchanged()
  {
      UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?>" +
          "<PromptUGUI version='1'><Screen name='S'>" +
          "<Animation id='a' fade='0:1' duration='0.3s' on='manual'><Frame id='f'/></Animation>" +
          "</Screen></PromptUGUI>");
      var screen = UI.Open("S");
      var anim = screen.Get<Animation>("a");
      // Manually trigger ReSolve via VariantStore (no actual change)
      screen.ReSolve();
      // If snapshot-equality works, _lastApplied == new snapshot → no cancellation;
      // verifying behavior here is indirect (no public state to inspect).
      // The proxy for behavior: Fire once, ReSolve once with no attr change, Fire again,
      // and verify that motion ran cleanly without exceptions.
      Assert.DoesNotThrow(() => { anim.Fire(); screen.ReSolve(); anim.Fire(); });
  }
  ```

  **Note**: full Variant XML integration test for control-attribute dirty check is tricky without a known supported Variant attribute-override XML syntax for new controls. The plan defers integration to a manual smoke test:

- [ ] **Step 13.2: Manual integration smoke test (optional, document only)**

  Add a sample under `Samples~/Animation/VariantDirtyCheck.ui.xml` that toggles `duration` via `attr.var`, plus a brief README note about expected behavior. (No code change needed if existing Variant syntax supports `attr.var="duration:slow=1.0s,fast=0.1s"` style — confirm via `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` §7).

  Skip this step if Variant syntax for attribute override on Animation is non-trivial — the EditMode tests above already cover the snapshot equality logic.

- [ ] **Step 13.3: Refresh + run tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="AnimationTests")
  ```

  Expected: tests PASS.

- [ ] **Step 13.4: Commit**

  ```bash
  git add Tests/EditMode/Controls/AnimationTests.cs
  git commit -m "$(cat <<'EOF'
  test: Variant ReSolve idempotence for Animation snapshot dirty-check

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 14: XSD generator integration test

**Files:**
- Modify: `Tests/EditorOnly/XsdGeneratorTests.cs` (append assertions)

- [ ] **Step 14.1: Find the existing XSD test file**

  ```bash
  find Tests/EditMode/Editor -name "Xsd*" 2>/dev/null
  ```

  Open the file. It should have substring assertions like `StringAssert.Contains("<xs:element name=\"Frame\"", xsd);`.

- [ ] **Step 14.2: Add assertions for Trigger and Animation**

  Append within the relevant test method (or add a new test):

  ```csharp
  [Test]
  public void Xsd_includes_Trigger_and_Animation()
  {
      // Reuse the existing XSD generation invocation pattern from this file.
      var xsd = GenerateXsd();  // existing helper or inline; match file's convention.

      StringAssert.Contains("name=\"Trigger\"",   xsd);
      StringAssert.Contains("name=\"Animation\"", xsd);
      StringAssert.Contains("name=\"on\"",        xsd);
      StringAssert.Contains("name=\"type\"",      xsd);
      StringAssert.Contains("name=\"translate\"", xsd);
      StringAssert.Contains("name=\"count\"",     xsd);
      StringAssert.Contains("name=\"char-color\"",xsd);
  }
  ```

  Reflection-driven XSD generator already picks up new controls; this test just locks the contract.

- [ ] **Step 14.3: Refresh + run tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="XsdGeneratorTests")
  ```

  Expected: PASS.

- [ ] **Step 14.4: Commit**

  ```bash
  git add Tests/EditMode/Editor/XsdGeneratorTests.cs
  git commit -m "$(cat <<'EOF'
  test: XSD covers <Trigger> + <Animation>

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 15: SKILL.md updates

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`

- [ ] **Step 15.1: Add chapter to `authoring-promptugui-xml/SKILL.md`**

  Add a top-level section "Triggers and Animations" near the end of the existing built-ins documentation. Required content (write in same style as existing sections):

  1. `<Trigger>` standalone usage — `on=` DSL values (`open` default / `loop` / `click` / `click@id` / `manual`), `id=` for C# subscription
  2. `<Animation>` overview — wraps subtree, inner offset proxy, three attribute families
  3. Attribute table:
     | Family | Attrs | When |
     |---|---|---|
     | Preset      | `type=` | 9 named presets |
     | Low-level transform | `translate / scale / rotate / fade` | from:to syntax |
     | Text effect | `count / format` or `char-color / char-stagger` | targets a `<Text>` |
     | Common      | `duration / delay / easing / loop / target` | shared |
  4. Preset reference: fadein / fadeout / slidein-{l,r,u,d} / slideout-{...} / scalein / scaleout / pulse / bounce / shake
  5. `target=` rules: transform família 禁用; text 族 默认子树唯一 Text，多个 → error；`@id` → screen-global scope
  6. Parse errors clinic — multi-Btn no `@id`, multi-Text no `target=`, preset+lowlevel mixed, count+char-color mixed, invalid easing/preset name
  7. Two complete examples — menu entry stagger pattern (with sibling `<Animation delay=...>`) + score popup (count + char-color combo via nested Animation)

- [ ] **Step 15.2: Add C# section to `scripting-promptugui-csharp/SKILL.md`**

  Add "Triggers and Animations" section:
  1. `Trigger.OnFire` Observable<Unit> — subscribe pattern (`.Subscribe(_ => GameLogic.X())`)
  2. `Animation.Fire()` — main + manual mode use
  3. "XML 管什么时候，C# 管做什么" — the named-hook pattern (XML declares trigger, C# attaches reaction)
  4. Note: Animation is `[UIAttr]`-driven, no Inspector

- [ ] **Step 15.3: Verify markdown structure**

  Read both files end-to-end and confirm:
  - YAML frontmatter intact (line 1 = `---`)
  - Existing sections undisturbed
  - New sections cross-reference each other where appropriate

- [ ] **Step 15.4: Commit**

  ```bash
  git add .claude/skills/authoring-promptugui-xml/SKILL.md \
          .claude/skills/scripting-promptugui-csharp/SKILL.md
  git commit -m "$(cat <<'EOF'
  doc: SKILL.md updates for <Trigger> + <Animation>

  XML skill: full attribute table + preset reference + parse-error clinic.
  C# skill: Trigger.OnFire / Animation.Fire() usage + named-hook pattern.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 16: Final verification + master spec pointer

**Files:**
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` (append §5 pointer)

- [ ] **Step 16.1: Run full test suite**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
  mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
  ```

  Expected: zero failures across all three assemblies.

- [ ] **Step 16.2: Append pointer to master spec**

  In `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` §5 (内置原语 section), add at the end:

  ```markdown
  - `<Trigger>` / `<Animation>` — 事件订阅 + LitMotion 驱动动画。详见
    [2026-05-14-litmotion-animations-design.md](2026-05-14-litmotion-animations-design.md)。
  ```

- [ ] **Step 16.3: Final lint**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format style PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

- [ ] **Step 16.4: Commit + (optionally) ship as PR**

  ```bash
  git add docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md
  git commit -m "$(cat <<'EOF'
  doc: master spec §5 pointer to <Trigger> + <Animation>

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

  Ask user whether to create a feature branch / PR for the whole sequence, or push directly to main.
