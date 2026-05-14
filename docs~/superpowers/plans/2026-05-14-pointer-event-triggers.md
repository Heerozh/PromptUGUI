# Pointer Event Triggers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the `<Trigger>` / `<Animation>` `on=` DSL with 3 new pointer-event triggers (`hover-enter` / `hover-exit` / `press`). Both `<Btn>` and `<Image>` can act as event sources.

**Architecture:** A new `internal sealed class PointerEventRelay : MonoBehaviour` implements `IPointerEnterHandler` / `IPointerExitHandler` / `IPointerDownHandler` and exposes the events as R3 Observables. A new `internal interface IPointerEventSource` exposes the three streams. `Btn` and `Image` implement the interface via lazy-added `PointerEventRelay` component. `TriggerSourceResolver.FindPointerSource(...)` walks the Trigger subtree for `IPointerEventSource` descendants (unique or `@<id>` disambiguation). `Trigger.InitTriggerSubscription` gets 3 new cases that resolve the source and subscribe to the relevant stream.

**Tech Stack:** Unity 6+, .NET Standard 2.1, R3 (Cysharp), Unity EventSystems (IPointer*Handler), NUnit (EditMode + PlayMode), UnityMCP for test execution.

**Spec reference:** `docs~/superpowers/specs/2026-05-14-pointer-event-triggers-design.md`

---

## Pre-flight

- [ ] **Step P1: Verify spec is committed**

  Spec was committed as `1f9be7a` on branch `feat/litmotion-animations`. The branch is continuing — same branch as the v1 LitMotion implementation, additional commits will land on top.

  ```bash
  git log --oneline -3
  ```

  Expected: top commit is `1f9be7a doc: spec for pointer-event triggers ...`.

- [ ] **Step P2: Verify Unity baseline is green**

  ```
  ToolSearch(query="select:refresh_unity,run_tests,read_console", max_results=3)
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: no compile errors. Baseline test counts (from end of LitMotion v1 work):
  - EditMode: 522 PASS
  - EditorOnly: 146 PASS
  - PlayMode: 95 PASS

- [ ] **Step P3: Commit plan doc (ask user first)**

  > "Plan written to `docs~/superpowers/plans/2026-05-14-pointer-event-triggers.md`. Commit it as `doc: plan for pointer-event triggers` before starting Task 1?"

  If yes:

  ```bash
  git add docs~/superpowers/plans/2026-05-14-pointer-event-triggers.md
  git commit -m "$(cat <<'EOF'
  doc: plan for pointer-event triggers (hover-enter/hover-exit/press)

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 1: `IPointerEventSource` interface + `PointerEventRelay` MonoBehaviour

**Files:**
- Create: `Runtime/Controls/Internal/IPointerEventSource.cs`
- Create: `Runtime/Controls/Internal/PointerEventRelay.cs`

No tests yet — this task creates the substrate. Task 2 wires it into Btn / Image and adds the first integration smoke test.

- [ ] **Step 1.1: Create `IPointerEventSource.cs`**

  Create `Runtime/Controls/Internal/IPointerEventSource.cs`:

  ```csharp
  using R3;

  namespace PromptUGUI.Controls.Internal
  {
      internal interface IPointerEventSource
      {
          Observable<Unit> OnPointerEnter { get; }
          Observable<Unit> OnPointerExit  { get; }
          Observable<Unit> OnPointerDown  { get; }
      }
  }
  ```

- [ ] **Step 1.2: Create `PointerEventRelay.cs`**

  Create `Runtime/Controls/Internal/PointerEventRelay.cs`:

  ```csharp
  using R3;
  using UnityEngine;
  using UnityEngine.EventSystems;

  namespace PromptUGUI.Controls.Internal
  {
      internal sealed class PointerEventRelay : MonoBehaviour,
          IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
      {
          private readonly Subject<Unit> _enter = new();
          private readonly Subject<Unit> _exit  = new();
          private readonly Subject<Unit> _down  = new();

          public Observable<Unit> OnPointerEnter => _enter;
          public Observable<Unit> OnPointerExit  => _exit;
          public Observable<Unit> OnPointerDown  => _down;

          void IPointerEnterHandler.OnPointerEnter(PointerEventData e) => _enter.OnNext(Unit.Default);
          void IPointerExitHandler.OnPointerExit(PointerEventData e)   => _exit.OnNext(Unit.Default);
          void IPointerDownHandler.OnPointerDown(PointerEventData e)   => _down.OnNext(Unit.Default);

          private void OnDestroy()
          {
              _enter.Dispose();
              _exit.Dispose();
              _down.Dispose();
          }
      }
  }
  ```

- [ ] **Step 1.3: Refresh + verify clean compile**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: 0 errors. Run the full existing test suites to confirm no regression:

  ```
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
  mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
  ```

  Expected counts unchanged: EditMode 522, PlayMode 95.

- [ ] **Step 1.4: Lint**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

