# `scale="Nx"` 设备像素密度锁定 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 `scale` 属性新增 `Nx`（N 正整数）取值，语义 `localScale = N / canvasFactor`（净 N 物理像素/设计单位），并在 canvas factor 变化时重算，让像素美术项目的中文位图字在任意整数 factor 下保持像素对齐。

**Architecture:** 复用已有的 box-preserving `scale` 管线。parser 接受 `Nx` 形态；`Screen` 缓存本次生效的 `_canvasFactor`，`ApplyScales` 把 `Nx` 解成 `N/factor` 后走现有 `ApplyBoxPreservingCompensation`；含 `Nx` 的 Screen 在 resize 时走 `ReSolve`（重置基线 + 重算，已被测试证明不累积补偿），无 `Nx` 的 Screen 走原轻量路径不变。不分支 scale-mode、不碰 `PixelScaleSolver` / sprite 缩放 / C# 公开 API。

**Tech Stack:** C# (Unity 6, `Awaitable`, LangVersion 9.0)；NUnit EditMode 测试经 UnityMCP 运行；`dotnet format` lint。

**Spec:** [`docs~/superpowers/specs/2026-05-31-scale-device-density-design.md`](../specs/2026-05-31-scale-device-density-design.md)

**Branch:** `feat/scale-device-density`（已创建，spec 已 commit）

---

## 关键工具命令（每个 Task 重复使用）

编译刷新（任何 `.cs` 改动后）：

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

跑 EditMode 测试（本特性全在 `ScaleAttributeTests`）：

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScaleAttributeTests")
```

Lint（从仓库根）：

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

> **TDD 提示**：MCP 跑测试偶发 "failed to initialize"（多为场景未保存）/挂起；遇到先 `refresh_unity` 重试，必要时让用户重启 Unity（见 memory `project_unity_mcp_test_gotchas`）。"测试应失败"那步若 MCP 不稳，至少确认编译通过 + 新断言引用了尚不存在的行为。

---

## Task 1: Parser 接受 `Nx`，拒绝非整数 / 非法形态

**Files:**
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs`（`ValidateScale`，约 line 527-539）
- Test: `Tests/EditMode/Application/ScaleAttributeTests.cs`（在 "Parser validation" 区追加）

- [ ] **Step 1: 写失败测试**（追加到 `ScaleAttributeTests.cs` 现有 parser 测试之后，约 line 114 之后）

```csharp
        // ---------- Parser validation: device-density 'Nx' ----------

        [Test]
        public void Parser_accepts_device_scale_integer()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='2x'/></Screen>
</PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Parser_accepts_device_scale_one_and_multidigit()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='a' scale='1x'/><Frame id='b' scale='10x'/></Screen>
</PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Parser_accepts_device_scale_variant()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='1x' scale.portrait='2x'/></Screen>
</PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Parser_rejects_fractional_device_scale()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='1.5x'/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("device-density", ex.Message);
        }

        [Test]
        public void Parser_rejects_zero_device_scale()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='0x'/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("device-density", ex.Message);
        }

        [Test]
        public void Parser_rejects_bare_x_device_scale()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='x'/></Screen>
</PromptUGUI>";
            // 'x' 长度<2 → 不进 device 分支 → 走 float 校验 → 仍报错（含 'scale'）。
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale", ex.Message);
        }

        [Test]
        public void Parser_rejects_negative_device_scale()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='-1x'/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("device-density", ex.Message);
        }
```

