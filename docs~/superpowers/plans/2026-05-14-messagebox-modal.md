# MessageBox Modal Dialog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a generic modal dialog system (`UI.Modal` queue + `ModalRequest<TResult>` extension point) with a builtin `MessageBox` as the default modal type.

**Architecture:** Each modal is a regular `Screen` that goes through the existing PromptUGUI pipeline (`LoadDocument` → `Open`). `UI.Modal` adds three things on top: a FIFO queue with `AwaitableCompletionSource<TResult>` bridging, escape-key listening (dual-rail New/Legacy Input System), and forced high `sortingOrder`. Modals queue up; FIFO pump runs them one at a time.

**Tech Stack:** Unity 6+, R3 (already present), Unity Input System (optional, gated by `ENABLE_INPUT_SYSTEM`), NUnit (EditMode + PlayMode).

**Spec:** [`docs~/superpowers/specs/2026-05-14-messagebox-modal-design.md`](../specs/2026-05-14-messagebox-modal-design.md)

---

## File Structure

**New files (Runtime):**

| Path | Responsibility |
|---|---|
| `Runtime/Application/Modals/Btn.cs` | `[Flags] enum Btn` (OK/Cancel/Yes/No/Close) |
| `Runtime/Application/Modals/ModalRequest.cs` | Abstract `ModalRequest<TResult>` base |
| `Runtime/Application/Modals/ModalSourceLoader.cs` | Loads modal XML from package Resources vs caller `UI.SourceResolver` |
| `Runtime/Application/Modals/ModalEscapeListener.cs` | MonoBehaviour with dual-rail ESC subscription |
| `Runtime/Application/Modals/UI.Modal.cs` | `partial class UI` adding nested `Modal` static class — queue, pump, sortingOrder, teardown hook |
| `Runtime/Application/Modals/MessageBoxRequest.cs` | Builtin `ModalRequest<Btn>` implementation |
| `Runtime/Application/Modals/MessageBox.cs` | Static wrapper with `Open(text, buttons, icon, title)` overloads |
| `Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml` | Default modal layout |

**Modified files:**

| Path | Change |
|---|---|
| `Runtime/Controls/Btn.cs` | Add `internal void SimulateClick()` |
| `Runtime/Application/UI.cs` | Add internal `UnloadDocument(name)`; ensure `ResetForTests` and `UnloadAll` call `Modal.CancelAllForTeardown()` first |
| `.claude/skills/scripting-promptugui-csharp/SKILL.md` | Add "Modal dialogs" section |

**New test files:**

| Path | Purpose |
|---|---|
| `Tests/EditMode/Modals/ModalTestFixture.cs` | Shared `[SetUp]` / `[TearDown]` boilerplate |
| `Tests/EditMode/Modals/BtnFlagsTests.cs` | enum bitwise behavior |
| `Tests/EditMode/Modals/ModalSourceLoaderTests.cs` | builtin path + caller resolver path |
| `Tests/EditMode/Modals/UIUnloadDocumentTests.cs` | `UI.UnloadDocument` helper |
| `Tests/EditMode/Modals/ModalQueueTests.cs` | OpenAsync / pump / FIFO / sortingOrder |
| `Tests/EditMode/Modals/ModalCancelTests.cs` | CloseAll / ResetForTests / UnloadAll |
| `Tests/EditMode/Modals/MessageBoxRequestTests.cs` | Bind behavior, TryEscape, custom labels |
| `Tests/EditMode/Modals/MessageBoxStaticTests.cs` | Static wrapper overloads |
| `Tests/EditMode/Modals/ModalReSolveTests.cs` | §6.1 ReSolve does not clobber Bind SetActive |
| `Tests/PlayMode/Modals/ModalEscapePlayModeTests.cs` | ModalEscapeListener.FireForTests + sortingOrder integration |

---

## Workflow rules

