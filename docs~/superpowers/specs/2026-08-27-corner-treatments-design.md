# 角部处理：cut / notch / hexagon —— 程序化表面的形状词汇（第一层）

> 状态：**已实现**（M0–M3 一轮做完，见 §11）。本文保留设计推理与实施记录。
> 相关：`2026-08-23-procedural-style-design.md`（`radius` 语法的出处，§2.1/§2.2）、
> `2026-08-26-procedural-surface-design.md`（`__Surface` / 内层 `<layer>Radius` / mask="self"）、
> `2026-08-23-glass-fill-design.md`（玻璃填充与边缘光照）、
> `2026-08-27-theme-procedural-shape-exemption-design.md`（SHAPE 规则）。

## 1. 问题

程序化表面只会画圆角矩形。`PuguiSdRoundBox`（`UI-PanelSDF.cginc`）是整套形状系统唯一的
SDF —— 四角独立半径加 `pill`，到此为止。而科幻 HUD 风格（参考需求图：星海指挥官主界面）的
形状词汇是：

- 左右切成尖端的六边形主按钮（「开始匹配」横幅）；
- 上两角斜切的梯形 Tab（底部导航）；
- 斜切角卡片、方形缺口面板（军事/机械 HUD 惯用语）。

这些形状今天在这个库里**做不出来**。作者唯一的退路是贴图（sprite / `.pxl`）——而贴图皮肤
恰恰被 #104 刚打通的「主题换形状」排除在外：主题能把一个控件从像素风换成玻璃圆角，
却换不成玻璃切角。程序化表面的能力半径就是主题系统的能力半径，圆角矩形是当前的硬边界。

**分层设计的位置**：这是形状扩展的第一层（角部处理词汇表）。第二层（边缘装饰原语
bracket / tick 等）与第三层（任意路径逃生舱）另行立项，本文不涉及。

## 2. 否决的方案

**SVG / 任意路径作为主接口。否决，三条理由，第一条是硬的。**

1. **打断整条 SDF 派生链。** border 向内描、glow 向外晕、glass 的折射与边缘光照
   （`PuguiSdNormal` 解析法线）、`mask="self"` 的实心区裁剪、weld 融合、
   `ProceduralMaterialCache` 的材质共享 —— 全部派生自解析 SDF。任意贝塞尔要么曲面细分成
   mesh（上述全灭，mesh 给不出法线），要么烘焙 SDF 纹理（法线退化为差分采样、材质按纹理
   失效、需要整条烘焙管线）。#104 建立的「一份 cginc 同时喂不透明面与玻璃面」的架构会碎掉。
2. **没有拉伸语义。** SDF 参数以画布单位表达，角是角、边是边，任意尺寸下锐利（`radius`
   的既有行为）。SVG viewBox 一拉伸角就变形，要自己发明路径版 9-slice。
3. **LLM 写 SVG 语法流利，坐标是盲画的。** 没有渲染反馈闭环时摆坐标成功率很低（`.pxl`
   工作流配 PxlPreview 正是为此）。从枚举词汇表选参数是 LLM 最可靠的操作方式，还能进
   纯 C# 的 UIXmlLint。

**独立 `corner=` 属性 / CSS `corner-shape` 双属性镜像。否决。** `radius` 的值语法被五个
属性共用（`radius` / `fillRadius` / `handleRadius` / `frameRadius` / `maskRadius`），扩展
值语法让五者一次获得能力、一个解析器改动；独立属性意味着属性面积 ×5 加并存冲突规则。
双属性镜像（尺寸列表 + 形状列表按位对齐）在 Variant 只覆盖其一时会失步 —— 单属性覆盖
是原子的。

## 3. 方案总览

扩展 `radius=` 的**值语法**：逐角值从「半径」升级为「处理方式 + 尺寸」，整形关键字在
`pill` 旁边加 `hexagon`。

```xml
<Btn radius="cut 16">                     <!-- 四角 45° 斜切 -->
<Btn radius="cut 16, 8, cut 16, 8">       <!-- 对角斜切 + 圆角混用 -->
<Btn radius="cut 24x16">                  <!-- 横 24 纵 16 的扁平斜切 -->
<Frame radius="notch 12, 0, notch 12, 0"> <!-- 上两角方形缺口 -->
<Btn radius="hexagon">                    <!-- 左右尖端六边形，纵向恒=半高 -->
<Btn radius="hexagon 32">                 <!-- 尖端横向伸出 32 -->
```

