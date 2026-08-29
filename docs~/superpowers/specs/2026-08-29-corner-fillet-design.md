# 角部倒圆 `rN` —— cut / notch / hexagon 顶点的圆滑化（形状词汇第一层的补全）

> 状态：**已实现**（M0–M2 一轮做完，见 §11）。决策见 §8，2026-08-29 与作者对齐。
> 相关：`2026-08-27-corner-treatments-design.md`（`cut` / `notch` / `hexagon` 的出处，本文是它的直接续篇；
> 折叠帧、逐轴 clamp、精确外侧距离全部沿用）、`2026-08-23-procedural-style-design.md`（`radius` 语法 §2.1）、
> `2026-08-23-glass-fill-design.md`（玻璃边缘打光走解析法线 —— 倒圆后法线必须在圆弧上连续旋转）、
> `2026-08-28-inner-glow-design.md`（内发光只吃 `d`，跟随免费）、
> `2026-08-27-theme-procedural-shape-exemption-design.md`（SHAPE 规则按属性名集合，不受影响）。

## 1. 问题

#107 给了程序化表面斜切、缺口和六边形，但它们的每个顶点都是 C0 折角。底部导航的选中态玻璃页签：

```xml
<Style name="nav-tab-glass"
       anchor="top-stretch" height="104" margin="-52,4,_,4"
       glass="true" frost="0.5" depth="6" lightAngle="0"
       radius="0, 0, cut 16x99, cut 16x99"
       color="primary-light/0.45" borderWidth="1" borderColor="primary-lighter/0.6"/>
```

可见部分是个倒梯形：两条 16×52 的斜边接到平底，两个 107° 的顶点在 1px 亮边和 `depth="6"`
的边缘光下是两个硬尖；斜边与竖边在中线相接的 163° 折角也会在描边上露出一个拐点。
作者要的是「外围 cut 出来的锐角都圆滑化」—— 斜边保留，顶点换成与两边相切的圆弧。

**这不是哪个现有属性的边缘用法，今天做不出来：**

- `radius="0,0,20,20"`（圆角）是另一个形状，梯形没了。
- `weld` 会把角熔圆，但只作用于玻璃组、且明确把 cut 退化成圆角（`PUI-WELD-CORNER`）。
- `glow` / `innerGlow` 是光，不改几何。
- 贴图：丢掉主题换形状，corner spec §1 的同一条论证。

**业内没有现成的声明式原语。** CSS `corner-shape`（Chrome 139+）的 `bevel`、Material 的
`CornerFamily.CUT`、Flutter 的 `BeveledRectangleBorder` 都是一等的切角，也都是锐角 —— 锐角就是
行业默认。「切角再倒圆」是 CAD（chamfer → fillet，两个正交参数）与 SDF（iq 的 `opRound`）层面的
概念。本库恰好是 SDF 架构，可以原生提供，而且 #107 把 cut 的外侧距离做精确（corner spec §11.2）
正是它的前提。

**分层位置**：仍是形状词汇的第一层（角部处理）。它不引入新形状，只给已有的三种处理方式
加一个正交参数。

## 2. 否决的方案

**CSS `superellipse(k)` 在 bevel 与 round 之间取中间值。否决。** 那是**一条**连续曲线：中段
也是弯的、没有直线段，不是「斜边保留 + 顶点倒圆」；并且超椭圆没有解析 SDF ——
corner spec §8 决策 2 否决 squircle 的同一条理由。

**smooth-max（多项式 smin）把半平面交熔圆。否决。** 便宜、不需要几何，但不是真距离场：
k 不是像素半径、圆弧不是真圆，`borderWidth` / `glow` 的像素语义在圆弧附近失真。weld 用它
是因为融合本身就接受近似（corner spec §5）；单面板不接受。

**独立属性 `fillet=`。否决。** 五个 `radius` 属性（`radius` / `fillRadius` / `handleRadius` /
`frameRadius` / `maskRadius`）共享值语法，扩展值语法一次全拿到；独立属性 ×5 加并存规则，
corner spec §2 的同一条论证。

**凸 / 凹分开给半径（`notch 12 r4/2`）。不进 v1。** 一个 r 的心智模型是「这个角造出来的顶点
都不比 r 尖」；分开控制在需求出现前是语法面积。

