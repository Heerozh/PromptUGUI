# `<Scrollbar>` —— 滚动条作为部件子元素（程序化表面 + 几何 + 复用）

> 状态：**设计稿**（2026-09-12 brainstorm，作者已确认 §11 的全部决策；待 plan）。
> 需求来源：[`2026-09-12-scrolllist-scrollbar-procedural-surface-requirements.md`](2026-09-12-scrolllist-scrollbar-procedural-surface-requirements.md)（R1–R4 全部覆盖，语法改为本文的形态）。
> 相关：
> `2026-08-27-decor-primitives-design.md`（§2 否决「宿主属性」选子元素的先例 —— 本文同一条理由）、
> `2026-08-23-control-inner-layer-attrs-design.md`（`<layer>` / `<layer>Color` 前缀规约的出处；本文把滚动条**整体搬出**那套规约，§2.4 的 `Scrollbar` 撞名问题随之消失）、
> `2026-08-26-procedural-surface-design.md`（`ProceduralControl`：一个基类给控件全套形状属性 —— `<Scrollbar>` 的轨道就靠它白拿 16 个属性）、
> `2026-05-09-m5-1-default-control-alignment-design.md`（默认滚动条的几何基线，§8.3；其「❌ 加独立 `<Scrollbar>` primitive」的决定**在此推翻**）、
> `2026-08-31-collapsible-design.md`（`<Header>` 的子元素路由先例：`ScreenInstantiator` 按 tag 把子树送到宿主指定的 transform）、
> `2026-09-10-scrolllist-grid-and-static-children-requirements.md`（`scrollbarWidth` / `scrollbarOverlay` 的出处 —— 本文退役它们）。

## 1. 问题

需求方要画一根 HUD 风格的滚动条：胶囊轨道 + 细描边，滑块缩在轨道里、更亮、带外发光。按库里现有的
`<layer>` 前缀规约（`scrollbar` / `scrollbarColor` / `scrollbarHandle` / `scrollbarHandleColor`），需求
稿给 `<ScrollList>` 开了 12 个新属性：轨道 5 个形状（`scrollbarRadius` / `scrollbarBorderWidth` /
`scrollbarBorderColor` / `scrollbarGlow` / `scrollbarGlowColor`）、滑块 5 个同款、再加
`scrollbarPadding` / `scrollbarSpacing`。加上已有的 6 个，`<ScrollList>` 上会有 **18 个 `scrollbar*`**。

这条路有三处硬伤：

1. **已经在复制了。** `Dropdown.cs:348-371` 与 `ScrollList.cs:597-625` 各自有一套一模一样的
   4 个 `scrollbar*`。要给 Dropdown 的弹窗滚动条同样的外观，12 个再抄一遍。将来 `<Collapsible>` /
   `<Markdown>`（各自带 `ScrollRect`、没有滚动条）要加滚动条，还是抄。
2. **「内层只给形状」是 API 面积的限制，不是技术限制。** `AddInnerSurface` 建的是同一个
   `ProceduralSurface`，border / glow 都画得了；`<Slider>` / `<Progress>` 只开 `<layer>Radius`，就是不想每层
   再来一组 `fillBorderWidth` / `fillGlow…`。需求要在滚动条上「开例外」，等于承认前缀规约撑不住一个
   有两层、每层要全套形状的部件。
3. **复用只能靠属性包。** 同一根滚动条要在两个列表和一个下拉里出现，作者只能把 12 个前缀属性打成一个
   `<Style>`。模板（结构 + 参数）这一层完全用不上。

库里已经有一次同样的抉择：`<Decor>` 的 spec §2 否决了宿主属性 `decor="…"`，理由是
「`class=` 属性包对任意标签生效（`StyleMerger` 不看 tag），子元素形态的主题化本来就是免费的」。
滚动条是一模一样的情形，而且更严重 —— 它有两层。

## 2. 否决的方案

**A. 需求原文：12 个 `scrollbar*` 前缀属性。否决。** 理由见 §1。补一句：作者要记 12 个新名字，而
它们说的东西（`radius` / `borderWidth` / `glow`）作者早就会写了 —— 前缀只是把已知的词汇表复制一份。

**B. 按名引用模板：`<ScrollList scrollbar="HudScrollbar">`（类 `itemTemplate=`）。否决。** 它比 A 少
11 个属性，但模板体里的那根滚动条仍然要有人定义属性 —— 也就是说 B 仍然需要 §3 的 `<Scrollbar>`
控件，然后**额外**养一套「宿主按名实例化 chrome 模板」的机制：模板体根必须是 `Scrollbar` 的校验、
`<Param>` 必须有 default（`ItemTemplateGuard` 同款）、动态子树的 id 作用域另议。B 独有的能力只有
一条：模板名是属性，能进 `<Style>`，于是主题能整根**换结构**（像素主题要带箭头钮的滚动条）。
箭头钮是非目标；sprite 皮 vs SDF 皮的差别全是属性，`class=` 已经能换。为一个非目标多养一套机制不值。
（模板复用本身没丢：§4.3，`<Template>` 的 body 根就是 `<Scrollbar>`，零新机制。）

**C. 部件样式引用：`<ScrollList scrollbarClass="hud-bar" handleClass="hud-knob">`。否决。** 两个属性
就够，但它要求控件在**运行期**自己查 `<Style>` 池、再用一个迷你 applier 把属性套到内层上 —— 复制
`StyleMerger` + `ControlAttributeApplier` 的活，破坏「样式在展开期合并、运行期树里不存在样式」的
不变量，`.variant` 后缀与值 lint 全部要重做。

**D. 滑块再嵌一层 `<Handle>` 子元素。否决（v1）。** 零前缀、滑块也白拿全套属性，但常见用法要写两层
嵌套、多记一个 tag，而且和 `<Slider handle= handleColor= handleRadius=>` 的既有词汇分叉。滑块用
`handle*` 前缀：与 `<Slider>` 逐字一致，多出来的 `handleBorderWidth` / `handleBorderColor` /
`handleGlow` / `handleGlowColor` 只是把同一条规则延长。若将来滑块要 `<Decor>` / 状态门控，再升格。

## 3. 方案总览