- [ ] **Step 2: 跑测试，确认失败**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScaleAttributeTests")
```

Expected: `Parser_accepts_device_scale_*` FAIL（现 `ValidateScale` 把 `2x` 当浮点 → `float.TryParse("2x")` 失败 → 抛 ParseException）；`Parser_rejects_*` 里 `1.5x`/`0x`/`-1x` 当前抛的 message 不含 `device-density` → FAIL。

- [ ] **Step 3: 改 `ValidateScale`**（替换 `Runtime/Core/Parser/UIDocumentParser.cs` 现有 `ValidateScale` 方法体）

```csharp
        private static void ValidateScale(string raw, string contextLabel)
        {
            // scale="N" sets RectTransform.localScale = N (relative to layout box; works in
            // any scale-mode). Must be a positive float. N=1 is the no-op identity.
            // scale="Nx" (N positive integer) is the device-density form: localScale =
            // N / canvasFactor at runtime — locks the element to N physical pixels per
            // design-unit. See 2026-05-31-scale-device-density-design.md.
            if (string.IsNullOrEmpty(raw))
                throw new ParseException(
                    $"{contextLabel}: value cannot be empty " +
                    $"(expected a positive number like '0.5', or a device-density like '2x')");

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

            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var v) || v <= 0f)
                throw new ParseException(
                    $"{contextLabel}: invalid value '{raw}' " +
                    $"(expected a positive number like '0.5', or a device-density like '2x')");
        }
```

> `NumberStyles.None` 拒绝 `1.5x`（小数）、`+2x`（符号）、`2 x`（空格）。`<Animation>` 的 `scale="1:0.5"` 仍被现有豁免（`if (!(tag == "Animation" && ns == null))`，约 line 503）跳过。

- [ ] **Step 4: 跑测试，确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScaleAttributeTests")
```

Expected: 新 7 个用例全 PASS；现有 `Parser_*` 用例（`zero`/`negative`/`non_numeric`/`Animation` 等）仍 PASS。

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Parser/UIDocumentParser.cs Tests/EditMode/Application/ScaleAttributeTests.cs
git commit -m "feat: parser accepts scale='Nx' device-density form

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: 运行期 `Nx` → `localScale = N / canvasFactor`（Open 时）

**Files:**
- Modify: `Runtime/Application/Screen.cs`（新增 `_canvasFactor` 字段；`ApplyPixel` / `ApplyAuto` 缓存 factor；`ApplyScales` 加 `Nx` 分支 + `TryParseDeviceScale` helper）
- Test: `Tests/EditMode/Application/ScaleAttributeTests.cs`（新增 device-density runtime 区）

- [ ] **Step 1: 写失败测试**（追加到 `ScaleAttributeTests.cs`，runtime 区之后）

```csharp
        // ---------- Runtime device-density: localScale = N / canvasFactor ----------

        [Test]
        public void DeviceScale_1x_in_pixel_factor3_is_one_third()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // /1920x1080 = factor 3
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='1x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(1f / 3f, rt.localScale.y, 1e-5f);
        }

        [Test]
        public void DeviceScale_2x_in_pixel_factor3_is_two_thirds()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // factor 3
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='2x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(2f / 3f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void DeviceScale_3x_in_pixel_factor3_is_identity()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // factor 3
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='3x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void DeviceScale_2x_in_pixel_factor2_is_identity()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f); // /1920x1080 = factor 2
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='2x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void DeviceScale_2x_in_pixel_factor4_is_half()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1920f, 1080f); // /480x270 = factor 4
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='480x270'>
    <Frame id='f' scale='2x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void DeviceScale_1x_in_auto_factor2_is_half()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f); // /1920x1080 = factor 2
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <Frame id='f' scale='1x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void DeviceScale_2x_in_auto_no_reference_is_two()
        {
            // No reference → ConstantPixelSize factor 1 → localScale = 2/1 = 2.
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto'>
    <Frame id='f' scale='2x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(2f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void DeviceScale_2x_box_preserving_stretch_in_factor3()
        {
            // localScale = 2/3; inv = 1/0.6667 = 1.5. stretch span widened 1.5 about 0.5
            // → [-0.25, 1.25]; sizeDelta = -(10+10) * 1.5 = -30.
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // factor 3
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' anchor='stretch' margin='10,10,10,10' scale='2x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(2f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-0.25f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(1.25f, rt.anchorMax.x, 1e-4f);
            Assert.AreEqual(-30f, rt.sizeDelta.x, 1e-3f);
        }

        [Test]
        public void DeviceScale_under_layout_group_keeps_unscaled_slot_and_no_widen()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // factor 3
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <VStack>
      <Frame id='f' width='100' height='50' scale='2x'/>
    </VStack>
  </Screen>
</PromptUGUI>");
            var c = screen.Get("f");
            var rt = c.RectTransform;
            var le = c.GameObject.GetComponent<UnityEngine.UI.LayoutElement>();
            Assert.AreEqual(2f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(100f, le.preferredWidth, 1e-4f);   // unscaled slot
            Assert.GreaterOrEqual(rt.anchorMin.x, 0f);          // compensation skipped
        }
```

