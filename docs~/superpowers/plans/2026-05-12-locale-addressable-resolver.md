# Locale (PO) Addressable Resolver Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `UI.Locale.UseAddressableResolver()` so the .po loading channel can pull from Addressables (label = locale). Make `UI.PoResolver` async-shaped; sync `Locale.Set`/`ReloadCurrent` become fire-and-forget wrappers that auto-trigger ReSolve when the download completes. Add `SetAsync` / `ReloadCurrentAsync` for callers that need ordering.

**Architecture:** Three layers. (1) `UI.PoResolver` switches from `Func<string, IEnumerable<PoEntry>>` to `Func<string, Awaitable<IEnumerable<PoEntry>>>`; sync resolvers wrap result via `AwaitableHelpers.Completed(...)` and keep synchronous semantics. (2) `Locale.Set(locale)` stays `void` but fires `_ = LoadPoFilesAndApplyAsyncLogged(locale)`; a `Current != locale` guard inside the async helper discards stale loads from rapid consecutive calls. (3) `Runtime/Application/LocaleAddressableResolverHelper.cs` (gated by `PROMPTUGUI_HAS_ADDRESSABLES`) registers a PoResolver that calls `Addressables.LoadAssetsAsync<TextAsset>(locale, null)` and parses every returned `.po` TextAsset.

**Tech Stack:** Unity 6 `UnityEngine.Awaitable` / `AwaitableCompletionSource`, `UnityEngine.AddressableAssets` (com.unity.addressables ≥ 1.0), R3 (Cysharp) `Subject`/`Observable`, NUnit EditMode test runner, Unity MCP for compile/test cycles.

**Spec:** [`docs~/superpowers/specs/2026-05-12-locale-addressable-resolver-design.md`](../specs/2026-05-12-locale-addressable-resolver-design.md)

---

## File Structure

**New files:**
- `Runtime/Application/LocaleAddressableResolverHelper.cs` — entire file under `#if PROMPTUGUI_HAS_ADDRESSABLES`; defines `partial class UI { partial class Locale { ... } }` with `UseAddressableResolver()` + the private `LoadPoFromAddressablesAsync` helper.
- `Tests/EditMode/Application/LocaleSetAsyncTests.cs` — tests for the new `Set` / `SetAsync` / `ReloadCurrentAsync` semantics + race guard + error path. Does NOT depend on Addressables (uses in-memory async resolvers).
- `Tests/EditMode/Addressables/LocaleAddressableResolverTests.cs` — two wiring smoke tests for the Addressable helper, mirroring the EditMode-async-limitation pattern from existing `AddressableResolverTests.cs`.

**Modified files:**
- `Runtime/Application/UI.cs` —
  - Line 24: change `Func<string, IEnumerable<I18n.PoEntry>>` → `Func<string, Awaitable<IEnumerable<I18n.PoEntry>>>`.
  - Line 43: change `public static class Locale` → `public static partial class Locale`.
  - Lines 48–67 (`Set`): body becomes fire-and-forget wrapper.
  - Lines 111–117 (`ReloadCurrent`): body becomes fire-and-forget wrapper.
  - Lines 130–141 (`LoadPoFiles`): converted to `async Awaitable LoadPoFilesAsync`; sync helper renamed `LoadPoFromPath` → `LoadPoFromResourcesPath`.
  - Add in Task 1 (internal/private): `LoadPoFilesAndApplyAsync` (internal), `LoadPoFilesAndApplyAsyncLogged` (private), `ReloadCurrentAsyncInternal` (internal), `ReloadCurrentAsyncLogged` (private).
  - Add in Task 2 (public): `SetAsync`, `ReloadCurrentAsync` — both delegate to the internal helpers from Task 1.
- `Tests/PlayMode/E2E/I18nHotReloadTests.cs:22` — migrate `PoResolver = _ => Enumerable.Empty<PoEntry>()` → async-completed form.
- `Tests/PlayMode/E2E/I18nFontSwapTests.cs:31` — same migration.
- `.claude/skills/authoring-promptugui-xml/SKILL.md` — append a `.po file location / Addressables` subsection inside the i18n section (around line 320, after the existing `UI.Locale.Set` example), and add one row to the i18n reference table (around line 591).

