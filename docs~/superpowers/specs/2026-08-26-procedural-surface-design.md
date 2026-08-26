# 程序化表面：让任何控件都能被换出形状

> 状态：**已实现**（M-lint / M-mask / M0–M4 全部合入，见 §14）。本文保留设计推理与实施记录。
> 相关：`2026-08-26-theme-driven-style-design.md`（主题驱动样式）、PR #100（玻璃填充）。

## 1. 问题

`<Style>` / `<Theme>` 这套承诺的是「整套皮肤都能换」。**但换不动形状。**

```xml
<Btn class="btn" radius="8" borderWidth="1" glass="true"/>   <!-- 三个属性全部被静默丢弃 -->
```

`<Frame>` 能自绘圆角、描边、发光、玻璃；其余每一个内置控件都画不了。写上去不报错、不警告、什么也不发生。

## 2. 为什么现在提

农场 ↔ 玻璃的示例（`Samples~/CommonControls`）撞出来的。面板是 `<Frame glass="true">`，切到玻璃主题确实变成磨砂玻璃；同一屏上的按钮、勾选框、滑块、下拉框全是**纯色块** —— 没有圆角、没有描边、没有模糊。属性包能把它们的 sprite 换成 `none`、把颜色换成半透明白，然后就到头了。

一句话：主题能换材质，换不了形状。而「像素 ↔ 玻璃」这种换皮，形状恰恰是主要差异。

## 3. 现状：能力边界在哪儿

**只有 `Frame` 碰 `ProceduralPanel`。** 全仓扫过一遍，`Runtime/Controls/` 下唯一出现 `ProceduralPanel` 的文件就是 `Frame.cs`。它的规则是懒挂：

```csharp
private ProceduralPanel Panel => _panel ??= GameObject.AddComponent<ProceduralPanel>();
// 作者写了任一程序化视觉属性时才 lazy 挂 —— 没写就一个 Graphic 都不挂，零成本。
```

**其余内置控件都是 Image 系。** 主表面的位置分两种（实测）：

| 控件 | `color=` / `sprite=` 落在哪个 Graphic | 位置 |
|---|---|---|
| `Btn` / `Tab` / `Dropdown` / `InputField` / `ScrollList` | `_bg` | **自身节点** |
| `Toggle` | `Background` | 子节点 |
| `Slider` | `Background`（轨道） | 子节点 |
| `Progress` | `MaskWrapper/Bg` | 子节点 |
| `Image` / `RawImage` | 自身 | — |

实测的完整层级（`UI.Open` 之后 dump 出来的真实树）：

```
sl (Slider)  [无 Graphic]           pr (Progress)  [无 Graphic]      tg (Toggle) [无 Graphic]
   Background [Image] ← 主表面 轨道     MaskWrapper                       Background [Image] ← 主表面
   Fill Area                              Bg   [Image] ← 主表面              Checkmark [Image]
      Fill    [Image]                     Fill [Image]                   Label      [TMP]
   Handle Slide Area                   Frame  [Image]
      Handle  [Image] ← targetGraphic
```

有两点对后面的设计很重要：

- **`Slider` / `Progress` / `Toggle` 的根节点上一个 Graphic 都没有**；每个图层各自是一个只挂一张 Image 的 GameObject。所以「一个 GameObject 上两个 Graphic」的麻烦在这三个身上根本不存在，退位是**逐层**的。
- **`Slider` 的主表面是轨道，而 `targetGraphic` 是滑块**，两者不是一个东西（实测 `targetGraphic of sl = Handle/Image`）。见 §13.1。

`Toggle` / `Slider` / `Dropdown` / `ScrollList` 的 `Selectable.targetGraphic` 本来就指在子节点上 —— 这条先例在 §5 有用。

**写错了没人管。** `ControlAttributeApplier` 对控件没有的属性名直接 `continue`；parser 不知道每个控件有哪些属性；`Core/Lint` 是纯 C#、反射不到 Unity 侧的控件。三道关卡全部放行 —— 所以 `<Btn radius="8">` 是彻底静默的。

## 4. 否决的方案：就地换 Graphic

最直觉的做法是「声明了程序化属性就把这个表面的 `Image` 换成 `ProceduralPanel`」。**不行，两条理由，第二条是硬的。**

**① 一个 GameObject 上挂不了两个 Graphic。** `Graphic` 带 `[DisallowMultipleComponent]` —— 这条仓库自己已经写下来了，`GlassGroupPanel.Attach` 的注释原话是「the container already needs a `ProceduralPanel` to carry the group-level parameters — adding a second Graphic there **silently returns null**」。实测复现一致：往已有 `Image` 的节点 `AddComponent<RawImage>()` 被拒，`CanvasRenderer` 恒为 1。所以「并存、按需显隐」这条路根本不存在。

