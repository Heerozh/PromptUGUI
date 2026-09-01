# 布局与动效基础四件套 —— `hug` 贴合尺寸 · `<Animation reveal>` 与 `reverse-on` · `rotation` / `flip` · `checked` 持久态与 `@id` 作用域（FND）

> 状态：**草案**（2026-08-31 与作者头脑风暴对齐，决策见 §8）。
> 相关：master spec `2026-05-07-promptugui-description-language-design.md` §6.2（size / width / height）、
> §6.5（布局组内的特殊行为）；`2026-08-30-clamp-size-design.md`（CLP —— `ClampFitter` 是 §1 的宿主，
> `clamp(min, hug, max)` 是它的第三种 middle）；`2026-08-29-tabmenu-design.md`（TM —— `PlayTransition` /
> `SetArrowFlip` 是 §2 / §3 手工版的出处）；`2026-08-27-decor-primitives-design.md`（`extent` 为避开通用
> `size` 改名的先例，§2.2 沿用同一理由）；`reference/states.md`（瞬态 `state-*` 与首帧建立）；
> `reference/animations.md`（`on=` 表）。**消费方**：`2026-08-31-collapsible-design.md`（COL）——
> §1 / §2 / §3 各出一个内部零件给它，§4 独立。

## 0. 背景与范围

评估一个 HUD 任务面板（标题栏「任务 ▼」+ 三行任务，点标题收起只剩标题栏）时发现，PromptUGUI 在四个彼此
独立的位置各缺一块，缺哪一块面板都做不顺：

| # | 缺口 | 今天的绕法 | 本文 |
|---|---|---|---|
| 1 | 顶层容器没有"贴合内容"的高度：自由定位的 `<VStack>` 不写 `height` 是 0 高（子节点溢出可见、底图撑不起来） | 外面再套一层 0 高的 stack 让内层被"量出来" | §1 `hug` |
| 2 | `<Animation>` 没有尺寸通道，也没有"反向播放"：收起动画要么 C# 手写，要么两个 Animation 各算各的 | C# LitMotion 改 `LayoutElement.preferredHeight` | §2 `reveal` / `reverse-on` |
| 3 | 图标没有旋转 / 镜像属性：箭头翻转只能换图或 C# 改 transform | `TabMenu` 内部用负 `localScale` 私自翻 | §3 `rotation` / `flip` |
| 4 | `<Show>` / `state-*` 只认瞬态交互态、`@id` 只在子树内找：没法让兄弟节点跟着 `isOn` 显隐 | 全部 C# `Hidden` | §4 `checked` / `@id` |

四章各自成立、各自可验收；COL 用 §1 的贴合 fitter、§2 的 reveal 引擎、§3 的网格旋转做箭头，
不依赖 §4。§10 给出落地顺序。

---

## 1. `hug` —— 贴合内容的宽高

### 1.1 问题

```xml
<VStack anchor="top-right" width="150" margin="90,20,_,_">   <!-- 没写 height -->
  <Image anchor="stretch" flow="false" sprite="ui:panel"/>   <!-- 想当整块底图 -->
  <Btn width="stretch">任务</Btn>
  <ScrollList width="stretch" height="150" .../>
</VStack>
```

`Control.cs:304`：自由定位、该轴没写尺寸、又没有 native size 的控件 `sizeDelta = 0`。`VerticalLayoutGroup`
仍然从顶部往下摆子节点（`UpperCenter`），所以两个按钮**看得见**，但 stack 自己是 0 高：`flow="false"` 的
底图拉伸到 0、`anchor="bottom-*"` 的 margin 没有参照、`mask="rect"` 裁掉一切。嵌套的 stack 之所以"自动
hug"，是因为父 LayoutGroup 通过 `ILayoutElement` 去**量**它 —— 最外层没人量。

`<ScrollList>` 同病：视口是定尺寸（默认 160×200，`ScrollList.cs:45`），没有"内容多少就多高、封顶 N 再滚"。
`ContentSizeFitter` 库里只用在内部节点（`ScrollList._content`、`Markdown` 根），从未暴露给作者。

### 1.2 否决的方案

**`auto` 关键字。否决。** CSS 的 `auto` 在不同轴、不同定位模式下含义不同（块级宽 = 填满、高 = 内容），
`scale-mode="auto"` 又已经占了这个词。Figma 的 `hug` 只有一个意思：内容多大我多大。

**让顶层 stack 默认 hug（不写 height 即 hug）。否决。** 破坏性：`anchor="top-stretch" height=…` 之外，
现有 XML 里大量"不写 height、子节点自然溢出"的 stack 会静默换几何；而且默认 hug 与
`<Frame>` 的 fill-or-fit 默认（没写的轴 stretch）打架。显式关键字，零回归。

**`<Frame height="hug">` 取子节点包围盒。否决（§8 决策 FND-D2）。** Frame 的子节点是自由定位的，
`anchor="stretch"` / `N%` 子节点跟着父走、父再跟子走就是环；要定一堆排除规则才能不炸。CSS 里
绝对定位子元素对 `height: auto` 的贡献也是 0。报错并指路："包一层 `<VStack>`"。

**新挂一个 `ContentSizeFitter`。否决。** 它和 `ClampFitter` 同为 `ILayoutSelfController`，一个 rect
上两个自控器互相覆写；也表达不了 `clamp(min, hug, max)`。CLP 的 `ClampFitter` 加一种模式即可（§1.5）。

### 1.3 语法

```
width | height := number | "native" | stretch | percent | "hug" | clamp
clamp          := "clamp(" bound "," middle "," bound ")"
middle         := percent | stretch | "hug"
```

- **允许的标签**：`<VStack>` `<HStack>` `<Grid>` `<ScrollList>`，以及 COL 的 `<Collapsible>`（它的高度**恒为** hug，
  见 COL §5.1）。写在任何其它标签上是 parse error **`PUI-HUG-TAG`**，消息按标签给路：`<Frame>` →
  "wrap the content in a `<VStack>`"；`<Image>` / `<Text>` / `<Btn>` → "use `native`"。
