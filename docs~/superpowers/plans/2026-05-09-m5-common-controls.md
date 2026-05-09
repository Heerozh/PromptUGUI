# M5 — Common Controls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship four reference custom controls — `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` — auto-registered alongside `<Btn>`, with procedurally-built visuals, R3 event streams, `BindItems` / `BindOptions` data push, i18n integration, and full XML→C# round-trip tests + a `CommonControls` sample.

**Architecture:** New controls live in `Runtime/Controls/` and register from `BuiltinPrimitives`. They use shared procedural-build helpers (`Runtime/Controls/Internal/ProceduralBuilders.cs`) for RectTransform/Image/TMP_Text construction. `Toggle.Group` keys a `ToggleGroupRegistry` hung off `Screen`. `ScrollList.itemTemplate` resolves at instantiation against `ControlRegistry` (Control class) → `LoadedDoc.Templates` (TemplateDef) → ParseException; per-item instantiation goes through a new public `ScreenInstantiator.InstantiateNode` entry. New `IControl.Get<T>(idPath)` path API lets BindItems lambdas navigate slot subtrees. Master spec §5 and §10 plus SKILL.md primitives table are updated in lockstep.

**Tech Stack:** Unity 6+, C# (PromptUGUI.Runtime / .Tests asmdefs), TMP, R3 (Cysharp), uGUI (Toggle / Slider / TMP_Dropdown / ScrollRect), NUnit via UnityMCP.

**Spec:** `docs~/superpowers/specs/2026-05-09-m5-common-controls-design.md`. Read it before starting.

---

## File Structure

**New files (Runtime):**
- `Runtime/Controls/Internal/ProceduralBuilders.cs` — shared RectTransform / Image / TMP_Text constructors
- `Runtime/Controls/Internal/ToggleGroupRegistry.cs` — Screen-scoped string-keyed `ToggleGroup` cache
- `Runtime/Controls/Internal/DropdownOption.cs` — `(string Text, Sprite Icon)` POCO
- `Runtime/Controls/Toggle.cs`
- `Runtime/Controls/Slider.cs`
- `Runtime/Controls/Dropdown.cs`
- `Runtime/Controls/ScrollList.cs`

**Modified files (Runtime):**
- `Runtime/Controls/IControl.cs` — add `Get<T>(idPath)` / `Get(idPath)`
- `Runtime/Controls/Control.cs` — implement `Get<T>` via `ScopedIds` path walk
- `Runtime/Application/Screen.cs` — host `ToggleGroupRegistry` field; expose internal accessor
- `Runtime/Application/ScreenInstantiator.cs` — public `InstantiateNode(node, parent, scope)` entry; refactor existing `InstantiateRecursive` callers
- `Runtime/Application/BuiltinPrimitives.cs` — register Toggle / Slider / Dropdown / ScrollList

**Tests:**
- `Tests/EditMode/Controls/ToggleTests.cs`
- `Tests/EditMode/Controls/SliderTests.cs`
- `Tests/EditMode/Controls/DropdownTests.cs`
- `Tests/EditMode/Controls/ScrollListTests.cs`
- `Tests/EditMode/Controls/IControlGetPathTests.cs`
- `Tests/PlayMode/Controls/CommonControlsPlayTests.cs`

**New files (Sample):**
- `Samples~/CommonControls/CommonControlsRunner.cs`
- `Samples~/CommonControls/Resources/UI/Settings.ui.xml`

**Modified files (Docs / Package):**
- `package.json` — add `Common Controls Demo` sample entry
- `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` — §5 primitives table 4 new rows; §10 reword "不开" → "默认开启"
- `.claude/skills/authoring-promptugui-xml/SKILL.md` — primitives table + event/data-binding examples

---

## Pre-flight

- [x] **Refresh Unity & check console**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: zero compile errors before starting.

- [x] **Read the spec**

Read `docs~/superpowers/specs/2026-05-09-m5-common-controls-design.md` end-to-end. The decision table (§2) and risk table (§10) are the contract this plan implements.

- [x] **Read affected files**

```
Runtime/Controls/Btn.cs                          ← procedural-visuals reference pattern
Runtime/Controls/Text.cs                         ← TMP wiring + locale font hook
Runtime/Controls/Control.cs                      ← base class + ScopedIds
Runtime/Controls/IControl.cs                     ← interface to extend
Runtime/Application/BuiltinPrimitives.cs         ← registration call site
Runtime/Application/ScreenInstantiator.cs        ← InstantiateRecursive to expose
Runtime/Application/Screen.cs                    ← host ToggleGroupRegistry
Runtime/Application/ControlAttributeApplier.cs   ← TrResolver + variant routing
```

---

## Task 1: ProceduralBuilders helper

**Files:**
- Create: `Runtime/Controls/Internal/ProceduralBuilders.cs`

- [x] **Step 1: Write the helper**

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls.Internal
{
    internal static class ProceduralBuilders
    {
        public static RectTransform AddChild(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static UnityImage AddImage(RectTransform parent, string name, bool raycast = true)
        {
            var rt = AddChild(parent, name);
            var img = rt.gameObject.AddComponent<UnityImage>();
            img.raycastTarget = raycast;
            return img;
        }

        public static TMP_Text AddText(RectTransform parent, string name)
        {
            var rt = AddChild(parent, name);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
```

- [x] **Step 2: Refresh + verify compiles**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: no errors.

- [x] **Step 3: Commit**

```bash
git add Runtime/Controls/Internal/ProceduralBuilders.cs
git commit -m "feat(controls): ProceduralBuilders 共享 RectTransform/Image/TMP_Text 构造 helpers"
```

---

## Task 2: IControl.Get<T> path API

**Files:**
- Modify: `Runtime/Controls/IControl.cs`
- Modify: `Runtime/Controls/Control.cs`
- Test: `Tests/EditMode/Controls/IControlGetPathTests.cs`

- [x] **Step 1: Write the failing test**

Create `Tests/EditMode/Controls/IControlGetPathTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class IControlGetPathTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Get_path_walks_scoped_ids_inside_template_instance()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <HStack>
      <Text id='label'>hi</Text>
    </HStack>
  </Template>
  <Screen name='S'>
    <Row id='row'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");

            var row = screen.Get("row");
            var label = row.Get<Text>("label");

            Assert.IsNotNull(label);
            Assert.AreSame(label, screen.Get<Text>("row/label"));
        }

        [Test]
        public void Get_path_throws_on_missing_segment()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <HStack><Text id='label'/></HStack>
  </Template>
  <Screen name='S'><Row id='row'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var row = screen.Get("row");

            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => row.Get<Text>("nope"));
        }
    }
}
```

- [x] **Step 2: Run test to verify it fails**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IControlGetPathTests")
```