**② 于是换 Graphic 就等于运行时 `Destroy` + `AddComponent`。** 而这正是 `PUI-MASK-VARIANT` 当年拒绝掉的那一类 —— 那条规则的原话是「would require AddComponent/Destroy at runtime」。整套变体 / 主题机制的地基是 **`VariantStore.Changed → Screen.ReSolve` 只重放属性、不重建 GameObject**（引用与 R3 订阅必须存活）。一个变体把 `glass` 开了又关，就会把控件的 Graphic 拆了又装，`Selectable.targetGraphic`、状态反应器捕获的基色、mask 的 stencil 全部跟着失效。

**否决。**

## 5. 方案：程序化底层（backing layer）

控件声明了任一程序化属性时，在主表面**旁边**懒挂一个铺满的 `ProceduralPanel` 子节点，让原 Image 退位：

```
Btn (GameObject)
├─ Image  _bg           ← 退位：sprite 清空、alpha 归零（组件保留，不销毁）
├─ __surface__          ← 懒挂的 ProceduralPanel，anchor=stretch，SetSiblingIndex(0)
└─ Label / 作者写的子节点
```

- **没声明就一个都不挂** —— 跟 `Frame` 今天的规则一字不差，对不用这个特性的工程零成本。
- **挂上之后只改参数与可见性，永不销毁** —— 和 Add block 的 Strategy C 同构，变体来回切保持幂等。
- **`ProceduralPanel` 强制 `raycastTarget = false`**（它自己的注释写着「a Frame stays click-through」），所以点击照常落到控件本体上，不需要额外处理。
- **层序：永远 `SetSiblingIndex(0)`**，画在控件自有内容（label / checkmark / arrow）和作者子节点**之下**。

**机制不是新发明，仓库里已经有一份跑着的。** `GlassGroupPanel.Attach` 为 weld 承载者建的 `GlassWeld` 子节点就是这个形状：新建 GameObject、四角锚定铺满、`raycastTarget = false`、`SetSiblingIndex(0)`，注释写明「so the fused pane draws behind everything the blocks contain」。`__surface__` 照抄它即可 —— 包括层序那条结论（§13.6）。

另一半先例是 `targetGraphic`：`Toggle._toggle.targetGraphic = _bg`（`Background` 子节点）、`Slider._slider.targetGraphic = _handle`、Dropdown 的 item toggle 和两个滚动条 —— 「`targetGraphic` 指在子 Graphic 上」在这个仓库里已经是常态。把它指到 `__surface__` 走的是同一条路。

## 6. 范围：主表面拿全套，内层只拿形状

**主表面**（`sprite=` / `color=` 今天已经在管的那一层）进入程序化模式，拿到全套属性：`radius` / `borderWidth` / `borderColor` / `glow` / `glowColor` / `glass` 及 8 个玻璃参数。

**内层**（Slider 的 fill/handle、Progress 的 fill/frame、Dropdown 的 arrow/popup/scrollbar、Toggle 的 checkmark）**只拿 `<layer>Radius`**。

命名不用新发明 —— `Slider.cs` 的注释已经把规约写死了：「内部图层：与 `<Progress>` 同一套命名规约 —— 每层一对 `<layer>` (sprite) + `<layer>Color`」。今天 Slider 是 `sprite`/`color`（轨道）、`fill`/`fillColor`、`handle`/`handleColor`；Progress 是 `bg`/`bgColor`、`fill`/`fillColor`、`frame`/`frameColor`、`mask`。于是新增就是 `fillRadius` / `handleRadius` / `frameRadius`：**Slider +2、Progress +2**。

**为什么内层不给玻璃**（这不是为了省属性，是语义上就不对）：backdrop 采集**不含 UI 自身**（`WarnOnBackdropFeedbackLoop` 说的就是这件事）。所以压在玻璃轨道上的玻璃 Fill 采的是**同一张 backdrop**，两层长得一模一样 —— 进度条会直接消失。内层真正缺的是形状；颜色那一半 `fillColor` / `handleColor` 早就支持 token / `/alpha` / 渐变了。

`PuguiResolveRadius` 里是 `clamp(r, 0, min(halfW, halfH))`，所以 Fill 变窄时圆角自动收成胶囊头 —— 正好是对的观感，不需要额外处理。

**一个硬冲突：`<Progress mode="fill">` 与 `fillRadius` 不能共存。** `ReconcileFill` 在 `mode="fill"` 下走 `Image.type = Filled` + `fillAmount`，而 `ProceduralPanel` 没有 `fillAmount` —— 真的做不到。默认的 `mode="scale"` 是改 rect 锚点，没问题；Slider 的 fillRect 永远是锚点驱动，也没问题。所以这一组要报错。

