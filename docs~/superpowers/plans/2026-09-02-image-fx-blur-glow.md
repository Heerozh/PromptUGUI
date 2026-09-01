# `<Image>` / `<Icon>` 的 sprite 级模糊与外发光（Image Fx，M1）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `<Image>` / `<Icon>` 获得 `blur="N"`（本体像素圆盘模糊）、`glow="N"`（自剪影外发光，垫在本体下）、`glowColor=`（未写 = 自体模糊色）三个属性；零 fx 时零开销、不破合批；`tint="linear"` 与禁用灰度折进同一个 shader；图集打包卫生进 `SpriteAtlasSyncer`。M1 覆盖 ≤ ~12px 的小半径，大半径 / `<RawImage>` / `<Btn>` 底图留 M2。

**Architecture:** `FxImage : UnityEngine.UI.Image`（`Icon` / `Image` 一律创建它）在 `OnPopulateMesh` 里调 `base` 后由纯几何的 `FxMesh.Inflate` 外扩 quad、外推 uv0、写 `uv1`（sprite UV 矩形）/ `uv2`（uv/px 换算）；作者参数进 `FxParams`，由 `FxMaterialCache`（`ProceduralMaterialCache` 同构）按参数集共享材质；`UI-ImageFx.shader` 全 uniform 分支：矩形钳制的多 tap 采样一轮做 blur（预乘累加）、一轮做 glow 覆盖率（`g²` 衰减，与 SDF 外发光同形），`PuguiOver` 合成，Linear Light 与去饱和折在末尾。`ISelfGrayscale` 让 `DisabledGrayscaleController` 对 `FxImage` 与 `ProceduralPanel` 同一分派。

**Tech Stack:** Unity 6 / C# (LangVersion 9.0)、uGUI（`Image.OnPopulateMesh` / `VertexHelper` / `Canvas.additionalShaderChannels`）、纯 CG shader（Runtime/Resources，不含 SRP 头）。无新增包。

设计依据：`docs~/superpowers/specs/2026-09-02-image-fx-blur-glow-design.md`（下文 §N 指该 spec；已定决策见 spec §9）。先例：`ProceduralPanel` / `ProceduralMaterialCache`（自持材质 + 缓存）、`DecorPanel.OnPopulateMesh`（外扩 quad + `uv1`）、`RotateFlipEffect`（叶子 Graphic 的网格级属性）。

## Global Constraints

- **仓库真实路径**：`C:/xsoft/PromptUGUI`（`ssw_re/Game/PromptUGUI` 是指向它的符号链接）。所有 `git` / `dotnet` 命令 `cd C:/xsoft/PromptUGUI` 后执行。
- **分支**：全部工作在 `feat/image-fx-blur-glow`（Task 0 建）。**绝不提交到 main。**
- **LangVersion 9.0**：无 primary constructor、无 collection expression `[]`、无 `[field: SerializeField]`。`in` 参数 / target-typed `new()` / `??=` 可用。
- **Core 纯 C# 子集**：`Runtime/Core/Lint/ImageFxRules.cs` 与 `StyleRules.cs` 的改动 **不得** `using UnityEngine`。`FxMesh` / `FxImage` / `FxMaterialCache` 在 `Controls/Internal`，不受此限。
- **shader 不含 SRP 头文件**：`UI-ImageFx.shader` / `UI-ImageTint.cginc` 只 `#include "UnityCG.cginc"` / `"UnityUI.cginc"` / 本目录的 `.cginc`（理由见 `UI-GlassBlur.shader` 头注释）。
- **lint**：每个 Task 收尾从仓库根跑 `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`。**不要** `dotnet format analyzers --severity info`。
- **CLI 编译守门**：Task 6 之后跑 `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/`，确认规则改动在 Unity 外仍编译、内置文档零 error。
- **同 PR 必改 SKILL**（英文）：见 Task 10。
- **测试只经 Unity MCP 跑**。**禁止** `execute_menu_item("Assets/Reimport All")`；用 `refresh_unity(mode="force", scope="all")`。外部写入 `.cs` / `.shader` 后如出现诡异 CS0246，按「删文件 + meta → refresh → 重写」处理。
- **InternalsVisibleTo** 已对 `PromptUGUI.Tests.EditMode` / `PromptUGUI.Tests.PlayMode` / `PromptUGUI.Tests.EditorOnly` 开放。
- **Red first**：每个 Task 先写失败测试、跑一次看它**因正确的原因**失败，再写实现。
- **渲染测试纪律**：第一条永远是夹具自检；每个阈值必须在**关掉功能的那一版**上验过一次（fx 关 → 断言反向成立）；不靠放宽容差过绿，先看 PNG dump。
- **零回归守门**：`ImageRotateFlipTests` / `RotateFlipEffectTests` / `GradientTintTests` / `GradientTintStopTests` / `GradientColorAttrTests` / `GradientStopRenderTests` / `ImageTintTests` / `RawImageTests` / `DisabledGrayscaleTests` / `AttributeReversibilityTests` / `BtnStateTests` / `DecorRenderTests` / `ProceduralSurfaceRenderTests` / `GlassRenderTests` / `SpriteAtlasSyncerTests` / `XsdGeneratorTests` 每个里程碑末尾必跑。

**RUN(ClassName) = 跑 EditMode 测试的标准流程：**
1. `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`；返回 idle 后再等 ~15s（编译可能尚未真正结束）
2. `mcp__UnityMCP__read_console(action="get", types=["error"])` —— 编译错误必须为空才继续
3. `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["ClassName"])` → 轮询 `mcp__UnityMCP__get_test_job(job_id=...)` 直到完成，读 pass/fail；**核对 `summary.total` > 0**（`total:0 + Passed` = 没跑）

**RUNED(ClassName)** 同上，`assembly_names=["PromptUGUI.Tests.EditorOnly"]`（`Tests/EditMode/Editor/` 下的 Syncer / XSD 测试）。

**RUNPLAY(ClassName)** 同上，`mode="PlayMode"` + `assembly_names=["PromptUGUI.Tests.PlayMode"]`（PlayMode 连跑前先 force refresh，否则第二个 job 会 0 条执行还报 Passed）。

---

## File Structure

