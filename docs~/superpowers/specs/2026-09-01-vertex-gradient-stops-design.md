# 顶点路径的渐变色标与提示 —— `GradientTint` 几何切分（VGS）

> 状态：**已对齐，待实施**（2026-09-01）。决策编号 `VGS-Dn`（§11）。
> 相关：`2026-08-30-gradient-stop-position-design.md`（色标 / 提示语法与 shader 路径；其 §5 / §12
> 把顶点路径的实现明确留作后补 —— 本文就是那个后补）；`2026-06-13-gradient-color-design.md`
> （`GradientTint`）；`2026-08-31-hug-reveal-flip-checked-design.md` §3（`rotation` / `flip`）；
> `2026-09-01-graphic-reflection-design.md`（`reflect=` 属性方案 —— **备用，未立项**，见 §13）。

## 0. 背景与范围

起点是「给图标 / 图片加镜面倒影」（资源条图标落在玻璃台面上的淡影、卡片立在反光地板上的倒影）。
讨论过三种落点：

1. `reflect=` 属性（同网格追加镜像顶点）—— 一行属性、零同步，但**做不了「倒影 < 地面 < 物件」的
   夹层**（倒影与本体永远同一 draw），且倒影在界面里出现得少。
2. 独立节点 `<Reflection of=>`（代理 Graphic）—— 能夹层，但是一个中型机制。
3. **作者用现有原语手工组合**：`flip="y"` 翻一份、渐变 `color` 淡出、按 XML 顺序自己排层 ——
   零新机制，夹层天然可行。唯一的缺口是**渐变在精灵图上只能铺满整个高度**：`A, B 50%` 这种
   已有语法在顶点路径上被丢弃（`PUI-GRADIENT-STOP-NO-SURFACE`），所以「淡到一半就归零」得再套
   一层 `mask="rect"`。

定案走 3，本文补那个缺口：让色标（`A 70%, B`）与提示（`A, 70%, B`）在**所有顶点着色的 Graphic**
上生效。这是通用能力（`<Progress>` 填充条、按钮 sprite 皮肤、`<Image>` 蒙层……全部受益），倒影只是
第一个用例。`reflect=` 的设计保留为备用（§13）。

## 1. 问题

`GradientTint.ModifyMesh` 把 `Lerp(Bottom, Top, s)` 写进**既有顶点**。Simple 图只有上下两排顶点，
`f(0)=Top`、`f(1)=Bottom`，色标位置在光栅化那一步被抹平；Sliced 图在九宫格边界多几排顶点，
得到一个**取决于边框宽度的**分段线性近似 —— 比不生效还糟，看起来像生效了。2026-08-30 spec §5
的结论：「要在顶点路径上做对，必须按两条水平线切三角形……本轮不做」，§12 并给出了后补时的
改动面：「删掉 `GradientStopRules` 里对应的那几行 + `ColorApplier` 的 warning，语法不动」。

同时暴露出的第二个问题：`flip="y"` 与渐变 `color` 都是 `BaseMeshEffect`，uGUI 按**组件添加顺序**
调用 `ModifyMesh`，而添加顺序 = 属性 setter 顺序 = `Attributes` 字典插入顺序 ≈ XML 里属性书写的
先后。`<Icon flip="y" color="A,B">` 与 `<Icon color="A,B" flip="y">` 画出来上下相反。倒影配方
恰好同时用到两者，这个坑必须一起填。

## 2. 否决的方案

**新属性 `reflect=`（同网格追加镜像顶点）。备用，未立项。** 设计完整（§13 指向的文件），成本约一天。
不立项的理由：倒影出现频次低；夹层场景它做不了而手工组合能做；手工组合缺的只是本文这项通用能力。
等出现 `cover` 图 / C# 动态换图 / 按钮内倒影这类手工版开始难受的场景再提上来。

**倒影 alpha 走自定义材质。否决。** 与 `UI-LinearLightTint` / `UI-Grayscale` 的材质槽打架，破坏
同图集合批；色标是 y 的分段仿射函数，切开后顶点插值**精确**，不需要 fragment 参与。

**只做色标、不做提示。否决。** 倒影要的正是「衰减偏向上部、没有拐点」—— 那是提示（幂曲线）。
提示用 K 条等距切线逼近，代价可忽略（§4.2）。

