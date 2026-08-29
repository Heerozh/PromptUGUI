# 尺寸钳制 `clamp(min, N%, max)` —— 随父级连续拉伸、但有上下限的宽高（CLP）

> 状态：**已实现**（M0–M3 一轮做完，见 §13）。决策见 §10，2026-08-30 与作者对齐。
> 相关：master spec `2026-05-07-promptugui-description-language-design.md` §6.2（size / width / height）、
> §6.5（布局组内的特殊行为）；`1aaf244`（`N%` 分数尺寸与 `stretch*N` 权重的出处，本文是它的直接续篇：
> clamp 就是"带上下限的 `%` / `stretch`"）；`2026-08-27-decor-primitives-design.md`（FLW `flow="false"`
> 出流子节点 —— `%` 在布局组内唯一合法的位置，clamp 同样适用）；
> `Tests/EditMode/Controls/SafeAreaTests.cs:189`（`OnRectTransformDimensionsChange` 里写 RT 的写回环，
> 本文 §6.4 的组件设计就是为了绕开它）。

## 1. 问题

聊天面板，竖屏 `reference="360x600"` 下 167 宽约占半屏：

```xml
<Frame class="panel" mask="self" anchor="bottom-left" width="167" height="400"/>
```

作者要的是：**最少 167，屏幕更宽就跟着拉，到 250 封顶**——不能 `anchor="stretch"`（占满不好看），
也不能固定值（大屏浪费）。这是 CSS `width: clamp(167px, 46.4%, 250px)` 的语义。

宽度这一轴今天勉强能绕：`reference` + Expand 缩放保证画布宽 ≥ 360，`width="46.4%"` 的下限自动成立；
上限 250 对应画布宽 539 ≈ 宽高比 0.9，`width.landscape="250"` 用内置 variant 就能盖住 99% 的机型。
**高度这一轴绕不过去**：横屏 `reference.landscape="640x360"` + Expand 下，16:9 画布高 360、4:3 是 480、
接近方形时 640；`height="200" height.portrait="400"` 让面板从 55% 掉到 31%，再在切竖屏的一瞬间跳到 400 ——
Variant 是阶跃，作者要的是连续函数加上下限。

**今天的语言表达不了这件事，不是哪个属性的边缘用法：**

- `width="46%"`：只有比例，没有上限。
- `width.landscape="250"`：阶跃，且横竖屏之外的中间宽高比没有落点。
- 外面套 `<HStack>`、里面写 `width="250"` 指望它"空间不够就缩"：`Control.cs:427`（LGC-D17）把显式数值
  钉成 `LayoutElement.min = preferred`，布局组永远不会压缩它 —— 这是"strictly NxN"契约，不能靠它偷 clamp。
- 锚点本身是仿射的：`width = 父宽 × (aMax − aMin) + sizeDelta`，表达不了钳制。uGUI 对自由定位的
  RectTransform 没有任何 min/max 机制；只有布局组子节点的 `LayoutElement.min / preferred` 有
  "min→preferred 插值"，那正是 clamp（§5.2 用它）。

**业内先例**：CSS `clamp()` / `min()` / `max()`；uGUI 自己的 `AspectRatioFitter` /
`ContentSizeFitter` 是"自控节点在布局 pass 里改自己 rect"的官方模式（`ILayoutSelfController`），§6.4 照抄它。

## 2. 否决的方案

**`minWidth` / `maxWidth` / `minHeight` / `maxHeight` 四个独立属性。否决（作者选择，§10 决策 1）。**
公共属性名在仓库里硬编码了 6 处（`Core/IR/CommonAttributes.cs`、`Lint/VariantBaseRules.cs:65`、
`Lint/DecorRules.cs:54`、`Lint/ImageFitRules.cs:19`、`Editor/XsdGenerator.cs:244`、
`TemplateExpander.CommonAttrs`），每加一个名字六处都动；只改 `width` / `height` 的**值语法**一处都不用动
（XSD 里它们是无 pattern 的 `xs:string`）。独立属性还会留下 `maxWidth` 对数值 `width` 失效的 inert 组合、
又要一条 lint；而 `width.landscape="clamp(167, 46%, 300)"` 一次覆盖整个轴，Variant 语义原子。

