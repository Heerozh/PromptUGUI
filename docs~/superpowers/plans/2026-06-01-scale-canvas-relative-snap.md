# `scale="<r>r"` 画布相对像素吸附缩放 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 `scale` 属性新增取值形态 `<r>r`（r 正浮点），运行期解析为 `localScale = max(1, round(canvasFactor × r)) / canvasFactor`——随 canvas factor 响应窗口、净尺寸吸附整数保持像素对齐。

**Architecture:** 复用已合并的 device-density（`Nx`）全部基础设施：同一个 `_canvasFactor` 缓存、同一条 `OnCanvasDimensionsChanged → ReSolve` 重算路径。新增一个 `TryParseRelativeScale` helper 与 `ApplyScales` 分支；把原本只认 `Nx` 的 resize 门控（`_hasDeviceScale`）泛化为"含 factor 依赖型 scale"（`_hasFactorScale`），让 `<r>r` 也触发 resize 重算。parser 加 `R` 后缀校验。无 C# 公开 API 变化。

**Tech Stack:** C# (Unity 6, `Awaitable`/uGUI), NUnit EditMode tests via Unity MCP, `dotnet format`（`.lint/`）。

**规范来源:** `docs~/superpowers/specs/2026-06-01-scale-canvas-relative-snap-design.md`（决策编号 CRS-D1…D18）。

---

## 文件结构

| 文件 | 职责 | 改动 |
|---|---|---|
| `Runtime/Core/Parser/UIDocumentParser.cs` | XML→IR 解析 + 属性校验 | 改 `ValidateScale`：加 `R` 后缀分支 + 错误信息补 `0.5r` 示例 |
| `Runtime/Application/Screen.cs` | 运行期 layout / scale 应用 / resize 重算 | 加 `TryParseRelativeScale` helper；`ApplyScales` 加 `R` 分支；门控 `_hasDeviceScale`→`_hasFactorScale` 泛化 + 检测扩到 `R`；更新注释 |
| `Tests/EditMode/Application/ScaleAttributeTests.cs` | EditMode 测试 | 加 parser / runtime / box-preserving / resize / 门控 用例 |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` | XML 作者文档 | `scale` 表行 + 新增 "Canvas-relative snapped" 子节 + 三形态对照 + caveat |
| `docs~/superpowers/specs/2026-05-07-...-design.md` | master spec | §6 `scale` 行旁追加 `<r>r` 一行 + 引用 |
| （核实）XSD generator | `scale` 是否被 XSD 约束 | 大概率 anyAttribute → 无需改；Task 4 核实 |

**测试执行方式（Unity MCP）：** 每次改 C# 后先

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

确认无编译错误，再

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScaleAttributeTests")
```

（先加载工具：`ToolSearch(query="select:refresh_unity,read_console,run_tests", max_results=3)`。MCP 不可用时尝试重连或告知用户重启 MCP。）

---

## Task 1: Parser — `<r>r` 校验

**Files:**
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs`（`ValidateScale`，约 line 527-555）
- Test: `Tests/EditMode/Application/ScaleAttributeTests.cs`

- [ ] **Step 1: 写失败的 parser 测试**

在 `ScaleAttributeTests.cs` 的 device-density parser 段（约 line 205 `Parser_rejects_uppercase_device_scale` 之后）插入：

```csharp
        // ---------- Parser validation: canvas-relative '<r>r' ----------

        [Test]
        public void Parser_accepts_relative_scale_half()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='0.5r'/></Screen>
</PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Parser_accepts_relative_scale_fractional_and_integer_prefix()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame id='a' scale='0.25r'/><Frame id='b' scale='1.5r'/>
    <Frame id='c' scale='1r'/><Frame id='d' scale='2r'/>
  </Screen>
</PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Parser_accepts_relative_scale_variant()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='1' scale.portrait='0.5r'/></Screen>
</PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Parser_rejects_zero_relative_scale()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='0r'/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("canvas-relative", ex.Message);
        }

        [Test]
        public void Parser_rejects_negative_relative_scale()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='-0.5r'/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("canvas-relative", ex.Message);
        }

        [Test]
        public void Parser_rejects_bare_r()
        {
            // 'r' length<2 → not the r branch → falls to float check → still errors (msg contains 'scale').
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='r'/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale", ex.Message);
        }

        [Test]
        public void Parser_rejects_uppercase_relative_scale()
        {
            // Canvas-relative suffix is lowercase 'r' only (CRS-D10), matching device-density's
            // lowercase 'x'. '0.5R' falls through to the float check and is rejected.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='0.5R'/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale", ex.Message);
        }
```

- [ ] **Step 2: 跑测试确认失败**

```
ToolSearch(query="select:refresh_unity,read_console,run_tests", max_results=3)
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScaleAttributeTests")
```

Expected: `Parser_accepts_relative_scale_*` 失败（`0.5r` 当前走 float 校验 → `float.TryParse("0.5r")` fail → ParseException）；`Parser_rejects_zero/negative_relative_scale` 失败（断言找 `canvas-relative` 子串，但当前 fallback msg 无此词）。

- [ ] **Step 3: 实现 `ValidateScale` 的 `R` 分支**

在 `Runtime/Core/Parser/UIDocumentParser.cs` 的 `ValidateScale` 里，把 `x` 分支（`if (raw.Length >= 2 && raw[raw.Length - 1] == 'x')` ... 那段）之后、最后的 float fallback 之前，插入 `R` 分支，并把两处 fallback 错误信息补上 `0.5r` 示例。改完后整个方法体为：

```csharp
        private static void ValidateScale(string raw, string contextLabel)
        {
            // scale="N" sets RectTransform.localScale = N (relative to layout box; works in
            // any scale-mode). Must be a positive float. N=1 is the no-op identity.
            // scale="Nx" (N positive integer) is the device-density form: localScale =
            // N / canvasFactor at runtime — locks the element to N physical pixels per
            // design-unit. See 2026-05-31-scale-device-density-design.md.
            // scale="<r>r" (r positive float) is the canvas-relative snapped form: localScale =
            // round(canvasFactor × r) / canvasFactor at runtime — scales with the factor but the
            // net physical-px/unit snaps to the nearest integer, keeping pixel alignment while
            // still responding to window size. See 2026-06-01-scale-canvas-relative-snap-design.md.
            if (string.IsNullOrEmpty(raw))
                throw new ParseException(
                    $"{contextLabel}: value cannot be empty " +
                    $"(expected a positive number like '0.5', a device-density like '2x', " +
                    $"or a canvas-relative scale like '0.5r')");

            if (raw.Length >= 2 && raw[raw.Length - 1] == 'x')
            {
                var num = raw.Substring(0, raw.Length - 1);
                if (int.TryParse(num, System.Globalization.NumberStyles.None,
                                 System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 1)
                    return;
                throw new ParseException(
                    $"{contextLabel}: invalid device-density '{raw}' " +
                    $"(expected a positive integer before 'x', e.g. '1x' or '2x')");
            }

            if (raw.Length >= 2 && raw[raw.Length - 1] == 'r')
            {
                var num = raw.Substring(0, raw.Length - 1);
                if (float.TryParse(num, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out var rr) && rr > 0f)
                    return;
                throw new ParseException(
                    $"{contextLabel}: invalid canvas-relative scale '{raw}' " +
                    $"(expected a positive number before lowercase 'r', e.g. '0.5r' or '0.25r')");
            }

            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var v) || v <= 0f)
                throw new ParseException(
                    $"{contextLabel}: invalid value '{raw}' " +
                    $"(expected a positive number like '0.5', a device-density like '2x', " +
                    $"or a canvas-relative scale like '0.5r')");
        }
```

- [ ] **Step 4: 跑测试确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScaleAttributeTests")
```

Expected: 新增 8 个 parser 测试全绿；原有 parser/runtime 测试不回归。

- [ ] **Step 5: Lint**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: 无 diff、无 warn。（**不要**用 `dotnet format analyzers --severity info`。）

- [ ] **Step 6: Commit**