- [ ] **Step 1.5: Commit**

  ```bash
  git add Runtime/Controls/Internal/IPointerEventSource.cs \
          Runtime/Controls/Internal/IPointerEventSource.cs.meta \
          Runtime/Controls/Internal/PointerEventRelay.cs \
          Runtime/Controls/Internal/PointerEventRelay.cs.meta
  git commit -m "$(cat <<'EOF'
  feat: IPointerEventSource interface + PointerEventRelay MonoBehaviour

  R3-based pointer event capture. PointerEventRelay implements Unity's
  IPointerEnter/Exit/DownHandler trio and forwards each to a Subject<Unit>
  exposed as Observable<Unit>. IPointerEventSource is the consumer-facing
  interface — Btn and Image will implement it in Task 2.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 2: Btn + Image implement `IPointerEventSource` (lazy relay)

**Files:**
- Modify: `Runtime/Controls/Btn.cs`
- Modify: `Runtime/Controls/Image.cs`
- Create: `Tests/PlayMode/Controls/PointerEventRelayTests.cs` (smoke test for Risk #1 in spec — Unity Button + PointerEventRelay coexistence)

- [ ] **Step 2.1: Write the failing smoke test**

  Create `Tests/PlayMode/Controls/PointerEventRelayTests.cs`:

  ```csharp
  using System.Collections;
  using NUnit.Framework;
  using PromptUGUI.Application;
  using PromptUGUI.Controls;
  using PromptUGUI.Controls.Internal;
  using R3;
  using UnityEngine;
  using UnityEngine.EventSystems;
  using UnityEngine.TestTools;

  namespace PromptUGUI.Tests.PlayMode.Controls
  {
      public class PointerEventRelayTests
      {
          private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
              "<PromptUGUI version='1'><Screen name='S'>";
          private const string Footer = "</Screen></PromptUGUI>";

          [SetUp]    public void SetUp()    => UI.ResetForTests();
          [TearDown] public void TearDown() => UI.ResetForTests();

          [UnityTest]
          public IEnumerator Btn_exposes_pointer_streams_and_Button_still_works()
          {
              // Spec Risk #1: Unity Button implements IPointer* for state changes.
              // PointerEventRelay also implements them. Both must coexist — Unity's
              // EventSystem dispatches the event to ALL components that implement the
              // handler interface, so both Button (state change) and Relay (stream emit) fire.
              UI.LoadDocument("t", $"{Header}<Btn id='b'>OK</Btn>{Footer}");
              var screen = UI.Open("S");
              var btn = screen.Get<Btn>("b");
              var src = (IPointerEventSource)btn;
              int enterCount = 0, clickCount = 0;
              src.OnPointerEnter.Subscribe(_ => enterCount++);
              btn.OnClick.Subscribe(_ => clickCount++);

              // Simulate PointerEnter via ExecuteEvents (works in PlayMode without an EventSystem GO).
              var data = new PointerEventData(EventSystem.current ?? new GameObject("ES").AddComponent<EventSystem>())
              { position = btn.RectTransform.position };
              ExecuteEvents.Execute(btn.GameObject, data, ExecuteEvents.pointerEnterHandler);
              yield return null;

              Assert.AreEqual(1, enterCount, "Relay must receive pointer enter");

              // Click stream should still work too (sanity — Button isn't disrupted).
              btn.GameObject.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
              Assert.AreEqual(1, clickCount);
          }

          [UnityTest]
          public IEnumerator Image_exposes_pointer_streams()
          {
              UI.LoadDocument("t", $"{Header}<Image id='img' sprite='ui/dummy'/>{Footer}");
              var screen = UI.Open("S");
              var img = screen.Get<Image>("img");
              var src = (IPointerEventSource)img;
              int downCount = 0;
              src.OnPointerDown.Subscribe(_ => downCount++);

              var data = new PointerEventData(EventSystem.current ?? new GameObject("ES").AddComponent<EventSystem>())
              { position = img.RectTransform.position };
              ExecuteEvents.Execute(img.GameObject, data, ExecuteEvents.pointerDownHandler);
              yield return null;

              Assert.AreEqual(1, downCount);
          }
      }
  }
  ```

- [ ] **Step 2.2: Run tests, verify they fail**

  ```
  ToolSearch(query="select:refresh_unity,run_tests,read_console", max_results=3)
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: compile error because `Btn` and `Image` don't implement `IPointerEventSource` yet.

- [ ] **Step 2.3: Add `IPointerEventSource` to `Btn.cs`**

  Read `Runtime/Controls/Btn.cs` to understand the current structure (it inherits Control, has private fields `_bg / _btn / _autoLabel / _click`, an `OnClick` Observable property, and an `OnAttached` method).

  Modify the class declaration to add the interface, and add the new members. The new code goes near the other private fields and Observable getters:

  ```csharp
  public sealed class Btn : Control, IPointerEventSource
  {
      // ... existing fields ...
      private Internal.PointerEventRelay _pointerRelay;

      private Internal.PointerEventRelay EnsureRelay()
          => _pointerRelay ??= GameObject.AddComponent<Internal.PointerEventRelay>();

      public Observable<Unit> OnPointerEnter => EnsureRelay().OnPointerEnter;
      public Observable<Unit> OnPointerExit  => EnsureRelay().OnPointerExit;
      public Observable<Unit> OnPointerDown  => EnsureRelay().OnPointerDown;

      // ... rest unchanged ...
  }
  ```

  Make sure `using PromptUGUI.Controls.Internal;` is in the file's imports if `IPointerEventSource` resolves cleanly with `Internal.` prefix used inline.

