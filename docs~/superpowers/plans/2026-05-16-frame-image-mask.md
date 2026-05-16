# Frame / Image mask 属性 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 `<Frame>` 加 `mask="rect"` + `maskPadding`，给 `<Image>` 加 `mask="rect|self"` + `showMask` + `maskPadding`。stencil mask 必须显式 `mask="self"`，绝不因 sprite 自动开启。lint + runtime warning 共享一份规则。

**Architecture:**
- 纯 C# parser `MaskPaddingParser`（T,R,B,L ↔ Vector4(L,B,R,T)）
- 纯 C# lint rules `MaskAttributeRules.CheckFrame` / `CheckImage`，由 `IRWalker`（编辑期）+ `ScreenInstantiator`（运行期）双消费
- 控件层 `Frame` / `Image` 用 `[UIAttr]` setter 创建 `RectMask2D` / `Mask` 组件；`_pendingMaskPadding` / `_pendingShowMask` 字段消除 setter 顺序依赖

**Tech Stack:** Unity 6+ uGUI（`RectMask2D` / `Mask`），NUnit（EditMode 测试经 UnityMCP），纯 C# 的 lint & parser 子层。

**依赖 spec:** [`2026-05-16-frame-image-mask-design.md`](../specs/2026-05-16-frame-image-mask-design.md)

**测试入口:**
```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="...")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

每次写完代码 + 跑 lint:
```
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

---

## Task 1: `MaskPaddingParser`

**Files:**
- Create: `Runtime/Core/Layout/MaskPaddingParser.cs`
- Create: `Tests/EditMode/Layout/MaskPaddingParserTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Tests/EditMode/Layout/MaskPaddingParserTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Layout;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Layout
{
    public class MaskPaddingParserTests
    {
        [Test]
        public void Empty_ReturnsZero()
        {
            Assert.AreEqual(Vector4.zero, MaskPaddingParser.Parse(""));
            Assert.AreEqual(Vector4.zero, MaskPaddingParser.Parse(null));
        }

        [Test]
        public void OneComponent_AppliesToAllFour()
        {
            // "8" → top=8, right=8, bottom=8, left=8 → Vector4(L,B,R,T) = (8,8,8,8)
            Assert.AreEqual(new Vector4(8f, 8f, 8f, 8f), MaskPaddingParser.Parse("8"));
        }

        [Test]
        public void TwoComponents_VerticalThenHorizontal()
        {
            // "4,6" → top=bottom=4, right=left=6 → Vector4(L,B,R,T) = (6,4,6,4)
            Assert.AreEqual(new Vector4(6f, 4f, 6f, 4f), MaskPaddingParser.Parse("4,6"));
        }

        [Test]
        public void FourComponents_TRBL_FlippedToLBRT()
        {
            // Author "T,R,B,L" = "1,2,3,4" → Vector4(L,B,R,T) = (4,3,2,1)
            Assert.AreEqual(new Vector4(4f, 3f, 2f, 1f), MaskPaddingParser.Parse("1,2,3,4"));
        }

        [Test]
        public void UnderscorePlaceholder_BecomesZero()
        {
            // "_,16,_,_" → T=0, R=16, B=0, L=0 → Vector4(L,B,R,T) = (0,0,16,0)
            Assert.AreEqual(new Vector4(0f, 0f, 16f, 0f), MaskPaddingParser.Parse("_,16,_,_"));
        }

        [Test]
        public void NegativeValues_AreAllowed()
        {
            // InputField 的 textArea 用了负 padding(-8,-5,-8,-5)
            Assert.AreEqual(new Vector4(-5f, -5f, -8f, -8f), MaskPaddingParser.Parse("-8,-8,-5,-5"));
        }

        [Test]
        public void ThreeComponents_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => MaskPaddingParser.Parse("1,2,3"));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compile error)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: CS0246 `MaskPaddingParser` not found.

- [ ] **Step 3: Implement `MaskPaddingParser`**

Create `Runtime/Core/Layout/MaskPaddingParser.cs`:

```csharp
using System;
using System.Globalization;
using UnityEngine;

namespace PromptUGUI.Layout
{
    /// <summary>
    /// 解析作者层 "T,R,B,L" 1/2/4 分量字符串（"_" = 0 占位），翻转成 Unity 原生
    /// <see cref="UnityEngine.UI.RectMask2D.padding"/> 的 Vector4(L,B,R,T) 顺序。
    /// </summary>
    internal static class MaskPaddingParser
    {
        public static Vector4 Parse(string value)
        {
            if (string.IsNullOrEmpty(value)) return Vector4.zero;
            var parts = value.Split(',');
            float t, r, b, l;
            switch (parts.Length)
            {
                case 1:
                    t = r = b = l = ParseOne(parts[0]);
                    break;
                case 2:
                    t = b = ParseOne(parts[0]);
                    r = l = ParseOne(parts[1]);
                    break;
                case 4:
                    t = ParseOne(parts[0]);
                    r = ParseOne(parts[1]);
                    b = ParseOne(parts[2]);
                    l = ParseOne(parts[3]);
                    break;
                default:
                    throw new ArgumentException(
                        $"maskPadding: expected 1, 2, or 4 components, got {parts.Length} in '{value}'");
            }
            return new Vector4(l, b, r, t);
        }

        private static float ParseOne(string s)
        {
            s = s.Trim();
            if (s == "_") return 0f;
            return float.Parse(s, CultureInfo.InvariantCulture);
        }
    }
}
```

注：`internal` 让同 asmdef（`PromptUGUI.Runtime`）的 Frame/Image 能用；测试通过 `InternalsVisibleTo` 访问。

- [ ] **Step 4: Run tests to verify they pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="MaskPaddingParserTests")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: 7 tests, 7 passed.

- [ ] **Step 5: Lint**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

- [ ] **Step 6: Commit**

```bash
git add Runtime/Core/Layout/MaskPaddingParser.cs Runtime/Core/Layout/MaskPaddingParser.cs.meta \
        Tests/EditMode/Layout/MaskPaddingParserTests.cs Tests/EditMode/Layout/MaskPaddingParserTests.cs.meta
git commit -m "$(cat <<'EOF'
feat: MaskPaddingParser (T,R,B,L → Vector4(L,B,R,T))

