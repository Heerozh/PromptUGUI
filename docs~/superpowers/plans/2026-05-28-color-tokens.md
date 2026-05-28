# Color Tokens Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add XML-declared color tokens (`<Theme><Color name=.../></Theme>`) with runtime swap (`UI.Theme.Set(...)`), and route `Image` / `Text` / `Btn` color setters through `UI.Theme.Resolve` per spec `2026-05-28-color-tokens-design.md`.

**Architecture:** Mirror the i18n pipeline. `ThemeStore` is the registry singleton (parallel to `TranslationStore`); `UI.Theme.*` is the public façade (parallel to `UI.Locale.*`); `Theme.Changed` triggers `Screen.ReSolve` (parallel to `Variants.Changed`). Color attr resolution is in-setter via `UI.Theme.Resolve(value)`, matching the established `IsSprite` / `UI.ResolveSprite` convention — no new applier branch.

**Tech Stack:** Unity 6, uGUI, R3 (Cysharp), NUnit + Unity Test Framework, Unity MCP for compile + test orchestration.

**Branch:** `feat/color-tokens` (already created; spec + spec amendment already committed: `bd48752`, `0871aae`).

**Verification cadence:** After every source edit, `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` → `mcp__UnityMCP__read_console(action="get", types=["error"])`. Run tests via `mcp__UnityMCP__run_tests(...)` with `filter=` per task. Per memory note: `run_tests` can be flaky; if it hangs, restart Unity and retry. Compile-clean is the floor for "task complete"; green tests confirm.

---

## File Structure

| Path | Role | Action |
|---|---|---|
| `Runtime/Registry/UIAttrAttribute.cs` | Add `bool IsColor` flag | **Modify** |
| `Runtime/Registry/ControlMeta.cs` | Collect `ColorAttrs` set (parallel to `SpriteAttrs`) | **Modify** |
| `Runtime/Application/ThemeStore.cs` | Theme registry singleton: register / clear / lookup with base chain / cycle validate | **Create** |
| `Runtime/Core/IR/ThemeBlock.cs`, `ColorEntry.cs` | Parser IR POCOs | **Create** |
| `Runtime/Core/IR/UIDocument.cs` | Add `List<ThemeBlock> Themes` field | **Modify** |
| `Runtime/Core/Parser/UIDocumentParser.cs` | Recognize `<Theme>` / `<Color>`; per-token validation | **Modify** |
| `Runtime/Application/DocumentLoader.cs` | Route parsed themes into `ThemeStore`; cross-doc conflict; base resolve | **Modify** |
| `Runtime/Application/UI.cs` | Nested `UI.Theme` static: `Current` / `Available` / `Set` / `Lookup` / `Resolve` / `Changed`; `ResetForTestsInternal` | **Modify** |
| `Runtime/Application/Screen.cs` | Subscribe `UI.Theme.Changed` → `ReSolve` | **Modify** |
| `Runtime/Controls/Image.cs:42-50` | `Color` setter → `UI.Theme.Resolve(value)`, mark `IsColor=true` | **Modify** |
| `Runtime/Controls/Text.cs:88-96` | same | **Modify** |
| `Runtime/Controls/Btn.cs:100-108` | same | **Modify** |
| `Runtime/Controls/Internal/AnimationSpec.cs` | `ParseColorFromTo` → `UI.Theme.Resolve` per part | **Modify** |
| `Editor/UIAssetPostprocessor.cs` / `Runtime/Application/UI.cs` HotReload path | Theme block hot reload → re-register + fire `Theme.Changed` | **Modify** |
| `Editor/XsdGenerator.cs` | Output `<Theme>` / `<Color>` schema | **Modify** |
| `Runtime/Core/Lint/ColorLiteralRules.cs` | Static rule: `color="#..."` literal must parse | **Create** |
| `Runtime/Core/Lint/IRWalker.cs` | Dispatch `ColorLiteralRules` | **Modify** |
| `Tests/EditMode/Registry/ControlMetaColorAttrsTests.cs` | `IsColor` flag collected | **Create** |
| `Tests/EditMode/Application/ThemeStoreTests.cs` | Storage + chain + cycle | **Create** |
| `Tests/EditMode/Application/UIThemeTests.cs` | `Set` / `Lookup` / `Resolve` / `Changed` | **Create** |
| `Tests/EditMode/Parser/ThemeParseTests.cs` | XML → IR | **Create** |
| `Tests/EditMode/Application/ThemeLoadingTests.cs` | Loader → store integration | **Create** |
| `Tests/EditMode/Controls/ColorTokenIntegrationTests.cs` | Setter routes through Resolve; loud failures | **Create** |
| `Tests/EditMode/Controls/AnimationCharColorTokenTests.cs` | `char-color` tokens | **Create** |
| `Tests/EditMode/Application/ThemeSwitchTests.cs` | E2E: open Screen, switch theme, controls re-color | **Create** |
| `Tests/EditMode/Editor/ThemeHotReloadTests.cs` | Editor-only: theme XML reload fires Changed | **Create** |
| `Tests/EditMode/Editor/XsdGeneratorTests.cs` | Add Theme / Color substring asserts | **Modify** |
| `Tests/EditMode/Lint/ColorLiteralRulesTests.cs` | Bad hex literal flagged | **Create** |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` | Add `<Theme>` / `<Color>` section, `color` attr shadow rule, error codes | **Modify** |
| `.claude/skills/scripting-promptugui-csharp/SKILL.md` | Add `UI.Theme.*` API, `[UIAttr(IsColor=true)]` guidance | **Modify** |
| `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` | §5 / §6 / §8 light touch | **Modify** |

---

## Tasks

### Task 1: `IsColor` flag + `ControlMeta.ColorAttrs` collection

**Files:**
- Modify: `Runtime/Registry/UIAttrAttribute.cs`
- Modify: `Runtime/Registry/ControlMeta.cs:8-60` (mirror `SpriteAttrs` collection)
- Create: `Tests/EditMode/Registry/ControlMetaColorAttrsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Registry/ControlMetaColorAttrsTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Registry;

namespace PromptUGUI.Tests.EditMode.Registry
{
    public class ControlMetaColorAttrsTests
    {
        private class FakeControl
        {
            [UIAttr(IsColor = true)] public string Color { get; set; }
            [UIAttr] public string Label { get; set; }
            [UIAttr(IsSprite = true)] public string Bg { get; set; }
        }

        [Test]
        public void ColorAttrs_Contains_Only_IsColor_Marked()
        {
            var meta = ControlMeta.Build(typeof(FakeControl));
            CollectionAssert.AreEquivalent(new[] { "color" }, meta.ColorAttrs);
            CollectionAssert.AreEquivalent(new[] { "bg" }, meta.SpriteAttrs);
        }
    }
}
```

- [ ] **Step 2: Run test, verify it fails (compile error — IsColor / ColorAttrs unknown)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile error referencing `IsColor` and `ColorAttrs`.

- [ ] **Step 3: Implement**

`Runtime/Registry/UIAttrAttribute.cs` — add after `IsSprite`:

```csharp
/// <summary>
/// Marks this attribute as carrying a color reference (resolved via
/// <c>UI.Theme.Resolve</c>). The Editor-side lint pipeline reads this flag
/// to know which attribute names carry colors. Runtime resolution is in
/// the setter itself (parallel to <c>IsSprite</c> + <c>UI.ResolveSprite</c>);
/// the applier does not branch on this flag.
/// </summary>
public bool IsColor { get; set; }
```

`Runtime/Registry/ControlMeta.cs` — add `ColorAttrs` field + collection in `Build`:

```csharp
// Add field next to SpriteAttrs:
public IReadOnlyCollection<string> ColorAttrs { get; }

// In ctor, accept colorAttrs:
private ControlMeta(Dictionary<string, Action<object, string>> setters,
                    IReadOnlyCollection<string> spriteAttrs,
                    IReadOnlyCollection<string> colorAttrs)
{
    _setters = setters;
    SpriteAttrs = spriteAttrs;
    ColorAttrs = colorAttrs;
}

// In Build, after `var spriteAttrs = new List<string>();`:
var colorAttrs = new List<string>();

// In the loop, after `if (attr.IsSprite) spriteAttrs.Add(name);`:
if (attr.IsColor) colorAttrs.Add(name);