现状全部原样兼容：`radius="8"`、四值、`pill`、空串。border / glow / glass / mask 自动
跟随新轮廓，**零个新属性**。

## 4. 语法

```
radius      := "" | whole-shape | corner-list
whole-shape := "pill" | "hexagon" [ SP number ]
corner-list := segment | segment "," segment "," segment "," segment
segment     := number                        // round，现状语义不变；0 = 方角
             | keyword SP size
keyword     := "cut" | "notch"
size        := number [ "x" number ]         // W 或 WxH，画布单位
```

- 四值顺序仍是 CSS 序（TL,TR,BR,BL 顺时针自左上）；单值应用到四角。
- `cut W`：45° 对称斜切；`cut WxH`：横向 W、纵向 H 的斜切（参考图的扁平尖端 = 大W小H）。
- `notch W`：W×W 方形缺口；`notch WxH`：横向 W、纵深 H。
- 逐角混用合法：`radius="cut 16, 8, notch 8, 0"`。
- `hexagon`：左右两侧切成尖端 —— TL/BL 与 TR/BR 各以纵向 H=半高的斜切相接于中线。
  纵向尺寸在 shader 逐像素解算（同 `pill` 的理由：依赖 rect 尺寸，CPU 解算会丢材质共享，
  见 `RadiusParser.cs` 的 remarks）。裸 `hexagon` 横向也取半高（45° 尖端）；`hexagon 32`
  指定横向伸出量。
- `pill` / `hexagon` 与逐角值混写是 parse error（沿用 pill 现有规则与措辞风格）。
- 空串 = `Zero`（方角）照旧 —— Variant 只能改值不能删属性，`radius.desktop=""` 是退回
  方角的唯一写法，必须保持合法。
- 负数 / NaN / Infinity 检查逐字段沿用现有实现。
- **关键字与 `x` 分隔符小写、`x` 两侧无空格、关键字与尺寸之间以空白分隔（容忍多个空格）**
  （`Trim` 后按空白切分）。大小写不做宽容：报错消息里列出合法词，LLM 照错误自纠比
  静默容错更可靠，且 lint / 运行时行为一致。

### 4.1 解析错误（parse-time，纯 C# 子集，CLI 同步可见）

| 情形 | 结果 |
|---|---|
| 未知关键字（`bevel 16` / `scoop 8`） | error：列出合法词 `cut` / `notch`（及裸数字、`pill` / `hexagon`） |
| 关键字后缺尺寸（`cut`） | error |
| `WxH` 段不是两个有限非负数（`cut 16x` / `cut x8` / `cut 16x-2`） | error，指明是哪个角哪一段 |
| `pill` / `hexagon` 混入四值列表 | error（沿用 pill 措辞：whole-shape keyword，单独成值） |
| `hexagon 32x16` | error：hexagon 只收横向一个参数，纵向恒为半高 |
| 逗号切出 2 段或 ≥5 段 | error（照旧） |

## 5. 语义

**clamp 按轴独立，仍在 shader 逐像素做。** `cut` / `notch` 的 W clamp 到半宽、H clamp 到
半高 —— 每轴 clamp 保证相邻两角同轴尺寸之和不超边长，角不互相穿越。round 沿用
「clamp 到短边半长」不变（`PuguiResolveRadius` 现状）。`hexagon` 的 H 恒取半高，于是
TL/BL（TR/BR 同理）的斜切精确相接成尖端，尺寸变化自动跟随，作者不需要手算。

**派生特性自动跟随，逐条：**

- **border**：向内描，压在填充上（`_BorderWidth > 0` 的 uniform 分支不变），沿新轮廓。
- **glow**：`saturate(1 - d/_GlowSize)` 只依赖外侧距离场 —— cut 外侧距离要做到精确
  （§6.2），notch 凹角处的近似误差要 render test 验收（§6.2）。
- **glass**：折射与边缘光照走 `PuguiSdNormal`，cut 区法线 = 斜边半平面法线，notch 区
  = 缺口两条边的盒法线 —— 解析可写，画布空间语义（GL 翻转问题）与现状一致。
- **`mask="self"` / `maskRadius`**：遮罩形状 = SDF 实心区（M-mask 那四行），cut / notch /
  hexagon 的裁剪免费成立。Progress 的 `maskRadius` 自动跟随 `radius`（`DeclaredRadius`
  流经，见 `ProceduralControl.cs:96`），语法升级后跟随语义不变。
