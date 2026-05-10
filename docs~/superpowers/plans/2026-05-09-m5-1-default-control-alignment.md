# M5.1 Default Control Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Align 7 existing PromptUGUI controls (Btn / Toggle / Slider / Dropdown / ScrollList / Image / Text) and add a new `<InputField>` primitive to match Unity 6's default UI prefabs (the `GameObject → UI → …` menu output) — at the level of color, font size, sub-node geometry, AND component types (Mask vs RectMask2D, missing Scrollbar children, etc.).

**Architecture:** Procedural construction stays inside `Runtime/Controls/*.cs`. The 8 default-color constants in `ProceduralBuilders` move from "dark theme" to Unity's "white sliced + #323232 text" theme. `pugui.png` atlas is reused (no new sprites). Toggle / Slider / Dropdown / ScrollList all gain prefab-faithful sub-node trees: stencil Mask viewports (alpha=1, no alpha-discard), Scrollbar children, item geometry. New `<InputField>` is the 13th built-in primitive (TMP_InputField + RectMask2D TextArea + Placeholder + Text). Public API surface is unchanged for existing controls; InputField adds a new class with `[UIAttr]` + R3 events.

**Tech Stack:** Unity 6 / uGUI / TextMeshPro / R3 (Cysharp) / NUnit (EditMode + PlayMode) / UnityMCP for live test feedback / dotnet format for lint.

**Spec:** `docs~/superpowers/specs/2026-05-09-m5-1-default-control-alignment-design.md`

---

## File Structure

| Status | Path | Responsibility |
|---|---|---|
| modify | `Runtime/Controls/Internal/ProceduralBuilders.cs` | Default color constants; `AddText` defaults; `ApplyDefaultSimpleSprite` signature |
| modify | `Runtime/Controls/Btn.cs` | Auto-label fontSize=24 |
| modify | `Runtime/Controls/Toggle.cs` | Root strips Image; child Background+Checkmark+Label tree |
| modify | `Runtime/Controls/Slider.cs` | Root strips Image; Y-inset Background; Fill Area / Handle Slide Area tree |
| modify | `Runtime/Controls/Dropdown.cs` | Arrow geometry; Viewport Mask (stencil); Item geometry; Scrollbar Vertical |
| modify | `Runtime/Controls/ScrollList.cs` | Viewport Mask (stencil); ScrollRect explicit params; direction-based Scrollbar |
| create | `Runtime/Controls/InputField.cs` | TMP_InputField primitive (new 13th built-in) |
| modify | `Runtime/Application/BuiltinPrimitives.cs` | Register `InputField` |
| modify | `Tests/EditMode/Controls/ToggleTests.cs` | + geometry / raycast assertions |
| modify | `Tests/EditMode/Controls/SliderTests.cs` | + geometry / handle simple-type assertions |
| modify | `Tests/EditMode/Controls/DropdownTests.cs` | + Arrow / Viewport Mask alpha=1 / Item geometry / Scrollbar assertions |
| modify | `Tests/EditMode/Controls/ScrollListTests.cs` | + Viewport Mask alpha=1 / direction-based Scrollbar assertions |
| modify | `Tests/EditMode/Controls/BtnTests.cs` | (create if missing) Btn label fontSize / colors |
| create | `Tests/EditMode/Controls/InputFieldTests.cs` | Full InputField behavior + structure |
| create | `Tests/PlayMode/InputFieldRuntimeTests.cs` | InputField key-input integration |
| modify | `.claude/skills/authoring-promptugui-xml/SKILL.md` | + InputField row, theme note, cheatsheet |
| modify | `Samples~/MainMenu/...` | Add InputField example screen |

---

## Workflow conventions (apply to every task)

1. **TDD order:** write red test → run via UnityMCP → confirm fail → implement → refresh → run → confirm pass → lint → commit
2. **UnityMCP:** if MCP unavailable, STOP and tell the user (per CLAUDE.md). Do not proceed with code-only verification.
3. **Lint:** after every `.cs` edit, run from repo root:
   ```bash
   cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
   dotnet format style PromptUGUI.Lint.slnx
   dotnet format analyzers PromptUGUI.Lint.slnx
   dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
   ```
   **NEVER** run `dotnet format analyzers --severity info` (per CLAUDE.md, will break compile).
4. **Refresh:** after every `.cs` write,
   ```
   mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
   mcp__UnityMCP__read_console(action="get", types=["error"])
   ```
   No console errors before running tests.
5. **Tests:**
   ```
   mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="<ClassName>")
   ```
6. **Commits:** one per task at end. Conventional commit message; reference spec §-numbers where useful.

---

## Task 0: Setup feature branch

**Files:** none (branch only).

- [ ] **Step 1: Create + check out feature branch off main**

```bash
git checkout main
git pull --ff-only origin main
git checkout -b feature/m5.1-default-controls-alignment
```

- [ ] **Step 2: Verify clean tree**

```bash
git status
```
Expected: `working tree clean`.

---

## Task 1: ProceduralBuilders palette migration

**Files:**
- Modify: `Runtime/Controls/Internal/ProceduralBuilders.cs`
- Test: `Tests/EditMode/Controls/ProceduralBuildersTests.cs` (create)

Three changes in one file:
1. 8 existing `Default*` constants change to Unity-default values
2. Add 2 new constants: `DefaultLabelColor`, `DefaultPlaceholderColor`
3. `AddText` applies `DefaultLabelColor` + `fontSize=14` by default
4. `ApplyDefaultSimpleSprite` adds optional `bool preserveAspect = false` parameter (default false; previously hardcoded true)

- [ ] **Step 1: Write failing test**

Create `Tests/EditMode/Controls/ProceduralBuildersTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ProceduralBuildersTests
    {
        [Test]
        public void Palette_BgColors_AreWhite()
        {
            Assert.AreEqual(Color.white, ProceduralBuilders.DefaultBtnColor);
            Assert.AreEqual(Color.white, ProceduralBuilders.DefaultControlBgColor);
            Assert.AreEqual(Color.white, ProceduralBuilders.DefaultTrackColor);
            Assert.AreEqual(Color.white, ProceduralBuilders.DefaultFillColor);
            Assert.AreEqual(Color.white, ProceduralBuilders.DefaultHandleColor);
            Assert.AreEqual(Color.white, ProceduralBuilders.DefaultPopupBgColor);
        }

        [Test]
        public void Palette_ContainerColor_IsTranslucentWhite()
        {
            var c = ProceduralBuilders.DefaultContainerColor;
            Assert.AreEqual(1f, c.r);
            Assert.AreEqual(1f, c.g);
            Assert.AreEqual(1f, c.b);
            Assert.That(c.a, Is.EqualTo(0.392f).Within(0.001f));
        }

        [Test]
        public void Palette_GlyphColor_IsDarkGrey()
        {
            var c = ProceduralBuilders.DefaultGlyphColor;
            Assert.That(c.r, Is.EqualTo(0.196f).Within(0.001f));
            Assert.That(c.g, Is.EqualTo(0.196f).Within(0.001f));
            Assert.That(c.b, Is.EqualTo(0.196f).Within(0.001f));
            Assert.AreEqual(1f, c.a);
        }

        [Test]
        public void Palette_LabelColor_IsDarkGrey()
        {
            var c = ProceduralBuilders.DefaultLabelColor;
            Assert.That(c.r, Is.EqualTo(0.196f).Within(0.001f));
            Assert.AreEqual(1f, c.a);
        }

        [Test]
        public void Palette_PlaceholderColor_IsDarkGreyHalfAlpha()
        {
            var c = ProceduralBuilders.DefaultPlaceholderColor;
            Assert.That(c.r, Is.EqualTo(0.196f).Within(0.001f));
            Assert.That(c.a, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void AddText_AppliesDefaultLabelColorAndFontSizeFourteen()
        {
            var go = new GameObject("Parent", typeof(RectTransform));
            try
            {
                var parent = (RectTransform)go.transform;
                var tmp = ProceduralBuilders.AddText(parent, "Label");
                Assert.AreEqual(ProceduralBuilders.DefaultLabelColor, tmp.color);
                Assert.AreEqual(14f, tmp.fontSize);
                Assert.AreEqual(TextAlignmentOptions.Center, tmp.alignment);
                Assert.IsFalse(tmp.raycastTarget);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ApplyDefaultSimpleSprite_DefaultsPreserveAspectFalse()
        {
            var go = new GameObject("Img", typeof(RectTransform));
            try
            {
                var img = go.AddComponent<UnityImage>();
                ProceduralBuilders.ApplyDefaultSimpleSprite(img, ProceduralBuilders.SpriteCheckmark);
                Assert.IsFalse(img.preserveAspect, "default preserveAspect should be false");
                Assert.AreEqual(UnityImage.Type.Simple, img.type);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ApplyDefaultSimpleSprite_OptInPreserveAspectTrue()
        {
            var go = new GameObject("Img", typeof(RectTransform));
            try
            {
                var img = go.AddComponent<UnityImage>();
                ProceduralBuilders.ApplyDefaultSimpleSprite(img, ProceduralBuilders.SpriteCheckmark, preserveAspect: true);
                Assert.IsTrue(img.preserveAspect);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
```

- [ ] **Step 2: Refresh + run tests; expect failure**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", filter="ProceduralBuildersTests")
```
Expected: tests fail (DefaultLabelColor / DefaultPlaceholderColor not found; old constants not white; AddText fontSize/color not set; ApplyDefaultSimpleSprite has no preserveAspect param).

- [ ] **Step 3: Edit `Runtime/Controls/Internal/ProceduralBuilders.cs`**

Replace the constants block (lines 12–20) with:

```csharp
// 默认配色对齐 Unity 6 标准控件（菜单 GameObject → UI → … 创建出来的 prefab）
// 全部白底 sliced + #323232 深字；sprite 由 atlas tint 表现明暗。
public static readonly Color DefaultBtnColor         = Color.white;
public static readonly Color DefaultControlBgColor   = Color.white;
public static readonly Color DefaultTrackColor       = Color.white;
public static readonly Color DefaultFillColor        = Color.white;
public static readonly Color DefaultHandleColor      = Color.white;
public static readonly Color DefaultPopupBgColor     = Color.white;
public static readonly Color DefaultContainerColor   = new(1f, 1f, 1f, 0.392f);
public static readonly Color DefaultGlyphColor       = new(0.196f, 0.196f, 0.196f, 1f);
public static readonly Color DefaultLabelColor       = new(0.196f, 0.196f, 0.196f, 1f);
public static readonly Color DefaultPlaceholderColor = new(0.196f, 0.196f, 0.196f, 0.5f);
```

Update `AddText`:

```csharp
public static TMP_Text AddText(RectTransform parent, string name)
{
    var rt = AddChild(parent, name);
    var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
    tmp.alignment = TextAlignmentOptions.Center;
    tmp.raycastTarget = false;
    tmp.color = DefaultLabelColor;
    tmp.fontSize = 14;
    return tmp;
}
```

Update `ApplyDefaultSimpleSprite` signature:

```csharp
public static void ApplyDefaultSimpleSprite(UnityImage img, string spriteName, bool preserveAspect = false)
{
    if (img == null || img.sprite != null) return;
    var s = GetDefaultSprite(spriteName);
    if (s == null) return;
    img.sprite = s;
    img.type = UnityImage.Type.Simple;
    img.preserveAspect = preserveAspect;
}
```

- [ ] **Step 4: Refresh + run tests; expect pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", filter="ProceduralBuildersTests")
```
Expected: 7 PASS.