新内置标签 **`<Scrollbar>`**：滚动宿主（`<ScrollList>` / `<Dropdown>`）的**非排版部件子元素**，
本身是一个 `ProceduralControl`。

```xml
<ScrollList columns="4" cellSize="63x86" spacing="4">
  <Scrollbar thickness="6" overlay="true" padding="1.5"
             radius="pill" color="glass-primary-darker/0.6" borderWidth="0.4" borderColor="hud-edge-cyan/0.6"
             handleRadius="pill" handleColor="hud-edge-cyan" handleGlow="3" handleGlowColor="hud-edge-cyan/0.6"/>
  <!-- 其余子节点照旧进 Content -->
</ScrollList>
```

- **轨道 = 控件主表面。** `radius` / `borderWidth` / `glow` / `intensity` / `glass`… 与 `<Frame>` 逐字
  相同，从 `ProceduralControl` 继承，一个都不用重新定义。`sprite` / `color` 是轨道的贴图与颜色。
- **滑块 = 内层。** `handle` / `handleColor` / `handleRadius` 沿用 `<Slider>` 的名字，再加
  `handleBorderWidth` / `handleBorderColor` / `handleGlow` / `handleGlowColor`。
- **几何四个：** `thickness`（厚度）、`padding`（滑块内缩）、`spacing`（与视口的距离）、`overlay`
  （压在内容上 / 视口让位）。朝向不是作者写的 —— 宿主给。
- **一个宿主一根**，直接子元素。不写就是今天的默认条（同一个类、逐像素一致）。
- **复用零新机制：** 属性走 `<Style>` + `class=`；结构 / 参数走普通 `<Template>`（body 根是
  `<Scrollbar>`，在宿主里当子元素调用）；主题走 `<Theme>` 里的 `<Style>`。
- **宿主上的 `scrollbar*` 属性全部退役**（ScrollList 6 个、Dropdown 4 个），不留转发器（§11-3）。
- 不在排版流里（`ParticipatesInLayout => false`），不进 `<ScrollList>` 的 slot 集合，不被 Viewport
  的 mask 裁（它是 ScrollRect 的直接子级 —— uGUI 的 expand 模式也要求这一点）。

## 4. 作者面

### 4.1 `<Scrollbar>` 属性

| 属性 | 类型 | 默认 | 语义 |
|---|---|---|---|
| `thickness` | float ≥ 0 | `20` | 条的厚度：竖条的宽、横条的高。名字避开通用布局属性 `width` / `height`（`<Decor extent>` 同理）。`0` = 事实上没有滚动条（沿用 `scrollbarWidth="0"` 的既有用法）。 |
| `overlay` | bool | `false` | `true`：条压在内容上（`ScrollbarVisibility.AutoHide`）；`false`：内容溢出时视口让出 `thickness + spacing`（`AutoHideAndExpandViewport`）。 |
| `spacing` | float | `max(−thickness, −3)` | 条与视口的距离，落到 `ScrollRect.*ScrollbarSpacing`：正 = 分开，负 = 压进视口。**`overlay="true"` 时 uGUI 不读这个值**（`PUI-SCROLLBAR-OVERLAY-SPACING`）；overlay 下与内容的距离靠内容 `padding` 让。 |
| `padding` | `"P"` 或 `"E,S"`，非负 | `0` | 滑块相对轨道的四边内缩。单值四边同量；两值 = **沿轴（端头）, 跨轴（两侧）** —— 竖条读作 `V,H`，横条读作 `H,V`。跨轴内缩让滑块比轨道窄，沿轴内缩让滑块不顶到端头。公式见 §5.1。 |
| `sprite` | sprite key | 内置 `pugui_9slice_inset` | 轨道贴图。`""` / `none` = 无图（纯色）。 |
| `color` | color | `white` | 轨道颜色：token / `/alpha` / 渐变。程序化模式下即 SDF 填充色（`Surface.SetFill`），同 `<Slider color>`。 |
| `radius` `borderWidth` `borderColor` `glow` `glowColor` `innerGlow` `innerGlowColor` `intensity` `glass` `frost` `depth` `dispersion` `lightAngle` `lightIntensity` `saturation` `noise` | 同 `<Frame>` | — | 轨道的程序化表面，继承自 `ProceduralControl`，语义逐字同 `<Frame>`。写任一即轨道换 SDF 面、Image 让位不销毁。 |
| `handle` | sprite key | 内置 `pugui_9slice_round` | 滑块贴图。`""` / `none` = 无图。 |
| `handleColor` | color | `white` | 滑块颜色（token / `/alpha` / 渐变；既 `ColorApplier.Apply` 又 `HandleSurface.SetFill`，同 `<Slider handleColor>`）。 |
| `handleRadius` | 同 `radius` | — | 滑块圆角；`pill` = 胶囊。 |
| `handleBorderWidth` · `handleBorderColor` | 同 `borderWidth` / `borderColor` | — | 滑块内描边，向内画，不改布局。 |
| `handleGlow` · `handleGlowColor` | 同 `glow` / `glowColor` | — | 滑块外发光：撑大绘制 quad、不动布局；条在 Viewport mask 之外，**不会被裁**。`handleGlowColor` 不写时跟随 `handleColor`（同 `glowColor` 跟随 `color`）。 |

通用属性里 **允许** `id` / `class` / `if` / `interactable` 及全部 `.variant` 后缀；**拒绝** `anchor` /
`size` / `width` / `height` / `margin` / `pivot` / `flow` / `scale` / `hidden`（`PUI-SCROLLBAR-LAYOUT-ATTR`）。
几何由控件按（朝向, `thickness`）自己写；`hidden` 会和 uGUI 的 AutoHide 每帧 `SetActive` 打架，所以
一并拒绝 —— 要「没有滚动条」写 `thickness="0"`。

**滑块不给 `glass`**（无此属性）：内层玻璃采的 backdrop 与轨道同一张，两层长得一样，滑块直接消失
（同 `<Slider>` / `<Progress>` 内层的既有理由）。**轨道的 `glass` 保留**：它和 `<Btn glass>` 是同一回事
（采 canvas 后面的 backdrop，不含 UI 自身），语义一致、零成本，不特殊化。

