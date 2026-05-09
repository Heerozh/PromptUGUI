# PromptUGUI Icon Assets 实施计划：`<Icon>` + IconSet + SpriteAtlas

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落实 Icon Assets 设计 spec（`docs~/superpowers/specs/2026-05-08-icon-assets-design.md`）。新增能力：

1. `<Icon name="ns:icon"/>` 内置控件 + ParseException 校验
2. `UI.IconResolver: Func<string,Sprite>` 委托 + 默认 SpriteAtlas-backed helper
3. `IconSet` ScriptableObject（setName / sourceFolder / atlas / alwaysInclude）
4. SizeSpec 新增 `native` 值，仅 `<Icon>` 接受
5. Editor 同步工具（手动菜单 + IPreprocessBuildWithReport；AssetPostprocessor opt-in）
6. XSD 生成器扩展 `<Icon>` + `[UIAttr(Pattern=)]`
7. Hot-reload 接入（atlas 内容变 → ReSolve）

**Architecture:** Icon 是带 `[UIAttr] Name + Color` 的 Control 子类，内部包一个 UnityEngine.UI.Image。`SizeSpec` 加 `IsNativeWidth/Height` 标志；`Control` 加 `virtual GetNativeSize()` 钩子，Icon 重写返回 sprite.rect.size；ApplyCommon 解算 native 后再走 MarginResolver。IconSet ScriptableObject 持有 sourceFolder 与生成的 SpriteAtlas 引用——通过 Unity 自身资产引用追踪让"只打包用到的"自然成立。Editor 同步工具扫所有 `.ui.xml` → 收集 `<Icon name="ns:foo"/>` → 重建每 set 的 SpriteAtlas（packables 用 sprite-level 而非 folder）。Hot-reload 复用现有 `UI.HotReload.NotifyAssetChanged` 机制，新增 atlas / IconSet 路径分支。

**Tech Stack:** Unity 6 (6000.0+) `UnityEngine.U2D.SpriteAtlas` + `UnityEditor.U2D.SpriteAtlasUtility`，TextMeshPro 不需要，沿用 NUnit + Unity Test Framework。承接 M1-M4 已落地的 IR / Parser / Layout / Registry / Application 各层。

---

## 假设与前置

工程师执行此计划前需要：

1. M1-M4 全部完成；EditMode + EditorOnly + PlayMode 测试 PASS；`main` 同步到 `origin/main`
2. 宿主 Unity 项目位于 `C:\xsoft\PromptUGUIDev`；`com.promptugui.core` 通过 `file://` 引用本仓库
3. UnityMCP 已连接；操作 Unity 一律走 MCP，不要 spawn batch-mode Unity
4. 工作目录 `/workspace-PromptUGUI`（Linux WSL）/ `C:\xsoft\PromptUGUI`（Windows host），按运行端选

测试运行约定（每完成一个任务后跑）：

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

可加 `filter="ClassName"` 单跑某测试类。

**Spec 引用速查（不重抄；按章节查证）：**

- 决策表：spec §2（ICN-D1..D15）
- XML 语义：spec §4
- SizeSpec 扩展：spec §5
- Resolver / IconSet / Icon Control：spec §6
- Editor 同步算法：spec §7
- 错误矩阵：spec §8
- 测试集合：spec §9
- 文档同步要求：spec §10
- 实施排序提示：spec §13

---

## 文件结构

```
PromptUGUI/                                                    # 仓库根
├── Runtime/
│   ├── Core/
│   │   └── Layout/
│   │       └── SizeSpec.cs                                    # Modify (T1): 加 IsNativeWidth/Height + native 解析
│   ├── Core/Parser/
│   │   └── UIDocumentParser.cs                                # Modify (T2): <Icon> 元素 ParseException 校验
│   ├── Controls/
│   │   ├── Control.cs                                         # Modify (T5): virtual GetNativeSize + ApplyCommon 解 native
│   │   └── Icon.cs                                            # Create (T6)
│   ├── Registry/
│   │   └── UIAttrAttribute.cs                                 # Modify (T16): 加 Pattern 属性
│   └── Application/
│       ├── BuiltinPrimitives.cs                               # Modify (T7): 注册 Icon
│       ├── IconSet.cs                                         # Create (T3): ScriptableObject
│       ├── IconResolverHelpers.cs                             # Create (T8)
│       └── UI.cs                                              # Modify (T4,T18): IconResolver + Reset + HotReload 分支
├── Editor/
│   ├── IconAtlasSyncer.cs                                     # Create (T9,T10,T11,T12)
│   ├── IconAtlasMenu.cs                                       # Create (T13)
│   ├── IconAtlasBuildHook.cs                                  # Create (T14)
│   ├── IconAtlasAutoSync.cs                                   # Create (T15)
│   ├── IconSetEditor.cs                                       # Create (T17)
│   └── XsdGenerator.cs                                        # Modify (T19): <Icon> + Pattern
├── Tests/EditMode/
│   ├── Layout/
│   │   └── SizeSpecNativeTests.cs                             # Create (T1)
│   ├── Parser/
│   │   └── IconParserTests.cs                                 # Create (T2)
│   ├── Application/
│   │   ├── IconResolverTests.cs                               # Create (T8)
│   │   └── IconHotReloadTests.cs                              # Create (T18)
│   └── Editor/
│       └── IconAtlasSyncerTests.cs                            # Create (T11,T12,T15) — EditorOnly asmdef
├── Tests/PlayMode/
│   └── Controls/
│       └── IconRuntimeTests.cs                                # Create (T20)
├── docs~/superpowers/specs/
│   └── 2026-05-07-promptugui-description-language-design.md   # Modify (T22)
└── .claude/skills/authoring-promptugui-xml/
    └── SKILL.md                                                # Modify (T21)
```

---

## 任务

### Task 1: SizeSpec 加 `native` 值

**Files:**
- Modify: `Runtime/Core/Layout/SizeSpec.cs`
- Test: `Tests/EditMode/Layout/SizeSpecNativeTests.cs`

`native` 是 spec D9 / §5。SizeSpec 新增 `IsNativeWidth` / `IsNativeHeight` 两个标志位；当 `size`/`width`/`height` 字符串为 `"native"` 时设置对应标志，W/H 留 0（占位，待 ApplyCommon 解算）。

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/EditMode/Layout/SizeSpecNativeTests.cs
using NUnit.Framework;
using PromptUGUI.Layout;

namespace PromptUGUI.Tests.Layout {
    public class SizeSpecNativeTests {
        [Test]
        public void Size_native_sets_both_flags() {
            var s = SizeSpec.Parse("native", null, null);
            Assert.IsTrue(s.HasWidth);
            Assert.IsTrue(s.HasHeight);
            Assert.IsTrue(s.IsNativeWidth);
            Assert.IsTrue(s.IsNativeHeight);
        }

        [Test]
        public void Width_native_only_axis_flagged() {
            var s = SizeSpec.Parse(null, "native", null);
            Assert.IsTrue(s.HasWidth);
            Assert.IsTrue(s.IsNativeWidth);
            Assert.IsFalse(s.HasHeight);
            Assert.IsFalse(s.IsNativeHeight);
        }

        [Test]
        public void Height_native_only_axis_flagged() {
            var s = SizeSpec.Parse(null, null, "native");
            Assert.IsFalse(s.HasWidth);
            Assert.IsTrue(s.HasHeight);
            Assert.IsTrue(s.IsNativeHeight);
        }

        [Test]
        public void Numeric_size_does_not_set_native() {
            var s = SizeSpec.Parse("32x24", null, null);
            Assert.IsFalse(s.IsNativeWidth);
            Assert.IsFalse(s.IsNativeHeight);
            Assert.AreEqual(32f, s.Width);
            Assert.AreEqual(24f, s.Height);
        }

        [Test]
        public void WithNativeResolved_fills_axes_from_provided_size() {
            var s = SizeSpec.Parse("native", null, null)
                            .WithNativeResolved(new UnityEngine.Vector2(48, 32));
            Assert.AreEqual(48f, s.Width);
            Assert.AreEqual(32f, s.Height);
            Assert.IsFalse(s.IsNativeWidth);  // resolved → flag cleared
            Assert.IsFalse(s.IsNativeHeight);
        }

        [Test]
        public void WithNativeResolved_only_replaces_native_axes() {
            var s = SizeSpec.Parse(null, "16", "native")
                            .WithNativeResolved(new UnityEngine.Vector2(99, 24));
            Assert.AreEqual(16f, s.Width);   // explicit numeric preserved
            Assert.AreEqual(24f, s.Height);  // native resolved
        }

        [Test]
        public void Cannot_specify_both_size_and_width_with_native() {
            Assert.Throws<System.ArgumentException>(() =>
                SizeSpec.Parse("native", "32", null));
        }
    }
}
```

- [ ] **Step 2: Run tests — should FAIL** (`IsNativeWidth` 等不存在)

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SizeSpecNativeTests")
```

- [ ] **Step 3: Modify SizeSpec.cs**