// Return:
return new ControlMeta(setters, spriteAttrs, colorAttrs);
```

- [ ] **Step 4: Run test, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ControlMetaColorAttrsTests")
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Registry/UIAttrAttribute.cs Runtime/Registry/ControlMeta.cs Tests/EditMode/Registry/ControlMetaColorAttrsTests.cs
git commit -m "feat: UIAttr.IsColor flag + ControlMeta.ColorAttrs"
```

---

### Task 2: `ThemeStore` singleton

**Files:**
- Create: `Runtime/Application/ThemeStore.cs`
- Create: `Tests/EditMode/Application/ThemeStoreTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Application/ThemeStoreTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Application
{
    public class ThemeStoreTests
    {
        [SetUp] public void SetUp() => ThemeStore.Instance.Clear();
        [TearDown] public void TearDown() => ThemeStore.Instance.Clear();

        private static Dictionary<string, Color> Map(params (string k, string v)[] entries)
        {
            var d = new Dictionary<string, Color>();
            foreach (var (k, v) in entries)
            {
                ColorUtility.TryParseHtmlString(v, out var c);
                d[k] = c;
            }
            return d;
        }

        [Test]
        public void Register_And_Lookup_Hits_Same_Theme()
        {
            ThemeStore.Instance.Register("light", baseName: null, Map(("primary", "#ff8800")), src: "t");
            ThemeStore.Instance.ResolveBases();
            var c = ThemeStore.Instance.LookupChained("light", "primary");
            Assert.IsTrue(c.HasValue);
            Assert.AreEqual(new Color32(0xff, 0x88, 0x00, 0xff), (Color32)c.Value);
        }

        [Test]
        public void Lookup_Walks_Base_Chain()
        {
            ThemeStore.Instance.Register("light", null, Map(("primary", "#ff8800"), ("bg", "#ffffff")), "t");
            ThemeStore.Instance.Register("dark", baseName: "light", Map(("primary", "#cc6600")), "t");
            ThemeStore.Instance.ResolveBases();
            // bg not in dark → walks to light
            var bg = ThemeStore.Instance.LookupChained("dark", "bg");
            Assert.IsTrue(bg.HasValue);
            // primary in dark → returns dark's
            var p = ThemeStore.Instance.LookupChained("dark", "primary");
            Assert.AreEqual(new Color32(0xcc, 0x66, 0x00, 0xff), (Color32)p.Value);
        }

        [Test]
        public void Lookup_Missing_Returns_Null()
        {
            ThemeStore.Instance.Register("light", null, Map(("primary", "#ff8800")), "t");
            ThemeStore.Instance.ResolveBases();
            Assert.IsNull(ThemeStore.Instance.LookupChained("light", "nope"));
        }

        [Test]
        public void ResolveBases_Throws_On_Missing_Base()
        {
            ThemeStore.Instance.Register("dark", baseName: "ghost", Map(("primary", "#000000")), "t");
            var ex = Assert.Throws<ParseException>(() => ThemeStore.Instance.ResolveBases());
            StringAssert.Contains("ghost", ex.Message);
            StringAssert.Contains("not found", ex.Message);
        }

        [Test]
        public void ResolveBases_Throws_On_Cycle()
        {
            ThemeStore.Instance.Register("a", baseName: "b", Map(), "t");
            ThemeStore.Instance.Register("b", baseName: "a", Map(), "t");
            var ex = Assert.Throws<ParseException>(() => ThemeStore.Instance.ResolveBases());
            StringAssert.Contains("cycle", ex.Message.ToLowerInvariant());
        }

        [Test]
        public void Register_Duplicate_Name_Throws_With_Both_Srcs()
        {
            ThemeStore.Instance.Register("light", null, Map(), "themes/main");
            var ex = Assert.Throws<ParseException>(() =>
                ThemeStore.Instance.Register("light", null, Map(), "themes/extra"));
            StringAssert.Contains("themes/main", ex.Message);
            StringAssert.Contains("themes/extra", ex.Message);
        }
    }
}
```

- [ ] **Step 2: Run test, verify it fails**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile error — `ThemeStore` not found.

- [ ] **Step 3: Implement**

Create `Runtime/Application/ThemeStore.cs`:

```csharp
using System.Collections.Generic;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// Singleton holding parsed &lt;Theme&gt; blocks. Mirrors <c>TranslationStore</c>:
    /// loaders register, runtime looks up. Hot reload routes through <c>ReplaceFromSrc</c>.
    /// Public surface is <c>UI.Theme.*</c>; this class is internal to keep the surface tight.
    /// </summary>
    internal sealed class ThemeStore
    {
        public static ThemeStore Instance { get; } = new();

        private sealed class Entry
        {
            public string Name;
            public string BaseName;
            public Dictionary<string, Color> Colors;
            public string Src;
            public Entry ResolvedBase;
        }

        private readonly Dictionary<string, Entry> _themes = new();

        public IReadOnlyCollection<string> Available => _themes.Keys;

        public void Register(string name, string baseName,
                             IReadOnlyDictionary<string, Color> colors, string src)
        {
            if (_themes.TryGetValue(name, out var existing))
                throw new ParseException(
                    $"duplicate <Theme name=\"{name}\"> in '{existing.Src}' and '{src}'");
            _themes[name] = new Entry
            {
                Name = name,
                BaseName = baseName,
                Colors = new Dictionary<string, Color>(colors),
                Src = src,
            };
        }

        public void ReplaceFromSrc(string src,
            IReadOnlyList<(string name, string baseName, IReadOnlyDictionary<string, Color> colors)> blocks)
        {
            // Hot reload: drop everything previously from src, then register new.
            var toRemove = new List<string>();
            foreach (var kv in _themes)
                if (kv.Value.Src == src) toRemove.Add(kv.Key);
            foreach (var k in toRemove) _themes.Remove(k);
            foreach (var b in blocks)
                Register(b.name, b.baseName, b.colors, src);
            ResolveBases();
        }

        public void ResolveBases()
        {
            foreach (var e in _themes.Values)
            {
                if (string.IsNullOrEmpty(e.BaseName)) { e.ResolvedBase = null; continue; }
                if (!_themes.TryGetValue(e.BaseName, out var b))
                    throw new ParseException(
                        $"<Theme name=\"{e.Name}\" base=\"{e.BaseName}\">: " +
                        $"base theme '{e.BaseName}' not found");
                e.ResolvedBase = b;
            }
            // Cycle: DFS from each theme, fail if we revisit.
            foreach (var e in _themes.Values)
            {
                var seen = new HashSet<string>();
                for (var cur = e; cur != null; cur = cur.ResolvedBase)
                {
                    if (!seen.Add(cur.Name))
                        throw new ParseException(
                            $"<Theme> base cycle starting at '{e.Name}': " +
                            string.Join(" → ", seen) + $" → {cur.Name}");
                }
            }
        }

        public Color? LookupChained(string themeName, string token)
        {
            if (!_themes.TryGetValue(themeName, out var e)) return null;
            for (var cur = e; cur != null; cur = cur.ResolvedBase)
            {
                if (cur.Colors.TryGetValue(token, out var c)) return c;
            }
            return null;
        }

        public void Clear() => _themes.Clear();
    }
}
```