Reused by Frame and Image mask attrs; flips author-friendly CSS order
to Unity's RectMask2D.padding native order.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: `MaskAttributeRules.CheckFrame`

**Files:**
- Create: `Runtime/Core/Lint/MaskAttributeRules.cs`
- Create: `Tests/EditMode/Lint/MaskAttributeRulesTests.cs`

- [ ] **Step 1: Write failing tests**

Create `Tests/EditMode/Lint/MaskAttributeRulesTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class MaskAttributeRulesTests
    {
        // ===== Frame =====

        [Test]
        public void Frame_NoMaskAttrs_NoIssue()
        {
            var n = new ElementNode("Frame");
            Assert.IsEmpty(MaskAttributeRules.CheckFrame(n));
        }

        [Test]
        public void Frame_MaskRect_NoIssue()
        {
            var n = new ElementNode("Frame");
            n.Attributes["mask"] = "rect";
            Assert.IsEmpty(MaskAttributeRules.CheckFrame(n));
        }

        [Test]
        public void Frame_MaskSelf_FrameSelfIssue()
        {
            var n = new ElementNode("Frame") { Id = "f" };
            n.Attributes["mask"] = "self";
            var issues = MaskAttributeRules.CheckFrame(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.FrameSelfCode, issues[0].Code);
            StringAssert.Contains("Frame", issues[0].Message);
            StringAssert.Contains("Image", issues[0].Message);
        }

        [Test]
        public void Frame_MaskBogus_ValueIssue()
        {
            var n = new ElementNode("Frame");
            n.Attributes["mask"] = "circle";
            var issues = MaskAttributeRules.CheckFrame(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.ValueCode, issues[0].Code);
        }

        [Test]
        public void Frame_MaskPaddingWithoutRect_PaddingIssue()
        {
            var n = new ElementNode("Frame");
            n.Attributes["maskPadding"] = "8";
            var issues = MaskAttributeRules.CheckFrame(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.PaddingNoRectCode, issues[0].Code);
        }

        [Test]
        public void Frame_MaskRectWithPadding_NoIssue()
        {
            var n = new ElementNode("Frame");
            n.Attributes["mask"] = "rect";
            n.Attributes["maskPadding"] = "8";
            Assert.IsEmpty(MaskAttributeRules.CheckFrame(n));
        }

        [Test]
        public void Frame_MaskInVariantOverride_VariantIssue()
        {
            var n = new ElementNode("Frame");
            n.VariantOverrides["mask"] =
                new List<(string Variant, string Value)> { ("mobile", "rect") };
            var issues = MaskAttributeRules.CheckFrame(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.VariantCode, issues[0].Code);
        }

        [Test]
        public void Frame_MaskPaddingInVariantOverride_VariantIssue()
        {
            var n = new ElementNode("Frame");
            n.Attributes["mask"] = "rect";
            n.VariantOverrides["maskPadding"] =
                new List<(string Variant, string Value)> { ("mobile", "8") };
            var issues = MaskAttributeRules.CheckFrame(n).ToList();
            // No PaddingNoRect (mask=rect base), but VARIANT
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.VariantCode, issues[0].Code);
        }
    }
}
```

- [ ] **Step 2: Run to verify fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: CS0246 `MaskAttributeRules` not found.

- [ ] **Step 3: Implement `MaskAttributeRules.CheckFrame` (placeholder `CheckImage`)**

Create `Runtime/Core/Lint/MaskAttributeRules.cs`:

```csharp
using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Mask-family lint rules for `<Frame>` and `<Image>`.
    /// Consumed by both <c>IRWalker</c> (UIXmlLint CLI, build-time errors) and
    /// <c>ScreenInstantiator</c> (runtime warnings). Single source of truth.
    /// </summary>
    public static class MaskAttributeRules
    {
        public const string FrameSelfCode      = "PUI-MASK-FRAME-SELF";
        public const string ValueCode          = "PUI-MASK-VALUE";
        public const string PaddingNoRectCode  = "PUI-MASK-PADDING-NO-RECT";
        public const string ShowMaskNoSelfCode = "PUI-MASK-SHOWMASK-NO-SELF";
        public const string VariantCode        = "PUI-MASK-VARIANT";
        public const string SelfNoSpriteCode   = "PUI-MASK-SELF-NO-SPRITE";

        public static IEnumerable<LintIssue> CheckFrame(ElementNode n)
        {
            foreach (var issue in CheckVariantOverrides(n)) yield return issue;

            n.Attributes.TryGetValue("mask", out var mask);
            var hasPadding = n.Attributes.ContainsKey("maskPadding");

            if (!string.IsNullOrEmpty(mask))
            {
                if (mask == "self")
                {
                    yield return new LintIssue(
                        FrameSelfCode, n.Tag, n.Id,
                        $"<Frame id='{n.Id}'>: mask=\"self\" requires an Image graphic on the same GameObject, " +
                        "but Frame has none. Use <Image mask=\"self\"> for stencil masking, " +
                        "or <Frame mask=\"rect\"> for rectangular clipping.");
                }
                else if (mask != "rect")
                {
                    yield return new LintIssue(
                        ValueCode, n.Tag, n.Id,
                        $"<Frame id='{n.Id}'>: mask=\"{mask}\" is invalid. Frame allows only mask=\"rect\".");
                }
            }

            if (hasPadding && mask != "rect")
            {
                yield return new LintIssue(
                    PaddingNoRectCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: maskPadding only takes effect with mask=\"rect\" (RectMask2D); " +
                    "stencil masks have no padding concept. " +
                    "Add mask=\"rect\" or remove maskPadding.");
            }
        }

        public static IEnumerable<LintIssue> CheckImage(ElementNode n)
        {
            yield break; // Task 3 fills this in
        }

        private static IEnumerable<LintIssue> CheckVariantOverrides(ElementNode n)
        {
            if (n.VariantOverrides.ContainsKey("mask")
                || n.VariantOverrides.ContainsKey("showMask")
                || n.VariantOverrides.ContainsKey("maskPadding"))
            {
                yield return new LintIssue(
                    VariantCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: variant overrides on mask / showMask / maskPadding are not supported in v1 " +
                    "(switching mask mode requires AddComponent / Destroy which has performance / lifetime issues). " +
                    "Pick a single mask config; if you need per-variant clipping, split into two Screens or use <Add into=...>.");
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="MaskAttributeRulesTests")
```

