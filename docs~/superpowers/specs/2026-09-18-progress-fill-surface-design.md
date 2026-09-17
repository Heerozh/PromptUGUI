# `<Progress>` 的 fill 成为主表面 —— 全套程序化原语 + SDF 进度裁切（光晕溢出轨道）

> 状态：**已实现**（分支 `feat/progress-fill-surface`，M0–M3 分步提交；实施记录见 §14。决策见 §12，2026-09-18 与作者对齐）。
> 相关：
> `2026-05-27-progress-control-design.md`（四层结构 `MaskWrapper/Bg` + `Fill` + `Frame` 与 PB-D* 决策的出处；本文改主表面归属、保留层结构）、
> `2026-08-26-procedural-surface-design.md`（`ProceduralControl` / `ProceduralSurface`；§6「内层只给形状」与 §13.3「Progress 主表面 = Bg」**在此推翻**；§9 的「panel 当遮罩源」被本文用作 sprite-fill 路径的保留形态）、
> `2026-09-12-scrollbar-part-element-design.md`（§2-D 否决「内层再嵌一层子元素」——本文同一条理由不做 `<Fill>` 子元素；§11-3「退役不留转发器」——本文 `fillRadius` 同款）、
> `2026-09-17-haze-design.md` / `2026-09-12-intensity-design.md` / `2026-08-28-inner-glow-design.md`（这些原语全部从同一个 `d` 派生，是本文「一行切割、全家跟随」的前提）、
> `2026-08-23-glass-fill-design.md`（玻璃；本文放开 fill 上的 `glass`，理由见 §5.6）。

## 1. 问题

需求：HUD 风格的进度条 —— 填充段带外发光、噪声雾、描边，**光晕要溢出轨道之外**，而且进度裁切
（`value="0.1"`）不能把光晕一起切没。

今天 `<Progress>` 做不到，三个原因叠在一起：

1. **主表面在 Bg 层。** `radius` / `glow` / `haze` … 全部落在轨道上（`Progress.cs:20`
   `SurfaceHost => _bg.gameObject`）。fill 是内层，按 procedural-surface spec §6 只有 `fillRadius`
   一个形状钩子。要给 fill 全套，按前缀规约得加 `fillGlow` / `fillGlowColor` / `fillBorderWidth` /
   `fillBorderColor` / `fillInnerGlow` / `fillInnerGlowColor` / `fillIntensity` / `fillHaze` /
   `fillHazeColor` / `fillHazeDrift` / `fillHazeDensity` —— **11 个新属性**，每一个说的都是作者早就会写的词。
2. **轨道形状靠 stencil。** `radius=` 写的是 bg 的 SDF，fill 是压在上面的另一张方角 Image，所以
   `maskRadius` 自动跟随 `radius`，在 `MaskWrapper` 上建 `Mask` 把 bg + fill 一起裁
   （`Progress.cs:274` `ReconcileProceduralMask`）。stencil 丢弃轨道形状**之外**的全部 fragment ——
   外发光正好画在形状之外，**整圈被裁掉**，只剩推进边那一点。
3. **进度裁切与 SDF 不兼容。** `mode="scale"` 收缩 fill 的 rect 锚点：SDF 的 radius 会随 rect 变窄重新
   clamp，10% 时是个圆点，不是「整条的左 10%」。`mode="fill"` 走 `Image.fillAmount`，根本没 SDF
   （`PUI-PROG-FILL-RADIUS-MODE` 就是在报这个）。

## 2. 否决的方案

**A. 11 个 `fill*` 前缀属性。否决。** 与 scrollbar spec §1-2 同一条：前缀规约撑不住「一层要全套」。
即便加了，问题 2 / 3 仍在 —— 光晕照样被 stencil 裁、`mode="fill"` 照样没 SDF。

**B. `<Fill>` 子元素（照 `<Scrollbar>` 的部件形态）。否决。** 零新属性，但 scrollbar spec §2-D 已经否决
过「内层再嵌一层子元素」；而且它多一个 tag 换来的唯一收益是「轨道保住全套效果」—— 作者已接受
「高级轨道 = 外面包一层 `<Frame>`」（`Samples~/ProceduralStyle/.../ProceduralStyle.ui.xml:177` 今天就是
这个写法），于是这个收益是零。`<Scrollbar>` 升格的真正理由（多宿主复制、模板复用）fill 一条都不沾。

**C. 只加 4 个（`fillBorderWidth` / `fillBorderColor` / `fillGlow` / `fillGlowColor`，即 `<Scrollbar>`
handle 的先例）。否决。** 需求要的是全套（haze / intensity / innerGlow 都要），4 个不够；且同样不解决
问题 2 / 3。