- [ ] **Step 4: Run test, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ThemeStoreTests")
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/ThemeStore.cs Tests/EditMode/Application/ThemeStoreTests.cs
git commit -m "feat: ThemeStore (registry + base chain + cycle validate)"
```

---

### Task 3: `UI.Theme` API surface + `ResetForTestsInternal`

**Files:**
- Modify: `Runtime/Application/UI.cs` (add nested `static class Theme` near `Locale`; wire into `ResetForTests`)
- Create: `Tests/EditMode/Application/UIThemeTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Application/UIThemeTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Application
{
    public class UIThemeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void Seed(string name, string baseName, params (string k, string v)[] entries)
        {
            var d = new Dictionary<string, Color>();
            foreach (var (k, v) in entries) { ColorUtility.TryParseHtmlString(v, out var c); d[k] = c; }
            ThemeStore.Instance.Register(name, baseName, d, src: "test");
            ThemeStore.Instance.ResolveBases();
        }

        [Test]
        public void Set_Unknown_Throws()
        {
            Assert.Throws<ArgumentException>(() => UI.Theme.Set("nope"));
        }

        [Test]
        public void Set_Updates_Current_And_Fires_Changed()
        {
            Seed("light", null, ("primary", "#ff8800"));
            string fired = null;
            UI.Theme.Changed += n => fired = n;
            UI.Theme.Set("light");
            Assert.AreEqual("light", UI.Theme.Current);
            Assert.AreEqual("light", fired);
        }

        [Test]
        public void Resolve_With_No_Theme_Falls_Back_To_Hex()
        {
            Assert.AreEqual(new Color32(0xff, 0x00, 0x00, 0xff),
                            (Color32)UI.Theme.Resolve("#ff0000"));
        }

        [Test]
        public void Resolve_Token_Hit_Returns_Color()
        {
            Seed("light", null, ("primary", "#ff8800"));
            UI.Theme.Set("light");
            Assert.AreEqual(new Color32(0xff, 0x88, 0x00, 0xff),
                            (Color32)UI.Theme.Resolve("primary"));
        }

        [Test]
        public void Resolve_Token_Miss_Falls_Through_To_Hex()
        {
            Seed("light", null, ("primary", "#ff8800"));
            UI.Theme.Set("light");
            Assert.AreEqual(new Color32(0x00, 0xff, 0x00, 0xff),
                            (Color32)UI.Theme.Resolve("#00ff00"));
        }

        [Test]
        public void Resolve_Unknown_Token_Unknown_Hex_Throws()
        {
            Seed("light", null, ("primary", "#ff8800"));
            UI.Theme.Set("light");
            var ex = Assert.Throws<Exception>(() => UI.Theme.Resolve("primaru"));
            StringAssert.Contains("primaru", ex.Message);
            StringAssert.Contains("light", ex.Message);
        }

        [Test]
        public void Resolve_Empty_Throws()
        {
            Assert.Throws<Exception>(() => UI.Theme.Resolve(""));
            Assert.Throws<Exception>(() => UI.Theme.Resolve(null));
        }

        [Test]
        public void Lookup_When_No_Current_Returns_Null()
        {
            Seed("light", null, ("primary", "#ff8800"));
            // didn't Set
            Assert.IsNull(UI.Theme.Lookup("primary"));
        }

        [Test]
        public void Lookup_Walks_Base()
        {
            Seed("light", null, ("primary", "#ff8800"), ("bg", "#ffffff"));
            Seed("dark", "light", ("primary", "#cc6600"));
            UI.Theme.Set("dark");
            Assert.IsTrue(UI.Theme.Lookup("bg").HasValue);  // from light
        }

        [Test]
        public void ResetForTests_Clears_Store_And_Current()
        {
            Seed("light", null, ("primary", "#ff8800"));
            UI.Theme.Set("light");
            UI.ResetForTests();
            Assert.IsNull(UI.Theme.Current);
            CollectionAssert.IsEmpty(UI.Theme.Available);
        }
    }
}
```

- [ ] **Step 2: Run test, verify fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile error — `UI.Theme` not found.

- [ ] **Step 3: Implement**

In `Runtime/Application/UI.cs`, add nested static class `Theme` after the `Locale` class (around line 222). Insert the following:

```csharp
public static partial class Theme
{
    public static string Current { get; private set; }
    public static IReadOnlyCollection<string> Available => ThemeStore.Instance.Available;

    public static event System.Action<string> Changed;

    public static void Set(string name)
    {
        if (name == null) throw new System.ArgumentNullException(nameof(name));
        var available = ThemeStore.Instance.Available;
        if (!System.Linq.Enumerable.Contains(available, name))
            throw new System.ArgumentException(
                $"UI.Theme.Set: theme '{name}' not registered (available: " +
                string.Join(", ", available) + ")");
        if (Current == name) return;
        Current = name;
        Changed?.Invoke(name);
    }

    public static UnityEngine.Color? Lookup(string token)
    {
        if (Current == null) return null;
        return ThemeStore.Instance.LookupChained(Current, token);
    }

    public static UnityEngine.Color Resolve(string value)
    {
        if (string.IsNullOrEmpty(value))
            throw new System.Exception("empty color value");
        if (Current != null)
        {
            var hit = ThemeStore.Instance.LookupChained(Current, value);
            if (hit.HasValue) return hit.Value;
        }
        if (UnityEngine.ColorUtility.TryParseHtmlString(value, out var c))
            return c;
        throw new System.Exception(
            $"unknown color token \"{value}\" (no entry in theme " +
            $"'{Current ?? "(none)"}', not a valid hex/named literal)");
    }

    internal static void ResetForTestsInternal()
    {
        Current = null;
        Changed = null;
        ThemeStore.Instance.Clear();
    }

    /// <summary>Called by DocumentLoader after loading commons; if only one
    /// theme is registered and Current is unset, auto-select it (single-theme
    /// projects work without explicit Set).</summary>
    internal static void AutoSetIfSingleAvailable()
    {
        if (Current != null) return;
        var available = ThemeStore.Instance.Available;
        if (available.Count != 1) return;
        var only = System.Linq.Enumerable.First(available);
        Current = only;
        Changed?.Invoke(only);
    }
}
```

And in `UI.ResetForTests()` (around line 725), add the call:

```csharp
internal static void ResetForTests()
{
    Locale.ResetForTestsInternal();
    Orientation.ResetForTestsInternal();
    Theme.ResetForTestsInternal();   // ← add this line
    // ... rest
}
```

- [ ] **Step 4: Run test, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="UIThemeTests")
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/UI.cs Tests/EditMode/Application/UIThemeTests.cs
git commit -m "feat: UI.Theme API (Current/Set/Lookup/Resolve/Changed)"
```

---

### Task 4: Parser recognizes `<Theme>` / `<Color>`

**Files:**
- Create: `Runtime/Core/IR/ThemeBlock.cs`, `Runtime/Core/IR/ColorEntry.cs`
- Modify: `Runtime/Core/IR/UIDocument.cs` (add `Themes` field)
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs` (top-level dispatch + per-attr validation)
- Create: `Tests/EditMode/Parser/ThemeParseTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Parser/ThemeParseTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    public class ThemeParseTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>";
        private const string Footer = "</PromptUGUI>";

        [Test]
        public void Single_Theme_Single_Color_Parses()
        {
            var doc = UIDocumentParser.Parse(Header +
                "<Theme name='light'><Color name='primary' value='#ff8800'/></Theme>" + Footer);
            Assert.AreEqual(1, doc.Themes.Count);
            Assert.AreEqual("light", doc.Themes[0].Name);
            Assert.IsNull(doc.Themes[0].BaseName);
            Assert.AreEqual(1, doc.Themes[0].Colors.Count);
            Assert.AreEqual("primary", doc.Themes[0].Colors[0].Name);
            Assert.AreEqual("#ff8800", doc.Themes[0].Colors[0].Value);
        }

        [Test]
        public void Theme_With_Base()
        {
            var doc = UIDocumentParser.Parse(Header +
                "<Theme name='light'><Color name='p' value='#ff0000'/></Theme>" +
                "<Theme name='dark' base='light'><Color name='p' value='#000000'/></Theme>" + Footer);
            Assert.AreEqual("light", doc.Themes[1].BaseName);
        }

        [Test]
        public void Theme_Missing_Name_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(
                Header + "<Theme><Color name='p' value='#ff0000'/></Theme>" + Footer));
            StringAssert.Contains("name", ex.Message);
        }

        [Test]
        public void Color_Missing_Name_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(
                Header + "<Theme name='l'><Color value='#ff0000'/></Theme>" + Footer));
            StringAssert.Contains("name", ex.Message);
        }

        [Test]
        public void Color_Missing_Value_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(
                Header + "<Theme name='l'><Color name='p'/></Theme>" + Footer));
            StringAssert.Contains("value", ex.Message);
        }

        [Test]
        public void Color_Invalid_Value_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(
                Header + "<Theme name='l'><Color name='p' value='#xyz'/></Theme>" + Footer));
            StringAssert.Contains("invalid color literal", ex.Message);
        }

        [Test]
        public void Token_Name_NonKebab_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(
                Header + "<Theme name='l'><Color name='Primary' value='#ff0000'/></Theme>" + Footer));
            StringAssert.Contains("kebab-case", ex.Message);
        }

        [Test]
        public void Duplicate_Color_Name_Within_Theme_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(Header +
                "<Theme name='l'><Color name='p' value='#ff0000'/>" +
                "<Color name='p' value='#000000'/></Theme>" + Footer));
            StringAssert.Contains("twice", ex.Message);
        }

        [Test]
        public void Duplicate_Theme_Name_Within_Doc_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(Header +
                "<Theme name='light'/><Theme name='light'/>" + Footer));
            StringAssert.Contains("light", ex.Message);
        }

        [Test]
        public void Non_Color_Child_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(Header +
                "<Theme name='l'><Frame/></Theme>" + Footer));
            StringAssert.Contains("Frame", ex.Message);
        }
    }
}
```

- [ ] **Step 2: Run test, verify fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile error — `doc.Themes` not found / `ThemeBlock` undefined.

- [ ] **Step 3: Implement**

Create `Runtime/Core/IR/ColorEntry.cs`:

```csharp
namespace PromptUGUI.IR
{
    /// <summary>Single &lt;Color name=... value=.../&gt; entry within a &lt;Theme&gt; block.</summary>
    public sealed class ColorEntry
    {
        public string Name;
        public string Value;
    }
}
```

Create `Runtime/Core/IR/ThemeBlock.cs`:

```csharp
using System.Collections.Generic;

