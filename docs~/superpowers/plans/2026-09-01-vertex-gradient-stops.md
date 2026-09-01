# 顶点路径的渐变色标与提示（VGS）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让已有的渐变色标 / 提示语法（`A 70%, B`、`A, 70%, B`）在所有顶点着色的 Graphic 上真正生效（`<Image>` / `<Icon>` / `<RawImage>`、控件底图、进度条填充……），并把 `flip` / `rotation` 与渐变的执行顺序钉死为「渐变在最终网格上算」。交付物之一是 SKILL 里的倒影配方 `<Icon flip="y" color="white/0.35, white/0 50%"/>`。

**Architecture:** `ColorSpec.Evaluate(s)` 与 shader `PuguiFillRamp` 同公式；新 `MeshSlicer` 沿水平线切三角形流；`GradientTint` 在 `HasStops` 时改走「de-index → 切分 → 逐顶点求值 → 透明端剔除」，否则原路径逐位不变；`RotateFlipApplier.ReserveSlot` 让三个叶子控件在渐变落地前先占住 `RotateFlipEffect` 的槽位；`GradientStopRules` 收窄到 TMP 路径。无新属性、无新标签、无 XSD 改动。

**Tech Stack:** Unity 6 / C# (LangVersion 9.0)、uGUI（`BaseMeshEffect` / `VertexHelper.GetUIVertexStream` / `AddUIVertexTriangleStream`）。无新增包。

设计依据：`docs~/superpowers/specs/2026-09-01-vertex-gradient-stops-design.md`（下文 §N 指该 spec；决策 `VGS-Dn`）。上游：`2026-08-30-gradient-stop-position-design.md`（§5 / §12 预留了本 plan 的改动面）。

## Global Constraints

- **分支**：全部工作在 `feat/vertex-gradient-stops`（Task 0 建）。**绝不提交到 main。**
- **LangVersion 9.0**：无 primary constructor、无 collection expression `[]`、无 `[field: SerializeField]`。`in` 参数 / target-typed `new()` / `??=` 可用。
- **不用 System.Threading / Task**：本特性无异步。
- **Core 纯 C# 子集**：`Runtime/Core/Lint/GradientStopRules.cs` 的改动 **不得** `using UnityEngine`。`ColorSpec` 在 `Application/`，不在 CLI 编译集，可用 `Color` / `Mathf`。
- **lint**：每个 Task 收尾从仓库根跑 `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`。**不要** `dotnet format analyzers --severity info`。
- **CLI 编译守门**：Task 7 之后跑 `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/`，确认规则改动在 Unity 外仍编译、内置文档零 error。
- **同 PR 必改 SKILL**（英文）：见 Task 10。
- **测试只经 Unity MCP 跑**。**禁止** `execute_menu_item("Assets/Reimport All")`；用 `refresh_unity(mode="force", scope="all")`。
- **InternalsVisibleTo** 已对 `PromptUGUI.Tests.EditMode` / `PromptUGUI.Tests.PlayMode` 开放。
- **Red first**：每个 Task 先写失败测试、跑一次看它**因正确的原因**失败，再写实现。
- **零回归守门**：`GradientTintTests` / `GradientColorAttrTests` / `StateGradientTests` / `ColorApplierTests` / `GradientStopPanelTests` / `GradientStopResolveTests` / `RotateFlipEffectTests` / `ImageRotateFlipTests` / `DecorRenderTests` / `ProceduralSurfaceRenderTests` 每个里程碑末尾必跑。