| 文件 | 责任 | 动作 |
|---|---|---|
| `Runtime/Controls/Internal/FxMesh.cs` | `Inflate(VertexHelper vh, float pad)`：4 顶点外扩 + uv0 外推 + 写 uv1 / uv2 | Create |
| `Runtime/Controls/Internal/FxMaterialCache.cs` | `FxParams` + `Acquire` / `Release` / 备用栈 / `ResetForTests` / `LiveMaterialCount` | Create |
| `Runtime/Controls/Internal/FxImage.cs` | `sealed class FxImage : UnityEngine.UI.Image, ISelfGrayscale`：属性、`OnPopulateMesh`、`FlushParams` / `UpdateMaterial`、canvas 通道、一次性警告 | Create |
| `Runtime/Controls/Internal/ISelfGrayscale.cs` | `void SetDisabledGrayscale(bool value)` | Create |
| `Runtime/Controls/Internal/ProceduralPanel.cs` | 实现 `ISelfGrayscale`（方法已存在，加接口声明） | Modify |
| `Runtime/Controls/Internal/DisabledGrayscaleController.cs` | `is ProceduralPanel` → `is ISelfGrayscale` | Modify |
| `Runtime/Controls/Internal/ImageTint.cs` | `FxImage` 分支写参数 | Modify |
| `Runtime/Controls/Image.cs` `Icon.cs` | `OnAttached` 创建 `FxImage`；`Blur` / `Glow` / `GlowColor` setter；非 Simple 警告 | Modify |
| `Runtime/Application/UI.cs` | `ResetForTests` 加 `FxMaterialCache.ResetForTests()` | Modify |
| `Runtime/Resources/PromptUGUI/Material/UI-ImageTint.cginc` | `PuguiLinearLight(half4 sprite, half4 tint)`（从 `UI-LinearLightTint.shader` 抽出） | Create |
| `Runtime/Resources/PromptUGUI/Material/UI-LinearLightTint.shader` | 改为 include 上一项 | Modify |
| `Runtime/Resources/PromptUGUI/Material/UI-ImageFx.shader` | 全部 fx 的 fragment | Create |
| `Runtime/Core/Lint/ImageFxRules.cs` | `PUI-FX-TAG` / `-TYPE` / `-ATTR` / `-MASK` / `-RADIUS` | Create |
| `Runtime/Core/Lint/StyleRules.cs` | `PixelAttrs` 加 `blur` | Modify |
| `Runtime/Core/Lint/IRWalker.cs` | `Image` 分支加 fx 规则；新增 `Icon` 分支；`CheckTag` 进通用段 | Modify |
| `Runtime/Application/ScreenInstantiator.cs` | `Image` / `Icon` 分支加 `PUI-FX-TYPE` 运行时警告 | Modify |
| `Editor/XsdGenerator.cs` | `Image` / `Icon` 手列表各加 `blur` `glow` `glowColor` | Modify |
| `Editor/SpriteAtlasSyncer.cs` | 新建 atlas 设 packing settings；`SyncAll` 对已有 atlas 告警 | Modify |
| `Tests/EditMode/Controls/FxMeshTests.cs` | 纯几何 | Create |
| `Tests/EditMode/Controls/FxMaterialCacheTests.cs` | 共享 / 释放 / 备用栈 / 重置 | Create |
| `Tests/EditMode/Controls/FxImageTests.cs` | 属性落点、材质有无、Variant 往返、零组件、排版不变、非 Simple 警告、rebuild 不抛 | Create |
| `Tests/EditMode/Controls/ImageTintTests.cs`（既有） | `<Image tint="linear">` 期望翻到 `UI/ImageFx` | Modify |
| `Tests/EditMode/Controls/DisabledGrayscaleTests.cs`（既有） | `<Btn>` 里的 `<Icon>` 走 `ISelfGrayscale` | Modify |
| `Tests/EditMode/Controls/GradientTintStopTests.cs`（既有） | 守卫：切条保留 uv1 / uv2 | Modify |
| `Tests/EditMode/Controls/ImageFxRenderTests.cs` | RT 像素回归（双 sprite 图集） | Create |
| `Tests/EditMode/Lint/ImageFxRulesTests.cs` | 五条规则正反例 | Create |
| `Tests/EditMode/Lint/StyleRulesTests.cs`（既有，若无则建） | `blur` 走 `PUI-PROCEDURAL-VALUE` | Modify |
| `Tests/EditMode/Editor/XsdGeneratorTests.cs`（既有） | 三个属性名 | Modify |
| `Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs`（既有） | 新建 atlas 的 packing settings；已有 atlas 告警 | Modify |
| `Tests/PlayMode/Controls/ImageFxPlayTests.cs` | 过帧 smoke + 禁用灰度 | Create |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` | `<Image>` / `<Icon>` 表；**Blur & glow** 小节；Quick reference；lint 码 | Modify |
| `.claude/skills/authoring-promptugui-xml/reference/icons.md` | 图集打包要求 | Modify |
| `.claude/skills/scripting-promptugui-csharp/SKILL.md` | `Image.Blur` / `Glow` 可补间 | Modify |
| `.claude/skills/authoring-promptugui-pxl/SKILL.md` | 一句：光晕 / 模糊不要画进 `.pxl` | Modify |
| `.lint/UIXmlLint/README.md` | 规则列表五条 + `PUI-PROCEDURAL-VALUE` 行补 `blur` | Modify |
| `docs~/superpowers/specs/2026-09-02-image-fx-blur-glow-design.md` | §13 实施记录 | Modify |

---

## Task 0: 建分支 + 落 spec 与 plan

- [ ] `cd C:/xsoft/PromptUGUI && git checkout -b feat/image-fx-blur-glow`
- [ ] `git add docs~/superpowers/specs/2026-09-02-image-fx-blur-glow-design.md docs~/superpowers/plans/2026-09-02-image-fx-blur-glow.md` 并提交 `docs(spec): image fx — blur & glow on <Image>/<Icon> (M1 design + plan)`。

---

# M1 —— 几何与材质底座（无视觉；shader 先只透传）

## Task 1: `FxMesh.Inflate`（spec §4.1 / §4.2）

**Files:**
- Create: `Runtime/Controls/Internal/FxMesh.cs`
- Test: `Tests/EditMode/Controls/FxMeshTests.cs`

**Interface：**
```csharp
namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The geometry half of blur / glow on a sprite graphic (spec 2026-09-02 §4.2). Takes the four
    /// vertices <c>Image</c> generated for a Simple sprite and pushes each one <paramref name="pad"/>
    /// units outward on both axes, extrapolating uv0 by the same amount so the shader can keep
    /// sampling the atlas linearly past the sprite's edge. Before touching anything it records the
    /// sprite's UV rectangle (uv0 bounds) into uv1 and the uv-per-unit scale into uv2 — the two
    /// things the fragment needs to clip taps to THIS sprite and to size a radius written in pixels.
    /// </summary>
    internal static class FxMesh
    {
        /// <returns>false (and leaves the mesh untouched) unless there are exactly four vertices
        /// spanning a non-degenerate rect.</returns>
        public static bool Inflate(VertexHelper vh, float pad);
    }
}
```
算法：
1. 4 顶点读出，求 `position.xy` 包围盒 `(minX, minY, maxX, maxY)` 与 `uv0.xy` 包围盒 `(minU, minV, maxU, maxV)`；任一边 ≤ 0 → return false。
2. `rect = (minU, minV, maxU, maxV)`；`perUnit = ((maxU−minU)/(maxX−minX), (maxV−minV)/(maxY−minY))`。
3. 逐顶点：`sx = position.x < cx ? −1 : +1`（`cx` 为位置中心），`sy` 同理；`su = uv0.x < cu ? −1 : +1`（`cu` 为 uv 中心），`sv` 同理。`position += (sx·pad, sy·pad)`；`uv0 += (su·pad·perUnit.x, sv·pad·perUnit.y)`；`uv1 = rect`；`uv2 = (perUnit.x, perUnit.y, 0, 0)`。uv 与位置各自按自己所在的一侧向外推 —— 不假设 uv 与位置同向（flip 过的素材 / 将来的 `packingRotation` 检测都不用改这里）。
4. `pad == 0` 时只写 uv1 / uv2（调用方在 `pad == 0` 时根本不会调；保留这一分支让测试能单独验 uv1 / uv2）。

- [ ] **Step 1: 红测试** —— `FxMeshTests`（`VertexHelper` 手搓 4 顶点：位置 `(0,0)(0,40)(60,40)(60,0)`，uv0 `(0.25,0.5)(0.25,0.75)(0.5,0.75)(0.5,0.5)`，即 60×40 的 quad 映射到 uv 上 0.25×0.25 的矩形）：
  - `Inflate(vh, 6)` 后位置包围盒为 `(−6,−6)–(66,46)`；每个顶点仍在原来那一角。
  - uv0 外推：左下角 uv 变为 `(0.25 − 6·0.25/60, 0.5 − 6·0.25/40)`，右上角对称；容差 1e-5。
  - 每个顶点 `uv1 == (0.25, 0.5, 0.5, 0.75)`；`uv2.xy == (0.25/60, 0.25/40)`。
  - `Inflate(vh, 0)` 位置 / uv0 逐位不变，uv1 / uv2 已写。
  - 5 顶点 / 退化 quad（宽 0）→ 返回 false 且顶点不变。
  - uv 反向（把 uv0 的 x 顺序颠倒，模拟 `flip` 后的素材）：外推后 uv 包围盒仍对称扩大 `pad·perUnit`，且 uv1 仍是原包围盒。
- [ ] **Step 2**: RUN(FxMeshTests) —— 因 `FxMesh` 不存在而编译失败即为正确的红。
- [ ] **Step 3**: 实现。
- [ ] **Step 4**: RUN(FxMeshTests) 绿。
- [ ] **Step 5**: format；提交 `feat(fx): FxMesh inflates a sprite quad and records its atlas rect in uv1/uv2`。

## Task 2: `FxMaterialCache` + `UI-ImageFx.shader` 骨架（spec §5.2 / §5.3）

**Files:**
- Create: `Runtime/Controls/Internal/FxMaterialCache.cs`
- Create: `Runtime/Resources/PromptUGUI/Material/UI-ImageFx.shader`（+ `.meta` 由 Unity 生成）
- Modify: `Runtime/Application/UI.cs`（`ResetForTests`）
- Test: `Tests/EditMode/Controls/FxMaterialCacheTests.cs`

**Interface：**
```csharp
internal readonly struct FxParams : IEquatable<FxParams>
{
    public readonly float Blur;        // px, ≥ 0
    public readonly float Glow;        // px, ≥ 0
    public readonly Color GlowColor;   // only meaningful when !GlowSelf
    public readonly bool GlowSelf;     // glowColor not written → glow takes the sprite's own blurred colour
    public readonly bool TintLinear;   // tint="linear"
    public readonly bool Desaturate;   // disabled state
    public FxParams(float blur, float glow, Color glowColor, bool glowSelf, bool tintLinear, bool desaturate);
    // GlowColor is forced to Color.white when GlowSelf, and to default when Glow == 0, in the ctor —
    // same reason PanelParams zeroes GlassParams for opaque panels: identical pixels must hash identical.
}