After every code edit:

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error","warning"])
```

If compile errors appear, fix before continuing. After tests pass:

```
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Run tests via:

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="<TestClassFragment>")
```

Commit after each task passes.

---

## Task 1: Btn enum

**Files:**
- Create: `Runtime/Application/Modals/Btn.cs`
- Create: `Tests/EditMode/Modals/BtnFlagsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Modals/BtnFlagsTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class BtnFlagsTests
    {
        [Test]
        public void None_is_zero() => Assert.AreEqual(0, (int)Btn.None);

        [Test]
        public void Flags_are_powers_of_two()
        {
            Assert.AreEqual(1,  (int)Btn.OK);
            Assert.AreEqual(2,  (int)Btn.Cancel);
            Assert.AreEqual(4,  (int)Btn.Yes);
            Assert.AreEqual(8,  (int)Btn.No);
            Assert.AreEqual(16, (int)Btn.Close);
        }

        [Test]
        public void Combination_yields_or_of_values()
        {
            var combo = Btn.Yes | Btn.No | Btn.Cancel;
            Assert.AreEqual(14, (int)combo);
            Assert.IsTrue((combo & Btn.Yes)    != 0);
            Assert.IsTrue((combo & Btn.No)     != 0);
            Assert.IsTrue((combo & Btn.Cancel) != 0);
            Assert.IsTrue((combo & Btn.OK)     == 0);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile error `The type or namespace name 'Btn' could not be found` (or `Modals`).

- [ ] **Step 3: Write minimal implementation**

Create `Runtime/Application/Modals/Btn.cs`:

```csharp
using System;

namespace PromptUGUI.Application.Modals
{
    [Flags]
    public enum Btn
    {
        None   = 0,
        OK     = 1,
        Cancel = 2,
        Yes    = 4,
        No     = 8,
        Close  = 16,
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__run_tests(mode="EditMode", filter="BtnFlagsTests")
```

Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/Modals/Btn.cs \
        Runtime/Application/Modals/Btn.cs.meta \
        Tests/EditMode/Modals/ \
        Tests/EditMode/Modals.meta
git commit -m "feat: add Btn flags enum for modal results"
```

---

## Task 2: ModalRequest&lt;TResult&gt; abstract base

**Files:**
- Create: `Runtime/Application/Modals/ModalRequest.cs`

This task has no test of its own (it's a pure type definition); subsequent tasks exercise it via subclasses.

- [ ] **Step 1: Write the type**

Create `Runtime/Application/Modals/ModalRequest.cs`:

```csharp
using System;
using PromptUGUI.Application;

namespace PromptUGUI.Application.Modals
{
    public abstract class ModalRequest<TResult>
    {
        public abstract string XmlSrc { get; }

        public abstract void Bind(IScreen screen, Action<TResult> close);

        public virtual bool TryEscape(out TResult result)
        {
            result = default;
            return false;
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Runtime/Application/Modals/ModalRequest.cs \
        Runtime/Application/Modals/ModalRequest.cs.meta
git commit -m "feat: add ModalRequest<TResult> abstract base"
```

---

## Task 3: ModalSourceLoader

**Files:**
- Create: `Runtime/Application/Modals/ModalSourceLoader.cs`
- Create: `Tests/EditMode/Modals/ModalSourceLoaderTests.cs`

- [ ] **Step 1: Write failing tests**

Create `Tests/EditMode/Modals/ModalSourceLoaderTests.cs`:

```csharp
using System;
using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class ModalSourceLoaderTests
    {
        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void NonBuiltin_src_uses_UI_SourceResolver()
        {
            UI.SourceResolver = src => AwaitableHelpers.Completed(src == "my/Foo" ? "<xml/>" : null);
            var xml = ModalSourceLoader.LoadAsync("my/Foo").GetAwaiter().GetResult();
            Assert.AreEqual("<xml/>", xml);
        }

        [Test]
        public void NonBuiltin_src_with_no_resolver_throws()
        {
            UI.SourceResolver = null;
            Assert.Throws<InvalidOperationException>(() =>
                ModalSourceLoader.LoadAsync("my/Foo").GetAwaiter().GetResult());
        }

        [Test]
        public void Builtin_prefix_missing_resource_throws()
        {
            // No file at Resources/PromptUGUI/Modals/Nonexistent.ui.xml
            Assert.Throws<InvalidOperationException>(() =>
                ModalSourceLoader.LoadAsync("PromptUGUI/Modals/Nonexistent").GetAwaiter().GetResult());
        }
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile error `ModalSourceLoader` not found.

- [ ] **Step 3: Implement ModalSourceLoader**

Create `Runtime/Application/Modals/ModalSourceLoader.cs`:

```csharp
using System;
using UnityEngine;

namespace PromptUGUI.Application.Modals
{
    internal static class ModalSourceLoader
    {
        public const string BuiltinPrefix = "PromptUGUI/";

        public static async Awaitable<string> LoadAsync(string src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));

            if (src.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
            {
                var req = Resources.LoadAsync<TextAsset>(src);
                await req;
                if (req.asset is not TextAsset ta)
                    throw new InvalidOperationException(
                        $"Builtin modal XML missing at Resources/{src}.ui.xml");
                return ta.text;
            }

            if (UI.SourceResolver == null)
                throw new InvalidOperationException(
                    $"UI.SourceResolver must be set to load non-builtin modal '{src}'");
            return await UI.SourceResolver(src);
        }
    }
}
```

- [ ] **Step 4: Run tests**

```
mcp__UnityMCP__run_tests(mode="EditMode", filter="ModalSourceLoaderTests")
```

Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/Modals/ModalSourceLoader.cs \
        Runtime/Application/Modals/ModalSourceLoader.cs.meta \
        Tests/EditMode/Modals/ModalSourceLoaderTests.cs \
        Tests/EditMode/Modals/ModalSourceLoaderTests.cs.meta
git commit -m "feat: ModalSourceLoader splits builtin Resources vs caller SourceResolver"
```

---

## Task 4: Controls.Btn.SimulateClick

**Files:**
- Modify: `Runtime/Controls/Btn.cs`

Subsequent tests need to simulate button clicks without an EventSystem.

- [ ] **Step 1: Add the helper**

In `Runtime/Controls/Btn.cs`, just after the `OnClick` property and before `Dispose()`, insert:

```csharp
        internal void SimulateClick() => _click.OnNext(R3.Unit.Default);
```

The current code (Runtime/Controls/Btn.cs:106-113):

```csharp
        public Observable<Unit> OnClick => _click;

        public override void Dispose()
```

Becomes:

```csharp
        public Observable<Unit> OnClick => _click;

        internal void SimulateClick() => _click.OnNext(Unit.Default);

        public override void Dispose()
```

(Note: `Unit` is already imported via `using R3;` at the top of the file.)

- [ ] **Step 2: Verify it compiles**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Runtime/Controls/Btn.cs
git commit -m "feat: add internal Btn.SimulateClick for test wiring"
```

---

## Task 5: UI.UnloadDocument helper

**Files:**
- Modify: `Runtime/Application/UI.cs`
- Create: `Tests/EditMode/Modals/UIUnloadDocumentTests.cs`

Modal hot-reload (§6.5) needs an API to drop a Screen def so the next `LoadDocument` doesn't throw "already loaded".

- [ ] **Step 1: Write failing test**

Create `Tests/EditMode/Modals/UIUnloadDocumentTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.Modals
{
    public class UIUnloadDocumentTests
    {
        private const string Xml = @"<?xml version='1.0'?><PromptUGUI version='1'>
            <Screen name='T'><Frame id='a'/></Screen>
          </PromptUGUI>";

        [SetUp]    public void SetUp()    => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void UnloadDocument_removes_screen_def()
        {
            UI.LoadDocument("T", Xml);
            UI.UnloadDocument("T");
            // re-loading should not throw "already loaded"
            UI.LoadDocument("T", Xml);
            Assert.IsNotNull(UI.Open("T"));
        }

        [Test]
        public void UnloadDocument_unknown_name_is_silent()
        {
            Assert.DoesNotThrow(() => UI.UnloadDocument("DoesNotExist"));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile error `UI.UnloadDocument` not found.

- [ ] **Step 3: Implement UnloadDocument**

In `Runtime/Application/UI.cs`, after the `LoadDocument(string label, string xml)` method (around line 264), add:

```csharp
        internal static void UnloadDocument(string screenName)
        {
            _docs.Remove(screenName);
        }
```

- [ ] **Step 4: Run tests**

```
mcp__UnityMCP__run_tests(mode="EditMode", filter="UIUnloadDocumentTests")
```

Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/UI.cs \
        Tests/EditMode/Modals/UIUnloadDocumentTests.cs \
        Tests/EditMode/Modals/UIUnloadDocumentTests.cs.meta
git commit -m "feat: internal UI.UnloadDocument for modal cache invalidation"
```

---

## Task 6: ModalEscapeListener (component skeleton)

**Files:**
- Create: `Runtime/Application/Modals/ModalEscapeListener.cs`

No test in this task; PlayMode end-to-end (Task 13) covers it via `FireForTests`.

- [ ] **Step 1: Implement the component**

Create `Runtime/Application/Modals/ModalEscapeListener.cs`:

```csharp
using System;
using UnityEngine;

namespace PromptUGUI.Application.Modals
{
    internal sealed class ModalEscapeListener : MonoBehaviour
    {
        internal Action OnEscape;

#if ENABLE_INPUT_SYSTEM
        private UnityEngine.InputSystem.InputAction _action;

        private void OnEnable()
        {
            _action = new UnityEngine.InputSystem.InputAction(
                "PromptUGUI.Modal.Escape",
                UnityEngine.InputSystem.InputActionType.Button);
            _action.AddBinding("<Keyboard>/escape");
            _action.AddBinding("<Gamepad>/start");
            _action.performed += OnPerformed;
            _action.Enable();
        }

        private void OnDisable()
        {
            if (_action == null) return;
            _action.performed -= OnPerformed;
            _action.Dispose();
            _action = null;
        }

        private void OnPerformed(UnityEngine.InputSystem.InputAction.CallbackContext _)
            => OnEscape?.Invoke();

#elif ENABLE_LEGACY_INPUT_MANAGER
        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape))
                OnEscape?.Invoke();
        }
#endif

        internal void FireForTests() => OnEscape?.Invoke();
    }
}
```

- [ ] **Step 2: Verify it compiles**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: no errors. If both `ENABLE_INPUT_SYSTEM` and `ENABLE_LEGACY_INPUT_MANAGER` are undefined the file compiles to an empty MonoBehaviour with only `FireForTests` — that's intended.

- [ ] **Step 3: Commit**

```bash
git add Runtime/Application/Modals/ModalEscapeListener.cs \
        Runtime/Application/Modals/ModalEscapeListener.cs.meta
git commit -m "feat: ModalEscapeListener dual-rail New/Legacy Input System"
```

---

## Task 7: Shared test fixture (ModalTestFixture)

**Files:**
- Create: `Tests/EditMode/Modals/ModalTestFixture.cs`

A reusable `[SetUp]` / `[TearDown]` base so subsequent test classes don't repeat boilerplate.

- [ ] **Step 1: Write the fixture**

Create `Tests/EditMode/Modals/ModalTestFixture.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public abstract class ModalTestFixture
    {
        protected Dictionary<string, string> Files;

        protected const string MinimalMboxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Box1'>
    <Image id='backdrop' anchor='stretch' color='#0000007F'/>
    <Frame id='dialog' anchor='center' size='400x200'>
      <VStack anchor='stretch' margin='16' spacing='8'>
        <Text id='title' fontSize='20'/>
        <Text id='text'  fontSize='14'/>
        <Icon id='icon'  width='32' height='32'/>
        <Btn  id='ok'>OK</Btn>
        <Btn  id='cancel'>Cancel</Btn>
        <Btn  id='yes'>Yes</Btn>
        <Btn  id='no'>No</Btn>
        <Btn  id='close'>Close</Btn>
      </VStack>
    </Frame>
  </Screen>
</PromptUGUI>";

        [SetUp]
        public virtual void SetUp()
        {
            UI.ResetForTests();
            Files = new Dictionary<string, string> { ["test/Box1"] = MinimalMboxXml };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(Files.TryGetValue(src, out var v) ? v : null);
            MessageBox.XmlSrc = "test/Box1";
        }

        [TearDown]
        public virtual void TearDown() => UI.ResetForTests();
    }
}
```

- [ ] **Step 2: Verify it compiles (will fail because MessageBox not defined yet)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile error `MessageBox` not found.

This is intentional — we'll fix it in Task 11. To unblock Tasks 8-10, temporarily comment out the `MessageBox.XmlSrc = ...` line:

```csharp
            // MessageBox.XmlSrc = "test/Box1";   // uncommented in Task 11
```

Verify compile clean now:

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Tests/EditMode/Modals/ModalTestFixture.cs \
        Tests/EditMode/Modals/ModalTestFixture.cs.meta
git commit -m "test: shared fixture for modal tests"
```

---

## Task 8: UI.Modal queue + pump (no ESC, no cancel)

**Files:**
- Create: `Runtime/Application/Modals/UI.Modal.cs`
- Create: `Tests/EditMode/Modals/ModalQueueTests.cs`

Core queue + sortingOrder + Tcs bridging. ESC and cancel paths arrive in later tasks.

- [ ] **Step 1: Write failing tests**

Create `Tests/EditMode/Modals/ModalQueueTests.cs`:

```csharp
using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.Modals
{
    public class ModalQueueTests : ModalTestFixture
    {
        private sealed class FakeRequest : ModalRequest<int>
        {
            public string Src = "test/Box1";
            public Action<IScreen, Action<int>> OnBind;
            public override string XmlSrc => Src;
            public override void Bind(IScreen screen, Action<int> close) => OnBind?.Invoke(screen, close);
        }

        [Test]
        public void OpenAsync_returns_awaitable_completed_after_close()
        {
            Action<int> capturedClose = null;
            var task = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, close) => capturedClose = close });

            Assert.IsNotNull(capturedClose, "Bind must have been called synchronously by pump");
            Assert.IsTrue(UI.Modal.IsAnyOpen);

            capturedClose(42);
            Assert.AreEqual(42, task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Second_open_queues_until_first_closes()
        {
            Action<int> close1 = null, close2 = null;
            var t1 = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close1 = c });
            var t2 = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close2 = c });

            Assert.IsNotNull(close1, "first Bind should run");
            Assert.IsNull(close2, "second Bind should NOT run until first closes");
            Assert.AreEqual(2, UI.Modal.QueuedCount);

            close1(1);
            Assert.AreEqual(1, t1.GetAwaiter().GetResult());
            Assert.IsNotNull(close2, "second Bind should now run");

            close2(2);
            Assert.AreEqual(2, t2.GetAwaiter().GetResult());
            Assert.AreEqual(0, UI.Modal.QueuedCount);
        }

        [Test]
        public void Close_double_call_is_idempotent()
        {
            Action<int> capturedClose = null;
            var task = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => capturedClose = c });
            capturedClose(7);
            capturedClose(99);              // ignored
            Assert.AreEqual(7, task.GetAwaiter().GetResult());
        }

        [Test]
        public void SortingOrder_uses_SortingOrderBase_and_overrideSorting()
        {
            UI.Modal.SortingOrderBase = 500;
            Action<int> close1 = null;
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close1 = c });

            var canvas = UI.Get("test/Box1").RootGameObject.GetComponent<UnityEngine.Canvas>();
            Assert.AreEqual(500, canvas.sortingOrder);
            Assert.IsTrue(canvas.overrideSorting,
                "overrideSorting must be true so SortingOrderBase wins over inherited canvas order");

            close1(0);
        }

        [Test]
        public void Bind_exception_dequeues_and_pumps_next()
        {
            var t1 = UI.Modal.OpenAsync(new FakeRequest {
                OnBind = (_, __) => throw new InvalidOperationException("boom"),
            });
            Action<int> close2 = null;
            var t2 = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close2 = c });

            Assert.Throws<InvalidOperationException>(() => t1.GetAwaiter().GetResult());
            Assert.IsNotNull(close2, "second modal should still pump");
            close2(7);
            Assert.AreEqual(7, t2.GetAwaiter().GetResult());
        }
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile error `UI.Modal` not found.

