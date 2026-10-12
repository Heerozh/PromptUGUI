# `<Cooldown>` —— 冷却转圈：SDF 径向裁切（扇形暗罩 / 环形弧）+ 自带时钟 + `ready` / `cooling` 事件

> 状态：**设计中**（§12 决策 2026-10-13 与作者对齐，按推荐已定；分支 `feat/cooldown-control`）。
> 相关：
> `2026-05-27-progress-control-design.md`（PB-D6：「radial 不在 v1，未来开 `<Cooldown>` 控件单独做」—— 本文兑现；PB-D1 只读 / 无事件 —— 本文正是因为要事件才不扩 Progress）、
> `2026-09-18-progress-fill-surface-design.md`（fill 是主表面；`value` 是 SDF 里的一刀、参数走 `uv1.zw` 顶点通道、`value=0` 不出几何 —— 本文在同一条通道上加径向码）、
> `2026-08-26-procedural-surface-design.md`（`ProceduralControl` / `ProceduralSurface` / `AddInnerSurface`）、
> `2026-09-17-haze-design.md` §5.5（`HazeClock`：挂 `Canvas.willRenderCanvases` 的全局时钟 + 测试钉表 —— 本文时钟的先例）、
> `2026-08-31-hug-reveal-flip-checked-design.md`（`checked` / `unchecked` 持久对、`<Show>` 登记式、首帧建立 —— `ready` / `cooling` 照抄）。

## 1. 问题

游戏 HUD 的冷却显示几乎都是「转圈」，两种形态：

1. **扇形暗罩**：技能图标上一层半透明暗色，扫掠边从 12 点顺时针走一圈，暗区越来越小，走完即可用（MOBA / MMO 的经典样式）。暗罩要覆盖**整个图标**，方图标的四角也要被扫到。
2. **环形弧**：图标外圈一条弧（buff 剩余时间、蓄力、读条），常带底环、发光的弧头、圆头端点。

两种都要「走表」：给一个总时长，控件自己动，转完时通知（闪一下、让按钮亮起来）；断线重连 / 服务器校时时能从中途接上。

今天做不到：

- `<Progress>` 只有线性方向（`horizontal` / `vertical` / `reverse-*`），PB-D6 明确把径向排除在外；
- 手搭也不行：`<Image type="filled">` 只能设类型，没有 `fillMethod` / `fillOrigin` / `fillAmount` 属性；就算从 C# 设成 `Radial360`，也只能扫位图，扫不了程序化表面（圆角、发光、雾都没有）；
- 没有任何控件自带时钟或「转完」事件 —— 作者得每帧在 C# 里推 `value`，再自己判边沿、自己写倒计时文字。

## 2. 否决的方案

**A. 给 `<Progress>` 加 `mode="radial"`。否决。** PB-D6 的两条理由都还在：径向要 `origin` / `clockwise` / `thickness` / `cap` / `draw`，对线性条全无意义，属性表会一半作废；冷却要时钟和 `ready` 事件，而 Progress 按 PB-D1 是只读显示控件、无事件。再加上 Progress 的 `MaskWrapper` / `frame` / `mask` 四层结构冷却一样都用不上。

**B. 只做纯显示的 `<RadialProgress>`，时钟留给 C#。否决。** 「每帧推 value + 判边沿 + 格式化倒计时」是每个游戏都要写一遍的样板，正是控件该吸收的部分；而且时钟一旦在控件外，`<Animation on="ready">` 这种 XML 侧的反馈就没有事件源。纯推值用法本方案照样支持（`Value = x`），所以这个名字能覆盖的用途（下载环、蓄力）`<Cooldown>` 也覆盖。

**C. 只走 `Image.Type.Filled` + `Radial360`。否决作为主路径。** Unity 自带、零 shader 工作，但只能扫位图：没有圆角 / 发光 / 雾 / 描边，环形要另画一张环形贴图，圆头端点做不到。它保留为**位图 fill 的路径**（§5.5），与 Progress 的位图 `mode="fill"` 同构。

**D. 用 stencil（`Mask`）裁扇形。否决。** 与 fill-surface spec 否决 stencil 的理由相同：stencil 会裁掉外发光，弧头发光与「光晕溢出轨道」都做不出来。

**E. v1 就做 shader 时钟（顶点里写起止时间，shader 用全局时间自己算角度）。推迟。** CPU 零开销、Canvas 不必每帧重合批，是手机上一屏十几个冷却同时转时的正解；但要多塞顶点参数、位图路径照样得 CPU 驱动、`ready` 边沿也仍需 CPU 计时。v1 先走 CPU（§5.9），编码留出余地，实测成问题再做（§11）。

## 3. 方案总览

一个新的叶子控件 `<Cooldown>`，三件事：

1. **渲染**：fill 是主表面（Progress 同款），颜色 fill 永远是 SDF。`value` 不再是一条直线切割，而是**形状 ∩ 扇形** —— 对任何形状都成立（圆角方图标的四角也被扫到、圆、六边形）。加 `thickness` 把实心形状变成沿边框一圈的**环**（onion 运算），于是环形弧也是同一套。描边 / 发光 / 雾都从同一个 `d` 派生，自动沿扇形切边和弧头走。
2. **时钟**：`Start(total[, remaining])` 后控件自己走表（时间戳模型，隐藏时照走），转完置 0 并发 `ready`；也可以像 Progress 一样纯推 `Value`。
3. **事件与文字**：`ready` / `cooling` 是一对持久状态（同 `checked` / `unchecked`），供 `<Animation>` / `<Trigger>` / `<Show>` 使用；`countdown="true"` 内置倒计时数字。