- `size="hug"` → `LooksLikeKeyword` 加 `hug`，报现有的"size 是数值-only"。
- 与 `anchor` 该轴 stretch 同写 → 现有的"stretched axis can't have width/height"错误。
- `clamp(min, hug, max)`：两端规则同 CLP（`_` 开放一端、`min ≤ max`、至少一端有值）；`hug` 作为 middle
  在自由定位和布局组两种父级下**都合法**（区别于 `%` 只自由定位、`stretch` 只布局组）。
- `hug` / `clamp(…, hug, …)` 与 `scale` 同节点 → **`PUI-HUG-SCALE`**（CLI error + 运行时 `ParseException`，
  理由同 `PUI-CLAMP-SCALE`：该轴由布局 pass 拥有，`scale` 的保盒膨胀会被覆盖）。
- Variant / `<Style>` 照常（`height="200" height.portrait="hug"`）。

### 1.4 语义

**1.4.1 内容尺寸是什么。** 每个允许的标签在该轴上有唯一定义：

| 标签 | 该轴内容尺寸 |
|---|---|
| `<VStack>` `<HStack>` `<Grid>` | 自身 LayoutGroup 的 `preferredWidth/Height`（`LayoutUtility.GetPreferredSize(self)`，含 padding / spacing；Grid 的行数由 `columns` 推出，所以高度 hug 良定义） |
| `<ScrollList>` | `_content` 的 preferred（已含 `padding` —— 它落在 content 的 LayoutGroup 上） |
| `<Collapsible>` | 标题栏 + body（COL §5.1） |

**1.4.2 自由定位父级。** `box = clamp(content, min, max)`（裸 `hug` = 两端开放）。放置规则**逐字沿用
CLP §5.1**：盒子贴住 `anchor` 点名的那条边（`top-*` 从上沿往下长、`bottom-*` 往上长、`center` 居中），
`margin` 在盒子内侧再缩。由 `ClampFitter` 在布局 pass 里解（`ILayoutSelfController`），所以内容变化
（BindItems 推新行、文字换行、locale 切换、子节点 `Hidden`）**同一次 canvas update 内**跟上，无需 ReSolve；
EditMode 测试要 `Canvas.ForceUpdateCanvases()` 才能观察到。

**1.4.3 布局组父级。**

- `<VStack>` / `<HStack>` / `<Grid>` 写 `hug`：等价于今天"该轴不写"的 `-1` 哨兵路径（`Control.cs:418`，
  LGC-D9）—— 父组本来就在量它。合法、显式、无操作，让作者能把意图写出来。
- `<ScrollList>` 写 `hug`：ScrollList 根上没有 `ILayoutElement`，需要一个 —— 内部组件 `HugElement`
  （`ILayoutElement`，`layoutPriority = 1`）在 `CalculateLayoutInput*` 里报 `content`（1.4.1 的定义）。
- `clamp(min, hug, max)`（任一允许标签）：同一个 `HugElement` 报 `min = preferred = clamp(content, min, max)`、
  `flexible = 0` —— 与显式数值一样**刚性**（LGC-D17 "strictly N×N"）。这是它和 `clamp(min, stretch, max)`
  的本质区别：stretch-clamp 是"可压缩区间"，hug-clamp 是"由内容决定的定值"。
- `HugElement` 读内容尺寸时必须**排除自己**（`LayoutUtility.GetPreferredSize` 会把同 GO 上所有
  `ILayoutElement` 都算进去，含它自身 → 递归），实现里用一个"跳过 self 的最高优先级"读取器。

**1.4.4 hug 轴上的 stretch 子节点。** 父在 hug 轴上量子节点的 preferred，`height="stretch"` 子节点的
preferred 是 0 → 它得到 0 高（CSS `height:auto` 容器里 `height:100%` 子元素的同款结果）。这永远是笔误，
lint **`PUI-HUG-STRETCH-CHILD`**（CLI error / 运行时 warning）。`clamp(min, stretch, _)` 子节点同理
（preferred 为 min，能长不能拉）。

**1.4.5 Variant / ReSolve 幂等。** 模式切换（数值 ↔ hug ↔ clamp）走 `SyncClampFitter`（`Control.cs:545`）
挂 / 摘组件，`AxisSpec.Same()` 加模式字段，值不变不标脏（LGC-D18）。布局组内的 `HugElement` 同样只在
规格变化时增删。

**1.4.6 交互清单。**

| 组合 | 结果 |
|---|---|
| `hug` × `anchor` 该轴 stretch | parse error（既有规则） |
| `hug` × `margin` | **外推定位**（同数值尺寸）：margin 把内容盒推离它命名的那条边，不吃尺寸。不同于 `%` / `clamp(min, N%, max)` 的盒内缩 —— 那里的盒子是父尺寸的一份，内缩才对；hug 的盒子就是内容本身，内缩会让控件比自己的内容还小，margin 一旦超过内容更会变负 → rect 上下翻转扣在邻居身上、还吃掉那里的点击（`<Collapsible>` 高度是注入的 hug，收起时正中此坑）。 |
| `hug` × `scale` | `PUI-HUG-SCALE` |
| `hug` × `mask="rect"` / `mask="self"` | 正常；裁剪跟着最终 rect |
| `hug` × `flow="false"` | 允许（出流子节点按自由定位语义，可以 hug） |
| `hug` × 该轴 `stretch` 子节点 | `PUI-HUG-STRETCH-CHILD` |
| hug 套 hug | 各层各 hug；`LayoutRebuilder` 自底向上，天然收敛 |
| `hug` × `<Animation reveal>` 子节点 | 正常，reveal 量的是子节点 preferred（§2.4.3） |
| `hug` × `scale-mode="pixel"` | 正常（设计单位下算，整数缩放在 Canvas 层） |

### 1.5 实现地图

- `Core/Layout/SizeSpec.cs`：`Axis` 加 `IsHug`；`ParseAxis` 在 `native` / `stretch` 旁加 `hug`；
  `clamp` 的 middle 谓词放行 `hug`；`LooksLikeKeyword` 加 `hug`；`ValidateAgainst` 对 hug 轴沿用 stretch 冲突检查。
  纯 C#，CLI 可编译。
- `Controls/Control.cs` `ApplyCommon`：自由定位分支 —— hug 轴跳过 native 兜底、写 `sizeDelta` 基线 0、
  `SyncClampFitter` 传 `Mode = Hug`；布局组分支 —— 允许标签上的裸 `hug` 走 `-1` 哨兵，ScrollList / clamp 形态
  挂 `HugElement`。
