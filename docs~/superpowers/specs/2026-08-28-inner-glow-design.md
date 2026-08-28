# 内发光 `innerGlow` —— 程序化表面的第四层光

> 状态：**已实现**（M0 + M1 一轮做完，见 §12）。决策见 §8，2026-08-28 与作者对齐。
> 相关：`2026-08-23-procedural-style-design.md`（`glow` / `glowColor` 的出处，§2.1；shader 分段 §2.2）、
> `2026-08-26-procedural-surface-design.md`（`ProceduralControl` 让九个控件共享同一套属性）、
> `2026-08-23-glass-fill-design.md`（玻璃填充 + weld 容器持有轮廓参数）、
> `2026-08-27-corner-treatments-design.md`（cut / notch / hexagon —— 内发光必须原样跟随这些轮廓）、
> `2026-08-27-theme-procedural-shape-exemption-design.md`（SHAPE 规则的程序化豁免集从 `NeedsPanel` 派生）。

## 1. 问题

参考需求图（星海指挥官主界面）的「开始匹配」主按钮，剥掉外圈线条只看本体，是四层光叠出来的：

| 层 | 观感 | 今天 |
|---|---|---|
| 金色上下渐变填充 | 顶部略亮、底部略暗 | ✅ `color="A,B"` |
| **边缘一圈浅黄向内衰减 ~15px** | 轮廓被光照亮、中心回到本色 | ❌ |
| 1px 亮描边压在最外沿 | 轮廓的硬边 | ✅ `borderWidth` / `borderColor` |
| 柔和金色光晕溢出到形状外 | 「在发光」 | ✅ `glow` / `glowColor` |

缺的那一层就是 Photoshop 意义上的 **Inner Glow（边缘模式）**。程序化表面今天从 SDF 派生出
填充、内描边、外发光、玻璃折射四样东西，**没有任何一样在轮廓内侧带衰减地画东西** ——
描边是硬边带，填充只有纵向渐变。于是参考图里最能决定「高级金属牌」质感的那一层做不出来。

现有的绕路全部不成立：

- **贴图**：丢掉主题换形状（#104 打通的能力，corner spec §1 的同一条论证）。
- **内嵌一层 `<Frame borderWidth="14" borderColor="white/0.3">`**：硬边带、无衰减；内层 Frame
  是矩形内缩，跟不上外层的 `cut` / `hexagon` 轮廓；每个按钮多一个 draw call。
- **玻璃的 `lightIntensity` 边缘光**：只在 `glass="true"` 下存在，方向性、依赖 backdrop，
  不是这个东西。

同一机制换个深色就是**内阴影 / 边缘暗角**（CSS `box-shadow: inset`）—— 一个属性两种用途，
所以混合方式的选择（§8.3）比看起来重要。

**分层位置**：这是程序化表面「光」的第二个原语（第一个是外发光）。参考图剩下的两处差距 ——
顶部冠形凸起（第 7/8 条边）与斜向光带 `sheen` —— 另行立项，本文不涉及。

## 2. 否决的方案

**`glow="12 inset"`（CSS 式关键字，复用 `glow` 属性）。否决。** 参考图同时需要外发光和
内发光；一个属性只能表达其一，`glowColor` 也没法一分为二。属性面积 +2 是正确的代价。

**screen / additive 混合。否决。** screen 更像「光」，但只能变亮，做不了内阴影；additive
在非 HDR 的 UI 上容易过曝且不可控。两者都会是这个库里唯一一处非 source-over 的合成 ——
`PuguiOver` 是填充 / 描边 / 外发光 / 玻璃 tint 全部共用的合成方式，`/alpha` 控强度的心智
模型一致。要更亮，作者把 `innerGlowColor` 调浅就行。

**方向性内发光（用 `lightAngle` 调制成 bevel / emboss）。不进 v1。** 参考图四边亮度均匀，
是内发光不是斜面。方向性版本是自然的扩展位（§9），但它引入的是另一个视觉概念。