Expected: FAIL — `IControl` has no `Get` member (compile error or method-missing).

- [x] **Step 2: Run test to verify it fails** (verified compile errors before adding API)

- [x] **Step 3: Add `Get` to IControl**

Edit `Runtime/Controls/IControl.cs` — append after `ScopedIds`:

```csharp
        /// <summary>路径查询：a/b/c 沿 ScopedIds 下钻；与 IScreen.Get 对齐。</summary>
        public T Get<T>(string idPath) where T : class, IControl;
        public IControl Get(string idPath);
```

- [x] **Step 4: Implement on Control**

Edit `Runtime/Controls/Control.cs` — add inside the class:

```csharp
        public T Get<T>(string idPath) where T : class, IControl
        {
            var c = Get(idPath);
            return c as T ?? throw new System.InvalidCastException(
                $"control at '{idPath}' is {c?.GetType().Name ?? "null"}, not {typeof(T).Name}");
        }

        public IControl Get(string idPath)
        {
            if (string.IsNullOrEmpty(idPath))
                throw new System.ArgumentException("idPath is empty");
            var segs = idPath.Split('/');
            IControl current = this;
            foreach (var seg in segs)
            {
                if (!current.ScopedIds.TryGetValue(seg, out var next))
                    throw new System.Collections.Generic.KeyNotFoundException(
                        $"id '{seg}' not found under '{current.Id ?? current.GameObject?.name}'");
                current = next;
            }
            return current;
        }
```

- [x] **Step 5: Run test, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IControlGetPathTests")
```

Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add Runtime/Controls/IControl.cs Runtime/Controls/Control.cs Tests/EditMode/Controls/IControlGetPathTests.cs
git commit -m "feat(controls): IControl.Get<T>(idPath) 路径 API；走 ScopedIds 下钻"
```

---

## Task 3: ToggleGroupRegistry (Screen-scoped) [DONE]

**Files:**
- Create: `Runtime/Controls/Internal/ToggleGroupRegistry.cs`
- Modify: `Runtime/Application/Screen.cs`

- [x] **Step 1: Write the registry**

Create `Runtime/Controls/Internal/ToggleGroupRegistry.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>Screen 级别的 string→ToggleGroup 缓存；同 group 名共享一个 ToggleGroup 组件。</summary>
    internal sealed class ToggleGroupRegistry
    {
        private readonly Transform _hostParent;
        private readonly Dictionary<string, ToggleGroup> _groups = new();

        public ToggleGroupRegistry(Transform hostParent) { _hostParent = hostParent; }

        public ToggleGroup GetOrCreate(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_groups.TryGetValue(name, out var g) && g != null) return g;
            var go = new GameObject($"ToggleGroup:{name}", typeof(RectTransform));
            go.transform.SetParent(_hostParent, worldPositionStays: false);
            g = go.AddComponent<ToggleGroup>();
            _groups[name] = g;
            return g;
        }

        public void Clear() => _groups.Clear();
    }
}
```

- [x] **Step 2: Wire registry into Screen**

In `Runtime/Application/Screen.cs`, find the field block (around the `_byId` declaration) and add:

```csharp
        internal Internal.ToggleGroupRegistry ToggleGroups { get; private set; }
```

(use `using PromptUGUI.Controls;` already in scope; `Internal` is the sub-namespace)

In the constructor / `Open` flow — locate where `_byId` is reset and add:

```csharp
            ToggleGroups = new Internal.ToggleGroupRegistry(_root.transform);
```

In the `Close()` / cleanup path — alongside `_byId.Clear()`:

```csharp
            ToggleGroups?.Clear();
            ToggleGroups = null;
```

> If the existing Screen.cs structure differs from these landmark names, follow the same lifecycle hooks: create on Screen instantiation, clear on close. Re-creation on `ReSolve` is unnecessary — registry survives variant ReSolve like the other Screen-scoped state.

- [x] **Step 3: Refresh + verify compiles**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: no errors. (Registry has no callers yet — Toggle wires it up next task.)

- [x] **Step 4: Commit**

```bash
git add Runtime/Controls/Internal/ToggleGroupRegistry.cs Runtime/Application/Screen.cs
git commit -m "feat(controls): Screen 级 ToggleGroupRegistry，按 group 名复用 ToggleGroup"
```

---

## Task 4: Toggle control [DONE]

**Files:**
- Create: `Runtime/Controls/Toggle.cs`
- Modify: `Runtime/Application/BuiltinPrimitives.cs`
- Modify: `Runtime/Application/UI.cs` (added `OwnerScreenOf`; register Screen in `_open` before `screen.Open()` so transform-tree owner lookup works during instantiation)
- Modify: `Runtime/Application/Screen.cs` (assign `RootGameObject` before `InstantiateInto` so OwnerScreenOf resolves during attribute application)
- Modify: `Tests/EditMode/PromptUGUI.Tests.EditMode.asmdef` (added `Unity.TextMeshPro` reference for TMP_Text assertion)
- Test: `Tests/EditMode/Controls/ToggleTests.cs`

- [x] **Step 1: Write the failing test**