- [ ] **Step 2.4: Add `IPointerEventSource` to `Image.cs`**

  Read `Runtime/Controls/Image.cs` to understand current structure (it inherits Control, has private `_img` UnityEngine.UI.Image field, and `[UIAttr]` properties for sprite/color/type).

  Same pattern as Btn:

  ```csharp
  public sealed class Image : Control, IPointerEventSource
  {
      // ... existing fields ...
      private Internal.PointerEventRelay _pointerRelay;

      private Internal.PointerEventRelay EnsureRelay()
          => _pointerRelay ??= GameObject.AddComponent<Internal.PointerEventRelay>();

      public Observable<Unit> OnPointerEnter => EnsureRelay().OnPointerEnter;
      public Observable<Unit> OnPointerExit  => EnsureRelay().OnPointerExit;
      public Observable<Unit> OnPointerDown  => EnsureRelay().OnPointerDown;

      // ... rest unchanged ...
  }
  ```

  Ensure `using R3;` is present for `Observable<Unit>`.

- [ ] **Step 2.5: Refresh + run tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="PointerEventRelayTests")
  ```

  Expected: 2 tests PASS.

  If `Btn_exposes_pointer_streams_and_Button_still_works` FAILS (specifically: enterCount=0), this confirms the **spec Risk #1 is real** — Unity Button captures pointer enter and somehow blocks the relay. STOP and report BLOCKED with concrete error so the controller can revise the design.

- [ ] **Step 2.6: Run full PlayMode + EditMode suites for regression**

  ```
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
  mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
  ```

  Expected: EditMode 522 PASS (unchanged); PlayMode 97 PASS (95 + 2 new).

- [ ] **Step 2.7: Lint + Commit**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  ```bash
  git add Runtime/Controls/Btn.cs Runtime/Controls/Image.cs \
          Tests/PlayMode/Controls/PointerEventRelayTests.cs \
          Tests/PlayMode/Controls/PointerEventRelayTests.cs.meta
  git commit -m "$(cat <<'EOF'
  feat: Btn + Image implement IPointerEventSource via lazy PointerEventRelay

  Smoke-tested coexistence with Unity Button — both Button (state change) and
  Relay (stream emit) receive pointer events. Confirms spec Risk #1 is benign.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 3: `TriggerKind` extension + `TriggerSpec.Parse` new values

**Files:**
- Modify: `Runtime/Controls/Internal/TriggerSpec.cs`
- Modify: `Tests/EditMode/Controls/TriggerSpecTests.cs` (append parse tests)

- [ ] **Step 3.1: Append failing parse tests**

  Append to `Tests/EditMode/Controls/TriggerSpecTests.cs` (inside the existing class):

  ```csharp
  [Test] public void Hover_enter_parses() =>
      Assert.AreEqual(TriggerKind.HoverEnter, TriggerSpec.Parse("hover-enter").Kind);

  [Test] public void Hover_exit_parses() =>
      Assert.AreEqual(TriggerKind.HoverExit, TriggerSpec.Parse("hover-exit").Kind);

  [Test] public void Press_parses() =>
      Assert.AreEqual(TriggerKind.Press, TriggerSpec.Parse("press").Kind);

  [Test] public void Hover_enter_with_id_parses()
  {
      var spec = TriggerSpec.Parse("hover-enter@btn");
      Assert.AreEqual(TriggerKind.HoverEnter, spec.Kind);
      Assert.AreEqual("btn", spec.SourceId);
  }

  [Test] public void Hover_exit_with_id_parses()
  {
      var spec = TriggerSpec.Parse("hover-exit@img");
      Assert.AreEqual(TriggerKind.HoverExit, spec.Kind);
      Assert.AreEqual("img", spec.SourceId);
  }

  [Test] public void Press_with_id_parses()
  {
      var spec = TriggerSpec.Parse("press@btn");
      Assert.AreEqual(TriggerKind.Press, spec.Kind);
      Assert.AreEqual("btn", spec.SourceId);
  }

  [Test] public void Pointer_with_empty_id_throws()
  {
      Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("hover-enter@"));
      Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("hover-exit@"));
      Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("press@"));
  }

  [Test] public void Pointer_with_double_at_throws()
  {
      Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("hover-enter@a@b"));
      Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("press@x@y"));
  }
  ```

- [ ] **Step 3.2: Run tests, verify they fail**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: compile error because `TriggerKind.HoverEnter` / `HoverExit` / `Press` don't exist yet.

- [ ] **Step 3.3: Extend `TriggerKind` enum**

  Edit `Runtime/Controls/Internal/TriggerSpec.cs`. Change the enum from:

  ```csharp
  internal enum TriggerKind { Open, Loop, Click, Manual }
  ```

  To:

  ```csharp
  internal enum TriggerKind { Open, Loop, Click, Manual, HoverEnter, HoverExit, Press }
  ```

  Order matters only for default value (Open=0) — append new values at the end for ABI stability.