internal static class FxMaterialCache
{
    internal const string ShaderResourcePath = "PromptUGUI/Material/UI-ImageFx";
    public static Material Acquire(in FxParams p);
    public static void Release(in FxParams p);
    internal static int LiveMaterialCount { get; }
    internal static int SpareCount { get; }
    internal static void ResetForTests();
}
```
shader 属性名：`_Blur` `_Glow` `_GlowColor` `_GlowSelf` `_TintLinear` `_Desaturate`（float / Color）。`Configure` 逐一 `SetFloat` / `SetColor`。材质 `hideFlags = HideAndDontSave`，`name = "PromptUGUI/ImageFx"`。结构逐字沿用 `ProceduralMaterialCache`（单备用栈；不抽泛型 —— `DecorMaterialCache` 已是第二份复制，本 plan 不扩大改动面，记入 spec §11.2 的实施记录）。

shader 骨架（本 Task 只要能编译、行为等于 `UI/Default`）：克隆 `UI-Grayscale.shader` 全部 Properties / Tags / Stencil / Blend / `multi_compile_local`；`Shader "UI/ImageFx"`；`appdata_t` 加 `float4 texcoord1 : TEXCOORD1; float4 texcoord2 : TEXCOORD2;`；`v2f` 加 `float4 rect : TEXCOORD2; float2 perUnit : TEXCOORD3;`（`worldPosition` 仍 TEXCOORD1）；声明六个 uniform；fragment 暂时 `= (tex2D + _TextureSampleAdd) * IN.color` + clip。

- [ ] **Step 1: 红测试** —— `FxMaterialCacheTests`（`[SetUp]/[TearDown]` 调 `UI.ResetForTests()`）：
  - 同一 `FxParams` `Acquire` 两次 → 同一 `Material`，`LiveMaterialCount == 1`；shader 名 `UI/ImageFx`；`GetFloat("_Blur")` 等于参数。
  - `Release` 两次后 `LiveMaterialCount == 0`、`SpareCount == 1`；再 `Acquire` 另一组参数 → 复用同一个 `Material` 实例（引用相等）、`SpareCount == 0`。
  - 模拟补间：循环 100 次 `Acquire(p_i)` / `Release(p_{i−1})` → `LiveMaterialCount == 1`、`SpareCount ≤ 1`、`Resources.FindObjectsOfTypeAll<Material>()` 中名为 `PromptUGUI/ImageFx` 的数量 ≤ 2。
  - `GlowSelf == true` 时不同 `GlowColor` 的两组参数 `Equals` 为真（ctor 归一）；`Glow == 0` 时 `GlowSelf` 真假两组 `Equals` 为真。
  - `UI.ResetForTests()` 后 `LiveMaterialCount == 0`、`SpareCount == 0`。
- [ ] **Step 2**: RUN(FxMaterialCacheTests) 红。
- [ ] **Step 3**: 实现 cache + shader 骨架；`UI.ResetForTests` 在 `ProceduralMaterialCache.ResetForTests()` 后一行加 `Controls.Internal.FxMaterialCache.ResetForTests();`。
- [ ] **Step 4**: RUN(FxMaterialCacheTests) 绿；`read_console(types=["error","warning"])` 确认 shader 无编译警告。
- [ ] **Step 5**: format；提交 `feat(fx): FxMaterialCache shares one material per parameter set; UI/ImageFx shader skeleton`。

## Task 3: `FxImage` + `Icon` / `Image` 接线（spec §5.1 / §5.4）

**Files:**
- Create: `Runtime/Controls/Internal/FxImage.cs`、`Runtime/Controls/Internal/ISelfGrayscale.cs`
- Modify: `Runtime/Controls/Image.cs`、`Runtime/Controls/Icon.cs`
- Test: `Tests/EditMode/Controls/FxImageTests.cs`

**Interface：**
```csharp
internal interface ISelfGrayscale { void SetDisabledGrayscale(bool value); }

