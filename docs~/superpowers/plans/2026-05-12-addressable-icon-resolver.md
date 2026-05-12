# Addressable Icon Resolver Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `IconResolverHelpers.UseAddressableSpriteAtlasIconResolver(label)` — async preload of `IconSet` ScriptableObjects from Addressables, auto-pulling the referenced `SpriteAtlas` as Addressables dependency.

**Architecture:** Add `UI.OnReset` internal event so Helper can hook handle cleanup into `ResetForTests` without UI.cs knowing about Addressables. Extend `IconResolverHelpers` as a `partial class` via new `Runtime/Application/AddressableIconResolverHelper.cs` (gated by `PROMPTUGUI_HAS_ADDRESSABLES`). Method returns `Awaitable` (not `Awaitable<T>`) because `UI.IconResolver` is sync — callers must `await` before opening any `<Icon>`-containing Screen.

**Tech Stack:** Unity 6 `UnityEngine.Awaitable`, `UnityEngine.AddressableAssets` (com.unity.addressables ≥ 1.0), NUnit EditMode test runner, Unity MCP for compile/test cycles.

**Spec:** [`docs~/superpowers/specs/2026-05-12-addressable-icon-resolver-design.md`](../specs/2026-05-12-addressable-icon-resolver-design.md)

---

## File Structure

**New files:**
- `Runtime/Application/AddressableIconResolverHelper.cs` — entire file under `#if PROMPTUGUI_HAS_ADDRESSABLES`; defines `partial class IconResolverHelpers` with the new method, the static handle field, the cleanup hook, and the internal test counter.
- `Tests/EditMode/Application/UIResetEventTests.cs` — single test for the new `UI.OnReset` event; lives in the non-Addressables EditMode assembly so it runs without Addressables installed.
- `Tests/EditMode/Addressables/AddressableIconResolverTests.cs` — three tests for the Addressable helper, mirroring the EditMode-async-limitation pattern from existing `AddressableResolverTests.cs`.

**Modified files:**
- `Runtime/Application/UI.cs` — add `internal static event Action OnReset` and `OnReset?.Invoke()` at the end of `ResetForTests()`.
- `Runtime/Application/IconResolverHelpers.cs` — change `public static class` → `public static partial class`.
- `.claude/skills/authoring-promptugui-xml/SKILL.md` — append Addressables icon helper section after the existing two `UseSpriteAtlasIconResolver` examples (around line 417).

**Unchanged:** `Samples~/`, all other Runtime / Editor / test files, existing `IconResolverTests.cs` and `AddressableResolverTests.cs`.

---

## Task 1: Add `UI.OnReset` event with red-first test

**Files:**
- Create: `Tests/EditMode/Application/UIResetEventTests.cs`
- Modify: `Runtime/Application/UI.cs` (around lines 410–430, inside `ResetForTests`)

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Application/UIResetEventTests.cs`:

```csharp
using System;
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Application
{
    public class UIResetEventTests
    {
        [SetUp]
        public void Setup() => UI.ResetForTests();

        [TearDown]
        public void Teardown() => UI.ResetForTests();

        [Test]
        public void OnReset_event_fires_after_reset()
        {
            var fired = 0;
            Action handler = () => fired++;
            UI.OnReset += handler;
            try
            {
                UI.ResetForTests();
                Assert.AreEqual(1, fired,
                    "UI.OnReset should fire exactly once per ResetForTests call");
            }
            finally
            {
                UI.OnReset -= handler;
            }
        }
    }
}
```

- [ ] **Step 2: Refresh Unity and verify the test fails to compile**

Run (deferred MCP tools — load first if needed via `ToolSearch(query="select:refresh_unity,read_console,run_tests", max_results=3)`):

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile error along the lines of `'UI' does not contain a definition for 'OnReset'`. If no compile error appears, stop and investigate — the test isn't actually exercising the new API.

- [ ] **Step 3: Add the event and the invoke**

In `Runtime/Application/UI.cs`, add the event declaration. The natural spot is right above `ResetForTests` (line ~409). Edit:

```csharp
        // 仅测试使用
        internal static void ResetForTests()
