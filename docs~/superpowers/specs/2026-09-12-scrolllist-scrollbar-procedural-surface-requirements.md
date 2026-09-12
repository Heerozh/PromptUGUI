# ScrollList 滚动条程序化表面 —— 需求说明

日期：2026-09-12
提出方：heeroz 4xn 的建造网格 （ScrollList columns="4"）
状态：**已由 design 接手**，见 [`2026-09-12-scrollbar-part-element-design.md`](2026-09-12-scrollbar-part-element-design.md)。
目标（R1–R4）与 §5 验收全部保留；**§1 / §3 里的 `scrollbar*` 前缀属性语法已被取代** —— 滚动条改为
`<Scrollbar>` 子元素（轨道 = 主表面继承 `<Frame>` 全套形状属性，滑块 = `handle*`，几何 = `thickness` /
`padding` / `spacing` / `overlay`），宿主上的 `scrollbar*` 属性整体退役，Dropdown 一并纳入。
本文其余内容作为需求原文保留，不再更新。
前置：2026-09-10 `scrolllist-grid-and-static-children`（已合并，PR #131）

## 1. 想画什么

设计稿里的滚动条是 HUD 风格的一根「胶囊轨道 + 发光胶囊滑块」：

- **轨道**：竖向长胶囊（两端全圆），深色半透明填充，一圈细青色描边；与内容之间空一段距离。
- **滑块**：更短的胶囊，亮青色，**缩在轨道里面**（比轨道窄、两端不顶到轨道端头），带柔和的外发光。

期望写法（属性名按本库既有惯例起，可以改，语义别变）：

```xml
<ScrollList columns="4" cellSize="63x86" spacing="4" scrollbarOverlay="true"
  scrollbar="" scrollbarWidth="6" scrollbarRadius="pill"
  scrollbarColor="glass-primary-darker/0.6" scrollbarBorderWidth="0.4" scrollbarBorderColor="hud-edge-cyan/0.6"
  scrollbarHandle="" scrollbarHandleRadius="pill" scrollbarHandleColor="hud-edge-cyan"
  scrollbarHandleGlow="3" scrollbarHandleGlowColor="hud-edge-cyan/0.6"
  scrollbarPadding="1.5" scrollbarSpacing="6" …/>
```

## 2. 现状为什么写不出来

`Runtime/Controls/ScrollList.cs` 的滚动条只有 **sprite + color** 两对（`scrollbar` / `scrollbarColor`、
`scrollbarHandle` / `scrollbarHandleColor`），外加 2026-09-10 加的 `scrollbarWidth` / `scrollbarOverlay`：

1. **没有程序化表面**：`radius` / `borderWidth` / `glow` 这一族在滚动条两层上不存在。`sprite=""` 只能得到
   纯色直角矩形；要圆头 / 描边 / 发光只能靠 sprite，而 4～8 单位宽的 9-slice 在这个尺寸下描边挤成一串点
   （库默认那张 `pugui_9slice_round` 就是这样），发光更做不了。
2. **滑块几何写死**：`ApplyScrollbarMetrics` 里 Sliding Area 两轴各内缩一个 `w`、handle 再加回 `w` ——
   净效果是滑块横跨整条宽度、只有两端各留 `w` 留白。滑块没法比轨道窄。
3. **与视口的间距写死**：`verticalScrollbarSpacing = max(-w, -3)`，作者改不了。

`<Slider>` 的 `fillRadius` / `handleRadius` 和 `<Progress>` 的 `fillRadius` / `frameRadius` / `maskRadius`
已经用 `AddInnerSurface` + `ProceduralSurface.Declare(p => p.SetRadius(v))` 给内层做过同样的事，
本需求就是把这条路铺到 ScrollList 的两根滚动条上，并且多开 border / glow 两组。

## 3. 需求

### R1 轨道与滑块各接一个 ProceduralSurface

两层各自一套，命名沿用 `<layer>` 前缀惯例（轨道前缀 `scrollbar`、滑块前缀 `scrollbarHandle`）：

| 层 | 属性 | 语义 |
|---|---|---|
| 轨道 | `scrollbarRadius` | 同 `<Frame radius>` 全语法（`pill` / 数值 / 四角列表） |
| 轨道 | `scrollbarBorderWidth` · `scrollbarBorderColor` | 同 `<Frame>`，向内画，不改布局 |
| 轨道 | `scrollbarGlow` · `scrollbarGlowColor` | 同 `<Frame>`（可选，优先级低于滑块的） |
| 滑块 | `scrollbarHandleRadius` | 同上 |
| 滑块 | `scrollbarHandleBorderWidth` · `scrollbarHandleBorderColor` | 同上 |
| 滑块 | `scrollbarHandleGlow` · `scrollbarHandleGlowColor` | 同 `<Frame glow>`：外发光，撑大绘制 quad、不动布局 |