namespace PromptUGUI.IR
{
    /// <summary>Top-level &lt;Theme name=... base=...&gt; block from a UIDocument.</summary>
    public sealed class ThemeBlock
    {
        public string Name;
        public string BaseName;     // null if no base=
        public List<ColorEntry> Colors = new();
    }
}
```

Modify `Runtime/Core/IR/UIDocument.cs` — add field next to existing `Screens` / `Templates`:

```csharp
public List<ThemeBlock> Themes = new();
```

Modify `Runtime/Core/Parser/UIDocumentParser.cs` — extend top-level element dispatch (find the switch over `<Screen>` / `<Templates>` / `<Import>`):

```csharp
case "Theme":
    doc.Themes.Add(ParseTheme(child));
    break;
```

Add helper near other `Parse*` methods:

```csharp
private static System.Text.RegularExpressions.Regex KebabRx =
    new("^[a-z0-9]+(-[a-z0-9]+)*$", System.Text.RegularExpressions.RegexOptions.Compiled);

private static ThemeBlock ParseTheme(System.Xml.Linq.XElement el)
{
    var name = (string)el.Attribute("name");
    if (string.IsNullOrEmpty(name))
        throw new ParseException("<Theme>: missing required attribute 'name'");
    var block = new ThemeBlock { Name = name, BaseName = (string)el.Attribute("base") };
    var seen = new System.Collections.Generic.HashSet<string>();
    foreach (var child in el.Elements())
    {
        if (child.Name.LocalName != "Color")
            throw new ParseException(
                $"<Theme name=\"{name}\">: unexpected child <{child.Name.LocalName}> " +
                $"(only <Color> allowed)");
        var cn = (string)child.Attribute("name");
        var cv = (string)child.Attribute("value");
        if (string.IsNullOrEmpty(cn))
            throw new ParseException($"<Color value=\"{cv}\">: missing required attribute 'name'");
        if (cv == null)
            throw new ParseException($"<Color name=\"{cn}\">: missing required attribute 'value'");
        if (!KebabRx.IsMatch(cn))
            throw new ParseException(
                $"<Color name=\"{cn}\">: token name must be kebab-case [a-z0-9-]");
        if (!UnityEngine.ColorUtility.TryParseHtmlString(cv, out _))
            throw new ParseException(
                $"<Color name=\"{cn}\" value=\"{cv}\">: invalid color literal");
        if (!seen.Add(cn))
            throw new ParseException(
                $"<Theme name=\"{name}\"> declares '{cn}' twice");
        block.Colors.Add(new ColorEntry { Name = cn, Value = cv });
    }
    return block;
}
```

In the top-level switch, also enforce no duplicate theme names within the document:

```csharp
// After dispatching to ParseTheme:
if (doc.Themes.Count > 0)
{
    var lastName = doc.Themes[doc.Themes.Count - 1].Name;
    for (int i = 0; i < doc.Themes.Count - 1; i++)
        if (doc.Themes[i].Name == lastName)
            throw new ParseException(
                $"duplicate <Theme name=\"{lastName}\"> within document");
}
```

- [ ] **Step 4: Run test, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ThemeParseTests")
```

