# Layout Group 子节点固定尺寸 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地 spec §6.5 文本——在 `<VStack>` / `<HStack>` 下，子节点的 `size` / `width` / `height` 走 `LayoutElement.preferredX` + `flexibleX=0`，而不是 `RectTransform.sizeDelta`。VStack / HStack 自身锁 `childControl*=true, childForceExpand*=false` 默认值。Grid 不动（cellSize 已经掌权）。

**Architecture:** 改动集中在两个文件：
- `Runtime/Controls/VStack.cs` / `HStack.cs` 的 `OnAttached` —— 显式写四个 child* 标志位
- `Runtime/Controls/Control.cs` 的 `ApplyCommon` —— 分支：父级是 `UnityEngine.UI.LayoutGroup` → 写 `LayoutElement`；否则保留现状（写 sizeDelta + anchoredPosition）

判定 "父级是 LayoutGroup" 由 `ApplyCommon` 内部 `RectTransform.parent.GetComponent<LayoutGroup>()` 自查（决策 LGC-D6），不动 `ApplyCommon` 签名、不改 `ControlAttributeApplier` / `ScreenInstantiator`。这样 `Screen.ReSolve`（Variant 切换）自动走对路径，无额外状态。

**Tech Stack:** Unity 6 `UnityEngine.UI.LayoutGroup` / `LayoutElement` / `VerticalLayoutGroup` / `HorizontalLayoutGroup`、NUnit EditMode + PlayMode test runner、Unity MCP for compile/test cycles。

**Spec:** [`docs~/superpowers/specs/2026-05-12-layout-group-child-sizing-design.md`](../specs/2026-05-12-layout-group-child-sizing-design.md)

---

## File Structure

**New files:**
- `Tests/EditMode/Controls/ControlApplyCommonLayoutGroupTests.cs` — 跨 control 类型的 LayoutElement 应用断言。EditMode 走 `UI.LoadDocument(label, xml)` + `UI.Open` 拼装 Screen，断言 `LayoutElement` / `RectTransform.sizeDelta` / `anchorMin/Max` 数值。

**Modified files:**
- `Runtime/Controls/VStack.cs` — `OnAttached` 加 4 行 child* 标志位锁定
- `Runtime/Controls/HStack.cs` — 同 VStack
- `Runtime/Controls/Control.cs` — `ApplyCommon` 分支 + 新 private `ApplyLayoutElement` 方法
- `Tests/PlayMode/Controls/VStackTests.cs` — 加 `Adds_VerticalLayoutGroup_with_fixed_child_sizing_flags` 单测 + 加 `Child_size_is_respected_after_layout_rebuild` UnityTest
- `Tests/PlayMode/Controls/HStackTests.cs` — 同 VStack 两条
- `.claude/skills/authoring-promptugui-xml/SKILL.md` — 在 layout group 相关段落（line ~152-154 附近）追加 LayoutElement 行为说明

**Unchanged:** `Runtime/Controls/Grid.cs`、`Runtime/Controls/Frame.cs`、`Runtime/Application/ScreenInstantiator.cs`、`Runtime/Application/ControlAttributeApplier.cs`、`Runtime/Application/Screen.cs`、所有 Layout/IR/Parser/Variants/Application 模块、所有现有测试（除上面列出的两个 PlayMode 文件）。

---

## Task 1: Lock `<VStack>` / `<HStack>` LayoutGroup flags

把 VStack / HStack 的默认行为从 Unity AddComponent 默认（四个 child* 全 true）改成 `childControl*=true, childForceExpand*=false`。先写 PlayMode red test，再补实现。

**Files:**
- Modify: `Tests/PlayMode/Controls/VStackTests.cs` (append)
- Modify: `Tests/PlayMode/Controls/HStackTests.cs` (append)
- Modify: `Runtime/Controls/VStack.cs` (in `OnAttached`)
- Modify: `Runtime/Controls/HStack.cs` (in `OnAttached`)

- [ ] **Step 1: Write the failing tests**

Append to `Tests/PlayMode/Controls/VStackTests.cs` (inside `public class VStackTests`):