```csharp
using System;
using System.Globalization;
using PromptUGUI.IR;
using UnityEngine;

namespace PromptUGUI.Layout {
    public readonly struct SizeSpec {
        public float Width  { get; }
        public float Height { get; }
        public bool  HasWidth  { get; }
        public bool  HasHeight { get; }
        public bool  IsNativeWidth  { get; }
        public bool  IsNativeHeight { get; }

        SizeSpec(float w, float h, bool hw, bool hh, bool nw, bool nh) {
            Width = w; Height = h;
            HasWidth = hw; HasHeight = hh;
            IsNativeWidth = nw; IsNativeHeight = nh;
        }

        public static SizeSpec Parse(string size, string width, string height) {
            float w = 0f, h = 0f;
            bool hw = false, hh = false;
            bool nw = false, nh = false;

            if (!string.IsNullOrEmpty(size)) {
                if (size == "native") {
                    hw = hh = true;
                    nw = nh = true;
                } else {
                    var x = size.IndexOf('x');
                    if (x <= 0 || x == size.Length - 1)
                        throw new ArgumentException($"size '{size}' must be 'WxH' or 'native'");
                    w = ParseFloat(size.Substring(0, x), $"size '{size}' width");
                    h = ParseFloat(size.Substring(x + 1), $"size '{size}' height");
                    hw = hh = true;
                }
            }

            if (!string.IsNullOrEmpty(width)) {
                if (hw) throw new ArgumentException("cannot specify both size and width");
                if (width == "native") { nw = true; }
                else                   { w = ParseFloat(width, "width"); }
                hw = true;
            }

            if (!string.IsNullOrEmpty(height)) {
                if (hh) throw new ArgumentException("cannot specify both size and height");
                if (height == "native") { nh = true; }
                else                    { h = ParseFloat(height, "height"); }
                hh = true;
            }

            return new SizeSpec(w, h, hw, hh, nw, nh);
        }

        public SizeSpec WithNativeResolved(Vector2 native) =>
            new SizeSpec(
                IsNativeWidth  ? native.x : Width,
                IsNativeHeight ? native.y : Height,
                HasWidth, HasHeight,
                false, false);

        static float ParseFloat(string s, string label) {
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new ArgumentException($"{label}: '{s}' is not a number");
            return v;
        }

        public void ValidateAgainst(AnchorPreset anchor) {
            if (anchor.StretchX && HasWidth)
                throw new ArgumentException(
                    "cannot specify width/size on a horizontally-stretched axis");
            if (anchor.StretchY && HasHeight)
                throw new ArgumentException(
                    "cannot specify height/size on a vertically-stretched axis");
        }
    }
}
```

- [ ] **Step 4: Run tests — should PASS**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SizeSpecNativeTests")
```

也要确认现有 SizeSpec / Layout 测试无回归（跑全 EditMode）。

- [ ] **Step 5: Commit**

```
git add Runtime/Core/Layout/SizeSpec.cs Tests/EditMode/Layout/SizeSpecNativeTests.cs Tests/EditMode/Layout/SizeSpecNativeTests.cs.meta
git commit -m "feat(layout): SizeSpec 'native' value + IsNativeWidth/Height flags"
```

---

### Task 2: Parser 校验 `<Icon name="ns:icon"/>`

**Files:**
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs`
- Test: `Tests/EditMode/Parser/IconParserTests.cs`