**`mask="rect"` 包裹当作正式配方。否决。** 每个倒影 +2 节点、没有提示曲线、且 `mask` 的语义是
裁子节点 —— 让作者为了截短一段渐变去开一个遮罩层，是把实现细节推给作者。

## 3. 语法：无新增

色标 / 提示语法原样（2026-08-30 spec §3 / §14）。本文只是让下面这些写法在精灵图上**真的生效**：

```xml
<!-- 一行倒影：顶边 0.35 → 50% 处降到透明 → 以下全透明（几何直接剔除，无 overdraw） -->
<Icon name="res:gold" flip="y" color="white/0.35, white/0 50%"/>

<!-- 更柔和：加提示，整段幂曲线衰减、偏向上部，无马赫带 -->
<Icon name="res:gold" flip="y" color="white/0.35, 30%, white/0 60%"/>

<!-- 与倒影无关的既有需求，顺带解锁 -->
<Progress fillColor="accent 60%, accent-dark"/>        <!-- 填充条下 40% 收暗 -->
<Image sprite="ui:vignette" color="black/0 40%, black/0.6"/>   <!-- 精灵蒙层从 40% 起压暗 -->
```

## 4. 语义

### 4.1 求值函数：与 shader 同一条公式

`ColorSpec.Evaluate(float s)`，`s` = 距**网格包围盒顶边**的归一化距离（0 顶、1 底，与 shader 的
`PuguiFillRamp` 同向），`a` / `b` / `E` = `TopStop` / `BottomStop` / `Curve`：

```
u = saturate((s − a) / max(b − a, 1e-4))
if (E != 1) u = pow(u, E)
return lerp(Top, Bottom, u)
```

逐字对应 `UI-PanelSDF.cginc` 的 `PuguiFillRamp`；`E` 仍由 `ColorParser.StopCurveExponent` 算好。
同一份 `color=` 在 `<Frame>`（shader）与 `<Image>`（顶点）上的转换位置因此一致 —— 这是 2026-08-30
spec §7「三处逐字一致」原则的延伸。

### 4.2 切分规则

`HasStops == false`（默认 `0% / 100%`、无提示）→ **走今天的逐顶点路径，逐位不变**：不 de-index、
不分配、既有文档零回归（VGS-D1）。

`HasStops == true` →

1. `GetUIVertexStream` 取三角形流；求包围盒 `minY` / `maxY`，`H = maxY − minY`；
2. 切线集合（局部 y）：`yA = maxY − a·H`、`yB = maxY − b·H`；`E != 1` 时在 `(a, b)` 内再加
   `K−1` 条等距切线（`K = 8`，常量不暴露）；`a == b` 时两线重合，只切一次（硬边：线上顶点被
   两侧各自持有 —— 三角形流本就不共享顶点）；
3. 逐条切线对每个三角形做水平线分割（Sutherland–Hodgman 的单半平面特例）：整体在一侧的原样
   保留；跨线的分成「尖端三角形 + 四边形（两个三角形）」，新顶点沿被切边线性插值 **position /
   color / uv0–uv3 / normal / tangent**，其 `y` 直接赋为切线值（不留插值漂移）；绕向保持；
4. 每个顶点：`color *= Evaluate((maxY − y) / H)`。切线上的顶点落在 `a` / `b` / 提示切线的精确位置，
   段内颜色是 y 的线性函数，硬件插值**精确**（色标）或**逐段线性逼近幂曲线**（提示，K=8 时肉眼
   不可辨）；
5. `vh.Clear()` + `AddUIVertexTriangleStream`。

### 4.3 透明尾段 / 头段剔除

`Bottom.a == 0` 时，完全位于 `y ≤ yB` 的三角形**不发**；对称地 `Top.a == 0` 时剔除 `y ≥ yA` 的。
倒影场景零 overdraw，效果等价于免费的 `mask="rect"` 裁切（VGS-D2）。不影响任何别的东西：
raycast 按 rect、`RectMask2D` 剔除按 rect、`GetNativeSize()` 读 sprite —— 都不看网格。

### 4.4 顺序：渐变在旋转 / 镜像**之后**算（屏幕空间）