**关键字 `round` / `fillet`。否决（作者选择，§8 决策 1）。** `cut 16x99 r8` 最短；`r` 与
`WxH` 一样是尺寸段的一部分而不是新的处理方式关键字，读起来不会和 `cut` / `notch` 平级。

## 3. 方案总览

给逐角段和 `hexagon` 加一个可选的尾段 `rN`：

```xml
<Style name="nav-tab-glass" radius="0, 0, cut 16x99 r8, cut 16x99 r8" .../>   <!-- 本文的动机 -->
<Btn radius="cut 16 r6">开始匹配</Btn>                       <!-- 四角斜切，六个顶点倒圆 -->
<Btn radius="hexagon 40 r6">开始匹配</Btn>                   <!-- 圆滑尖端的六边形 -->
<Frame radius="notch 12 r4, 0, 0, notch 12 r4"/>             <!-- 软缺口：缺口口沿与内角都圆 -->
<Btn radius="cut 16 r30">                                    <!-- ≡ radius="30"，见 §5.2 -->
```

- 每个顶点变成与两边**相切**的 r 圆弧；直边留在原位；border / 内外 glow / 玻璃折射与打光 /
  `mask="self"` 全部自动跟随 —— 它们只吃 `d`。
- 零新属性，五个 `radius` 属性一次获得；纯材质参数，Variant / 主题切换只换材质。
- `r` 从 0 到大是一条连续谱：`cut W rN` 在 r 超过斜边容量后**逐字等于**普通圆角 r。

## 4. 语法

```
radius      := "" | whole-shape | corner-list
whole-shape := "pill" | "hexagon" [ SP number ] [ SP fillet ]
corner-list := segment | segment "," segment "," segment "," segment
segment     := number                              // round，不变；0 = 方角
             | keyword SP size [ SP fillet ]
keyword     := "cut" | "notch"
size        := number [ "x" number ]
fillet      := "r" number                          // 粘连：r8；画布单位
```

- `rN` 只能跟在 `cut` / `notch` 段或 `hexagon` 之后。裸数字本身就是圆角、`pill` 全是圆弧，
  给它们写 `rN` 是 parse error（§4.1）。
- `r` 小写、与数字粘连、无空格 —— 与 `WxH` 的 `x` 同一条规则（corner spec §4）。
  `cut 16 r 8` 报的是专门的错（"write r8"），不是泛泛的"too many parts"。
- `hexagon r6`（自动尖端宽度 + 倒圆）合法；`hexagon 40 r6` 合法；顺序固定，`hexagon r6 40`
  是 error。
- 数值检查（负数 / NaN / Infinity）逐字沿用 `TryParseNumber`。
- 空串 = `Zero` 照旧。

### 4.1 解析错误（parse-time，纯 C# 子集，CLI 同步可见）

| 情形 | 结果 |
|---|---|
| `16 r4` | error：a round corner takes no fillet —— 裸数字已经是圆角，把 r 去掉或把它写成半径 |
| `pill r4` | error：`pill` takes no size or fillet（沿用 `pill 32` 的措辞） |
| `cut 16 r` / `cut 16 r-2` / `cut 16 rx` / `cut 16 r4x2` | error：fillet must be `r` followed by a finite non-negative number，指明角与段 |
| `cut 16 r 8` | error：write `r8` — no space between `r` and the number |
| `cut 16 R8` / `cut 16 fillet 8` / `cut 16 round 8` | error：unknown trailing part，列出唯一合法形态 `rN` |
| `cut 16 r4 5` / `cut 16 r4 r5` | error：too many parts（沿用） |
| `cut r4` | error：needs a size（沿用 `cut` 缺尺寸的措辞；`r4` 不是尺寸） |
| `hexagon r6 40` / `hexagon 40 r6 7` | error：`hexagon` takes at most a size then a fillet |

## 5. 语义

### 5.1 几何定义：形态学 opening

倒圆 = 先把形状向内**收缩** r，再向外**膨胀** r（`opRound`：`sd(shrunk) − r`）。在 SDF 上这是
两步精确操作：收缩后的多边形仍是多边形（每条边平行内移 r），膨胀等于距离场减 r。结果：