- [ ] **Step 5: Run full EditMode suite; expect no regressions**

```
mcp__UnityMCP__run_tests(mode="EditMode")
```
Expected: all green (existing control tests don't assert colors, so the palette change is invisible to them).

- [ ] **Step 6: Lint**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format style PromptUGUI.Lint.slnx
dotnet format analyzers PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

- [ ] **Step 7: Commit**

```bash
git add Runtime/Controls/Internal/ProceduralBuilders.cs Tests/EditMode/Controls/ProceduralBuildersTests.cs
git commit -m "$(cat <<'EOF'
feat(controls): default palette migration to Unity-style light theme (M5.1 §3)

8 Default* color constants切到白底 + #323232 字 (跟 Unity 6 GameObject → UI 默认 prefab 一致)。
新增 DefaultLabelColor / DefaultPlaceholderColor。
AddText 默认应用 DefaultLabelColor + fontSize=14。
ApplyDefaultSimpleSprite 加 preserveAspect 可选参数 (默认 false，对齐 prefab)。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Btn label fontSize=24

**Files:**
- Modify: `Runtime/Controls/Btn.cs`
- Test: `Tests/EditMode/Controls/BtnTests.cs` (create new file)

Spec §4: Btn auto-label needs explicit `fontSize = 24` (overrides AddText default of 14, matches default Button prefab's "Text (TMP)" child).

- [ ] **Step 1: Write failing test**

Create `Tests/EditMode/Controls/BtnTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class BtnTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Visual_BgColorIsWhiteByDefault()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b'>Hi</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var img = btn.GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(Color.white, img.color);
        }

        [Test]
        public void Visual_LabelFontSizeIsTwentyFour()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b'>Hi</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var label = btn.GameObject.GetComponentInChildren<TMP_Text>();
            Assert.IsNotNull(label, "Btn auto-label should exist");
            Assert.AreEqual(24f, label.fontSize);
        }

        [Test]
        public void Visual_LabelColorIsDarkGrey()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b'>Hi</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var label = btn.GameObject.GetComponentInChildren<TMP_Text>();
            Assert.AreEqual(ProceduralBuilders.DefaultLabelColor, label.color);
        }
    }
}
```

- [ ] **Step 2: Refresh + run; expect first 2 tests pass (Color.white from Task 1, label color from AddText), `Visual_LabelFontSizeIsTwentyFour` fails**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__run_tests(mode="EditMode", filter="BtnTests")
```
Expected: `Visual_LabelFontSizeIsTwentyFour` FAIL (fontSize is 14 from AddText default).

- [ ] **Step 3: Edit `Runtime/Controls/Btn.cs` `EnsureLabel()` method**

Inside `EnsureLabel()`, after `_autoLabel = go.AddComponent<TextMeshProUGUI>();` and the existing `alignment` / `raycastTarget` lines, add:

```csharp
_autoLabel.fontSize = 24;  // 默认 prefab Button label 字号；color 由 AddText 默认提供 (DefaultLabelColor)
```

Note: don't touch the existing `_autoLabel.alignment = TextAlignmentOptions.Center;` etc.

- [ ] **Step 4: Refresh + run; expect pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__run_tests(mode="EditMode", filter="BtnTests")
```
Expected: 3 PASS.

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
cd ..
git add Runtime/Controls/Btn.cs Tests/EditMode/Controls/BtnTests.cs
git commit -m "$(cat <<'EOF'
feat(Btn): auto-label fontSize=24 to match Unity default Button prefab (M5.1 §4)

EnsureLabel() 现在显式设 fontSize=24 (AddText 默认 14 是 Toggle/Dropdown 字号)。
+ BtnTests visual 三项基线断言。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Toggle structural rebuild

**Files:**
- Modify: `Runtime/Controls/Toggle.cs`
- Modify: `Tests/EditMode/Controls/ToggleTests.cs`

Spec §5 + §2.5 Toggle row. Big change: root strips its Image; new child `Background` (20x20 top-left) holds the Image; `Checkmark` reparents under Background; `Label` becomes a separate stretched child with raycastTarget=true.

- [ ] **Step 1: Write failing tests (append to ToggleTests.cs)**

Add inside `ToggleTests` class:

```csharp
[Test]
public void Geometry_RootHasNoImage()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Toggle id='t'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var t = UI.Open("S").Get<Toggle>("t");
    Assert.IsNull(t.GameObject.GetComponent<UnityEngine.UI.Image>(),
        "root Toggle should have no Image (default prefab parity)");
}

[Test]
public void Geometry_BackgroundIsTwentyByTwentyTopLeft()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Toggle id='t'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var t = UI.Open("S").Get<Toggle>("t");
    var bg = t.GameObject.transform.Find("Background") as RectTransform;
    Assert.IsNotNull(bg, "Background child must exist");
    Assert.AreEqual(new Vector2(0, 1), bg.anchorMin);
    Assert.AreEqual(new Vector2(0, 1), bg.anchorMax);
    Assert.AreEqual(new Vector2(20, 20), bg.sizeDelta);
    Assert.AreEqual(new Vector2(10, -10), bg.anchoredPosition);
    Assert.IsNotNull(bg.GetComponent<UnityEngine.UI.Image>());
}

[Test]
public void Geometry_CheckmarkIsChildOfBackgroundAndCentered()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Toggle id='t'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var t = UI.Open("S").Get<Toggle>("t");
    var bg = t.GameObject.transform.Find("Background");
    var checkmark = bg.Find("Checkmark") as RectTransform;
    Assert.IsNotNull(checkmark, "Checkmark must be child of Background");
    Assert.AreEqual(new Vector2(0.5f, 0.5f), checkmark.anchorMin);
    Assert.AreEqual(new Vector2(0.5f, 0.5f), checkmark.anchorMax);
    Assert.AreEqual(new Vector2(20, 20), checkmark.sizeDelta);
    Assert.AreEqual(Vector2.zero, checkmark.anchoredPosition);
}

[Test]
public void Geometry_LabelStretchesRightOfBackground()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Toggle id='t'>X</Toggle></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var t = UI.Open("S").Get<Toggle>("t");
    var label = t.GameObject.transform.Find("Label") as RectTransform;
    Assert.IsNotNull(label);
    Assert.AreEqual(new Vector2(0, 0), label.anchorMin);
    Assert.AreEqual(new Vector2(1, 1), label.anchorMax);
    Assert.AreEqual(new Vector2(9, 0), label.offsetMin);
    Assert.AreEqual(new Vector2(-28, 0), label.offsetMax);
}

[Test]
public void Visual_LabelRaycastTargetTrue()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Toggle id='t'>X</Toggle></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var t = UI.Open("S").Get<Toggle>("t");
    var labelGo = t.GameObject.transform.Find("Label").gameObject;
    var tmp = labelGo.GetComponent<TMPro.TMP_Text>();
    Assert.IsTrue(tmp.raycastTarget,
        "Toggle label must be raycast target so clicks register on the right side of the toggle (default prefab behavior)");
}
```

- [ ] **Step 2: Refresh + run; expect new 5 tests fail, existing 4 pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__run_tests(mode="EditMode", filter="ToggleTests")
```
Expected: existing tests pass; new geometry tests fail.

- [ ] **Step 3: Rewrite `Toggle.OnAttached()` in `Runtime/Controls/Toggle.cs`**

Replace the existing `OnAttached()` method:

```csharp
public override void OnAttached()
{
    _toggle = GameObject.GetComponent<UnityToggle>() ?? GameObject.AddComponent<UnityToggle>();

    // Background：左上角 20x20 box（默认 prefab Toggle 的 Background 节点）
    var bgRt = ProceduralBuilders.AddChild(RectTransform, "Background");
    bgRt.anchorMin = new Vector2(0f, 1f);
    bgRt.anchorMax = new Vector2(0f, 1f);
    bgRt.pivot = new Vector2(0.5f, 0.5f);
    bgRt.sizeDelta = new Vector2(20f, 20f);
    bgRt.anchoredPosition = new Vector2(10f, -10f);
    _bg = bgRt.gameObject.AddComponent<UnityImage>();
    _bg.color = ProceduralBuilders.DefaultControlBgColor;
    ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);
    _toggle.targetGraphic = _bg;

    // Checkmark：放在 Background 内部，居中 20x20 simple sprite
    _checkmark = ProceduralBuilders.AddImage(bgRt, "Checkmark", raycast: false);
    _checkmark.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
    _checkmark.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
    _checkmark.rectTransform.sizeDelta = new Vector2(20f, 20f);
    _checkmark.rectTransform.anchoredPosition = Vector2.zero;
    _checkmark.color = ProceduralBuilders.DefaultGlyphColor;
    ProceduralBuilders.ApplyDefaultSimpleSprite(_checkmark, ProceduralBuilders.SpriteCheckmark);
    _toggle.graphic = _checkmark;

    // Label：右侧水平 stretch；raycastTarget=true 让整条 toggle 都能点击
    _label = ProceduralBuilders.AddText(RectTransform, "Label");
    _label.alignment = TextAlignmentOptions.Left;
    _label.raycastTarget = true;
    var labelRt = _label.rectTransform;
    labelRt.anchorMin = new Vector2(0f, 0f);
    labelRt.anchorMax = new Vector2(1f, 1f);
    labelRt.pivot = new Vector2(0.5f, 0.5f);
    labelRt.offsetMin = new Vector2(9f, 0f);
    labelRt.offsetMax = new Vector2(-28f, 0f);

    ApplyFont();

    _toggle.onValueChanged.AddListener(v => _changed.OnNext(v));
    PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
}
```

