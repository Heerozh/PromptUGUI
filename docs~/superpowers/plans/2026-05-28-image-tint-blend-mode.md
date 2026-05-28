# Image Tint Blend Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `tint="multiply|linear"` attribute to every Image-backed control that swaps the underlying `UnityEngine.UI.Image.material` between Unity's default (multiply blend) and the bundled `UI-LinearLightTint` material (Linear Light blend, 128-gray neutral).

**Architecture:** A single internal helper `ImageTint.Apply(Image, mode)` owns material loading (lazy `Resources.Load`, process-wide shared instance) and the multiply/linear/unknown dispatch. Each control adds a thin `[UIAttr] Tint` setter that calls the helper against its image field(s). `Progress` is the only multi-image case and uses a `_pendingTint` field so tint survives the bg/frame layers activating after the setter runs.

**Tech Stack:** Unity 6 uGUI, C# 9, NUnit EditMode tests, PromptUGUI `[UIAttr]` reflection registry. The shader/material assets already exist under `Runtime/Resources/PromptUGUI/Material/`.

---

## Background the implementer needs

**Why tests must NOT assert `image.material == null`.** Unity's `Graphic.material` getter returns `defaultMaterial` (the UI/Default canvas material, non-null) whenever its backing field is null. So `image.material = null` is the correct way to select multiply blend, but reading `image.material` afterward returns a non-null default material. Tests therefore assert:
- **multiply / no tint:** `Assert.AreEqual(img.defaultMaterial, img.material)` — the getter falls through to the default.
- **linear:** `Assert.AreNotEqual(img.defaultMaterial, img.material)` **and** `Assert.AreEqual("UI/LinearLightTint", img.material.shader.name)`.

`img.defaultMaterial` is a public property on `Graphic`.

**The shipped shader name is `"UI/LinearLightTint"`** (see `Runtime/Resources/PromptUGUI/Material/UI-LinearLightTint.shader:10`). The material asset is `Runtime/Resources/PromptUGUI/Material/UI-LinearLightTint.mat`, loadable via `Resources.Load<Material>("PromptUGUI/Material/UI-LinearLightTint")` (Resources folders inside UPM packages are merged into the global Resources lookup).

**Control image fields (verified):**

| Control | File | Image field | Tint applies to |
|---|---|---|---|
| `Image` | `Runtime/Controls/Image.cs` | `_img` | `_img` |
| `Icon` | `Runtime/Controls/Icon.cs` | `_img` | `_img` |
| `Btn` | `Runtime/Controls/Btn.cs` | `_bg` | `_bg` |
| `Toggle` | `Runtime/Controls/Toggle.cs` | `_bg` | `_bg` |
| `Slider` | `Runtime/Controls/Slider.cs` | `_bg` | `_bg` |
| `Dropdown` | `Runtime/Controls/Dropdown.cs` | `_bg` | `_bg` |
| `ScrollList` | `Runtime/Controls/ScrollList.cs` | `_bg` | `_bg` |
| `InputField` | `Runtime/Controls/InputField.cs` | `_bg` | `_bg` |
| `Progress` | `Runtime/Controls/Progress.cs` | `_fill` (always) + `_bg` / `_frame` (lazy) | all three |

Every `_bg` control has, right after its `[UIAttr(IsColor = true), Preserve] public string Color { set => _bg.color = UI.Theme.Resolve(value); }` block, an `[UIAttr(IsSprite = true), Preserve] public string Sprite` block. The `Tint` setter goes **between** the `Color` and `Sprite` blocks in each.

**`using` directives:** `Btn`, `Toggle`, `Slider`, `Dropdown`, `ScrollList`, `InputField`, `Progress`, and `Image` already have `using PromptUGUI.Controls.Internal;`. **`Icon.cs` does NOT** — Task 3 adds it.