`RotateFlipEffect` 必须排在 `GradientTint` 前面（VGS-D3）。规则只有一条，对作者和 SKILL 都是：
**「第一段永远是你看到的顶部」**，与旋转 / 镜像无关。

实现：`RotateFlipApplier.ReserveSlot(graphic)` —— 若尚无 `RotateFlipEffect` 则以**禁用态**预挂一个。
`<Image>` / `<Icon>` / `<RawImage>` 的 `Color` setter 在 `spec.IsGradient` 时、调 `ColorApplier.Apply`
**之前**调它。`GradientTint` 只在渐变时才被添加，因此其槽位必然在后。其它经 `ColorApplier` 的
Graphic（按钮底、进度条填充…）没有 `rotation` / `flip` 属性，不需要预留。

代价：三个叶子标签上**声明了渐变色**的实例多一个禁用组件；恒等的 `RotateFlipEffect` 即使启用
也在 `ModifyMesh` 首行早退，禁用只是更干净。`RotateFlipApplier.Apply` 的既有逻辑（已存在 → 赋值
→ `enabled = !IsIdentity`）无需改动。

否决的替代：`RotateFlipApplier` 在挂效果时销毁并重建已有的 `GradientTint` 以调整顺序 —— Play 模式
下 `Destroy` 延迟到帧末，同帧内 `GetComponent<GradientTint>` 会拿到待销毁的那个，同一 pass 后续的
`color` setter 写进去的值随之丢失；改用 `DestroyImmediate` 又违背「效果组件禁用不销毁」的惯例。
把三个效果合成一个固定顺序的链组件治本但要动十来个测试文件，与本文规模不称（留 §12）。

### 4.5 哪些路径因此获得色标

所有走 `ColorApplier.Apply` 的颜色属性（`Image` / `Icon` / `RawImage` 的 `color`；`Btn` / `Tab` /
`Toggle` / `Dropdown` / `InputField` / `ScrollList` / `Slider` / `TabMenu` / `Collapsible` 未开程序化
表面时的 `color` 及绝对状态色；`Progress` 的 `fillColor` / `bgColor` / `frameColor`；`Slider` 的
`fillColor` / `handleColor`；各控件的 `arrowColor` / `iconColor` / `headerColor` / `checkmarkColor` /
`popupColor` / `itemColor` / `scrollbarColor` / `scrollbarHandleColor` / `frameColor`）。

**仍然做不到的**：TMP 文本 —— `<Text color>` 与各控件的 `textColor` / `itemTextColor`
（`LabelColorApplier` → TMP `VertexGradient`，逐字形四顶点，不走 `IMeshModifier`）。
`PUI-GRADIENT-STOP-NO-SURFACE` 收窄到只管这些（§7）。

### 4.6 状态色与主题

- `GradientTint` 存整个 `ColorSpec`；`ColorApplier.Peek` 回读时**带回色标与提示**（今天的 `Peek`
  只重建 Top/Bottom，会把形状丢掉）。`StateTintReactor` 的基色捕获与 `Multiply` 因而保形 ——
  悬停调制不会把色标抹平。
- 渐变端点的状态转换仍然 snap（既有规则不变）。
- Variant / 主题切换重放 setter，`HasStops` 在 true / false 之间来回切换时两条路径各自独立、幂等。

### 4.7 不变的东西

RectTransform、布局、raycast、材质、贴图、合批 —— 全部不变。`type="sliced" / "tiled" / "filled"`、
`useSpriteMesh` 的紧致网格：都是三角形流，同一段切分代码。

## 5. 倒影配方（SKILL 收录）

```xml
<!-- 一排图标各带倒影：图标 + 倒影包成一个单元，宽度由图标决定，数字列宽度变化不影响对齐 -->
<HStack spacing="24">
  <VStack spacing="0">
    <Icon name="res:gold"/>
    <Icon name="res:gold" flip="y" color="white/0.35, white/0 50%"/>
  </VStack>
  <VStack>…数字…</VStack>
</HStack>

<!-- 倒影 < 地面 < 物件：绘制顺序 = XML 顺序 -->
<Image sprite="card:01" anchor="top-center" flip="y" color="white/0.3, 25%, white/0 45%" …/>
<Image sprite="bg:floor-tiles" color="white/0.6"/>
<Image sprite="card:01" anchor="top-center" …/>
```