**`<Decor>` 支持。否决。** 装饰原语是 2px 笔画和薄三角，没有「内部」可言；
`PureContainerVisualAttrRules` 已经把 `NeedsPanel` 里 Decor 不支持的名字报成
`PUI-CONTAINER-VISUAL-ATTR`，新名字自动落进去。

**逐状态变体 `hoverInnerGlow` 等。不进 v1。** 状态反应器（`StateTintReactor`）只驱动
`Graphic.color` 整体 tint，没有任何程序化参数有逐状态版本；要「hover 时边缘更亮」走
`<Show on="state-hover">` 叠一层，与今天 `glow` 的处理一致。

## 3. 方案总览

两个新属性，语法、解析器、落点全部镜像 `glow` / `glowColor`：

```xml
<Frame color="#1b263b" radius="16" innerGlow="12"/>                           <!-- 白色边缘光 -->
<Btn radius="hexagon 60" color="#ead49a,#c39a45"
     innerGlow="16" innerGlowColor="#fff3c4/0.9"
     borderWidth="1" borderColor="#fff0b8/0.9"
     glow="24" glowColor="#e8b860/0.5">开始匹配</Btn>                           <!-- 参考图 -->
<Frame color="surface" radius="12" innerGlow="20" innerGlowColor="black/0.35"/> <!-- 内阴影 -->
<Style name="gold-plate" innerGlow="14" innerGlowColor="#fff3c4/0.9" borderWidth="1"/>
```

- `innerGlow`：px，发光带宽度，从形状边缘向内量。
- `innerGlowColor`：纯色，默认 `white`。
- 作用面 = `glow` 的作用面：`<Frame>`、`ProceduralControl` 的九个控件（Btn / Tab / Toggle /
  Slider / Dropdown / InputField / ScrollList / Progress / …）、weld 容器（融合后的轮廓）。
- **纯材质参数**：不外扩 quad、不动布局、不动顶点；Variant / 主题切换只换材质。

## 4. 语法

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `innerGlow` | px（非负有限数） | `0` | 内发光带宽度。`""` = 退回 0（Variant 只能改值不能删属性，同 `glow`） |
| `innerGlowColor` | hex / CSS 名 / 主题 token / `/alpha`，**纯色 only** | `white` | 发光色；`/alpha` 就是强度旋钮 |

### 4.1 解析错误（parse-time，纯 C# 子集，CLI 同步可见）

全部复用既有 chokepoint，**不新增错误码**：

| 写法 | 结果 |
|---|---|
| `innerGlow="abc"` | `ProceduralValueParser.Pixels`：`innerGlow="abc": expected a number of pixels (e.g. "1", "2.5")` |
| `innerGlow="-4"` | `must not be negative` |
| `innerGlow="NaN"` | `must be a finite number` |
| `innerGlowColor="a,b"` | `UI.Theme.Resolve` 拒绝渐变（同 `borderColor` / `glowColor`） |
| `<Style innerGlow="abc">` | `PUI-PROCEDURAL-VALUE`（`StyleRules.PixelAttrs` 加名字即可） |

## 5. 语义

### 5.1 带与衰减

设 `d` 为 SDF（内部为负），`inside` 为抗锯齿覆盖率：

```
g = saturate(1 + d / innerGlow)      // d ∈ [-innerGlow, 0] → g ∈ [0, 1]，边缘处 1
alpha = innerGlowColor.a × g² × inside
```

与外发光同一条 `g²` 曲线、镜像方向。这样 `glow="12" innerGlow="12"` 读起来是**跨越边缘的一整圈
对称光晕**，而不是两种手感的光拼在一起。`g²` 让能量收在边缘附近、中心保持本色 ——
Photoshop 的默认内发光大致也是这个曲线。不提供 choke / spread 旋钮（§9）。