Create `Tests/EditMode/Controls/ToggleTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ToggleTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Parses_isOn_initial_value()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='t' isOn='true'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            Assert.IsTrue(screen.Get<Toggle>("t").IsOn);
        }

        [Test]
        public void Setter_triggers_OnValueChanged()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='t'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var t = screen.Get<Toggle>("t");

            bool? last = null;
            t.OnValueChanged.Subscribe(v => last = v);
            t.IsOn = true;
            Assert.AreEqual(true, last);
        }

        [Test]
        public void Same_group_is_mutually_exclusive()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack>
    <Toggle id='a' group='g' isOn='true'/>
    <Toggle id='b' group='g'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var a = screen.Get<Toggle>("a");
            var b = screen.Get<Toggle>("b");

            b.IsOn = true;
            Assert.IsTrue(b.IsOn);
            Assert.IsFalse(a.IsOn, "selecting b in same group should clear a");
        }

        [Test]
        public void Default_text_attr_routes_text_content()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='t'>静音</Toggle>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var t = screen.Get<Toggle>("t");
            // Toggle constructs a TMP_Text label child; verify text reached it.
            var labels = t.GameObject.GetComponentsInChildren<TMPro.TMP_Text>();
            Assert.That(labels, Has.Some.Property("text").EqualTo("静音"));
        }
    }
}
```

- [x] **Step 2: Run test to verify it fails**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ToggleTests")
```

Expected: FAIL — `Toggle` type missing.

- [x] **Step 2: Run test to verify it fails** (verified compile errors before implementing)

- [x] **Step 3: Implement Toggle**

Create `Runtime/Controls/Toggle.cs`:

```csharp
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;
using UnityToggle = UnityEngine.UI.Toggle;

namespace PromptUGUI.Controls
{
    public sealed class Toggle : Control
    {
        private UnityImage _bg;
        private UnityImage _checkmark;
        private UnityToggle _toggle;
        private TMP_Text _label;
        private string _fontType = "default";
        private string _groupName;
        private readonly Subject<bool> _changed = new();

        public override void OnAttached()
        {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _toggle = GameObject.GetComponent<UnityToggle>() ?? GameObject.AddComponent<UnityToggle>();
            _toggle.targetGraphic = _bg;

            _checkmark = ProceduralBuilders.AddImage(RectTransform, "Checkmark", raycast: false);
            _toggle.graphic = _checkmark;

            _label = ProceduralBuilders.AddText(RectTransform, "Label");
            ApplyFont();

            _toggle.onValueChanged.AddListener(v => _changed.OnNext(v));
            PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
        }

        private void ApplyFont()
        {
            if (_label == null) return;
            var settings = PromptUGUI.Application.PromptUGUISettings.Instance;
            var locale = PromptUGUI.Application.UI.Locale.Current;
            var asset = settings?.ResolveFont(locale, _fontType);
            if (asset != null) _label.font = asset;
        }

        [UIAttr]
        public string Text
        {
            set
            {
                if (_label != null) _label.text = value ?? "";
            }
        }

        [UIAttr]
        public string Font
        {
            set
            {
                _fontType = string.IsNullOrEmpty(value) ? "default" : value;
                ApplyFont();
            }
        }

        [UIAttr]
        public string Color
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (ColorUtility.TryParseHtmlString(value, out var c)) _bg.color = c;
            }
        }

        [UIAttr]
        public string Sprite
        {
            set
            {
                if (string.IsNullOrEmpty(value)) { _checkmark.sprite = null; return; }
                _checkmark.sprite = Resources.Load<Sprite>(value);
            }
        }

        [UIAttr]
        public bool IsOn
        {
            get => _toggle.isOn;
            set => _toggle.isOn = value;
        }

        [UIAttr]
        public string Group
        {
            set
            {
                _groupName = value;
                if (string.IsNullOrEmpty(value)) { _toggle.group = null; return; }
                var screen = PromptUGUI.Application.UI.OwnerScreenOf(this) as Screen;
                _toggle.group = screen?.ToggleGroups.GetOrCreate(value);
            }
        }

        public Observable<bool> OnValueChanged => _changed;

        public override void Dispose()
        {
            PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
            _changed.Dispose();
            base.Dispose();
        }
    }
}
```

> **Note**: `UI.OwnerScreenOf(IControl)` is needed for the Toggle to find its host Screen. If it doesn't exist, add it to `UI.cs` as a static helper that walks `Application.Screens` and finds the screen whose `_byId.Values.Contains(control)` or `NodeMap.Values.Contains(control)`. If implementation churn is high, alternative is a `Control._owner` field set by `Screen` after instantiation. Pick whichever is least intrusive in the current codebase.

- [x] **Step 4: Register Toggle**

Edit `Runtime/Application/BuiltinPrimitives.cs`, add inside `Register`:

```csharp
            reg.Register<Toggle>("Toggle", null, defaultTextAttr: "text");
```

- [x] **Step 5: Run tests, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ToggleTests")
```

Expected: 4 PASS.

- [x] **Step 6: Commit**

```bash
git add Runtime/Controls/Toggle.cs Runtime/Application/BuiltinPrimitives.cs Tests/EditMode/Controls/ToggleTests.cs
git commit -m "feat(controls): Toggle 控件，OnValueChanged 流 + group 互斥 + Tr 接入"
```

---

## Task 5: Slider control [DONE]

**Files:**
- Create: `Runtime/Controls/Slider.cs`
- Modify: `Runtime/Application/BuiltinPrimitives.cs`
- Test: `Tests/EditMode/Controls/SliderTests.cs`

- [x] **Step 1: Write the failing test**

Create `Tests/EditMode/Controls/SliderTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class SliderTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Parses_min_max_value()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Slider id='s' min='0' max='10' value='3'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var s = screen.Get<Slider>("s");
            Assert.AreEqual(3f, s.Value);
        }

        [Test]
        public void Setter_triggers_OnValueChanged()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Slider id='s' min='0' max='1'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var s = screen.Get<Slider>("s");
            float? last = null;
            s.OnValueChanged.Subscribe(v => last = v);
            s.Value = 0.5f;
            Assert.AreEqual(0.5f, last);
        }

        [Test]
        public void Direction_parses_to_unity_enum()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Slider id='s' direction='vertical'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var s = screen.Get<Slider>("s");
            var u = s.GameObject.GetComponent<UnityEngine.UI.Slider>();
            Assert.AreEqual(UnityEngine.UI.Slider.Direction.BottomToTop, u.direction);
        }
    }
}
```