- 规则照抄程序化表面一节：写了任一形状属性即该层换 SDF 面、Image 让位但不销毁；`scrollbar` /
  `scrollbarHandle` 的 sprite 与之矛盾时报 `PUI-PROC-SPRITE-CONFLICT` 同款 lint（`""` / `none` 不算矛盾）。
- 颜色仍走 `scrollbarColor` / `scrollbarHandleColor`（token / `/alpha` / 渐变都支持，同 Slider 的
  `HandleColor` 既 `ColorApplier.Apply` 又 `Surface.SetFill`）。
- **不给玻璃**（理由同 Progress 内层：backdrop 采集不含 UI 自身）。
- 两根滚动条（横 / 竖）共用同一份声明，同现有皮肤属性；懒建的那根建完要回放。
- 发光不会被视口裁掉：滚动条是 ScrollList 根的直接子节点、在 Viewport 的 mask 之外，这点要有测试钉住
  （glow 存在时滚动条节点仍在 mask 外、quad 撑大量 = glow）。
- Variant 可覆盖，走正常 ReSolve；`BeginPass` / `Reconcile` 的「模式每轮重算不 latch」纪律照旧。

### R2 `scrollbarPadding`：滑块缩在轨道里

- 单值或 `V,H`（沿轴 / 跨轴），单位设计像素。含义：滑块矩形相对轨道矩形**四边内缩**的量 ——
  跨轴内缩让滑块比轨道窄，沿轴内缩让滑块不顶到轨道端头。
- 实现即改 `ApplyScrollbarMetrics` 里 Sliding Area 与 handle 的 sizeDelta：Sliding Area 沿轴缩
  `2·padV`、跨轴缩 `2·padH`，handle 跨轴不再加回 `w`（改为 `w − 2·padH`）。
- **不写时保持现状**（沿轴端头留白 = `w`、滑块横跨整宽），别动已有调用点。
- `scrollbarWidth` 仍是轨道厚度；滑块厚度 = `w − 2·padH`，算出来 ≤ 0 时钳到 1 并 warning。

### R3 `scrollbarSpacing`：与视口的距离

- 带符号数值，单位设计像素，直接落到 `ScrollRect.verticalScrollbarSpacing` / `horizontalScrollbarSpacing`。
  正数 = 条离视口边缘这么远（非 overlay 模式下视口相应再缩），负数 = 压进视口。
- 不写时保持现状（`max(-w, -3)`）。
- overlay 模式下 uGUI 不读这个值（条压在视口上），文档要写明：overlay 下与内容的距离靠内容 `padding`
  （网格模式 `padding="0,R,0,0"`）或列宽让出来 —— 这条现在就能用，不必发明新属性。

### R4 配套

- `Editor/XsdGenerator` 重跑，`PromptUGUI.gen.xsd` 带上新属性。
- lint：`ProceduralSurfaceRules` 把 `scrollbar*` / `scrollbarHandle*` 的形状组纳入 sprite 冲突检查；
  `PUI-PROCEDURAL-VALUE` 覆盖新属性的值校验。
- skill 文档 `authoring-promptugui-xml` 的 `<ScrollList>` 属性表与「程序化表面 → 内层也能给形状」一节
  补这两层（那一节现在说「内层只给形状」，滚动条这里开了 border / glow，要把例外写清楚）。
- 测试（`Tests/EditMode/Controls/ScrollListTests.cs`）：写 `scrollbarHandleRadius` 后 handle 节点下出现
  `__Surface` 面板且 Image 让位；`scrollbarHandle="ui:x"` + radius 触发冲突 lint；`scrollbarPadding="1,2"`
  后 Sliding Area / handle 的 sizeDelta 符合 R2 公式，不写时与现状逐字相同；`scrollbarSpacing="6"` 落到
  ScrollRect；glow 存在时滚动条 RectTransform 不在 Viewport 子树内。

## 4. 非目标

- 不做滚动条的 hover / pressed 状态色（uGUI Scrollbar 自带 ColorTint 够用；要做另起需求）。
- 不做滚动条端头的箭头钮。
- 不改 `<Slider>` / `<Progress>` 内层「只给形状」的现状。

## 5. 验收

星球面板把槽位列表改成第 1 节的写法后，UIPreview 里：轨道是带青色细描边的深色长胶囊、滑块是缩在里面
的亮青胶囊并带外晕，滑块两侧各留 1.5、不顶端头；`scrollbarSpacing` 在非 overlay 模式下能看到条离开
视口；关掉全部新属性后与 PR #131 之后的现状逐像素一致。