**RUN(ClassName) = 跑 EditMode 测试的标准流程：**
1. `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
2. `mcp__UnityMCP__read_console(action="get", types=["error"])` —— 编译错误必须为空才继续
3. `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["ClassName"])` → 轮询 `mcp__UnityMCP__get_test_job(job_id=...)` 直到完成，读 pass/fail；**核对 `summary.total` > 0**

**RUNPLAY(ClassName)** 同上，`mode="PlayMode"` + `assembly_names=["PromptUGUI.Tests.PlayMode"]`（PlayMode 连跑前先 force refresh，否则第二个 job 会 0 条执行还报 Passed）。

---

## File Structure

| 文件 | 责任 | 动作 |
|---|---|---|
| `Runtime/Application/ColorSpec.cs` | `Evaluate(float s)`；`HasStops` 注释改口 | Modify |
| `Runtime/Controls/Internal/MeshSlicer.cs` | `Lerp(in UIVertex, in UIVertex, float)`；`SplitAlongY(List<UIVertex> tris, float y, List<UIVertex> output)` | Create |
| `Runtime/Controls/Internal/GradientTint.cs` | `Set(in ColorSpec)` / `Spec`；`HasStops` 分支（切分 + 求值 + 剔除） | Modify |
| `Runtime/Controls/Internal/ColorApplier.cs` | `Apply` 传整个 spec；`Peek` 回读整个 spec | Modify |
| `Runtime/Controls/Internal/RotateFlipApplier.cs` | `ReserveSlot(Graphic)` | Modify |
| `Runtime/Controls/Image.cs` `Icon.cs` `RawImage.cs` | `Color` setter：渐变时先 `ReserveSlot`；删 `GradientStopWarning.IfMoved` | Modify |
| `Runtime/Controls/Internal/ProceduralSurface.cs` | 删 `Reconcile` 关闭分支里的 `GradientStopWarning.IfMoved` | Modify |
| `Runtime/Controls/Internal/GradientStopWarning.cs` | 注释：只剩 TMP 路径在用 | Modify |
| `Runtime/Core/Lint/GradientStopRules.cs` | 只保留 `Text` + `textColor` / `itemTextColor` | Modify |
| `Tests/EditMode/Application/ColorSpecEvaluateTests.cs` | §4.1 求值 | Create |
| `Tests/EditMode/Controls/MeshSlicerTests.cs` | 纯几何切分 | Create |
| `Tests/EditMode/Controls/GradientTintStopTests.cs` | 色标 / 提示 / 硬边 / 剔除 / 原路径不变 / Peek 往返 | Create |
| `Tests/EditMode/Controls/GradientFlipOrderTests.cs` | 组件顺序与属性书写顺序无关；恒等零组件 | Create |
| `Tests/EditMode/Controls/GradientStopRenderTests.cs` | RT 像素回归 | Create |
| `Tests/EditMode/Lint/GradientStopLintTests.cs`（既有） | 期望翻转 | Modify |
| `Tests/EditMode/Controls/ColorApplierTests.cs`（既有） | Peek 保形 | Modify |
| `Tests/PlayMode/Controls/GradientPlayTests.cs`（既有） | 带色标的 `<Image>` 过帧 smoke | Modify |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` | Gradients 节改写；Image/Icon/RawImage 行；Reflection recipe；错误表 | Modify |
| `.claude/skills/authoring-promptugui-xml/reference/states.md` | 第 9 行状态色色标说明 | Modify |
| `.claude/skills/authoring-promptugui-pxl/SKILL.md` | 一句：倒影不要画进 `.pxl` | Modify |
| `docs~/superpowers/specs/2026-08-30-gradient-stop-position-design.md` | §12 第一条标注「已补（VGS）」 | Modify |
| `docs~/superpowers/specs/2026-09-01-vertex-gradient-stops-design.md` | §14 实施记录 | Modify |

---

## Task 0: 建分支 + 落 plan

- [ ] `cd C:/xsoft/PromptUGUI && git checkout -b feat/vertex-gradient-stops`
- [ ] `git add docs~/superpowers/specs/2026-09-01-vertex-gradient-stops-design.md docs~/superpowers/specs/2026-09-01-graphic-reflection-design.md docs~/superpowers/plans/2026-09-01-vertex-gradient-stops.md` 并提交（分支上）。

---

# M1 —— 求值与切分（纯逻辑）

## Task 1: `ColorSpec.Evaluate(float s)`（spec §4.1）

**Files:**
- Modify: `Runtime/Application/ColorSpec.cs`
- Test: `Tests/EditMode/Application/ColorSpecEvaluateTests.cs`

**Interface：**
```csharp
/// <summary>The colour at normalized distance <paramref name="s"/> from the TOP edge (0 = top,
/// 1 = bottom) — the same ramp <c>PuguiFillRamp</c> draws per fragment, evaluated per vertex.</summary>
public Color Evaluate(float s)
{
    var span = Mathf.Max(BottomStop - TopStop, 1e-4f);
    var u = Mathf.Clamp01((s - TopStop) / span);
    if (Curve != 1f) u = Mathf.Pow(u, Curve);
    return Color.Lerp(Top, Bottom, u);
}
```

- [ ] **Step 1: 红测试** —— `ColorSpecEvaluateTests`：
  - `Solid(c).Evaluate(0.3f) == c`。
  - `Gradient(red, blue).Evaluate(s)` 在 `s ∈ {0, 0.25, 0.5, 1}` 等于 `Color.Lerp(red, blue, s)`（无色标时与今天的顶点公式一致）。
  - `Gradient(red, blue, 0.3f, 0.6f)`：`Evaluate(0.1f) == red`、`Evaluate(0.9f) == blue`、`Evaluate(0.45f) ≈ Lerp(red, blue, 0.5f)`。
  - 硬边 `Gradient(red, blue, 0.5f, 0.5f)`：`Evaluate(0.499f) == red`、`Evaluate(0.501f) == blue`。
  - 提示：`curve = ColorParser.StopCurveExponent(0f, 1f, 0.3f)`；`Gradient(white, black, 0f, 1f, curve).Evaluate(0.3f)` 的 r ≈ 0.5（容差 0.01）。