**圆角进度条有更好的路，而且是 §9 顺出来的。** `<Progress mask=>` 今天已经在 `MaskWrapper` 上建 stencil `Mask` + 一张 Image 当遮罩源。既然 §9 证明了 `ProceduralPanel` 可以当遮罩源，`maskRadius="8"` **一个属性**就给出圆角进度条：bg 和 fill 一起被圆角裁，fill 的推进边保持方的（正确观感），而且 `mode="fill"` / `mode="scale"` 两种模式都吃。**这是推荐路径**，`fillRadius` 是逃生口。

## 7. `sprite` 与程序化在同一表面上互斥

贴图叠在 SDF 面上是一团糟，`Image.type` 的 sliced/tiled 推导对 SDF 也没有意义。规则：

- 声明了任一程序化属性 → 该表面进入程序化模式；同一表面上的 `sprite=` 是**矛盾声明**，lint 报错，运行时以程序化为准。
- `sprite="none"` / `sprite=""` **不算冲突** —— 它的语义是「清掉贴图」，跟进入程序化模式是一致的，而且换肤属性包里到处都是这个写法。
- **控件自带的默认 sprite 不算作者声明。** 实测 Slider 的 `Background` 出厂就是 `pugui_9slice_inset`、`Fill` 是 `pugui_9slice_round`、Toggle 的 `Background` 是 `pugui_9slice_round`。进入程序化模式时控件自己把它清掉，不报冲突。
- `color=` **不冲突**：它在两种模式下都是填充色（程序化模式走 `Panel.SetFill`）。

## 8. 状态视觉怎么组合

- **`*Color` / `*Modulate` 要分开落，不能像 Image 那样预乘。** 本节最初写的是「它们驱动的是 `Graphic.color`，而 `ProceduralPanel` 是 `MaskableGraphic`，所以直接可用」—— **这句是错的**，示例跑起来 hover 玻璃按钮时暴露出来。

  面板把作者写的样子放在**材质**里，`Graphic.color` 是叠在上面的**乘子**（shader 里 `col *= IN.color`）—— 正是这个拆分让同 style 的面板共用一份材质、保持合批。而 Image 没有这个拆分：那里 `Graphic.color` **就是**填充。所以 `StateTintReactor` 那句「把 modulate 预乘进 base、结果写给 `.color`」在 Image 上对、在这里错两次：

  1. base 会被应用两遍（一次当填充、一次当顶点色），`color="#3366ff"` 渲染成它自己的平方；
  2. 写进乘子通道的「绝对」hoverColor 根本不绝对 —— 它只是把底下的东西压暗，在玻璃上就是给模糊的 backdrop 上色，而不是改玻璃本身的 tint。

  正确做法：**绝对色驱动 fill，modulate 留在顶点色**。代价是绝对色变化**不再淡入**（fill 是材质参数，逐帧补间会通过 `ProceduralMaterialCache` 每帧铸一份材质；状态是离散的，所以缓存里是每状态一份而不是每帧一份）。modulate 照常淡入。
- **`targetGraphic` 的迁移必须是「算出来的」，不能一次性设。** 变体把程序化模式开了又关时，targetGraphic 要跟着回到原 Image；留在已隐藏的层上就是一个不可逆状态。这正是本周刚修的那一类缺陷（`Btn.ReconcileTransition`、`Progress.ReconcileLayers`、`StateTintReactor` 的基色），同样的形状：**从当前声明推，不从一次性快照推。**
- **`pressedSprite` / `disabledSprite` / `selectedSprite` 在程序化表面上没有意义** —— 它们是 `Image.overrideSprite` 交换。判为矛盾声明（lint 报）。它们与 ColorTint 的让位逻辑已经是算出来的（M2.5），这里要一并纳入同一个 `Reconcile`。
- **Disabled 去色要换实现，不能沿用材质替换。** 见 §13.5 —— 这是本设计里唯一一处会**弄坏现有功能**的交互，M1 必须一起做。

## 9. 遮罩：`mask="self"` 与 SDF 面

### 9.1 实测：SDF 面**已经**能当 stencil 遮罩源

在真实 `<Frame radius="24">` 上挂 `Mask`，一路查到底：

```
mask.graphic               = ProceduralPanel      MaskEnabled = True
面板   materialForRendering = Stencil Id:1, Op:Replace, Comp:Always, ColorMask:0, AlphaClip:True
子节点 materialForRendering = Stencil Id:1, Op:Keep,    Comp:Equal,  ReadMask:1
```