**Unchanged:** `Samples~/`, `Runtime/Application/TranslationStore.cs`, `Runtime/Application/TrResolver.cs`, `Runtime/Core/I18n/PoParser.cs`, `Editor/UIAssetPostprocessor.cs` (Addressables .po hot reload is non-goal — see spec §8 / LAR-D13), all other Runtime / Editor / test files.

---

## Task 1: PoResolver async signature + LoadPoFilesAsync refactor

Foundation. Drives a compile failure first by adding one test that requires the new signature; then changes the signature, refactors the sync `LoadPoFiles` to an async chain, makes `Locale` partial, and converts sync `Set`/`ReloadCurrent` to fire-and-forget wrappers. Public `SetAsync`/`ReloadCurrentAsync` and the race guard come in Task 2 — Task 1 stops at "the minimum required for green compile + existing tests still pass".

**Files:**
- Create: `Tests/EditMode/Application/LocaleSetAsyncTests.cs`
- Modify: `Runtime/Application/UI.cs`
- Modify: `Tests/PlayMode/E2E/I18nHotReloadTests.cs:22`
- Modify: `Tests/PlayMode/E2E/I18nFontSwapTests.cs:31`

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Application/LocaleSetAsyncTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.I18n;

namespace PromptUGUI.Tests.Application
{
    public class LocaleSetAsyncTests
    {
        [SetUp] public void Setup() => UI.ResetForTests();
        [TearDown] public void Teardown() => UI.ResetForTests();