- 每个**凸顶点**变成以收缩后顶点为圆心、半径 r 的圆弧，与相邻两边相切；
- 直边回到原位（收了又放）；
- 圆弧的相切点落在顶点两侧的直边上 —— **倒圆会向相邻直边各吃进一段**。切角
  `cut WxH` 的两个顶点：沿水平边多吃 `r·(L−W)/H`，沿竖直边多吃 `r·(L−H)/W`
  （`L = √(W²+H²)`；45° 时各 0.414r）。这是 fillet 的本性，SKILL 要写明。

收缩后的切角斜边平行内移 r，伸出量按同一系数缩放：

```
k  = 1 − r·(W + H − L) / (W·H)          // ≤ 0 表示斜边被两个圆弧吃光
s' = k·s
```

### 5.2 连续谱：r 超过斜边容量退化为圆角

`k ≤ 0`（`r ≥ W·H / (W+H−L)`，`cut 16` 约 27px）时 `s' = 0`，`PuguiSdCutCorner` 退回象限场，
`− r` 之后就是 `PuguiSdRoundCorner(u, r)` —— **同一串指令，逐位相等**。所以 `cut 16 r30`
与 `radius="30"` 像素一致，作者可以把 r 当成「多圆」的连续旋钮。render test 钉住这条。

### 5.3 轮廓外延不变（决策 2）

斜边伸到半高（作者的 tab；`hexagon` 的尖端恒如此）时，收缩后的斜边顶点会越过象限中线
`r·(L−H)/W`。两种处理里选了**外延守恒**：

```
b' = b − r
m  = min(1, b'.x / s'.x, b'.y / s'.y)    // 分量为 0 的轴跳过
s' = m·s'
```

按比例（而非逐轴）clamp 让斜边**平行**外移到顶点恰好落在中线上：斜率不变、圆弧仍与两边
精确相切、每个圆弧完整落在自己的象限里 —— 折叠帧的前提（"另外三个角够不着这里"）继续成立，
中线两侧无接缝。代价是斜边比未倒圆时外移 `r·(1 − H/L)`：作者的 16×52 在 r=8 时 0.35px，
45° 尖端 0.29r。收益是 `hexagon 40 r6` 的圆尖仍顶到 rect 左右边 —— 布局对齐与未倒圆时一致。

对照：CAD 真 fillet 斜边原地不动、圆尖向内缩 `r·(L−H)/H`（45° 尖端 r=6 缩 2.5px；
`hexagon 60` 配 40px 高的扁尖端 r=8 缩 17px），形状不再填满 rect；且非对称接缝（斜边接竖边）
除非 shader 跨象限感知，否则有 ≤ 0.29r 的鼓包。不选。

逐轴 clamp（今天 `cut` 用的那种）在这里不适用：它改斜率不改位置，`hexagon 60` 配 40px 高
在 r=8 时尖角会从 37° 收到 25°，肉眼可见。**基础尺寸的逐轴 clamp 保留**（作者靠 `cut 16x99` 钳到 16×52，
那是他要的语义），比例 clamp 只作用于收缩后的尺寸。

### 5.4 `notch`：口沿两个凸顶点 + 内角一个凹顶点，同一个 r（决策 3）

opening 只圆凸顶点（膨胀把收缩出来的凹弧又缩回尖角）。凹角走另一条路：**缺口本身换成
内角倒圆 r 的圆角矩形**。收缩帧里缺口仍是 W×H（两壁与盒子各内移 r，相对位置不变），
凹弧半径放大为 `2r`（凹弧被收缩放大、被膨胀缩回）：

```
rc    = 2r                                           // r 已 clamp 到 min(W,H)/2，所以 rc ≤ min(W,H)
t     = u' + s                                       // 以缺口内角为原点
dBite = PuguiSdRoundCorner(−t, rc)                   // 无限象限 {t ≥ 0}，原点倒圆 rc —— 这就是缺口腔
dBox  = PuguiSdQuadrant(u')
d'    = dBox < 0 ? max(dBox, −dBite)                                          // 盒内：材料 / 腔内，两者精确
                 : min(PuguiSdQuadrant(u' + (W,0)), PuguiSdQuadrant(u' + (0,H)))   // 盒外：今天的并集式，精确
d     = d' − r
```