**D. 把 fill 搬出 `MaskWrapper`（reparent）让它躲开 stencil。否决。** 治标：躲开了轨道 mask，进度裁切
本身（问题 3）还是没有 SDF 形态。而且 sprite-fill 路径**需要**留在 mask 里（`mask="ui:pill" fill="ui:bar"`
是位图圆角进度条唯一的做法）。真正的解在 shader 里（§5.2），fill 不用动位置。

## 3. 方案总览

三件事，零新属性、零新 tag：

1. **主表面从 Bg 改到 Fill。** `ProceduralControl` 的 20 个属性（`radius` / `borderWidth` / `borderColor` /
   `glow` / `glowColor` / `innerGlow` / `innerGlowColor` / `intensity` / `haze` ×4 / `glass` + 7 个玻璃参数）
   直接作用于 fill。`fill` / `fillColor` 是这个主表面的贴图 / 颜色（不改名）。
2. **`radius` 是「条的形状」，三个消费者共享**：fill（程序化时）、bg（纯色层时）、clip mask（fill 是
   位图时）。其余 19 个属性只归 fill。这一条让 `<Style name="progress" radius="pill" bgColor fillColor>`
   （`Samples~/CommonControls/.../CommonControls.ui.xml:120`）的观感逐像素不变。
3. **进度裁切进 SDF。** 程序化 fill 的 rect 保持 stretch，`value` 变成 shader 里的一个半平面交集
   `d = max(d, dot(p, n) − e)`。border / 两层 glow / haze / intensity / 玻璃折射全部从这个 `d` 派生，
   于是推进边是直的、光晕沿切完的形状走（包括推进边）、四面溢出轨道；**没有 stencil 参与**。

```xml
<!-- HUD 能量条：胶囊轨道（纯色）+ 发光填充 + 底部雾 -->
<Progress value="0.6" radius="pill" bgColor="#0b1a33"
          fillColor="to right, hud-edge-cyan, #3cf" glow="6" glowColor="hud-edge-cyan/0.6"
          haze="10" hazeColor="to top, white/0.5, white/0" borderWidth="0.5" borderColor="white/0.4"/>

<!-- 高级轨道：外面包 Frame，Progress 自己不画轨道 -->
<Frame radius="pill" glass="true" borderWidth="1" borderColor="white/0.3" height="20">
  <Progress anchor="stretch" margin="2,2,2,2" value="0.6" radius="pill"
            fillColor="accent" glow="4" intensity="1.4"/>
</Frame>

<!-- 位图路径不变：fill 是 sprite 时 radius 走 mask（今天的行为） -->
<Progress value="0.4" radius="8" fill="ui:bar_blue" bgColor="#222"/>
```

## 4. 作者面

### 4.1 属性表（变化部分）

| 属性 | 变化 | 语义 |
|---|---|---|
| `radius` | **语义扩展** | 条的圆角。fill 程序化时是 fill 的 SDF 圆角；fill 是位图时是 clip mask 的圆角（= 今天的 `maskRadius` 自动跟随）；bg 是纯色层时同时也是 bg 的圆角。`pill` = 胶囊。 |
| `borderWidth` `borderColor` `glow` `glowColor` `innerGlow` `innerGlowColor` `intensity` `haze` `hazeColor` `hazeDrift` `hazeDensity` `glass` `frost` `depth` `dispersion` `lightAngle` `lightIntensity` `saturation` `noise` | **改归 fill** | 语义逐字同 `<Frame>`，作用在已填充段上。写任一即 fill 程序化（`fill=` 位图让位不销毁，同 `PUI-PROC-SPRITE-CONFLICT` 规则）。 |
| `fill` / `fillColor` | 不变 | 主表面的贴图 / 颜色。`fillColor` 在程序化模式下是 SDF 填充色（含渐变），渐变按**整条**定义再被裁 —— 30% 时看到的是渐变前 30%（与 `Image.Filled` 一致）。 |
| `fillRadius` | **退役** | `PUI-PROG-RETIRED-ATTR`。写 `radius`。（`<Slider fillRadius>` 不受影响。） |
| `bg` / `bgColor` | 不变 | 轨道贴图 / 颜色。轨道只有这两个 + `radius`；要描边 / 玻璃 / 发光的轨道，外面包 `<Frame>`。 |
| `frame` / `frameColor` / `frameRadius` | 不变 | 顶层装饰，画在 fill（含其光晕）之上。 |
| `mask` / `maskRadius` | 不变，但**自动跟随只在位图路径生效** | fill 程序化时不再自动建 mask（fill 自己有圆角，切边靠 SDF）。显式写了且 fill 声明了 `glow` → `PUI-PROG-MASK-CLIPS-GLOW`（warning）。 |
| `mode` | **两种 fill 同一个词** | `scale`（默认）= 形状本身缩到 value（位图：rect 锚定，9-slice 两端圆角都保留；SDF：盒子缩小，radius 随之 clamp，胶囊条推进端保持圆）；`fill` = 在 value 处裁断（位图：`Image.fillAmount`；SDF：半平面交集，推进边直）。`PUI-PROG-FILL-RADIUS-MODE` 退役。 |
| `direction` | 不变 | 程序化 fill 下决定半平面的法线。 |
| `value` | 不变 | 程序化 fill 下 `0` = 不画任何东西（含光晕）；`1` = 不裁。 |