两个事实：uGUI 的 `Mask` 要的是 `Graphic` 而不是 `Image`（`ProceduralPanel : MaskableGraphic` 正好满足）；`AlphaClip:True` 意味着 uGUI 给遮罩源打开了 `UNITY_UI_ALPHACLIP`，于是 `clip(col.a - 0.001)` 作用在 SDF 的输出上 —— **stencil 就写在 SDF 不透明的地方，圆角是白送的**。仓库里五个 shader（ProceduralPanel / GlassPanel / GlassGroup / Grayscale / LinearLightTint）全都带完整的 `_Stencil` + `UNITY_UI_ALPHACLIP` 块，玻璃面同样吃这条路。

**所以 `PUI-MASK-FRAME-SELF` 的前提已经过期。** 它的消息是「Frame has no Image graphic」—— 那是 Frame 还完全没有 Graphic 的年代写的。今天 `<Frame radius="16" mask="self">` 被 lint 拒了、运行时也什么都不做（`Frame.Mask` 只认 `"rect"`），于是**圆角头像、圆角滚动区这两个最常见的需求在这个库里做不出来**。这个洞独立于本特性。

### 9.2 规则：一句话

> **`mask="self"` 能裁出程序化形状，当且仅当程序化 Graphic 就在挂 Mask 的那个节点上。**

`Graphic` 是 `[DisallowMultipleComponent]`（§4），`Mask` 用**自身节点**的 Graphic、裁**自身的后代**。两条一合，四种结构实测如下：

| 节点 | 自身 Graphic | `mask="self"` |
|---|---|---|
| `<Frame>` 无程序化属性 | 无 | `MaskEnabled=False` —— 今天的报错依然成立 |
| `<Frame radius/glass/…>` | `ProceduralPanel` | ✅ **按 SDF 裁剪，已验证** |
| `<Frame weld="16">` | **无**（融合面在 `GlassWeld` 子节点） | `MaskEnabled=False` → 静默失效 |
| 任意控件（§5 的 `__surface__` 在子节点） | 退位的 `Image`（alpha=0） | alpha-clip 全丢弃 → **把子节点全裁没** |

控件那一格不是「语义冲突」，是**结构上无解**：Mask 挂控件本体抓到的是退位的 Image；挂到 `__surface__` 上裁的是它自己的子节点，而控件真正的子节点是它的兄弟。没有第三种摆法。weld 承载者同理。

### 9.3 shader 改动：遮罩形状取 SDF 实心区

`clip()` 杀的是整个 fragment，颜色和 stencil 一起没，所以「光晕可见但不写 stencil」在单 pass 里做不到。但可以换掉裁的**依据** —— 遮罩形状本来就该是 SDF 的实心区，而不是「面板最终恰好不透明的地方」：

```hlsl
#ifdef UNITY_UI_ALPHACLIP
// 当 stencil 遮罩源时，形状取 SDF 实心区而非最终 alpha：
// 外发光在形状之外（inside=0），不该把遮罩撑大；没有 fill 的面也不该被裁空。
float maskCoverage = inside;
#ifdef UNITY_UI_CLIP_RECT
maskCoverage *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
#endif
clip(maskCoverage - 0.5);
#endif
```

三份 shader（`UI-ProceduralPanel` / `UI-GlassPanel` / `UI-GlassGroup`）改同一份四行，`inside` 在三处的该位置都在作用域内。

**为什么安全**：`UNITY_UI_ALPHACLIP` 只在 stencil 遮罩源上开 —— 实测里面板是 `AlphaClip:True`、它的子节点是 `AlphaClip:False`，因为 uGUI 只在「这次 draw 要写 stencil」时才打开它。所以**正常渲染路径逐位不变**，对今天已出货的东西零风险。

**买到什么**：

- 外发光不再把遮罩撑大一圈（`glow.a *= g*g*(1-inside)` 在形状外仍 > 0.001，按旧写法会渗）。
- 没有 fill 的面也能当遮罩 —— `<Frame radius="16" mask="self" showMask="false">` 就是一个**隐形的纯圆角裁剪器**，最有用的形态，零额外属性。按旧写法它内部 `col.a = 0`、只有描边环写 stencil，子内容只在细环里露出来。
- 玻璃面在 backdrop 还没采到时（编辑器无相机、首帧之前）遮罩形状照样正确 —— 否则 `base = float4(0,0,0,0)` 会让它裁空一帧。
- **原本需要的两条 lint 规则（no-fill 遮罩、glow 撑大遮罩）直接不需要了。**

代价如实记一条：`clip` 是硬切，遮罩边缘没有抗锯齿。但这是 uGUI stencil 遮罩的固有行为（sprite 遮罩一模一样），不是回退。