- [ ] **Step 3: Implement UI.Modal**

Create `Runtime/Application/Modals/UI.Modal.cs`:

```csharp
using System;
using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        public static class Modal
        {
            private interface IModalEntry
            {
                string XmlSrc { get; }
                void RunBind(IScreen screen, Action onClose);
                bool TryEscape(Action wakePump);
                void Cancel(Exception ex);
                bool Resolved { get; }
            }

            private sealed class ModalEntry<TResult> : IModalEntry
            {
                public readonly Modals.ModalRequest<TResult> Request;
                public readonly AwaitableCompletionSource<TResult> Tcs = new();
                public bool Resolved { get; private set; }

                public ModalEntry(Modals.ModalRequest<TResult> request) { Request = request; }

                public string XmlSrc => Request.XmlSrc;

                public void RunBind(IScreen screen, Action onClose)
                {
                    Request.Bind(screen, result =>
                    {
                        if (Resolved) return;
                        Resolved = true;
                        Tcs.TrySetResult(result);
                        onClose?.Invoke();
                    });
                }

                public bool TryEscape(Action wakePump)
                {
                    if (Resolved) return false;
                    if (!Request.TryEscape(out var r)) return false;
                    Resolved = true;
                    Tcs.TrySetResult(r);
                    wakePump?.Invoke();
                    return true;
                }

                public void Cancel(Exception ex)
                {
                    if (Resolved) return;
                    Resolved = true;
                    Tcs.TrySetException(ex);
                }
            }

            private static readonly Queue<IModalEntry> _queue = new();
            private static readonly HashSet<string> _loadedSrcs = new();
            private static IModalEntry _current;
            private static string _currentScreenName;
            private static AwaitableCompletionSource<bool> _currentWaiter;
            private static bool _pumping;

            public static int SortingOrderBase { get; set; } = 1000;
            public static int QueuedCount => _queue.Count + (_current != null ? 1 : 0);
            public static bool IsAnyOpen => _current != null;

            public static Awaitable<TResult> OpenAsync<TResult>(Modals.ModalRequest<TResult> request)
            {
                if (request == null) throw new ArgumentNullException(nameof(request));
                var entry = new ModalEntry<TResult>(request);
                _queue.Enqueue(entry);
                if (!_pumping) _ = PumpAsync();
                return entry.Tcs.Awaitable;
            }

            private static async Awaitable PumpAsync()
            {
                if (_pumping) return;
                _pumping = true;
                try
                {
                    while (_queue.Count > 0)
                    {
                        var entry = _queue.Dequeue();
                        _current = entry;
                        _currentScreenName = entry.XmlSrc;
                        _currentWaiter = new AwaitableCompletionSource<bool>();
                        Modals.ModalEscapeListener attachedListener = null;
                        try
                        {
                            if (!_loadedSrcs.Contains(entry.XmlSrc))
                            {
                                var xml = await Modals.ModalSourceLoader.LoadAsync(entry.XmlSrc);
                                LoadDocument(entry.XmlSrc, xml);
                                _loadedSrcs.Add(entry.XmlSrc);
                            }
                            var screen = Open(entry.XmlSrc);
                            var canvas = screen.RootGameObject.GetComponent<Canvas>();
                            canvas.overrideSorting = true;
                            canvas.sortingOrder = SortingOrderBase;
                            // (FIFO queue means only one modal is visible at once;
                            //  see spec §2 — the "stacking +1" is reserved for a future
                            //  concurrent-modal extension that v1 does not implement.)

                            var waiter = _currentWaiter;
                            var captured = entry;                       // avoid loop-var closure pitfalls
                            captured.RunBind(screen, () => waiter.TrySetResult(true));

                            attachedListener = screen.RootGameObject
                                .AddComponent<Modals.ModalEscapeListener>();
                            attachedListener.OnEscape = () =>
                                captured.TryEscape(() => waiter.TrySetResult(true));

                            await waiter.Awaitable;

                            if (entry.Resolved && _open.ContainsKey(entry.XmlSrc))
                                Close(entry.XmlSrc);
                        }
                        catch (Exception ex)
                        {
                            entry.Cancel(ex);
                            if (_open.ContainsKey(entry.XmlSrc))
                                Close(entry.XmlSrc);
                        }
                        finally
                        {
                            _current = null;
                            _currentScreenName = null;
                            _currentWaiter = null;
                            // listener is destroyed with the Screen's RootGameObject
                        }
                    }
                }
                finally { _pumping = false; }
            }

            internal static void CancelAllForTeardown()
            {
                var oce = new OperationCanceledException("Modal cancelled (UI teardown)");
                _current?.Cancel(oce);
                while (_queue.Count > 0) _queue.Dequeue().Cancel(oce);
                _currentWaiter?.TrySetResult(true);   // wake pump so its while-loop can exit
                _current = null;
                _currentScreenName = null;
                _currentWaiter = null;
                _loadedSrcs.Clear();
                // _open / Screens are torn down by the caller (UnloadAll / ResetForTests)
            }

            public static void CloseAll()
            {
                var oce = new OperationCanceledException("Modal cancelled (CloseAll)");
                _current?.Cancel(oce);
                while (_queue.Count > 0) _queue.Dequeue().Cancel(oce);
                if (_currentScreenName != null && _open.ContainsKey(_currentScreenName))
                    Close(_currentScreenName);
                _currentWaiter?.TrySetResult(true);
                _current = null;
                _currentScreenName = null;
                _currentWaiter = null;
            }

            internal static bool IsModalScreen(string screenName) =>
                _currentScreenName == screenName;
        }
    }
}
```