### 5.2 合成顺序

```
不透明面：  填充 → [内发光 over] → 外发光 (under) → 描边 over
玻璃面：    玻璃体 → tint over → [内发光 over] → 外发光 (under) → 描边 over
```

内发光紧跟在填充（玻璃是 tint）之后：外发光带 `(1 - inside)`、内发光带 `inside`，两者除
AA 那一像素外不相交，谁先谁后只影响那一像素；把内发光放在外发光之前，让外发光的 under
合成看到的是「填充 + 内发光」这一个完整实心体。描边始终最后，压在所有东西上面。

`mask="self"` 的遮罩覆盖率仍取 `inside`（形状就是形状，与画了什么无关）—— 内发光在形状
内部，本来就不会改变它。

### 5.3 起点：从形状边缘 d = 0 量起，描边压在上面

Photoshop Inner Glow 语义（§8.4）。`borderWidth="1" innerGlow="16"`：发光带 16px，最外 1px 被
描边盖住。

- **半透明细描边**（库里最常见的 `borderColor="white/0.4"` / `stroke/0.15`）：发光延续到描边
  底下，描边区 = 描边色 over 满强度发光 → 边缘最亮、无缝。
- **粗不透明描边**：最外 `borderWidth` px 的发光被盖掉，作者看到的带比写的窄、起点强度是
  `(1 - borderWidth/innerGlow)²`。把数值调大即可，文档写明。

否决的 CSS `inset box-shadow` 语义（从描边内沿量）在半透明描边下会在边缘出 1px 暗缝 ——
一个常见配置里的视觉 bug，比「粗描边要调大数值」严重得多。

### 5.4 默认色：`white`

外发光默认跟随填充色，因为「这个形状在发光」是它的默认含义。内发光跟填充同色、填充又不
透明时**完全不可见** —— 那个默认是个陷阱。`white` 是「边缘被光照亮」的直觉，任何填充上
都可见，且与 `borderColor` 的默认一致。要暖色 / 冷色作者显式写。

显式值优先，`/alpha` 缩放强度；不做「跟随 `glowColor`」的联动。

### 5.5 可见性

`innerGlow > 0 && innerGlowColor.a > 0` 让面板可见 —— 没有填充、没有描边、只有内发光是一个
合法的形态（空心的发光环，同 border-only 的先例），`ComputeVisible` 加一条。

### 5.6 形状跟随

派生自同一份 SDF，圆角 / `cut` / `notch` / `pill` / `hexagon` 一律自动跟随，不需要任何新
属性；`notch` 凹顶点处内描边已是精确解（corner spec §11），内发光吃同一个 `d`，同样精确。

### 5.7 disabled

`_grayed` 时 `innerGlowColor` 与其他三种颜色一起 `Desaturate` —— 灰掉的按钮不该有一圈
彩色内光。

### 5.8 玻璃与 weld

- **玻璃面**：同一段代码，画在 tint 之上；backdrop 不可用的降级路径（半透明面板）照画。
- **weld 容器**：内发光沿**融合后**的轮廓走，所以是容器的参数（`GlassRules.GroupAttrs` +2，
  `GlassGroupPanel` 发布容器的 `InnerGlow*` 到组材质）。写在成员上会被
  `PUI-GLASS-WELD-PARAM-PLACEMENT` 报出来 —— 既有规则，只扩名字表。理由同描边：逐成员
  的内发光会在焊缝处画出 weld 存在的意义就是要抹掉的那条分界。

### 5.9 不到达的地方