**Screen 后置 pass + `_hasClamp` 门控（仿 `_hasFactorScale`）。否决（§10 决策 2）。** 门控本身 20 行，
但 clamp 没法在 `ApplyCommon` 里算，得是"所有 ApplyCommon 之后、`ApplyScales` 之前"的 top-down pass，
而这条路有三个洞：(1) `Open` 的 apply 是 DFS 后序（`Screen.cs:190`，子先父后，因为 `GetNativeSize` 要量
TMP），`ReSolve` 遍历的 `_nodeMap` 是 Dictionary、无序 —— 得另存父先子后的顺序，动态子树还要按层深排序；
(2) 父被布局组驱动（`<VStack><Frame width="stretch"><X width="clamp(…)">`）时 pass 读到的 rect 是陈旧的，
要先 `Canvas.ForceUpdateCanvases()`；(3) 不经过 ReSolve 的父级变化收不到：`TabMenu.cs:835` 弹板动态
`ForceRebuildLayoutImmediate` 定尺寸、Animation 改父尺寸。另外 resize 会触发整树属性重放（拖窗口时连发）。

**在 `OnRectTransformDimensionsChange` 里直接写 RT。否决。** `SafeAreaTests.cs:189` 守门测试禁止的
正是这个：RectTransform setter 内部反向求解的中间态会反过来触发回调，跟 `ApplyCommon` 形成写入回环。
§6.4 的组件在回调里**只标脏**，写发生在 `LayoutRebuilder` 的 pass 里 —— 回调不写，就没有回环。

**Update 轮询（`SafeAreaTracker` 模式）。未选。** 能用，但每个 clamp 节点每帧一次比较、且父变化后晚一帧
生效；`ILayoutSelfController` 在同一次 canvas update 里完成、空闲零开销。

**CSS `min()` / `max()` 函数形态。否决。** 一条语法：开放一端写 `_`（`clamp(_, 46%, 250)`），与 `margin`
的 `_` 占位同一个心智模型。

**视口单位 `vw` / `vh`。不进。** 那是"相对画布"而非"相对父级"，是另一个原语；本文的 `%` 相对父级，
Screen 直下子节点的父级就是画布，已覆盖作者的场景。

## 3. 方案总览

给 `width` / `height` 的值语法加一个函数形态 `clamp(min, middle, max)`，middle 是今天已有的两个可变尺寸
关键字之一 —— clamp 就是"带上下限的 `%`"或"带上下限的 `stretch`"：

```xml
<!-- 本文的动机：自由定位，随父级 46.4% 拉伸，钳在 [167, 250] -->
<Frame class="panel" mask="self" anchor="bottom-left"
       width="clamp(167, 46.4%, 250)" height="clamp(200, 55%, 400)"/>

<Frame anchor="bottom-left" width="clamp(_, 46%, 250)"/>        <!-- 只封顶 -->
<Frame anchor="top-center"  width="clamp(320, 60%, _)"/>        <!-- 只保底 -->

<!-- 布局组内：LayoutElement(min=167, preferred=250, flexible=0)，uGUI 自带的插值就是 clamp -->
<HStack anchor="bottom-stretch" height="400">
  <Frame class="panel" width="clamp(167, stretch, 250)"/>
  <Frame width="stretch"/>                                      <!-- spacer 吃掉剩余 -->
</HStack>

<!-- Variant 一次换整个轴 -->
<Frame width="clamp(167, 46.4%, 250)" width.landscape="250"/>
```

- 零新属性名；`size=` 仍数值-only（同 `%` / `stretch` 的规则）。
- 自由定位：`ApplyCommon` 照 `%` 写分数锚区基线，节点上挂一个内部组件 `ClampFitter`
  （`ILayoutSelfController`）在布局 pass 里按父 rect 算最终几何。Screen / ReSolve / resize 路径**一行不改**，
  父子顺序、布局组驱动的父、resize、Variant、弹板、动画、BindItems 行全由 Unity 的布局重建驱动。
- 布局组：纯 `LayoutElement` 三元组映射，没有组件。
- 未触及钳制区间时，几何与今天的 `%` / `stretch` **逐位相等**（§5.1 / §5.2 各有测试钉住）。

## 4. 语法

```
width | height := number | "native" | stretch | percent | clamp
stretch        := "stretch" [ "*" number ]
percent        := number "%"
clamp          := "clamp(" bound "," middle "," bound ")"
bound          := number | "_"                     // "_" = 该端开放
middle         := percent | stretch
```

