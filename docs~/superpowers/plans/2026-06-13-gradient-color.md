# 渐变色支持 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 颜色值语法新增"逗号分隔双色 = 上下渐变"（`color="#ffe08a,#b8860b"`），覆盖所有给 Graphic 上色的属性与状态色，不新增 XML 属性。

**Architecture:** 解析层 `ColorParser.TrySplitGradient`（纯 C#，lint 共享）→ 值模型 `ColorSpec`（`Runtime/Application/`）→ `UI.Theme.ResolveSpec` 统一解析 → 应用层 `ColorApplier`（纯色写 `Graphic.color`；渐变写 `GradientTint : BaseMeshEffect` 顶点色 + `Graphic.color=white`）；Text 走 TMP 原生 `colorGradient`；状态色把 `StateColorSet` 槽位从 `Color?` 拓宽为 `ColorSpec?`，reactor 预乘 Modulate 后经 ColorApplier 落地。

**Tech Stack:** Unity 6 uGUI / TMP / LitMotion（现有 tween）。测试经 UnityMCP（`refresh_unity` → `run_tests`，详见 CLAUDE.md），lint 经 `.lint/` dotnet format + UIXmlLint CLI。

**Spec:** `docs~/superpowers/specs/2026-06-13-gradient-color-design.md`（先通读，尤其 §2 定义处/引用处区别、§5 预乘与 Peek、§6 不支持名单）。

**约定（每个 Task 通用，不再重复写步骤）：**
- 测试跑法：先 `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`，再 `mcp__UnityMCP__read_console(action="get", types=["error"])` 确认零编译错误，然后 `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["<测试类名>"])`，轮询 `get_test_job` 到完成。**Task 12 跑全量，不带 group 过滤。**
- 每个 Task 结尾：`cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`（首次需先 `dotnet restore PromptUGUI.Lint.slnx`），然后 git commit（feature 分支，永不碰 main）。
- 新建 .cs 文件后 refresh 会生成 `.meta`——**commit 时连同 `.meta` 一起提交**（PR #60 的教训）。
- EditMode 测试类碰 `UI` 的，`[SetUp]`/`[TearDown]` 都要 `UI.ResetForTests()`。

---

### Task 0: 建分支 + 提交 spec

**Files:** 无代码。

- [ ] **Step 1:** `git checkout -b feat/gradient-color`
- [ ] **Step 2:** `git add docs~/superpowers/specs/2026-06-13-gradient-color-design.md docs~/superpowers/plans/2026-06-13-gradient-color.md && git commit -m "docs: gradient color spec + plan"`

---

### Task 1: `ColorParser.TrySplitGradient`（纯 C# 切分，lint 共享）

