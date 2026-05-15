# Btn 内容自适应 + 自由定位 native fallback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 `<Btn>OK</Btn>` 不写 size 时自动按文字宽 + tap-target 高度自适应；并把"自由定位下无 size 时回退到 native"作为通用 Control 规则。

**Architecture:** Btn 只覆写 `GetNativeSize()`；LayoutElement 的挂管仍归 `Control.ApplyLayoutElement`（扩展为"无 size 但有 native → 挂 LE 报告 native"）；`Control.ApplyCommon` 自由定位分支新增 native fallback。`SizeSpec` 加一个内部 `FromNumeric` 工厂方法服务 fallback 注入。

**Tech Stack:** Unity 6+ uGUI, TMPro, R3 (only in existing Btn code), NUnit (EditMode tests via UnityMCP).

**依赖 spec:** [`2026-05-15-btn-content-sizing-design.md`](../specs/2026-05-15-btn-content-sizing-design.md)

**测试入口:** 全部 EditMode 测试，通过 UnityMCP 跑：
```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="...")
mcp__UnityMCP__read_console(action="get", types=["error"])
```

---

## Task 1: `SizeSpec.FromNumeric` 工厂方法

**Files:**
- Modify: `Runtime/Core/Layout/SizeSpec.cs`
- Test: `Tests/EditMode/Layout/SizeSpecFromNumericTests.cs` (Create)

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Layout/SizeSpecFromNumericTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Layout;

namespace PromptUGUI.Tests.EditMode.Layout
{
    public class SizeSpecFromNumericTests
    {
        [Test]
        public void FromNumeric_sets_both_axes_as_explicit_numeric()
        {
            var s = SizeSpec.FromNumeric(120f, 44f);
            Assert.IsTrue(s.HasWidth);
            Assert.IsTrue(s.HasHeight);
            Assert.AreEqual(120f, s.Width);
            Assert.AreEqual(44f, s.Height);
            Assert.IsFalse(s.IsNativeWidth);
            Assert.IsFalse(s.IsNativeHeight);
            Assert.IsFalse(s.IsFlexibleWidth);
            Assert.IsFalse(s.IsFlexibleHeight);
            Assert.IsFalse(s.IsFractionalWidth);
            Assert.IsFalse(s.IsFractionalHeight);
        }
    }
}
```

- [ ] **Step 2: Verify test fails (compile error: FromNumeric not defined)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: CS0117 `'SizeSpec' does not contain a definition for 'FromNumeric'`.

- [ ] **Step 3: Add `FromNumeric` to `SizeSpec`**

In `Runtime/Core/Layout/SizeSpec.cs`, after the `WithNativeResolved` method (around line 108):

```csharp
internal static SizeSpec FromNumeric(float w, float h) =>
    new(w, h, true, true, false, false, false, false, 1f, 1f, false, false, 0f, 0f);
```

注意：构造器 `SizeSpec(...)` 是 private，所以只能从同一文件加 factory。`internal` 让 PromptUGUI.Runtime 内部能用（`Control.cs` 在同 asmdef）。

`InternalsVisibleTo` 已经把 `PromptUGUI.Tests.EditMode` 接进来（见 `Runtime/AssemblyInfo.cs`），所以测试也能访问。

- [ ] **Step 4: Run test to verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SizeSpecFromNumericTests")
```

Expected: 1 test, 1 passed.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Layout/SizeSpec.cs Tests/EditMode/Layout/SizeSpecFromNumericTests.cs Tests/EditMode/Layout/SizeSpecFromNumericTests.cs.meta
git commit -m "$(cat <<'EOF'
feat: SizeSpec.FromNumeric internal factory

For BCS-D7 native fallback injection in ApplyCommon free-positioning branch.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

注：`.meta` 文件由 Unity 自动生成；提交前用 `git status` 看一下，如果是 untracked 就一并 add。

---

## Task 2: `Btn.GetNativeSize` 覆写