### 4.2 与 `<Slider>` 的分叉（明说）

`<Slider glow>` 是**轨道**发光，`<Progress glow>` 是**填充段**发光。Slider 不跟：它的 HUD 主角是
handle（`handle*` 前缀已经给了 border + glow），fill 是配角；把 Slider 的主表面也搬到 fill 会让
`<Slider radius>` 失去「圆轨道」这个最常见用法。文档里一句话讲清楚（§10）。

### 4.3 复用 / 主题 / Variant

不变。全部是属性，`<Style>` + `class=` 与 `<Theme>` 照旧；`.variant` 后缀照旧（`glow.mobile="0"`）。
`PUI-THEME-STYLE-SHAPE` / `PUI-VARIANT-NO-BASE` 的「主表面 = 一组 NeedsPanel 属性整体开关」规则对
Progress 自动成立 —— 只是这组现在开关的是 fill。

## 5. 语义细节

### 5.1 `radius` 的三路分发

`radius` 由 `ProceduralControl.Radius` 收（`ProceduralControl.cs:102`，把值记进 `DeclaredRadius`），
但**不在 setter 里** `Surface.Declare` —— 属性顺序在一次 apply 内未定义，`fill=` 可能在它之后才写。
Progress 在 `OnAfterApply` 里、`base.OnAfterApply()`（触发 `Reconcile`）**之前**分发：

| fill 层是 | bg 层是 | fill | bg | mask |
|---|---|---|---|---|
| 程序化（无 sprite） | 纯色 / 关 | `Surface.Declare(radius)` | `BgSurface.Declare(radius)`（bg 开着时） | 不建；已建的 disable |
| 程序化（无 sprite） | 位图 | 同上 | 保留位图（位图自带圆角，SDF 不碰它） | 同上 |
| 位图 | 任意 | 不声明（Image 留任） | 纯色 → `BgSurface.Declare(radius)`；位图 → 保留 | **自动跟随**（今天的 `ReconcileProceduralMask`） |

「fill 层是程序化」的判定 = 本 pass 没有声明真实的 `fill=` 位图（`""` / `none` 不算）。
「其余 19 个属性」不走这张表：它们在 setter 里直接 `Surface.Declare`（同所有 `ProceduralControl`），
撞上位图 fill 就是 `PUI-PROC-SPRITE-CONFLICT`，程序化赢、位图让位。

`ProceduralControl` 需要一个 `private protected virtual` 钩子让子类接管 `radius` 的声明时机（默认实现
= 今天的 `Surface.Declare`），只有 Progress 覆盖。

### 5.2 SDF 进度裁切

`UI-ProceduralPanel.shader:198` 之后（`UI-GlassPanel.shader:198` 同款）：

```hlsl
float d = PuguiSdPanel(p, b, corner);
d = max(d, dot(p, n) - e);          // n = 推进方向单位向量；e = (2·value − 1) · dot(b, abs(n))
```

- `n`：`horizontal` = (1,0)，`reverse-horizontal` = (−1,0)，`vertical` = (0,1)，`reverse-vertical` = (0,−1)。
- `e` 在 rect 局部像素空间（与 `p` / `b` 同一空间）。`value = 0.5`、`b.x = 80` → `e = 0`，切在中线。
- `max` 做交集是仓库自己的做法（`PuguiSdCutCorner`，`UI-PanelSDF.cginc:76`）。切角外侧楔形区距离略被
  低估，光晕在那一点比真实距离场稍尖 —— 可接受，不做 exact 交集。
- 切割**必须放在解析法线也看得到的地方**：玻璃路径用解析梯度（`UI-PanelSDF.cginc:559` 刻意不用
  `ddx/ddy`），交集里切割项赢的区域法线就是 `n`，折射 / 打光才会沿推进边走。
- 下游全部不用改：`inside` 从新 `d` 来 → 填充 / haze 在切完的内侧；`PuguiApplyInnerGlow` /
  `PuguiApplyOuterGlow` / border 都吃 `d` → 沿切完的边（含推进边）；`UNITY_UI_ALPHACLIP` 的遮罩源路径
  也自动是切完的形状。