Key behaviors:

- **ESC listener** is `AddComponent`-ed on the modal Screen's root after `Open`; destroyed automatically when `UI.Close` destroys the GameObject. Only the currently-active modal has a listener (multi-modal stacking: lower modals don't react).
- **`_currentWaiter`** lets cancel paths (`CloseAll`, `CancelAllForTeardown`) wake the pump's inner `await waiter.Awaitable` so the `while` loop can iterate / exit.
- **`entry.Resolved`** flag is checked before `Close()` in the happy path — if the entry was cancelled (not resolved with a Btn), we skip `Close()` because cancel paths already called `Close()` or are leaving teardown to the outer flow.

- [ ] **Step 4: Run tests**

```
mcp__UnityMCP__run_tests(mode="EditMode", filter="ModalQueueTests")
```

Expected: 5 passed. If `OpenAsync_returns_awaitable_completed_after_close` fails because Bind is async, double-check that `PumpAsync` calls Bind synchronously before awaiting — it should.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/Modals/UI.Modal.cs \
        Runtime/Application/Modals/UI.Modal.cs.meta \
        Tests/EditMode/Modals/ModalQueueTests.cs \
        Tests/EditMode/Modals/ModalQueueTests.cs.meta
git commit -m "feat: UI.Modal queue + pump + sortingOrder"
```

---

## Task 9: Cancel paths (CloseAll + UI integration)

**Files:**
- Modify: `Runtime/Application/UI.cs`
- Create: `Tests/EditMode/Modals/ModalCancelTests.cs`

- [ ] **Step 1: Write failing tests**

Create `Tests/EditMode/Modals/ModalCancelTests.cs`:

```csharp
using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class ModalCancelTests : ModalTestFixture
    {
        private sealed class StuckRequest : ModalRequest<int>
        {
            public override string XmlSrc => "test/Box1";
            public override void Bind(IScreen screen, Action<int> close) { /* never close */ }
        }

        [Test]
        public void CloseAll_cancels_active_modal()
        {
            var task = UI.Modal.OpenAsync(new StuckRequest());
            Assert.IsTrue(UI.Modal.IsAnyOpen);

            UI.Modal.CloseAll();
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void CloseAll_cancels_queued_modals()
        {
            var t1 = UI.Modal.OpenAsync(new StuckRequest());
            var t2 = UI.Modal.OpenAsync(new StuckRequest());

            UI.Modal.CloseAll();
            Assert.Throws<OperationCanceledException>(() => t1.GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => t2.GetAwaiter().GetResult());
        }

        [Test]
        public void UnloadAll_cancels_pending_modals()
        {
            var t1 = UI.Modal.OpenAsync(new StuckRequest());
            var t2 = UI.Modal.OpenAsync(new StuckRequest());

            UI.UnloadAll();

            Assert.Throws<OperationCanceledException>(() => t1.GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => t2.GetAwaiter().GetResult());
        }

        [Test]
        public void ResetForTests_cancels_pending_modals()
        {
            var t1 = UI.Modal.OpenAsync(new StuckRequest());
            UI.ResetForTests();

            Assert.Throws<OperationCanceledException>(() => t1.GetAwaiter().GetResult());
        }
    }
}
```

- [ ] **Step 2: Run tests to verify partial failure**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__run_tests(mode="EditMode", filter="ModalCancelTests")
```

Expected: `CloseAll_*` pass (already implemented in Task 8); `UnloadAll_*` and `ResetForTests_*` fail.

- [ ] **Step 3: Wire cancel into UnloadAll and ResetForTests**

In `Runtime/Application/UI.cs`:

Change `UnloadAll()` (currently around line 461):

```csharp
        public static void UnloadAll()
        {
            Modal.CancelAllForTeardown();
            foreach (var s in _open.Values) s.Close();
            _open.Clear();
            _docs.Clear();
            _commonsPool.Clear();
            _depGraph.Clear();
        }
```

Change `ResetForTests()` (currently around line 507) — add `Modal.CancelAllForTeardown()` as the very first line:

```csharp
        internal static void ResetForTests()
        {
            Modal.CancelAllForTeardown();
            Locale.ResetForTestsInternal();
            // ...rest unchanged...
```

- [ ] **Step 4: Run tests**

```
mcp__UnityMCP__run_tests(mode="EditMode", filter="ModalCancelTests")
```

Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/UI.cs \
        Tests/EditMode/Modals/ModalCancelTests.cs \
        Tests/EditMode/Modals/ModalCancelTests.cs.meta
