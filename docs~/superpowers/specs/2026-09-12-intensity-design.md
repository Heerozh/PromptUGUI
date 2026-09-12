# `intensity` —— 程序化表面与 sprite 的「点亮」语义

> 状态：**已实现**（M0 + M1 一轮做完，见 §13）。决策见 §8，2026-09-12 与作者 brainstorm 后起草，作者以「跳过 plan 直接实现」确认了 §8 的全部默认值。
> 相关：`2026-08-23-procedural-style-design.md`（`glow` / `glowColor` 的出处，§2.1；参数进材质、形状进顶点 §12.1）、
> `2026-08-26-procedural-surface-design.md`（`ProceduralControl` 让九个控件共享同一套属性）、
> `2026-08-28-inner-glow-design.md`（内发光；本文的实现地图逐项镜像它）、
> `2026-09-02-image-fx-blur-glow-design.md`（`FxImage`：`<Image>` / `<Icon>` 的 sprite 级 `glow`）、
> `2026-08-23-glass-fill-design.md`（玻璃 —— 本文明确排除的表面）、
> `2026-06-02-state-color-absolute-modulate-design.md`（「multiply 只能变暗」—— 本文补的正是「变亮」这一半）。

## 1. 问题

设计稿上的「亮」图标（深蓝底上一个发光的 `+`）拆开看是三件事：

| 位置 | 观感 | 数值（设计稿取样） |
|---|---|---|
| 核心 | **比本色白得多**，近乎白但仍带蓝 | `(194, 228, 253)` |
| 光晕 | **保持饱和的蓝**，向外柔和衰减 | `(27, 53, 101)` → 底色 `(26, 47, 82)` |
| 两者之间 | 白 → 淡蓝 → 蓝 → 底色，**连续过渡** | — |

这不是一个颜色，是一条**随能量变化的色相曲线**：能量高处三个通道逐个饱和 → 发白；能量低处保持
本色的通道比例 → 色相不变。真实世界里它来自过曝（胶片 / 传感器 / 显示器的软裁），人眼把这条曲线
读成「光」，把任何单一色相的渐变读成「颜色」。

今天的程序化表面每一层只有「一个色相 × 一条 alpha 衰减」：fill 一色、`glowColor` 一色。作者在颜色
维度上怎么转都到不了：

- `color="#cfe6ff"`（模仿白核）+ `glowColor="#3b82f6"` → 白十字 + 一圈蓝**描边**，色相在形状边缘
  硬切，读成轮廓不是光；
- `color` 与 `glowColor` 同为蓝 → 蓝十字蓝边，没有亮核；
- 玻璃的 `lightIntensity` 是方向性边缘光，依赖 backdrop，不是这个东西。

缺的是**能量**这个维度。

### 1.1 为什么不是 HDR

brainstorm 里先评估了「颜色支持 HDR（>1）」的路线，结论是在这个库里走不通，记在这里免得再议：

- **默认 Overlay 画布上 HDR 永远只是裁切。** `Screen.cs` 默认 `ScreenSpaceOverlay`；overlay UI 直画
  backbuffer（UNorm，逐通道钳到 1），而 URP HDR Output 下 overlay UI 画进的离屏图是
  `R8G8B8A8_SRGB`（`DrawScreenSpaceUIPass.cs:46`），照样钳位；后处理 Bloom 跑在 overlay UI **之前**，
  看不到 UI。结果是逐通道硬裁 → **偏色**（`#3b82f6 × 3` 蓝变青），只有白没有亮。
- **真 HDR 要 `canvas="camera"` + 相机 HDR + Bloom，代价是整张 UI 被 tonemap**，所有 `#hex` 主题色
  和设计稿对不上 —— 工程级渲染决策，同一份 `.ui.xml` 在不同工程里长得不一样，违反「一份描述到处
  一致」。
- **顶点色路径天然 LDR。** 纯 `<Image>` / `<Text>` / 所有 `*Modulate` 走 `Graphic.color` →
  `UIVertex.color`（`Color32`）。

而「过曝」这条曲线本身**不需要 HDR**：它的输入输出都在 0..1，可以直接搬进 SDF shader，在 SDR、
Overlay 画布、任何平台上给出逐位相同的结果。这就是本文的方案。

## 2. 否决的方案

**alpha > 1 = 亮度（`color="accent/2"`）。否决。** 行业惯例是 RGB > 1、alpha 恒在 0..1（Unity `[HDR]`
Color 与它的取色器「基色 + Intensity」、Godot `modulate`、UE `FLinearColor`、CSS Color 4 的 `color()`）；
`/alpha` 的文档语义是「REPLACE 解析出的 alpha，0..1」，`ColorParser.TrySplitAlpha` 与
`PUI-COLOR-LITERAL-INVALID` 同步拦截；解析管线属性无关，放开就得对所有颜色属性放开，而落在
`Graphic.color` 上的那些会静默钳到 1；hex 写不出 >1，`<Color value=>` 定义侧又不收后缀，HDR 没法
烘进 token。运行时第一件事就是把 alpha 拆回 `(rgb·k, a)` —— 它只是线上编码的花招。

**additive / screen 合成。否决。** inner-glow spec §2 已否决过一次：非 HDR 的 UI 上过曝且不可控，且会是
库里唯一一处非 source-over 的合成。本文的曲线**保持 source-over**，「光」的观感来自颜色曲线而不是
混合模式。