```xml
<!-- ① 技能按钮：图标 + 扇形暗罩 + 倒计时；转好时整颗按钮弹一下 -->
<Animation type="pulse" on="ready" duration="0.3s">
  <Btn id="skill1" size="64x64" radius="10">
    <Icon name="skill:fireball" anchor="stretch"/>
    <Cooldown id="cd" anchor="stretch" radius="10" countdown="true"/>
  </Btn>
</Animation>

<!-- ② buff 环：底环 + 发光弧 + 圆头 -->
<Cooldown id="buff" size="48x48" radius="pill" thickness="4" cap="round"
          bgColor="white/0.15" fillColor="accent" glow="6"/>

<!-- ③ 冷却完成后亮起一圈光：持久状态；裸写 ready 就近绑到兄弟 Cooldown（§5.8） -->
<Btn id="skill2" size="64x64" radius="10">
  <Icon name="skill:heal" anchor="stretch"/>
  <Cooldown id="cd2" anchor="stretch" radius="10" countdown="true"/>
  <Show on="ready"><Frame anchor="stretch" radius="10" borderWidth="2" borderColor="accent" glow="6"/></Show>
</Btn>

<!-- ④ 无进度的「加载中」转圈：不是新功能，Cooldown 定值 + 开屏起无限旋转（rotate 正值为逆时针） -->
<Animation loop="true" easing="linear" rotate="0:-360" duration="1s">
  <Cooldown size="32x32" radius="pill" thickness="3" cap="round" value="0.25" fillColor="accent"/>
</Animation>
```

```csharp
var cd = screen.Get<Cooldown>("cd");
cd.Start(8f);                           // 从满开始，8 秒走完
cd.Start(total: 8f, remaining: 3.2f);   // 断线重连 / 服务器校时：从中途接上
cd.Stop();                              // 立即就绪（触发 ready）
cd.Value = 0.4f;                        // 纯推值：停表，显示 40% 剩余
cd.OnReady.Subscribe(_ => PlayReadySfx()).AddTo(screen);
```

## 4. 作者面

### 4.1 属性表

| 属性 | 取值 | 默认 | 语义 |
|---|---|---|---|
| `value` | float `[0..1]` | `0` | **剩余**比例：1 = 刚开始冷却，0 = 就绪。超界 clamp，NaN → 0。改它会停表（§5.6）。 |
| `origin` | `top` \| `right` \| `bottom` \| `left` | `top` | 扫掠边的起点（也是终点）。 |
| `clockwise` | bool | `true` | 冷却进行时扫掠边的移动方向。 |
| `draw` | `remaining` \| `elapsed` | `remaining` | 画哪一块：`remaining` = 还没扫过的部分（暗罩随时间缩小，经典样式）；`elapsed` = 已扫过的部分（弧随时间长满）。 |
| `thickness` | px（> 0） | — | 写了就是**环**：沿形状边框向内一圈、宽 `thickness` 的带；不写是实心（扇形）。bg 跟着同宽（§5.3）。 |
| `cap` | `flat` \| `round` | `flat` | 环两端的样式。`round` 只在圆环（`radius="pill"` 且 rect 为正方形）上成立，其余形状按 `flat` 画并告警（§5.2、§6）。 |
| `fill` / `fillColor` | sprite key / 颜色（token / `/alpha` / 渐变） | — / `black/0.6` | 主表面的贴图 / 颜色。默认色让 `<Cooldown anchor="stretch"/>` 直接就是能用的暗罩。 |
| `bg` / `bgColor` | sprite key / 颜色 | — | 轨道：完整（不裁）的形状 / 环。两者都没写时 bg 层关闭（Progress 同款计算式显隐）。 |
| `radius` | 同 `<Frame>` | — | 形状。同时给 fill 与**颜色** bg（位图 bg 自带形状，不碰）。 |
| `borderWidth` `borderColor` `glow` `glowColor` `innerGlow` `innerGlowColor` `intensity` `haze` `hazeColor` `hazeDrift` `hazeDensity` `hazeSpace` | 同 `<Frame>` | — | 全部落在 fill（已裁的扇形 / 弧）上，沿切边与弧头走；外发光溢出 rect。 |
| `glass` (+ 参数) | — | — | **不支持**（`PUI-CD-GLASS`，§5.10）。 |
| `time` | `scaled` \| `unscaled` | `scaled` | 走表用的时钟：`scaled` 随 `timeScale`（暂停时冷却也停）；`unscaled` 不受影响。 |
| `countdown` | bool | `false` | 显示剩余秒数（§5.7）。只在走表时有字。 |
| `tenthsBelow` | 秒（≥ 0） | `1` | 剩余少于这么多秒时显示一位小数；`0` = 永不显示小数。 |
| `font` · `fontSize` · `textColor` | 同 `<Btn>` | `default` · `24` · `white` | 倒计时文字。 |

叶子控件：不接子元素（`PUI-CD-CHILDREN`）。`raycastTarget` 永远是 `false`，它叠在 `<Btn>` 上，绝不能挡点击（与 `<Progress>` / `<Icon>` / `<Decor>` 同列，`PUI-RAYCAST-TAG`）。`GetNativeSize()` = `64×64`。

### 4.2 图层结构（固定，Strategy C）