```

to:

```csharp
        // ResetForTests 末尾触发；let helpers (e.g. AddressableIconResolverHelper)
        // 释放 Addressables 句柄等外部资源。订阅者必须在 ResetForTests 自身把状态
        // 清空之后再跑，所以 Invoke 放在方法尾部。
        internal static event Action OnReset;

        // 仅测试使用
        internal static void ResetForTests()
```

Then add the invoke at the end of `ResetForTests` — directly inside the `#if UNITY_EDITOR` ... `#endif` block's closing brace (so it fires regardless of Editor/Player). The current tail is:

```csharp
#if UNITY_EDITOR
            HotReload.AssetPathToSrc = null;
            HotReload.IconResolverRebuilder = null;
            HotReload.Enabled = true;
#endif
        }
```

Change to:

```csharp
#if UNITY_EDITOR
            HotReload.AssetPathToSrc = null;
            HotReload.IconResolverRebuilder = null;
            HotReload.Enabled = true;
#endif
            OnReset?.Invoke();
        }
```

Also ensure `using System;` is present at the top of `UI.cs` (it almost certainly is for `Action`, but verify). If not, add it.

- [ ] **Step 4: Refresh and run the test**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="UIResetEventTests")
```

Expected: 0 compile errors. `OnReset_event_fires_after_reset` passes.

- [ ] **Step 5: Run the full EditMode suite to make sure nothing else broke**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

Expected: all green. (`ResetForTests` is called in many `[SetUp]` / `[TearDown]` — adding `OnReset?.Invoke()` with no subscribers is a no-op, but verify.)

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application/UI.cs Tests/EditMode/Application/UIResetEventTests.cs
git commit -m "$(cat <<'EOF'
feat: add internal UI.OnReset event triggered by ResetForTests

Hook point for runtime helpers (e.g. forthcoming Addressable icon
resolver) that need to release external resources when tests reset
global state. Event fires after all of ResetForTests' own cleanup
so subscribers observe a freshly-reset UI.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Make `IconResolverHelpers` a partial class

This is a single-character refactor required before Task 3 can extend the class from a separate file. No new test — Task 1's full-suite run already covers regression.

**Files:**
- Modify: `Runtime/Application/IconResolverHelpers.cs:7`

- [ ] **Step 1: Change class declaration**

Edit `Runtime/Application/IconResolverHelpers.cs` line 7:

```csharp
    public static class IconResolverHelpers
```

to:

```csharp
    public static partial class IconResolverHelpers
```

- [ ] **Step 2: Refresh and verify clean compile**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: no errors.

- [ ] **Step 3: Run existing IconResolver tests**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IconResolverTests")
```

Expected: all 5 existing tests pass.

- [ ] **Step 4: Commit**

```bash
git add Runtime/Application/IconResolverHelpers.cs
git commit -m "$(cat <<'EOF'
refactor: make IconResolverHelpers a partial class

Prep for the Addressables-gated extension file; no behavior change.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Addressable Icon Resolver — tests + implementation

**Files:**
- Create: `Tests/EditMode/Addressables/AddressableIconResolverTests.cs`
- Create: `Runtime/Application/AddressableIconResolverHelper.cs`

- [ ] **Step 1: Write the three failing tests**

Create `Tests/EditMode/Addressables/AddressableIconResolverTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEditor.AddressableAssets;

namespace PromptUGUI.Tests.Addressables
{
    /// <summary>
    /// Wiring smoke tests for IconResolverHelpers.UseAddressableSpriteAtlasIconResolver.
    ///
    /// End-to-end "label → IconSet → IconResolver returns sprite" is NOT tested
    /// in EditMode: AsyncOperationHandle continuations need the player-loop
    /// SynchronizationContext which is absent in EditMode test runners. Same
    /// limitation documented in AddressableResolverTests. The synchronous prefix
    /// of the async method (release-previous-handle, start-load, store-handle,
    /// hook-reset) is what gets covered here.
    /// </summary>
    public class AddressableIconResolverTests
    {
        private const string FixtureLabel = "promptugui-test/icons";