`<Scrollbar>` **不接子节点**（v1，`PUI-SCROLLBAR-CHILD`）：`<Decor>` / `<Show>` 之类在 Dropdown 的克隆弹窗
里（§5.5）状态不会跟过去，两个宿主行为不一致；等有真实需求再开。

### 4.2 宿主与归位

| 宿主 | 条挂在哪 | 默认条 | 朝向 |
|---|---|---|---|
| `<ScrollList>` | ScrollList 根（ScrollRect 所在，Viewport 的兄弟） | 有 | 跟 `direction`：竖列 / 网格 = 竖条靠右，`horizontal` = 横条靠下 |
| `<Dropdown>` | 弹窗 `Template`（popup 根，ScrollRect 所在） | 有 | 恒竖条靠右 |

- `<Scrollbar>` 必须是宿主的**直接子元素**（`PUI-SCROLLBAR-OUTSIDE`）。`ScreenInstantiator` 沿
  `<Header>` 的先例按 tag 路由：宿主实现 `IScrollbarHost`，子树实例化到 `host.ScrollbarHost` 而不是
  `ChildHostTransform`，随后 `host.AdoptScrollbar(bar)` 接线。ScrollList 的 `Content`（layout group）
  永远看不到它，静态 slot 收集跳过它，`SlotCount` 不变。
- **一个宿主一根。** 第二根：lint 错误 `PUI-SCROLLBAR-DUPLICATE`；运行期文档序第一根生效，其余
  不接线并 `Debug.LogWarning`。
- **没写就是默认条。** 宿主在首轮 `OnAfterApply` 发现没有认领到任何 `<Scrollbar>` 时，自己 `new`
  一个同类控件挂上（不走 `ScreenInstantiator`、不进 `_nodeMap`，随宿主 `Dispose`）。默认值全部
  等于今天：`thickness=20`、`padding=0`、`spacing=max(−20,−3)=−3`、`overlay=false`、inset 轨道 +
  round 滑块、白色 —— 与 PR #131 之后的现状**逐像素一致**（§7 用矩形角点钉住，不是用 sizeDelta）。
- 节点名固定 `Scrollbar`（`id` 覆盖，同任何控件）。ScrollList 今天的 `Scrollbar Vertical` /
  `Scrollbar Horizontal` 两个懒建节点合并为**一个按朝向翻转的节点**（§5.4），名字里的方向后缀取消。
- `<Scrollbar>` 出现在 Variant `<Add>` 块里：不支持（`PUI-SCROLLBAR-IN-ADD`，运行期按重复处理）。
  Strategy C 的 SetActive 切换会和 AutoHide 打架，且默认条已经建好。平台差异用 `.variant` 覆盖
  同一根条的属性（`thickness.mobile="4"`）。

### 4.3 复用：Style / Template / Theme

```xml
<!-- 属性复用：一个包同时穿在列表和下拉的滚动条上 -->
<Style name="scrollbar" thickness="6" radius="pill" color="glass-primary-darker/0.6"
       borderWidth="0.4" borderColor="hud-edge-cyan/0.6"
       handleRadius="pill" handleColor="hud-edge-cyan" handleGlow="3" handleGlowColor="hud-edge-cyan/0.6"
       padding="1.5"/>

<!-- 主题换皮：像素主题换贴图、关掉形状 —— 全是属性，class= 就够 -->
<Theme name="pixel">
  <Style name="scrollbar" sprite="px:bar" handle="px:knob" radius="0" handleRadius="0"
         borderWidth="0" handleGlow="0" thickness="8" padding="0"/>
</Theme>

<!-- 结构 + 参数复用：普通 <Template>，body 根就是 <Scrollbar> -->
<Template name="HudScrollbar">
  <Param name="accent" default="hud-edge-cyan"/>
  <Scrollbar class="scrollbar" handleColor="{{accent}}" handleGlowColor="{{accent}}/0.6"/>
</Template>

<ScrollList …><HudScrollbar/></ScrollList>
<ScrollList …><HudScrollbar accent="gold"/></ScrollList>
<Dropdown …><Scrollbar class="scrollbar"/></Dropdown>
```

- `TemplateExpander` 展开后实例根保留 body 根的 tag（`TemplateExpander.cs:300-303`），宿主看到的仍是
  `Scrollbar` 节点 —— 路由、认领、lint 全部照常。
- `class=` 写在 body 里的 `<Scrollbar>` 上，不写在 `<HudScrollbar class=>` 调用上
  （`PUI-THEME-STYLE-ON-INVOCATION` 的既有规则）。
- `PUI-THEME-STYLE-SHAPE` 的 all-or-nothing 判定对 `handle*` 组按**一个内层表面**处理（§4.5）。

### 4.4 Variant / ReSolve

- `<Scrollbar>` 是树里的普通节点，进 `_nodeMap`，`.variant` 覆盖走正常 ReSolve；宿主的 `direction`
  变体切换只翻转朝向（§5.4），条的属性不重放也不丢。
- `BeginPass` / `Reconcile` 的「模式每轮重算、不 latch」纪律照旧：轨道主表面与滑块内层各自一份
  `ProceduralSurface`，`OnBeforeApply` 清、setter 声明、`OnAfterApply` 对账。
- `PUI-VARIANT-NO-BASE` 对 `handle*` 组：滑块表面**没有**任何 base 形状属性时，base-less 的
  `handleGlow.mobile` 让整个滑块表面随变体整体开关、自愈；有 base `handleRadius` 时，base-less
  `handleGlow.mobile` 会卡在最后一次的值 —— 报。这就是主表面既有规则按表面分组后的样子（§4.5）。

### 4.5 lint