/// <summary>
/// The Image every <Image> / <Icon> is built on. With nothing written it IS a plain Image: material
/// null (UI/Default), the base mesh untouched, no extra components. Once blur / glow / a linear tint
/// / the disabled grey is asked for, it owns its material the way ProceduralPanel does (spec
/// 2026-09-02 §5.1) — shared per parameter set through FxMaterialCache, never one per instance.
/// </summary>
internal sealed class FxImage : UnityEngine.UI.Image, ISelfGrayscale
{
    public float Blur { get; set; }              // clamps at 0; pad change → SetVerticesDirty
    public float Glow { get; set; }
    public void SetGlowColor(Color c);           // explicit colour
    public void ClearGlowColor();                // back to "the sprite's own blurred colour"
    public bool TintLinear { get; set; }         // ImageTint routes here
    void ISelfGrayscale.SetDisabledGrayscale(bool value);

    internal float Pad => Mathf.Max(_blur, _glow);
    internal bool HasGeometryFx => sprite != null && type == Type.Simple && Pad > 0f;
    internal bool HasMaterialFx => HasGeometryFx || _tintLinear || _grayed;
    internal bool HasKeyForTests { get; }
    internal FxParams KeyForTests { get; }
    internal void BuildMeshForTests(VertexHelper vh) => OnPopulateMesh(vh);
    internal void FlushParams(bool fromRebuild = false);   // ProceduralPanel 同构

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        base.OnPopulateMesh(vh);
        if (!HasGeometryFx) return;
        if (!FxMesh.Inflate(vh, Pad)) WarnOnce("...expects the 4-vertex Simple quad...");
    }
    protected override void UpdateMaterial() { FlushParams(fromRebuild: true); base.UpdateMaterial(); }
    protected override void OnEnable() { base.OnEnable(); EnsureCanvasChannels(); }
    protected override void OnTransformParentChanged() { base.OnTransformParentChanged(); EnsureCanvasChannels(); }
    protected override void OnDestroy() { if (_hasKey) FxMaterialCache.Release(_key); base.OnDestroy(); }
}
```
要点：
- `FlushParams`：`!HasMaterialFx` → 若有 key 则 `Release`、`_hasKey = false`、`m_Material = null`（回到 `defaultMaterial`）；否则 `BuildParams` → 与 `_key` 相同则返回 → `Acquire` 新、`Release` 旧、写 `m_Material`；`!fromRebuild` 才 `SetMaterialDirty()`（注释同 `ProceduralPanel.FlushParams`）。
- `EnsureCanvasChannels`：`TexCoord1 | TexCoord2`，只在 `HasGeometryFx` 时才开（无 fx 的 Icon 不该给画布加通道）；在 `Blur` / `Glow` setter 首次 > 0 时也调一次。
- `Icon.OnAttached` / `Image.OnAttached`：`_img = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<FxImage>();` —— 已有的 plain `Image`（prefab）沿用；三个 fx setter 开头 `if (_img is not FxImage fx) { WarnOnce($"<{Tag}> blur/glow/glowColor need PromptUGUI's FxImage; this node carries a plain Image (prefab?) — attribute ignored"); return; }`。
- `[UIAttr, Preserve] public string Blur { set => fx.Blur = ProceduralValueParser.Pixels(value, "blur"); }`；`Glow` 同；`[UIAttr(IsColor = true), Preserve] public string GlowColor { set { if (string.IsNullOrEmpty(value)) fx.ClearGlowColor(); else fx.SetGlowColor(UI.Theme.Resolve(value)); } }`。
- `Image.OnAfterApply`：自动判成 Sliced 之后，若 `fx.Pad > 0 && _img.type != Simple` → `Debug.LogWarning` 一次（`PUI-FX-TYPE` 的运行时兄弟，消息指出 sprite 带 9-slice 边框、写 `type="simple"` 可强制）。
- `Icon.OnAttached` 里现有的 `preserveAspect = true` / `raycastTarget = false` / `color = white` 不变。

- [ ] **Step 1: 红测试** —— `FxImageTests`（`UI.ResetForTests` + `UI.SpriteResolver = _ => stub`，`stub = Sprite.Create(new Texture2D(8, 8), new Rect(0,0,8,8), new Vector2(.5f,.5f))`；`Open(body)` helper 同 `ImageRotateFlipTests`）：
  - `Icon_and_Image_are_built_on_FxImage`：`<Icon id='i' name='ui:x'/>` 与 `<Image id='m' sprite='ui:x' size='40x40'/>` 的 `GetComponent<UnityImage>()` 都 `is FxImage`。
  - `No_fx_means_no_material_no_channels`：`material == defaultMaterial`（即 `m_Material` 为 null）、`HasKeyForTests == false`、`canvas.additionalShaderChannels` 不含 `TexCoord2`；`GetComponents<Component>().Length` 与改动前的 Icon 相同（记录一个基线数字）。
  - `Glow_lands_and_acquires_a_shared_material`：`<Icon glow='6'/>` 两个 → 同一 `Material`，`FxMaterialCache.LiveMaterialCount == 1`，shader 名 `UI/ImageFx`，`_Glow == 6`、`_GlowSelf == 1`；`canvas.additionalShaderChannels` 含 `TexCoord1 | TexCoord2`。
  - `GlowColor_explicit_and_cleared`：`glowColor='#ff0000'` → `_GlowSelf == 0`、`_GlowColor == red`；C# `fx.ClearGlowColor()` + `FlushParams()` → `_GlowSelf == 1`。
  - `Mesh_is_inflated_by_the_larger_radius`：`<Image blur='3' glow='8' size='40x40'/>` → `BuildMeshForTests(vh)` 4 顶点包围盒 56×56、uv1 == `(0,0,1,1)`（stub 整张）、uv2 == `(1/40, 1/40)`。`glow='0' blur='0'` → 包围盒 40×40、`Inflate` 未被调（uv1 全零）。
  - `Layout_is_untouched`：`<Icon glow='8' size='32x32'/>` → `rect.size == (32,32)`，`GetNativeSize() == (8,8)`；`<VStack>` 里两个带 glow 的 Icon 间距 = `spacing`。
  - `Variant_round_trip_returns_to_default_material`：`glow='6' glow.portrait='0'` → `UI.Variants.Set("portrait", true)` 后 `HasKeyForTests == false`、`material == defaultMaterial`；设回 false → 重新持有 key；组件数不变。
  - `Tween_walks_the_spare_stack`：`for i in 1..100: fx.Glow = i * 0.1f; fx.FlushParams();` → `LiveMaterialCount == 1`、`SpareCount ≤ 1`。
  - `Non_simple_type_ignores_fx_with_one_warning`：stub 改为带 border 的 sprite（`Sprite.Create(..., border: new Vector4(2,2,2,2))`），`<Image sprite='ui:x' glow='6'/>` 未写 `type=` → `LogAssert.Expect(LogType.Warning, Regex("9-slice"))`；`HasGeometryFx == false`、`material == defaultMaterial`。写 `type='simple'` → 有 fx、无警告。
  - `Plain_image_from_prefab_warns`：手动 `new GameObject` + `AddComponent<UnityImage>()` 后再 `Attach` 一个 `Icon`（用 `ControlRegistry` 的实例化路径或直接 `new Icon()` + `Attach(go)` —— 看 `Control` 基类怎么绑 GameObject）→ `glow` setter 触发一次 warning 且不抛。
  - `ForceUpdateCanvases_does_not_throw`：`<Icon glow='6' blur='2'/>` → `Canvas.ForceUpdateCanvases()` 不抛；`GetComponent<CanvasRenderer>().GetMaterial(0)` 的 shader 名 `UI/ImageFx`。
- [ ] **Step 2**: RUN(FxImageTests) 红。
- [ ] **Step 3**: 实现。
- [ ] **Step 4**: RUN(FxImageTests) 绿；RUN(ImageRotateFlipTests) / RUN(RotateFlipEffectTests) / RUN(GradientColorAttrTests) / RUN(AttributeReversibilityTests) / RUN(BtnStateTests) 绿（Icon / Image 的组件类型换了，这是最容易踩的回归面）。
- [ ] **Step 5**: format；提交 `feat(fx): FxImage — <Image>/<Icon> gain blur / glow / glowColor with a shared, cached material`。

## Task 4: `tint="linear"` 与禁用灰度折进 `FxImage`（spec §4.4 表、§5.4）

**Files:**
- Modify: `Runtime/Controls/Internal/ImageTint.cs`、`DisabledGrayscaleController.cs`、`ProceduralPanel.cs`
- Test: `Tests/EditMode/Controls/ImageTintTests.cs`、`DisabledGrayscaleTests.cs`（既有，改 / 加用例）

**改动：**
```csharp
// ImageTint.Apply
if (img is FxImage fx) { fx.TintLinear = mode == "linear"; if (unknown mode) warn as today; return; }
// DisabledGrayscaleController.ApplyAll
else if (c.Graphic is ISelfGrayscale self) self.SetDisabledGrayscale(_grayed);
// ProceduralPanel : ..., ISelfGrayscale  （SetDisabledGrayscale 已存在，改 public/显式实现）
```
`Recapture` / `Configure` 对 `ISelfGrayscale` 的 graphic 仍捕获 `g.material`（无害，永远不会写回 —— `ApplyAll` 走接口分支）；在 `Captured` 构造处加一行注释说明。

- [ ] **Step 1: 改期望 + 红测试**：
  - `ImageTintTests.TintLinear_UsesLinearLightTintMaterial`（`<Image>`）：期望改为 shader 名 `UI/ImageFx` 且 `GetFloat("_TintLinear") == 1`；`tint="multiply"` 回到 `defaultMaterial`。`RawImageTests` 的同名用例**不动**（RawImage 不在 M1，仍换 `.mat`）。`DisabledGrayscaleTests` 第 278 行附近对 `<Btn tint="linear">` 的 `bg`（plain Image）期望**不动**。
  - `DisabledGrayscaleTests` 新增 `Icon_inside_disabled_Btn_greys_from_inside`：`<Btn id='b'><Icon id='i' name='ui:x' glow='6'/></Btn>` → `SimulateState(Disabled)` 后 Icon 的 material shader 仍是 `UI/ImageFx`（**不是** `UI/Grayscale`）、`_Desaturate == 1`、`_Glow == 6`（光晕没丢）；`SimulateState(Normal)` → `_Desaturate == 0`。再加一条无 fx 的 `<Icon>`：禁用时 shader `UI/ImageFx` + `_Desaturate == 1`，恢复后 `material == defaultMaterial`。
  - `DisabledGrayscaleTests` 新增 `ProceduralPanel_is_dispatched_through_ISelfGrayscale`（既有 `<Frame color radius>` 禁用用例若已覆盖则只改断言措辞）。
- [ ] **Step 2**: RUN(ImageTintTests) / RUN(DisabledGrayscaleTests) 红。
- [ ] **Step 3**: 实现。
- [ ] **Step 4**: RUN(ImageTintTests) / RUN(DisabledGrayscaleTests) / RUN(RawImageTests) / RUN(BtnStateTests) / RUN(FxImageTests) 绿。
- [ ] **Step 5**: format；提交 `feat(fx): linear tint and disabled grey become FxImage parameters (ISelfGrayscale)`。

**M1 收尾**：零回归守门清单全跑一遍。

---

# M2 —— shader 视觉

## Task 5: `UI-ImageFx.shader` fragment + `UI-ImageTint.cginc` + `ImageFxRenderTests`（spec §4.3 / §7）

**Files:**
- Create: `Runtime/Resources/PromptUGUI/Material/UI-ImageTint.cginc`
- Modify: `Runtime/Resources/PromptUGUI/Material/UI-LinearLightTint.shader`（include）、`UI-ImageFx.shader`（fragment）
- Test: `Tests/EditMode/Controls/ImageFxRenderTests.cs`

**cginc：**
```hlsl
// UI-ImageTint.cginc —— tint="linear" 的 Linear Light，UI-LinearLightTint.shader 与 UI-ImageFx.shader 共用。
// 在 gamma 空间做（美术眼中的 128 灰 = 中性）；linear 工程下 sprite 先转回 gamma、算完再转回。
half4 PuguiLinearLight(half4 sprite, half4 tint);   // 返回 (rgb, sprite.a * tint.a)
```
`UI-LinearLightTint.shader` 的 fragment 改为调用它，行为逐位不变（`ImageTintTests` / `RawImageTests` 守）。

**fragment（伪码，最终以实现为准）：**
```hlsl
// 采样核：中心 + 24 个 Vogel 圆盘点（r_i = sqrt((i+0.5)/24), θ_i = i·2.399963），权重 w_i = exp(-2 r_i²)，
// 常量数组 static const。半径 R 以 px 计，tap 偏移 = disk_i · R · IN.perUnit。
half4 SampleClamped(float2 uv, float4 rect)
{
    bool inside = all(uv >= rect.xy) && all(uv <= rect.zw);
    return inside ? tex2D(_MainTex, uv) + _TextureSampleAdd : half4(0,0,0,0);
}
// 一轮：返回预乘累加 (Σw·rgb·a, Σw·a) / Σw
half4 Disk(float2 uv, float4 rect, float2 perUnit, float radius);