- 逗号两侧允许空白；`clamp` 小写；括号必须闭合。
- `_` 表示该端不钳。两端都 `_` 是 error（那就是 middle 本身，直接写 `46%`）。
- `min` / `max`：有限、非负；`min ≤ max`。
- `middle` 只能是 `N%`（自由定位）或 `stretch`（布局组）。裸数字是 error —— 常量的 clamp 还是常量。
- `stretch*N`（权重）只在 **上限开放** 时合法：`clamp(167, stretch*2, _)`。有限上限下 flexible 必须为 0
  （flexible > 0 会越过 preferred，见 §5.2），`clamp(167, stretch*2, 250)` 是 error。
- `N%` 范围 `(0%, 100%]`，沿用。
- `size="clamp(…)"`：`LooksLikeKeyword` 加 `clamp`，报现有的"size 是数值-only，用 per-axis 属性"那条错。

### 4.1 解析错误（parse-time，`SizeSpec.Parse`，`UI.Open` 时带节点上下文抛 `ParseException`）

| 情形 | 结果 |
|---|---|
| `clamp(_, 46%, _)` | error：both bounds open — write `46%` |
| `clamp(300, 46%, 250)` | error：min 300 > max 250 |
| `clamp(-1, 46%, 250)` / `clamp(167, 46%, NaN)` | error：bounds must be finite and ≥ 0 |
| `clamp(167, 200, 250)` | error：middle must be `N%` or `stretch` — a constant needs no clamp |
| `clamp(167, stretch*2, 250)` | error：a weighted stretch cannot be capped — drop the weight or open the max (`_`) |
| `clamp(167, 46%)` / `clamp(167, 46%, 250, 1)` | error：clamp takes exactly 3 parts `clamp(min, middle, max)` |
| `clamp 167 46% 250` / `Clamp(…)` / `clamp(…` | error：unknown width value — the only function form is `clamp(min, middle, max)` |
| `size="clamp(…)"` | error（沿用）：size is numeric-only, use `width=` / `height=` |

以下不是新错误、只是消息里补一句 clamp 的指引：

| 情形 | 结果 |
|---|---|
| `clamp(167, 46%, 250)` 在 V/HStack/Grid 直下（在流内） | error（沿用 `%` 禁令，`Control.cs:281`）：… use `clamp(min, stretch, max)` instead |
| `clamp(167, stretch, 250)` 在自由定位父级下（或 `flow="false"`） | error（沿用 `stretch` 禁令，`Control.cs:313`）：… use `clamp(min, N%, max)` instead |
| `anchor="bottom-stretch" width="clamp(…)"` | error（沿用 `ValidateAgainst`）：拉伸轴禁止 width |

## 5. 语义

### 5.1 自由定位（middle = `N%`）

父级是 Frame / Screen / SafeArea，或 `flow="false"` 出流时的布局组本身。记该轴上：

```
P      = 父 rect 长度
f      = 分数（46.4% → 0.464）
lo, hi = 该轴两侧 margin（水平 = 左、右；竖直 = 下、上）
A      = anchor 在该轴的端点：left / center / right（竖直：bottom / center / top）

box    = clamp(f·P, min, max)          // 开放端不钳；未钳时 box = f·P
W      = box − (lo + hi)               // margin 在 box 内再 inset —— 同今天 % 的规则
low    = A=left/bottom : lo
         A=right/top   : P − box + lo   // 即 high = P − hi
         A=center      : (P − box)/2 + lo
high   = low + W
```

**RectTransform 表示**：`ApplyCommon` 写的分数锚区不变（`[0,f]` / `[1−f,1]` / `[(1−f)/2,(1+f)/2]`，
`Control.cs:516` `ComputeFractionalAnchor`），组件写 `offsetMin = low − a0·P`、`offsetMax = high − a1·P`。
用 offset 而不是 sizeDelta / anchoredPosition：offset 与 pivot 无关，作者显式 `pivot=` 时公式不变。

- **未钳时逐位等于今天的 `%`**：`box = f·P` ⇒ `offsetMin = lo`、`offsetMax = −hi`，正是
  `MarginResolver` stretch 分支（`sizeDelta = −(lo+hi)`、`anchoredPosition = (lo−hi)/2`、pivot 0.5）的等价表示。