- [ ] **Step 3.4: Extend `TriggerSpec.Parse`**

  Replace the `Parse` method with:

  ```csharp
  public static TriggerSpec Parse(string value)
  {
      if (string.IsNullOrEmpty(value)) return new TriggerSpec { Kind = TriggerKind.Open };
      switch (value)
      {
          case "open":        return new TriggerSpec { Kind = TriggerKind.Open };
          case "loop":        return new TriggerSpec { Kind = TriggerKind.Loop };
          case "manual":      return new TriggerSpec { Kind = TriggerKind.Manual };
          case "click":       return new TriggerSpec { Kind = TriggerKind.Click };
          case "hover-enter": return new TriggerSpec { Kind = TriggerKind.HoverEnter };
          case "hover-exit":  return new TriggerSpec { Kind = TriggerKind.HoverExit };
          case "press":       return new TriggerSpec { Kind = TriggerKind.Press };
      }
      foreach (var (prefix, kind) in s_prefixedKinds)
      {
          if (value.StartsWith(prefix))
          {
              var id = value.Substring(prefix.Length);
              if (string.IsNullOrEmpty(id) || id.Contains('@'))
                  throw new ArgumentException(
                      $"Invalid trigger source id in 'on=\"{value}\"' — expected '<prefix>@<id>' with non-empty single id");
              return new TriggerSpec { Kind = kind, SourceId = id };
          }
      }
      throw new ArgumentException(
          $"Invalid trigger 'on=\"{value}\"' — expected one of: open / loop / click / click@<id> / " +
          "hover-enter / hover-enter@<id> / hover-exit / hover-exit@<id> / press / press@<id> / manual");
  }

  private static readonly (string prefix, TriggerKind kind)[] s_prefixedKinds = {
      ("click@",       TriggerKind.Click),
      ("hover-enter@", TriggerKind.HoverEnter),
      ("hover-exit@",  TriggerKind.HoverExit),
      ("press@",       TriggerKind.Press),
  };
  ```

  Note: the existing `Parse` already handles `click@<id>` inline; this refactor unifies all `<prefix>@<id>` cases into the table-driven loop. The behavior for `click@<id>` is preserved.

- [ ] **Step 3.5: Refresh + run tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TriggerSpecTests")
  ```

  Expected: 15 tests PASS (7 from Task 3 of LitMotion plan + 8 new).

  Also run the full EditMode suite to verify the `Parse` refactor didn't break the existing `click@<id>` cases:

  ```
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
  ```

  Expected: 530 PASS (522 + 8 new).

- [ ] **Step 3.6: Lint + Commit**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  ```bash
  git add Runtime/Controls/Internal/TriggerSpec.cs \
          Tests/EditMode/Controls/TriggerSpecTests.cs
  git commit -m "$(cat <<'EOF'
  feat: TriggerSpec parses hover-enter / hover-exit / press (+ @id forms)

  TriggerKind enum extended with 3 new values. Parse switches to a
  table-driven prefix matcher for all @id forms, preserving click@<id>
  semantics.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 4: `TriggerSourceResolver.FindPointerSource` + resolver tests

**Files:**
- Modify: `Runtime/Controls/Internal/TriggerSourceResolver.cs`
- Modify: `Tests/EditMode/Controls/TriggerTests.cs` (append 4 resolver tests)

The resolver gets a new method `FindPointerSource` that walks Trigger's subtree for `IPointerEventSource` descendants (Btn or Image). Same disambiguation rules as `FindBtn`.

- [ ] **Step 4.1: Write failing tests**

  Append to `Tests/EditMode/Controls/TriggerTests.cs` (inside the existing class):

  ```csharp
  [Test] public void Pointer_trigger_subtree_unique_Btn_resolves()
  {
      // No assertion on event firing here (that's PlayMode in Task 6).
      // We just confirm parsing + resolution + subscription don't throw.
      Assert.DoesNotThrow(() =>
      {
          UI.LoadDocument("t", $"{Header}" +
              "<Trigger id='t' on='hover-enter'><Btn id='b'>OK</Btn></Trigger>" +
              $"{Footer}");
          UI.Open("S");
      });
  }

  [Test] public void Pointer_trigger_subtree_unique_Image_resolves()
  {
      Assert.DoesNotThrow(() =>
      {
          UI.LoadDocument("t", $"{Header}" +
              "<Trigger id='t' on='press'><Image id='i' sprite='ui/dummy'/></Trigger>" +
              $"{Footer}");
          UI.Open("S");
      });
  }

  [Test] public void Pointer_trigger_subtree_multiple_sources_no_id_throws()
  {
      Assert.That(() =>
      {
          UI.LoadDocument("t", $"{Header}" +
              "<Trigger id='t' on='hover-enter'>" +
              "  <Btn id='b'>OK</Btn>" +
              "  <Image id='i' sprite='ui/dummy'/>" +
              "</Trigger>" +
              $"{Footer}");
          UI.Open("S");
      }, Throws.InstanceOf<System.Exception>());
  }

  [Test] public void Pointer_trigger_at_id_pointing_to_Text_throws()
  {
      Assert.That(() =>
      {
          UI.LoadDocument("t", $"{Header}" +
              "<Trigger id='t' on='press@label'>" +
              "  <Text id='label'>hi</Text>" +
              "</Trigger>" +
              $"{Footer}");
          UI.Open("S");
      }, Throws.InstanceOf<System.Exception>());
  }
  ```