        [Test]
        public void Set_with_completed_PoResolver_loads_translations_synchronously()
        {
            // Sync-completed Awaitable: integral regression contract that the new
            // async PoResolver shape keeps sync-completion semantics for callers
            // who don't actually need to defer (Resources path, in-memory tests).
            UI.PoResolver = _ => AwaitableHelpers.Completed<IEnumerable<PoEntry>>(new[]
            {
                new PoEntry { Msgid = "hi", Msgstr = "Hello" },
            });
            UI.Locale.Set("en");
            Assert.AreEqual("en", UI.Locale.Current);
            Assert.AreEqual("Hello", UI.Tr("hi"),
                "Sync-completed PoResolver path must populate TranslationStore " +
                "before Set returns (preserves pre-async-shape behavior).");
        }
    }
}
```

- [ ] **Step 2: Refresh Unity and verify the test fails to compile**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: at least one error referencing line `UI.PoResolver = _ => AwaitableHelpers.Completed<IEnumerable<PoEntry>>(...)` — current type is `Func<string, IEnumerable<PoEntry>>` so the lambda body returning `Awaitable<IEnumerable<PoEntry>>` is incompatible. If the test compiles, the previous PoResolver type didn't change — re-read the file.

- [ ] **Step 3: Change PoResolver signature, make `Locale` partial, refactor `LoadPoFiles`**

Open `Runtime/Application/UI.cs`.

3a. Line 24 — change PoResolver type:

```csharp
public static System.Func<string, UnityEngine.Awaitable<IEnumerable<I18n.PoEntry>>> PoResolver { get; set; }
```

3b. Line 43 — make Locale partial:

```csharp
public static partial class Locale
{
```

3c. Lines 48–67 — replace the `Set` body with fire-and-forget:

```csharp
public static void Set(string locale)
{
    if (Current == locale) return;
    if (Current != null)
    {
        VariantStore.Set(Current, false);
        TranslationStore.Instance.UnloadLocale(Current);
    }
    Current = locale;
    if (locale != null)
    {
        _ = LoadPoFilesAndApplyAsyncLogged(locale);
    }
    else
    {
        VariantStore.NotifyChangedInternal();
        Changed?.Invoke();
    }
}
```

3d. Lines 111–117 — replace the `ReloadCurrent` body with fire-and-forget:

```csharp
public static void ReloadCurrent()
{
    if (Current == null) return;
    _ = ReloadCurrentAsyncLogged();
}
```

3e. Add the private async helpers inside the `Locale` class (anywhere after `ReloadCurrent`, before `ResetForTestsInternal`):

```csharp
internal static async UnityEngine.Awaitable LoadPoFilesAndApplyAsync(string locale)
{
    await LoadPoFilesAsync(locale);
    // Task 2 will insert: if (Current != locale) return;  (race guard)
    VariantStore.Set(locale, true);
    Changed?.Invoke();
}

private static async UnityEngine.Awaitable LoadPoFilesAndApplyAsyncLogged(string locale)
{
    try { await LoadPoFilesAndApplyAsync(locale); }
    catch (System.Exception e)
    {
        UnityEngine.Debug.LogError(
            $"[PromptUGUI] locale load failed for '{locale}': {e}");
    }
}

internal static async UnityEngine.Awaitable ReloadCurrentAsyncInternal()
{
    if (Current == null) return;
    TranslationStore.Instance.UnloadLocale(Current);
    await LoadPoFilesAsync(Current);
    VariantStore.NotifyChangedInternal();
}

private static async UnityEngine.Awaitable ReloadCurrentAsyncLogged()
{
    try { await ReloadCurrentAsyncInternal(); }
    catch (System.Exception e)
    {
        UnityEngine.Debug.LogError(
            $"[PromptUGUI] locale reload failed for '{Current}': {e}");
    }
}
```

(Note `LoadPoFilesAndApplyAsync` and `ReloadCurrentAsyncInternal` are `internal` rather than `private` so Task 2's `SetAsync` / public `ReloadCurrentAsync` can call them without code duplication. `*Logged` wrappers stay `private` — they're the fire-and-forget tails for sync entry points only.)

3f. Lines 130–160 — convert `LoadPoFiles` → `LoadPoFilesAsync` and rename `LoadPoFromPath` → `LoadPoFromResourcesPath`:

```csharp
private static async UnityEngine.Awaitable LoadPoFilesAsync(string locale)
{
    if (PoResolver != null)
    {
        var entries = await PoResolver(locale);
        // Task 2 will insert: if (Current != locale) return;  (race guard)
        if (entries != null)
            TranslationStore.Instance.Load(locale, entries);
        return;
    }
    LoadPoFromResourcesPath($"PromptUGUI/i18n/{locale}", locale);
    LoadPoFromResourcesPath($"PromptUGUI/i18n-custom/{locale}", locale);
}

private static void LoadPoFromResourcesPath(string resourcesPath, string locale)
{
    var assets = UnityEngine.Resources.LoadAll<UnityEngine.TextAsset>(resourcesPath);
    foreach (var asset in assets)
    {
        try
        {
            var entries = new System.Collections.Generic.List<I18n.PoEntry>(
                I18n.PoParser.Parse(asset.text));
            TranslationStore.Instance.Load(locale, entries);
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError(
                $"[PromptUGUI] failed to parse .po asset '{asset.name}': {e.Message}");
        }
    }
}
```

The async marker on `LoadPoFilesAsync` is needed even when `PoResolver` is null — the method signature is `Awaitable`, so the compiler turns the `return` into a completed-Awaitable return. No state machine penalty when no `await` runs.

- [ ] **Step 4: Migrate `Tests/PlayMode/E2E/I18nHotReloadTests.cs:22`**

Open `Tests/PlayMode/E2E/I18nHotReloadTests.cs`. Find line 22:

```csharp
UI.PoResolver = _ => System.Linq.Enumerable.Empty<PoEntry>();
```

Replace with:

```csharp
UI.PoResolver = _ => AwaitableHelpers.Completed<System.Collections.Generic.IEnumerable<PoEntry>>(
    System.Array.Empty<PoEntry>());
```

`AwaitableHelpers` is `internal` in `PromptUGUI.Application`; the PlayMode test assembly has `InternalsVisibleTo` access per `Runtime/AssemblyInfo.cs:5`.

- [ ] **Step 5: Migrate `Tests/PlayMode/E2E/I18nFontSwapTests.cs:31`**

Same replacement as Step 4, applied at line 31 of `I18nFontSwapTests.cs`.

- [ ] **Step 6: Refresh and verify clean compile**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: zero errors. If any error mentions `LoadPoFiles` or `LoadPoFromPath` (the old names) — those are stale references; grep the codebase to confirm only the old definitions inside `UI.cs` were touched and other call sites still see the public surface (`Set` / `ReloadCurrent`).

- [ ] **Step 7: Run the new test**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="LocaleSetAsyncTests")
```

Expected: 1 test pass (`Set_with_completed_PoResolver_loads_translations_synchronously`).

- [ ] **Step 8: Regression — run the existing Locale + i18n EditMode tests**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="Locale")
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="Translation")
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TrResolver")
```

Expected: all green. `LocaleSetTests` covers `Set_UpdatesCurrent_FiresChanged`, `Set_TogglesVariantNamedAfterLocale`, etc. — these all rely on `Set` being effectively synchronous for the default (Resources) path. Because `LoadPoFilesAsync` doesn't `await` when `PoResolver` is null and the fire-and-forget call site `_ = LoadPoFilesAndApplyAsyncLogged(locale)` runs the entire async state machine synchronously up to the first real `await`, sync behavior is preserved. If any of these tests fail, the sync-completion contract is broken — diagnose before continuing.

- [ ] **Step 9: Commit**

```bash
git add Runtime/Application/UI.cs \
        Tests/EditMode/Application/LocaleSetAsyncTests.cs \
        Tests/PlayMode/E2E/I18nHotReloadTests.cs \
        Tests/PlayMode/E2E/I18nFontSwapTests.cs