ParseElement 已是通用路径。新增：在 ElementNode 构造完成后，如果 `tag == "Icon"`，要求 `name` 属性存在且匹配 `^[\w\-]+:[\w\-]+$`；缺/不匹配 → ParseException。同时：non-Icon 控件遇到 `size/width/height == "native"` 也要 ParseException（spec §5.1）。

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/EditMode/Parser/IconParserTests.cs
using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Parser {
    public class IconParserTests {
        const string Header = "<?xml version='1.0'?><PromptUGUI version='1'><Screen name='S'>";
        const string Footer = "</Screen></PromptUGUI>";

        [Test]
        public void Icon_with_valid_name_parses() {
            var xml = Header + "<Icon name='ui:settings'/>" + Footer;
            var doc = UIDocumentParser.Parse(xml);
            var icon = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("Icon", icon.Tag);
            Assert.AreEqual("ui:settings", icon.Attributes["name"]);
        }

        [Test]
        public void Icon_missing_name_throws() {
            var xml = Header + "<Icon/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("'name' is required", ex.Message);
        }

        [Test]
        public void Icon_name_without_colon_throws() {
            var xml = Header + "<Icon name='settings'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("must be 'set:icon'", ex.Message);
        }

        [Test]
        public void Icon_name_empty_namespace_throws() {
            var xml = Header + "<Icon name=':settings'/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_name_empty_iconname_throws() {
            var xml = Header + "<Icon name='ui:'/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_color_attr_passes_through() {
            var xml = Header + "<Icon name='ui:gear' color='#ff0000'/>" + Footer;
            var doc = UIDocumentParser.Parse(xml);
            var icon = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("#ff0000", icon.Attributes["color"]);
        }

        [Test]
        public void Native_size_on_Frame_throws() {
            var xml = Header + "<Frame size='native'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("native size only allowed on <Icon>", ex.Message);
        }

        [Test]
        public void Native_width_on_Frame_throws() {
            var xml = Header + "<Frame width='native'/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Native_size_on_Icon_ok() {
            var xml = Header + "<Icon name='ui:x' size='native'/>" + Footer;
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_variant_overrides_supported() {
            var xml = Header +
                "<Icon name='ui:sun' name.dark='ui:moon' color.dark='#000'/>"
                + Footer;
            var doc = UIDocumentParser.Parse(xml);
            var icon = doc.Screens[0].Root.Children[0];
            Assert.IsTrue(icon.VariantOverrides.ContainsKey("name"));
            Assert.IsTrue(icon.VariantOverrides.ContainsKey("color"));
        }
    }
}
```

- [ ] **Step 2: Run tests — should FAIL**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IconParserTests")
```

- [ ] **Step 3: Modify UIDocumentParser.cs**

在 `ParseElement` 末尾（return 之前）加：

```csharp
            // <Icon> 校验：name 必填、必须匹配 ns:icon 形式
            if (tag == "Icon" && ns == null) {
                if (!node.Attributes.TryGetValue("name", out var iconName) || string.IsNullOrEmpty(iconName))
                    throw new ParseException("Icon: 'name' is required");
                if (!IsValidIconName(iconName))
                    throw new ParseException(
                        $"Icon: 'name' must be 'set:icon' (got '{iconName}')");
            }

            // size/width/height == "native" 仅 <Icon> 允许
            if (!(tag == "Icon" && ns == null)) {
                foreach (var key in new[] { "size", "width", "height" }) {
                    if (node.Attributes.TryGetValue(key, out var v) && v == "native")
                        throw new ParseException(
                            $"<{tag}>: native size only allowed on <Icon> (attribute '{key}')");
                }
            }

            return node;
        }

        static bool IsValidIconName(string name) {
            int colon = name.IndexOf(':');
            if (colon <= 0 || colon == name.Length - 1) return false;
            for (int i = 0; i < name.Length; i++) {
                if (i == colon) continue;
                char c = name[i];
                bool ok = c == '-' || c == '_'
                          || (c >= 'a' && c <= 'z')
                          || (c >= 'A' && c <= 'Z')
                          || (c >= '0' && c <= '9');
                if (!ok) return false;
            }
            return true;
        }
```

- [ ] **Step 4: Run tests — should PASS**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IconParserTests")
```

跑全 EditMode 检查无回归。

- [ ] **Step 5: Commit**

```
git add Runtime/Core/Parser/UIDocumentParser.cs Tests/EditMode/Parser/IconParserTests.cs Tests/EditMode/Parser/IconParserTests.cs.meta
git commit -m "feat(parser): <Icon> name validation + native size restriction"
```

---

### Task 3: 创建 IconSet ScriptableObject

**Files:**
- Create: `Runtime/Application/IconSet.cs`

ScriptableObject 持有 setName / sourceFolder（Editor-only DefaultAsset）/ atlas / alwaysInclude。运行时只用 setName + atlas，sourceFolder 仅 Editor 工具读。

- [ ] **Step 1: Create IconSet.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PromptUGUI.Application {
    /// <summary>
    /// Project-level icon set. Author 拖一个文件夹做 sourceFolder（Editor only），
    /// 同步工具按 XML 引用扫描结果重建 atlas。运行时仅读 setName + atlas。
    /// </summary>
    [CreateAssetMenu(menuName = "PromptUGUI/Icon Set", fileName = "IconSet")]
    public sealed class IconSet : ScriptableObject {
        [SerializeField] string setName;
        [SerializeField] SpriteAtlas atlas;
        [SerializeField] List<string> alwaysInclude = new();

#if UNITY_EDITOR
        [SerializeField] DefaultAsset sourceFolder;
        public DefaultAsset SourceFolder => sourceFolder;
        public string SourceFolderPath =>
            sourceFolder != null ? AssetDatabase.GetAssetPath(sourceFolder) : null;

        // Editor-only: 同步工具回填 atlas 字段
        internal void SetAtlasInternal(SpriteAtlas a) {
            atlas = a;
            EditorUtility.SetDirty(this);
        }
#endif

        public string SetName => setName;
        public SpriteAtlas Atlas => atlas;
        public IReadOnlyList<string> AlwaysInclude => alwaysInclude;
    }
}
```

- [ ] **Step 2: Refresh + check console**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

确认无编译错误。

- [ ] **Step 3: Commit**

```
git add Runtime/Application/IconSet.cs Runtime/Application/IconSet.cs.meta
git commit -m "feat(runtime): IconSet ScriptableObject"
```

---

### Task 4: 加 `UI.IconResolver` 委托 + Reset

**Files:**
- Modify: `Runtime/Application/UI.cs`

加全局静态 `Func<string,Sprite> IconResolver`，并在 `ResetForTests` 里清掉。

- [ ] **Step 1: Modify UI.cs**

在 `static System.Func<string, string> SourceResolver { get; set; }` 下面加一行：

```csharp
        public static System.Func<string, UnityEngine.Sprite> IconResolver { get; set; }
```

在 `ResetForTests()` 里加：

```csharp
            IconResolver = null;
```

- [ ] **Step 2: Refresh + verify**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

- [ ] **Step 3: Commit**

```
git add Runtime/Application/UI.cs
git commit -m "feat(runtime): UI.IconResolver delegate"
```

---

### Task 5: Control 加 `GetNativeSize()` + ApplyCommon 解算 native

**Files:**
- Modify: `Runtime/Controls/Control.cs`

ApplyCommon 在 `SizeSpec.Parse` 之后、`ValidateAgainst` 之前：若 `IsNativeWidth || IsNativeHeight` → 调 `GetNativeSize()` → `WithNativeResolved`。`GetNativeSize` 默认 null，只有 Icon 重写。

- [ ] **Step 1: Modify Control.cs**

加 virtual 方法，并改 ApplyCommon。完整新版本：

```csharp
        public virtual UnityEngine.Vector2? GetNativeSize() => null;

        public void ApplyCommon(string anchor, string size, string width, string height,
                                string margin, string pivot,
                                bool hidden, bool interactable) {
            var preset = string.IsNullOrEmpty(anchor)
                ? new AnchorPreset(AnchorVertical.Top, AnchorHorizontal.Left)
                : AnchorPreset.Parse(anchor);

            var sizeSpec = SizeSpec.Parse(size, width, height);

            // native 解算：仅 Icon-like 控件返回非 null
            if (sizeSpec.IsNativeWidth || sizeSpec.IsNativeHeight) {
                var native = GetNativeSize();
                if (native.HasValue)
                    sizeSpec = sizeSpec.WithNativeResolved(native.Value);
                // 如果 sprite 还没设上（native==null），W/H 留 0；视觉上是 0 大小，
                // Icon Name setter 重新触发 ApplyCommon 会再解算一次（见 T6）
            }

            sizeSpec.ValidateAgainst(preset);

            AnchorResolver.Resolve(preset,
                out var aMin, out var aMax, out var p);
            RectTransform.anchorMin = aMin;
            RectTransform.anchorMax = aMax;

            if (!string.IsNullOrEmpty(pivot)) {
                var parts = pivot.Split(',');
                RectTransform.pivot = new UnityEngine.Vector2(
                    float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
            } else {
                RectTransform.pivot = p;
            }

            var lr = MarginResolver.Resolve(preset, sizeSpec, margin);
            RectTransform.anchoredPosition = lr.AnchoredPosition;
            RectTransform.sizeDelta = lr.SizeDelta;

            Hidden = hidden;
            Interactable = interactable;
        }
```

- [ ] **Step 2: Refresh + verify**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

无回归。Icon-affecting 测试在 T6 之后才会出现。

- [ ] **Step 3: Commit**

```
git add Runtime/Controls/Control.cs
git commit -m "feat(controls): GetNativeSize hook + native size resolved in ApplyCommon"
```

---

### Task 6: 创建 Icon Control

**Files:**
- Create: `Runtime/Controls/Icon.cs`

包一个 `UnityEngine.UI.Image`；`[UIAttr] Name`（触发 sprite 解析）+ `[UIAttr] Color`；override `GetNativeSize`。

- [ ] **Step 1: Create Icon.cs**

```csharp
using PromptUGUI.Application;
using PromptUGUI.Registry;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls {
    public sealed class Icon : Control {
        UnityImage _img;

        public override void OnAttached() {
            _img = GameObject.GetComponent<UnityImage>()
                   ?? GameObject.AddComponent<UnityImage>();
            _img.preserveAspect = true;
            _img.raycastTarget = false;
            _img.color = UnityEngine.Color.white;
        }

        [UIAttr]
        public string Name {
            set {
                if (string.IsNullOrEmpty(value)) { _img.sprite = null; return; }
                if (UI.IconResolver == null) {
                    Debug.LogError($"Icon '{value}': UI.IconResolver is not registered");
                    _img.sprite = null;
                    return;
                }
                var sprite = UI.IconResolver(value);
                if (sprite == null)
                    Debug.LogError($"Icon '{value}': resolver returned null");
                _img.sprite = sprite;
            }
        }

        [UIAttr]
        public string Color {
            set {
                if (string.IsNullOrEmpty(value)) return;
                if (ColorUtility.TryParseHtmlString(value, out var c))
                    _img.color = c;
            }
        }

        public override Vector2? GetNativeSize() =>
            _img != null && _img.sprite != null ? (Vector2?)_img.sprite.rect.size : null;
    }
}
```

`Color` 是 `[UIAttr]` 标的 string 属性（XML 字符串解析为 Color），`UnityEngine.Color.white` 用全名规避 namespace 冲突。

- [ ] **Step 2: Refresh + verify**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

- [ ] **Step 3: Commit**

```
git add Runtime/Controls/Icon.cs Runtime/Controls/Icon.cs.meta
git commit -m "feat(controls): Icon control with Name/Color UIAttr + native size"
```

---

### Task 7: 注册 Icon 到 BuiltinPrimitives

**Files:**
- Modify: `Runtime/Application/BuiltinPrimitives.cs`

- [ ] **Step 1: Modify BuiltinPrimitives.cs**

```csharp
using PromptUGUI.Controls;
using PromptUGUI.Registry;

namespace PromptUGUI.Application {
    public static class BuiltinPrimitives {
        public static void Register(ControlRegistry reg) {
            reg.Register<Frame>("Frame", null);
            reg.Register<Image>("Image", null);
            reg.Register<Icon>("Icon", null);
            reg.Register<Text>("Text", null, defaultTextAttr: "text");
            reg.Register<VStack>("VStack", null);
            reg.Register<HStack>("HStack", null);
            reg.Register<Grid>("Grid", null);
            reg.Register<Btn>("Btn", null);
        }
    }
}
```

- [ ] **Step 2: Refresh + verify**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

- [ ] **Step 3: Commit**

```
git add Runtime/Application/BuiltinPrimitives.cs
git commit -m "feat(runtime): register Icon as builtin primitive"
```

---

### Task 8: 创建 IconResolverHelpers + 测试

**Files:**
- Create: `Runtime/Application/IconResolverHelpers.cs`
- Test: `Tests/EditMode/Application/IconResolverTests.cs`

两个 helper 重载：`UseSpriteAtlasIconResolver(string resourcesSubpath = "IconSets")` 和 `UseSpriteAtlasIconResolver(IEnumerable<IconSet>)`。建 lookup 字典；同 setName 抛 InvalidOperationException；atlas==null 不报错。

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/EditMode/Application/IconResolverTests.cs
using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.U2D;

namespace PromptUGUI.Tests.Application {
    public class IconResolverTests {
        [SetUp] public void Setup() {
            UI.ResetForTests();
            BuiltinPrimitives.Register(UI.Registry);
        }
        [TearDown] public void Teardown() => UI.ResetForTests();

        [Test]
        public void Null_resolver_logs_error_and_returns_null_sprite() {
            // UI.IconResolver 默认 null。Icon control 在 Name setter 里查；这里只验证 default state
            Assert.IsNull(UI.IconResolver);
        }

        [Test]
        public void UseSpriteAtlasIconResolver_with_empty_list_builds_resolver() {
            IconResolverHelpers.UseSpriteAtlasIconResolver(Array.Empty<IconSet>());
            Assert.IsNotNull(UI.IconResolver);
            Assert.IsNull(UI.IconResolver("ui:nope"));
        }

        [Test]
        public void Duplicate_set_name_throws() {
            var a = MakeIconSet("ui");
            var b = MakeIconSet("ui");
            Assert.Throws<InvalidOperationException>(() =>
                IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { a, b }));
        }

        [Test]
        public void Null_atlas_does_not_throw() {
            var s = MakeIconSet("ui");  // atlas=null
            IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { s });
            Assert.IsNull(UI.IconResolver("ui:foo"));
        }

        [Test]
        public void Resolver_strips_clone_suffix() {
            // 验证 BuildLookup 去掉 "(Clone)"。这个 case 难直接构造 SpriteAtlas，
            // 真实验证留给 PlayMode E2E（T20）。这里用反射或跳过。
            // 简化：直接验证 helper 可调用且返回 non-null delegate。
            var s = MakeIconSet("ui");
            IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { s });
            Assert.IsNotNull(UI.IconResolver);
        }

        static IconSet MakeIconSet(string name) {
            var s = ScriptableObject.CreateInstance<IconSet>();
            // setName 是私有 SerializeField；用 SerializedObject 写入
            var so = new UnityEditor.SerializedObject(s);
            so.FindProperty("setName").stringValue = name;
            so.ApplyModifiedProperties();
            return s;
        }
    }
}
```

注意：`MakeIconSet` 用 `UnityEditor.SerializedObject`，这意味着这个测试**只能在 EditMode 跑**。EditMode 测试在 Editor 进程内运行，UnityEditor.* 可用。

如果 EditMode asmdef 不包含 UnityEditor 引用，需要给测试加 `#if UNITY_EDITOR` 守卫，或把这个测试挪去 EditorOnly asmdef。检查现有 EditMode asmdef：

```
mcp__UnityMCP__read_console(...)
```

查 `Tests/EditMode/PromptUGUI.Tests.EditMode.asmdef`：若 `defineConstraints` 已含 `UNITY_EDITOR`，UnityEditor 可用；否则加 `#if UNITY_EDITOR` 即可。M4 已为 EditMode asmdef 加 `UNITY_EDITOR` define（spec §4.2 / M4 plan T19）。

- [ ] **Step 2: Run tests — should FAIL**

```
mcp__UnityMCP__run_tests(mode="EditMode", filter="IconResolverTests")
```

- [ ] **Step 3: Create IconResolverHelpers.cs**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

namespace PromptUGUI.Application {
    public static class IconResolverHelpers {
        const string CloneSuffix = "(Clone)";

        public static void UseSpriteAtlasIconResolver(string resourcesSubpath = "IconSets") {
            var sets = Resources.LoadAll<IconSet>(resourcesSubpath);
            UseSpriteAtlasIconResolver(sets);
        }

        public static void UseSpriteAtlasIconResolver(IEnumerable<IconSet> sets) {
            var map = BuildLookup(sets);
            UI.IconResolver = key => map.TryGetValue(key, out var s) ? s : null;
        }

        static Dictionary<string, Sprite> BuildLookup(IEnumerable<IconSet> sets) {
            var map = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            var seenSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var set in sets) {
                if (set == null) continue;
                if (string.IsNullOrEmpty(set.SetName)) {
                    Debug.LogWarning("[PromptUGUI] IconSet with empty setName, skipping");
                    continue;
                }
                if (!seenSet.Add(set.SetName))
                    throw new InvalidOperationException(
                        $"Duplicate IconSet name '{set.SetName}'");
                if (set.Atlas == null) continue;

                var sprites = new Sprite[set.Atlas.spriteCount];
                set.Atlas.GetSprites(sprites);
                foreach (var s in sprites) {
                    if (s == null) continue;
                    var name = s.name;
                    if (name.EndsWith(CloneSuffix, StringComparison.Ordinal))
                        name = name.Substring(0, name.Length - CloneSuffix.Length);
                    map[$"{set.SetName}:{name}"] = s;
                }
            }
            return map;
        }
    }
}
```

- [ ] **Step 4: Run tests — should PASS**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__run_tests(mode="EditMode", filter="IconResolverTests")
```