- [ ] **Step 4.2: Run tests, verify they fail**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TriggerTests")
  ```

  Expected: 4 new tests FAIL with `NotImplementedException` from `Trigger.SubscribePointer` (which doesn't exist yet, or with whatever error surfaces). Actually they will fail at the `UI.Open("S")` step inside `Trigger.InitTriggerSubscription` reaching the `HoverEnter` / `Press` cases that don't exist yet. Either compile error or runtime exception — both count as fail.

- [ ] **Step 4.3: Add `FindPointerSource` to `TriggerSourceResolver`**

  Edit `Runtime/Controls/Internal/TriggerSourceResolver.cs`. Add a new public static method alongside the existing `FindBtn`:

  ```csharp
  public static IPointerEventSource FindPointerSource(Trigger trigger, string sourceId)
  {
      if (!string.IsNullOrEmpty(sourceId))
      {
          if (!trigger.ScopedIds.TryGetValue(sourceId, out var ctrl))
              throw new InvalidOperationException(
                  $"<Trigger on=\"...@{sourceId}\"> in '{trigger.Id ?? trigger.GameObject.name}': " +
                  $"id '{sourceId}' not found in trigger subtree scope");
          return ctrl as IPointerEventSource ?? throw new InvalidOperationException(
              $"<Trigger on=\"...@{sourceId}\">: id '{sourceId}' is a " +
              $"{ctrl.GetType().Name}, not supported as pointer event source. Use <Btn> or <Image>.");
      }

      var found = new List<IPointerEventSource>();
      CollectPointerSources(trigger, found);
      if (found.Count == 0)
          throw new InvalidOperationException(
              $"<Trigger> in '{trigger.Id ?? trigger.GameObject.name}': " +
              "no <Btn> or <Image> found in subtree. Add one or use ...@<id>.");
      if (found.Count > 1)
          throw new InvalidOperationException(
              $"<Trigger> in '{trigger.Id ?? trigger.GameObject.name}': " +
              $"ambiguous — found {found.Count} pointer-event-source descendants. " +
              "Use on=\"...@<id>\" to disambiguate.");
      return found[0];
  }

  private static void CollectPointerSources(IControl c, List<IPointerEventSource> outList)
  {
      foreach (var child in c.Children)
      {
          if (child is IPointerEventSource src)
          {
              outList.Add(src);
              // Source nodes (Btn / Image) are leaves for traversal — same rule as CollectBtns.
          }
          else if (child is Control cc)
          {
              CollectPointerSources(cc, outList);
          }
      }
  }
  ```

  Note: this task only adds the resolver. `Trigger.SubscribePointer` (which calls into this) is added in Task 5. The tests will still fail at this point because Trigger's switch doesn't handle the new TriggerKind values.

- [ ] **Step 4.4: Refresh + verify build is clean (tests still fail — expected)**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  ```

  Expected: 0 compile errors. Tests added in Step 4.1 will still fail since Trigger doesn't subscribe yet — that's fine, the resolver alone has no exercising path. We'll verify both in Task 5.