- [ ] **Step 4: Refresh + run; expect all 9 ToggleTests pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", filter="ToggleTests")
```
Expected: 9 PASS.

- [ ] **Step 5: Run full suite; check for regressions**

```
mcp__UnityMCP__run_tests(mode="EditMode")
```

- [ ] **Step 6: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
cd ..
git add Runtime/Controls/Toggle.cs Tests/EditMode/Controls/ToggleTests.cs
git commit -m "$(cat <<'EOF'
feat(Toggle): rebuild structure to match Unity default Toggle prefab (M5.1 §5)

Root 不再有 Image；新增 Background 子节点 (左上 20x20 box) 持 Image；
Checkmark 重新挂到 Background 下，居中 20x20；Label 独立子节点
水平 stretch (offset 9,-28)，raycastTarget=true 让整条 toggle 可点击。

测试加：root 无 Image / Background 几何 / Checkmark 父级与位置 / Label
stretch 几何 / Label raycastTarget=true。

破坏面：旧 Toggle 视觉是 checkmark 占满整框；新视觉是左上小方框 + 右侧文字。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Slider structural rebuild

**Files:**
- Modify: `Runtime/Controls/Slider.cs`
- Modify: `Tests/EditMode/Controls/SliderTests.cs`

Spec §6 + §2.5 Slider row. Root strips Image; Background Y-inset (anchorMin=(0,0.25), anchorMax=(1,0.75)); Fill Area inset; Handle becomes simple sprite (not sliced) with preserveAspect=false; child names match prefab ("Fill Area" / "Handle Slide Area" two-word).

- [ ] **Step 1: Write failing tests (append to SliderTests.cs)**

Add inside `SliderTests` class:

```csharp
[Test]
public void Geometry_RootHasNoImage()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Slider id='s'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var s = UI.Open("S").Get<Slider>("s");
    Assert.IsNull(s.GameObject.GetComponent<UnityEngine.UI.Image>());
}

[Test]
public void Geometry_BackgroundYInset()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Slider id='s'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var s = UI.Open("S").Get<Slider>("s");
    var bg = s.GameObject.transform.Find("Background") as RectTransform;
    Assert.IsNotNull(bg);
    Assert.AreEqual(new Vector2(0, 0.25f), bg.anchorMin);
    Assert.AreEqual(new Vector2(1, 0.75f), bg.anchorMax);
}

[Test]
public void Geometry_FillAreaInset()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Slider id='s'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var s = UI.Open("S").Get<Slider>("s");
    var fa = s.GameObject.transform.Find("Fill Area") as RectTransform;
    Assert.IsNotNull(fa, "Fill Area child (two words, matches prefab)");
    Assert.AreEqual(new Vector2(0, 0.25f), fa.anchorMin);
    Assert.AreEqual(new Vector2(1, 0.75f), fa.anchorMax);
    Assert.AreEqual(new Vector2(-5, 0), fa.anchoredPosition);
    Assert.AreEqual(new Vector2(-20, 0), fa.sizeDelta);
}

[Test]
public void Geometry_FillSizeDelta()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Slider id='s'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var s = UI.Open("S").Get<Slider>("s");
    var fill = s.GameObject.transform.Find("Fill Area/Fill") as RectTransform;
    Assert.IsNotNull(fill);
    Assert.AreEqual(new Vector2(0, 0), fill.anchorMin);
    Assert.AreEqual(new Vector2(0, 0), fill.anchorMax);
    Assert.AreEqual(new Vector2(10, 0), fill.sizeDelta);
}

[Test]
public void Geometry_HandleSlideArea()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Slider id='s'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var s = UI.Open("S").Get<Slider>("s");
    var hsa = s.GameObject.transform.Find("Handle Slide Area") as RectTransform;
    Assert.IsNotNull(hsa, "Handle Slide Area child (three words, matches prefab)");
    Assert.AreEqual(new Vector2(0, 0), hsa.anchorMin);
    Assert.AreEqual(new Vector2(1, 1), hsa.anchorMax);
    Assert.AreEqual(new Vector2(-20, 0), hsa.sizeDelta);
}

[Test]
public void Geometry_HandleIsSimpleNotPreserveAspect()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Slider id='s'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var s = UI.Open("S").Get<Slider>("s");
    var handle = s.GameObject.transform.Find("Handle Slide Area/Handle").GetComponent<UnityEngine.UI.Image>();
    Assert.AreEqual(UnityEngine.UI.Image.Type.Simple, handle.type);
    Assert.IsFalse(handle.preserveAspect);
}
```

- [ ] **Step 2: Refresh + run; expect new 6 tests fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__run_tests(mode="EditMode", filter="SliderTests")
```

- [ ] **Step 3: Rewrite `Slider.OnAttached()` in `Runtime/Controls/Slider.cs`**

Replace existing `OnAttached()`:

```csharp
public override void OnAttached()
{
    // Background：竖向内缩到中间 50% (Y 0.25 — 0.75) sliced 轨道
    var bgRt = ProceduralBuilders.AddChild(RectTransform, "Background");
    bgRt.anchorMin = new Vector2(0f, 0.25f);
    bgRt.anchorMax = new Vector2(1f, 0.75f);
    bgRt.offsetMin = Vector2.zero;
    bgRt.offsetMax = Vector2.zero;
    _bg = bgRt.gameObject.AddComponent<UnityImage>();
    _bg.color = ProceduralBuilders.DefaultTrackColor;
    ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);

    // Fill Area：跟 Background 同样 Y 内缩，X 两侧各留 10px (handle 半径)
    var fillArea = ProceduralBuilders.AddChild(RectTransform, "Fill Area");
    fillArea.anchorMin = new Vector2(0f, 0.25f);
    fillArea.anchorMax = new Vector2(1f, 0.75f);
    fillArea.anchoredPosition = new Vector2(-5f, 0f);
    fillArea.sizeDelta = new Vector2(-20f, 0f);
    _fill = ProceduralBuilders.AddImage(fillArea, "Fill", raycast: false);
    var fillRt = _fill.rectTransform;
    fillRt.anchorMin = Vector2.zero;
    fillRt.anchorMax = Vector2.zero;
    fillRt.sizeDelta = new Vector2(10f, 0f);
    _fill.color = ProceduralBuilders.DefaultFillColor;
    ProceduralBuilders.ApplyDefaultSlicedSprite(_fill);

    // Handle Slide Area：水平 stretch，左右各留 10px
    var handleArea = ProceduralBuilders.AddChild(RectTransform, "Handle Slide Area");
    handleArea.anchorMin = Vector2.zero;
    handleArea.anchorMax = Vector2.one;
    handleArea.sizeDelta = new Vector2(-20f, 0f);
    handleArea.anchoredPosition = Vector2.zero;
    _handle = ProceduralBuilders.AddImage(handleArea, "Handle", raycast: false);
    var handleRt = _handle.rectTransform;
    handleRt.anchorMin = Vector2.zero;
    handleRt.anchorMax = Vector2.zero;
    handleRt.sizeDelta = new Vector2(20f, 0f);
    _handle.color = ProceduralBuilders.DefaultHandleColor;
    // Handle 用 simple type；preserveAspect=false（与默认 Knob 一致）
    ProceduralBuilders.ApplyDefaultSimpleSprite(_handle, ProceduralBuilders.SpriteRoundedRect);

    _slider = GameObject.GetComponent<UnitySlider>() ?? GameObject.AddComponent<UnitySlider>();
    _slider.targetGraphic = _handle;
    _slider.fillRect = _fill.rectTransform;
    _slider.handleRect = _handle.rectTransform;
    _slider.direction = UnitySlider.Direction.LeftToRight;

    _slider.onValueChanged.AddListener(v => _changed.OnNext(v));
}
```

- [ ] **Step 4: Refresh + run; expect all SliderTests pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", filter="SliderTests")
```

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
cd ..
git add Runtime/Controls/Slider.cs Tests/EditMode/Controls/SliderTests.cs
git commit -m "$(cat <<'EOF'
feat(Slider): rebuild structure to match Unity default Slider prefab (M5.1 §6)

Root 不再有 Image；Background 子节点 Y 内缩到 0.25-0.75；Fill Area / Handle
Slide Area 三词命名跟 prefab 对齐；Handle 改 simple sprite, preserveAspect=false。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Dropdown Arrow geometry

**Files:**
- Modify: `Runtime/Controls/Dropdown.cs`
- Modify: `Tests/EditMode/Controls/DropdownTests.cs`

Spec §7.1. Arrow size 14x10 → 20x20; anchoredPosition (-12,0) → (-15,0).

- [ ] **Step 1: Write failing tests (append to DropdownTests.cs)**

```csharp
[Test]
public void Geometry_ArrowSizeIsTwentyTwenty()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var d = UI.Open("S").Get<Dropdown>("d");
    var arrow = d.GameObject.transform.Find("Arrow") as RectTransform;
    Assert.IsNotNull(arrow);
    Assert.AreEqual(new Vector2(20, 20), arrow.sizeDelta);
}

[Test]
public void Geometry_ArrowAnchoredPositionMinusFifteen()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var d = UI.Open("S").Get<Dropdown>("d");
    var arrow = d.GameObject.transform.Find("Arrow") as RectTransform;
    Assert.AreEqual(new Vector2(-15, 0), arrow.anchoredPosition);
}
```

- [ ] **Step 2: Refresh + run; expect 2 fails**

```
mcp__UnityMCP__run_tests(mode="EditMode", filter="DropdownTests")
```

- [ ] **Step 3: Edit `Runtime/Controls/Dropdown.cs`**

Find the Arrow setup block (around line 41-46) and update:

```diff
-arrow.rectTransform.sizeDelta = new Vector2(14f, 10f);
-arrow.rectTransform.anchoredPosition = new Vector2(-12f, 0f);
+arrow.rectTransform.sizeDelta = new Vector2(20f, 20f);
+arrow.rectTransform.anchoredPosition = new Vector2(-15f, 0f);
```

- [ ] **Step 4: Refresh + run; expect pass**

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
cd ..
git add Runtime/Controls/Dropdown.cs Tests/EditMode/Controls/DropdownTests.cs
git commit -m "fix(Dropdown): Arrow geometry 20x20 @ pos -15 to match prefab (M5.1 §7.1)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Dropdown Viewport stencil Mask switch

**Files:**
- Modify: `Runtime/Controls/Dropdown.cs`
- Modify: `Tests/EditMode/Controls/DropdownTests.cs`

Spec §7.2 + risk §14.3 (alpha-discard guard). Viewport switches from `RectMask2D` → `Mask` + `Image` (alpha=1, sliced sprite, `showMaskGraphic=false`). sizeDelta.x = -18 reserves space for Scrollbar.

- [ ] **Step 1: Write failing tests (append to DropdownTests.cs)**

```csharp
[Test]
public void Viewport_HasStencilMaskAndImageWithAlphaOne()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var d = UI.Open("S").Get<Dropdown>("d");
    var viewport = d.GameObject.GetComponentInChildren<UnityEngine.UI.Mask>(includeInactive: true);
    Assert.IsNotNull(viewport, "Viewport should use stencil Mask (default prefab parity)");
    Assert.IsFalse(viewport.showMaskGraphic, "Mask graphic must be hidden");

    var img = viewport.GetComponent<UnityEngine.UI.Image>();
    Assert.IsNotNull(img, "Mask requires Image graphic on same GO");
    Assert.AreEqual(1f, img.color.a, "alpha=1 critical to avoid 4af322b alpha-discard regression");
}