精确性论证与 corner spec §11.2 同型：盒内 `max` 的两个场各自精确、相减处两条壁与凹弧就是
真边界；盒外沿用已验证的双象限并集（口沿两个凸顶点）。凹弧只朝腔内，盒外的点永远更靠近
口沿顶点，不需要它参与。

**r 在 notch 上 clamp 到 `min(W,H)/2`。** 到顶时两壁恰好被口沿圆弧吃光、凹弧半径仍是 r，
轮廓是「凸弧 – 凹弧 – 凸弧」的光滑 S 形。不这么 clamp 的话再往上走两段凸弧会在腔底相接成
一个尖 cusp —— 比静默钳住难看得多。（对照 `cut`：不钳、退化成圆角，因为那条退化路径是光滑的。）

### 5.5 clamp 汇总（shader 逐像素，与 pill / hexagon 同理）

| | 规则 |
|---|---|
| 基础尺寸 | 逐轴 clamp 到 `b`（不变） |
| `r` | `≤ min(b.x, b.y)`；notch 另 `≤ min(W,H)/2`；尺寸为 0 的处理方式忽略 r（没有顶点可倒，与 `CornerSpec.IsSquare` 一致） |
| cut 收缩后尺寸 | `k·s`，`k` clamp ≥ 0；再按比例 clamp 进 `b − r`（§5.3） |
| notch 收缩后缺口 | 逐轴 clamp 到 `b − r`（口沿圆弧不越中线；缺口让位） |

### 5.6 派生特性逐条

- **border**：向内描，沿 `d`；圆弧处宽度不变（`d` 精确）。
- **glow / innerGlow**：只吃 `d`，跟随。
- **glass**：折射与边缘光走 `PuguiPanelNormal`。圆弧带（收缩形状外侧、`r` 以内）的法线必须是
  「从收缩后顶点指向片元」的方向 —— 连续旋转，而不是最近半平面的法线在圆弧中间硬切换。
  这是本设计唯一需要真写代码的地方（§6.2）。
- **`mask="self"` / `maskRadius`**：遮罩 = SDF 实心区，跟随。
- **纵向渐变**：只看包围盒，不变。
- **weld**：不变。`GlassGroupPanel.ResolveRadius` 已把 cut / notch / hexagon 退化成同 W 圆角，
  fillet 随之丢弃；`PUI-WELD-CORNER` 的措辞已覆盖（"a cut / notch / hexagon radius on a welded
  block"），不加新规则。
- **`round` 角**：语法上不允许 `rN`，shader 路径不碰。

### 5.7 材质共享

`r` 是材质参数（`_CornerFillet` float4），同 `class=` 的面板仍共享一份材质。收缩 / 比例
clamp 依赖 rect 尺寸，与 pill / hexagon 一样在 GPU 逐像素解算 —— 不在 C# 里算，理由同
`RadiusParser.cs` remarks。

## 6. 实现地图

### 6.1 数据流

| 层 | 文件 | 改动 |
|---|---|---|
| 解析 | `Runtime/Core/Parser/RadiusParser.cs` | 段语法加 `[SP fillet]`（`TryParseCorner` 的 3-token 形态；`TryParseWholeShape` 的 hexagon 分支）；`CornerSpec.Fillet`；§4.1 错误。纯 C# 子集不变，CLI lint 免费获得 |
| 控件 | `ProceduralControl` / `Frame` / `Slider` / `Progress` | **零改动**（`Parse → SetRadius(RadiusSpec)` 透传） |
| 材质键 | `ProceduralMaterialCache.cs` `PanelParams` | 加 `CornerFillet`（Vector4，CSS 序），进相等与哈希；`Apply` 多一行 `SetVector(_CornerFillet)` |
| shader | `UI-PanelSDF.cginc` + `UI-ProceduralPanel.shader` / `UI-GlassPanel.shader` | 属性 `_CornerFillet`；`PuguiCorner` 加 `fillet`；`PuguiResolveCorner` 做 §5.3 / §5.5 的收缩与 clamp；`PuguiSdPanel` / `PuguiPanelNormal` 的 cut / notch 分支走收缩帧（§6.2）。`UI-GlassGroup.shader` 不动 |
| weld | `GlassGroupPanel.ResolveRadius` | 不动 |
| lint | `Runtime/Core/Lint/` | 无新规则；语法错误走 parser |
| XSD | `Editor/XsdGenerator.cs` | `radius` 是无 pattern 的 `xs:string`，无改动 |
| SHAPE 规则 | `ThemeStyleRules` | 属性名集合没变，无改动 —— 守卫测试确认 |

