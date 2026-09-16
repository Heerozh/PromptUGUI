# 渐变原语补全：方向 / 多色标 / 全部程序化色槽 —— 按 CSS `linear-gradient` 对齐

> 状态：**已对齐，待实施**（2026-09-17）。决策编号 `LG-Dn`（§15）。
> 相关：`2026-06-13-gradient-color-design.md`（逗号双色语法的出处；其 §10 留的「方向 / ≥3 色标」
> 扩展位由本文一次填平）、`2026-08-30-gradient-stop-position-design.md`（色标位置 + 提示；
> `PuguiFillRamp` 与 `ColorSpec.Evaluate` 的出处）、`2026-09-01-vertex-gradient-stops-design.md`
> （VGS：顶点路径按色标切网格；本文把「水平切线」泛化成「垂直于渐变方向的切线」）、
> `2026-08-26-procedural-surface-design.md`（`ProceduralSurface` / `ProceduralPanel` / 材质缓存 key）、
> `2026-08-27-decor-primitives-design.md`（`<Decor>` 的第三份 fill shader）、
> `2026-09-12-intensity-design.md`（曝光曲线在合成链里的位置，本文不动）。

## 1. 问题

参考图（星海指挥官行星面板）的**边框**不是一种颜色：左上角与右下角亮青、右上与左下暗青，
沿对角线过去是 **亮 → 暗 → 亮** 三段；两个按钮的描边同样是 **左右两端亮、中段暗**。把它拆开，
是三件今天做不到的事叠在一起：

| 缺口 | 今天 | 需要 |
|---|---|---|
| 色槽 | `borderColor` / `glowColor` / `innerGlowColor` **纯色 only**，渐变值 = 错误 | 描边 / 发光吃渐变 |
| 方向 | 渐变只能从上到下 | 对角、横向 |
| 色标数 | 恰好两色（+ 一个提示） | 三色（亮暗亮） |

而且「对角」在这里有个陷阱：面板是宽的（约 2:1），写 `135deg` 得到的 50% 线**不经过**另外
两个角 —— 右上角和左下角一个偏亮一个偏暗，左上 / 右下的高亮也不对称。CSS 为此定了
`to bottom right` 的「魔法角」语义：方向按宽高比算，保证四个角分别落在 0% / 50% / 50% / 100%。
参考图要的正是这个。

三个缺口彼此独立，但都是**同一套色值语法**的扩展。分三次做，作者会面对三份「这个属性支持哪一
半渐变语法」的例外表；一次做完，语法就是一条：**凡是接受渐变的地方，接受完整的渐变**。

## 2. 否决的方案

| 写法 / 做法 | 否决理由 |
|---|---|
| CSS 全写法 `linear-gradient(135deg, A, B, C)` | gradient spec §2 已否决：太长，逗号语法已确立，包一层函数是纯噪音。本文只把括号里的**内容**照搬 |
| 新属性 `gradientAngle=` / `borderGradient=` | gradient spec §1「不加属性」：`color` / `hoverColor` / `borderColor` / `glowColor` … 每个都得配一份平行属性，Variant 覆写要成对写 |
| 边框沿**周长**渐变（0% = 左上角起点顺时针一圈） | 能表达「角亮边暗」，但与填充语义不同、同一个 token 不能在 `color` 与 `borderColor` 间互用、提示（hint）在环上没有定义；且 SDF 没有现成的周长参数化，切角 / 缺口下更难。「角亮」由方向 + 三色标覆盖（§13 验收） |
| 自定义角度约定（0deg = 向下 = 今天的默认） | 本文的全部价值在于「LLM 作者已经会 CSS」。CSS 的 0deg = 向上、90deg = 向右、180deg = 向下；我们的默认恰好就是 CSS 的默认（`to bottom` = 180deg），照搬零成本 |
| `to <corner>` = 固定 45° 别名 | 实现简单，但不解决 §1 的宽面板问题，且与 CSS 语义不同 —— 作者带着 CSS 直觉来，得到的是错的画面 |
| 色标无上限（1D ramp 贴图） | 需要每个渐变一张贴图，破坏「同样式共享一份材质」的合批模型，也破坏无贴图承诺。固定 4 个色标（§3.3） |
| 顶点路径本轮不做、只在程序化表面生效（2026-08-30 的「路径 A」先例） | VGS 已经把切网格做完了，泛化方向只是把 `y` 比较换成点积；再留一张例外表得不偿失（LG-D3） |

## 3. 语法

### 3.1 文法