**`1 − exp(−E·c)` 曝光曲线（brainstorm 第一版）。改用 `1 − (1 − x)^k`。** 两者是同一族（后者 =
把能量按 `E = −ln(1 − p)` 重新归一后再曝光），但前者在 `k = 1` 时**不是恒等**（0.5 → 0.39，中间调变暗），
要靠分支才能保住既有面板逐位不变，且 `k = 1 → 1.01` 有跳变，Variant / 动画没法连续过渡。后者
`k = 1` 精确恒等，无跳变。

**曲线作用在「能量 × 颜色」上（`1 − (1 − c)^E`，E 随衰减变化）。否决。** 通道为 1 的颜色（`#00f`
的 B）在任何 E > 0 处都直接饱和 → 光晕尾巴整片纯蓝。曲线必须作用在**预乘色**（`rgb·a`）上，尾巴处
`p → 0`，行为正确。

**`glowIntensity` / `innerGlowIntensity`（只点亮光晕）。否决。** 设计稿的核心（fill）也是白的 ——
「亮」是整个表面的属性，一个旋钮同时驱动 fill + glow + innerGlow + border 才是那条连续曲线；拆开就
回到「白核 + 蓝描边」的硬边。

**逐状态 `hoverIntensity` 等。不进 v1。** 状态反应器只驱动 `Graphic.color`，没有任何程序化参数有逐状态
版本；与 `glow` / `innerGlow` 同待遇（`<Show on="state-hover">` 叠一层）。是自然的扩展位（§9）——
states spec 记录的「multiply 只能变暗」缺口，`intensity` 正是那个「变亮」算子。

**光晕尾巴换高斯。不进 v1。** 高斯尾在「光」的观感上更像（brainstorm 的对比图 ⑧），但它是独立于
`intensity` 的决定：今天的 `g²` 在 `intensity` 下同样成立，且 `g²` / 高斯之争影响所有既有光晕，
另立项。

## 3. 方案总览

一个新属性 **`intensity`**（Unity HDR 取色器的词汇，LLM 认识），语义：**这个表面发出的光有多亮**。
`1` = 今天（逐位不变）；`2` ≈ 亮一档；`3`–`5` = 霓虹；`≥ 8` 基本全白。

```xml
<Frame color="#4f88ff" radius="2" glow="14" glowColor="#4f88ff/0.35" intensity="3"/>   <!-- 设计稿的 + -->
<Icon name="ui:plus" color="#4f88ff" glow="14" glowColor="self/0.35" intensity="3"/>    <!-- 图标版，同一条曲线 -->
<Btn radius="hexagon 70" color="#efdca6,#c08f36" innerGlow="30" innerGlowColor="#fff6cf"
     borderWidth="2" glow="36" intensity="2" textColor="#4a3208">开始匹配</Btn>          <!-- 金色牌点亮 -->
<Decor kind="line" at="bottom" thickness="1" color="accent" glow="8" intensity="4"/>    <!-- 霓虹分割线 -->
<Style name="neon" glow="12" intensity="4"/>                                            <!-- 样式 / 主题 / Variant 照常 -->
```

- **作用面**：`<Frame>` 与 `ProceduralControl` 全家（Btn / Tab / TabMenu / Toggle / Slider / Dropdown /
  InputField / ScrollList / Collapsible / Progress）的**不透明**程序化表面、`<Decor>`、`<Image>` /
  `<Icon>`（`FxImage`）。
- **玻璃面不作用**（§5.4）：玻璃画的是 backdrop，不是表面发出的光。
- **纯材质参数**：不外扩 quad、不动布局、不动顶点；Variant / 主题切换只换材质。
- **已有旋钮分工不变**：`glow` 仍是半径，`glowColor` 的 `/alpha` 仍是「光晕有多少」；`intensity` 是
  「有多烫」。两者正交（brainstorm 对比图 ⑦）。

## 4. 语法

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `intensity` | 数，`≥ 1`，有限 | `1` | 曝光倍数。`""` = 退回 `1`（Variant 只能改值不能删属性，同 `frost` 等） |

无上限：`k` 越大越白，`pow(1 − p, k)` 对任意大的 `k` 数值稳定；文档写「有用区间 1–8」。`< 1` 是欠曝，
与 `*Modulate` 的变暗重叠，v1 拒掉（§9 记为扩展位）。

### 4.1 解析错误（parse-time，纯 C# 子集，CLI 同步可见）

解析器 `Core/Parser/IntensityAttrParser.cs`（纯 C#，镜像 `GlassAttrParser.TryParseValue` 的空值 /
NaN / 范围三段），运行时 setter 与 `StyleRules.CheckValue` 共用，**不新增错误码**：

| 写法 | 结果 |
|---|---|
| `intensity="abc"` | `intensity="abc": expected a number (e.g. "3")` |
| `intensity="0.5"` / `"0"` / `"-1"` | `intensity="0.5": must not be less than 1` |
| `intensity="NaN"` / `"Infinity"` | `must be a finite number` |
| `<Style intensity="abc">` / `<Frame intensity="0.5">` | Lint `PUI-PROCEDURAL-VALUE`（同 `frost` 那一支） |

**不进 `GlassAttrParser.NumericAttrs`**：`GlassRules` 用那张表报 `PUI-GLASS-PARAM-NO-GLASS`，
`intensity` 恰恰是不玻璃的属性。

## 5. 语义

### 5.1 曲线