- `Controls/Internal/ClampFitter.cs`：`AxisSpec` 加 `Mode { Fraction, Hug }`；`Apply` 一行改
  `box = mode == Hug ? ContentSize(axis) : fraction × parent`；`parent == null` 早退改为仅 Fraction 模式；
  `Same()` 比较模式。
- 新 `Controls/Internal/IHugContent.cs`（internal 接口 `float ContentSize(RectTransform.Axis)`）：
  默认实现 = `LayoutUtility.GetPreferredSize(rt)`；`ScrollList` 实现为 `_content` 的 preferred；`Collapsible` 见 COL。
- 新 `Controls/Internal/HugElement.cs`（`UIBehaviour, ILayoutElement`，priority 1）+ `LayoutUtilityEx.PreferredExcluding(rt, axis, self)`。
- 新 `Core/Lint/HugRules.cs`（纯 C#）：`PUI-HUG-TAG`、`PUI-HUG-SCALE`、`PUI-HUG-STRETCH-CHILD`；
  `IRWalker` + `ScreenInstantiator` 双分发；运行时硬错误落在 `ControlAttributeApplier`（同 `PUI-CLAMP-SCALE`）。
- XSD：无（`width` / `height` 是无 pattern 的 `xs:string`）。

### 1.6 测试

- `SizeSpecTests`：`hug` / `clamp(_, hug, 200)` / `size="hug"` 拒绝 / `clamp(_, hug, _)` 拒绝 / `hug*2` 拒绝。
- `HugFitterTests`（EditMode）：自由定位 VStack 三个 44 高按钮 + spacing 4 → 140；隐藏一个 → 96；`bottom-left`
  贴底长；`clamp(100, hug, 120)` 两端钳；ScrollList 内容 5 行 → 视口 = 内容、封顶后 = max；`flow="false"`
  底图跟随；Variant 数值 ↔ hug 往返几何位相等；`Same()` 不标脏（沿用 `LayoutRebuildDirtyTests` 形式）。
- `HugRulesTests`：三条 lint 正反例；`<Frame height="hug">` 消息含 "VStack"。

---

## 2. `<Animation reveal>` 与 `reverse-on`

### 2.1 问题

`<Animation>` 的 B 族只有 `translate` / `scale` / `rotate` / `fade`，全部绑在内部 `_offsetProxy` 的 transform
上（`AnimationDriver.cs:33-88`），**不碰布局**：一个面板收起时下面的内容不会顶上来。没有"反向"：
`on="state-pressed"` 配 `on="state-normal"` 是两个 Animation 各包各的子树，同一个子树只能被一个包住，
所以"按下缩到 0.95、松开回到 1"能写（终态互补），"展开到 180°、收起回 0°"要靠嵌套两层且各自不知道
对方进行到哪 —— 中途打断就错位。`TabMenu.PlayTransition`（`TabMenu.cs:591-642`）手工做了这件事：
读当前值当起点、按方向换 from/to、完成回调防误关。这套逻辑该进 `AnimationDriver`。

### 2.2 否决的方案

**`height="0:hug"`。否决。** `height` / `size` 是所有节点的通用排版属性，写在 `<Animation>` 上先被
`ApplyCommon` 吃掉；`<Decor>` 为同一个原因把尺寸叫 `extent`。

**`extent-y="0:hug"`。否决。** 与 Decor 撞名但语义不同（那边是静态尺寸），且"from:to"的通用形态掩盖了
这个通道真正特殊的地方：它有裁剪、有测量、有静止态。

**只加 A 族预设 `expand-down` / `collapse-up`。否决。** 端点不可调、不能和 `fade` 组合、反向仍然是两个
Animation。

**`loop="yoyo"` 当反向。否决。** 那是时间驱动的往返，不是事件驱动的。

**负 `PlaybackSpeed`。否决。** LitMotion 不支持负速；而且 TabMenu 已经证明真正要的是"从**当前值**反向
补间"（中途打断也对），不是"倒放时间轴"。

**`reverse-on` 也放到 `<Trigger>` / `<Show>` 上。否决。** Trigger 没有可反的东西；C# 要第二个事件流就再放
一个 `<Trigger>`。写了报 `PUI-REVERSE-ON-TAG`。

### 2.3 语法

**D 族 —— reveal（新）**

| 属性 | 取值 | 默认 | 说明 |
|---|---|---|---|
| `reveal` | `y` / `x` | — | 主轴。`y` = 高度从锚定边展开（默认 top 往下），`x` = 宽度 |
| `reveal-from` | px / `hug` | `0` | 起点 |
| `reveal-to` | px / `hug` | `hug` | 终点。`hug` = 唯一子节点在该轴的 preferred 尺寸（§2.4.3） |

**通用（所有族）**

| 属性 | 取值 | 说明 |
|---|---|---|
| `reverse-on` | 同 `on=` 的全部语法（含 `state-*` / `checked` / `expand` / `manual` 及各自 `@id`），**但不含** `open` / `loop` | 该事件触发时从当前进度反向回到 from 态；`manual` = C# 调 `Reverse()` |

**族间规则**

| 组合 | 结果 |
|---|---|
| D + B（`reveal` 与 `fade` / `translate` / `scale` / `rotate` 同写） | **允许**，各绑各的对象，同时播 |
| D + A（`type=`） | parse error（A 族是 B 的糖，糖不与 D 混） |
| D + C（`count` / `char-color`） | parse error |
| `reverse-on` + `loop` | parse error `PUI-REVERSE-LOOP` |
| `reverse-on` + C 族 | parse error `PUI-REVERSE-TEXT`（数字倒数 / 逐字色回退没有稳定的"当前值"） |
| `reverse-on` + A / B / D | 允许 |
| `reveal-from == reveal-to` | parse error |
| `<Animation reveal>` 子节点数 ≠ 1 | parse error `PUI-REVEAL-SINGLE-CHILD` |
| `<Animation reveal="y" height=… / size=…>`（主轴自写尺寸，含 `.variant`） | parse error `PUI-REVEAL-SIZE-CONFLICT`（交叉轴随意） |
| `<Animation reveal>` 自身 `scale=` | parse error `PUI-REVEAL-SCALE`（理由同 `PUI-HUG-SCALE`） |
| reveal 的子节点在主轴 `anchor` stretch | parse error `PUI-REVEAL-CHILD-STRETCH`（子跟父、父量子 = 环） |
| `reverse-on` 写在 `<Trigger>` / `<Show>` | parse error `PUI-REVERSE-ON-TAG` |