**Files:**
- Modify: `Runtime/Core/Parser/ColorParser.cs`
- Test: `Tests/EditMode/Parser/ColorParserGradientTests.cs`（新建）

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    public class ColorParserGradientTests
    {
        [Test]
        public void NoComma_ReturnsSingleSegment()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("black/0.5", out var top, out var bottom, out var err));
            Assert.AreEqual("black/0.5", top);
            Assert.IsNull(bottom);
            Assert.IsNull(err);
        }

        [Test]
        public void TwoSegments_SplitAndTrimmed()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("#ffe08a, #b8860b", out var top, out var bottom, out _));
            Assert.AreEqual("#ffe08a", top);
            Assert.AreEqual("#b8860b", bottom);
        }

        [TestCase("a,b,c")]
        [TestCase("a,")]
        [TestCase(",b")]
        [TestCase(",")]
        public void Malformed_Fails(string raw)
        {
            Assert.IsFalse(ColorParser.TrySplitGradient(raw, out _, out _, out var err));
            StringAssert.Contains("gradient", err);
        }

        [Test]
        public void Empty_IsSingleNullSegment_HandledByCaller()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("", out var top, out var bottom, out _));
            Assert.AreEqual("", top);
            Assert.IsNull(bottom);
        }
    }
}
```

- [ ] **Step 2:** refresh + 跑 `group_names=["ColorParserGradientTests"]`，预期 FAIL（方法不存在 → 编译错，先确认 console 报 CS 错误即为"红"）。
- [ ] **Step 3: 实现**（加在 `TrySplitAlpha` 后面）

```csharp
/// <summary>
/// Splits an optional two-stop gradient value on ','. <c>"#fff,#000"</c> → top <c>"#fff"</c>,
/// bottom <c>"#000"</c>; no comma → top = raw, bottom = null. Segments are trimmed (authors
/// write <c>"a, b"</c>). Each segment still carries its own token / <c>/alpha</c> form —
/// this method does NOT validate segment contents, only the split shape.
/// Returns false when there are &gt;2 segments or any segment is empty.
/// </summary>
public static bool TrySplitGradient(string raw, out string top, out string bottom, out string error)
{
    top = raw;
    bottom = null;
    error = null;
    if (string.IsNullOrEmpty(raw)) return true;   // empty handled by caller

    var comma = raw.IndexOf(',');
    if (comma < 0) return true;

    if (raw.IndexOf(',', comma + 1) >= 0)
    {
        error = $"color \"{raw}\": gradient supports exactly two colours (top,bottom)";
        return false;
    }

    var head = raw.Substring(0, comma).Trim();
    var tail = raw.Substring(comma + 1).Trim();
    if (head.Length == 0 || tail.Length == 0)
    {
        error = $"color \"{raw}\": gradient segment is empty — expected \"top,bottom\"";
        return false;
    }

    top = head;
    bottom = tail;
    return true;
}
```

- [ ] **Step 4:** refresh + 跑同 group，预期 PASS。
- [ ] **Step 5:** lint + commit `feat: ColorParser.TrySplitGradient comma split`

---

### Task 2: `ColorSpec` 值模型 + `ThemeStore` 拓宽 + 定义处校验

**Files:**
- Create: `Runtime/Application/ColorSpec.cs`
- Modify: `Runtime/Application/ThemeStore.cs`（`Entry.Colors`、`Register`、`ReplaceFromSrc`、`LookupChained`）
- Modify: `Runtime/Application/UI.cs` `RegisterThemesAndAutoSet`（~line 916：每个 token 值过 `TrySplitGradient` 后逐段 `ColorUtility.TryParseHtmlString`）
- Modify: `Runtime/Application/UI.cs` `Theme.Lookup`（LookupChained 返回类型变了；public 签名保持 `Color?`，渐变 token 返回 `Top`，加 doc 注释）
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs` ParseTheme（~line 116：`ColorParser.TryParseHtmlString(cv)` 改为先 `TrySplitGradient` 再逐段校验；段带 `/` 或非字面量 → 现有 "invalid color literal" 错误文案补充段信息）
- Modify: `Runtime/Editor` 若 HotReload 路径调 `ReplaceFromSrc`（grep `ReplaceFromSrc` 调用方，签名跟随拓宽）
- Test: `Tests/EditMode/Application/ThemeGradientTests.cs`（新建）；`Tests/EditMode/Parser/` 现有 Theme 解析测试类追加定义处错误用例

**`ColorSpec` 完整代码：**

```csharp
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// A resolved colour value: solid, or a two-stop vertical gradient (Top → Bottom).
    /// Produced by <see cref="UI.Theme.ResolveSpec"/>; applied by <c>ColorApplier</c>
    /// (vertex-tint slot) or the TMP vertex-gradient path in <c>Text</c>.
    /// Solid values keep <see cref="Bottom"/> == <see cref="Top"/> so consumers that
    /// only need one colour can read Top unconditionally.
    /// </summary>
    internal readonly struct ColorSpec
    {
        public readonly Color Top;
        public readonly Color Bottom;
        public readonly bool IsGradient;

        private ColorSpec(Color top, Color bottom, bool isGradient)
        {
            Top = top;
            Bottom = bottom;
            IsGradient = isGradient;
        }

        public static ColorSpec Solid(Color c) => new(c, c, false);
        public static ColorSpec Gradient(Color top, Color bottom) => new(top, bottom, true);

        /// <summary>Component-wise multiply both stops (modulate premultiply, spec §5).</summary>
        public ColorSpec Multiply(Color m) => new(Top * m, Bottom * m, IsGradient);
    }
}
```

**ThemeStore 拓宽要点**（机械替换 `Color` → `ColorSpec`）：
- `Entry.Colors`: `Dictionary<string, ColorSpec>`
- `Register(string name, string baseName, IReadOnlyDictionary<string, ColorSpec> colors, string src)`
- `ReplaceFromSrc` 元组里的 `colors` 同步拓宽
- `LookupChained(string, string)` 返回 `ColorSpec?`

**`RegisterThemesAndAutoSet` 内层改为：**