设表面合成完的直 alpha 颜色为 `(rgb, a)`，`k` = `intensity`：

```
p   = rgb · a                        // 直 alpha → 预乘
p'  = 1 − (1 − p)^k                  // 逐通道曝光
a'  = 1 − (1 − a)^k
E   = k · max(p)                     // 曝光前的最亮通道能量
h   = saturate((E − 1) / 3)          // 越过 1 的程度；E = 4 时饱和
p'  = lerp(p', max(p'), 0.5 · h)     // 过曝串扰：向自身最亮通道靠拢（去饱和）
rgb' = p' / a'                       // 回直 alpha
```

性质：

- **`k = 1` 精确恒等**，既有面板逐位不变（shader 里 `k <= 1` 直接 return，uniform 分支零开销）。
- **暗端线性**：`p → 0` 处 `p' ≈ k·p`、`a' ≈ k·a`，直色 `≈ rgb` —— 光晕尾巴**保色相**、只是更亮更宽。
  `#4f88ff` 光晕 `a = 0.2` 处，`k = 5` 后直色 `(94, 156, 255)` vs 本色 `(79, 136, 255)`，`a → 0.67`。
- **亮端趋白**：`p → 1` 处所有通道饱和到 1。`#4f88ff` 核心：`k=2 (147,192,255)`、`k=3 (184,220,255)`、
  `k=5 (213,241,255)`；设计稿 `(194,228,253)` 落在 `k ≈ 3.5`。
- **`1 − (1 − x)^k` = k 份同样的光 screen 叠加**（多次曝光），这是它对作者的直觉解释；数学上等价于
  `1 − exp(−k·E)`，`E = −ln(1 − p)`。
- **过曝串扰补的是逐通道曲线的天然缺陷**：`#f00` / `#00f` 这类有零通道的纯色，无论 `k` 多大零通道
  永远是 0，「永远不发白」（brainstorm 对比图 ③ 的最右两格，纯蓝还很暗）。串扰让能量越过 1 的像素向
  自身最亮通道靠拢：`#f00` → `k=5 (255,188,188)`，粉白核 + 红晕，霓虹的观感；纯色最多白到一半
  （0.5 上限）。阈值 3 与系数 0.5 是按设计稿标定的常数，**不暴露属性**（§8）；尾巴处 `E < 1`，`h = 0`，
  不动。
- **alpha 也曝光**：光晕 `k` 倍亮就 `k` 倍宽，这是光的物理；`glowColor` 的 `/alpha` 是对冲它的旋钮。
  AA 边缘的半透明像素同样被推实，边缘约硬半个像素 —— bloom 也这样。
- **输出恒在 0..1**：`p ≤ a` 逐通道成立（`rgb ≤ 1`），单调曲线保序 → `p' ≤ a'` → `rgb' ≤ 1`；串扰向
  `max(p') ≤ a'` 靠拢也不越界。`fixed4` 精度、`Blend SrcAlpha OneMinusSrcAlpha` 都不用动 —— 与 HDR
  路线（§1.1）的本质区别。

### 5.2 作用位置：合成结果一次，顶点色之前

```
不透明面：  填充 → 内发光 over → 外发光 under → 描边 over → [曝光] → × 顶点色 → 裁剪
<Decor>：   填充 → 外发光 under → [曝光] → × 顶点色 → 裁剪
```

曝光作用在**这个表面画出来的全部东西**上（fill + innerGlow + glow + border 的合成），恰好一次，四个
`pow`。描边跟着变白（`white/0.4` 的细描边在 `k=5` 下近乎纯白）是想要的：它也是表面的一部分。

放在 `col *= IN.color` **之前**。顶点色 = `Graphic.color`（`*Modulate` 状态调制 / `hoverModulate`
的 0.1s 渐变）× CanvasRenderer / CanvasGroup alpha（淡入淡出）。曝光在前意味着：

- **淡出就是淡出**：一个点亮的面板 CanvasGroup 淡出时整体变透明，不会「冷却」回本色。
- **`*Modulate` 压暗的是点亮后的表面**：`pressedModulate="#cccccc"` 让白热的按钮整体暗 20%，可预测。

### 5.3 `FxImage`（`<Image>` / `<Icon>`）：顶点 rgb 在曝光之前，顶点 alpha 在之后

sprite 的颜色 = 贴图 × 顶点 rgb —— `<Icon color="#4f88ff">` 就是这么给白图标上色的，所以曝光必须在
顶点 rgb **之后**，否则曝光的是白图标（白还是白）。但淡出（顶点 alpha）仍应在曝光之后（§5.2 的理由）。
于是 `UI-ImageFx.shader` 里把 `IN.color` 拆开：

```
image = 本体采样（blur 可选）；image.rgb ×= IN.color.rgb（或 PuguiLinearLight，alpha 传 1）
glow  = 自体色 / 指定色（同今天，alpha 不再乘 IN.color.a）
color = PuguiOver(image, glow) → [曝光] → color.a ×= IN.color.a → 去色 → 裁剪
```

**接受的一个不对称**：`FxImage` 上 `*Modulate`（hover 压暗）改的是顶点 rgb = 图标的颜色 = 曝光的输入，
所以 hover 时一个点亮的图标会**稍微冷却**（能量降、色相回来一点）而不是均匀变暗。物理上合理
（光源功率下降），且 `*Modulate` 在 `FxImage` 上本来就是「改颜色」而非「改亮度」。文档写一句。