Expected: 8 tests, 8 passed (Frame-related; Image tests come in Task 3).

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI
git add Runtime/Core/Lint/MaskAttributeRules.cs Runtime/Core/Lint/MaskAttributeRules.cs.meta \
        Tests/EditMode/Lint/MaskAttributeRulesTests.cs Tests/EditMode/Lint/MaskAttributeRulesTests.cs.meta
git commit -m "$(cat <<'EOF'
feat: MaskAttributeRules.CheckFrame + variant override guard

Single source of truth for <Frame mask=/maskPadding=> lint rules,
shared between IRWalker (CLI errors) and ScreenInstantiator
(runtime warnings). CheckImage stub for Task 3.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: `MaskAttributeRules.CheckImage`

**Files:**
- Modify: `Runtime/Core/Lint/MaskAttributeRules.cs`
- Modify: `Tests/EditMode/Lint/MaskAttributeRulesTests.cs`

- [ ] **Step 1: Add failing Image tests**

Append to `Tests/EditMode/Lint/MaskAttributeRulesTests.cs` (inside class body):

```csharp
        // ===== Image =====

        [Test]
        public void Image_NoMaskAttrs_NoIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["sprite"] = "card-bg";
            Assert.IsEmpty(MaskAttributeRules.CheckImage(n));
        }

        [Test]
        public void Image_MaskRect_NoIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["mask"] = "rect";
            Assert.IsEmpty(MaskAttributeRules.CheckImage(n));
        }

        [Test]
        public void Image_MaskSelf_WithSprite_NoIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["mask"] = "self";
            n.Attributes["sprite"] = "round-card";
            Assert.IsEmpty(MaskAttributeRules.CheckImage(n));
        }

        [Test]
        public void Image_MaskSelf_NoSprite_SelfNoSpriteIssue()
        {
            var n = new ElementNode("Image") { Id = "i" };
            n.Attributes["mask"] = "self";
            var issues = MaskAttributeRules.CheckImage(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.SelfNoSpriteCode, issues[0].Code);
        }

        [Test]
        public void Image_MaskBogus_ValueIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["mask"] = "circle";
            var issues = MaskAttributeRules.CheckImage(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.ValueCode, issues[0].Code);
        }

        [Test]
        public void Image_ShowMaskWithoutSelf_ShowMaskIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["mask"] = "rect";
            n.Attributes["showMask"] = "false";
            var issues = MaskAttributeRules.CheckImage(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.ShowMaskNoSelfCode, issues[0].Code);
        }

        [Test]
        public void Image_ShowMaskWithoutMask_ShowMaskIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["showMask"] = "false";
            var issues = MaskAttributeRules.CheckImage(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.ShowMaskNoSelfCode, issues[0].Code);
        }

        [Test]
        public void Image_MaskPaddingWithoutRect_PaddingIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["mask"] = "self";
            n.Attributes["sprite"] = "round-card";
            n.Attributes["maskPadding"] = "8";
            var issues = MaskAttributeRules.CheckImage(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.PaddingNoRectCode, issues[0].Code);
        }

        [Test]
        public void Image_MaskInVariantOverride_VariantIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["sprite"] = "round";
            n.VariantOverrides["mask"] =
                new List<(string Variant, string Value)> { ("mobile", "self") };
            var issues = MaskAttributeRules.CheckImage(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.VariantCode, issues[0].Code);
        }
```

- [ ] **Step 2: Verify they fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="MaskAttributeRulesTests")
```

Expected: tests starting with `Image_…` fail（CheckImage 还是 stub）.

- [ ] **Step 3: Fill in `CheckImage`**

Replace the `CheckImage` body in `Runtime/Core/Lint/MaskAttributeRules.cs`:

```csharp
        public static IEnumerable<LintIssue> CheckImage(ElementNode n)
        {
            foreach (var issue in CheckVariantOverrides(n)) yield return issue;

            n.Attributes.TryGetValue("mask", out var mask);
            var hasSprite = n.Attributes.ContainsKey("sprite");
            var hasPadding = n.Attributes.ContainsKey("maskPadding");
            var hasShowMask = n.Attributes.ContainsKey("showMask");

            if (!string.IsNullOrEmpty(mask) && mask != "rect" && mask != "self")
            {
                yield return new LintIssue(
                    ValueCode, n.Tag, n.Id,
                    $"<Image id='{n.Id}'>: mask=\"{mask}\" is invalid. Image allows mask=\"rect\" or mask=\"self\".");
            }

            if (mask == "self" && !hasSprite)
            {
                yield return new LintIssue(
                    SelfNoSpriteCode, n.Tag, n.Id,
                    $"<Image id='{n.Id}'>: mask=\"self\" with no sprite= will not clip anything (stencil Mask " +
                    "needs an Image graphic as the mask source). Add sprite=, or use mask=\"rect\" if you want " +
                    "a rectangular clip without a sprite.");
            }

            if (hasPadding && mask != "rect")
            {
                yield return new LintIssue(
                    PaddingNoRectCode, n.Tag, n.Id,
                    $"<Image id='{n.Id}'>: maskPadding only takes effect with mask=\"rect\" (RectMask2D); " +
                    "stencil Mask (mask=\"self\") has no padding concept. " +
                    "Add mask=\"rect\" or remove maskPadding.");
            }

            if (hasShowMask && mask != "self")
            {
                yield return new LintIssue(
                    ShowMaskNoSelfCode, n.Tag, n.Id,
                    $"<Image id='{n.Id}'>: showMask only takes effect with mask=\"self\" (stencil Mask). " +
                    "RectMask2D has no graphic to show/hide. Add mask=\"self\" or remove showMask.");
            }
        }
```

- [ ] **Step 4: Run tests to verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="MaskAttributeRulesTests")
```

Expected: 17 tests, 17 passed (8 Frame + 9 Image).

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI
git add Runtime/Core/Lint/MaskAttributeRules.cs Tests/EditMode/Lint/MaskAttributeRulesTests.cs
git commit -m "$(cat <<'EOF'
feat: MaskAttributeRules.CheckImage (rect/self/showMask/padding/variant)