- [ ] **Step 2: 跑测试，确认失败**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScaleAttributeTests")
```

Expected: 所有 `DeviceScale_*` FAIL —— 现 `ApplyScales` 对 `"2x"` 走 `float.TryParse` 失败分支 → `localScale = 1`（identity），断言值不符。

- [ ] **Step 3a: 加 `_canvasFactor` 字段**（`Runtime/Application/Screen.cs`，紧挨现有 `private bool _isReapplyingScaler;`，约 line 29）

```csharp
        private bool _isReapplyingScaler;
        // The pixel/auto factor that ApplyCanvasScaler last applied; 'Nx' scale divides by it.
        private float _canvasFactor = 1f;
```

- [ ] **Step 3b: `ApplyPixel` 末尾缓存 factor**（`Screen.cs` 约 line 205-206）

找到：

```csharp
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = factor;
        }
```

改为：

```csharp
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = factor;
            _canvasFactor = factor;
        }
```

- [ ] **Step 3c: `ApplyAuto` 两分支缓存 factor**（`Screen.cs` 约 line 165-181）

替换整个 `ApplyAuto` 方法体：

```csharp
        private void ApplyAuto(UnityEngine.UI.CanvasScaler scaler)
        {
            var raw = PromptUGUI.Variants.VariantResolver.ResolveAttribute(
                Def.Root, "reference", Variants);
            var parsed = PromptUGUI.Application.ReferenceResolutionParser.Parse(
                raw, $"<Screen name='{Def.Name}' reference> (runtime)");
            if (!parsed.HasValue)
            {
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
                _canvasFactor = 1f;
                return;
            }
            var size = parsed.Value;
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = size;
            scaler.matchWidthOrHeight = size.x >= size.y ? 0f : 1f;
            // Cache the effective factor for 'Nx' scale. Replicates Unity's
            // ScaleWithScreenSize output at the match endpoints we use (0 → width-locked,
            // 1 → height-locked). Same screen-size source as pixel mode (CanvasSizeOverride
            // for tests, else Canvas.pixelRect).
            var screenPx = UI.CanvasSizeOverride != null
                ? UI.CanvasSizeOverride()
                : ReadCanvasRectSize();
            _canvasFactor = size.x >= size.y
                ? (size.x > 0f ? screenPx.x / size.x : 1f)
                : (size.y > 0f ? screenPx.y / size.y : 1f);
        }
