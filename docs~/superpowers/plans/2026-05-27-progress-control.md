# `<Progress>` Control Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `<Progress>` built-in control: a linear (horizontal / vertical) progress bar that packages frame / mask / bg / fill / mode (scale or `Image.Type.Filled`) / direction / value into one XML element, per spec `2026-05-27-progress-control-design.md`.

**Architecture:** New leaf control `PromptUGUI.Controls.Progress` (mirrors `Slider`'s programmatic-child-tree pattern). Always builds 4 RectTransforms (`MaskWrapper → [Bg, Fill]`, `Frame`); attaches `UnityImage` / `UnityEngine.UI.Mask` lazily by setter. Lint rules live in `Runtime/Core/Lint/ProgressAttributeRules.cs` and are dispatched by both `IRWalker` (CLI) and `ScreenInstantiator` (runtime warnings), matching the `MaskAttributeRules` pattern.

**Tech Stack:** Unity 6, uGUI (`UnityEngine.UI.Image`, `UnityEngine.UI.Mask`), R3, NUnit + Unity Test Framework, Unity MCP for compile + test orchestration.

**Branch:** `feat/progress-control` (already created; spec already committed).

**Verification cadence:** After every source edit, `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` → `mcp__UnityMCP__read_console(action="get", types=["error"])`. Run tests via `mcp__UnityMCP__run_tests(...)` with `filter=` per task. Lint CLI: `dotnet run --project .lint/UIXmlLint -- <path>`.

---

## File Structure

| Path | Role | Action |
|---|---|---|
| `Runtime/Controls/Progress.cs` | The control: structure + setters + reconcile | **Create** |
| `Runtime/Application/BuiltinPrimitives.cs` | Register `Progress` tag | **Modify** (1 line) |
| `Runtime/Core/Lint/ProgressAttributeRules.cs` | 6 lint rules: value range / mode / direction / children / mask-variant / no-fill | **Create** |
| `Runtime/Core/Lint/IRWalker.cs` | Dispatch `Progress` self-check | **Modify** (add branch to `WalkNode`) |
| `Runtime/Application/ScreenInstantiator.cs` | Dispatch `Progress` self-check (runtime warning) | **Modify** (add branch ~line 190) |
| `Tests/EditMode/Controls/ProgressTests.cs` | Runtime behaviour (instantiation / reconcile / layer activation / GetNativeSize) | **Create** |
| `Tests/EditMode/Lint/ProgressAttributeRulesTests.cs` | Unit tests for the 6 lint rules | **Create** |
| `Tests/EditMode/Lint/IRWalkerProgressTests.cs` | IRWalker dispatches Progress rules | **Create** |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` | Add `<Progress>` row + Progress section + lint codes | **Modify** |
| `.claude/skills/scripting-promptugui-csharp/SKILL.md` | Add `Get<Progress>().Value` note | **Modify** |
| `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` | Add Progress row in §5 | **Modify** |

---

## Tasks

### Task 1: Stub Progress + register + structure smoke test

**Files:**
- Create: `Runtime/Controls/Progress.cs`
- Modify: `Runtime/Application/BuiltinPrimitives.cs:24` (add registration line after Slider)
- Create: `Tests/EditMode/Controls/ProgressTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Controls/ProgressTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;
using UnityMask = UnityEngine.UI.Mask;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ProgressTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Progress Open(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Progress>("p");
        }

        [Test]
        public void Empty_Progress_Has_MaskWrapper_Fill_Frame_Children()
        {
            var p = Open("<Progress id='p'/>");
            var maskWrapper = p.GameObject.transform.Find("MaskWrapper") as RectTransform;
            var bg = p.GameObject.transform.Find("MaskWrapper/Bg") as RectTransform;
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            var frame = p.GameObject.transform.Find("Frame") as RectTransform;
            Assert.IsNotNull(maskWrapper, "MaskWrapper RT");
            Assert.IsNotNull(bg, "Bg RT (inside MaskWrapper)");
            Assert.IsNotNull(fill, "Fill RT (inside MaskWrapper)");
            Assert.IsNotNull(frame, "Frame RT");
            Assert.IsFalse(bg.gameObject.activeSelf, "Bg starts disabled");
            Assert.IsFalse(frame.gameObject.activeSelf, "Frame starts disabled");
            Assert.IsNull(maskWrapper.gameObject.GetComponent<UnityMask>(),
                "no mask= → no UI.Mask on MaskWrapper");
            Assert.IsNull(maskWrapper.gameObject.GetComponent<UnityImage>(),
                "no mask= → no UnityImage on MaskWrapper");
        }

        [Test]
        public void Progress_Root_Has_No_Image()
        {
            var p = Open("<Progress id='p'/>");
            Assert.IsNull(p.GameObject.GetComponent<UnityImage>(),
                "Progress root is a pure RectTransform host, no Graphic");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails (compile error — Progress tag unknown)**

Run via MCP:

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile error referencing `PromptUGUI.Controls.Progress` (type doesn't exist).

- [ ] **Step 3: Write minimal Progress.cs**

Create `Runtime/Controls/Progress.cs`:

```csharp
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    /// Linear progress bar (horizontal / vertical, scale or Image.Type.Filled).
    /// Radial fill (cooldown ring) is intentionally out of scope; introduce a
    /// <Cooldown> control instead — see spec PB-D6.
    public sealed class Progress : Control
    {
        private UnityImage _bg;
        private UnityImage _fill;
        private UnityImage _frame;
        private UnityImage _maskGraphic;     // null until mask= setter runs
        private UnityEngine.UI.Mask _stencilMask;

        public override void OnAttached()
        {
            // MaskWrapper: stretch wrapper around Bg + Fill. UI.Mask + UnityImage attached
            // lazily when mask= setter runs (PB-D7 / PB-D8).
            var maskRt = ProceduralBuilders.AddChild(RectTransform, "MaskWrapper");

            // Bg: pre-built but inactive until bg=/bgColor= sets it (PB-D8 / PB-D9 / PB-D10).
            var bgRt = ProceduralBuilders.AddChild(maskRt, "Bg");
            bgRt.gameObject.SetActive(false);
            _bg = bgRt.gameObject.AddComponent<UnityImage>();
            _bg.raycastTarget = false;

            // Fill: always present; reconcile writes its anchors or fillAmount.
            _fill = ProceduralBuilders.AddImage(maskRt, "Fill", raycast: false);

            // Frame: pre-built but inactive until frame= sets it. PB-D16: raycast off.
            var frameRt = ProceduralBuilders.AddChild(RectTransform, "Frame");
            frameRt.gameObject.SetActive(false);
            _frame = frameRt.gameObject.AddComponent<UnityImage>();
            _frame.raycastTarget = false;
        }
    }
}
```

Modify `Runtime/Application/BuiltinPrimitives.cs` — after the Slider line (`reg.Register<Slider>("Slider", null);`):

```csharp
            reg.Register<Slider>("Slider", null);
            reg.Register<Progress>("Progress", null);
            reg.Register<Dropdown>("Dropdown", null);
```

- [ ] **Step 4: Run tests to verify they pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ProgressTests")
```

Expected: PASS — both tests green.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Progress.cs Runtime/Controls/Progress.cs.meta Runtime/Application/BuiltinPrimitives.cs Tests/EditMode/Controls/ProgressTests.cs Tests/EditMode/Controls/ProgressTests.cs.meta
git commit -m "feat(progress): stub control + builtin registration + structure test"
```

(Note: `.meta` files appear after Unity refresh; if they don't exist yet, omit them and a subsequent commit will catch them once Unity generates them.)

---

### Task 2: `value` attr — clamp [0..1]

**Files:**
- Modify: `Runtime/Controls/Progress.cs` (add `Value` field + setter)
- Modify: `Tests/EditMode/Controls/ProgressTests.cs` (3 new tests)

- [ ] **Step 1: Write failing tests**

Append to `ProgressTests.cs`:

```csharp
        [Test]
        public void Value_Stores_InRangeAsIs()
        {
            var p = Open("<Progress id='p' value='0.5'/>");
            Assert.AreEqual(0.5f, p.Value);
        }

        [Test]
        public void Value_Below_Zero_Clamps_To_Zero()
        {
            var p = Open("<Progress id='p'/>");
            p.Value = -0.3f;
            Assert.AreEqual(0f, p.Value);
        }

        [Test]
        public void Value_Above_One_Clamps_To_One()
        {
            var p = Open("<Progress id='p' value='1.7'/>");
            Assert.AreEqual(1f, p.Value);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ProgressTests")
```

Expected: 3 new tests FAIL ("Value not found" or compile error).

- [ ] **Step 3: Add Value field + setter to Progress.cs**

In `Progress.cs`, add private field below the `_stencilMask` line:

```csharp
        private float _value;
```

Add public attr below `OnAttached()`:

```csharp
        [UIAttr, Preserve]
        public float Value
        {
            get => _value;
            set => _value = Mathf.Clamp01(value);
        }
```

Note: `Mathf.Clamp01` handles NaN → 0 (PB-D19) because `Clamp01` returns 0 for NaN inputs.

- [ ] **Step 4: Run tests to verify they pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ProgressTests")
```

Expected: PASS — 5 total tests green.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Progress.cs Tests/EditMode/Controls/ProgressTests.cs
git commit -m "feat(progress): value attr with [0..1] clamp"
```

---

### Task 3: scale mode + horizontal direction reconcile

Implements the default `mode="scale"` + `direction="horizontal"` path: writing `value` updates `Fill` `anchorMin/anchorMax`.

**Files:**
- Modify: `Runtime/Controls/Progress.cs` (add `OnAfterApply` + `ReconcileFill`)
- Modify: `Tests/EditMode/Controls/ProgressTests.cs`

- [ ] **Step 1: Write failing tests**

Append:

```csharp
        [Test]
        public void Scale_Horizontal_Value_Half_Anchors_Right_At_Half()
        {
            var p = Open("<Progress id='p' value='0.5'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            Assert.AreEqual(Vector2.zero, fill.anchorMin);
            Assert.AreEqual(new Vector2(0.5f, 1f), fill.anchorMax);
            Assert.AreEqual(Vector2.zero, fill.offsetMin);
            Assert.AreEqual(Vector2.zero, fill.offsetMax);
        }

        [Test]
        public void Scale_Horizontal_Value_Zero_Fill_Is_Zero_Width()
        {
            var p = Open("<Progress id='p' value='0'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            Assert.AreEqual(new Vector2(0f, 1f), fill.anchorMax);
        }

        [Test]
        public void Scale_Horizontal_Value_One_Fill_Full_Width()
        {
            var p = Open("<Progress id='p' value='1'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            Assert.AreEqual(new Vector2(1f, 1f), fill.anchorMax);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Expected: 3 new tests FAIL (anchorMax still default `(1,1)` since reconcile doesn't exist yet).

- [ ] **Step 3: Add OnAfterApply + ReconcileFill (scale-horizontal only)**

In `Progress.cs`, add private fields:

```csharp
        private string _mode = "scale";
        private string _direction = "horizontal";
```

Override `OnAfterApply` (it's `internal virtual` on `Control` — see `Runtime/Controls/Control.cs:54`):

```csharp
        internal override void OnAfterApply()
        {
            ReconcileFill();
        }

        private void ReconcileFill()
        {
            var rt = _fill.rectTransform;
            // v1 single path: mode=scale, direction=horizontal (other modes/directions
            // land in tasks 4-5).
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(_value, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ProgressTests")
```

Expected: PASS — 8 total green.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Progress.cs Tests/EditMode/Controls/ProgressTests.cs
git commit -m "feat(progress): scale-mode horizontal reconcile from value"
```

---

### Task 4: `direction` attr — all four directions in scale mode

**Files:**
- Modify: `Runtime/Controls/Progress.cs`
- Modify: `Tests/EditMode/Controls/ProgressTests.cs`

- [ ] **Step 1: Write failing tests**

Append:

```csharp
        [Test]
        public void Scale_ReverseHorizontal_Anchors_From_Right()
        {
            var p = Open("<Progress id='p' value='0.25' direction='reverse-horizontal'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            Assert.AreEqual(new Vector2(0.75f, 0f), fill.anchorMin);
            Assert.AreEqual(new Vector2(1f, 1f), fill.anchorMax);
        }

        [Test]
        public void Scale_Vertical_Anchors_From_Bottom()
        {
            var p = Open("<Progress id='p' value='0.4' direction='vertical'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            Assert.AreEqual(Vector2.zero, fill.anchorMin);
            Assert.AreEqual(new Vector2(1f, 0.4f), fill.anchorMax);
        }

        [Test]
        public void Scale_ReverseVertical_Anchors_From_Top()
        {
            var p = Open("<Progress id='p' value='0.4' direction='reverse-vertical'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            Assert.AreEqual(new Vector2(0f, 0.6f), fill.anchorMin);
            Assert.AreEqual(new Vector2(1f, 1f), fill.anchorMax);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Expected: 3 new tests FAIL.

- [ ] **Step 3: Add Direction setter + 4-way switch in ReconcileFill**

Add setter below `Value`:

```csharp
        [UIAttr, Preserve]
        public string Direction { set => _direction = value; }
```

Replace `ReconcileFill` body with the 4-way switch:

```csharp
        private void ReconcileFill()
        {
            var rt = _fill.rectTransform;
            (rt.anchorMin, rt.anchorMax) = _direction switch
            {
                "horizontal"          => (new Vector2(0f, 0f),        new Vector2(_value, 1f)),
                "reverse-horizontal"  => (new Vector2(1f - _value, 0f), new Vector2(1f, 1f)),
                "vertical"            => (new Vector2(0f, 0f),        new Vector2(1f, _value)),
                "reverse-vertical"    => (new Vector2(0f, 1f - _value), new Vector2(1f, 1f)),
                _                     => (new Vector2(0f, 0f),        new Vector2(_value, 1f)),
            };
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ProgressTests")
```

Expected: PASS — 11 total green.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Progress.cs Tests/EditMode/Controls/ProgressTests.cs
git commit -m "feat(progress): direction attr (4-way switch for scale mode)"
```

---

### Task 5: `mode="fill"` — Image.Type.Filled with fillMethod/fillOrigin/fillAmount

**Files:**
- Modify: `Runtime/Controls/Progress.cs`
- Modify: `Tests/EditMode/Controls/ProgressTests.cs`

- [ ] **Step 1: Write failing tests**

Append:

```csharp
        [Test]
        public void Fill_Horizontal_Sets_Type_Filled_And_FillAmount()
        {
            var p = Open("<Progress id='p' value='0.7' mode='fill'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreEqual(UnityImage.Type.Filled, fill.type);
            Assert.AreEqual(UnityImage.FillMethod.Horizontal, fill.fillMethod);
            Assert.AreEqual((int)UnityImage.OriginHorizontal.Left, fill.fillOrigin);
            Assert.AreEqual(0.7f, fill.fillAmount);
            var rt = fill.rectTransform;
            Assert.AreEqual(Vector2.zero, rt.anchorMin);
            Assert.AreEqual(Vector2.one, rt.anchorMax);
        }

        [Test]
        public void Fill_ReverseHorizontal_Origin_Right()
        {
            var p = Open("<Progress id='p' value='0.5' mode='fill' direction='reverse-horizontal'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreEqual(UnityImage.FillMethod.Horizontal, fill.fillMethod);
            Assert.AreEqual((int)UnityImage.OriginHorizontal.Right, fill.fillOrigin);
        }

        [Test]
        public void Fill_Vertical_Origin_Bottom()
        {
            var p = Open("<Progress id='p' value='0.5' mode='fill' direction='vertical'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreEqual(UnityImage.FillMethod.Vertical, fill.fillMethod);
            Assert.AreEqual((int)UnityImage.OriginVertical.Bottom, fill.fillOrigin);
        }

        [Test]
        public void Fill_ReverseVertical_Origin_Top()
        {
            var p = Open("<Progress id='p' value='0.5' mode='fill' direction='reverse-vertical'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreEqual(UnityImage.FillMethod.Vertical, fill.fillMethod);
            Assert.AreEqual((int)UnityImage.OriginVertical.Top, fill.fillOrigin);
        }

        [Test]
        public void Switch_From_Fill_Back_To_Scale_Resets_Type_And_FillAmount()
        {
            var p = Open("<Progress id='p' value='0.6' mode='fill'/>");
            p.Mode = "scale";
            // ReSolve isn't auto-fired; simulate by writing Value to trigger setter path,
            // but actual reconcile happens in OnAfterApply. We need to call it via re-apply.
            // Instead use the public setter pattern: setting Mode then Value won't call
            // OnAfterApply by itself; for this test, set both via XML reload.
            var p2 = Open("<Progress id='p' value='0.6' mode='scale'/>");
            var fill = p2.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreNotEqual(UnityImage.Type.Filled, fill.type,
                "scale mode must reset away from Filled");
            Assert.AreEqual(1f, fill.fillAmount, "scale mode resets fillAmount to 1");
            var rt = fill.rectTransform;
            Assert.AreEqual(new Vector2(0.6f, 1f), rt.anchorMax);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Expected: 5 new tests FAIL.

- [ ] **Step 3: Add Mode setter + fill branch in ReconcileFill**

Add setter:

```csharp
        [UIAttr, Preserve]
        public string Mode { set => _mode = value; }
```

Replace `ReconcileFill` body with mode dispatch:

```csharp
        private void ReconcileFill()
        {
            var rt = _fill.rectTransform;
            if (_mode == "fill")
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                _fill.type = UnityImage.Type.Filled;
                (_fill.fillMethod, _fill.fillOrigin) = _direction switch
                {
                    "horizontal"         => (UnityImage.FillMethod.Horizontal, (int)UnityImage.OriginHorizontal.Left),
                    "reverse-horizontal" => (UnityImage.FillMethod.Horizontal, (int)UnityImage.OriginHorizontal.Right),
                    "vertical"           => (UnityImage.FillMethod.Vertical,   (int)UnityImage.OriginVertical.Bottom),
                    "reverse-vertical"   => (UnityImage.FillMethod.Vertical,   (int)UnityImage.OriginVertical.Top),
                    _                    => (UnityImage.FillMethod.Horizontal, (int)UnityImage.OriginHorizontal.Left),
                };
                _fill.fillAmount = _value;
            }
            else // scale (default)
            {
                // Reset away from Filled, then pick Simple/Sliced per sprite border.
                _fill.fillAmount = 1f;
                _fill.type = (_fill.sprite != null && _fill.sprite.border != Vector4.zero)
                    ? UnityImage.Type.Sliced
                    : UnityImage.Type.Simple;
                (rt.anchorMin, rt.anchorMax) = _direction switch
                {
                    "horizontal"         => (new Vector2(0f, 0f),         new Vector2(_value, 1f)),
                    "reverse-horizontal" => (new Vector2(1f - _value, 0f), new Vector2(1f, 1f)),
                    "vertical"           => (new Vector2(0f, 0f),         new Vector2(1f, _value)),
                    "reverse-vertical"   => (new Vector2(0f, 1f - _value), new Vector2(1f, 1f)),
                    _                    => (new Vector2(0f, 0f),         new Vector2(_value, 1f)),
                };
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
        }
```

- [ ] **Step 4: Run tests to verify they pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ProgressTests")
```

Expected: PASS — 16 total green.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Progress.cs Tests/EditMode/Controls/ProgressTests.cs
git commit -m "feat(progress): mode=fill (Image.Type.Filled) + 4-way fillOrigin"
```

---

### Task 6: `fill` + `fillColor` attrs

**Files:**
- Modify: `Runtime/Controls/Progress.cs`
- Modify: `Tests/EditMode/Controls/ProgressTests.cs`

The fill sprite goes through `UI.ResolveSprite` (matches `Image.cs:38`). 9-slice auto-detect happens in `ReconcileFill`'s scale branch (already wired in Task 5).

- [ ] **Step 1: Write failing tests**

Append:

```csharp
        [Test]
        public void Fill_Sprite_Resolves_Via_UI_ResolveSprite()
        {
            // Use the default atlas sprite shipped with the package
            var p = Open("<Progress id='p' fill='pugui#pugui_9slice_round'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.IsNotNull(fill.sprite, "sprite resolved from atlas key");
            Assert.AreEqual("pugui_9slice_round", fill.sprite.name);
        }

        [Test]
        public void Fill_9Slice_Sprite_Auto_Sliced_In_Scale_Mode()
        {
            var p = Open("<Progress id='p' value='0.5' fill='pugui#pugui_9slice_round'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            // pugui_9slice_round has non-zero border → Sliced
            Assert.AreEqual(UnityImage.Type.Sliced, fill.type);
        }

        [Test]
        public void Fill_9Slice_Sprite_Becomes_Filled_When_Mode_Is_Fill()
        {
            var p = Open("<Progress id='p' value='0.5' mode='fill' fill='pugui#pugui_9slice_round'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreEqual(UnityImage.Type.Filled, fill.type,
                "mode=fill must force Filled even for 9-slice sprites");
        }

        [Test]
        public void FillColor_Parses_Hex()
        {
            var p = Open("<Progress id='p' fillColor='#ff0000'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreEqual(Color.red, fill.color);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Expected: 4 new tests FAIL.

- [ ] **Step 3: Add Fill + FillColor setters**

In `Progress.cs`, add `using PromptUGUI.Application;` if not present (for `UI.ResolveSprite`).

Add setters below `Direction`:

```csharp
        [UIAttr, Preserve]
        public string Fill
        {
            set => _fill.sprite = UI.ResolveSprite(value);
        }

        [UIAttr, Preserve]
        public string FillColor
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (ColorUtility.TryParseHtmlString(value, out var c)) _fill.color = c;
            }
        }
```

- [ ] **Step 4: Run tests to verify they pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ProgressTests")
```

Expected: PASS — 20 total green.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Progress.cs Tests/EditMode/Controls/ProgressTests.cs
git commit -m "feat(progress): fill (sprite) + fillColor attrs"
```

---

### Task 7: `bg` + `bgColor` attrs — activate Bg layer + auto-Slice

**Files:**
- Modify: `Runtime/Controls/Progress.cs`
- Modify: `Tests/EditMode/Controls/ProgressTests.cs`

- [ ] **Step 1: Write failing tests**

Append:

```csharp
        [Test]
        public void Bg_Sprite_Activates_Bg_Layer()
        {
            var p = Open("<Progress id='p' bg='pugui#pugui_9slice_round'/>");
            var bg = p.GameObject.transform.Find("MaskWrapper/Bg");
            Assert.IsTrue(bg.gameObject.activeSelf, "Bg activated by bg=");
            var img = bg.GetComponent<UnityImage>();
            Assert.IsNotNull(img.sprite);
            Assert.AreEqual(UnityImage.Type.Sliced, img.type, "9-slice sprite auto-Sliced");
        }

        [Test]
        public void BgColor_Alone_Activates_Bg_Layer_With_Color()
        {
            var p = Open("<Progress id='p' bgColor='#222222'/>");
            var bg = p.GameObject.transform.Find("MaskWrapper/Bg");
            Assert.IsTrue(bg.gameObject.activeSelf, "Bg activated by bgColor= alone");
            var img = bg.GetComponent<UnityImage>();
            ColorUtility.TryParseHtmlString("#222222", out var expected);
            Assert.AreEqual(expected, img.color);
        }

        [Test]
        public void No_Bg_No_BgColor_Bg_Layer_Stays_Inactive()
        {
            var p = Open("<Progress id='p' fill='pugui#pugui_9slice_round'/>");
            var bg = p.GameObject.transform.Find("MaskWrapper/Bg");
            Assert.IsFalse(bg.gameObject.activeSelf);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Expected: 3 new tests FAIL.

- [ ] **Step 3: Add Bg + BgColor setters + AutoSlice call in OnAfterApply**

Add to `Progress.cs`:

```csharp
        [UIAttr, Preserve]
        public string Bg
        {
            set
            {
                _bg.sprite = UI.ResolveSprite(value);
                _bg.gameObject.SetActive(true);
            }
        }

        [UIAttr, Preserve]
        public string BgColor
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (!ColorUtility.TryParseHtmlString(value, out var c)) return;
                _bg.color = c;
                _bg.gameObject.SetActive(true);
            }
        }
```

Update `OnAfterApply` to also auto-Slice Bg:

```csharp
        internal override void OnAfterApply()
        {
            AutoSlice(_bg);
            ReconcileFill();
        }

        private static void AutoSlice(UnityImage img)
        {
            if (img == null || img.sprite == null) return;
            img.type = img.sprite.border != Vector4.zero
                ? UnityImage.Type.Sliced
                : UnityImage.Type.Simple;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Expected: PASS — 23 total green.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Progress.cs Tests/EditMode/Controls/ProgressTests.cs
git commit -m "feat(progress): bg + bgColor attrs (activate Bg layer, auto-Slice)"
```

---

### Task 8: `frame` attr — activate Frame layer

**Files:**
- Modify: `Runtime/Controls/Progress.cs`
- Modify: `Tests/EditMode/Controls/ProgressTests.cs`

- [ ] **Step 1: Write failing tests**

Append:

```csharp
        [Test]
        public void Frame_Sprite_Activates_Frame_Layer_With_Raycast_Off()
        {
            var p = Open("<Progress id='p' frame='pugui#pugui_9slice_round'/>");
            var frame = p.GameObject.transform.Find("Frame");
            Assert.IsTrue(frame.gameObject.activeSelf);
            var img = frame.GetComponent<UnityImage>();
            Assert.IsNotNull(img.sprite);
            Assert.AreEqual(UnityImage.Type.Sliced, img.type, "9-slice sprite auto-Sliced");
            Assert.IsFalse(img.raycastTarget, "Frame must not eat input (PB-D16)");
        }

        [Test]
        public void No_Frame_Frame_Layer_Stays_Inactive()
        {
            var p = Open("<Progress id='p' fill='pugui#pugui_9slice_round'/>");
            var frame = p.GameObject.transform.Find("Frame");
            Assert.IsFalse(frame.gameObject.activeSelf);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Expected: 2 new tests FAIL (no Frame setter yet).

- [ ] **Step 3: Add Frame setter + AutoSlice in OnAfterApply**

```csharp
        [UIAttr, Preserve]
        public string Frame
        {
            set
            {
                _frame.sprite = UI.ResolveSprite(value);
                _frame.gameObject.SetActive(true);
            }
        }
```

Extend `OnAfterApply`:

```csharp
        internal override void OnAfterApply()
        {
            AutoSlice(_bg);
            AutoSlice(_frame);
            ReconcileFill();
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Expected: PASS — 25 total green.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Progress.cs Tests/EditMode/Controls/ProgressTests.cs
git commit -m "feat(progress): frame attr (decorative overlay, raycast off)"
```

---

### Task 9: `mask` attr + `showMaskGraphic` reconcile

**Files:**
- Modify: `Runtime/Controls/Progress.cs`
- Modify: `Tests/EditMode/Controls/ProgressTests.cs`

`UnityEngine.UI.Mask` requires a Graphic on the same GameObject (`UnityImage` in our case). The combined setter creates both. `showMaskGraphic` is determined in `OnAfterApply` (after all setters have run) based on whether `Bg` is active.

- [ ] **Step 1: Write failing tests**

Append:

```csharp
        [Test]
        public void Mask_Alone_Adds_Image_Plus_Mask_With_ShowMaskGraphic_True()
        {
            var p = Open("<Progress id='p' mask='pugui#pugui_9slice_mask'/>");
            var wrapper = p.GameObject.transform.Find("MaskWrapper").gameObject;
            Assert.IsNotNull(wrapper.GetComponent<UnityImage>(), "mask= adds UnityImage to wrapper");
            var m = wrapper.GetComponent<UnityMask>();
            Assert.IsNotNull(m, "mask= adds UI.Mask");
            Assert.IsTrue(m.showMaskGraphic, "no bg → mask sprite visible (PB-D9)");
        }

        [Test]
        public void Mask_With_Bg_Sprite_Hides_Mask_Graphic()
        {
            var p = Open("<Progress id='p' mask='pugui#pugui_9slice_mask' bg='pugui#pugui_9slice_round'/>");
            var wrapper = p.GameObject.transform.Find("MaskWrapper").gameObject;
            var m = wrapper.GetComponent<UnityMask>();
            Assert.IsFalse(m.showMaskGraphic, "bg present → mask is invisible stencil only (PB-D10)");
        }

        [Test]
        public void Mask_With_BgColor_Only_Hides_Mask_Graphic()
        {
            var p = Open("<Progress id='p' mask='pugui#pugui_9slice_mask' bgColor='#222222'/>");
            var m = p.GameObject.transform.Find("MaskWrapper").GetComponent<UnityMask>();
            Assert.IsFalse(m.showMaskGraphic, "bgColor alone also counts as bg present");
        }

        [Test]
        public void No_Mask_No_Image_No_Mask_Component_On_Wrapper()
        {
            var p = Open("<Progress id='p' fill='pugui#pugui_9slice_round'/>");
            var wrapper = p.GameObject.transform.Find("MaskWrapper").gameObject;
            Assert.IsNull(wrapper.GetComponent<UnityImage>());
            Assert.IsNull(wrapper.GetComponent<UnityMask>());
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Expected: 4 new tests FAIL.

- [ ] **Step 3: Add Mask setter + showMaskGraphic reconcile**

Add setter (lazy `AddComponent` per PB-D7):

```csharp
        [UIAttr, Preserve]
        public string Mask
        {
            set
            {
                if (_maskGraphic == null)
                {
                    var maskRt = (RectTransform)_fill.transform.parent;
                    _maskGraphic = maskRt.gameObject.AddComponent<UnityImage>();
                    _maskGraphic.raycastTarget = false;
                    _stencilMask = maskRt.gameObject.AddComponent<UnityEngine.UI.Mask>();
                }
                _maskGraphic.sprite = UI.ResolveSprite(value);
            }
        }
```

Extend `OnAfterApply` to settle `showMaskGraphic` after all setters (incl. Bg) have run:

```csharp
        internal override void OnAfterApply()
        {
            AutoSlice(_bg);
            AutoSlice(_frame);
            AutoSlice(_maskGraphic);
            if (_stencilMask != null)
                _stencilMask.showMaskGraphic = !_bg.gameObject.activeSelf;
            ReconcileFill();
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Expected: PASS — 29 total green.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Progress.cs Tests/EditMode/Controls/ProgressTests.cs
git commit -m "feat(progress): mask attr + showMaskGraphic reconcile from bg state"
```

---

### Task 10: `GetNativeSize()` fallback chain

**Files:**
- Modify: `Runtime/Controls/Progress.cs`
- Modify: `Tests/EditMode/Controls/ProgressTests.cs`

- [ ] **Step 1: Write failing tests**

Append:

```csharp
        [Test]
        public void GetNativeSize_Default_Is_160x16()
        {
            var p = Open("<Progress id='p'/>");
            var n = p.GetNativeSize();
            Assert.IsTrue(n.HasValue);
            Assert.AreEqual(new Vector2(160f, 16f), n.Value);
        }

        [Test]
        public void GetNativeSize_Falls_Back_To_Bg_When_No_Frame()
        {
            var p = Open("<Progress id='p' bg='pugui#pugui_9slice_round'/>");
            var n = p.GetNativeSize();
            Assert.IsTrue(n.HasValue);
            // Expected = bg sprite rect / pixelsPerUnit (same formula as Image.GetNativeSize)
            var img = p.GameObject.transform.Find("MaskWrapper/Bg").GetComponent<UnityImage>();
            var expected = new Vector2(img.sprite.rect.width / img.pixelsPerUnit,
                                       img.sprite.rect.height / img.pixelsPerUnit);
            Assert.AreEqual(expected, n.Value);
        }

        [Test]
        public void GetNativeSize_Prefers_Frame_Over_Bg()
        {
            var p = Open("<Progress id='p' bg='pugui#pugui_9slice_round' frame='pugui#pugui_9slice_mask'/>");
            var n = p.GetNativeSize();
            var img = p.GameObject.transform.Find("Frame").GetComponent<UnityImage>();
            var expected = new Vector2(img.sprite.rect.width / img.pixelsPerUnit,
                                       img.sprite.rect.height / img.pixelsPerUnit);
            Assert.AreEqual(expected, n.Value);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Expected: 3 new tests FAIL (base `GetNativeSize` returns `null`).

- [ ] **Step 3: Override GetNativeSize**

Add to `Progress.cs`:

```csharp
        public override Vector2? GetNativeSize()
        {
            if (_frame != null && _frame.sprite != null) return NativeOf(_frame);
            if (_bg != null && _bg.sprite != null) return NativeOf(_bg);
            return new Vector2(160f, 16f);
        }

        private static Vector2 NativeOf(UnityImage img)
        {
            var ppu = img.pixelsPerUnit;
            return new Vector2(img.sprite.rect.width / ppu, img.sprite.rect.height / ppu);
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Expected: PASS — 32 total green.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Progress.cs Tests/EditMode/Controls/ProgressTests.cs
git commit -m "feat(progress): GetNativeSize fallback (frame > bg > 160x16)"
```

---

### Task 11: `ProgressAttributeRules` — 6 lint rules

**Files:**
- Create: `Runtime/Core/Lint/ProgressAttributeRules.cs`
- Create: `Tests/EditMode/Lint/ProgressAttributeRulesTests.cs`

Mirrors `MaskAttributeRules.cs`. Rules implemented in this order to make the test file easy to read.

- [ ] **Step 1: Write failing tests**

Create `Tests/EditMode/Lint/ProgressAttributeRulesTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class ProgressAttributeRulesTests
    {
        private static ElementNode N(params (string k, string v)[] attrs)
        {
            var n = new ElementNode("Progress") { Id = "p" };
            foreach (var (k, v) in attrs) n.Attributes[k] = v;
            return n;
        }

        [Test]
        public void Clean_Progress_No_Issues()
        {
            var n = N(("value", "0.5"), ("fill", "ui:bar"));
            Assert.IsEmpty(ProgressAttributeRules.CheckProgress(n));
        }

        // ===== value range =====

        [Test]
        public void Value_Below_Zero_ValueRange_Warning()
        {
            var n = N(("value", "-0.1"), ("fill", "ui:bar"));
            var issues = ProgressAttributeRules.CheckProgress(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ProgressAttributeRules.ValueRangeCode, issues[0].Code);
        }

        [Test]
        public void Value_Above_One_ValueRange_Warning()
        {
            var n = N(("value", "1.5"), ("fill", "ui:bar"));
            var issues = ProgressAttributeRules.CheckProgress(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ProgressAttributeRules.ValueRangeCode, issues[0].Code);
        }

        [Test]
        public void Value_Non_Numeric_No_ValueRange_Issue()
        {
            // Dynamic binding sources (e.g. "{state.hp}") parse as non-numeric and must
            // be ignored — lint can only judge literals.
            var n = N(("value", "{state.hp}"), ("fill", "ui:bar"));
            Assert.IsEmpty(ProgressAttributeRules.CheckProgress(n));
        }

        // ===== mode =====

        [Test]
        public void Mode_Bogus_ModeCode_Error()
        {
            var n = N(("mode", "radial"), ("fill", "ui:bar"));
            var issues = ProgressAttributeRules.CheckProgress(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ProgressAttributeRules.ModeCode, issues[0].Code);
        }

        [Test]
        public void Mode_Scale_And_Fill_Both_OK()
        {
            Assert.IsEmpty(ProgressAttributeRules.CheckProgress(N(("mode", "scale"), ("fill", "ui:bar"))));
            Assert.IsEmpty(ProgressAttributeRules.CheckProgress(N(("mode", "fill"), ("fill", "ui:bar"))));
        }

        // ===== direction =====

        [Test]
        public void Direction_Bogus_DirectionCode_Error()
        {
            var n = N(("direction", "diagonal"), ("fill", "ui:bar"));
            var issues = ProgressAttributeRules.CheckProgress(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ProgressAttributeRules.DirectionCode, issues[0].Code);
        }

        [Test]
        public void Direction_All_Four_Values_OK()
        {
            foreach (var d in new[] { "horizontal", "vertical", "reverse-horizontal", "reverse-vertical" })
                Assert.IsEmpty(ProgressAttributeRules.CheckProgress(N(("direction", d), ("fill", "ui:bar"))),
                    $"direction='{d}'");
        }

        // ===== children =====

        [Test]
        public void Children_ChildrenCode_Error()
        {
            var n = N(("fill", "ui:bar"));
            n.Children.Add(new ElementNode("Image"));
            var issues = ProgressAttributeRules.CheckProgress(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ProgressAttributeRules.ChildrenCode, issues[0].Code);
        }

        // ===== mask variant =====

        [Test]
        public void Mask_In_Variant_Override_MaskVariantCode_Error()
        {
            var n = N(("fill", "ui:bar"));
            n.VariantOverrides["mask"] =
                new List<(string Variant, string Value)> { ("mobile", "ui:pill") };
            var issues = ProgressAttributeRules.CheckProgress(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ProgressAttributeRules.MaskVariantCode, issues[0].Code);
        }

        // ===== no fill =====

        [Test]
        public void Value_Set_But_No_Fill_Or_FillColor_NoFillCode_Warning()
        {
            var n = N(("value", "0.5"));
            var issues = ProgressAttributeRules.CheckProgress(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ProgressAttributeRules.NoFillCode, issues[0].Code);
        }

        [Test]
        public void Value_Plus_Fill_No_NoFill_Issue()
        {
            Assert.IsEmpty(ProgressAttributeRules.CheckProgress(N(("value", "0.5"), ("fill", "ui:bar"))));
        }

        [Test]
        public void Value_Plus_FillColor_No_NoFill_Issue()
        {
            Assert.IsEmpty(ProgressAttributeRules.CheckProgress(N(("value", "0.5"), ("fillColor", "#f00"))));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compile error — type missing)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: compile errors referencing `ProgressAttributeRules`.

- [ ] **Step 3: Create ProgressAttributeRules.cs**

Create `Runtime/Core/Lint/ProgressAttributeRules.cs`:

```csharp
using System.Collections.Generic;
using System.Globalization;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Lint rules for the <Progress> control's attribute family.
    /// Consumed by both <c>IRWalker</c> (UIXmlLint CLI) and <c>ScreenInstantiator</c>
    /// (runtime warnings). Single source of truth — mirrors MaskAttributeRules.
    /// </summary>
    public static class ProgressAttributeRules
    {
        public const string ValueRangeCode  = "PUI-PROG-VALUE-RANGE";
        public const string ModeCode        = "PUI-PROG-MODE";
        public const string DirectionCode   = "PUI-PROG-DIRECTION";
        public const string ChildrenCode    = "PUI-PROG-CHILDREN";
        public const string MaskVariantCode = "PUI-PROG-MASK-VARIANT";
        public const string NoFillCode      = "PUI-PROG-NO-FILL";

        private static readonly HashSet<string> ValidModes = new() { "scale", "fill" };
        private static readonly HashSet<string> ValidDirections = new()
        {
            "horizontal", "vertical", "reverse-horizontal", "reverse-vertical"
        };

        public static IEnumerable<LintIssue> CheckProgress(ElementNode n)
        {
            // value range (literal only — dynamic bindings parse as non-numeric and skip)
            if (n.Attributes.TryGetValue("value", out var rawValue)
                && float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                && (v < 0f || v > 1f))
            {
                yield return new LintIssue(
                    ValueRangeCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: value='{rawValue}' is outside [0..1] and will be clamped. " +
                    "Adjust the literal, or ignore this if you bind value dynamically.");
            }

            // mode
            if (n.Attributes.TryGetValue("mode", out var mode) && !ValidModes.Contains(mode))
            {
                yield return new LintIssue(
                    ModeCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: mode='{mode}' is invalid. Valid: scale, fill.");
            }

            // direction
            if (n.Attributes.TryGetValue("direction", out var dir) && !ValidDirections.Contains(dir))
            {
                yield return new LintIssue(
                    DirectionCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: direction='{dir}' is invalid. " +
                    "Valid: horizontal, vertical, reverse-horizontal, reverse-vertical.");
            }

            // children
            if (n.Children.Count > 0)
            {
                yield return new LintIssue(
                    ChildrenCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: Progress is a leaf control and does not accept child elements. " +
                    "Use the frame / mask / bg / fill attributes to compose the visual layers.");
            }

            // mask variant override
            if (n.VariantOverrides.ContainsKey("mask"))
            {
                yield return new LintIssue(
                    MaskVariantCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: mask cannot be overridden per Variant (would require " +
                    "AddComponent/Destroy at runtime). Fix mask in the base declaration; other attrs " +
                    "(value / fill / bg / mode / direction) are safe in variants.");
            }

            // no fill (warning when value is set but no fill+fillColor)
            if (n.Attributes.ContainsKey("value")
                && !n.Attributes.ContainsKey("fill")
                && !n.Attributes.ContainsKey("fillColor"))
            {
                yield return new LintIssue(
                    NoFillCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: value is set but neither fill nor fillColor — " +
                    "nothing will be visibly filled.");
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ProgressAttributeRulesTests")
```

Expected: PASS — 13 tests green.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Lint/ProgressAttributeRules.cs Runtime/Core/Lint/ProgressAttributeRules.cs.meta Tests/EditMode/Lint/ProgressAttributeRulesTests.cs Tests/EditMode/Lint/ProgressAttributeRulesTests.cs.meta
git commit -m "feat(progress): lint rules (value-range / mode / direction / children / mask-variant / no-fill)"
```

---

### Task 12: Wire IRWalker + ScreenInstantiator dispatch

**Files:**
- Modify: `Runtime/Core/Lint/IRWalker.cs:38-43` (add Progress branch)
- Modify: `Runtime/Application/ScreenInstantiator.cs:186-190` (add Progress branch)
- Create: `Tests/EditMode/Lint/IRWalkerProgressTests.cs`

- [ ] **Step 1: Write failing tests**

Create `Tests/EditMode/Lint/IRWalkerProgressTests.cs`:

```csharp
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class IRWalkerProgressTests
    {
        private static UIDocument Parse(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            return UIDocumentParser.Parse(xml);
        }

        [Test]
        public void Walker_Dispatches_Progress_Children_Rule()
        {
            var doc = Parse("<Progress id='p' fill='ui:bar'><Image/></Progress>");
            var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
            CollectionAssert.Contains(codes, ProgressAttributeRules.ChildrenCode);
        }

        [Test]
        public void Walker_Dispatches_Progress_Mode_Rule()
        {
            var doc = Parse("<Progress id='p' mode='radial' fill='ui:bar'/>");
            var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
            CollectionAssert.Contains(codes, ProgressAttributeRules.ModeCode);
        }

        [Test]
        public void Walker_Clean_Progress_No_Issues()
        {
            var doc = Parse("<Progress id='p' value='0.5' fill='ui:bar'/>");
            Assert.IsEmpty(IRWalker.Walk(doc));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IRWalkerProgressTests")
```

Expected: FAIL — dispatch not wired, codes don't appear in walker output.

- [ ] **Step 3: Add dispatch branches**

In `Runtime/Core/Lint/IRWalker.cs:38-43`, extend the `if/else if` chain:

```csharp
            if (node.Tag == "Frame")
                foreach (var issue in MaskAttributeRules.CheckFrame(node))
                    yield return issue;
            else if (node.Tag == "Image")
                foreach (var issue in MaskAttributeRules.CheckImage(node))
                    yield return issue;
            else if (node.Tag == "Progress")
                foreach (var issue in ProgressAttributeRules.CheckProgress(node))
                    yield return issue;
```

In `Runtime/Application/ScreenInstantiator.cs:186-190`, add the matching branch (use the same `Debug.LogWarning(issue.Message)` pattern already used for Mask rules):

```csharp
            if (node.Tag == "Frame")
                foreach (var issue in MaskAttributeRules.CheckFrame(node))
                    Debug.LogWarning(issue.Message);
            else if (node.Tag == "Image")
                foreach (var issue in MaskAttributeRules.CheckImage(node))
                    Debug.LogWarning(issue.Message);
            else if (node.Tag == "Progress")
                foreach (var issue in ProgressAttributeRules.CheckProgress(node))
                    Debug.LogWarning(issue.Message);
```

- [ ] **Step 4: Run tests to verify they pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IRWalkerProgressTests")
```

Expected: PASS — 3 green. Also re-run `ProgressTests` and `ProgressAttributeRulesTests` to confirm no regression.

- [ ] **Step 5: Lint CLI smoke**

Create a temp file to confirm the CLI now reports Progress issues:

```bash
printf '%s\n' '<?xml version="1.0" encoding="utf-8"?>' \
              '<PromptUGUI version="1"><Screen name="S">' \
              '  <Progress id="p" mode="radial" fill="ui:bar"/>' \
              '</Screen></PromptUGUI>' > /tmp/progress-lint-smoke.ui.xml
dotnet run --project .lint/UIXmlLint -- /tmp/progress-lint-smoke.ui.xml; echo "exit=$?"
```

Expected: stdout contains `PUI-PROG-MODE`; `exit=1`.

- [ ] **Step 6: Commit**

```bash
git add Runtime/Core/Lint/IRWalker.cs Runtime/Application/ScreenInstantiator.cs Tests/EditMode/Lint/IRWalkerProgressTests.cs Tests/EditMode/Lint/IRWalkerProgressTests.cs.meta
git commit -m "feat(progress): wire IRWalker + ScreenInstantiator dispatch"
```

---

### Task 13: SKILL.md + main spec updates

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`

- [ ] **Step 1: Locate the built-in primitives table in the XML skill**

Run:

```bash
grep -n '<Slider>\|<Dropdown>\|Built-in primitives\|## Controls' .claude/skills/authoring-promptugui-xml/SKILL.md
```

Identify the row position for `<Progress>` (insert after `<Slider>` to match `BuiltinPrimitives.cs` ordering).

- [ ] **Step 2: Add `<Progress>` row to the built-ins table**

Insert a row after `<Slider>` describing:
- Tag: `<Progress>`
- Attrs: `value`, `fill`, `fillColor`, `bg`, `bgColor`, `frame`, `mask`, `mode` (`scale`/`fill`), `direction` (`horizontal`/`vertical`/`reverse-horizontal`/`reverse-vertical`)
- Purpose: 显示型线性进度条；frame / mask / bg 都可选

- [ ] **Step 3: Add a "Progress" section**

Insert a new section (after the existing Slider section if present, otherwise after the controls table) containing:

1. The 6 examples from spec §4 (verbatim from `docs~/superpowers/specs/2026-05-27-progress-control-design.md` §4).
2. The 4-row "mask × bg combination" table from spec §6.
3. The 6 lint codes from spec §8 with one-line summaries.
4. The boundary note: "radial fill is not supported by `<Progress>` — future `<Cooldown>` control".

- [ ] **Step 4: Update the C# skill**

In `.claude/skills/scripting-promptugui-csharp/SKILL.md`, find the `Get<T>()` / `IControl` section. Insert a paragraph:

> `screen.Get<Progress>("hp").Value = 0.42f;` — Progress 是显示型控件，无 `OnValueChanged`，用 `Bind`-属性或直接 setter 推值。`Value` 被 `Mathf.Clamp01` 钳位。

(If a `## 进度 / Progress` subsection doesn't exist, add a one-line entry under the existing controls list.)

- [ ] **Step 5: Update the main spec controls table**

In `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`, find the `<Slider>` row in §5 controls table and add a `<Progress>` row after it:

```markdown
| `<Progress>` | 线性进度条 (scale / Image.Type.Filled, horizontal / vertical, +可选 frame / mask / bg / fill 装饰) | RectTransform（+ 内部 4 个图层；详见 [`2026-05-27-progress-control-design.md`](2026-05-27-progress-control-design.md)） |
```

- [ ] **Step 6: Verify rendering / no broken links**

```bash
grep -n 'Progress' .claude/skills/authoring-promptugui-xml/SKILL.md .claude/skills/scripting-promptugui-csharp/SKILL.md
grep -n '2026-05-27-progress-control-design' docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md
```

Expected: all three files now reference `<Progress>` / the design doc.

- [ ] **Step 7: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md .claude/skills/scripting-promptugui-csharp/SKILL.md docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md
git commit -m "docs(progress): SKILL.md + main spec entries for <Progress>"
```

---

### Task 14: Lint + full-suite verification

**Files:** none modified — verification only.

- [ ] **Step 1: Run dotnet format checks**

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: exit 0 (no format violations).

If failures: fix in place, re-run.

- [ ] **Step 2: Run the full EditMode test assembly**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

Expected: all green. No regressions in pre-existing tests (especially `ImageMaskTests`, `FrameMaskTests`, `IRWalkerMaskTests` — they share the dispatch path we extended).

- [ ] **Step 3: Run UIXmlLint over the repo's example XML**

```bash
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/
```

Expected: exit 0 — no Progress lint issues introduced into shipped XML.

- [ ] **Step 4: Push the branch and open PR**

```bash
git push -u origin feat/progress-control
```

Open PR via `gh pr create` (title: `feat: <Progress> control (linear scale + Image.Type.Filled)`). Body should reference the spec at `docs~/superpowers/specs/2026-05-27-progress-control-design.md` and summarise the user-visible additions (1 new tag, 9 attrs, 6 lint codes).

---

## Self-Review

**Spec coverage** (cross-reference against `2026-05-27-progress-control-design.md`):

| Spec section | Task |
|---|---|
| §3 attribute table — value | T2 |
| §3 attribute table — fill / fillColor | T6 |
| §3 attribute table — bg / bgColor | T7 |
| §3 attribute table — frame | T8 |
| §3 attribute table — mask | T9 |
| §3 attribute table — mode | T5 |
| §3 attribute table — direction | T4, T5 |
| §6 fixed hierarchy + activation table | T1 (skeleton), T7 (bg activation), T8 (frame activation), T9 (mask AddComponent + showMaskGraphic) |
| §7.1 scale-mode anchor formulae | T3 (horizontal default), T4 (other directions) |
| §7.2 fill-mode fillMethod/Origin | T5 |
| §7.3 reconcile timing (OnAfterApply) | T3 |
| §8 lint rules (×6) | T11 (rules + tests), T12 (dispatch) |
| §9.1 Progress.cs骨架 | T1–T10 incrementally |
| §9.2 BuiltinPrimitives registration | T1 |
| §9.3 ProgressAttributeRules | T11 |
| §9.4 IRWalker dispatch | T12 |
| §9.5 ScreenInstantiator dispatch | T12 |
| §10 SKILL + main-spec sync | T13 |
| §11 out-of-scope (radial, tween, min/max, segmented) | comment in T1 source + no task added (correct) |
| §12 risks — reconcile resets both branches | T5 step 3 (scale branch resets fillAmount=1 + type away from Filled) |
| §12 risks — Mask setter `??=` survives variant override bypass | T9 step 3 (the `if (_maskGraphic == null)` gate) |
| §12 risks — frame raycast off | T1 OnAttached + T8 test |

All spec sections covered. PB-D2 (clamp NaN→0) is handled by `Mathf.Clamp01` (Unity's docs confirm); a test isn't added because NaN literals can't appear in XML — runtime callers writing `p.Value = float.NaN` get clamped to 0.

**Placeholder scan:** no `TBD` / `TODO` / `similar to Task N` / unspecified test bodies. Every code block is complete.

**Type consistency:**
- `Value` is `float` (T2, T11), matches `Mathf.Clamp01` signature.
- `Mode` / `Direction` / `Fill` / `FillColor` / `Bg` / `BgColor` / `Frame` / `Mask` all `string` setters, matches `Image.cs` / `Slider.cs` precedent (`[UIAttr]` requires string-typed setters for non-float / non-bool attrs).
- `ReconcileFill` private void — called from `OnAfterApply`. `AutoSlice` private static void — called from `OnAfterApply`. `NativeOf` private static `Vector2`.
- `ProgressAttributeRules.CheckProgress` returns `IEnumerable<LintIssue>` (matches `MaskAttributeRules.CheckFrame` / `CheckImage`).
- All 6 lint code constants (`ValueRangeCode` etc.) referenced in T11 tests match the constants defined in T11 step 3.

No inconsistencies found.

---

## Execution Handoff

Plan complete and saved to `docs~/superpowers/plans/2026-05-27-progress-control.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
