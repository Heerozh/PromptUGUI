# 玻璃融合台阶缝：厚度差的高光与折射 + `seam` + 成员角种类

**日期**：2026-08-31
**状态**：设计完成，待实施（分支 `feat/glass-seam`）。
**作用域**：在 [glass-fill](2026-08-23-glass-fill-design.md) §7 的 `weld` 融合组之上，把成员之间的**厚度台阶真正画出来**：像真实熔接玻璃那样，高出的一方沿交界有一道细高光、并把背景轻微折一下。为此（1）把逐像素厚度改成一张按**声明顺序覆盖**的高度图；（2）新增承载者属性 `seam`（台阶过渡带宽度，px，默认 3），tint 的分界跟它走；（3）组 shader 的成员形状改用单面板同一套角解算，`cut` / `notch` / `hexagon` / `rN` 全部进融合组，`PUI-WELD-CORNER` 退休。
**关联**：视觉层次与参数放置继承 glass-fill §7 / §13.5（组级 vs 逐块）；角解算复用 [corner-treatments](2026-08-27-corner-treatments-design.md) 与 [corner-fillet](2026-08-29-corner-fillet-design.md) 的 `PuguiResolveQuad` / `PuguiSdPanel` / `PuguiPanelNormal`；解析法线的理由继承 glass-fill §13.7（跨平台一致 + 分支安全）；真渲染验证义务继承 procedural-style §12.2。

---

## 1. 背景：台阶有数据、没有像素

参考图是一条 HUD 顶栏：左段等级区、右段资源区，两段玻璃的厚度不同，交界处高出的一方带一道很细的高光，像两片玻璃熔在一起。

`UI-GlassGroup.shader` 今天已经按 `exp(-(dⱼ-dmin)/k)` 权重把各块的 `depth` 混成逐像素的 `depth`，也有一条 smin 折痕的接触阴影（`crease`）。但折射和打光都被 `band = saturate(1 + d/depth)` 门控，`d` 是到**融合后外轮廓**的距离——内部那条厚 6 → 薄 3 的台阶 `band == 0`，既不折射也不受光。台阶只能从"外轮廓上斜面宽度变了"间接看出来。

三个附带的问题一起解决：

- **缝的位置不对。** 现在的权重偏向"像素更深入哪一块"，两块相接时缝在交界线上没问题；但小块**落在大块区域内部**时（参考图正是这种布局），缝会落在两条轮廓的中线上，而不是上层块自己的轮廓上。
- **缝太宽。** depth 混合的尺度就是 `k = weld`，logistic 过渡跨 2–4·k px；`weld="10"` 就是 20–40 px 的软带，不是细线。
- **梯形画不出来。** `GlassGroupPanel.ResolveRadius` 只给组 shader 打包圆角半径，`cut` / `notch` / `hexagon` 退化成同伸出量的圆角。当初的理由是"外轮廓反正会被 smin 圆掉、不值两个 uniform 数组"——内部台阶恰恰沿着成员**自己的** SDF 走，斜边正是台阶要画的东西，这个前提不再成立。

## 2. 视觉模型

在 glass-fill §2 五层之上，第 2 层（边缘折射带）与第 4 层（边缘光照）各加一个**内部台阶项**。外轮廓那一圈斜面、折痕、tint 合成、描边、发光全部不动。

### 2.1 高度图：按声明顺序覆盖，不累加

融合面是一片厚度不均的玻璃。逐像素高度 `h(p)`：

- 每个成员 j 的**软覆盖** `rⱼ = smoothstep(saturate(0.5 − dⱼ / seam))`：以成员自己的轮廓为中心、宽 `seam` 的 S 形过渡（轮廓上恰好 0.5）。居中而不是单侧，是为了两块**相接**时 `r_A + r_B ≡ 1`，接缝处不会凹下去。
- 按**子级声明顺序**（`SyncMembers` 收集的 sibling 顺序）source-over 折叠，再按覆盖率归一：

  ```
  acc_h = lerp(acc_h, depthⱼ, rⱼ)        acc_t = lerp(acc_t, tintⱼ, rⱼ)        cov = lerp(cov, 1, rⱼ)
  h = acc_h / max(cov, ε)                 tint = acc_t / max(cov, ε)
  ```

  后声明的块**覆盖**前面的（不相加）：重叠区的厚度就是上层块的 `depth`。厚块盖薄块 = 凸台阶；薄块盖厚块 = 凹槽。作者的心智模型：每块把自己的厚度"涂"进这片玻璃，后涂的盖住先涂的——与 uGUI 绘制顺序一致，不需要排序。

  归一化的四条保证（全部可在纸上验证）：
  1. **单块**：`acc_h = D·r`、`cov = r` → `h ≡ D` 直到轮廓——高度图在外轮廓处**没有**自己的坡，外轮廓仍只由既有斜面负责，台阶项无需在边缘另做遮罩（∇h 按商法则恰好为 0）。
  2. **相接、等厚**：`h ≡ D`，无沟。
  3. **相接、6|3**：从 6 到 3 的单调光滑台阶，宽约 `seam`，中点偏向先声明的一侧约 0.12·seam（seam=3 时 ≈ 0.35 px，不可辨）。
  4. **重叠**：台阶落在**后声明块的轮廓**上；先声明的块只要不露出来，形状随便（等级区可以是最朴素的矩形，不需要挖角去凑梯形）。

