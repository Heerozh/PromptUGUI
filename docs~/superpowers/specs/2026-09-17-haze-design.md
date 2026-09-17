# `haze` —— 程序化表面的噪声雾层（云雾状亮斑）

> 状态：**已实现**（M0–M2 一轮做完，见 §15）。决策见 §13，2026-09-17 与作者对齐，四项全部取推荐项；
> 实施中改了两处常数（§5.1：覆盖率映射 0.55 / 0.85、第二三 octave 旋转），正文已按实际落地更新。
> 相关：`2026-09-17-linear-gradient-primitive-design.md`（LG-D6 把本文从边框渐变里剥出来；`hazeColor` 直接复用它的
> 方向 / 多色标 ramp，是本文「方向遮罩零新语法」的前提）、
> `2026-09-12-intensity-design.md`（曝光；本文的实现地图逐项镜像它，雾排在曝光之前让 `intensity` 能把雾点亮）、
> `2026-08-28-inner-glow-design.md`（`innerGlowColor` 默认白的理由，本文同一条）、
> `2026-08-26-procedural-surface-design.md`（`ProceduralControl` 让全家共享属性）、
> `2026-08-23-glass-fill-design.md`（玻璃 —— 本文明确排除的表面；它的 `noise` 与本文不是一回事，见 §1.1）。

## 1. 问题

参考图（星海指挥官行星面板）的两个按钮：「建造」深底青边，底边与左右下角贴着一团**青色光雾**；
「造船」深底金边，底部中央一团**橙色光雾**。上半截是干净的深色。雾不是均匀的，是几块软边亮斑，
尺寸十几到几十 px，边缘无硬线。

今天的程序化表面每一层都是「颜色 × 沿一条直线的 ramp」：填充、描边、两层发光，全部**沿轴单调**。
作者在这套词汇里怎么转都到不了「不规则」：

- `innerGlow` 是均匀的一圈，压不出「只在底边、而且是几坨」；
- `color="to top, cyan/0.6, #0b1a33"` 得到一条干净的青色渐变带，是霓虹管不是雾；
- 叠一张画好的雾 PNG（`<Image blur>` + `mask="self"`）能做，但每个主题重画一张，回到贴图美学那条线，
  且不受 `intensity` / 状态色 / 主题 token 管。

缺的是**空间上的不规则性**：一个低频噪声场。

### 1.1 为什么不是玻璃的 `noise`

玻璃的 `noise` 是逐像素 interleaved gradient noise，高频白噪声，用途是磨砂颗粒和压 banding。云雾是
**低频**的 —— 几个 octave 叠起来的 value noise（fBm），特征尺寸以 px 计而不是以像素计。两者频段不同、
用途不同、复用不了；`noise` 这个名字也已经被占用，本文另起一族。

## 2. 否决的方案

**平铺噪声贴图（`Resources` 里一张 64×64）。否决。** 每像素一次纹理采样比实时 fBm 便宜，但破坏
「程序化表面无贴图」的承诺；大面板上平铺接缝露馅；需要 mip / filter 设置随平台走。fBm 三个 octave
共 12 次 hash，对按钮尺寸的面无感，全屏面可接受（§8.4）。第一版不引入贴图，等移动端真跑不动再议。

**雾作为填充的调制（`fill.rgb *= 1 + haze × fbm`）。否决。** 没填充的空心框画不出雾；雾的颜色被锁死
在填充色相上，「深蓝底、青雾」写不出；也没法用 `/alpha` 做强度旋钮。雾是**一层**（`hazeColor` 的
颜色，fBm 决定它的 alpha），over 在填充之上 —— 与描边、内发光同构。

**独立的方向遮罩属性（`hazeFrom="bottom"`）。否决（H-D2）。** 「从底边渗入」只是一条 alpha ramp；
`hazeColor` 本来就是一个色槽，色槽今天吃完整的 `linear-gradient` 语法。`hazeColor="to top, cyan/0.8, cyan/0"`
就是方向遮罩，多色标 / 提示 / `to bottom right` 全部继承，一个新语法都不加。

**显式 `hazeSeed`。否决（H-D3）。** 两个同尺寸同参数的按钮共享一份材质，若噪声在 rect 局部坐标里采样，
它们的雾逐像素相同，参考图里明显不是。要作者写 seed，LLM 作者几乎一定忘。§5.3 用 Canvas 空间坐标采样，
相邻按钮天然不同，不需要任何新属性。

**雾进 `<Decor>` / `<Image>` / 玻璃。本轮不做。** 见 §12。

## 3. 方案总览

三个属性，落在 `ProceduralControl` 全家（`<Frame>` / `<Btn>` / `<Tab>` / `<TabMenu>` / `<Toggle>` /
`<Slider>` 轨道 / `<Dropdown>` / `<InputField>` / `<ScrollList>` / `<Scrollbar>` / `<Collapsible>` /
`<Progress>`），命名与 `glow` / `glowColor` 同构：裸名给尺寸，`*Color` 给颜色且 `/alpha` 是强度。