`intensity` **不需要几何**：曝光不外扩 quad，所以对 `type="sliced"` / `tiled` / `filled` 一样生效
（`!hasRect` 分支画完照常曝光）—— 与 `blur` / `glow` 只在 simple / contain / cover 上生效不同，
`PUI-FX-TYPE` 不覆盖 `intensity`。`HasMaterialFx` 加一项 `_intensity > 1`：一个只写 `intensity` 的
图标也要 fx 材质。

`glowColor="self"` 的自体光晕跟随本体一起曝光（它取的是 tint 后的本体色），设计稿的 `+` 就是这个形态。

### 5.4 各表面

| 表面 | 行为 |
|---|---|
| 不透明程序化面板（`<Frame>` + `ProceduralControl` 全家） | §5.2 |
| `<Decor>` | 同一个 helper，一行调用；`DecorRules.SupportedProceduralAttrs` + `intensity`（不加就会被 `PUI-CONTAINER-VISUAL-ATTR` 报成不支持） |
| `<Image>` / `<Icon>` | §5.3；`ImageFxRules.SupportedProceduralAttrs` + `intensity` |
| **玻璃面**（`glass="true"`） | **不作用**。玻璃体是 backdrop 的模糊采样 + 薄 tint，把它曝光等于把背景提亮，不是「表面发光」。`BuildParams` 在 `_glass` 时把 `Intensity` 归 1 进 key（与玻璃参数在不透明面归零同一理由：不让渲染相同的面板分裂材质）。Lint 新码 **`PUI-GLASS-INTENSITY`**（`GlassRules`）：`glass="true"` / weld 容器与成员上写 `intensity` → *"intensity has no effect on a glass surface — glass paints the backdrop, which is not light the surface emits; drop glass or drop intensity"*。走 style-aware 两遍，`class=` 带进来的也报 |
| weld 组 | 玻璃 only → 同上；`GlassGroupPanel` 不发布 `_Intensity`，组 shader 不加 |
| 内层 `<layer>Radius`（`fillRadius` 等） | 无 `fillIntensity`：procedural-surface spec §6 内层 shape-only |

### 5.5 disabled：光灭了

`_grayed`（面板）/ `Desaturate`（`FxImage`）时 **`k` 强制为 1**。灰度是「去色」，曝光是「加亮」，
两者叠加得到的是一个白热的灰面板 —— 读起来是「亮着的」，而禁用态必须读起来「不通电」（同玻璃在禁用
态把 `depth` × 0.35 的取舍）。`BuildParams` 与 `FxImage.BuildParams` 各一行；`intensity` 本身不变，
`interactable` 回来时光就回来。

### 5.6 可见性

`ComputeVisible` **不改**：`intensity` 单独存在时表面什么都没画，没有东西可点亮。

### 5.7 不到达的地方

| 地方 | 行为 | 机制 |
|---|---|---|
| `<VStack>` / `<HStack>` / `<Grid>` / `<SafeArea>` | `PUI-CONTAINER-VISUAL-ATTR`，指路「套一层 Frame」 | `ProceduralAttrNames.NeedsPanel`，自动 |
| `<Btn sprite="…">` 等 sprite 皮肤 | 与 `glow` 相同：写了 `intensity` 就进程序化模式、bg 让位 | `PanelAttaching`，自动；要点亮 sprite 用 `<Icon>` / `<Image>` |
| `<Text>` | 不支持 | TMP 自己的材质；另立项 |
| `<RawImage>` | 不支持 | `FxImage` M2 未做，`ImageFxRules.FxTags` 不含它 |
| `<Animation>` | 不可动画 | 没有任何程序化参数可动画，不新开口（§9） |
| SHAPE 主题规则 / `VariantBaseRules` | 进豁免集 / 与 `glow` 同待遇 | 从 `NeedsPanel` 派生，自动 |

### 5.8 材质共享与性能

`PanelParams` / `DecorParams` / `FxParams` 各多一个 `float Intensity`，参与 `Equals` / `GetHashCode`；
同 style 的面板照旧共用一个材质。fragment 多一个 uniform 分支 + 四个 `pow` + 几条算术，`k <= 1` 时整段
跳过。**不动几何**：`MarkDirty` 的顶点脏检查只看 `_glowSize` / `Pad`，`intensity` 不加进去。

### 5.9 色彩空间

曲线作用在 shader 拿到的数值上。Linear 工程（宿主工程 `m_ActiveColorSpace: 1`）里 `Color` 属性上传时
已做 sRGB → linear，曝光发生在**线性光**中 —— 这正是过曝的物理定义，`k = 2` 严格是「两份光」。Gamma
工程里 shader 拿到 sRGB 值，同一个 `k` 发白得慢（`k = 3` 观感接近 Linear 的 `k = 2`）。文档写明
「以 Linear 工程为准」，不做补偿（库的其余光效也都没有）。

`_Intensity` 是 `Float` 属性，不受颜色空间转换影响 —— 这也是它不能编码进颜色值的又一个理由（§2）。

### 5.10 浅色背景

source-over 下「光」在亮底上退化：中灰底上是一层淡色薄雾，近白底上形状自身反而洗淡、光晕不可见
（brainstorm 对比图 ⑥）。物理上正确（白底上没有「光」），是深底效果；文档写一句，不做特殊处理。

### 5.11 文字对比

点亮的表面会变浅：蓝色 pill `k ≥ 3` 时白色 label 失去对比（对比图 ⑤）。`textColor` 是子节点、不受
`intensity` 影响，作者要自己配深色文字。文档写一句。