**Files:**
- Modify: `Runtime/Controls/Btn.cs`
- Test: `Tests/EditMode/Controls/BtnContentSizingTests.cs` (Create)

- [ ] **Step 1: Write the failing test (icon-only fallback path)**

Create `Tests/EditMode/Controls/BtnContentSizingTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class BtnContentSizingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Btn_without_text_GetNativeSize_returns_icon_only_defaults()
        {
            // 无 text → 回退到 (80, 44)
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var native = btn.GetNativeSize();
            Assert.IsTrue(native.HasValue, "Btn must report a native size (icon-only fallback)");
            Assert.AreEqual(80f, native.Value.x);
            Assert.AreEqual(44f, native.Value.y);
        }

        [Test]
        public void Btn_with_text_GetNativeSize_reports_label_preferred_plus_padding()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b'>OK</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var label = btn.GameObject.GetComponentInChildren<TMP_Text>();
            label.ForceMeshUpdate();
            var textW = label.preferredWidth;

            var native = btn.GetNativeSize();
            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(textW + 32f, native.Value.x, 0.5f,
                "preferredWidth = label.preferredWidth + 16*2 padding");
            // 高度：max(44, label.preferredHeight + 12) — "OK" 单行高度通常远小于 44，所以应该=44
            Assert.AreEqual(44f, native.Value.y, 0.5f,
                "preferredHeight = max(44, label.preferredHeight + 6*2)");
        }
    }
}
```

- [ ] **Step 2: Verify both tests fail (Btn.GetNativeSize returns null)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="BtnContentSizingTests")
```

Expected: both tests FAIL — `Btn_without_text_GetNativeSize_returns_icon_only_defaults` 报 `HasValue==false`；`Btn_with_text_GetNativeSize_reports_label_preferred_plus_padding` 同样。

- [ ] **Step 3: Implement `GetNativeSize` on `Btn`**

In `Runtime/Controls/Btn.cs`, add at the top of the class body (after the `_pointerRelay` field), new constants:

```csharp
private const float HorizontalPadding = 16f;
private const float VerticalPadding = 6f;
private const float MinTapHeight = 44f;
private const float DefaultIconBtnWidth = 80f;
```

Then add the override (anywhere in the class — placing it after `OnAttached` is fine):

```csharp
public override Vector2? GetNativeSize()
{
    if (_autoLabel != null && !string.IsNullOrEmpty(_autoLabel.text))
    {
        _autoLabel.ForceMeshUpdate();
        var w = _autoLabel.preferredWidth + HorizontalPadding * 2f;
        var h = Mathf.Max(MinTapHeight, _autoLabel.preferredHeight + VerticalPadding * 2f);
        return new Vector2(w, h);
    }
    return new Vector2(DefaultIconBtnWidth, MinTapHeight);
}
```

- [ ] **Step 4: Run tests to verify pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="BtnContentSizingTests")
```

Expected: 2 passed.

Note: 这一步只测了 `GetNativeSize()` 直接返回值。Btn 还没在 layout 流程里发挥作用 —— `ControlApplyCommonLayoutGroupTests.Btn_in_VStack_with_no_size_attrs_gets_no_LayoutElement` 现在仍然 PASS（ApplyLayoutElement 还没改），但 Task 3 改完后它会 FAIL，那个测试本身要在 Task 3 一起改。

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Btn.cs Tests/EditMode/Controls/BtnContentSizingTests.cs Tests/EditMode/Controls/BtnContentSizingTests.cs.meta
git commit -m "$(cat <<'EOF'
feat: Btn.GetNativeSize override — label.preferred + padding, or icon-only fallback

BCS-D2/D3/D4: preferredW = label.preferredWidth + 32 (左右 16 padding); preferredH = max(44, label.preferredHeight + 12); 无 text 回退 (80, 44). ForceMeshUpdate 强制 TMP 同步避免首帧抖动。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: `ApplyLayoutElement` 扩展：无 size 时若有 native 则挂 LE