### 6.2 shader

`PuguiResolveCorner` 在现有的哨兵解算与逐轴 clamp 之后追加：

```hlsl
float r = c.fillet;                                   // 本角的 rN，round 角恒为 0
r = min(r, min(b.x, b.y));
if (PUGUI_KIND_IS_NOTCH(c.kind)) r = min(r, 0.5 * min(c.size.x, c.size.y));
if (r > 0.0 && !PUGUI_KIND_IS_ROUND(c.kind))
{
    float2 bp = b - r;
    if (PUGUI_KIND_IS_NOTCH(c.kind))
        c.size = min(c.size, bp);                                        // §5.5 缺口让位
    else
    {
        float2 s = c.size;
        float L = length(s);
        float k = (min(s.x, s.y) > 0.0) ? 1.0 - r * (s.x + s.y - L) / (s.x * s.y) : 0.0;
        s *= max(k, 0.0);                                                // §5.1
        float m = 1.0;
        if (s.x > 0.0) m = min(m, bp.x / s.x);
        if (s.y > 0.0) m = min(m, bp.y / s.y);
        c.size = s * m;                                                  // §5.3
    }
}
c.fillet = r;
```

`PuguiSdPanel`：`u' = abs(p) − b + r`（round 时 r = 0，逐字是今天的 `u`）；
cut → `PuguiSdCutCorner(u', c.size) − r`；notch → §5.4 的 `PuguiSdNotchCornerR(u', c.size, 2r) − r`。

**`r == 0` 走今天的函数原样。** cut 的 `u + 0` / `s × 1` 是逐位恒等，但 notch 的新形态
（§5.4 按 `dBox` 符号分流）与旧形态（按并集式符号分流）只是数学等价，不保证逐位相等 ——
所以 notch 在 `r == 0` 时保留旧函数，新函数只在 `r > 0` 进入。这是 uniform 分支，全体片元同路。
round-only / 未倒圆的 cut / notch 逐像素不变，用 corner spec §11.1 的方法量出来（0 差异）。

**法线**（`PuguiSdCutNormal` / `PuguiSdNotchNormal` 的收缩帧版本）：镜像距离函数的分支 ——
外侧按「谁赢了距离」取法线：斜边线段赢 → `normalize(w − e·t)`（线段内部是斜边法线，两端楔形区
自然变成指向顶点的方向，这就是圆弧上的连续旋转）；顶边赢 → `(0,1)`；竖边赢 → `(1,0)`。
内侧沿用今天的最近半平面逻辑（打光带在内侧的部分离顶点远，选谁都照不到）。notch 盒内
`max(dBox, −dBite)` 的梯度：`dBox` 赢 → 象限法线；否则 `PuguiSdQuadrantNormal(−t + rc)`
（方向朝腔内，即材料的外法线）。

**成本**：新增分支全是 uniform 决定路径；一次 `length` + 几次乘除。不写 `rN` 的面板只多读一个
float4。几何、材质共享、合批不变。

### 6.3 `RadiusSpec` / `CornerSpec` 结构扩展（兼容性）

- `CornerSpec` 加只读 `Fillet`（float，默认 0）；新增四参构造 `(kind, width, height, fillet)`，
  三参构造保留（`Fillet = 0`）。`Round(r)` / `Square` 不变。
- `RadiusSpec.Hexagon(hexWidth, fillet)`：四个角写成 `CornerSpec(Round, 0, 0, fillet)` 携带 r ——
  hexagon 的 kind / size 本来就由 shader 哨兵覆盖，`Fillet` 是唯一从 C# 流到 GPU 的量。
  `IsSquare` / `IsZero` 语义不变（`Shape != None` 已排除）。
- `PanelParams.CornerFillet` 从四个角的 `Fillet` 打包。

## 7. SKILL 更新（同一 PR 内）