```

- [ ] **Step 3d: `ApplyScales` 加 `Nx` 分支 + helper**（`Screen.cs` 约 line 232-249）

找到现有 resolve 块：

```csharp
                var raw = PromptUGUI.Variants.VariantResolver.ResolveAttribute(
                    node, "scale", Variants);
                var rt = kv.Value.RectTransform;
                if (rt == null) continue;

                if (string.IsNullOrEmpty(raw)
```

在 `if (rt == null) continue;` 之后、`if (string.IsNullOrEmpty(raw)` 之前插入：

```csharp
                if (TryParseDeviceScale(raw, out var devN))
                {
                    var f = _canvasFactor > 0f ? _canvasFactor : 1f;
                    var dv = devN / f;
                    rt.localScale = new Vector3(dv, dv, 1f);
                    ApplyBoxPreservingCompensation(rt, dv);
                    continue;
                }

```

并在 `ApplyScales` 方法之后（`ApplyBoxPreservingCompensation` 附近）新增 helper：

```csharp
        // scale="Nx" (N positive integer): localScale = N / canvasFactor → renders the
        // element at exactly N physical pixels per design-unit, independent of the auto
        // factor. Returns false for the plain-multiplier form (handled by float.TryParse).
        private static bool TryParseDeviceScale(string raw, out int n)
        {
            n = 0;
            if (string.IsNullOrEmpty(raw) || raw[raw.Length - 1] != 'x') return false;
            return int.TryParse(raw.Substring(0, raw.Length - 1),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out n) && n >= 1;
        }
```

- [ ] **Step 3e: 更新 `ApplyScales` 过时注释**（`Screen.cs` 约 line 216-218）

把方法上方注释里 "No dependence on canvas factor: ..." 这句改为：

```csharp
        // Plain-multiplier 'scale="N"' has no dependence on canvas factor. The device-density
        // form 'scale="Nx"' divides by _canvasFactor, so a factor change (canvas resize) must
        // re-run this — routed via ReSolve in OnCanvasDimensionsChanged when _hasDeviceScale.
```

- [ ] **Step 4: 跑测试，确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScaleAttributeTests")
```

Expected: 全部 `DeviceScale_*` PASS；现有裸乘数 / box-preserving / variant 用例仍 PASS。

> Step 3e 注释引用了 `_hasDeviceScale`（Task 3 才加）。注释引用未来字段无碍编译；如介意可在 Task 3 完成后再补。`TryParseDeviceScale` 在本 Task 已被 `ApplyScales` 调用 → 无 "unused" 警告。

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/Screen.cs Tests/EditMode/Application/ScaleAttributeTests.cs
git commit -m "feat: scale='Nx' resolves to localScale = N / canvasFactor at Open

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: canvas factor 变化时重算 `Nx`（resize / 旋转）

**Files:**
- Modify: `Runtime/Application/Screen.cs`（新增 `_hasDeviceScale` 字段 + `DeclaresDeviceScale` helper；`Open` 计算；`OnCanvasDimensionsChanged` 路由）
- Test: `Tests/EditMode/Application/ScaleAttributeTests.cs`（resize 区）

- [ ] **Step 1: 写失败测试**（追加到 `ScaleAttributeTests.cs`）

```csharp
        // ---------- Device-density recompute on canvas resize ----------

        [Test]
        public void DeviceScale_recomputes_localScale_on_resize()
        {
            UnityEngine.Vector2 size = new(5760f, 3240f); // factor 3
            UI.CanvasSizeOverride = () => size;
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='1x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1f / 3f, rt.localScale.x, 1e-5f);

            // Resize to factor 2; fire the relay (same seam ScreenScaleModeTests uses).
            size = new UnityEngine.Vector2(3840f, 2160f); // factor 2
            var relay = screen.RootGameObject.GetComponent<PromptUGUI.Application.RectDimensionsRelay>();
            relay.OnDimensionsChanged?.Invoke();

            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void DeviceScale_box_preserving_does_not_accumulate_across_resizes()
        {
            UnityEngine.Vector2 size = new(5760f, 3240f); // factor 3
            UI.CanvasSizeOverride = () => size;
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' anchor='stretch' margin='10,10,10,10' scale='1x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            var relay = screen.RootGameObject.GetComponent<PromptUGUI.Application.RectDimensionsRelay>();

            // factor 3: localScale 1/3, inv 3 → span 3 about 0.5 → [-1, 2]; sizeDelta -20*3 = -60.
            Assert.AreEqual(1f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-1f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(-60f, rt.sizeDelta.x, 1e-3f);

            // → factor 2: localScale 1/2, inv 2 → [-0.5, 1.5]; sizeDelta -20*2 = -40.
            size = new UnityEngine.Vector2(3840f, 2160f);
            relay.OnDimensionsChanged?.Invoke();
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(-40f, rt.sizeDelta.x, 1e-3f);

            // → back to factor 3: must equal first reading, NOT compounded.
            size = new UnityEngine.Vector2(5760f, 3240f);
            relay.OnDimensionsChanged?.Invoke();
            Assert.AreEqual(1f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-1f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(-60f, rt.sizeDelta.x, 1e-3f);
        }

        [Test]
        public void Resize_without_device_scale_still_recomputes_factor()
        {
            // Regression: a Screen with NO 'Nx' takes the lightweight path (no ReSolve)
            // and still recomputes the canvas scaleFactor on resize.
            UnityEngine.Vector2 size = new(1920f, 1080f); // factor 1
            UI.CanvasSizeOverride = () => size;
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='0.5'/>
  </Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(1f, scaler.scaleFactor, 1e-6f);
            // localScale is the plain multiplier 0.5, factor-independent.
            Assert.AreEqual(0.5f, screen.Get("f").RectTransform.localScale.x, 1e-6f);

            size = new UnityEngine.Vector2(3840f, 2160f); // factor 2
            var relay = screen.RootGameObject.GetComponent<PromptUGUI.Application.RectDimensionsRelay>();
            relay.OnDimensionsChanged?.Invoke();

            Assert.AreEqual(2f, scaler.scaleFactor, 1e-6f);
            Assert.AreEqual(0.5f, screen.Get("f").RectTransform.localScale.x, 1e-6f); // unchanged
        }
```

- [ ] **Step 2: 跑测试，确认失败**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScaleAttributeTests")
```

Expected: `DeviceScale_recomputes_localScale_on_resize` 与 `..._does_not_accumulate_...` FAIL（现 `OnCanvasDimensionsChanged` 只跑 `ApplyCanvasScaler`，不重算 localScale → localScale 停在 1/3）。`Resize_without_device_scale_still_recomputes_factor` 应已 PASS（现有行为）。

- [ ] **Step 3a: 加 `_hasDeviceScale` 字段**（`Screen.cs`，紧挨 `_canvasFactor`）

```csharp
        private float _canvasFactor = 1f;
        // True if any node declares scale="Nx" (base or variant). Gates the resize path:
        // such Screens re-run ReSolve (re-baseline + recompute) on canvas resize; others
        // keep the lightweight ApplyCanvasScaler-only path (zero behavior change).
        private bool _hasDeviceScale;
```

- [ ] **Step 3b: `Open` 计算 `_hasDeviceScale`**（`Screen.cs` 约 line 135-137，`ApplyScales();` 之前）

找到：

```csharp
            // scale must run after _nodeMap is populated and attributes have been applied
            // (so it doesn't fight ApplyCommon writes). Independent of canvas factor.
            ApplyScales();
```

改为：

```csharp
            // scale must run after _nodeMap is populated and attributes have been applied
            // (so it doesn't fight ApplyCommon writes).
            _hasDeviceScale = false;
            foreach (var node in _nodeMap.Keys)
            {
                if (DeclaresDeviceScale(node)) { _hasDeviceScale = true; break; }
            }
            ApplyScales();
```

- [ ] **Step 3c: 加 `DeclaresDeviceScale` helper**（`Screen.cs`，挨着 `TryParseDeviceScale`）

```csharp
        // Whether a node declares scale="Nx" in its base attribute or any variant override.
        private static bool DeclaresDeviceScale(PromptUGUI.IR.ElementNode node)
        {
            if (node.Attributes.TryGetValue("scale", out var baseVal)
                && TryParseDeviceScale(baseVal, out _)) return true;
            if (node.VariantOverrides.TryGetValue("scale", out var list))
                foreach (var (_, value) in list)
                    if (TryParseDeviceScale(value, out _)) return true;
            return false;
        }
```

> **核对**：`ElementNode` 的命名空间。打开 `Runtime/Core/IR/ElementNode.cs` 看 `namespace`（应为 `PromptUGUI.IR`）。若 `Screen.cs` 顶部已 `using PromptUGUI.IR;`（`_nodeMap` 的 key 类型就是它），helper 签名可直接写 `ElementNode` 而非全限定。按 `Screen.cs` 现有风格二选一。

- [ ] **Step 3d: `OnCanvasDimensionsChanged` 路由**（`Screen.cs` 约 line 305-312）

找到 try 块：

```csharp
            _isReapplyingScaler = true;
            try
            {
                var scaler = RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler != null) ApplyCanvasScaler(scaler);
            }
            finally { _isReapplyingScaler = false; }
```

改为：

```csharp
            _isReapplyingScaler = true;
            try
            {
                var scaler = RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler == null) return;
                if (_hasDeviceScale)
                    // 'Nx' localScale depends on the factor: re-baseline + recompute via the
                    // tested ReSolve path (ApplyCommon → ApplyCanvasScaler → ApplyScales) so the
                    // box-preserving inflation does not accumulate.
                    ReSolve();
                else
                    ApplyCanvasScaler(scaler);
            }
            finally { _isReapplyingScaler = false; }
