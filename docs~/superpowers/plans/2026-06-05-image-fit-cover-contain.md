# `<Image>` fit 模式（cover / contain）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 `<Image type=>` 增加 `contain` / `cover` 两个值，经 Unity 内置 `AspectRatioFitter` 相对父级做纵横比适配，并加两条 lint 规则。

**Architecture:** `Image.Type` setter 扩出 `contain`/`cover` 两个分支：强制 `Image.Type.Simple` + 懒挂一个 `AspectRatioFitter`（`contain`=FitInParent、`cover`=EnvelopeParent，靠 `enabled` 复用、绝不 Destroy），`aspectRatio` 在 `OnAfterApply` 用最终 sprite 算。裁切由作者在父框 `mask="rect"` 负责。两条 lint：`PUI-IMAGE-FIT-VARIANT`（fit 值进变体，运行期+CLI，仿 mask）、`PUI-IMAGE-FIT-GEOMETRY`（fit 模式下 Image 自身几何属性失效，CLI-only，仿 margin-inert）。

**Tech Stack:** Unity 6 uGUI（`UnityEngine.UI.AspectRatioFitter`）、C# 9、NUnit EditMode、纯 C# Lint（`Runtime/Core/Lint/`）、UnityMCP 跑测试。

**分支:** `feat/image-fit-cover-contain`（已建，spec 已提交 `c220bce`）。**禁止提交到 main。**

**关联 spec:** `docs~/superpowers/specs/2026-06-05-image-fit-cover-contain-design.md`

---

## File Structure