- [ ] **Step 2**: RUN(ColorSpecEvaluateTests) —— 因 `Evaluate` 不存在而编译失败即为正确的红。
- [ ] **Step 3**: 实现；`HasStops` 的 `<summary>` 改为「作者塑形过 ramp；shader 与 `GradientTint` 的切分路径都能画，只有 TMP 文本画不了」。
- [ ] **Step 4**: RUN(ColorSpecEvaluateTests) 绿；RUN(GradientStopResolveTests) 绿（回归）。
- [ ] **Step 5**: `dotnet format --verify-no-changes`；提交 `feat(color): ColorSpec.Evaluate mirrors PuguiFillRamp`。

## Task 2: `MeshSlicer`（spec §4.2 第 3 步）

**Files:**
- Create: `Runtime/Controls/Internal/MeshSlicer.cs`
- Test: `Tests/EditMode/Controls/MeshSlicerTests.cs`

**Interface：**
```csharp
internal static class MeshSlicer
{
    /// <summary>Linear blend of every UIVertex channel: position, normal, tangent, colour, uv0–uv3.</summary>
    public static UIVertex Lerp(in UIVertex a, in UIVertex b, float t);

    /// <summary>
    /// Splits a de-indexed triangle list (3 verts per triangle) along the horizontal line y = cut.
    /// Triangles wholly on one side are appended unchanged; a crossing triangle becomes the tip
    /// triangle plus a quad (two triangles). New vertices are Lerp'ed along the crossed edges and
    /// their y is set to exactly <paramref name="cut"/>. Winding is preserved. Appends to output.
    /// </summary>
    public static void SplitAlongY(List<UIVertex> tris, float cut, List<UIVertex> output);
}
```

**算法（写进代码注释）：** 对每个三角形 `(a,b,c)` 算 `side(v) = v.y > cut ? +1 : v.y < cut ? −1 : 0`。
- 没有严格异号的顶点（含线上顶点）→ 原样追加。
- 恰有一个顶点严格在一侧、另两个严格在另一侧 → 把三角形循环旋转（`(a,b,c)→(b,c,a)→(c,a,b)`，保绕向）使孤立顶点为 `p`，其余 `q`、`r`；`mq = Lerp(p, q, (cut−p.y)/(q.y−p.y))`、`mr` 同理，两者 `y = cut`；追加 `(p, mq, mr)`、`(mq, q, r)`、`(mq, r, mr)`。
- 一个顶点在线上、另两个异号 → 旋转使线上顶点为 `p`；`m = Lerp(q, r, (cut−q.y)/(r.y−q.y))`，`m.y = cut`；追加 `(p, q, m)`、`(p, m, r)`。
- `Color32` 用 `Color32.Lerp`；`Vector4` 的 uv 用 `Vector4.Lerp`。

- [ ] **Step 1: 红测试** —— `MeshSlicerTests`（顶点用 `UIVertex.simpleVert` 改 position / color / uv0）：
  - 单三角形 `(0,0) (100,0) (50,100)` 切 `y=50` → 输出 9 顶点（3 个三角形）；所有新顶点 `y == 50`（精确相等，非近似）；线上顶点 uv0 / color 为两端按 0.5 插值。
  - 四边形（两个三角形，0..100）切 `y=50` → 4 个三角形（12 顶点）；`y>50` 的顶点与 `y<50` 的顶点各占一半。
  - 有顶点恰在线上的三角形 `(0,50) (100,0) (100,100)` 切 `y=50` → 2 个三角形，且没有面积为 0 的三角形（用叉积断言 |area| > 1e-3）。
  - 线在包围盒外（`y=200`）→ 输出与输入逐顶点相同。
  - 绕向：输入顺时针（uGUI 的 BL, TL, TR 序）→ 输出每个三角形叉积符号相同。
  - 连切两线（30、60）→ 三段各自的顶点 y 全在对应区间内。
- [ ] **Step 2**: RUN(MeshSlicerTests) 红。
- [ ] **Step 3**: 实现。
- [ ] **Step 4**: RUN(MeshSlicerTests) 绿。
- [ ] **Step 5**: format；提交 `feat(mesh): MeshSlicer splits UIVertex triangle lists along a horizontal line`。

---