```csharp
        [UnityTest]
        public IEnumerator Locks_child_sizing_flags()
        {
            var v = new VStack();
            var go = new GameObject("vstack", typeof(RectTransform));
            v.AttachTo(go);
            var lg = go.GetComponent<VerticalLayoutGroup>();
            Assert.IsTrue(lg.childControlWidth,
                "VStack must let LayoutElement.preferredWidth drive child width");
            Assert.IsTrue(lg.childControlHeight,
                "VStack must let LayoutElement.preferredHeight drive child height");
            Assert.IsFalse(lg.childForceExpandWidth,
                "VStack must NOT force-expand children horizontally — that defeats fixed-size children");
            Assert.IsFalse(lg.childForceExpandHeight,
                "VStack must NOT force-expand children vertically — that defeats fixed-size children");
            Object.Destroy(go);
            yield return null;
        }
```

Append to `Tests/PlayMode/Controls/HStackTests.cs` (inside `public class HStackTests`):

```csharp
        [UnityTest]
        public IEnumerator Locks_child_sizing_flags()
        {
            var h = new HStack();
            var go = new GameObject("hstack", typeof(RectTransform));
            h.AttachTo(go);
            var lg = go.GetComponent<HorizontalLayoutGroup>();
            Assert.IsTrue(lg.childControlWidth);
            Assert.IsTrue(lg.childControlHeight);
            Assert.IsFalse(lg.childForceExpandWidth);
            Assert.IsFalse(lg.childForceExpandHeight);
            Object.Destroy(go);
            yield return null;
        }
```

- [ ] **Step 2: Refresh Unity and verify the tests fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="VStackTests")
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="HStackTests")
```

Expected: 0 compile errors. Both `Locks_child_sizing_flags` tests **fail** — `childForceExpandWidth/Height` will be reported as `true` (Unity 6 `AddComponent` default).

If the assertions about `childControlWidth/Height` also fail (also returning `false`), the Unity default has shifted; update the spec footnote, but the test still serves its purpose.

- [ ] **Step 3: Apply the fix to VStack**

Edit `Runtime/Controls/VStack.cs`, `OnAttached` method:

```csharp
        public override void OnAttached()
        {
            _layout = GameObject.GetComponent<VerticalLayoutGroup>()
                      ?? GameObject.AddComponent<VerticalLayoutGroup>();
        }
```

to:

```csharp
        public override void OnAttached()
        {
            _layout = GameObject.GetComponent<VerticalLayoutGroup>()
                      ?? GameObject.AddComponent<VerticalLayoutGroup>();
            // spec §6.5: child size/width/height routes to LayoutElement.preferredX with flexibleX=0.
            // childControl* must be true for LayoutElement to take effect; force-expand must be off
            // so fixed-size children are not stretched into the remaining space.
            _layout.childControlWidth = true;
            _layout.childControlHeight = true;
            _layout.childForceExpandWidth = false;
            _layout.childForceExpandHeight = false;
        }
```

Apply the same change to `Runtime/Controls/HStack.cs` (`HorizontalLayoutGroup`, same four field names).

- [ ] **Step 4: Refresh and re-run**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="VStackTests")
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="HStackTests")
```

Expected: both `Locks_child_sizing_flags` tests pass; pre-existing `Adds_*LayoutGroup` / `Spacing*` / `Padding*` tests still pass (the flags are independent of those).

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/VStack.cs Runtime/Controls/HStack.cs \
        Tests/PlayMode/Controls/VStackTests.cs Tests/PlayMode/Controls/HStackTests.cs
git commit -m "$(cat <<'EOF'
feat: lock VStack/HStack child-sizing flags to no-force-expand