- [x] **Step 2: Run test to verify it fails**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SliderTests")
```

Expected: FAIL — `Slider` type missing.

- [x] **Step 3: Implement Slider**

Create `Runtime/Controls/Slider.cs`:

```csharp
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;
using UnitySlider = UnityEngine.UI.Slider;

namespace PromptUGUI.Controls
{
    public sealed class Slider : Control
    {
        private UnityImage _bg;
        private UnityImage _fill;
        private UnityImage _handle;
        private UnitySlider _slider;
        private readonly Subject<float> _changed = new();

        public override void OnAttached()
        {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            var fillArea = ProceduralBuilders.AddChild(RectTransform, "FillArea");
            _fill = ProceduralBuilders.AddImage(fillArea, "Fill", raycast: false);
            var handleArea = ProceduralBuilders.AddChild(RectTransform, "HandleArea");
            _handle = ProceduralBuilders.AddImage(handleArea, "Handle", raycast: false);

            _slider = GameObject.GetComponent<UnitySlider>() ?? GameObject.AddComponent<UnitySlider>();
            _slider.targetGraphic = _handle;
            _slider.fillRect = _fill.rectTransform;
            _slider.handleRect = _handle.rectTransform;
            _slider.direction = UnitySlider.Direction.LeftToRight;

            _slider.onValueChanged.AddListener(v => _changed.OnNext(v));
        }

        [UIAttr] public float Min { set => _slider.minValue = value; }
        [UIAttr] public float Max { set => _slider.maxValue = value; }
        [UIAttr] public float Value
        {
            get => _slider.value;
            set => _slider.value = value;
        }
        [UIAttr] public bool WholeNumbers { set => _slider.wholeNumbers = value; }

        [UIAttr]
        public string Direction
        {
            set
            {
                _slider.direction = value switch
                {
                    "horizontal" => UnitySlider.Direction.LeftToRight,
                    "vertical" => UnitySlider.Direction.BottomToTop,
                    "reverse-horizontal" => UnitySlider.Direction.RightToLeft,
                    "reverse-vertical" => UnitySlider.Direction.TopToBottom,
                    _ => throw new System.ArgumentException(
                        $"Slider.direction='{value}' invalid; expected horizontal|vertical|reverse-horizontal|reverse-vertical"),
                };
            }
        }

        [UIAttr]
        public string Color
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (ColorUtility.TryParseHtmlString(value, out var c)) _bg.color = c;
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

        public Observable<float> OnValueChanged => _changed;

        public override void Dispose()
        {
            _changed.Dispose();
            base.Dispose();
        }
    }
}
```

- [x] **Step 4: Register Slider**

Edit `Runtime/Application/BuiltinPrimitives.cs`, append:

```csharp
            reg.Register<Slider>("Slider", null);
```

- [x] **Step 5: Run tests, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SliderTests")
```

Expected: 3 PASS.

- [x] **Step 6: Commit**

```bash
git add Runtime/Controls/Slider.cs Runtime/Application/BuiltinPrimitives.cs Tests/EditMode/Controls/SliderTests.cs
git commit -m "feat(controls): Slider 控件，min/max/value + direction + OnValueChanged 流"
```

---

## Task 6: Dropdown control [DONE]

**Files:**
- Create: `Runtime/Controls/Internal/DropdownOption.cs`
- Create: `Runtime/Controls/Dropdown.cs`
- Modify: `Runtime/Application/BuiltinPrimitives.cs`
- Test: `Tests/EditMode/Controls/DropdownTests.cs`

- [x] **Step 1: Write DropdownOption POCO**

Create `Runtime/Controls/Internal/DropdownOption.cs`:

```csharp
using UnityEngine;

namespace PromptUGUI.Controls
{
    public readonly struct DropdownOption
    {
        public readonly string Text;
        public readonly Sprite Icon;
        public DropdownOption(string text, Sprite icon = null)
        {
            Text = text;
            Icon = icon;
        }
    }
}
```

- [x] **Step 2: Write the failing tests**

Create `Tests/EditMode/Controls/DropdownTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class DropdownTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void BindOptions_strings_populates_tmp_dropdown()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var d = screen.Get<Dropdown>("d");

            d.BindOptions(Observable.Return<IEnumerable<string>>(new[] { "Low", "High" }));
            var tmp = d.GameObject.GetComponentInChildren<TMPro.TMP_Dropdown>();
            Assert.AreEqual(2, tmp.options.Count);
            Assert.AreEqual("Low", tmp.options[0].text);
        }

        [Test]
        public void OnSelected_fires_when_value_setter_changes()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var d = screen.Get<Dropdown>("d");
            d.BindOptions(Observable.Return<IEnumerable<string>>(new[] { "A", "B", "C" }));
            int? last = null;
            d.OnSelected.Subscribe(i => last = i);
            d.Value = 2;
            Assert.AreEqual(2, last);
        }
    }
}
```