| 地方 | 行为 | 机制 |
|---|---|---|
| 内层 `<layer>Radius`（`fillRadius` 等） | 无 `fillInnerGlow` | procedural-surface spec §6：内层 shape-only |
| `<Decor>` | 静默丢弃 → `PUI-CONTAINER-VISUAL-ATTR` | `NeedsPanel` − `DecorRules.SupportedProceduralAttrs`，自动 |
| `<VStack>` / `<HStack>` / `<Grid>` / `<SafeArea>` | 同上，指路「套一层 Frame」 | `ProceduralAttrNames.All`，自动 |
| `<Animation>` | 不可动画 | 没有任何程序化参数可动画，不新开口 |
| SHAPE 主题规则 | 进豁免集 | `ThemeStyleRules` 从 `NeedsPanel` 派生，自动 |
| `VariantBaseRules` | 与 `glow` 同待遇 | 从 `NeedsPanel` 派生，自动 |

### 5.10 材质共享与性能

`PanelParams` 多两个字段，参与 `Equals` / `GetHashCode`。同 style 的面板照旧共用一个材质；
只有内发光不同的两块面板是两个材质 —— 与任何其他参数相同。fragment 多一个 uniform 分支 +
四条算术，`_InnerGlowSize == 0` 时整段跳过（同 `_BorderWidth` 的注释：uniform 分支全体
fragment 同路径，开销可忽略）。

**不动几何**：`MarkDirty` 的顶点脏检查只看 `_glowSize`，内发光不加进去。

## 6. 实现地图

### 6.1 数据流（改动点自上而下）

| 文件 | 改动 |
|---|---|
| `Runtime/Resources/PromptUGUI/Material/UI-PanelSDF.cginc` | 新增 `PuguiApplyInnerGlow(col, d, inside, size, color)`：§5.1 的四行，三个 shader 共用，保证不透明面 / 玻璃面 / 融合面逐像素一致（corner spec 的「一份 cginc 喂所有面」原则） |
| `UI-ProceduralPanel.shader` / `UI-GlassPanel.shader` / `UI-GlassGroup.shader` | `_InnerGlowSize` / `_InnerGlowColor` 属性 + 声明 + 在 §5.2 的位置调一次 helper。`UI-Decor.shader` **不改** |
| `Controls/Internal/ProceduralMaterialCache.cs` | `PanelParams` + `InnerGlowColor` / `InnerGlowSize`（ctor、`Equals`、`GetHashCode`）；`Configure` 两个 `SetX`；两个 `PropertyToID` |
| `Controls/Internal/ProceduralPanel.cs` | 字段 `_innerGlowColor = white` / `_innerGlowSize`；`SetInnerGlowSize` / `SetInnerGlowColor`；`BuildParams` 传入 + `_grayed` 时 `Desaturate`；`ComputeVisible` 加一条 |
| `Controls/Internal/GlassGroupPanel.cs` | 组材质发布容器的 `InnerGlowColor` / `InnerGlowSize`（同 `BorderColor` / `GlowSize` 那两行的旁边） |
| `Controls/Frame.cs` | `[UIAttr] InnerGlow` / `InnerGlowColor`，直连 `Panel.SetX` |
| `Controls/ProceduralControl.cs` | 同名两个 `[UIAttr]`，走 `Surface.Declare` —— 九个控件自动获得 |
| `Core/Lint/ProceduralAttrNames.cs` | `PanelAttaching` / `All` / `NeedsPanel` 各 +2（`ProceduralAttrNamesTests` 守镜像） |
| `Core/Lint/StyleRules.cs` | `PixelAttrs` + `"innerGlow"` |
| `Core/Lint/GlassRules.cs` | `GroupAttrs` + `"innerGlow"`, `"innerGlowColor"` |
| `Core/Lint/PureContainerVisualAttrRules.cs` | 消息文案里「Only glow / glowColor carry over」依旧成立，不改 |
| `Editor/XsdGenerator.cs` | Frame 手写清单 +2（注释明说「every attribute added to it has to be added here too」） |

`PanelParams` 的 ctor 签名变了，实现时 grep 全部构造点（`ProceduralPanel.BuildParams` 与测试）。

### 6.2 shader 段落