Expected: PASS. If any existing parser test broke (e.g., tests that walked all top-level children), fix the cause.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/IR/ColorEntry.cs Runtime/Core/IR/ThemeBlock.cs Runtime/Core/IR/UIDocument.cs Runtime/Core/Parser/UIDocumentParser.cs Tests/EditMode/Parser/ThemeParseTests.cs
git commit -m "feat: parser recognizes <Theme>/<Color> top-level blocks"
```

---

### Task 5: `DocumentLoader` registers themes into `ThemeStore`

**Files:**
- Modify: `Runtime/Application/DocumentLoader.cs` (after parse-and-merge, route `Themes` to `ThemeStore`; cross-doc conflict; auto-set)
- Create: `Tests/EditMode/Application/ThemeLoadingTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Application/ThemeLoadingTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Application
{
    public class ThemeLoadingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void Resolver(Dictionary<string, string> files)
        {
            UI.UseSourceResolver(src =>
                AwaitableHelpersTestProxy.Completed(files[src]));
        }

        [Test]
        public void LoadCommonLibrary_Registers_Themes_And_AutoSets_When_Single()
        {
            Resolver(new() {
                ["themes/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Theme name='light'><Color name='primary' value='#ff8800'/></Theme>
                </PromptUGUI>"
            });
            UI.LoadCommonLibraryAsync("themes/main").GetAwaiter().GetResult();
            CollectionAssert.AreEquivalent(new[] { "light" }, UI.Theme.Available);
            Assert.AreEqual("light", UI.Theme.Current);  // auto-set single
        }

        [Test]
        public void LoadCommonLibrary_Two_Themes_Does_Not_AutoSet()
        {
            Resolver(new() {
                ["themes/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Theme name='light'><Color name='primary' value='#ff8800'/></Theme>
                    <Theme name='dark' base='light'><Color name='primary' value='#cc6600'/></Theme>
                </PromptUGUI>"
            });
            UI.LoadCommonLibraryAsync("themes/main").GetAwaiter().GetResult();
            Assert.AreEqual(2, UI.Theme.Available.Count);
            Assert.IsNull(UI.Theme.Current);
        }

        [Test]
        public void CrossDoc_Duplicate_Theme_Throws()
        {
            Resolver(new() {
                ["themes/a"] = "<?xml version='1.0'?><PromptUGUI version='1'><Theme name='light'/></PromptUGUI>",
                ["themes/b"] = "<?xml version='1.0'?><PromptUGUI version='1'><Theme name='light'/></PromptUGUI>",
            });
            UI.LoadCommonLibraryAsync("themes/a").GetAwaiter().GetResult();
            var ex = Assert.Throws<ParseException>(() =>
                UI.LoadCommonLibraryAsync("themes/b").GetAwaiter().GetResult());
            StringAssert.Contains("themes/a", ex.Message);
            StringAssert.Contains("themes/b", ex.Message);
        }

        [Test]
        public void Missing_Base_Throws_At_Load()
        {
            Resolver(new() {
                ["themes/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Theme name='dark' base='ghost'/>
                </PromptUGUI>"
            });
            var ex = Assert.Throws<ParseException>(() =>
                UI.LoadCommonLibraryAsync("themes/main").GetAwaiter().GetResult());
            StringAssert.Contains("ghost", ex.Message);
        }
    }

    // Test-only shim because Tests.EditMode can't directly reach the internal
    // helper (or to bypass the API). Replace with the internal helper if visible.
    internal static class AwaitableHelpersTestProxy
    {
        public static UnityEngine.Awaitable<string> Completed(string value)
            => AwaitableHelpers.Completed(value);
    }
}
```

(If `AwaitableHelpers.Completed` is internal and already visible to `PromptUGUI.Tests.EditMode` via `InternalsVisibleTo`, you can drop the proxy and call it directly.)

- [ ] **Step 2: Run test, verify fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: tests fail — themes parsed but not registered into `ThemeStore`.

- [ ] **Step 3: Implement**

In `Runtime/Application/DocumentLoader.cs`, after `LoadAndMergeAsync` finishes assembling the final document (where it currently iterates Templates), add:

```csharp
// Register themes from the loaded document into ThemeStore.
// Cross-document conflict detection happens inside ThemeStore.Register.
foreach (var theme in finalDoc.Themes)
{
    var colors = new Dictionary<string, UnityEngine.Color>(theme.Colors.Count);
    foreach (var ce in theme.Colors)
    {
        UnityEngine.ColorUtility.TryParseHtmlString(ce.Value, out var c);
        colors[ce.Name] = c;
    }
    ThemeStore.Instance.Register(theme.Name, theme.BaseName, colors, src);
}
ThemeStore.Instance.ResolveBases();
UI.Theme.AutoSetIfSingleAvailable();
```

(Adjust variable names — `finalDoc` may be named differently; check current `DocumentLoader`.)

- [ ] **Step 4: Run test, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ThemeLoadingTests")
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/DocumentLoader.cs Tests/EditMode/Application/ThemeLoadingTests.cs
git commit -m "feat: DocumentLoader registers <Theme> blocks into ThemeStore"
```

---

### Task 6: Image / Text / Btn setters → `UI.Theme.Resolve`

**Files:**
- Modify: `Runtime/Controls/Image.cs:35-50`, `Text.cs:88-96`, `Btn.cs:100-108`
- Create: `Tests/EditMode/Controls/ColorTokenIntegrationTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Controls/ColorTokenIntegrationTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Parser;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ColorTokenIntegrationTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void SeedLight(string primaryHex)
        {
            var d = new System.Collections.Generic.Dictionary<string, Color>();
            ColorUtility.TryParseHtmlString(primaryHex, out var c);
            d["primary"] = c;
            ThemeStore.Instance.Register("light", null, d, "test");
            ThemeStore.Instance.ResolveBases();
            UI.Theme.Set("light");
        }

        private static Screen Open(string innerXml)
        {
            UI.LoadDocument("t",
                $"<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'>{innerXml}</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        [Test]
        public void Image_Hex_Literal_Still_Works()
        {
            var s = Open("<Image id='x' color='#00ff00'/>");
            var img = s.Get<Image>("x").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(new Color32(0, 0xff, 0, 0xff), (Color32)img.color);
        }

        [Test]
        public void Image_Token_Resolves()
        {
            SeedLight("#ff8800");
            var s = Open("<Image id='x' color='primary'/>");
            var img = s.Get<Image>("x").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(new Color32(0xff, 0x88, 0, 0xff), (Color32)img.color);
        }

        [Test]
        public void Image_Unknown_Token_Throws_With_Node_Context()
        {
            SeedLight("#ff8800");
            var ex = Assert.Throws<ParseException>(() => Open("<Image id='avatar' color='primaru'/>"));
            StringAssert.Contains("Image", ex.Message);
            StringAssert.Contains("avatar", ex.Message);
            StringAssert.Contains("primaru", ex.Message);
        }

        [Test]
        public void Text_Token_Resolves()
        {
            SeedLight("#222222");
            // Use 'primary' so Text sees a token hit even though semantically odd.
            var s = Open("<Text id='t' color='primary' text='Hi'/>");
            var tmp = s.Get<Text>("t").GameObject.GetComponent<TMPro.TMP_Text>();
            Assert.AreEqual(new Color32(0x22, 0x22, 0x22, 0xff), (Color32)tmp.color);
        }

        [Test]
        public void Btn_Token_Resolves()
        {
            SeedLight("#ff8800");
            var s = Open("<Btn id='b' color='primary' label='Buy'/>");
            // Btn._bg is added on Btn.GameObject itself, not a child. Matches BtnTests.cs:26.
            var bg = s.Get<Btn>("b").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(new Color32(0xff, 0x88, 0, 0xff), (Color32)bg.color);
        }

        [Test]
        public void Variant_Bad_Color_Throws_On_Switch()
        {
            SeedLight("#ff8800");
            // base color valid; variant override invalid → throws on Variants.Set
            var s = Open(
                "<Image id='x' color='primary'>" +
                "  <Variant when='dark' color='#bagval'/>" +
                "</Image>");
            // initial: ok
            var img = s.Get<Image>("x").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(new Color32(0xff, 0x88, 0, 0xff), (Color32)img.color);
            // switching to bad variant: throws (loud-fail per spec §6)
            Assert.Throws<ParseException>(() => UI.Variants.Set("dark", true));
        }
    }
}
```

- [ ] **Step 2: Run test, verify fail (current setters silently no-op on bad value)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ColorTokenIntegrationTests")
```

Expected: hex-literal test passes (unchanged behavior); token tests fail (setter doesn't know about Theme); unknown-token test fails (current code is silent no-op, not a throw); variant test fails.

- [ ] **Step 3: Implement**

`Runtime/Controls/Image.cs:41-50` — replace existing `Color` property:

```csharp
[UIAttr(IsColor = true), Preserve]
public string Color
{
    set => _img.color = UI.Theme.Resolve(value);
}
```

`Runtime/Controls/Text.cs:88-96` — replace:

```csharp
[UIAttr(IsColor = true), Preserve]
public string Color
{
    set => _tmp.color = UI.Theme.Resolve(value);
}
```

`Runtime/Controls/Btn.cs:100-108` — replace:

```csharp
[UIAttr(IsColor = true), Preserve]
public string Color
{
    set => _bg.color = UI.Theme.Resolve(value);
}
```

(Remove all three `if (string.IsNullOrEmpty(value)) return;` / `if (ColorUtility.TryParseHtmlString(...)) ...` guards.)

- [ ] **Step 4: Run test, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ColorTokenIntegrationTests")
```

Expected: PASS. Also run wider regression to catch any existing `.ui.xml` fixture with invalid color literal:

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

If any unrelated test now fails because of a malformed `color=` literal in a fixture XML, fix the fixture (this is the §6.3 documented breaking change).

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Image.cs Runtime/Controls/Text.cs Runtime/Controls/Btn.cs Tests/EditMode/Controls/ColorTokenIntegrationTests.cs
git commit -m "feat: Image/Text/Btn color attr routes through UI.Theme.Resolve

setter 改调 UI.Theme.Resolve(value)，token / 字面值统一通道。
顺带修 \"解析失败静默 no-op\" bug —— 现在解析失败直接抛 ParseException
带节点上下文。"
```

---

### Task 7: `AnimationSpec.SetCharColor` via `UI.Theme.Resolve`

**Files:**
- Modify: `Runtime/Controls/Internal/AnimationSpec.cs:60-65` (`SetCharColor` / `ParseColorFromTo`)
- Create: `Tests/EditMode/Controls/AnimationCharColorTokenTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Controls/AnimationCharColorTokenTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class AnimationCharColorTokenTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void Seed(string token, string hex)
        {
            var d = new Dictionary<string, Color>();
            ColorUtility.TryParseHtmlString(hex, out var c);
            d[token] = c;
            ThemeStore.Instance.Register("t", null, d, "test");
            ThemeStore.Instance.ResolveBases();
            UI.Theme.Set("t");
        }

        [Test]
        public void Token_To_Token_Resolves()
        {
            Seed("primary", "#ff8800");
            var spec = new AnimationSpec();
            spec.SetCharColor("primary:primary");
            Assert.AreEqual(new Color32(0xff, 0x88, 0, 0xff), (Color32)spec.CharColorFrom);
        }

        [Test]
        public void Token_To_Literal_Mixes()
        {
            Seed("primary", "#ff8800");
            var spec = new AnimationSpec();
            spec.SetCharColor("primary:#00ff00");
            Assert.AreEqual(new Color32(0xff, 0x88, 0, 0xff), (Color32)spec.CharColorFrom);
            Assert.AreEqual(new Color32(0, 0xff, 0, 0xff), (Color32)spec.CharColorTo);
        }

        [Test]
        public void Bad_Token_Throws()
        {
            Seed("primary", "#ff8800");
            var spec = new AnimationSpec();
            Assert.Throws<System.Exception>(() => spec.SetCharColor("primaru:#000"));
        }

        [Test]
        public void Wrong_Shape_Throws()
        {
            var spec = new AnimationSpec();
            Assert.Throws<System.Exception>(() => spec.SetCharColor("#ff0000"));
        }
    }
}
```

- [ ] **Step 2: Run test, verify fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="AnimationCharColorTokenTests")
```