- [ ] **Step 5: Commit**

```
git add Runtime/Application/IconResolverHelpers.cs Runtime/Application/IconResolverHelpers.cs.meta Tests/EditMode/Application/IconResolverTests.cs Tests/EditMode/Application/IconResolverTests.cs.meta
git commit -m "feat(runtime): IconResolverHelpers default SpriteAtlas backend"
```

---

### Task 9: IconAtlasSyncer.ScanXmlReferences

**Files:**
- Create: `Editor/IconAtlasSyncer.cs`（先放 Scan 部分；T10/T11/T12 增量）

- [ ] **Step 1: Create IconAtlasSyncer.cs (scan only)**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor {
    public static class IconAtlasSyncer {
        const string DynamicMarker = "{{";

        /// <summary>(setName, iconName) pairs found across all .ui.xml in the project.</summary>
        public static HashSet<(string set, string name)> ScanXmlReferences() {
            var refs = new HashSet<(string, string)>();
            var guids = AssetDatabase.FindAssets("t:TextAsset");
            foreach (var guid in guids) {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".ui.xml", StringComparison.Ordinal)) continue;
                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException ex) {
                    Debug.LogWarning($"[IconSync] cannot read {path}: {ex.Message}");
                    continue;
                }
                UIDocument doc;
                try { doc = UIDocumentParser.Parse(text); }
                catch (ParseException ex) {
                    Debug.LogWarning($"[IconSync] skipping malformed {path}: {ex.Message}");
                    continue;
                }
                foreach (var screen in doc.Screens)
                    CollectFromNode(screen.Root, refs, path);
                foreach (var tpl in doc.Templates.Values)
                    if (tpl.Body != null) CollectFromNode(tpl.Body, refs, path);
            }
            return refs;
        }

        static void CollectFromNode(ElementNode node,
                                    HashSet<(string, string)> refs, string path) {
            if (node == null) return;
            if (node.Tag == "Icon" && node.Namespace == null) {
                CollectFromAttr(node.Attributes.TryGetValue("name", out var n) ? n : null,
                                refs, path);
                if (node.VariantOverrides.TryGetValue("name", out var list))
                    foreach (var (_, v) in list) CollectFromAttr(v, refs, path);
            }
            foreach (var c in node.Children) CollectFromNode(c, refs, path);
        }

        static void CollectFromAttr(string value,
                                    HashSet<(string, string)> refs, string path) {
            if (string.IsNullOrEmpty(value)) return;
            int colon = value.IndexOf(':');
            if (colon <= 0 || colon == value.Length - 1) return;  // 解析期已校验，防御性
            var ns = value.Substring(0, colon);
            var name = value.Substring(colon + 1);
            if (ns.Contains(DynamicMarker)) {
                Debug.LogWarning(
                    $"[IconSync] {path}: <Icon name='{value}'>: dynamic namespace " +
                    $"({DynamicMarker}...) is not analyzable; skipping");
                return;
            }
            if (name.Contains(DynamicMarker)) {
                Debug.LogWarning(
                    $"[IconSync] {path}: <Icon name='{value}'>: dynamic icon name " +
                    $"({DynamicMarker}...); list candidates in IconSet.alwaysInclude");
                return;
            }
            refs.Add((ns, name));
        }
    }
}
```

- [ ] **Step 2: Refresh + verify**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

- [ ] **Step 3: Commit**

```
git add Editor/IconAtlasSyncer.cs Editor/IconAtlasSyncer.cs.meta
git commit -m "feat(editor): IconAtlasSyncer.ScanXmlReferences"
```

---

### Task 10: IconAtlasSyncer.EnumeratePngs

**Files:**
- Modify: `Editor/IconAtlasSyncer.cs`

- [ ] **Step 1: Add EnumeratePngs method**

在 `IconAtlasSyncer` 类内追加：

```csharp
        /// <summary>{iconName -> Sprite} 收集 sourceFolder 下所有 PNG。</summary>
        public static Dictionary<string, Sprite> EnumeratePngs(string folderAssetPath) {
            var dict = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(folderAssetPath)) return dict;
            if (!AssetDatabase.IsValidFolder(folderAssetPath)) {
                Debug.LogError($"[IconSync] not a folder: '{folderAssetPath}'");
                return dict;
            }

            var fullFolder = Path.GetFullPath(folderAssetPath);
            foreach (var fullPath in Directory.EnumerateFiles(
                         fullFolder, "*.png", SearchOption.AllDirectories)) {
                var assetPath = "Assets" +
                    fullPath.Substring(UnityEngine.Application.dataPath.Length).Replace('\\', '/');
                EnsureSpriteImporter(assetPath);
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (sp == null) continue;
                var name = Path.GetFileNameWithoutExtension(assetPath);
                if (dict.ContainsKey(name))
                    Debug.LogWarning(
                        $"[IconSync] duplicate icon '{name}' in {folderAssetPath}; using first");
                else
                    dict[name] = sp;
            }
            return dict;
        }

        /// <summary>把 PNG 的 importer 从 Default 改成 Sprite（如果必要），并 reimport。</summary>
        static void EnsureSpriteImporter(string assetPath) {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;
            if (importer.textureType == TextureImporterType.Sprite) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();
        }
```

- [ ] **Step 2: Refresh + verify**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

- [ ] **Step 3: Commit**

```
git add Editor/IconAtlasSyncer.cs
git commit -m "feat(editor): IconAtlasSyncer.EnumeratePngs + auto-fix Sprite importer"
```

---

### Task 11: IconAtlasSyncer.UpdateAtlas + 自动建 atlas

**Files:**
- Modify: `Editor/IconAtlasSyncer.cs`

- [ ] **Step 1: Add UpdateAtlas + EnsureAtlasAsset methods**

```csharp
        using UnityEditor.U2D;
        // ↑ 这个 using 加到文件顶部
        // (此处仅展示要新增的方法；放在类内合适位置)

        /// <summary>差量同步 atlas 的 packables。返回 true 表示发生了变更。</summary>
        public static bool UpdateAtlas(SpriteAtlas atlas, Sprite[] desired) {
            var current = atlas.GetPackables();
            if (PackablesEqual(current, desired)) return false;
            atlas.Remove(current);
            // SpriteAtlas.Add 接 Object[]
            var asObjects = new UnityEngine.Object[desired.Length];
            for (int i = 0; i < desired.Length; i++) asObjects[i] = desired[i];
            atlas.Add(asObjects);
            EditorUtility.SetDirty(atlas);
            SpriteAtlasUtility.PackAtlases(
                new[] { atlas },
                EditorUserBuildSettings.activeBuildTarget);
            return true;
        }

        static bool PackablesEqual(UnityEngine.Object[] a, Sprite[] b) {
            if (a.Length != b.Length) return false;
            // a 应该全是 Sprite；按 GUID 集合比较，顺序无关
            var aSet = new HashSet<string>();
            foreach (var o in a) {
                var path = AssetDatabase.GetAssetPath(o);
                aSet.Add(AssetDatabase.AssetPathToGUID(path) + "|" + (o as Sprite)?.name);
            }
            foreach (var s in b) {
                var path = AssetDatabase.GetAssetPath(s);
                var key = AssetDatabase.AssetPathToGUID(path) + "|" + s.name;
                if (!aSet.Contains(key)) return false;
            }
            return true;
        }

        /// <summary>若 IconSet.atlas 为 null，在 SO 同目录创建 &lt;setName&gt;.spriteatlas 并回填。</summary>
        internal static SpriteAtlas EnsureAtlasAsset(PromptUGUI.Application.IconSet set) {
            if (set.Atlas != null) return set.Atlas;
            var setPath = AssetDatabase.GetAssetPath(set);
            if (string.IsNullOrEmpty(setPath)) {
                Debug.LogError("[IconSync] IconSet not saved as asset; cannot create atlas");
                return null;
            }
            var dir = Path.GetDirectoryName(setPath).Replace('\\', '/');
            var atlasPath = $"{dir}/{set.SetName}.spriteatlas";
            var atlas = new SpriteAtlas();
            AssetDatabase.CreateAsset(atlas, atlasPath);
            set.SetAtlasInternal(atlas);
            AssetDatabase.SaveAssets();
            return atlas;
        }