**Test access patterns (verified against existing tests):**
- `Image` / `Icon`: `s.Get<Image>("x").GameObject.GetComponent<UnityImage>()`.
- `Btn`: bg is on the control's own GameObject — `s.Get<Btn>("b").GameObject.GetComponent<UnityImage>()`.
- `Toggle`: bg is a child — `s.Get<Toggle>("t").GameObject.transform.Find("Background").GetComponent<UnityImage>()`.
- `Progress` layers: `p.GameObject.transform.Find("MaskWrapper/Fill")`, `"MaskWrapper/Bg"`, `"Frame"` — each `.GetComponent<UnityImage>()`.

**Unknown-value warning** is asserted with `LogAssert.Expect(LogType.Warning, new Regex("tint"))` (NUnit `UnityEngine.TestTools.LogAssert`).

**Test SetUp/TearDown:** every class touching `UI` calls `UI.ResetForTests()` in both `[SetUp]` and `[TearDown]` (CLAUDE.md). `UI.LoadDocument(label, xml)` is the synchronous raw-XML entry point used throughout EditMode control tests.

**Build/test loop (CLAUDE.md):** after each source edit, in the host Unity project run
`mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`,
then `mcp__UnityMCP__read_console(action="get", types=["error"])` to confirm no compile errors,
then `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageTintTests")`.
Load these MCP tools first with `ToolSearch(query="select:refresh_unity,read_console,run_tests", max_results=3)`.

---

## File Structure

- **Create** `Runtime/Controls/Internal/ImageTint.cs` — the material load/cache/dispatch helper. One responsibility: turn a tint-mode string into the right `Image.material`.
- **Create** `Tests/EditMode/Controls/ImageTintTests.cs` — all behavior tests (Image matrix, Btn, Toggle, Progress, sharing, unknown-value warning, variant ReSolve).
- **Modify** `Runtime/Controls/Image.cs` — add `Tint` setter.
- **Modify** `Runtime/Controls/Icon.cs` — add `using PromptUGUI.Controls.Internal;` + `Tint` setter.
- **Modify** `Runtime/Controls/Btn.cs`, `Toggle.cs`, `Slider.cs`, `Dropdown.cs`, `ScrollList.cs`, `InputField.cs` — add `Tint` setter after each `Color` setter.
- **Modify** `Runtime/Controls/Progress.cs` — add `_pendingTint` field, `Tint` setter, and reapply at the four bg/frame activation points.
- **Modify** `.claude/skills/authoring-promptugui-xml/SKILL.md` — document `tint` attr + new "Tint blend modes" subsection.
- **Modify** `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` — footnote on Image-backed control attrs.

The shader/material assets already exist and were committed with the spec — no asset work in this plan.

---

## Task 1: `ImageTint` helper + `Image.Tint` setter (core matrix)

**Files:**
- Create: `Runtime/Controls/Internal/ImageTint.cs`
- Modify: `Runtime/Controls/Image.cs` (insert `Tint` setter after the `Color` setter at lines 41-45)
- Test: `Tests/EditMode/Controls/ImageTintTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/EditMode/Controls/ImageTintTests.cs`:

```csharp
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ImageTintTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string innerXml)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'>{innerXml}</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        private static UnityImage ImageOf(PromptUGUI.Application.Screen s, string id)
            => s.Get<Image>(id).GameObject.GetComponent<UnityImage>();

        [Test]
        public void NoTint_UsesDefaultMaterial()
        {
            var img = ImageOf(Open("<Image id='i' color='#ffffff'/>"), "i");
            Assert.AreEqual(img.defaultMaterial, img.material);
        }

        [Test]
        public void TintMultiply_UsesDefaultMaterial()
        {
            var img = ImageOf(Open("<Image id='i' color='#ffffff' tint='multiply'/>"), "i");
            Assert.AreEqual(img.defaultMaterial, img.material);
        }

        [Test]
        public void TintLinear_UsesLinearLightTintMaterial()
        {
            var img = ImageOf(Open("<Image id='i' color='#ffffff' tint='linear'/>"), "i");
            Assert.AreNotEqual(img.defaultMaterial, img.material);
            Assert.AreEqual("UI/LinearLightTint", img.material.shader.name);
        }

        [Test]
        public void TintUnknown_WarnsAndFallsBackToDefault()
        {
            LogAssert.Expect(LogType.Warning, new Regex("tint"));
            var img = ImageOf(Open("<Image id='i' color='#ffffff' tint='glow'/>"), "i");
            Assert.AreEqual(img.defaultMaterial, img.material);
        }

        [Test]
        public void TintLinearThenMultiply_ResetsToDefault()
        {
            var s = Open("<Image id='i' color='#ffffff' tint='linear'/>");
            var img = ImageOf(s, "i");
            Assert.AreEqual("UI/LinearLightTint", img.material.shader.name);

            // Re-drive the setter to simulate a value change; img is the same live component.
            s.Get<Image>("i").Tint = "multiply";
            Assert.AreEqual(img.defaultMaterial, img.material);
        }

        [Test]
        public void TwoLinearImages_ShareSameMaterialInstance()
        {
            var s = Open("<Image id='a' tint='linear'/><Image id='b' tint='linear'/>");
            var a = ImageOf(s, "a");
            var b = ImageOf(s, "b");
            Assert.AreSame(a.material, b.material);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Refresh Unity, then:
`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageTintTests")`
Expected: compile error (`Tint` setter and `ImageTint` don't exist yet) OR all tests fail. Either is the expected "red".

- [ ] **Step 3: Create the `ImageTint` helper**

Create `Runtime/Controls/Internal/ImageTint.cs`:

```csharp
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Switches a <see cref="UnityImage"/> between Unity's default multiply tint
    /// (material = null → Graphic falls back to UI/Default) and PromptUGUI's
    /// Linear Light tint material. The material asset is shared process-wide and
    /// lazy-loaded from Resources on first use; only the material setter is ever
    /// touched (never the getter), so no per-Image material instance is created.
    /// </summary>
    internal static class ImageTint
    {
        private const string LinearLightTintResourcePath = "PromptUGUI/Material/UI-LinearLightTint";
        private static Material _linearLightTint;

        public static void Apply(UnityImage img, string mode)
        {
            if (img == null) return;
            switch (mode)
            {
                case null:
                case "":
                case "multiply":
                    img.material = null;
                    break;
                case "linear":
                    img.material = _linearLightTint ??=
                        Resources.Load<Material>(LinearLightTintResourcePath);
                    break;
                default:
                    Debug.LogWarning(
                        $"PromptUGUI: tint=\"{mode}\" is not a recognized value " +
                        "(expected: multiply, linear). Falling back to multiply.");
                    img.material = null;
                    break;
            }
        }
    }
}
```

- [ ] **Step 4: Add the `Tint` setter to `Image.cs`**

In `Runtime/Controls/Image.cs`, immediately after the `Color` setter (lines 41-45, the block ending `set => _img.color = UI.Theme.Resolve(value);` and its closing `}`), insert:

```csharp
        [UIAttr, Preserve]
        public string Tint
        {
            set => ImageTint.Apply(_img, value);
        }
```

`Image.cs` already has `using PromptUGUI.Controls.Internal;` (line 2) — no using change needed.

- [ ] **Step 5: Run tests to verify they pass**

Refresh, check console for errors, then:
`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageTintTests")`
Expected: all 6 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add Runtime/Controls/Internal/ImageTint.cs Runtime/Controls/Internal/ImageTint.cs.meta \
        Runtime/Controls/Image.cs Tests/EditMode/Controls/ImageTintTests.cs Tests/EditMode/Controls/ImageTintTests.cs.meta
git commit -m "$(cat <<'EOF'
feat: tint=multiply|linear on <Image> via ImageTint helper

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

(Unity generates the `.meta` files on refresh; include them if present.)

---

## Task 2: `Tint` on the six `_bg` controls (Btn / Toggle / Slider / Dropdown / ScrollList / InputField)

**Files:**
- Modify: `Runtime/Controls/Btn.cs` (after `Color` setter, lines 100-104)
- Modify: `Runtime/Controls/Toggle.cs` (after `Color` setter, lines 110-114)
- Modify: `Runtime/Controls/Slider.cs` (after `Color` setter, lines 112-116)
- Modify: `Runtime/Controls/Dropdown.cs` (after `Color` setter, lines 194-198)
- Modify: `Runtime/Controls/ScrollList.cs` (after `Color` setter, lines 176-180)
- Modify: `Runtime/Controls/InputField.cs` (after `Color` setter, lines 164-168)
- Test: `Tests/EditMode/Controls/ImageTintTests.cs` (add Btn + Toggle tests)

- [ ] **Step 1: Write the failing tests**

Append these two methods inside the `ImageTintTests` class (before the closing brace):

```csharp
        [Test]
        public void Btn_TintLinear_AppliesToBackground()
        {
            var s = Open("<Btn id='b' color='#ffffff' label='Go' tint='linear'/>");
            var bg = s.Get<Btn>("b").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual("UI/LinearLightTint", bg.material.shader.name);
        }

        [Test]
        public void Toggle_TintLinear_AppliesToBackgroundChild()
        {
            var s = Open("<Toggle id='t' color='#ffffff' text='On' tint='linear'/>");
            var bg = s.Get<Toggle>("t").GameObject.transform.Find("Background")
                      .GetComponent<UnityImage>();
            Assert.AreEqual("UI/LinearLightTint", bg.material.shader.name);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageTintTests")`
Expected: compile error (no `Tint` on `Btn`/`Toggle`) or the two new tests fail.

- [ ] **Step 3: Add the `Tint` setter to all six controls**

The identical block goes between each control's `Color` setter and its `Sprite` setter. All six files already `using PromptUGUI.Controls.Internal;`.

In **`Runtime/Controls/Btn.cs`**, after lines 100-104 (`public string Color { set => _bg.color = UI.Theme.Resolve(value); }`):

```csharp
        [UIAttr, Preserve]
        public string Tint
        {
            set => ImageTint.Apply(_bg, value);
        }
```

Insert the **same block** after the `Color` setter in each of:
- `Runtime/Controls/Toggle.cs` (after lines 110-114)
- `Runtime/Controls/Slider.cs` (after lines 112-116)
- `Runtime/Controls/Dropdown.cs` (after lines 194-198)
- `Runtime/Controls/ScrollList.cs` (after lines 176-180)
- `Runtime/Controls/InputField.cs` (after lines 164-168)

The field is named `_bg` in all six — the block is byte-for-byte identical in every file.

- [ ] **Step 4: Run tests to verify they pass**

Refresh, check console, then:
`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageTintTests")`
Expected: all 8 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Btn.cs Runtime/Controls/Toggle.cs Runtime/Controls/Slider.cs \
        Runtime/Controls/Dropdown.cs Runtime/Controls/ScrollList.cs Runtime/Controls/InputField.cs \
        Tests/EditMode/Controls/ImageTintTests.cs
git commit -m "$(cat <<'EOF'
feat: tint attr on Btn/Toggle/Slider/Dropdown/ScrollList/InputField backgrounds

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: `Tint` on `Icon` (needs `using` added)

**Files:**
- Modify: `Runtime/Controls/Icon.cs` (add `using` at top; add `Tint` setter after `Color` setter at lines 44-48)
- Test: `Tests/EditMode/Controls/ImageTintTests.cs` (add Icon test)

- [ ] **Step 1: Write the failing test**

Append inside `ImageTintTests`:

```csharp
        [Test]
        public void Icon_TintLinear_AppliesToImage()
        {
            // No sprite resolver registered → a sprite LogError is expected; tint is independent.
            LogAssert.Expect(LogType.Error, new Regex("SpriteResolver"));
            var s = Open("<Icon id='i' name='ui:gear' color='#ffffff' tint='linear'/>");
            var img = s.Get<Icon>("i").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual("UI/LinearLightTint", img.material.shader.name);
        }
```

- [ ] **Step 2: Run test to verify it fails**

`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageTintTests")`
Expected: compile error (`Icon` has no `Tint`) or the new test fails.

- [ ] **Step 3: Add the `using` and `Tint` setter to `Icon.cs`**

In `Runtime/Controls/Icon.cs`, change the top `using` block (lines 1-4) from:

```csharp
using PromptUGUI.Application;
using PromptUGUI.Registry;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;
```

to (add the `Internal` using):

```csharp
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;
```

Then, after the `Color` setter (lines 44-48, ending `set => _img.color = UI.Theme.Resolve(value);` and its `}`), insert:

```csharp
        [UIAttr, Preserve]
        public string Tint
        {
            set => ImageTint.Apply(_img, value);
        }
```

- [ ] **Step 4: Run test to verify it passes**

Refresh, check console, then:
`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageTintTests")`
Expected: all 9 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Icon.cs Tests/EditMode/Controls/ImageTintTests.cs
git commit -m "$(cat <<'EOF'
feat: tint attr on <Icon>

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: `Tint` on `Progress` (three layers + activation-order safety)

**Files:**
- Modify: `Runtime/Controls/Progress.cs` (add `_pendingTint` field; add `Tint` setter; reapply at the `Bg`/`BgColor`/`Frame`/`FrameColor` activation points)
- Test: `Tests/EditMode/Controls/ImageTintTests.cs` (add Progress tests)

**Why the extra wiring:** `_bg` and `_frame` start inactive and are created/activated only when `bg=`/`bgColor=`/`frame=`/`frameColor=` run. If the `Tint` setter runs before those, `_bg`/`_frame` are null at tint time and would miss the material. Storing `_pendingTint` and reapplying it at each activation point makes the result independent of `[UIAttr]` reflection order (same pattern as the mask `_pending*` fields in `Image.cs`). `ImageTint.Apply` null-guards internally, so calling it on a not-yet-active layer is a safe no-op.

- [ ] **Step 1: Write the failing tests**

Append inside `ImageTintTests`. The second test pins the ordering guarantee by driving the `Tint` setter *before* `Bg` exists, then activating bg.

```csharp
        [Test]
        public void Progress_TintLinear_AppliesToFillBgAndFrame()
        {
            var s = Open("<Progress id='p' value='0.5' color='#ffffff' " +
                         "bgColor='#222222' frameColor='#888888' tint='linear'/>");
            var p = s.Get<Progress>("p").GameObject.transform;
            var fill  = p.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            var bg    = p.Find("MaskWrapper/Bg").GetComponent<UnityImage>();
            var frame = p.Find("Frame").GetComponent<UnityImage>();
            Assert.AreEqual("UI/LinearLightTint", fill.material.shader.name,  "fill");
            Assert.AreEqual("UI/LinearLightTint", bg.material.shader.name,    "bg");
            Assert.AreEqual("UI/LinearLightTint", frame.material.shader.name, "frame");
        }

        [Test]
        public void Progress_TintSetBeforeBgActivates_StillTintsBg()
        {
            // Drive setters in an order where Tint is applied while _bg is still inactive,
            // then activate bg via BgColor. The activation point must reapply _pendingTint.
            var prog = Open("<Progress id='p' value='0.5'/>").Get<Progress>("p");
            prog.Tint = "linear";          // _bg still inactive here
            prog.BgColor = "#222222";      // activates _bg; must reapply pending tint
            var bg = prog.GameObject.transform.Find("MaskWrapper/Bg").GetComponent<UnityImage>();
            Assert.AreEqual("UI/LinearLightTint", bg.material.shader.name);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageTintTests")`
Expected: compile error (`Progress` has no `Tint`) or the two new tests fail.

- [ ] **Step 3: Add `_pendingTint` field + `Tint` setter to `Progress.cs`**

In `Runtime/Controls/Progress.cs`, in the "Attribute state" group (after line 24, the `private string _mode = "scale";` line), add:

```csharp
        private string _pendingTint;
```

Then add the `Tint` setter. Place it right after the `Mode` setter block (after line 57, before the `Fill` setter at line 59):

```csharp
        [UIAttr, Preserve]
        public string Tint
        {
            set
            {
                _pendingTint = value;
                ImageTint.Apply(_fill, value);
                ImageTint.Apply(_bg, value);    // no-op if _bg not yet activated
                ImageTint.Apply(_frame, value); // no-op if _frame not yet activated
            }
        }
```

`Progress.cs` already has `using PromptUGUI.Controls.Internal;` (line 2).

- [ ] **Step 4: Reapply `_pendingTint` at the four activation points**

These edits add one line each so a layer that activates *after* the `Tint` setter still gets the material.

In the **`Bg`** setter (lines 75-86), after `AutoSlice(_bg);` (line 83) and before `ReconcileMaskVisibility();`:

```csharp
                ImageTint.Apply(_bg, _pendingTint);
```

In the **`BgColor`** setter (lines 88-97), after `_bg.gameObject.SetActive(true);` (line 94) and before `ReconcileMaskVisibility();`:

```csharp
                ImageTint.Apply(_bg, _pendingTint);
```

In the **`Frame`** setter (lines 99-109), after `AutoSlice(_frame);` (line 107):

```csharp
                ImageTint.Apply(_frame, _pendingTint);
```

In the **`FrameColor`** setter (lines 111-119), after `_frame.gameObject.SetActive(true);` (line 117):

```csharp
                ImageTint.Apply(_frame, _pendingTint);
```

- [ ] **Step 5: Run tests to verify they pass**

Refresh, check console, then:
`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageTintTests")`
Expected: all 11 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add Runtime/Controls/Progress.cs Tests/EditMode/Controls/ImageTintTests.cs
git commit -m "$(cat <<'EOF'
feat: tint attr on <Progress> (fill + bg + frame, activation-order safe)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Variant override test (runtime ReSolve path)

**Files:**
- Test: `Tests/EditMode/Controls/ImageTintTests.cs` (add variant test only — no source change expected)

This task proves `tint.var` works through the existing `VariantStore.Changed → Screen.ReSolve` path with no extra code. If it passes immediately, that confirms TINT-D10; if it fails, the implementer debugs (the setter must be a plain `[UIAttr]`, which it is).

- [ ] **Step 1: Write the test**

Append inside `ImageTintTests`:

```csharp
        [Test]
        public void Variant_OverridesTint_OnReSolve()
        {
            var s = Open("<Image id='i' color='#ffffff' tint='multiply' tint.dark='linear'/>");
            var img = ImageOf(s, "i");
            Assert.AreEqual(img.defaultMaterial, img.material, "multiply before variant");

            UI.VariantStore.Set("dark", true);
            s.ReSolve();
            Assert.AreEqual("UI/LinearLightTint", img.material.shader.name, "linear after variant");

            UI.VariantStore.Set("dark", false);
            s.ReSolve();
            Assert.AreEqual(img.defaultMaterial, img.material, "back to multiply");
        }
```

- [ ] **Step 2: Run test**

`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageTintTests")`
Expected: PASS (12 tests total). If it fails, debug per superpowers:systematic-debugging before proceeding — do not weaken the assertion.

- [ ] **Step 3: Commit**

```bash
git add Tests/EditMode/Controls/ImageTintTests.cs
git commit -m "$(cat <<'EOF'
test: tint.var variant override survives ReSolve

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Full EditMode run + lint

**Files:** none (verification only)

- [ ] **Step 1: Run the full EditMode suite**

`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])`
Expected: all green, including the 12 `ImageTintTests` and no regressions in `ProgressTests` / `ColorTokenIntegrationTests` / `BtnTests` / etc.

- [ ] **Step 2: Lint the C# (from repo root)**

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: no changes / no warnings. Do **not** run `dotnet format analyzers --severity info` (CLAUDE.md — it breaks Unity reflection contracts).

- [ ] **Step 3: Commit any whitespace fixes (if `dotnet format whitespace` changed files)**

```bash
git add -A && git commit -m "$(cat <<'EOF'
chore: dotnet format whitespace

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