- **贴边由 anchor 决定**：`bottom-left` 封顶后 box 仍贴父级左边（不是在 46% 的锚区里居中 —— 那是"锚区 +
  pivot 0.5"的天然结果，正是要修正的东西）。
- 两轴独立，可只钳一轴。
- `W` 可以为负（margin 大于 box），与 `%` 今天一样不特判。
- `P = 0`（EditMode / 首帧画布未定尺寸）⇒ `box = min`；下一次 rect 变化自动修正。

### 5.2 布局组（middle = `stretch`）

V/HStack 直下、在流内的子节点。映射到 `LayoutElement` 三元组：

| 形态 | `min` | `preferred` | `flexible` |
|---|---|---|---|
| `clamp(min, stretch, max)` | min | max | 0 |
| `clamp(_, stretch, max)` | −1 | max | 0 |
| `clamp(min, stretch, _)` | min | 0 | 1 |
| `clamp(min, stretch*N, _)` | min | 0 | N |
| （对照）`stretch` | −1 | 0 | 1 |
| （对照）数值 `W`，LGC-D17 | W | W | 0 |

uGUI `HorizontalOrVerticalLayoutGroup` 主轴：`size = lerp(min, preferred, t) + flexible × 剩余`，
`t = clamp01((可用 − Σmin) / (Σpreferred − Σmin))` **全组共享**。于是：

- 独子、或配一个 `width="stretch"` spacer 兄弟：`size = clamp(可用, min, max)`，精确。
- 多个可收缩兄弟：按各自 `(preferred − min)` 比例同步收缩（CSS `flex-shrink` 的类比）。SKILL 要写明。
- 上限开放保留 flexible：先长到 `preferred=0` 之上……即按权重分剩余，`min` 兜底 —— 这就是"带下限的
  `stretch`"，也是 LGC-D17 "显式尺寸永不收缩"之外**唯一**受支持的可收缩形态。
- 交叉轴（HStack 里的 `height="clamp(…)"`）：`childControlHeight=true` 时 uGUI 取
  `clamp(组内高, min, preferred)`（flexible=0），同样精确。
- Grid：`LayoutElement` 被 `cellSize` 无视，`clamp(…, stretch, …)` 在 Grid 下走今天 `stretch` 的报错。
- `flow="false"`：出流后没有 flex 权重，`stretch` 中段非法（沿用），改用 `N%` 中段。

### 5.3 与 anchor 的规则

- 拉伸轴禁止 `width` / `height`：clamp 使 `HasWidth = true`，`ValidateAgainst` 原样拒绝。
- `<Frame>` 省略 anchor 时的默认（`Frame.cs:28`）：`HasWidth` ⇒ 该轴 left/top，不是 stretch —— 与 `%` 一致。

### 5.4 Variant / ReSolve 幂等

- `ApplyCommon` 每次都完整重写：clamp 轴 ⇒ 写基线 + 把 spec 推进组件 + 启用该轴 + 标脏；
  非 clamp 轴 ⇒ 禁用组件该轴（组件保留，同"残留 LayoutElement 清回 −1"的做法，`Control.cs:420`）。
  两轴都不 clamp ⇒ 组件 `enabled = false`。
- 组件是 spec + 父 rect 的**纯函数**，两次 pass 结果相同；`LayoutRebuildDirtyTests` 的 Drain 模式钉住
  "ReSolve 两次、drain 后队列为空"。
- `width="clamp(…)" width.landscape="250"`：切到 landscape 走数值路径、组件该轴禁用；切回来重新启用。

### 5.5 交互清单