```hlsl
// UI-PanelSDF.cginc
// 内发光：仅在形状内侧衰减，与外发光镜像 —— 同一条 g² 曲线，于是 glow 与 innerGlow 等宽时
// 读起来是跨越边缘的一整圈对称光晕。从 d=0 量起，描边随后压在上面（Photoshop Inner Glow 语义）。
float4 PuguiApplyInnerGlow(float4 col, float d, float inside, float size, float4 color)
{
    if (size <= 0.0) return col;
    float g = saturate(1.0 + d / size);
    color.a *= g * g * inside;
    return PuguiOver(color, col);
}
```

调用点：不透明面在 `col.a *= inside` 之后、外发光之前；玻璃面与融合面在 `col = PuguiOver(tint, base)` 之后、外发光之前。

**外发光是否一并抽成 `PuguiApplyOuterGlow`**：建议同 PR 做（三份复制的四行代码正是 cginc
要消灭的漂移），但必须逐像素不变 —— `CornerTreatmentRenderTests` / `ProceduralSurfaceRenderTests`
/ `GlassRenderTests` 的既有基线是守卫。做不到逐像素不变就不抽，不为这个特性冒险。

### 6.3 CLI

`Core/Lint` 三处改动都是名字表，纯 C# 子集不变。UIXmlLint 两遍（raw + expanded）自动覆盖
`<Style innerGlow=>` 经 `class=` 落到控件上的情形。

## 7. SKILL 更新（同 PR，英文）

`authoring-promptugui-xml/SKILL.md`：

- 原语目录 `<Frame>` 行：`color` / `radius` / `borderWidth` / `glow` 后追加 `innerGlow`。
- `<Frame>` 属性表：`glowColor` 之后 +2 行（§4 的表），示例块加一行金色 hexagon + 内发光，
  一行 `black/0.35` 内阴影（让「同一属性两种用途」进作者的视野）。
- 「Only `glow` is affected by an ancestor `RectMask2D`」那条：补一句 `innerGlow` 永远在 rect
  内，不受影响。
- Corner treatments 小节「Border, glow, glass and `mask="self"` follow the new outline
  automatically」→ 加 inner glow。
- 「Which tags draw procedurally」引用块与 Procedural surfaces 一节的属性行：加 `innerGlow` /
  `innerGlowColor`。
- 描边与内发光的叠放规则一句话：measured from the shape edge, an opaque border covers the
  outermost `borderWidth` px of it —— 粗描边要把数值调大。

`reference/glass.md`：开头「`glow` all behave identically」与参数归属表（容器行）加
`innerGlow` / `innerGlowColor`。

`reference/decor.md`：**不改**（Decor 不支持，lint 会报）。C# skill 不改（无公共 API 变化）。

## 8. 已定的决策（2026-08-28 与作者对齐）

1. **两个新属性 `innerGlow` / `innerGlowColor`，不复用 `glow` 加关键字。** 参考图同时要
   内外两层，一个属性表达不了（§2）。
2. **默认色 `white`。** 跟随填充色在不透明填充上不可见，是陷阱；跟随 `glowColor` 同理
   （§5.4）。
3. **source-over 合成。** 与库里所有合成一致，`/alpha` 控强度；深色即内阴影，一属性两用
   （§2）。
4. **发光带从形状边缘 d = 0 量起，描边压在上面。** Photoshop 语义；半透明细描边无缝，粗
   描边调大数值（§5.3）。
5. **衰减曲线与外发光同为 `g²`。** 内外等宽时是一整圈对称光晕（§5.1）。
6. **不进 v1**：`<Decor>`、内层 `fillInnerGlow` 等、逐状态 `hoverInnerGlow`、`<Animation>`
   开口、choke / spread 旋钮、方向性 bevel（§2 / §9）。
7. **weld：容器持有，成员写了报 `PUI-GLASS-WELD-PARAM-PLACEMENT`。** 同描边（§5.8）。