```

- [ ] **Step 4: 跑测试，确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScaleAttributeTests")
```

Expected: 3 个 resize 用例全 PASS；Task 1/2 用例仍 PASS。

- [ ] **Step 5: 跑相邻套件防回归**（pixel-mode resize 路径被改动）

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ScreenScaleMode")
```

Expected: 全 PASS（无 `Nx` 的 Screen 仍走 `ApplyCanvasScaler`，`Resize_event_recomputes_pixel_factor` / `Resize_does_not_recurse` / `Pixel_apply_is_idempotent...` 不受影响）。

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application/Screen.cs Tests/EditMode/Application/ScaleAttributeTests.cs
git commit -m "feat: recompute scale='Nx' on canvas resize via ReSolve

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: 全套测试 + lint + XSD 核对

**Files:** 无代码改动（验证 + 可能的 lint 自动修复）

- [ ] **Step 1: 跑整个 EditMode 套件**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

Expected: 全绿。若有红，回到对应 Task 修。

- [ ] **Step 2: XSD 核对（无需改）**

`scale` 已在 `Editor/XsdGenerator.cs` 的 `commonAttrs`（约 line 214）以 `type="xs:string"` 声明，无 enum/pattern 约束 → `scale="2x"` 本就 schema-valid。**确认无需改 XsdGenerator**；不跑 `XsdGeneratorTests` 也无回归（本特性不动 XSD）。本步只是确认，不产出改动。

- [ ] **Step 3: lint**

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: `--verify-no-changes` 退出码 0。若 whitespace 有改动，`git add` 改动文件。**不要**跑 `dotnet format analyzers --severity info`（见 CLAUDE.md 的护栏表）。

- [ ] **Step 4: Commit（若 lint 有 whitespace 改动）**

```bash
git add -A
git commit -m "chore: lint whitespace for scale='Nx'

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