```xml
<!-- 参考图「建造」：深底、青边、底边渗入的青雾 -->
<Btn radius="6" color="#0b1a33" borderWidth="1" borderColor="to right, cyan, cyan/0.3, cyan"
     haze="40" hazeColor="to top, cyan/0.8, cyan/0" intensity="1.6">建造</Btn>

<!-- 参考图「造船」：金边、底部中央的橙雾（to top 加提示：雾集中在最底下三分之一、往上变稀）-->
<Btn radius="6" color="#1a1408" borderWidth="1" borderColor="to right, #f2c14e, #f2c14e/0.3, #f2c14e"
     haze="32" hazeColor="to top, orange/0.9, 30%, orange/0" hazeDrift="6">造船</Btn>

<!-- 空心框里一团缓慢流动的光雾 -->
<Frame borderWidth="1" borderColor="white/0.3" haze="64" hazeColor="#7ec8ff/0.5" hazeDrift="12"/>

<!-- 主题一次换整套 -->
<Style name="hud-btn" radius="6" borderWidth="1" haze="40" hazeColor="to top, @accent/0.8, @accent/0"/>
```

雾是**这个表面画出来的东西之一**：排在填充之后、内发光之前，被 `intensity` 一起曝光（雾的亮核推白、
尾巴保色相 —— 正是霓虹雾的读法），被 `*Modulate` 一起压暗，被 `mask="self"` 一起裁，随 CanvasGroup
一起淡出。

## 4. 语法

| 属性 | 值 | 默认 | 说明 |
|---|---|---|---|
| `haze` | px | `0`（无雾） | 斑块的**特征尺寸**：噪声基频一格的边长。`> 0` 即开启。`""` = 回到无雾 |
| `hazeColor` | 完整颜色语法（token / `/alpha` / 方向 / 2..4 色标 / 提示） | `white` | 雾的颜色；fBm 只乘在它的 alpha 上。**不跟随填充**（填充色的雾在不透明填充上看不见，`innerGlowColor` 的同一条理由）。`/alpha` 是强度旋钮 |
| `hazeDrift` | px/s，`≥ 0` | `0`（静止） | 雾的流速。三个 octave 各沿不同向量漂移，图样在流动中**形变**而不是整体平移。走未缩放时钟（§5.5） |

解析：`haze` / `hazeDrift` 走 `ProceduralValueParser.Pixels`（非数 / NaN / 负数三段错误与 `glow` 逐字同款）；
`hazeColor` 走 `UI.Theme.ResolveSpec`，形状错误由 `ColorParser` 报（LG spec §8）。

## 5. 语义

### 5.1 噪声

可平铺的 value noise，三个 octave（lacunarity 2、gain 0.5），quintic 插值（`6t⁵ − 15t⁴ + 10t³`，
比 cubic 少一圈可见的格子感）。hash 用 Dave Hoskins `hash12`（无 `sin`，移动端精度稳定）。
格点坐标先对 **256** 取正模再 hash，噪声因此以 256 格为周期平铺 —— `hazeDrift` 的时间偏移永远不需要
回绕，也不会出现接缝。

fBm 归一到 `0..1` 后过一个固定的覆盖率映射：

```
w = smoothstep(0.55, 0.85, fbm)
```

两个常数按参考图标定，**不是参数**：`/alpha` 已经是强度旋钮，再给覆盖率会让作者在两个耦合的旋钮之间
打转。fbm 的均值 0.50、标准差 0.15，映射后约 28% 的像素沾雾、9% 在半亮以上、2% 全亮 —— 稀疏的软边亮斑
（§15.1 有标定过程；起草时写的 0.45 / 0.80 在第一张探针图上是「大半张脸都是白的」）。

第二、三个 octave 的采样域各**转一个常角**（0.7 / 1.4 rad）并错开原点：value noise 的极值落在格点上，三层
同轴叠加会露出一张方格；转开后叠成没有轴向的云团（§15.1）。

### 5.2 作用位置

```
不透明面：  填充 → [雾 over] → 内发光 over → 外发光 under → 描边 over → 曝光 → × 顶点色 → 裁剪
```

```hlsl
float4 haze = PuguiGradient(p, b, HAZE_RAMP);                 // 与其他色槽同一条渐变线（LG-D5）
haze.a *= inside * PuguiHazeWeight(IN.worldPosition.xy, _HazeSize, _HazeDrift, _PuguiUnscaledTime);
col = PuguiOver(haze, col);
```