        [SetUp]
        public void Setup()
        {
            UI.ResetForTests();
            // Ensure AddressableAssetSettings exists; without it
            // Addressables.LoadAssetsAsync throws synchronously in a fresh project.
            _ = AddressableAssetSettingsDefaultObject.Settings
                ?? AddressableAssetSettingsDefaultObject.GetSettings(true);
            IconResolverHelpers._testReleaseCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
        }

        [Test]
        public void Invocation_returns_an_awaitable()
        {
            var awaitable =
                IconResolverHelpers.UseAddressableSpriteAtlasIconResolver(FixtureLabel);
            Assert.IsNotNull(awaitable,
                "UseAddressableSpriteAtlasIconResolver should return non-null Awaitable");
            // Note: Awaitable intentionally not awaited; underlying AsyncOperationHandle
            // remains pending until TearDown's ResetForTests releases it. We don't
            // assert on UI.IconResolver state — its post-await value depends on
            // whether LoadAssetsAsync completed synchronously (rare but possible),
            // which is a C# state-machine detail rather than this helper's contract.
        }

        [Test]
        public void Releases_previous_handle_on_second_call()
        {
            _ = IconResolverHelpers.UseAddressableSpriteAtlasIconResolver(FixtureLabel);
            var beforeSecond = IconResolverHelpers._testReleaseCount;
            _ = IconResolverHelpers.UseAddressableSpriteAtlasIconResolver(FixtureLabel);
            Assert.AreEqual(beforeSecond + 1, IconResolverHelpers._testReleaseCount,
                "Second call should release the first call's handle exactly once");
        }

        [Test]
        public void ResetForTests_releases_handle()
        {
            _ = IconResolverHelpers.UseAddressableSpriteAtlasIconResolver(FixtureLabel);
            var beforeReset = IconResolverHelpers._testReleaseCount;
            UI.ResetForTests();
            Assert.AreEqual(beforeReset + 1, IconResolverHelpers._testReleaseCount,
                "ResetForTests should trigger OnReset → helper releases the handle");
        }
    }
}
```

- [ ] **Step 2: Refresh and verify the tests fail to compile**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile errors referencing `UseAddressableSpriteAtlasIconResolver` and `_testReleaseCount` not found on `IconResolverHelpers`. If only one of the two errors appears, something is off — both APIs are missing.

- [ ] **Step 3: Write the implementation**

Create `Runtime/Application/AddressableIconResolverHelper.cs`:

```csharp
#if PROMPTUGUI_HAS_ADDRESSABLES
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace PromptUGUI.Application
{
    public static partial class IconResolverHelpers
    {
        // Held alive so the IconSet refs (and their dependent SpriteAtlas) stay
        // loaded for the lifetime of UI.IconResolver. Released on second-call /
        // UI.ResetForTests. (PROMPTUGUI_HAS_ADDRESSABLES only.)
        private static AsyncOperationHandle<IList<IconSet>>? _addressableIconHandle;
        private static bool _addressableResetHooked;

        // Test observation point. Tests assert the increment to verify Release was
        // called, without inspecting Addressables internals.
        internal static int _testReleaseCount;

        public static async Awaitable UseAddressableSpriteAtlasIconResolver(
            string label = "IconSets")
        {
            ReleaseAddressableIconHandle();
            HookResetOnce();

            var handle = Addressables.LoadAssetsAsync<IconSet>(label, null);
            _addressableIconHandle = handle;
            var sets = await handle.Task;
            var snapshot = new List<IconSet>(sets ?? Array.Empty<IconSet>());

            void Rebuild()
            {
                var map = BuildLookup(snapshot);
                UI.IconResolver = key => map.TryGetValue(key, out var sp) ? sp : null;
            }
            Rebuild();
#if UNITY_EDITOR
            UI.HotReload.IconResolverRebuilder = Rebuild;
#endif
        }

        private static void HookResetOnce()
        {
            if (_addressableResetHooked) return;
            UI.OnReset += ReleaseAddressableIconHandle;
            _addressableResetHooked = true;
        }

        private static void ReleaseAddressableIconHandle()
        {
            if (_addressableIconHandle.HasValue && _addressableIconHandle.Value.IsValid())
            {
                Addressables.Release(_addressableIconHandle.Value);
                _testReleaseCount++;
            }
            _addressableIconHandle = null;
        }
    }
}
#endif
```

Notes on the code:
- `partial class` merges with the body in `IconResolverHelpers.cs`; `BuildLookup` (private static there) is reachable from this file because partials share the same class scope.
- `AsyncOperationHandle<T>` is a value-type struct; `?` makes it nullable so we can represent "no handle yet" without inventing a sentinel.
- `Addressables.LoadAssetsAsync<IconSet>(label, null)` — the `null` second parameter is the per-item callback; we don't need it.
- `await handle.Task` — chosen over `await handle` for Addressables version compatibility (existing `AddressableResolverHelper.cs` uses the same `.Task` pattern; see the comment there).
- Failures (label not found, type mismatch, etc.) surface as exceptions when the caller awaits the method's `Awaitable`. We don't catch them — same as `UseAddressableResolver()`.
- `_testReleaseCount` is `internal` (visible to the Addressables test asmdef via `[InternalsVisibleTo]` already declared in `Runtime/AssemblyInfo.cs`).

- [ ] **Step 4: Refresh and run the new tests**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Addressables"], filter="AddressableIconResolverTests")
```