# M2 —— `GradientTint` 色标路径

## Task 3: `GradientTint.Set(in ColorSpec)` + `ColorApplier` 全 spec 往返（spec §4.6）

**Files:**
- Modify: `Runtime/Controls/Internal/GradientTint.cs`、`Runtime/Controls/Internal/ColorApplier.cs`
- Test: `Tests/EditMode/Controls/ColorApplierTests.cs`（既有，加用例）、`Tests/EditMode/Controls/GradientTintStopTests.cs`（新，先放 Set/Spec 用例）

**Interface：**
```csharp
// GradientTint
public void Set(in ColorSpec spec);          // 存整个 spec；变化时 SetVerticesDirty
public void Set(Color top, Color bottom)     // 便捷重载 → Set(ColorSpec.Gradient(top, bottom))
public ColorSpec Spec { get; }
public Color Top => Spec.Top;  public Color Bottom => Spec.Bottom;   // 既有测试在用，保留
// ColorApplier
Apply: tint.Set(spec)             // 不再拆 Top/Bottom
Peek : tint.enabled ? tint.Spec : ColorSpec.Solid(g.color)
```

- [ ] **Step 1: 红测试**：
  - `ColorApplierTests`：`Apply(g, Gradient(red, blue, 0.3f, 0.6f, 2f))` 后 `Peek(g)` 的 `TopStop == 0.3f`、`BottomStop == 0.6f`、`Curve == 2f`（今天会被重建成 0/1/1 → 红）。
  - `GradientTintStopTests.Set_Spec_RoundTrips`：`fx.Set(spec)` 后 `fx.Spec` 各字段相等；再 `Set` 同值不 dirty（可选：用 `graphic.SetVerticesDirty` 无公开计数，跳过 dirty 断言）。
- [ ] **Step 2**: RUN(ColorApplierTests) / RUN(GradientTintStopTests) 红。
- [ ] **Step 3**: 实现（`ModifyMesh` 暂仍用 `Spec.Top/Bottom` 走原路径）。
- [ ] **Step 4**: RUN 上述两类 + RUN(GradientTintTests) / RUN(StateGradientTests) / RUN(GradientColorAttrTests) 绿。
- [ ] **Step 5**: format；提交 `refactor(gradient): GradientTint keeps the whole ColorSpec; Peek round-trips stops`。

## Task 4: 色标 / 提示的切分路径 + 透明端剔除（spec §4.2–4.3，VGS-D1/D2/D4）

**Files:**
- Modify: `Runtime/Controls/Internal/GradientTint.cs`
- Test: `Tests/EditMode/Controls/GradientTintStopTests.cs`、`Tests/EditMode/Controls/GradientTintTests.cs`（既有，加一条「无色标不 de-index」）

**`ModifyMesh` 结构：**
```csharp
public override void ModifyMesh(VertexHelper vh)
{
    if (!IsActive() || vh.currentVertCount == 0) return;
    if (!_spec.HasStops) { ModifyPlain(vh); return; }     // 今天的循环，原样搬进去
    ModifyWithStops(vh);
}

private const int HintStrips = 8;   // VGS-D4

private void ModifyWithStops(VertexHelper vh)
{
    var tris = ListPool<UIVertex>.Get(); var scratch = ListPool<UIVertex>.Get();   // UnityEngine.Pool
    vh.GetUIVertexStream(tris);
    // bounds
    float minY = +inf, maxY = -inf; foreach v: track
    var h = maxY - minY; if (h <= 0f) { plain fallback; return; }
    // cut lines, local y
    var a = _spec.TopStop; var b = _spec.BottomStop;
    Cut(maxY - a * h); if (b != a) Cut(maxY - b * h);
    if (_spec.Curve != 1f && b > a)
        for (k = 1; k < HintStrips; k++) Cut(maxY - (a + (b - a) * k / HintStrips) * h);
    // Cut(y): MeshSlicer.SplitAlongY(tris, y, scratch); swap(tris, scratch); scratch.Clear();
    // colour + cull
    var yA = maxY - a * h; var yB = maxY - b * h; const float eps = 1e-3f;
    var cullBottom = _spec.Bottom.a <= 0f; var cullTop = _spec.Top.a <= 0f;
    for each triangle (i, i+1, i+2):
        if (cullBottom && all three y <= yB + eps) continue;
        if (cullTop && all three y >= yA - eps) continue;
        for each of the 3: v.color = (Color)v.color * _spec.Evaluate((maxY - v.position.y) / h); scratch.Add(v);
    vh.Clear(); vh.AddUIVertexTriangleStream(scratch);
    release pools
}
```
注意：`Evaluate` 在切线上的顶点处恰为 `a` / `b` / 提示切线值 —— 由 `MeshSlicer` 把 `y` 精确赋成切线值保证。