```
value      := colour                              -- 纯色
            | [direction ","] stop ("," [hint ","] stop){1,3}   -- 渐变：2..4 个色标，色标之间可夹一个提示
direction  := <number>deg                         -- CSS 角度：0deg 向上、90deg 向右、180deg 向下（默认）、270deg 向左
            | "to" side                           -- to top / to bottom / to left / to right
            | "to" side side                      -- to top left / to bottom right …（一纵一横，顺序任意）
stop       := colour [" " percent]                -- 色标：颜色 + 可选位置
hint       := percent                             -- 提示：裸百分比，夹在两个色标之间
colour     := (token | "#"hex | css-name) ["/" alpha]
```

段以逗号切开、逐段 trim。**段的分类零歧义**：

| 段的形状 | 分类 | 为什么不会撞 |
|---|---|---|
| `^-?\d+(\.\d+)?deg$`（不区分大小写） | 方向 | hex 以 `#` 开头、CSS 命名色纯字母；token 虽然合法地可以叫 `45deg`，但**首段先按形状判**，这样的 token 从渐变首段不可达 —— 解析器同时拒绝 `<Color name="45deg">`（§8），把坑填掉 |
| `^to\s+…$` | 方向 | 含空白，今天已经不是合法的颜色段 |
| `^\d+(\.\d+)?%$` | 提示 | 没有颜色写法以 `%` 结尾（2026-08-30 §14 同一论证） |
| 其它 | 色标 | 段内再按空白切出位置，再剥 `/alpha`（既有顺序） |

方向只允许在**首段**；提示不能在首尾、不能相邻。

### 3.2 示例

```xml
<!-- 今天的全部写法原样有效：不写方向 = to bottom -->
<Frame color="primary 70%, complement"/>
<Frame color="primary, 70%, complement"/>

<!-- 方向 -->
<Frame color="to right, accent, accent-dark"/>
<Frame color="45deg, #ffe08a, #b8860b"/>

<!-- 三色：参考图的对角高亮边框（§13）-->
<Frame borderWidth="1.5"
       borderColor="to bottom right, hud-cyan, hud-cyan/0.3 50%, hud-cyan"/>

<!-- 四色 + 位置 + 提示混用，提示只弯它所在的那一段 -->
<Frame color="to right, red, 30%, orange 40%, yellow 60%, green"/>

<!-- 定义处也吃方向和形状 —— 主题换的是整条渐变线 -->
<Theme name="default">
  <Color name="edge-lit" value="to bottom right, #7ef3ff, #1c6d8a80 50%, #7ef3ff"/>
</Theme>
<Frame borderColor="edge-lit"/>
<Btn   borderColor="edge-lit/0.5"/>        <!-- /alpha 换掉每一个色标的 alpha，形状和方向不动 -->
```

### 3.3 位置的默认与约束（照 CSS）

- 第一个色标没写位置 → `0%`；最后一个没写 → `100%`。
- 中间的色标没写位置 → 在**左右最近的两个已定位色标之间等距摊开**（CSS 同规则）。
  `A, B, C` = `A 0%, B 50%, C 100%`；`A, B, C 40%, D` = `A 0%, B 20%, C 40%, D 100%`。
- 位置**不得递减**（`a > b` 报错，不像 CSS 那样静默钳制 —— 本仓一贯选报错）。相等 = 硬边，合法。
- 提示必须落在它两侧色标的有效位置之间（含端点则退化为硬边，沿用 `StopCurveExponent` 的钳制）。
- 色标 **2..4 个**；提示最多 3 个（每段一个）。超出报错（LG-D2）。
- 一个色标最多一个位置（CSS 的 `red 10% 30%` 双位置写法不支持，§14）。

## 4. 语义

### 4.1 渐变线（照 CSS）

设盒子宽 `w`、高 `h`、中心 `c`，局部坐标 y 向上（Unity 局部空间；shader 的 `p` 与
`GradientTint` 的顶点坐标同此）。

**方向向量**

| 写法 | `dir`（单位向量，y 向上） |
|---|---|
| `θdeg` | `(sin θ, cos θ)` —— 0deg = `(0, 1)` 向上，90deg = `(1, 0)` 向右，180deg = `(0, −1)` 向下 |
| `to top` / `to right` / `to bottom` / `to left` | 0 / 90 / 180 / 270 deg |
| `to top right` | `normalize(h, w)` |
| `to bottom right` | `normalize(h, −w)` |
| `to bottom left` | `normalize(−h, −w)` |
| `to top left` | `normalize(−h, w)` |

角的公式来自 CSS「渐变线垂直于**相邻两角**的连线」：`to bottom right` 的相邻角是右上与左下，
连线向量 `(w, h)`，垂直且指向右下象限的是 `(h, −w)`。正方形上退化为 135deg；宽盒子上角度自动
变陡，四个角分别落在 0% / 50% / 50% / 100%。**角模式的方向依赖盒子尺寸**，所以它在材质里存的是
「角的编号」而不是向量，由 shader 逐 fragment（`b` 就在手上）/ `GradientTint` 按包围盒算。