配方的既知代价（写进 SKILL，不做机制）：sprite / `hidden` / Variant 换图要写两遍，C# 动态换图要设
两处；`<Icon>` 的 `preserveAspect` letterbox 上下对称，`flip="y"` 后仍对齐；素材自带透明留白时倒影
与本体的缝会翻倍，用 `margin` 拉回。倒影里的几何被剔到色标为止，`<VStack>` 里它仍按**完整
rect** 占位 —— 要更紧凑就给倒影节点显式 `height`。

## 6. 实现地图

| 层 | 文件 | 改动 |
|---|---|---|
| 值模型 | `Application/ColorSpec.cs` | `Evaluate(float s)`（§4.1） |
| 切分 | `Controls/Internal/MeshSlicer.cs`（新） | `SplitAlongY(List<UIVertex> tris, float y, List<UIVertex> output)` + `Lerp(in UIVertex, in UIVertex, float)`；纯几何、无状态、可单测 |
| 效果 | `Controls/Internal/GradientTint.cs` | `Set(in ColorSpec)` / `Spec`；`HasStops` 分支走 §4.2–4.3；`Set(top, bottom)` 保留为便捷重载 |
| 落点 | `Controls/Internal/ColorApplier.cs` | `Apply` 传整个 spec；`Peek` 回读整个 spec |
| 顺序 | `Controls/Internal/RotateFlipApplier.cs` + `Image.cs` / `Icon.cs` / `RawImage.cs` | `ReserveSlot`；三个 `Color` setter 渐变时先预留 |
| 撤警告 | `Image.cs` / `Icon.cs` / `RawImage.cs` / `Internal/ProceduralSurface.cs` | 删 `GradientStopWarning.IfMoved` 调用（`Text.cs` / `LabelColorApplier.cs` 保留）；`GradientStopWarning` 与 `ColorSpec.HasStops` 的注释改口 |
| lint | `Core/Lint/GradientStopRules.cs` | 只剩 TMP 路径（§7） |
| XSD / 注册 / 图集 / i18n | 无 | 无新属性、无新标签 |

## 7. lint

`PUI-GRADIENT-STOP-NO-SURFACE` 收窄：

| 之前报 | 之后 |
|---|---|
| `<Image>` / `<Icon>` / `<RawImage>` 的 `color` | **不报**（生效了） |
| 未开程序化表面的控件 `color` / 绝对状态色（`MainSurfaceAttrs`） | **不报** |
| 未开 `<layer>Radius` 的 `fillColor` / `handleColor` / `frameColor`（`InnerSurfaceAttrs`） | **不报** |
| `NeverSurfaceAttrs` 里的非文本项（`arrowColor` / `checkmarkColor` / `popupColor` …） | **不报** |
| `<Text color>`；`textColor` / `itemTextColor` | **仍报**，文案改为「TMP 逐字形四顶点，放不下色标」 |

规则名与代码不变（作者已见过它），只是触发面缩到真正做不到的地方。CLI 与运行时 warning 共用
同一实现，自动同步。

## 8. 测试

- `ColorSpecEvaluateTests`（纯 C#）：无色标时 `Evaluate(s) == Lerp(Top, Bottom, s)`；`a` 之前纯顶色、
  `b` 之后纯底色；`a == b` 硬边；提示在其位置处恰为 50/50 混合（对照 `StopCurveExponent`）。
- `MeshSlicerTests`（纯几何）：单三角形被一条线切成 3 个；四边形（2 三角）切一次得 4 个；顶点恰在
  线上不产生零面积三角形；线在包围盒外不改动；插值顶点的 uv / 颜色 / 位置按比例、`y` 精确等于切线；
  绕向保持；连切两线。
- `GradientTintTests`（既有）+ `GradientTintStopTests`（新）：无色标路径顶点数仍为 4（未 de-index）
  且颜色与今天一致；`A, B 50%` 后存在 `y == 50` 的顶点、其色为 B、更低处无几何（B 透明时）/ 为 B
  （B 不透明时）；`A 30%, B 60%` 上段全 A、下段全 B；硬边 `50%, 50%`；提示时切线数为 K；
  `Set(spec)` / `Peek` 往返保形。