- **只在形状内侧**（`× inside`）：雾不进外发光，也不撑大 quad。
- **在内发光 / 描边之下**：描边压在雾上，边框仍然清晰；雾贴着描边内侧渗出来，正是参考图的读法。
- **在曝光之前**：`intensity="1.6"` 把雾最亮的几块推向白、其余保色相。雾没有自己的 `intensity`。
- **无填充也画**：`col` 从透明起步，雾 over 上去就是「空心框里一团光」，与「无填充 + 描边 = 空心框」同构。

### 5.3 采样坐标：Canvas 空间，不是 rect 局部

噪声在 **`IN.worldPosition.xy`**（`_ClipRect` 裁剪用的同一个坐标：CanvasRenderer 把网格烘到 Canvas 根
的局部空间）上采样，除以 `haze` 得到格坐标。后果，全部是想要的：

- **同参数的相邻按钮各不相同**：位置不同就采到噪声场的不同区域，材质照旧共享（H-D3）。
- **确定、稳定**：同一位置、同一时刻永远同一张图；不依赖实例 id、不依赖构建顺序。
- **跨面板连续**：一块 HUD 由多个面板拼成时，它们读成同一片星云的不同窗口。
- **单位与 `radius` 一致**：Canvas 单位（CanvasScaler 的参考 px），不随分辨率变粒度。

代价：一个面板用 `<Animation>` 平移时，雾像窗外的云一样在它身上滑过（表面是「看向一片星云的窗」）。
静止雾 + 300 ms 的入场滑动几乎看不出；开了 `hazeDrift` 的雾本来就在动。记进 SKILL，不做补救。

`hazeColor` 的渐变线仍按 **rect** 定（`p, b`），与其他四个色槽同一条：方向遮罩跟着面板走，只有噪声
纹理留在 Canvas 里。

### 5.4 与其他层的交互

| | 规则 |
|---|---|
| `glass="true"` | 雾**不画**：`BuildParams` 把 `haze` 归 0（`intensity` 同款折进 key，材质不分裂）；lint `PUI-GLASS-HAZE`（§7）。玻璃的填充是 backdrop，雾在它上面要另立合成规则，且焊接组是第三份 shader —— 本轮不做（H-D4） |
| `weld` 容器 / 成员 | 同上：焊接组不吃雾 |
| disabled（`_grayed`） | `hazeColor` 与其他色槽一起 `Desaturate()`；**`hazeDrift` 强制 0**。灰掉但还在流动的雾读成「活的」，失能控件要读成惰性的 —— 与 `intensity` 归 1 是同一条理由 |
| `intensity` | 作用在雾上（§5.2） |
| `*Modulate` / CanvasGroup | 顶点色在曝光之后统一乘，雾跟着整面暗 / 淡 |
| `mask="self"` | 遮罩形状取 SDF 实心区，与画了什么无关（既有规则），雾在内侧、天然被裁 |
| 内层（`handle*` / Progress fill / Slider fill） | 不给雾（§12） |
| `<Decor>` / `<Image>` / `<Icon>` | 不给雾（§12） |

### 5.5 时钟

`hazeDrift > 0` 时 shader 读全局 `_PuguiUnscaledTime`。由 `HazeClock`（`Controls/Internal/`，静态）在
第一份带雾材质创建时订阅 `Canvas.willRenderCanvases`，每帧一次 `Shader.SetGlobalFloat(…, Time.unscaledTime)`。
未缩放：暂停菜单（`timeScale = 0`）上的雾照常流动，与 close-transition / Toast / Carousel 的时钟一致。
EditMode 场景视图不重绘就不动，与其他一切动画相同。

漂移向量是常数：三个 octave 分别沿 `(1, 0.35)`、`(−0.6, 0.8)`、`(0.3, −1)`（归一化）以
`hazeDrift / haze` 格每秒的速度乘 `1 / 1.5 / 2` 移动。不共线 → 形变；细 octave 更快 → 表面「沸腾」、
整体缓慢。方向不是参数。

### 5.6 可见性与材质

- `haze > 0` 才让面板可见（`IsPanelVisible` 的 or 项 +1）：`haze="40"` 单独写在一个无填充无描边的
  Frame 上，是一团光，不是空。
- `haze == 0` 时 `hazeColor` / `hazeDrift` 在 `BuildParams` 里归零再进 key（玻璃参数在非玻璃面上归零的
  同一条）：只写了 `hazeColor` 的面板与没写的共享材质。
- `haze` / `hazeColor` / `hazeDrift` 变更是材质级：不重建网格、不 dirty 顶点（`Haze_ChangeDoesNotDirtyVertices`）。

## 6. shader 段落

`UI-PanelSDF.cginc` 新增（所有面板 shader 可见；本轮只有 `UI-ProceduralPanel.shader` 调用）：