| 代码 | 触发 | 级别 / 位置 |
|---|---|---|
| `PUI-SCROLLBAR-OUTSIDE` | `<Scrollbar>` 的父节点不是 `ScrollList` / `Dropdown`（Template body 根无父节点，不报；raw 遍下未知 tag 父节点不报） | CLI 两遍 + 运行期 warning（孤儿条什么也不接、只是画在那里） |
| `PUI-SCROLLBAR-DUPLICATE` | 同一宿主下第二个 `<Scrollbar>` | CLI + 运行期 warning（第一根生效） |
| `PUI-SCROLLBAR-IN-ADD` | `<Add>` 块内出现 `<Scrollbar>` | CLI |
| `PUI-SCROLLBAR-LAYOUT-ATTR` | `anchor` / `size` / `width` / `height` / `margin` / `pivot` / `flow` / `scale` / `hidden`（含 `.variant`） | CLI + 运行期 warning；`width` / `height` 的提示指向 `thickness` |
| `PUI-SCROLLBAR-CHILD` | `<Scrollbar>` 有子节点 | CLI + 运行期 warning（子节点不实例化） |
| `PUI-SCROLLBAR-VALUE` | `thickness` 非数 / 负；`spacing` 非数；`padding` 不是 1–2 个非负数；`2·S ≥ thickness`（滑块厚度 ≤ 0，运行期钳到 1 并 warning） | CLI；运行期同款解析抛 `ArgumentException` |
| `PUI-SCROLLBAR-OVERLAY-SPACING` | `overlay="true"` 与 `spacing=` 同时声明 | CLI |
| `PUI-SCROLLBAR-RETIRED-ATTR` | `<ScrollList>` / `<Dropdown>` 上的 `scrollbar` / `scrollbarColor` / `scrollbarHandle` / `scrollbarHandleColor` / `scrollbarWidth` / `scrollbarOverlay`，以及 `<Style>` 里的同名属性（没有任何标签再认识它们） | CLI；提示指向 `<Scrollbar>` 子元素 |
| `PUI-PROC-SPRITE-CONFLICT`（扩） | `ProceduralSurfaceRules` 从「只看 `sprite`」推广为**按层**：每层 `(spriteAttr, shapeAttrs)`。`<Scrollbar>`：`("sprite", 主表面集)`、`("handle", handleRadius/handleBorderWidth/handleBorderColor/handleGlow/handleGlowColor)`。顺带把 `<Slider>` `("fill",[fillRadius])` `("handle",[handleRadius])`、`<Progress>` `("fill",[fillRadius])` `("frame",[frameRadius])` 纳入 —— 今天这些内层冲突没人报。`""` / `none` 不算矛盾 | CLI |
| `PUI-PROCEDURAL-VALUE`（扩） | `StyleRules.CheckValue` 的 radius 语法从只认 `radius` 扩到 `handleRadius` 及 `ProceduralAttrNames.InnerLayerRadius` 全部；像素属性表加 `handleBorderWidth` / `handleGlow` | CLI |
| `PUI-VARIANT-NO-BASE` / `PUI-THEME-STYLE-SHAPE`（扩） | `ProceduralAttrNames.InnerLayerRadius`（单属性表）改为 **`InnerLayerGroups`**（每个内层一组：`fill=[fillRadius]`、`frame=[frameRadius]`、`mask=[maskRadius]`、`handle=[handleRadius, handleBorderWidth, handleBorderColor, handleGlow, handleGlowColor]`），两条规则的「一个属性一个表面」假设改为「一组属性一个表面」，语义不变 | CLI |

`ProceduralSurfaceRules.SurfaceTags` 加 `Scrollbar`（`ProceduralAttrNamesTests` 双向核对注册表）；
`BuiltinTags.All` 加 `Scrollbar`（`BuiltinTagsTests` 守）。`ScrollbarRules.cs` 放 `Runtime/Core/Lint/`，
纯 C#，CLI 与 `ScreenInstantiator` 共用。

### 4.6 退役的属性

| 标签 | 退役属性 | 去处 |
|---|---|---|
| `<ScrollList>` | `scrollbar` `scrollbarColor` `scrollbarHandle` `scrollbarHandleColor` `scrollbarWidth` `scrollbarOverlay` | `<Scrollbar sprite= color= handle= handleColor= thickness= overlay=>` |
| `<Dropdown>` | `scrollbar` `scrollbarColor` `scrollbarHandle` `scrollbarHandleColor` | 同上 |

不留转发器、不留别名（§11-3）。运行期未知属性本来就静默忽略（`ControlAttributeApplier` 的
`HasAttribute` 门），所以要靠 `PUI-SCROLLBAR-RETIRED-ATTR` 把旧写法在 CLI 里喊出来。
`<ScrollList>` 的 `columns` 行文档里「滚动条吃掉 `scrollbarWidth − 3`」的说法改为
「`thickness + spacing`」。

## 5. 语义细节

### 5.1 几何

记 `t = thickness`，`padding = (E, S)`（沿轴端头 / 跨轴两侧），滑块跨轴厚度 `h = t − 2·S`
（`≤ 0` 时钳到 `1` 并 warning）。三层 RectTransform 全部由 `<Scrollbar>` 自己写：

| 节点 | 竖条 | 横条 |
|---|---|---|
| 条（自身） | anchor (1,0)–(1,1)，pivot (1,1)，sizeDelta `(t, 0)`，anchoredPosition 0 | anchor (0,0)–(1,0)，pivot (0,0)，sizeDelta `(0, t)` |
| `Sliding Area` | anchor stretch，pivot 中心，sizeDelta `(−2S, −(2E + h))` | sizeDelta `(−(2E + h), −2S)` |
| `Handle` | anchor (0,0)–(1,0.2)（沿轴由 uGUI `Scrollbar` 驱动），sizeDelta `(0, h)` | anchor (0,0)–(0.2,1)，sizeDelta `(h, 0)` |

- 沿轴的 `−h` / `+h` 一对是 uGUI 的既有手法：Sliding Area 沿轴缩 `h`、滑块再加回 `h`，滑块行程正好
  到达端头，同时滑块有 `h` 的最小长度（内容再多也抓得住）。今天的默认 `(−t,−t)/(t,t)` 就是这个手法在
  `E = S = 0, h = t` 时的样子 —— 跨轴那一对 `−t/+t` 抵消，本文直接写成 `−2S / 0`，**矩形逐像素相同**。
- 滑块在 `value = 0` 时的底边 = `slidingBottom − h/2 = (E + h/2) − h/2 = E`：端头留白就是 `E`。
- 滑块居中在 Sliding Area 跨轴：两侧留白各 `S`。
- 默认 `padding=0` ⇒ `sliding = (0, −t)`、`handle = (0, t)`，与今天的结果一致；§7 的测试用父空间
  角点断言，不用 sizeDelta。

