# SafeArea Margin Absorb v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `<SafeArea margin="6,6,6,6">` absorb device safe-area inset per edge (`max(designMargin_i, deviceInset_i)`) at the container level, so children排版照旧 — no v1-style child-level plumbing.

**Architecture:** SafeArea control reads its own `margin` attribute via the existing `ApplyCommon` path (which writes "pure design margin" offsets to the RectTransform). After `ApplyCommon`, `OnAfterApply` snapshots those offsets via `tracker.CaptureDesignMargin`, then `tracker.Apply` blends with the device safe-area inset (converted to design px through `canvas.scaleFactor`) and writes the final offsets. `MarginResolver` / `Control.ApplyCommon` / `ControlAttributeApplier` / `Screen` 一行不动。

**Tech Stack:** Unity 6 uGUI, NUnit (EditMode + PlayMode), C# 9. No new dependencies.

**Spec:** [`docs~/superpowers/specs/2026-05-26-safearea-margin-absorb-v2-design.md`](../specs/2026-05-26-safearea-margin-absorb-v2-design.md)

---

## File Structure

**Modified production files (3):**
- `Runtime/Core/Parser/UIDocumentParser.cs` — remove `"margin"` from SafeArea attribute deny list (lines ~408-422), update parse error message
- `Runtime/Controls/Internal/SafeAreaTracker.cs` — add `_designOffsetMin/Max` + `CaptureDesignMargin` + `ScaleFactorOverride` (test injection); rewrite `Apply` to do per-edge max blend
- `Runtime/Controls/SafeArea.cs` — `OnAfterApply` calls `_tracker.CaptureDesignMargin(RectTransform)` before `_tracker.Apply()`

**Modified test files (3):**
- `Tests/EditMode/Parser/SafeAreaParserTests.cs` — flip `SafeArea_with_margin_throws` → `SafeArea_with_margin_does_not_throw`; keep all other deny tests
- `Tests/EditMode/Controls/SafeAreaTests.cs` — update existing anchor-fraction assertions to v2 offset representation; add max-blend matrix tests
- `Tests/PlayMode/Controls/SafeAreaTests.cs` — update anchor-fraction assertions to v2 offset representation; add one margin-absorption end-to-end test

**Modified docs (2):**
- `.claude/skills/authoring-promptugui-xml/SKILL.md` — rewrite "Safe area" section (around line 150); update tag table at line 85
- `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` — rewrite §5.5 around line 208

**NOT touched** (architectural commitment, see spec §3 + MA2-D1):
- `Runtime/Controls/Control.cs`
- `Runtime/Core/Layout/MarginResolver.cs`
- `Runtime/Application/ControlAttributeApplier.cs`
- `Runtime/Application/Screen.cs`
- All other built-in controls

---

### Task 1: Baseline check

**Files:** none — verification only.

- [ ] **Step 1: Confirm starting branch**

Run: `git status` (via Bash)
Expected output starts with: `On branch feat/safearea-margin-absorb-v2` and `nothing to commit, working tree clean`. The previous commit is the spec doc.

- [ ] **Step 2: Run existing SafeArea EditMode tests via UnityMCP**

Use `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` then `mcp__UnityMCP__read_console(action="get", types=["error"])`.
Expected: no compile errors.

Then `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SafeArea")`.
Expected: all SafeArea EditMode tests pass (parser tests + tracker tests).

- [ ] **Step 3: Run existing SafeArea PlayMode tests**

Use `mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="SafeArea")`.
Expected: all 3 SafeArea PlayMode tests pass.

If MCP is unavailable or any of these fail, STOP and surface the issue before touching code. Per `CLAUDE.md` memory `project_unity_mcp_test_gotchas`: MCP "failed to initialize" usually means an unsaved scene in the host Unity project — ask the user to save/close the scene and retry; restart MCP if it hangs.

---

### Task 2: Parser allow `margin` on SafeArea

**Files:**
- Modify: `Tests/EditMode/Parser/SafeAreaParserTests.cs:52-58` (flip the throw test to an allow test)
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs:406-422` (drop "margin" from deny list, update error message)

- [ ] **Step 1: Update parser test — replace throw test with allow test**

Edit `Tests/EditMode/Parser/SafeAreaParserTests.cs`, replace the `SafeArea_with_margin_throws` test body with:

```csharp
        [Test]
        public void SafeArea_with_margin_does_not_throw()
        {
            // v2 (2026-05-26-safearea-margin-absorb-v2): SafeArea accepts `margin` —
            // per-edge max-blended with device safe area inset.
            var xml = Header + "<SafeArea margin='6,6,6,6'/>" + Footer;
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }
```

- [ ] **Step 2: Run parser tests, confirm new test FAILS (red)**

Use `mcp__UnityMCP__refresh_unity` + `read_console` (expect no compile errors), then `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SafeAreaParserTests")`.

Expected: `SafeArea_with_margin_does_not_throw` FAILS (because parser still throws `ParseException` on `margin`). Other parser tests pass.

- [ ] **Step 3: Drop "margin" from the parser deny list + update error message**

Edit `Runtime/Core/Parser/UIDocumentParser.cs` around line 410. Change:

```csharp
            // <SafeArea> 校验：禁止 layout 类属性，几何完全由 Screen.safeArea 决定。
            // 要 padding 用 <Frame margin="..."> 嵌套；要不同形状用其他容器组合。
            if (tag == "SafeArea" && ns == null)
            {
                foreach (var key in new[] { "anchor", "size", "width", "height", "margin", "pivot" })
                {
                    if (node.Attributes.ContainsKey(key))
                        throw new ParseException(
                            $"<SafeArea> does not accept attribute '{key}'; " +
                            $"SafeArea is always stretched to Screen.safeArea. " +
                            $"To add inner padding, wrap content in <Frame margin=\"...\"/> inside the SafeArea.");
                    if (node.VariantOverrides.ContainsKey(key))
                        throw new ParseException(
                            $"<SafeArea> does not accept variant override for '{key}'; " +
                            $"SafeArea is always stretched to Screen.safeArea.");
                }
            }
```

to:

```csharp
            // <SafeArea> 校验：仍禁止形状类 layout 属性（anchor/size/width/height/pivot），
            // 几何固定为"stretch + per-edge max(margin, deviceInset)"。
            // `margin` 在 v2 (2026-05-26-safearea-margin-absorb-v2) 已解禁 —— 它是 SafeArea
            // 自身的设计 margin，跟 device safe-area inset 取大。
            if (tag == "SafeArea" && ns == null)
            {
                foreach (var key in new[] { "anchor", "size", "width", "height", "pivot" })
                {
                    if (node.Attributes.ContainsKey(key))
                        throw new ParseException(
                            $"<SafeArea> does not accept attribute '{key}'; " +
                            $"SafeArea is always stretched to its parent. " +
                            $"Use <SafeArea margin=\"...\"> for inset (absorbed by device safe area).");
                    if (node.VariantOverrides.ContainsKey(key))
                        throw new ParseException(
                            $"<SafeArea> does not accept variant override for '{key}'; " +
                            $"SafeArea is always stretched to its parent.");
                }
            }
```