```hlsl
// ---- 噪声雾（haze，spec 2026-09-17）----
#define PUGUI_HAZE_PERIOD 256.0

// Dave Hoskins hash12：无 sin，移动端 half 精度下不塌。
float PuguiHash12(float2 c)
{
    float3 p3 = frac(float3(c.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// 可平铺 value noise，quintic 插值。格点坐标取正模，于是时间偏移无需回绕。
float PuguiValueNoise(float2 q)
{
    float2 i = floor(q);
    float2 f = q - i;
    i -= PUGUI_HAZE_PERIOD * floor(i / PUGUI_HAZE_PERIOD);
    float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    // 右邻 / 上邻格点在周期边界上要回绕到 0，否则平铺处有一条缝：i 取过模，i + 1 仍可能等于 256。
    float2 i1 = i + 1.0;
    i1 -= PUGUI_HAZE_PERIOD * floor(i1 / PUGUI_HAZE_PERIOD);
    float a = PuguiHash12(i);
    float b = PuguiHash12(float2(i1.x, i.y));
    float c = PuguiHash12(float2(i.x, i1.y));
    float d = PuguiHash12(i1);
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// 三 octave fBm → 覆盖率映射。pos 为 Canvas 空间坐标（§5.3），size 为特征尺寸 px，
// drift 为 px/s，t 为未缩放秒。旋转角、原点错位、覆盖率常数 0.55 / 0.85 按参考图标定（§5.1），不是参数。
static const float2x2 PUGUI_HAZE_ROT1 = float2x2(0.7648, -0.6442, 0.6442, 0.7648);   // 0.7 rad
static const float2x2 PUGUI_HAZE_ROT2 = float2x2(0.1700, -0.9854, 0.9854, 0.1700);   // 1.4 rad

float PuguiHazeWeight(float2 pos, float size, float drift, float t)
{
    float2 q = pos / max(size, 1e-3);
    float  v = drift * t / max(size, 1e-3);          // 格 / 秒 × 秒
    float2 o0 = q + normalize(float2(1.0, 0.35)) * v;
    float2 o1 = mul(PUGUI_HAZE_ROT1, q * 2.0) + float2(13.1, 7.3)  + normalize(float2(-0.6, 0.80)) * v * 1.5;
    float2 o2 = mul(PUGUI_HAZE_ROT2, q * 4.0) + float2(26.2, 14.6) + normalize(float2(0.3, -1.00)) * v * 2.0;
    float  n = 0.5 * PuguiValueNoise(o0) + 0.25 * PuguiValueNoise(o1) + 0.125 * PuguiValueNoise(o2);
    n /= 0.875;
    return smoothstep(0.55, 0.85, n);
}
```

`UI-ProceduralPanel.shader`：

```hlsl
PUGUI_RAMP_UNIFORMS(_Haze)      // 第五组色槽，7 个 float4；面板 shader 共 35 个
float _HazeSize;
float _HazeDrift;
float _PuguiUnscaledTime;       // 全局，HazeClock 写

// frag：填充之后、内发光之前
if (_HazeSize > 0.0)            // uniform 分支：无雾面板逐位不变
{
    float4 haze = PuguiGradient(p, b, HAZE_RAMP);
    haze.a *= inside * PuguiHazeWeight(IN.worldPosition.xy, _HazeSize, _HazeDrift, _PuguiUnscaledTime);
    col = PuguiOver(haze, col);
}
```

`UI-GlassPanel.shader` / `UI-GlassGroup.shader` / `UI-Decor.shader` / `UI-ImageFx.shader` **不改**。

## 7. 报错

| 码 | 级别 | 触发 |
|---|---|---|
| `PUI-PROCEDURAL-VALUE` | error | `haze` / `hazeDrift` 非数 / NaN / 负（既有码，`StyleRules.CheckValue` 加两个名字） |
| `PUI-COLOR-*`（既有） | error | `hazeColor` 的渐变形状错误，与其他色槽同款 |
| `PUI-GLASS-HAZE` | warning | `glass="true"` / `weld` 容器 / 焊接成员上写了 `haze`（raw + style-aware，`PUI-GLASS-INTENSITY` 同型）：*'haze' has no effect on a glass surface — drop 'glass' (or 'weld'), or drop 'haze'* |
| `PUI-CONTAINER-VISUAL-ATTR`（既有） | warning | `haze*` 写在 `*Stack` / `Grid` / `SafeArea` / `<Image>` 等上 —— 三个名字进 `ProceduralAttrNames` 自动覆盖 |

`hazeColor` / `hazeDrift` 写了而 `haze` 没写：**不报**。`glowColor` 无 `glow` 今天也不报，同一条规则；
主题可以只在一个主题里给 `haze`、另一主题给 `hazeColor` 而不触发噪音。

## 8. 实现地图

### 8.1 数据流（改动点自上而下）