- `GradientFlipOrderTests`：`<Icon color flip>` 与 `<Icon flip color>` 两种书写，`GetComponents<BaseMeshEffect>()`
  顺序均为 `[RotateFlipEffect, GradientTint]`；Variant 把 solid 切成 gradient 后顺序不变；未声明渐变的
  `<Image>` 不多挂任何组件（flip spec 的「恒等零组件」承诺不破）。
- `GradientStopLintTests`（既有，改期望）：Image / Icon / RawImage / 无表面 Btn / Progress / Slider /
  Toggle checkmark / Dropdown popup 全部转为 **不报**；Text / textColor 仍报。
- `GradientStopRenderTests`（EditMode，`Camera.Render()` 到 RT，`DecorRenderTests` 同款夹具；`<Image>`
  无 sprite 时 uGUI 画白色实心矩形，无需图集）：第一条夹具自检；`#f00, #00f 50%` 在 25% 高处为红、
  75% 处为蓝、50% 处为紫；提示 `#fff, 30%, #000` 在 30% 处约 50% 灰；`flip="y"` + 色标两种属性顺序
  顶部像素相同；`#fff, #fff/0 50%` 下半等于背景色（剔除生效）。数据层测试对「屏幕上没有」是盲的，
  这一组是回归防线。
- PlayMode：`GradientPlayTests` 加一条带色标的 `<Image>` 过两帧仍保形（smoke）。

## 9. SKILL 更新（同 PR，英文）

- `authoring-promptugui-xml/SKILL.md`
  - **Gradients** 节：「Stops only work on a procedural surface」整段改写 —— 色标与提示在**所有**
    顶点着色的 Graphic 上生效，唯一例外是 TMP 文本；加一句「透明端点的那一段几何会被剔除」；
    加一句「渐变按最终画出来的网格算，`rotation` / `flip` 不改变『第一段是顶部』」。
  - `<Image>` / `<Icon>` / `<RawImage>` 的 `color` 行提及色标 / 提示可用。
  - **Rotation & flip** 之后新增 **Reflection recipe** 小节（§5 两个模板 + 代价清单）。
  - 错误表 `PUI-GRADIENT-STOP-NO-SURFACE` 一行改为「text-only」。
- `reference/states.md` 第 9 行：`hoverColor="#fff 70%,#aaa"` 不再需要程序化表面。
- `authoring-promptugui-pxl/SKILL.md` 一句：倒影用 `flip` + 渐变生成，**不要**画进 `.pxl`。
- `scripting-promptugui-csharp/SKILL.md`：无公共 API 变化，不动。

## 10. 成本

| | 开销 |
|---|---|
| 没写色标 / 提示的渐变 | **零变化** —— 原路径逐位相同 |
| 写了色标的 Graphic | 一次 de-index（四边形 4 → 6 顶点）+ 每条切线对跨线三角形 +2 个三角形；Simple 图两色标 ≈ 18 顶点，提示 ≈ 40 顶点；材质 / 贴图 / 合批不变 |
| 三个叶子标签上的渐变色 | 多一个禁用的 `RotateFlipEffect` |
| 透明端点 | 几何剔除，overdraw 反而更少 |

## 11. 已定的决策（2026-09-01 与作者对齐）

1. **VGS-D1** 无色标路径逐位不变；色标路径才 de-index + 切分。
2. **VGS-D2** 透明端点那一段几何直接剔除。
3. **VGS-D3** 渐变在旋转 / 镜像**之后**算（屏幕空间）；靠 `ReserveSlot` 预留槽位保证顺序，不销毁重建。
4. **VGS-D4** 提示用 K = 8 条等距切线逼近，常量不暴露。
5. **VGS-D5** 顶点求值与 shader 共用同一条公式（`ColorSpec.Evaluate` ↔ `PuguiFillRamp`）。
6. **VGS-D6** `reflect=` 属性方案备用不立项；倒影以配方形态进 SKILL。
7. **VGS-D7** lint 规则名不变，只收窄到 TMP 路径。

## 12. 开放问题（留给 plan / 实现期）