- 梯度解析求得，与 smin 的既有做法同一精神（不用 `ddx/ddy`，理由见 glass-fill §13.7）：`∇rⱼ = 6u(1−u) · (−nⱼ / seam)`，`u = saturate(0.5 − dⱼ/seam)`，`nⱼ` 是成员的解析外法线；折叠时同步折叠 `∇acc_h`、`∇cov`，最后按商法则得 `G = ∇h`。所有量都在同一个循环里、每个成员的 SDF 与法线只求一次。

### 2.2 台阶项

- 坡面法线 `n_step = −G/|G|`（朝下坡，即从厚块指向薄块），坡度强度 `s = saturate(|G|)`（1 = 45°，等厚时严格为 0）。
- **打光**：与外斜面同一公式 `pow(saturate(dot(n_step, L)), 4) + 0.35·pow(saturate(−dot), 4)`，乘 `s · lightIntensity · inside`。厚块朝光的那一侧亮起来，背光侧只有 0.35 的弱补光——"高出的一方有一点点高光"。不另加阴影项：`crease` 仍在外轮廓交界处负责压暗。
- **折射**：backdrop 采样偏移加 `n_step · s · (seam 的屏幕像素数) · 0.5`，与外斜面 `n · bevel · depth · 0.5` 同一约定（峰值位移 ≈ 半个 seam）。这一步是"台阶"和"画了条线"的区别：真玻璃的厚度台阶会把背景折一下。色散沿用同一份 `spread`。
- 加在外斜面项之后、`saturation` / `crease` / `noise` 之前——它和斜面高光同一层，都是"光打在玻璃上"。

### 2.3 保持不变

- 外斜面的逐像素 `depth`、法线仍按旧的 `exp(-(dⱼ-dmin)/k)` 权重混合——外轮廓一圈的观感与现在逐像素一致（只是 SDF 换成了完整角解算，圆角成员的值不变）。
- `crease`、描边、外/内发光、`mask="self"`、降级链全部不动。
- **等厚组只有一处变化**：tint 的过渡宽度从 ~`weld` 收窄到 ~`seam`（§3.2 已定案）。

## 3. 作者面

### 3.1 新属性 `seam`（承载者，`[UIAttr]`，Variant / ReSolve / `<Style>` 照常）

| 属性 | 取值 | 默认 | 说明 |
|---|---|---|---|
| `seam` | px，≥ 0 有限 | `3` | 厚度台阶的过渡带宽度。高光约占其一半。`0` = 最锐（运行时钳到 2 个屏幕像素，永远不会锐到消失）；台阶本身没有"关"——不想要就把 depth 写成一样 |

写在带 `weld` 的 Frame 上，是组级参数（一片玻璃只有一种熔接工艺）。语法/范围错误走 `GlassAttrParser` 同一张表（`PUI-PROCEDURAL-VALUE`）。

### 3.2 语义变更（既有作者可见）

- **叠放顺序**：重叠区厚度 = 后声明块的 `depth`，tint 亦然。
- **tint 分界跟 `seam` 走**：与厚度台阶重合、同样窄。等厚相接组的颜色过渡会略微变脆。
- **角种类进融合组**：`cut` / `notch` / `hexagon` / `rN` 在成员上按写的画，外轮廓和内部台阶都出斜边。`PUI-WELD-CORNER` 删除（不是保留为空规则：它描述的限制不存在了）。

### 3.3 lint

| 代码 | 含义 | 动作 |
|---|---|---|
| `PUI-GLASS-WELD-PARAM-PLACEMENT` | `seam` 写在成员上 | 既有码，`GroupAttrs` 加 `seam` |
| `PUI-GLASS-SEAM-NO-WELD` | `seam` 写在没有 `weld` 的节点上（单块玻璃 shader 不读它） | 新码，error |
| `PUI-PROCEDURAL-VALUE` | 负数 / 非有限 | 既有 |
| `PUI-WELD-CORNER` | — | **删除**（规则、常量、测试、文档） |

`ProceduralAttrNames.All` / `NeedsPanel` 加 `seam`（同 `weld`：不进 `PanelAttaching`——它不挂面板）。XSD `Frame` 加 `seam`。

## 4. 实现

### 4.1 C#