| 文件 | 改动 |
|---|---|
| `Runtime/Resources/PromptUGUI/Material/UI-PanelSDF.cginc` | §6 的四个函数 |
| `UI-ProceduralPanel.shader` | `_Haze*` 三个属性 + `PUGUI_RAMP_UNIFORMS(_Haze)` + `_PuguiUnscaledTime`；填充之后的 uniform 分支 |
| `Controls/Internal/ProceduralMaterialCache.cs` | `PanelParams` + `Haze`（`ColorSpec`）/ `HazeSize` / `HazeDrift`（ctor、`Equals`、`GetHashCode`）；`Configure` 一次 `WriteRamp` + 两个 `SetFloat`；`PropertyToID` 三个 + 一组 ramp id |
| `Controls/Internal/HazeClock.cs`（新） | 静态；`Ensure()` 由 `Configure` 在 `HazeDrift > 0` 时调，首次订阅 `Canvas.willRenderCanvases`，每帧 `SetGlobalFloat(_PuguiUnscaledTime, Time.unscaledTime)`；`ResetForTests` 退订；测试用 `SetTimeForTests(t)` 直接写全局 |
| `Controls/Internal/ProceduralPanel.cs` | 字段 `_hazeSize = 0` / `_hazeColor = white` / `_hazeDrift = 0`；三个 setter；`BuildParams`：玻璃 → 尺寸归 0；`haze == 0` → 色 / 漂移归零；`_grayed` → 色去饱和、漂移归 0；`ComputeVisible` or 项 +1；`CopyStateFrom`（焊接镜像）三个字段 |
| `Controls/Internal/ProceduralSurface.cs` | **不改**：`Surface.Declare(p => p.SetHazeSize(v))` 直接写面板，`ProceduralControl` 的每个属性都是这么走的 |
| `Controls/Frame.cs` | `[UIAttr] Haze` / `HazeColor` / `HazeDrift`，直连 `Panel` |
| `Controls/ProceduralControl.cs` | 同名三个 `[UIAttr]`，走 `Surface.Declare` —— 全家自动获得 |
| `Core/Lint/ProceduralAttrNames.cs` | `PanelAttaching` / `All` / `NeedsPanel` 各 +3（`ProceduralAttrNamesTests` 守镜像） |
| `Core/Lint/StyleRules.cs` | `CheckValue` 的 px 分支加 `haze` / `hazeDrift` |
| `Core/Lint/GlassRules.cs` | 新码 `PUI-GLASS-HAZE`，raw + style-aware，与 `IntensityOnGlassCode` 并列 |
| `Editor/XsdGenerator.cs` | Frame 手写清单 +3（`ProceduralControl` 子类走反射） |
| `Samples~/ProceduralStyle` | 加一块「HUD 按钮」：参考图的两个按钮 + 一个流动的空心框 |

`PanelParams` 的 ctor 签名变了，grep 全部构造点（含测试）。`ProceduralPanel` / `ProceduralControl` 类注释里的
属性计数 +3（inner-glow spec §12.3 记过这个坑）。

### 8.2 CLI

`Core/Lint` 三处名字表 / 分支，纯 C# 子集不变。UIXmlLint 两遍（raw + expanded）自动覆盖
`<Style haze=>` 经 `class=` 落到玻璃控件上的情形。

### 8.3 里程碑

一个 PR，三个提交，每步 red 先行（实际落地时 M0 与 M1 合成一个提交，见 §15）：

1. **M0 雾层**：cginc + shader + `PanelParams` + `ProceduralPanel` / `Frame` / `ProceduralControl`；
   静止雾。渲染探针 + 参数测试。
2. **M1 时钟与失能**：`HazeClock`；disabled 冻结；漂移探针。
3. **M2 lint / XSD / SKILL / sample**：`PUI-GLASS-HAZE`、名字表、XSD、`reference/haze.md`、`CLAUDE.md` 触发表、sample。

### 8.4 性能

每像素 12 次 hash + 3 次 quintic + 一次 ramp 求值，仅在 `_HazeSize > 0` 的面上；无雾面板走 uniform 分支
跳过整段，逐位不变。按钮尺寸的面无感；一块全屏雾面板约等于一层小半径 blur 的代价 —— SKILL 里提示
「雾给按钮 / 卡片，全屏背景用一张图」。`hazeDrift` 不增加 fragment 开销（同样的三次采样，只是坐标带时间），
CPU 侧每帧一个 `SetGlobalFloat`。

## 9. ReSolve / Variant / 主题

三个属性都是普通 `[UIAttr]`：`haze.mobile="0"` 关雾、`hazeColor.dark=` 换色、`<Style>` 包一次换整套，
`ThemeStyleRules` 的豁免规则不变（一个主题写整套、另一主题不写 → 不报）。状态色（`hoverColor` 等）不碰雾
—— 要 hover 时雾变亮，写 `intensity` 的状态版本或 `<Show on="state-hover">` 叠一个 Frame。