- [ ] **Step 1: 红测试** —— `GradientTintStopTests`，四边形 helper 复用 `GradientTintTests.BuildWhiteQuad`（0..100，白色）；读回用 `vh.GetUIVertexStream(list)`：
  - `Plain_NoStops_KeepsIndexedQuad`（放进既有 `GradientTintTests`）：`Set(red, blue)` 后 `vh.currentVertCount == 4`（未 de-index，VGS-D1）。
  - `Stop_Bottom50_CutsAtMidline`：`Set(Gradient(red, blue, 0f, 0.5f))` → 存在 `y == 50` 的顶点且其色 ≈ blue；`y == 100` 的顶点 ≈ red；`y == 0` 的顶点 ≈ blue；`y == 75` 处无顶点但 `y==50`/`y==100` 间线性 → 断言 `y==50` 顶点全为 blue 即可。
  - `Stop_TransparentBottom_CullsTail`：`Set(Gradient(white, white/0, 0f, 0.5f))` → 所有顶点 `y >= 50 − 1e-3`；顶点数 == 6（上半一个四边形 = 2 三角形）。
  - `Stop_TransparentTop_CullsHead`：对称。
  - `Stops_Band_30_60`：`Gradient(red, blue, 0.3f, 0.6f)` → `y >= 70` 全 red、`y <= 40` 全 blue（y 从顶边量：30% → y=70，60% → y=40）。
  - `HardEdge_50_50`：`Gradient(red, blue, 0.5f, 0.5f)` → `y == 50` 的顶点既有 red 也有 blue（两侧各持一份）；无面积为 0 的三角形。
  - `Hint_AddsStrips`：`Gradient(white, black, 0f, 1f, StopCurveExponent(0,1,0.3f))` → 不同 y 值的集合大小 == HintStrips + 1（0、12.5、…、100）；`y == 70`（s = 0.3）处顶点 r ≈ 0.5。
  - `Sliced_ManyTriangles_StillCorrect`：手工搭一个 3×3 九宫格（18 三角形）切 50% → 无异常，`y == 50` 顶点存在，上下颜色正确（防止「只在 4 顶点 quad 上对」）。
- [ ] **Step 2**: RUN(GradientTintStopTests) 红（色标被忽略：无 `y==50` 顶点）。
- [ ] **Step 3**: 实现。
- [ ] **Step 4**: RUN(GradientTintStopTests) / RUN(GradientTintTests) / RUN(StateGradientTests) / RUN(GradientColorAttrTests) / RUN(ColorApplierTests) 绿。
- [ ] **Step 5**: format；提交 `feat(gradient): stop positions and hints on vertex-coloured graphics (mesh slicing)`。

---

# M3 —— 顺序、警告与 lint

## Task 5: `RotateFlipApplier.ReserveSlot` + 三个 `Color` setter（spec §4.4，VGS-D3）

**Files:**
- Modify: `Runtime/Controls/Internal/RotateFlipApplier.cs`、`Runtime/Controls/Image.cs`、`Icon.cs`、`RawImage.cs`
- Test: `Tests/EditMode/Controls/GradientFlipOrderTests.cs`

**Interface：**
```csharp
// RotateFlipApplier
/// <summary>Attaches a disabled RotateFlipEffect if none exists, so it sits BEFORE any mesh effect
/// added afterwards (GradientTint) — the gradient is then evaluated on the rotated / flipped mesh
/// (spec 2026-09-01 VGS §4.4: "the first colour is always the top of what you see").</summary>
public static void ReserveSlot(Graphic graphic);
```
三个控件的 `Color` setter：
```csharp
var spec = UI.Theme.ResolveSpec(value);
if (spec.IsGradient) Internal.RotateFlipApplier.ReserveSlot(_img);   // before the tint lands
Internal.ColorApplier.Apply(_img, spec);
// GradientStopWarning.IfMoved(...) 删除（Task 6 统一删，这里可先删掉以免两次提交打架）
```