(Skip if nothing changed.)

---

## Task 7: SKILL + spec documentation

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`

CLAUDE.md requires any new XML attribute to be reflected in the XML skill in the same PR. C# SKILL is **not** touched (no public C# API — `ImageTint` is internal; TINT-D15).

- [ ] **Step 1: Read the relevant SKILL sections**

Read `.claude/skills/authoring-promptugui-xml/SKILL.md` and locate (a) the built-in controls table rows for `<Image>` / `<Icon>` / `<Btn>` / `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` / `<InputField>` / `<Progress>`, and (b) the color/sprite section where a new "Tint blend modes" subsection fits (just after the color-tokens material).

- [ ] **Step 2: Add `tint` to each Image-backed control's attribute list**

In the controls table, append `tint="multiply\|linear"` to the attributes column for each of the nine controls listed above. Match the table's existing formatting exactly (do not reflow other rows).

- [ ] **Step 3: Add a "Tint blend modes" subsection**

Insert after the color section. Content (adapt headings to the file's existing markdown style):

```markdown
### Tint blend modes

Every Image-backed control (`<Image>`, `<Icon>`, `<Btn>`, `<Toggle>`, `<Slider>`,
`<Dropdown>`, `<ScrollList>`, `<InputField>`, `<Progress>`) accepts `tint=`, which
chooses how `color` combines with the sprite:

| `tint` | Blend | Use it for |
|---|---|---|
| `multiply` (default) | `result = sprite × color` — Unity's UI/Default. Omitting `tint` is the same as `multiply`. | Normal colored sprites; darkening. |
| `linear` | Linear Light — sprite is the blend layer, `color` is the base; 128-gray in the sprite is neutral, darker pushes toward black, lighter toward white. | Grayscale sprites you want to recolor across the full range (can brighten, not only darken). |

```xml
<!-- grayscale sprite recolored with Linear Light -->
<Image src="card-grayscale" color="#FF8040" tint="linear"/>

<!-- default multiply (unchanged from before) -->
<Image src="card-color" color="#888888"/>
```

- `tint` is orthogonal to `color`: `color` may be a hex value or a theme token; `tint`
  only selects the blend material.
- `<Text>` does **not** support `tint` — TMP text uses its own shader, not the UI Image
  shader.
- On `<Progress>`, `tint` applies to the fill, background, and frame layers together.
- Variants can switch it: `tint.landscape="linear"`.
```

- [ ] **Step 4: Add the spec footnote**

In `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`, in the §5/§6 control-attributes area for Image-backed controls, add a one-line note that these controls accept `tint="multiply|linear"` and point to `2026-05-28-image-tint-blend-mode-design.md`. Match the footnote style already used (e.g. the mask/color-token references).

- [ ] **Step 5: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md \
        docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md
git commit -m "$(cat <<'EOF'
docs: document tint=multiply|linear in XML skill + master spec

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Done criteria

- All 12 `ImageTintTests` pass; full EditMode suite green.
- `tint="linear"` swaps the Image material to `UI/LinearLightTint` on all nine Image-backed controls; `tint="multiply"` / omitted leaves the default material; unknown values warn and fall back.
- `<Progress>` tints fill + bg + frame regardless of attribute order.
- `tint.var` variant overrides flip the material on `ReSolve`.
- Lint clean. XML SKILL + master spec document the attribute.

## Notes / out of scope (from spec)

- No XSD enum work — the new `[UIAttr] Tint` is auto-discovered by `ControlMeta` reflection and serialized as `xs:string` like `Type`; regenerate the XSD via `Tools → PromptUGUI → Schema` when convenient (not required for tests). TINT-D11.
- No lint rule for illegal `tint` values (runtime warning suffices). TINT-D12.
- No `<Text>`/TMP tint, no user-supplied material attr, no per-layer Progress tint, no R3 binding. Future `tint` enum values (e.g. `grayscale`, `outline`) are where the user-material "route B" would land. §7 of the spec.
```