若无改动，跳过本步。

---

## Task 5: 更新 XML SKILL（CLAUDE.md 要求:属性取值变更必须同 PR 反映)

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`（`scale` 属性行 ~line 245；"Relative scale (box-preserving)" 段 ~line 605）

> **注意**：`.claude/skills/authoring-promptugui-xml/SKILL.md` 在 working tree 已有未提交改动（与本特性无关，是 commit 908fea7 上下文的残留）。**只编辑 `scale` 相关段落，`git add` 时只 add 本文件并确保不引入无关回退**。SKILL 改动写英文。

- [ ] **Step 1: 改 `scale` 属性表行**（约 line 245）

把现有 `| scale="N" | positive float | Sets RectTransform.localScale = (N, N, 1), **box-preserving**: ...` 这一行的类型/说明扩展为同时覆盖 `Nx`：

```markdown
| `scale="N"` / `scale="Nx"`  | positive float, **or** positive-integer + `x` | `scale="N"` (float): `localScale=(N,N,1)`, **box-preserving** — declared `anchor`/`size`/`margin` stays the visual box, `N` only changes render density (`N<1` finer, `N>1` coarser). `scale="Nx"` (device-density, N a positive integer): `localScale = N / canvasFactor` → locks the element to **N physical pixels per design-unit**, recomputed when the factor changes. Use `Nx` for pixel-perfect bitmap text under a `scale-mode='pixel'` integer factor (e.g. a 12×12 CJK font: `scale="2x"` renders it 24×24 and crisp at factor 2, 3 *or* 4). `scale="2"` (coarse 2×) and `scale="2x"` (net 2 device-px) are different. See "Relative scale" below. |
```

- [ ] **Step 2: 扩 "Relative scale (box-preserving)" 段**（约 line 605，在该段已有正文之后、"Where to put scale" 之前插入一个小节）

```markdown
### Device-density (`scale="Nx"`)