- [x] **Step 3: Run test to verify it fails**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="DropdownTests")
```

Expected: FAIL — `Dropdown` type missing.

- [x] **Step 4: Implement Dropdown**

Create `Runtime/Controls/Dropdown.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class Dropdown : Control
    {
        private UnityImage _bg;
        private TMP_Dropdown _tmp;
        private string _fontType = "default";
        private readonly Subject<int> _selected = new();

        public override void OnAttached()
        {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _tmp = GameObject.AddComponent<TMP_Dropdown>();
            _tmp.targetGraphic = _bg;

            // Construct minimum required children for TMP_Dropdown:
            // Label + Arrow + Template (Viewport > Content > Item).
            var label = ProceduralBuilders.AddText(RectTransform, "Label");
            _tmp.captionText = label;

            var template = ProceduralBuilders.AddChild(RectTransform, "Template");
            template.gameObject.SetActive(false);
            template.gameObject.AddComponent<UnityImage>();
            var viewport = ProceduralBuilders.AddChild(template, "Viewport");
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>();
            viewport.gameObject.AddComponent<UnityImage>();
            var content = ProceduralBuilders.AddChild(viewport, "Content");
            var item = ProceduralBuilders.AddChild(content, "Item");
            var itemBg = item.gameObject.AddComponent<UnityImage>();
            var itemToggle = item.gameObject.AddComponent<UnityEngine.UI.Toggle>();
            itemToggle.targetGraphic = itemBg;
            var itemLabel = ProceduralBuilders.AddText(item, "Item Label");
            _tmp.template = template;
            _tmp.itemText = itemLabel;

            _tmp.onValueChanged.AddListener(i => _selected.OnNext(i));
            ApplyFont();
            PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
        }

        private void ApplyFont()
        {
            if (_tmp?.captionText == null) return;
            var settings = PromptUGUI.Application.PromptUGUISettings.Instance;
            var locale = PromptUGUI.Application.UI.Locale.Current;
            var asset = settings?.ResolveFont(locale, _fontType);
            if (asset != null)
            {
                _tmp.captionText.font = asset;
                if (_tmp.itemText != null) _tmp.itemText.font = asset;
            }
        }

        [UIAttr]
        public int Value
        {
            get => _tmp.value;
            set => _tmp.value = value;
        }

        [UIAttr]
        public string Color
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (ColorUtility.TryParseHtmlString(value, out var c)) _bg.color = c;
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
            set
            {
                _fontType = string.IsNullOrEmpty(value) ? "default" : value;
                ApplyFont();
            }
        }

        public Observable<int> OnSelected => _selected;

        public IDisposable BindOptions(Observable<IEnumerable<string>> source) =>
            source.Subscribe(seq => SetOptions(seq.Select(s => new DropdownOption(s)).ToList()));

        public IDisposable BindOptions(Observable<IEnumerable<DropdownOption>> source) =>
            source.Subscribe(seq => SetOptions(seq.ToList()));

        private void SetOptions(List<DropdownOption> opts)
        {
            var wasOpen = _tmp.IsExpanded;
            if (wasOpen) _tmp.Hide();

            _tmp.options.Clear();
            foreach (var o in opts)
                _tmp.options.Add(new TMP_Dropdown.OptionData(o.Text ?? "", o.Icon));
            _tmp.RefreshShownValue();

            if (wasOpen) _tmp.Show();
        }

        public override void Dispose()
        {
            PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
            _selected.Dispose();
            base.Dispose();
        }
    }
}
```

- [x] **Step 5: Register Dropdown**

Edit `Runtime/Application/BuiltinPrimitives.cs`, append:

```csharp
            reg.Register<Dropdown>("Dropdown", null);
```

- [x] **Step 6: Run tests, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="DropdownTests")
```

Expected: 2 PASS.

- [x] **Step 7: Commit**

```bash
git add Runtime/Controls/Dropdown.cs Runtime/Controls/Internal/DropdownOption.cs Runtime/Application/BuiltinPrimitives.cs Tests/EditMode/Controls/DropdownTests.cs
git commit -m "feat(controls): Dropdown 控件，BindOptions 推送选项 + OnSelected 流"
```

---

## Task 7: ScreenInstantiator.InstantiateNode public entry [DONE]

**Files:**
- Modify: `Runtime/Application/ScreenInstantiator.cs`

- [x] **Step 1: Write the failing test**

Append to `Tests/EditMode/Controls/IControlGetPathTests.cs` (or create `Tests/EditMode/Application/InstantiateNodeTests.cs`):

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Application
{
    public class InstantiateNodeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Public_InstantiateNode_creates_subtree_under_parent()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack><Text id='label'>x</Text></HStack></Template>
  <Screen name='S'/>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var doc = UI.GetLoadedDoc("test");
            var rowDef = doc.Templates[(null, "Row")];

            var instantiator = UI.GetInstantiator();
            var parent = new GameObject("Host", typeof(RectTransform));
            var root = instantiator.InstantiateNode(rowDef.Body, ((RectTransform)parent.transform), screen);

            Assert.IsNotNull(root);
            Assert.AreEqual(parent.transform, root.GameObject.transform.parent);
            Assert.IsNotNull(root.Get<Text>("label"));
        }
    }
}
```

> **Implementation note**: `UI.GetLoadedDoc` and `UI.GetInstantiator` are existing internals (or near-internals). If they aren't already exposed, add them as `internal static` accessors on `UI.cs` for test consumption — they already exist as private state. Adjust the test to use whatever access path is least invasive.

- [x] **Step 2: Run test to verify it fails**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="InstantiateNodeTests")
```

Expected: FAIL — `InstantiateNode` method missing.

- [x] **Step 3: Add public entry to ScreenInstantiator**

Edit `Runtime/Application/ScreenInstantiator.cs`, add a new public method that wraps `InstantiateRecursive`:

```csharp
        /// <summary>
        /// 单节点子树实例化（用于 ScrollList 这类需要按数据动态实例化模板的控件）。
        /// 节点内的 id 写入 outerScope（通常是 caller 自己的 ScopedIds），不污染 Screen._byId。
        /// </summary>
        public IControl InstantiateNode(ElementNode node, RectTransform parent, Screen owner)
        {
            var scope = new Dictionary<string, IControl>();
            var nodeMap = new Dictionary<ElementNode, Control>();
            var parentIsLayoutGroup = parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null;

            int prevChildCount = parent.childCount;
            InstantiateRecursive(node, parent, parentIsLayoutGroup, scope, nodeMap);

            // 取最后追加的子项作为根（InstantiateRecursive 会在 parent 末尾追加 1 个）
            var rootGo = parent.GetChild(prevChildCount).gameObject;
            // 找到对应 Control：在 nodeMap 里找 GameObject = rootGo 的那个
            foreach (var kv in nodeMap)
                if (kv.Value.GameObject == rootGo)
                    return kv.Value;
            return null;
        }
```

> **About `owner`**: parameter is reserved for future hooks (e.g., per-screen registries Toggle/ScrollList query). If unused right now, prefix `owner` with `_ =` or simply keep it for the API stability — caller already has the reference.

- [x] **Step 4: Run test, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="InstantiateNodeTests")
```

Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add Runtime/Application/ScreenInstantiator.cs Tests/EditMode/Application/InstantiateNodeTests.cs
git commit -m "feat(application): ScreenInstantiator.InstantiateNode 公开入口，供 ScrollList 复用"
```

---

## Task 8: ScrollList control [DONE]