[Test]
public void Viewport_HasNoRectMask2D()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var d = UI.Open("S").Get<Dropdown>("d");
    var rm2d = d.GameObject.GetComponentInChildren<UnityEngine.UI.RectMask2D>(includeInactive: true);
    Assert.IsNull(rm2d, "RectMask2D should be replaced by stencil Mask");
}

[Test]
public void Viewport_SizeDeltaXMinusEighteen()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var d = UI.Open("S").Get<Dropdown>("d");
    // Template > Viewport
    var viewport = d.GameObject.transform.Find("Template/Viewport") as RectTransform;
    Assert.IsNotNull(viewport);
    Assert.AreEqual(-18f, viewport.sizeDelta.x, "viewport sizeDelta.x = -18 reserves 18px for Vertical Scrollbar");
}
```

- [ ] **Step 2: Refresh + run; expect 3 fails**

- [ ] **Step 3: Edit `Runtime/Controls/Dropdown.cs` Viewport block**

Find the existing Viewport setup (around lines 60-69) and replace:

```diff
-// Viewport (full-fills the template; clip items via RectMask2D — scissor-rect, no stencil/graphic).
+// Viewport: stencil Mask + sliced Image (alpha=1, showMaskGraphic=false) ── 跟默认 prefab 一致。
+// CRITICAL: alpha 必须为 1。alpha=0.01 会触发 UI/Default shader 的 alpha-discard，
+// 把 stencil 写飞 (4af322b 之前的 bug)。
 var viewport = ProceduralBuilders.AddChild(template, "Viewport");
 viewport.anchorMin = new Vector2(0f, 0f);
 viewport.anchorMax = new Vector2(1f, 1f);
 viewport.pivot = new Vector2(0f, 1f);
 viewport.offsetMin = Vector2.zero;
 viewport.offsetMax = Vector2.zero;
-viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
+viewport.sizeDelta = new Vector2(-18f, 0f);  // 留 18px 给 Vertical Scrollbar (Task 8 加)
+var viewportImg = viewport.gameObject.AddComponent<UnityImage>();
+viewportImg.color = Color.white;  // alpha=1 关键
+ProceduralBuilders.ApplyDefaultSlicedSprite(viewportImg);
+var viewportMask = viewport.gameObject.AddComponent<UnityEngine.UI.Mask>();
+viewportMask.showMaskGraphic = false;
```

- [ ] **Step 4: Refresh + run; expect pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", filter="DropdownTests")
```

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
cd ..
git add Runtime/Controls/Dropdown.cs Tests/EditMode/Controls/DropdownTests.cs
git commit -m "$(cat <<'EOF'
feat(Dropdown): Viewport 切回 stencil Mask + alpha=1 (M5.1 §7.2)

跟默认 prefab 对齐。alpha=1 + showMaskGraphic=false 视觉等价于"看不见的 mask"，
不会触发 4af322b 的 UI/Default shader alpha-discard。
sizeDelta.x=-18 给 Task 8 的 Vertical Scrollbar 留位。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Dropdown Item geometry alignment

**Files:**
- Modify: `Runtime/Controls/Dropdown.cs`
- Modify: `Tests/EditMode/Controls/DropdownTests.cs`

Spec §7.3. Item height 48 → 20; Item Background changes to a separate child (simple, no sprite, color=#F5F5F5); Item Checkmark 14x14@(12,0) → 20x20@(10,0); Item Label offsetMin (24,0) → (20,1.5), offsetMax (-10,0) → (-10,-1.5).

- [ ] **Step 1: Write failing tests (append to DropdownTests.cs)**

```csharp
[Test]
public void Geometry_ItemHeightIsTwenty()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var d = UI.Open("S").Get<Dropdown>("d");
    var item = d.GameObject.transform.Find("Template/Viewport/Content/Item") as RectTransform;
    Assert.IsNotNull(item);
    Assert.AreEqual(20f, item.sizeDelta.y);
}

[Test]
public void Geometry_ItemBackgroundIsSimpleHighlightedF5()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var d = UI.Open("S").Get<Dropdown>("d");
    var bgRt = d.GameObject.transform.Find("Template/Viewport/Content/Item/Item Background") as RectTransform;
    Assert.IsNotNull(bgRt, "Item Background must be a child node (default prefab parity)");
    var bg = bgRt.GetComponent<UnityEngine.UI.Image>();
    Assert.AreEqual(UnityEngine.UI.Image.Type.Simple, bg.type);
    Assert.IsNull(bg.sprite, "Item Background uses no sprite (highlighted color band only)");
    Assert.That(bg.color.r, Is.EqualTo(0.961f).Within(0.005f));
    Assert.That(bg.color.g, Is.EqualTo(0.961f).Within(0.005f));
    Assert.AreEqual(1f, bg.color.a);
}

[Test]
public void Geometry_ItemCheckmarkSizeAndPos()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var d = UI.Open("S").Get<Dropdown>("d");
    var ck = d.GameObject.transform.Find("Template/Viewport/Content/Item/Item Checkmark") as RectTransform;
    Assert.IsNotNull(ck);
    Assert.AreEqual(new Vector2(20, 20), ck.sizeDelta);
    Assert.AreEqual(new Vector2(10, 0), ck.anchoredPosition);
}

[Test]
public void Geometry_ItemLabelOffset()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var d = UI.Open("S").Get<Dropdown>("d");
    var lbl = d.GameObject.transform.Find("Template/Viewport/Content/Item/Item Label") as RectTransform;
    Assert.IsNotNull(lbl);
    Assert.AreEqual(new Vector2(20, 1.5f), lbl.offsetMin);
    Assert.AreEqual(new Vector2(-10, -1.5f), lbl.offsetMax);
}
```

- [ ] **Step 2: Refresh + run; expect 4 fails**

- [ ] **Step 3: Edit `Runtime/Controls/Dropdown.cs` Item block**

Find the Item construction block. Replace:

```diff
-// Item template (cloned per option; fixed height + horizontal stretch).
-const float itemHeight = 48f;
+// Item: 默认 prefab height=20，Item Background 用独立子节点 simple+#F5F5F5
+const float itemHeight = 20f;
 var item = ProceduralBuilders.AddChild(content, "Item");
 item.anchorMin = new Vector2(0f, 0.5f);
 item.anchorMax = new Vector2(1f, 0.5f);
 item.pivot = new Vector2(0.5f, 0.5f);
 item.sizeDelta = new Vector2(0f, itemHeight);

-var itemBg = item.gameObject.AddComponent<UnityImage>();
-itemBg.color = ProceduralBuilders.DefaultControlBgColor;
-ProceduralBuilders.ApplyDefaultSlicedSprite(itemBg);
+// Item Background: 独立子节点，simple + 无 sprite + #F5F5F5 (highlighted-tinted 色带)
+var itemBgRt = ProceduralBuilders.AddChild(item, "Item Background");
+var itemBg = itemBgRt.gameObject.AddComponent<UnityImage>();
+itemBg.type = UnityImage.Type.Simple;
+itemBg.sprite = null;
+itemBg.color = new Color(0.961f, 0.961f, 0.961f, 1f);
 var itemToggle = item.gameObject.AddComponent<UnityEngine.UI.Toggle>();
 itemToggle.targetGraphic = itemBg;

-// Item checkmark anchored on the left side of the item.
 var itemCheckmark = ProceduralBuilders.AddImage(item, "Item Checkmark", raycast: false);
 itemCheckmark.color = ProceduralBuilders.DefaultGlyphColor;
 ProceduralBuilders.ApplyDefaultSimpleSprite(itemCheckmark, ProceduralBuilders.SpriteCheckmark);
 itemCheckmark.rectTransform.anchorMin = new Vector2(0f, 0.5f);
 itemCheckmark.rectTransform.anchorMax = new Vector2(0f, 0.5f);
 itemCheckmark.rectTransform.pivot = new Vector2(0.5f, 0.5f);
-itemCheckmark.rectTransform.sizeDelta = new Vector2(14f, 14f);
-itemCheckmark.rectTransform.anchoredPosition = new Vector2(12f, 0f);
+itemCheckmark.rectTransform.sizeDelta = new Vector2(20f, 20f);
+itemCheckmark.rectTransform.anchoredPosition = new Vector2(10f, 0f);
 itemToggle.graphic = itemCheckmark;

-// Item label fills the rest of the item.
 var itemLabel = ProceduralBuilders.AddText(item, "Item Label");
 itemLabel.alignment = TextAlignmentOptions.Left;
 itemLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
 itemLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
-itemLabel.rectTransform.offsetMin = new Vector2(24f, 0f);
-itemLabel.rectTransform.offsetMax = new Vector2(-10f, 0f);
+itemLabel.rectTransform.offsetMin = new Vector2(20f, 1.5f);
+itemLabel.rectTransform.offsetMax = new Vector2(-10f, -1.5f);
```

- [ ] **Step 4: Refresh + run; expect pass**

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
cd ..
git add Runtime/Controls/Dropdown.cs Tests/EditMode/Controls/DropdownTests.cs
git commit -m "feat(Dropdown): Item geometry 对齐 prefab (M5.1 §7.3)

Item height 20；Item Background 独立子节点 simple+#F5F5F5；Checkmark 20x20@(10,0)；
Item Label offset (20,1.5)/(-10,-1.5)。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: Dropdown Scrollbar Vertical addition

**Files:**
- Modify: `Runtime/Controls/Dropdown.cs`
- Modify: `Tests/EditMode/Controls/DropdownTests.cs`

Spec §7.4. Add `Scrollbar` child under Template; wire `templateScroll.verticalScrollbar` and `_tmp.template/itemText` references.

- [ ] **Step 1: Write failing tests (append to DropdownTests.cs)**

```csharp
[Test]
public void Has_ScrollbarChildWithCorrectGeometry()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var d = UI.Open("S").Get<Dropdown>("d");
    var sb = d.GameObject.transform.Find("Template/Scrollbar") as RectTransform;
    Assert.IsNotNull(sb, "Template/Scrollbar must exist (default prefab parity)");
    Assert.AreEqual(new Vector2(1, 0), sb.anchorMin);
    Assert.AreEqual(new Vector2(1, 1), sb.anchorMax);
    Assert.AreEqual(new Vector2(20, 0), sb.sizeDelta);

    var scrollbar = sb.GetComponent<UnityEngine.UI.Scrollbar>();
    Assert.IsNotNull(scrollbar);
    Assert.AreEqual(UnityEngine.UI.Scrollbar.Direction.BottomToTop, scrollbar.direction);
}

[Test]
public void Wired_VerticalScrollbarOnScrollRect()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var d = UI.Open("S").Get<Dropdown>("d");
    var template = d.GameObject.transform.Find("Template");
    var scrollRect = template.GetComponent<UnityEngine.UI.ScrollRect>();
    Assert.IsNotNull(scrollRect.verticalScrollbar);
    Assert.AreEqual(UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide,
        scrollRect.verticalScrollbarVisibility);
    Assert.AreEqual(-3f, scrollRect.verticalScrollbarSpacing);
}
```

- [ ] **Step 2: Refresh + run; expect 2 fails**

- [ ] **Step 3: Edit `Runtime/Controls/Dropdown.cs`**

Insert this block AFTER the Item construction (after `itemToggle.graphic = itemCheckmark;` block, before `templateScroll.viewport = viewport;`):

```csharp
// Scrollbar Vertical (default prefab 在 Template 内有这个子树)
var scrollbarRt = ProceduralBuilders.AddChild(template, "Scrollbar");
scrollbarRt.anchorMin = new Vector2(1f, 0f);
scrollbarRt.anchorMax = new Vector2(1f, 1f);
scrollbarRt.pivot = new Vector2(1f, 1f);
scrollbarRt.sizeDelta = new Vector2(20f, 0f);
scrollbarRt.anchoredPosition = Vector2.zero;
var scrollbarBg = scrollbarRt.gameObject.AddComponent<UnityImage>();
scrollbarBg.color = ProceduralBuilders.DefaultControlBgColor;
ProceduralBuilders.ApplyDefaultSlicedSprite(scrollbarBg);
var scrollbar = scrollbarRt.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
scrollbar.value = 0f;
scrollbar.size = 0.2f;

var slidingArea = ProceduralBuilders.AddChild(scrollbarRt, "Sliding Area");
slidingArea.sizeDelta = new Vector2(-20f, -20f);

var sbHandle = ProceduralBuilders.AddImage(slidingArea, "Handle");
sbHandle.color = Color.white;
ProceduralBuilders.ApplyDefaultSlicedSprite(sbHandle);
sbHandle.rectTransform.anchorMin = new Vector2(0f, 0f);
sbHandle.rectTransform.anchorMax = new Vector2(1f, 0.2f);
sbHandle.rectTransform.sizeDelta = new Vector2(20f, 20f);
sbHandle.rectTransform.anchoredPosition = Vector2.zero;
scrollbar.targetGraphic = sbHandle;
scrollbar.handleRect = sbHandle.rectTransform;

templateScroll.verticalScrollbar = scrollbar;
templateScroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
templateScroll.verticalScrollbarSpacing = -3f;
```

- [ ] **Step 4: Refresh + run; expect pass**

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
cd ..
git add Runtime/Controls/Dropdown.cs Tests/EditMode/Controls/DropdownTests.cs
git commit -m "feat(Dropdown): popup 加 Scrollbar Vertical (M5.1 §7.4)

Template 内增 Scrollbar/Sliding Area/Handle 子树；ScrollRect.verticalScrollbar
+ scrollbarVisibility=AutoHide + scrollbarSpacing=-3 wired up。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: ScrollList Viewport stencil Mask switch

**Files:**
- Modify: `Runtime/Controls/ScrollList.cs`
- Modify: `Tests/EditMode/Controls/ScrollListTests.cs`

Spec §8.1. Same as Task 6 but for ScrollList.

- [ ] **Step 1: Write failing tests (append to ScrollListTests.cs)**

```csharp
[Test]
public void Viewport_HasStencilMaskAndImageWithAlphaOne()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' itemTemplate='Slot'/></Screen>
</PromptUGUI>";
    UI.LoadDocument("test", xml);
    var sl = UI.Open("S").Get<ScrollList>("sl");
    var mask = sl.GameObject.GetComponentInChildren<UnityEngine.UI.Mask>(includeInactive: true);
    Assert.IsNotNull(mask, "Viewport should use stencil Mask");
    Assert.IsFalse(mask.showMaskGraphic);

    var img = mask.GetComponent<UnityEngine.UI.Image>();
    Assert.IsNotNull(img);
    Assert.AreEqual(1f, img.color.a, "alpha=1 critical to avoid 4af322b alpha-discard regression");
}

[Test]
public void Viewport_HasNoRectMask2D()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' itemTemplate='Slot'/></Screen>
</PromptUGUI>";
    UI.LoadDocument("test", xml);
    var sl = UI.Open("S").Get<ScrollList>("sl");
    Assert.IsNull(sl.GameObject.GetComponentInChildren<UnityEngine.UI.RectMask2D>(includeInactive: true));
}
```

- [ ] **Step 2: Refresh + run; expect 2 fails**

- [ ] **Step 3: Edit `Runtime/Controls/ScrollList.cs` Viewport block**

Find the Viewport setup (around line 37-39) and replace:

```diff
 var viewport = ProceduralBuilders.AddChild(RectTransform, "Viewport");
-viewport.gameObject.AddComponent<RectMask2D>();
+// Viewport: stencil Mask + alpha=1 sliced Image + showMaskGraphic=false (跟默认 prefab 一致；
+// alpha=1 关键，避免 4af322b 的 UI/Default shader alpha-discard)
+var viewportImg = viewport.gameObject.AddComponent<UnityImage>();
+viewportImg.color = Color.white;
+ProceduralBuilders.ApplyDefaultSlicedSprite(viewportImg);
+var viewportMask = viewport.gameObject.AddComponent<Mask>();
+viewportMask.showMaskGraphic = false;
 _scroll.viewport = viewport;
```

You may also need to add `using UnityEngine.UI;` if `Mask` isn't already in scope (the file already imports `UnityEngine.UI` for `RectMask2D` etc., so likely fine).

- [ ] **Step 4: Refresh + run; expect pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", filter="ScrollListTests")
```

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
cd ..
git add Runtime/Controls/ScrollList.cs Tests/EditMode/Controls/ScrollListTests.cs
git commit -m "feat(ScrollList): Viewport 切回 stencil Mask + alpha=1 (M5.1 §8.1)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: ScrollList ScrollRect explicit params + bg color

**Files:**
- Modify: `Runtime/Controls/ScrollList.cs`
- Modify: `Tests/EditMode/Controls/ScrollListTests.cs`

Spec §8.2. ScrollList root bg uses `DefaultContainerColor` (already white α=0.392 from Task 1). Set ScrollRect default params explicitly.

- [ ] **Step 1: Write failing tests**

```csharp
[Test]
public void Visual_BgColorIsTranslucentWhite()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' itemTemplate='Slot'/></Screen>
</PromptUGUI>";
    UI.LoadDocument("test", xml);
    var sl = UI.Open("S").Get<ScrollList>("sl");
    var img = sl.GameObject.GetComponent<UnityEngine.UI.Image>();
    Assert.IsNotNull(img);
    Assert.AreEqual(1f, img.color.r);
    Assert.AreEqual(1f, img.color.g);
    Assert.AreEqual(1f, img.color.b);
    Assert.That(img.color.a, Is.EqualTo(0.392f).Within(0.005f));
}