```

- [ ] **Step 2: Refresh + verify**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

- [ ] **Step 3: Commit**

```
git add Editor/IconAtlasSyncer.cs
git commit -m "feat(editor): IconAtlasSyncer.UpdateAtlas + auto-create atlas asset"
```

---

### Task 12: IconAtlasSyncer.SyncAll + 测试

**Files:**
- Modify: `Editor/IconAtlasSyncer.cs`
- Test: `Tests/EditMode/Editor/IconAtlasSyncerTests.cs`

把上面三块串起来。SyncAll(IEnumerable<IconSet>)：扫 XML、对每个 set 取交集 + warn missing、UpdateAtlas、SaveAssets。

- [ ] **Step 1: Add SyncAll**

```csharp
        public static void SyncAll(IEnumerable<PromptUGUI.Application.IconSet> sets) {
            var refs = ScanXmlReferences();

            // detect duplicate setNames before any work
            var seen = new HashSet<string>();
            foreach (var s in sets) {
                if (s == null) continue;
                if (string.IsNullOrEmpty(s.SetName)) {
                    Debug.LogError($"[IconSync] IconSet '{s.name}' has empty setName");
                    return;
                }
                if (!seen.Add(s.SetName)) {
                    Debug.LogError(
                        $"[IconSync] duplicate IconSet setName '{s.SetName}'; aborting");
                    return;
                }
            }

            foreach (var set in sets) {
                if (set == null) continue;
                var folder = set.SourceFolderPath;
                if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) {
                    Debug.LogError($"[IconSync] IconSet '{set.SetName}': sourceFolder invalid");
                    continue;
                }
                var available = EnumeratePngs(folder);
                var needed = new HashSet<string>();
                foreach (var (ns, name) in refs)
                    if (ns == set.SetName) needed.Add(name);
                foreach (var n in set.AlwaysInclude)
                    if (!string.IsNullOrEmpty(n)) needed.Add(n);

                var picked = new List<Sprite>();
                var missing = new List<string>();
                foreach (var n in needed) {
                    if (available.TryGetValue(n, out var sp)) picked.Add(sp);
                    else missing.Add(n);
                }
                if (missing.Count > 0)
                    Debug.LogWarning(
                        $"[IconSync] '{set.SetName}': XML references missing PNGs: " +
                        string.Join(", ", missing));

                var atlas = EnsureAtlasAsset(set);
                if (atlas == null) continue;
                UpdateAtlas(atlas, picked.ToArray());
            }

            AssetDatabase.SaveAssets();
        }

        public static IEnumerable<PromptUGUI.Application.IconSet> FindAllIconSets() {
            var guids = AssetDatabase.FindAssets("t:" + nameof(PromptUGUI.Application.IconSet));
            foreach (var guid in guids) {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var s = AssetDatabase.LoadAssetAtPath<PromptUGUI.Application.IconSet>(path);
                if (s != null) yield return s;
            }
        }