- [ ] **Step 4.5: Lint + Commit**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  ```bash
  git add Runtime/Controls/Internal/TriggerSourceResolver.cs \
          Tests/EditMode/Controls/TriggerTests.cs
  git commit -m "$(cat <<'EOF'
  feat: TriggerSourceResolver.FindPointerSource — Btn/Image subtree walk

  Mirror of FindBtn for IPointerEventSource. Resolves source by id or unique
  descendant; throws on ambiguity / wrong type / not found.

  Tests are appended but still fail (Trigger.SubscribePointer not yet wired —
  Task 5).

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 5: `Trigger.InitTriggerSubscription` extension + `SubscribePointer`

**Files:**
- Modify: `Runtime/Controls/Trigger.cs`

This task wires the resolver into `Trigger.InitTriggerSubscription`. Task 4's failing tests should pass after this.

- [ ] **Step 5.1: Extend `Trigger.InitTriggerSubscription`**

  Read `Runtime/Controls/Trigger.cs` first. The existing `InitTriggerSubscription` is a switch on `_spec.Kind`. Modify to handle the 3 new kinds. Replace:

  ```csharp
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
              break;
      }
  }
  ```

  With:

  ```csharp
  protected virtual void InitTriggerSubscription()
  {
      switch (_spec.Kind)
      {
          case Internal.TriggerKind.Open:
          case Internal.TriggerKind.Loop:
              Fire();
              break;
          case Internal.TriggerKind.Click:
              SubscribeClick();
              break;
          case Internal.TriggerKind.HoverEnter:
          case Internal.TriggerKind.HoverExit:
          case Internal.TriggerKind.Press:
              SubscribePointer(_spec.Kind);
              break;
          case Internal.TriggerKind.Manual:
              break;
      }
  }
  ```

- [ ] **Step 5.2: Add `SubscribePointer` private method**

  Add to `Trigger.cs` (next to `SubscribeClick`):

  ```csharp
  private void SubscribePointer(Internal.TriggerKind kind)
  {
      var src = Internal.TriggerSourceResolver.FindPointerSource(this, _spec.SourceId);
      var stream = kind switch
      {
          Internal.TriggerKind.HoverEnter => src.OnPointerEnter,
          Internal.TriggerKind.HoverExit  => src.OnPointerExit,
          Internal.TriggerKind.Press      => src.OnPointerDown,
          _ => throw new System.InvalidOperationException("unreachable"),
      };
      _sourceSub = stream.Subscribe(_ => Fire());
  }
  ```

- [ ] **Step 5.3: Refresh + run all Trigger-related tests**

  ```
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TriggerTests")
  ```

  Expected: 13 tests PASS in TriggerTests (9 from prior LitMotion work + 4 new from Task 4 of this plan, all now passing).

  Run full EditMode suite to confirm no regression:

  ```
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
  ```

  Expected: 530 PASS (522 baseline + 8 TriggerSpec + 4 TriggerTests = 534... wait, let me recount). Baseline at start of this plan: 522. Task 3 added 8 (TriggerSpecTests). Task 4 added 4 (TriggerTests). So expected: 522 + 8 + 4 = 534.

- [ ] **Step 5.4: Lint + Commit**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  ```bash
  git add Runtime/Controls/Trigger.cs
  git commit -m "$(cat <<'EOF'
  feat: Trigger subscribes to hover-enter / hover-exit / press events

  InitTriggerSubscription routes the 3 new TriggerKinds to SubscribePointer,
  which calls TriggerSourceResolver.FindPointerSource + subscribes to the
  matching Observable on the resolved IPointerEventSource.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 6: PlayMode tests — pointer events actually fire triggers

**Files:**
- Create: `Tests/PlayMode/Controls/PointerTriggerPlayTests.cs`

EditMode tests only cover parse + resolution. PlayMode tests use Unity's `ExecuteEvents.Execute` to simulate pointer events and verify the OnFire → Animation chain actually fires.