- `authoring-promptugui-xml/SKILL.md`
  - `radius` 属性行：值语法追加 `[rN]`（`cut W[xH] [rN]` / `notch W[xH] [rN]` / `hexagon [W] [rN]`）。
  - **Corner treatments** 小节：表格加 `cut 16 r6` / `notch 12 r4` / `hexagon 40 r6` 三行；
    bullets 补四句 —— 圆弧与两边相切并向直边各吃进一段（45° 约 0.41r）；r 超过斜边容量退化为
    普通圆角（`cut 16 r30` ≡ `30`）；倒圆不改变轮廓外延（圆尖仍顶到 rect 边）；notch 的
    r 上限是 `min(W,H)/2`。
  - 示例：梯形页签改成 `<Tab radius="cut 14 r4, cut 14 r4, 0, 0">`，六边形主按钮加 `r6`。
  - 错误措辞：`16 r4` / `pill r4` / `cut 16 r 8` 三条进 XML parse errors 列表。
- `reference/glass.md`：weld 小节 `PUI-WELD-CORNER` 一句不用改（措辞已覆盖），确认即可。
- C# SKILL 不动（`CornerSpec` 扩展兼容，控件作者 API 无感知）。

## 8. 已定的决策（2026-08-29 与作者对齐）

1. **关键字用 `r` 短形式，粘连数字：`cut 16x99 r8`。** 候选 `round 8` / `fillet 8` 否决 ——
   `r` 是尺寸段的延续而不是与 `cut` 平级的处理方式；粘连与 `WxH` 的 `x` 同一条规则，
   `cut 16 r 8` 给专门的错误信息而不是静默容错。
2. **轮廓外延不变（§5.3）。** 斜边平行外移 `r·(1−H/L)` 换取圆尖仍顶到 rect 边、逐象限精确、
   中线无接缝；否决 CAD 真 fillet（尖端内缩、形状不填满 rect、非对称接缝需跨象限感知）。
3. **notch 的凹角一起圆，同一个 r（§5.4）。** 心智模型「这个角造出来的顶点都不比 r 尖」；
   凸凹分开给值不进 v1。
4. 沿用的既定规则（不另议）：`round` / `pill` 不收 `rN`（parse error）；数值检查逐字沿用；
   weld 退化不变；r 在 shader 逐像素 clamp 保材质共享。

## 9. 开放问题（实现期已结）

- ~~法线 render 探针能否稳定~~ —— 稳。玻璃 `cut 60 r40`、`lightAngle=0`、`noise=0`、灰底
  （避免通道裁顶），左上角圆弧带中点的亮度严格落在顶边与斜边之间，一次通过
  （`CornerFilletGlassRenderTests.EdgeHighlight_SweepsRoundTheFilletArc`）。
- **`hexagon` 在正方形 rect 上退化成菱形时 `rN` 的观感** —— 未单独验，几何上就是圆角菱形；
  需求出现时看一眼。

## 10. 里程碑

| | 内容 | 依赖 |
|---|---|---|
| **M0 Red** | parser 矩阵（§4 + §4.1 逐条，扩 `CornerTreatmentParserTests`）；`CornerSpec` 兼容契约（扩 `RadiusSpecCompatTests`）；`PanelParams` 相等 / 哈希 / 材质共享与区分；render 基线：round-only 与未倒圆 cut / notch 像素快照 | 无 |
| **M1 cut + hexagon + notch** | parser / `CornerSpec` / `PanelParams` / cginc（收缩、比例 clamp、notch 圆角腔、法线）/ 三 shader 属性；render 探针（§10.1）；SKILL 同 PR | M0 |
| **M2 收尾** | `dotnet format` / `UIXmlLint Runtime/Resources/` / SHAPE 守卫确认 / §11 实施记录 | M1 |

一轮做完不拆 PR，理由同 corner spec §10。

### 10.1 render 探针（几何谓词，不用 golden image）

新建 `CornerFilletRenderTests`，沿用 `CornerTreatmentRenderTests` 的 `Camera.Render()` 架子与
双通道 `AssertPainted`（corner spec §11.3 坑二）：