git commit -m "feat: wire Modal.CancelAllForTeardown into UnloadAll + ResetForTests"
```

---

## Task 10: Builtin MessageBox.ui.xml resource

**Files:**
- Create: `Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml`

This is a pure asset. Verify only that Resources can find it.

- [ ] **Step 1: Create the XML**

Create `Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="PromptUGUI/Modals/MessageBox" reference="1920x1080">
    <Image id="backdrop" anchor="stretch" color="#0000007F"/>

    <Frame id="dialog" anchor="center" size="640x300">
      <Image anchor="stretch" color="#202020"/>
      <VStack anchor="stretch" margin="24" spacing="12">
        <HStack height="40" spacing="12">
          <Icon id="icon" width="40" height="40"/>
          <Text  id="title" fontSize="24"/>
        </HStack>
        <Text id="text" anchor="stretch" fontSize="18"/>
        <HStack height="44" spacing="8">
          <Btn id="ok">OK</Btn>
          <Btn id="cancel">Cancel</Btn>
          <Btn id="yes">Yes</Btn>
          <Btn id="no">No</Btn>
          <Btn id="close">Close</Btn>
        </HStack>
      </VStack>
    </Frame>
  </Screen>
</PromptUGUI>
```

- [ ] **Step 2: Verify Unity imports it**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error","warning"])
```

Expected: no errors. A `.meta` file should appear next to the XML.

- [ ] **Step 3: Smoke test — Resources can find the asset**

Append to `Tests/EditMode/Modals/ModalSourceLoaderTests.cs`:

```csharp
        [Test]
        public void Builtin_MessageBox_xml_is_loadable()
        {
            var xml = ModalSourceLoader.LoadAsync("PromptUGUI/Modals/MessageBox")
                .GetAwaiter().GetResult();
            StringAssert.Contains("<Screen name=\"PromptUGUI/Modals/MessageBox\"", xml);
            StringAssert.Contains("id=\"backdrop\"", xml);
            StringAssert.Contains("id=\"ok\"", xml);
        }
```

- [ ] **Step 4: Run test**

```
mcp__UnityMCP__run_tests(mode="EditMode", filter="ModalSourceLoaderTests")
```

Expected: 4 passed (3 from Task 3 + 1 new).

- [ ] **Step 5: Commit**

```bash
git add Runtime/Resources/ \
        Tests/EditMode/Modals/ModalSourceLoaderTests.cs
git commit -m "feat: builtin MessageBox.ui.xml resource"
```

---

## Task 11: MessageBoxRequest + ReSolve SetActive verification

**Files:**
- Create: `Runtime/Application/Modals/MessageBoxRequest.cs`
- Create: `Tests/EditMode/Modals/MessageBoxRequestTests.cs`
- Create: `Tests/EditMode/Modals/ModalReSolveTests.cs`
- Modify: `Tests/EditMode/Modals/ModalTestFixture.cs` (re-enable the `MessageBox.XmlSrc =` line)

The §6.1 risk — does `ReSolve` clobber `SetActive(false)`? Plan-time decision: write the red test first, find out.

- [ ] **Step 1: Write failing tests (MessageBoxRequest behavior)**

Create `Tests/EditMode/Modals/MessageBoxRequestTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.Modals
{
    public class MessageBoxRequestTests : ModalTestFixture
    {
        [Test]
        public void Bind_only_OK_hides_other_buttons()
        {
            UI.Modal.OpenAsync(new MessageBoxRequest { Text = "hi", Buttons = Btn.OK });
            var s = UI.Get("test/Box1");

            Assert.IsTrue (s.Get<PromptUGUI.Controls.Btn>("ok").GameObject.activeSelf);
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("cancel").GameObject.activeSelf);
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("yes").GameObject.activeSelf);
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("no").GameObject.activeSelf);
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("close").GameObject.activeSelf);
        }

        [Test]
        public void Click_OK_returns_Btn_OK()
        {
            var task = UI.Modal.OpenAsync(new MessageBoxRequest { Text = "hi", Buttons = Btn.OK });
            UI.Get("test/Box1").Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(Btn.OK, task.GetAwaiter().GetResult());
        }

        [Test]
        public void Click_Cancel_returns_Btn_Cancel_when_OK_Cancel_combo()
        {
            var task = UI.Modal.OpenAsync(new MessageBoxRequest {
                Text = "hi", Buttons = Btn.OK | Btn.Cancel,
            });
            UI.Get("test/Box1").Get<PromptUGUI.Controls.Btn>("cancel").SimulateClick();
            Assert.AreEqual(Btn.Cancel, task.GetAwaiter().GetResult());
        }

        [Test]
        public void Title_null_hides_title_node()
        {
            UI.Modal.OpenAsync(new MessageBoxRequest { Text = "hi", Buttons = Btn.OK, Title = null });
            var s = UI.Get("test/Box1");
            Assert.IsFalse(s.Get<Text>("title").GameObject.activeSelf);
        }

        [Test]
        public void Title_present_shows_title_node_with_text()
        {
            UI.Modal.OpenAsync(new MessageBoxRequest {
                Text = "body", Buttons = Btn.OK, Title = "Heading",
            });
            var s = UI.Get("test/Box1");
            var title = s.Get<Text>("title");
            Assert.IsTrue(title.GameObject.activeSelf);
            Assert.AreEqual("Heading", title.TextValue);
        }

        [Test]
        public void Icon_null_hides_icon_node()
        {
            UI.Modal.OpenAsync(new MessageBoxRequest { Text = "hi", Buttons = Btn.OK, Icon = null });
            Assert.IsFalse(UI.Get("test/Box1").Get<Icon>("icon").GameObject.activeSelf);
        }

        [Test]
        public void Click_with_custom_labels_returns_mapped_flag()
        {
            var task = UI.Modal.OpenAsync(new MessageBoxRequest {
                Text = "hi",
                Buttons = Btn.OK | Btn.Cancel,
                CustomLabels = new[] { ("Retry", Btn.OK), ("Skip", Btn.Cancel) },
            });
            UI.Get("test/Box1").Get<PromptUGUI.Controls.Btn>("cancel").SimulateClick();
            Assert.AreEqual(Btn.Cancel, task.GetAwaiter().GetResult());
        }

        [Test]
        public void TryEscape_only_OK_returns_false()
        {
            var req = new MessageBoxRequest { Buttons = Btn.OK };
            Assert.IsFalse(req.TryEscape(out _));
        }

        [Test]
        public void TryEscape_priority_Cancel_over_No_over_Close()
        {
            var c = new MessageBoxRequest { Buttons = Btn.Cancel | Btn.No | Btn.Close };
            Assert.IsTrue(c.TryEscape(out var r1));
            Assert.AreEqual(Btn.Cancel, r1);

            var n = new MessageBoxRequest { Buttons = Btn.No | Btn.Close };
            Assert.IsTrue(n.TryEscape(out var r2));
            Assert.AreEqual(Btn.No, r2);

            var x = new MessageBoxRequest { Buttons = Btn.Close };
            Assert.IsTrue(x.TryEscape(out var r3));
            Assert.AreEqual(Btn.Close, r3);
        }

        [Test]
        public void Escape_via_listener_returns_negative_button()
        {
            var task = UI.Modal.OpenAsync(new MessageBoxRequest {
                Text = "x", Buttons = Btn.OK | Btn.Cancel,
            });
            var listener = UI.Get("test/Box1")
                .RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(listener, "Pump must attach ModalEscapeListener to the modal Screen root");
            listener.FireForTests();
            Assert.AreEqual(Btn.Cancel, task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Escape_via_listener_with_only_OK_does_not_resolve()
        {
            var task = UI.Modal.OpenAsync(new MessageBoxRequest {
                Text = "x", Buttons = Btn.OK,
            });
            var listener = UI.Get("test/Box1")
                .RootGameObject.GetComponent<ModalEscapeListener>();
            listener.FireForTests();
            Assert.IsTrue(UI.Modal.IsAnyOpen, "ESC on OK-only should be no-op");
            Assert.IsFalse(task.GetAwaiter().IsCompleted);
            UI.Modal.CloseAll();
        }
    }
}
```