### 5.2 与视口的关系

- `overlay=false`（默认）：`AutoHideAndExpandViewport`。uGUI 在需要滚动时把 Viewport 的 sizeDelta 驱动为
  `−(t + spacing)`（`ScrollRect.SetLayoutHorizontal`，`m_VSliderWidth = rect.width`），**前提是条是
  ScrollRect 的直接子级**（`allAreChildren`）—— §4.2 的挂载点就是为此。`spacing` 默认
  `max(−t, −3)`：库存 `−3` 让条压进视口 3，条比 3 还细时夹到条自身厚度（PR #131 的规则原样保留）。
- `overlay=true`：`AutoHide`。uGUI 不再驱动 Viewport，条压在内容上；`spacing` 无效（lint）。与内容的
  距离靠内容 `padding`（网格 `padding="0,R,0,0"`）或列宽让 —— 这条今天就能用，文档写明。
- Dropdown 弹窗 Viewport 的静态预留 `sizeDelta.x = −18`（TMP 默认 prefab 值）改为
  `overlay ? 0 : −(t + spacing)`：expand 模式下 ScrollRect 随后接管、数值一致无首帧跳动；overlay 模式下
  ScrollRect 不接管，静态值就是最终值 —— 今天的 `−18` 在细条 + overlay 时会白让 18 个单位。

### 5.3 表面

- **轨道**：`SurfaceHost = GameObject`（条自身，Image + uGUI `Scrollbar` 都在这个节点上）。
  `SurfaceSelectable = null` —— uGUI `Scrollbar.targetGraphic` 是滑块，不是轨道（同 `<Slider>` 的理由）。
  写任一形状属性 → `__Surface` 子面板出现、轨道 Image 让位（sprite 清、alpha 0、不销毁、仍接 raycast）。
- **滑块**：`HandleSurface = AddInnerSurface(handle, selectable: bar)` —— `AddInnerSurface` 增加一个
  `Selectable` 参数（默认 null，其它调用点不变），让 uGUI `Scrollbar.targetGraphic` 在程序化模式下
  跟着面板走（`ProceduralSurface.Reconcile` 的既有逻辑），ColorTint 的 hover / pressed 不会打在
  alpha 0 的 Image 上。`__Surface` 是 Handle 的子节点，随滑块移动。
- **glow 不被裁**：条是 Viewport 的兄弟、在 mask 之外；`handleGlow` 撑大的是滑块面板的 quad，
  同样在 mask 外。§7 钉住「条不在 Viewport 子树内 + 面板 glow 撑大量 = handleGlow」。
- **颜色**：`color` / `handleColor` 既 `ColorApplier.Apply` 到 Image 又 `Surface.SetFill`，哪层在画
  就哪层收（同 `<Slider>`）。默认色 white → `<Scrollbar radius="pill"/>` 是白色胶囊，不是隐形条。
- **disabled**：随宿主 `interactable` 走 CanvasGroup；表面去饱和沿用 `ISelfGrayscale` 既有路径。

### 5.4 朝向：一个节点翻转

ScrollList 今天懒建两根条、按 `direction` 激活一根。改为宿主认领到的**同一个** `<Scrollbar>` 节点按
`direction` 翻转：`bar.Orient(vertical)` 重写三层 RectTransform（§5.1 两列之一）、设 uGUI
`Scrollbar.direction`（`BottomToTop` / `LeftToRight`）、宿主把 `_scroll.verticalScrollbar` /
`horizontalScrollbar` 二选一接上（另一个置 null）。

- 变体切 `direction.mobile="horizontal"` 只翻朝向，条的属性 / 表面 / 订阅原样保留。
- 需求里「两根共用一份声明、懒建的那根建完要回放」的复杂度随之消失。
- `PreConfigureContent` 已在子节点实例化前把 `direction` 推给 ScrollList，所以认领时朝向已知；
  `Direction` setter 再跑时重新 `Orient`，幂等。

### 5.5 Dropdown 弹窗是克隆体

`TMP_Dropdown.Show()` 每次展开都 `Instantiate(template)`，弹窗里活着的滚动条是 `Template/Scrollbar`
的**克隆**。Image 的 sprite / color 是序列化字段，克隆自带 —— 今天的 `scrollbar*` 就是这么工作的。
`ProceduralPanel` 的参数全是私有非序列化字段，克隆出来是一块空面板（fill clear、radius 0）——
这也是为什么 Dropdown 至今没有 `popupRadius`。

解法：`Controls/Internal/PuiDropdown : TMP_Dropdown`（同 `PuiButton` / `PuiToggle` 的既有做法），
override `CreateDropdownList(template)`：`base` 克隆之后，按层级路径把 template 子树里每个
`ProceduralPanel` 的状态拷到克隆体对应节点（新增 `internal void ProceduralPanel.CopyStateFrom(ProceduralPanel)`：
参数 + `_paramsDirty = true`，材质缓存按 key 自然命中）。`Dropdown.OnAttached` 的
`AddComponent<TMP_Dropdown>()` 换成 `PuiDropdown`；测试里 `GetComponent<TMP_Dropdown>()` 照常命中。
克隆期间 ReSolve 改属性不影响已展开的那批（收起再展开即生效）—— 与 `2026-08-23` §2.6 的既有限制一致，
写进 SKILL。

顺带打开了 `popupRadius` 之类的门，但本文不做（§10）。

### 5.6 C# 面

- `public sealed class Scrollbar : ProceduralControl`，注册为 `Scrollbar`。C# 里与 `UnityEngine.UI.Scrollbar`
  撞名：`Scrollbar.cs` / `ScrollList.cs` 用 `using UnityScrollbar = UnityEngine.UI.Scrollbar;`
  （`UnityImage` / `UnitySlider` 的既有别名习惯）；Dropdown 一直全限定，不受影响；测试里已全限定。
- `screen.Get<Scrollbar>("list/bar")` 通过 `id` 路径可达（`AddScopedId` 走的是 `parentControl`，
  即宿主）。v1 不暴露 `OnValueChanged` / `Value`（§12）。