### 9.4 于是变成三条 lint 规则

1. **`PUI-MASK-FRAME-SELF` 收窄** —— 只在 Frame 没有任何程序化属性时报（消息照旧，依然成立）。
2. **新规则**：控件上程序化属性 + `mask="self"` → 报错，指路 `mask="rect"` 或外面套一层 `<Frame radius mask="self">`。
3. **新规则**：`weld` + `mask="self"` → 报错（融合面在 `GlassWeld` 子节点，够不着）。

## 10. 成本

| | 开销 |
|---|---|
| 没用程序化皮肤的控件 | **零** —— 一个组件都不挂，和今天逐字节相同 |
| 用了的控件 | 多一个 GameObject + CanvasRenderer + 一次 draw |
| 同 style 的多个面板 | 共享材质、可合批（`ProceduralMaterialCache`） |
| 玻璃 backdrop 采集 | 每帧一次固定开销，与面板数量无关；没有可见玻璃面板时不存在 |
| `mask="self"` | stencil 遮罩会打断合批 —— uGUI 固有，`mask="rect"` 更便宜。SKILL 提一句即可 |

## 11. 这套要替代的 userland 写法

今天能做到的是往控件里塞一个铺满的玻璃 `<Frame>`（已验证可行：铺满、`raycastTarget=false`、点击照常落到控件）：

```xml
<Btn id="ok" sprite="none" color="#00000000">
  <Frame anchor="stretch" glass="true" radius="8" borderWidth="1" borderColor="white/0.55"/>
  <Text anchor="center">确定</Text>
</Btn>
```

三个缺点，正好对应本设计要解决的东西：

1. 每个控件都要多写一层，而且这一层的显隐还得自己挂 class 让主题去切。
2. **`<Btn>` 不允许文字简写与子元素混用**（`<Btn><Frame/>确定</Btn>` 直接 parse error：「mixes text and child elements」），所以文字必须改写成 `<Text>` 子节点 —— 于是 `textColor=` / `fontSize=` 这些**控件级**属性够不到它，主题换字色的路断了。
3. Toggle 的勾选框、Slider 的轨道、Dropdown 的弹出层这些**内层根本塞不进去**，userland 无解。

## 12. 与 lint / SKILL 的关系

这三条**不依赖本特性**，可以先做；但第一条在本特性落地后要反过来删掉，所以要一起规划。
**1 与 2 已实现**（M-lint），3 仍未做。

1. ~~**`PureContainerVisualAttrRules` 补第三档。**~~ **已做。** 它已经拿着那份一模一样的程序化属性清单（`color radius borderWidth borderColor glow glowColor glass frost depth dispersion lightAngle lightIntensity saturation noise weld`），也已经在做「这个标签会静默丢弃这些属性」的判断 —— 只是 `AppliesTo` 现在只覆盖 `Frame`（查 `sprite`）和四个纯排版容器（查全部）。缺的是中间那档：**挂了 Image、所以 `color`/`sprite` 有效，但没有 `ProceduralPanel`、所以程序化那一组一律被丢。** 那个类的注释里作者当年明确考虑过 `<Btn>` 并把它排除在 `LayoutOnlyTags` 之外（正确 —— `color` 在 Btn 上有效），但没人补上第三档。

   实现时没有手写「哪些标签挂了 Image」的清单，而是把 `AppliesTo` 换成 `BuiltinTags.IsBuiltin` —— 底下的事实只有一条（**只有 Frame 挂 `ProceduralPanel`**），清单会烂而事实不会。报的属性是 `ProceduralAttrNames.NeedsPanel`（= `All` 减去 `color`，因为 `color` 在任何有 Image 的控件上都成立）。

   配套的 `OnlyFrame_HasThePanelRequiringAttributes` 拿活的注册表验证那条事实 —— 于是 **M2 让某个控件长出程序化表面的那天，这个守卫会失败**，把「该去删规则」这件事变成不可能忘。
2. ~~**SKILL 缺一句边界话。**~~ **已做。** `glass.md` 写的是 "All live on `<Frame>`"，但紧接着 "all work through `<Style>` / `class=` … **like any other attribute**" —— 读起来像是通用的。主 SKILL 从正面说了 Frame 会自绘、从反面说了纯排版容器啥也画不了，唯独中间这档从没人写过。
3. **更大的洞：内置控件上写错属性名是完全静默的。** 同一轮里我写 `<ScrollList scrollbarSprite="none">` 什么都没发生 —— 它的 XML 名其实是 `scrollbar`（`[UIAttr(IsSprite)]` 会剥掉尾部 `Sprite`，好和 `<Dropdown scrollbar=>` 对齐）。要让 CLI 能查，得把「tag → 属性名」这张表送进 `Core/Lint`。零件都在：`Editor/XsdGenerator.cs` 已经在反射注册表生成 schema，`BuiltinTags` + `BuiltinTagsTests` 是「手工镜像 + 守卫测试」的现成先例。**独立一条，不属于本设计。**