## 6. 实现地图

### 6.1 数据流（改动点自上而下）

| 文件 | 改动 |
|---|---|
| `Core/Parser/IntensityAttrParser.cs`（新，纯 C#） | `Name = "intensity"`、`Default = 1f`、`TryParse` / `Parse`：空 → 1；非数 / NaN / `< 1` 三段错误（§4.1） |
| `Runtime/Resources/PromptUGUI/Material/UI-PanelSDF.cginc` | 新增 `PuguiExpose(col, k)`：§5.1 的八行，所有 shader 共用 |
| `UI-ProceduralPanel.shader` / `UI-Decor.shader` | `_Intensity` 属性 + 声明；描边 over 之后、`col *= IN.color` 之前调一次。`UI-GlassPanel.shader` / `UI-GlassGroup.shader` **不改** |
| `UI-ImageFx.shader` | `_Intensity`；`IN.color` 拆 rgb / a（§5.3）；`PuguiOver(image, glow)` 之后调一次 |
| `Controls/Internal/ProceduralMaterialCache.cs` | `PanelParams` + `Intensity`（ctor、`Equals`、`GetHashCode`）；`Configure` 一个 `SetFloat`；`PropertyToID` |
| `Controls/Internal/ProceduralPanel.cs` | 字段 `_intensity = 1f`；`SetIntensity`；`BuildParams`：`_glass` 或 `_grayed` → 1（§5.4 / §5.5） |
| `Controls/Internal/DecorPanel.cs` / `DecorMaterialCache.cs` | `DecorParams` + `Intensity`（ctor、`Equals`、`GetHashCode`）；`DecorPanel.SetIntensity`；`Configure` 一个 `SetFloat` |
| `Controls/Internal/FxImage.cs` / `FxMaterialCache.cs` | `_intensity`；`Intensity` setter；`FxParams` + `Intensity`（`Desaturate` 时归 1）；`HasMaterialFx` 加 `_intensity > 1`；`Configure` 一个 `SetFloat` |
| `Controls/Internal/ImageFxApplier.cs` | `SetIntensity(graphic, tag, value)`，走 `IntensityAttrParser.Parse` |
| `Controls/Frame.cs` | `[UIAttr] Intensity`，直连 `Panel.SetIntensity` |
| `Controls/ProceduralControl.cs` | 同名 `[UIAttr]`，走 `Surface.Declare` —— 全家（Btn … Progress）自动获得 |
| `Controls/Decor.cs` / `Image.cs` / `Icon.cs` | 各一个 `[UIAttr] Intensity` |
| `Core/Lint/ProceduralAttrNames.cs` | `PanelAttaching` / `All` / `NeedsPanel` 各 +1（`ProceduralAttrNamesTests` 守镜像） |
| `Core/Lint/StyleRules.cs` | `CheckValue` 加 `IntensityAttrParser` 一支 → `PUI-PROCEDURAL-VALUE` |
| `Core/Lint/GlassRules.cs` | 新码 `PUI-GLASS-INTENSITY`（§5.4），raw + style-aware |
| `Core/Lint/DecorRules.cs` / `ImageFxRules.cs` | `SupportedProceduralAttrs` 各 + `intensity` |
| `Editor/XsdGenerator.cs` | Frame / Image / Icon 手写清单各 +1（Decor 走反射，见 §13.4） |

`PanelParams` / `FxParams` / `DecorParams` 的 ctor 签名都变了，实现时 grep 全部构造点（含测试）。
类注释里的属性计数（`ProceduralPanel`「sixteen attributes」、`ProceduralControl`「fifteen」）跟着 +1
—— inner-glow spec §12.3 记过这个坑。

### 6.2 shader 段落

```hlsl
// UI-PanelSDF.cginc
// 曝光：把 k 份同样的光 screen 叠在一起。k=1 逐位恒等；暗端 ≈ k·x（光晕保色相）；亮端趋白。
// 作用在预乘色上 —— 作用在直色上时，通道为 1 的颜色在任何能量下都会直接饱和，光晕尾巴整片纯色。
// 过曝串扰：能量 k·max(p) 越过 1 的像素向自身最亮通道靠拢（去饱和），补「零通道永远不发白」；
// 阈值 3 / 系数 0.5 按设计稿标定（spec 2026-09-12 §5.1），不是参数。
float4 PuguiExpose(float4 col, float k)
{
    if (k <= 1.0) return col;
    float3 p  = col.rgb * col.a;
    float3 fp = 1.0 - pow(1.0 - p, k);
    float  fa = 1.0 - pow(1.0 - col.a, k);
    float  energy = k * max(p.r, max(p.g, p.b));
    float  h = saturate((energy - 1.0) / 3.0);
    float  target = max(fp.r, max(fp.g, fp.b));
    fp = lerp(fp, target.xxx, 0.5 * h);
    return float4(fp / max(fa, 1e-5), fa);
}
```

调用点：不透明面在描边 `PuguiOver` 之后、`col *= IN.color` 之前；`<Decor>` 同位置；`FxImage` 见 §5.3。

### 6.3 CLI

`Core/Parser` 加一个纯 C# 文件、`Core/Lint` 四处名字表 / 分支，纯 C# 子集不变。UIXmlLint 两遍（raw +
expanded）自动覆盖 `<Style intensity=>` 经 `class=` 落到玻璃控件上的情形。