Expected: token tests fail (current `ParseColorFromTo` uses raw `TryParseHtmlString` per part).

- [ ] **Step 3: Implement**

In `Runtime/Controls/Internal/AnimationSpec.cs`, replace `ParseColorFromTo` (find via `grep -n ParseColorFromTo`):

```csharp
private static void ParseColorFromTo(string v, out UnityEngine.Color from, out UnityEngine.Color to)
{
    var parts = v.Split(':');
    if (parts.Length != 2)
        throw new System.Exception($"char-color=\"{v}\": expected 'from:to'");
    from = PromptUGUI.Application.UI.Theme.Resolve(parts[0]);
    to   = PromptUGUI.Application.UI.Theme.Resolve(parts[1]);
}
```

- [ ] **Step 4: Run test, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="AnimationCharColorTokenTests")
```

Expected: PASS. Also run existing `AnimationSpecTests` to ensure no regression:

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="AnimationSpecTests")
```

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Internal/AnimationSpec.cs Tests/EditMode/Controls/AnimationCharColorTokenTests.cs
git commit -m "feat: Animation char-color tokens via UI.Theme.Resolve"
```

---

### Task 8: `Screen` subscribes `UI.Theme.Changed` → `ReSolve`

**Files:**
- Modify: `Runtime/Application/Screen.cs` (around `:137`, beside existing `Variants.Changed` subscription)
- Create: `Tests/EditMode/Application/ThemeSwitchTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Application/ThemeSwitchTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Application
{
    public class ThemeSwitchTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void RegisterTwoThemes()
        {
            var light = new Dictionary<string, Color>();
            ColorUtility.TryParseHtmlString("#ff8800", out var lp); light["primary"] = lp;
            ThemeStore.Instance.Register("light", null, light, "test");

            var dark = new Dictionary<string, Color>();
            ColorUtility.TryParseHtmlString("#cc6600", out var dp); dark["primary"] = dp;
            ThemeStore.Instance.Register("dark", null, dark, "test");
            ThemeStore.Instance.ResolveBases();
        }

        [Test]
        public void Switch_Theme_ReSolves_Open_Screen_Colors()
        {
            RegisterTwoThemes();
            UI.Theme.Set("light");
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                "<Screen name='S'><Image id='x' color='primary'/></Screen></PromptUGUI>");
            var s = UI.Open("S");
            var img = s.Get<Image>("x").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(new Color32(0xff, 0x88, 0, 0xff), (Color32)img.color);

            UI.Theme.Set("dark");
            Assert.AreEqual(new Color32(0xcc, 0x66, 0, 0xff), (Color32)img.color);

            UI.Theme.Set("light");
            Assert.AreEqual(new Color32(0xff, 0x88, 0, 0xff), (Color32)img.color);
        }

        [Test]
        public void Closed_Screen_Does_Not_ReSolve_On_Theme_Change()
        {
            RegisterTwoThemes();
            UI.Theme.Set("light");
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                "<Screen name='S'><Image id='x' color='primary'/></Screen></PromptUGUI>");
            var s = UI.Open("S");
            s.Close();
            // Should not throw / leak — closed Screen unsubscribed from Theme.Changed.
            Assert.DoesNotThrow(() => UI.Theme.Set("dark"));
        }
    }
}
```

- [ ] **Step 2: Run test, verify fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ThemeSwitchTests")
```

Expected: first test fails — switching theme doesn't update color (Screen not subscribed).

- [ ] **Step 3: Implement**

In `Runtime/Application/Screen.cs`, near line 137 (where `_variantSub = Variants.Changed.Subscribe(_ => ReSolve());`), add:

```csharp
// Re-solve on theme switch (parallel to Variants.Changed handling).
System.Action<string> _themeHandler = _ => ReSolve();
UI.Theme.Changed += _themeHandler;
_disposables.Add(new System.Action(() => UI.Theme.Changed -= _themeHandler));
```