```csharp
foreach (var ce in theme.Colors)
{
    Parser.ColorParser.TrySplitGradient(ce.Value, out var topRaw, out var bottomRaw, out _);
    UnityEngine.ColorUtility.TryParseHtmlString(topRaw, out var top);
    if (bottomRaw == null)
    {
        colors[ce.Name] = ColorSpec.Solid(top);
    }
    else
    {
        UnityEngine.ColorUtility.TryParseHtmlString(bottomRaw, out var bottom);
        colors[ce.Name] = ColorSpec.Gradient(top, bottom);
    }
}
```

（坏值在 ParseTheme 已拦截，这里沿用现有"信任 parser"风格不再报错。）

**ParseTheme 校验改为：**

```csharp
if (!ColorParser.TrySplitGradient(cv, out var gTop, out var gBottom, out var gErr))
    throw new ParseException($"<Color name=\"{cn}\" value=\"{cv}\">: {gErr}");
if (!ColorParser.TryParseHtmlString(gTop)
    || (gBottom != null && !ColorParser.TryParseHtmlString(gBottom)))
    throw new ParseException(
        $"<Color name=\"{cn}\" value=\"{cv}\">: invalid color literal" +
        (gBottom != null ? " (each gradient segment must be a hex/named literal — no tokens, no /alpha)" : ""));
```

- [ ] **Step 1: 写失败测试**（ThemeGradientTests：注册含渐变 token 的主题 → `ThemeStore.Instance.LookupChained("t","grad")` 拿到 IsGradient + 两端值；base 链上的渐变 token 可继承；solid token 不回归。Parser 侧：`value="#fff,#000,#111"`、`value="#fff,"`、`value="gold,black"`（token 段）、`value="#fff/0.5,#000"`（/alpha 段）各报 ParseException 且文案含 "gradient" 或 "invalid color literal"。fake-files 模式参照 `Tests/EditMode/Application/UIThemeTests.cs`。）
- [ ] **Step 2:** refresh + 跑红（编译错或断言失败均可）。
- [ ] **Step 3:** 按上述实现；grep `LookupChained\|ThemeStore.Instance.Register\|ReplaceFromSrc` 修齐所有编译错（含 `Theme.Lookup` 返回 `hit?.Top`）。
- [ ] **Step 4:** refresh + 跑 `group_names=["ThemeGradientTests","UIThemeTests"]` 及被改的 parser 测试类，PASS。
- [ ] **Step 5:** lint + commit `feat: ColorSpec + ThemeStore gradient tokens`

---

### Task 3: `UI.Theme.ResolveSpec` + 旧 `Resolve` 拒绝渐变

**Files:**
- Modify: `Runtime/Application/UI.cs` `Theme` 类（~line 448）
- Test: `Tests/EditMode/Application/ThemeGradientTests.cs`（追加）

**实现（替换现有 `Resolve`，`ResolveBase` 保留不动）：**

```csharp
/// <summary>
/// Resolve a colour value that may be a two-stop vertical gradient ("top,bottom").
/// Each segment independently supports theme tokens, hex/named literals and the
/// /alpha suffix. A whole-value token may itself BE a gradient token; "/alpha" on
/// it replaces BOTH stops' alpha. A gradient token used as a segment is an error
/// (no nested gradients).
/// </summary>
internal static ColorSpec ResolveSpec(string value)
{
    if (string.IsNullOrEmpty(value))
        throw new System.Exception("empty color value");

    if (!Parser.ColorParser.TrySplitGradient(value, out var topRaw, out var bottomRaw, out var gErr))
        throw new System.Exception(gErr);

    if (bottomRaw == null)
        return ResolveSingle(topRaw, allowGradientToken: true);

    var top = ResolveSingle(topRaw, allowGradientToken: false);
    var bottom = ResolveSingle(bottomRaw, allowGradientToken: false);
    return ColorSpec.Gradient(top.Top, bottom.Top);
}

/// <summary>One segment: token / literal + optional /alpha. Solid unless the whole
/// segment is a gradient token AND allowGradientToken.</summary>
private static ColorSpec ResolveSingle(string value, bool allowGradientToken)
{
    if (!Parser.ColorParser.TrySplitAlpha(value, out var baseValue, out var alpha, out var err))
        throw new System.Exception(err);

    var spec = ResolveBaseSpec(baseValue);
    if (spec.IsGradient && !allowGradientToken)
        throw new System.Exception(
            $"color \"{value}\": token resolves to a gradient — gradients cannot nest inside a gradient");
    if (alpha.HasValue)
    {
        var t = spec.Top; t.a = alpha.Value;
        var b = spec.Bottom; b.a = alpha.Value;
        spec = spec.IsGradient ? ColorSpec.Gradient(t, b) : ColorSpec.Solid(t);
    }
    return spec;
}

public static UnityEngine.Color Resolve(string value)
{
    var spec = ResolveSpec(value);
    if (spec.IsGradient)
        throw new System.Exception(
            $"color \"{value}\": this attribute does not support gradient colors");
    return spec.Top;
}
```