- [ ] **Step 1: 红测试** —— `GradientFlipOrderTests`（`UI.ResetForTests` + `UI.LoadDocument` + `UI.Open`）：
  - `ColorThenFlip_EffectsInCanonicalOrder`：`<Image id='g' color='#fff,#000' flip='y'/>` → `GetComponents<BaseMeshEffect>()` 类型序列 == `[RotateFlipEffect, GradientTint]`（今天是 `[GradientTint, RotateFlipEffect]` → 红）。
  - `FlipThenColor_SameOrder`：反过来写，同样序列。
  - `Icon_And_RawImage_SameRule`：`<Icon name='ui:x' color='#fff,#000' flip='y'/>`（需 stub SpriteResolver，参考 `ResolveSpriteTests` 的 `Sprite.Create(Texture2D.whiteTexture, …)`）、`<RawImage …/>` 同断言。
  - `Variant_SolidToGradient_StillOrdered`：`<Image color='#fff' color.big='#fff,#000' flip='y'/>`，激活 `big` 后序列仍为 `[RotateFlipEffect, GradientTint]`。
  - `SolidColour_ReservesNothing`：`<Image color='#fff'/>` → 没有 `RotateFlipEffect` 也没有 `GradientTint`（恒等零组件承诺）。
  - `GradientWithoutFlip_ReservedButDisabled`：`<Image color='#fff,#000'/>` → 有 `RotateFlipEffect` 且 `enabled == false`。
- [ ] **Step 2**: RUN(GradientFlipOrderTests) 红。
- [ ] **Step 3**: 实现。
- [ ] **Step 4**: RUN(GradientFlipOrderTests) / RUN(ImageRotateFlipTests) / RUN(RotateFlipEffectTests) / RUN(GradientColorAttrTests) 绿。
- [ ] **Step 5**: format；提交 `fix(gradient): gradient is evaluated after rotation/flip regardless of attribute order`。

## Task 6: 撤运行时警告

**Files:**
- Modify: `Runtime/Controls/Image.cs`、`Icon.cs`、`RawImage.cs`（若 Task 5 未删）、`Runtime/Controls/Internal/ProceduralSurface.cs`、`Runtime/Controls/Internal/GradientStopWarning.cs`

- [ ] **Step 1**: `grep -rn "PUI-GRADIENT-STOP-NO-SURFACE\|gradient stop position, but it paints" Tests/` —— 找出对运行时 warning 的 `LogAssert.Expect` 期望（重点 `GradientStopPanelTests`、`GradientColorAttrTests`）。对 Image / Icon / RawImage / 无表面控件的期望：改为**断言不再有 warning**（`LogAssert.NoUnexpectedReceived()`）且 `GradientTint.Spec.HasStops == true`。对 `<Text>` / label 的期望保留。
- [ ] **Step 2**: RUN 这些类 → 改过期望的用例红。
- [ ] **Step 3**: 删四处 `GradientStopWarning.IfMoved` 调用（`Text.cs` / `LabelColorApplier.cs` 保留）；`GradientStopWarning` 的 `<summary>` 改为「只剩 TMP 路径：`<Text>` 与 label 颜色」；`ProceduralSurface.Reconcile` 关闭分支的注释同步删掉。
- [ ] **Step 4**: RUN(GradientStopPanelTests) / RUN(GradientColorAttrTests) / RUN(ProceduralSurfaceRenderTests) 绿。
- [ ] **Step 5**: format；提交 `chore(gradient): drop the no-surface warning where stops now render`。

## Task 7: `GradientStopRules` 收窄（spec §7，VGS-D7）

**Files:**
- Modify: `Runtime/Core/Lint/GradientStopRules.cs`
- Test: `Tests/EditMode/Lint/GradientStopLintTests.cs`（既有）

**目标形态：**
```csharp
// 只剩 TMP：
private static readonly Dictionary<string, string> AlwaysVertexTags = new() { ["Text"] = "TMP paints a <Text> gradient per character, and four glyph corners have nowhere to put a stop" };
private static readonly Dictionary<string, string[]> TextAttrs = new()
{
    ["Btn"] = { "textColor" }, ["Tab"] = { "textColor" }, ["TabMenu"] = { "textColor" },
    ["Collapsible"] = { "textColor" }, ["Toggle"] = { "textColor" }, ["InputField"] = { "textColor" },
    ["Dropdown"] = { "textColor", "itemTextColor" },
};
// MainSurfaceAttrs / InnerSurfaceAttrs / NeverSurfaceAttrs 的非文本项与 DeclaresSurface 整段删除。
```
类注释改写：色标现在由 `GradientTint` 在顶点路径切分实现，规则只剩 TMP（`LabelColorApplier` → `VertexGradient`）。