frag:
  half4 img = SampleClamped(uv0, rect);                                  // 本体（矩形外透明）
  if (_Blur > 0) { half4 p = Disk(uv0, rect, perUnit, _Blur); img = half4(p.a > 1e-4 ? p.rgb / p.a : 0, p.a); }   // 反预乘
  img = _TintLinear > 0.5 ? PuguiLinearLight(img, IN.color) : img * IN.color;
  half4 glow = 0;
  if (_Glow > 0) {
      half4 p = Disk(uv0, rect, perUnit, _Glow);
      half g = saturate(2.0 * p.a); half a = g * g;                       // 边缘 c≈0.5 → g=1；R 处 → 0；与 PuguiApplyOuterGlow 同形
      glow = _GlowSelf > 0.5
           ? half4((p.a > 1e-4 ? p.rgb / p.a : 0) * IN.color.rgb, a * IN.color.a)
           : half4(_GlowColor.rgb, _GlowColor.a * a * IN.color.a);
  }
  half4 col = PuguiOver(img, glow);                                       // 本体在上（PuguiOver(src_top, dst_under)，同 PuguiApplyOuterGlow 的调用约定）
  if (_Desaturate > 0.5) col.rgb = dot(col.rgb, half3(0.299, 0.587, 0.114)).xxx;
  clip rect / alpha clip 同 UI/Default