**Files:**
- Create: `Runtime/Controls/ScrollList.cs`
- Modify: `Runtime/Application/BuiltinPrimitives.cs`
- Test: `Tests/EditMode/Controls/ScrollListTests.cs`

- [x] **Step 1: Write the failing tests**

Create `Tests/EditMode/Controls/ScrollListTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ScrollListTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void BindItems_template_creates_one_slot_per_data_item()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack><Text id='label'>x</Text></HStack></Template>
  <Screen name='S'><ScrollList id='list' itemTemplate='Row'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");

            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "a", "b", "c" }),
                (IControl slot, string s) => slot.Get<Text>("label").TextValue = s);

            Assert.AreEqual(3, list.SlotCount);
        }

        [Test]
        public void BindItems_rebuild_replaces_slots()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack><Text id='label'/></HStack></Template>
  <Screen name='S'><ScrollList id='list' itemTemplate='Row'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");

            var src = new ReactiveProperty<IReadOnlyList<string>>(new[] { "a", "b" });
            list.BindItems(src, (IControl slot, string s) => slot.Get<Text>("label").TextValue = s);
            Assert.AreEqual(2, list.SlotCount);

            src.Value = new[] { "x" };
            Assert.AreEqual(1, list.SlotCount);
        }

        [Test]
        public void Unknown_itemTemplate_throws_at_screen_open()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><ScrollList id='list' itemTemplate='Nope'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            Assert.Throws<PromptUGUI.IR.ParseException>(() => UI.Open("S"));
        }
    }
}
```

- [x] **Step 2: Run test to verify it fails**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScrollListTests")
```

Expected: FAIL — `ScrollList` type missing.

- [x] **Step 3: Implement ScrollList**

Create `Runtime/Controls/ScrollList.cs`:

```csharp
using System;
using System.Collections.Generic;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.IR;
using PromptUGUI.Registry;
using R3;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class ScrollList : Control
    {
        private UnityImage _bg;
        private ScrollRect _scroll;
        private RectTransform _content;
        private LayoutGroup _layoutGroup;
        private string _direction = "vertical";
        private string _itemTemplate;
        private float _spacing;
        private string _padding;
        private Func<RectTransform, IControl> _factory;
        private readonly List<IControl> _slots = new();

        public int SlotCount => _slots.Count;

        public override void OnAttached()
        {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _scroll = GameObject.GetComponent<ScrollRect>() ?? GameObject.AddComponent<ScrollRect>();

            var viewport = ProceduralBuilders.AddChild(RectTransform, "Viewport");
            viewport.gameObject.AddComponent<UnityImage>().color = new Color(1, 1, 1, 0);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            _scroll.viewport = viewport;

            _content = ProceduralBuilders.AddChild(viewport, "Content");
            _scroll.content = _content;
            ApplyDirection();
        }

        private void ApplyDirection()
        {
            if (_layoutGroup != null) UnityEngine.Object.Destroy(_layoutGroup);
            var fitter = _content.GetComponent<ContentSizeFitter>()
                         ?? _content.gameObject.AddComponent<ContentSizeFitter>();

            if (_direction == "horizontal")
            {
                _scroll.horizontal = true;
                _scroll.vertical = false;
                _layoutGroup = _content.gameObject.AddComponent<HorizontalLayoutGroup>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            }
            else
            {
                _scroll.horizontal = false;
                _scroll.vertical = true;
                _layoutGroup = _content.gameObject.AddComponent<VerticalLayoutGroup>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            ApplySpacingPadding();
        }

        private void ApplySpacingPadding()
        {
            switch (_layoutGroup)
            {
                case HorizontalLayoutGroup h: h.spacing = _spacing; break;
                case VerticalLayoutGroup v:   v.spacing = _spacing; break;
            }
            // padding 字符串: "X" | "V,H" | "T,R,B,L"
            if (string.IsNullOrEmpty(_padding) || _layoutGroup == null) return;
            var parts = _padding.Split(',');
            int t = 0, r = 0, b = 0, l = 0;
            switch (parts.Length)
            {
                case 1: int.TryParse(parts[0], out t); r = b = l = t; break;
                case 2:
                    int.TryParse(parts[0], out t); b = t;
                    int.TryParse(parts[1], out r); l = r; break;
                case 4:
                    int.TryParse(parts[0], out t);
                    int.TryParse(parts[1], out r);
                    int.TryParse(parts[2], out b);
                    int.TryParse(parts[3], out l); break;
            }
            _layoutGroup.padding = new RectOffset(l, r, t, b);
        }

        [UIAttr]
        public string ItemTemplate
        {
            set
            {
                _itemTemplate = value;
                _factory = ResolveFactory(value);
            }
        }

        [UIAttr]
        public string Direction
        {
            set { _direction = string.IsNullOrEmpty(value) ? "vertical" : value; ApplyDirection(); }
        }

        [UIAttr]
        public float Spacing { set { _spacing = value; ApplySpacingPadding(); } }

        [UIAttr]
        public string Padding { set { _padding = value; ApplySpacingPadding(); } }

        [UIAttr]
        public string Color
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (ColorUtility.TryParseHtmlString(value, out var c)) _bg.color = c;
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

        private Func<RectTransform, IControl> ResolveFactory(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            // 1) Template
            var doc = PromptUGUI.Application.UI.GetCurrentDoc();
            if (doc != null && doc.TryFindTemplate(tag, out var tplBody))
            {
                var instantiator = PromptUGUI.Application.UI.GetInstantiator();
                var owner = PromptUGUI.Application.UI.OwnerScreenOf(this) as Screen;
                return parent => instantiator.InstantiateNode(tplBody, parent, owner);
            }
            // 2) Control class
            if (PromptUGUI.Application.UI.Registry.TryResolve(tag, out var entry))
            {
                var owner = PromptUGUI.Application.UI.OwnerScreenOf(this) as Screen;
                return parent =>
                {
                    var instantiator = PromptUGUI.Application.UI.GetInstantiator();
                    // 用单元素 ElementNode 包一层，复用 InstantiateNode 路径
                    var node = new ElementNode { Tag = tag };
                    return instantiator.InstantiateNode(node, parent, owner);
                };
            }
            throw new ParseException(
                $"<ScrollList itemTemplate='{tag}'>: tag is neither a registered Control nor a Template");
        }

        public IDisposable BindItems<T, TSlot>(
            Observable<IReadOnlyList<T>> source,
            Action<TSlot, T> bind)
            where TSlot : class, IControl =>
            source.Subscribe(items => Rebuild(items, bind));

        public IDisposable BindItems<T>(
            Observable<IReadOnlyList<T>> source,
            Action<IControl, T> bind) =>
            BindItems<T, IControl>(source, bind);

        private void Rebuild<T, TSlot>(IReadOnlyList<T> items, Action<TSlot, T> bind)
            where TSlot : class, IControl
        {
            if (_factory == null)
                throw new InvalidOperationException(
                    "ScrollList.itemTemplate must be set before BindItems is called");

            ClearSlots();
            for (int i = 0; i < items.Count; i++)
            {
                var slot = _factory(_content);
                _slots.Add(slot);
                if (slot is TSlot typed) bind(typed, items[i]);
                else throw new InvalidCastException(
                    $"itemTemplate='{_itemTemplate}' instantiated {slot.GetType().Name}, " +
                    $"but BindItems expected {typeof(TSlot).Name}");
            }
        }

        private void ClearSlots()
        {
            foreach (var s in _slots)
            {
                s.Dispose();
                if (s.GameObject != null) UnityEngine.Object.Destroy(s.GameObject);
            }
            _slots.Clear();
        }

        public override void Dispose()
        {
            ClearSlots();
            base.Dispose();
        }
    }
}
```

> **About `UI.GetCurrentDoc` / `UI.GetInstantiator` / `UI.OwnerScreenOf` / `LoadedDoc.TryFindTemplate`**: implement these as thin internal accessors over existing private state. If the codebase already has equivalents under different names, reuse them. If not, add the smallest possible accessor (no public surface).

- [x] **Step 4: Register ScrollList**

Edit `Runtime/Application/BuiltinPrimitives.cs`, append:

```csharp
            reg.Register<ScrollList>("ScrollList", null);