Catches: invalid mask value, mask=self without sprite, maskPadding
without rect, showMask without self, variant overrides on any mask
attr.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: `IRWalker` tag-dispatch

**Files:**
- Modify: `Runtime/Core/Lint/IRWalker.cs`
- Create: `Tests/EditMode/Lint/IRWalkerMaskTests.cs`

- [ ] **Step 1: Write failing test**

Create `Tests/EditMode/Lint/IRWalkerMaskTests.cs`:

```csharp
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class IRWalkerMaskTests
    {
        [Test]
        public void Walk_DispatchesFrameMaskRulesOnRootAndDescendants()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame id='root' mask='self'>
      <Frame id='inner' mask='circle'/>
    </Frame>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();
            // root: mask=self → FRAME-SELF; inner: mask=circle → VALUE
            Assert.IsTrue(issues.Any(i => i.Code == MaskAttributeRules.FrameSelfCode && i.Id == "root"));
            Assert.IsTrue(issues.Any(i => i.Code == MaskAttributeRules.ValueCode && i.Id == "inner"));
        }

        [Test]
        public void Walk_DispatchesImageMaskRules()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Image id='bad' mask='self'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();
            Assert.IsTrue(issues.Any(i => i.Code == MaskAttributeRules.SelfNoSpriteCode && i.Id == "bad"));
        }

        [Test]
        public void Walk_NonFrameNonImageTags_NoMaskIssue()
        {
            // <VStack mask="rect"> 不该触发 mask rule（mask 只 Frame/Image 暴露）
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <VStack id='v' mask='rect'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).Where(i =>
                i.Code.StartsWith("PUI-MASK-")).ToList();
            Assert.IsEmpty(issues);
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IRWalkerMaskTests")
```

Expected: 2 of 3 tests fail (issues not produced; `NonFrameNonImageTags` may already pass since walker doesn't dispatch yet).

- [ ] **Step 3: Modify `IRWalker.WalkNode`**

Replace `WalkNode` body in `Runtime/Core/Lint/IRWalker.cs`:

```csharp
        private static IEnumerable<LintIssue> WalkNode(ElementNode node)
        {
            // Self-checks (tag-specific). Mask rules are about the node itself,
            // not its parent (unlike LayoutGroupChildRules which is parent-relative).
            if (node.Tag == "Frame")
                foreach (var issue in MaskAttributeRules.CheckFrame(node))
                    yield return issue;
            else if (node.Tag == "Image")
                foreach (var issue in MaskAttributeRules.CheckImage(node))
                    yield return issue;

            var isLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid";
            foreach (var child in node.Children)
            {
                if (isLayoutGroup)
                    foreach (var issue in LayoutGroupChildRules.CheckChild(child))
                        yield return issue;
                foreach (var issue in WalkNode(child))
                    yield return issue;
            }
        }
```

- [ ] **Step 4: Run tests pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IRWalker")
```

Expected: existing `IRWalkerTests` still pass + 3 new `IRWalkerMaskTests` pass.

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI
git add Runtime/Core/Lint/IRWalker.cs Tests/EditMode/Lint/IRWalkerMaskTests.cs Tests/EditMode/Lint/IRWalkerMaskTests.cs.meta
git commit -m "$(cat <<'EOF'
feat: IRWalker dispatches mask rules for <Frame>/<Image>

Self-checks happen at every node visit, before recursing into children
(distinct from LayoutGroupChildRules which is parent-relative).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: `ScreenInstantiator` runtime warning mirror

**Files:**
- Modify: `Runtime/Application/ScreenInstantiator.cs` (around line 168)
- Create: `Tests/EditMode/Application/MaskRuntimeWarningTests.cs`

- [ ] **Step 1: Write failing test**

Create `Tests/EditMode/Application/MaskRuntimeWarningTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Lint;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace PromptUGUI.Tests.EditMode.Application
{
    public class MaskRuntimeWarningTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Frame_MaskSelf_LogsRuntimeWarning()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' mask='self'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);

            LogAssert.Expect(LogType.Warning, new Regex(MaskAttributeRules.FrameSelfCode));
            UI.Open("S");
        }

        [Test]
        public void Image_MaskSelfWithoutSprite_LogsRuntimeWarning()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' mask='self'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);

            LogAssert.Expect(LogType.Warning, new Regex(MaskAttributeRules.SelfNoSpriteCode));
            UI.Open("S");
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="MaskRuntimeWarningTests")
```

Expected: tests fail — `LogAssert.Expect` reports "expected warning not received".

- [ ] **Step 3: Hook up runtime warnings**

In `Runtime/Application/ScreenInstantiator.cs`, find the `if (parentIsLayoutGroup) { ... }` block around line 166-170 (`InstantiateRecursive`). Insert mask checks right after that block, before `var entry = _registry.Resolve(node.Tag);`:

```csharp
            if (parentIsLayoutGroup)
            {
                foreach (var issue in LayoutGroupChildRules.CheckChild(node))
                    Debug.LogWarning(issue.Message);
            }

            // Mask-family self-checks (mirror of IRWalker dispatch; runtime warns)
            if (node.Tag == "Frame")
                foreach (var issue in MaskAttributeRules.CheckFrame(node))
                    Debug.LogWarning(issue.Message);
            else if (node.Tag == "Image")
                foreach (var issue in MaskAttributeRules.CheckImage(node))
                    Debug.LogWarning(issue.Message);

            var entry = _registry.Resolve(node.Tag);
```

If `using PromptUGUI.Lint;` isn't already at the top, add it.

- [ ] **Step 4: Run pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="MaskRuntimeWarningTests")
```

Expected: 2 tests, 2 passed.

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI
git add Runtime/Application/ScreenInstantiator.cs \
        Tests/EditMode/Application/MaskRuntimeWarningTests.cs Tests/EditMode/Application/MaskRuntimeWarningTests.cs.meta
git commit -m "$(cat <<'EOF'
feat: ScreenInstantiator runtime warnings for mask rules

Mirrors IRWalker tag dispatch; uses the same MaskAttributeRules
source so CLI errors and runtime Debug.LogWarning never diverge.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: `<Frame>` mask + maskPadding 实现