`ResolveBaseSpec` = 现有 `ResolveBase` 的 ColorSpec 版：theme 命中返回 `LookupChained` 的 `ColorSpec`；hex/named → `ColorSpec.Solid`；soft-fail 路径返回 `ColorSpec.Solid(white)`。把现 `ResolveBase` 的函数体改造为它（原 `ResolveBase` 删除或改为 `ResolveBaseSpec(value).Top`——只剩 `Lookup` 用得到的话直接删）。

- [ ] **Step 1: 失败测试**（追加到 ThemeGradientTests）：
  - `ResolveSpec("#fff,#000")` → IsGradient、Top=white、Bottom=black
  - `ResolveSpec("gold-grad")`（注册过的渐变 token）→ IsGradient
  - `ResolveSpec("gold-grad/0.5")` → 两端 a==0.5
  - `ResolveSpec("accent,accent-dark/0.5")`（两 token，第二段带 alpha）→ Bottom.a==0.5
  - `ResolveSpec("gold-grad,black")` → throws，文案含 "cannot nest"
  - `Resolve("#fff,#000")` / `Resolve("gold-grad")` → throws，文案含 "does not support gradient"
  - `ResolveSpec("plain-token")`（纯色 token）→ !IsGradient（不回归）
  - 未注册主题 soft-fail：`Theme.Set("nope")` 后 `ResolveSpec("sometoken")` → Solid(white)
- [ ] **Step 2:** refresh + 红。
- [ ] **Step 3:** 实现如上。
- [ ] **Step 4:** refresh + 跑 `group_names=["ThemeGradientTests","UIThemeTests"]` PASS。**另跑一次全量 EditMode**：`Resolve` 行为变化（逗号值从"ColorUtility 解析失败"变为明确 throw）可能影响现有负例测试。
- [ ] **Step 5:** lint + commit `feat: UI.Theme.ResolveSpec gradient resolution`

---

### Task 4: `GradientTint` 顶点修改器

**Files:**
- Create: `Runtime/Controls/Internal/GradientTint.cs`
- Test: `Tests/EditMode/Controls/GradientTintTests.cs`（新建）