| 探针 | 谓词 |
|---|---|
| 顶点被圆掉 | `cut 20x80 r40`（104° 顶点，矢高 ≈ 10.8px）：沿顶点角平分线向内 4px 的像素，未倒圆 painted、倒圆后透明 |
| 直边不动 | 四条边中点内 1px 仍 painted；边外 1px 仍透明 |
| 连续谱 | `cut 20 r60` 与 `radius="60"` 逐像素 0 差异 |
| 外延守恒 | `hexagon r30`：尖端 `(b.x−2, 0)` 倒圆前后都 painted；`(b.x−4, 8)` 未倒圆透明（45° 尖端在该高度退到 b.x−8）、倒圆后 painted（圆尖轮廓在 b.x−1.1，探针在内 2.9px） |
| notch 凹角 | `notch 40 r16`：内角对角线上 `(−37,−37)`（腔内、离圆弧 2.4px）未倒圆透明、倒圆后 painted |
| notch 口沿 | `notch 40 r16`：`(−44,−2)` 未倒圆 painted（壁左侧）、倒圆后透明（口沿圆弧内） |
| 描边跟随圆弧 | `cut 20x80 r40 borderWidth=8`：圆弧中点深 4px 是描边色、深 12px 是填充色 |
| 玻璃法线 | §9 第一条 |
| 零回归 | M0 基线快照与 M1 之后 0 差异 |

## 11. 实施记录

**验证结果**：EditMode 2730 / EditorOnly 309 / PlayMode 171 全绿；
`dotnet format --verify-no-changes` 无改动；`UIXmlLint Runtime/Resources/` 零 issue。

### 11.1 零回归是量出来的，不是断言出来的

`CornerFilletRenderTests.Baseline_LogsPixelHashes_ForZeroRegressionMeasurement` 渲染十个**不带 `rN`**
的形状（round / cut / cut WxH / notch / hexagon / hexagon W / pill / 带描边的 cut 与 notch /
混合角 + 双 glow），对像素做 FNV-1a 后打日志。shader 改动前跑一遍、改动后再跑一遍，
**十个哈希逐一相同**。这比 corner spec §11.1 的手工 stash 便宜：测试本身就是量具，以后每次动
cginc 都能重跑。

### 11.2 连续谱真的逐位相等

`cut 20 r60` 与 `radius="60"` 的整张 256×256 渲染 **0 个像素不同**
（`Fillet_BeyondTheChamfersCapacity_IsExactlyARoundCorner`）。靠的是 `k ≤ 0 → s' = 0` 之后
`PuguiSdCutCorner` 退回 `PuguiSdQuadrant(u')`，再 `− r` —— 与 `PuguiSdRoundCorner` 是同一串指令。
没有为此写任何特判。

### 11.3 玻璃法线一次过

§9 担心的探针没抖：灰底 + `noise=0` 下，圆弧带中点亮度严格落在顶边与斜边之间。新的
`PuguiSdCutNormal` 外侧分支镜像距离函数的三个候选（斜边线段 / 顶边 / 竖边），谁的距离赢取谁的
方向；线段两端楔形区自然给出「从顶点指向片元」的旋转法线。内侧分支逐字未动。

### 11.4 一个把测试变成假消息的坑（自己踩的）

`Mathf.Lerp` 会把 t 钳到 [0, 1]。「边缘外 2px 必须是背景」的探针写成 `At(−2/W, …)`，t 被钳成 0，
采到的是边缘像素本身 —— 于是这条探针在形状真的越界时也报绿。第一轮它报的是**红**（边缘像素是
实心），才顺藤摸到这个问题；换成 `LerpUnclamped` 后红变绿。逆向的教训与 corner spec §11.3 同型：
探针要先证明自己能红。

### 11.5 实现上的小结论

- **控件层零改动**成立：五个 `radius` 属性全是 `Parse → SetRadius(RadiusSpec)` 透传。
- `hexagon` 的 fillet 走四个角的 `CornerSpec.Fillet`（kind / size 被哨兵覆盖，fillet 是唯一要过 GPU
  的量），`PanelParams` 不需要新的整形字段。
- `r == 0` 在 `PuguiSdPanel` 里是独立分支、走旧函数原样；notch 的圆角腔形态（按 `dBox` 符号分流）
  与旧式只是数学等价，这个分支就是 §11.1 那十个哈希不变的直接来源。
- `PuguiResolveCorner` 里对尺寸为 0 的处理方式把 r 清零，与 `CornerSpec.IsSquare` 的语义对齐 ——
  `cut 0 r4` 是方角，不是圆角 4。