- **纵向渐变填充**：`t = (p.y + b.y) / 2b.y` 只看包围盒，与角部处理无关，不变。
- **`PUI-PROG-FILL-RADIUS-MODE`**（`mode="fill"` × `fillRadius` 冲突）不受影响。

**weld 是唯一的例外：v1 不支持。** `GlassGroupPanel` 把成员形状打包成 `_WeldRects` +
`_WeldRadii`（float4，pill 已在 CPU 解算），融合走 `PuguiSmin`。逐成员再带 kind + H 两组
数组、smooth-union 又会把角熔圆 —— 复杂度买不到观感。规则：weld 成员的 radius 含
cut / notch / hexagon 时，**CPU 打包处降级为同 W 的圆角**，lint 报 warning
`PUI-WELD-CORNER`（不是 error：形状与 weld 可能来自两个主题包的合流，作者未必同屏写过）。

## 6. 实现地图

### 6.1 数据流（改动点自上而下）

| 层 | 文件 | 改动 |
|---|---|---|
| 解析 | `Runtime/Core/Parser/RadiusParser.cs` | 语法升级；`RadiusSpec` 结构扩展（§6.3）。纯 C# 子集不变，CLI lint 免费获得新语法报错（`StyleRules.cs:86` 已在调 `TryParse`） |
| 控件 | `ProceduralControl.cs:98` / `Frame.cs:137` / `Slider.cs:170,199` / `Progress.cs:111,171,192` | **零改动** —— 全部只做 `RadiusParser.Parse` → `SetRadius(RadiusSpec)` 透传 |
| 面板 | `Runtime/Controls/Internal/ProceduralPanel.cs:158` | `SetRadius` 签名不变；`FlushParams` 的 `PanelParams` 打包扩展 |
| 材质键 | `ProceduralMaterialCache.cs:70` | `PanelParams` 增 kind / H / shape 字段，参与相等比较与哈希 |
| shader | `UI-PanelSDF.cginc` + `UI-ProceduralPanel.shader` / `UI-GlassPanel.shader` | 形状函数广义化（§6.2）；材质属性：`_Radius`（语义变为逐角 W）+ 新增 `_CornerKind`(float4: 0 round / 1 cut / 2 notch)、`_CornerH`(float4)、`_Shape`(float: 0 none / 1 pill / 2 hexagon，替代 `_Pill`)、`_HexW` |
| weld | `GlassGroupPanel` 打包处 + `UI-GlassGroup.shader` | 降级为圆角（§5），shader 不动 |
| lint | `Runtime/Core/Lint/` | `PUI-WELD-CORNER`（§5）；语法错误走 parser，无新规则 |
| XSD | `Editor/XsdGenerator.cs:91` | `radius` 本就是无 pattern 的 `xs:string`，无改动 |
| SHAPE 规则 | `ThemeStyleRules` (#105) | 属性名集合没变，无改动 —— 守卫测试确认 |

### 6.2 shader：形状函数广义化

`PuguiSdRoundBox(p, b, r)` → `PuguiSdPanel(p, b, kind4, w4, h4)`，三个 shader 共 include
一处改。逐象限选角（现有的 `p.x > 0` / `p.y > 0` 二选一逻辑照旧），按该角 kind 分派：

- **round**：现有路径逐字不变。round-only 面板（含 pill）改前改后**逐像素一致**，用
  render test 钉住 —— 这是本设计对已出货皮肤的零回归承诺。
- **cut**：矩形与斜边半平面求交（`max`）。半平面交在两条边界法线的夹角区内会低估到顶点
  的距离 → glow 在斜切角外侧被撑大。修正：夹角区取到**斜边线段端点**的精确距离
  （同 round 角用 `length()` 精确化的思路）。外侧距离精确是 glow 不失真的验收线。
- **notch**：矩形减去角上的方块（`max(d, -dNotch)`）。减法 SDF 在凹角外侧近处是近似 ——
  glow 在缺口腔内的观感要 render test 验收，误差不可接受时再换精确分段距离（到缺口两条
  边的线段距离），spec 不预先绑死实现。
- **`PuguiResolveRadius` → `PuguiResolveCorners`**：pill / hexagon 哨兵解算 + 每轴 clamp。
  hexagon：四角 kind=cut，H = `b.y`，W = `_HexW > 0 ? _HexW : b.y`（再 clamp 到 `b.x`）。
- **`PuguiSdNormal` 同步扩展**：cut 区返回斜边法线，notch 区返回缺口盒法线 —— 玻璃边缘
  光照与折射的方向正确性靠它。象限折回（`s * g`）逻辑沿用。

**成本**：新增分支全是「材质 uniform 决定路径」的分支，与 `_BorderWidth > 0` 同类 ——
全体 fragment 同路径，开销可忽略。不用新词汇的面板多读几个 uniform，几何、材质共享、
合批行为不变。

### 6.3 `RadiusSpec` 结构扩展（兼容性）

`RadiusSpec` 是 public struct（自定义控件作者可能在用）。扩展保持既有成员语义：

- `TopLeft` / `TopRight` / `BottomRight` / `BottomLeft` 保留，语义 = 该角的 W（round 时
  即半径，逐字兼容）；`IsPill` 保留。
- 新增：逐角 `CornerKind`（`Round` / `Cut` / `Notch`）与 H；整形 `Shape`
  （`None` / `Pill` / `Hexagon`）与 `HexW`。`IsPill` 改为 `Shape == Pill` 的只读别名。
- 现有构造函数保留（构造出全 round），新形态走新构造。

## 7. SKILL 更新（每个落地里程碑的同一 PR 内）

- `authoring-promptugui-xml/SKILL.md`：`radius` 行改写 —— 值语法表加 `cut` / `notch` /
  `hexagon` 与 WxH 形态，示例给参考图三件套（六边形主按钮 / 梯形 Tab / 缺口面板）；
  程序化表面小节提一句 weld 例外。
- `reference/glass.md`：weld 小节补 `PUI-WELD-CORNER` 一句。
- C# SKILL 不动（`RadiusSpec` 扩展兼容，控件作者 API 无感知）。

## 8. 已定的决策（2026-08-27 与作者对齐）

1. **扩展 `radius` 值语法，不加新属性。** 理由见 §2。属性名叫 radius 而值可以是切角，
   语义略扭 —— 接受，五属性一次获得能力的收益压倒命名洁癖。
2. **v1 词汇 = `cut` + `notch`；`scoop`（内凹圆）与 `squircle`（超椭圆）不进。** scoop
   参考图用不到；squircle 无解析 SDF（迭代逼近）、与 HUD 风格无关。语法按 §4 设计成
   可平滑追加新关键字。
3. **命名用 `cut` 不用 CSS 的 `bevel`。** 既然没有镜像 CSS 的双属性模型，对齐 CSS 词汇
   的收益已经不大；`cut 16` 更短更直读。`notch` 保留 CSS 词（无更口语的替代）。
4. **双值 `WxH` 进 v1。** 45° 对称切角做不出参考图的扁平尖端；SDF 仍解析，语法只多一种
   段形态。
5. **`hexagon` 整形关键字进 v1。** 尖端要求切角纵向恰=半高，作者手算会在尺寸变化时失效
   —— 与 pill 同型的问题用同型的解法（shader 哨兵解算，保材质共享）。
6. **竖版 hexagon（上下尖端）不进 v1。** 需求出现时加方向参数，语法可平滑扩展。
7. **weld × 新词汇：运行时降级圆角 + lint warning。** 理由见 §5。

## 9. 开放问题（实现期已结）

- ~~notch 凹角 glow 的验收标准~~ —— 结论比预想的更重：近似不只影响 glow，**内描边**也会在
  凹顶点鼓一块。做成了精确的双场解（§11）。
- ~~`_Shape` 替代 `_Pill` 的材质资产引用~~ —— 全仓只有一个序列化 `.mat`
  （`UI-LinearLightTint.mat`），走的是另一个 shader。改名安全。

## 10. 里程碑拆分（草案）

| | 内容 | 依赖 |
|---|---|---|
| **M0 Red** | `RadiusParser` 新语法全矩阵（§4 + §4.1 逐条）+ `RadiusSpec` 兼容契约 + round-only 逐像素回归的 render 基线 | 无 |
| **M1 cut + hexagon** | parser / `RadiusSpec` / `PanelParams` / cginc（cut + 哨兵解算 + 法线）/ 三 shader 属性；SKILL 同 PR。参考图刚需先落地 | M0 |
| **M2 notch** | notch SDF + 凹角 glow render 验收；SKILL 同 PR | M1 |
| **M3 收尾** | weld 降级 + `PUI-WELD-CORNER` + `glass.md`；SHAPE 规则守卫确认 | M1 |

四档一轮做完，没有拆成四个 PR：M1 落地后 M2 只是 cginc 里多一个 `PuguiSdNotchCorner`，
M3 只是一条 lint —— 拆开的收益不抵四轮 Unity 编译 + 三次文档往返。

## 11. 实施记录

**验证结果**：EditMode 2531 / EditorOnly 308 / PlayMode 171 全绿；`dotnet format --verify-no-changes`
无改动；`UIXmlLint Runtime/Resources/` 零 issue。

### 11.1 兑现了「round-only 逐像素不变」

不是靠断言，是**量出来的**：把三份 shader `git stash` 回 HEAD（新 C# 设的
`_CornerH` / `_CornerKind` / `_Shape` 在旧 shader 上是静默 no-op，`_Radius` 语义对 round 逐字
未变），渲染同一个 `radius='60'` 面板，与新 shader 的输出比对 —— **0 个像素不同**。

前提是渲染确定性，这一条也单独验了：同一构建连跑两次，输出 0 差异。有了这个前提，后面
所有的「改了 shader 有没有效果」才能用像素 diff 来回答。

### 11.2 两处精度问题，第二处比 spec 预估的严重

**cut 的外部**：`max(dBox, dLine)` 在斜边两个端点外的楔形里最多低估 7.6%（45° 时），会让
外发光在那两点鼓出去。按 §6.2 的计划做了线段距离修正。

**notch 的内部**：spec 只担心了 glow，实际上**内描边**才是被咬到的那个。并集取 min 在凹顶点
处最多浅 √2 倍，`borderWidth=20` 的描边会一路铺到 28 —— 在缺口的内角上肉眼可见地鼓一块。
最终用两个场按符号分流：外部 `min(两个象限场)`（精确，含缺口造出的两个凸顶点），内部
`max(dBox, -dRect)`（精确，因为两个场各自精确、相减处两条边就是真边界）。

### 11.3 两个把「测试通过」变成假消息的坑

**坑一：测试程序集编译失败时，`run_tests` 跑的是上一次的旧程序集，而且报告 Passed。**
`PanelParams.Radius` → `CornerWidth` 的改名让 15 处既有测试调用点编译不过，而那一轮
`run_tests` 报了 2485/2485 通过 —— 跑的全是改名生效前的旧 DLL。后续只在同一个 group 上过滤时
才露馅（`total: 0`）。**每次 refresh 之后必须先 `read_console(types=["error"])`**，通过数不能
当编译成功的证据。

**坑二：第一版精度测试根本不可能失败。** `AssertPainted` 只查蓝通道，而白色描边的蓝通道也是
1.0 —— 于是「填充」和「描边盖过来了」这两种结果它都判通过。把 notch 的精确修正换成
`if (true) return d;` 之后测试**照样绿**，才发现问题在测试而不是 shader。修成同时要求红通道低
（填充 `#3366ff` 红=0.2，描边白=1.0）之后，falsification probe 如期报红、报的还是预测的那句话。

这条值得记住的地方在于：**做了 falsification 才知道守卫是假的**。像素 diff 显示那次改动确实
动了 431 个像素、其中就有探针那一格，但断言看不见它。

### 11.4 实现上的小结论

- **控件层真的零改动** —— 五个 `radius` 属性全是 `Parse → SetRadius(RadiusSpec)` 透传，
  语法升级完全被 `RadiusParser` + `PanelParams` 吸收。Progress 的 `maskRadius` 自动跟随白拿。
- **`PanelParams.Radius` 改名成 `CornerWidth`** —— 升级之后它装的是「逐角水平伸出量」，
  只在圆角时才等于半径，留着旧名字会持续误导。
- **`PuguiSdRoundBox` / `PuguiSdNormal` 原样保留**给 weld 融合面用，于是 `UI-GlassGroup.shader`
  一行没动，weld 降级只发生在 CPU 打包处（`GlassGroupPanel.ResolveRadius`）。
- **round 分支走的是同一份 `PuguiSdRoundCorner`**（`q = u + r` 与原来的 `abs(p) - b + radius`
  逐字等价），这是 §11.1 那个 0 差异的直接来源。
- **正方形 rect 上 `hexagon` 退化成菱形** —— 尖端自动尺寸取半高、再按半宽 clamp，两者相等时
  上下的平边长度归零。不是缺陷，但值得作者知道：六边形要横向长条的 rect。