```
Cooldown (RectTransform)
├─ Bg     (Image；bg / bgColor 都没写时 SetActive(false)；颜色时挂 inner surface)
├─ Fill   (Image；主表面 —— 颜色 fill 挂 ProceduralSurface，位图 fill 走 Image.Filled)
└─ Label  (TMP；countdown="false" 时 SetActive(false))
```

三个节点在 `OnAttached` 一次建好，之后只切 active / enabled，从不 Destroy —— Variant、主题切换、`countdown` 开关都不重建。

### 4.3 `ready` / `cooling` 事件

| `on=` | 事件 | 作为 `<Show>` 状态 |
|---|---|---|
| `ready` · `ready@<id>` | 冷却结束（剩余从 > 0 变成 0） | 就绪期间显示 |
| `cooling` · `cooling@<id>` | 冷却开始（剩余从 0 变成 > 0） | 冷却期间显示 |

- 与 `checked` / `unchecked` 同构：**持久对**，`<Animation on="ready" reverse-on="cooling">` 是一对；边沿触发 `<Animation>` / `<Trigger>`；`<Show>` 是登记式，按当前状态显隐，没有 Normal 兜底。
- **首帧建立**：Screen 打开时 Cooldown 已处于某状态（通常是就绪），对应 `on=` 的 `<Animation>` **落在终态而不播放**（同 `checked` 的首帧规则）—— 所以 `<Animation on="ready">` 不会在开屏时闪一下；普通 `<Trigger>` 照常 fire（同 `checked`）。
- `Start` 一个已经在冷却的 Cooldown（重新校时）不会再发 `cooling`；`Stop` / `Value = 0` 一个已经就绪的不会再发 `ready`。只有状态翻转才是边沿。
- 事件源解析见 §5.8：裸写找**就近**的 `<Cooldown>`，`@id` 走词法作用域。

### 4.4 C# API

```csharp
public sealed class Cooldown : ProceduralControl
{
    [UIAttr] public float Value { get; set; }   // 剩余比例；set 会停表
    public void Start(float total);              // = Start(total, total)
    public void Start(float total, float remaining); // remaining ≤ 0 等同 Stop()；remaining > total 时 clamp 到 total
    public void Stop();                          // 立即就绪
    public bool  IsCooling { get; }              // Value > 0
    public bool  IsRunning { get; }              // 正由时钟驱动
    public float Total     { get; }              // 秒；纯推值模式下为 0
    public float Remaining { get; }              // 秒；纯推值模式下为 0
    public Observable<Unit> OnReady   { get; }   // 剩余 > 0 → 0
    public Observable<Unit> OnCooling { get; }   // 剩余 0 → > 0
    public Func<float, string> CountdownFormatter { set; } // 自定义倒计时文字；null = 默认格式
}
```

- `OnReady` 是 **UI 事件**：用来闪光、播音效、点亮按钮。玩法上的「技能能不能放」以游戏自己的计时 / 服务器状态为准 —— 控件的时钟可以被暂停（`time="scaled"`）、可以随 Screen 关闭而消失。
- 服务器权威的冷却：每次同步调 `Start(total, remaining)`，控件在两次同步之间本地插值。
- **不**自动禁用父 `<Btn>`（§12-f）：有的游戏冷却中点击要弹「冷却中」提示，这由游戏逻辑决定。

### 4.5 复用 / 主题 / Variant

全部是属性：`<Style>` + `class=`、`<Theme>`、`.variant` 后缀照旧。注册时 `runtimeStateAttr: "value"`，所以正在走表的 Cooldown 在 Variant ReSolve（横竖屏切换等）时不会被 XML 的 `value` 写回 —— 与 `Slider.value` / `Progress.value` 同一机制。

## 5. 语义细节

### 5.1 扫掠几何

记剩余比例 `r`，已扫角 `s = (1 − r) · 360°`，扫掠边位于从 `origin` 出发、沿扫掠方向（`clockwise`）转过 `s` 处。

| `draw` | 画出的扇区（沿扫掠方向度量，`origin` = 0°） | 画出的比例 `f` |
|---|---|---|
| `remaining` | `[s, 360°]` —— 扫掠边到 `origin` | `r` |
| `elapsed` | `[0°, s]` —— `origin` 到扫掠边 | `1 − r` |

- `f = 0` → **不出几何**（同 Progress 的 `value=0`：连发光都没有）；`f = 1` → 不裁（完整形状 / 完整环）。
- 扇区关于自己的中心线对称，所以送进 shader 的只有**中心角**和**半张角** `f · 180°`；`clockwise` / `draw` 在 C# 里折算掉，shader 不知道方向。
- 默认（`origin="top" clockwise="true" draw="remaining"`）即经典暗罩：扫掠边从 12 点顺时针走，暗区是扫掠边到 12 点之间顺时针的那一块。

### 5.2 SDF 径向裁切

沿用 fill-surface spec §5.2 的通道：`uv1.zw = (code, e)`，四个顶点同值，`value` 变化只 `SetVerticesDirty()`、不动材质。新增码段：

| `code` | 含义 | `e` |
|---|---|---|
| `0` | 不切（不变） | — |
| `±1..±4` | 线性 flat / round（不变） | 切线偏移（px） |
| `[5, 6)` | 径向，平端；`code − 5` = 扇区中心角 / 360°（0 = 正上，顺时针增） | 半张角（弧度，`0..π`） |
| `[7, 8)` | 径向，圆头（只用于圆环，见下） | 同上 |

现有判定要收紧到各自码段：`PuguiCutIsRound` 由 `abs(code) > 2.5` 改为 `2.5 < a < 4.5`，`PuguiCutTerm` 的 flat 判定不变（`0.5 < a < 2.5`）—— 否则径向码会被当成线性 round 码。