| 文件 | 动作 | 职责 |
|---|---|---|
| `Runtime/Controls/Image.cs` | Modify | `Type` setter 加 contain/cover 分支 + `_fitter` 字段 + `EnsureFitter()`；`OnAfterApply` 设 `aspectRatio` |
| `Runtime/Core/Lint/ImageFitRules.cs` | Create | 两条 fit lint 规则（`CheckVariant` 运行期+CLI、`CheckGeometry` CLI-only） |
| `Runtime/Core/Lint/IRWalker.cs` | Modify | Image 分支派发两条 fit 规则（CLI） |
| `Runtime/Application/ScreenInstantiator.cs` | Modify | Image 分支派发 `CheckVariant`（运行期 warning） |
| `Tests/EditMode/Controls/ImageFitTests.cs` | Create | 控件行为测试（ARF 配置、aspectRatio、teardown） |
| `Tests/EditMode/Lint/ImageFitRulesTests.cs` | Create | 规则级单测 |
| `Tests/EditMode/Lint/IRWalkerImageFitTests.cs` | Create | IRWalker 派发集成测试 |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` | Modify | `<Image>` type 取值 + 两条 lint + 写法（英文） |
| `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` | Modify | `<Image>` 行示例补 contain/cover |

---

## Task 1: Image 控件 — contain / cover → AspectRatioFitter

**Files:**
- Modify: `Runtime/Controls/Image.cs`
- Test: `Tests/EditMode/Controls/ImageFitTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `Tests/EditMode/Controls/ImageFitTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;
using PromptUGUIImage = PromptUGUI.Controls.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ImageFitTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Sprite = "PromptUGUI/Defaults/pugui#pugui_9slice_round";

        private static PromptUGUIImage Build(string typeAttr)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='box' size='320x180'>
    <Image id='i' sprite='{Sprite}' type='{typeAttr}'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<PromptUGUIImage>("i");
        }

        [Test]
        public void Contain_AddsAspectRatioFitter_FitInParent_TypeSimple()
        {
            var img = Build("contain");
            var arf = img.GameObject.GetComponent<AspectRatioFitter>();
            Assert.IsNotNull(arf, "contain must add an AspectRatioFitter");
            Assert.IsTrue(arf.enabled);
            Assert.AreEqual(AspectRatioFitter.AspectMode.FitInParent, arf.aspectMode);
            Assert.AreEqual(Image.Type.Simple, img.GameObject.GetComponent<Image>().type);
        }

        [Test]
        public void Cover_AddsAspectRatioFitter_EnvelopeParent_TypeSimple()
        {
            var img = Build("cover");
            var arf = img.GameObject.GetComponent<AspectRatioFitter>();
            Assert.IsNotNull(arf);
            Assert.IsTrue(arf.enabled);
            Assert.AreEqual(AspectRatioFitter.AspectMode.EnvelopeParent, arf.aspectMode);
            Assert.AreEqual(Image.Type.Simple, img.GameObject.GetComponent<Image>().type);
        }

        [Test]
        public void Cover_AspectRatio_MatchesSpriteRect()
        {
            var img = Build("cover");
            var unityImg = img.GameObject.GetComponent<Image>();
            var arf = img.GameObject.GetComponent<AspectRatioFitter>();
            var expected = unityImg.sprite.rect.width / unityImg.sprite.rect.height;
            Assert.AreEqual(expected, arf.aspectRatio, 0.001f);
        }

        [Test]
        public void Simple_NoAspectRatioFitter()
        {
            var img = Build("simple");
            Assert.IsNull(img.GameObject.GetComponent<AspectRatioFitter>());
        }

        [Test]
        public void NoTypeAttr_NoAspectRatioFitter()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' sprite='" + Sprite + @"'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var img = UI.Open("S").Get<PromptUGUIImage>("i");
            Assert.IsNull(img.GameObject.GetComponent<AspectRatioFitter>());
        }

        [Test]
        public void Cover_NoSprite_DoesNotThrow_NoAspectRatioUpdate()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='box' size='320x180'><Image id='i' type='cover'/></Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            // 不抛即可（无 sprite 时不更新 aspectRatio）
            var img = UI.Open("S").Get<PromptUGUIImage>("i");
            Assert.IsNotNull(img.GameObject.GetComponent<AspectRatioFitter>());
        }

        [Test]
        public void BaseCover_VariantSliced_TogglesFitterEnabled()
        {
            // base cover + 非 fit 变体值（lint 允许）：验证 teardown 走 enabled 开关。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='box' size='320x180'>
    <Image id='i' sprite='" + Sprite + @"' type='cover' type.mobile='sliced'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var img = UI.Open("S").Get<PromptUGUIImage>("i");
            var arf = img.GameObject.GetComponent<AspectRatioFitter>();
            Assert.IsTrue(arf.enabled, "base cover → fitter enabled");

            UI.Variants.Set("mobile", true);
            Assert.IsFalse(arf.enabled, "variant sliced → fitter disabled");
            Assert.AreEqual(Image.Type.Sliced, img.GameObject.GetComponent<Image>().type);

            UI.Variants.Set("mobile", false);
            Assert.IsTrue(arf.enabled, "back to base cover → fitter re-enabled");
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageFitTests")
```
Expected: 编译通过、`ImageFitTests` 全部 FAIL（contain/cover 还没实现，ARF 为 null）。

- [ ] **Step 3: 实现 Image.cs**

在 `Runtime/Controls/Image.cs` 加字段（`_typeExplicit` 旁边，约 line 16）：

```csharp
        private bool _typeExplicit;
        private AspectRatioFitter _fitter;
```

把 `Type` setter（当前 line 53-67）整段替换为：

```csharp
        [UIAttr, Preserve]
        public string Type
        {
            set
            {
                _typeExplicit = true;
                switch (value)
                {
                    case "contain":
                    case "cover":
                        // Fit 模式：sprite 完整画进 ARF 算好的 rect（9-slice 对 contain/cover 无意义）。
                        // 框 = 父级 rect，由 AspectRatioFitter 相对父级驱动；Image 自身 anchor/size 被接管。
                        _img.type = UnityImage.Type.Simple;
                        var f = EnsureFitter();
                        f.enabled = true;
                        f.aspectMode = value == "cover"
                            ? AspectRatioFitter.AspectMode.EnvelopeParent
                            : AspectRatioFitter.AspectMode.FitInParent;
                        break;
                    default:
                        _img.type = value switch
                        {
                            "sliced" => UnityImage.Type.Sliced,
                            "tiled" => UnityImage.Type.Tiled,
                            "filled" => UnityImage.Type.Filled,
                            _ => UnityImage.Type.Simple,
                        };
                        if (_fitter != null) _fitter.enabled = false;
                        break;
                }
            }
        }

        private AspectRatioFitter EnsureFitter()
            => _fitter ??= GameObject.AddComponent<AspectRatioFitter>();
```