[Test]
public void ScrollRect_HasDefaultMovementParams()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' itemTemplate='Slot'/></Screen>
</PromptUGUI>";
    UI.LoadDocument("test", xml);
    var sl = UI.Open("S").Get<ScrollList>("sl");
    var sr = sl.GameObject.GetComponent<UnityEngine.UI.ScrollRect>();
    Assert.AreEqual(UnityEngine.UI.ScrollRect.MovementType.Elastic, sr.movementType);
    Assert.That(sr.elasticity, Is.EqualTo(0.1f).Within(0.001f));
    Assert.IsTrue(sr.inertia);
    Assert.That(sr.decelerationRate, Is.EqualTo(0.135f).Within(0.001f));
    Assert.AreEqual(1f, sr.scrollSensitivity);
}
```

- [ ] **Step 2: Refresh + run; expect bg test passes (Task 1 changed DefaultContainerColor), ScrollRect test fails**

- [ ] **Step 3: Edit `Runtime/Controls/ScrollList.cs` `OnAttached()`**

After `_scroll = GameObject.GetComponent<ScrollRect>() ?? GameObject.AddComponent<ScrollRect>();` and the Viewport/Content setup, add explicit ScrollRect defaults BEFORE the `ApplyDirection()` call:

```csharp
_scroll.movementType = ScrollRect.MovementType.Elastic;
_scroll.elasticity = 0.1f;
_scroll.inertia = true;
_scroll.decelerationRate = 0.135f;
_scroll.scrollSensitivity = 1f;
```

- [ ] **Step 4: Refresh + run; expect pass**

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
cd ..
git add Runtime/Controls/ScrollList.cs Tests/EditMode/Controls/ScrollListTests.cs
git commit -m "feat(ScrollList): ScrollRect 显式默认参数 + bg 半透明白 (M5.1 §8.2)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 11: ScrollList direction-based Scrollbar

**Files:**
- Modify: `Runtime/Controls/ScrollList.cs`
- Modify: `Tests/EditMode/Controls/ScrollListTests.cs`

Spec §8.3. Add `EnsureVerticalScrollbar()` and `EnsureHorizontalScrollbar()` helpers. `ApplyDirection()` calls one and deactivates the other. Visibility=AutoHide.

- [ ] **Step 1: Write failing tests**

```csharp
[Test]
public void Has_VerticalScrollbarByDefaultDirection()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' itemTemplate='Slot'/></Screen>
</PromptUGUI>";
    UI.LoadDocument("test", xml);
    var sl = UI.Open("S").Get<ScrollList>("sl");
    var sb = sl.GameObject.transform.Find("Scrollbar Vertical") as RectTransform;
    Assert.IsNotNull(sb, "default direction is vertical → Scrollbar Vertical exists");
    Assert.AreEqual(new Vector2(1, 0), sb.anchorMin);
    Assert.AreEqual(new Vector2(1, 0), sb.anchorMax);
    Assert.AreEqual(new Vector2(20, 0), sb.sizeDelta);

    var scrollbar = sb.GetComponent<UnityEngine.UI.Scrollbar>();
    Assert.AreEqual(UnityEngine.UI.Scrollbar.Direction.BottomToTop, scrollbar.direction);

    var sr = sl.GameObject.GetComponent<UnityEngine.UI.ScrollRect>();
    Assert.AreSame(scrollbar, sr.verticalScrollbar);
    Assert.AreEqual(UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide,
        sr.verticalScrollbarVisibility);
}