git commit -m "$(cat <<'EOF'
refactor: make UI.PoResolver async-shaped

Switch PoResolver from sync IEnumerable<PoEntry> to async
Awaitable<IEnumerable<PoEntry>>. Locale.Set/ReloadCurrent become fire-and-forget
wrappers; sync-completed resolvers (Resources path) preserve existing behavior.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Public `SetAsync` + `ReloadCurrentAsync` + race guard (tests + impl)

Add the two `*Async` public methods + the `Current != locale` race guard, driven by six new tests in `LocaleSetAsyncTests.cs`. Tests describe the full async surface; implementation is a tight set of changes to `UI.cs`.

**Files:**
- Modify: `Tests/EditMode/Application/LocaleSetAsyncTests.cs`
- Modify: `Runtime/Application/UI.cs`

- [ ] **Step 1: Write the six new tests**

Append the following tests to `Tests/EditMode/Application/LocaleSetAsyncTests.cs` (inside the existing class body):

```csharp
        [Test]
        public void SetAsync_with_completed_PoResolver_loads_translations()
        {
            UI.PoResolver = _ => AwaitableHelpers.Completed<IEnumerable<PoEntry>>(new[]
            {
                new PoEntry { Msgid = "hi", Msgstr = "Bonjour" },
            });
            UI.Locale.SetAsync("fr").GetAwaiter().GetResult();
            Assert.AreEqual("fr", UI.Locale.Current);
            Assert.AreEqual("Bonjour", UI.Tr("hi"));
        }

        [Test]
        public void SetAsync_propagates_resolver_exception()
        {
            UI.PoResolver = _ =>
                AwaitableHelpers.Faulted<IEnumerable<PoEntry>>(
                    new System.IO.IOException("boom"));
            var ex = Assert.Throws<System.IO.IOException>(
                () => UI.Locale.SetAsync("en").GetAwaiter().GetResult(),
                "SetAsync should surface resolver exceptions to the awaiting caller");
            StringAssert.Contains("boom", ex.Message);
        }

        [Test]
        public void Set_fire_and_forget_logs_error_on_resolver_throw()
        {
            UI.PoResolver = _ =>
                AwaitableHelpers.Faulted<IEnumerable<PoEntry>>(
                    new System.IO.IOException("boom-sync"));
            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "locale load failed for 'en'.*boom-sync"));
            UI.Locale.Set("en");
            // Locale.Current still advances even when load fails — caller can retry.
            Assert.AreEqual("en", UI.Locale.Current);
        }

        [Test]
        public void Set_rapid_consecutive_with_pending_resolver_discards_stale_load()
        {
            // Race scenario: Set("zh-Hans") starts a deferred load; before it
            // completes, Set("en") supersedes it. When the zh-Hans load finally
            // resolves, the guard inside LoadPoFilesAsync must drop the result.
            var srcZh = new UnityEngine.AwaitableCompletionSource<IEnumerable<PoEntry>>();
            var srcEn = new UnityEngine.AwaitableCompletionSource<IEnumerable<PoEntry>>();
            UI.PoResolver = locale =>
                locale == "zh-Hans" ? srcZh.Awaitable : srcEn.Awaitable;

            UI.Locale.Set("zh-Hans");
            UI.Locale.Set("en");
            Assert.AreEqual("en", UI.Locale.Current);

            // Resume en first, then zh — the guard must drop zh entries.
            srcEn.SetResult(new[] { new PoEntry { Msgid = "hi", Msgstr = "Hello" } });
            srcZh.SetResult(new[] { new PoEntry { Msgid = "hi", Msgstr = "你好" } });

            Assert.AreEqual("Hello", UI.Tr("hi"),
                "Race guard must keep en translations; zh-Hans load is stale.");
            Assert.IsFalse(UI.Variants.IsActive("zh-Hans"),
                "Stale zh-Hans load must not flip its variant back on.");
        }

        [Test]
        public void ReloadCurrentAsync_with_completed_PoResolver_reloads_translations()
        {
            // Initial load: "hi" → "Hello"
            UI.PoResolver = _ => AwaitableHelpers.Completed<IEnumerable<PoEntry>>(new[]
            {
                new PoEntry { Msgid = "hi", Msgstr = "Hello" },
            });
            UI.Locale.Set("en");
            Assert.AreEqual("Hello", UI.Tr("hi"));

            // Swap the resolver to return a new translation, then ReloadCurrent.
            UI.PoResolver = _ => AwaitableHelpers.Completed<IEnumerable<PoEntry>>(new[]
            {
                new PoEntry { Msgid = "hi", Msgstr = "Howdy" },
            });
            UI.Locale.ReloadCurrentAsync().GetAwaiter().GetResult();
            Assert.AreEqual("Howdy", UI.Tr("hi"));
        }

        [Test]
        public void ReloadCurrentAsync_returns_immediately_when_Current_is_null()
        {
            // Current is null after ResetForTests; ReloadCurrentAsync should be a no-op
            // (no resolver invocation, no exception).
            var invocations = 0;
            UI.PoResolver = _ =>
            {
                invocations++;
                return AwaitableHelpers.Completed<IEnumerable<PoEntry>>(System.Array.Empty<PoEntry>());
            };
            UI.Locale.ReloadCurrentAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, invocations);
        }
```