```

- [x] **Step 5: Run tests, verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScrollListTests")
```

Expected: 3 PASS.

- [x] **Step 6: Run full EditMode suite**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

Expected: all green; no regressions in earlier M1–M4 tests.

- [x] **Step 7: Commit**

```bash
git add Runtime/Controls/ScrollList.cs Runtime/Application/BuiltinPrimitives.cs Tests/EditMode/Controls/ScrollListTests.cs
git commit -m "feat(controls): ScrollList，itemTemplate→工厂解析 + BindItems 全量重建"
```

---

## Task 9: PlayMode integration tests [DONE]

**Files:**
- Create: `Tests/PlayMode/Controls/CommonControlsPlayTests.cs`

- [x] **Step 1: Write the integration tests**

Create `Tests/PlayMode/Controls/CommonControlsPlayTests.cs`:

```csharp
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    public class CommonControlsPlayTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator Toggle_group_runtime_switching()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack>
    <Toggle id='a' group='g' isOn='true'/>
    <Toggle id='b' group='g'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var a = screen.Get<Toggle>("a");
            var b = screen.Get<Toggle>("b");

            yield return null;  // give Unity one frame to wire up ToggleGroup

            b.IsOn = true;
            yield return null;
            Assert.IsFalse(a.IsOn);
            Assert.IsTrue(b.IsOn);
        }

        [UnityTest]
        public IEnumerator ScrollList_renders_via_real_layout()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack height='32'><Text id='label'/></HStack></Template>
  <Screen name='S'>
    <ScrollList id='list' anchor='center' size='400x300' itemTemplate='Row'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");
            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "alpha", "beta", "gamma" }),
                (IControl slot, string s) => slot.Get<Text>("label").TextValue = s);

            yield return null;
            yield return null;
            Assert.AreEqual(3, list.SlotCount);
        }
    }
}
```

- [x] **Step 2: Run PlayMode tests**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="CommonControlsPlayTests")
```

Expected: 2 PASS.

- [x] **Step 3: Commit**

```bash
git add Tests/PlayMode/Controls/CommonControlsPlayTests.cs
git commit -m "test(playmode): Toggle group + ScrollList 真实 layout 路径冒烟"
```

---

## Task 10: Sample (CommonControls demo) [DONE]

**Files:**
- Create: `Samples~/CommonControls/CommonControlsRunner.cs`
- Create: `Samples~/CommonControls/Resources/UI/Settings.ui.xml`
- Modify: `package.json`

- [x] **Step 1: Write the sample XML**

Create `Samples~/CommonControls/Resources/UI/Settings.ui.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Template name="OptionRow">
    <HStack height="32" spacing="8">
      <Text id="label" fontSize="20"/>
      <Frame width="0"/>
    </HStack>
  </Template>

  <Screen name="Settings">
    <Image anchor="stretch" color="#202020"/>
    <VStack id="root" anchor="center" size="500x600" spacing="16" padding="24">
      <Text fontSize="28" align="center">Common Controls Demo</Text>

      <Toggle id="muteAudio" group="audio">静音</Toggle>
      <Slider id="masterVol" min="0" max="1" value="0.8"/>
      <Dropdown id="quality"/>

      <ScrollList id="list" anchor="stretch" itemTemplate="OptionRow"
                  spacing="4" padding="8"/>
    </VStack>
  </Screen>
</PromptUGUI>
```

- [x] **Step 2: Write the runner MonoBehaviour**

Create `Samples~/CommonControls/CommonControlsRunner.cs`:

```csharp
using System.Collections.Generic;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;