## 10. 测试（Red 先行）

**`FrameProceduralPanelTests`**（材质参数观测走 `CurrentParams`，与 `Intensity*` 同型）

- `Haze_DefaultsToZero`；`Haze_ParsesPixels`（`haze='40'` → `HazeSize == 40`）；`Haze_Empty_ResetsToZero`；
  `Haze_Negative_Rejected`；`Haze_NaN_Rejected`；`HazeDrift_Negative_Rejected`。
- `HazeColor_DefaultsToWhite`；`HazeColor_TakesAGradient`（`to top, cyan, cyan/0` 进 `Haze` 的 `ColorSpec`，方向与色标齐全）。
- `Haze_AloneIsVisible`：无 fill 无 border 无 glow，`haze='40'` → `IsPanelVisible`。
- `HazeColorWithoutHaze_DoesNotSplitTheKey`：只写 `hazeColor` → `CurrentParams` 等于什么都不写。
- `Haze_DoesNotInflateMesh`；`Haze_ChangeDoesNotDirtyVertices`。
- `Glass_IgnoresHaze`：`glass='true' haze='40'` → `HazeSize == 0`。
- `Disabled_FreezesHazeDrift`：`hazeDrift='6'` 失能 → `HazeDrift == 0`，`Haze` 去饱和；再启用后回到 6。
- `SameHaze_SharesOneMaterial` / `DifferentHaze_SplitsMaterial`（`LiveMaterialCount`）。

**`ProceduralSurfaceContractTests`**：`AnyPanelAttachingAttr_AttachesASurface` 用例表 + `haze='40'` /
`hazeColor='cyan'` / `hazeDrift='6'`；`<Btn>` 上三个属性进同一份 `PanelParams`。

**`HazeClockTests`**（EditMode）：`Ensure` 幂等（多次调用一次订阅）；`ResetForTests` 后全局归 0；
`SetTimeForTests(3)` → `Shader.GetGlobalFloat == 3`。

**Lint**

- `ProceduralAttrNamesTests`：镜像测试自动覆盖。
- `GlassRulesTests.BadValues_AreFlagged` + `haze='abc'` / `hazeDrift='-1'`。
- `GlassRulesTests`（新组）：`glass='true' haze='40'` → `PUI-GLASS-HAZE`；weld 容器与成员各一例；
  `GlassRulesStyleAwareTests`：经 `class=` 带进来的也报；`<Frame haze='40'>` 不报；`hazeColor` 单独写在玻璃上**不报**。
- `PureContainerVisualAttrRulesTests`：`<VStack haze=>` 报；`<Image hazeColor=>` 报。
- `ThemeStyleRulesTests`：一个主题写 `haze` + `hazeColor`、另一主题只写 `hazeColor` → 不报。

**XSD**（`XsdGeneratorTests`，substring）：Frame / Btn 含 `haze` / `hazeColor` / `hazeDrift`。

**Render**（`HazeRenderTests`，`IntensityRenderTests` 的 `Camera.Render()` + `ReadPixels` 套路，底色 `#101010`）

- `HazeUnset_IsPixelIdenticalToBefore`：不写 `haze` 的既有基线（`CornerTreatmentRenderTests` / `InnerGlowRenderTests`
  / `IntensityRenderTests`）逐像素不变（守 uniform 分支）。
- `Haze_IsIrregular`：`color='#101828' haze='24' hazeColor='white'` → 填充区像素 luma 的标准差显著大于 0
  （无雾时为 0），且均值高于无雾。
- `Haze_IsSoft`：任意相邻像素 luma 差 < 一个阈值（无硬边）。
- `HazeColorRamp_FadesFromAnEdge`：`hazeColor='to top, white, white/0'` → 下 1/4 带的平均 luma 高于上 1/4 带，且上 1/4 带与无雾基线逐像素相等。
- `Haze_DoesNotReachPastTheShape`：`glow='16'` 时形状外侧与无雾基线逐像素相等；`radius='pill'` 的角外同样。
- `Haze_IsUnderTheBorder`：`borderWidth='4' borderColor='red'` → 描边带内像素与无雾基线逐像素相等。
- `TwoPanels_DifferentPositions_DifferentHaze`：同参数两块面板分别放在 `(0,0)` / `(300,0)` → 两张 dump 不相等；
  同一块面板渲染两次 → 相等（确定性）。
- `Intensity_LightsTheHaze`：`intensity='3'` 时雾峰值像素的 R 通道显著高于 `k=1`（发白）。
- `Glass_IgnoresHaze`（`GlassRenderTests`）：`glass='true' haze='40'` 与不写逐像素相等。
- `HazeDrift_MovesWithTheClock`：`hazeDrift='20'`，`SetTimeForTests(0)` 与 `SetTimeForTests(2)` 两张 dump 不相等；
  `hazeDrift='0'` 时相等。