**Files:**
- Modify: `Runtime/Controls/Control.cs:217-234` (ApplyLayoutElement 的 "无 size" 早 return 分支)
- Modify: `Tests/EditMode/Controls/ControlApplyCommonLayoutGroupTests.cs:56-70`（更新 `Btn_in_VStack_with_no_size_attrs_gets_no_LayoutElement`）
- Test: `Tests/EditMode/Controls/BtnContentSizingTests.cs`（增 1 个测试）

- [ ] **Step 1: Write the failing test (HStack 路径)**

Append to `BtnContentSizingTests.cs`:

```csharp
[Test]
public void Btn_in_HStack_no_size_gets_LayoutElement_with_native_preferred()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='400' height='44'>
    <Btn id='b'>OK</Btn>
  </HStack>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var screen = UI.Open("S");
    var btn = screen.Get<Btn>("b");
    var le = btn.GameObject.GetComponent<LayoutElement>();
    Assert.IsNotNull(le, "BCS-D6: Btn under LayoutGroup with no size should auto-attach LE reporting GetNativeSize");
    var native = btn.GetNativeSize().Value;
    Assert.AreEqual(native.x, le.preferredWidth, 0.5f);
    Assert.AreEqual(native.y, le.preferredHeight, 0.5f);
    Assert.AreEqual(-1f, le.flexibleWidth);
    Assert.AreEqual(-1f, le.flexibleHeight);
}
```

- [ ] **Step 2: Verify it fails**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="Btn_in_HStack_no_size_gets_LayoutElement_with_native_preferred")
```

Expected: FAIL — `le` is null (current behavior: ApplyLayoutElement early returns without attaching when no size).

- [ ] **Step 3: Update `ApplyLayoutElement` in `Control.cs`**

Replace lines 222-234 (the `if (!sizeSpec.HasWidth && !sizeSpec.HasHeight)` block) with:

```csharp
            // 决策 LGC-D8 + BCS-D6:
            // 作者没写任何 size 属性 → 如果控件能报告 native (GetNativeSize 非 null)，
            // 挂 LayoutElement 把 native 当 preferred 暴露给 LayoutGroup;
            // 否则维持原"无约束 / 清空到 -1"行为，让 Image/TMP 自带 ILayoutElement 主导。
            if (!sizeSpec.HasWidth && !sizeSpec.HasHeight)
            {
                var existing = GameObject.GetComponent<UnityEngine.UI.LayoutElement>();
                var native = GetNativeSize();
                if (native.HasValue)
                {
                    existing ??= GameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    existing.preferredWidth = native.Value.x;
                    existing.preferredHeight = native.Value.y;
                    existing.flexibleWidth = -1;
                    existing.flexibleHeight = -1;
                }
                else if (existing != null)
                {
                    // 前一次 Variant 可能挂过 LayoutElement，本次没尺寸 + 控件没有 native → 还原成"无约束"
                    existing.preferredWidth = -1;
                    existing.preferredHeight = -1;
                    existing.flexibleWidth = -1;
                    existing.flexibleHeight = -1;
                }
                return;
            }
```

- [ ] **Step 4: Run new test — should pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="Btn_in_HStack_no_size_gets_LayoutElement_with_native_preferred")
```

Expected: PASS.