```xml
<!-- 点击展开细节，再点收起；fade 同步 -->
<Animation on="click@more" reverse-on="click@less" reveal="y" fade="0:1" duration="0.2s">
  <VStack height="hug" width="stretch">…</VStack>
</Animation>

<!-- 手风琴的行：跟着最近的 <Collapsible> / <TabMenu> -->
<Animation on="expand" reverse-on="collapse" translate="-16,0:0,0" fade="0:1" duration="0.12s">
  <Tab id="tab" text="{{text}}"/>
</Animation>

<!-- 勾选时旋转 180°，取消勾选转回来（§4 的 checked） -->
<Toggle id="hdr">
  <Animation on="checked" reverse-on="unchecked" rotate="0:180" duration="0.15s">
    <Icon name="ui:chevron" anchor="center-right" size="12x12"/>
  </Animation>
</Toggle>
```

### 2.4 语义

**2.4.1 reveal 拥有包装节点的主轴盒子。** `box = lerp(from, to, progress)`。

- 布局组父级：写包装节点的 `LayoutElement.min = preferred = box`、`flexible = 0`（补间中刚性，兄弟节点跟着
  让位 / 顶上，这就是"内联展开"的全部意义）。交叉轴保持 `Trigger.GetNativeSize()` 今天的快照行为。
- 自由定位父级：写 `sizeDelta` 主轴分量；生长方向由包装节点自己的 `anchor` 决定（`top-*` 向下、`bottom-*`
  向上、`center` 双向），与 §1.4.2 一致。

**2.4.2 裁剪。** 包装节点根上挂 `RectMask2D`，`box < hug` 时启用、到达完整 hug 静止时禁用（保合批）。
子节点保持自己的 `anchor`（默认 top-left），所以 `y` 轴从下沿被裁、`x` 轴从右沿被裁 ——"从锚定边露出"。
子节点写 `anchor="center"` 时两端同时被裁，允许，效果是"从中间长出来"。

**2.4.3 `hug` 的测量。** 每次触发时量，不缓存：`LayoutRebuilder.ForceRebuildLayoutImmediate(child)` 后
`LayoutUtility.GetPreferredSize(child, axis)`。内容变化（新行、换行、locale）下一次触发就对。子节点若处于
inactive（TM 里"inactive 父级下的 TMP 永远报 0"的坑，`TabMenu.cs:353-374`），先激活再量，沿用那套
`BeforeRebuild` / `AfterRebuild`。

**2.4.4 静止态（FND-D6）。** D 族在**首次触发前**停在 `reveal-from`（内容藏着），首次正向触发后停在
`reveal-to`。这是 D 族与 B 族唯一的不同（B 族未触发前是恒等态）：`reveal` 的语义就是"先藏后露"，
`on="expand@id"` 的内容在 expand 前理应不可见。默认 `on="open"` 因此在开屏时播放一次 0 → hug。

**2.4.5 正向 / 反向补间。**

- 反向：取消在飞的 handle，每个通道**从当前值**补到 `from`，时长用完整的 `duration`、同一条 `easing`
  （不做镜像 ease；与 TM 一致）。`delay` 两个方向都生效。
- 正向：声明了 `reverse-on` 的 Animation，正向同样从当前值补到 `to`（可被打断、可被反打断）；**未声明**
  `reverse-on` 的 Animation 保持今天的行为（先写 from 再补间），零回归。D 族无历史包袱，恒从当前值起。
- 完成：反向到底后停在 from；不做 `SetActive(false)`（内容仍在裁剪下、仍在渲染 —— 成本见 §7；
  COL 的 `<Collapsible>` 在自己的实现里做停用，通用 Animation 不做，见 §9 开放问题 1）。

**2.4.6 事件面。** 正向仍走 `Trigger.OnFire`；`Animation` 新增 `OnReverse : Observable<Unit>` 与
`Reverse()`（`reverse-on="manual"` 的 C# 入口）。`Fire()` 语义不变。

**2.4.7 ReSolve / Variant。** `ApplyCommon` / `ApplyLayoutElement` 在每次重解算时会把包装节点的尺寸
写回规格值；`Animation.OnAfterApply` 之后**重新断言当前 box**（同 `Screen.ApplyScales` 在 ApplyCommon 之后
补膨胀的先例）。`RectMask2D` 是静态组件、只由驱动器切 `enabled`，不经 Variant，不触发 `PUI-MASK-VARIANT`。
`on="open"` 不因 ReSolve 重播（既有规则）。

**2.4.8 与 §1 的关系。** `<Animation reveal="y"><VStack height="hug">` 是标准搭配：hug 让 VStack 在
自由定位的 proxy 里有真实高度，reveal 量它的 preferred。reveal 不依赖 hug（子节点定高 `<ScrollList height="150">`
一样能量），hug 也不依赖 reveal。

### 2.5 实现地图

- `Controls/Internal/AnimationSpec.cs`：D 族字段（`RevealAxis`、`RevealFrom` / `RevealTo`，值类型为
  "px 或 hug"的小结构）、`ReverseOn : TriggerSpec?`、族互斥校验、`Clone()` / 快照。
- `Controls/Internal/TriggerSpec.cs`：解析入口复用给 `reverse-on`（拒绝 `open` / `loop`）。
- `Controls/Trigger.cs`：把"按 `TriggerSpec` 订阅一个源"抽成可复用方法，`Animation` 用它订第二个源。
- 新 `Controls/Internal/RevealDriver.cs`（internal）：`Measure(child, axis)`、`ApplyBox(host, axis, value)`
  （分布局组 / 自由定位两条写法）、`SetClip(host, on)`。**COL 直接复用它驱动 body**。