```bash
git add Runtime/Core/Parser/UIDocumentParser.cs Tests/EditMode/Application/ScaleAttributeTests.cs
git commit -m "feat: parse scale=\"<r>r\" canvas-relative scale form

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Screen — `TryParseRelativeScale` + `ApplyScales` 分支（Open 期）

**Files:**
- Modify: `Runtime/Application/Screen.cs`（`ApplyScales` 约 line 242；`TryParseDeviceScale` 旁约 line 309；`ApplyScales` 上方注释约 line 236）
- Test: `Tests/EditMode/Application/ScaleAttributeTests.cs`

> 本任务只覆盖 **Open 期固定 factor** 的 localScale + box-preserving。resize 重算依赖门控（Task 3），故 resize 用例放 Task 3。

- [ ] **Step 1: 写失败的 runtime 测试**

在 `ScaleAttributeTests.cs` device-density runtime 段（约 line 636，`DeviceScale_under_layout_group...` 之后）插入：

```csharp
        // ---------- Runtime canvas-relative: localScale = max(1, round(f·r)) / f ----------

        [Test]
        public void RelativeScale_half_in_pixel_factor2_is_half()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f); // /1920x1080 = factor 2
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='0.5r'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f); // round(2*0.5)=1 → 1/2
            Assert.AreEqual(0.5f, rt.localScale.y, 1e-5f);
        }

        [Test]
        public void RelativeScale_half_in_pixel_factor3_is_two_thirds()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // factor 3
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='0.5r'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(2f / 3f, rt.localScale.x, 1e-5f); // round(3*0.5)=round(1.5)=2 → 2/3
        }

        [Test]
        public void RelativeScale_half_in_pixel_factor4_is_half()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1920f, 1080f); // /480x270 = factor 4
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='480x270'>
    <Frame id='f' scale='0.5r'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f); // round(4*0.5)=2 → 2/4
        }

        [Test]
        public void RelativeScale_half_in_pixel_factor5_rounds_half_up()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(2400f, 1350f); // /480x270 = factor 5
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='480x270'>
    <Frame id='f' scale='0.5r'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(3f / 5f, rt.localScale.x, 1e-5f); // round(5*0.5)=round(2.5)=3 (half-up) → 3/5
        }

        [Test]
        public void RelativeScale_half_in_pixel_factor1_does_not_clamp()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1920f, 1080f); // factor 1
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='0.5r'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1f, rt.localScale.x, 1e-5f); // round(1*0.5)=round(0.5)=1 → 1/1
        }

        [Test]
        public void RelativeScale_quarter_in_pixel_factor1_clamps_to_one()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1920f, 1080f); // factor 1
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='0.25r'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1f, rt.localScale.x, 1e-5f); // round(0.25)=0 → max(1,0)=1 → 1/1
        }

        [Test]
        public void RelativeScale_one_in_pixel_factor3_is_identity()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // factor 3
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='1r'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1f, rt.localScale.x, 1e-5f); // round(3*1)=3 → 3/3
        }

        [Test]
        public void RelativeScale_half_in_auto_factor2_is_half()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f); // /1920x1080 = factor 2
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <Frame id='f' scale='0.5r'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f); // round(2*0.5)=1 → 1/2
        }

        [Test]
        public void RelativeScale_box_preserving_stretch_in_factor3()
        {
            // localScale = round(3*0.5)/3 = 2/3; inv = 1.5. Same numbers as 2x@factor3 (net 2).
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // factor 3
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' anchor='stretch' margin='10,10,10,10' scale='0.5r'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(2f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-0.25f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(1.25f, rt.anchorMax.x, 1e-4f);
            Assert.AreEqual(-30f, rt.sizeDelta.x, 1e-3f);
        }
```

- [ ] **Step 2: 跑测试确认失败**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScaleAttributeTests")
```

Expected: 9 个 `RelativeScale_*` 全失败——当前 `ApplyScales` 不识别 `R`，`float.TryParse("0.5r")` fail → localScale 落到 identity 1（断言 0.5/0.6667 等 → 失败）。

- [ ] **Step 3: 实现 helper + `ApplyScales` 分支**

`Runtime/Application/Screen.cs`：在 `TryParseDeviceScale`（约 line 309）下方新增 helper：