**渐变线长度与参数**

```
L = |w · dir.x| + |h · dir.y|                 -- CSS：让 0% / 100% 恰好触到盒子的两个角
s = (dot(p − c, dir) + L / 2) / L             -- 0 = 起点，1 = 终点，盒子外饱和到端色
```

默认 `dir = (0, −1)`：`L = h`，`s = (c.y − p.y + h/2) / h` = 今天 `PuguiFillRamp` 的
`(b.y − p.y) / (2 b.y)`，逐字相同 —— 既有文档在像素探针容差内零回归。

### 4.2 多色标求值

色标 `(C_i, P_i)`，`i = 0..n−1`，`n ≤ 4`；每段一个曲线指数 `E_i`（无提示 = 1，由既有
`ColorParser.StopCurveExponent(P_i, P_{i+1}, hint_i)` 逐段算）：

```
s ≤ P_0          → C_0
P_i ≤ s < P_{i+1} → u = (s − P_i) / max(P_{i+1} − P_i, 1e-4);  if E_i ≠ 1: u = u^E_i;  lerp(C_i, C_{i+1}, u)
s ≥ P_{n−1}      → C_{n−1}
```

两色时与今天的 `Evaluate` / `PuguiFillRamp` 逐字相同。这一条公式仍然是**唯一实现的三份拷贝**
（cginc 一份、`ColorSpec.Evaluate` 一份、TMP 四角求值调 `Evaluate`），2026-08-30 §7「三处逐字
一致」的原则不变。

### 4.3 「第一段是你看到的起点」

VGS §4.4 的规则原样推广：渐变在旋转 / 镜像**之后**、在屏幕空间里算。`to right` 就是你看到的
左 → 右，无论这个 `<Icon>` 写没写 `flip="x"`。`RotateFlipEffect` 仍排在 `GradientTint` 前。

### 4.4 发光区域的取值

`glowColor` 的渐变线由**布局 rect** 定（`b` 是 rect 半尺寸，发光只撑大绘制 quad），形状外侧的
发光像素 `s` 饱和到端色。作者要「光晕从青到透明」写 `glowColor="to right, cyan, cyan/0"`，
光晕左半青、右半渐隐、超出 rect 的部分保持两端色 —— 与 `<Image glow>` 今天「色标按撑大的 quad
归一化」的行为不同，程序化表面这边更直觉，两者的差异记进 SKILL。

## 5. 值模型

### 5.1 `ColorParser`（`Core/Parser/`，纯 C#，CLI 共享）

`GradientParts` 从「两段 + 一个提示」拓成：

```csharp
public readonly struct GradientParts
{
    public readonly GradientDirection Direction;   // 纯 C# 值：Kind(Angle | Corner) + AngleDeg + CornerX/CornerY 符号
    public readonly string[] Colours;              // 1..4 段的颜色文本（已剥位置）
    public readonly float?[] Stops;                // 作者写的位置；null = 未写
    public readonly float?[] Hints;                // 长度 n−1；null = 该段无提示
    public bool IsGradient => Colours.Length > 1;
    public float[] EffectiveStops();               // §3.3 的默认规则摊开后
    public float[] CurveExponents();               // 逐段 StopCurveExponent
}
```

新增 `TrySplitDirection(string seg, out GradientDirection dir, out string error)`；`TrySplitStop` /
`TrySplitAlpha` / `StopCurveExponent` 不动。旧签名 `TrySplitGradient(raw, out top, out bottom, out error)`
删除（只剩 lint 与 `ParseThemeColor` 在用，一并改到新结构）。

### 5.2 `ColorSpec`（`Application/`）

固定 4 槽的 readonly struct —— 它是材质缓存 key 的一部分，要值语义、不分配：