**参数走顶点通道，不走材质。** `ProceduralPanel.OnPopulateMesh`（`ProceduralPanel.cs:647`）今天用
`uv0.xy` 传局部坐标、`uv1.xy` 传半尺寸，`uv0.zw` / `uv1.zw` 四个 float 空着（shader 头注释：
「形状输入走顶点通道而非材质，这是刻意的」）。实际落地为**两个 float 全放 `uv1.zw`**：`z` = 轴码（0 不切 / ±1 沿 x / ±2 沿 y，符号 = 填充方向），`w` = `e`；`uv0.zw` 不动，不多开 canvas 通道。于是：

- 材质仍由 `ProceduralMaterialCache` 按参数共享，**`value` 动画不产生材质实例**（`BuildParams` 不含切割）；
- `value` 变化只 `SetVerticesDirty()`，不脏布局（今天 `scale` 模式每帧改锚点 → layout rebuild）；
- 无切割 = 轴码 0：shader 里切割项取 `−1e6`（select，不用分支 —— `d` 随后要过 `fwidth`），`max(d, −1e6) = d`，非 Progress 的面板逐位不变；顶点通道留 0 即可，没有任何其它写 mesh 的路径要改。

**两个端点：**

- `value = 0`：切线与起始边重合，`inside` 在起始边上有半像素 AA 带，外发光还会沿起始边画一条 ——
  0% 的进度条不该有任何东西。规则：**`value == 0` 时面板不出几何**（`ComputeVisible` 为假）。
- `value = 1`：切线与末端边重合，`max` 与不切逐位等价（末端外侧 `d_shape ≥ p·n − b`）；实现上直接
  编码为无切割，省掉边界上的浮点纠结。

**可选的 overdraw 收缩**（不在 v1）：`OnPopulateMesh` 可把 quad 在推进边一侧收到 `e + glow`。

### 5.3 fill 的 rect 与 `mode`

| fill 层 | rect | 进度 |
|---|---|---|
| 程序化 | 永远 stretch（`anchorMin 0, anchorMax 1, offset 0`） | §5.2 的 SDF 切割 |
| 位图，`mode="scale"` | 锚点随 value（今天） | rect |
| 位图，`mode="fill"` | stretch + `Image.Type.Filled`（今天） | `fillAmount` |

`ReconcileFill` 按 `SurfaceIsDrawing` 分支，每次都把 rect 写成当前模式该有的样子 —— 主题从玻璃皮切到
像素皮时 fill 从 SDF 变回位图，rect 要重新按 value 锚定；反向则要重置成 stretch。运行期 `progress.Value = x`
不经过 apply pass，同样走这个分支。

程序化 fill 下 `mode` 与位图路径逐字同义（2026-09-18 二次对齐，作者要求推进端也保持圆角）：

| `mode` | SDF 形态 | shader |
|---|---|---|
| `scale`（默认） | 形状本身沿轴缩到切线，radius 随缩小后的尺寸 clamp —— 胶囊条的推进端是半圆，5% 是一枚贴着起始边的细胶囊（9-slice 拉伸时两端圆角都保留的观感） | `PuguiCutShrink`：把 `(p, b)` 换成缩小后的盒子再 `PuguiResolveQuad` / `PuguiSdPanel`；无交集 |
| `fill` | 半平面裁断，推进边直（`Image.fillAmount` 的观感） | `PuguiSdCut`：`max(d, dot(p, n) − e)` |

两者渐变都按整条 rect 铺（`PuguiGradient` 拿原 `p` / `b`）：裁的是形状不是颜色。顶点通道编码：`uv1.z` = `±1 / ±2` flat、`±3 / ±4` round（轴码 + 2）；`uv1.w` 同一个 `e`。

### 5.4 bg 层

- 只有 `bg` / `bgColor` / `radius`（纯色时）。开关条件回到 `_bgSprite || _bgColor`，去掉今天的
  `|| SurfaceIsDrawing`（`Progress.cs:251`）—— 那一项存在是因为主表面曾经住在 bg 里。
  **行为变化**：`<Progress radius="pill" fillColor="x"/>`（没写 `bgColor`）今天会凭空出一条白色轨道
  （surface 拿 `_hostImage.color` = 白当填充），改后 bg 保持关闭，只有一条圆角 fill。
- bg 的圆角通过 `AddInnerSurface(_bg.gameObject)`（只收 radius）；bg 是位图时不碰
  （今天 `radius` + `bg="ui:track"` 会静默把位图丢掉 —— 改后位图保留）。

### 5.5 mask

- 位图 fill：`maskRadius` 自动跟随 `radius`，`mask=` sprite 路径，`PUI-PROG-MASK-RADIUS-CONFLICT` ——
  全部照旧。