| 与 | 规则 |
|---|---|
| `scale`（三种形态） | **v1 禁止**同节点（§10 决策 4）：`PUI-CLAMP-SCALE`，CLI error + 运行时 `ParseException`。原因：`Screen.ApplyBoxPreservingCompensation`（`Screen.cs:523`）在 ApplyCommon 后同步膨胀 RT，组件在布局 pass 里按 spec 重写该轴会覆掉膨胀。要支持时把按轴膨胀抽成共享 helper 让组件自己做。 |
| `pivot=` | 组件写 offset，与 pivot 无关；显式 pivot 照常用于旋转 / 缩放中心。 |
| `<Animation>` / 按压偏移 | 它们写的是 `_offsetProxy` / content-holder 的 `anchoredPosition`（`AnimationDriver.cs:48`、`PressOffsetController.cs:48`），不是节点自身 RT —— 无冲突。 |
| `hidden` / `<Add>` 块（Strategy C `SetActive`） | 重新激活 ⇒ `OnEnable` ⇒ 标脏 ⇒ 下一次 canvas update 重算。 |
| BindItems 动态子树 | 组件随行实例化；resize 路径跳过行的属性重放（`Screen.cs:832` 文档化的成本决定）对 clamp 无影响 —— 行的父 rect 变了组件自己会重算。 |
| `<SafeArea>` 父级 | `SafeAreaTracker` 每帧改 SafeArea 的 offset ⇒ 子节点 rect 变 ⇒ 组件跟随。 |
| `scale-mode="pixel"` / `reference` / Expand | clamp 单位是画布单位，与缩放模式正交。 |
| `<Template>` `{{param}}` / `<Style class>` | 字符串级替换 / 属性包合并，在解析之前完成，透明。 |
| `PUI-VARIANT-NO-BASE` | `width` / `height` 本来就在自愈集合里，不变。 |
| `PUI-MARGIN-INERT-SIDE`（`MarginAnchorRules`） | 该规则只看 anchor、不认 `%`：`anchor="bottom-left" width="46%" margin="0,16,0,16"` 今天就会把右 16 误报为 inert（实际上分数轴两侧 margin 都生效）。clamp 继承同一个既有误报 —— §11 建议顺手修。 |

## 6. 实现地图

### 6.1 数据流

```
width="clamp(167, 46.4%, 250)"
   │ SizeSpec.Parse                      Core/Layout/SizeSpec.cs
   ▼
SizeSpec { IsFractionalWidth, WidthFraction=0.464, IsClampedWidth, MinWidth=167, MaxWidth=250 }
   │ Control.ApplyCommon                 Runtime/Controls/Control.cs
   ├─ 自由定位分支：照 % 写分数锚区 / pivot / margin 基线（不变）
   │     └─ ClampFitter.SetAxis(x, spec) + LayoutRebuilder.MarkLayoutForRebuild
   └─ 布局组分支 ApplyLayoutElement：三元组按 §5.2 表写入（无组件）
   │
   ▼ 下一次 CanvasUpdateRegistry 布局 pass（或测试里 ForceUpdateCanvases / ForceRebuildLayoutImmediate）
ClampFitter.SetLayoutHorizontal / Vertical：读 parent.rect → §5.1 公式 → 写 offsetMin / offsetMax
   │
   ▼ 之后任何 rect 变化（父 resize / Variant / 弹板 / 动画 / SafeArea）
OnRectTransformDimensionsChange → 只标脏 → 再来一次 pass
```

### 6.2 `SizeSpec`（`Core/Layout`，不在 CLI 编译集内，可用 `Mathf` / `float.PositiveInfinity`）

- 新字段：`IsClampedWidth / IsClampedHeight`（bool），`MinWidth / MaxWidth / MinHeight / MaxHeight`
  （float；开放端存 `float.NegativeInfinity` / `float.PositiveInfinity`，`Mathf.Clamp` 对它们是恒等）。
- `ParseAxis` 识别 `clamp(`：拆三段 → 解析两端（`_` / 数字）→ 中段递归走既有的 `%` / `stretch` 分支
  （复用 `IsFractional` / `IsFlexible` / `Weight` 的填充）→ 校验 §4 的规则。
- `LooksLikeKeyword` 加 `clamp`。
- `WithNativeResolved` / `FromNumeric` / `WithFallbackForMissing` 透传新字段。

### 6.3 `Control.ApplyCommon`

- 自由定位分支：`IsFractional*` 为真时的现有代码**一行不改**（锚区、pivot 0.5、`effectivePreset` 走
  MarginResolver 的 stretch 分支）。末尾新增：
  ```
  if (IsClampedWidth || IsClampedHeight) {
      fitter ??= AddComponent<ClampFitter>();
      fitter.SetAxis(0, IsClampedWidth,  WidthFraction,  MinWidth,  MaxWidth,  l, r, preset.H);
      fitter.SetAxis(1, IsClampedHeight, HeightFraction, MinHeight, MaxHeight, b, t, preset.V);
      fitter.enabled = true;  LayoutRebuilder.MarkLayoutForRebuild(RectTransform);
  } else if (fitter != null) fitter.enabled = false;
  ```
  margin 的四个分量要从 `MarginResolver` 暴露出来（今天 `ParseMargin` 是 private；加一个
  `internal static void Parse(string, out t, out r, out b, out l)`）。
  组件挂在 `RectTransform` 所在 GO（自由定位下 `LayoutHost ≡ RectTransform`；wrapper 只在
  "V/HStack 直下 + scale"时存在，与 clamp 互斥）。