- [ ] **Step 6.1: Write the failing PlayMode tests**

  Create `Tests/PlayMode/Controls/PointerTriggerPlayTests.cs`:

  ```csharp
  using System.Collections;
  using NUnit.Framework;
  using PromptUGUI.Application;
  using PromptUGUI.Controls;
  using R3;
  using UnityEngine;
  using UnityEngine.EventSystems;
  using UnityEngine.TestTools;

  namespace PromptUGUI.Tests.PlayMode.Controls
  {
      public class PointerTriggerPlayTests
      {
          private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
              "<PromptUGUI version='1'><Screen name='S'>";
          private const string Footer = "</Screen></PromptUGUI>";

          [SetUp]    public void SetUp()    => UI.ResetForTests();
          [TearDown] public void TearDown() => UI.ResetForTests();

          private static PointerEventData NewEventData()
          {
              var es = EventSystem.current ?? new GameObject("ES").AddComponent<EventSystem>();
              return new PointerEventData(es);
          }

          [UnityTest]
          public IEnumerator HoverEnter_fires_on_Btn_pointer_enter()
          {
              UI.LoadDocument("t", $"{Header}" +
                  "<Trigger id='t' on='hover-enter'><Btn id='b'>OK</Btn></Trigger>" +
                  $"{Footer}");
              var screen = UI.Open("S");
              int fires = 0;
              screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
              var btn = screen.Get<Btn>("t/b");

              ExecuteEvents.Execute(btn.GameObject, NewEventData(), ExecuteEvents.pointerEnterHandler);
              yield return null;

              Assert.AreEqual(1, fires);
          }

          [UnityTest]
          public IEnumerator HoverExit_fires_on_Btn_pointer_exit()
          {
              UI.LoadDocument("t", $"{Header}" +
                  "<Trigger id='t' on='hover-exit'><Btn id='b'>OK</Btn></Trigger>" +
                  $"{Footer}");
              var screen = UI.Open("S");
              int fires = 0;
              screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
              var btn = screen.Get<Btn>("t/b");

              ExecuteEvents.Execute(btn.GameObject, NewEventData(), ExecuteEvents.pointerExitHandler);
              yield return null;

              Assert.AreEqual(1, fires);
          }

          [UnityTest]
          public IEnumerator Press_fires_on_Btn_pointer_down()
          {
              UI.LoadDocument("t", $"{Header}" +
                  "<Trigger id='t' on='press'><Btn id='b'>OK</Btn></Trigger>" +
                  $"{Footer}");
              var screen = UI.Open("S");
              int fires = 0;
              screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
              var btn = screen.Get<Btn>("t/b");

              ExecuteEvents.Execute(btn.GameObject, NewEventData(), ExecuteEvents.pointerDownHandler);
              yield return null;

              Assert.AreEqual(1, fires);
          }

          [UnityTest]
          public IEnumerator HoverEnter_fires_on_Image_pointer_enter()
          {
              UI.LoadDocument("t", $"{Header}" +
                  "<Trigger id='t' on='hover-enter'><Image id='i' sprite='ui/dummy'/></Trigger>" +
                  $"{Footer}");
              var screen = UI.Open("S");
              int fires = 0;
              screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
              var img = screen.Get<Image>("t/i");

              ExecuteEvents.Execute(img.GameObject, NewEventData(), ExecuteEvents.pointerEnterHandler);
              yield return null;

              Assert.AreEqual(1, fires);
          }

          [UnityTest]
          public IEnumerator Press_fires_on_Image_pointer_down()
          {
              UI.LoadDocument("t", $"{Header}" +
                  "<Trigger id='t' on='press'><Image id='i' sprite='ui/dummy'/></Trigger>" +
                  $"{Footer}");
              var screen = UI.Open("S");
              int fires = 0;
              screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
              var img = screen.Get<Image>("t/i");

              ExecuteEvents.Execute(img.GameObject, NewEventData(), ExecuteEvents.pointerDownHandler);
              yield return null;

              Assert.AreEqual(1, fires);
          }

          [UnityTest]
          public IEnumerator Press_triggers_Animation_scale_change()
          {
              // End-to-end: <Animation> with on="press" actually starts a LitMotion tween
              // when the Btn receives PointerDown.
              UI.LoadDocument("t", $"{Header}" +
                  "<Animation id='a' scale='1:0.5' duration='0.1s' on='press'>" +
                  "  <Btn id='b'>OK</Btn>" +
                  "</Animation>" +
                  $"{Footer}");
              var screen = UI.Open("S");
              var anim = screen.Get<Animation>("a");
              var btn = screen.Get<Btn>("a/b");

              ExecuteEvents.Execute(btn.GameObject, NewEventData(), ExecuteEvents.pointerDownHandler);
              yield return new WaitForSeconds(0.2f);

              var proxy = (RectTransform)anim.GameObject.transform.Find("_offsetProxy");
              Assert.AreEqual(0.5f, proxy.localScale.x, 0.01f);
          }
      }
  }
  ```

  Note the `using Animation = PromptUGUI.Controls.Animation;` is NOT in this file — only the end-to-end test references `<Animation>` and even then via `screen.Get<Animation>(...)`. C# resolves `Animation` from `PromptUGUI.Controls` because `using PromptUGUI.Controls;` is at the top. But if `using UnityEngine;` is also there (which it is — for `GameObject` / `Vector2`), the ambiguity reappears. Add the alias:

  Change the using block at the top to include:

  ```csharp
  using Animation = PromptUGUI.Controls.Animation;
  ```

- [ ] **Step 6.2: Run tests, verify they fail or pass**

  ```
  ToolSearch(query="select:refresh_unity,run_tests,read_console", max_results=3)
  mcp__UnityMCP__refresh_unity(compile="request", mode="standard", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="PointerTriggerPlayTests")
  ```

  Expected: all 6 tests PASS — the implementation is already complete from Tasks 1-5. This is the integration / end-to-end verification.

  If any test fails:
  - Look at `read_console(types=["error"])` for runtime errors
  - The most likely failure mode is `ExecuteEvents.Execute` not dispatching to the GameObject — check that the GameObject has the right component (PointerEventRelay should be auto-added on first `OnPointerEnter` access; if not, the test misses subscribe → relay-creation timing). If so, force-create the relay by subscribing BEFORE the ExecuteEvents call (which the test does — subscribe is part of Trigger.OnAfterApply at instantiation time, before the test does ExecuteEvents).

- [ ] **Step 6.3: Run full PlayMode suite for regression**

  ```
  mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
  ```

  Expected: 103 PASS (95 baseline + 2 from PointerEventRelayTests in Task 2 + 6 from PointerTriggerPlayTests).

- [ ] **Step 6.4: Lint + Commit**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

  ```bash
  git add Tests/PlayMode/Controls/PointerTriggerPlayTests.cs \
          Tests/PlayMode/Controls/PointerTriggerPlayTests.cs.meta
  git commit -m "$(cat <<'EOF'
  test: PlayMode coverage for hover-enter / hover-exit / press triggers

  Uses ExecuteEvents.Execute to simulate pointer events on Btn and Image
  targets. End-to-end test verifies an <Animation on="press"> actually
  starts the LitMotion tween on Btn pointer-down.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 7: SKILL.md updates

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`

The XML authoring skill needs the new `on=` values + the event-source constraints + the raycastTarget caveats. C# scripting skill needs no changes (no new public C# API surface — all driven from XML via existing `OnFire`).

- [ ] **Step 7.1: Find the existing Triggers section**

  Read `.claude/skills/authoring-promptugui-xml/SKILL.md`. Find the "Triggers and Animations" chapter (added in v1 LitMotion Task 15). Locate the `on=` value table.