## 13. 已定的决策

原开放问题，逐条定了。

**13.1 `Slider` 的状态色落在滑块，不动。** `targetGraphic` 留在 `Handle` 上，程序化只换轨道（`Background`）的画法。代价是 `hoverColor` 够不到轨道 —— 接受，因为轨道本来就不是可交互的那个部件，而滑块是。想让轨道也随状态变，用 `<Show on="state-*">`。

**13.2 `weld` 不跨控件。** weld 的成员是同一个 carrier 的**直接子级**，而各控件的 `__surface__` 分属不同父节点。作为特例背景保留现状，**不做**自动跨控件融合。

**13.3 `Progress` 纳入。** 它的 bg / fill 默认就没有贴图（纯色层），本来就算「已经是程序化的一种」。主表面 = `MaskWrapper/Bg`，内层按 §6 给 `fillRadius` / `frameRadius`，圆角走 `maskRadius`。

**13.4 `mask="self"`** —— 见 §9，独立成节。

**13.5 Disabled 去色：玻璃降饱和 + 降厚度，非玻璃降饱和。** 具体：玻璃面 `saturation → 0` 且 `depth` 下调（薄玻璃 = 失活）；非玻璃程序化面只降饱和。

**但实现不能沿用现有机制，这是本设计里唯一一处会弄坏现有功能的地方。** `DisabledGrayscaleController` 今天的做法是把非 TMP `Graphic` 的 `material` 换成共享的 `UI-Grayscale` —— 换到 `ProceduralPanel` 上就是**把 SDF 材质整个换掉**，形状、描边、发光、玻璃全部消失，只剩一块灰方（还可能是被 glow 撑大过的 quad）。而且是双向坏：`ProceduralPanel.FlushParams` 每次参数变化 / canvas 重建都会把 `m_Material` 从缓存里写回去，**又会把灰度材质冲掉**。

所以程序化面必须从材质替换里**摘出去**，改成在自己的材质里降饱和 —— 玻璃的 `saturation` 参数已经现成，非玻璃需要给 fill / border / glow 颜色补一条去色路径（或加一个 uniform）。`DisabledGrayscaleController._captured` 的 capture-once 语义要相应地对 `ProceduralPanel` 走另一条分支。**M1 必须一起做**，否则第一个接上 `__surface__` 的控件一进 Disabled 就露馅。

**13.6 `__surface__` 层序：永远 `SetSiblingIndex(0)`。** 画在控件自有内容和作者子节点之下。照抄 `GlassWeld`（§5）。

## 14. 里程碑拆分（草案）

| | 内容 | 依赖 |
|---|---|---|
| ~~**M-lint**~~ ✅ | §12.1 + §12.2：`PureContainerVisualAttrRules` 第三档 + SKILL 边界话 | 无，可先合 |
| ~~**M-mask**~~ ✅ | §9 全部：shader 四行、`PUI-MASK-FRAME-SELF` 收窄、`Frame.Mask` 认 `"self"`、两条新规则、SKILL | 无，可先合 |
| ~~**M0 Red test**~~ ✅ | 钉住 §7 / §8 的契约（25 条，落地时 21 红 4 绿） | 无 |
| ~~**M1 表面抽象 + 打通一个控件**~~ ✅ | `ProceduralSurface` + `ProceduralControl` 基类，`Btn` 第一个接上；§13.5 的 Disabled 分支一起做 | M0 |
| ~~**M2 铺开**~~ ✅ | Tab / Toggle / Slider / Dropdown / InputField / ScrollList / Progress | M1 |
| ~~**M3 内层形状**~~ ✅ | §6 的 `fillRadius` / `handleRadius` / `frameRadius` / `maskRadius`，`mode="fill"` 冲突规则 | M2 |
| ~~**M4 收尾**~~ ✅ | §8 的状态 sprite × ColorTint 组合、每个控件小节的边界指路、spec 状态 | M3 |

**M-lint 与 M-mask 都不依赖本特性，已先于 M0 合入。** 在本特性落地前，`<Btn radius="8">` 确实是错的，早一天报错早一天省事；而 §9 那条圆角裁剪今天就该能用。

### M0 / M1 实施记录