- `Disabled_FreezesTheHaze`：失能面板在 `t=0` / `t=2` 逐像素相等。

## 11. SKILL 更新（同 PR，英文）

- **`authoring-promptugui-xml/SKILL.md`**
  - `<Frame>` 属性表 +3 行（`haze` / `hazeColor` / `hazeDrift`），指向 `reference/haze.md`。
  - 「Which tags draw procedurally」那条 blockquote 的属性列表 + `haze` 一族。
  - 程序化表面小节的 `<Btn>` 示例加参考图的「建造」按钮。
  - **Lighting it up** 加一句：`haze` + `intensity` 是「霓虹雾」；雾在曝光之前。
  - Error codes 表 + `PUI-GLASS-HAZE`。
  - 速查块（`FRAME VISUAL` 一行）加 `haze`。
- **`reference/haze.md`**（新）：噪声是什么（低频 fBm，不是玻璃的 `noise`）；「从一条边渗入」的配方
  （`hazeColor` 的 ramp 就是遮罩，含 `to top, A, 30%, A/0` 的提示写法）；Canvas 空间采样的三个后果
  （相邻不同 / 跨面板连续 / 平移时雾滑过）；`hazeDrift` 的形变而非平移、未缩放时钟；玻璃不吃、失能冻结；
  性能提示（按钮 / 卡片用，全屏背景用图）。
- **`reference/glass.md`**：一句「玻璃不吃 `haze`（`PUI-GLASS-HAZE`）」。
- **`CLAUDE.md`** 触发表：`haze`（`haze` / `hazeColor` / `hazeDrift`）→ `reference/haze.md`。

## 12. 不做的事（YAGNI 记录）

- **覆盖率 / 对比度旋钮**（`hazeCoverage`）—— 常数按参考图标定；`/alpha` 是唯一的强度旋钮（§5.1）。
- **漂移方向**（`hazeDrift="6 45deg"`）—— 三个 octave 的方向是常数；要「向上升腾」的雾等真实需求。
- **玻璃 / 焊接组上的雾** —— backdrop 之上的合成规则 + 第三份 shader（H-D4）。
- **`<Decor>` 上的雾** —— 小件上看不出斑块。
- **`<Image>` / `<Icon>` 上的雾** —— 那是 blur 路径不是 SDF；且 sprite 美学有自己的画法（画进图里）。
- **内层雾**（`handleHaze` / Progress fill）—— 内层没有一个需要它的用例。
- **状态雾**（`hoverHaze`）—— `intensity` 的状态版本 / `<Show on="state-*">` 已覆盖。
- **雾贴在 rect 上而不是 Canvas 上**（面板平移时雾跟着走）—— 需要顶点带位置偏移，且布局期位置未定时构建
  的网格会把相邻控件烘成同一偏移；等有人被 §5.3 的「窗外的云」真正困扰再议。
- **贴图噪声** —— §2。
- **径向 / 多层不同色的雾** —— 叠两个 Frame。

## 13. 决策记录（2026-09-17 对齐）

| 编号 | 决定 |
|---|---|
| H-D1 | 命名 `haze` / `hazeColor` / `hazeDrift`：裸名给特征尺寸 px（`> 0` 开启），`*Color` 给颜色且 `/alpha` 是强度 —— 与 `glow` / `innerGlow` 同构 |
| H-D2 | 方向遮罩**复用渐变语法**：`hazeColor` 是完整色槽，`to top, A, A/0` 就是「从底边渗入」；不加 `hazeFrom=` |
| H-D3 | 同参数的相邻面板**自动不同**：噪声在 Canvas 空间采样（实施定稿：直接用 `IN.worldPosition.xy`，不走顶点数据）；不加 `hazeSeed=` |
| H-D4 | v1 只改 `UI-ProceduralPanel.shader`：全家 `ProceduralControl` 表面；玻璃 / 焊接组归零 + `PUI-GLASS-HAZE`；Decor / 内层不做 |
| H-D5 | `hazeDrift` 进 v1，默认 0；未缩放时钟；失能时冻结（本文定，未单独对齐 —— 与 `intensity` 归 1 同一条理由） |
| H-D6 | `hazeColor` 默认白、不跟随填充（`innerGlowColor` 的同一条理由，本文定） |

## 14. 验收

参考图的两个按钮（§3 前两个片段）在引擎里渲染出来：深底、亮边、底边渗入的几块软边光雾，上半截干净；
`intensity="1.6"` 时雾最亮的一两块泛白；两个按钮的雾各不相同；`hazeDrift="6"` 的那个缓慢形变。
`ProceduralStyle` sample 里并排放 `haze` 关 / 开 / 加 `intensity` / 加 `hazeDrift` 四块，PNG 探针核过后
截图进 §15。

## 15. 实施记录（2026-09-17）