**平端（扇形 / 平头弧）**：把 `p` 旋到扇区中心线 = +y 上，取 iq `sdPie` 的无限半径形式作扇区的有符号距离，与形状求交：

```hlsl
// c = (sin h, cos h)，h = 半张角；q = p 绕中心转到扇区中心线为 +y
q.x = abs(q.x);
float m = length(q - c * max(dot(q, c), 0.0));
float dSector = m * sign(c.y * q.x - c.x * q.y);
d = max(dShape, dSector);
```

对 `h ∈ (0, π]` 精确（`h = π` 时整平面在内）；与形状的 `max` 交集在外侧楔形区略低估距离，与线性切割已接受的近似相同。与线性切割一样**用 select 不用分支**算切割项（`d` 之后要过 `fwidth`）。

**圆头**：只对圆环成立。C# 判定「圆环」= `thickness > 0` 且形状是圆（rect 宽高差 < 0.5px，且 `radius` 为 `pill` 或 ≥ 短边一半）；成立时发 `[7, 8)` 码，shader 用 iq `sdArc` 直接给出带圆头的弧：

```hlsl
// ra = R − t/2（环中线半径，R = min(b)），rb = t/2
q.x = abs(q.x);
d = ((c.y * q.x > c.x * q.y) ? length(q - c * ra) : abs(length(q) - ra)) - rb;
```

不成立（实心、或非圆环写了 `cap="round"`）时 C# 发平端码，`UILog.Warn` 一次；lint 在能静态看出的情形（无 `thickness`、`radius` 不是 `pill`）报 `PUI-CD-ROUND-CAP`（§6）。非圆形状的圆头需要在 C# 里求扫掠边与环中线的交点，v1 不做（§11）。

下游不改：`inside`、填充、雾、内发光、外发光、描边全部吃新的 `d`。渐变仍按整个 rect 线性铺（`PuguiGradient` 拿原 `p` / `b`）。

### 5.3 `thickness`：环

形状 SDF 先做 onion，得到沿边框向内、宽 `t` 的带，再做 §5.2 的裁切：

```hlsl
if (_RingWidth > 0.0) dShape = abs(dShape + _RingWidth * 0.5) - _RingWidth * 0.5;   // uniform 分支
```

- 对**任何形状**成立：圆 → 圆环，圆角方 → 圆角方环，六边形 → 六边环。
- `t` 是**材质参数** `_RingWidth`（进 `PanelParams` 键，同 `borderWidth`）：它是样式而不是逐实例状态，同一个 `class=` 的环共享材质。没有它（0）的面板逐位不变。
- 与 `borderWidth` 不是一回事：描边是沿最终形状（含扇形切边）画的一圈；环是形状本身。环上的 `borderWidth` 给弧描边。
- bg 是颜色层时同样拿 `_RingWidth`（不裁），即「底环」。位图 bg 不碰。
- `thickness` ≥ 短边一半时退化为实心，不报错。

### 5.4 fill / bg 与 `radius`

比 Progress 简单（没有 mask，没有 frame）：

- **颜色 fill 永远是 SDF。** Unity `Image` 没有 sprite 时会忽略 `type = Filled`、画整个 rect，所以径向裁切只能在 SDF 里做。Cooldown 在 `fill` 不是位图时**总是**声明主表面（不写 `radius` 也一样，等价 `radius="0"`）。
- **位图 fill**（`fill="ui:x"`）：Image 留任，走 §5.5。此时任一程序化属性（含 `radius`、`thickness`、`cap`）与之矛盾 —— `radius` 与效果属性走既有的 `PUI-PROC-SPRITE-CONFLICT`（程序化赢、位图让位）；`thickness` / `cap` 是 Cooldown 自己的属性，在位图 fill 上被忽略并报 `PUI-CD-BITMAP-SHAPE`（warning）。
- `radius`：fill（非位图时）+ 颜色 bg 各自 `Declare`。与 Progress 一样覆盖 `DeclareRadius` 只记录、在 `OnAfterApply` 里 `base` 之前分发 —— 「fill 是不是位图」要等这一遍 apply 结束才知道（属性顺序未定义）；区别只是没有 mask 这第三个消费者。「颜色 fill 永远是 SDF」也在这里落实：fill 不是位图时，即使没写 `radius` 也 `Declare` 一次（`radius = 0`）。
- bg 显隐 = `_bgSprite || _bgColor`，计算式（`bg=""` 能关回去）。

### 5.5 位图路径（`Image.Type.Filled` + `Radial360`）

| | `fillOrigin` | `fillClockwise` | `fillAmount` |
|---|---|---|---|
| `draw="remaining"` | `origin` → `Origin360.Top/Right/Bottom/Left` | `!clockwise` | `r` |
| `draw="elapsed"` | 同上 | `clockwise` | `1 − r` |

（`remaining` 的扇区是「从 `origin` 逆着扫掠方向转 `r · 360°`」，所以填充方向取反。）像素风等纯贴图主题由此拿到冷却：暗罩用一张纯色方块 sprite，环用一张环形 sprite。贴图主题 ↔ SDF 主题的切换与 Progress 一样双向复位：回到 SDF 时 `type` 复位、`fillAmount = 1`；回到位图时面板 `ClearCut`。

### 5.6 时钟