- 程序化 fill：不自动建 mask。已经建过的（主题切换来的）`SetMaskSource(false)` + disable，不销毁
  （Strategy C）。
- 显式 `maskRadius=` / `mask=` + 程序化 fill：照写（作者可能要非矩形的 sprite 遮罩），但它会裁掉光晕 ——
  fill 声明了 `glow` 时 lint warning `PUI-PROG-MASK-CLIPS-GLOW`。

### 5.6 fill 上的 `glass`

放开（决策 §12-b）。procedural-surface spec §6 禁内层玻璃的理由是「压在玻璃轨道上的玻璃 fill 采的是同一张
backdrop，两层一样、进度条消失」。改后轨道不能再是玻璃（bg 只有纯色 / 位图），前提不存在。语义：fill 是
一块在 value 处裁断的玻璃，采的是 **canvas 后面的场景**，不是它底下的 `bgColor` —— 不透明 `bgColor` 上的
玻璃 fill 显示的是场景，不是轨道色。文档写明即可。`PuguiSdNormal` 沿切边（§5.2）保证折射带在推进边也有。

### 5.7 层序与溢出

`MaskWrapper(Bg, Fill) → Frame` 的兄弟顺序不变。fill 的光晕撑大的是它自己的 quad，越过 Progress 的 rect ——
uGUI 不裁子级，除非上游有 `Mask` / `RectMask2D`（在 `<ScrollList>` 视口里被裁是预期）。`Frame` 层画在光晕
之上，透明区透出光晕。放在 `<VStack>` 里光晕会压到邻居 —— 与 `<Frame glow>` 在 stack 里一样，已知行为。

## 6. 报错（`Core/Lint/ProgressAttributeRules.cs` + `ProceduralSurfaceRules.cs`）

| Code | 触发 | 级别 | 变化 |
|---|---|---|---|
| `PUI-PROG-RETIRED-ATTR` | `<Progress>` 节点（含经 `class=` 解析）出现 `fillRadius` | error | **新增**。消息指向 `radius`。只在 Progress 上报 —— `fillRadius` 仍是 `<Slider>` 的合法属性，`<Style>` 包不能盲报。 |
| `PUI-PROG-MASK-CLIPS-GLOW` | 显式 `mask=` / `maskRadius=` 且 fill 声明了 `glow`（fill 是程序化的：无真实 `fill=` 位图） | warning | **新增**。 |
| `PUI-PROC-SPRITE-CONFLICT` | `fill="ui:x"` + 任一 19 个效果属性（`radius` 除外） | error | **调整**：Progress 的主表面 sprite 属性叫 `fill` 不叫 `sprite`，`ProceduralSurfaceRules` 加一张 per-tag 主表面 sprite 名映射（默认 `sprite`，`Progress → fill`）；`InnerLayers["Progress"]` 去掉 `("fill", fillRadius)`，保留 frame。`radius` 不算冲突（位图 fill 下它走 mask，§5.1）。 |
| `PUI-PROG-FILL-RADIUS-MODE` | — | — | **退役**。 |
| `PUI-PROG-VALUE-RANGE` `PUI-PROG-MODE` `PUI-PROG-DIRECTION` `PUI-PROG-CHILDREN` `PUI-PROG-MASK-VARIANT` `PUI-PROG-NO-FILL` `PUI-PROG-MASK-RADIUS-CONFLICT` | 不变 | | `PUI-PROG-NO-FILL` 仍成立：没写 `fillColor` 的程序化 fill 拿 Image 的白当填充，和今天白色 Image 一样「能看见但不是你要的」。 |

`ProceduralAttrNames.InnerLayerGroups` 的 `("fill", ["fillRadius"])` **保留**（Slider 还在用）；
`ProceduralAttrNamesTests` 的双向镜像断言按 Progress 不再有 `fillRadius` 更新。

## 7. 实现地图