- [ ] **Step 5: Run full EditMode suite — `Btn_in_VStack_with_no_size_attrs_gets_no_LayoutElement` should now FAIL**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ControlApplyCommonLayoutGroupTests")
```

Expected: `Btn_in_VStack_with_no_size_attrs_gets_no_LayoutElement` FAIL，其他 PASS。

- [ ] **Step 6: Update the old test contract**

In `Tests/EditMode/Controls/ControlApplyCommonLayoutGroupTests.cs`, replace lines 56-70 (the entire `Btn_in_VStack_with_no_size_attrs_gets_no_LayoutElement` test) with:

```csharp
        [Test]
        public void Btn_in_VStack_with_no_size_attrs_gets_LayoutElement_from_native()
        {
            // BCS-D6: Btn 提供 GetNativeSize → ApplyLayoutElement 在无 size 时挂 LE 报告 native preferred.
            // 这覆写了 LGC-D8 在 GetNativeSize 不为 null 控件上的行为；GetNativeSize=null 的控件仍走原 LGC-D8.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='200' height='200'>
    <Btn id='b'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var le = btn.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "BCS-D6: GetNativeSize 非 null 的控件无 size → 自动挂 LE 报告 native");
            // Btn 无 text → fallback (80, 44)
            Assert.AreEqual(80f, le.preferredWidth);
            Assert.AreEqual(44f, le.preferredHeight);
        }

        [Test]
        public void Image_in_VStack_with_no_size_attrs_gets_no_LayoutElement()
        {
            // LGC-D8 原契约：GetNativeSize=null 的控件（如 Image）无 size → 不挂 LE，让 UnityImage 自带的 ILayoutElement 主导。
            // 这条契约保留下来；Btn 测试拆出去用了 BCS-D6 新分支。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='200' height='200'>
    <Image id='i'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var img = screen.Get<Image>("i");
            Assert.IsNull(img.GameObject.GetComponent<LayoutElement>(),
                "Image.GetNativeSize() == null → 不挂 LE，让 UnityImage 自带 ILayoutElement 主导");
        }
```

- [ ] **Step 7: Run ControlApplyCommonLayoutGroupTests — all green**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ControlApplyCommonLayoutGroupTests")
```

Expected: all PASS（新增的 `Image_in_VStack_with_no_size_attrs_gets_no_LayoutElement` 同步过）。

- [ ] **Step 8: Commit**

```bash
git add Runtime/Controls/Control.cs Tests/EditMode/Controls/ControlApplyCommonLayoutGroupTests.cs Tests/EditMode/Controls/BtnContentSizingTests.cs
git commit -m "$(cat <<'EOF'
feat: ApplyLayoutElement reports GetNativeSize as preferred when no size attr

BCS-D6: Control 提供 GetNativeSize → ApplyLayoutElement 在 sizeSpec 无 size 时挂 LE 并写 native preferred；GetNativeSize=null 的控件维持 LGC-D8 原"不挂 LE / 清空到 -1"语义。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: `ApplyCommon` 自由定位 fallback

**Files:**
- Modify: `Runtime/Controls/Control.cs:155-211`（else 分支，AnchorResolver.Resolve 调用之前）
- Test: `Tests/EditMode/Controls/BtnContentSizingTests.cs`（增 2 个测试）

- [ ] **Step 1: Write failing tests (Frame 路径 + anchor=stretch 不影响)**

Append to `BtnContentSizingTests.cs`:

```csharp
[Test]
public void Btn_in_Frame_no_size_sizeDelta_matches_native()
{
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Btn id='b'>Cancel</Btn>
  </Frame>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var screen = UI.Open("S");
    var btn = screen.Get<Btn>("b");
    var native = btn.GetNativeSize().Value;
    Assert.AreEqual(native.x, btn.RectTransform.sizeDelta.x, 0.5f,
        "BCS-D7: free-positioning + no size + has native → sizeDelta = native");
    Assert.AreEqual(native.y, btn.RectTransform.sizeDelta.y, 0.5f);
    Assert.AreEqual(44f, btn.RectTransform.sizeDelta.y, 0.5f);
}