Expected: 0 compile errors. All 3 tests pass:
- `Invocation_returns_an_awaitable`
- `Releases_previous_handle_on_second_call`
- `ResetForTests_releases_handle`

- [ ] **Step 5: Run the full Addressables EditMode suite to confirm no regression in `AddressableResolverTests`**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Addressables"])
```

Expected: all green (the existing `AddressableResolverTests` shouldn't be affected by this change).

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application/AddressableIconResolverHelper.cs Tests/EditMode/Addressables/AddressableIconResolverTests.cs
git commit -m "$(cat <<'EOF'
feat: add Addressables-backed IconSet preloader

IconResolverHelpers.UseAddressableSpriteAtlasIconResolver(label="IconSets")
loads all IconSets tagged with the given Addressables label and wires
them into UI.IconResolver. Addressables pulls the dependent SpriteAtlas
as a transitive dependency, so callers only tag IconSets.

Handle is held in a static field for the resolver's lifetime (sprite
refs depend on atlas residency). Second call to this method releases
the previous handle; UI.OnReset (fired by ResetForTests) also releases.
Gated by PROMPTUGUI_HAS_ADDRESSABLES.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Update SKILL.md

CLAUDE.md mandates that new public C# API surface gets SKILL.md coverage in the same PR. This is a new API surface, not a transparent default.

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md` (around line 417)

- [ ] **Step 1: Locate the insertion point**

The current "Icon system setup" block ends (around line 417) with:

```markdown
The helper builds a `(set:icon) → Sprite` lookup from each IconSet's SpriteAtlas. To use a different backend (Addressables, custom), set `UI.IconResolver` directly.
```

That last sentence's "(Addressables, custom)" hint is now stale — Addressables is a first-class helper. Replace the trailing sentence and append the new section.

- [ ] **Step 2: Apply the edit**

Replace:

```markdown
The helper builds a `(set:icon) → Sprite` lookup from each IconSet's SpriteAtlas. To use a different backend (Addressables, custom), set `UI.IconResolver` directly.
```

with:

````markdown
The helper builds a `(set:icon) → Sprite` lookup from each IconSet's SpriteAtlas.

**Addressables variant** (when `com.unity.addressables` ≥ 1.0 is installed):

```csharp
// Tag your IconSet assets in Addressables with label="IconSets".
// Addressables auto-pulls the referenced SpriteAtlas as a dependency.
await IconResolverHelpers.UseAddressableSpriteAtlasIconResolver();
// Or custom label:
await IconResolverHelpers.UseAddressableSpriteAtlasIconResolver("MyIcons");
```