```csharp
internal readonly struct ColorSpec : IEquatable<ColorSpec>
{
    public readonly Color C0, C1, C2, C3;      // 色标颜色；未用槽 = 最后一个色标（读起来不必判 Count）
    public readonly float P0, P1, P2, P3;      // 色标位置（0..1，沿渐变线）
    public readonly float E0, E1, E2;          // 逐段曲线指数
    public readonly byte Count;                // 1 = 纯色
    public readonly GradientDirection Direction;

    public bool IsGradient => Count > 1;
    public Color Start => C0;                  // 取代 Top
    public Color End => …;                     // 取代 Bottom
    /// TMP 四角画不出来的形状：≥3 色、挪过的位置、提示。方向不算 —— 四角能表达任意方向的双色线性 ramp。
    public bool HasStops => IsGradient && (Count > 2 || P0 != 0f || P_last != 1f || any E != 1f);

    public Vector2 DirectionFor(Vector2 size);         // §4.1 的表；角模式在这里吃尺寸
    public float   LineLengthFor(Vector2 size, Vector2 dir);
    public Color   Evaluate(float s);                  // §4.2
    public ColorSpec Multiply(Color m);                // 逐色标；位置 / 曲线 / 方向不动
    public ColorSpec WithAlpha(float a);               // token/alpha：换掉每个色标的 a
    public ColorSpec Opaque();                         // glow 默认值用：每个色标 a = 1
    public ColorSpec Desaturate();                     // 禁用灰化：逐色标
    public bool AnyVisible => 任一色标 a > 0;
    public static ColorSpec Solid(Color c);
    public static ColorSpec Gradient(GradientParts parts, Color[] resolved);   // 唯一的渐变构造入口
}
```

`Top` / `Bottom` 改名 `Start` / `End`（有方向之后「顶」是谎言）。既有的 `Gradient(top, bottom, …)`
重载删掉：构造调用点只有 `UI.ResolveSpec`、`ParseThemeColor`、`GradientTint.Set(Color, Color)`；
读 `.Top` / `.Bottom` 的约 40 处（`StateTintReactor`、`ProceduralPanel`、`Text`、`GradientTint`…）
机械改名，语义不变。`Equals` / `GetHashCode` 按全部字段。

`GradientDirection`：`Kind`（`Angle` / `Corner`）、`AngleDeg`、角模式下的 `CornerX` / `CornerY`
（±1）。`to top` 等四个边在解析期归一化为角度；默认值 = `Angle 180`。

### 5.3 `UI.Theme`

- `ResolveSpec`：对 `parts.Colours` 逐段 `ResolveSingle(allowGradientToken: false)`；单段且无方向 /
  位置 → 走 `allowGradientToken: true`（整值引用渐变 token）。渐变 token 作为某一段仍是嵌套错误。
- `token/alpha` → `spec.WithAlpha(a)`。
- `Resolve`（纯色签名）不动，仍在 `IsGradient` 时 throw —— `*Modulate` / caret / `char-color` 等
  纯色属性的错误信息自动正确。
- `ParseThemeColor`（定义处）：逐段 `ColorUtility.TryParseHtmlString`，方向 / 位置 / 提示照收 ——
  定义处「字面量 only」只约束**颜色**，形状不是颜色（2026-08-30 §3.2 同一裁定）。

## 6. 渲染路径

### 6.1 程序化表面（三份 shader）

`UI-PanelSDF.cginc` 新增共享的 ramp 结构与求值函数，**替换** `PuguiFillRamp`：

```hlsl
struct PuguiRamp
{
    float4 c0, c1, c2, c3;   // 色标
    float4 stops;            // P0..P3
    float4 curves;           // E0..E2, w = Count
    float4 dir;              // xy = 单位方向（角度模式）; z = 0 角度 / 1 角; w = 角编号
};

float4 PuguiGradient(float2 p, float2 b, PuguiRamp r)
{
    if (r.curves.w <= 1.0) return r.c0;                       // 纯色：uniform 分支，整段跳过
    float2 dir = r.dir.z > 0.5 ? PuguiCornerDir(b, r.dir.w) : r.dir.xy;
    float  L   = abs(2.0 * b.x * dir.x) + abs(2.0 * b.y * dir.y);
    float  s   = saturate((dot(p, dir) + 0.5 * L) / max(L, 1e-4));
    // 三段展开（Count ≤ 4），每段：u = saturate((s − Pi) / max(Pi+1 − Pi, 1e-4)); Ei != 1 时 pow
    …
}
```

每份 shader 按色槽声明一组 uniform，前缀 `_Fill` / `_Border` / `_Glow` / `_InnerGlow`
（`UI-Decor.shader` 只有 `_Fill` / `_Glow`），每组 7 个 float4；面板 shader 共 28 个，可接受。
fragment 里四处调用：

```hlsl
float4 col = PuguiGradient(p, b, FILL_RAMP);
col = PuguiApplyInnerGlow(col, d, inside, _InnerGlowSize, PuguiGradient(p, b, INNERGLOW_RAMP));
col = PuguiApplyOuterGlow(col, d, inside, _GlowSize,      PuguiGradient(p, b, GLOW_RAMP));
if (_BorderWidth > 0.0) { float4 border = PuguiGradient(p, b, BORDER_RAMP); … }
```