**`__Surface` 命名**：spec 原稿写的是 `__surface__`，实际用 `__Surface` —— 跟着仓库已出货的 `__FocusCursor`，而不是跟着我自己的草稿。

**模式是「当前解析值」的问题，不是「写没写」的问题。** 一度以为变体只能改值不能删属性、所以程序化模式一旦打开就关不掉、§8 的 targetGraphic 迁移问题不存在。`VariantResolverTests.Variant_only_attr_returns_override_when_active` 推翻了这个假设：**只有变体、没有基值**的属性是合法的，`radius.mobile='8'` 在 mobile 关掉时模式真的会翻回去。

而 `ControlAttributeApplier` 表达「这个属性现在没有值」的方式是 `if (v == null) continue;` —— **不调 setter**，也就是说「作者不再声明它」是以沉默的形式到达的。于是给 `Control` 加了 `OnBeforeApply()`：每轮开头清掉本轮标记，各 setter 重新声明，`OnAfterApply` 按当下为真的东西 reconcile。这就是 §8 那条「从当前声明推、不 latch」的落地形式。

**退位是把 alpha 归零，不是 `enabled = false`。** uGUI 只对**启用**的 Graphic 做射线检测，而面板是 `raycastTarget = false` —— 把 Image 关掉会让控件完全收不到点击，而且不报任何错。同理退位要**每轮重放**：控件自己的 `color=` setter 排在 `Reconcile` 之前，会把 alpha 又写回去。

**属性声明放在 `ProceduralControl` 基类上一次搞定。** `ControlMeta.Build` 用的是 `BindingFlags.Public | BindingFlags.Instance`，**包含继承来的属性**，所以子类白拿 13 个。`color` 刻意不在基类里：在 `Frame` 上它会挂面板（Frame 没别的东西可上色），在 Image 系控件上它只是普通 tint，不该把控件拖进程序化模式。

**M-lint 那一档按预期缩小了，而守卫按预期先报警。** `OnlyFrame_HasThePanelRequiringAttributes` 在 Btn 接上的那一刻失败 —— 正是它存在的意义。现在它跟 `ProceduralSurfaceRules.SurfaceTags` 双向对表：接了线没登记（规则继续误报可用属性）和登记了没接线（规则对真坏掉的属性闭嘴）都会挂。

**`weld` 是那一档留下的唯一例外。** 控件接上程序化表面之后，`weld` 仍然无效（它融的是 Frame 的直接玻璃**子级**，控件没有），所以 tier 2 对已接线的控件只报 `weld` 这一条，不是整体闭嘴。这条是被自己的测试 `("Btn","weld")` 抓出来的。

**一处测试前提作废，如实改掉**：`StyleIntegrationTests.Style_AppliesToAnyControl_IgnoringAttributesItDoesNotHave` 的注释原文是「`radius` means nothing to `<Btn>`」—— 那正是本里程碑要删掉的边界。改成 `Style_SkinsAFrameAndAControlAlike`（同一个 pack 现在真的能同时换掉 Frame 和 Btn 的形状），另补一条用 `weld` 演示原来那个「控件没有的属性照样被忽略」的性质。

### M2 实施记录

七个控件接上，每个只要指出 `SurfaceHost` / `SurfaceSelectable` 再登记进 `ProceduralSurfaceRules.SurfaceTags`。**没有一条既有测试因此挂掉** —— 两轮全量跑下来失败的全是 lint 用例本身用了已接线的标签当例子，属于预期的名单迁移。

**表面挂在「原 Image 所在那一层」的内部**，所以它盖住的正好是 `color=` / `sprite=` 今天上色的那块：Toggle 的形状只覆盖勾选框、不覆盖 label；Slider 只覆盖轨道、不覆盖滑块。这条是渲染出来肉眼确认的，不是推的。

**两个特例**：

- `Slider` 的 `SurfaceSelectable` 故意留空（§13.1）—— targetGraphic 留在滑块上，会响应 hover/press 的本来就是滑块。
- `Progress` 的 `Bg` 层出厂就是 `SetActive(false)`，只有写了 `bg=`/`bgColor=` 才亮。表面住在那一层里面，所以 `ReconcileLayers` 得把 `SurfaceIsDrawing` 也算进去，否则形状被画在一个不可见的层里。`OnAfterApply` 必须**先** `base`（表面 reconcile）**再** `ReconcileLayers`，顺序反了就读到上一轮的状态。为此给 `ProceduralControl` 加了不分配的 `SurfaceIsDrawing`（`Surface` 属性会懒建对象，用在这里会让每个 Progress 都白建一个）。