- [ ] **Step 2: Refresh and verify the tests fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile errors for `UI.Locale.SetAsync(...)` and `UI.Locale.ReloadCurrentAsync()` — neither method exists yet as `public`. If the compile succeeds, Task 1 may have already promoted these — go check.

- [ ] **Step 3: Add public `SetAsync` and `ReloadCurrentAsync`, insert the race guards**

Open `Runtime/Application/UI.cs`. Inside `public static partial class Locale`, add the two new public methods immediately after `Set`:

```csharp
public static async UnityEngine.Awaitable SetAsync(string locale)
{
    if (Current == locale) return;
    if (Current != null)
    {
        VariantStore.Set(Current, false);
        TranslationStore.Instance.UnloadLocale(Current);
    }
    Current = locale;
    if (locale != null)
    {
        await LoadPoFilesAndApplyAsync(locale);
    }
    else
    {
        VariantStore.NotifyChangedInternal();
        Changed?.Invoke();
    }
}

public static async UnityEngine.Awaitable ReloadCurrentAsync()
{
    if (Current == null) return;
    await ReloadCurrentAsyncInternal();
}
```

Add the race guard inside both async helpers from Task 1.

3a. In `LoadPoFilesAsync` (created in Task 1, Step 3f), add the guard after the `await PoResolver(locale)` line:

```csharp
private static async UnityEngine.Awaitable LoadPoFilesAsync(string locale)
{
    if (PoResolver != null)
    {
        var entries = await PoResolver(locale);
        if (Current != locale) return;          // race guard: stale load
        if (entries != null)
            TranslationStore.Instance.Load(locale, entries);
        return;
    }
    LoadPoFromResourcesPath($"PromptUGUI/i18n/{locale}", locale);
    LoadPoFromResourcesPath($"PromptUGUI/i18n-custom/{locale}", locale);
}
```

3b. In `LoadPoFilesAndApplyAsync` (created in Task 1, Step 3e), add the guard after `await LoadPoFilesAsync`:

```csharp
internal static async UnityEngine.Awaitable LoadPoFilesAndApplyAsync(string locale)
{
    await LoadPoFilesAsync(locale);
    if (Current != locale) return;              // race guard: don't flip variant for stale
    VariantStore.Set(locale, true);
    Changed?.Invoke();
}
```

Both guards are necessary: the inner one prevents stale entries entering `TranslationStore`; the outer one prevents the stale locale's variant flipping back on. Without the inner guard, a stale load would pollute `TranslationStore.Instance` (entries from a locale that's no longer current); without the outer, even with empty entries the variant would be re-activated.