- `internal interface IScrollbarHost { RectTransform ScrollbarHost { get; } void AdoptScrollbar(Scrollbar bar); }`
  —— 将来 `<Collapsible>` / `<Markdown>` 要滚动条就实现它，一行路由不用改。

## 6. 实现地图

数据流：

```
<Scrollbar> 节点 ──ScreenInstantiator（tag 路由）──> host.ScrollbarHost 下实例化
                                                          │
                                                   host.AdoptScrollbar(bar)   ← 接线 + Orient
                                                          │
                bar.Apply：setter 存值/声明 → ApplyCommon（默认 stretch，随即被覆盖）→ bar.OnAfterApply：
                     写三层几何(§5.1) · Surface/HandleSurface.Reconcile · 通知 host 重放 visibility/spacing/viewport
                                                          │
                host.OnAfterApply：没认领到 → new 默认条并 Adopt；ScrollList 再把 Frame 钉回最顶
```

| 文件 | 改动 |
|---|---|
| `Runtime/Controls/Scrollbar.cs`（新） | 控件本体：三层结构（`OnAttached` 建 track Image + uGUI Scrollbar、`Sliding Area`、`Handle`，默认皮沿用 `ApplyDefaultInsetSprite` / `ApplyDefaultSlicedSprite`）；`Orient`；§4.1 全部 setter；`OnBeforeApply` 清 pending、`OnAfterApply` 写几何 + 对账 + 通知宿主；`GetDefaultAnchor` 返 stretch（同 `<Decor>`，反正随后覆盖）；`ParticipatesInLayout => false` |
| `Runtime/Controls/Internal/IScrollbarHost.cs`（新） | §5.6 接口 |
| `Runtime/Controls/Internal/PuiDropdown.cs`（新） | `CreateDropdownList` 克隆后同步面板状态（§5.5） |
| `Runtime/Controls/Internal/ProceduralPanel.cs` | `CopyStateFrom` |
| `Runtime/Controls/ProceduralControl.cs` | `AddInnerSurface(GameObject host, Selectable selectable = null)` |
| `Runtime/Controls/ScrollList.cs` | 删 `EnsureVerticalScrollbar` / `EnsureHorizontalScrollbar` / `ApplyScrollbarMetrics` / `ApplyScrollbarSkin` 及 6 个 `scrollbar*` setter 与 pending 字段；实现 `IScrollbarHost`；`ApplyLayoutMode` 改为 `_bar?.Orient(...)` + 接线；`OnAfterApply` 建默认条、静态 slot 收集跳过 `Scrollbar`；`GetNativeSize` 注释里的 `scrollbarWidth − 3` 改 `thickness + spacing`；`Dispose` 释放自建条 |
| `Runtime/Controls/Dropdown.cs` | 删 `Template/Scrollbar` 子树构建与 4 个 setter；实现 `IScrollbarHost`（`ScrollbarHost = template`）；`AddComponent<PuiDropdown>`；Viewport 预留按 §5.2 |
| `Runtime/Application/ScreenInstantiator.cs` | 子节点循环里 `<Header>` 之后加 `Scrollbar` 路由 + `AdoptScrollbar`；`ScrollbarRules` 的运行期 warning |
| `Runtime/Application/BuiltinPrimitives.cs` | `reg.Register<Scrollbar>("Scrollbar", null)` |
| `Runtime/Core/Lint/ScrollbarRules.cs`（新） | §4.5 的 `PUI-SCROLLBAR-*` 七条；`Tag = "Scrollbar"`、`HostTags = { ScrollList, Dropdown }` |
| `Runtime/Core/Lint/BuiltinTags.cs` | `+Scrollbar` |
| `Runtime/Core/Lint/ProceduralAttrNames.cs` | `InnerLayerRadius` → `InnerLayerGroups`（保留旧名作扁平视图给现有调用点亦可） |
| `Runtime/Core/Lint/ProceduralSurfaceRules.cs` | `SurfaceTags += Scrollbar`；按层的 sprite 冲突表 |
| `Runtime/Core/Lint/StyleRules.cs` | radius 语法 / 像素属性表扩展 |
| `Runtime/Core/Lint/VariantBaseRules.cs` · `ThemeStyleRules.cs` | 按组处理内层 |
| `Runtime/Core/Lint/IRWalker.cs` · `DocumentLinter.cs` | 接 `ScrollbarRules`（节点规则 + 父子规则 + `<Add>` 规则 + `<Style>` 的 RETIRED 检查） |
| `Editor/XsdGenerator` | 重跑；`<Scrollbar>` 元素及属性随注册表出现，`<ScrollList>` / `<Dropdown>` 的退役属性消失 |
| `.lint/UIXmlLint` | 无改动（编译 `Core/Lint` 整目录） |

约束核对：`Core/Lint` 新文件不引用 `UnityEngine`（`ScrollbarRules` 只看 `ElementNode` / `StyleDef`）；
`ProceduralValueParser` / `RadiusParser` 是运行时与 lint 共用的纯 C# 解析器，`thickness` / `padding` /
`spacing` 的解析放在 `Core/Parser` 或 `Core/Lint` 可及的位置，让 CLI 与 setter 拒绝完全相同的输入。

## 7. 测试（Red 先行）

`Tests/EditMode/Controls/ScrollbarTests.cs`（新，取代 `ScrollListScrollbarTests.cs`）：

1. **默认条逐像素**：`<ScrollList>` 竖 / 横、`<Dropdown>` 三种宿主，默认条的条 / Sliding Area / Handle
   三个矩形（父空间角点）等于今天的数值；uGUI `Scrollbar.direction`、`ScrollRect.*Scrollbar` 引用、
   visibility、spacing 与今天相同；默认 sprite 是 inset / round（`DefaultSkinTests` 的两条改路径后保留）。
2. `thickness="6"`：条、Sliding Area、Handle 三层随之；横条同理。
3. `padding="1,2"`：三层矩形符合 §5.1 公式；`padding="3"` 四边同量；`padding="0"` 与不写逐字相同。
4. `thickness="6" padding="0,3"`：滑块厚度钳到 1 + `LogAssert.Expect` warning。
5. `spacing="6"` 落到 `ScrollRect.verticalScrollbarSpacing`（横条落 horizontal）；不写 = `max(−t,−3)`：
   `thickness="2"` → `−2`，`thickness="20"` → `−3`。