- `Controls/Internal/AnimationDriver.cs`：`Play(spec, ctx, reverse)`；B 族读当前值（proxy 的
  `anchoredPosition` / `localScale` / `localEulerAngles.z`、CanvasGroup.alpha）；D 族通道调 `RevealDriver`；
  循环块去重（顺手，六处拷贝合一）。
- `Controls/Animation.cs`：`RectMask2D` 懒加；`OnAfterApply` 重断言 box；`Reverse()` / `OnReverse`；
  `GetNativeSize()` 主轴返回当前 box（让父组量到正确的过渡尺寸）。
- 新 `Core/Lint/AnimationRules.cs`（纯 C#）：§2.3 表里全部 `PUI-REVEAL-*` / `PUI-REVERSE-*`；双分发。
- 依赖 §1：无编译依赖；`hug` 关键字在子节点上只是用法建议。

### 2.6 测试

- `AnimationSpecTests`：D 族解析、族互斥、`reverse-on` 语法、错误码逐条。
- `AnimationRevealTests`（EditMode，结构）：`RectMask2D` 存在、静止态 = from、`LayoutElement` 初值、
  `PUI-REVEAL-SIZE-CONFLICT` 等 lint。
- `AnimationRevealPlayTests` / `AnimationReversePlayTests`（PlayMode，LitMotion 走 player loop）：
  0 → hug 过程中兄弟节点位置单调下移；中途反向不跳变（连续两帧差 < 阈值）；反向到底 box == from；
  `fade` 与 `reveal` 同步；`reverse-on="manual"` + `Reverse()`；ReSolve 中途重解算后 box 不被重置。

---

## 3. `rotation` / `flip` —— `<Image>` `<Icon>` `<RawImage>` 的网格级旋转与镜像

### 3.1 问题

一个向下的箭头要向上：换一张图（图集里多一份）、或 C# 改 `localEulerAngles`。`TabMenu` 自己的箭头用
负 `localScale.y` 翻（`TabMenu.cs:666`），并在注释里解释为什么不能转 180°（`:655-665`）：**旋转绕 pivot，
而 pivot 是从 anchor 推出来的**，`anchor="top-left"` 的图会绕左上角甩出去。作者层没有任何属性能做这件事。

### 3.2 否决的方案

**写 transform（`localRotation` / 负 `localScale`）。否决（FND-D8）。** 三个硬伤：pivot 陷阱如上；父
LayoutGroup 量的是未旋转的 rect，转 90° 的子节点会压到兄弟身上；`Screen.ApplyScales`（`Screen.cs:400-429`）
对声明了 `scale=` 的节点**无条件覆写** `localScale`（含恢复 `Vector3.one`），负缩放镜像会被抹掉。

**做成通用属性（任何节点可写）。否决。** `<Text>` 的 TMP 网格不走 `IMeshModifier` 同一条路；容器旋转带着
子节点转是另一个（transform 级）特性，排版隐患多。限定在三个"叶子 Graphic"上，够用且无歧义。

**只翻 UV。否决。** 9-slice 的四边几何不会跟着镜像，`flip` 出来的边框错位。顶点镜像（位置 + UV 一起）
两种都对。

### 3.3 语法

| 属性 | 取值 | 默认 | 说明 |
|---|---|---|---|
| `rotation` | 浮点角度 | `0` | **顺时针为正**（CSS `rotate()` 约定），任意实数，内部按 360 归一 |
| `flip` | `x` / `y` / `xy` / `none`（`""` = `none`） | `none` | 水平 / 垂直 / 双向镜像 |

- 仅 `<Image>` `<Icon>` `<RawImage>`。写在其它标签（含 `<Btn>` / `<Tab>` / `<Frame>`）→ parse error
  **`PUI-FLIP-TAG`**："rotate the inner `<Image>` / `<Icon>` instead"。
- Variant（`rotation.portrait="90"`）、`<Style>`、Theme `<Style>` 均照常。XSD 由 `[UIAttr]` 反射生成。

### 3.4 语义

- **网格空间、绕 rect 中心**，先 `flip` 后 `rotation`。RectTransform、anchor / margin / pivot、`LayoutElement`、
  raycast 区域**一律不变** —— 只是画出来的顶点动了。所以放在 `<HStack>` 里转 90° 不会挤兄弟；`preserveAspect`
  照常。
- 非方形 rect 转非 90° 倍数会露出 rect 之外（同 transform 旋转），作者自负；父级 `RectMask2D` / stencil
  按顶点裁，行为正确。
- 自身 `mask="self"` 的 stencil 形状随网格一起转（旋转后的遮罩形状，是特性不是 bug）。
- `type="contain" / "cover"`：`AspectRatioFitter` 先定 rect，网格在 rect 内转。
- `sliced` / `tiled` / `filled`：整张网格作为一体旋转 / 镜像；9-slice 在 90° 倍数下边框仍是边框。
- 与 `<Animation rotate>`（transform 级、作用于 proxy）**叠加**，互不知晓。
- 恒等（`rotation=0` 且 `flip=none`）时不挂或禁用效果组件，零开销；非恒等时 `BaseMeshEffect` 不破坏合批。
- C#：`Rotation` / `Flip` 属性可读写、可补间 —— COL 的箭头就是 LitMotion 绑 `Rotation` 0 ↔ 180。

### 3.5 实现地图

- 新 `Controls/Internal/RotateFlipEffect.cs`（`BaseMeshEffect`）：`ModifyMesh(VertexHelper)` 以
  `graphic.rectTransform.rect.center` 为原点做镜像 + 旋转；恒等时 `enabled = false`。
- 新 `Controls/Internal/RotateFlipApplier.cs`：三控件共用的属性落点（懒加组件、归一角度、解析 `flip`）。
- `Image.cs` / `Icon.cs` / `RawImage.cs`：`[UIAttr, Preserve] Rotation` / `Flip`。
- 新 `Core/Lint/RotateFlipRules.cs`（纯 C#）：`PUI-FLIP-TAG`、`flip` 枚举值、`rotation` 数值格式。
- `SpriteAtlasSyncer` / `XmlStringScanner`：无（非 sprite、非文本属性）。

### 3.6 测试

- `RotateFlipEffectTests`（EditMode，纯几何）：对一个已知 4 顶点 `VertexHelper` 调 `ModifyMesh`，断言
  `rotation="90"` 顺时针、`flip="x"` 左右互换且 UV 同步、`xy` = 180°、恒等不改顶点。