Returns `Awaitable` — `await` it before opening any Screen that contains `<Icon>`, since `UI.IconResolver` is set inside the continuation. The loaded handle is held static and released either on a second `UseAddressableSpriteAtlasIconResolver` call (swap label/locale) or on `UI.ResetForTests`. Only visible when `PROMPTUGUI_HAS_ADDRESSABLES` is defined.

To use a fully custom backend, set `UI.IconResolver` directly with your own `(key → Sprite)` lookup.
````

- [ ] **Step 3: Verify no other SKILL.md sections referenced the old "Addressables, custom" sentence**

```bash
grep -n "Addressables, custom" /workspace-PromptUGUI/.claude/skills/authoring-promptugui-xml/SKILL.md
```

Expected: no matches (the old phrasing was unique to that one sentence). If matches appear elsewhere, update those too.

- [ ] **Step 4: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "$(cat <<'EOF'
doc: document UseAddressableSpriteAtlasIconResolver in SKILL.md

Promote Addressables from "do it yourself by setting UI.IconResolver"
to a first-class helper with usage example and lifecycle notes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Lint + full test sweep

Per CLAUDE.md: "Always check lint after write code." Per project workflow: full EditMode + PlayMode runs before declaring done.

**Files:** none modified unless lint reports something.

- [ ] **Step 1: Run lint (whitespace + style + analyzers, all at warn severity)**

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format style       PromptUGUI.Lint.slnx
dotnet format analyzers   PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: `--verify-no-changes` returns exit code 0. Per CLAUDE.md, do NOT pass `--severity info` to `dotnet format analyzers` — the listed Unity-breaking auto-fixers (CA1822, CA1846, CA2016, IDE0032, IDE0044) will damage the codebase.

If lint applied any whitespace/style fixes, stage and commit them separately:

```bash
cd /workspace-PromptUGUI
git status
git add -p   # review hunks
git commit -m "$(cat <<'EOF'
chore: lint pass for addressable icon resolver

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 2: Run all three EditMode test assemblies + PlayMode**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Addressables"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

Expected: all green. If any test fails, do NOT proceed to the next task — diagnose using `mcp__UnityMCP__read_console(action="get", types=["error"])` and the test runner output; fix the root cause; re-run.

- [ ] **Step 3: Final git status check**

```bash
git status
git log --oneline -5
```

Expected: clean working tree; commit log shows (in order) the OnReset event, the partial refactor, the Addressable helper + tests, the SKILL.md update, and (optionally) the lint pass.

---

## Notes for the executing engineer

- **Test execution is via Unity MCP only.** Do not invoke `Unity.exe -batchmode` or similar. If `mcp__UnityMCP__run_tests` is unavailable, reconnect MCP or stop and ask the user.
- **Forbidden: `mcp__UnityMCP__execute_menu_item(menu_path="Assets/Reimport All")`** — pops a blocking modal in Unity that the user must dismiss manually. Use `refresh_unity(mode="force", scope="all")` instead.
- **After every code edit, refresh first, then `read_console` for errors before running tests.** Compile errors won't appear in the test runner output — they'll just look like missing test assemblies.
- **Filter tests by class name** with `filter="ClassName"` (substring match) for fast iteration during red-green cycles.
- **Don't release the Addressables handle in the `await` continuation.** The handle must outlive the method — its lifecycle is owned by the helper's static field, not the method scope. (This is the opposite of `UseAddressableResolver()` which releases right after extracting `.text`.)
- **Partial class scope:** Once Task 2 lands, `BuildLookup` (declared `private static` in `IconResolverHelpers.cs`) is reachable from `AddressableIconResolverHelper.cs` — partials share the same class scope. If a name-collision appears at compile time, the new file may have accidentally re-declared something.
- **EditMode tests cannot `await` the Addressable handle.** Don't try; the player-loop SynchronizationContext doesn't exist. Tests assert on the synchronous prefix and on side effects observable from sync code (the `_testReleaseCount` counter). This is the same limitation `AddressableResolverTests.cs` already documents.