[Test]
public void Btn_in_Frame_anchor_stretch_skips_native_fallback()
{
    // anchor='stretch' + margin: 走 MarginResolver stretch 分支, sizeDelta = -(l+r), -(t+b)
    // native fallback 不应介入(BCS-D7 要求两轴都不 stretch)
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Btn id='b' anchor='stretch' margin='8'>OK</Btn>
  </Frame>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var screen = UI.Open("S");
    var btn = screen.Get<Btn>("b");
    // stretch 两轴, margin=8 → sizeDelta = (-16, -16) (相对父容器内缩)
    Assert.AreEqual(-16f, btn.RectTransform.sizeDelta.x, 0.5f,
        "anchor=stretch + margin=8: sizeDelta.x = -(l+r) = -16, 不被 native fallback 覆盖");
    Assert.AreEqual(-16f, btn.RectTransform.sizeDelta.y, 0.5f);
}
```

- [ ] **Step 2: Verify they fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="BtnContentSizingTests")
```

Expected:
- `Btn_in_Frame_no_size_sizeDelta_matches_native`: FAIL (sizeDelta.x == 0, MarginResolver 没 size 时给 0)
- `Btn_in_Frame_anchor_stretch_skips_native_fallback`: 这条**可能现在就 PASS**（anchor=stretch 已经走 MarginResolver stretch 分支，根本不进 fallback）。先确认其状态，再继续。如果 PASS，留着做回归保护。

- [ ] **Step 3: Add fallback to `ApplyCommon` else 分支**

In `Runtime/Controls/Control.cs`, find the else branch around line 155 (after `if (parentIsAutoLayout)` block). At the top of the else block (before `if (sizeSpec.IsFlexibleWidth || sizeSpec.IsFlexibleHeight)`), insert:

```csharp
                // BCS-D7: 自由定位 + anchor 两轴都不 stretch + sizeSpec 完全无尺寸 →
                // 若控件能提供 native size (GetNativeSize)，用 native 作为默认尺寸，
                // 避免 sizeDelta=(0,0) 不可见。已有 size="native" 关键字处理在前(IsNativeWidth/Height),
                // 此 fallback 只覆盖"完全没写 size"的情况,两条互斥。
                if (!preset.StretchX && !preset.StretchY
                    && !sizeSpec.HasWidth && !sizeSpec.HasHeight)
                {
                    var nativeFallback = GetNativeSize();
                    if (nativeFallback.HasValue)
                    {
                        sizeSpec = SizeSpec.FromNumeric(nativeFallback.Value.x, nativeFallback.Value.y);
                    }
                }

```

注意：`AnchorPreset` 有 `StretchX` / `StretchY` 属性吗？查一下 `Runtime/Core/Layout/AnchorResolver.cs` 里 `AnchorPreset` 的定义。如果没有，需要看现有代码用什么属性判 stretch。

预期 `AnchorPreset` 有 `StretchX` / `StretchY`，因为 `SizeSpec.ValidateAgainst(preset)` 已经在用：
```csharp
public void ValidateAgainst(AnchorPreset anchor)
{
    if (anchor.StretchX && HasWidth) ...
```

所以这两个属性存在，照搬就行。

- [ ] **Step 4: Run tests — both should PASS now**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="BtnContentSizingTests")
```

Expected: 全部 PASS。

- [ ] **Step 5: Run full EditMode suite — make sure no regression**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: all PASS. 特别关注 `IconTests` —— Icon 也覆写了 GetNativeSize，现在自由定位下会自动走 fallback；如果旧 Icon 测试依赖"无 size 时 Icon 是 0x0"会失败，要看现有 Icon 测试是不是都显式写了 size。

如果有 Icon 回归，更新对应测试（应该是验证"Icon 无 size 在 Frame 下也按 sprite native 撑开"，是预期行为）。

- [ ] **Step 6: Commit**

```bash
git add Runtime/Controls/Control.cs Tests/EditMode/Controls/BtnContentSizingTests.cs
git commit -m "$(cat <<'EOF'
feat: ApplyCommon free-positioning fallback to GetNativeSize when no size attr

BCS-D7: 自由定位 + anchor 两轴都不 stretch + sizeSpec 完全无尺寸 → 用 GetNativeSize() 当默认尺寸。避免 <Frame><Btn>OK</Btn></Frame> 默认 sizeDelta=(0,0) 不可见。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: 剩余测试覆盖（显式覆盖 + Variant 切换）