namespace PromptUGUI.Samples.CommonControls
{
    /// <summary>
    /// 演示 Toggle / Slider / Dropdown / ScrollList 四个常用控件的 XML 写法 + R3 数据流。
    /// 使用步骤：
    ///   1. 场景里建空 GameObject，挂本组件
    ///   2. 按 Play
    /// </summary>
    public sealed class CommonControlsRunner : MonoBehaviour
    {
        void Start()
        {
            UI.UseResourcesResolver("UI");
            UI.LoadDocumentFromSrc("Settings.ui");
            var screen = UI.Open("Settings");

            screen.Get<Toggle>("muteAudio").OnValueChanged
                  .Subscribe(b => Debug.Log($"[Sample] mute = {b}")).AddTo(screen);

            screen.Get<Slider>("masterVol").OnValueChanged
                  .Subscribe(v => Debug.Log($"[Sample] master vol = {v:F2}")).AddTo(screen);

            var quality = screen.Get<Dropdown>("quality");
            quality.BindOptions(Observable.Return<IEnumerable<string>>(
                new[] { "Low", "Medium", "High", "Ultra" }));
            quality.OnSelected.Subscribe(i => Debug.Log($"[Sample] quality = {i}")).AddTo(screen);

            var list = screen.Get<ScrollList>("list");
            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[]
                {
                    "VSync", "Anti-Aliasing", "Shadows", "Texture Quality",
                    "Particles", "Reflections", "Post Processing", "Bloom",
                    "Motion Blur", "Depth of Field"
                }),
                (IControl slot, string text) =>
                {
                    slot.Get<Text>("label").TextValue = text;
                });
        }
    }
}
```

- [x] **Step 3: Add sample entry to package.json**

Edit `package.json`, append the new entry inside the `samples` array:

```json
  "samples": [
    {
      "displayName": "Main Menu Demo",
      "description": "Hello-world: 主菜单 + 三按钮 + R3 点击订阅",
      "path": "Samples~/MainMenu"
    },
    {
      "displayName": "Common Controls Demo",
      "description": "Toggle / Slider / Dropdown / ScrollList 四个常用控件示例",
      "path": "Samples~/CommonControls"
    }
  ]
```

- [x] **Step 4: Refresh + verify the sample compiles**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: no errors. (`Samples~` is excluded from main asmdef compilation but the C# itself should be syntactically valid.)

- [x] **Step 5: Commit**

```bash
git add Samples~/CommonControls/ package.json
git commit -m "feat(sample): CommonControls demo (Toggle/Slider/Dropdown/ScrollList)"
```

---

## Task 11: Doc updates (master spec + SKILL.md) [DONE]

**Files:**
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`

- [x] **Step 1: Update master spec §5 primitives table**

Open `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`. In §5, change the "8 个原语" wording to "12 个原语" and append four rows to the table:

```markdown
| `<Toggle>` | 复选 / 单选（OnValueChanged: bool；group= 字符串键互斥） | Image + Toggle (uGUI) + 内置 label |
| `<Slider>` | 数值滑块（OnValueChanged: float） | Image + Slider (uGUI) |
| `<Dropdown>` | 下拉选择（OnSelected: int；BindOptions 推送选项） | TMP_Dropdown |
| `<ScrollList>` | 滚动列表（BindItems 推送数据；itemTemplate 引用 Template/Control 类） | ScrollRect + Mask |
```

Update the descriptive paragraph below the table — replace:

> 不开 `<Toggle>`、`<Slider>`、`<Dropdown>`、`<ScrollList>` 等更复杂的原语。这些**必须**由代码侧注册的自定义控件提供…

with:

> `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` 默认开启的参考实现（详见 [`2026-05-09-m5-common-controls-design.md`](2026-05-09-m5-common-controls-design.md)）。视觉风格用 `sprite` / `color` 等属性表达；需要项目级强差异化样式（像素描边、按下震动等）时作者继承相应类重写 `OnAttached`。

- [x] **Step 2: Update SKILL.md primitives table**

Open `.claude/skills/authoring-promptugui-xml/SKILL.md`. Find the "内置原语" section and add four lines:

```
<Toggle group="g" isOn="false">文本</Toggle>     OnValueChanged: bool
<Slider min="0" max="1" value="0">                OnValueChanged: float
<Dropdown value="0"/>                              OnSelected: int (BindOptions 推送选项)
<ScrollList itemTemplate="TagName"/>               BindItems(IObservable<IReadOnlyList<T>>) 推送
```

- [x] **Step 3: Update SKILL.md C# bridge examples**

Append a `BindItems / BindOptions` block to the C# examples section:

```
### 列表 / 选项推送
screen.Get<Dropdown>("d")
      .BindOptions(Observable.Return(new[] {"A","B"}))
      .AddTo(screen);

screen.Get<ScrollList>("list")
      .BindItems(player.Inventory, (IControl slot, Item item) => {
          slot.Get<Text>("label").TextValue = item.Name;
      })
      .AddTo(screen);
```

- [x] **Step 4: Commit**

```bash
git add docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "doc: 主 spec §5/§10 + SKILL.md 同步 M5 四个常用控件"
```

---

## Task 12: Lint + final verification [DONE]

**Files:**
- _no edits expected_

- [x] **Step 1: Run `dotnet format` whitespace + style + analyzers**

From repo root:

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format style       PromptUGUI.Lint.slnx
dotnet format analyzers   PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: clean exit, no diff produced. If `--verify-no-changes` reports drift, run the un-verified `dotnet format` once more to absorb fixers, commit the result.

- [x] **Step 2: Run full EditMode suite**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

Expected: all green.

- [x] **Step 3: Run full PlayMode suite**

```
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

Expected: all green.

- [x] **Step 4: Run EditorOnly suite**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
```

Expected: all green.

- [x] **Step 5: Final console check**

```
mcp__UnityMCP__read_console(action="get", types=["error","warning"])
```

Expected: no new errors; warnings only from acknowledged sources (e.g., M3 Strategy C debug logs). No `[Bind]` warnings, no `[UIAttr]` warnings on the four new controls.

- [x] **Step 6: Commit any lint fixups**

```bash
git status
# if any diff:
git add -p
git commit -m "chore: lint autofix on M5 controls"
```

- [x] **Step 7: Push branch and report**

```bash
git push -u origin <branch-name>
```

Report to user with:
- Branch name and last commit SHA
- Number of tests added (per-control test class count)
- Console output of final test run
- Any deviations from the spec / decision table
- Suggested PR title: `M5: Toggle / Slider / Dropdown / ScrollList 四常用控件参考实现`

---

_Plan ends. Subagent is done when all 12 tasks have green checkboxes and the final console shows no errors._