## 9. 开放问题 / 扩展位

- **外发光抽 helper 是否同 PR**（§6.2）：倾向做，以既有 render 基线为准绳；实现期定。
- **`Editor/XsdGenerator.cs` 的 Btn 手写清单今天就缺 `radius` / `glow` 等整组程序化属性**
  （只有 Frame 那份清单是全的）—— 既有缺口，不在本特性范围，记一笔。
- 扩展位（不做，留名）：`innerGlowChoke`（Photoshop 的 choke%）；方向性内发光 = bevel /
  emboss（复用 `lightAngle` 语义，边缘光强按 `dot(n, lightDir)` 调制 —— `PuguiPanelNormal`
  已经有了）；`sheen` 斜向光带（参考图的下一层）；`glowOffset`（drop shadow，procedural-style
  spec §7 已记）。

## 10. 里程碑拆分

| | 内容 | 依赖 |
|---|---|---|
| **M0 Red** | §11 全部测试先红：解析 / 默认色 / 可见性 / 不外扩 / 材质共享 / disabled 灰化 / lint 三处名字表 / weld 归属 / XSD / render 断言 | 无 |
| **M1 实现** | cginc helper + 三 shader + `PanelParams` + `ProceduralPanel` + `GlassGroupPanel` + 两处 `[UIAttr]` + 三处 lint 名字表 + XSD + SKILL（§7） | M0 |

一个 PR（分支 `feat/inner-glow`）。改动面全是「顺着 `glow` 的管线各加一份」，拆开没有收益。
验证：EditMode / EditorOnly / PlayMode 全绿 + `dotnet format --verify-no-changes` + UIXmlLint
跑 `Runtime/Resources/` 无新 issue。

## 11. 测试（Red 先行）

**`FrameProceduralPanelTests`**（材质参数观测走 `CurrentParams`，与 `Glow*` 测试同型）

- `InnerGlow_ParsesPixels`：`innerGlow='12'` → `InnerGlowSize == 12`。
- `InnerGlow_Empty_ResetsToZero`；`InnerGlow_Negative_Rejected`；`InnerGlow_NaN_Rejected`。
- `InnerGlowColor_DefaultsToWhite`：`color='#ff0000' innerGlow='8'` → `InnerGlowColor == white`
  （**不是**填充色 —— 断言消息写明陷阱）。
- `InnerGlowColor_ExplicitWins`；`InnerGlowColor_RejectsGradient`。
- `InnerGlowOnly_IsVisible`：无 fill 无 border，`innerGlow='8'` → `IsPanelVisible`。
- `InnerGlow_DoesNotInflateMesh`：`innerGlow='20'` 的 mesh 包围盒 == 布局 rect（对照
  `Glow_InflatesMeshByGlowRadius`）。
- `InnerGlow_ChangeDoesNotDirtyVertices`：改 `innerGlow` 不触发顶点重建（走既有的
  vertices-dirty 观测方式，若无则用 `BuildMeshForTests` 前后顶点相等代替）。
- `Disabled_DesaturatesInnerGlowColor`。
- `SameInnerGlow_SharesOneMaterial` / `DifferentInnerGlow_SplitsMaterial`
  （`ProceduralMaterialCache.LiveMaterialCount`）。

**`ProceduralSurfaceContractTests`**：`AnyPanelAttachingAttr_AttachesASurface` 的用例表加
`innerGlow='8'` 与 `innerGlowColor='#fff'` 两条（Btn 上写了就进程序化模式）。

**`FrameGlassPanelTests` / `GlassWeldGroupTests`**：玻璃面参数带 `InnerGlow*`；weld 组材质
拿到容器的值、成员的值不进组材质。

**Lint**