**Files:**
- Test: `Tests/EditMode/Controls/BtnContentSizingTests.cs`（增 2 个测试）

- [ ] **Step 1: Add test — 显式 size 覆盖 native**

Append to `BtnContentSizingTests.cs`:

```csharp
[Test]
public void Btn_in_Frame_explicit_size_overrides_native()
{
    // BCS-D5: 显式 size 优先；fallback 只在没写 size 时启用
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Btn id='b' size='200x60'>OK</Btn>
  </Frame>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    var screen = UI.Open("S");
    var btn = screen.Get<Btn>("b");
    Assert.AreEqual(new Vector2(200f, 60f), btn.RectTransform.sizeDelta);
}
```

- [ ] **Step 2: Add test — Variant 切换 Text 长度，preferred 跟随**

```csharp
[Test]
public void Btn_in_HStack_variant_text_change_updates_preferred()
{
    // BCS-D9: ApplyCommon 在 Variant 切换时重跑 → GetNativeSize 重算 → LE.preferred 跟随
    const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='400' height='44'>
    <Btn id='b' text='OK' text.long='Confirm and Apply Changes'/>
  </HStack>
</Screen></PromptUGUI>";
    UI.LoadDocument("test", xml);
    UI.Variants.Set("long", false);
    var screen = UI.Open("S");
    var btn = screen.Get<Btn>("b");
    var le = btn.GameObject.GetComponent<LayoutElement>();
    var preferredShort = le.preferredWidth;
    Assert.Greater(preferredShort, 0f, "base text='OK' preferred 应该 > 0");

    UI.Variants.Set("long", true);
    Assert.Greater(le.preferredWidth, preferredShort,
        "long variant 文字更长 → preferredWidth 应该更大");
}
```

注意：`text='OK'` 是 attribute 形式而非 text content 形式，这样才能通过 Variant `text.long=` 覆盖。`Btn.Text` 是 `[UIAttr]` setter，能接 attribute；但需要确认 `ControlAttributeApplier` 的 Variant 路径 — `VariantResolver.ResolveAttribute` 在 attribute 路径下会处理 `text.long` 覆盖。

如果 attribute-form `text=` 不工作（必须 text-content 才有 shorthand），fallback 写法：用 attribute-form `text=`，并验证 Btn.cs 的 `[UIAttr] public string Text` setter 在 Variant 重应用时被调到（应该是）。

- [ ] **Step 3: Run all `BtnContentSizingTests`**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="BtnContentSizingTests")
```

Expected: all PASS（共 6 个测试：icon-only、with-text-native、HStack-no-size、Frame-no-size、anchor-stretch-skip、explicit-override、variant-change —— 实际 7 个，先确认数量对得上）。

- [ ] **Step 4: Run full EditMode suite — 终态绿**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: all PASS, no errors in console.

- [ ] **Step 5: Commit**

```bash
git add Tests/EditMode/Controls/BtnContentSizingTests.cs
git commit -m "$(cat <<'EOF'
test: explicit size override + variant-driven preferred refresh