把 `OnAfterApply`（当前 line 112-122）整段替换为：

```csharp
        internal override void OnAfterApply()
        {
            // Fit 模式：用最终 sprite 算 aspectRatio（Sprite/Type setter 同循环、顺序不保证，
            // 这里在所有 setter 之后跑；sprite 变化（含 variant 换图）也会重算）。
            if (_fitter != null && _fitter.enabled && _img.sprite != null)
            {
                var r = _img.sprite.rect;
                if (r.height > 0f) _fitter.aspectRatio = r.width / r.height;
            }

            // Auto-pick Sliced for 9-slice sprites when author didn't write type=.
            // Sprite border is set in the Sprite Editor; non-zero on any edge means the
            // asset was authored for 9-slice rendering.
            if (_typeExplicit) return;
            var s = _img.sprite;
            _img.type = (s != null && s.border != Vector4.zero)
                ? UnityImage.Type.Sliced
                : UnityImage.Type.Simple;
        }
```

- [ ] **Step 4: 跑测试确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageFitTests")
```
Expected: 控制台无编译 error；`ImageFitTests` 7 个全 PASS。

- [ ] **Step 5: 提交**

```bash
git add Runtime/Controls/Image.cs Tests/EditMode/Controls/ImageFitTests.cs
git commit -m "feat(image): type=cover/contain via AspectRatioFitter

contain→FitInParent, cover→EnvelopeParent (relative to parent),
forces Image.Type.Simple, lazy fitter reused via enabled, aspectRatio
tracked in OnAfterApply. Clipping is the author's job (parent mask).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: ImageFitRules lint 规则