```
两轮各自 `if (R <= 0)` 跳过 —— uniform 分支，`UI-ProceduralPanel` 同款。`_Color` 材质属性照 `UI-Grayscale` 保留（乘进顶点色）。

**渲染夹具（`ImageFxRenderTests`）**：`DecorRenderTests` 同款相机 / RT（256²）/ PNG dump；测试内手搓 **64×32 的双 sprite 图集**：左 32×32 tile 透明底（RGB = 黑、A = 0）上一个半径 10 的白色实心圆盘；右 32×32 tile 纯红不透明；`filterMode = Point`；`Sprite.Create(tex, new Rect(0,0,32,32), (.5,.5), pixelsPerUnit: 32)` 与 `Rect(32,0,32,32)`；`UI.SpriteResolver` 按 `ui:disc` / `ui:red` 返回。文档：`<Frame id='host' anchor='center' width='200' height='200'><Icon id='i' name='ui:disc' anchor='center' size='64x64' {attrs}/></Frame>`，圆盘直径 = 20 tex px × 2 = 40 design px。探针以 host 归一坐标取样（`At(u,v)`），圆盘中心 `(0.5,0.5)`、半径 0.1（40/2/200）。

- [ ] **Step 1: 夹具 + 红测试**：
  1. `Fixture_SelfCheck`：无 fx → 圆心亮（r,g,b > 0.9）、圆外 4px 处 = 背景（< 0.05）、`ui:red` 的 Icon 中心 r > 0.9 / g < 0.1。
  2. `Glow_LightsUpOutsideTheSilhouette_AndFadesToZeroByR`：`glow='8'`（= 4 tex px；探针按 design px 换算）→ 圆外 2px 亮度 > 0.15；沿 +x 方向 2 / 4 / 6px 三个采样单调递减；圆外 10px = 背景。**同一套探针在 `glow='0'` 上全部反向成立**（阈值校验）。
  3. `Glow_DoesNotBleedFromTheNeighbourSprite`：`ui:disc` 开 `glow='8'`，sprite 矩形右缘外 1–8px 一列像素的 `r ≤ max(g,b) + 0.02`（没有红块漏进来）；`ui:red` 开 `glow='8'` 时其矩形左缘外没有白（`g < 0.1`）。这一条是矩形钳制的**唯一**真实回归防线。
  4. `Blur_SoftensTheEdge`：`blur='4'` → 圆盘边缘（半径 ±1px）像素亮度 ∈ (0.15, 0.85)；`blur='8'` 的过渡带比 `blur='4'` 宽（边缘外 3px 处更亮）。
  5. `Blur_HasNoDarkFringe`：`blur='6'` → 边缘半透明像素（alpha ∈ (0.2, 0.8)，通过 RT 的 alpha 或对比黑 / 白两种相机背景）rgb 归一后 ≥ 0.9（预乘正确；错误实现会给出灰 / 黑环）。
  6. `GlowColor_ExplicitTints_SelfFollowsVertexColour`：`glowColor='#ff0000'` → 圆外 2px `r > g + 0.3`；未写 + `color='#00ff00'` → 圆外 2px `g > r + 0.3`。
  7. `Disabled_GreysTheGlowToo`：`<Btn interactable='false'>` 里的 `ui:red` Icon `glow='8'` → 圆外 2px 与圆心都 `|r−g| < 0.05`。
  8. `LinearTint_MatchesTheOldShader`：中灰 sprite（`#808080` 实心 tile）`tint='linear' color='#c0c0c0'`：`FxImage` 渲染 vs 同文档换 `<RawImage>`（走旧 `.mat`）—— 或直接与 `UI-LinearLightTint.mat` 手动画的一张 RT 比 —— 中心像素三通道差 ≤ 2/255。
  9. `AncestorRectMask_ClipsTheGlow`：host 加 `mask='rect'`、Icon 贴 host 右缘 → host 右缘外 = 背景。
- [ ] **Step 2**: RUN(ImageFxRenderTests) —— 1 绿、其余红（骨架 shader 不画光晕）；**同时**把 2 的反向断言在 `glow='0'` 上跑一遍确认阈值站得住。
- [ ] **Step 3**: 实现 cginc + fragment；`UI-LinearLightTint.shader` 改 include。
- [ ] **Step 4**: RUN(ImageFxRenderTests) 全绿；红了先看 `Application.temporaryCachePath` 下的 PNG 再改（尤其 3 / 5：串色和暗环肉眼一眼可辨）。RUN(ImageTintTests) / RUN(RawImageTests) / RUN(GradientStopRenderTests) / RUN(DecorRenderTests) 绿。`read_console(types=["warning"])` 无 shader 警告。
- [ ] **Step 5**: format；提交 `feat(fx): UI/ImageFx — rect-clamped disk blur (premultiplied) and coverage glow under the sprite`。

**M2 收尾**：零回归守门清单。

---

# M3 —— lint / XSD / 图集

## Task 6: `ImageFxRules` + `StyleRules.PixelAttrs` + 登记（spec §6）

**Files:**
- Create: `Runtime/Core/Lint/ImageFxRules.cs`
- Modify: `Runtime/Core/Lint/StyleRules.cs`、`IRWalker.cs`、`Runtime/Application/ScreenInstantiator.cs`
- Test: `Tests/EditMode/Lint/ImageFxRulesTests.cs`、`Tests/EditMode/Lint/StyleRulesTests.cs`（既有则加用例）

**Interface：**
```csharp
namespace PromptUGUI.Lint
{
    /// <summary>blur / glow lint for the sprite graphics (spec 2026-09-02 §6). TAG is generic (any
    /// tag, raw pass, like RotateFlipRules); the rest are Image/Icon self-checks after class merge.</summary>
    public static class ImageFxRules
    {
        public const string TagCode = "PUI-FX-TAG";
        public const string TypeCode = "PUI-FX-TYPE";
        public const string AttrCode = "PUI-FX-ATTR";
        public const string MaskCode = "PUI-FX-MASK";
        public const string RadiusCode = "PUI-FX-RADIUS";
        public const float RadiusSoftLimit = 12f;

        /// <summary>CLI, raw: `blur` on anything but Image / Icon (RawImage: "not in M1").</summary>
        public static IEnumerable<LintIssue> CheckTag(ElementNode n, StyleAttributeView styles = null);
        /// <summary>Runtime: TYPE only (the others are authoring nits).</summary>
        public static IEnumerable<LintIssue> CheckImage(ElementNode n);
        /// <summary>CLI, expanded (class merged): TYPE / ATTR / MASK / RADIUS. Also used for Icon.</summary>
        public static IEnumerable<LintIssue> CheckImage(ElementNode n, StyleAttributeView styles);
    }
}
```
- 值读取沿用 `RotateFlipRules` / `MaskAttributeRules` 的 `styles` 视图（class 合并后的有效值）；`{{` 占位一律跳过；数字解析 `float.TryParse(InvariantCulture)`。
- `StyleRules.PixelAttrs` 加 `"blur"`（`glow` 已在）。
- `IRWalker`：通用段（`RotateFlipRules.Check` 旁）加 `ImageFxRules.CheckTag`；`Image` 分支加 `CheckImage(node, styles)`；**新增 `Icon` 分支**只调 `CheckImage(node, styles)`。`ScreenInstantiator`：`Image` 分支加 `CheckImage(node)`；新增 `Icon` 分支同。
- `PUI-FX-TYPE` 与 Task 3 的运行时"自动判 Sliced"警告是两回事：lint 只看作者写的 `type=`。