| 处 | 改动 |
|---|---|
| `Runtime/Resources/PromptUGUI/Material/UI-PanelSDF.cginc` | `PuguiSdCut(d, p, n, e)`（一行 `max`）+ 解析法线里切割项赢时返回 `n`。 |
| `UI-ProceduralPanel.shader` / `UI-GlassPanel.shader` | `texcoord1` 改 `float4`，`zw` 经 `v2f` 多传一个 `float2 cut`；`d = PuguiSdPanel(...)` 之后调 `PuguiSdCut`。哨兵编码保证非 Progress 面板逐位不变。 |
| `Controls/Internal/ProceduralPanel.cs` | `SetCut(Vector2 n, float e)` / `ClearCut()`：只 `SetVerticesDirty`，不进 `BuildParams`；`OnPopulateMesh` 写通道；`value == 0`（由 Progress 传 `SetCutEmpty()` 或 `e` 哨兵）→ `ComputeVisible` 假。 |
| `Controls/ProceduralControl.cs` | `Radius` setter 改调 `private protected virtual void DeclareRadius(RadiusSpec)`，默认 = 今天的 `Surface.Declare`。 |
| `Controls/Progress.cs` | `SurfaceHost => _fill.gameObject`；删 `FillRadius` / `FillSurface`；加 `BgSurface`（inner，只收 radius）；`FillColor` → `Surface.SetFill`；`BgColor` → `BgSurface.SetFill`；`_fillSprite` 标志；覆盖 `DeclareRadius` 只记录；`OnAfterApply` 先按 §5.1 分发 radius 再 `base`；`ReconcileLayers` 去掉 `SurfaceIsDrawing`；`ReconcileProceduralMask` 加「fill 程序化 → 不建」；`ReconcileFill` 加程序化分支（stretch + `SetCut`）。 |
| `Core/Lint/ProgressAttributeRules.cs` | 删 `FillRadiusModeCode`；加 `RetiredAttrCode` / `MaskClipsGlowCode`。 |
| `Core/Lint/ProceduralSurfaceRules.cs` | per-tag 主表面 sprite 名；`InnerLayers["Progress"]` 去 fill。 |
| `Editor/`（XSD） | 反射生成，`fillRadius` 自动从 Progress 消失；无手改。 |

不动：`Slider.cs`、`Scrollbar.cs`、`ProceduralSurface.cs`、`ProceduralMaterialCache.cs`、`DecorPanel` /
`GlassGroupPanel`（它们不走 `PuguiSdCut`，哨兵即无切割）。

## 8. 测试（Red 先行）

**shader / panel（`ProceduralSurfaceRenderTests` 的 ReadPixels 套路）**
- `radius="pill" fillColor="#f00" glow="8" glowColor="#0f0" value="0.5"`，横向：rect 内 25% 处像素 = 红；75% 处 = 透明（无 bg）；rect **上方** 4px（25% 处）像素带绿（光晕溢出）；推进边（50%）右侧 4px 像素带绿（光晕包住推进边）；75% 处上方 4px = 透明（切完的形状外没有光晕）。
- 同上 `value="0"`：整张全透明（含光晕）。`value="1"`：与不切割逐像素相等。
- `direction` 四值各一例（只验证哪一半有填充）。
- 玻璃 fill：切边上折射带非零（对比 `depth="0"`）。

**Progress 控件（`ProgressTests` / `InnerLayerRadiusTests` 改写）**
- 任一效果属性 → `__Surface` 挂在 `Fill` 之下、`Bg` 之下没有；fill rect 是 stretch，不随 value 变；`Value = 0.3` 后 panel 的 cut 参数 = f(0.3, direction)，材质引用不变。
- `radius="8" fillColor bgColor`（纯色路径）→ fill panel radius 8、bg panel radius 8、`MaskWrapper` 上无启用的 `Mask`。
- `radius="8" fill="ui:bar" bgColor` → fill 无 surface、Image 留任、锚点随 value；`MaskWrapper` 有 `Mask` + `ProceduralPanel(maskSource)` radius 8（今天的断言原样保留）。
- `radius="8" bg="ui:track"` → bg 位图保留，`_bg.sprite != null`。
- `radius="pill" fillColor`（无 bgColor）→ Bg 不激活。
- 主题切换：玻璃皮（程序化 fill）→ 像素皮（`fill="px:bar"`）→ 回玻璃皮：rect 在 stretch / 锚定之间正确往返，mask enable 往返，无 Destroy。
- `AttributeReversibilityTests` 现有的 Progress 用例照跑。

**lint（`ProgressAttributeRulesTests` / `ScrollbarRulesTests:248` 那条 `fillRadius` 用例 / `IRWalkerProgressTests`）**
- `<Progress fillRadius="4"/>` → `PUI-PROG-RETIRED-ATTR`；经 `class=` 同报；`<Slider fillRadius>` 不报。
- `<Progress maskRadius="8" glow="4" fillColor/>` → `PUI-PROG-MASK-CLIPS-GLOW`；`fill="ui:x"`（位图）时不报。
- `<Progress fill="ui:x" glow="4"/>` → `PUI-PROC-SPRITE-CONFLICT`；`<Progress fill="ui:x" radius="8"/>` **不报**。
- `<Progress mode="fill" radius="6" fillColor/>` 不再报 `PUI-PROG-FILL-RADIUS-MODE`（code 删除后编译即失败的测试一并删）。
- `ProceduralAttrNamesTests` 镜像断言更新。