**Files:**
- Create: `Runtime/Core/Lint/ImageFitRules.cs`
- Test: `Tests/EditMode/Lint/ImageFitRulesTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `Tests/EditMode/Lint/ImageFitRulesTests.cs`：

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class ImageFitRulesTests
    {
        private static ElementNode Img(string id = "i")
            => new ElementNode("Image") { Id = id };

        // ===== PUI-IMAGE-FIT-VARIANT =====

        [Test]
        public void FitInVariant_Cover_VariantIssue()
        {
            var n = Img();
            n.VariantOverrides["type"] =
                new List<(string, string)> { ("mobile", "cover") };
            var issues = ImageFitRules.CheckVariant(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ImageFitRules.VariantCode, issues[0].Code);
            StringAssert.Contains("cover", issues[0].Message);
        }

        [Test]
        public void FitInVariant_Contain_VariantIssue()
        {
            var n = Img();
            n.VariantOverrides["type"] =
                new List<(string, string)> { ("portrait", "contain") };
            var issues = ImageFitRules.CheckVariant(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ImageFitRules.VariantCode, issues[0].Code);
        }

        [Test]
        public void NonFitInVariant_NoIssue()
        {
            // type.mobile="sliced" 是纯枚举、无组件 → 不报。
            var n = Img();
            n.VariantOverrides["type"] =
                new List<(string, string)> { ("mobile", "sliced") };
            Assert.IsEmpty(ImageFitRules.CheckVariant(n));
        }

        [Test]
        public void BaseCover_NoVariant_NoVariantIssue()
        {
            // 基础 type="cover" 稳定可用 → 不是变体问题。
            var n = Img();
            n.Attributes["type"] = "cover";
            Assert.IsEmpty(ImageFitRules.CheckVariant(n));
        }

        // ===== PUI-IMAGE-FIT-GEOMETRY =====

        [Test]
        public void CoverWithSize_GeometryIssue()
        {
            var n = Img();
            n.Attributes["type"] = "cover";
            n.Attributes["size"] = "100x100";
            var issues = ImageFitRules.CheckGeometry(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ImageFitRules.GeometryCode, issues[0].Code);
            StringAssert.Contains("size", issues[0].Message);
        }

        [Test]
        public void ContainWithAnchorAndMargin_GeometryIssue_ListsBoth()
        {
            var n = Img();
            n.Attributes["type"] = "contain";
            n.Attributes["anchor"] = "center";
            n.Attributes["margin"] = "8";
            var issues = ImageFitRules.CheckGeometry(n).ToList();
            Assert.AreEqual(1, issues.Count, "one combined issue per Image");
            StringAssert.Contains("anchor", issues[0].Message);
            StringAssert.Contains("margin", issues[0].Message);
        }

        [Test]
        public void CoverNoGeometry_NoIssue()
        {
            var n = Img();
            n.Attributes["type"] = "cover";
            Assert.IsEmpty(ImageFitRules.CheckGeometry(n));
        }

        [Test]
        public void SimpleWithSize_NoIssue()
        {
            // 非 fit 模式：size 有效，不报。
            var n = Img();
            n.Attributes["type"] = "simple";
            n.Attributes["size"] = "100x100";
            Assert.IsEmpty(ImageFitRules.CheckGeometry(n));
        }

        [Test]
        public void CoverWithPivot_NoIssue()
        {
            // pivot 不被 ARF 接管 → 不报。
            var n = Img();
            n.Attributes["type"] = "cover";
            n.Attributes["pivot"] = "0,0";
            Assert.IsEmpty(ImageFitRules.CheckGeometry(n));
        }

        [Test]
        public void CoverWithVariantGeometry_GeometryIssue()
        {
            // 变体形态的几何属性同样失效。
            var n = Img();
            n.Attributes["type"] = "cover";
            n.VariantOverrides["width"] =
                new List<(string, string)> { ("mobile", "100") };
            var issues = ImageFitRules.CheckGeometry(n).ToList();
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("width", issues[0].Message);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
```
Expected: 编译 FAIL（`ImageFitRules` 不存在，CS0103/CS0246）。

- [ ] **Step 3: 实现 ImageFitRules.cs**

创建 `Runtime/Core/Lint/ImageFitRules.cs`：