**完整代码：**

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Two-stop vertical gradient tint as a vertex-colour effect (spec §4.2). Multiplies
    /// Lerp(Bottom, Top, normalizedY) into each vertex's existing colour, so the final
    /// composite stays <c>texture × Graphic.color × gradient</c> — the Graphic.color slot
    /// remains free for state modulates. Y is normalized across the actual mesh bounds
    /// (Sliced/Tiled have &gt;4 verts; vertex order is not assumed). Lazy-added by
    /// <see cref="ColorApplier"/> and toggled via <c>enabled</c>, never destroyed
    /// (Variant/ReSolve round-trips, same convention as ApplyViewportMask).
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    internal sealed class GradientTint : BaseMeshEffect
    {
        private Color _top = Color.white;
        private Color _bottom = Color.white;

        public void Set(Color top, Color bottom)
        {
            if (_top == top && _bottom == bottom) return;
            _top = top;
            _bottom = bottom;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public Color Top => _top;
        public Color Bottom => _bottom;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;

            var v = new UIVertex();
            float minY = float.MaxValue, maxY = float.MinValue;
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                if (v.position.y < minY) minY = v.position.y;
                if (v.position.y > maxY) maxY = v.position.y;
            }

            var h = maxY - minY;
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                var t = h > 0f ? (v.position.y - minY) / h : 1f;
                v.color *= Color.Lerp(_bottom, _top, t);
                vh.SetUIVertex(v, i);
            }
        }
    }
}
```

注意 `OnEnable`/`OnDisable`：`BaseMeshEffect` 基类已经在 enable/disable 时 `SetVerticesDirty`，不用自己写。

- [ ] **Step 1: 失败测试**（EditMode 可直接驱动 `ModifyMesh`）：

```csharp
// 手工构造 VertexHelper：4 顶点 quad（y ∈ {0,100}，色全 white），
// effect.Set(red, blue) 后 ModifyMesh，断言 y=100 顶点 color≈red、y=0 ≈blue。
// 第二个用例：先把顶点色设为 (0.5,0.5,0.5,1)，断言乘法叠加（y=100 → 0.5*red）。
// 第三个用例：enabled=false 时 ModifyMesh 不改顶点。
// 第四个用例：Set 两次相同值不触发 SetVerticesDirty 副作用（可只断言值幂等）。
// VertexHelper 用法：var vh = new VertexHelper(); vh.AddVert(pos, color, uv);
// 读回：UIVertex v = default; vh.PopulateUIVertex(ref v, i);
// 组件挂在带 Image 的 GameObject 上（RequireComponent），TearDown DestroyImmediate。
```

- [ ] **Step 2:** refresh + 红。
- [ ] **Step 3:** 实现如上（含 .meta 提交）。
- [ ] **Step 4:** refresh + `group_names=["GradientTintTests"]` PASS。
- [ ] **Step 5:** lint + commit `feat: GradientTint vertex effect`

---

### Task 5: `ColorApplier`（Apply / Peek）

**Files:**
- Create: `Runtime/Controls/Internal/ColorApplier.cs`
- Test: `Tests/EditMode/Controls/ColorApplierTests.cs`（新建）

**完整代码：**

```csharp
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Single chokepoint for landing a resolved <see cref="ColorSpec"/> on a Graphic
    /// (spec §4.1). Solid → <c>graphic.color</c> (and disables any GradientTint);
    /// gradient → GradientTint vertex slot + <c>graphic.color = white</c> so the
    /// Graphic.color slot stays free for state modulates. GradientTint is lazy-added
    /// and toggled, never destroyed.
    /// </summary>
    internal static class ColorApplier
    {
        public static void Apply(Graphic g, ColorSpec spec)
        {
            if (spec.IsGradient)
            {
                var tint = g.GetComponent<GradientTint>() ?? g.gameObject.AddComponent<GradientTint>();
                tint.Set(spec.Top, spec.Bottom);
                tint.enabled = true;
                g.color = Color.white;
            }
            else
            {
                var tint = g.GetComponent<GradientTint>();
                if (tint != null) tint.enabled = false;
                g.color = spec.Top;
            }
        }

        /// <summary>Read back the currently-applied value — gradient if a GradientTint is
        /// enabled, else the plain graphic colour. Used by StateTintReactor base capture.</summary>
        public static ColorSpec Peek(Graphic g)
        {
            var tint = g.GetComponent<GradientTint>();
            return tint != null && tint.enabled
                ? ColorSpec.Gradient(tint.Top, tint.Bottom)
                : ColorSpec.Solid(g.color);
        }
    }
}
```

- [ ] **Step 1: 失败测试**：纯色 Apply 写 color；渐变 Apply 加组件 + enabled + color==white；渐变→纯色往返组件仍在但 disabled（**断言不被 Destroy**）；Peek 双态读回正确。
- [ ] **Step 2:** refresh + 红。 **Step 3:** 实现。 **Step 4:** `group_names=["ColorApplierTests"]` PASS。 **Step 5:** lint + commit `feat: ColorApplier solid/gradient chokepoint`

---

### Task 6: Graphic 类 setter 全量切换（一档属性）

**Files（全部 Modify，模式统一：`X.color = UI.Theme.Resolve(value)` → `ColorApplier.Apply(X, UI.Theme.ResolveSpec(value))`）：**

| 文件 | 属性（行号当前值，执行时以 grep 为准） |
|---|---|
| `Runtime/Controls/Image.cs:48` | `Color`（_img） |
| `Runtime/Controls/Icon.cs:48` | `Color`（_img） |
| `Runtime/Controls/RawImage.cs:48` | `Color`（_raw） |
| `Runtime/Controls/Btn.cs:167` | `Color`（_bg） |
| `Runtime/Controls/Tab.cs:268` | `Color`（_bg） |
| `Runtime/Controls/Toggle.cs:123` | `Color`（_bg） |
| `Runtime/Controls/Dropdown.cs:193,221` | `Color`（_bg）、`PopupColor`（_templateBg） |
| `Runtime/Controls/InputField.cs:232` | `Color`（_bg）——**仅 bg**；TextColor/PlaceholderColor/CaretColor/SelectionColor 保持旧 `Resolve`（spec 作用域注） |
| `Runtime/Controls/Progress.cs:90,111,134` | `Color`（_fill）、`BgColor`（_bg）、`FrameColor`（_frame） |
| `Runtime/Controls/ScrollList.cs:175,222` | `Color`（_bg）、`FrameColor`（EnsureFrame()） |
| `Runtime/Controls/Slider.cs:117` | `BgColor`（_bg） |

不动：`Carousel` dotColor/dotSelectedColor、`Tab/Toggle.SelectedColor`（Task 8 处理）、`*Modulate`（保持纯色，Task 8）、AnimationSpec、MarkdigRenderer。

- Test: `Tests/EditMode/Controls/GradientColorAttrTests.cs`（新建，fake-files + `UI.LoadDocument` 模式参照现有 Controls 测试）

- [ ] **Step 1: 失败测试**：
  - `<Image color="#fff,#000">` → Open 后 `_img` 的 GameObject 上有 enabled 的 `GradientTint`（Top=white、Bottom=black）且 `_img.color==white`
  - `<Icon ...>`、`<Btn color="#fff,#000">` 同型抽查（不必 11 个属性全写，每类代表 1 个：Image / Btn bg / Progress fill / ScrollList frame）
  - 纯色不回归：`<Image color="#ff0000">` → color==red、无 enabled 的 GradientTint
  - **ReSolve 往返**：Variant 把 `color.mobile="#fff"`（纯色）覆盖渐变基色 → 激活 Variant 后 GradientTint disabled、color==white→red；关 Variant 还原渐变（参照现有 Variants 测试的激活模式）
  - `<Btn hoverModulate="#fff,#000">` → Open 抛错/LogError，文案含 "does not support gradient"（此时 Modulate 仍走旧 Resolve，自动正确）
- [ ] **Step 2:** refresh + 红。 **Step 3:** 机械替换上表（各文件 `using PromptUGUI.Controls.Internal;` 已基本都在，缺则补）。 **Step 4:** `group_names=["GradientColorAttrTests"]` + **全量 EditMode**（防 setter 行为差异回归）。 **Step 5:** lint + commit `feat: gradient on all Graphic color attrs`

---

### Task 7: Text 的 TMP 渐变路径

**Files:**
- Modify: `Runtime/Controls/Text.cs:86-90`（`Color` setter）
- Test: `Tests/EditMode/Controls/GradientColorAttrTests.cs`（追加）

**实现：**

```csharp
[UIAttr(IsColor = true), Preserve]
public string Color
{
    set
    {
        var spec = UI.Theme.ResolveSpec(value);
        if (spec.IsGradient)
        {
            _tmp.enableVertexGradient = true;
            _tmp.colorGradient = new TMPro.VertexGradient(spec.Top, spec.Top, spec.Bottom, spec.Bottom);
            _tmp.color = UnityEngine.Color.white;
        }
        else
        {
            _tmp.enableVertexGradient = false;
            _tmp.color = spec.Top;
        }
    }
}
```

- [ ] **Step 1: 失败测试**：`<Text color="#fff,#000">` → `enableVertexGradient==true`、`colorGradient.topLeft==white`、`bottomRight==black`、`color==white`；纯色往返把 `enableVertexGradient` 关回 false。
- [ ] **Step 2:** 红。 **Step 3:** 实现。 **Step 4:** group + PASS。 **Step 5:** lint + commit `feat: Text gradient via TMP VertexGradient`

---

### Task 8: 状态色拓宽（StateColorSet / StateTintReactor / Installer / Btn / Tab / Toggle）

**Files:**
- Modify: `Runtime/Controls/Internal/StateColorSet.cs` — 槽位 `Color?` → `ColorSpec?`；`Resolve` 拆成两个工厂：

```csharp
/// <summary>Absolute base overrides — gradients allowed (spec §5).</summary>
public static StateColorSet ResolveAbsolutes(string hover, string pressed, string selected, string disabled)
    => new(RSpec(hover), RSpec(pressed), RSpec(selected), RSpec(disabled));