- 布局组分支 `ApplyLayoutElement`：`HasWidth && IsFlexibleWidth && IsClampedWidth` ⇒ 按 §5.2 表填
  `prefW / flexW / minW`；LGC-D17 的注释补一句"clamp 是受支持的可收缩形态"。
- `%` 在组内 / `stretch` 在组外的两条既有报错，消息各补一句 clamp 指引（§4.1 第二表）。

### 6.4 `Controls/Internal/ClampFitter`

```csharp
[DisallowMultipleComponent]
internal sealed class ClampFitter : UIBehaviour, UnityEngine.UI.ILayoutSelfController
{
    internal void SetAxis(int axis, bool on, float fraction, float min, float max,
                          float marginLow, float marginHigh, /*Left|Center|Right*/ int align);

    public void SetLayoutHorizontal() => Apply(0);
    public void SetLayoutVertical()   => Apply(1);

    protected override void OnEnable()                         => SetDirty();
    protected override void OnTransformParentChanged()         => SetDirty();
    protected override void OnRectTransformDimensionsChange()  => DirtyOrDefer();
}
```

- `Apply(axis)`：轴未启用 ⇒ 返回；`parent as RectTransform` 为 null ⇒ 返回；按 §5.1 算 `low / high`，
  与当前 `offsetMin/Max` 逐分量比较（`Mathf.Approximately`），**变了才写** —— 避免无谓的二次标脏。
- `SetDirty()`：`IsActive()` 才 `LayoutRebuilder.MarkLayoutForRebuild(rt)`。
- `DirtyOrDefer()`：`CanvasUpdateRegistry.IsRebuildingLayout()` 时不能入队（Unity 会报 "Trying to add …
  for layout rebuild while we are already inside a layout rebuild loop"），置延迟标志、下一帧补标脏 ——
  `AspectRatioFitter` 的 `m_DelayedSetDirty` 模式。空闲时无每帧工作（实现可用 `Update` 早退，或一次性
  订阅 `Canvas.preWillRenderCanvases`，plan 期定）。
- **回调里永远不写 RT**。这条由测试钉住（§12 M1）。
- 布局根发现：`MarkLayoutForRebuild` 只沿 `ILayoutGroup` 祖先上溯，自由定位父级下根就是节点自己，一次
  pass 只跑它和它的子树；`flow="false"` 在布局组里时根是最外层布局组，但那次 resize 本来就要重建它。
- 父由布局组驱动时的时序：CanvasUpdateRegistry 按层深排序，浅的先重建 ⇒ 父先定 ⇒ 我们的回调在重建
  循环里触发 ⇒ 延迟一帧补算。与 `AspectRatioFitter` 在布局组里的官方行为一致。
- 不用 `DrivenRectTransformTracker`（§11）。

### 6.5 lint 与运行时硬错误（`Core/Lint/ClampRules.cs`，纯 C#）

- `HasClamp(node)`：`width` / `height` 的 base 或任一 variant 值 `TrimStart().StartsWith("clamp(")`。
  字符串级判定，不依赖 `SizeSpec`（它在 CLI 编译集之外）。
- `CheckClampScale(node)` → `PUI-CLAMP-SCALE`：`HasClamp` 且 `scale` 有 base 或任一 variant 值。**按声明
  判定、不按解析结果**（clamp 在 base、scale 只在某个 variant 也算）—— CLI 与运行时看到的是同一个谓词，
  行为一致。
- 消费者：CLI（`DocumentLinter`，error）；运行时 `ControlAttributeApplier.Apply` 在调 `ApplyCommon` 之前
  `foreach issue → throw new ParseException(issue.Message)`（作者选的是硬错，不是
  `ScreenInstantiator` 那条 `Debug.LogWarning` 通道）。