- [ ] **Step 4: Run parser tests again, confirm GREEN**

`mcp__UnityMCP__refresh_unity` + `read_console` (no compile errors), then `mcp__UnityMCP__run_tests(mode="EditMode", filter="SafeAreaParserTests")`.

Expected: all 9 SafeAreaParserTests pass (including the new `SafeArea_with_margin_does_not_throw`).

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Parser/UIDocumentParser.cs Tests/EditMode/Parser/SafeAreaParserTests.cs
git commit -m "$(cat <<'EOF'
feat(parser): allow `margin` on <SafeArea> for v2 absorb semantics

Drop "margin" from the SafeArea attribute deny list. Per spec
2026-05-26-safearea-margin-absorb-v2-design §3.1: `margin` on
<SafeArea> is now the design value that gets max-blended with the
device safe-area inset. Implementation of the max blend lands in
the next commit (SafeAreaTracker rewrite). Updated parse-error text
to point authors at the v2 usage.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Verify: `git status` shows clean tree.

---

### Task 3: SafeAreaTracker — design margin capture + max-blend Apply

**Files:**
- Modify: `Runtime/Controls/Internal/SafeAreaTracker.cs` (full rewrite of `Apply` + new fields/methods/override)
- Modify: `Tests/EditMode/Controls/SafeAreaTests.cs` (rewrite existing anchor-fraction tests + add max-blend matrix tests)

This task changes RectTransform representation:
- v0: `anchorMin/Max = safeArea / Screen.size` fraction, `offsetMin/Max = 0`
- v2: `anchorMin/Max = (0,0)/(1,1)`, `offsetMin/Max = per-edge max(designMargin, inset_designPx)`

Visual result for `<SafeArea/>` (no margin) is identical to v0; internal RectTransform values differ. Existing tracker tests that assert anchor fractions need to switch to asserting offsets.

- [ ] **Step 1: Add a red test for the v2 max-blend behavior**

Append to `Tests/EditMode/Controls/SafeAreaTests.cs` (inside the `SafeAreaTests` class, before the closing `}`):

```csharp
        [Test]
        public void Tracker_max_blends_design_margin_with_device_inset()
        {
            // v2: tracker writes anchor=stretch + offsetMin/Max = max(designMargin, inset_designPx) per edge.
            // safe rect (0, 100, 1080, 1820) over screen 1080×1920 → device insets t=0, r=0, b=100, l=0
            // (yMin=100 → bottom inset 100; yMax=1920 → top inset 0; xMin/Max touch screen edges → l/r=0)
            // With scaleFactor=1, design insets = device insets.
            // With design margin top=50, others=0:
            //   final top    = max(50, 0)   = 50
            //   final right  = max(0,  0)   = 0
            //   final bottom = max(0,  100) = 100  ← absorbed
            //   final left   = max(0,  0)   = 0
            // offsetMin = (left, bottom) = (0, 100)
            // offsetMax = (-right, -top) = (0, -50)
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 100f, 1080f, 1820f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1080f, 1920f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride =
                    () => 1f;

                var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
                var rt = (UnityEngine.RectTransform)go.transform;
                // Pre-stage "ApplyCommon-produced" pure-design offsets for margin="50,_,_,_":
                rt.anchorMin = UnityEngine.Vector2.zero;
                rt.anchorMax = UnityEngine.Vector2.one;
                // ApplyCommon convention: offsetMin = (left, bottom), offsetMax = (-right, -top).
                // margin top=50, others=0 → offsetMin=(0,0), offsetMax=(-0, -50).
                rt.offsetMin = new UnityEngine.Vector2(0f, 0f);
                rt.offsetMax = new UnityEngine.Vector2(0f, -50f);

                var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
                tracker.CaptureDesignMargin(rt);
                tracker.Apply();

                Assert.AreEqual(UnityEngine.Vector2.zero, rt.anchorMin, "anchor should be (0,0)/(1,1) stretch");
                Assert.AreEqual(UnityEngine.Vector2.one, rt.anchorMax);
                Assert.AreEqual(0f, rt.offsetMin.x, 0.001f, "left=max(0, 0)");
                Assert.AreEqual(100f, rt.offsetMin.y, 0.001f, "bottom=max(0, 100)=100 (absorbed)");
                Assert.AreEqual(0f, rt.offsetMax.x, 0.001f, "-right=-max(0,0)=0");
                Assert.AreEqual(-50f, rt.offsetMax.y, 0.001f, "-top=-max(50, 0)=-50 (design wins)");

                UnityEngine.Object.DestroyImmediate(go);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }
```

- [ ] **Step 2: Run new test, confirm FAIL (red)**