/// <summary>Relative multipliers — solid only; a gradient value throws (spec §6).</summary>
public static StateColorSet ResolveModulates(string hover, string pressed, string selected, string disabled)
    => new(RSolid(hover), RSolid(pressed), RSolid(selected), RSolid(disabled));

private static ColorSpec? RSpec(string v)
    => string.IsNullOrWhiteSpace(v) ? (ColorSpec?)null : UI.Theme.ResolveSpec(v);
private static ColorSpec? RSolid(string v)
    => string.IsNullOrWhiteSpace(v) ? (ColorSpec?)null : ColorSpec.Solid(UI.Theme.Resolve(v));
```

- Modify: `Runtime/Controls/Internal/StateTintReactor.cs`：
  - `_baseColor`: `Color` → `ColorSpec`；捕获改 `_baseColor = ColorApplier.Peek(_graphic)`（`EnsureInit`，line 53-57）
  - `_selectedBase`: `Color?` → `ColorSpec?`；`Configure` 参数同步
  - `BaseFor` 返回 `ColorSpec`；`MultiplierFor` 返回 `Color`（mod 槽恒纯色，取 `.For(state)?.Top ?? white`）
  - `OnState`：

```csharp
private void OnState(InteractState state)
{
    if (_graphic == null) return;
    var target = BaseFor(state).Multiply(MultiplierFor(state));   // 预乘，spec §5

    if (_handle.IsActive()) _handle.TryCancel();

    var current = ColorApplier.Peek(_graphic);
    // Gradients snap (no Color-lerp path for vertex gradients); solid↔solid keeps the tween.
    if (TestForceInstant || _fade <= 0f
        || target.IsGradient || current.IsGradient
        || CrossesTransparency(current.Top, target.Top))
    {
        ColorApplier.Apply(_graphic, target);
        return;
    }

    _handle = LMotion.Create(_graphic.color, target.Top, _fade)
        .Bind(_graphic, static (c, g) => g.color = c);
}
```

- Modify: `Runtime/Controls/Internal/StateTintInstaller.cs` — `selectedBase` 参数 `Color?` → `ColorSpec?`（line 22、48、76）
- Modify: `Runtime/Controls/Btn.cs` / `Tab.cs` / `Toggle.cs` 的 `OnAfterApply`：`StateColorSet.Resolve(...)` 调用点 → abs 用 `ResolveAbsolutes`、mod 用 `ResolveModulates`；Tab/Toggle 的 `selectedBase` 解析 `UI.Theme.Resolve(_selectedColor)` → `UI.Theme.ResolveSpec(_selectedColor)`（grep `StateColorSet.Resolve` 找全调用点）
- Test: `Tests/EditMode/Controls/StateGradientTests.cs`（新建；`StateTintReactor.TestForceInstant = true` 模式参照现有 state-visuals 测试）

- [ ] **Step 1: 失败测试**：
  - 渐变 `hoverColor`：`<Btn color="#888" hoverColor="#fff,#000">` → 驱动 broadcaster 进 Hover（参照现有 Btn state 测试的驱动方式）→ bg 有 enabled GradientTint(white,black)；回 Normal → disabled、color==#888 还原
  - 渐变**基色** + 纯色 hoverColor：hover 时 GradientTint disabled、color==hoverColor；回 Normal 渐变还原（验证 Peek 基色捕获）
  - 渐变基色 × `pressedModulate="#808080"`：Pressed 时 GradientTint 两端 ≈ 基色×0.5（预乘）
  - 渐变 `selectedColor`（Tab isOn）→ 选中态顶点槽生效，hover 离开后保持
  - ReSolve 后渐变状态不丢（模拟 re-Configure：参照 47d181b 的既有测试）
  - `hoverModulate="#fff,#000"` → throw "does not support gradient"
- [ ] **Step 2:** 红。 **Step 3:** 实现上述全部。 **Step 4:** `group_names=["StateGradientTests"]` + **全量 EditMode**（reactor 是高扇出热点，必须全量）。 **Step 5:** lint + commit `feat: gradient state colors (absolutes + selectedBase)`

---

### Task 9: Lint 规则更新（CLI + runtime 共享）

**Files:**
- Modify: `Runtime/Core/Lint/ColorLiteralRules.cs` — `Check` 开头先 `TrySplitGradient`：切分失败（>2 段 / 空段）→ 新 issue `PUI-COLOR-GRADIENT-MALFORMED`（静态可查，token 也逃不掉）；切分成功则对每段跑现有 alpha+hex 检查（提取 per-segment local func）。
- Modify 同文件或新增 `GradientModulateRules.cs`：遍历 `node.Attributes` 中名字以 `Modulate` 结尾的属性，值含 `,` → `PUI-GRADIENT-MODULATE`（"Modulate is a multiplier — gradients are not supported"）。挂进 `IRWalker`（参照现有规则注册方式，纯 dispatch 零运行时成本）。
- Test: `Tests/EditMode/Lint/`（参照现有 lint 测试文件命名，新建 `GradientLintTests.cs`）

- [ ] **Step 1: 失败测试**：`color="#fff,#000,#111"` → MALFORMED；`color="#fff,#zzz"` → 现有 LITERAL-INVALID（段内）；`color="#fff,#000"` → 无 issue；`hoverModulate="a,b"` → GRADIENT-MODULATE；`hoverColor="a,b"` → 无 issue。
- [ ] **Step 2:** 红。 **Step 3:** 实现。 **Step 4:** `group_names=["GradientLintTests"]` + 现有 lint 测试类 PASS；另跑 `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` 确认内置 XML 无新告警。 **Step 5:** lint + commit `feat: lint rules for gradient values`

---

### Task 10: XSD / EditorOnly 校验

**Files:** 预期零改动（颜色属性在 XSD 是 `xs:string`）；若 EditorOnly 套件红了才动 `Editor/` 的 XSD 生成器。

- [ ] **Step 1:** `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])` → 预期 PASS。红了则按失败的 substring 断言修生成器，再 commit `fix: xsd for gradient values`。

---

### Task 11: SKILL 更新（英文，同 PR）

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md` — Color tokens 一节加：逗号双色语法（第一色在顶）、定义处字面量 only vs 引用处 token+`/alpha`、`token/alpha` 作用于两端、不支持名单（`*Modulate`、char-color、InputField text/placeholder/caret/selection、Carousel dot*）、Text 是 TMP **逐字符**渐变语义、Frame 无 color 不适用。
- Modify: `.claude/skills/authoring-promptugui-xml/reference/states.md` — `*Color`（含 `selectedColor`）接受渐变 + snap 不 tween；`*Modulate` 不接受。