Create `Tests/EditMode/Modals/ModalReSolveTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class ModalReSolveTests : ModalTestFixture
    {
        [Test]
        public void Bind_SetActive_false_survives_VariantStore_Changed()
        {
            UI.Modal.OpenAsync(new MessageBoxRequest { Text = "x", Buttons = Btn.OK });
            var s = UI.Get("test/Box1");
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("cancel").GameObject.activeSelf);

            UI.Variants.Set("mobile", true);   // triggers ReSolve
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("cancel").GameObject.activeSelf,
                "ReSolve must not clobber Bind's SetActive(false)");
        }
    }
}
```

- [ ] **Step 2: Un-comment the MessageBox line in ModalTestFixture**

In `Tests/EditMode/Modals/ModalTestFixture.cs`, change the `SetUp()` body so it ends with the active line:

```csharp
            MessageBox.XmlSrc = "test/Box1";
```

- [ ] **Step 3: Run tests to verify failure**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile errors `MessageBoxRequest` and `MessageBox` not found.

- [ ] **Step 4: Implement MessageBoxRequest**

Create `Runtime/Application/Modals/MessageBoxRequest.cs`:

```csharp
using System;
using System.Collections.Generic;
using R3;

namespace PromptUGUI.Application.Modals
{
    public sealed class MessageBoxRequest : ModalRequest<Btn>
    {
        public string Text;
        public Btn Buttons = Btn.OK;
        public string Icon;
        public string Title;
        public IReadOnlyList<(string label, Btn key)> CustomLabels;

        public override string XmlSrc => MessageBox.XmlSrc;

        public override void Bind(IScreen screen, Action<Btn> close)
        {
            screen.Get<PromptUGUI.Controls.Text>("text").TextValue = Text ?? "";

            var titleCtl = screen.Get<PromptUGUI.Controls.Text>("title");
            if (string.IsNullOrEmpty(Title)) titleCtl.GameObject.SetActive(false);
            else titleCtl.TextValue = Title;

            var iconCtl = screen.Get<PromptUGUI.Controls.Icon>("icon");
            if (string.IsNullOrEmpty(Icon)) iconCtl.GameObject.SetActive(false);
            else iconCtl.Name = Icon;

            BindBtn(screen, "ok",     Btn.OK,     close);
            BindBtn(screen, "cancel", Btn.Cancel, close);
            BindBtn(screen, "yes",    Btn.Yes,    close);
            BindBtn(screen, "no",     Btn.No,     close);
            BindBtn(screen, "close",  Btn.Close,  close);
        }

        public override bool TryEscape(out Btn result)
        {
            if ((Buttons & Btn.Cancel) != 0) { result = Btn.Cancel; return true; }
            if ((Buttons & Btn.No)     != 0) { result = Btn.No;     return true; }
            if ((Buttons & Btn.Close)  != 0) { result = Btn.Close;  return true; }
            result = Btn.None;
            return false;
        }

        private void BindBtn(IScreen screen, string id, Btn flag, Action<Btn> close)
        {
            var btn = screen.Get<PromptUGUI.Controls.Btn>(id);
            if ((Buttons & flag) == 0) { btn.GameObject.SetActive(false); return; }

            if (CustomLabels != null)
            {
                for (var i = 0; i < CustomLabels.Count; i++)
                {
                    var (label, key) = CustomLabels[i];
                    if (key == flag && !string.IsNullOrEmpty(label)) { btn.Text = label; break; }
                }
            }
            btn.OnClick.Subscribe(_ => close(flag)).AddTo(screen);
        }
    }

    public static class MessageBox
    {
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/MessageBox";
    }
}
```

The `MessageBox` class is intentionally stubbed here (only `XmlSrc` exists) — Task 12 fleshes out `Open(...)`.

- [ ] **Step 5: Run tests (expect ReSolve test to fail)**

```
mcp__UnityMCP__run_tests(mode="EditMode", filter="MessageBoxRequestTests")
mcp__UnityMCP__run_tests(mode="EditMode", filter="ModalReSolveTests")
```

Expected MessageBoxRequestTests: 11 passed.

Expected ModalReSolveTests: **1 failed**. The §6.1 risk is confirmed by inspection of `Runtime/Controls/Control.cs:213` (`Hidden = hidden;` is unconditional in `ApplyCommon`) and `Runtime/Application/ControlAttributeApplier.cs:67,72` (`hidden = hiddenStr == "true"` defaults to false when XML omits `hidden=`, then is force-written). Every `ReSolve` re-activates all nodes.

- [ ] **Step 6: Make ApplyCommon's Hidden write conditional on XML/Variant declaration**

`Control.ApplyCommon` has a single caller (`ControlAttributeApplier.Apply`); signature change is safe. Make `hidden` nullable so the assignment only fires when XML actually declares `hidden=` (or a Variant overrides it).

Modify `Runtime/Controls/Control.cs:106-108` — change the signature:

```csharp
        public void ApplyCommon(string anchor, string size, string width, string height,
                                string margin, string pivot,
                                bool? hidden, bool interactable)
```

Modify the same file at line 213, replace `Hidden = hidden;` with:

```csharp
            if (hidden.HasValue) Hidden = hidden.Value;
```

Modify `Runtime/Application/ControlAttributeApplier.cs:65-72`. Replace the block:

```csharp
            var hiddenStr = VariantResolver.ResolveAttribute(node, "hidden", variants);
            var interactableStr = VariantResolver.ResolveAttribute(node, "interactable", variants);
            var hidden = hiddenStr == "true";
            var interactable = interactableStr != "false";
```

with:

```csharp
            var hiddenStr = VariantResolver.ResolveAttribute(node, "hidden", variants);
            var interactableStr = VariantResolver.ResolveAttribute(node, "interactable", variants);
            bool? hidden = hiddenStr == null ? null : hiddenStr == "true";
            var interactable = interactableStr != "false";
```

(`Interactable` stays unconditional — its idempotent setter is harmless on ReSolve, and Bind never disables it. Symmetry refactor deferred to YAGNI.)

- [ ] **Step 7: Re-run tests, including the full EditMode suite to catch regressions**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__run_tests(mode="EditMode", filter="ModalReSolveTests")
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

Expected: ModalReSolveTests now passes. **Full EditMode suite must remain green** — if any existing test breaks, the most likely cause is a test that asserted a node's `Hidden=false` after ReSolve when XML had no `hidden=` (i.e., relied on the force-write behavior). Inspect the failing test; if it expected the default visible state, the test should still pass because GameObjects default to active=true. If it expected ReSolve to re-show a Control whose `Hidden` was set via C# (not XML), that's the corner case this patch intentionally changes; update the test to declare `hidden=` in XML.

- [ ] **Step 8: Commit**

```bash
git add Runtime/Controls/Control.cs \
        Runtime/Application/ControlAttributeApplier.cs \
        Runtime/Application/Modals/MessageBoxRequest.cs \
        Runtime/Application/Modals/MessageBoxRequest.cs.meta \
        Tests/EditMode/Modals/MessageBoxRequestTests.cs \
        Tests/EditMode/Modals/MessageBoxRequestTests.cs.meta \
        Tests/EditMode/Modals/ModalReSolveTests.cs \
        Tests/EditMode/Modals/ModalReSolveTests.cs.meta \
        Tests/EditMode/Modals/ModalTestFixture.cs
git commit -m "feat: MessageBoxRequest + only-write-Hidden-when-declared in ApplyCommon"
```

---

## Task 12: MessageBox static wrapper