**Files:**
- Modify: `Runtime/Controls/Frame.cs`
- Create: `Tests/EditMode/Controls/FrameMaskTests.cs`

- [ ] **Step 1: Write failing tests**

Create `Tests/EditMode/Controls/FrameMaskTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class FrameMaskTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void NoMaskAttr_NoRectMask2D()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var f = s.Get<Frame>("f");
            Assert.IsNull(f.GameObject.GetComponent<RectMask2D>(),
                "Frame without mask attr should not auto-add RectMask2D");
        }

        [Test]
        public void MaskRect_AddsRectMask2D()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' mask='rect'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var f = s.Get<Frame>("f");
            Assert.IsNotNull(f.GameObject.GetComponent<RectMask2D>());
        }

        [Test]
        public void MaskRectWithPadding_AppliesPadding_TRBL_Flipped()
        {
            // Author "1,2,3,4" (T,R,B,L) → Unity Vector4(L,B,R,T) = (4,3,2,1)
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' mask='rect' maskPadding='1,2,3,4'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var f = s.Get<Frame>("f");
            var rm = f.GameObject.GetComponent<RectMask2D>();
            Assert.IsNotNull(rm);
            Assert.AreEqual(new Vector4(4f, 3f, 2f, 1f), rm.padding);
        }

        [Test]
        public void MaskPaddingWithoutMaskRect_NoRectMask2D()
        {
            // PUI-MASK-PADDING-NO-RECT 已 warn,但 runtime 仍要"安全":
            // 只写 maskPadding 没写 mask=rect → 不挂 RectMask2D。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' maskPadding='8'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);

            // Swallow the PUI-MASK-PADDING-NO-RECT warning so the test framework
            // doesn't flag it as an unexpected log.
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(
                    PromptUGUI.Lint.MaskAttributeRules.PaddingNoRectCode));

            var s = UI.Open("S");
            var f = s.Get<Frame>("f");
            Assert.IsNull(f.GameObject.GetComponent<RectMask2D>());
        }
    }
}
```

- [ ] **Step 2: Verify they fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="FrameMaskTests")
```

Expected: `MaskRect_*` and `MaskRectWithPadding_*` fail; `NoMaskAttr` and `MaskPaddingWithoutMaskRect` pass (Frame is empty, no RectMask2D anywhere).

- [ ] **Step 3: Implement Frame mask attributes**

Replace `Runtime/Controls/Frame.cs` content:

```csharp
using PromptUGUI.Layout;
using PromptUGUI.Registry;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    public sealed class Frame : Control
    {
        // 无视觉、纯 RectTransform 容器；可选 RectMask2D（mask="rect"）。
        private RectMask2D _rectMask;
        private string _pendingMaskPadding;

        [UIAttr]
        public string Mask
        {
            set
            {
                if (value == "rect")
                {
                    _rectMask ??= GameObject.AddComponent<RectMask2D>();
                    if (!string.IsNullOrEmpty(_pendingMaskPadding))
                        _rectMask.padding = MaskPaddingParser.Parse(_pendingMaskPadding);
                }
                // 其他值 (空 / self / 无效): lint 已 warn; runtime 静默忽略 (FIM-D9 safety net)
            }
        }

        [UIAttr]
        public string MaskPadding
        {
            set
            {
                _pendingMaskPadding = value;
                if (_rectMask != null)
                    _rectMask.padding = MaskPaddingParser.Parse(value);
            }
        }
    }
}
```

注：`[UIAttr]` 来自 `PromptUGUI.Registry`（跟 `Image.cs` 的 import 一致）。

- [ ] **Step 4: Run tests to pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="FrameMaskTests")
```

Expected: 4 tests, 4 passed.

- [ ] **Step 5: Run full suite to catch regressions**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: full suite green.

- [ ] **Step 6: Lint + commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI
git add Runtime/Controls/Frame.cs Tests/EditMode/Controls/FrameMaskTests.cs Tests/EditMode/Controls/FrameMaskTests.cs.meta
git commit -m "$(cat <<'EOF'
feat: <Frame mask="rect"> + maskPadding

Adds RectMask2D on demand; maskPadding parses author-friendly T,R,B,L
into Unity's native Vector4(L,B,R,T). Setter ordering is irrelevant
thanks to _pendingMaskPadding buffering.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: `<Image mask="rect">` + maskPadding

**Files:**
- Modify: `Runtime/Controls/Image.cs`
- Create: `Tests/EditMode/Controls/ImageMaskTests.cs`

- [ ] **Step 1: Write failing tests**

Create `Tests/EditMode/Controls/ImageMaskTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;
using PromptUGUIImage = PromptUGUI.Controls.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ImageMaskTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void NoMaskAttr_NoMaskComponents()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' sprite='pugui'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var img = s.Get<PromptUGUIImage>("i");
            Assert.IsNull(img.GameObject.GetComponent<RectMask2D>());
            Assert.IsNull(img.GameObject.GetComponent<Mask>());
        }

        [Test]
        public void MaskRect_AddsRectMask2D_NotStencilMask()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' mask='rect'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var img = s.Get<PromptUGUIImage>("i");
            Assert.IsNotNull(img.GameObject.GetComponent<RectMask2D>());
            Assert.IsNull(img.GameObject.GetComponent<Mask>());
        }

        [Test]
        public void MaskRectWithPadding_AppliesPadding()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' mask='rect' maskPadding='1,2,3,4'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var img = s.Get<PromptUGUIImage>("i");
            var rm = img.GameObject.GetComponent<RectMask2D>();
            Assert.IsNotNull(rm);
            Assert.AreEqual(new Vector4(4f, 3f, 2f, 1f), rm.padding);
        }
    }
}
```