## 7. SKILL 更新（同 PR，英文）

`authoring-promptugui-xml/SKILL.md`：

- 原语目录 `<Frame>` 行：`innerGlow` 后追加 `intensity`。
- `<Frame>` 属性表 +1 行（§4 的表）+ 一段「Lighting it up」：`intensity` 是什么、`1` = today、有用区间、
  `glowColor` 的 `/alpha` 与它正交、深底效果、`textColor` 要配深色；示例块加设计稿的 `+` 与点亮的
  金色牌。
- 「Which tags draw procedurally」引用块与 Procedural surfaces 一节的属性行：加 `intensity`，并写明
  **not on glass**（`PUI-GLASS-INTENSITY`）。
- `<Image>` / `<Icon>` 属性表 +1 行；「Blur & glow」一节加一段：同一条曲线、对任何 `type` 生效、
  `self` 光晕跟随本体、hover 会略微冷却。
- Color Tokens「Alpha suffix」加一句：alpha is opacity — brightness is `intensity`, never `/2`（把 LLM 从
  alpha > 1 的尝试上引开）。
- Quick reference 加一行 `INTENSITY intensity="3"  Frame-family / Decor / Image / Icon; not glass`；
  lint 码表加 `PUI-GLASS-INTENSITY`。

`reference/glass.md`：参数归属表下加一句「`intensity` has no effect on glass」。
`reference/decor.md`：属性表 +1。
`reference/states.md`：`*Modulate` 段补一句「darkens the lit result; no per-state intensity — layer a
lit twin under `<Show on="state-hover">`」。
C# skill 不改（无公共 API 变化）。

## 8. 已定的决策（2026-09-12 brainstorm，作者确认）

1. **一个属性 `intensity`，作用于整个表面**，不拆 `glowIntensity`（§2）。备选名 `emission` /
   `brightness` / `exposure` 都没有「Unity HDR 取色器同款词」这个优势。
2. **曲线 `1 − (1 − x)^k`，作用在预乘色上**，`k = 1` 精确恒等（§5.1）。
3. **过曝串扰内置 0.5、阈值 3，不暴露属性**。没有它纯色不工作，有它的观感就是设计稿；要「冷光 /
   不去饱和」再开旋钮（§9）。
4. **范围 `≥ 1`，默认 1，无上限**（§4）。
5. **曝光在顶点色之前**；`FxImage` 例外地把顶点 rgb 放进曝光、alpha 留在后面（§5.2 / §5.3）。
6. **玻璃不作用 + `PUI-GLASS-INTENSITY`**，不做「只点亮玻璃上的光晕」（§5.4，§9 记扩展位）。
7. **disabled 时 `k` 归 1**（§5.5）。
8. **`<Decor>` 进 v1**（一行调用 + 名字表；霓虹分割线 / 角括号是自然用例）。
9. **不进 v1**：逐状态 `hoverIntensity`、`<Animation intensity>`、`k < 1` 欠曝、高斯尾、`<Text>` /
   `<RawImage>`、玻璃上的光晕曝光。

## 9. 开放问题 / 扩展位

- **`<Decor>` 是否留到 M2**：作者若想缩 PR，去掉它只需不加 `DecorRules.SupportedProceduralAttrs`
  （lint 会自动报不支持），其余不变。
- **`FxImage` 的 hover 冷却**（§5.3）：若作者要「均匀变暗」，替代方案是把 `*Modulate` 也拆成
  「tint 进曝光 / modulate 不进」—— 但顶点色里两者不可分（同一个 `Graphic.color`），得走材质参数，
  超出本特性。先接受不对称。
- 扩展位（不做，留名）：`hoverIntensity` / `selectedIntensity`（states spec 的「变亮」缺口）；
  `<Animation intensity="1:4">`（脉冲发光 —— 第一个值得动画的程序化参数，需要材质参数动画通道）；
  `intensity < 1` 欠曝；串扰旋钮；玻璃面只曝光 glow / innerGlow / border 三层（逐层曝光，`pow` × 3）；
  高斯尾（`glow="14 soft"`）。

## 10. 里程碑拆分

| | 内容 | 依赖 |
|---|---|---|
| **M0 Red** | §11 全部测试先红：解析 / 默认 / 恒等 / 不外扩 / 材质共享 / 玻璃忽略 / disabled 归 1 / `FxImage` 材质门 / lint 五处 / XSD / render 断言 | 无 |
| **M1 实现** | cginc helper + 三 shader + 三个 Params + 三个 Panel/Image + 五处 `[UIAttr]` + 解析器 + lint + XSD + SKILL（§7） | M0 |

一个 PR（分支 `feat/intensity`）。改动面全是「顺着 `innerGlow` 的管线各加一份」，拆开没有收益。
验证：EditMode / EditorOnly / PlayMode 全绿 + `dotnet format --verify-no-changes` + UIXmlLint 跑
`Runtime/Resources/` 无新 issue。

## 11. 测试（Red 先行）

**`IntensityAttrParserTests`**（纯 C#，`Tests/EditMode/Parser/`）：空 → 1；`"3"` → 3；`"1"` → 1；
`"abc"` / `"0.5"` / `"0"` / `"NaN"` / `"Infinity"` 各自的错误文案（§4.1 逐字）。

**`FrameProceduralPanelTests`**（材质参数观测走 `CurrentParams`，与 `InnerGlow*` 测试同型）