**Files:**
- Modify: `Runtime/Application/Modals/MessageBoxRequest.cs` (extend the `MessageBox` class)
- Create: `Tests/EditMode/Modals/MessageBoxStaticTests.cs`

- [ ] **Step 1: Write failing tests**

Create `Tests/EditMode/Modals/MessageBoxStaticTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class MessageBoxStaticTests : ModalTestFixture
    {
        [Test]
        public void Open_default_overload_returns_Btn_OK()
        {
            var task = MessageBox.Open("hello", Btn.OK);
            UI.Get("test/Box1").Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(Btn.OK, task.GetAwaiter().GetResult());
        }

        [Test]
        public void Open_custom_labels_overload_returns_mapped_key()
        {
            var task = MessageBox.Open("hello",
                new[] { ("Retry", Btn.OK), ("Skip", Btn.Cancel) });
            UI.Get("test/Box1").Get<PromptUGUI.Controls.Btn>("cancel").SimulateClick();
            Assert.AreEqual(Btn.Cancel, task.GetAwaiter().GetResult());
        }

        [Test]
        public void Open_default_overload_no_buttons_arg_defaults_to_OK()
        {
            var task = MessageBox.Open("hello");
            var s = UI.Get("test/Box1");
            Assert.IsTrue (s.Get<PromptUGUI.Controls.Btn>("ok").GameObject.activeSelf);
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("cancel").GameObject.activeSelf);
            s.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(Btn.OK, task.GetAwaiter().GetResult());
        }
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile errors `MessageBox.Open` not defined.

- [ ] **Step 3: Implement Open overloads**

In `Runtime/Application/Modals/MessageBoxRequest.cs`, expand the `MessageBox` class:

```csharp
    public static class MessageBox
    {
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/MessageBox";

        public static UnityEngine.Awaitable<Btn> Open(
            string text, Btn buttons = Btn.OK, string icon = null, string title = null)
            => UI.Modal.OpenAsync(new MessageBoxRequest {
                Text = text, Buttons = buttons, Icon = icon, Title = title,
            });

        public static UnityEngine.Awaitable<Btn> Open(
            string text,
            System.Collections.Generic.IEnumerable<(string label, Btn key)> buttons,
            string icon = null, string title = null)
        {
            var list = new System.Collections.Generic.List<(string, Btn)>(buttons);
            var mask = Btn.None;
            foreach (var (_, k) in list) mask |= k;
            return UI.Modal.OpenAsync(new MessageBoxRequest {
                Text = text, CustomLabels = list, Buttons = mask, Icon = icon, Title = title,
            });
        }
    }
```

- [ ] **Step 4: Run tests**

```
mcp__UnityMCP__run_tests(mode="EditMode", filter="MessageBoxStaticTests")
```

Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/Modals/MessageBoxRequest.cs \
        Tests/EditMode/Modals/MessageBoxStaticTests.cs \
        Tests/EditMode/Modals/MessageBoxStaticTests.cs.meta
git commit -m "feat: MessageBox.Open static wrapper with default and custom-label overloads"
```

---

## Task 13: PlayMode end-to-end (ESC + sortingOrder via real Canvas)

**Files:**
- Create: `Tests/PlayMode/Modals/ModalEscapePlayModeTests.cs`

- [ ] **Step 1: Write the PlayMode test**

Create `Tests/PlayMode/Modals/ModalEscapePlayModeTests.cs`:

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Modals
{
    public class ModalEscapePlayModeTests
    {
        private GameObject _es;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _es = new GameObject("EventSystem");
            _es.AddComponent<EventSystem>();
            _es.AddComponent<StandaloneInputModule>();
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_es != null) Object.Destroy(_es);
        }

        [UnityTest]
        public IEnumerator Escape_returns_Cancel_when_OK_Cancel_combo()
        {
            var task = MessageBox.Open("first", Btn.OK | Btn.Cancel);
            yield return null;

            var screen = UI.Get(MessageBox.XmlSrc);
            Assert.IsNotNull(screen);
            var listener = screen.RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(listener, "ModalEscapeListener should be attached");

            listener.FireForTests();
            yield return null;

            Assert.AreEqual(Btn.Cancel, task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [UnityTest]
        public IEnumerator Escape_only_OK_does_not_close()
        {
            var task = MessageBox.Open("only ok", Btn.OK);
            yield return null;

            var listener = UI.Get(MessageBox.XmlSrc).RootGameObject.GetComponent<ModalEscapeListener>();
            listener.FireForTests();
            yield return null;

            Assert.IsTrue(UI.Modal.IsAnyOpen, "ESC with OK-only must not close");
            UI.Modal.CloseAll();
            Assert.Throws<System.OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator SortingOrder_uses_SortingOrderBase()
        {
            UI.Modal.SortingOrderBase = 777;
            var task = MessageBox.Open("x", Btn.OK);
            yield return null;

            var canvas = UI.Get(MessageBox.XmlSrc).RootGameObject.GetComponent<Canvas>();
            Assert.AreEqual(777, canvas.sortingOrder);
            Assert.IsTrue(canvas.overrideSorting);

            UI.Get(MessageBox.XmlSrc).Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            yield return null;
            Assert.AreEqual(Btn.OK, task.GetAwaiter().GetResult());
        }
    }
}
```

This test class does **not** inherit from `ModalTestFixture` (which lives in EditMode asmdef). It uses the real builtin Resources path — `MessageBox.XmlSrc` stays at its default.

- [ ] **Step 2: Verify the PlayMode asmdef has a Modals subfolder asmref**

Check `Tests/PlayMode/Modals/`. If it doesn't exist, the `[UnityTest]` discovery happens through the parent `PromptUGUI.Tests.PlayMode.asmdef` automatically — no per-folder asmdef needed. Just create the folder.

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: no errors.

- [ ] **Step 3: Run tests**

```
mcp__UnityMCP__run_tests(mode="PlayMode", filter="ModalEscapePlayModeTests")
```

Expected: 3 passed.

- [ ] **Step 4: Commit**

```bash
git add Tests/PlayMode/Modals/
git commit -m "test: PlayMode coverage for Modal ESC + sortingOrder"
```

---

## Task 14: Hot-reload invalidation hook (Editor-only)

**Files:**
- Modify: `Runtime/Application/Modals/UI.Modal.cs`
- Modify: `Editor/UIAssetPostprocessor.cs` (if it exists; otherwise note in spec)

Per spec §6.5, modal XML edits in Editor should invalidate the cache so the next Open re-loads.

- [ ] **Step 1: Inspect UIAssetPostprocessor**

```
mcp__UnityMCP__find_in_file(path="Editor/UIAssetPostprocessor.cs", pattern="NotifyAssetChanged|AssetPathToSrc")
```

Note the existing hook — it calls `UI.HotReload.NotifyAssetChanged(assetPath)`. Modal Screens never enter `_depGraph`, so `NotifyAssetChanged` silently no-ops for them — that's fine for production. But for Editor iteration we want the cache cleared.

- [ ] **Step 2: Add InvalidateCacheForEditor to UI.Modal**

In `Runtime/Application/Modals/UI.Modal.cs`, inside the `Modal` class, add:

```csharp
#if UNITY_EDITOR
            internal static void InvalidateCacheForEditor(string src)
            {
                if (string.IsNullOrEmpty(src)) return;
                if (_loadedSrcs.Remove(src))
                {
                    UnloadDocument(src);
                }
            }
#endif
```

- [ ] **Step 3: Hook into Editor postprocessor**

Modify `Editor/UIAssetPostprocessor.cs`: in the `OnPostprocessAllAssets` / asset-changed callback, **after** the existing `UI.HotReload.NotifyAssetChanged(assetPath)` call, add:

```csharp
#if UNITY_EDITOR
            // Modal Screens are loaded outside the DepGraph; invalidate their cache so
            // the next MessageBox.Open re-reads the edited XML.
            var modalSrc = UI.HotReload.AssetPathToSrc?.Invoke(assetPath);
            if (!string.IsNullOrEmpty(modalSrc))
                UI.Modal.InvalidateCacheForEditor(modalSrc);
#endif
```

(If the postprocessor's exact API differs, adapt — the goal is one call per changed asset path.)

- [ ] **Step 4: Write Editor-asmdef test**

This is `PromptUGUI.Tests.EditorOnly` territory (under `Tests/EditMode/Editor/`). Create `Tests/EditMode/Editor/ModalHotReloadTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.EditorOnly
{
    public class ModalHotReloadTests
    {
        [SetUp]    public void SetUp()    => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void InvalidateCacheForEditor_lets_next_open_reload_xml()
        {
            // Stand up a fake "loaded" modal src
            var src = "test/HotMbox";
            UI.SourceResolver = s => AwaitableHelpers.Completed(
                s == src
                    ? "<?xml version='1.0'?><PromptUGUI version='1'><Screen name='" + src +
                      "'><Frame id='a'/></Screen></PromptUGUI>"
                    : null);
            MessageBox.XmlSrc = src;

            // First open caches
            var t1 = MessageBox.Open("x", Btn.OK);
            UI.Get(src).Get<PromptUGUI.Controls.Btn>("a"); // would throw - intentionally just trigger pump
        }
    }
}
```

Note: this is a smoke test for the API surface; full re-read behavior requires Editor postprocessor wiring which is harder to test in isolation. The simpler smoke check: `InvalidateCacheForEditor` does not throw on unknown src.

Replace with a cleaner version:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.EditorOnly
{
    public class ModalHotReloadTests
    {
        [SetUp]    public void SetUp()    => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void InvalidateCacheForEditor_is_silent_for_unknown_src()
        {
            Assert.DoesNotThrow(() => UI.Modal.InvalidateCacheForEditor("not/cached"));
        }
    }
}
```

- [ ] **Step 5: Run tests**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="ModalHotReloadTests")
```

Expected: 1 passed.

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application/Modals/UI.Modal.cs \
        Editor/UIAssetPostprocessor.cs \
        Tests/EditMode/Editor/ModalHotReloadTests.cs \
        Tests/EditMode/Editor/ModalHotReloadTests.cs.meta
git commit -m "feat: Editor-only modal cache invalidation for hot-reload"
```