- [ ] **Step 2: Verify they fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageMaskTests")
```

Expected: `MaskRect_*` and `MaskRectWithPadding_*` fail (Image has no Mask setters yet).

- [ ] **Step 3: Add `Mask` (rect path only) + `MaskPadding` to Image**

Add to `Runtime/Controls/Image.cs` (above the existing `OnAfterApply`):

```csharp
        private RectMask2D _rectMask;
        private UnityEngine.UI.Mask _stencilMask;     // populated by Task 8
        private string _pendingMaskPadding;
        private bool? _pendingShowMask;               // populated by Task 8

        [UIAttr]
        public string Mask
        {
            set
            {
                if (value == "rect")
                {
                    _rectMask ??= GameObject.AddComponent<RectMask2D>();
                    if (!string.IsNullOrEmpty(_pendingMaskPadding))
                        _rectMask.padding = PromptUGUI.Layout.MaskPaddingParser.Parse(_pendingMaskPadding);
                }
                // mask="self" path implemented in Task 8
            }
        }

        [UIAttr]
        public string MaskPadding
        {
            set
            {
                _pendingMaskPadding = value;
                if (_rectMask != null)
                    _rectMask.padding = PromptUGUI.Layout.MaskPaddingParser.Parse(value);
            }
        }
```

Update `using` block at top of `Image.cs` if `using UnityEngine.UI;` isn't already present (it imports `UnityImage` via alias; `RectMask2D` lives in same namespace).

注：保留 `_stencilMask` / `_pendingShowMask` 字段（Task 8 用）防止 Task 8 编辑同一处时再 init order 摔。

- [ ] **Step 4: Verify Image mask=rect tests pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageMaskTests")
```

Expected: 3 tests, 3 passed.

- [ ] **Step 5: Lint + commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI
git add Runtime/Controls/Image.cs Tests/EditMode/Controls/ImageMaskTests.cs Tests/EditMode/Controls/ImageMaskTests.cs.meta
git commit -m "$(cat <<'EOF'
feat: <Image mask="rect"> + maskPadding (stencil in next commit)

Adds RectMask2D path; stencil Mask (mask="self") + showMask come
in the next commit.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: `<Image mask="self">` + showMask

**Files:**
- Modify: `Runtime/Controls/Image.cs`
- Modify: `Tests/EditMode/Controls/ImageMaskTests.cs`

- [ ] **Step 1: Add failing tests**

Append to `Tests/EditMode/Controls/ImageMaskTests.cs` (inside class):

```csharp
        [Test]
        public void MaskSelf_WithSprite_AddsStencilMask_NotRectMask()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' sprite='pugui#pugui_9slice_round' mask='self'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var img = s.Get<PromptUGUIImage>("i");
            Assert.IsNotNull(img.GameObject.GetComponent<Mask>());
            Assert.IsNull(img.GameObject.GetComponent<RectMask2D>());
        }

        [Test]
        public void MaskSelf_DefaultShowMaskGraphicTrue()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' sprite='pugui#pugui_9slice_round' mask='self'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var img = s.Get<PromptUGUIImage>("i");
            var m = img.GameObject.GetComponent<Mask>();
            Assert.IsTrue(m.showMaskGraphic, "default showMask=true (FIM-D5)");
        }

        [Test]
        public void MaskSelf_ShowMaskFalse_HidesGraphic()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' sprite='pugui#pugui_9slice_round' mask='self' showMask='false'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var img = s.Get<PromptUGUIImage>("i");
            var m = img.GameObject.GetComponent<Mask>();
            Assert.IsFalse(m.showMaskGraphic);
        }

        [Test]
        public void NestedMaskShape_OuterAndInnerImageBoth()
        {
            // §4 用例 5: 外层装饰 + 内层 stencil mask
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='outer' sprite='pugui#pugui_9slice_round'>
    <Image id='inner' sprite='pugui#pugui_9slice_mask' mask='self' showMask='false'
           anchor='stretch' margin='8'/>
  </Image>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var outer = s.Get<PromptUGUIImage>("outer");
            var inner = s.Get<PromptUGUIImage>("inner");
            // Outer: no mask
            Assert.IsNull(outer.GameObject.GetComponent<Mask>());
            Assert.IsNull(outer.GameObject.GetComponent<RectMask2D>());
            // Inner: stencil mask, graphic hidden
            var innerMask = inner.GameObject.GetComponent<Mask>();
            Assert.IsNotNull(innerMask);
            Assert.IsFalse(innerMask.showMaskGraphic);
        }
    }
}
```

注：`pugui#pugui_9slice_round` 是 `Runtime/Resources/PromptUGUI/Defaults/pugui.png` 切片，跟 ScrollList/Dropdown 现有测试一致；`UI.ResolveSprite` 走 bare-path + slice 通道，不需要 SpriteResolver。

- [ ] **Step 2: Verify they fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageMaskTests")
```

Expected: 4 new tests fail (mask="self" path 还没实现).

- [ ] **Step 3: Fill in self path + ShowMask setter**

In `Runtime/Controls/Image.cs`, update the `Mask` setter and add `ShowMask`:

```csharp
        [UIAttr]
        public string Mask
        {
            set
            {
                if (value == "rect")
                {
                    _rectMask ??= GameObject.AddComponent<RectMask2D>();
                    if (!string.IsNullOrEmpty(_pendingMaskPadding))
                        _rectMask.padding = PromptUGUI.Layout.MaskPaddingParser.Parse(_pendingMaskPadding);
                }
                else if (value == "self")
                {
                    _stencilMask ??= GameObject.AddComponent<UnityEngine.UI.Mask>();
                    if (_pendingShowMask.HasValue)
                        _stencilMask.showMaskGraphic = _pendingShowMask.Value;
                }
            }
        }

        [UIAttr]
        public string ShowMask
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                _pendingShowMask = bool.Parse(value);
                if (_stencilMask != null)
                    _stencilMask.showMaskGraphic = _pendingShowMask.Value;
            }
        }
```

- [ ] **Step 4: Verify all Image mask tests pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImageMaskTests")
```

Expected: 7 tests, 7 passed (3 from Task 7 + 4 new).

- [ ] **Step 5: Run full suite**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: full suite green; specifically `ScrollListTests` / `DropdownTests` (which already use stencil Mask internally) still pass.

- [ ] **Step 6: Lint + commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI
git add Runtime/Controls/Image.cs Tests/EditMode/Controls/ImageMaskTests.cs
git commit -m "$(cat <<'EOF'
feat: <Image mask="self"> + showMask (stencil Mask)

Image now supports stencil Mask via mask="self"; showMask controls
Mask.showMaskGraphic (default true). _pendingShowMask buffers setter
order so MaskPadding / ShowMask / Mask can land in any order.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: UIXmlLint end-to-end fixture