`PuguiApplyOuterGlow` / `PuguiApplyInnerGlow` / 描边段的合成公式**一字不改**：只是把原来读一个
uniform 的地方换成读一个 ramp 求值。曝光（`PuguiExpose`）仍在整条合成链之后跑一次。

### 6.2 材质缓存 key

`PanelParams` 的 `FillTop / FillBottom / FillStopTop / FillStopBottom / FillCurve / BorderColor /
GlowColor / InnerGlowColor` 八个字段折成四个 `ColorSpec`：`Fill / Border / Glow / InnerGlow`；
`DecorParams` 同理折成 `Fill / Glow`。`Equals` / `GetHashCode` 委托给 `ColorSpec`。
`Configure` 用一个 `WriteRamp(mat, RampIds, in ColorSpec)` 写 7 个 uniform，四组 id 各一份静态表。
纯色槽的默认 key（Count = 1）与今天纯色面板一一对应 —— 老工程不会多出一批材质。

### 6.3 顶点路径（`GradientTint` + `MeshSlicer`）

- 包围盒 → `c`、`size`；`dir = spec.DirectionFor(size)`；`L = spec.LineLengthFor(size, dir)`；
  每个顶点 `s(v) = (dot(v.pos − c, dir) + L/2) / L`。
- `HasStops == false`（含「任意方向的双色」）→ 逐顶点 `color *= Evaluate(s)`：双色无位置时
  `Evaluate` 是 `s` 的线性函数，硬件插值精确 —— 这是今天 `ModifyPlain` 的推广，不 de-index、不分配。
- `HasStops == true` → VGS §4.2 的流程，切线从「水平线 `y = …`」换成「`dot(pos, dir) = d`」：
  `MeshSlicer.SplitAlongY(tris, cut, out)` 改为 `SplitAlongLine(tris, dir, cut, out)`，
  `Side` = `sign(dot(pos, dir) − cut)`，新顶点插值后把投影**钉到** `cut`（`pos += dir · (cut − dot(pos, dir))`，
  默认方向下等价于今天「y 直接赋为切线值」）。切线集合：每个色标一条（`d_i = −L/2 + P_i · L`），
  每个 `E_i ≠ 1` 的段再加 `K−1` 条等距线（`K = 8` 不变）。
- 透明端剔除（VGS §4.3）推广：`C0.a == 0` 剔除 `s ≤ P0` 的三角形，`C_last.a == 0` 剔除 `s ≥ P_last` 的。
  中间色标透明不剔（几何上是一条缝，不是一端）。
- `CentroidBias` 沿用。

### 6.4 `<Text>`（TMP 四角）

`VertexGradient(topLeft, topRight, bottomLeft, bottomRight)` 的四个角分别取
`Evaluate(s)`，`s` 按**单位正方形**上的四角算（每个字形一个 quad、TMP 只给一份 `VertexGradient`，
拿不到逐字宽高，所以角模式在文字上退化为 45° 家族 —— 写进 SKILL）。双色任意方向因此精确；
`HasStops` 为真（≥3 色 / 挪位 / 提示）时仍走既有的 `GradientStopWarning` + lint，只是消息与判定
扩到「第三个颜色」。`LabelColorApplier` 同改。

## 7. 程序化色槽放开

| 色槽 | 今天 | 改后 |
|---|---|---|
| `borderColor`（Frame / 11 个程序化控件 / Scrollbar `handleBorderColor`） | `UI.Theme.Resolve` → `Color` | `ResolveSpec` → `ColorSpec`，`ProceduralPanel.SetBorderColor(in ColorSpec)` |
| `glowColor`（同上 + `handleGlowColor` + `<Decor glowColor>`） | 同上 | 同上；**未写时跟随整条填充渐变**：`fill.AnyVisible ? fill.Opaque() : Solid(white)`（今天是 `FillTop` 置不透明 —— 推广为每个色标置不透明、形状与方向照搬）。`glow="12"` 单写时「这块东西发光」的读法在渐变填充上也成立（LG-D4，现有「渐变填充 + 未写 glowColor」的面板光晕会随之换色，属预期视觉变化） |
| `innerGlowColor` | 同上 | 同上；默认仍是纯白（2026-08-28 §5.4 的理由不变） |

`ProceduralPanel` / `DecorPanel` 的 `_borderColor` / `_glowColor` / `_innerGlowColor` 字段类型改
`ColorSpec`；`BuildParams` 的灰化改 `spec.Desaturate()`；`HasVisibleContent` 的 `_borderColor.a > 0`
改 `Border.AnyVisible`。`ProceduralSurface.Declare(p => p.SetBorderColor(v))` 的 lambda 形态不变。
`CopyFrom` 逐字段拷贝照旧。