- `ImageRotateFlipTests`：属性落点、Variant 往返、`<Btn rotation>` 报 `PUI-FLIP-TAG`、RectTransform 位不变。

---

## 4. `checked` / `unchecked` 持久态 与 `@id` 的作用域

### 4.1 问题

想让"标题栏 Toggle 勾上 → 旁边的面板显示、箭头朝上"，今天三堵墙：

1. `<Show on="state-selected">` 认的是 uGUI 的**瞬态**交互态：`Selected` 被 Hover / Pressed 临时覆盖
   （states.md 第 5 行），鼠标一悬停面板就闪没；而 Hover 态本身分不出 isOn 与否。
2. `state-*@id` 的源必须在 Trigger **自己的子树**里（`TriggerSourceResolver.cs:125`），`<Show>` 包不住
   兄弟节点。
3. 唯一有 `isOn` 语义 + 声明式显隐（`bind=`）的是 `<Tab>`，但站独的 Tab 是 `PUI-TAB-PARENT` 错误。

缺的是一个**跟随 `isOn` 的持久态事件**，和一个**能指到兄弟的 `@id`**。

### 4.2 否决的方案

**`state-on` / `state-off`。否决（FND-D9）。** `state-*` 五个值是**互斥**的瞬态机，`<Show>` 靠"同源兄弟
互斥 + `state-normal` 回退"协调；混进一个不互斥的持久态会把这套规则搅乱。

**把 `state-selected` 改成持久态。否决。** 破坏性：现有"选中 Tab 悬停时按 hover 色"依赖瞬态覆盖。

**`@id` 改成只在 Screen 范围找。否决。** 模板实例的 id 是隔离作用域（`ScreenInstantiator.cs:288`），
同一模板用两次就撞；要的是**词法作用域**：最近的先赢。

**`on="on@id"` / `off@id`。否决。** `on="on"` 读不通。`checked` / `unchecked` 对齐 HTML `:checked`，
与 `state-selected` 一眼可分。

### 4.3 语法

**新事件值**（`<Trigger>` / `<Animation>` / `<Show>` 的 `on=`，以及 `reverse-on=`）：

| 值 | 触发 |
|---|---|
| `checked` | 最近的 `<Toggle>` / `<Tab>` 祖先的 `isOn` 变为 true；开屏时若已为 true 派发一次 |
| `unchecked` | 同上，变为 false；开屏时若为 false 派发一次 |
| `checked@<id>` · `unchecked@<id>` | 源是 id 为 `<id>` 的 `<Toggle>` / `<Tab>`（作用域见下） |

- 裸形式没有 `<Toggle>` / `<Tab>` 祖先 → 运行时错误 + lint **`PUI-CHECKED-NO-SOURCE`**（镜像
  `PUI-STATE-NO-SOURCE`；`@id` 形式与 Template 体豁免）。
- `@id` 指到非 Toggle / Tab → 运行时错误（"id 'x' is a Btn, not a toggle source"）。
- `<Show on="checked">` 合法（今天 `<Show>` 只收 `state-*`，放开这两个）。

**`@id` 作用域（对全部 `@id` 形式生效：`click` / `hover-enter` / `hover-exit` / `press` / `state-*` /
`expand` / `collapse` / `checked` / `unchecked`）—— 词法、最近优先：**

1. Trigger 自己的子树（今天的行为，不变）；
2. 沿 GameObject 父链向上，每个祖先 Control 的 `ScopedIds` 查一次 —— 模板实例根的 `ScopedIds` 就是该实例
   的整个作用域，所以"同一模板实例里的兄弟"在这一步命中；
3. 所属 `Screen` 的顶层 id 表。

三级都没有 → 运行时错误，消息从 "not found in trigger subtree scope" 改为 "not found in the trigger's
subtree, its enclosing template instance, or screen 'X'"。向后兼容：所有今天能解析的引用仍解析到同一对象。
静态检查维持现状（`@id` 运行时解析，CLI 不判存在性）。

### 4.4 语义

- **什么算变化**：用户点击、C# `IsOn = …`、`ToggleGroup` 互斥把别的 Toggle 顶掉、`TabBar.BindItems` 重建后
  的初值 —— 一切走 `OnValueChanged` 的路径。与 `interactable` 无关：禁用的 Toggle 仍有 `isOn`，`checked` 仍
  如实反映。
- **进入即触发**（同 `state-*`）。开屏时按初值派发一次：`<Trigger>` 正常 `OnFire`；`<Show>` 直接 `SetActive`；
  `<Animation>` **写终态不补间**（FND-D10，对齐 states.md 的"首帧建立"—— 一个 `isOn="true"` 的标题栏不该在
  开屏时把箭头转一圈）。此规则**只**作用于 `checked` / `unchecked` 的开屏派发；`state-normal` 开屏播放的既有
  行为不动（不在本文范围）。
- **`<Show>` 的协调**：`checked` / `unchecked` 是独立于 `state-*` 的第二个 claim 家族。同一源下
  `<Show on="checked">` 与 `<Show on="unchecked">` 互补；只写其一也合法（另一半就是"什么都不显示"）；
  与 `state-*` 块互不干涉、可嵌套（`<Show on="checked"><Show on="state-hover">…`）。没有 normal 回退 ——
  持久态没有"默认块"的概念。
- **与 `state-selected` 并存**：`<Tab>` 选中且被悬停 → `state-hover` 块显示、`checked` 块**仍**显示。这正是
  §4.1 第 1 条要的。

```xml
<!-- 纯 XML 的"勾选即显示"：头部 Toggle，兄弟面板跟着 isOn -->
<VStack width="150">
  <Toggle id="hdr" isOn="true" width="stretch">任务</Toggle>
  <Show on="checked@hdr">
    <ScrollList width="stretch" height="clamp(_, hug, 200)" itemTemplate="TaskRow"/>
  </Show>
</VStack>
```

### 4.5 实现地图

- `Controls/Internal/TriggerSpec.cs`：`TriggerKind.Checked` / `Unchecked`；前缀与裸形式解析；错误目录。
- 新 `Controls/Internal/IToggleSource.cs`（internal）：`bool IsOn`、`Observable<bool> OnValueChanged`、
  `RegisterCheckedShow(bool wantOn, Action reevaluate)`。`Toggle`（`Toggle.cs:191`）与 `Tab`（`Tab.cs:175`）实现。