```csharp
        // scale="<r>r" (r positive float): localScale = max(1, round(canvasFactor·r)) / canvasFactor
        // → scales relative to the factor but snaps net physical-px/unit to the nearest integer
        // so it stays pixel-aligned at any factor. Returns false for the 'Nx' and plain-multiplier
        // forms (handled by TryParseDeviceScale / float.TryParse).
        private static bool TryParseRelativeScale(string raw, out float r)
        {
            r = 0f;
            if (string.IsNullOrEmpty(raw) || raw.Length < 2 || raw[raw.Length - 1] != 'r') return false;
            return float.TryParse(raw.Substring(0, raw.Length - 1),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out r) && r > 0f;
        }
```

在 `ApplyScales`（约 line 242）的 device-scale 分支（`if (TryParseDeviceScale(raw, out var devN)) { ... continue; }`，约 line 257-264）**之后**、`if (string.IsNullOrEmpty(raw) || !float.TryParse...` 之前插入：

```csharp
                if (TryParseRelativeScale(raw, out var relR))
                {
                    var f = _canvasFactor > 0f ? _canvasFactor : 1f;
                    // round-half-up to nearest integer effective (≥1), then divide the factor
                    // back out: net physical-px/unit = effective (integer → pixel-aligned),
                    // and grows with f (responds to window size). See CRS-D3/D4/D5.
                    var eff = UnityEngine.Mathf.Max(1f, UnityEngine.Mathf.Floor(f * relR + 0.5f));
                    var dv = eff / f;
                    rt.localScale = new UnityEngine.Vector3(dv, dv, 1f);
                    ApplyBoxPreservingCompensation(rt, dv);
                    continue;
                }
```

同时更新 `ApplyScales` 上方注释（约 line 236-238）的 "Plain-multiplier has no dependence... The device-density form 'Nx' divides by _canvasFactor" 段，改为同时提及 `<r>r`：

```csharp
        // Plain-multiplier 'scale="N"' has no dependence on canvas factor. The device-density
        // form 'scale="Nx"' and the canvas-relative form 'scale="<r>r"' both divide by
        // _canvasFactor, so a factor change (canvas resize) must re-run this — routed via
        // ReSolve in OnCanvasDimensionsChanged when _hasFactorScale.
```

> 注：`_hasFactorScale` 在 Task 3 才重命名出现；本任务先按计划写注释，编译以 Task 3 落地后为准——若想本任务独立编译通过，可暂时写 `_hasDeviceScale`，Task 3 再随重命名一并改。推荐合并 Task 2/3 一起 refresh 编译，避免中间态注释引用未定义符号（注释不影响编译，安全）。