四个色槽共用**同一条渐变线**（同 rect、同方向定义）：同一个 token 写在 `color` 与 `borderColor` 上，
转换点落在同一排像素。这是选「与填充同一坐标系」而不是「沿周长」的直接收益（LG-D5）。

## 8. 报错

全部 parse-time（运行时 `ResolveSpec` throw 同一条消息；CLI 由 `ColorLiteralRules` 报
`PUI-COLOR-GRADIENT-MALFORMED`，其 `CheckSegment` 改为遍历全部色标段，方向段单独校验）：

| 输入 | 消息要点 |
|---|---|
| 5 个及以上颜色段 | `gradient supports 2 to 4 colours` |
| 空段 | `gradient segment is empty`（不变） |
| `to right, #fff` | `a direction needs at least two colours` |
| `#fff, to right, #000` | `the direction must be the first segment` |
| `45deg #fff, #000` | `the direction must be its own comma-separated segment ("45deg, #fff, #000")` —— 在 `TrySplitStop` 前判：首个空白分段匹配角度形状即报 |
| `to up, …` / `to top bottom, …` | `a direction is "<N>deg" or "to <side or corner>" — top / bottom / left / right, or a vertical + horizontal pair` |
| `A, 30%, 60%, B` / `30%, A, B` / `A, B, 30%` | `a colour hint must sit BETWEEN two colours`（不变，覆盖三种位置） |
| `A 60%, B 30%, C` | `stop positions must not decrease along the gradient`（取代「second … must not sit above the first」） |
| 提示落在所在段之外 | `the hint must sit between the two stops of its segment` |
| 单色带位置 `blue 70%` | 不变 |
| 一段两个位置 `red 10% 30%` | 不变（`at most one stop position`） |
| `<Color name="45deg">` | `token name reads as a gradient direction — pick another name` |
| 渐变 token 作为某一段 | 不变（嵌套） |
| 渐变写在 `*Modulate` / caret / `char-color` … | 不变（`Resolve` throw；lint `PUI-GRADIENT-MODULATE`） |

lint 的 `GradientStopRules`（TMP 路径）：`HasStop` 判定扩到 `Count > 2`，消息加一句「or a third
colour」；错误码 `PUI-GRADIENT-STOP-NO-SURFACE` 沿用（改码只会让既有工程的 lint 基线抖动）。
lint 里没有「纯色 only 属性名单」（三个色槽的拒绝只在运行时 `Resolve` 抛），因此 lint 侧
无需删规则；`GradientModulateRules` 不动。

## 9. 改动面

1. **`Runtime/Core/Parser/ColorParser.cs`** — `GradientParts` 拓成 N 段 + 方向（§5.1）；
   `TrySplitDirection`；`TrySplitGradient` 的分段分类；§3.3 默认位置摊开；§8 的消息。纯 C#。
2. **`Runtime/Core/Parser/UIDocumentParser.cs`** — `<Color value>` 校验遍历全部段；`<Color name>`
   拒绝角度形状。
3. **`Runtime/Application/ColorSpec.cs`** — §5.2 重写；`GradientDirection` 放同文件或
   `Core/Parser/GradientDirection.cs`（纯 C#，供 `GradientParts` 用 —— 后者）。
4. **`Runtime/Application/UI.cs`** — `ResolveSpec` / `ResolveSingle` / `ParseThemeColor`（§5.3）。
5. **`Runtime/Resources/PromptUGUI/Material/UI-PanelSDF.cginc`** — `PuguiRamp` / `PuguiGradient` /
   `PuguiCornerDir`，删 `PuguiFillRamp`。
6. **`UI-ProceduralPanel.shader` / `UI-GlassPanel.shader` / `UI-Decor.shader`** — uniform 块 + 四（二）处调用。
7. **`Runtime/Controls/Internal/ProceduralMaterialCache.cs` / `DecorMaterialCache.cs`** — `PanelParams` /
   `DecorParams` 折成 `ColorSpec` 字段；`WriteRamp`。
8. **`ProceduralPanel.cs` / `DecorPanel.cs`** — 色槽字段与 setter 改 `ColorSpec`；glow 默认；灰化；
   `HasVisibleContent`。
9. **setter 调用点**（`Resolve` → `ResolveSpec`）：`Frame.cs` ×3、`ProceduralControl.cs` ×3、
   `Scrollbar.cs` ×2、`Decor.cs` ×1。
10. **`GradientTint.cs` / `MeshSlicer.cs`** — §6.3。
11. **`Text.cs` / `LabelColorApplier.cs` / `GradientStopWarning.cs`** — §6.4。
12. **`StateTintReactor.cs` / `StateColorSet.cs`** — 无逻辑改动；`Multiply` 推广后状态色免费获得
    方向与多色标。渐变端仍 snap（VGS §4.6 不变）。