[Test]
public void Has_HorizontalScrollbarWhenDirectionHorizontal()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' direction='horizontal' itemTemplate='Slot'/></Screen>
</PromptUGUI>";
    UI.LoadDocument("test", xml);
    var sl = UI.Open("S").Get<ScrollList>("sl");
    var sb = sl.GameObject.transform.Find("Scrollbar Horizontal") as RectTransform;
    Assert.IsNotNull(sb);
    Assert.AreEqual(new Vector2(0, 0), sb.anchorMin);
    Assert.AreEqual(new Vector2(0, 0), sb.anchorMax);
    Assert.AreEqual(new Vector2(0, 20), sb.sizeDelta);

    var scrollbar = sb.GetComponent<UnityEngine.UI.Scrollbar>();
    Assert.AreEqual(UnityEngine.UI.Scrollbar.Direction.LeftToRight, scrollbar.direction);

    var sr = sl.GameObject.GetComponent<UnityEngine.UI.ScrollRect>();
    Assert.AreSame(scrollbar, sr.horizontalScrollbar);
}
```

- [ ] **Step 2: Refresh + run; expect 2 fails**

- [ ] **Step 3: Edit `Runtime/Controls/ScrollList.cs`**

Add 2 fields to the class:

```csharp
private Scrollbar _vertScrollbar;
private Scrollbar _horizScrollbar;
```

Inside `ApplyDirection()`, after the existing `_layoutGroup` / `fitter` / `_content` configuration AND after the new ScrollRect param block from Task 10 — add at the very end:

```csharp
if (_direction == "horizontal")
{
    EnsureHorizontalScrollbar();
    if (_vertScrollbar != null) _vertScrollbar.gameObject.SetActive(false);
}
else
{
    EnsureVerticalScrollbar();
    if (_horizScrollbar != null) _horizScrollbar.gameObject.SetActive(false);
}
```

Add two private helper methods at the end of the class (before `Dispose`):

```csharp
private void EnsureVerticalScrollbar()
{
    if (_vertScrollbar != null) { _vertScrollbar.gameObject.SetActive(true); return; }
    var rt = ProceduralBuilders.AddChild(RectTransform, "Scrollbar Vertical");
    rt.anchorMin = new Vector2(1f, 0f);
    rt.anchorMax = new Vector2(1f, 0f);
    rt.pivot = new Vector2(1f, 1f);
    rt.sizeDelta = new Vector2(20f, 0f);
    var bg = rt.gameObject.AddComponent<UnityImage>();
    bg.color = Color.white;
    ProceduralBuilders.ApplyDefaultSlicedSprite(bg);
    _vertScrollbar = rt.gameObject.AddComponent<Scrollbar>();
    _vertScrollbar.direction = Scrollbar.Direction.BottomToTop;

    var sliding = ProceduralBuilders.AddChild(rt, "Sliding Area");
    sliding.sizeDelta = new Vector2(-20f, -20f);
    var handle = ProceduralBuilders.AddImage(sliding, "Handle");
    handle.color = Color.white;
    ProceduralBuilders.ApplyDefaultSlicedSprite(handle);
    handle.rectTransform.anchorMin = Vector2.zero;
    handle.rectTransform.anchorMax = Vector2.zero;
    handle.rectTransform.sizeDelta = new Vector2(20f, 20f);
    _vertScrollbar.targetGraphic = handle;
    _vertScrollbar.handleRect = handle.rectTransform;

    _scroll.verticalScrollbar = _vertScrollbar;
    _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    _scroll.verticalScrollbarSpacing = -3f;
}

private void EnsureHorizontalScrollbar()
{
    if (_horizScrollbar != null) { _horizScrollbar.gameObject.SetActive(true); return; }
    var rt = ProceduralBuilders.AddChild(RectTransform, "Scrollbar Horizontal");
    rt.anchorMin = new Vector2(0f, 0f);
    rt.anchorMax = new Vector2(0f, 0f);
    rt.pivot = new Vector2(0f, 0f);
    rt.sizeDelta = new Vector2(0f, 20f);
    var bg = rt.gameObject.AddComponent<UnityImage>();
    bg.color = Color.white;
    ProceduralBuilders.ApplyDefaultSlicedSprite(bg);
    _horizScrollbar = rt.gameObject.AddComponent<Scrollbar>();
    _horizScrollbar.direction = Scrollbar.Direction.LeftToRight;

    var sliding = ProceduralBuilders.AddChild(rt, "Sliding Area");
    sliding.sizeDelta = new Vector2(-20f, -20f);
    var handle = ProceduralBuilders.AddImage(sliding, "Handle");
    handle.color = Color.white;
    ProceduralBuilders.ApplyDefaultSlicedSprite(handle);
    handle.rectTransform.anchorMin = Vector2.zero;
    handle.rectTransform.anchorMax = Vector2.zero;
    handle.rectTransform.sizeDelta = new Vector2(20f, 20f);
    _horizScrollbar.targetGraphic = handle;
    _horizScrollbar.handleRect = handle.rectTransform;

    _scroll.horizontalScrollbar = _horizScrollbar;
    _scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    _scroll.horizontalScrollbarSpacing = -3f;
}
```

- [ ] **Step 4: Refresh + run; expect pass**

- [ ] **Step 5: Run full ScrollListTests; ensure existing pass**

```
mcp__UnityMCP__run_tests(mode="EditMode", filter="ScrollListTests")
```

- [ ] **Step 6: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
cd ..
git add Runtime/Controls/ScrollList.cs Tests/EditMode/Controls/ScrollListTests.cs
git commit -m "feat(ScrollList): direction-based Scrollbar (Vertical/Horizontal) (M5.1 §8.3)

EnsureVerticalScrollbar / EnsureHorizontalScrollbar 按 direction 创建/激活；
另一方向的 scrollbar 若已创建则 SetActive(false)。
visibility=AutoHide + spacing=-3 默认；wired up 到 ScrollRect.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 12: InputField primitive — root + bg + TextArea

**Files:**
- Create: `Runtime/Controls/InputField.cs`
- Create: `Tests/EditMode/Controls/InputFieldTests.cs`
- Modify: `Runtime/Application/BuiltinPrimitives.cs`

Spec §10. New 13th built-in primitive: TMP_InputField on root + sliced bg + Text Area child with RectMask2D negative padding.

- [ ] **Step 1: Write failing test**

Create `Tests/EditMode/Controls/InputFieldTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class InputFieldTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Build_HasBgImageAndTMPInputField()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var f = screen.Get<InputField>("f");
            Assert.IsNotNull(f.GameObject.GetComponent<Image>(), "root has Image bg");
            Assert.IsNotNull(f.GameObject.GetComponent<TMP_InputField>());
        }

        [Test]
        public void Geometry_TextAreaInsetMatchesPrefab()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<InputField>("f");
            var ta = f.GameObject.transform.Find("Text Area") as RectTransform;
            Assert.IsNotNull(ta, "Text Area child must exist");
            Assert.AreEqual(new Vector2(0, 0), ta.anchorMin);
            Assert.AreEqual(new Vector2(1, 1), ta.anchorMax);
            Assert.AreEqual(new Vector2(-20, -13), ta.sizeDelta);
            Assert.AreEqual(new Vector2(0, -0.5f), ta.anchoredPosition);
        }

        [Test]
        public void Geometry_TextAreaHasRectMask2DWithPadding()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<InputField>("f");
            var ta = f.GameObject.transform.Find("Text Area").gameObject;
            var rm = ta.GetComponent<RectMask2D>();
            Assert.IsNotNull(rm, "Text Area uses RectMask2D (matches default prefab)");
            Assert.AreEqual(new Vector4(-8, -5, -8, -5), rm.padding);
        }
    }
}
```

- [ ] **Step 2: Refresh; expect compile fail (`InputField` doesn't exist)**

- [ ] **Step 3: Create `Runtime/Controls/InputField.cs`**

```csharp
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class InputField : Control
    {
        private UnityImage _bg;
        private TMP_InputField _input;
        private TMP_Text _text;
        private TMP_Text _placeholder;

        public override void OnAttached()
        {
            // Root：sliced bg + TMP_InputField
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultControlBgColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);

            // Text Area：跟 default prefab 一致 (sizeDelta=-20,-13, anchoredPos=0,-0.5, RectMask2D padding=-8,-5,-8,-5)
            var textAreaRt = ProceduralBuilders.AddChild(RectTransform, "Text Area");
            textAreaRt.anchorMin = new Vector2(0f, 0f);
            textAreaRt.anchorMax = new Vector2(1f, 1f);
            textAreaRt.offsetMin = Vector2.zero;
            textAreaRt.offsetMax = Vector2.zero;
            textAreaRt.sizeDelta = new Vector2(-20f, -13f);
            textAreaRt.anchoredPosition = new Vector2(0f, -0.5f);
            var textAreaMask = textAreaRt.gameObject.AddComponent<RectMask2D>();
            textAreaMask.padding = new Vector4(-8f, -5f, -8f, -5f);

            _input = GameObject.AddComponent<TMP_InputField>();
            _input.targetGraphic = _bg;
            _input.textViewport = textAreaRt;
        }
    }
}
```

- [ ] **Step 4: Edit `Runtime/Application/BuiltinPrimitives.cs`** — register InputField

Find the registration block (likely contains `registry.Register<Toggle>("Toggle");` etc.) and add:

```csharp
registry.Register<InputField>("InputField");
```

- [ ] **Step 5: Refresh + run; expect Build_HasBgImageAndTMPInputField + Geometry tests pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", filter="InputFieldTests")
```

- [ ] **Step 6: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
cd ..
git add Runtime/Controls/InputField.cs Runtime/Application/BuiltinPrimitives.cs Tests/EditMode/Controls/InputFieldTests.cs
git commit -m "feat(InputField): new built-in primitive — root + Text Area RectMask2D (M5.1 §10)

Step 1/4: 基础结构。Image bg + TMP_InputField on root；Text Area child
with RectMask2D padding=(-8,-5,-8,-5)。注册到 BuiltinPrimitives。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 13: InputField Placeholder + Text children

**Files:**
- Modify: `Runtime/Controls/InputField.cs`
- Modify: `Tests/EditMode/Controls/InputFieldTests.cs`

- [ ] **Step 1: Append failing tests to `InputFieldTests.cs`**