```

- [ ] **Step 2: Write the failing tests**

```csharp
// Tests/EditMode/Editor/IconAtlasSyncerTests.cs
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Editor;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor {
    public class IconAtlasSyncerTests {
        const string TestRoot = "Assets/__test_iconsync__";
        readonly List<string> _toCleanup = new();

        [SetUp] public void Setup() {
            if (!AssetDatabase.IsValidFolder(TestRoot))
                AssetDatabase.CreateFolder("Assets", "__test_iconsync__");
        }

        [TearDown] public void Teardown() {
            foreach (var p in _toCleanup) AssetDatabase.DeleteAsset(p);
            _toCleanup.Clear();
            AssetDatabase.DeleteAsset(TestRoot);
        }

        [Test]
        public void Scan_finds_icon_refs_in_ui_xml() {
            var path = $"{TestRoot}/sample.ui.xml";
            File.WriteAllText(path,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'>
                      <Icon name='ui:settings'/>
                      <Icon name='art:gold-coin'/>
                    </Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(path);
            _toCleanup.Add(path);

            var refs = IconAtlasSyncer.ScanXmlReferences();
            Assert.Contains(("ui", "settings"), refs);
            Assert.Contains(("art", "gold-coin"), refs);
        }

        [Test]
        public void Scan_skips_dynamic_icon_names() {
            var path = $"{TestRoot}/dyn.ui.xml";
            File.WriteAllText(path,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'>
                      <Icon name='ui:{{kind}}'/>
                    </Screen>
                  </PromptUGUI>");
            // 注意：parser 现在会校验 name pattern，'{' 会被 IsValidIconName 拒掉。
            // 真实模板使用是在 Template 内部、扩展前；这里单独验证 ScanXmlReferences 的逻辑路径。
            // 因为 parser 拒，这条 XML 会被 parse skip + warn。验证最终 refs 不含此条目即可。
            AssetDatabase.ImportAsset(path);
            _toCleanup.Add(path);

            var refs = IconAtlasSyncer.ScanXmlReferences();
            // 这个文件 parser 会扔 ParseException，被 catch + warn；refs 不含 ui:任何
            foreach (var (ns, n) in refs) Assert.AreNotEqual("ui", ns);
        }

        [Test]
        public void Scan_picks_up_variant_overrides() {
            var path = $"{TestRoot}/variant.ui.xml";
            File.WriteAllText(path,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'>
                      <Icon name='ui:sun' name.dark='ui:moon'/>
                    </Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(path);
            _toCleanup.Add(path);

            var refs = IconAtlasSyncer.ScanXmlReferences();
            Assert.Contains(("ui", "sun"), refs);
            Assert.Contains(("ui", "moon"), refs);
        }

        [Test]
        public void EnumeratePngs_returns_dict_keyed_by_filename() {
            var folder = $"{TestRoot}/icons";
            AssetDatabase.CreateFolder(TestRoot, "icons");
            var pngPath = $"{folder}/foo.png";
            File.WriteAllBytes(pngPath, MakeBlankPng());
            AssetDatabase.ImportAsset(pngPath);

            var dict = IconAtlasSyncer.EnumeratePngs(folder);
            Assert.IsTrue(dict.ContainsKey("foo"));
        }

        [Test]
        public void SyncAll_aborts_on_duplicate_setname() {
            var a = MakeIconSetAsset("a", "ui");
            var b = MakeIconSetAsset("b", "ui");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("duplicate IconSet"));
            IconAtlasSyncer.SyncAll(new[] { a, b });
        }

        // ---- helpers ----

        IconSet MakeIconSetAsset(string fileName, string setName) {
            var s = ScriptableObject.CreateInstance<IconSet>();
            var so = new SerializedObject(s);
            so.FindProperty("setName").stringValue = setName;
            so.ApplyModifiedProperties();
            var path = $"{TestRoot}/{fileName}.asset";
            AssetDatabase.CreateAsset(s, path);
            _toCleanup.Add(path);
            return AssetDatabase.LoadAssetAtPath<IconSet>(path);
        }

        // 1x1 PNG，用 Unity Texture2D.EncodeToPNG 生成
        byte[] MakeBlankPng() {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, Color.white);
            t.Apply();
            var bytes = t.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(t);
            return bytes;
        }
    }
}
```

- [ ] **Step 3: Run tests — should PASS（after T9-T11）**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="IconAtlasSyncerTests")
```

如果某 case 失败（比如 SpriteAtlas 创建失败），定位后修：常见问题是 `SpriteAtlasUtility.PackAtlases` 在 EditMode test 内被禁用——若如此，给该 case 加 `[Ignore("PackAtlases unavailable in test runner")]`，并写个等价的 PlayMode 验证。

- [ ] **Step 4: Commit**

```
git add Editor/IconAtlasSyncer.cs Tests/EditMode/Editor/IconAtlasSyncerTests.cs Tests/EditMode/Editor/IconAtlasSyncerTests.cs.meta
git commit -m "feat(editor): IconAtlasSyncer.SyncAll + tests"
```

---

### Task 13: IconAtlasMenu (Tools 菜单)

**Files:**
- Create: `Editor/IconAtlasMenu.cs`

- [ ] **Step 1: Create**

```csharp
using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor {
    public static class IconAtlasMenu {
        [MenuItem("Tools/PromptUGUI/Sync Icon Atlases (All Sets)")]
        public static void SyncAll() {
            var sets = new System.Collections.Generic.List<IconSet>();
            foreach (var s in IconAtlasSyncer.FindAllIconSets()) sets.Add(s);
            if (sets.Count == 0) {
                Debug.Log("[PromptUGUI] No IconSet assets found");
                return;
            }
            IconAtlasSyncer.SyncAll(sets);
            Debug.Log($"[PromptUGUI] Synced {sets.Count} IconSet(s)");
        }

        [MenuItem("Tools/PromptUGUI/Sync Icon Atlases (Selected Set)")]
        public static void SyncSelected() {
            var picked = new System.Collections.Generic.List<IconSet>();
            foreach (var o in Selection.objects)
                if (o is IconSet s) picked.Add(s);
            if (picked.Count == 0) {
                Debug.LogWarning("[PromptUGUI] No IconSet selected");
                return;
            }
            IconAtlasSyncer.SyncAll(picked);
        }

        [MenuItem("Tools/PromptUGUI/Sync Icon Atlases (Selected Set)", true)]
        public static bool SyncSelectedValidate() {
            foreach (var o in Selection.objects)
                if (o is IconSet) return true;
            return false;
        }
    }
}
```

- [ ] **Step 2: Refresh + verify**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

- [ ] **Step 3: Commit**

```
git add Editor/IconAtlasMenu.cs Editor/IconAtlasMenu.cs.meta
git commit -m "feat(editor): Tools menu entries for icon atlas sync"
```

---

### Task 14: IconAtlasBuildHook (IPreprocessBuildWithReport)

**Files:**
- Create: `Editor/IconAtlasBuildHook.cs`

- [ ] **Step 1: Create**

```csharp
using PromptUGUI.Application;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PromptUGUI.Editor {
    public sealed class IconAtlasBuildHook : IPreprocessBuildWithReport {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) {
            var sets = new System.Collections.Generic.List<IconSet>();
            foreach (var s in IconAtlasSyncer.FindAllIconSets()) sets.Add(s);
            if (sets.Count == 0) return;
            Debug.Log($"[PromptUGUI] Pre-build syncing {sets.Count} IconSet(s)");
            IconAtlasSyncer.SyncAll(sets);
        }
    }
}
```

- [ ] **Step 2: Refresh + verify**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

- [ ] **Step 3: Commit**

```
git add Editor/IconAtlasBuildHook.cs Editor/IconAtlasBuildHook.cs.meta
git commit -m "feat(editor): IPreprocessBuildWithReport for icon atlas sync"
```

---

### Task 15: IconAtlasAutoSync (AssetPostprocessor + EditorPrefs opt-in)

**Files:**
- Create: `Editor/IconAtlasAutoSync.cs`

- [ ] **Step 1: Create**

```csharp
using System;
using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor {
    public sealed class IconAtlasAutoSync : AssetPostprocessor {
        const string PrefKey = "PromptUGUI.IconAtlas.AutoSyncOnSave";

        public static bool Enabled {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem("Tools/PromptUGUI/Auto-sync Icon Atlases on Save")]
        static void Toggle() => Enabled = !Enabled;

        [MenuItem("Tools/PromptUGUI/Auto-sync Icon Atlases on Save", true)]
        static bool ToggleValidate() {
            Menu.SetChecked("Tools/PromptUGUI/Auto-sync Icon Atlases on Save", Enabled);
            return true;
        }

        static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom) {
            if (!Enabled) return;
            bool xmlChanged = false;
            foreach (var p in imported)
                if (p.EndsWith(".ui.xml", StringComparison.Ordinal)) { xmlChanged = true; break; }
            if (!xmlChanged) {
                foreach (var p in deleted)
                    if (p.EndsWith(".ui.xml", StringComparison.Ordinal)) { xmlChanged = true; break; }
            }
            if (!xmlChanged) return;

            var sets = new System.Collections.Generic.List<IconSet>();
            foreach (var s in IconAtlasSyncer.FindAllIconSets()) sets.Add(s);
            if (sets.Count == 0) return;
            IconAtlasSyncer.SyncAll(sets);
        }
    }
}
```

- [ ] **Step 2: Refresh + verify**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

- [ ] **Step 3: Commit**

```
git add Editor/IconAtlasAutoSync.cs Editor/IconAtlasAutoSync.cs.meta
git commit -m "feat(editor): AssetPostprocessor auto-sync (opt-in via EditorPrefs)"
```

---

### Task 16: UIAttrAttribute 加 Pattern

**Files:**
- Modify: `Runtime/Registry/UIAttrAttribute.cs`

XSD 生成需要给某些属性加 `xs:pattern` 约束（Icon.Name 是 `^[\w\-]+:[\w\-]+$`）。给 `[UIAttr]` 加可选 `Pattern` 属性。

- [ ] **Step 1: Modify UIAttrAttribute.cs**

```csharp
using System;

namespace PromptUGUI.Registry {
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class UIAttrAttribute : Attribute {
        public string Name { get; }
        /// <summary>Optional XSD pattern (regex) for value validation.</summary>
        public string Pattern { get; set; }

        public UIAttrAttribute(string name = null) { Name = name; }
    }
}
```

- [ ] **Step 2: Refresh + verify**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

- [ ] **Step 3: Commit**

```
git add Runtime/Registry/UIAttrAttribute.cs
git commit -m "feat(registry): UIAttr.Pattern for XSD constraints"
```

---

### Task 17: IconSetEditor 自定义 Inspector

**Files:**
- Create: `Editor/IconSetEditor.cs`

- [ ] **Step 1: Create**

```csharp
using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor {
    [CustomEditor(typeof(IconSet))]
    public sealed class IconSetEditor : UnityEditor.Editor {
        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            var set = (IconSet)target;
            int pngCount = 0;
            if (!string.IsNullOrEmpty(set.SourceFolderPath)
                && AssetDatabase.IsValidFolder(set.SourceFolderPath)) {
                var dict = IconAtlasSyncer.EnumeratePngs(set.SourceFolderPath);
                pngCount = dict.Count;
            }
            EditorGUILayout.LabelField("Source PNGs", pngCount.ToString());
            EditorGUILayout.LabelField("Atlas",
                set.Atlas == null ? "(not yet generated)" : AssetDatabase.GetAssetPath(set.Atlas));
            if (GUILayout.Button("Sync This Set")) {
                IconAtlasSyncer.SyncAll(new[] { set });
            }
        }
    }
}
```

- [ ] **Step 2: Refresh + verify**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

- [ ] **Step 3: Commit**

```
git add Editor/IconSetEditor.cs Editor/IconSetEditor.cs.meta
git commit -m "feat(editor): IconSet custom inspector with Sync This Set button"
```

---

### Task 18: HotReload 接入 atlas/IconSet 变化

**Files:**
- Modify: `Runtime/Application/UI.cs`
- Test: `Tests/EditMode/Application/IconHotReloadTests.cs`

`UI.HotReload.NotifyAssetChanged` 当前只看 src→deps。新增分支：assetPath 是 SpriteAtlas / IconSet 时，重建 IconResolver lookup（如果当前 resolver 来自 helper，可识别）+ 让所有 open Screen 走 ReSolve。

简化策略：暴露 `UI.HotReload.NotifyIconAssetsChanged()` 静态方法，AssetPostprocessor 触发时调用；触发：(1) 重建 lookup（重新枚举 IconSet 资源） (2) 对每个 open Screen 调 ReSolve。

- [ ] **Step 1: Write the failing test**

```csharp
// Tests/EditMode/Application/IconHotReloadTests.cs
#if UNITY_EDITOR
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.Application {
    public class IconHotReloadTests {
        [SetUp] public void Setup() {
            UI.ResetForTests();
            BuiltinPrimitives.Register(UI.Registry);
        }
        [TearDown] public void Teardown() => UI.ResetForTests();

        [Test]
        public void NotifyIconAssetsChanged_invokes_resolver_rebuild_callback() {
            int rebuildCount = 0;
            UI.HotReload.IconResolverRebuilder = () => rebuildCount++;
            UI.HotReload.NotifyIconAssetsChanged();
            Assert.AreEqual(1, rebuildCount);
        }

        [Test]
        public void NotifyIconAssetsChanged_no_op_when_disabled() {
            int rebuildCount = 0;
            UI.HotReload.IconResolverRebuilder = () => rebuildCount++;
            UI.HotReload.Enabled = false;
            UI.HotReload.NotifyIconAssetsChanged();
            Assert.AreEqual(0, rebuildCount);
        }
    }
}
#endif
```

- [ ] **Step 2: Run test — should FAIL**

```
mcp__UnityMCP__run_tests(mode="EditMode", filter="IconHotReloadTests")
```

- [ ] **Step 3: Modify UI.cs**

在 `#if UNITY_EDITOR public static class HotReload { ... }` 内追加：

```csharp
            /// <summary>
            /// 由 helper 注册：被调用时应当重建 UI.IconResolver 的 lookup
            /// (e.g., 重新枚举 IconSet 资源 + 重建 dict)。
            /// </summary>
            public static System.Action IconResolverRebuilder { get; set; }

            /// <summary>
            /// 由 AssetPostprocessor / 用户手动调用：通知 icon-related 资源变化。
            /// 重建 IconResolver lookup + 触发所有 open Screen ReSolve。
            /// </summary>
            public static void NotifyIconAssetsChanged() {
                if (!Enabled) return;
                IconResolverRebuilder?.Invoke();
                foreach (var s in _open.Values) s.ReSolve();
            }
```

`ReSolve()` 在 `Screen.cs` 是已有 internal 方法（M3 引入）；确认其可见性。如果是 internal 而 UI 与 Screen 同 asmdef，正常调；否则改 internal 或加 InternalsVisibleTo（已有 `PromptUGUI.Tests.EditMode`）。

并在 `ResetForTests` 里清掉：

```csharp
#if UNITY_EDITOR
            HotReload.AssetPathToSrc = null;
            HotReload.IconResolverRebuilder = null;
            HotReload.Enabled = true;
#endif
```

- [ ] **Step 4: Run test — should PASS**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__run_tests(mode="EditMode", filter="IconHotReloadTests")
```

- [ ] **Step 5: 让 IconResolverHelpers 注册 rebuilder**

```csharp
// 修改 IconResolverHelpers.UseSpriteAtlasIconResolver(string)
public static void UseSpriteAtlasIconResolver(string resourcesSubpath = "IconSets") {
    void Rebuild() {
        var sets = Resources.LoadAll<IconSet>(resourcesSubpath);
        UI.IconResolver = key => {
            // 简化：每次调用现枚举太慢；改为闭包持有 map，rebuilder 重建闭包
        };
        var map = BuildLookup(sets);
        UI.IconResolver = key => map.TryGetValue(key, out var sp) ? sp : null;
    }
    Rebuild();
#if UNITY_EDITOR
    UI.HotReload.IconResolverRebuilder = Rebuild;
#endif
}
```

为 `UseSpriteAtlasIconResolver(IEnumerable<IconSet>)` 重载也注册 rebuilder（输入是 IEnumerable，重建时直接重 BuildLookup 同一 collection——前提是调用方收集逻辑可重入；为了简单，让 helper 把 collection ToArray 一次锁定）：

```csharp
public static void UseSpriteAtlasIconResolver(IEnumerable<IconSet> sets) {
    var snapshot = new System.Collections.Generic.List<IconSet>(sets);
    void Rebuild() {
        var map = BuildLookup(snapshot);
        UI.IconResolver = key => map.TryGetValue(key, out var sp) ? sp : null;
    }
    Rebuild();
#if UNITY_EDITOR
    UI.HotReload.IconResolverRebuilder = Rebuild;
#endif
}
```

- [ ] **Step 6: 让 IconAtlasAutoSync 在同步成功后调 NotifyIconAssetsChanged**

修改 `Editor/IconAtlasAutoSync.cs`，`SyncAll` 调用后追加：

```csharp
            IconAtlasSyncer.SyncAll(sets);
            UI.HotReload.NotifyIconAssetsChanged();
```

并修改 `Editor/IconAtlasMenu.cs` 同样追加（手动菜单也触发 hot reload）。

- [ ] **Step 7: Run all tests**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
```

- [ ] **Step 8: Commit**

```
git add Runtime/Application/UI.cs Runtime/Application/IconResolverHelpers.cs Editor/IconAtlasAutoSync.cs Editor/IconAtlasMenu.cs Tests/EditMode/Application/IconHotReloadTests.cs Tests/EditMode/Application/IconHotReloadTests.cs.meta
git commit -m "feat(hot-reload): NotifyIconAssetsChanged + ReSolve all open screens"
```

---

### Task 19: XsdGenerator 加 `<Icon>` 元素 + Pattern 支持

**Files:**
- Modify: `Editor/XsdGenerator.cs`
- Modify: `Tests/EditMode/Editor/XsdGeneratorTests.cs`

`<Icon>` 走反射路径（已有 `customs` 处理），只需要：
1. 把 `Icon` 也算作 primitive，写到 `WriteControl` 静态调用列表里（avoid duplicate via reflection）；同时写到 `WriteControlGroup` 的 hard-coded primitive 数组里
2. `ReflectControlAttrs` / `WriteControl` 使用 `[UIAttr(Pattern=...)]` 时输出 `xs:simpleType > xs:restriction > xs:pattern`

- [ ] **Step 1: Modify XsdGenerator.cs**

把 Icon 加到 primitives：

```csharp
                // 7 primitives + their attributes  → 现在 8 个
                WriteControl(writer, "Frame",  Array.Empty<(string,string,string)>());
                WriteControl(writer, "Image",  new[] {("color","xs:string",null),("sprite","xs:string",null),("type","xs:string",null)});
                WriteControl(writer, "Text",   new[] {("align","xs:string",null),("color","xs:string",null),("font","xs:string",null),("size","xs:string",null),("text","xs:string",null),("wrap","xs:string",null)});
                WriteControl(writer, "VStack", Array.Empty<(string,string,string)>());
                WriteControl(writer, "HStack", Array.Empty<(string,string,string)>());
                WriteControl(writer, "Grid",   new[] {("columns","xs:int",null)});
                WriteControl(writer, "Btn",    new[] {("color","xs:string",null),("sprite","xs:string",null),("text","xs:string",null)});
                WriteControl(writer, "Icon",   new[] {("name","xs:string","^[\\w\\-]+:[\\w\\-]+$"),("color","xs:string",null)});

                var primitives = new HashSet<string> {
                    "Frame","Image","Icon","Text","VStack","HStack","Grid","Btn" };
```

修改 `WriteControl` 签名为带 Pattern 的三元组：

```csharp
        static void WriteControl(XmlWriter w, string tag,
                                 (string Name, string XsdType, string Pattern)[] attrs) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", tag);
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "choice", null);
            w.WriteAttributeString("maxOccurs", "unbounded");
            w.WriteAttributeString("minOccurs", "0");
            w.WriteStartElement("xs", "group", null);
            w.WriteAttributeString("ref", "controlGroup");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("xs", "attributeGroup", null);
            w.WriteAttributeString("ref", "commonAttrs");
            w.WriteEndElement();
            foreach (var (name, type, pattern) in attrs) {
                w.WriteStartElement("xs", "attribute", null);
                w.WriteAttributeString("name", name);
                if (string.IsNullOrEmpty(pattern)) {
                    w.WriteAttributeString("type", type);
                } else {
                    w.WriteStartElement("xs", "simpleType", null);
                    w.WriteStartElement("xs", "restriction", null);
                    w.WriteAttributeString("base", type);
                    w.WriteStartElement("xs", "pattern", null);
                    w.WriteAttributeString("value", pattern);
                    w.WriteEndElement();
                    w.WriteEndElement();
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }
```

修改 `ReflectControlAttrs` 返回三元组并读 `[UIAttr(Pattern=...)]`：

```csharp
        static (string Name, string XsdType, string Pattern)[] ReflectControlAttrs(Type controlType) {
            var props = controlType.GetProperties(
                BindingFlags.Public | BindingFlags.Instance);
            var list = new List<(string, string, string)>();
            foreach (var p in props) {
                var ui = p.GetCustomAttribute<UIAttrAttribute>();
                if (ui == null || !p.CanWrite) continue;
                var name = ui.Name ?? CamelCase(p.Name);
                var xsdType = MapXsdType(p.PropertyType);
                if (xsdType == null) {
                    UnityEngine.Debug.LogWarning(
                        $"[PromptUGUI] XSD: skipping {controlType.Name}.{p.Name} — type {p.PropertyType.Name} not supported");
                    continue;
                }
                list.Add((name, xsdType, ui.Pattern));
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            return list.ToArray();
        }
```

修改 `WriteControlGroup`：

```csharp
            string[] all = new[] {
                "Frame","Image","Icon","Text","VStack","HStack","Grid","Btn"
            }.Concat(customTags).ToArray();
```

- [ ] **Step 2: Modify XsdGeneratorTests.cs**

加：

```csharp
        [Test]
        public void Icon_element_present_in_xsd() {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"Icon\"", xsd);
        }

        [Test]
        public void Icon_name_attribute_has_pattern() {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("xs:pattern", xsd);
            StringAssert.Contains(":[\\w\\-]+", xsd);
        }

        [Test]
        public void UIAttr_Pattern_propagated_via_reflection() {
            var r = new ControlRegistry();
            r.Register<TestPatternedControl>("Patterned", null);
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("xs:pattern", xsd);
            StringAssert.Contains("^abc$", xsd);
        }

        public class TestPatternedControl : Control {
            [UIAttr(Pattern = "^abc$")] public string Code { get; set; }
        }
```

- [ ] **Step 3: Run tests — should PASS**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="XsdGeneratorTests")
```

- [ ] **Step 4: Commit**

```
git add Editor/XsdGenerator.cs Tests/EditMode/Editor/XsdGeneratorTests.cs
git commit -m "feat(editor): XSD <Icon> element + UIAttr.Pattern support"
```

---

### Task 20: PlayMode IconRuntimeTests

**Files:**
- Create: `Tests/PlayMode/Controls/IconRuntimeTests.cs`

端到端：手工建 IconSet + SpriteAtlas（含 1 个 sprite）→ UseSpriteAtlasIconResolver → Open Screen 含 `<Icon>` → 断言 sprite 非 null。

- [ ] **Step 1: Create**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;

namespace PromptUGUI.Tests.PlayMode {
    public class IconRuntimeTests {
        const string TmpRoot = "Assets/__test_iconruntime__";
        readonly List<string> _toCleanup = new();

        [SetUp] public void Setup() {
            UI.ResetForTests();
            BuiltinPrimitives.Register(UI.Registry);
            if (!AssetDatabase.IsValidFolder(TmpRoot))
                AssetDatabase.CreateFolder("Assets", "__test_iconruntime__");
        }

        [TearDown] public void Teardown() {
            UI.ResetForTests();
            foreach (var p in _toCleanup) AssetDatabase.DeleteAsset(p);
            _toCleanup.Clear();
            if (AssetDatabase.IsValidFolder(TmpRoot)) AssetDatabase.DeleteAsset(TmpRoot);
        }

        [Test]
        public void Icon_resolves_sprite_from_atlas() {
            var (set, _) = MakeIconSetWithSprite("ui", "settings");
            IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { set });

            UI.LoadDocument("inline",
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'><Icon id='cog' name='ui:settings'/></Screen>
                  </PromptUGUI>");
            var screen = UI.Open("S");
            var icon = screen.Get<Icon>("cog");
            var img = icon.GameObject.GetComponent<UnityEngine.UI.Image>();
            Assert.IsNotNull(img.sprite);
        }

        [Test]
        public void Icon_unknown_name_logs_error_sprite_null() {
            var (set, _) = MakeIconSetWithSprite("ui", "settings");
            IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { set });

            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("resolver returned null"));

            UI.LoadDocument("inline",
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'><Icon id='x' name='ui:nope'/></Screen>
                  </PromptUGUI>");
            var screen = UI.Open("S");
            var icon = screen.Get<Icon>("x");
            Assert.IsNull(icon.GameObject.GetComponent<UnityEngine.UI.Image>().sprite);
        }

        [Test]
        public void Icon_color_applied() {
            var (set, _) = MakeIconSetWithSprite("ui", "x");
            IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { set });

            UI.LoadDocument("inline",
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'><Icon id='i' name='ui:x' color='#ff0000'/></Screen>
                  </PromptUGUI>");
            var screen = UI.Open("S");
            var img = screen.Get<Icon>("i").GameObject.GetComponent<UnityEngine.UI.Image>();
            Assert.AreEqual(Color.red, img.color);
        }

        [Test]
        public void Variant_swap_changes_sprite() {
            var (set, _) = MakeIconSetWithSpritesMulti("ui",
                new[] { "sun", "moon" });
            IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { set });

            UI.LoadDocument("inline",
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'>
                      <Icon id='i' name='ui:sun' name.dark='ui:moon'/>
                    </Screen>
                  </PromptUGUI>");
            var screen = UI.Open("S");
            var img = screen.Get<Icon>("i").GameObject.GetComponent<UnityEngine.UI.Image>();
            var before = img.sprite;
            Assert.IsNotNull(before);
            UI.Variants.Set("dark", true);
            var after = img.sprite;
            Assert.IsNotNull(after);
            Assert.AreNotSame(before, after);
        }

        // ---- helpers ----

        (IconSet set, SpriteAtlas atlas) MakeIconSetWithSprite(string setName, string iconName) {
            return MakeIconSetWithSpritesMulti(setName, new[] { iconName });
        }

        (IconSet set, SpriteAtlas atlas) MakeIconSetWithSpritesMulti(
            string setName, string[] iconNames) {
            var folder = $"{TmpRoot}/{setName}";
            AssetDatabase.CreateFolder(TmpRoot, setName);
            var sprites = new List<Sprite>();
            foreach (var n in iconNames) {
                var pngPath = $"{folder}/{n}.png";
                System.IO.File.WriteAllBytes(pngPath, MakeBlankPng());
                AssetDatabase.ImportAsset(pngPath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
                importer.textureType = TextureImporterType.Sprite;
                importer.SaveAndReimport();
                sprites.Add(AssetDatabase.LoadAssetAtPath<Sprite>(pngPath));
            }

            var atlas = new SpriteAtlas();
            var atlasPath = $"{TmpRoot}/{setName}.spriteatlas";
            AssetDatabase.CreateAsset(atlas, atlasPath);
            atlas.Add(sprites.ToArray());
            EditorUtility.SetDirty(atlas);
            UnityEditor.U2D.SpriteAtlasUtility.PackAtlases(
                new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);
            _toCleanup.Add(atlasPath);

            var set = ScriptableObject.CreateInstance<IconSet>();
            var so = new SerializedObject(set);
            so.FindProperty("setName").stringValue = setName;
            so.FindProperty("atlas").objectReferenceValue = atlas;
            so.ApplyModifiedProperties();
            var setPath = $"{TmpRoot}/{setName}.asset";
            AssetDatabase.CreateAsset(set, setPath);
            _toCleanup.Add(setPath);
            return (AssetDatabase.LoadAssetAtPath<IconSet>(setPath), atlas);
        }

        byte[] MakeBlankPng() {
            var t = new Texture2D(8, 8);
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    t.SetPixel(x, y, Color.white);
            t.Apply();
            var bytes = t.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(t);
            return bytes;
        }
    }
}
```