13. **`Runtime/Core/Lint/ColorLiteralRules.cs` / `GradientStopRules.cs`** — §8。
14. 测试（§11）与 SKILL（§12）。

`Core/Parser` 的改动不得引入 `UnityEngine` —— `GradientDirection` 只存 kind / 角度 / 符号，向量在
`ColorSpec.DirectionFor` 里才出现。

## 10. ReSolve / Variant / 主题

方向、位置、提示、色标数都是**值的一部分**，因此全部免费：`borderColor.mobile="to bottom, A, B"`
走同一个 setter；换主题 → `ReSolve` 重放 → `SetBorderColor` 幂等、材质 key 变了就换一份缓存材质；
渐变 token 带方向，皮肤能换的又多一样（方向本身）。`Multiply` 保形状 ⇒ 纯色 `*Modulate` 压在三色
对角边框上，形状与方向不动、只变暗。

## 11. 测试

EditMode（`UI.ResetForTests` 约定；既有文件就地扩，新增的标在括号里）：

1. **`ColorParserGradientTests` / `ColorParserStopTests`**：方向段三种写法、大小写、`to right top`
   顺序归一化；3 / 4 色；默认位置摊开（§3.3 两例）；每段提示；§8 全表逐行。
2. **`GradientStopResolveTests` / `ThemeGradientTests`**：`ResolveSpec` 多段解析、token 携带方向、
   `token/alpha` 换全部色标 alpha、定义处带方向注册成功、`<Color name="45deg">` 抛 `ParseException`。
3. **`ColorSpecEvaluateTests`**：`DirectionFor` 八个方向 + 角模式在 2:1 盒子上四角落 0/50/50/100；
   `LineLengthFor`；三色 `Evaluate` 分段；`HasStops` 真值表（方向不算、第三色算）；`Multiply` /
   `WithAlpha` / `Opaque` / `Desaturate` 保形状；`Equals` / `GetHashCode` 值语义。
4. **`GradientStopPanelTests`**（材质 key）：`PanelParams` 纯色槽默认 key 与今天相同；四个色槽任一
   渐变 → 不同 key；`Configure` 写入的 uniform 与 spec 对应（`_BorderColor0..3` / `_BorderDir`）。
5. **`ProceduralSurfaceRenderTests` / `DecorRenderTests` / `GradientStopRenderTests`**（真渲染探针）：
   默认双色面板探针值不变（回归护栏）；`to right` 双色左右探针；`to bottom right` 三色在 2:1 面板的
   四角探针（左上 / 右下 = 端色，右上 / 左下 = 中间色）；渐变 `borderColor` 在描边带内上下两处探针
   不同色、填充区不受影响；`glowColor` 未写时跟随渐变填充（光晕两端探针）；玻璃面板同一组。
6. **`GradientTintTests` / `GradientTintStopTests`**：横向双色无切分、逐顶点；三色沿 `to right` 切两刀、
   顶点数；对角切线新顶点的投影钉在 `cut` 上；首尾透明剔除按投影；`GradientFlipOrderTests` 加
   `flip="x"` + `to right` 仍是「看到的左 → 右」。
7. **`GradientColorAttrTests` / `StateGradientTests`**：`hoverColor="to right, A, B, C"` 进 hover 换槽、
   snap；`borderColor` / `glowColor` / `innerGlowColor` 在 `<Frame>` / `<Btn>` / `<Scrollbar handle*>` /
   `<Decor>` 上接受渐变且 ReSolve 幂等。
8. **`GradientLintTests` / `GradientStopLintTests`**：§8 表的 CLI 侧；`<Text color="to right, A, B">`
   不报、`<Text color="A, B, C">` 报；`borderColor` 渐变不再触发任何 lint。
9. **Text**：`VertexGradient` 四角与 `Evaluate` 一致（`to right` 时左右两列不同、上下相同）。

PlayMode（`GradientPlayTests`）：一条 `<Btn borderColor="to right, cyan, cyan/0.3, cyan" glow="8">`
的 hover / pressed 冒烟，断言材质 `_BorderCurves.w == 3`。

## 12. SKILL 更新（同 PR，英文）