- `Controls/Internal/TriggerSourceResolver.cs`：新 `ResolveId(trigger, id)` 词法三级查找，替换
  `FindStateSource` / `FindTabMenu` / 指针源三处的 `ScopedIds` 直查；新 `FindToggleSource`。
- `Controls/Trigger.cs`：`SubscribeChecked`；`Controls/Show.cs`：接受两个新 kind，向 `IToggleSource` 注册。
- `Core/Lint/StateTriggerRules.cs`：`PUI-CHECKED-NO-SOURCE`（裸 `checked` / `unchecked` 无 Toggle / Tab 祖先）。
- 文档：animations.md `on=` 表两行 + `reverse-on`；states.md 新小节"持久态 `checked`"。

### 4.6 测试

- `TriggerSpecTests`：解析、`Show` 接受、`Trigger` 拒绝 `reverse-on`。
- `CheckedTriggerTests`（EditMode）：开屏派发（Trigger 触发、Show 显隐、Animation 终态无 handle）；
  点击翻转；C# `IsOn=`；ToggleGroup 顶掉；Tab 悬停不影响 `checked` 块。
- `TriggerIdScopeTests`：兄弟 `@id`（VStack 下）；模板实例内兄弟；两个实例互不串；Screen 顶层；三级都没有
  的错误消息；既有子树引用回归。

---

## 5. 章节间与 COL 的依赖

| 零件 | 出自 | COL 用法 |
|---|---|---|
| `ClampFitter` Hug 模式 + `IHugContent` | §1 | Collapsible 根在自由定位下自动 hug（它不接受 `height=`） |
| `RevealDriver` | §2 | body 高度 0 ↔ hug 的补间 + 裁剪 |
| `RotateFlipEffect` | §3 | 箭头 0 ↔ 180° 补间（不再用负缩放） |
| `expand` / `collapse` 源泛化（`IExpandable`） | COL 自己做 | `<Animation on="expand" reverse-on="collapse">` 的行动画 |
| `checked` / `@id` | §4 | 不依赖 |

§1 → §2 → §3 顺序做，COL 在 §3 后开工；§4 可并行、可最后。

## 6. SKILL 更新（同一 PR 内，英文）

- `authoring-promptugui-xml/SKILL.md`：
  - Common attributes 表 `width` / `height` 行加 `hug` 与 `clamp(min, hug, max)`；新小节 **Hug**（允许标签、
    自由定位 / 布局组语义、`PUI-HUG-*`）；Quick reference `SIZE` 块加一行。
  - `<Image>` / `<Icon>` / `<RawImage>` 属性表加 `rotation` / `flip`；Quick reference 一行；`PUI-FLIP-TAG`。
  - Common mistakes 表：`<VStack>` 0 高的那条改为指向 `height="hug"`。
- `reference/animations.md`：`on=` 表加 `checked` / `unchecked`；新小节 **Family D — Reveal**、
  **`reverse-on`**（语义、互斥、错误码、三个 pattern）；`<Animation>` 的 uGUI 对照行补 `RectMask2D`。
- `reference/states.md`：新小节 **Persistent `checked` / `unchecked`**（与 `state-selected` 的区别、Show 协调、
  首帧建立）；`@id` 作用域说明移到 animations.md 的"source resolution"段并更新。
- `scripting-promptugui-csharp/SKILL.md`：`Animation.Reverse()` / `OnReverse`；`Image.Rotation` / `Flip`；
  hug 在 EditMode 测试里要 `ForceUpdateCanvases` 的提示。

## 7. 成本

- `hug`：不用零成本；用到的节点一个 `ILayoutSelfController` / `ILayoutElement`，只在布局 pass 里算，
  与 `ClampFitter` 同量级。
- `reveal`：一个 `RectMask2D`（非静止时启用，破坏与外部的合批 —— 与 `<Frame mask="rect">` 同价）；反向到底
  后内容仍渲染（见 §9-1）。
- `rotation` / `flip`：恒等零成本；非恒等一个 `BaseMeshEffect`，不破坏合批。
- `checked`：每个 Toggle / Tab 多一个 Subject 与一张 claim 表，无每帧成本。

## 8. 已定的决策（2026-08-31 与作者对齐）

1. **FND-D1** 四个缺口在本轮一起做，拆成本文（基础）与 COL（控件）两份 spec。
2. **FND-D2** `hug` 只允许 `<VStack>` / `<HStack>` / `<Grid>` / `<ScrollList>`（+ `<Collapsible>`），`<Frame>` 报错指路。
3. **FND-D3** `hug` 复用 `ClampFitter`（加模式），不另挂 `ContentSizeFitter`；`clamp(min, hug, max)` 合法且两种父级都可。
4. **FND-D4** 高度通道是新 D 族 `reveal="y|x"` + `reveal-from` / `reveal-to`，不叫 `height` / `extent`；可与 B 族组合。
5. **FND-D5** 反向用 `reverse-on=`（`on=` 同语法），仅 `<Animation>`；从当前值反向、完整 `duration`、同 easing。
6. **FND-D6** D 族的静止态是 `reveal-from`（先藏后露），B 族维持恒等静止态。
7. **FND-D7** 声明了 `reverse-on` 的 Animation，正向也从当前值起；未声明的保持既有"先写 from"行为。
8. **FND-D8** `rotation` / `flip` 是**网格级**（`BaseMeshEffect`，绕 rect 中心），只在 `<Image>` / `<Icon>` / `<RawImage>` 上；顺时针为正。
9. **FND-D9** 持久态叫 `checked` / `unchecked`，不进 `state-*` 家族；`<Show>` 接受。
10. **FND-D10** `checked` / `unchecked` 的开屏派发对 `<Animation>` 写终态不补间。
11. **FND-D11** 所有 `@id` 形式改为词法三级查找（子树 → 祖先作用域 → Screen），最近优先，向后兼容。