`scale="Nx"` (N a **positive integer**) is the device-density form: `localScale = N / canvasFactor`, where `canvasFactor` is the live CanvasScaler factor. Because the factor cancels, the element's content renders at exactly **N physical pixels per design-unit** regardless of what integer factor `scale-mode="pixel"` auto-computes (2 / 3 / 4 …), and it is **recomputed on canvas resize / device rotation**.

Why this exists: a pixel font's glyphs are crisp only when one source pixel maps to an integer number of physical pixels. A fixed multiplier like `scale="0.5"` breaks that under an odd factor (`3 × 0.5 = 1.5` px/source-pixel → blur). `Nx` divides the factor out so the result is always the integer N.

```xml
<!-- 12×12 CJK bitmap font, pixel-mode canvas. Want it smaller than the chunky 36×36
     that factor 3 gives, but still crisp. scale="2x" → 24×24, 2 device-px per source
     pixel, crisp at factor 2, 3 AND 4. Set fontSize to the font's native pixel height. -->
<Text fontSize="12" scale="2x" alignment="center">设置</Text>
```

Caveats:
- **N must be a positive integer.** `1.5x` is a parse error — use the plain multiplier `scale="1.5"` if you really want non-aligned scaling. Non-integer N cannot be pixel-aligned.
- **`Nx` is truly crisp only in `scale-mode="pixel"`** (integer factor + `Canvas.pixelPerfect` snaps vertices). In `auto` mode the *size* is still exactly N device-px per design-unit, but the element's position can land on a sub-pixel (auto mode does not snap), so text may be slightly soft.
- `Nx` only locks density. The font must also be authored so 1 source pixel = 1 design-unit — i.e. set `fontSize` to the font's native pixel height. `fontSize` ≠ native still misaligns.
- Box-preserving and the LayoutGroup caveat below apply identically to `Nx` (the inflation uses `1 / localScale`).
```

- [ ] **Step 3: 同步 "Variant-overridable" 一句**（同段末，约 line 632 现有 variant 说明处）补一句 `scale.mobile="2x"` 也走标准 variant 覆盖（如该句已足够泛化可跳过）。

- [ ] **Step 4: 校验 SKILL 内 `.ui.xml` 片段无 lint 问题**（若新增片段是 layout-group 子节点等需要校验的情形——本例 `<Text>` 在自由定位下，无 anchor/margin 违规，通常无需，但保险起见若 SKILL 有可独立 lint 的样例文件再跑；纯内联片段不强制）。

- [ ] **Step 5: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "doc(skill): document scale='Nx' device-density form (XML skill)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: master spec 同步 + 收尾

**Files:**
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`（relative-scale / `scale` 相关处）