---

## Task 15: SKILL.md update

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`

Add a "Modal dialogs" section per spec §8.

- [ ] **Step 1: Add the new section**

In `.claude/skills/scripting-promptugui-csharp/SKILL.md`, insert a new top-level section **before** "## `<Trigger>` and `<Animation>` from C#" (which currently sits near the bottom):

```markdown
## Modal dialogs

PromptUGUI ships a generic modal system in `PromptUGUI.Application.Modals` plus a builtin MessageBox.

### Quick usage

```csharp
using PromptUGUI.Application.Modals;

// Default messagebox
var r = await MessageBox.Open(UI.Tr("Save changes?"),
                              Btn.Yes | Btn.No | Btn.Cancel);
if (r == Btn.Yes) await game.SaveAsync();

// Custom button labels (still returns mapped Btn flag)
var r2 = await MessageBox.Open(UI.Tr("File not found."),
    new[] { (UI.Tr("Retry"), Btn.OK), (UI.Tr("Skip"), Btn.Cancel) });

// Optional icon ("set:name" form) and title
await MessageBox.Open("Saved.", Btn.OK, icon: "ui:check", title: "Done");
```

### Behavior

- **Modal stacking**: when one MessageBox is open, subsequent `Open(...)` calls queue FIFO. The next pops automatically when the active one closes.
- **ESC / Android Back**: maps to the most-negative button in the combo: `Cancel > No > Close`. ESC on an `OK`-only modal does nothing.
- **Raycast block**: the modal Screen overrides `Canvas.sortingOrder` to `UI.Modal.SortingOrderBase` (default 1000), so it sits above every regular Screen. The XML's backdrop Image fills the canvas and absorbs clicks.
- **Locale / Variant**: a modal is a regular `Screen` — locale switches translate its button labels; Variants re-apply attribute values normally.

### Cancelling

```csharp
UI.Modal.CloseAll();   // every pending await throws OperationCanceledException
```

`UI.UnloadAll()` and `UI.ResetForTests()` also cancel all pending modals (then the underlying Screens are torn down).

### Custom modal types

Subclass `ModalRequest<TResult>` and pass it to `UI.Modal.OpenAsync(...)`. Your `Bind(screen, close)` wires events; `close(result)` resolves the awaiter.

```csharp
public sealed class NamePickerRequest : ModalRequest<string> {
    public override string XmlSrc => "MyUI/Modals/NamePicker";
    public override void Bind(IScreen screen, Action<string> close) {
        screen.Get<Btn>("ok").OnClick.Subscribe(_ =>
            close(screen.Get<InputField>("input").Text)).AddTo(screen);
        screen.Get<Btn>("cancel").OnClick.Subscribe(_ => close(null)).AddTo(screen);
    }
    public override bool TryEscape(out string r) { r = null; return true; }
}

var name = await UI.Modal.OpenAsync(new NamePickerRequest());
```

Custom modal `XmlSrc` keys go through the caller's `UI.SourceResolver` like any other Screen.

### Overriding the builtin MessageBox layout

Set `MessageBox.XmlSrc` once at boot to point at your own XML file (which must declare a Screen with the same set of ids: `text`, `title`, `icon`, `ok`, `cancel`, `yes`, `no`, `close`):

```csharp
MessageBox.XmlSrc = "MyUI/Modals/PixelMessageBox";  // resolved by UI.SourceResolver
```

Keys starting with `PromptUGUI/` resolve to the package's bundled Resources; other keys go through `UI.SourceResolver`.
```

- [ ] **Step 2: Add modal entries to the cheatsheet block**

Inside the same SKILL.md, in the `## Quick reference (cheatsheet)` code block, add:

```
MODAL          var r = await MessageBox.Open(text, Btn.OK|Btn.Cancel, icon, title)
               UI.Modal.OpenAsync(new MyRequest())          custom ModalRequest<T>
               UI.Modal.CloseAll()                          cancel all pending
               UI.Modal.SortingOrderBase = 1000             default
```

- [ ] **Step 3: Verify lint clean**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: no diff.

- [ ] **Step 4: Commit**

```bash
git add .claude/skills/scripting-promptugui-csharp/SKILL.md
git commit -m "docs: SKILL.md modal dialogs section"
```

---

## Final verification

- [ ] **Step 1: Run full test suites**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

Expected: all green (existing + new tests).

- [ ] **Step 2: Lint**

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: no diff.

- [ ] **Step 3: Acceptance against spec §10**

Manually verify in a Play sample (e.g. the existing `Samples~/MainMenu`):

```csharp
async void Start() {
    UI.UseResourcesResolver("UI");
    await UI.LoadDocumentAsync("screens/main");
    UI.Open("MainMenu");

    await Awaitable.WaitForSecondsAsync(0.5f);
    var r = await PromptUGUI.Application.Modals.MessageBox.Open(
        "Hello, modal!",
        PromptUGUI.Application.Modals.Btn.OK | PromptUGUI.Application.Modals.Btn.Cancel);
    Debug.Log($"Got: {r}");
}
```

Expected: dialog appears on top of MainMenu; lower-screen buttons don't respond while dialog open; ESC returns Cancel; clicking OK returns OK.