**时间戳模型。** 走表状态是 `(_total, _end, _clock)`：`remaining = max(0, _end − now)`，`r = remaining / _total`。不累加 `deltaTime`，所以掉帧、隐藏、重新校时都不会累积误差。

| 调用 | 效果 |
|---|---|
| `Start(total, remaining)` | `_total = total`，`_end = now + min(remaining, total)`；进入走表。`remaining ≤ 0` 或 `total ≤ 0` = `Stop()`。 |
| `Stop()` | 停表，`r = 0`。 |
| `Value = x`（含 XML `value=`） | 停表，`r = clamp01(x)`；`Total` / `Remaining` 归 0（纯推值模式不知道秒数）。 |
| 走到 `remaining = 0` | 停表，`r = 0`。 |

所有路径的状态翻转（`r > 0` ↔ `r = 0`）统一在一处判边沿、发 `OnReady` / `OnCooling` 和 `ready` / `cooling` 触发器 —— 纯推值时同样有边沿，所以服务器推送的比例也能驱动 `<Animation on="ready">`。

**`now`**：`time="scaled"` → `Time.time`；`unscaled` → `Time.unscaledTime`；不在 Play 模式时两者都用 `Time.realtimeSinceStartup`（`HazeClock` 同理）。`time` 在走表中途被改（Variant）时，按旧时钟算出当下的 `remaining`，换到新时钟上重新起表 —— 剩余秒数不跳。

**驱动**：一个内部静态 `CooldownClock` 持有「正在走表」的集合，每帧统一 tick 一次：刷新显示、判结束、发边沿，结束的移出集合。它**不依附 GameObject 的激活状态**：Cooldown 所在页被 `<TabBar>` / Router 隐藏时照样走表、照样到点发 `ready`；隐藏期间不刷新画面，重新激活（`OnEnable`）时按当下时间重算一次。挂点用 `Canvas.willRenderCanvases`（`HazeClock` 先例：每帧一次、在 canvas 绘制之前、`Canvas.ForceUpdateCanvases` 也会触发，EditMode 测试可驱动）；M2 要实测「所在 Screen 整体隐藏（Canvas 失活）时它仍逐帧触发」，不成立就改为 PlayerLoop 注入，契约不变。测试钉表：`CooldownClock.SetTimeForTests(t)`（`scaled` / `unscaled` 同钉），`ResetForTests` 时清空集合与钉表（由 `UI.ResetForTests` 调用）。

**生命周期**：`Dispose`（Screen 关闭、行回收）把自己移出集合；之后不会再发 `OnReady`。

### 5.7 倒计时文字

`countdown="true"` 且走表时显示；纯推值模式（没有秒数）与就绪时为空。默认格式，取 `remaining` 秒：

| 剩余 | 显示 | 例 |
|---|---|---|
| `< tenthsBelow` | 一位小数，**向上**取整到 0.1 | `0.43` → `0.5` |
| `< 60` | 整秒，向上取整 | `7.2` → `8` |
| `≥ 60` | `m:ss`（按向上取整后的整秒） | `89.4` → `1:30` |

向上取整保证显示到 0 的那一刻就是就绪的那一刻（不会挂着一个「0」）。数字和 `:` 与语言无关，不走 i18n；要「2m」这类本地化写法，用 `CountdownFormatter`。

**只在字符串变化时写 TMP**：整秒段每秒一次，小数段每 0.1 秒一次，不是每帧 —— TMP 重排远比四个顶点贵。Label 居中、stretch；要放到别处的倒计时，作者自己摆 `<Text>`，在 C# 里读 `Remaining`。

`FontFloorRules.TextTags` 加 `Cooldown` 行：默认 24，**仅在 `countdown="true"` 时计入**（今天的 `OnlyWithText` 换成一个「看哪个属性」的条件）；`FontFloorDefaultsTests` 对着真控件钉住默认值。

### 5.8 事件源解析

Cooldown 是叶子，通常与它装饰的东西做兄弟（图标、按钮内的高光），所以既有的两种解析都不够：向下找子树（`click`）找不到兄弟，向上找祖先（`checked`）永远找不到叶子。新规则 —— **就近**：

1. 裸 `ready` / `cooling`：先找触发器自己的子树；没有，再逐级向外，每一级找**该祖先整棵子树**里的 `<Cooldown>`，在**第一个找到任何 Cooldown 的层级**停下：恰好一个 → 绑它；多于一个 → 报错，要求写 `@id`；一直到 Screen 根都没有 → 报错。
2. `ready@<id>` / `cooling@<id>`：`TriggerSourceResolver.ResolveId` 的词法作用域（子树 → 外层模板实例 → Screen），类型须是 `<Cooldown>`。

于是 §3 的例 ①（Animation 包着按钮，Cooldown 在子树里）与「`<Show on="ready">` 写在 Cooldown 的兄弟位置」都不必写 `@id`；一个面板里放了多颗技能、写错层级时会得到明确的歧义报错而不是悄悄绑错。lint 镜像同一规则：`PUI-CD-NO-SOURCE`（找不到）/ `PUI-CD-AMBIGUOUS`（同层多个），Template 体与 `@id` 形式留给运行时（同既有 `*-NO-SOURCE` 规则的豁免）。

### 5.9 性能