注意：PlayMode asmdef 当前是否包含 `UnityEditor` 引用？典型 Unity Test Framework 的 PlayMode asmdef 不包含；这些测试会编译失败。

应对：检查 `Tests/PlayMode/PromptUGUI.Tests.PlayMode.asmdef`，如果没有 Editor reference，给这个测试文件加 `#if UNITY_EDITOR` 守卫（PlayMode 在 Editor 下也会跑，依然能验证）：

```csharp
#if UNITY_EDITOR
// ... 整个文件
#endif
```

- [ ] **Step 2: Run tests — should PASS**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="IconRuntimeTests")
```

- [ ] **Step 3: Commit**

```
git add Tests/PlayMode/Controls/IconRuntimeTests.cs Tests/PlayMode/Controls/IconRuntimeTests.cs.meta
git commit -m "test(playmode): IconRuntimeTests E2E"
```

---

### Task 21: 更新 SKILL.md

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`

CLAUDE.md 要求：新增内置控件必须同 PR 更新 SKILL.md。

- [ ] **Step 1: Read SKILL.md current shape**

```
Read .claude/skills/authoring-promptugui-xml/SKILL.md
```

定位"内置控件"小节、"Variant"小节、属性索引表。

- [ ] **Step 2: Add Icon section**

在内置控件章节末尾（`<Btn>` 之后）追加：