- [ ] **Step 4: Refresh and run the six new tests**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="LocaleSetAsyncTests")
```

Expected: all 7 tests pass (1 from Task 1 + 6 new). If `Set_rapid_consecutive_with_pending_resolver_discards_stale_load` fails specifically and the others pass, EditMode may be unable to resume `AwaitableCompletionSource` continuations without a player loop. In that case, mark the test `[Ignore("EditMode lacks player-loop SynchronizationContext for AwaitableCompletionSource continuations; covered by PlayMode follow-up")]` and proceed — the guard itself is still implemented and visually inspectable in `LoadPoFilesAsync` / `LoadPoFilesAndApplyAsync`.

- [ ] **Step 5: Regression — run all EditMode locale + i18n tests**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="Locale")
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="Translation")
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TrResolver")
```

Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application/UI.cs Tests/EditMode/Application/LocaleSetAsyncTests.cs
git commit -m "$(cat <<'EOF'
feat: add Locale.SetAsync / ReloadCurrentAsync with race guard

Public awaitable variants for callers that need ordering. Race guard inside
LoadPoFilesAsync + LoadPoFilesAndApplyAsync drops loads whose locale no longer
matches Current — prevents pollution from rapid consecutive Set() calls.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: `LocaleAddressableResolverHelper.cs` — tests + implementation

Wires Addressables into PoResolver. Tests + implementation written together (mirrors the `AddressableIconResolverHelper` plan structure: synchronous wiring is what gets exercised in EditMode; end-to-end "label → .po → TranslationStore" verification is an EditMode-async limitation documented inline).

**Files:**
- Create: `Tests/EditMode/Addressables/LocaleAddressableResolverTests.cs`
- Create: `Runtime/Application/LocaleAddressableResolverHelper.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/EditMode/Addressables/LocaleAddressableResolverTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEditor.AddressableAssets;

namespace PromptUGUI.Tests.Addressables
{
    /// <summary>
    /// Wiring smoke tests for UI.Locale.UseAddressableResolver.
    ///
    /// End-to-end "label='zh-Hans' → .po TextAssets → TranslationStore" is NOT
    /// tested in EditMode: AsyncOperationHandle continuations need the player-loop
    /// SynchronizationContext, which is absent in EditMode test runners. Same
    /// limitation documented in AddressableResolverTests. Tests here cover only
    /// the synchronous registration prefix: PoResolver is non-null after the
    /// call, and invoking it returns a non-null Awaitable.
    /// </summary>
    public class LocaleAddressableResolverTests
    {
        [SetUp]
        public void Setup()
        {
            UI.ResetForTests();
            // Ensure AddressableAssetSettings exists; without it
            // Addressables.LoadAssetsAsync throws synchronously in a fresh project.
            _ = AddressableAssetSettingsDefaultObject.Settings
                ?? AddressableAssetSettingsDefaultObject.GetSettings(true);
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void UseAddressableResolver_sets_PoResolver()
        {
            Assert.IsNull(UI.PoResolver,
                "PoResolver should be null after ResetForTests");
            UI.Locale.UseAddressableResolver();
            Assert.IsNotNull(UI.PoResolver,
                "PoResolver should be set after UseAddressableResolver");
        }

        [Test]
        public void PoResolver_invocation_after_register_returns_awaitable()
        {
            UI.Locale.UseAddressableResolver();
            var awaitable = UI.PoResolver("zh-Hans");
            Assert.IsNotNull(awaitable,
                "Registered resolver should return a non-null Awaitable. " +
                "Not awaited — EditMode has no player loop, the Addressables " +
                "AsyncOperationHandle won't resume; TearDown's ResetForTests " +
                "releases the handle.");
        }
    }
}
```

- [ ] **Step 2: Refresh and verify the tests fail to compile**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile error referencing `UI.Locale.UseAddressableResolver` not found. If no error, the helper may have leaked in from an earlier task — re-check.

- [ ] **Step 3: Create `LocaleAddressableResolverHelper.cs`**

Create `Runtime/Application/LocaleAddressableResolverHelper.cs`:

```csharp
#if PROMPTUGUI_HAS_ADDRESSABLES
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using PromptUGUI.I18n;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        public static partial class Locale
        {
            /// <summary>
            /// 把 PoResolver 设为按 label=locale 加载所有 .po TextAsset 的 Addressables 实现。
            /// Set("zh-Hans") → Addressables.LoadAssetsAsync&lt;TextAsset&gt;("zh-Hans", null)。
            /// 仅在装了 com.unity.addressables 时存在（PROMPTUGUI_HAS_ADDRESSABLES 编译定义）。
            ///
            /// 注意 fire-and-forget 模型下 Set 返回后 UI 还看到 msgid，要等下载完才切译文；
            /// 想避免闪烁用 await Locale.SetAsync(...)。
            /// </summary>
            public static void UseAddressableResolver()
            {
                PoResolver = LoadPoFromAddressablesAsync;
            }

            private static async Awaitable<IEnumerable<PoEntry>> LoadPoFromAddressablesAsync(
                string locale)
            {
                var handle = Addressables.LoadAssetsAsync<TextAsset>(locale, null);
                try
                {
                    var assets = await handle.Task;
                    var entries = new List<PoEntry>();
                    foreach (var ta in assets ?? Array.Empty<TextAsset>())
                    {
                        if (ta == null) continue;
                        try
                        {
                            foreach (var e in PoParser.Parse(ta.text)) entries.Add(e);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError(
                                $"[PromptUGUI] failed to parse .po asset '{ta.name}': {ex.Message}");
                        }
                    }
                    return entries;
                }
                finally
                {
                    if (handle.IsValid()) Addressables.Release(handle);
                }
            }
        }
    }
}
#endif
```

Three things to verify visually before refreshing:

1. The whole file is under `#if PROMPTUGUI_HAS_ADDRESSABLES` — `LoadPoFromAddressablesAsync` references `Addressables.LoadAssetsAsync`, which doesn't exist when the package isn't installed.
2. The file declares `partial class UI` AND nested `partial class Locale`, both `public static`. C# requires the `partial` modifier on every declaration that splits a type — both UI (already partial elsewhere) and Locale (made partial in Task 1).
3. Release happens in `finally` — `assets` are parsed before release because `PoParser.Parse(ta.text)` reads the TextAsset's text. Once `.text` is captured, releasing the handle is safe (the parsed `PoEntry` list doesn't hold UnityEngine.Object references).

- [ ] **Step 4: Refresh and run the new tests**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Addressables"], filter="LocaleAddressableResolverTests")
```

Expected: 2 tests pass.

- [ ] **Step 5: Regression — run all three EditMode assemblies**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Addressables"])
```

Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application/LocaleAddressableResolverHelper.cs \
        Tests/EditMode/Addressables/LocaleAddressableResolverTests.cs
git commit -m "$(cat <<'EOF'
feat: add UI.Locale.UseAddressableResolver

Addressables-backed PoResolver: Set('zh-Hans') loads all TextAssets with
Addressables label='zh-Hans', parses them, and feeds TranslationStore.
Gated by PROMPTUGUI_HAS_ADDRESSABLES.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: SKILL.md update