- 每个**可见且在走表**的 Cooldown 每帧：重算 `(code, e)`、`SetVerticesDirty()`（4 个顶点），所在 Canvas 因此每帧重新合批。这与 uGUI 里用 `Image.fillAmount` 做冷却的常规做法同量级。
- 材质不变：`value` 走顶点，`thickness` / 颜色走材质键，同 `class=` 的 Cooldown 共享一个材质，合批不被打断。
- 倒计时文字按 §5.7 节流。
- 隐藏的 Cooldown 只做一次减法和比较，不碰网格。
- 大量常驻冷却的 HUD 若在 profiler 里看到合批开销，后续做 shader 时钟（§2-E / §11）；skill 里提一句「频繁变化的 HUD 放进自己的子 Canvas」这一 uGUI 通用建议。

### 5.10 不支持 `glass`

玻璃采的是 **canvas 后面的场景**（backdrop 不含 UI）。叠在图标上的玻璃暗罩显示的是场景而不是模糊的图标；玻璃环也没有可信的用途。两份面板 shader 里只有 `UI-ProceduralPanel.shader` 需要径向码与 onion；`UI-GlassPanel.shader` 只吃 §5.2 收紧后的谓词（径向码对它而言就是「不切」）。Cooldown 上写 `glass`（直接或经 `class=`）：lint `PUI-CD-GLASS`（error）；运行时 Cooldown 在 `OnAfterApply` 里于 `base` 之前补一条 `SetGlass(false)` 声明（后声明者赢）并 `UILog.Warn` 一次 —— 不会走到画不出径向的玻璃 shader 上。

## 6. 报错（`Core/Lint/CooldownRules.cs` + 既有规则扩展）

| Code | 触发 | 级别 |
|---|---|---|
| `PUI-CD-VALUE-RANGE` | `value` 字面量超出 `[0..1]` | warning |
| `PUI-CD-ENUM` | `origin` / `draw` / `cap` / `time` 不在各自取值集合内（消息列出合法值） | error |
| `PUI-CD-CHILDREN` | `<Cooldown>` 有子元素 | error |
| `PUI-CD-ROUND-CAP` | `cap="round"` 但没有 `thickness`，或 `radius` 不是 `pill`（圆头只在圆环上成立，否则按平端画） | warning |
| `PUI-CD-BITMAP-SHAPE` | 位图 `fill=` 上写了 `thickness` / `cap`（被忽略：位图自带形状，`Radial360` 只扫它） | warning |
| `PUI-CD-GLASS` | `glass`（直接 / 经 `class=` / 变体后缀） | error |
| `PUI-CD-TENTHS` | `tenthsBelow` 为负或非数 | error |
| `PUI-CD-NO-SOURCE` / `PUI-CD-AMBIGUOUS` | 裸 `ready` / `cooling` 按 §5.8 找不到 / 同层多个（Template 体、`@id` 豁免） | error |
| `PUI-PROC-SPRITE-CONFLICT` | （扩展）位图 `fill=` + 任一程序化属性，**含 `radius`**（没有 mask 接 `radius`） | error |
| `PUI-RAYCAST-TAG` | （扩展）`<Cooldown raycastTarget>` —— 永远穿透 | error |
| `PUI-FONT-FLOOR` | （扩展）`countdown="true"` 的倒计时文字 | 同既有 |

扩展点：`BuiltinTags` 加 `Cooldown`；`RaycastRules` 的永远穿透列表加 `Cooldown`；`ProceduralSurfaceRules` 的程序化标签列表加 `Cooldown`、`PrimarySprite["Cooldown"] = "fill"`（`RadiusSparesTheBitmap` 不含它）；`StateTriggerRules` 加 `ready` / `cooling` 的就近解析；`IRWalker` 分派 `CooldownRules`。全部纯 C#，CLI 与运行时 warning 路径共用。

## 7. 实现地图

| 处 | 改动 |
|---|---|
| `Runtime/Resources/PromptUGUI/Material/UI-PanelSDF.cginc` | 线性谓词收紧到各自码段；新增 `PuguiCutIsRadial` / `PuguiSdSector`（sdPie 无限半径）/ `PuguiSdArcRound`（sdArc）/ `PuguiOnion`；`PuguiSdCut` 认得径向码（select，不分支）。 |
| `UI-ProceduralPanel.shader` | `_RingWidth` 属性；`dShape` 先 onion 再切；圆头码直接用 sdArc 的 `d`。 |
| `UI-GlassPanel.shader` | 不加 `_RingWidth`、不画径向；只因 cginc 里线性谓词收紧而受影响（径向码对它是「不切」）。 |
| `Controls/Internal/ProceduralPanel.cs` | `SetRadialCut(float centreTurns, float halfAngle, bool roundCap)`（只 `SetVerticesDirty`，`OnPopulateMesh` 写 `uv1.zw`；`halfAngle = 0` → `_cutEmpty`；`π` → 不切）；`SetRingWidth(float)` 进 `BuildParams`。 |
| `Controls/Internal/ProceduralMaterialCache.cs` | `PanelParams.RingWidth` + 键 + `_RingWidth` 写入。 |
| `Controls/Internal/CooldownClock.cs`（新） | §5.6 的静态驱动 + 测试钉表 + `ResetForTests`。 |
| `Controls/Internal/ICooldownSource.cs`（新） | `IsCooling` / 边沿流 / `RegisterShow`（`IToggleSource` 同构），供 Trigger / Show 使用。 |
| `Controls/Cooldown.cs`（新） | §4 的属性与 API；`SurfaceHost => _fill`；`BgSurface`（inner，收 radius + ring）；`OnAfterApply` 定 fill 是否位图 → 声明主表面 / radius / ring / glass 抑制；`ReconcileFill`（SDF：`SetRadialCut`；位图：Filled + Radial360 表）；倒计时 Label 节流；`runtimeStateAttr: "value"`。 |
| `Controls/Internal/TriggerSpec.cs` | `TriggerKind.Ready` / `Cooling`，裸写与 `@id` 两种解析；`ParseReverseOn` 接受它们。 |
| `Controls/Internal/TriggerSourceResolver.cs` | `FindCooldown(trigger, sourceId)`：§5.8 就近规则。 |
| `Controls/Trigger.cs` · `Animation.cs` · `Show.cs` | 订阅 / 首帧建立 / `<Show>` 登记（照 `checked` 路径）。 |
| `Application/BuiltinPrimitives.cs` | `reg.Register<Cooldown>("Cooldown", null, runtimeStateAttr: "value")`。 |
| `Application/UI.cs` | `ResetForTests` 调 `CooldownClock.ResetForTests`。 |
| `Core/Lint/*` | §6。 |
| `Editor/`（XSD） | 反射生成，无手改。 |