6. `overlay="true"` → `AutoHide`；不写 → `AutoHideAndExpandViewport`；Dropdown 的 Viewport 静态预留按 §5.2。
7. `radius="pill"` → 条节点下出现 `__Surface`、轨道 Image 让位（sprite null、alpha 0、仍 raycastTarget）；
   `color` 同时进面板 fill。
8. `handleRadius="pill" handleGlow="3"` → Handle 下出现 `__Surface`、滑块 Image 让位、uGUI
   `Scrollbar.targetGraphic` 指向面板；面板 glow 撑大量 = 3；条的 RectTransform 不在 Viewport 子树内。
9. 认领：`<ScrollList><Scrollbar id="bar"/><Frame/></ScrollList>` → `ScrollRect.verticalScrollbar` 是
   `bar` 的 uGUI 组件；根下只有一个 `Scrollbar` 节点（没建默认条）；`SlotCount == 1`（`Frame`）；
   `Get<Scrollbar>("list/bar")` 可达；`Children` 里有它但 `Content` 下没有。
10. 朝向翻转：`direction.mobile="horizontal"` 激活 / 退出前后是**同一个** `Scrollbar` 实例，三层矩形按
    §5.1 两列切换，`ScrollRect` 的 vertical / horizontal 引用互换、另一个为 null。
11. Variant：`thickness.mobile="4"` 到达活条；`handleGlow.mobile` 无 base 时退出变体后滑块表面关闭（自愈）。
12. Style / Theme：`<Scrollbar class="scrollbar"/>` + `<Theme>` 覆盖同名包 → `UI.Theme` 切换后 sprite / radius 重算。
13. Template：`<Template name="X"><Scrollbar thickness="6"/></Template>` + `<ScrollList><X/></ScrollList>` → 认领成功、`thickness` 生效。
14. 重复：两根 → 第一根接线、第二根不接线 + warning。
15. Dropdown 克隆：`PuiDropdown.CreateDropdownList(template)` 返回的克隆体里 `Scrollbar/__Surface` 与
    `Handle/__Surface` 的面板参数（radius / glow / fill）等于 template 上的。
16. `Dispose`：自建默认条随宿主释放；认领条由 Screen 释放，宿主不重复 `Dispose`（无异常）。

`Tests/EditMode/Lint/ScrollbarRulesTests.cs`（新）：§4.5 每一条的正反例；`PUI-PROC-SPRITE-CONFLICT` 对
`handle="ui:x"` + `handleRadius`（报）、`handle="none"` + `handleRadius`（不报）、`<Slider fill="ui:x" fillRadius>`（报，新增覆盖）；
`PUI-PROCEDURAL-VALUE` 对 `handleRadius="abc"` / `handleGlow="-1"`；RETIRED 对宿主属性与 `<Style>`。

改动的既有测试：`ScrollListScrollbarTests.cs`（删，内容迁入 1–6）、`InnerLayerSkinAttrTests.cs`
（滚动条用例改成 `<Scrollbar>` 子元素形态）、`DefaultSkinTests.cs` / `ScrollListTests.cs` / `DropdownTests.cs`
（节点路径 `Scrollbar` / `Template/Scrollbar`）、`XsdGeneratorTests.cs`（`ScrollList_lists_its_grid_and_scrollbar_sizing_attributes`
去掉两个退役属性；新增 `Scrollbar_lists_its_attributes`）、`BuiltinTagsTests` / `ProceduralAttrNamesTests`（自动守）、
`VariantBaseRulesTests` / `ThemeStyleRulesTests`（内层分组）。

## 8. SKILL / 文档更新（同一 PR 内，英文）

- `authoring-promptugui-xml/SKILL.md`
  - 原语目录加 `<Scrollbar>` 一行 + 指向 `reference/controls-scrollbar.md` 的 stub；「Which tags draw
    procedurally」列表加 `<Scrollbar>`。
  - `<ScrollList>` / `<Dropdown>` 属性表删 `scrollbar*` 行；`columns` 行的 `scrollbarWidth − 3` 改述；
    示例（`slots` 网格、Collapsible 里的列表）改成 `<Scrollbar>` 子元素；结构表两行改「`Scrollbar` 部件」。
  - 「Re-skinning a built-in」一段：`<ScrollList scrollbar*=>` / `<Dropdown … scrollbar*=>` 改为「the
    scrollbar is a `<Scrollbar>` child」。
  - 「内层也能给形状，但只给形状」一节加例外：`<Scrollbar>` 的滑块还给 border / glow，理由一句。
  - lint 码速查加 `PUI-SCROLLBAR-*`、扩展后的 `PUI-PROC-SPRITE-CONFLICT` 覆盖面。
- `authoring-promptugui-xml/reference/controls-scrollbar.md`（新）：属性表、几何公式与图、三种复用写法、
  overlay 与 spacing 的关系、Dropdown 克隆的限制、lint 表。
- `authoring-promptugui-xml/reference/controls-collapsible.md`：两处 `scrollbar=""` 示例改子元素；
  §「Horizontal folds …」提到的「a scrollbar skin for the …」按 `IScrollbarHost` 的现状改述。
- `scripting-promptugui-csharp/SKILL.md`：re-skin 列表那句去掉 `scrollbar*`，指向 `<Scrollbar>`。
- `CLAUDE.md` 的 SKILL 触发表：`<Scrollbar>` → `reference/controls-scrollbar.md`。
- 主 spec `2026-05-07 …description-language-design.md` §5 原语表加 `<Scrollbar>` 行（部件子元素，
  宿主 `<ScrollList>` / `<Dropdown>`）。
- `Samples~/ProceduralStyle/README.md` 第 140 行附近的示例改子元素形态。

## 9. 演示同步（与代码同一 PR）