- [ ] **Step 1: 红测试** —— `ImageFxRulesTests`（用既有 lint 测试的 `Lint(xml)` helper，断言 `Code` + `Message` substring）：
  - TAG：`<Frame blur="4">` / `<Btn blur="4">` / `<RawImage blur="4">`（消息含 `M2`）报；`<Image blur>` / `<Icon blur>` 不报；`<Frame glow="4">` 不报（Frame 自己的 glow）；`class=` 带进来的 `blur` 也报；`blur="{{x}}"` 不报。
  - TYPE：`<Image sprite glow="4" type="sliced">` 报；`tiled` / `filled` 报；`contain` / `cover` / `simple` / 未写不报；`type` 来自 `<Style>` + `class=` 时也报（expanded）。
  - ATTR：`glowColor` 无 `glow` → warning；有 `glow="0"` 也 warning；有 `glow="4"` 不报。
  - MASK：`glow` + `mask="self"` warning；`mask="rect"` 不报。
  - RADIUS：`blur="13"` / `glow="12.5"` warning；`12` 不报；`{{}}` 不报。
  - `StyleRulesTests`：`<Image blur="abc">` / `blur="-1"` / `<Style name="s" blur="x">` → `PUI-PROCEDURAL-VALUE`。
- [ ] **Step 2**: RUN(ImageFxRulesTests) / RUN(StyleRulesTests) 红。
- [ ] **Step 3**: 实现 + 登记。
- [ ] **Step 4**: RUN 两类绿；RUN(FxImageTests) 绿（运行时警告落点）；`cd C:/xsoft/PromptUGUI && dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` 退出码 0。
- [ ] **Step 5**: format；提交 `feat(lint): PUI-FX-* rules for blur / glow on sprite graphics`。

## Task 7: XSD

**Files:**
- Modify: `Editor/XsdGenerator.cs`（第 113 / 147 行附近的 `Image` / `Icon` 手列表）
- Test: `Tests/EditMode/Editor/XsdGeneratorTests.cs`

- [ ] **Step 1: 红测试** —— 仿 `Image_and_Icon_list_their_rotation_and_flip_attributes`：`Image_and_Icon_list_blur_glow_glowColor`：三个 `name="..."` substring。
- [ ] **Step 2**: RUNED(XsdGeneratorTests) 红。
- [ ] **Step 3**: 两个手列表各加 `("blur", "xs:string", null)`、`("glow", "xs:string", null)`、`("glowColor", "xs:string", null)`。
- [ ] **Step 4**: RUNED(XsdGeneratorTests) 绿；`execute_menu_item` 重新生成宿主工程的 `PromptUGUI.gen.xsd`（菜单名见 `XsdGenerator` 的 `[MenuItem]`）并确认 diff 只多三个属性。
- [ ] **Step 5**: format；提交 `feat(xsd): blur / glow / glowColor on Image and Icon`。

## Task 8: `SpriteAtlasSyncer` 打包卫生（spec §4.5）

**Files:**
- Modify: `Editor/SpriteAtlasSyncer.cs`
- Test: `Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs`

**改动：**
```csharp
// 新建 atlas（第 905 行附近）：CreateAsset 之后、ApplyTemplateFilterMode 之前
ApplySafePackingSettings(atlas);
private static void ApplySafePackingSettings(SpriteAtlas atlas)
{
    // blur/glow 的采样按 sprite 矩形钳制，前提是「矩形 = 这个 sprite」：rotation 让 uv 轴错位
    // （uGUI Image 本身就画倒），tight packing 让邻居钻进透明角落（模糊后显形）。spec 2026-09-02 §4.5。
    var ps = atlas.GetPackingSettings();
    ps.enableRotation = false;
    ps.enableTightPacking = false;
    atlas.SetPackingSettings(ps);
}
// SyncAll 的每 set 循环里，拿到已有 atlas 后：
WarnIfPackingUnsafe(set.SetName, atlas);   // 任一为 true → Debug.LogWarning("[SpriteSync] atlas 'X': enableRotation/enableTightPacking is on — blur/glow on its sprites will sample neighbours. Turn both off in the SpriteAtlas inspector and Pack Preview.")
```

- [ ] **Step 1: 红测试** —— `SpriteAtlasSyncerTests`（既有夹具 `TestRoot` + `_toCleanup`）：
  - `New_atlas_disables_rotation_and_tight_packing`：建一个 SpriteSet（看既有用例怎么建）→ 同步 → `atlas.GetPackingSettings().enableRotation == false && enableTightPacking == false`。
  - `Existing_atlas_with_unsafe_packing_warns`：手动 `SetPackingSettings(rotation=true)` 后再同步 → `LogAssert.Expect(LogType.Warning, Regex("enableRotation"))`，且设置**未被改动**。
- [ ] **Step 2**: RUNED(SpriteAtlasSyncerTests) 红。
- [ ] **Step 3**: 实现。
- [ ] **Step 4**: RUNED(SpriteAtlasSyncerTests) 绿（KNOWN FLAKE：IDE 占 csproj 时 teardown 可能报 Sharing violation，重跑）。
- [ ] **Step 5**: format；提交 `feat(sprite-sync): new atlases pack without rotation / tight packing; warn on existing ones`。

**M3 收尾**：零回归守门清单 + CLI 守门。

---

# M4 —— 守卫与 PlayMode

## Task 9: `GradientTint` 切条守卫 + PlayMode smoke

**Files:**
- Modify: `Tests/EditMode/Controls/GradientTintStopTests.cs`
- Create: `Tests/PlayMode/Controls/ImageFxPlayTests.cs`

- [ ] **Step 1**: `GradientTintStopTests` 加 `Slicing_preserves_uv1_and_uv2`：`BuildWhiteQuad()` 的 4 顶点先写 `uv1 = (0.1,0.2,0.3,0.4)`、`uv2 = (0.5,0.6,0,0)`，`Set(Gradient(red, blue, 0.3f, 0.6f))` 后 `ModifyMesh` → `GetUIVertexStream` 里**每个**顶点 uv1 / uv2 逐位等于原值（`MeshSlicer.Lerp` 对常量插值必须不变）。RUN(GradientTintStopTests) —— 预期直接绿（VGS 的 `Lerp` 覆盖 uv0–uv3）；若红说明 `Lerp` 漏了通道，修 `MeshSlicer`。
- [ ] **Step 2**: `ImageFxPlayTests`：
  - `GlowIcon_SurvivesFrames`：`<Icon id='i' name='ui:x' glow='6' blur='2' size='32x32'/>`（`UI.SpriteResolver` stub）过两帧 → `CanvasRenderer.GetMaterial(0).shader.name == "UI/ImageFx"`、`FxMaterialCache.LiveMaterialCount == 1`；`Glow = 0; Blur = 0` 再过一帧 → `material == defaultMaterial`、`LiveMaterialCount == 0`。
  - `DisabledBtn_WithGlowIcon_GreysFromInside`：`<Btn interactable='false'><Icon glow='6'/></Btn>` 过一帧 → Icon 材质 `_Desaturate == 1`；`Interactable = true` 过一帧 → `_Desaturate == 0`、`_Glow == 6`。