Per CLAUDE.md ("Public C# API surface changes" requires SKILL.md sync) and spec §5.3 / LAR-D14. Two edits in `.claude/skills/authoring-promptugui-xml/SKILL.md`: add the Addressables subsection inside the i18n section, and add the reference-table row at the bottom.

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`

- [ ] **Step 1: Locate the i18n examples block**

Open `.claude/skills/authoring-promptugui-xml/SKILL.md`. The i18n section starts around line 287. The code block ending with `UI.Locale.SetToSystemDefault();` is around lines 312–319. The reference table is around line 585.

- [ ] **Step 2: Append the .po file location / Addressables subsection**

Find this existing line (the "Reserved namespace" paragraph that closes the locale-switching example):

```markdown
**Reserved namespace**: `UI.Locale.Set("zh-Hans")` internally registers `zh-Hans` as an active Variant. Authors must NOT reuse a Variant of the same name to express anything other than locale state.
```

After that line, insert two blank lines and the following subsection:

````markdown
**.po file location**

By default `.po` files live in `Assets/Resources/PromptUGUI/i18n/<locale>/` or
`/PromptUGUI/i18n-custom/<locale>/`. Files anywhere under those paths are
picked up by `Resources.LoadAll<TextAsset>`; subfolder names are ignored.

When the project ships `.po` via Addressables, call
`UI.Locale.UseAddressableResolver()` at boot. The resolver loads every TextAsset
whose Addressables **label matches the locale string** (so `Locale.Set("zh-Hans")`
loads everything labelled `zh-Hans`). Files can live anywhere. Only available
when `com.unity.addressables` ≥ 1.0 is installed (gated by
`PROMPTUGUI_HAS_ADDRESSABLES`).

```csharp
UI.Locale.UseAddressableResolver();
UI.Locale.Set("zh-Hans");                  // sync; UI shows msgid briefly during download
// or:
await UI.Locale.SetAsync("zh-Hans");       // awaits download + parse + ReSolve
```

`Locale.Set` returns immediately after issuing the load. While the download is
in flight, open Screens briefly fall back to msgid text; when the load
completes the locale variant flips on and all open Screens re-resolve to the
translated strings. `SetAsync` returns only after that re-resolve completes —
use it when you need to read `UI.Tr(...)` immediately after switching locales.
````

- [ ] **Step 3: Update the i18n reference table**

Find the table block around line 585:

```markdown
UI.Tr("...")                     C# extraction entry point
UI.Locale.Set("zh-Hans")         switch locale (= switch .po + switch font)
```

Append two rows after the `UI.Locale.Set` line:

```markdown
UI.Locale.SetAsync("zh-Hans")    awaitable variant; completes after .po load + ReSolve
UI.Locale.UseAddressableResolver()   load .po via Addressables, label = locale string
```

- [ ] **Step 4: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "$(cat <<'EOF'
docs: SKILL.md addressable locale + SetAsync

Document the new Locale.UseAddressableResolver / SetAsync surface in the
authoring guide so LLM authors discover the addressable-backed PO path.

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
chore: lint pass for locale addressable resolver

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
git log --oneline -6
```

Expected: clean working tree; commit log shows (in order) the PoResolver async refactor, the SetAsync + race guard, the Addressables helper, the SKILL.md update, and (optionally) the lint pass.

---

## Notes for the executing engineer

- **Test execution is via Unity MCP only.** Do not invoke `Unity.exe -batchmode` or similar. If `mcp__UnityMCP__run_tests` is unavailable, reconnect MCP or stop and ask the user.
- **Forbidden: `mcp__UnityMCP__execute_menu_item(menu_path="Assets/Reimport All")`** — pops a blocking modal in Unity that the user must dismiss manually. Use `refresh_unity(mode="force", scope="all")` instead.
- **After every code edit, refresh first, then `read_console` for errors before running tests.** Compile errors won't appear in the test runner output — they'll just look like missing test assemblies.
- **Filter tests by class name** with `filter="ClassName"` (substring match) for fast iteration during red-green cycles.
- **Locale stays sync on Resources path.** The whole point of the `AwaitableHelpers.Completed(...)` wrapping in `LoadPoFilesAsync` is to keep the existing sync-completion contract. If `LocaleSetTests` regresses (any test in there that calls `UI.Locale.Set("...")` then synchronously asserts on `UI.Tr(...)`), that contract is broken — investigate before papering over with `await SetAsync`.
- **Race guard is two lines, two places.** `LoadPoFilesAsync` after `await PoResolver(locale)` and `LoadPoFilesAndApplyAsync` after `await LoadPoFilesAsync(locale)`. Both are needed: the inner protects TranslationStore from stale entries; the outer protects VariantStore from stale flips.
- **AwaitableCompletionSource resumption in EditMode** is an open question — see Task 2 Step 4 fallback. If the race guard test specifically can't run in EditMode, `[Ignore]` it and document a follow-up PlayMode test rather than weakening the implementation.
- **`AwaitableHelpers` is internal.** Test assemblies (EditMode + EditMode.Addressables + PlayMode) have `InternalsVisibleTo` from `Runtime/AssemblyInfo.cs`. External library consumers must use `AwaitableCompletionSource<T>` (public Unity 6 API) — that migration is documented in spec §7.1.
- **Addressables call is bounded.** Unlike `UseAddressableResolver()` (documents) which loads per-key on every `LoadDocumentAsync` call, the locale resolver loads everything labelled `<locale>` at every `Set(locale)`. Caller-controlled scope: keep the per-locale `.po` count reasonable. Released immediately after parse (unlike icon resolver's permanent handle).