**Files:**
- Create: `Tests/EditMode/Lint/Fixtures/mask-rules-bad.ui.xml`
- Create: `Tests/EditMode/Lint/UIXmlLintMaskTests.cs`

注：`Fixtures/` 子目录可能还不存在。Lint 测试目录已有，但 fixture 文件通常通过字符串直接传给 parser，不一定需要外部 `.ui.xml`。这里我们用一个内嵌 xml 字符串走 `IRWalker.Walk` 直接，但同时检查 `.ui.xml` 文件在仓里也能被 `UIXmlLint` CLI 捕获 —— 用 `dotnet run` 跑一次。

- [ ] **Step 1: Write failing integration test**

Create `Tests/EditMode/Lint/UIXmlLintMaskTests.cs`:

```csharp
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class UIXmlLintMaskTests
    {
        // 一份 xml 同时触发 6 条 mask 规则 — 验证 IRWalker 全部能产出。
        [Test]
        public void EndToEnd_AllSixMaskRulesFire()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame id='f-self' mask='self'/>
    <Frame id='f-bogus' mask='circle'/>
    <Frame id='f-pad-no-rect' maskPadding='8'/>
    <Image id='i-self-no-sprite' mask='self'/>
    <Image id='i-show-no-self' mask='rect' showMask='false'/>
    <Image id='i-variant' sprite='x' mask.mobile='self'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();

            string[] expected =
            {
                MaskAttributeRules.FrameSelfCode,
                MaskAttributeRules.ValueCode,
                MaskAttributeRules.PaddingNoRectCode,
                MaskAttributeRules.SelfNoSpriteCode,
                MaskAttributeRules.ShowMaskNoSelfCode,
                MaskAttributeRules.VariantCode,
            };
            foreach (var code in expected)
                Assert.IsTrue(issues.Any(i => i.Code == code),
                    $"expected at least one issue with code {code}; got: {string.Join(", ", issues.Select(i => i.Code))}");
        }
    }
}
```

- [ ] **Step 2: Verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="UIXmlLintMaskTests")
```

Expected: 1 test, 1 passed（前面 Task 1-5 已经实现所有底层逻辑，这里只是 end-to-end 确认）。

- [ ] **Step 3: Also verify the CLI exits non-zero on the same input**

Save the same XML to a temp file and run the CLI:

```bash
mkdir -p /tmp/pugui-lint-fixture
cat > /tmp/pugui-lint-fixture/bad.ui.xml <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="S">
    <Frame id="f-self" mask="self"/>
    <Frame id="f-bogus" mask="circle"/>
    <Frame id="f-pad-no-rect" maskPadding="8"/>
    <Image id="i-self-no-sprite" mask="self"/>
    <Image id="i-show-no-self" mask="rect" showMask="false"/>
    <Image id="i-variant" sprite="x" mask.mobile="self"/>
  </Screen>
</PromptUGUI>
EOF
cd /workspace-PromptUGUI/.lint
dotnet run --project UIXmlLint -- /tmp/pugui-lint-fixture/bad.ui.xml
echo "exit=$?"
```

Expected output: 6 lines of `[PUI-MASK-*]` errors; final `UIXmlLint: 6 issue(s) across 1 file(s).`; `exit=1`.

If exit is 0, IRWalker dispatch is broken — revisit Task 4.

```bash
rm -rf /tmp/pugui-lint-fixture
```

- [ ] **Step 4: Verify no regression on existing fixtures**

```bash
cd /workspace-PromptUGUI/.lint
dotnet run --project UIXmlLint -- ../Runtime/Resources/
```

Expected: `UIXmlLint: no issues across N file(s).` (no false positives on shipping `.ui.xml` files).

- [ ] **Step 5: Lint + commit**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI
git add Tests/EditMode/Lint/UIXmlLintMaskTests.cs Tests/EditMode/Lint/UIXmlLintMaskTests.cs.meta
git commit -m "$(cat <<'EOF'
test: UIXmlLint end-to-end mask rules coverage

Single XML exercises all 6 mask rule codes; complements per-rule
MaskAttributeRulesTests with whole-pipeline coverage.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: SKILL + master spec docs

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`

- [ ] **Step 1: Edit XML SKILL — `<Frame>` row**

In `.claude/skills/authoring-promptugui-xml/SKILL.md`, find the built-in primitives table row:

```
| `<Frame>`      | Empty container (RectTransform only).                                                                                                                                                                                                                        | —                                                                                                                                                                                                                                                                                                                   |
```

Replace with:

```
| `<Frame>`      | Empty container; optional `RectMask2D` via `mask="rect"`.                                                                                                                                                                                                    | `mask` (`rect`), `maskPadding` (`T,R,B,L`, "_" placeholder; only with `mask="rect"`)                                                                                                                                                                                                                                |
```

- [ ] **Step 2: Edit XML SKILL — `<Image>` row**

Find:

```
| `<Image>`      | uGUI Image; loads sprites from `Resources`.                                                                                                                                                                                                                  | `sprite` (resource path), `color` (`#RRGGBB[AA]`), `type` (`simple` / `sliced` / `tiled` / `filled`; **omit to auto-pick `sliced` when sprite has a non-zero border, else `simple`** — explicit value always wins)                                                                                                  |
```

Replace with:

```
| `<Image>`      | uGUI Image; loads sprites from `Resources`. Optional `RectMask2D` via `mask="rect"`, or stencil `Mask` via `mask="self"` (Image's own sprite becomes the mask shape).                                                                                        | `sprite`, `color` (`#RRGGBB[AA]`), `type` (`simple` / `sliced` / `tiled` / `filled`; **omit to auto-pick `sliced` when sprite has a non-zero border, else `simple`**), `mask` (`rect` / `self`), `showMask` (bool, default `true`; only with `mask="self"`), `maskPadding` (`T,R,B,L`; only with `mask="rect"`)     |
```

- [ ] **Step 3: Insert "Mask & clipping" section**

Find the existing `<Btn>` 内容自适应 section（or whichever section comes after the primitives table — look for a `##` heading near `<Btn>` 默认按文字自适应）. Insert a new section just before "Common pitfalls" (or wherever feels structurally adjacent — search for "## " near the bottom):

```markdown
## Mask & clipping

PromptUGUI never auto-enables masking — you must opt in via `mask=`. Two reasons: (1) stencil Mask isn't free (extra SetPass call, breaks batching with elements outside the mask); (2) "decorative background that lets children overflow" is a legit, common pattern.

| Want | Recipe | Component used |
|---|---|---|
| Pure container, no clip | `<Frame/>` (current default) | none |
| Cheap rectangular clip (viewport-style) | `<Frame mask="rect"/>` or `<Image mask="rect" sprite="..."/>` | `RectMask2D` |
| Sprite-shape clip + sprite drawn (rounded card) | `<Image sprite="round" mask="self"/>` | stencil `Mask`, `showMaskGraphic=true` |
| Sprite-shape clip + sprite hidden (viewport with shaped mask) | `<Image sprite="round-mask" mask="self" showMask="false"/>` | stencil `Mask`, `showMaskGraphic=false` |
| Decorated outer frame + different inner clip shape | Nest two `<Image>` — outer has `sprite=` only; inner has `mask="self" sprite=` (different shape) + `margin=` to control inner size | none on outer, stencil on inner |

**`<Frame>` never supports `mask="self"`** — Frame has no Image graphic to use as the mask source. Use `<Image mask="self">` for sprite-shape clipping.

**`maskPadding`** is `RectMask2D`-only and uses the same `T,R,B,L` (1/2/4 components, `"_"` placeholder) convention as `padding`. Negative values are allowed (matches Unity's `RectMask2D.padding` semantics; used internally by `<InputField>` for text-area insets).

**`showMask`** is stencil-Mask-only; `true` (default) keeps the Image visible, `false` hides it (the sprite still defines the clip shape but isn't drawn).

**Variant overrides** on `mask` / `showMask` / `maskPadding` are rejected in v1 (`PUI-MASK-VARIANT`) — switching mask mode means `AddComponent`/`Destroy` at runtime, which we don't support. If you need per-variant clipping, split into two Screens or use `<Add into=…>`.

Lint codes you might see (CLI errors / Unity warnings):

| Code | Meaning |
|---|---|
| `PUI-MASK-FRAME-SELF` | `<Frame mask="self">` is invalid (no graphic) |
| `PUI-MASK-VALUE` | `mask=` value not in `rect` / `self` (Image) or `rect` (Frame) |
| `PUI-MASK-PADDING-NO-RECT` | `maskPadding` only applies to `mask="rect"` |
| `PUI-MASK-SHOWMASK-NO-SELF` | `showMask` only applies to `mask="self"` |
| `PUI-MASK-VARIANT` | `mask` / `showMask` / `maskPadding` cannot be overridden in variants |
| `PUI-MASK-SELF-NO-SPRITE` | `<Image mask="self">` with no `sprite=` won't clip anything |
```

- [ ] **Step 4: Edit master spec (`§5` table + §5.3 bullet)**

In `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`, find the `<Frame>` table row:

```
| `<Frame>` | 纯定位容器，无视觉 | 空 RectTransform |
```

Replace with:

```
| `<Frame>` | 纯定位容器，无视觉；可选 `mask="rect"` 启用 RectMask2D | RectTransform（+ 可选 RectMask2D） |
```

Find the §5.3 Image bullet:

```
- `<Image sprite="bg/main" color="#FFFFFFAA" type="sliced|simple|filled|tiled"/>`
```

Append (after that line):

```
- `<Image mask="rect|self" showMask="true|false" maskPadding="T,R,B,L"/>` — 见 [`2026-05-16-frame-image-mask-design.md`](2026-05-16-frame-image-mask-design.md)
- `<Frame mask="rect" maskPadding="T,R,B,L"/>` — 同上
```

- [ ] **Step 5: Sanity-check SKILL.md (XSD path still applies; CLI command unchanged)**

Read the top "Validation & feedback loop" section once to make sure nothing in that flow references attribute lists that need updating. (It doesn't.)

- [ ] **Step 6: Commit**

```bash
cd /workspace-PromptUGUI
git add .claude/skills/authoring-promptugui-xml/SKILL.md \
        docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md \
        docs~/superpowers/specs/2026-05-16-frame-image-mask-design.md \
        docs~/superpowers/specs/2026-05-16-frame-image-mask-design.md.meta \
        docs~/superpowers/plans/2026-05-16-frame-image-mask.md \
        docs~/superpowers/plans/2026-05-16-frame-image-mask.md.meta
git commit -m "$(cat <<'EOF'
docs: <Frame>/<Image> mask attrs in SKILL + master spec

Adds "Mask & clipping" section to authoring-promptugui-xml SKILL,
updates primitives table for Frame/Image, links new design spec
from §5 / §5.3 in the master spec.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

注：`docs~/` 不被 Unity 当 asset 处理（`~` 后缀），所以没 `.meta` 文件需 add；只 commit `.md`。`.claude/skills/` 同样不是 Unity asset，不生成 `.meta`。

---

## Self-Review checklist

- [ ] §3 属性表的每一条 attr 都在 Task 6/7/8 有实现 + 测试覆盖
- [ ] §5 lint 表的每一条规则在 Task 2/3 有单元测试，且在 Task 9 end-to-end fixture 中触发
- [ ] FIM-D7 (T,R,B,L → L,B,R,T 翻转) 在 Task 1 测试 + Task 6/7 集成测试都覆盖
- [ ] FIM-D14 (setter 顺序无关) 隐式覆盖 — `_pendingMaskPadding` 在 Frame 测试 Task 6 Step 3 / Image Task 7 Step 3 都包含
- [ ] FIM-D8 (variant override → lint error) 在 Task 2/3 单元测试 + Task 9 end-to-end 覆盖
- [ ] Runtime warning mirror 在 Task 5 测试
- [ ] SKILL.md 更新含全部新属性 + 新规则代码（Task 10 Step 3）
- [ ] 主 spec §5 表 + §5.3 注脚（Task 10 Step 4）
- [ ] 没有 placeholders / TBD / "similar to Task N"

---

## Execution handoff

Plan complete. Two execution options:

1. **Subagent-Driven (recommended)** — dispatch fresh subagent per task with two-stage review between tasks
2. **Inline Execution** — execute tasks in this session with checkpoint review

Which approach?