BCS-D5/D9 回归覆盖：显式 size 优先于 native fallback；Variant 切换 Text 长度时 LE.preferredWidth 自动跟随。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: SKILL.md 同步 + lint + 最终验证

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`
- Test: 跑全套 EditMode + PlayMode

- [ ] **Step 1: 找到 `<Btn>` 在 SKILL.md 里的现有段落**

```bash
grep -n "Btn\b" .claude/skills/authoring-promptugui-xml/SKILL.md | head -20
```

- [ ] **Step 2: 在 `<Btn>` 段落补一句"默认按文字自适应"**

具体补哪行依据 grep 结果定位。补充内容（中文 / 跟现有段落语言风格保持一致）:

> **默认按文字自适应**: 不写 `size`/`width`/`height` 时，Btn 自动按文字宽 + 左右各 16 padding、上下取 max(44, 文字高 + 12) 作为 preferred / 默认 sizeDelta；无 text（icon-only）时回退到 80x44。显式 `size` / `width` / `height` 始终覆盖。

- [ ] **Step 3: 在 anchor / 自由定位段落补一句 native fallback 规则**

定位:

```bash
grep -n "free-positioning\|自由定位\|anchor.*stretch" .claude/skills/authoring-promptugui-xml/SKILL.md | head -10
```

补充:

> **自由定位 fallback to native**: 在 `<Frame>` / `<Screen>` 等自由定位父级下，控件没写 `size`/`width`/`height` 且 anchor 没 stretch 时，若控件实现了 `GetNativeSize()`，自动用 native 作为 sizeDelta（避免 0x0 不可见）。目前 `<Btn>` 和 `<Icon>` 走这条路。

- [ ] **Step 4: lint pass — 跑 dotnet format check**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: 退出码 0；若有 whitespace 差异，跑 `dotnet format whitespace PromptUGUI.Lint.slnx` 修一遍后再 verify。

- [ ] **Step 5: 跑 UIXmlLint over Runtime/Resources（防止 layout-group 子节点上残留非法 anchor）**

```bash
cd /workspace-PromptUGUI && dotnet run --project .lint/UIXmlLint -- Runtime/Resources/
```

Expected: 退出码 0。

- [ ] **Step 6: 最终全套测试**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: all PASS, no errors.

- [ ] **Step 7: 验证用户原 MessageBox 用例视觉效果**

打开 host Unity 项目的 MessageBox 测试场景（或现有 EditMode/PlayMode 的 MessageBox tests），确认 5 个按钮（OK / Cancel / Yes / No / Close）在 HStack 里各自按文字宽度撑开、视觉合理。

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="MessageBox")
```

如果有现有 MessageBox 视觉断言（应该有，docs/specs/2026-05-14-messagebox-modal-design.md 提到过），确认仍 PASS。

- [ ] **Step 8: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "$(cat <<'EOF'
docs: SKILL.md — Btn auto content sizing + free-positioning native fallback

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review Notes

- **Spec coverage**: BCS-D1 ↔ Task 2 / Task 3; D2/D3/D4 ↔ Task 2; D5 ↔ Task 5; D6 ↔ Task 3; D7/D8 ↔ Task 4; D9 ↔ Task 5; D10 (无新报错) 不需要 task; D11 (spec §6 改) 暂不在本计划范围（spec 本身的 §6 后续做 master spec batch 更新时再合）; D12 ↔ Task 6.

- **Placeholder scan**: 无 TBD / TODO / "implement later" / "似类 X" 引用。

- **Type consistency**:
  - `Btn.GetNativeSize()` 返回 `Vector2?` (Task 2)；`Control.GetNativeSize()` 基类签名同（Runtime/Controls/Control.cs:103）。
  - `SizeSpec.FromNumeric(float, float)` 返回 `SizeSpec` (Task 1)；`Control.ApplyCommon` 在 Task 4 调 `SizeSpec.FromNumeric(...)` 接收 `SizeSpec`。一致。
  - `AnchorPreset.StretchX / StretchY` 假设存在（Task 4 引用）—— Step 3 提到要查 AnchorResolver.cs 确认；`SizeSpec.ValidateAgainst` 现有代码用了这两属性，所以存在性可靠。

- **Risk: Task 4 Step 5 的全套跑里如果 Icon 测试回归** — 计划里已经提醒处理。

- **Task 5 Step 2 variant test 的 attribute-form 假设** — 计划里给了 fallback 路径说明。

---

## Execution Handoff

Plan complete and saved to `docs~/superpowers/plans/2026-05-15-btn-content-sizing.md`. Two execution options:

1. **Subagent-Driven (recommended)** - 我每 task 派一个独立 subagent，task 之间复盘，迭代快
2. **Inline Execution** - 当前 session 直接跑 executing-plans，批量执行 + checkpoint

Which approach?