分支 `feat/haze`，三个提交：雾层 + 时钟（M0 + M1 一起，`BuildParams` 的漂移归零与 `Configure` 的
`HazeClock.Ensure()` 把两者绑在一处，拆开反而要留一个半成品的中间态）→ lint / XSD → SKILL / 样例 / 本文。
每步 red 先行：先写 `FrameProceduralPanelTests` 的 haze 组、`ProceduralSurfaceContractTests`、
`HazeRenderTests`（19 条探针）、`HazeClockTests`、四个 lint 测试类与 XSD 测试，编译失败在预期的缺失 API 上，
再补实现。EditMode 4245 / 4245、EditorOnly 347 / 347；`dotnet format --verify-no-changes` 干净；UIXmlLint
CLI 对 `class=` 带进玻璃的 `haze` 报 `PUI-GLASS-HAZE`、对单独的 `hazeColor` 不报。

### 15.1 两处常数改了：起草的映射太密，同轴 value noise 是一张方格

第一张探针图（`color="#101828" haze="24" hazeColor="white"`，120×80）在 0.45 / 0.80 下大半张脸是白的 ——
数值上 50% 的像素沾雾、21% 在半亮以上，但暗底上 w = 0.2 的白已经读成「亮」。而且亮斑是**方的**、沿轴排列：
value noise 的极值在格点上，三个 octave 同轴叠加，方格就露出来了。

在 uv 里用 numpy 逐字复刻 shader 函数（同 hash、同 quintic、同 256 取模）做标定，Python 出图与 Unity 探针
图一致：

| 映射 | 沾雾（w > 0.05） | 半亮（w > 0.5） | 全亮（w > 0.9） | 观感 |
|---|---|---|---|---|
| 0.45 / 0.80 | 50% | 21% | 6% | 密，整面发白 |
| **0.55 / 0.85** | **28%** | **9%** | **2%** | 稀疏软斑、亮核，参考图的密度 |
| 0.60 / 0.90 | 18% | 4% | 1% | 更稀，偏「几缕」 |

Perlin（梯度）噪声也试了：映射到 0..1 后标准差只有 0.07，同一套阈值下是几根细丝，要另调；而 value
noise 只要把第二、三 octave 各转 0.7 / 1.4 rad 并错开原点（`+ (13.1, 7.3)`、`+ (26.2, 14.6)`），方格就消失、
叠成云团，成本不变（仍 12 次 hash）。定稿 = 旋转 octave 的 value noise + smoothstep(0.55, 0.85)。

### 15.2 实施中定下、正文没预料到的几件事

- **周期边界要折两次。** `i` 取过 256 的模后 `i + 1` 仍可能等于 256，右邻 / 上邻格点得再折一次，否则平铺
  处有一条缝（`hazeDrift` 迟早会把它漂进视野）。
- **`HazeClock` 的测试钉。** 渲染探针要在两次快照之间冻结时钟，而 `Canvas.ForceUpdateCanvases()` 会触发
  `willRenderCanvases` → 每帧发布覆盖掉钉住的值；`SetTimeForTests` 因此是「钉住」而不是「写一次」，
  `ResetForTests` 解钉。Edit mode 下 `Time.unscaledTime` 不走，发布的是 `Time.realtimeSinceStartup`。
- **`PanelParams` 的雾槽默认值。** ctor 默认参数只能是 `default(ColorSpec)`（零色标），与面板自己的默认
  （白）不相等；ctor 里把零色标折成白，`HazeColorWithoutHaze_DoesNotSplitTheKey` 才成立。
- **两条探针放宽了。** 「无硬边」的相邻像素 Δluma 上限从 0.3 放到 0.5（小斑块最陡的侧沿约 0.32，硬边是
  0.9）；「从底边渗入」改成比较**下半**与顶带而不是一条 12px 的细带 —— 斑块 24 px 宽，细带可能恰好落在
  两块之间。
- **`gold` 不是命名色。** 例子里的金边改成 `#f2c14e`；库的命名色只有 CSS 基础 16 色 + 几个。

### 15.3 视觉验收

`execute_code` 在宿主工程里按 §3 前两个片段渲染（960×540，相机移到场景之外免得采到 UIPreview 的背景）：
「建造」深底、两端亮中段暗的青边、底边渗入的几团青雾、上半截干净，`intensity="1.6"` 下最亮的一两块泛白；
「造船」金边、底部橙雾往上变稀；同参数的第二个「建造」雾形不同。`HazeRenderTests` 的 dump 逐张看过：
漂移 t=0 → t=2 是形变不是平移，失能后灰而静止，胶囊 + 外发光下雾只在形状内、光晕干净，6px 红描边压在
雾上仍然锐利。`ProceduralStyle` 样例的 Styles 页加了并排四块（无雾 / 雾 / + intensity / + drift）。