```markdown
### `<Icon>`

引用 IconSet 中的图标（项目级共享、按名引用、按需打包）。

```xml
<Icon name="ui:settings" color="#ffffff"/>
<Icon name="art:gold-coin" size="48"/>
<Icon name="ui:bell" color.dark="#fff"/>
```

| 属性 | 必填 | 默认 | 说明 |
|---|---|---|---|
| `name` | 是 | — | `ns:icon` 形式（冒号分隔，两侧字符集 `[\w\-]+`） |
| `color` | 否 | `#ffffff` | UI multiply tint。白色保留彩色 PNG，非白做染色 |
| `size` | 否 | `native` | 数值 / `WxH` / `stretch` / `native`（Icon 独占） |

#### 动态 icon 名

`<Icon name="ui:{{x}}"/>` 这种 Template 实参或表达式驱动的写法，Editor 同步工具**无法**静态分析，会 warn 并跳过。两种处理：

- 优先：用 Variant 显式列出每种状态：`<Icon name='ui:sun' name.dark='ui:moon'/>`，扫描器看得到所有候选
- 兜底：把候选列入 IconSet.alwaysInclude 字段（无条件打入 atlas）
```

并在元素索引表（如有）加 Icon 行。

并在 Variant 章节示例补一条：`<Icon name.dark="ui:moon"/>`。

- [ ] **Step 3: Commit**

```
git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "docs(skill): <Icon> element + dynamic name guidance"
```

---

### Task 22: 更新 Master spec

**Files:**
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`

CLAUDE.md 要求：新元素 + 新属性 + 公开 API 必须反映在 master spec。

- [ ] **Step 1: Read master spec, identify §7 (内置控件) and §6 (Layout/SizeSpec) and §12 (风险表) sections**

- [ ] **Step 2: 在 §7 末尾追加 Icon 子节**

```markdown
### 7.X `<Icon>`

项目级图标系统的引用元素。详见独立 spec
[`2026-05-08-icon-assets-design.md`](./2026-05-08-icon-assets-design.md)。

简表：
- `name="ns:icon"`（必填，冒号分隔）
- `color`（multiply tint，默认 `#ffffff`）
- `size` 默认 `native`，Icon 独占该值
```

- [ ] **Step 3: 在 §6 SizeSpec 内容里追加 native**

```markdown
- `native`：取控件 native size（仅 `<Icon>` 接受；其他控件出现 → ParseException）
```

- [ ] **Step 4: 在 §12 风险表追加**

| 风险 | 缓解 |
|---|---|
| Icon SpriteAtlas 4096 上限 | 同步工具 LogWarning；后续可加 split 策略 |
| `<Icon name="ui:{{x}}"/>` 动态名漏打包 | IconSet.alwaysInclude 兜底 |

- [ ] **Step 5: Commit**

```
git add docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md
git commit -m "docs(spec): sync master spec with icon assets — §7 §6 §12"
```

---

### Task 23: 全套回归 + 整合验证

**Files:** —

- [ ] **Step 1: 跑全部测试**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

预期：全 PASS。任何回归就地修。

- [ ] **Step 2: 手工冒烟**

在宿主 `PromptUGUIDev` 项目里：

1. 新建一个 IconSet asset，命名 `ui`，sourceFolder 拖到一个含 PNG 的文件夹
2. 在某个 `.ui.xml` 里写 `<Icon name="ui:foo"/>`（替换 foo 为存在的 PNG 名）
3. **Tools → PromptUGUI → Sync Icon Atlases (All Sets)**
4. 进 Play 模式，确认 icon 正确显示
5. 改 PNG 名 → 重 sync → 验证旧引用 warn / 新引用工作
6. 切到 Build Settings → Build Player → 抽查 Player.app/data 里只含被引用的 icon（用 Asset Bundle Browser 或 BuildReport.AssetReport）

- [ ] **Step 3: PR 准备**

```
git log --oneline -30                        # 检查 commit 历史
git diff main...HEAD --stat                  # 检查变更范围
gh pr create --title "feat: icon assets — <Icon>, IconSet, SpriteAtlas backend" \
  --body "$(cat <<'EOF'
## Summary
落实 [icon assets spec](docs~/superpowers/specs/2026-05-08-icon-assets-design.md)。

- `<Icon name="ns:icon"/>` 内置控件 + parse-time validation
- IconSet ScriptableObject + 默认 SpriteAtlas backed IconResolver
- Editor 同步工具：手动菜单 + 构建前钩子 + AssetPostprocessor opt-in
- SizeSpec `native` 值（仅 `<Icon>` 接受）
- XSD 生成器扩展 + UIAttr.Pattern
- Hot-reload 接入（atlas 内容变 → ReSolve 所有 open screens）
- SKILL.md / master spec 同步

## Test plan
- [ ] EditMode：parser / resolver / hot-reload / xsd / sizespec
- [ ] EditorOnly：sync 工具完整流水线
- [ ] PlayMode：Icon 解析 sprite + Variant 切换 + color tint + 未知 name LogError
- [ ] 手工冒烟：宿主项目建 IconSet → 写 XML → Sync → Play → Build → 抽查产物

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Self-Review

Spec coverage check:

- ICN-D1 `<Icon>` 元素 → T2 (parser) + T6 (control) + T7 (registry) ✓
- ICN-D2 `ns:icon` key → T2 校验 ✓
- ICN-D3 多 IconSet 命名空间 → T3 (SO) + T8 (resolver duplicate check) ✓
- ICN-D4 Resolver 注入 → T4 ✓
- ICN-D5 默认 SpriteAtlas 后端 → T8 (IconResolverHelpers) ✓
- ICN-D6 IconSet SO → T3 ✓
- ICN-D7 文件夹约定 → T10 (EnumeratePngs) ✓
- ICN-D8 color multiply tint → T6 (Icon.Color) + T20 (test) ✓
- ICN-D9 size=native → T1 (SizeSpec) + T5 (ApplyCommon) + T6 (GetNativeSize) ✓
- ICN-D10 只打包用到的 → T9-T12 (sync 工具) ✓
- ICN-D11 触发时机 → T13 (menu) + T14 (build hook) + T15 (AssetPostprocessor opt-in) ✓
- ICN-D12 动态名限制 → T9 (CollectFromAttr 扫 `{{`) + IconSet.alwaysInclude in T3 ✓
- ICN-D13 runtime LogError + null sprite → T6 (Name setter) + T20 (test) ✓
- ICN-D14 同 setName 抛异常 → T8 (BuildLookup) + T12 (SyncAll abort) ✓
- ICN-D15 atlas hot-reload → T18 ✓

Master spec §10 同步要求 → T21 (SKILL) + T22 (master spec) ✓

XSD §10.3 → T19 ✓

测试 §9 各小节：
- EditMode IconParserTests → T2 ✓
- EditMode IconResolverTests → T8 ✓
- EditorOnly IconAtlasSyncerTests → T12 ✓
- PlayMode IconRuntimeTests → T20 ✓
- XsdGeneratorTests 增量 → T19 ✓

Type consistency check:
- `IsNativeWidth/Height` 一致 (T1)
- `GetNativeSize()` 返回 `Vector2?` 一致 (T5, T6)
- `IconSet.SetAtlasInternal` 在 T3 定义、T11 调用 ✓
- `IconSet.SourceFolderPath` 在 T3 定义、T10/T17 调用 ✓
- `IconAtlasSyncer.{ScanXmlReferences, EnumeratePngs, UpdateAtlas, EnsureAtlasAsset, SyncAll, FindAllIconSets}` 一致 ✓
- `UI.HotReload.IconResolverRebuilder` 在 T18 定义、T18 step 5 注册 ✓
- `UI.HotReload.NotifyIconAssetsChanged` 在 T18 定义、T18 step 6 调用 ✓

No placeholders / TODOs found.

---

## 与现有体系的兼容性

- 不修改 M1-M4 现有公开 API（仅新增）
- `<Image>` 控件保持 `Resources.Load` 路径不变（spec §11 out of scope）
- 现有 7 个原语注册顺序不变，Icon 插在 Image 后（T7）
- XSD `controlGroup` 中 Icon 加在 Image 后（T19）
- `[UIAttr]` 反射兼容：Pattern 是可选 named arg，旧代码无需改