- ~~K = 8 是否在极高的倒影上（>200px）看得出折线~~ —— 已量：200px 高的提示渐变 PNG 上看不出折线，K 保持 8（见 §14）。
- 链组件重构（`GradientTint` + `RotateFlipEffect` (+ 将来的 `ReflectionEffect`) 合一）—— 有第三个
  网格效果时再做，届时 `ReserveSlot` 自然退役。
- `MeshSlicer` 是否值得公开给自定义控件作者 —— 先 `internal`。

## 13. 备用：`reflect=` 属性方案

`2026-09-01-graphic-reflection-design.md` 是完整的属性方案（同网格追加镜像顶点、线性渐隐、
`reflectLength` / `reflectGap`、顺序保证、lint、渲染测试）。本文落地后它的几何部分可以直接复用
`MeshSlicer`（半平面裁剪就是同一段代码）。触发重启的信号：`type="cover"` 图要倒影、C# 动态换图的
物件要倒影、`<Btn>` 内部的图标要随状态调制一起倒影 —— 任一出现即可按那份 spec 出 plan。

## 14. 实施记录（2026-09-01）

**测试**：EditMode 模式下三个程序集（`Tests.EditMode` + `Tests.EditorOnly` +
`Tests.EditMode.Addressables`）合计 3724/3724 全绿；PlayMode 198/198 全绿。新增
`ColorSpecEvaluateTests`(7) / `MeshSlicerTests`(7) / `GradientTintStopTests`(11) /
`GradientFlipOrderTests`(8) / `GradientStopRenderTests`(8) + PlayMode 1 条。

**K 的最终值：8**。`GradientStopRenderTests.Hint_PutsTheHalfwayMixAtTheHint` 用 200px 高的
`<Image>` 渲染 `#ffffff, 30%, #000000` 并 dump PNG，肉眼看不出折线；数值上相邻切线之间的弦
在提示处与真值差 < 0.005。§12 第一条据此关闭。

**与设计的偏差**

1. **硬边需要「顶点朝三角形重心微偏」**（`GradientTint.CentroidBias = 1e-3`）。§4.2 假定把新顶点的
   `y` 精确钉在切线上就够 —— 不够：正好落在切线上的顶点同时属于两侧的三角形，而两侧要的颜色
   不同，这正是硬边的定义。求值时把 `s` 朝本三角形的重心挪 0.1% 即可各取所需；在 ramp 连续的
   地方这点位移远小于一个色阶。
2. **切一个四边形得到 3 + 3 个三角形，不是 2 + 2**。两个三角形共一条对角线，切线各自把它们切成
   「尖端 + 四边形」。plan 里按顶点数写的断言因此换成了「面积守恒 + 每个三角形整体落在一侧」——
   本来也更该这么断言，顶点数是在钉三角剖分而不是钉画面。
3. **运行期 warning 推迟到 Task 6 才删**（plan Task 5 曾允许提前删）。分开做每个提交都是绿的。
4. **`GradientStopWarning` 文案重写**：旧文案说「只有程序化表面能画色标」，现在只对 TMP 成立。

**顺带发现**

1. **`ColorApplier.Peek` 会把色标抹平**（既有 bug，Task 3 一并修掉）。`StateTintReactor` 捕获基色
   再带 modulate 重新落地，而 `Peek` 是用 `Top` / `Bottom` 重建 spec 的 —— 任何带色标的
   `hoverColor` / `pressedColor` 在第一次状态切换后就退回全高 ramp。
2. **`flip` 与渐变的顺序确实是不确定的**，不是理论担忧：红测试里 `<Image color flip>` 的组件顺序
   是 `[GradientTint, RotateFlipEffect]`，`<Image flip color>` 则相反 —— 前者把渐变画在镜像之前，
   两种写法画出来上下颠倒。
3. **本工程是 linear 色彩空间**。渲染回归里「一半的 ramp」读回 0.53 而不是 0.25，第一版切片
   用例的阈值因此太松：把 `HasStops` 临时改成 `false` 时它仍然通过。补了「不串色」断言才咬得住 ——
   渲染测试的阈值必须拿关掉功能的那一版验一遍，否则只是在测试渲染管线还活着。