- `authoring-promptugui-xml/SKILL.md`
  - **Gradients** 一节重写：§3.1 文法表、方向表（含魔法角一句话）、位置默认规则、2..4 色、提示逐段；
    「Where gradients work」加上 `borderColor` / `glowColor` / `innerGlowColor` / `handleBorderColor` /
    `handleGlowColor` / `<Decor glowColor>`；不支持名单只剩 `*Modulate` / `char-color` / caret /
    selection / Carousel dot / `<Image glow>` 的 `glowColor`；TMP 一段补「方向可以，角模式按正方形」；
    发光的取值范围（§4.4）。
  - `<Frame>` 属性表 `borderColor` / `glowColor` / `innerGlowColor` 三行去掉「纯色 only」，
    `glowColor` 默认改「跟随填充渐变」；`<Btn>` 等复用行的措辞同步。
  - **Error codes** 列表按 §8 增删。
  - 文末速查块（`FRAME VISUAL` / `gradient runs on the FINAL mesh` 两行）补方向。
- `reference/states.md`：`*Color` 一段提「方向 / 多色标」，snap 规则不变。
- `reference/glass.md` §复用：`color` 与三个色槽都吃完整渐变语法。
- `reference/decor.md`：`glowColor` 行加渐变；`color` 行的「comma gradient」换成「full gradient grammar」。
- `reference/controls-scrollbar.md`：`handleBorderColor` / `handleGlowColor` 行同步。
- C# skill 不更新：`ColorSpec` / `ResolveSpec` 均 internal。

## 13. 验收

参考图的行星面板与两个按钮，各一行：

```xml
<Frame color="#0b1e3a/0.85, #071226/0.9" radius="12, 12, 12, cut 18"
       borderWidth="1.5" borderColor="to bottom right, hud-cyan, hud-cyan/0.3 50%, hud-cyan"
       glow="10"         glowColor="to bottom right, hud-cyan/0.5, hud-cyan/0 50%, hud-cyan/0.5"/>

<Btn radius="8" color="#0d2140" borderWidth="1.5"
     borderColor="to right, hud-cyan, hud-cyan/0.35, hud-cyan" innerGlow="14" innerGlowColor="hud-cyan/0.5">建造</Btn>
<Btn radius="8" color="#1a1408" borderWidth="1.5"
     borderColor="to right, hud-gold, hud-gold/0.35, hud-gold" innerGlow="14" innerGlowColor="hud-gold/0.5">造船</Btn>
```

肉眼确认：面板左上 / 右下角描边最亮、右上 / 左下最暗、沿对角线亮暗亮；面板拉宽到 3:1 后四个角
的亮暗归属不变；按钮描边两端亮中段暗；填充与描边写同一个 token 时转换点重合。

## 14. 不做的事（YAGNI 记录）

- **>4 色标**、**径向 / 锥形渐变**、CSS **双位置色标**（`red 10% 30%`）、`turn` / `rad` 单位。
- **`<Image glow>` 的 `glowColor` 渐变** —— 那是 blur 路径不是 SDF，要在 `UI-ImageFx.shader` 里乘一条
  ramp；等有需求。`self` 关键字照旧。
- **`<Animation char-color>` / caret / selection / Carousel dot** 继续纯色（gradient spec §6 的理由不变）。
- **TMP 逐字 aspect 的角模式** —— 需要逐字写顶点色而不是 `VertexGradient`，与 char-color 渐变是同一
  件事，一起等。
- **状态色的渐变 ↔ 渐变 tween** —— 四槽 `ColorSpec` 逐字段 lerp 是可行的，但两端形状不同（色标数 /
  方向不同）时没有自然定义；snap 规则不变。
- **硬边抗锯齿** —— 2026-08-30 §12 的同一条。

## 15. 决策记录（2026-09-17 对齐）

| 编号 | 决定 |
|---|---|
| LG-D1 | 方向写法照搬 CSS：`<N>deg` 与 `to <side-or-corner>`，角度约定与 CSS 逐字一致（0deg 向上、180deg 向下 = 今天默认），`to <corner>` 实现 CSS 的宽高比「魔法角」语义 |
| LG-D2 | 色标上限 **4**，提示最多 3（逐段）；SDF 与顶点路径同一上限，由解析器统一报错 |
| LG-D3 | 顶点路径（Image / Icon / RawImage / sprite 皮肤控件）的方向与多色标**本轮一起做**，不留「只在程序化表面生效」的例外；`<Text>` 是唯一例外（双色任意方向可以，≥3 色 / 位置 / 提示报错） |
| LG-D4 | 放开**全部 SDF 色槽**：`borderColor` / `glowColor` / `innerGlowColor`，含 Scrollbar `handle*` 与 `<Decor glowColor>`；未写 `glowColor` 时跟随整条填充渐变 |
| LG-D5 | 边框 / 发光与填充共用**同一条渐变线**（同 rect、同方向定义），不做沿周长参数化 |
| LG-D6 | 边框渐变不单独立项，并入本文；「噪声雾」（按钮上的云雾亮斑）另开 spec，与本文正交 |