(If `_disposables` doesn't exist, follow whatever pattern the file uses — store the handler as a field and unsubscribe in `Close()`. Match the existing style — check `Variants.Changed` and `Locale.Changed` unsub paths nearby.)

- [ ] **Step 4: Run test, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ThemeSwitchTests")
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/Screen.cs Tests/EditMode/Application/ThemeSwitchTests.cs
git commit -m "feat: Screen subscribes UI.Theme.Changed → ReSolve"
```

---

### Task 9: Hot reload integration

**Files:**
- Modify: `Editor/UIAssetPostprocessor.cs` and/or `Runtime/Application/UI.cs` `HotReload` path (find where `ReloadCommonLibraryAsync` lives; ensure theme blocks re-register via `ThemeStore.ReplaceFromSrc` and `Theme.Changed` fires when Current's tokens changed)
- Create: `Tests/EditMode/Editor/ThemeHotReloadTests.cs` (asmdef: `PromptUGUI.Tests.EditMode` — uses internal `ThemeStore.ReplaceFromSrc`)

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Editor/ThemeHotReloadTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Editor
{
    public class ThemeHotReloadTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void ReplaceFromSrc_Updates_Token_Value()
        {
            var v1 = new Dictionary<string, Color>();
            ColorUtility.TryParseHtmlString("#ff8800", out var c1); v1["primary"] = c1;
            ThemeStore.Instance.Register("light", null, v1, "themes/main");
            ThemeStore.Instance.ResolveBases();
            UI.Theme.Set("light");

            // Simulate hot reload: same src, new value for primary.
            var v2 = new Dictionary<string, Color>();
            ColorUtility.TryParseHtmlString("#00ff00", out var c2); v2["primary"] = c2;

            string fired = null;
            UI.Theme.Changed += n => fired = n;

            ThemeStore.Instance.ReplaceFromSrc("themes/main",
                new List<(string, string, IReadOnlyDictionary<string, Color>)>
                {
                    ("light", null, v2)
                });
            // Wrapper helper that calls ReplaceFromSrc *and* fires Theme.Changed
            // for Current — implemented in Task 9 Step 3.
            UI.Theme.NotifyAfterReplaceForTests("light");

            Assert.AreEqual("light", fired);
            Assert.AreEqual(new Color32(0, 0xff, 0, 0xff), (Color32)UI.Theme.Resolve("primary"));
        }
    }
}
```

- [ ] **Step 2: Run test, verify fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile error — `NotifyAfterReplaceForTests` missing.

- [ ] **Step 3: Implement**

In `Runtime/Application/UI.cs` (`Theme` class), add:

```csharp
/// <summary>Fire Theme.Changed for the current theme. Used by hot reload
/// and by tests that simulate hot reload.</summary>
internal static void NotifyAfterReplaceForTests(string themeNameIfStillCurrent)
{
    if (Current != null && Current == themeNameIfStillCurrent)
        Changed?.Invoke(Current);
}
```

Then find the production hot-reload entry point (likely `UI.HotReload.NotifyAssetChanged` → `ReloadCommonLibraryAsync` in `UI.cs`). In the post-replace step, after `ThemeStore.Instance.ReplaceFromSrc(...)`, call:

```csharp
if (Theme.Current != null)
    Theme.NotifyAfterReplaceForTests(Theme.Current);
```

(The "ForTests" name is misleading once production also uses it; rename to `RaiseChangedIfCurrent(string)` if cleanup is desired.)

If `DocumentLoader.LoadAndMerge` is the actual function called on reload, refactor it to detect "is this a reload? if so, route themes through `ReplaceFromSrc` instead of `Register`". One approach: add an `isReload` param plumbed through `ReloadCommonLibraryAsync`.

- [ ] **Step 4: Run test, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ThemeHotReloadTests")
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/UI.cs Runtime/Application/DocumentLoader.cs Editor/UIAssetPostprocessor.cs Tests/EditMode/Editor/ThemeHotReloadTests.cs
git commit -m "feat: theme hot reload re-registers + fires Theme.Changed"
```

(Stage only files actually modified — `git status` first.)

---

### Task 10: `XsdGenerator` output for `<Theme>` / `<Color>`

**Files:**
- Modify: `Editor/XsdGenerator.cs`
- Modify: `Tests/EditMode/Editor/XsdGeneratorTests.cs` (substring asserts only, per CLAUDE.md convention)

- [ ] **Step 1: Write the failing test**

Append to `Tests/EditMode/Editor/XsdGeneratorTests.cs`:

```csharp
[Test]
public void Generated_Xsd_Contains_Theme_Element()
{
    var xsd = XsdGenerator.Generate();
    StringAssert.Contains("name=\"Theme\"", xsd);
    StringAssert.Contains("name=\"Color\"", xsd);
    StringAssert.Contains("name=\"base\"", xsd);  // optional base attribute on Theme
}
```

- [ ] **Step 2: Run test, verify fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="XsdGeneratorTests")
```

(Assembly may be `PromptUGUI.Tests.EditorOnly` since XsdGenerator is in `Editor/`. Check the existing test's asmdef.)

Expected: new test fails — Theme/Color not yet in XSD.

- [ ] **Step 3: Implement**

In `Editor/XsdGenerator.cs`, find where top-level element definitions are emitted (next to `Screen`, `Templates`, `Import`). Add:

```csharp
sb.AppendLine(@"  <xs:element name=""Theme"">
    <xs:complexType>
      <xs:sequence>
        <xs:element name=""Color"" minOccurs=""0"" maxOccurs=""unbounded"">
          <xs:complexType>
            <xs:attribute name=""name"" type=""xs:string"" use=""required""/>
            <xs:attribute name=""value"" type=""xs:string"" use=""required""/>
          </xs:complexType>
        </xs:element>
      </xs:sequence>
      <xs:attribute name=""name"" type=""xs:string"" use=""required""/>
      <xs:attribute name=""base"" type=""xs:string""/>
    </xs:complexType>
  </xs:element>");
```

And ensure the top-level `<xs:element ref="Theme">` reference is in the document's top-level choice group.

- [ ] **Step 4: Run test, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="XsdGeneratorTests")
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Editor/XsdGenerator.cs Tests/EditMode/Editor/XsdGeneratorTests.cs
git commit -m "feat: XSD generator emits <Theme>/<Color> schema"
```

---

### Task 11: `ColorLiteralRules` lint (static `color="#..."` validity)

**Files:**
- Create: `Runtime/Core/Lint/ColorLiteralRules.cs`
- Modify: `Runtime/Core/Lint/IRWalker.cs` (dispatch to new rule for any element with `color=`)
- Create: `Tests/EditMode/Lint/ColorLiteralRulesTests.cs`

Rule (per spec §6.3 migration aid): if a `color=` attr value starts with `#`, it must parse via `ColorUtility.TryParseHtmlString`. Bare words are NOT flagged (could be tokens defined elsewhere).

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Lint/ColorLiteralRulesTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class ColorLiteralRulesTests
    {
        private static System.Collections.Generic.List<LintIssue> Lint(string xml)
        {
            var doc = UIDocumentParser.Parse(xml);
            var issues = new System.Collections.Generic.List<LintIssue>();
            IRWalker.Walk(doc, issues.Add);
            return issues;
        }

        private const string Hdr = "<?xml version='1.0'?><PromptUGUI version='1'><Screen name='S'>";
        private const string Ftr = "</Screen></PromptUGUI>";

        [Test]
        public void Valid_Hex_No_Issue()
        {
            var iss = Lint(Hdr + "<Image color='#ff8800'/>" + Ftr);
            Assert.IsEmpty(iss);
        }

        [Test]
        public void Token_Word_No_Issue()
        {
            // Bare word: could be a token defined elsewhere. Lint does not flag.
            var iss = Lint(Hdr + "<Image color='primary'/>" + Ftr);
            Assert.IsEmpty(iss);
        }

        [Test]
        public void Malformed_Hex_Is_Flagged()
        {
            var iss = Lint(Hdr + "<Image color='#ff800'/>" + Ftr);  // 5 digits, invalid
            Assert.AreEqual(1, iss.Count);
            StringAssert.Contains("color", iss[0].Message);
        }

        [Test]
        public void Empty_Value_Not_Crashing()
        {
            // Empty color attr at lint level → runtime ParseException; lint silent.
            var iss = Lint(Hdr + "<Image color=''/>" + Ftr);
            Assert.IsEmpty(iss);
        }
    }
}
```

(`LintIssue` is the existing issue type used by other lint rules — check `Runtime/Core/Lint/MaskAttributeRules.cs` or similar for the actual signature, and adapt.)

- [ ] **Step 2: Run test, verify fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile error — `ColorLiteralRules` doesn't exist.

- [ ] **Step 3: Implement**

Create `Runtime/Core/Lint/ColorLiteralRules.cs`:

```csharp
namespace PromptUGUI.Lint
{
    /// <summary>
    /// Static check on <c>color="..."</c> attribute values. Only flags hex
    /// literals that fail to parse (values starting with '#'). Bare words
    /// are deliberately not flagged — they may be tokens registered in a
    /// theme file not visible to this lint pass.
    /// </summary>
    public static class ColorLiteralRules
    {
        public static void Check(PromptUGUI.IR.ElementNode node,
                                 System.Action<LintIssue> report)
        {
            if (!node.Attributes.TryGetValue("color", out var v)) return;
            if (string.IsNullOrEmpty(v)) return;
            if (v[0] != '#') return;     // tokens not checked statically
            if (UnityEngine.ColorUtility.TryParseHtmlString(v, out _)) return;
            report(new LintIssue
            {
                Code = "color-literal-invalid",
                Message = $"<{node.Tag}>: invalid color literal value=\"{v}\"",
                NodePath = node.Id ?? node.Tag,
            });
        }
    }
}
```

In `Runtime/Core/Lint/IRWalker.cs`, find the per-node rule dispatch (next to `MaskAttributeRules.Check(...)` or similar) and add:

```csharp
ColorLiteralRules.Check(node, report);
```

- [ ] **Step 4: Run test, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ColorLiteralRulesTests")
```

Expected: PASS. Also run lint CLI smoke against the repo:

```
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/
```

Expected: no surprising new errors (if any, those are pre-existing bad hex literals worth fixing — §6.3 migration).

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Lint/ColorLiteralRules.cs Runtime/Core/Lint/IRWalker.cs Tests/EditMode/Lint/ColorLiteralRulesTests.cs
git commit -m "feat: lint rule color-literal-invalid (static hex check)"
```

---

### Task 12: `authoring-promptugui-xml` SKILL update

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`

No automated tests; verify by reading. Per CLAUDE.md "any functional change… must be reflected in the relevant skill(s) in the same PR (in english)."

- [ ] **Step 1: Edit**

Open `.claude/skills/authoring-promptugui-xml/SKILL.md`. Add a new top-level section "Color Tokens" after the section on `<Templates>` / `<Import>`:

```markdown
## Color Tokens

Define named colors in `<Theme>` blocks; reference them by name in any color attribute.

### Authoring

```xml
<UIDocument>
  <Theme name="light">
    <Color name="primary"   value="#ff8800"/>
    <Color name="secondary" value="#0080ff"/>
    <Color name="label-fg"  value="#222222"/>
  </Theme>
  <Theme name="dark" base="light">
    <Color name="primary"  value="#cc6600"/>
    <Color name="label-fg" value="#e6e6e6"/>
    <!-- secondary inherits from base="light" -->
  </Theme>