- 消息："`<Frame id='x'>`: width=\"clamp(…)\" and scale=\"…\" on the same node — a clamped axis is
  owned by the layout pass and would drop the box-preserving inflation. Fix: move scale to a child
  (wrap the content) or drop the clamp."
- clamp 的**语法**错误仍是运行时 `ParseException`（同 `%` / `stretch` 今天的状况：`SizeSpec` 不在 CLI
  编译集内，CLI 看不到 width 值语法）。要让 CLI 看到得把值语法解析下沉到 `Core/Parser`，§11。

### 6.6 纯 C# 边界

改动只碰 `Core/Layout`（本来就在 CLI 之外）、`Controls`、`Application`，和 `Core/Lint` 里一个只做字符串
判定的新文件。CLI 编译集不需要新增文件。

## 7. SKILL 更新（同一 PR 内，英文）

`authoring-promptugui-xml/SKILL.md`：

- Common attributes 表 `width` / `height` 行：格式列加 `clamp(min, N%, max)` / `clamp(min, stretch, max)`，
  注释指向新小节。
- "Fractional %" 之后新增 **"Clamp"** 小节：§3 的四个例子；§5.1 的对齐规则（贴边由 anchor 决定、margin 在
  box 内 inset）；§5.2 的 LE 表 + "多兄弟按比例收缩"一句；`_` 开放端；`stretch*N` 只配开放上限；
  Variant 示例 `width.landscape="250"`；"clamp + scale 同节点不支持（`PUI-CLAMP-SCALE`），把 scale 放到
  子节点"。
- "Stretch keyword" 段补一句：want a floor / cap? use `clamp(min, stretch, max)`（LGC-D17 唯一出口）。
- 错误表：`PUI-CLAMP-SCALE` 一行；`'%' … cannot be used inside` 与 `'stretch' … only valid inside` 两行的
  Fix 列各补 clamp 指引；§4.1 的 clamp 语法错误一行（合并写）。
- 反模式 / FAQ 表加一行："面板在宽屏上太宽、窄屏上太窄" → `clamp`。
- uGUI 对照表：自由定位 clamp ⇒ `ClampFitter (ILayoutSelfController)`；组内 ⇒ `LayoutElement min/preferred`。

master spec §6.2 末尾加一段 **"`clamp(min, N%, max)` / `clamp(min, stretch, max)`（尺寸钳制，CLP）"**
的摘要 + 指向本文（照 §6.5 FLW 段的做法）。

## 8. 成本

- 每个 clamp 节点一个组件；只在自身 rect 变化时做一次 O(1) 计算与最多两次 offset 写入；空闲零开销。
- Screen 的 `Open` / `ReSolve` / resize 路径不变，`_hasFactorScale` 不动。
- 布局组形态零运行时成本（本来就写 `LayoutElement`）。
- 解析：`ParseAxis` 多一个前缀分支。

## 9. 与既有契约的关系

- **"ApplyCommon 先重置、ApplyScales 再膨胀"**（STW-D4）：clamp 轴上组件是最后写者，因此 v1 禁止同节点
  scale（§5.5）；契约本身不变。
- **LGC-D17（显式数值钉 min = preferred）**：不变；clamp 是明确声明的可收缩区间，是它的补集而不是例外。
- **`%` 只在自由定位、`stretch` 只在布局组**：不变，clamp 的中段继承同一条分界。
- **Variant 不重建 GameObject**：组件常驻、按轴启停。

## 10. 已定的决策（2026-08-30 与作者对齐）

1. **语法用 `clamp(min, middle, max)` 函数形态**，不加 `minWidth` / `maxWidth` 独立属性（§2 第一条）。
2. **自由定位用 `ClampFitter` 组件（`ILayoutSelfController`）**，不做 Screen 后置 pass / `_hasClamp` 门控
   （§2 第二条）。
3. **v1 包含布局组形态 `clamp(min, stretch, max)`**（§5.2），语言规则两侧对称。
4. **clamp 与 `scale` 同节点 v1 禁止**：`PUI-CLAMP-SCALE`，CLI error + 运行时 `ParseException`（§5.5 / §6.5）。

作者未单独裁定、按惯例定下的：开放端用 `_`（同 margin）；`stretch*N` 只配开放上限；组件写 offset
（pivot 无关）；`PUI-CLAMP-SCALE` 按声明判定；`ParseException` 在 `ControlAttributeApplier` 里抛。