- [ ] **Step 7.2: Extend the `on=` value table**

  The existing table likely has 5 rows: open / loop / click / click@id / manual. Add 6 new rows (3 bare + 3 with @id):

  ```markdown
  | `hover-enter`     | 指针进入子树事件源（uGUI `IPointerEnterHandler`）。事件源默认 = 子树里唯一的 `<Btn>` 或 `<Image>`           |
  | `hover-enter@<id>`| 同上，但指定子树里的 `<id>` 为事件源（必须是 `<Btn>` 或 `<Image>`）                                          |
  | `hover-exit`      | 指针离开事件源 (`IPointerExitHandler`)。同 hover-enter 的源规则                                              |
  | `hover-exit@<id>` | 同上                                                                                                          |
  | `press`           | 指针按下瞬间 (`IPointerDownHandler`)。按下时触发一次；松开 / 长按 v2 再加                                    |
  | `press@<id>`      | 同上                                                                                                          |
  ```

  Match the existing table's column widths and tone.

- [ ] **Step 7.3: Add an event-source range note**

  After the table (or as a sub-bullet), add:

  ```markdown
  **可作 pointer event 源的控件**：`<Btn>`、`<Image>`。`<Btn>` 内置 `raycastTarget=true`；`<Image>` 默认也是 true。其他控件——`<Icon>`（硬编码 `raycastTarget=false`）、`<Text>`（默认 false）、`<Frame>`（无 Graphic）——作为 `@<id>` 引用会运行时报错"not supported as pointer event source. Use <Btn> or <Image>."

  **不要**给 `<Image raycastTarget="false">` 手动关掉 raycast 又作为 hover/press trigger 源——pointer event 不会 dispatch 到这个 GameObject，trigger 永远不会 fire（无报错，靠肉眼调试发现）。
  ```

- [ ] **Step 7.4: Add `click` vs `press` 区分说明**

  Add a separate note explaining when to use which:

  ```markdown
  **`click` vs `press` 区分**：
  - `click` 走 Unity Button 内置的 `onClick`（含 drag-cancel / disabled state 抑制等正确按钮交互语义），**只支持 `<Btn>`**。
  - `press` 走 `IPointerDownHandler`，是按下瞬间的原始事件，**支持 `<Btn>` 和 `<Image>`**。
  - 按钮点击反馈用 `click`；按下瞬间立即视觉反馈（scale 0.95 这类）用 `press`。
  ```

- [ ] **Step 7.5: Update uGUI 对照表**

  Find the uGUI 对照表 (the table that maps `<Tag>` → uGUI components). The `<Btn>` row currently lists `Image` + `Button`; add `PointerEventRelay` (lazy) to the auto-attached components note. Same for `<Image>` row — add `PointerEventRelay` (lazy) note.

  Edit the existing rows for `<Btn>` and `<Image>` — append after their existing component listing:

  ```markdown
  + (lazy) `PointerEventRelay` 当 trigger 引用为 hover-enter / hover-exit / press 源时挂上
  ```

  Match the column widths so the table doesn't break.

- [ ] **Step 7.6: Verify the file structure is intact**

  Read the file end-to-end. Confirm:
  - YAML frontmatter at line 1 is intact (`---`)
  - Existing sections (Triggers and Animations chapter, uGUI 对照表) still parse as markdown
  - No accidentally broken tables (column count consistency)

- [ ] **Step 7.7: Commit**

  ```bash
  git add .claude/skills/authoring-promptugui-xml/SKILL.md
  git commit -m "$(cat <<'EOF'
  doc: SKILL.md adds hover-enter / hover-exit / press to on= DSL

  - on= value table: 6 new rows (bare + @id forms)
  - Event source scope: Btn + Image; explicit caveats for raycastTarget=false
    and Icon/Text/Frame being excluded.
  - click vs press 区分 note.
  - uGUI 对照表: Btn + Image now show (lazy) PointerEventRelay attachment.

  Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
  EOF
  )"
  ```

---

## Task 8: Final verification

- [ ] **Step 8.1: Full test suite**

  ```
  ToolSearch(query="select:refresh_unity,run_tests,read_console", max_results=3)
  mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
  mcp__UnityMCP__read_console(action="get", types=["error"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
  mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
  mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
  ```

  Expected counts:
  - EditMode: 534 PASS (522 baseline + 8 TriggerSpec parse + 4 TriggerTests resolver = 534)
  - EditorOnly: 146 PASS (unchanged)
  - PlayMode: 103 PASS (95 baseline + 2 PointerEventRelay smoke + 6 PointerTrigger PlayMode = 103)
  - Grand total: 783 PASS

- [ ] **Step 8.2: Final lint**

  ```bash
  cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
  cd .lint && dotnet format style PromptUGUI.Lint.slnx
  cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
  ```

- [ ] **Step 8.3: Branch commit summary**

  ```bash
  git log --oneline main..HEAD
  ```

  Capture the output and report. The pointer-event commits should append to the existing v1 LitMotion commit list — total commits since branching from main include both v1 work and this extension.

- [ ] **Step 8.4: Report**

  Report final test counts vs. expected and the new commit SHAs. Branch is ready for the next step (more features, PR, etc.).