</UIDocument>
```

- `<Theme>` MUST have `name`. Optional `base="other-theme"` inherits missing tokens.
- `<Color>` MUST have `name` (kebab-case, `[a-z0-9-]`) and `value` (hex / CSS-named, anything `ColorUtility.TryParseHtmlString` accepts).
- Theme XML loads via `UI.LoadCommonLibraryAsync(...)` (boot) or via `<Import src="themes/main"/>` from any screen's `.ui.xml`.

### Reference

```xml
<Image color="primary"/>
<Text  color="label-fg" text="Hello"/>
```

Resolution: current theme token → walk `base` chain → fall back to literal `ColorUtility.TryParseHtmlString` → if all fail, parse error.

### Shadow rule

If you register a token named `red`, then `color="red"` resolves to that token (NOT the CSS named color `red`). Token always wins over literal when both could parse.

### Error codes

- `<Theme>: missing required attribute 'name'`
- `<Color name="X" value="Y">: invalid color literal`
- `<Color name="X">: token name must be kebab-case [a-z0-9-]`
- `<Theme name="X"> declares 'Y' twice`
- `duplicate <Theme name="X"> in 'src1' and 'src2'`
- `<Theme name="X" base="Y">: base theme 'Y' not found`
- `<Theme> base cycle starting at 'X': ...`
- `<Image id='X'> attribute color="Y": unknown color token "Y" (no entry in theme 'Z', not a valid hex/named literal)` — at runtime when value can't resolve via token or literal
- (Lint) `color-literal-invalid` — static check for malformed `#hex` literals
```

Also update the "Built-in tags" table to add `<Theme>` and `<Color>` rows.

Also update the `color` attribute description (in whatever common-attribute table exists) to: "Hex (`#rgb`/`#rrggbb`/`#rrggbbaa`) / CSS named color / theme token name. Resolution: token → literal."

- [ ] **Step 2: Verify by inspection** (no automated test)

Read the edited file and confirm sections are coherent with the rest of the SKILL document.

- [ ] **Step 3: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "docs(skill): add color token authoring guide"
```

---

### Task 13: `scripting-promptugui-csharp` SKILL update

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`

- [ ] **Step 1: Edit**

Add a new section "Theme switching" after `UI.Locale.*`:

```markdown
## Theme switching

```csharp
// Load themes (one-time at boot; themes piggyback on the commons library).
await UI.LoadCommonLibraryAsync("themes/main");

// Inspect / switch.
UI.Theme.Current;       // string?, the active theme name
UI.Theme.Available;     // IReadOnlyCollection<string>
UI.Theme.Set("dark");   // throws ArgumentException if "dark" not registered

// Subscribe to switches (Screens do this automatically and ReSolve).
UI.Theme.Changed += newName => Debug.Log($"theme switched to {newName}");

// Programmatic lookup (returns null when no Current theme or token absent).
Color? c = UI.Theme.Lookup("primary");

// Full resolution chain (token → base → literal → throw).
Color resolved = UI.Theme.Resolve("primary");          // theme token hit
Color resolved2 = UI.Theme.Resolve("#ff8800");         // hex literal
// UI.Theme.Resolve("primaru")  // throws (token miss + literal miss)
```

Single-theme projects: if `LoadCommonLibraryAsync` registers exactly one theme and `UI.Theme.Current` is null, the loader auto-selects it. Multi-theme projects must call `UI.Theme.Set` explicitly.
```

Add a row to the `UI.*` cheatsheet table for `UI.Theme.Set / Resolve / Lookup / Changed`.

Add a section "Custom controls with color attributes":

```markdown
### Color attributes on custom controls

Mark color-bearing `[UIAttr]` properties with `IsColor = true`. The setter receives a `string` and should call `UI.Theme.Resolve` to convert:

```csharp
[UIAttr(IsColor = true), Preserve]
public string AccentColor
{
    set => _accent.color = UI.Theme.Resolve(value);
}
```

`IsColor = true` enables static lint discovery (the lint pipeline checks hex literals). Runtime resolution is in the setter itself — there is no separate applier branch (same pattern as `IsSprite` + `UI.ResolveSprite`).
```

- [ ] **Step 2: Verify by inspection**

- [ ] **Step 3: Commit**

```bash
git add .claude/skills/scripting-promptugui-csharp/SKILL.md
git commit -m "docs(skill): add UI.Theme.* + IsColor authoring guide"
```

---

### Task 14: Master spec touch

**Files:**
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`

- [ ] **Step 1: Edit**

In §5 (Built-in elements table), add a row:

```
| `<Theme>` / `<Color>` | Color token block | n/a — top-level |
```

In §6 (Attributes), under the description of `color`, add: "Resolves via `UI.Theme.Resolve(value)`: theme token (current → base chain) → literal (`ColorUtility.TryParseHtmlString`) → parse error. See `2026-05-28-color-tokens-design.md` for the full design."

In §8 (Variants / ReSolve), add: "`UI.Theme.Set(name)` also triggers `Screen.ReSolve` on all open Screens, parallel to `UI.Locale.Set` and `UI.Variants.Set`."

- [ ] **Step 2: Verify by inspection**

- [ ] **Step 3: Commit**

```bash
git add docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md
git commit -m "docs: master spec — link color token design"
```

---

## Verification After All Tasks

- [ ] **Full EditMode suite**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
```

Expected: zero compile errors, zero test failures.

- [ ] **PlayMode regression**

```
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

Expected: zero failures. (No new PlayMode tests in this plan; existing ones should be unaffected.)

- [ ] **Lint CLI smoke**

```
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/
```

Expected: no new errors. If any pre-existing `.ui.xml` flagged by the new `color-literal-invalid` rule surfaces, fix the fixture (the §6.3 documented breaking change) — do not silence the rule.

- [ ] **`dotnet format` clean**

```
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format style       PromptUGUI.Lint.slnx
dotnet format analyzers   PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: no changes / clean exit.

- [ ] **Open PR**

```bash
gh pr create --title "feat: color tokens (<Theme>/<Color> + UI.Theme.*)" --body "$(cat <<'EOF'
## Summary
- Adds `<Theme name=...>` / `<Color name=... value=.../>` top-level XML blocks
- Adds `UI.Theme.Current` / `Available` / `Set` / `Lookup` / `Resolve` / `Changed`
- Routes `Image` / `Text` / `Btn` color attrs through `UI.Theme.Resolve(value)`; fixes the silent-no-op-on-parse-failure bug
- Animation `char-color` "from:to" resolves each side via `UI.Theme.Resolve`
- Single-theme projects: auto-select; multi-theme: explicit `Set` required

Spec: `docs~/superpowers/specs/2026-05-28-color-tokens-design.md`
Plan: `docs~/superpowers/plans/2026-05-28-color-tokens.md`

## Breaking change
`.ui.xml` files containing malformed hex literals (e.g. `color="#ff800"`) — previously silently no-op'd — now throw `ParseException` with node context. Spec §6.3 covers migration.

## Test plan
- [ ] EditMode: full PromptUGUI.Tests.EditMode + EditorOnly green
- [ ] PlayMode: existing PromptUGUI.Tests.PlayMode unaffected
- [ ] Lint CLI: `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` clean
- [ ] Sanity smoke in host project: open a Screen with a token, switch theme via `UI.Theme.Set`, observe color update
EOF
)"
```

---

## Notes / Watch-outs (for executor)

- **Existing fixtures.** Several existing tests load `.ui.xml` fragments with hex literals. None of them should fail (legitimate hex still works), but Task 6 explicitly runs the full test pass after the setter swap to catch any pre-existing malformed-hex landmines.
- **`run_tests` flakiness.** Per memory note, Unity MCP `run_tests` can hang. If it does, `refresh_unity` first, then retry. If still hung, the user may need to restart Unity.
- **`Screen.ReSolve` subscription style.** Task 8 sketches `_disposables`; check the actual field used by Variants subscription (`Screen.cs:137`) and match it.
- **`AwaitableHelpers.Completed` visibility.** Task 5 falls back to a proxy class; if `InternalsVisibleTo` already exposes it (it should), drop the proxy.
- **Hot reload entry point.** Task 9 is the fuzziest task — the production `ReloadCommonLibraryAsync` flow may need plumbing changes. If the simplest path doesn't fall out, defer Task 9 (file an issue, leave a TODO in the spec's §11) and ship the rest. The `Theme.Changed` model works end-to-end without hot reload; hot reload is an editor-time convenience.
- **`UI.Theme.NotifyAfterReplaceForTests` rename.** As mentioned in Task 9 Step 3, this method is used by both tests and production; rename to `RaiseChangedIfCurrent(string)` in the same task if you want a clean name.