- [ ] **Step 1: 改期望** —— `GradientStopLintTests`：`SpriteGraphic_WithStops_IsReported` → 改名 `_IsFine` 并断言 `IsFalse`；`Hint_OnASpriteGraphic_IsReported` → `_IsFine`；`Btn_WithoutProceduralAttrs_IsReported` / `Progress_BgColour_IsTheMainSurface` / `Slider_*` / `Progress_FrameColour_*` / `Toggle_CheckmarkColour_IsAlwaysReported` / `Dropdown_PopupColour_IsAlwaysReported` → 全部改为 `IsFalse`（保留用例，名字改 `_IsFine`，防止将来误加回去）；`Text_WithStops_IsReported` / `LabelColour_IsAlwaysReported` 保留；新增 `Dropdown_ItemTextColour_IsReported`。`Btn_WithAnUnresolvableClass_StaysQuiet` 保留（仍应安静）。
- [ ] **Step 2**: RUN(GradientStopLintTests) 红。
- [ ] **Step 3**: 实现。
- [ ] **Step 4**: RUN(GradientStopLintTests) 绿；`cd C:/xsoft/PromptUGUI && dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` 退出码 0（CLI 编译 + 内置文档零 error）。
- [ ] **Step 5**: format；提交 `feat(lint): PUI-GRADIENT-STOP-NO-SURFACE now only covers TMP text colours`。

---

# M4 —— 渲染回归与 PlayMode

## Task 8: `GradientStopRenderTests`（spec §8）

**Files:**
- Create: `Tests/EditMode/Controls/GradientStopRenderTests.cs`

夹具照抄 `DecorRenderTests`（`Camera` → `RenderTexture` 256² → `ReadPixels`，`ScreenSpaceCamera`，`Canvas.ForceUpdateCanvases()` + `_ui.Render()`，PNG dump 到 `Library/`）。被测节点：`<Image id='g' anchor='center' width='100' height='200' color='…'/>`（**无 sprite**：uGUI `Image` 在 `sprite == null` 时画纯色矩形，无需图集）。像素坐标：Image 顶边 / 底边在 RT 里的行由 rect 与 canvas 换算，写一个 `RowAt(fractionFromTop)` helper。

- [ ] **Step 1: 红测试**：
  - `Fixture_PaintsSolidImage`（永远第一条）：`color='#f00'` → 中心像素 r > 0.9。
  - `Stop50_TopRedBottomBlue`：`color='#f00, #00f 50%'` → 25% 高处 r > 0.8、b < 0.2；75% 处 b > 0.9；50% 处 r ≈ b ≈ 0.5（容差 0.15）。今天：全幅线性 → 25% 处 r ≈ 0.75/b ≈ 0.25，75% 处 b ≈ 0.75 → 第二、三条断言红。
  - `Hint30_HalfMixAtHint`：`color='#fff, 30%, #000'` → 30% 处灰度 ≈ 0.5（容差 0.08），15% 处 > 0.6。
  - `Flip_AttributeOrderIrrelevant`：`color='#fff, #000 50%' flip='y'` 与 `flip='y' color='#fff, #000 50%'` 两次渲染，10% 处都为白（r > 0.9），且两张图逐像素差 < 2/255（抽样 20 点）。
  - `TransparentTail_IsBackground`：背景黑，`color='#fff, #fff/0 50%'` → 75% 处 == 背景（r < 0.05），25% 处 r > 0.4。
  - `Sliced_Stop_Works`：需要 9-slice sprite —— 用 `Sprite.Create(tex 16×16, border (4,4,4,4))` 经 stub resolver 给 `<Image sprite='t:s' type='sliced' …>`；断言同 `Stop50`。
- [ ] **Step 2**: RUN(GradientStopRenderTests) —— 夹具自检绿、其余红。
- [ ] **Step 3**: 若有红且 M2 实现正确，先看 PNG dump 再改；不要靠放宽容差通过。
- [ ] **Step 4**: RUN(GradientStopRenderTests) 全绿；顺手在 `Hint30` 用例里把 K=8 的折线用 200px 高的 Image 看一眼 PNG（spec §12 第一条），肉眼可辨则把 `HintStrips` 提到 12 并记入实施记录。
- [ ] **Step 5**: format；提交 `test(gradient): pixel regression for stops, hints, flip order and tail culling`。

## Task 9: PlayMode smoke

**Files:**
- Modify: `Tests/PlayMode/Controls/GradientPlayTests.cs`

- [ ] **Step 1**: 加 `StopGradientImage_SurvivesFrames`：`<Image id='g' color='#fff, #fff/0 50%' flip='y' width='64' height='64'/>` 过两帧 → `GradientTint.enabled`、`Spec.BottomStop == 0.5f`、`GetComponents<BaseMeshEffect>()` 序列 `[RotateFlipEffect, GradientTint]`。
- [ ] **Step 2**: RUNPLAY(GradientPlayTests) 绿。
- [ ] **Step 3**: 提交 `test(gradient): play-mode smoke for stop gradients`。

---

# M5 —— 文档