| 文件 | 改动 |
|---|---|
| `Samples~/CommonControls/Resources/UI/CommonControls.ui.xml` | 全局 `<Style name="list">` / `<Style name="dropdown">` 删 `scrollbar*` 四项；新增全局 `<Style name="scrollbar" sprite="…#pugui_9slice_inset" color="white" handle="…#pugui_9slice_round" handleColor="white"/>`；玻璃主题 `<Theme>` 里同名包 `sprite="none" color="white/0.18" handle="none" handleColor="white/0.55"`（原 list / dropdown 包里的值原样搬过来）；`id="skin"` / `id="quality"` 两个 `<Dropdown>` 与 `id="list"` 的 `<ScrollList>` 各加 `<Scrollbar class="scrollbar"/>` 子元素；文件头注释第 34–35 行的钩子清单改述。**一个 `scrollbar` 包同时穿在三个宿主上 —— 这就是需求方要的复用，演示里直接体现。** |
| `Samples~/ProceduralStyle/Resources/UI/ProceduralStyle.ui.xml` | `id="theme"` / `id="quality"` 两个 `<Dropdown>` 与 `id="list"` 的 `<ScrollList>`：四个 `scrollbar*` 属性改为 `<Scrollbar sprite="" color="bg-bottom/0.6" handle="" handleColor="ink-dim"/>`；`id="list"` 那根顺势展示新能力：`thickness="6" radius="pill" handleRadius="pill" padding="1"`（这是程序化样式的 showcase，滚动条也该是程序化的）。 |
| `Samples~/ProceduralStyle/README.md` | 示例段同步。 |

两份 sample 改完各跑一遍 `dotnet run --project .lint/UIXmlLint -- Samples~/…`，并在宿主工程 UIPreview
里过目（§11-5 的验收就在这里做）。

## 10. 非目标

- 滚动条 hover / pressed 状态色与 `<Show on="state-*">` 门控（uGUI ColorTint 自带的够用；要做另起需求）。
- 端头箭头钮；双轴滚动条（ScrollList 仍单轴）。
- `<Slider>` / `<Progress>` 内层扩到 border / glow（只顺带补它们的 sprite 冲突 lint）。
- `<Collapsible>` / `<Markdown>` 加滚动条（`IScrollbarHost` 留好了口，各自另立需求）。
- Dropdown 的 `popupRadius` 等弹窗程序化属性（§5.5 打开了门，本文不走）。
- `<Scrollbar>` 的 `tint` / `handleInnerGlow` / `handleIntensity`。
- `<Scrollbar>` 接子节点（`<Decor>` 等）。

## 11. 已定的决策（2026-09-12 与作者对齐）

1. **子元素 `<Scrollbar>`，不做宿主前缀属性、不做按名引用模板。** 复用走既有 `<Style>` / `<Template>` / `<Theme>`。
2. **滑块用 `handle*` 前缀**（对齐 `<Slider>`），不嵌 `<Handle>`。
3. **不向前兼容。** ScrollList 6 个、Dropdown 4 个 `scrollbar*` 属性直接删除，不留转发器；CLI 用
   `PUI-SCROLLBAR-RETIRED-ATTR` 指路。（作者原话：项目用的人还不多，把以前 Dropdown 等遗留的问题也改了。）
4. **Dropdown 纳入本期**，与 ScrollList 共用同一个控件；弹窗克隆问题按 §5.5 解。
5. **项目内演示同步修改**（§9），且 ProceduralStyle 的列表滚动条改成程序化外观。
6. **轨道允许 `glass`（继承来的，不特殊化）；滑块没有 `glass`。**
7. **厚度叫 `thickness`**，避开通用 `width` / `height`；`padding` 两值按「沿轴, 跨轴」，与需求稿的
   `V,H` 在竖条上一致、横条上转置（需求稿的标注是按竖条写的）。
8. **一个节点按朝向翻转**，取代两根懒建条；节点名 `Scrollbar`。

## 12. 开放问题（留给 plan / 实现期）

- `AdoptScrollbar` 的时机：路由后立即（子节点尚未 Apply，`applyOrder` 延迟模式下尤其）还是宿主
  `OnAfterApply` 里统一？倾向前者（接线尽早，`Orient` 用 `PreConfigureContent` 已知的朝向），宿主
  `OnAfterApply` 只兜底建默认条；ReSolve 的顺序问题靠「宿主拉 + 条推」双向幂等消解。
- 宿主几何变化（`thickness` 变体切换）后 uGUI `Scrollbar` 的 `size` / `value` 是否需要 `SetDirty`：
  预计 `ScrollRect.LateUpdate` 会重算，若不重算则在 `OnAfterApply` 末尾 `LayoutRebuilder.MarkLayoutForRebuild`。
- 默认条要不要也命名为 `Scrollbar`（与认领条同名，测试路径统一）—— 倾向要。
- `ProceduralAttrNames.InnerLayerRadius` 是否保留为 `InnerLayerGroups` 的扁平投影，避免改 `ThemeStyleRules`
  之外的调用点；plan 时看引用数。
- `Scrollbar` C# 面：`Bar`（uGUI 组件）要不要公开 —— 等有调用方再说。

## 13. 里程碑拆分

- **M0 骨架（可独立合并）**：`Scrollbar` 控件 + `IScrollbarHost` + ScrollList / Dropdown 认领与默认条 +
  退役旧属性 + 路由 + `thickness` / `overlay` / `spacing` / `padding` + 样式 4 属性 + `PuiDropdown` 克隆同步 +
  测试 1–6、9、10、14、16 + lint OUTSIDE / DUPLICATE / IN-ADD / LAYOUT-ATTR / CHILD / VALUE / OVERLAY-SPACING /
  RETIRED + XSD + 演示 + SKILL。结束态：外观与 PR #131 之后逐像素一致，语法已是新的。
- **M1 程序化表面**：轨道继承属性接通、`handle*` 五个形状属性 + `AddInnerSurface(selectable)` +
  测试 7、8、11–13、15 + `PUI-PROC-SPRITE-CONFLICT` / `PUI-PROCEDURAL-VALUE` / 内层分组三条 lint 扩展 +
  ProceduralStyle 演示的程序化滚动条 + `reference/controls-scrollbar.md` 表面章节。结束态：需求 §5 的
  验收画面在 UIPreview 里成立。

两个里程碑可以一个分支一个 PR，也可以合一；M0 单独可合并是为了让「删旧语法」这件破坏性的事有一个
干净的、行为不变的落点。