- [ ] **Step 1:** 写两处文档（对照 spec §2/§5/§6 核对每条）。
- [ ] **Step 2:** commit `docs: gradient color in XML skill`

---

### Task 12: 全量验证 + PlayMode 冒烟

**Files:**
- Test: `Tests/PlayMode/` 新增 `GradientPlayTests.cs`（参照 `CarouselPlayTests` 的结构）：开一个 `<Btn color="#fff,#000" hoverModulate="#808080">` 屏，帧循环后断言 GradientTint enabled 且 Btn 可正常点击（EventSystem 真实路径——common-controls sample 的教训：state 链路要在真 play 下走一遍）。

- [ ] **Step 1:** PlayMode 冒烟测试写好跑红→实现为绿（多半零实现，纯验证）。
- [ ] **Step 2:** 三套全跑（**全量，不带 group**，feedback 教训）：
  - `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])`
  - `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])`
  - `run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])`
  - `read_console(types=["error"])` 零错误
- [ ] **Step 3:** `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx` + `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/`
- [ ] **Step 4:** commit + push 分支 + `gh pr create`（标题 `feat: gradient color value syntax`，body 引 spec 路径，结尾带 Claude Code 署名）。视觉 QA（金字标题 / 渐变按钮 hover）留给用户。

---

## 自检记录

- **Spec 覆盖**：§2 语法→Task 1/3；§2.1 错误→Task 1/2/3/9；§3 值模型→Task 2/3；§4.1 helper→Task 5；§4.2 GradientTint→Task 4；§4.3 Text→Task 7；§5 状态色→Task 8；§6 不支持→Task 3（Resolve throw）+ Task 9（静态）+ Task 6 范围注；§7 ReSolve/Variant→Task 6/8 测试；§8 测试→各 Task + Task 12；§9 SKILL→Task 11。
- **类型一致性**：`ColorSpec.Solid/Gradient/Multiply/IsGradient/Top/Bottom`、`ResolveSpec`、`ResolveSingle(allowGradientToken)`、`ColorApplier.Apply/Peek`、`GradientTint.Set/Top/Bottom`、`StateColorSet.ResolveAbsolutes/ResolveModulates` 在各 Task 间已对拍。
- **已知执行期注意**：Task 3 改 `Resolve` 抛错路径后必须全量跑（负例测试可能依赖旧行为）；Task 8 的 `StateColorSet.Resolve` 旧名删除会把所有调用点变编译错——grep 找全（Btn/Tab/Toggle 至少六处）。