## Task 10: SKILL 更新（英文，同 PR）

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`、`reference/states.md`、`.claude/skills/authoring-promptugui-pxl/SKILL.md`

- [ ] **Step 1**: `SKILL.md` **Gradients** 节：把「Stops only work on a procedural surface」整段 callout 改写为：
  - stops and hints work on **every** graphic — procedural surfaces (per fragment) and sprite / vertex-coloured graphics (`<Image>` / `<Icon>` / `<RawImage>`, control backgrounds, `<Progress>` fills, arrows, checkmarks…) alike; the one exception is **TMP text** (`<Text color>`, `textColor`, `itemTextColor`) → `PUI-GRADIENT-STOP-NO-SURFACE`;
  - a fully transparent end colour (`…, white/0 50%`) drops that part of the geometry entirely — no overdraw, no `mask="rect"` needed;
  - gradients are evaluated on the mesh **as finally drawn**: `rotation` / `flip` never change which end is "the top" — the first colour is always the top of what you see, whatever order the attributes are written in.
- [ ] **Step 2**: `<Image>` / `<RawImage>` 属性表的 `color` 行与 `<Icon>` 的 `color` 行：加「incl. stop positions / hints」。
- [ ] **Step 3**: **Rotation & flip** 小节之后新增 **Reflection recipe**（spec §5 的两个模板 + 代价清单：two declarations to keep in sync, letterbox symmetry, doubled transparent padding → `margin`, the reflection node still occupies its full rect in a stack → give it an explicit `height` if you want it tighter）。Quick reference 加一行 `REFLECTION  <Icon name="x" flip="y" color="white/0.35, white/0 50%"/>  — draw order = XML order; put a floor <Image> between the two for reflection < floor < object`。
- [ ] **Step 4**: 错误表 `PUI-GRADIENT-STOP-NO-SURFACE` 行改为「a gradient stop position / hint on TMP text (`<Text>`, `textColor`, `itemTextColor`) — TMP paints per glyph and cannot place one」。
- [ ] **Step 5**: `reference/states.md` 第 9 行：删掉「so it needs a procedural shape on the control … `PUI-GRADIENT-STOP-NO-SURFACE`」那半句，改为「stop positions / hints render on the Image-backed surface too」。
- [ ] **Step 6**: `authoring-promptugui-pxl/SKILL.md`：在素材建议处加一句 "Do not paint reflections into a `.pxl` — the XML side generates them (`flip="y"` + a stop gradient), see the XML skill's *Reflection recipe*."
- [ ] **Step 7**: 通读一遍三处改动，确认没有残留「stops only on procedural」的旧说法：`grep -n "procedural surface can" .claude/skills -r`。
- [ ] **Step 8**: 提交 `docs(skill): stops & hints on sprite graphics; reflection recipe`。

## Task 11: spec 实施记录 + 收尾

- [ ] **Step 1**: `2026-08-30-gradient-stop-position-design.md` §12 第一条改为「**已补**（2026-09-01，见 `2026-09-01-vertex-gradient-stops-design.md`）」。
- [ ] **Step 2**: `2026-09-01-vertex-gradient-stops-design.md` 加 §14 实施记录：测试计数（EditMode / EditorOnly / PlayMode 全量）、K 的最终值、与设计的偏差、顺带发现。
- [ ] **Step 3**: 全量回归：RUN 全部三个程序集（不带 `group_names`），`read_console(types=["error","warning"])` 确认无新增 warning；`dotnet format --verify-no-changes`；`dotnet run --project .lint/UIXmlLint -- Runtime/Resources/`。
- [ ] **Step 4**: 提交 `docs(spec): VGS implementation notes`；推分支、开 PR（标题 `feat: 顶点路径的渐变色标与提示 —— GradientTint 几何切分 + flip/渐变顺序钉死 + 倒影配方`，正文引用两份 spec；末尾附会话链接）。

---

## 验收清单（PR 描述里逐条勾）

- [ ] `<Icon flip="y" color="white/0.35, white/0 50%"/>` 在 Play 模式下是上实下透、到一半消失、下半无几何。
- [ ] `<Icon color="…" flip="y"/>` 与 `<Icon flip="y" color="…"/>` 画面相同。
- [ ] `<Frame color="A 70%, B"/>` 与 `<Image color="A 70%, B"/>` 的转换位置在同一行像素（±1）。
- [ ] 没写色标的既有文档：`GradientTintTests` 与所有渲染测试零改动通过。
- [ ] UIXmlLint 对 `<Image color="A 70%,B">` 不再报错，对 `<Text color="A 70%,B">` 仍报。
- [ ] SKILL 三处更新，`grep "procedural surface can"` 无残留。