| 文件 | 改动 |
|---|---|
| `Core/Parser/GlassAttrParser.cs` | `Seam` 常量、`DefaultSeam = 3`、范围 `[0, +∞)`、进 `NumericAttrs` |
| `Controls/Internal/ProceduralMaterialCache.cs` | `GlassParams` 加 `Seam`（Equals / Hash / ctor）。单块面板不读它，但它进 key 无害：`seam` 只会写在承载者上，承载者不出材质 |
| `Controls/Internal/ProceduralPanel.cs` | `_seam` + `SetSeam`，`RawGlassParams` / `FlushParams` 透传 |
| `Controls/Frame.cs` | `[UIAttr] Seam`（Frame-only，同 `Weld`） |
| `Controls/Internal/GlassGroupPanel.cs` | `ApplyGroupParams` 把 seam 塞进 `_GlassA.y`（现在的空位）；成员打包：`_WeldRadii` → `_WeldCornerW` / `_WeldCornerH` / `_WeldCornerKind` / `_WeldCornerFillet` 四个 `float4[8]`，`_WeldDepths.yz = (shape, hexW)`；`ResolveRadius` 删除——pill / hexagon 与短边钳制在 GPU 上解算，与单面板一致 |
| `Core/Lint/GlassRules.cs` | `GroupAttrs += seam`；`Check` 加 seam-no-weld；删 `WeldCornerCode` 与 `CheckWeldGroup` 里的角检查 |
| `Core/Lint/ProceduralAttrNames.cs`、`Editor/XsdGenerator.cs` | 加 `seam` |

### 4.2 Shader（`UI-GlassGroup.shader`）

- 成员 SDF：`PuguiResolveQuad(p − rect.xy, rect.zw, kindsⱼ, widthsⱼ, heightsⱼ, filletsⱼ, shapeⱼ, hexWⱼ)` → `PuguiSdPanel` / `PuguiPanelNormal`。cginc 末尾"只剩 weld 在用"的 `PuguiSdRoundBox` / `PuguiSdNormal` 删除。
- **两遍合成一遍**：角解算比圆角盒贵得多，不能再"重算一次 SDF 省 indexable temp"。旧权重改用在线 softmax（累加器随 `dmin` 下降按 `exp((dmin_new − dmin_old)/k)` 缩放，永不溢出），smin 折叠、高度图折叠、梯度折叠都在同一个循环里；每个成员的 SDF 与法线各求一次。
- `unitsPerPixel` 改从局部坐标的 `ddx/ddy(p)` 取（循环前、均匀控制流），不再依赖 `d`；`seam_eff = max(seam, 2·unitsPerPixel)`。
- 台阶项按 §2.2 加在外斜面项之后。

### 4.3 性能

每成员一次完整角解算（原来是两次圆角盒），融合组的片元成本约为 N 个单面板形状解算之和；uniform 从 5 个 `float4[8]` 变 8 个。融合组本来就少（HUD 级别一两个），可接受。零额外纹理采样。

## 5. 测试

Red first。EditMode：

- `NumericGuardTests` / `FrameGlassPanelTests`：`seam` 解析、默认 3、负数 / NaN 拒绝。
- `GlassWeldGroupTests`：`seam` 到 `_GlassA.y`；缺承载者面板时退回默认；成员 `cut 16` 的四个角数组按 CSS 顺序打包；`pill` / `hexagon` 作为 `_WeldDepths.y` 哨兵下发（**替换** `MemberPill_IsResolvedOnTheCpu` / `OversizedRadius_IsClampedToTheBlock` 两条——钳制移到 GPU）。
- `GlassRulesTests`：`seam` 在成员上 → placement；`seam` 无 `weld` → `PUI-GLASS-SEAM-NO-WELD`；`weld.mobile` + `seam` 不误报；删除五条 `WeldCornerCode` 用例。`PureContainerVisualAttrRulesTests` / `ProceduralAttrNamesTests` 名单含 `seam`。
- `XsdGeneratorTests`：`StringAssert.Contains("seam")`。
- `GlassRenderTests`（真渲染，`RenderAndSample` 加坐标参数）：
  1. 相接 8|3、光从左上：交界线上靠厚块一侧的像素比薄块内部亮；
  2. 同一布局等厚：交界像素与内部像素亮度差 < 1/255（台阶项恒零）；
  3. 成员 `radius="cut 40"`：距角 (12,12) 的像素**未绘制**（cut 切掉、round 会留下——两者在这一点上可分辨）。

## 6. 非目标

- 台阶背光侧的投影（只保留 0.35 补光）；
- 逐块不同的 `seam`；
- 玻璃看到玻璃（glass-fill §9 不变）；
- 单块 `<Frame glass>` 的内部台阶（没有第二块就没有台阶）。

## 7. 文档

- `reference/glass.md`：参数表加 `seam`（承载者栏）；weld 小节改写"厚度台阶"一段（叠放顺序、seam、折射/高光）、删"Corner treatments do not survive" 一条、lint 表删 `PUI-WELD-CORNER` 加 `PUI-GLASS-SEAM-NO-WELD`；
- `SKILL.md`：Frame 属性表加 `seam` 一行；角处理小节删 "One exception: `weld`" 一条；
- `2026-08-27-corner-treatments-design.md` §5 补一句"已由本 spec 取代"。