## 9. 演示同步（同 PR）

- `Samples~/ProceduralStyle/.../ProceduralStyle.ui.xml:177` 的 Progress 加 `radius="pill" glow="4" glowColor="accent/0.6" haze="8" hazeColor="to top, white/0.4, white/0"` —— 它已经在 well Frame 里，正是 §3 第二个例子。
- `Samples~/CommonControls/.../CommonControls.ui.xml:120` 不改：`radius="pill" bgColor fillColor` 的观感按 §5.1 逐像素不变，这是回归基准。

## 10. SKILL / 文档更新（同 PR，英文）

- `authoring-promptugui-xml/reference/controls-progress.md`：重写「程序化圆角」一节为「fill 是主表面」；`radius` 三路分发表；SDF 进度裁切（推进边直、光晕溢出、渐变按整条）；`value=0` 不画；`mode` 只对位图 fill；`glass` fill 语义；lint 表增删。
- `authoring-promptugui-xml/SKILL.md`：主表面表（`:418`）`<Progress>` 行 **bg 层** → **fill 层**；内层形状表（`:435`）去掉 `fillRadius`，留 `frameRadius` / `maskRadius`；`:440-447` 的自动跟随段与 `PUI-PROG-FILL-RADIUS-MODE` 段改写；`:423` 的按层冲突例子里 `<Progress fill= fillRadius=>` 换成 `<Progress fill= glow=>`；加一句 Slider / Progress 的分叉（§4.2）。
- `scripting-promptugui-csharp/SKILL.md`：`Progress.FillRadius` 属性移除（当前文档未列出，核对即可）。
- 主 spec `2026-05-07-...-design.md:159` Progress 行加本文链接。
- `2026-08-26-procedural-surface-design.md` §6 / §13.3 加一行「Progress 已按 2026-09-18 spec 改为 fill 主表面」。

## 11. 不做的事（YAGNI 记录）

- `<Fill>` 子元素 / `fill*` 前缀家族（§2）。
- `<Slider>` 主表面搬家；`<Slider>` fill 的 SDF 切割（它的 fillRect 由 uGUI 锚定，radius 跟着 clamp 的观感对 Slider 是可接受的）。
- exact 的 SDF 交集（§5.2）。
- quad 在推进边一侧的 overdraw 收缩（§5.2）。
- 轨道（bg）的 border / glow / glass —— 包 `<Frame>`。
- 径向 / 环形进度（`<Cooldown>`，PB-D6 原话）。

## 12. 决策记录（2026-09-18 与作者对齐）

- **a. 透穿而非子元素**：`ProceduralControl` 的属性直接作用于 fill；`radius` 作为「条的形状」由 fill / bg / mask 三方共享；其余归 fill。高级轨道 = 外包 `<Frame>`。
- **b. fill 上放开 `glass`**：禁令的前提（玻璃轨道）随本文消失。
- **c. 直接进 spec**，不另起 brainstorm 文档。
- 进度裁切进 SDF 而非 stencil；参数走顶点通道（作者提出的「光晕溢出且裁切不影响 glow」由此一并满足）。
- `fillRadius` 退役、不留转发器（scrollbar spec §11-3 先例）。

## 13. 里程碑拆分

| M | 内容 | 依赖 |
|---|---|---|
| M0 | shader `PuguiSdCut` + 解析法线 + 顶点通道 + `ProceduralPanel.SetCut` / `value=0` 空几何；render 测试 | — |
| M1 | Progress 主表面搬家 + `radius` 三路分发 + mask 抑制 + `ReconcileFill` 程序化分支 + 主题往返测试 | M0 |
| M2 | lint：`PUI-PROG-RETIRED-ATTR` / `PUI-PROG-MASK-CLIPS-GLOW` / 主表面 sprite 名映射 / 退役 `FILL-RADIUS-MODE` / 镜像测试 | M1 |
| M3 | SKILL 三处 + 样例 + 主 spec 指针 | M1–M2 |

## 14. 实施记录（2026-09-18）

按 §13 四步落地，每步 Red 先行、全量 EditMode（4374）/ EditorOnly（348）/ PlayMode（244）绿后提交：