- `ProceduralAttrNamesTests`：镜像测试自动覆盖（名字必须是 `<Frame>` 的真实属性）。
- `GlassRulesTests.BadValues_AreFlagged` 的用例表加 `innerGlow`（`PUI-PROCEDURAL-VALUE` 今天在
  那个类里测）；`BadValuesInAStyle_AreFlaggedWhereTheyAreWritten` 同型加一条 `<Style innerGlow='abc'>`。
- `PureContainerVisualAttrRulesTests`：`<VStack innerGlow=>` 与 `<Decor innerGlow=>` 都报。
- `GlassRulesStyleAwareTests`：成员上的 `innerGlow` → `PUI-GLASS-WELD-PARAM-PLACEMENT`。
- `ThemeStyleRulesTests`：一个主题写整套（含 `innerGlow`）、另一主题不写 → 豁免、不报。

**XSD**（`XsdGeneratorTests`，substring）：Frame 含 `innerGlow` 与 `innerGlowColor`。

**Render**（`ProceduralSurfaceRenderTests` 的 RT 读像素套路）

- 纯色填充 + `innerGlow`：边缘内 2px 处的亮度 > 中心亮度；形状外 1px 处与无内发光时**逐像素
  相等**（不外扩）。
- `+ borderWidth='4' borderColor='#000'`：描边区读到黑（描边在上）。
- `cut 16` 与 `hexagon` 各一例：斜边内侧同样变亮（形状跟随）。
- 既有 round-only 基线（`CornerTreatmentRenderTests`）在 `innerGlow` 缺省时逐像素不变 ——
  这条同时守住 §6.2 外发光抽 helper 的重构。

## 12. 实施记录

**验证结果**：EditMode 2680 / EditorOnly 309 / PlayMode 171 全通过；
`dotnet format --verify-no-changes --severity warn` exit 0；
`UIXmlLint Runtime/Resources/` no issues across 8 files。

### 12.1 §6.2 的外发光重构：做了，而且是零风险的

spec 把「外发光是否一并抽成 helper」列为开放问题，担心逐像素漂移。实际抽出来之后
`PuguiApplyOuterGlow` 的指令序列与原来那四行**逐字相同**（只是把 `_GlowSize` / `_GlowColor`
换成形参），三个 shader 的既有 render 基线全部照常通过 —— 包括
`CornerTreatmentRenderTests` 那组「round-only 逐像素不变」的守卫。三份复制就此消掉。

内外两层现在是 cginc 里紧挨着的一对函数，镜像关系一眼可见 —— 这正是当初要求「同一条曲线」
的那个约束能长期活下去的形式。

### 12.2 一个 spec 没写、但实现时必须决定的点：可见性的 alpha 检查

`ComputeVisible` 里既有两种写法并存：border 是 `_borderWidth > 0 && _borderColor.a > 0`，
而 glow 只有 `_glowSize > 0`（不看 alpha）。内发光跟了 **border** 那一支 —— 它和 border 一样画在
形状内部，`innerGlowColor="white/0"` 是作者明确说「这一层关掉」，不该因此让一个本来全透明的
面板产生一个 4 顶点的空 draw。外发光那条不一致是既有行为，不在本特性范围内动。

### 12.3 计数注释的连带修改

`ProceduralPanel` 的类注释和 `MarkDirty` 注释里写着「applying fourteen attributes at
instantiation」，`ProceduralControl` 写着「a subclass gets all thirteen for free」—— 属性 +2 之后
都得跟着改（16 / 15）。这类散在注释里的硬编码计数没有任何测试守着，只能靠改属性时顺手 grep。

### 12.4 视觉验收

按参考图配出的那颗按钮（`radius="hexagon 70"` + 金色渐变 + `innerGlow="34"` +
`borderWidth="2"` + `glow="40"`）渲染出来就是参考图主按钮的观感：亮边向内衰减到金色本体、
细亮描边、外侧柔和光晕。剩余差距与 §1 的判断一致 —— 顶部冠形凸起、斜向光带、细线纹样，
三者都在本 spec 范围之外。