```csharp
[Test]
public void Geometry_PlaceholderIsItalicHalfAlphaWithIgnoreLayout()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' placeholder='Enter...'/>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var f = UI.Open("S").Get<InputField>("f");
    var ph = f.GameObject.transform.Find("Text Area/Placeholder")?.GetComponent<TMP_Text>();
    Assert.IsNotNull(ph, "Placeholder must be Text Area child");
    Assert.AreEqual(FontStyles.Italic, ph.fontStyle);
    Assert.That(ph.color.a, Is.EqualTo(0.5f).Within(0.005f));
    Assert.IsFalse(ph.raycastTarget);

    var le = ph.gameObject.GetComponent<LayoutElement>();
    Assert.IsNotNull(le);
    Assert.IsTrue(le.ignoreLayout);
}

[Test]
public void Geometry_TextChildExists()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var f = UI.Open("S").Get<InputField>("f");
    var text = f.GameObject.transform.Find("Text Area/Text")?.GetComponent<TMP_Text>();
    Assert.IsNotNull(text);
    Assert.IsFalse(text.raycastTarget);
}

[Test]
public void Wired_TMPInputFieldRefsTextAndPlaceholder()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var f = UI.Open("S").Get<InputField>("f");
    var input = f.GameObject.GetComponent<TMP_InputField>();
    Assert.IsNotNull(input.textComponent);
    Assert.AreEqual("Text", input.textComponent.gameObject.name);
    Assert.IsNotNull(input.placeholder);
    Assert.AreEqual("Placeholder", input.placeholder.gameObject.name);
}
```

- [ ] **Step 2: Refresh + run; expect 3 fails**

- [ ] **Step 3: Edit `Runtime/Controls/InputField.cs` — add Placeholder + Text creation**

After `_input.textViewport = textAreaRt;` in `OnAttached()`, append:

```csharp
// Placeholder：italic + 半透明 + IgnoreLayout
_placeholder = ProceduralBuilders.AddText(textAreaRt, "Placeholder");
_placeholder.alignment = TextAlignmentOptions.TopLeft;
_placeholder.fontStyle = FontStyles.Italic;
_placeholder.color = ProceduralBuilders.DefaultPlaceholderColor;
_placeholder.text = "Enter text...";
_placeholder.enableWordWrapping = false;
var phLE = _placeholder.gameObject.AddComponent<LayoutElement>();
phLE.ignoreLayout = true;

// Text：用户输入显示组件
_text = ProceduralBuilders.AddText(textAreaRt, "Text");
_text.alignment = TextAlignmentOptions.TopLeft;
_text.color = ProceduralBuilders.DefaultLabelColor;
_text.text = string.Empty;

_input.textComponent = _text;
_input.placeholder = _placeholder;
_input.caretColor = ProceduralBuilders.DefaultGlyphColor;
_input.customCaretColor = false;
_input.selectionColor = new Color(0.659f, 0.808f, 1f, 0.753f);
```

- [ ] **Step 4: Refresh + run; expect pass**

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
cd ..
git add Runtime/Controls/InputField.cs Tests/EditMode/Controls/InputFieldTests.cs
git commit -m "feat(InputField): Placeholder + Text 子节点 + TMP_InputField wiring (M5.1 §10)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 14: InputField — `[UIAttr]` properties

**Files:**
- Modify: `Runtime/Controls/InputField.cs`
- Modify: `Tests/EditMode/Controls/InputFieldTests.cs`

Spec §10.2. Properties: `text`, `placeholder`, `contentType`, `lineType`, `characterLimit`, `readOnly`, `color`, `sprite`, `font`. Plus text-shorthand support (body content → text).

- [ ] **Step 1: Append failing tests**

```csharp
[Test]
public void Apply_TextAttribute()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' text='hello'/>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var f = UI.Open("S").Get<InputField>("f");
    Assert.AreEqual("hello", f.GameObject.GetComponent<TMP_InputField>().text);
}

[Test]
public void TextShorthand_BodyTextSetsText()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'>初始</InputField>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var f = UI.Open("S").Get<InputField>("f");
    Assert.AreEqual("初始", f.GameObject.GetComponent<TMP_InputField>().text);
}

[Test]
public void Apply_PlaceholderAttribute()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' placeholder='请输入'/>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var f = UI.Open("S").Get<InputField>("f");
    var ph = f.GameObject.transform.Find("Text Area/Placeholder").GetComponent<TMP_Text>();
    Assert.AreEqual("请输入", ph.text);
}

[Test]
public void Apply_ContentTypePassword()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' contentType='password'/>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var f = UI.Open("S").Get<InputField>("f");
    Assert.AreEqual(TMP_InputField.ContentType.Password,
        f.GameObject.GetComponent<TMP_InputField>().contentType);
}

[Test]
public void Apply_LineTypeMultiNewline()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' lineType='multi-newline'/>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var f = UI.Open("S").Get<InputField>("f");
    Assert.AreEqual(TMP_InputField.LineType.MultiLineNewline,
        f.GameObject.GetComponent<TMP_InputField>().lineType);
}

[Test]
public void Apply_CharacterLimit()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' characterLimit='10'/>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var f = UI.Open("S").Get<InputField>("f");
    Assert.AreEqual(10, f.GameObject.GetComponent<TMP_InputField>().characterLimit);
}

[Test]
public void Apply_ReadOnly()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' readOnly='true'/>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var f = UI.Open("S").Get<InputField>("f");
    Assert.IsTrue(f.GameObject.GetComponent<TMP_InputField>().readOnly);
}
```

- [ ] **Step 2: Refresh + run; expect 7 fails (no setters yet)**

- [ ] **Step 3: Edit `Runtime/Controls/InputField.cs` — add UIAttr properties**

Append below `OnAttached()` in the `InputField` class:

```csharp
[UIAttr("text")]
public string TextValue
{
    set => _input.text = value ?? string.Empty;
}

[UIAttr]
public string Placeholder
{
    set
    {
        if (_placeholder != null) _placeholder.text = value ?? string.Empty;
    }
}

[UIAttr]
public string ContentType
{
    set
    {
        _input.contentType = value switch
        {
            "standard" => TMP_InputField.ContentType.Standard,
            "autocorrected" => TMP_InputField.ContentType.Autocorrected,
            "integer-number" => TMP_InputField.ContentType.IntegerNumber,
            "decimal-number" => TMP_InputField.ContentType.DecimalNumber,
            "alphanumeric" => TMP_InputField.ContentType.Alphanumeric,
            "name" => TMP_InputField.ContentType.Name,
            "email" => TMP_InputField.ContentType.EmailAddress,
            "password" => TMP_InputField.ContentType.Password,
            "pin" => TMP_InputField.ContentType.Pin,
            "custom" => TMP_InputField.ContentType.Custom,
            _ => throw new System.ArgumentException(
                $"InputField.contentType='{value}' invalid; expected standard|autocorrected|integer-number|decimal-number|alphanumeric|name|email|password|pin|custom"),
        };
    }
}

[UIAttr]
public string LineType
{
    set
    {
        _input.lineType = value switch
        {
            "single" => TMP_InputField.LineType.SingleLine,
            "multi-newline" => TMP_InputField.LineType.MultiLineNewline,
            "multi-submit" => TMP_InputField.LineType.MultiLineSubmit,
            _ => throw new System.ArgumentException(
                $"InputField.lineType='{value}' invalid; expected single|multi-newline|multi-submit"),
        };
    }
}

[UIAttr]
public int CharacterLimit
{
    set => _input.characterLimit = value;
}

[UIAttr]
public bool ReadOnly
{
    set => _input.readOnly = value;
}

[UIAttr]
public string Color
{
    set
    {
        if (string.IsNullOrEmpty(value)) return;
        if (UnityEngine.ColorUtility.TryParseHtmlString(value, out var c)) _bg.color = c;
    }
}

[UIAttr]
public string Sprite
{
    set
    {
        if (string.IsNullOrEmpty(value)) { _bg.sprite = null; return; }
        _bg.sprite = Resources.Load<Sprite>(value);
    }
}

[UIAttr]
public string Font
{
    set => _fontType = string.IsNullOrEmpty(value) ? "default" : value;
    // ApplyFont 调用在 OnAttached 末尾处理；这里只更新内部 state
}

private string _fontType = "default";
```

Add `ApplyFont()` (called at OnAttached end and on locale change):

```csharp
private void ApplyFont()
{
    if (_text == null) return;
    var settings = PromptUGUI.Application.PromptUGUISettings.Instance;
    var locale = PromptUGUI.Application.UI.Locale.Current;
    var asset = settings?.ResolveFont(locale, _fontType);
    if (asset == null) return;
    _text.font = asset;
    if (_placeholder != null) _placeholder.font = asset;
}
```

In `OnAttached()` end, add:

```csharp
ApplyFont();
PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
```

Add `Dispose()`:

```csharp
public override void Dispose()
{
    PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
    base.Dispose();
}
```

- [ ] **Step 4: Refresh + run; expect 7 pass**

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
cd ..
git add Runtime/Controls/InputField.cs Tests/EditMode/Controls/InputFieldTests.cs
git commit -m "feat(InputField): [UIAttr] 属性 (text/placeholder/contentType/lineType/...) (M5.1 §10.2)

text + 短手 / placeholder / contentType (10 个值，hyphen-form) / lineType /
characterLimit / readOnly / color / sprite / font + i18n locale hookup。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 15: InputField — R3 events

**Files:**
- Modify: `Runtime/Controls/InputField.cs`
- Modify: `Tests/EditMode/Controls/InputFieldTests.cs`

Spec §10.3. `OnValueChanged` / `OnEndEdit` / `OnSubmit` as `Observable<string>`.

- [ ] **Step 1: Append failing tests**

```csharp
[Test]
public void Event_OnValueChanged_FiresOnTextSet()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var f = UI.Open("S").Get<InputField>("f");

    string last = null;
    f.OnValueChanged.Subscribe(v => last = v);
    f.GameObject.GetComponent<TMP_InputField>().text = "abc";
    Assert.AreEqual("abc", last);
}

[Test]
public void Event_OnEndEdit_FiresOnEndEditUnityCallback()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var f = UI.Open("S").Get<InputField>("f");

    string last = null;
    f.OnEndEdit.Subscribe(v => last = v);
    // 模拟：直接调用 UnityEvent（EditMode 没有真正失焦）
    f.GameObject.GetComponent<TMP_InputField>().onEndEdit.Invoke("done");
    Assert.AreEqual("done", last);
}

[Test]
public void Event_OnSubmit_FiresOnSubmitUnityCallback()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var f = UI.Open("S").Get<InputField>("f");

    string last = null;
    f.OnSubmit.Subscribe(v => last = v);
    f.GameObject.GetComponent<TMP_InputField>().onSubmit.Invoke("submitted");
    Assert.AreEqual("submitted", last);
}
```

- [ ] **Step 2: Refresh + run; expect 3 fails**

- [ ] **Step 3: Edit `Runtime/Controls/InputField.cs`**

Add 3 Subject fields at the top of the class:

```csharp
private readonly Subject<string> _changed = new();
private readonly Subject<string> _endEdit = new();
private readonly Subject<string> _submit = new();

public Observable<string> OnValueChanged => _changed;
public Observable<string> OnEndEdit => _endEdit;
public Observable<string> OnSubmit => _submit;
```

In `OnAttached()` after `_input.textComponent = _text;` and the wire-ups, add:

```csharp
_input.onValueChanged.AddListener(v => _changed.OnNext(v));
_input.onEndEdit.AddListener(v => _endEdit.OnNext(v));
_input.onSubmit.AddListener(v => _submit.OnNext(v));
```

In `Dispose()`:

```csharp
public override void Dispose()
{
    PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
    _changed.Dispose();
    _endEdit.Dispose();
    _submit.Dispose();
    base.Dispose();
}
```

- [ ] **Step 4: Refresh + run; expect pass**

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
cd ..
git add Runtime/Controls/InputField.cs Tests/EditMode/Controls/InputFieldTests.cs
git commit -m "feat(InputField): R3 events (OnValueChanged/OnEndEdit/OnSubmit) (M5.1 §10.3)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 16: InputField PlayMode integration test + XSD verify

**Files:**
- Create: `Tests/PlayMode/Controls/InputFieldRuntimeTests.cs`

Spec §12.3. End-to-end test that opens a Screen, simulates value change via direct invoke, verifies R3 event fires from a real running scene.

- [ ] **Step 1: Create `Tests/PlayMode/Controls/InputFieldRuntimeTests.cs`**

(Match existing PlayMode test conventions; check `Tests/PlayMode/` for existing structure first.)

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using TMPro;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    public class InputFieldRuntimeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator Typing_FiresOnValueChanged()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<InputField>("f");
            yield return null;  // wait one frame for canvas init

            string last = null;
            f.OnValueChanged.Subscribe(v => last = v);
            f.GameObject.GetComponent<TMP_InputField>().text = "typed";
            yield return null;

            Assert.AreEqual("typed", last);
        }
    }
}
```

- [ ] **Step 2: Refresh + run PlayMode**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="PlayMode", filter="InputFieldRuntimeTests")
```
Expected: PASS.

- [ ] **Step 3: Verify XSD regenerates with `<InputField>`**

Per CLAUDE.md, `Tools → PromptUGUI → Schema → Generate XSD` is user-triggered for Control class additions. Tell the user (in their language) to run that menu item, then re-verify by xmllint:

```bash
xmllint --noout --schema Assets/PromptUGUI.gen.xsd path/to/your/inputfield-test.ui.xml
```

If `Assets/PromptUGUI.gen.xsd` doesn't exist or doesn't list `InputField`, request regeneration.

- [ ] **Step 4: Commit**

```bash
git add Tests/PlayMode/Controls/InputFieldRuntimeTests.cs
git commit -m "test(InputField): PlayMode 端到端 OnValueChanged 用例 (M5.1 §12.3)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 17: SKILL.md sync

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`

Spec §11. Three changes: (1) update primitive count `(12)` → `(13)`, add `<InputField>` row; (2) add theme note; (3) update cheatsheet.

- [ ] **Step 1: Edit `.claude/skills/authoring-promptugui-xml/SKILL.md`**

Find the section header `## Built-in primitives (12)` and change to `## Built-in primitives (13)`.

After the existing `<ScrollList>` table row, add this new row:

```markdown
| `<InputField>`  | TMP_InputField；R3 `OnValueChanged` / `OnEndEdit` / `OnSubmit: string`。`<InputField>初始文本</InputField>` 短手设 `text=`。 | `text`, `placeholder`, `contentType` (`standard`/`autocorrected`/`integer-number`/`decimal-number`/`alphanumeric`/`name`/`email`/`password`/`pin`/`custom`), `lineType` (`single`/`multi-newline`/`multi-submit`), `characterLimit` (int), `readOnly` (bool), `color`, `sprite`, `font`, `tr` (placeholder)/`ctx` |
```

Above the primitive table, add a new short paragraph:

```markdown
**默认视觉主题**：白底 sliced + #323232 深字（与 Unity 6 `GameObject → UI → …` 创建出来的标准 prefab 一致）。所有控件的颜色/sprite 都能通过 `color=` / `sprite=` 属性 override；想要彻底深色主题项目级覆写 `ProceduralBuilders` 的常量，或用 Variant 方式 `color.dark="..."`。
```

In the `## Quick reference (cheatsheet)` block at bottom, update:

```diff
-BUILT-INS     <Frame> <Image> <Text> <VStack> <HStack> <Grid> <Btn> <Icon>
-              <Toggle> <Slider> <Dropdown> <ScrollList>
-TEXT SHORT    <Text>Hi</Text> ≡ <Text text="Hi"/>     (also <Btn>, <Toggle>)
+BUILT-INS     <Frame> <Image> <Text> <VStack> <HStack> <Grid> <Btn> <Icon>
+              <Toggle> <Slider> <Dropdown> <ScrollList> <InputField>
+TEXT SHORT    <Text>Hi</Text> ≡ <Text text="Hi"/>     (also <Btn>, <Toggle>, <InputField>)
```

- [ ] **Step 2: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "docs(skill): SKILL.md 同步 — InputField + 默认主题简注 (M5.1 §11)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 18: Sample InputField demo

**Files:**
- Modify: `Samples~/MainMenu/UI/...` (or create a new sample under `Samples~/`)

Spec §13. Add a default-controls demo screen so visual changes can be eyeballed.

- [ ] **Step 1: Find existing Sample structure**

```bash
ls Samples~/
```

Pick the most appropriate location:
- If `Samples~/MainMenu/` exists, add a side-screen `DefaultControls.ui.xml` and a button to navigate.
- Otherwise, create new `Samples~/DefaultControls/` with its own `UI/DefaultControls.ui.xml` + minimal C# bootstrap.

- [ ] **Step 2: Create XML**

Create `Samples~/MainMenu/UI/DefaultControls.ui.xml` (or equivalent under chosen sample):

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="DefaultControls">
    <Image anchor="stretch" color="#222222"/>
    <VStack anchor="center" size="320x600" spacing="12" padding="24">
      <Text fontSize="24">Default Controls 演示</Text>
      <Btn id="b">Click Me</Btn>
      <Toggle id="t1">Enable Audio</Toggle>
      <Slider id="vol" min="0" max="100" value="50"/>
      <Dropdown id="quality"/>
      <InputField id="search" placeholder="Search..."/>
    </VStack>
  </Screen>
</PromptUGUI>
```

- [ ] **Step 3: Wire up C# bootstrap (if separate sample)**

If you created a new sample folder, create the bootstrap script (look at existing samples for pattern). Match the conventions of Samples~/MainMenu (or whichever was chosen).

- [ ] **Step 4: Verify in Unity Editor**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Tell the user (in their language) to open the sample scene in Unity and visually compare with `docs~/default control/*.prefab`. Document any visual surprise.

- [ ] **Step 5: Commit**

```bash
git add Samples~/
git commit -m "samples: DefaultControls 演示页 (M5.1 §13)

跟 Unity 默认控件视觉差不多一致；只 sprite 风格不同。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Final task: Full regression + open PR

- [ ] **Step 1: Full EditMode + PlayMode green**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode")
mcp__UnityMCP__run_tests(mode="PlayMode")
```
Expected: 0 failures.

- [ ] **Step 2: Lint full project**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: no diff.

- [ ] **Step 3: Push branch + open PR**

```bash
git push -u origin feature/m5.1-default-controls-alignment
gh pr create --title "M5.1 默认控件对齐 (光照主题 + 结构对齐 + 新 InputField)" --body "$(cat <<'EOF'
## Summary

- 7 个现存控件 (Btn/Toggle/Slider/Dropdown/ScrollList/Image/Text) 颜色 / 字号 / 子节点几何 / 组件类型对齐 Unity 6 默认 prefab
- 新 `<InputField>` 内置原语（第 13 个）
- ProceduralBuilders 调色板由"深色"切到 Unity 浅色主题
- Dropdown / ScrollList Viewport 切回 stencil Mask + alpha=1（修复 4af322b 的 alpha-discard 同时对齐默认）
- Dropdown popup 加 Scrollbar Vertical；ScrollList 按 direction 加单一 Scrollbar
- SKILL.md / Sample 同步

设计：[`docs~/superpowers/specs/2026-05-09-m5-1-default-control-alignment-design.md`](docs~/superpowers/specs/2026-05-09-m5-1-default-control-alignment-design.md)
计划：[`docs~/superpowers/plans/2026-05-09-m5-1-default-control-alignment.md`](docs~/superpowers/plans/2026-05-09-m5-1-default-control-alignment.md)

## Test plan

- [x] EditMode all green (309+ 新增 ~30 个新断言)
- [x] PlayMode all green (含 InputFieldRuntimeTests)
- [x] Lint `dotnet format --verify-no-changes --severity warn` 无 diff
- [ ] 手测：Unity 打开 Samples~/.../DefaultControls 场景，visual 跟 `docs~/default control/*.prefab` 对照
- [ ] 手测：ScrollList vertical / horizontal 切换 scrollbar 显隐正确
- [ ] 手测：Dropdown 选项 > 5 时 popup scrollbar 出现
- [ ] 手测：InputField placeholder italic 半透明 / 输入后 placeholder 隐藏 / contentType=password 输入显星号

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Self-Review notes

(Performed before saving plan; fixes applied inline.)

**Spec coverage check:**

- §3 ProceduralBuilders palette → Task 1 ✓
- §4 Btn → Task 2 ✓
- §5 Toggle → Task 3 ✓ (incl. raycastTarget caveat)
- §6 Slider → Task 4 ✓
- §7 Dropdown (Arrow/Mask/Item/Scrollbar) → Tasks 5+6+7+8 ✓
- §8 ScrollList (Mask/ScrollRect/Scrollbar) → Tasks 9+10+11 ✓
- §9 Image/Text/Frame unchanged → no task needed ✓
- §10 InputField → Tasks 12+13+14+15 ✓
- §11 SKILL.md → Task 17 ✓
- §12 Tests → embedded into each task + PlayMode in Task 16 ✓
- §13 Sample → Task 18 ✓
- §14 Risks → addressed in Task 6 / Task 9 by alpha=1 assertion in test ✓
- §15 Main spec link → mentioned in commit messages, not a code task

**Type / API consistency check:**

- `ProceduralBuilders.DefaultLabelColor` / `DefaultPlaceholderColor` defined in Task 1, used in Tasks 2-15 ✓
- `ApplyDefaultSimpleSprite(img, name, preserveAspect=false)` signature change in Task 1, used by existing callers (Toggle Checkmark / Dropdown Arrow / Slider Handle) ✓
- `_vertScrollbar` / `_horizScrollbar` field names in Task 11 — consistent ✓
- `OnValueChanged` / `OnEndEdit` / `OnSubmit` Observable<string> defined in Task 15, used in InputFieldTests ✓
- `EnsureVerticalScrollbar` / `EnsureHorizontalScrollbar` matching pair ✓

No unresolved placeholders or TBDs.