`mcp__UnityMCP__refresh_unity` → expect compile error (`CaptureDesignMargin` / `ScaleFactorOverride` don't exist yet). That's the red state. Read console:

```
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: errors like `'SafeAreaTracker' does not contain a definition for 'CaptureDesignMargin'` and `'SafeAreaTracker' does not contain a definition for 'ScaleFactorOverride'`.

- [ ] **Step 3: Rewrite SafeAreaTracker.cs**

Overwrite `Runtime/Controls/Internal/SafeAreaTracker.cs` with:

```csharp
using System;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    [DisallowMultipleComponent]
    internal sealed class SafeAreaTracker : MonoBehaviour
    {
        // 仅测试注入。生产代码不应触碰这些字段。
        internal static Func<Rect> SafeAreaOverride;
        internal static Func<Vector2> ScreenSizeOverride;
        // v2: design-px 单位的换算系数；不注入则走真 canvas.scaleFactor。
        internal static Func<float> ScaleFactorOverride;

        private RectTransform _rt;
        private Canvas _canvas;
        private bool _warnedNoCanvas;

        // v2: 由 SafeArea.OnAfterApply (即 ApplyCommon 刚写完纯 design margin 之后) 调用
        // CaptureDesignMargin 抓拍 offsetMin/Max，作为 design margin 的真值来源。
        // Update poll 重新 max-blend 时复用这个抓拍，避免被前一次自己的 blended 输出污染。
        private Vector2 _designOffsetMin;
        private Vector2 _designOffsetMax;
        private bool _hasDesignMargin;

        private Rect _lastSafe;
        private Vector2 _lastScreenSize;
        private float _lastScaleFactor;
        private bool _hasApplied;

        private void OnEnable()
        {
            _rt = transform as RectTransform;
            _canvas = GetComponentInParent<Canvas>();
            Apply();
        }

        private void Update()
        {
            // 跟 Unity 官方 SafeArea 示例对齐：每帧 poll，只在 safeArea / 分辨率 / scaleFactor
            // 真的变了时写。不订阅 OnRectTransformDimensionsChange —— 那条路径会跟
            // ApplyCommon / RectTransform setter 内部反向求解形成写入循环（已观测，
            // 见 SafeAreaTests.Tracker_does_not_subscribe_to_rect_transform_dimensions_change）。
            var safe = ResolveSafeArea();
            var screenSize = ResolveScreenSize();
            var sf = ResolveScaleFactor();

            if (!_hasApplied
                || safe != _lastSafe
                || screenSize != _lastScreenSize
                || !Mathf.Approximately(sf, _lastScaleFactor))
            {
                Apply();
            }
        }

        // v2 入口：SafeArea.OnAfterApply 调用。snapshot 当前 RectTransform 的 offsetMin/Max
        // 作为"纯 design margin"——它们刚由 ApplyCommon 根据 <SafeArea margin="..."> 写入。
        // 之后 Apply 用这个抓拍 + device inset 取 max 写回最终 offsets。
        internal void CaptureDesignMargin(RectTransform rt)
        {
            _designOffsetMin = rt.offsetMin;
            _designOffsetMax = rt.offsetMax;
            _hasDesignMargin = true;
        }

        internal void Apply()
        {
            if (_rt == null) _rt = transform as RectTransform;
            if (_rt == null) return;

            var safe = ResolveSafeArea();
            var screenSize = ResolveScreenSize();
            if (screenSize.x <= 0f || screenSize.y <= 0f) return;
            var sf = ResolveScaleFactor();

            _lastSafe = safe;
            _lastScreenSize = screenSize;
            _lastScaleFactor = sf;
            _hasApplied = true;

            // device-px safe-area → 4 个 inset 距屏幕各边的距离，再除 scaleFactor 拿到 design px
            var insetL = safe.xMin / sf;
            var insetR = (screenSize.x - safe.xMax) / sf;
            var insetB = safe.yMin / sf;
            var insetT = (screenSize.y - safe.yMax) / sf;

            // 设计 margin: ApplyCommon 写出来的 offsetMin/Max 等价于 (l, b) / (-r, -t)。
            // _hasDesignMargin=false 时（OnEnable 先于第一次 OnAfterApply 调 Apply 的同帧）按 0 取，
            // 此时跟 v0"<SafeArea/> 无 margin"行为完全等价：SafeArea 正好 fit safe area。
            var desL = _hasDesignMargin ? _designOffsetMin.x : 0f;
            var desB = _hasDesignMargin ? _designOffsetMin.y : 0f;
            var desR = _hasDesignMargin ? -_designOffsetMax.x : 0f;
            var desT = _hasDesignMargin ? -_designOffsetMax.y : 0f;

            var finL = Mathf.Max(desL, insetL);
            var finR = Mathf.Max(desR, insetR);
            var finB = Mathf.Max(desB, insetB);
            var finT = Mathf.Max(desT, insetT);

            _rt.anchorMin = new Vector2(0f, 0f);
            _rt.anchorMax = new Vector2(1f, 1f);
            _rt.offsetMin = new Vector2(finL, finB);
            _rt.offsetMax = new Vector2(-finR, -finT);
        }

        private Rect ResolveSafeArea() =>
            SafeAreaOverride != null ? SafeAreaOverride() : Screen.safeArea;

        private Vector2 ResolveScreenSize() =>
            ScreenSizeOverride != null
                ? ScreenSizeOverride()
                : new Vector2(Screen.width, Screen.height);

        private float ResolveScaleFactor()
        {
            if (ScaleFactorOverride != null) return ScaleFactorOverride();
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                if (!_warnedNoCanvas)
                {
                    Debug.LogWarning(
                        "[SafeAreaTracker] no Canvas in parent chain; using 1:1 device→design scale " +
                        "(this should only happen in headless tests or detached GameObjects).");
                    _warnedNoCanvas = true;
                }
                return 1f;
            }
            return _canvas.scaleFactor;
        }
    }
}
```

- [ ] **Step 4: Run the new test, confirm GREEN**

`mcp__UnityMCP__refresh_unity` → `read_console` (no errors), then:

```
mcp__UnityMCP__run_tests(mode="EditMode", filter="Tracker_max_blends_design_margin_with_device_inset")
```

Expected: PASS.

- [ ] **Step 5: Update existing tracker tests to v2 representation**

The old tests asserted `anchorMin = safe / screen` fractions; v2 writes anchor=(0,0)/(1,1) + offsets. Visual outcome is the same.

Open `Tests/EditMode/Controls/SafeAreaTests.cs` and replace the four affected tests' bodies. Use a single multi-edit pass (each Edit replaces one block):

5a) Replace `Tracker_applies_safe_area_fractions` body. Keep the test name (just rename to `Tracker_writes_max_blended_offsets_with_no_design_margin` for clarity since the assertion shape changed):

```csharp
        [Test]
        public void Tracker_writes_max_blended_offsets_with_no_design_margin()
        {
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 100f, 1080f, 1820f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1080f, 1920f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride =
                    () => 1f;

                var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
                var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
                tracker.Apply();  // no CaptureDesignMargin call → _hasDesignMargin=false → design margins = 0

                var rt = (UnityEngine.RectTransform)go.transform;
                // v2 representation: anchor = stretch, offsets = device insets (design px since sf=1).
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.anchorMin);
                Assert.AreEqual(UnityEngine.Vector2.one, rt.anchorMax);
                // safe (0, 100, 1080, 1820), screen (1080, 1920) →
                //   insetL=0, insetR=0, insetB=100, insetT=0
                Assert.AreEqual(0f, rt.offsetMin.x, 0.001f);
                Assert.AreEqual(100f, rt.offsetMin.y, 0.001f);
                Assert.AreEqual(0f, rt.offsetMax.x, 0.001f);
                Assert.AreEqual(0f, rt.offsetMax.y, 0.001f);

                UnityEngine.Object.DestroyImmediate(go);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }
```

5b) Replace `Tracker_full_screen_safe_area_yields_identity_anchors` body. Still useful as an edge case (no inset → SafeArea fills parent exactly):

```csharp
        [Test]
        public void Tracker_full_screen_safe_area_yields_zero_offsets()
        {
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 0f, 1080f, 1920f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1080f, 1920f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride =
                    () => 1f;

                var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
                var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
                tracker.Apply();

                var rt = (UnityEngine.RectTransform)go.transform;
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.anchorMin);
                Assert.AreEqual(UnityEngine.Vector2.one, rt.anchorMax);
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.offsetMin);
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.offsetMax);

                UnityEngine.Object.DestroyImmediate(go);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }
```

5c) Replace `SafeArea_anchor_persists_after_ReSolve` body (this one uses `UI.Open`, so it has a real Canvas → no ScaleFactorOverride needed; but assertions need v2 representation):

```csharp
        [Test]
        public void SafeArea_offsets_persist_after_ReSolve()
        {
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 100f, 1080f, 1820f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1080f, 1920f);

                const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa'/>
</Screen></PromptUGUI>";
                UI.LoadDocument("test", xml);
                var screen = UI.Open("S");
                var sa = screen.Get<SafeArea>("sa");

                // ReSolve re-runs ApplyCommon → OnAfterApply → CaptureDesignMargin + tracker.Apply.
                // Result must still encode the inset on the bottom edge (insetB=100, design px),
                // not collapse back to zero offsets.
                screen.ReSolve();

                var rt = sa.RectTransform;
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.anchorMin,
                    "v2 SafeArea anchor is always (0,0)/(1,1) stretch");
                Assert.AreEqual(UnityEngine.Vector2.one, rt.anchorMax);
                // Canvas scaleFactor depends on host project's CanvasScaler config; we assert
                // the bottom offset is positive (inset absorbed) rather than a specific number.
                Assert.Greater(rt.offsetMin.y, 0f,
                    "bottom inset (100 device px) must absorb into offsetMin.y after ReSolve");
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
            }
        }
```

5d) `Tracker_zero_screen_size_is_noop` — still meaningful (tracker bails on zero screen). Update only the ScaleFactorOverride wiring + final assertion (the bail keeps anchor unchanged):

```csharp
        [Test]
        public void Tracker_zero_screen_size_is_noop()
        {
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 0f, 1080f, 1820f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => UnityEngine.Vector2.zero;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride =
                    () => 1f;

                var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
                var rt = (UnityEngine.RectTransform)go.transform;
                rt.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                rt.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);

                var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
                tracker.Apply();

                // Zero screen size → tracker bails; anchors unchanged (still 0.5,0.5).
                Assert.AreEqual(new UnityEngine.Vector2(0.5f, 0.5f), rt.anchorMin);
                Assert.AreEqual(new UnityEngine.Vector2(0.5f, 0.5f), rt.anchorMax);

                UnityEngine.Object.DestroyImmediate(go);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }
```

5e) `SafeArea_parses_and_instantiates`, `SafeArea_attaches_tracker_on_instantiation`, `Tracker_does_not_subscribe_to_rect_transform_dimensions_change` — no changes needed (no anchor-fraction assertions).

- [ ] **Step 6: Add the additional max-blend matrix tests**

Append these to `Tests/EditMode/Controls/SafeAreaTests.cs` (still in the `SafeAreaTests` class). They mirror spec §6.1 cases not yet covered:

```csharp
        // Helper: pre-stage RectTransform offsets as if ApplyCommon wrote them for a given margin,
        // then capture + apply. Saves repetition in the parametric cases below.
        private static (Vector2 offsetMin, Vector2 offsetMax) RunTrackerWith(
            UnityEngine.Rect safe, UnityEngine.Vector2 screen, float scaleFactor,
            float marginTop, float marginRight, float marginBottom, float marginLeft)
        {
            PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = () => safe;
            PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = () => screen;
            PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = () => scaleFactor;

            var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
            var rt = (UnityEngine.RectTransform)go.transform;
            rt.anchorMin = UnityEngine.Vector2.zero;
            rt.anchorMax = UnityEngine.Vector2.one;
            // ApplyCommon convention: offsetMin = (l, b), offsetMax = (-r, -t).
            rt.offsetMin = new UnityEngine.Vector2(marginLeft, marginBottom);
            rt.offsetMax = new UnityEngine.Vector2(-marginRight, -marginTop);

            var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
            tracker.CaptureDesignMargin(rt);
            tracker.Apply();

            var result = (rt.offsetMin, rt.offsetMax);
            UnityEngine.Object.DestroyImmediate(go);
            return result;
        }

        [Test]
        public void Tracker_PC_with_margin_6_writes_margin_directly()
        {
            try
            {
                // No inset (full screen safe). margin all 6 → final offsets all 6.
                var (oMin, oMax) = RunTrackerWith(
                    safe: new UnityEngine.Rect(0f, 0f, 1920f, 1080f),
                    screen: new UnityEngine.Vector2(1920f, 1080f),
                    scaleFactor: 1f,
                    marginTop: 6f, marginRight: 6f, marginBottom: 6f, marginLeft: 6f);
                Assert.AreEqual(new UnityEngine.Vector2(6f, 6f), oMin);
                Assert.AreEqual(new UnityEngine.Vector2(-6f, -6f), oMax);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }

        [Test]
        public void Tracker_iPhone_with_margin_6_inset_absorbs_top_and_bottom()
        {
            try
            {
                // iPhone-like: top inset 134, bottom inset 132, l/r 0.
                // margin 6 on each edge:
                //   t = max(6, 134) = 134 (inset wins)
                //   r = max(6,   0) = 6
                //   b = max(6, 132) = 132 (inset wins)
                //   l = max(6,   0) = 6
                var (oMin, oMax) = RunTrackerWith(
                    safe: new UnityEngine.Rect(0f, 132f, 1170f, 2266f),  // yMin=132, yMax=2398
                    screen: new UnityEngine.Vector2(1170f, 2532f),
                    scaleFactor: 1f,
                    marginTop: 6f, marginRight: 6f, marginBottom: 6f, marginLeft: 6f);
                Assert.AreEqual(new UnityEngine.Vector2(6f, 132f), oMin);
                Assert.AreEqual(new UnityEngine.Vector2(-6f, -134f), oMax);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }

        [Test]
        public void Tracker_design_top_200_beats_inset_134()
        {
            try
            {
                // Same screen as above; only top design margin is large (200).
                //   t = max(200, 134) = 200 (design wins)
                //   r = max(0,   0)   = 0
                //   b = max(0,   132) = 132 (inset)
                //   l = max(0,   0)   = 0
                var (oMin, oMax) = RunTrackerWith(
                    safe: new UnityEngine.Rect(0f, 132f, 1170f, 2266f),
                    screen: new UnityEngine.Vector2(1170f, 2532f),
                    scaleFactor: 1f,
                    marginTop: 200f, marginRight: 0f, marginBottom: 0f, marginLeft: 0f);
                Assert.AreEqual(new UnityEngine.Vector2(0f, 132f), oMin);
                Assert.AreEqual(new UnityEngine.Vector2(0f, -200f), oMax);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }

        [Test]
        public void Tracker_HiDPI_converts_device_inset_to_design_px_via_scaleFactor()
        {
            try
            {
                // Device 1170×2532, safe (l=0, r=0, bottomDev=68, topDev=134), scaleFactor=2
                //   design insets: t=67, r=0, b=34, l=0
                // margin top=6 → final t = max(6, 67) = 67
                var (oMin, oMax) = RunTrackerWith(
                    safe: new UnityEngine.Rect(0f, 68f, 1170f, 2330f),   // yMin=68, yMax=2398
                    screen: new UnityEngine.Vector2(1170f, 2532f),
                    scaleFactor: 2f,
                    marginTop: 6f, marginRight: 0f, marginBottom: 0f, marginLeft: 0f);
                Assert.AreEqual(0f, oMin.x, 0.001f);
                Assert.AreEqual(34f, oMin.y, 0.001f, "bottom design inset = 68/2");
                Assert.AreEqual(0f, oMax.x, 0.001f);
                Assert.AreEqual(-67f, oMax.y, 0.001f, "top final = max(6, 134/2) = 67");
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }
```

- [ ] **Step 7: Run all SafeArea EditMode tests, confirm GREEN**

`mcp__UnityMCP__refresh_unity` → `read_console` (no compile errors). Then:

```
mcp__UnityMCP__run_tests(mode="EditMode", filter="SafeArea")
```

Expected: all SafeArea tests pass — parser (9) + control (8 old, rewritten/extended) + 4 new max-blend matrix tests + the initial Tracker_max_blends_design_margin_with_device_inset test = ~22 tests green.

If any fail, fix in place before commit. Common issue: forgetting the `ScaleFactorOverride = null` cleanup in `finally` → next test sees stale override → cascading failures. Verify each test's `finally` block.

- [ ] **Step 8: Commit**

```bash
git add Runtime/Controls/Internal/SafeAreaTracker.cs Tests/EditMode/Controls/SafeAreaTests.cs
git commit -m "$(cat <<'EOF'
feat(safearea): tracker writes max-blended offsets for v2 absorb semantics

SafeAreaTracker now:
- Stores design margin via CaptureDesignMargin(RectTransform) (snapshot
  ApplyCommon's offsetMin/Max output).
- Apply() writes anchor=(0,0)/(1,1) + offsetMin/Max = per-edge
  max(designMargin, deviceInset_designPx). Device inset is computed from
  Screen.safeArea, converted to design px via Canvas.scaleFactor (or
  ScaleFactorOverride for headless tests; falls back to 1:1 +
  log-warn-once when no Canvas in parent chain).
- Update() poll-comparator extended to include scaleFactor changes
  (rotation can shift CanvasScaler.referenceResolution).

Per spec 2026-05-26-safearea-margin-absorb-v2-design §3.3, MA2-D2/D3/D4.

EditMode tracker tests rewritten to assert the new offset-based
representation (visual outcome identical to v0 for <SafeArea/> without
margin). Adds matrix tests covering PC + iPhone + design-wins-over-inset
+ HiDPI scaleFactor conversion.

SafeArea.OnAfterApply wiring lands in the next commit; this commit
leaves <SafeArea margin> behaving like a no-op margin (tracker still
sees _hasDesignMargin=false because no one calls CaptureDesignMargin).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: SafeArea.OnAfterApply wires CaptureDesignMargin

**Files:**
- Modify: `Runtime/Controls/SafeArea.cs` (call `CaptureDesignMargin` before `Apply` in `OnAfterApply`)
- Modify: `Tests/EditMode/Controls/SafeAreaTests.cs` (add integration test exercising the full Apply chain)

After Task 3, `<SafeArea margin>` is parsed but the margin doesn't reach the tracker — `_hasDesignMargin` stays false. Task 4 wires it up.

- [ ] **Step 1: Write red integration test**

Append to `Tests/EditMode/Controls/SafeAreaTests.cs`:

```csharp
        [Test]
        public void SafeArea_with_margin_attribute_absorbs_device_inset()
        {
            // End-to-end: <SafeArea margin> goes through ApplyCommon, OnAfterApply captures
            // the margin via CaptureDesignMargin, tracker.Apply max-blends with device inset.
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 100f, 1080f, 1820f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1080f, 1920f);

                const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa' margin='6,6,6,6'/>
</Screen></PromptUGUI>";
                UI.LoadDocument("test", xml);
                var screen = UI.Open("S");
                var sa = screen.Get<SafeArea>("sa");

                var rt = sa.RectTransform;
                // Anchor must always be (0,0)/(1,1) in v2.
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.anchorMin);
                Assert.AreEqual(UnityEngine.Vector2.one, rt.anchorMax);
                // safe (0, 100, 1080, 1820), screen (1080, 1920) → device insets (l=0, r=0, b=100, t=0).
                // Canvas scaleFactor depends on host CanvasScaler config; assert qualitatively:
                //   - left/right: max(6, 0) = 6 (design margin in design px) regardless of scaleFactor
                //   - bottom: at least max(6, 100/sf). With sf=1 → 100; with sf=2 → 50. Either way ≥ 6.
                //   - top: max(6, 0/sf) = 6
                Assert.AreEqual(6f, rt.offsetMin.x, 0.001f,
                    "left = max(designLeft=6, deviceL=0) = 6");
                Assert.AreEqual(-6f, rt.offsetMax.x, 0.001f,
                    "right encoded as -6 (margin design value)");
                Assert.AreEqual(-6f, rt.offsetMax.y, 0.001f,
                    "top = max(6, 0/sf) = 6 → offsetMax.y = -6");
                Assert.GreaterOrEqual(rt.offsetMin.y, 6f,
                    "bottom = max(6, 100/sf) ≥ 6; inset absorbs the 6 when sf yields ≥ 6 design px");
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
            }
        }
```

- [ ] **Step 2: Run, confirm FAIL (red)**

```
mcp__UnityMCP__refresh_unity → read_console (no errors)
mcp__UnityMCP__run_tests(mode="EditMode", filter="SafeArea_with_margin_attribute_absorbs_device_inset")
```

Expected: FAIL — the assertion on `offsetMin.x` (left = 6) likely fails, because OnAfterApply isn't capturing the design margin yet. The actual value will be the inset (0) since tracker treats design as 0.

- [ ] **Step 3: Update SafeArea.OnAfterApply**

Edit `Runtime/Controls/SafeArea.cs`. Replace the file body with:

```csharp
using PromptUGUI.Controls.Internal;

namespace PromptUGUI.Controls
{
    public sealed class SafeArea : Control
    {
        private SafeAreaTracker _tracker;

        public override void OnAttached()
        {
            _tracker = GameObject.AddComponent<SafeAreaTracker>();
        }

        internal override void OnAfterApply()
        {
            // ApplyCommon 在初次实例化和 Variant ReSolve 时都会把 anchor 写回 stretch、
            // 把 offsetMin/Max 写成纯 design margin。tracker 在这里 snapshot 设计 margin，
            // 再用 device safe-area inset 做 per-edge max-blend 写回 offsets。
            if (_tracker == null) return;
            _tracker.CaptureDesignMargin(RectTransform);
            _tracker.Apply();
        }
    }
}
```

- [ ] **Step 4: Run, confirm GREEN**

```
mcp__UnityMCP__refresh_unity → read_console (no errors)
mcp__UnityMCP__run_tests(mode="EditMode", filter="SafeArea")
```

Expected: all SafeArea EditMode tests pass, including the new integration test.

- [ ] **Step 5: Add a Variant margin override test**

Append to `Tests/EditMode/Controls/SafeAreaTests.cs` (still in the `SafeAreaTests` class):

```csharp
        [Test]
        public void SafeArea_margin_variant_override_re_blends_on_variant_switch()
        {
            try
            {
                // PC-like: no device inset, so design margin wins on every edge.
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 0f, 1920f, 1080f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1920f, 1080f);

                // Variant `wide` is auto-declared by being referenced via `margin.wide=...`;
                // no explicit `<Variants>` declaration block needed (see UIDocumentParserTests
                // and BtnContentSizingTests for prior art).
                const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa' margin='6' margin.wide='20'/>
</Screen></PromptUGUI>";
                UI.LoadDocument("test", xml);
                var screen = UI.Open("S");
                var sa = screen.Get<SafeArea>("sa");

                var rt = sa.RectTransform;
                Assert.AreEqual(6f, rt.offsetMin.x, 0.001f, "base margin=6 → left=6");
                Assert.AreEqual(-6f, rt.offsetMax.x, 0.001f, "base margin=6 → right=-6");

                // Switch variant: ApplyCommon re-runs with margin=20, OnAfterApply re-captures,
                // tracker.Apply re-blends. Inset still 0, so design margin still wins.
                // `screen.Variants.Set` matches the SafeArea PlayMode variant test pattern.
                screen.Variants.Set("wide", true);

                Assert.AreEqual(20f, rt.offsetMin.x, 0.001f, "variant margin=20 → left=20");
                Assert.AreEqual(-20f, rt.offsetMax.x, 0.001f, "variant margin=20 → right=-20");
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
            }
        }
```

Run and confirm GREEN:

```
mcp__UnityMCP__refresh_unity → read_console
mcp__UnityMCP__run_tests(mode="EditMode", filter="SafeArea_margin_variant_override_re_blends_on_variant_switch")
```

Expected: PASS. If this fails with "variant 'wide' not declared" or similar, check the host project's `<Variants>` declaration syntax (some Screens declare variants inline via `<Variants><Variant name="..."/></Variants>` — the above XML follows that pattern; verify against `Runtime/Resources/PromptUGUI/` examples if needed).

- [ ] **Step 6: Run full EditMode test suite as a regression check**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

Expected: all green. If anything other than SafeArea fails, STOP — that's a regression worth investigating (likely a test that uses `<SafeArea>` and asserts the old anchor-fraction representation indirectly). Fix in place before continuing.

- [ ] **Step 7: Commit**

```bash
git add Runtime/Controls/SafeArea.cs Tests/EditMode/Controls/SafeAreaTests.cs
git commit -m "$(cat <<'EOF'
feat(safearea): OnAfterApply captures design margin before tracker.Apply

SafeArea.OnAfterApply now calls _tracker.CaptureDesignMargin(rt) before
_tracker.Apply(). Snapshot happens immediately after ApplyCommon wrote
the pure design margin offsets, so tracker.Apply blends with the device
inset using the right reference values.

This completes the v2 absorb wiring: <SafeArea margin="6,6,6,6"> in XML
now ends up as per-edge max(6, deviceInset_designPx) offsets at runtime.

Closes the loop opened by 2026-05-26-safearea-margin-absorb-v2-design §3.2.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: PlayMode tests update

**Files:**
- Modify: `Tests/PlayMode/Controls/SafeAreaTests.cs` (rewrite anchor-fraction assertions; add one margin-absorption test)

PlayMode tests run against a real Canvas, so no `ScaleFactorOverride` injection. Canvas scaleFactor in PlayMode tests defaults to 1 unless the host Unity project's `<Screen>` config (CanvasScaler) overrides it — the existing tests don't set reference resolution, so sf=1 in default configuration.

- [ ] **Step 1: Rewrite `SafeArea_anchor_settles_after_one_frame`**

Edit `Tests/PlayMode/Controls/SafeAreaTests.cs`, replace the `SafeArea_anchor_settles_after_one_frame` body:

```csharp
        [UnityTest]
        public IEnumerator SafeArea_offsets_settle_after_one_frame()
        {
            SafeAreaTracker.SafeAreaOverride =
                () => new Rect(0f, 100f, 1080f, 1820f);
            SafeAreaTracker.ScreenSizeOverride =
                () => new Vector2(1080f, 1920f);

            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var sa = screen.Get<SafeArea>("sa");
            yield return null;

            var rt = sa.RectTransform;
            // v2: anchor always stretch, offsets carry the per-edge inset.
            // safe (0, 100, 1080, 1820), screen (1080, 1920), default sf=1:
            //   insetB = 100, others 0.
            Assert.AreEqual(Vector2.zero, rt.anchorMin);
            Assert.AreEqual(Vector2.one, rt.anchorMax);
            Assert.AreEqual(0f, rt.offsetMin.x, 0.001f);
            Assert.AreEqual(100f, rt.offsetMin.y, 0.001f);
            Assert.AreEqual(0f, rt.offsetMax.x, 0.001f);
            Assert.AreEqual(0f, rt.offsetMax.y, 0.001f);
        }
```

- [ ] **Step 2: Rewrite `Tracker_polls_provider_changes`**

Replace its body:

```csharp
        [UnityTest]
        public IEnumerator Tracker_polls_provider_changes()
        {
            SafeAreaTracker.SafeAreaOverride =
                () => new Rect(0f, 100f, 1080f, 1820f);
            SafeAreaTracker.ScreenSizeOverride =
                () => new Vector2(1080f, 1920f);

            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var sa = screen.Get<SafeArea>("sa");
            var rt = sa.RectTransform;
            yield return null;

            Assert.AreEqual(100f, rt.offsetMin.y, 0.001f,
                "initial bottom inset = 100");

            // Notch switches sides: gesture bar lives at the top now.
            //   safe (0, 0, 1080, 1830) → device top inset = 1920-1830 = 90
            //   bottom inset = 0
            SafeAreaTracker.SafeAreaOverride =
                () => new Rect(0f, 0f, 1080f, 1830f);
            yield return null;

            Assert.AreEqual(0f, rt.offsetMin.y, 0.001f, "new bottom inset = 0");
            Assert.AreEqual(-90f, rt.offsetMax.y, 0.001f, "new top inset = 90 → offsetMax.y = -90");
        }
```

- [ ] **Step 3: Rewrite `SafeArea_inside_variant_add_block_works_after_toggle`**

Replace its body:

```csharp
        [UnityTest]
        public IEnumerator SafeArea_inside_variant_add_block_works_after_toggle()
        {
            SafeAreaTracker.SafeAreaOverride =
                () => new Rect(0f, 100f, 1080f, 1820f);
            SafeAreaTracker.ScreenSizeOverride =
                () => new Vector2(1080f, 1920f);

            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Variant when='mobile'>
    <Add into='@root'>
      <SafeArea id='sa'/>
    </Add>
  </Variant>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            screen.Variants.Set("mobile", true);
            yield return null;

            var sa = screen.Get<SafeArea>("sa");
            Assert.IsNotNull(sa);
            Assert.AreEqual(100f, sa.RectTransform.offsetMin.y, 0.001f);

            screen.Variants.Set("mobile", false);
            yield return null;
            Assert.IsFalse(sa.GameObject.activeSelf, "Add block goes inactive");

            screen.Variants.Set("mobile", true);
            yield return null;
            Assert.IsTrue(sa.GameObject.activeSelf);
            Assert.AreEqual(100f, sa.RectTransform.offsetMin.y, 0.001f,
                "tracker re-applies after reactivation via OnEnable + OnAfterApply");
        }
```

- [ ] **Step 4: Add a new margin-absorption end-to-end test**

Append to the class (before the closing `}`):

```csharp
        [UnityTest]
        public IEnumerator SafeArea_with_margin_absorbs_inset_end_to_end()
        {
            SafeAreaTracker.SafeAreaOverride =
                () => new Rect(0f, 100f, 1080f, 1820f);
            SafeAreaTracker.ScreenSizeOverride =
                () => new Vector2(1080f, 1920f);

            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa' margin='6,6,6,6'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var sa = screen.Get<SafeArea>("sa");
            yield return null;

            var rt = sa.RectTransform;
            //   left  = max(6, 0)   = 6
            //   right = max(6, 0)   = 6 (encoded as -6 in offsetMax.x)
            //   bottom= max(6, 100) = 100 (inset absorbs)
            //   top   = max(6, 0)   = 6 (encoded as -6 in offsetMax.y)
            Assert.AreEqual(6f, rt.offsetMin.x, 0.001f);
            Assert.AreEqual(100f, rt.offsetMin.y, 0.001f);
            Assert.AreEqual(-6f, rt.offsetMax.x, 0.001f);
            Assert.AreEqual(-6f, rt.offsetMax.y, 0.001f);
        }
```

- [ ] **Step 5: Run PlayMode tests, confirm GREEN**

```
mcp__UnityMCP__refresh_unity → read_console
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="SafeArea")
```

Expected: 4 SafeArea PlayMode tests pass (3 rewritten + 1 new).

- [ ] **Step 6: Full PlayMode regression**

```
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

Expected: all green. If something else fails, look for indirect dependencies on SafeArea representation.

- [ ] **Step 7: Commit**

```bash
git add Tests/PlayMode/Controls/SafeAreaTests.cs
git commit -m "$(cat <<'EOF'
test(safearea): PlayMode tests assert v2 offset representation + margin absorb

Three existing tests reworked to assert anchor=(0,0)/(1,1) +
per-edge offsets instead of anchor-fraction representation (visual
behavior unchanged for <SafeArea/>; representation change is internal).
Adds SafeArea_with_margin_absorbs_inset_end_to_end covering the
new <SafeArea margin> attribute through the full XML → Screen.Open path.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: SKILL.md update

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md` (rewrite "Safe area" section + update tag table at line 85)

- [ ] **Step 1: Update the tag table at line 85**

Edit `.claude/skills/authoring-promptugui-xml/SKILL.md`. Find the row that begins with:

```
| `<SafeArea>`   | Stretches to `Screen.safeArea` (notch / status bar / home indicator). Auto-reacts to rotation, window resize, Device Simulator. **Rejects** `anchor` / `size` / `width` / `height` / `margin` / `pivot` (incl. `.variant`); see "Safe area" section below.
```

Replace it with:

```
| `<SafeArea>`   | Stretches to its parent; per-edge inset = `max(designMargin, Screen.safeArea_i)`. Auto-reacts to rotation, window resize, Device Simulator, Dynamic Island. Accepts `margin` (absorbed by device inset); **rejects** `anchor` / `size` / `width` / `height` / `pivot` (incl. `.variant`); see "Safe area" section below.
```

- [ ] **Step 2: Rewrite the "Safe area" section (around line 150)**

Replace the entire "Safe area" section (currently lines 150-171) with:

````markdown
### Safe area

Wrap UI in `<SafeArea>` and put a `margin` on it to control inset. Per-edge `inset = max(designMargin_i, Screen.safeArea_i)` — the safe-area inset absorbs the design margin (not adds to it), so the same XML looks right on PC and on notched devices:

```xml
<Screen name="Lobby">
  <Image anchor="stretch" color="#08152C"/>           <!-- bleed background, sibling of SafeArea -->
  <SafeArea margin="6,6,6,6">
    <HStack id="topIcons" anchor="top-stretch" height="24"
            margin="0,0,_,_" spacing="4" childAlign="middle-right">...</HStack>
  </SafeArea>
</Screen>
```

- PC (no inset): you get exactly the `margin` you wrote (here, 6px on each edge).
- Notched device: the safe-area inset wins where it's bigger than your margin. E.g. iPhone 14 Pro (top inset ≈ 134, bottom ≈ 132 device px, sf=1): top=134, right=6, bottom=132, left=6.
- Design margin wins past the inset: `<SafeArea margin="200,_,_,_">` on the same device gives top=200 (your design value is bigger than 134).
- Unspecified edges (`_` or shorter than 4 components) default to 0 → that edge fully absorbs the device inset.

Other notes:

- `<SafeArea>` still rejects `anchor` / `size` / `width` / `height` / `pivot` (and their `.variant` forms). The container is always stretched to its parent; only `margin` is author-controlled.
- One `<SafeArea>` per `<Screen>`. Backgrounds that need to bleed past the safe area stay as siblings of `<SafeArea>`, not children.
- For "fixed gap below the safe area" (e.g. always 16px below the notch, never flush), nest a `<Frame anchor="stretch" margin="16,_,_,_"/>` inside the `<SafeArea>` instead of using the SafeArea's own margin.
- Don't put `<SafeArea>` inside `<VStack>` / `<HStack>` / `<Grid>` — the layout group will override its anchor math.
- Reacts automatically to screen rotation, window resize, Unity 6's Device Simulator, and Dynamic Island animations. No code-side wiring needed.
````

- [ ] **Step 3: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "$(cat <<'EOF'
docs(skill): SKILL.md Safe area section reflects v2 margin-absorb semantics

Rewrites the Safe area section in authoring-promptugui-xml/SKILL.md
for v2 (<SafeArea margin> absorbs device inset, per-edge max). Updates
the tag table entry to mention `margin` as accepted, and lists the
remaining deny set explicitly.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Master spec §5.5 update

**Files:**
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` (§5.5 around line 208)

- [ ] **Step 1: Rewrite §5.5**

Edit `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`. Replace lines 208-215 (the entire §5.5 block) with:

```markdown
### 5.5 `<SafeArea>`（安全区容器）

显式安全区包裹层；运行时每条边 `inset = max(designMargin_i, Screen.safeArea_i)`（max-blend），自动响应屏幕旋转 / 窗口缩放 / Device Simulator / Variant ReSolve / Dynamic Island。完整设计见 [`2026-05-26-safearea-margin-absorb-v2-design.md`](2026-05-26-safearea-margin-absorb-v2-design.md)。

简表：
- 接受 `margin`：表示"距父级边至少这么多 design px"，会被 device safe-area inset 取大吸收。`<SafeArea/>`（无 margin）= SafeArea 正好 fit safe area。
- 不接受 `anchor` / `size` / `width` / `height` / `pivot`（含 `.variant` 覆盖）—— 形状固定为 stretch；写这些属性会在 parse 期抛 `ParseException`。
- 允许 `id` / `hidden` / `interactable` / `if=` / `margin` / `margin.variant`。
- 典型用法：作为 `<Screen>` 直接子节点，UI 全放它里面；需要 bleed 到屏幕物理边缘的背景图作为 SafeArea 的兄弟节点。
- 想要"safe area + 固定 padding"叠加（e.g. 16px below the notch, never flush），在 SafeArea 内嵌套 `<Frame anchor="stretch" margin="16,_,_,_"/>`。
```

- [ ] **Step 2: Commit**

```bash
git add docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md
git commit -m "$(cat <<'EOF'
docs(spec): master spec §5.5 expanded with v2 max-blend semantics

Drops the forward-reference placeholder ("v2 semantics") and inlines the
key facts: per-edge inset = max(margin, Screen.safeArea), accepted vs
rejected attribute set, and the fixed-gap escape hatch (nested Frame).
Points at 2026-05-26-safearea-margin-absorb-v2-design.md for full design.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Manual Device Simulator verification

**Files:** none — manual verification step.

This is a step the engineer runs by hand in the host Unity project. UnityMCP run_tests covers the math; the simulator verifies "looks right".

- [ ] **Step 1: Open host Unity project at `C:\xsoft\PromptUGUIDev`**

If the editor is closed, open it. If MCP is connected, reuse the active instance (`mcp://instances`); otherwise start the editor by hand.

- [ ] **Step 2: Open the Lobby screen / a screen with `<SafeArea>` in it**

In the host project, find or create a scene that opens a Screen that uses `<SafeArea margin="6,6,6,6">`. The author's `Lobby` screen mentioned in the brainstorming conversation is the primary target.

(Note: the user said they'd manually migrate the Lobby `.ui.xml` to use `<SafeArea margin>`. If that migration hasn't happened yet, this step is "expected to look the same as v0" because `<SafeArea>` without margin behaves visually identical to v0.)

- [ ] **Step 3: Open Device Simulator (Window > General > Device Simulator)**

Switch the device to **iPhone 14 Pro** (or any device with a notch).

- [ ] **Step 4: Verify visually**

Expected:
- SafeArea content sits clear of the notch / Dynamic Island at the top.
- SafeArea content sits clear of the home indicator at the bottom.
- If the Lobby is migrated to use `<SafeArea margin="6">`, you should see exactly 6px gap on the left/right edges (non-notched edges); top/bottom hug the safe-area boundary.

- [ ] **Step 5: Toggle orientation / device**

Switch between portrait, landscape, iPad, iPhone SE (no notch), Pixel 8 (punch-hole).
Expected: SafeArea adjusts each time within a frame or two — no stuck old inset, no flicker.

- [ ] **Step 6: Report findings**

If the visual result matches, report success. If something looks wrong (e.g. SafeArea offset feels off by canvas.scaleFactor ratio, or doesn't update on rotation), note exact device + observed offset and STOP. Likely cause: scaleFactor handling or polling. Don't try to patch in this step — surface the finding for analysis.

---

## Self-Review Checklist (performed by plan author)

This is a scratch checklist; the engineer doesn't need to redo it.

**Spec coverage:**
- §1 Background / goals → motivated by Task 1's user message; covered by Tasks 2-4.
- §2 Decisions MA2-D1 → D16 → Task 2 (D7 parser), Task 3 (D2/D3/D4/D5/D6/D11/D13), Task 4 (D3 wiring), Tasks 6-7 (D14/D15).
- §3 Change set → exact mapping in `File Structure` section above.
- §4 Trigger / re-compute flow → covered by Tasks 3 (Update poll, Variant ReSolve via OnAfterApply chain) and 4 (initial Apply).
- §5 Public API → Task 3 (CaptureDesignMargin internal, ScaleFactorOverride internal static) + Task 2 (margin attribute accepted).
- §6 Test matrix → Tasks 3 (matrix EditMode), 4 (integration), 5 (PlayMode).
- §7 SKILL.md → Task 6.
- §8 Master spec §5.5 → Task 7.
- §9 Risks → noted; no specific tasks (informational).
- §10 Implementation order → Tasks 1-7 follow §10 1→10 closely.
- §11 Open questions → resolved at plan time: ScaleFactorOverride yes (added in Task 3), XsdGenerator unchanged (not in deny list anyway), _designOffsetMin/Max not cleared on deactivate (intentional), no Mathf.Round.

**Placeholder scan:** None left. Every step has runnable commands and complete code blocks.

**Type consistency:**
- `CaptureDesignMargin(RectTransform)` consistent across SafeAreaTracker.cs, SafeArea.cs, tests.
- `ScaleFactorOverride : Func<float>` consistent.
- `_designOffsetMin/Max : Vector2` consistent.
- All commit messages reference the v2 spec dated 2026-05-26.

---

**Final post-implementation hygiene** (not a task; reminder):
- After Task 7's commit, run `dotnet format --verify-no-changes --severity warn .lint/PromptUGUI.Lint.slnx` to confirm no whitespace / style regressions.
- Run `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` if any `.ui.xml` was touched in the engineer's migration step (the user said they'd migrate Lobby manually; that file isn't in this repo, so this lint is a no-op here).