- [ ] **Step 4: 跑测试确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScaleAttributeTests")
```

Expected: 9 个 `RelativeScale_*`（含 box-preserving）全绿；原有用例不回归。

- [ ] **Step 5: Lint**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application/Screen.cs Tests/EditMode/Application/ScaleAttributeTests.cs
git commit -m "feat: apply scale=\"<r>r\" at Open (localScale = round(f*r)/f)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Screen — resize 门控泛化（`_hasDeviceScale`→`_hasFactorScale`，检测扩到 `R`）

**Files:**
- Modify: `Runtime/Application/Screen.cs`（字段约 line 35；`RecomputeHasDeviceScale` 约 line 286；`DeclaresDeviceScale` 约 line 296；调用点：`Open` 约 line 143、`OnCanvasDimensionsChanged` 约 line 376、`ReSolve` 约 line 482）
- Test: `Tests/EditMode/Application/ScaleAttributeTests.cs`

- [ ] **Step 1: 写失败的 resize 测试**

在 `ScaleAttributeTests.cs` device-density resize 段（约 line 748，`Resize_without_device_scale_still_recomputes_factor` 之后）插入：

```csharp
        // ---------- Canvas-relative recompute on resize + gate ----------

        [Test]
        public void RelativeScale_recomputes_localScale_on_resize()
        {
            // R-only Screen (no Nx): the resize gate must include the R form, else the
            // lightweight path skips ApplyScales and localScale stays stale.
            UnityEngine.Vector2 size = new(3840f, 2160f); // /1920x1080 = factor 2
            UI.CanvasSizeOverride = () => size;
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='0.5r'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f); // factor 2: round(1)=1 → 1/2

            // Resize to factor 3; fire the relay.
            size = new UnityEngine.Vector2(5760f, 3240f); // factor 3
            var relay = screen.RootGameObject.GetComponent<PromptUGUI.Application.RectDimensionsRelay>();
            relay.OnDimensionsChanged?.Invoke();
            Assert.AreEqual(2f / 3f, rt.localScale.x, 1e-5f); // factor 3: round(1.5)=2 → 2/3
        }

        [Test]
        public void RelativeScale_box_preserving_does_not_accumulate_across_resizes()
        {
            UnityEngine.Vector2 size = new(3840f, 2160f); // factor 2
            UI.CanvasSizeOverride = () => size;
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' anchor='stretch' margin='10,10,10,10' scale='0.5r'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            var relay = screen.RootGameObject.GetComponent<PromptUGUI.Application.RectDimensionsRelay>();

            // factor 2: round(1)=1 → localScale 1/2, inv 2 → span 2 about 0.5 → [-0.5, 1.5]; sizeDelta -20*2 = -40.
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(1.5f, rt.anchorMax.x, 1e-4f);
            Assert.AreEqual(-40f, rt.sizeDelta.x, 1e-3f);

            // → factor 3: round(1.5)=2 → localScale 2/3, inv 1.5 → [-0.25, 1.25]; sizeDelta -30.
            size = new UnityEngine.Vector2(5760f, 3240f);
            relay.OnDimensionsChanged?.Invoke();
            Assert.AreEqual(2f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-0.25f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(1.25f, rt.anchorMax.x, 1e-4f);
            Assert.AreEqual(-30f, rt.sizeDelta.x, 1e-3f);

            // → back to factor 2: must equal first reading, NOT compounded.
            size = new UnityEngine.Vector2(3840f, 2160f);
            relay.OnDimensionsChanged?.Invoke();
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(1.5f, rt.anchorMax.x, 1e-4f);
            Assert.AreEqual(-40f, rt.sizeDelta.x, 1e-3f);
        }
```

- [ ] **Step 2: 跑测试确认失败**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScaleAttributeTests")
```

Expected: 两个测试在 resize 后断言失败——`_hasDeviceScale` 当前只认 `Nx`，R-only Screen 走轻量 `ApplyCanvasScaler`（更新 `_canvasFactor` 但不跑 `ApplyScales`），localScale 停留在 Open 时的 0.5。

- [ ] **Step 3: 泛化门控（重命名 + 扩检测）**

`Runtime/Application/Screen.cs`，做 4 处机械改名 + 1 处检测扩展：

(a) 字段（约 line 35）：

```csharp
        private bool _hasFactorScale;      // 任一节点用了 scale="Nx" 或 "<r>r"（依赖 _canvasFactor）→ resize 走 ReSolve
```

(b) `RecomputeHasDeviceScale`（约 line 286）→ 重命名为 `RecomputeFactorScale`，注释同步：

```csharp
        // Sets _hasFactorScale if any currently-instantiated node uses a factor-dependent
        // scale form (scale="Nx" or scale="<r>r"). Called at Open and re-run in ReSolve:
        // Add-block activation (Strategy C) can introduce such nodes into _nodeMap after Open.
        private void RecomputeFactorScale()
        {
            _hasFactorScale = false;
            foreach (var node in _nodeMap.Keys)
            {
                if (DeclaresFactorScale(node)) { _hasFactorScale = true; break; }
            }
        }
```

(c) `DeclaresDeviceScale`（约 line 296）→ 重命名为 `DeclaresFactorScale`，检测加 `TryParseRelativeScale`：

```csharp
        // Whether a node declares a factor-dependent scale (Nx or <r>r) in its base attribute
        // or any variant override.
        private static bool DeclaresFactorScale(ElementNode node)
        {
            if (node.Attributes.TryGetValue("scale", out var baseVal)
                && (TryParseDeviceScale(baseVal, out _) || TryParseRelativeScale(baseVal, out _))) return true;
            if (node.VariantOverrides.TryGetValue("scale", out var list))
                foreach (var (_, value) in list)
                    if (TryParseDeviceScale(value, out _) || TryParseRelativeScale(value, out _)) return true;
            return false;
        }
```

(d) 调用点改名：
- `Open`（约 line 143）：`RecomputeHasDeviceScale();` → `RecomputeFactorScale();`
- `ReSolve`（约 line 482）：`RecomputeHasDeviceScale();` → `RecomputeFactorScale();`
- `OnCanvasDimensionsChanged`（约 line 376）：`if (_hasDeviceScale)` → `if (_hasFactorScale)`

用 grep 兜底确认无遗漏：

```bash
grep -rn "_hasDeviceScale\|DeclaresDeviceScale\|RecomputeHasDeviceScale" Runtime/
```

Expected: 无输出（全部改完）。

- [ ] **Step 4: 跑测试确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScaleAttributeTests")
```

Expected: 两个新 resize 测试转绿；**所有** `DeviceScale_*` / `RelativeScale_*` / 裸乘数 / variant 用例全绿（重命名未改行为）。特别确认 `Resize_without_device_scale_still_recomputes_factor` 仍绿（无 Nx 无 R → 仍走轻量路径）。

- [ ] **Step 5: Lint**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

- [ ] **Step 6: 全量 EditMode 回归**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

Expected: 全绿（确认重命名没碰到 Screen 其他路径）。

- [ ] **Step 7: Commit**

```bash
git add Runtime/Application/Screen.cs Tests/EditMode/Application/ScaleAttributeTests.cs
git commit -m "feat: gate resize-recompute on any factor-dependent scale (Nx or <r>r)

Generalize _hasDeviceScale → _hasFactorScale so scale=\"<r>r\" also re-runs
ReSolve on canvas resize. Internal rename, no behavior change for Nx.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: 核实 XSD generator 对 `scale` 的约束

**Files:**
- 核实（必要时 Modify）：XSD generator（Editor 下；grep 定位）

- [ ] **Step 1: 定位 `scale` 在 XSD 的处理方式**

```bash
grep -rn "scale\|anyAttribute\|xs:string\|AddAttribute" Editor/ Runtime/ --include=*.cs | grep -i "xsd\|schema\|anyAttribute" | head
grep -rln "anyAttribute\|XmlSchema\|xsd" Editor/ Runtime/ --include=*.cs
```

定位生成 `scale` 属性声明的代码处，判断它是：
- (A) `xs:anyAttribute` / `xs:string` 无 pattern/enum 约束 → **无需改**（`<r>r` 天然被接受）。
- (B) 有 pattern/enum 限定 `scale` 取值 → 需放开以接受 `<r>r`（如 pattern 加 `|[0-9]*\.?[0-9]+R` 分支）。

- [ ] **Step 2: 处置**

- 若 (A)：在本计划勾选记录"XSD 无 `scale` 约束，无需改"，跳到 Task 5。
- 若 (B)：改 generator 放开 `scale`；若有 XSD generator 测试（`StringAssert.Contains` 风格），加一条覆盖 `<r>r` 被接受；跑 `PromptUGUI.Tests.EditorOnly` 转绿。

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
```

- [ ] **Step 3: Commit（仅当 (B) 改了代码）**

```bash
git add Editor/
git commit -m "feat: XSD accepts scale=\"<r>r\" canvas-relative form

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: XML SKILL 同步

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`（`scale` 表行 line 236；Device-density 子节末尾约 line 635 后插入新子节）

- [ ] **Step 1: 改 `scale` 速查表行（line 236）**

把现有 `scale="N"` / `scale="Nx"` 行整行替换为同时含 `<r>r` 的版本：

```markdown
| `scale="N"` / `scale="Nx"` / `scale="<r>r"` | positive float `N`; **or** `Nx` (N positive integer); **or** `<r>r` (r positive float, lowercase `r`) | `scale="N"` (float): `localScale=(N,N,1)`, **box-preserving** — declared box stays, `N` only changes render density. `scale="Nx"` (device-density): `localScale = N / canvasFactor` → locks to **N physical pixels per design-unit** (constant size across factors, does **not** grow with window). `scale="<r>r"` (canvas-relative snapped): `localScale = max(1, round(canvasFactor × r)) / canvasFactor` → scales to `r×` the canvas factor but **snaps the net to an integer**, so it **grows with the window yet stays pixel-aligned at any factor** (e.g. `0.5r` is net 1 px/unit at factor 2, net 2 at factor 3 _and_ 4, net 3 at factor 6). All three recompute on factor change. See "Relative scale" / "Device-density" / "Canvas-relative snapped" below. |
```

- [ ] **Step 2: 在 "Device-density (`scale="Nx"`)" 子节之后（约 line 635 的 LayoutGroup caveat 项之后）新增子节**

```markdown
### Canvas-relative snapped (`scale="<r>r"`)

`scale="<r>r"` (r a **positive float**, lowercase `r`) scales the element to **r× the current canvas factor**, but snaps the result to the nearest integer net density so it stays pixel-aligned: `localScale = max(1, round(canvasFactor × r)) / canvasFactor`. The net physical-pixels-per-design-unit is `round(canvasFactor × r)` — an integer that **grows as the window grows** (unlike `Nx`, whose net is constant) while **never going off the pixel grid** (unlike a plain float, which blurs at odd factors). Recomputed on canvas resize / device rotation.

Why it exists: `scale="0.5"` follows the window but blurs at an odd factor (`3 × 0.5 = 1.5` px → off-grid); `scale="2x"` is always crisp but its size never grows with the window. `<r>r` gives the in-between: a smaller element that still responds to window size **and** stays crisp at every factor.

```xml
<!-- 12x12 CJK bitmap font: want it "about half" the chunky integer step, but crisp.
     0.5r → factor 2: net 1 (12px); factor 3: net 2 (24px); factor 4: net 2; factor 6: net 3.
     Always an integer net → pixel-aligned, and it grows as the window grows. -->
<Text fontSize="12" scale="0.5r" alignment="center">设置</Text>
```

Choosing between the three forms:

| Form | Net px/design-unit | Grows with window? | Pixel-aligned? | Use for |
|---|---|---|---|---|
| `scale="N"` (float) | `N × factor` | yes | only if `N×factor` is integer | render-density tweaks on SDF/TMP text; not pixel-art |
| `scale="Nx"` (N int) | `N` (constant) | no | yes (pixel mode) | UI text at a fixed physical size across devices |
| `scale="<r>r"` (r float) | `round(factor × r)` | yes | yes (pixel mode) | small bitmap text/elements that scale with the window but must stay crisp |

- **Rounding is round-half-up**: `round(factor × r)` rounds `.5` up, so `0.5r` at factor 3 → net 2 (not 1), at factor 5 → net 3.
- **Clamped to a minimum net of 1**: when `round(factor × r) < 1` (e.g. `0.25r` at factor 1), the net floors at 1 — you can't go below one physical pixel per design-unit and stay aligned.
- **r may exceed 1** (`2r` grows twice as fast and stays aligned), and may be fractional (`0.25r`, `1.5r`). `r` must be positive; the suffix is lowercase `r` only — matching device-density's lowercase `x` (`0.5R` is a parse error).
- **Truly crisp only in `scale-mode="pixel"`** (integer factor + `Canvas.pixelPerfect`). In `auto` mode the net is still integer but position can land sub-pixel — same caveat as `Nx`.
- **Composes with `UI.PixelScalePowerOfTwo` / `UI.MinPixelScale`**: `<r>r` reads the final effective factor, so it snaps relative to whatever factor those settings produce.
- Box-preserving behavior and the LayoutGroup-skip caveat (below) apply identically (inflation uses `1 / localScale`).
```

- [ ] **Step 3: 校验 SKILL 无破坏 + 文内一致**

通读改动两处，确认表格列对齐、示例 XML 合法、与 spec 数值一致（factor 3 → net 2）。

- [ ] **Step 4: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "docs(skill): document scale=\"<r>r\" canvas-relative snapped form

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: master spec 同步

**Files:**
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`（约 line 235，`scale="Nx"` 那一行）

- [ ] **Step 1: 在 `scale="Nx"` 行之后追加 `<r>r` 一行**

在 master spec 现有 `- 元素级 \`scale="Nx"\`（...）...详见 2026-05-31-scale-device-density-design.md。普通 \`scale="N"\`...` 那一条之后，追加：

```markdown
- 元素级 `scale="<r>r"`（r 正浮点，小写 `r`，支持 `.variant`）：画布相对吸附形态，`localScale = max(1, round(canvasFactor × r)) / canvasFactor`——缩放跟随 factor（随窗口长大），但净物理像素/设计单位吸附到整数保持像素对齐，填补 `scale="N"`（响应但奇数 factor 糊）与 `scale="Nx"`（恒定不长大）之间。详见 [`2026-06-01-scale-canvas-relative-snap-design.md`](2026-06-01-scale-canvas-relative-snap-design.md)。
```

- [ ] **Step 2: Commit**

```bash
git add "docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md"
git commit -m "doc: reference scale=\"<r>r\" spec from master design

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: host Unity 肉眼验收

**Files:** 无（手动验证）

- [ ] **Step 1: pixel 模式多 factor 目测**

在 host 工程 `C:\xsoft\PromptUGUIDev` 摆一个 `scale-mode="pixel"` Screen，放 `<Text fontSize="12" scale="0.5r">设置文字 ABC</Text>`，调窗口大小使 factor 落在 2 / 3 / 4 / 5：
- 每个 factor 下中文位图字**清晰不糊**（无半像素跨格）。
- factor 越大字越大（net 1→2→2→3），即随窗口响应。

- [ ] **Step 2: resize 实时重算**

拖动窗口跨越 factor 边界（如从 factor 2 区间拖到 factor 3 区间），确认字体尺寸**实时跳变并保持清晰**（resize→ReSolve 路径生效），无累积变形（来回拖动回到原 factor 字体精确复原）。

- [ ] **Step 3: 与 `0.5` / `2x` 对照**

同屏并列 `scale="0.5"`、`scale="2x"`、`scale="0.5r"` 三个 `<Text>`，在 factor 3 下确认：`0.5` 糊、`2x` 清晰但偏小固定、`0.5r` 清晰且尺寸介于两者并随窗口变化。

- [ ] **Step 4: 记录结论**

把目测结论（通过/问题）记到 PR 描述。

---

## 自检（spec 覆盖）

| spec 要点 | 对应任务 |
|---|---|
| CRS-D1/D2 语法 `<r>r` 正浮点 | Task 1 |
| CRS-D3/D4/D5 语义 + round-half-up + ≥1 钳制 | Task 2（Step 3 公式 `Max(1, Floor(f·r+0.5))`） |
| CRS-D6/D7 不分支 scale-mode + 读最终 `_canvasFactor` | Task 2（复用既有 `_canvasFactor`） |
| CRS-D8 门控泛化 `_hasFactorScale` | Task 3 |
| CRS-D9/D10 parse 校验 + 小写 r | Task 1 |
| CRS-D11 r>1 允许 | Task 1（`2r` 用例）+ Task 2（identity/放大隐含） |
| CRS-D12 ApplyScales 解析顺序 | Task 2（R 分支在 Nx 后、float 前） |
| CRS-D13 `_canvasFactor≤0` 兜底 | Task 2（`f = _canvasFactor>0 ? : 1`） |
| CRS-D14 LayoutGroup 子节点 | 复用 `ApplyBoxPreservingCompensation`（无新代码）；SKILL caveat（Task 5） |
| CRS-D15 XSD | Task 4 |
| CRS-D16 SKILL | Task 5 |
| CRS-D17 master spec | Task 6 |
| CRS-D18 测试位置 | Task 1/2/3（全部 `ScaleAttributeTests.cs`） |
| 风险：auto 模式 caveat / 三形态混淆 | Task 5 SKILL caveat + 对照表 |
| 验收：肉眼脆度 + resize | Task 7 |

**Placeholder 扫描：** 无 TBD/TODO；每个 code step 含完整代码。
**类型一致性：** `TryParseRelativeScale(string, out float)` / `_hasFactorScale` / `DeclaresFactorScale` / `RecomputeFactorScale` 在 Task 2/3 跨任务命名一致；`ApplyBoxPreservingCompensation(rt, dv)` 沿用既有签名。