不动：`Progress.cs`（线性码的 C# 编码不变）、`Slider.cs`、`DecorPanel` / `GlassGroupPanel`（不走切割）。

## 8. 测试（Red 先行）

**shader / panel（`ProceduralCutTests` 的 ReadPixels 套路，新 `ProceduralRadialCutTests`）**
- 方形 rect、无 radius、`f = 0.25`、中心角 = 右上象限中线：右上象限内像素 = 填充色，其余三象限 = 透明；**右上角那个角点**附近（离圆心 > 半边长）也是填充色 —— 扫的是方形不是内切圆。
- `f = 0.5` 半张角 = π/2：恰好半边；`f = 1` 与不切逐像素相等；`f = 0` 无几何（含发光）。
- 外发光沿扇形两条切边溢出；描边沿切边画。
- `_RingWidth = 6`，圆：圆心透明、环带内是填充色；`f = 1` 是完整圆环；圆角方 + ring：四边带宽一致。
- 圆头码：弧端外侧 `rb` 内的像素有填充（平端码同一处透明）。
- 线性码 `±1..±4` 的现有 `ProceduralCutTests` 全部照跑（谓词收紧不改它们）。
- 材质：同 `thickness` 的两个面板共享材质；`SetRadialCut` 前后材质引用不变。

**Cooldown 控件（`CooldownTests` / `CooldownClockTests`）**
- 结构：`Bg` / `Fill` / `Label` 三节点；默认 `Bg`、`Label` 不激活；raycast 全关。
- `origin` × `clockwise` × `draw` 的折算：给定 `r`，`SetRadialCut` 收到的中心角 / 半张角符合 §5.1 表（逐组合断言）；位图路径的 `fillOrigin` / `fillClockwise` / `fillAmount` 符合 §5.5 表。
- 颜色 fill 不写 radius 也是 SDF（主表面激活）；位图 fill 时 Image 留任、`type == Filled`、`fillMethod == Radial360`。
- 主题往返：SDF → 位图 → SDF，`type` / `fillAmount` / cut 正确复位，无 Destroy。
- 时钟（钉表）：`Start(8)` 后钉到 +2s → `Value == 0.75`、`Remaining == 6`；钉到 +8s → `Value == 0`、`OnReady` 恰好一次、`IsRunning == false`；`Start(8, 3.2)` → `Value == 0.4`；中途 `Start` 不发 `OnCooling`；`Stop` 发一次 `OnReady`；`Value = 0.4` 停表并发 `OnCooling`（若之前就绪）；`time="unscaled"` 只看 unscaled 钉表；`time` 中途切换剩余秒数不变。
- 隐藏：`SetActive(false)` 后照样到点发 `OnReady`；重新激活后网格是就绪态。
- `Dispose` 后不再发事件，集合里没有它。
- ReSolve：走表中翻 Variant，`value` 不被 XML 写回、时钟不停（`runtimeStateAttr`）。
- 倒计时：§5.7 表逐行（含 `tenthsBelow="0"`、`59.2 → 1:00`、`0.01 → 0.1`）；TMP 文本在同一整秒内多次 tick 只写一次；`CountdownFormatter` 覆盖默认；纯推值时为空。
- glass：`glass="true"` 不让主表面走玻璃 shader，告警一次。

**触发器（`TriggerCooldownTests`，照 `checked` 的用例组织）**
- `<Animation on="ready">` 包着含 Cooldown 的 Btn：冷却结束时播放；开屏时（已就绪）落在终态不播放；`reverse-on="cooling"` 在 `Start` 时反向。
- `<Show on="ready">` 写在 Cooldown 的兄弟位置：按状态显隐；`<Show on="cooling@cd">` 同理。
- 就近规则：同层两个 Cooldown → 歧义异常；子树里有一个时优先它；整屏没有 → 异常。
- `<Trigger on="ready">` 的 `OnFire` 次数。

**lint（`CooldownRulesTests`、`StateTriggerRules` / `RaycastRules` / `ProceduralSurfaceRules` / `FontFloorRules` 的扩展用例）**
- §6 每一行至少一个正例、一个反例；`class=` 带来的 `glass` 同报；`FontFloorDefaultsTests` 钉住 Cooldown 默认字号 24，`countdown` 未开时不计入。

## 9. 演示同步（同 PR）

- `Samples~/CommonControls` 的「展示反馈」页：在 Progress 旁加一组冷却 —— 一颗技能按钮（图标 + 暗罩 + 倒计时，点击 `Start(5)`）、一个发光的 buff 环、一个加载转圈（§3 例 ④）；`CommonControlsRunner.cs` 加点击即开始冷却的几行。
- `Samples~/ProceduralStyle`：皮肤文件里给 Cooldown 一个 `class`（环 + glow），证明主题能换掉冷却的外观。