- [ ] **Step 3**: RUNPLAY(ImageFxPlayTests) 绿；RUNPLAY(DisabledGrayscalePlayTests) 绿。
- [ ] **Step 4**: format；提交 `test(fx): uv1/uv2 survive gradient slicing; play-mode smoke for glow icons`。

---

# M5 —— 文档

## Task 10: SKILL 更新（英文，同 PR）

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`、`reference/icons.md`、`.claude/skills/scripting-promptugui-csharp/SKILL.md`、`.claude/skills/authoring-promptugui-pxl/SKILL.md`、`.lint/UIXmlLint/README.md`

- [ ] **Step 1**: `SKILL.md` `<Image>` 属性表（第 ~208 行）加三行：`blur`（px radius, `0` off — see **Blur & glow**）、`glow`、`glowColor`（solid; unset = the sprite's own blurred colour）；`<Icon>` 表（第 ~662 行）同三行，Notes 指向 **Blur & glow** under `<Image>`。
- [ ] **Step 2**: **Rotation & flip** 之后、**Reflection recipe** 之前新增 **#### Blur & glow**：spec §3 的四个示例；要点列表 —— only `<Image>` / `<Icon>` (`PUI-FX-TAG`; `<RawImage>` and `<Btn sprite=>` are M2); only `type="simple"` (incl. `contain` / `cover`) — `sliced` / `tiled` / `filled` = `PUI-FX-TYPE`, and a 9-slice sprite without `type=` silently keeps Sliced (runtime warning) — write `type="simple"` to force; glow is drawn geometry only, the rect / layout / raycast don't grow — leave `spacing` / `margin` for it, same as `<Frame glow>`; `glowColor` unset = the sprite's own blurred colour (a coloured icon glows in its colour, follows `color=` and state modulates), set = a flat silhouette glow in that colour; radii are design px in the element's own space (`scale=` scales them); `> 12` = `PUI-FX-RADIUS` (large radii are M2); `mask="self"` on the same node makes the glow part of the mask (`PUI-FX-MASK`); a stop gradient `color=` normalises over the inflated quad, so the body sees the ramp inset by `pad/height`; `tint="linear"` and the disabled grey still work (folded into the same shader).
- [ ] **Step 3**: Quick reference（第 ~1670 行 `REFLECTION` 旁）加一行 `FX            <Icon name="x" blur="4" glow="8" glowColor="accent"/>  — simple type only; glow is drawn outside the rect, leave spacing`。
- [ ] **Step 4**: lint 码：在 `SKILL.md` 里 `PUI-FLIP-TAG` 那句的写法旁一句带过五个 `PUI-FX-*`；`.lint/UIXmlLint/README.md` 表（第 ~205 行 `PUI-FLIP-TAG` 旁）加五行（TAG / TYPE / ATTR / MASK / RADIUS，标注 CLI / runtime），`PUI-PROCEDURAL-VALUE` 行的属性列表补 `blur`。
- [ ] **Step 5**: `reference/icons.md` 第 27 行段落后加一段 **Atlas packing**：the sync tool creates atlases with rotation and tight packing OFF and warns when an existing atlas has either on — `blur` / `glow` clip their samples to the sprite's rect, which is only THIS sprite when neither is on (uGUI `Image` already draws rotated-packed sprites wrong). Fix in the SpriteAtlas inspector, then Pack Preview.
- [ ] **Step 6**: `scripting-promptugui-csharp/SKILL.md` 第 ~1829 行 `### Image.Rotation / Flip` 之后加 `### Image.Blur / Glow — tweenable`：`LMotion.Create(0f, 8f, 0.2f).Bind(v => icon.Glow = v)`；each frame costs one material-cache lookup (no allocation) plus a mesh rebuild; `GlowColor` takes a colour string (`""` = back to the sprite's own colour).
- [ ] **Step 7**: `authoring-promptugui-pxl/SKILL.md` 第 173 行倒影那条之后加一条：**Do not paint a glow or a blur into the sprite.** `glow=` / `blur=` generate them at runtime, in any colour and radius, and a baked halo would double up and break `size="native"`.
- [ ] **Step 8**: 通读三处，`grep -rn "PUI-FX-VALUE" .claude .lint docs~` 应无结果（spec 已改口）。
- [ ] **Step 9**: 提交 `docs(skill): blur & glow on <Image>/<Icon>; atlas packing requirement`。

## Task 11: spec 实施记录 + 收尾

- [ ] **Step 1**: `2026-09-02-image-fx-blur-glow-design.md` 加 §13 实施记录：测试计数（EditMode / EditorOnly / PlayMode 全量）、最终采样核（tap 数 / 权重）与渲染探针阈值、与设计的偏差（至少：`PUI-FX-VALUE` 并入 `PUI-PROCEDURAL-VALUE`；`FxMaterialCache` 采用复制而非泛型；`GradientTint` 内缩是否可见）、顺带发现。
- [ ] **Step 2**: 全量回归：RUN 三个程序集全量（不带 `group_names`）+ RUNED 全量；`read_console(types=["error","warning"])` 确认无新增 warning；`dotnet format --verify-no-changes`；`dotnet run --project .lint/UIXmlLint -- Runtime/Resources/`。
- [ ] **Step 3**: 宿主工程手工目检：在 ssw_re_client 的 UIPreview 场景（memory：反射调 `LoadFileAsync`，先设 Theme + SpriteSet 解析器）放一个 `<Icon name="…" glow="8"/>` 与 `blur="4"`，用 `execute_code` 里的 `ScreenCapture.CaptureScreenshot` 截图（`manage_camera` 截图会丢 IMGUI 但 uGUI 无碍；仍按 memory 走稳妥路径）看一眼像素风图标上的观感；把结论写进 §13。**不改客户端仓库任何资产**（`PxlIcon.spriteatlas` 的 packing 改动是客户端的独立事项，只在 §13 记一句提醒）。
- [ ] **Step 4**: 提交 `docs(spec): image fx M1 implementation notes`；推分支、开 PR（标题 `feat: <Image>/<Icon> 的 sprite 级模糊与外发光 —— FxImage 自持材质 + 矩形钳制采样 + linear tint / 灰度折入 + 图集打包卫生`，正文引用 spec；末尾附会话链接）。**推送前先问作者**（对外动作）。

---

## 完成判据

- [ ] `<Icon glow="6"/>` / `<Image blur="4"/>` 在渲染测试里画出光晕 / 模糊，且双 sprite 图集不串色（Task 5 第 3 条）。
- [ ] 不写 fx 的文档：`FxImage.material == defaultMaterial`、组件数与改动前相同、画布不新增 `TexCoord2`。
- [ ] `tint="linear"` 与禁用灰度在 `<Image>` / `<Icon>` 上走 `FxImage` 参数，在 `<RawImage>` / `<Btn>` 底图上仍走旧材质，两组既有测试都绿。
- [ ] 五条 `PUI-FX-*` + `blur` 的 `PUI-PROCEDURAL-VALUE` 在 CLI 与运行时（TYPE）都生效；XSD 含三个新属性。
- [ ] Syncer 新建 atlas 两个打包开关为 false，已有 atlas 开着会告警。
- [ ] SKILL 五处 + README 更新，`grep PUI-FX-VALUE` 无残留。
- [ ] 零回归守门清单全绿；PR 已开在 `feat/image-fx-blur-glow`。
