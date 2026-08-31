# 玻璃融合台阶缝（`seam`）Implementation Plan

> **已完成。** 实施期的三处偏离与两条测试前提的更正记在 spec §8。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** weld 融合组里厚度不同的两块之间，沿交界画出真实熔接玻璃那样的细高光 + 轻微折射。厚度按子级**声明顺序覆盖**成一张高度图（不累加），新增承载者属性 `seam`（过渡带宽度，px，默认 3），tint 分界跟它走；组 shader 的成员形状改用单面板同一套角解算（`cut` / `notch` / `hexagon` / `rN` 都进融合组），`PUI-WELD-CORNER` 删除。

**Architecture:** 四层。(1) 属性：`GlassAttrParser.Seam` → `Frame.Seam` → `GlassGroupPanel.SetSeam` → `ApplyGroupParams` 写 `_GlassA.y`（**不经 `ProceduralPanel` / `GlassParams`** —— 只写 `weld`+`seam` 的承载者没有面板，理由见 spec §8.1）。(2) 成员打包：`_WeldRadii` 换成 `_WeldCornerW/H/Kind/Fillet` 四个数组 + `_WeldDepths.yz = (shape, hexW)`，钳制与 pill/hexagon 解算移到 GPU。(3) Shader：单循环——每成员一次 `PuguiResolveQuad` + `PuguiSdPanel` + `PuguiPanelNormal`；旧权重改在线 softmax；新增归一化 source-over 高度图折叠及其解析梯度；台阶项（高光 + 折射）加在外斜面项之后。(4) lint / XSD / 文档。

**Tech Stack:** Unity 6 / C# (LangVersion 9.0)、uGUI、CG/HLSL（`#pragma target 3.0`）。无新增包。

设计依据：`docs~/superpowers/specs/2026-08-31-glass-weld-seam-design.md`（下文 §N 均指该 spec 的节号）。

## Global Constraints

- **分支**：全部工作在 `feat/glass-seam`（Task 0 建）。**绝不提交到 main。**
- **LangVersion 9.0**：无 primary constructor、无 collection expression `[]`、无 `[field: SerializeField]`。
- **不用 System.Threading / Task**：本特性无异步。
- **Core 纯 C# 子集**：`Runtime/Core/Parser/GlassAttrParser.cs`、`Runtime/Core/Lint/*` 不得 `using UnityEngine`。
- **lint**：每个 Task 收尾从仓库根跑 `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`。**不要** `dotnet format analyzers --severity info`。
- **同 PR 必改 SKILL**（英文）：见 Task 7。
- **测试只经 Unity MCP 跑**。**禁止** `execute_menu_item("Assets/Reimport All")`；用 `refresh_unity(mode="force", scope="all")`。
- **Shader 改完必看图**：`GlassRenderTests` 的 `RenderAndSample(dumpName)` 会把 PNG 落到 `Application.temporaryCachePath`，控制台打印路径——**用 Read 工具打开看**，参数全对而图不对是玻璃的常态（procedural-style §12.2）。
- **Red first**：每个 Task 先写失败测试、跑一次看它**因正确的原因**失败，再写实现。