```csharp
using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Fit-mode lint rules for <c>&lt;Image type="cover"/"contain"&gt;</c>.
    /// FIT-VARIANT is shared by <c>IRWalker</c> (CLI) + <c>ScreenInstantiator</c> (runtime warning),
    /// like the mask rules — a fit value in a variant adds an AspectRatioFitter that can't be torn
    /// down when the variant turns off (ControlAttributeApplier skips null-resolving setters).
    /// FIT-GEOMETRY is CLI-only (a static authoring nit with no runtime effect), like PUI-MARGIN-INERT-SIDE.
    /// Single source of truth shared with the runtime warning path.
    /// </summary>
    public static class ImageFitRules
    {
        public const string VariantCode = "PUI-IMAGE-FIT-VARIANT";
        public const string GeometryCode = "PUI-IMAGE-FIT-GEOMETRY";

        private static readonly string[] GeometryAttrs = { "anchor", "size", "width", "height", "margin" };

        private static bool IsFit(string v) => v == "cover" || v == "contain";

        /// <summary>Runtime + CLI: a fit value (cover/contain) inside a type.&lt;variant&gt; override.</summary>
        public static IEnumerable<LintIssue> CheckVariant(ElementNode n)
        {
            if (n.VariantOverrides.TryGetValue("type", out var overrides))
            {
                foreach (var (variant, value) in overrides)
                {
                    if (IsFit(value))
                    {
                        yield return new LintIssue(
                            VariantCode, n.Tag, n.Id,
                            $"<Image id='{n.Id}'>: type=\"{value}\" in a variant override (type.{variant}) is not " +
                            "supported in v1. Switching to/from a fit mode adds/removes an AspectRatioFitter, which " +
                            "can't be torn down when the variant turns off. Use a fixed base type=, or split into " +
                            "per-orientation Screens / <Add into=...>.");
                        yield break; // one issue per Image
                    }
                }
            }
        }

        /// <summary>CLI-only: own anchor/size/width/height/margin under a fit mode (overridden by ARF).</summary>
        public static IEnumerable<LintIssue> CheckGeometry(ElementNode n)
        {
            if (!n.Attributes.TryGetValue("type", out var type) || !IsFit(type))
                yield break;

            var offenders = new List<string>();
            foreach (var attr in GeometryAttrs)
                if (n.Attributes.ContainsKey(attr) || n.VariantOverrides.ContainsKey(attr))
                    offenders.Add(attr);

            if (offenders.Count > 0)
                yield return new LintIssue(
                    GeometryCode, n.Tag, n.Id,
                    $"<Image id='{n.Id}'>: {string.Join(", ", offenders)} on a type=\"{type}\" Image " +
                    "have no effect — AspectRatioFitter sizes the Image to its PARENT, overriding the Image's own " +
                    "anchor/size/width/height/margin. Put the size on the parent container instead.");
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageFitRulesTests")
```
Expected: 无编译 error；`ImageFitRulesTests` 10 个全 PASS。

- [ ] **Step 5: 提交**