- **M0** `feat(procedural)`：`UI-PanelSDF.cginc` 加 `PuguiCutNormal` / `PuguiCutTerm` / `PuguiSdCut` / `PuguiPanelNormalCut`；两份面板 shader 的 `texcoord1` 改 `float4`，`zw` 进 `v2f.cut`，`d` 先切再 `fwidth`；玻璃 shader 保留 `dShape` 交给法线判定。`ProceduralPanel.SetCut(direction, value)` / `ClearCut()`：`_cutCode`（0 / ±1 / ±2）+ `_cutValue`，只 `SetVerticesDirty`；`OnPopulateMesh` 写 `uv1 = (hx, hy, code, (2·value − 1)·b_axis)`；`_cutEmpty`（value 0）让 `ComputeVisible` 为假。**编码与 §5.2 的差别**：不用 `(n, e)` 三个 float，而是「轴码 + 偏移」两个 float 全塞进 `uv1.zw`，`uv0.zw` 未动（不需要多开 canvas 通道）；不切 = code 0，切割项取 `−1e6`，用 select 不用分支（`d` 随后过 `fwidth`）。测试：`ProceduralCutTests`（顶点通道、value 0 无几何、value 1 = 不切、材质引用不变；渲染：推进边直、光晕四面溢出并包住推进边、切外无光晕、描边沿切边）、`ProceduralCutGlassRenderTests`（URP 门控：切边朝光一侧比背光亮 —— 没有法线修正时两者相等）。宿主 URP 已启用，玻璃用例实跑通过；渲染 dump 人工看过。
- **M1** `feat(progress)`：`ProceduralControl.Radius` 改经 `private protected virtual DeclareRadius`；加 `SurfacePanelOrNull`。`Progress.SurfaceHost => _fill`；`_fillSprite` 标志；`RouteRadius()` 在 `OnAfterApply` 里、`base` 之前按 §5.1 分发（fill 非位图 → `Surface.Declare`；`_bgColor && !_bgSprite` → `BgSurface.Declare`）；`ReconcileProceduralMask` 的自动跟随改为只在 `_fillSprite` 时；`ReconcileLayers` 去掉 `SurfaceIsDrawing`；`ReconcileFill` 程序化分支 = stretch + `SetCut`，回位图路径时 `ClearCut`。删 `FillRadius`。测试：`ProgressFillSurfaceTests`（13 条，含变体往返 位图 ↔ SDF 的 rect / mask / Image 三者复位、`toggled never rebuilt`）；`InnerLayerRadiusTests` / `ProceduralSurfaceRolloutTests` / `ProceduralSurfaceRenderTests` 里钉住旧语义的用例改写（自动跟随改用位图 fill；`radius` 单独不再点亮 bg；rollout 循环给 Progress 一个 `value`，因为 0 % 按设计不画）。
- **M2** `feat(lint)`：`PUI-PROG-RETIRED-ATTR`（直接 / 经 `class=` / 变体后缀；只在 Progress 上报）、`PUI-PROG-MASK-CLIPS-GLOW`（显式 `mask=` 真 sprite 或非空 `maskRadius` + `glow` + fill 非位图；`""` 是退出不是裁）、`PUI-PROG-FILL-RADIUS-MODE` 删除；`ProceduralSurfaceRules` 加 `PrimarySprite`（Progress → `fill`）与 `RadiusSparesTheBitmap`（`DeclaresProcedural(skipRadius)`），`InnerLayers["Progress"]` 只剩 frame。CLI 独立编译通过，跑遍 `Runtime/Resources` + `Samples~` 零 issue。
- **M3** 文档：主 `SKILL.md`（catalog 三行、主表面表、冲突例子、内层表、玻璃例外、`radius` 三路 + SDF 裁切两段、四个例子）；`reference/controls-progress.md`（「fill 是主表面」整节 + 三路分发表 + lint 表五行）；`ProceduralStyle` 样例的 Progress 加 `radius="pill" glow haze`；主 spec §Progress 行、procedural-surface spec §6 / §13.3 加指针。
- **M4** `feat(procedural)` round cut（作者二次对齐：推进端也要保持 radius，无新属性，复用 `mode`）：`UI-PanelSDF.cginc` 加 `PuguiCutIsRound` / `PuguiCutShrink`，`PuguiCutNormal` / `PuguiCutTerm` 认得 `±3 / ±4`；两份 shader 先 `PuguiCutShrink(pS, bS)` 再 `PuguiResolveQuad` / `PuguiSdPanel`，渐变仍用原 `p` / `b`，玻璃法线从 `(pS, bS)` 求。`ProceduralPanel.SetCut(direction, value, round)`；`Progress.ReconcileFill` 传 `round: _mode != "fill"`。测试：`ProceduralCutTests` 加 5 条（编码；推进角被圆掉 vs flat 保方；光晕包住圆端并溢出；ramp 按整条裁；5% 是细胶囊）；`ProgressFillSurfaceTests` 默认码改 ±3 / ±4，`Mode_PicksTheRoundOrTheFlatCut`。全量 EditMode 4379 绿。**默认观感变化**：`radius="pill"` 的程序化 fill 推进端从 M1 的直边变成半圆（与同页 `<Slider fillRadius="pill">` 一致）；要直边写 `mode="fill"`。