childControlWidth/Height=true, childForceExpandWidth/Height=false.
Prep for spec §6.5: child size/width/height routing to LayoutElement.
With force-expand on (Unity's AddComponent default), fixed-size children
get stretched to fill remaining space.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Route child `size` to `LayoutElement` inside layout groups

`Control.ApplyCommon` 分支：父级是 LayoutGroup → 写 `LayoutElement.preferredX/flexibleX`，跳过 anchor / pivot / sizeDelta / anchoredPosition；不是 → 保留现状。

**Files:**
- Create: `Tests/EditMode/Controls/ControlApplyCommonLayoutGroupTests.cs`
- Modify: `Runtime/Controls/Control.cs` (`ApplyCommon` 方法 + 新增 private `ApplyLayoutElement`)

- [ ] **Step 1: Write the failing tests**

Create `Tests/EditMode/Controls/ControlApplyCommonLayoutGroupTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ControlApplyCommonLayoutGroupTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Btn_in_VStack_with_size_writes_LayoutElement_preferred_with_flexible_zero()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='200' height='200'>
    <Btn id='b' size='64x64'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("stack/b");
            var le = btn.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "Btn inside VStack with size= must get a LayoutElement");
            Assert.AreEqual(64f, le.preferredWidth);
            Assert.AreEqual(64f, le.preferredHeight);
            Assert.AreEqual(0f, le.flexibleWidth);
            Assert.AreEqual(0f, le.flexibleHeight);
        }

        [Test]
        public void Btn_in_VStack_with_only_width_leaves_height_axis_unconstrained()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='200' height='200'>
    <Btn id='b' width='100'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("stack/b");
            var le = btn.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le);
            Assert.AreEqual(100f, le.preferredWidth);
            Assert.AreEqual(0f, le.flexibleWidth);
            Assert.AreEqual(-1f, le.preferredHeight,
                "Unspecified height axis must be -1 (LayoutElement 'ignored' sentinel)");
            Assert.AreEqual(-1f, le.flexibleHeight);
        }

        [Test]
        public void Btn_in_VStack_with_no_size_attrs_gets_no_LayoutElement()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='200' height='200'>
    <Btn id='b'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("stack/b");
            Assert.IsNull(btn.GameObject.GetComponent<LayoutElement>(),
                "No size/width/height -> no LayoutElement; intrinsic ILayoutElement (Image/TMP) drives sizing");
        }

        [Test]
        public void Btn_in_Frame_with_size_writes_sizeDelta_not_LayoutElement()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='frame' anchor='stretch' margin='0'>
    <Btn id='b' size='64x64'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("frame/b");
            Assert.IsNull(btn.GameObject.GetComponent<LayoutElement>(),
                "Btn under Frame (non-LayoutGroup parent) must NOT get a LayoutElement");
            Assert.AreEqual(new Vector2(64f, 64f), btn.RectTransform.sizeDelta,
                "Non-LayoutGroup parent: size still writes to sizeDelta (existing behavior preserved)");
        }

        [Test]
        public void Btn_in_Grid_with_size_gets_no_LayoutElement()
        {
            // Grid uses cellSize for all children; LayoutElement on Grid children is ignored
            // by GridLayoutGroup, so we don't add one — avoids the appearance of a knob that
            // actually does nothing.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Grid id='grid' columns='2' cellSize='40x40' width='200' height='200'>
    <Btn id='b' size='64x64'/>
  </Grid>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("grid/b");
            Assert.IsNull(btn.GameObject.GetComponent<LayoutElement>(),
                "Btn under Grid must NOT get a LayoutElement (GridLayoutGroup ignores it; cellSize wins)");
        }

        [Test]
        public void LayoutElement_inside_VStack_skips_rect_anchored_and_size_writes()
        {
            // Author-supplied anchor/margin under a LayoutGroup is ignored by design (spec §6.5).
            // We also skip writing anchorMin/anchorMax/anchoredPosition/sizeDelta so the layout
            // pass owns geometry without contention.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='200' height='200'>
    <Btn id='b' size='64x64'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("stack/b");
            // sizeDelta should be untouched by ApplyCommon (default 0,0 from RectTransform init).
            Assert.AreEqual(Vector2.zero, btn.RectTransform.sizeDelta,
                "ApplyCommon must not write sizeDelta when parent is a LayoutGroup");
        }
    }
}
```

- [ ] **Step 2: Refresh and verify the tests fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ControlApplyCommonLayoutGroupTests")
```

Expected: 0 compile errors. Most tests **fail**:
- `Btn_in_VStack_with_size_writes_LayoutElement_preferred_with_flexible_zero` — `GetComponent<LayoutElement>()` returns null
- `Btn_in_VStack_with_only_width_leaves_height_axis_unconstrained` — same null
- `LayoutElement_inside_VStack_skips_rect_anchored_and_size_writes` — `sizeDelta` is `(64,64)` not `(0,0)`

Should **pass** already (regression checks for behavior we won't break):
- `Btn_in_VStack_with_no_size_attrs_gets_no_LayoutElement` (current code never adds LayoutElement)
- `Btn_in_Frame_with_size_writes_sizeDelta_not_LayoutElement` (current code writes sizeDelta everywhere)
- `Btn_in_Grid_with_size_gets_no_LayoutElement` (current code never adds LayoutElement)

If a "should pass already" test fails, stop — the current state isn't what we documented in the spec; investigate before proceeding.

- [ ] **Step 3: Implement the branch in `Control.ApplyCommon`**

Edit `Runtime/Controls/Control.cs`. The current method (lines 91-133) sets RectTransform fields unconditionally. Restructure so the LayoutGroup branch runs first.

Replace the existing `public void ApplyCommon(...)` method body with:

```csharp
        public void ApplyCommon(string anchor, string size, string width, string height,
                                string margin, string pivot,
                                bool hidden, bool interactable)
        {
            var preset = string.IsNullOrEmpty(anchor)
                ? new AnchorPreset(AnchorVertical.Top, AnchorHorizontal.Left)
                : AnchorPreset.Parse(anchor);

            var sizeSpec = SizeSpec.Parse(size, width, height);

            if (sizeSpec.IsNativeWidth || sizeSpec.IsNativeHeight)
            {
                var native = GetNativeSize();
                if (native.HasValue)
                    sizeSpec = sizeSpec.WithNativeResolved(native.Value);
            }

            sizeSpec.ValidateAgainst(preset);

            var parentLg = RectTransform.parent != null
                ? RectTransform.parent.GetComponent<UnityEngine.UI.LayoutGroup>()
                : null;
            // GridLayoutGroup ignores LayoutElement on its children — cellSize wins.
            // Adding one would create the illusion of a knob that does nothing.
            var parentIsAuto = parentLg != null
                && !(parentLg is UnityEngine.UI.GridLayoutGroup);

            if (parentIsAuto)
            {
                ApplyLayoutElement(sizeSpec);
                // anchor / pivot / sizeDelta / anchoredPosition: LayoutGroup owns geometry.
                // Author-supplied anchor/margin under a LayoutGroup is already warned about
                // by ScreenInstantiator (spec §6.5); we silently ignore here.
            }
            else
            {
                AnchorResolver.Resolve(preset, out var aMin, out var aMax, out var p);
                RectTransform.anchorMin = aMin;
                RectTransform.anchorMax = aMax;
                if (!string.IsNullOrEmpty(pivot))
                {
                    var parts = pivot.Split(',');
                    RectTransform.pivot = new Vector2(
                        float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    RectTransform.pivot = p;
                }
                var lr = MarginResolver.Resolve(preset, sizeSpec, margin);
                RectTransform.anchoredPosition = lr.AnchoredPosition;
                RectTransform.sizeDelta = lr.SizeDelta;
            }

            Hidden = hidden;
            Interactable = interactable;
        }

        private void ApplyLayoutElement(SizeSpec sizeSpec)
        {
            // Decision LGC-D8: no LayoutElement when author wrote no size attrs.
            // Decision LGC-D9: per-axis routing — unspecified axis stays at -1 sentinel.
            // Decision LGC-D10: reset both axes first to clear prior Variant residue.
            if (!sizeSpec.HasWidth && !sizeSpec.HasHeight)
            {
                // If a LayoutElement got attached by a previous Variant pass, neutralize it.
                var existing = GameObject.GetComponent<UnityEngine.UI.LayoutElement>();
                if (existing != null)
                {
                    existing.preferredWidth = -1;
                    existing.preferredHeight = -1;
                    existing.flexibleWidth = -1;
                    existing.flexibleHeight = -1;
                }
                return;
            }
            var le = GameObject.GetComponent<UnityEngine.UI.LayoutElement>()
                     ?? GameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            le.preferredWidth = -1;
            le.preferredHeight = -1;
            le.flexibleWidth = -1;
            le.flexibleHeight = -1;
            if (sizeSpec.HasWidth)
            {
                le.preferredWidth = sizeSpec.Width;
                le.flexibleWidth = 0;
            }
            if (sizeSpec.HasHeight)
            {
                le.preferredHeight = sizeSpec.Height;
                le.flexibleHeight = 0;
            }
        }
```

Note on the GridLayoutGroup check: `GridLayoutGroup` derives from `LayoutGroup`, so a `parent.GetComponent<LayoutGroup>()` call returns it; the explicit `!(parentLg is GridLayoutGroup)` filter implements decision LGC-D12 by dropping Grid out of the new code path (Grid children fall through to the else branch and write sizeDelta — even though GridLayoutGroup will overwrite that during its layout pass, so it ends up benign).

- [ ] **Step 4: Refresh and re-run the EditMode tests**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ControlApplyCommonLayoutGroupTests")
```

Expected: all 6 tests pass.

- [ ] **Step 5: Run the full EditMode + EditorOnly suites for regression**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
```

Expected: all green. Likely impact zones to watch:
- `Tests/EditMode/Layout/AnchorResolverTests.cs` / `MarginResolverTests.cs` — these test resolvers directly (no ApplyCommon), should be untouched.
- `Tests/EditMode/Controls/*` — controls that get instantiated under `<Screen>` directly (no VStack/HStack parent) → still write sizeDelta → no behavior change.
- `Tests/EditMode/Application/InstantiateNodeTests.cs` — exercises `ScreenInstantiator.InstantiateNode` which already has `parentIsLayoutGroup` plumbing. Verify the assertions don't depend on sizeDelta of layout-group children.

If a test breaks, run it isolated and read the failure: likely it was implicitly relying on sizeDelta being set on a layout-group child. Fix the test if it was an unsound assumption, or revise the implementation if the breakage reveals a missing case.

- [ ] **Step 6: Commit**

```bash
git add Runtime/Controls/Control.cs \
        Tests/EditMode/Controls/ControlApplyCommonLayoutGroupTests.cs
git commit -m "$(cat <<'EOF'
feat: route layout-group child size to LayoutElement (spec §6.5)

Control.ApplyCommon now branches on parent's LayoutGroup presence:
- VStack/HStack child: write LayoutElement.preferredX (+ flexibleX=0)
  per axis specified; skip sizeDelta / anchor / pivot writes
- Frame / Screen-root / non-LayoutGroup parent: existing behavior
  (AnchorResolver + MarginResolver + sizeDelta)
- Grid child: skip LayoutElement (GridLayoutGroup ignores it;
  cellSize is authoritative) — fall through to legacy path

Per-axis routing: unspecified axis (e.g. width-only) leaves the other
axis at -1 (LayoutElement "ignored" sentinel) so intrinsic preferred
size applies. Variant re-application via Screen.ReSolve is covered by
the same code path — no extra state.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: PlayMode end-to-end — real layout pass respects declared sizes

EditMode tests assert on LayoutElement field values; only PlayMode runs the actual LayoutRebuilder.ForceRebuildLayoutImmediate pass. This task proves the whole chain (flag locking + LayoutElement routing) produces the expected `rect.size` after Unity's layout system runs.

**Files:**
- Modify: `Tests/PlayMode/Controls/VStackTests.cs` (append)
- Modify: `Tests/PlayMode/Controls/HStackTests.cs` (append)

- [ ] **Step 1: Write the new PlayMode test on VStackTests**

Append to `Tests/PlayMode/Controls/VStackTests.cs`:

```csharp
        [UnityTest]
        public IEnumerator Fixed_size_child_is_not_stretched_after_layout_rebuild()
        {
            // Mirrors the bug scenario from the spec: VStack height=84, Btn size=64x64,
            // Text height=14. Pre-fix, Btn would be stretched to ~41 by VLG force-expand.
            var canvasGo = new GameObject("canvas", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var vs = new VStack();
            var stackGo = new GameObject("stack", typeof(RectTransform));
            stackGo.transform.SetParent(canvasGo.transform, worldPositionStays: false);
            vs.AttachTo(stackGo);
            vs.Spacing = 2f;
            var stackRt = (RectTransform)stackGo.transform;
            stackRt.sizeDelta = new Vector2(70f, 84f);

            // Btn — sized via LayoutElement to mimic ApplyCommon's behavior under a layout group.
            var btnGo = new GameObject("btn",
                typeof(RectTransform),
                typeof(UnityEngine.UI.Image),
                typeof(LayoutElement));
            btnGo.transform.SetParent(stackGo.transform, worldPositionStays: false);
            var btnLe = btnGo.GetComponent<LayoutElement>();
            btnLe.preferredWidth = 64f;
            btnLe.preferredHeight = 64f;
            btnLe.flexibleWidth = 0f;
            btnLe.flexibleHeight = 0f;

            // Text — same shape.
            var textGo = new GameObject("text",
                typeof(RectTransform),
                typeof(LayoutElement));
            textGo.transform.SetParent(stackGo.transform, worldPositionStays: false);
            var textLe = textGo.GetComponent<LayoutElement>();
            textLe.preferredHeight = 14f;
            textLe.flexibleHeight = 0f;
            // width axis unconstrained — VStack childControlWidth fills the 70-wide stack

            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(stackRt);
            yield return null;

            var btnRt = (RectTransform)btnGo.transform;
            var textRt = (RectTransform)textGo.transform;
            Assert.AreEqual(64f, btnRt.rect.height, 0.5f,
                "Btn must not be stretched by VStack — LayoutElement.preferredHeight + flexibleHeight=0 is binding");
            Assert.AreEqual(14f, textRt.rect.height, 0.5f);
            Object.Destroy(canvasGo);
        }
```

Note: this test exercises VStack-with-LayoutElement-children directly, not the XML pipeline. EditMode tests already cover XML → LayoutElement; this PlayMode test covers LayoutElement → actual rect.

Append the symmetric test to `Tests/PlayMode/Controls/HStackTests.cs`:

```csharp
        [UnityTest]
        public IEnumerator Fixed_size_child_is_not_stretched_after_layout_rebuild()
        {
            var canvasGo = new GameObject("canvas", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var hs = new HStack();
            var stackGo = new GameObject("stack", typeof(RectTransform));
            stackGo.transform.SetParent(canvasGo.transform, worldPositionStays: false);
            hs.AttachTo(stackGo);
            hs.Spacing = 4f;
            var stackRt = (RectTransform)stackGo.transform;
            stackRt.sizeDelta = new Vector2(200f, 60f);

            var aGo = new GameObject("a", typeof(RectTransform), typeof(LayoutElement));
            aGo.transform.SetParent(stackGo.transform, worldPositionStays: false);
            var aLe = aGo.GetComponent<LayoutElement>();
            aLe.preferredWidth = 64f;
            aLe.flexibleWidth = 0f;

            var bGo = new GameObject("b", typeof(RectTransform), typeof(LayoutElement));
            bGo.transform.SetParent(stackGo.transform, worldPositionStays: false);
            var bLe = bGo.GetComponent<LayoutElement>();
            bLe.preferredWidth = 80f;
            bLe.flexibleWidth = 0f;

            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(stackRt);
            yield return null;

            Assert.AreEqual(64f, ((RectTransform)aGo.transform).rect.width, 0.5f);
            Assert.AreEqual(80f, ((RectTransform)bGo.transform).rect.width, 0.5f);
            Object.Destroy(canvasGo);
        }
```

- [ ] **Step 2: Refresh and run**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="VStackTests")
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="HStackTests")
```

Expected: all green. These tests assume Task 1 (flag locking) is already merged — they're written against the new defaults.

- [ ] **Step 3: Run the full PlayMode suite**

```
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

Expected: all green.

- [ ] **Step 4: Commit**

```bash
git add Tests/PlayMode/Controls/VStackTests.cs Tests/PlayMode/Controls/HStackTests.cs
git commit -m "$(cat <<'EOF'
test: PlayMode coverage for fixed-size children under V/HStack

Drives a real LayoutRebuilder.ForceRebuildLayoutImmediate pass to
prove LayoutElement.preferredX + flexibleX=0 produces the asserted
rect dimensions (vs. the prior force-expand behavior).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Variant re-application correctness

`Screen.ReSolve` calls `ControlAttributeApplier.Apply` → `Control.ApplyCommon` per node on every variant change. Since the LayoutGroup detection is recomputed inside `ApplyCommon` (decision LGC-D6), variant switches automatically take the right path. This task adds explicit coverage to prevent future regressions, especially when a variant changes the set of size attributes (e.g. `size` → `width` only).

**Files:**
- Modify: `Tests/EditMode/Controls/ControlApplyCommonLayoutGroupTests.cs` (append)

- [ ] **Step 1: Add the variant test**

Append inside `public class ControlApplyCommonLayoutGroupTests`:

```csharp
        [Test]
        public void Variant_switch_from_size_to_width_only_resets_height_axis()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Variants name='breakpoint'><Option name='desktop'/><Option name='mobile'/></Variants>
  <Screen name='S'>
    <VStack id='stack' width='200' height='200'>
      <Btn id='b' size='64x64' width.mobile='100'/>
    </VStack>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            UI.Variants.Set("breakpoint", "desktop");
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("stack/b");
            var le = btn.GameObject.GetComponent<LayoutElement>();
            Assert.AreEqual(64f, le.preferredHeight, "desktop variant: height fixed");
            Assert.AreEqual(0f, le.flexibleHeight);

            UI.Variants.Set("breakpoint", "mobile");
            // ReSolve has fired via Variants.Changed subscription.
            Assert.AreEqual(100f, le.preferredWidth, "mobile variant: width override");
            Assert.AreEqual(0f, le.flexibleWidth);
            Assert.AreEqual(-1f, le.preferredHeight,
                "mobile variant has no height — must reset to -1, not retain 64 from prior variant");
            Assert.AreEqual(-1f, le.flexibleHeight);
        }
```

Note: this test assumes `width.mobile` is a valid variant-override attribute syntax (spec §8 Variants). Verify by reading `Runtime/Core/Variants/VariantResolver.cs` if uncertain — but the existing variant tests in `Tests/EditMode/Variants/VariantResolverTests.cs` and `Tests/EditMode/Application/VariantStoreNotifyTests.cs` will confirm the syntax is supported. If `width.mobile` isn't the right notation, adjust the XML to whatever the project uses (e.g. inline `<Variant>` block).

If the variant syntax investigation reveals it's per-element nested (`<Btn><Variant when='mobile' width='100'/></Btn>`), use that form instead.

- [ ] **Step 2: Refresh and run**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ControlApplyCommonLayoutGroupTests")
```

Expected: all 7 tests pass (6 from Task 2 + 1 new). The variant reset path was implemented in `ApplyLayoutElement` from Task 2 (it always resets both axes to -1 before reapplying); this test just locks it down.

If the new test fails on "height retains 64 from prior variant", the reset block in `ApplyLayoutElement` isn't being entered — most likely because the implementation took a shortcut and only resets the axes it's about to write. Restore the unconditional reset.

- [ ] **Step 3: Commit**

```bash
git add Tests/EditMode/Controls/ControlApplyCommonLayoutGroupTests.cs
git commit -m "$(cat <<'EOF'
test: lock LayoutElement axis reset across Variant switches

When a Variant changes the set of size attributes (size -> width only),
the previously-set axis must be reset to -1 so intrinsic preferred size
applies. ApplyCommon's unconditional reset block enforces this, but
without an explicit test future refactors could silently regress.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Update SKILL.md

Per CLAUDE.md "Changes to anchor / size / margin / Variant / Template / Import / `if=` semantics" require SKILL.md sync. This change alters how `size` resolves under layout groups — clearly in scope.

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`

- [ ] **Step 1: Locate the insertion point**

The existing layout-group note is around line 152-154:

```markdown
`padding` and `spacing` are **NOT** universal — only on `<VStack>` / `<HStack>` / `<Grid>`.

`anchor` and `margin` are **NOT** available on `<VStack>` / `<HStack>` / `<Grid>`.
```

Verify by:

```bash
grep -n "anchor.*margin.*NOT.*available" /workspace-PromptUGUI/.claude/skills/authoring-promptugui-xml/SKILL.md
```

- [ ] **Step 2: Append the new paragraph after that line**

Insert a new paragraph immediately after the "anchor and margin are NOT available" line:

```markdown
**Inside `<VStack>` / `<HStack>`**, a child's `size` / `width` / `height` is written to `LayoutElement.preferredX` with `flexibleX=0` (not to `sizeDelta`). So `<Btn size="64x64"/>` inside a VStack is **strictly 64×64** — the layout group will not stretch it. Specifying only one axis (e.g. `width="100"`) leaves the other axis unconstrained, taking the child's intrinsic preferred size. Omitting all size attributes gets no `LayoutElement` — the child collapses to whatever its own components advertise (often 0 for an empty Frame), so write at least one axis when you need a visible footprint.

**Inside `<Grid>`**, the parent's `cellSize` is authoritative — a child's `size` is silently ignored.
```

- [ ] **Step 3: Verify the existing "spec §6.5" reference in SKILL.md is still consistent**

```bash
grep -n "§6.5\|spec.*6\.5" /workspace-PromptUGUI/.claude/skills/authoring-promptugui-xml/SKILL.md
```

If no matches, no further action. If a stale matching sentence describes the old (broken) behavior, update it inline.

- [ ] **Step 4: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "$(cat <<'EOF'
doc: SKILL.md — LayoutElement routing for V/HStack children

Document that <Btn size="64x64"/> inside VStack is now strictly 64x64
(LayoutElement.preferredX + flexibleX=0), and that single-axis specs
leave the other axis unconstrained. Grid's cellSize-wins semantics
called out explicitly to forestall confusion.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Lint + full test sweep + host project visual check

Per CLAUDE.md: lint after writing code; full EditMode + PlayMode runs before declaring done. Plus a brief host-project visual smoke check since this changes default layout behavior for any existing Screen demos.

**Files:** none modified unless lint reports something.

- [ ] **Step 1: Run lint (whitespace + style + analyzers, all at warn severity)**

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format style       PromptUGUI.Lint.slnx
dotnet format analyzers   PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Per CLAUDE.md, do NOT pass `--severity info` to `dotnet format analyzers` — the listed Unity-breaking auto-fixers (CA1822, CA1846, CA2016, IDE0032, IDE0044) will damage the codebase.

If lint applied any whitespace/style fixes, stage and commit them separately:

```bash
cd /workspace-PromptUGUI
git status
git add -p   # review hunks
git commit -m "$(cat <<'EOF'
chore: lint pass for layout-group child sizing

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 2: Run all four test assemblies**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Addressables"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

Expected: all green. If any test fails, do NOT proceed — diagnose using `mcp__UnityMCP__read_console(action="get", types=["error"])` and the test runner output; fix the root cause; re-run.

- [ ] **Step 3: Host project visual smoke check**

Open the host Unity project at `C:\xsoft\PromptUGUIDev` (or ask the user). For each Screen demo that uses VStack/HStack, open the corresponding `.ui.xml` Screen via the runtime entry point and visually compare against pre-change screenshots if available. Key things to look for:

- Children of VStack/HStack with explicit `size` / `width` / `height` now match those values (vs. previously being force-expanded)
- Children of VStack/HStack with **no** size attrs may now collapse to 0 — flag any visual regressions to the user for triage (the design intent per LGC-D8 is "author must specify size", but pre-existing demos may have been relying on force-expand behavior)
- Container-level spacing / padding still works
- Grid demos are unchanged

If a regression is found that needs source XML adjustment, raise it to the user before changing any demo `.ui.xml`. The user's instruction was "现在没多少 ui，影响不大" — confirm that holds.

- [ ] **Step 4: Final git status check**

```bash
git status
git log --oneline -10
```

Expected: clean working tree; commit log shows (in order):
1. Lock VStack/HStack child-sizing flags
2. Route layout-group child size to LayoutElement
3. PlayMode coverage for fixed-size children
4. Variant axis reset test
5. SKILL.md doc update
6. (optional) Lint pass

---

## Notes for the executing engineer

- **Test execution is via Unity MCP only.** Do not invoke `Unity.exe -batchmode` or similar. If `mcp__UnityMCP__run_tests` is unavailable, reconnect MCP or stop and ask the user.
- **Forbidden: `mcp__UnityMCP__execute_menu_item(menu_path="Assets/Reimport All")`** — pops a blocking modal in Unity that the user must dismiss manually. Use `refresh_unity(mode="force", scope="all")` instead.
- **After every code edit, refresh first, then `read_console` for errors before running tests.** Compile errors won't appear in the test runner output — they'll just look like missing test assemblies.
- **Filter tests by class name** with `filter="ClassName"` (substring match) for fast iteration during red-green cycles.
- **Don't refactor `ScreenInstantiator` / `ControlAttributeApplier`.** The spec deliberately keeps `ApplyCommon`'s LayoutGroup detection local (decision LGC-D6) — threading `parentIsLayoutGroup` through the call chain was considered and rejected because `Screen.ReSolve` can call `ApplyCommon` outside the instantiation context.
- **Watch for `GridLayoutGroup` derived-class trap.** `parent.GetComponent<LayoutGroup>()` returns `GridLayoutGroup` too (it inherits `LayoutGroup`). The `!(parentLg is GridLayoutGroup)` filter in `ApplyCommon` is load-bearing — drop it and Grid children will start getting useless LayoutElement components.
- **Per-axis -1 sentinel.** `LayoutElement.preferredWidth = -1` (and `flexibleWidth = -1`) tells Unity "no opinion on this axis"; setting either to 0 means "I want this axis to be 0" — very different. Don't conflate.
- **Variant tests can be tricky.** If Task 4 Step 1 fails due to variant syntax misunderstanding, read `Tests/EditMode/Variants/VariantResolverTests.cs` for canonical examples of how the project does per-attribute variant overrides — match that exactly rather than inventing syntax.
- **Don't touch `Frame` or `Image`'s sizing logic.** They're not layout groups; they take the existing else branch unchanged.