**`<Progress radius=>` 只圆 bg 那一层**，而 fill 是压在上面另一张方角 Image，所以进度条长成只有尾端是圆的。渲染确认过。这不是缺陷，是 `radius` 的作用范围 —— 也正是 §6 把 `maskRadius` 定为圆角进度条推荐路径的原因（M3）。已写进 `reference/controls-progress.md`，免得被当 bug 报。

### M3 实施记录

内层每层一个自己的 `ProceduralSurface`（`AddInnerSurface`，不带 Selectable），跟主表面一起参与同一轮 `BeginPass` / `Reconcile`。新增属性正好是 §6 说的那四个：Slider `fillRadius` / `handleRadius`，Progress `fillRadius` / `frameRadius` / `maskRadius`。

**`maskRadius` 改成自动跟随 `radius`**，spec 原稿把它写成一个纯手写属性。改的理由：`<Progress radius="12">` 单独用会渲染成**只有尾端是圆的**进度条（M2 那张图），而没有哪个作者写 `radius` 时是想要这个。自动跟随不是新发明 —— `<ScrollList mask>` 跟随 bg sprite、`<Dropdown popupMask>` 跟随 `popupSprite`，仓库里已经是既有规约，连「显式写任何值（含 `""`）即退出跟随」的语义都照搬。

渲染验过三行，**第一行和第二行只差一个 `maskRadius=""`**：`radius='22'` 两端都圆；加上 `maskRadius=''` 变成左端方、右端圆；单写 `maskRadius='22'` 也两端都圆。这是自动跟随确实在起作用的最直接证据，已固化成 render test。

**程序化 mask 是纯裁剪器**（`showMaskGraphic=false`），底交给 `bgColor`。sprite 版 `mask=` 保留它原有的双重身份（PB-D9：没写 bg 时 mask sprite 兼当底），所以 `ReconcileMaskVisibility` 要按 `_maskGraphic != null` 分流。这里也依赖 M-mask 那条 `SetMaskSource` —— 不画东西的裁剪面必须照常出几何。

两条新 lint：`PUI-PROG-FILL-RADIUS-MODE`（`mode="fill"` 靠 `Image.fillAmount`，SDF 面没有对应物 —— 这是少见的**真做不到**而不是「不推荐」）、`PUI-PROG-MASK-RADIUS-CONFLICT`（一个 GameObject 一个 Graphic，sprite 赢）。

`dotnet format` 抓到 Slider.cs 的 using 顺序 —— `dotnet format style` 修掉了。

### M4 实施记录

原计划的 M4 里，「逐个控件从 §12.1 那一档摘出去」和「§7 / §8 的冲突规则」**在 M1/M2 就自动完成了** —— 前者是因为把 `AppliesTo` 写成查 `SurfaceTags` 而不是硬编码名单，后者是 M1 顺手做掉的。剩下的是一条真缺陷加两处文档。

**真缺陷：状态 sprite 会把控件从 ColorTint 上摘下来，即使它根本显示不出来。** `<Btn radius="8" pressedSprite="ui:x">` 实测 `transition = None` —— 而 `pressedSprite` 换的那张 Image 在程序化模式下已经退位、alpha 为 0，所以按下去**什么反馈都没有**，而且没有任何属性能把它要回来。这正是 `Btn.ReconcileTransition` 当年为了避免而写的那个死角，从另一个方向又走进来了一次。

修法是把表面纳入同一个计算：状态 sprite 只在**它真的能显示**时才拿走 ColorTint。`Btn` 与 `Tab` 各一行。三条测试钉住：程序化模式下 ColorTint 保留、非程序化模式行为逐字不变（守卫）、变体来回切两个方向都对。

这条值得记住的地方在于 —— **lint 早就报了这个组合**（`PUI-PROC-STATE-SPRITE-CONFLICT`），但报错不等于运行时可以摆烂：作者忽略警告时得到的应该是「sprite 没生效」，不是「整个按钮失去反馈」。

**文档**：七个控件各自的属性表都补了一行指路。之前只有共享的「程序化表面」一节和 Frame 附近的边界说明 —— 而只翻 `<Slider>` 那一节的读者看不到它们，那恰恰是 SKILL 要防的失败模式。

M-mask 实施中发现的一件 spec 没写到的事，记在这里：**shader 那四行还不够。** `ProceduralPanel.ComputeVisible()` 会把「什么都不画」的面板整个剔掉几何，而 stencil 是 fragment 写的 —— 于是「隐形的圆角裁剪器」一个像素都不剩。补了 `SetMaskSource`，遮罩源照常出几何。这条是 render test 抓出来的：光看 C# 状态（Mask 挂上了、graphic 指对了、`MaskEnabled` 为 true）全是绿的，和当年缺 `[RequireComponent(CanvasRenderer)]` 那次同一类。