## 11. 开放问题（留给 plan / 实现期）

- **`MarginAnchorRules` 不认分数轴**（§5.5 末行）：`%` 今天就有的误报，clamp 继承。修法是把"值以 `%` 结尾或
  以 `clamp(` 开头"的轴视为消耗两侧 margin 槽（纯字符串判定，10 行）。**已采纳，随本 PR 修**（`MarginAnchorRulesTests` 钉住；同时纠正了 `%` 的既有误报）。
- **CLI 校验 width 值语法**：需要把 `clamp` / `%` / `stretch` 的语法解析下沉到 `Core/Parser`（纯 C#），
  `SizeSpec` 再调它。v2。
- `DrivenRectTransformTracker`：让 Inspector 显示该轴被驱动。纯体验，不进 v1。
- `[ExecuteAlways]`：`PromptUGUIDocumentHost` 是 `ExecuteAlways`，编辑态预览下组件的
  `OnRectTransformDimensionsChange` 不会跑 —— 若预览需要跟随窗口，给组件也加 `[ExecuteAlways]`。plan 期看
  DocumentHost 是否真的在编辑态开屏。
- 延迟标脏的载体（`Update` 早退 vs 一次性 `Canvas.preWillRenderCanvases`）：实现期定，测试只钉行为。

## 12. 里程碑

按 CLAUDE.md：先红测试再实现；EditMode 用 `Canvas.ForceUpdateCanvases()` /
`LayoutRebuilder.ForceRebuildLayoutImmediate` 驱动布局 pass（`LayoutRebuildDirtyTests.Drain` 的模式）；
每个里程碑同 PR 更新 SKILL。

**M0 — 解析**（`SizeSpecTests`）：§4.1 两张表逐行；`clamp(167, 46.4%, 250)` 的字段值；开放端 ±Infinity；
`size="clamp(…)"` 走 keyword 错误。

**M1 — 自由定位**（新 `ControlApplyCommonClampTests` + `ClampFitterTests`）：
- 父 Frame 宽 300 / 500 / 800，子 `anchor="bottom-left" width="clamp(167, 46.4%, 250)"` ⇒ drain 后
  rect.width = 167 / 232 / 250；左边贴 0。
- `center-right` / `center` 的贴边与居中；`margin="0,16,0,16"` 时 W = box − 32、边距各 16。
- `height` 轴同上（`clamp(200, 55%, 400)`，父高 300 / 600 / 800 ⇒ 200 / 330 / 400）。
- 未钳区间：与 `width="46.4%"` 的孪生节点 `offsetMin/Max` 逐位相等。
- 父宽改变后 `ForceRebuildLayoutImmediate` ⇒ 重算；ReSolve 两次 ⇒ drain 后 `PendingLayoutRebuilds == 0`，
  几何不变（幂等）。
- `width.wide="250"` 切入 ⇒ 组件轴禁用、几何等于数值路径；切出 ⇒ 重新启用。
- `hidden` 再显示 ⇒ 重算。
- 守门：`ClampFitter.OnRectTransformDimensionsChange` 不写 RT（在回调期间对 offset 的写入计数为 0 —— 用
  一个 `Open` 里父级反复改宽的场景断言不挂死、且最终几何正确）。
- PlayMode：直接改父 `sizeDelta`、yield 一帧 ⇒ 子已重算（回调路径，EditMode 跑不到）。

**M2 — 布局组**（`ControlApplyCommonLayoutGroupTests` 扩展）：
- §5.2 表五行的 `LayoutElement` 三元组。
- HStack 宽 150 / 220 / 300、独子 `clamp(167, stretch, 250)` ⇒ `ForceRebuildLayoutImmediate` 后
  167（溢出）/ 220 / 250；配 `stretch` spacer 时 spacer 吃剩余。
- 交叉轴：VStack 里 `width="clamp(100, stretch, 200)"`，组宽 80 / 150 / 300 ⇒ 100 / 150 / 200。
- Grid 下 / `flow="false"` 下的报错。

**M3 — lint 与文档**：`ClampRulesTests`（CLI error）+ 运行时 `ParseException`；`MarginAnchorRules` 分数轴
修正（若 §11 采纳）；SKILL 全部条目；master spec §6.2 摘要段。

## 13. 实施记录

（实现后填写。）