- [ ] **Step 1: 定位 master spec 里 `scale` / relative-scale 的描述**

```bash
grep -n "scale" docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md | grep -i "localScale\|relative\|render density\|box-preserv" | head
```

- [ ] **Step 2: 在该处追加一行引用本特性**（措辞按上下文语言，英文/中文随该 spec 段落）

```markdown
> `scale="Nx"` (N positive integer): device-density form, `localScale = N / canvasFactor`
> (N physical pixels per design-unit, recomputed on factor change). For pixel-perfect
> bitmap text under `scale-mode="pixel"`. See
> [`2026-05-31-scale-device-density-design.md`](2026-05-31-scale-device-density-design.md).
```

若 master spec 当前完全没有 relative-scale 段落（即 box-preserving `scale` 当时也没回写 master spec），则在 §6（layout）末尾新增上面这段即可——不必为旧的 box-preserving 补写完整说明。

- [ ] **Step 3: 最终全套件复跑 + lint 收尾**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: 测试全绿；lint 退出码 0。

- [ ] **Step 4: Commit**

```bash
git add docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md
git commit -m "doc: reference scale='Nx' from master spec

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 5: 收尾**（不自动合并到 main —— CLAUDE.md 禁止）

汇报：分支 `feat/scale-device-density` 上所有 commit + 测试全绿。是否开 PR 由用户决定（参照项目 workflow：feature branch → PR → merge）。host Unity 里建议肉眼验证：pixel 模式切 2/3/4 倍 + 一段中文位图字 `scale="2x"`，看是否始终 24×24 且脆；拖窗口看是否实时重算。

---

## Self-Review

**Spec coverage**（逐条对 §3 改动面 / §6 测试矩阵）：

- SDD-D1/D2/D12 parser `Nx` → Task 1 ✓
- SDD-D3/D4/D5/D6 `localScale=N/factor` + `_canvasFactor` 缓存 + auto 公式 → Task 2 ✓
- SDD-D10 LayoutGroup 跳过 → Task 2 `DeviceScale_under_layout_group...` ✓
- SDD-D7/D8/D9 resize 走 ReSolve + `_hasDeviceScale` 门控 + 再入 guard → Task 3 ✓
- §6 box-preserving 不累积 → Task 3 `..._does_not_accumulate...` ✓
- §6 无 `Nx` 回归 → Task 3 `Resize_without_device_scale...` + Task 3 Step 5 ✓
- SDD-D13 XSD 无需改 → Task 4 Step 2 ✓
- SDD-D14 XML SKILL → Task 5 ✓
- SDD-D15 master spec → Task 6 ✓
- SDD-D11 `_canvasFactor<=0` 兜底 → Task 2 Step 3d `_canvasFactor > 0f ? ... : 1f` ✓

**Placeholder scan:** 无 TBD/TODO；每个 code step 有完整代码与确切命令。Task 4 Step 2 / Task 6 Step 2 是"核对/条件性"步骤，已写明具体判据与产出。

**Type consistency:**
- `TryParseDeviceScale(string, out int)` — Task 2 定义，Task 2(`ApplyScales`) + Task 3(`DeclaresDeviceScale`) 调用，签名一致 ✓
- `DeclaresDeviceScale(ElementNode)` — Task 3 定义并调用；`ElementNode` 命名空间在 Step 3c 标注需核对 ✓
- `_canvasFactor`(float) / `_hasDeviceScale`(bool) — Task 2 / Task 3 引入；Task 2 Step 3e 注释前向引用 `_hasDeviceScale`（仅注释，不影响编译）✓
- `relay.OnDimensionsChanged?.Invoke()` resize seam — 与 `ScreenScaleModeTests.Resize_event_recomputes_pixel_factor` 用法一致 ✓
- `ReSolve()` / `ApplyCanvasScaler()` / `ApplyBoxPreservingCompensation()` — 均为 `Screen.cs` 现有成员 ✓