## 10. SKILL / 文档更新（同 PR，英文）

- **新** `authoring-promptugui-xml/reference/controls-cooldown.md`：两种形态的配方、扫掠几何表（`origin` / `clockwise` / `draw`）、环与圆头的限制、位图路径、`ready` / `cooling` 与就近解析、倒计时格式、性能提示、lint 表。
- `authoring-promptugui-xml/SKILL.md`：built-in 目录加 `<Cooldown>` 行；`### <Cooldown>` 小节（属性表 + 指向 reference）；「哪些标签画程序化」列表与主表面表加 Cooldown（fill 是主表面、无 glass）；raycast 段的永远穿透列表加 Cooldown；`:1901` / `:1975` 两处「Radial 冷却环不在范围内」改为指向 `<Cooldown>`。
- `reference/animations.md`：`on=` 表加 `ready` / `cooling`（含 `@id`）与就近解析；首帧建立段把 `ready` / `cooling` 并入 `checked` / `unchecked`。
- `reference/controls-progress.md:7`：改为链接 `controls-cooldown.md`。
- `scripting-promptugui-csharp/SKILL.md`：`Cooldown` 的 API（`Start` / `Stop` / `Value` / `OnReady` / `OnCooling` / `CountdownFormatter`），「`OnReady` 是 UI 事件」与「服务器校时用 `Start(total, remaining)`」两条。
- 主 spec `2026-05-07-...-design.md` §5 控件表加 `<Cooldown>` 行，链接本文；`2026-05-27-progress-control-design.md` PB-D6 行加「已由 `2026-10-13-cooldown-control-design.md` 兑现」。
- `AGENTS.md` 的 SKILL 路由表加一条：`<Cooldown>` → `reference/controls-cooldown.md`（`ready` / `cooling` 同时改 `animations.md` 的 `on=` 表）。

## 11. 不做的事（YAGNI 记录）

- shader 时钟（§2-E）—— 实测合批成问题再做；编码上径向码只占 `[5, 8)`，后续码段有余地。
- 非圆形状的圆头端点（要在 C# 求扫掠边与环中线的交点）。
- 角度渐变（沿弧的 conic 渐变，「弧头亮、弧尾暗」）—— 今天渐变只有线性。
- 部分弧量程（仪表盘式 270° 弧，`sweep=`）。
- 多层充能（「2 层」叠数）、分段刻度环 —— 层数是一个 `<Text>`，刻度用 `<Decor>`。
- 暂停单个冷却（`Pause` / `Resume`）—— 全局暂停走 `time="scaled"`，个别暂停用 `Start(total, remaining)` 重新校时。
- 冷却期间自动禁用父 `<Btn>`（§12-f）。
- 无进度转圈专用控件（§3 例 ④ 已覆盖）。
- `glass`（§5.10）。

## 12. 决策记录（2026-10-13 与作者对齐，按推荐已定）

- **a. 名字 `<Cooldown>`**（PB-D6 预留），不用中性的 `<RadialProgress>`：按需求找控件的人一眼能找到；纯推值用法同样覆盖下载环 / 蓄力。
- **b. 时钟进 v1**：`Start` / `Stop` / `OnReady`，这是它与 Progress 唯一真正不同的地方。
- **c. 倒计时数字内置**（`countdown="true"`），整秒 / 一位小数 / `m:ss`，只在字符串变化时更新；自定义走 `CountdownFormatter`。
- **d. 默认 `time="scaled"`**：冷却是玩法时间，游戏暂停时一起停；`unscaled` 可选。
- **e. 圆头只在圆环上**：其余形状按平端画 + 告警。
- **f. 不自动禁用父 `<Btn>`**：交给游戏逻辑。
- v1 走 CPU 驱动（每帧 4 个顶点），shader 时钟留作优化。
- 无进度的加载转圈不做新东西，写进 skill 示例（`<Animation on="loop" rotate>` 包一个定值 Cooldown）。

## 13. 里程碑拆分

| M | 内容 | 依赖 |
|---|---|---|
| M0 | shader：谓词收紧、径向码（平端 / 圆头）、onion + `_RingWidth`；`ProceduralPanel.SetRadialCut` / `SetRingWidth`；`PanelParams.RingWidth`；render 测试 + 现有线性切割回归 | — |
| M1 | `Cooldown` 控件：结构、属性、扫掠折算、SDF / 位图两路径与主题往返、glass 抑制、注册 | M0 |
| M2 | `CooldownClock` + `Start` / `Stop` / 边沿 / `OnReady` / `OnCooling` / 隐藏照走 / ReSolve；倒计时 Label；验证 `willRenderCanvases` 在 Canvas 失活时的触发（不成立改 PlayerLoop） | M1 |
| M3 | `ready` / `cooling` 触发器：`TriggerSpec`、就近解析、Animation 首帧建立、`<Show>` 登记、`<Trigger>` | M2 |
| M4 | lint：`CooldownRules` + 既有规则扩展 + `FontFloorRules` 行；CLI 跑遍 `Runtime/Resources` 与 `Samples~` | M1–M3 |
| M5 | 样例 + SKILL（新 reference + 主 SKILL + animations + progress 指针 + C# skill）+ 主 spec / PB-D6 指针 + `AGENTS.md` 路由 | M1–M4 |