- `Intensity_DefaultsToOne`；`Intensity_ParsesNumber`（`intensity='3'` → `Intensity == 3`）；
  `Intensity_Empty_ResetsToOne`；`Intensity_BelowOne_Rejected`；`Intensity_NaN_Rejected`。
- `Intensity_AloneIsNotVisible`：无 fill 无 border 无 glow，`intensity='3'` → `!IsPanelVisible`。
- `Intensity_DoesNotInflateMesh`；`Intensity_ChangeDoesNotDirtyVertices`。
- `Glass_IgnoresIntensity`：`glass='true' intensity='3'` → `CurrentParams.Intensity == 1`。
- `Disabled_ResetsIntensityToOne`；再启用后回到 3。
- `SameIntensity_SharesOneMaterial` / `DifferentIntensity_SplitsMaterial`
  （`ProceduralMaterialCache.LiveMaterialCount`）。

**`ProceduralSurfaceContractTests`**：`AnyPanelAttachingAttr_AttachesASurface` 用例表 + `intensity='3'`。

**`DecorTests`**（或既有 Decor 参数测试类）：`intensity='4'` 进 `DecorParams`；`''` 归 1。

**`FxImageTests` / `FxMaterialCacheTests`**

- `Intensity_AloneNeedsTheFxMaterial`：无 blur / glow，`intensity='3'` → `HasMaterialFx`，key 带 3。
- `Intensity_AppliesToSlicedType`：`type='sliced' intensity='3'` → key `Blur == Glow == 0` 但
  `Intensity == 3`（不需要几何）。
- `Intensity_Empty_ResetsToOne`；`Disabled_ResetsIntensityToOne`（`DisabledGrayscaleTests` 同型）。
- `FxParams` 的 `Equals` / `GetHashCode` 含 `Intensity`。

**Lint**

- `ProceduralAttrNamesTests`：镜像测试自动覆盖（名字必须是 `<Frame>` 的真实属性）。
- `GlassRulesTests.BadValues_AreFlagged` 用例表 + `intensity='abc'` / `'0.5'`；
  `BadValuesInAStyle_AreFlaggedWhereTheyAreWritten` + `<Style intensity='0'>`。
- `GlassRulesTests`（新组）：`glass='true' intensity='3'` → `PUI-GLASS-INTENSITY`；weld 容器与成员各一例；
  `GlassRulesStyleAwareTests`：经 `class=` 带进来的也报；不玻璃的 `<Frame intensity='3'>` 不报。
- `PureContainerVisualAttrRulesTests`：`<VStack intensity=>` 报；`<Decor intensity=>` **不**报。
- `ImageFxRulesTests`：`<Image type='sliced' intensity='3'>` **不**报 `PUI-FX-TYPE`；`<Icon intensity='3'>`
  无 `glow` 不报 `PUI-FX-ATTR`。
- `ThemeStyleRulesTests`：一个主题写整套（含 `intensity`）、另一主题不写 → 豁免、不报。

**XSD**（`XsdGeneratorTests`，substring）：Frame / Image / Icon / Decor 含 `intensity`。

**Render**（`IntensityRenderTests`，`InnerGlowRenderTests` 的 `Camera.Render()` + `ReadPixels` 套路，
底色 `#101010`，探针取 luma / 通道）

- `IntensityOne_IsPixelIdenticalToUnset`：`intensity='1'` 与不写 → 整张 dump 逐像素相等（守 `k <= 1`
  分支）。
- `Intensity_BrightensAndWhitensTheFill`：`color='#3b82f6' intensity='5'` 中心 luma 高于 `k=1`，且 R
  通道显著上升（`> 0.5`；`k=1` 时 `≈ 0.23`）—— 发白而非仅变亮。
- `Intensity_KeepsTheHaloHue`：`glow='16' glowColor='#3b82f6/0.5' intensity='5'`，光晕远端（`d ≈ 12`）
  探针 `b > g > r` 保序且 `r / b < 0.5`；同一探针比 `k=1` 亮。
- `Intensity_DoesNotReachPastTheGlow`：光晕半径之外与 `k=1` 逐像素相等（不外扩）。
- `PureBlue_WhitensThroughCrosstalk`：`color='#0000ff' intensity='5'` 中心 `r ≈ g > 0.6`（无串扰时为 0）。
- `Glass_IgnoresIntensity`（`GlassRenderTests`）：`glass='true' intensity='5'` 与不写逐像素相等。
- `ImageFxRenderTests`：`<Icon color='#4f88ff' intensity='5'>` 中心发白；`type='sliced'` 同样发白；
  `intensity='1'` 逐像素等于不写。
- 既有 round-only 基线（`CornerTreatmentRenderTests`）与 `ImageFxRenderTests` 既有用例在 `intensity`
  缺省时逐像素不变。

## 12. 附录：brainstorm 的参考模型

方案是先在 Unity 外用 numpy 复现 shader 合成顺序、对着设计稿调出来的（对比图：`intensity` 扫描 /
各色相 / 真实控件 / 浅色背景 / 旋钮正交 / 尾巴形状）。核心八行与 §5.1 逐字一致，记在这里供实现时
对数：

```python
p  = rgb * a                                   # 直 alpha → 预乘（线性光）
fp = 1 - (1 - p) ** k
fa = 1 - (1 - a) ** k
h  = clip((k * p.max() - 1) / 3, 0, 1)         # 过曝程度
fp = fp + (fp.max() - fp) * (0.5 * h)          # 串扰
rgb2, a2 = fp / fa, fa
```