```bash
git add Runtime/Core/Lint/ImageFitRules.cs Tests/EditMode/Lint/ImageFitRulesTests.cs
git commit -m "feat(lint): PUI-IMAGE-FIT-VARIANT + PUI-IMAGE-FIT-GEOMETRY rules

FIT-VARIANT (runtime+CLI): fit value in a type.<variant> override.
FIT-GEOMETRY (CLI-only): anchor/size/width/height/margin on a fit Image.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: 派发规则（IRWalker CLI + ScreenInstantiator 运行期）

**Files:**
- Modify: `Runtime/Core/Lint/IRWalker.cs:45-47`
- Modify: `Runtime/Application/ScreenInstantiator.cs:189-191`
- Test: `Tests/EditMode/Lint/IRWalkerImageFitTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `Tests/EditMode/Lint/IRWalkerImageFitTests.cs`：

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class IRWalkerImageFitTests
    {
        private static List<LintIssue> Lint(string body)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{body}</Screen></PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            return IRWalker.Walk(doc).ToList();
        }

        [Test]
        public void FitInVariant_SurfacedByWalker()
        {
            var issues = Lint(@"<Image id='i' sprite='x' type='simple' type.mobile='cover'/>");
            Assert.IsTrue(issues.Any(i => i.Code == ImageFitRules.VariantCode));
        }

        [Test]
        public void FitGeometry_SurfacedByWalker()
        {
            var issues = Lint(@"<Image id='i' sprite='x' type='cover' size='100x100'/>");
            Assert.IsTrue(issues.Any(i => i.Code == ImageFitRules.GeometryCode));
        }

        [Test]
        public void CleanFitImage_NoFitIssues()
        {
            // type="cover" 无几何、无变体 → 无 fit 规则命中。
            var issues = Lint(@"<Frame id='box' size='320x180'><Image id='i' sprite='x' type='cover'/></Frame>");
            Assert.IsFalse(issues.Any(i =>
                i.Code == ImageFitRules.GeometryCode || i.Code == ImageFitRules.VariantCode));
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IRWalkerImageFitTests")
```
Expected: 编译通过、3 个测试里 `FitInVariant_SurfacedByWalker` / `FitGeometry_SurfacedByWalker` FAIL（还没派发）。

- [ ] **Step 3: 派发到 IRWalker**

`Runtime/Core/Lint/IRWalker.cs`，把 Image 分支（当前 line 45-47）：

```csharp
            else if (node.Tag == "Image")
                foreach (var issue in MaskAttributeRules.CheckImage(node))
                    yield return issue;
```

改为：

```csharp
            else if (node.Tag == "Image")
            {
                foreach (var issue in MaskAttributeRules.CheckImage(node))
                    yield return issue;
                foreach (var issue in ImageFitRules.CheckVariant(node))
                    yield return issue;
                foreach (var issue in ImageFitRules.CheckGeometry(node))
                    yield return issue;
            }
```

- [ ] **Step 4: 派发到 ScreenInstantiator（运行期，仅 CheckVariant）**

`Runtime/Application/ScreenInstantiator.cs`，把 Image 分支（当前 line 189-191）：

```csharp
            else if (node.Tag == "Image")
                foreach (var issue in MaskAttributeRules.CheckImage(node))
                    Debug.LogWarning(issue.Message);
```

改为：

```csharp
            else if (node.Tag == "Image")
            {
                foreach (var issue in MaskAttributeRules.CheckImage(node))
                    Debug.LogWarning(issue.Message);
                // FIT-VARIANT only — FIT-GEOMETRY is CLI-only (inert, zero runtime cost).
                foreach (var issue in ImageFitRules.CheckVariant(node))
                    Debug.LogWarning(issue.Message);
            }
```

> `ScreenInstantiator.cs` 已有 `using PromptUGUI.Lint;`（line 5），故 `ImageFitRules` 直接用即可。

- [ ] **Step 5: 跑测试确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IRWalkerImageFitTests")
```
Expected: 无 error；3 个全 PASS。

- [ ] **Step 6: 提交**

```bash
git add Runtime/Core/Lint/IRWalker.cs Runtime/Application/ScreenInstantiator.cs Tests/EditMode/Lint/IRWalkerImageFitTests.cs
git commit -m "feat(lint): dispatch image-fit rules (IRWalker CLI + runtime variant)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: 文档（主 spec + XML SKILL）

**Files:**
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md:195`
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`

- [ ] **Step 1: 主 spec 示例补 contain/cover**

`docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` 第 195 行：

```
- `<Image sprite="bg/main" color="#FFFFFFAA" type="sliced|simple|filled|tiled"/>`
```

改为：

```
- `<Image sprite="bg/main" color="#FFFFFFAA" type="sliced|simple|filled|tiled|contain|cover"/>`（`contain`/`cover` 等比适配，相对父级框，裁切作者用父级 `mask="rect"` 负责）
```

- [ ] **Step 2: XML SKILL — `<Image>` 行 type 取值（英文）**

`.claude/skills/authoring-promptugui-xml/SKILL.md`，找到 `<Image>` 行内 `type` 的说明：

```
`type` (`simple` / `sliced` / `tiled` / `filled`; **omit to auto-pick `sliced` when sprite has a non-zero border, else `simple`**)
```

改为：

```
`type` (`simple` / `sliced` / `tiled` / `filled` / `contain` / `cover`; **omit to auto-pick `sliced` when sprite has a non-zero border, else `simple`**. `contain` / `cover` are aspect-fit modes via `AspectRatioFitter` **relative to the parent** — `contain` fits inside (letterbox), `cover` fills + overflows; the parent's rect is the box, so size the **parent**, and for `cover` add `mask="rect"` on the parent to crop the overflow. Under a fit mode the Image's own `anchor`/`size`/`width`/`height`/`margin` are taken over by `AspectRatioFitter` and have no effect (`PUI-IMAGE-FIT-GEOMETRY`). Don't use a fit mode as a direct `<VStack>`/`<HStack>`/`<Grid>` child — wrap in a `<Frame>`. Fit modes are not variant-overridable (`PUI-IMAGE-FIT-VARIANT`))
```

- [ ] **Step 3: XML SKILL — Mask & clipping 节加 cover 写法**

在 SKILL.md 的「Mask & clipping」表格后，补一段：

```markdown
**Aspect-fit (`type="cover"` / `"contain"`) clipping**: the library never auto-clips a `cover` Image — its `AspectRatioFitter` sizes the Image to *envelop* the parent, so the overflow is clipped by a `mask="rect"` (`RectMask2D`) you put on the **parent** frame:

​```xml
<Frame size="320x180" mask="rect">          <!-- box + clip both live on the parent -->
  <Image type="cover" sprite="ui:banner"/>  <!-- no anchor/size; the parent is the box -->
</Frame>
​```

`contain` needs no mask (it fits *inside* the parent, letterboxing against the parent's background).
```

- [ ] **Step 4: XML SKILL — 两条 lint 规则入「Common mistakes」/相应规则表**

在 SKILL.md 里能找到 `PUI-MASK-VARIANT` / `PUI-MARGIN-INERT-SIDE` 描述的位置附近，补两行（英文）说明：

- `PUI-IMAGE-FIT-VARIANT` — `type="cover"`/`"contain"` appearing in a `type.<variant>` override (CLI error + Unity warning); a fit mode adds/removes an `AspectRatioFitter` that can't be torn down on variant-off. Use a fixed base `type=`, or split per-orientation Screens.
- `PUI-IMAGE-FIT-GEOMETRY` — CLI-only warning: `anchor` / `size` / `width` / `height` / `margin` on a `type="cover"`/`"contain"` `<Image>` (inert — `AspectRatioFitter` sizes it to the parent). Move the size to the parent.

- [ ] **Step 5: 提交**

```bash
git add docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "doc(image-fit): document type=cover/contain + fit lint rules

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: 全量验证

**Files:** 无（仅验证）

- [ ] **Step 1: 编译 + 全量 EditMode**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```
Expected: 无编译 error；EditMode 全绿（新增 20 个测试在内，无回归）。

- [ ] **Step 2: XSD 测试无回归**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
```
Expected: 全绿（`type` 仍是 `xs:string`，XSD 未变）。

- [ ] **Step 3: UIXmlLint CLI 端到端 sanity（可选，若本机有 dotnet）**

写一个临时坏文件验证 CLI 非零退出：

```bash
cat > /tmp/fit-bad.ui.xml <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1"><Screen name="S">
  <Image id="i" sprite="ui:x" type="cover" size="100x100"/>
</Screen></PromptUGUI>
EOF
cd .lint && dotnet restore PromptUGUI.Lint.slnx >/dev/null 2>&1; cd ..
dotnet run --project .lint/UIXmlLint -- /tmp/fit-bad.ui.xml; echo "exit=$?"
```
Expected: 输出含 `PUI-IMAGE-FIT-GEOMETRY`，`exit=1`。删除临时文件：`rm /tmp/fit-bad.ui.xml`。

- [ ] **Step 4: dotnet format 静态检查（lint 代码风格）**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: 无 diff（若报风格问题，按提示 `dotnet format whitespace/style` 修，**不要**用 `analyzers --severity info`）。

- [ ] **Step 5: 最终提交（若 format 有改动）**

```bash
git add -A && git commit -m "style: dotnet format image-fit changes

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## 完成标准

- `<Image type="contain">` / `type="cover">` 在运行时挂 `AspectRatioFitter`（FitInParent / EnvelopeParent），`aspectRatio` 跟随 sprite。
- `PUI-IMAGE-FIT-VARIANT`（运行期+CLI）/ `PUI-IMAGE-FIT-GEOMETRY`（CLI-only）按 spec §6/§7 触发。
- EditMode + EditorOnly 全绿，无回归；lint CLI 对坏文件非零退出。
- 主 spec + XML SKILL 已更新（英文 SKILL）。
- 所有提交在 `feat/image-fit-cover-contain`，**未碰 main**。
- 用户视觉 QA（cover 实际填满 + 裁切、contain 留白）留给用户在 host 工程确认。
```