**RUN(ClassName) = 跑 EditMode 测试的标准流程：**
1. `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
2. `mcp__UnityMCP__read_console(action="get", types=["error"])` —— 编译错误（含 shader 编译错误）必须为空才继续
3. `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["ClassName"])` → 轮询 `mcp__UnityMCP__get_test_job(job_id=...)` 直到完成，读 pass/fail

**RUNEDITOR(ClassName)** 同上，`assembly_names=["PromptUGUI.Tests.EditorOnly"]`。

---

## File Structure

| 文件 | 责任 | 动作 |
|---|---|---|
| `Runtime/Core/Parser/GlassAttrParser.cs` | `Seam` 常量、`DefaultSeam = 3f`、范围 `[0, +∞)`、进 `NumericAttrs` | Modify |
| `Runtime/Controls/Internal/ProceduralMaterialCache.cs` | `GlassParams.Seam`（ctor / Equals / GetHashCode） | Modify |
| `Runtime/Controls/Internal/ProceduralPanel.cs` | `_seam` / `SetSeam` / `RawGlassParams` / `FlushParams` 透传 | Modify |
| `Runtime/Controls/Frame.cs` | `[UIAttr] Seam` | Modify |
| `Runtime/Controls/Internal/GlassGroupPanel.cs` | `_GlassA.y = seam`；成员角数组打包；删 `ResolveRadius` | Modify |
| `Runtime/Resources/PromptUGUI/Material/UI-GlassGroup.shader` | 单循环 + 角解算 + 高度图 + 台阶项 | Modify |
| `Runtime/Resources/PromptUGUI/Material/UI-PanelSDF.cginc` | 删 `PuguiSdRoundBox` / `PuguiSdNormal` | Modify |
| `Runtime/Core/Lint/GlassRules.cs` | `GroupAttrs += seam`；`SeamWithoutWeldCode`；删 `WeldCornerCode` 及角检查 | Modify |
| `Runtime/Core/Lint/ProceduralAttrNames.cs` | `All` / `NeedsPanel` 加 `seam` | Modify |
| `Editor/XsdGenerator.cs` | Frame 加 `seam` | Modify |
| `Tests/EditMode/Parser/NumericGuardTests.cs` | `seam` 的 NaN / 负数 / 默认 | Modify |
| `Tests/EditMode/Controls/FrameGlassPanelTests.cs` | `seam` 往返、默认 3 | Modify |
| `Tests/EditMode/Controls/GlassWeldGroupTests.cs` | `_GlassA.y`；角数组打包；替换两条 CPU 钳制用例 | Modify |
| `Tests/EditMode/Lint/GlassRulesTests.cs` | seam 放置 / seam-no-weld；删 5 条 WeldCorner | Modify |
| `Tests/EditMode/Lint/ProceduralAttrNamesTests.cs`、`PureContainerVisualAttrRulesTests.cs` | 名单含 `seam` | Modify |
| `Tests/EditMode/Editor/XsdGeneratorTests.cs` | `seam` | Modify |
| `Tests/EditMode/Controls/GlassRenderTests.cs` | `RenderAndSample` 加坐标；三条台阶/角渲染用例 | Modify |
| `.claude/skills/authoring-promptugui-xml/reference/glass.md`、`SKILL.md` | 文档 | Modify |
| `docs~/superpowers/specs/2026-08-27-corner-treatments-design.md` | §5 一句"已由 seam spec 取代" | Modify |
| `docs~/superpowers/specs/2026-08-31-glass-weld-seam-design.md` | §8 实施记录 | Modify |

---

## Task 0: 建分支 + 落 spec / plan

- [x] `git checkout -b feat/glass-seam`
- [x] `git add docs~/superpowers/specs/2026-08-31-glass-weld-seam-design.md docs~/superpowers/plans/2026-08-31-glass-weld-seam.md` → 提交（分支上）。

---

## Task 1: `seam` 属性走通到组材质（§3.1 / §4.1）

**Files:** `GlassAttrParser.cs`、`ProceduralMaterialCache.cs`、`ProceduralPanel.cs`、`Frame.cs`、`GlassGroupPanel.cs`
**Tests:** `NumericGuardTests`、`FrameGlassPanelTests`、`GlassWeldGroupTests`

- [x] **Step 1: 红测试**
  - `NumericGuardTests`：`[TestCase(GlassAttrParser.Seam, "NaN")]` / `"Infinity"` / `"-1"` 抛；`""` 回默认 3。
  - `FrameGlassPanelTests.Glass_Defaults`：`RawGlassParams.Seam == 3f`；`Glass_AllParamsRoundTrip` 加 `seam='5'`。
  - `GlassWeldGroupTests.GroupParams_ComeFromTheContainer`：容器 `seam='6'` → `mat.GetVector("_GlassA").y == 6`；`GroupParams_FallBackToDefaultsWithoutAContainerPanel`：`.y == DefaultSeam`。
  - `GlassWeldGroupTests`（新）：`Seam_OnAMember_DoesNotReachTheGroup`——成员写 `seam='9'`，`_GlassA.y` 仍是容器值/默认。
- [x] **Step 2: RUN** 三个类——新用例红（`GlassAttrParser.Seam` 不存在时先加常量让它编译）。
- [x] **Step 3: 实现**
  - `GlassAttrParser`：`public const string Seam = "seam"; public const float DefaultSeam = 3f;` 进 `NumericAttrs`；`Range` 分支 `min = 0, max = +∞, fallback = DefaultSeam`。
  - `GlassParams`：第 8 个字段 `Seam`；ctor 加参数；`Equals` / `GetHashCode` 纳入。**两个 `new GlassParams(...)` 调用点**（`ProceduralPanel.cs:289` / `:312`）与 `RawGlassParams` 都补 `_seam`。
  - `ProceduralPanel`：`private float _seam = GlassAttrParser.DefaultSeam; public void SetSeam(float v) { _seam = v; MarkDirty(); }`。
  - `Frame`：`[UIAttr, Preserve] public string Seam { set => Panel.SetSeam(GlassAttrParser.ParseValue(GlassAttrParser.Seam, value)); }`，XML doc 说明它是承载者参数。
  - `GlassGroupPanel.ApplyGroupParams`：`var seam = _container != null ? g.Seam : DefaultSeam;` → `_GlassA = (frost, seam, dispersion, noise)`。Shader 属性注释同步改成 `frost / seam / dispersion / noise`。
- [x] **Step 4: RUN** 三个类全绿。**Step 5: lint。**

---

## Task 2: lint / 属性名单 / XSD（§3.3）

**Files:** `GlassRules.cs`、`ProceduralAttrNames.cs`、`XsdGenerator.cs`
**Tests:** `GlassRulesTests`、`ProceduralAttrNamesTests`、`PureContainerVisualAttrRulesTests`、`XsdGeneratorTests`

- [x] **Step 1: 红测试**
  - `GlassRulesTests`：成员写 `seam` → `WeldParamPlacementCode`（id 指向成员）；`<Frame glass='true' seam='3'/>` → `SeamWithoutWeldCode`；`<Frame weld.mobile='10' seam='3'>` 不报；`<Frame weld='10' seam='3'>` 不报；`class=` 带 `seam` 且 style 未声明（uncertain）不报。**删除** 5 条 `WeldCornerCode` 用例（`GlassRulesTests.cs:195–240` 一带）。
  - `ProceduralAttrNamesTests`：`All` 含 `seam`，`PanelAttaching` 不含；`Frame` Meta `HasAttribute("seam")`。
  - `PureContainerVisualAttrRulesTests`：`[TestCase("Image", "seam")]` 等同 `weld` 的那组。
  - `XsdGeneratorTests`：`StringAssert.Contains("name=\"seam\"")`（照既有 `weld` 断言的写法）。
- [x] **Step 2: RUN + RUNEDITOR** → 红。
- [x] **Step 3: 实现**
  - `GlassRules`：`public const string SeamWithoutWeldCode = "PUI-GLASS-SEAM-NO-WELD";` `GroupAttrs` 加 `GlassAttrParser.Seam`；`Check` 里在 `if (isWeldGroup) {...; yield break;}` **之后**、`declaresGlassFlag` 之前：`if (styles.Declares(n, Seam)) yield return SeamWithoutWeld(...)`；随后 `NumericAttrs` 循环 `continue` 掉 `Weld` 和 `Seam`（seam 已单独报，不再叠报 NO-GLASS）。删 `WeldCornerCode`、`CheckWeldGroup` 里的角检查块及 `HasCornerTreatment` 辅助（若仅此处用）。
  - `ProceduralAttrNames.All` / `NeedsPanel` 末尾加 `"seam"`。
  - `XsdGenerator` Frame 列表加 `("seam", "xs:string", null)`。
- [x] **Step 4: RUN + RUNEDITOR** 全绿。**Step 5: lint。**

---

## Task 3: 成员角种类打包到组材质（§4.1 `GlassGroupPanel`）

**Files:** `GlassGroupPanel.cs`
**Tests:** `GlassWeldGroupTests`

- [x] **Step 1: 红测试**
  - `MemberCorners_ArePackedInCssOrder`：成员 `radius='cut 16'`（若语法为逐角，写一个 TL 圆 8 / TR cut 16 的混合）→ `_WeldCornerKind[0] == (0,1,1,1)`-类断言、`_WeldCornerW[0]` / `_WeldCornerH[0]` 是原始（未钳制）值、`_WeldCornerFillet[0]` 为 `rN`。
  - `MemberShape_IsPassedAsASentinel`：成员 `radius='pill'` → `_WeldDepths[0].y == (float)PanelShape.Pill`；`hexagon` → `.y == Hexagon`、`.z == HexWidth`。
  - **替换** `MemberPill_IsResolvedOnTheCpu` / `OversizedRadius_IsClampedToTheBlock`：新断言是"原样下发、不钳制"（`radius='500'` → `_WeldCornerW[0].x == 500`）。
  - `_WeldRadii` 不再存在：`GetVectorArray("_WeldRadii")` 返回 null/空——不用断言，删除引用即可。
- [x] **Step 2: RUN(GlassWeldGroupTests)** → 红。
- [x] **Step 3: 实现**
  - 新 `PropertyToID`：`_WeldCornerW` / `_WeldCornerH` / `_WeldCornerKind` / `_WeldCornerFillet`；对应四个 `Vector4[MaxMembers]` 字段；删 `_radii` / `WeldRadiiId` / `ResolveRadius`。
  - 打包：`_cornerW[count] = p.CornerWidth; _cornerH[count] = p.CornerHeight; _cornerKind[count] = p.CornerKinds; _cornerFillet[count] = p.CornerFillet; _depths[count] = new Vector4(p.GlassParams.Depth, (float)p.Shape, p.HexWidth, 0f);`——与 `ProceduralPanel` 给单面板 shader 的是**同一组值**（去 `ProceduralPanel` 里找它上传 `_CornerKind` / `_Shape` / `_HexW` 的代码核对枚举转换）。
  - 空槽清零照旧。
- [x] **Step 4: RUN** 全绿（shader 还没读新数组，材质 SetVectorArray 不会报错）。**Step 5: lint。**

---

## Task 4: Shader——角解算 + 单循环 + 高度图 + 台阶项（§2 / §4.2）

**Files:** `UI-GlassGroup.shader`、`UI-PanelSDF.cginc`
**Tests:** 先跑既有 `GlassRenderTests.WeldedGroup_Renders`（编译 + 非品红即可），真正的断言在 Task 5。

- [x] **Step 1: 成员形状换角解算**
  - uniform：`float4 _WeldCornerW[8]; _WeldCornerH[8]; _WeldCornerKind[8]; _WeldCornerFillet[8];` 删 `_WeldRadii`；`_WeldDepths` 注释 `x = depth, y = shape, z = hexW`。
  - `MemberSd(i, p, out float2 n)`：`float2 q = p - rect.xy; PuguiQuad quad = PuguiResolveQuad(q, rect.zw, _WeldCornerKind[i], _WeldCornerW[i], _WeldCornerH[i], _WeldCornerFillet[i], _WeldDepths[i].y, _WeldDepths[i].z); n = PuguiPanelNormal(q, rect.zw, quad); return PuguiSdPanel(q, rect.zw, quad);`
  - cginc 删 `PuguiSdRoundBox` / `PuguiSdNormal` 和那段"只剩 weld 在用"的注释。
- [x] **Step 2: 两遍并一遍（在线 softmax）**
  ```
  float k = max(_Weld, 1e-4);  float s = 1.0 / k;
  float2 dpdx = ddx(p), dpdy = ddy(p);
  float unitsPerPixel = max(sqrt(0.5 * (dot(dpdx,dpdx) + dot(dpdy,dpdy))), 1e-5);
  float seam = max(_GlassA.y, 2.0 * unitsPerPixel);

  float d = 1e6, dmin = 1e6;            // smin 折叠 / 权重基准
  float wsum = 0, depth = 0; float2 nrm = 0;     // 旧权重（外斜面用）
  float acc_h = 0, cov = 0; float4 acc_t = 0;    // 高度图折叠
  float2 g_h = 0, g_cov = 0;                     // 其梯度
  for (int j = 0; j < _WeldCount; j++) {
      float2 nj; float dj = MemberSd(j, p, nj);
      // 在线 softmax：基准下降时把已累加的量按 exp((dnew - dold)/k) 缩放
      float newMin = min(dmin, dj); float rescale = exp((newMin - dmin) * s);   // 首次 dmin=1e6 → exp(-大) = 0，累加器本来就是 0
      wsum *= rescale; depth *= rescale; nrm *= rescale; dmin = newMin;
      float w = exp(-(dj - dmin) * s);
      wsum += w; depth += _WeldDepths[j].x * w; nrm += nj * w;
      d = (j == 0) ? dj : PuguiSmin(d, dj, k);
      // 高度图：居中 S 形软覆盖 + 解析梯度
      float u = saturate(0.5 - dj / seam);
      float r = u * u * (3.0 - 2.0 * u);
      float2 gr = (6.0 * u * (1.0 - u)) * (-nj / seam);
      float tj = ...;  float4 tintj = lerp(_WeldTintBottom[j], _WeldTintTop[j], tj);
      g_h   = (1.0 - r) * g_h   + (_WeldDepths[j].x - acc_h) * gr;
      g_cov = (1.0 - r) * g_cov + (1.0 - cov) * gr;
      acc_h = lerp(acc_h, _WeldDepths[j].x, r);
      acc_t = lerp(acc_t, tintj, r);
      cov   = lerp(cov, 1.0, r);
  }
  float invCov = 1.0 / max(cov, 1e-4);
  float h = acc_h * invCov;  float4 tint = acc_t * invCov;
  float2 G = (g_h * cov - acc_h * g_cov) * invCov * invCov;   // 商法则
  ```
  `exp((newMin - dmin) * s)` 在 `dmin = 1e6` 首轮会下溢为 0——正确（累加器为 0）；但 `exp` 参数极大负值在部分 GLES 上可能给 NaN：用 `dmin = (j == 0) ? dj : min(dmin, dj)` 且 `rescale = (j == 0) ? 0.0 : exp(...)` 规避。
  `tint` / `depth` / `fusedNormal` 后续用法不变（`depth *= 1/wsum` 等）。
- [x] **Step 3: 台阶项**（放在 `rgb += spec * bevel * intensity;` 之后、`crease` 之前）
  ```
  float slope = length(G);
  float sStep = saturate(slope);
  float2 nStep = slope < 1e-5 ? float2(0,1) : -G / slope;
  float ndlS = dot(nStep, lightDir);
  float specS = pow(saturate(ndlS), 4.0) + 0.35 * pow(saturate(-ndlS), 4.0);
  rgb += specS * sStep * intensity * inside;
  ```
  折射偏移：在采样**之前**把 `offset += nStep * sStep * (seam / unitsPerPixel) * 0.5 / _ScreenParams.xy;`（色散的 `spread` 自然覆盖到它）。因此 `G` 的计算必须在 backdrop 采样之前——它已在循环里，满足。
- [x] **Step 4: RUN(GlassRenderTests)**：`read_console` 无 shader 编译错误；`WeldedGroup_Renders` 非品红。**打开 dump 的 PNG 看**：L 形连续、交界处有一道细高光。
- [x] **Step 5: lint**（shader 不在 dotnet format 范围，C# 无改动也跑一遍确认）。

---

## Task 5: 真渲染断言（§5）

**Files:** `GlassRenderTests.cs`

- [x] **Step 1: harness**：`RenderAndSample(string dumpName = null, int x = Size/2, int y = Size/2)`——读任意像素；再加一个 `RenderAndSampleMany(dumpName, params (int x, int y)[] pts)` 一次渲染多点（避免三次 `Render()`）。注意 `ReadPixels` 的 y 轴朝上，坐标以 RT 左下为原点——在断言里用相对布局中心的偏移计算。
- [x] **Step 2: 红测试**（先写，跑一次确认红——Task 4 已实现的话它们应直接绿；若绿，故意把 `sStep` 乘 0 验证一次它们真的在测台阶，再改回）
  1. `Seam_LightsTheThickSideOfTheStep`：容器 `weld='14' seam='3' lightAngle='-90'`（光从正左），`a` = `anchor='top-stretch' height='80' depth='8'`，`b` = `anchor='bottom-stretch' height='70' depth='3'`（相接于 y=80）——此时台阶法线朝下（−y），光从左照台阶不亮；换成左右相接：`a` 左半 `depth='8'`、`b` 右半 `depth='3'`，交界竖线 x=110，`lightAngle='-90'` 光从左——台阶下坡朝右（背光）→ 弱；`lightAngle='90'` 光从右 → 亮。断言：`lightAngle='90'` 时交界线上偏厚块 1 px 的像素 luma − 薄块内部 20 px 处像素 luma > 0.05。
  2. `EqualDepths_HaveNoStep`：同布局两块都 `depth='8'`：交界像素与薄块内部像素 |Δluma| < 2/255。
  3. `CutCornerMember_IsCutInTheGroup`：单成员 + 一个远离的第二成员（凑够 2）；成员 `radius='cut 40'` 的角外 (12,12) px 处像素 alpha/亮度 == 背景（黑），而 `radius='40'` 时同点已绘制。
- [x] **Step 3: RUN(GlassRenderTests)** 全绿。**看三张 PNG**。
- [x] **Step 4: lint。**

---

## Task 6: 全量回归

- [x] RUN 全部 EditMode（不带 `group_names`）+ RUNEDITOR 全部 + PlayMode 全部；`read_console` 无 error / 无新 warning。
- [x] `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` 通过（本特性不改内置 xml，仍跑一次确认 lint 规则改动没误伤）。
- [x] Samples~ 里若有 weld 用例（`grep -rn weld= Samples~`），跑 UIXmlLint 确认不再报 `PUI-WELD-CORNER`。

---

## Task 7: 文档（§7）

- [x] `reference/glass.md`：
  - 参数表（承载者栏）加 `seam`；
  - "Fusing panels" 小节：把"lets their thickness — not a line — say which is primary"扩成一段：the step is drawn（highlight on the lit side + slight refraction）、stacking = declaration order, later overwrites（thick over thin = ridge, thin over thick = groove）、`seam`；
  - 删 "Corner treatments do not survive the fusion" 一条，改为一句 "Members keep their corner treatments (`cut` / `notch` / `hexagon` / `rN`)"；
  - 放置表：容器栏加 `seam`；
  - Lint 表：删 `PUI-WELD-CORNER`，加 `PUI-GLASS-SEAM-NO-WELD`。
- [x] `SKILL.md`：Frame 属性表 `weld` 行下加 `seam` 行；第 192 行 "One exception: `weld`" 一条删除。
- [x] `2026-08-27-corner-treatments-design.md` §5 `PUI-WELD-CORNER` 段后加一句：`（2026-08-31 起由 glass-weld-seam spec 取代：融合组成员支持全部角种类，该码已删除）`。
- [x] 本 spec 加 §8 实施记录（偏离设计之处 + 测试计数 + PNG 目视结论）。

---

## Task 8: PR

- [x] `git push -u origin feat/glass-seam`；`gh pr create`——标题 `feat: 玻璃融合台阶缝 —— 厚度差的高光与折射、seam、成员角种类 (#N)` 风格与近期一致；正文列作者可见变更（叠放顺序、tint 分界、角种类、`PUI-WELD-CORNER` 删除）+ PNG。