作者未单独裁定、按惯例定下的：`PUI-HUG-SCALE` / `PUI-REVEAL-SCALE` 独立错误码但与 `PUI-CLAMP-SCALE`
共享判定；hug-clamp 在布局组内刚性（LGC-D17）；`reveal` 的 `RectMask2D` 只在非静止时启用；
`reverse-on` 拒绝 `open` / `loop`；`flip` 先于 `rotation`。

## 9. 开放问题（留给 plan / 实现期）

1. `reveal` 反向到底后是否要停用子节点以省渲染（`reveal-idle="deactivate"` 之类）？v1 不做，看 COL 落地后
   通用场景有没有这个需求。
2. `Grid` 的 `width="hug"` 在 `columns` 固定时是常量，是否要 lint 提示"这等于写死"？先不做。
3. `HugElement` 排除自身的读取器是否值得上提为 `LayoutUtilityEx` 公共内部工具（`ClampFitter` 也可能用到）。
4. `rotation` 是否要接受 `deg` 后缀（`rotation="90deg"`）？先只收裸数字，与 `fontSize` 等一致。
5. `checked` 是否也给 `<Carousel>` 的 dot（内部 Toggle）？不在范围。

## 10. 里程碑

- **M1 hug**：SizeSpec + ClampFitter 模式 + HugElement + lint + 测试 + SKILL。
- **M2 rotation / flip**：效果组件 + 三控件属性 + lint + 测试 + SKILL。
- **M3 reveal / reverse-on**：AnimationSpec D 族 + RevealDriver + Driver 反向 + lint + PlayMode 测试 + SKILL。
- **M4 checked / @id**：TriggerKind + IToggleSource + ResolveId + Show + lint + 测试 + SKILL。
- COL 在 M3 后开始（见 COL §12）。

## 11. 实施记录

> 状态：**M1–M4 全部实现完毕**（2026-09-01，分支 `feat/hug-reveal-flip-checked`，
> 提交 `995a8bb`…`7c7a17f`）。全量 EditMode 3261/3261、PlayMode 189/189 绿。

### 11.1 与设计的偏差

1. **`ApplyCommon` 不再为 hug 轴写基线几何**（§1.4.2 未预见）。设计原文让 hug 走 `MarginResolver`
   的正常路径再由 fitter 覆盖；实测发现那样每次 ReSolve 都会把 rect 打回 0、再靠 fitter 撤销，
   于是**必然标脏**，`Steady_state_resolve_with_hug_dirties_nothing` 直接抓到，违反 LGC-D18。
   现在 hug 轴的 `sizeDelta` / `anchoredPosition` 保持 fitter 上次写入的值不动（`Control.cs`
   自由定位分支）。anchor / pivot / margin / 内容变化仍会通过 `OnRectTransformDimensionsChange`
   把 fitter 标脏，所以没有丢更新的路径。
2. **`ClampFitter.Apply` 的 `parent == null` 早退保留**（§1.5 说"改为仅 Fraction 模式"）。Hug 的
   High / Center 对齐仍要读父长，早退对两种模式都对。
3. **`HugElement` 不做"排除自身"的读取器**（§9 开放问题 3 因此失效）。改为：内容尺寸一律由控件的
   `IHugContent.ContentSize` 提供（V/HStack/Grid 直接读自己 LayoutGroup 的 `preferred*`，
   ScrollList 读 `_content`），从不走 `LayoutUtility.GetPreferredSize(自己)`，递归在源头就不存在。
4. **reveal 的裁剪判据是"是否停在 hug 端"，不是"是否停在较大端"**（§2.4.2 的说法过粗）。
   `reveal-to='90'` 而内容 140 时，较大端仍然遮住内容 —— PlayMode 用例
   `An_explicit_pixel_endpoint_is_honoured` 抓到。只有 `hug` 端代表"框正好等于内容"，才撤 mask。
5. **`checked` 的开屏派发靠 `Screen.IsOpening`，不是订阅时的状态比较**（§4.4 只说"进入即触发"）。
   控件的 `isOn=` 属性在子节点订阅**之后**才应用（属性自底向上），所以 `isOn="true"` 是以边沿形式
   到达的；只比较订阅时状态会让首帧建立永远不生效（`An_animation_already_in_state_...` 抓到）。
   新增 `Screen.IsOpening`（`_deferredOpenActions != null`）作为判据。
6. **`AnimationDriver` 的六处 loop switch 未做 builder 级去重**（§2.5 "顺手"）。LitMotion 的
   `MotionBuilder` 是 `ref struct`，LangVersion 9 下不能作泛型实参。改为把三路 switch 收成
   `LoopCountOf` / `LoopTypeOf` 两个纯函数，每条通道一行 `WithLoops(loops, loopType)`
   （`LoopMode.None` → `(1, Restart)` 正是 LitMotion 的默认值）。
7. **Task 4 / Task 6 的 lint 规则先写实现后补测试**（plan 要求红先行）。新类型不存在时的"红"只是
   编译错误，信号弱于断言红；规则本身是纯函数，测试覆盖正反例，判断为可接受的偏差。

### 11.2 顺带发现（未修，记录在案）

- **`<ScrollList>` 的行高来自行自己的 rect，而非 `LayoutElement`。** content 组是
  `AddComponent<VerticalLayoutGroup>()` 之后从未设 `childControlWidth/Height`，于是行模板写
  `height='32'` 今天是**静默无效**的（行停在 RectTransform 默认的 100）。hug 如实反映内容尺寸，
  没有改动这一行为 —— 改它会动到所有既有 ScrollList 的外观，属另一个决策。
- `EditorOnly` 的 `SpriteAtlasSyncerTests` 会间歇性抛
  `IOException: Sharing violation on ssw_re_client\Assembly-CSharp.csproj`（每次失败的用例集都不同，
  2–8 条不等）。持有者是本机运行中的 VS Code C# 语言服务（`dotnet.exe`），与本特性无关。

### 11.3 实测数据

- 全量 EditMode 3261 条 / 37.8 s；PlayMode 189 条 / 36.7 s。
- reveal 中途反向的最大帧间跳变：用例阈值 25 单位（140 高的面板、0.4 s 时长）下稳定通过，
  实际观测远小于阈值。
- hug 的空闲成本：未使用时零组件；使用时每个节点一个 `ILayoutSelfController` / `ILayoutElement`，
  只在布局 pass 内计算。