核心色（线性光曝光后转回 sRGB）：

| 本色 | k=1 | k=2 | k=3 | k=5 | k=8 |
|---|---|---|---|---|---|
| `#4f88ff` | (79,136,255) | (147,192,255) | (184,220,255) | (213,241,255) | (223,249,255) |
| `#3b82f6` | (59,130,246) | (127,184,254) | (166,214,255) | (203,238,255) | (211,247,255) |
| `#ff0000` | (255,0,0) | (255,113,113) | (255,156,156) | (255,188,188) | (255,188,188) |
| `#efdca6` | (239,220,166) | (253,247,212) | (255,253,234) | (255,255,250) | (255,255,254) |

设计稿核心 `(194,228,253)` ≈ `#4f88ff` 的 `k ≈ 3.5`；纯色在串扰上限处封顶（`#ff0000` 的 k=5 与 k=8
相同），是有意的。

## 13. 实施记录

**验证结果**：EditMode 3673 / EditorOnly 343 / PlayMode 201 全通过；
`dotnet format --verify-no-changes --severity warn` exit 0；
`UIXmlLint Runtime/Resources/` no issues across 8 files；一份手写的坏文档经 CLI 报出
`PUI-PROCEDURAL-VALUE`（`intensity="0.5"`）、`PUI-GLASS-INTENSITY`（直接写与经 `class=` 合并各一）、
`PUI-CONTAINER-VISUAL-ATTR`（`<VStack intensity>`），`<Decor>` / `<Icon>` 干净。

分支 `feat/intensity`，按 spec / 解析器 / 面板 / Decor / FxImage / lint+XSD / SKILL 七步提交，每步
Red 先行、每个提交可编译可测。

### 13.1 引擎里的数值与 brainstorm 的 numpy 模型逐位一致

`IntensityRenderTests` 的 dump 读回：`#3b82f6` 填充 `k=5` 核心 `(203, 238, 255)`、`#0000ff` 核心
`(188, 188, 255)` —— 与 §12 附表完全相同。说明宿主（Linear 工程）里 `Color` 属性上传转线性、shader
在线性光中曝光、RT 读回 sRGB 这条链，正是模型假设的那条链；§5.9 关于 Gamma 工程的保留意见仍然成立。

### 13.2 §5.3 的 `FxImage` 顶点 alpha 拆分要靠一个 uniform 分支才能保住「k=1 逐位不变」

spec 只写了「顶点 alpha 移到曝光之后」。实现时发现：今天的路径是**逐层**乘淡出（本体 alpha 与光晕
alpha 各乘 `IN.color.a`，再 over），而「合成后整体乘」在 `a < 1` 时与它不相等（over 的交叉项
`da·sa·f` vs `da·sa·f²`）。半透明的 `<Icon color="white/0.6" glow=…>` 会在 k=1 处漂移。
于是 shader 里加了 `bool lit = _Intensity > 1.0`：未点亮时 `layerFade = IN.color.a` 走今天的
逐层路径（`ImageFxRenderTests.IntensityOne_IsPixelIdenticalToUnset` 守着），点亮时 `layerFade = 1`、
曝光后整体乘 alpha。顺带的观察：整体乘才是 CanvasGroup 淡出的正确语义（逐层乘会让光晕在本体
下面多透出来一点），但那是既有行为，不在本特性里改。

面板没有这个问题：四种颜色都在材质里，顶点色本来就是最后一乘。

### 13.3 曲线的两个数值护栏

`PuguiExpose` 里 `pow(max(1 - p, 0), k)`：`p` 理论上 ≤ 1，但 `half` 精度的预乘结果可能比 1 大一个
ulp，负底数的 `pow` 在部分 GPU 上是 NaN。C# 侧 `FxParams` / `ProceduralPanel.SetIntensity` /
`DecorPanel.SetIntensity` 各自钳到 `≥ 1`（解析器已拒绝，钳位是给直接调 setter 的 C# 调用者）。

### 13.4 XSD 测试的计数

`XsdGeneratorTests` 用的是空 `ControlRegistry`，反射不到 `<Decor>` 与 `ProceduralControl` 全家，
输出里 `intensity` 恰好三处（Frame / Image / Icon 三份手写清单），测试断言 `== 3`。§6.1 表里
「Decor 手写清单 +1」是误记：Decor 走反射，无需改 XsdGenerator。

### 13.5 `<Decor kind="sprite">` 上的 `intensity`

`DecorRules` 的 sprite 分支原本对 `SupportedProceduralAttrs` 一律报「the glow is cast from a
distance field…」；`intensity` 进那张表后文案不再成立，给了它单独一句（picture 由普通 Image 画，
没有曝光曲线，指路 `<Icon>` / `<Image>`）。`<Decor>` 没有禁用灰度路径（`DecorPanel` 不是
`ISelfGrayscale`，既有），所以 §5.5 的「禁用归 1」只落在面板与 `FxImage`。

### 13.6 视觉验收

设计稿的 `+`（`color="#4f88ff" glow="14" glowColor="#4f88ff/0.35" intensity="3"`）在引擎里渲染出来
就是白蓝核心 + 蓝色光晕；`<Icon>` 版与 `<Frame>` 版同一观感；`type="sliced"` 的红色贴图在 `k=5`
下是粉白（串扰封顶），`<Decor kind="line">` 变成霓虹线。
