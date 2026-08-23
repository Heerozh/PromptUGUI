# 程序化样式系统：SDF 渲染原语 + `<Style>`/`class` 机制

**日期**：2026-08-23
**状态**：已实施（M1 + M2 一次做完，分支 `feat/procedural-style`）。实施期偏离设计的三处记在 §12。
**作用域**：两个正交子系统。（A）`<Frame>` 获得一组程序化视觉属性——圆角、半透明填充、渐变、内描边、外发光——由 SDF shader 渲染，零 sprite；（B）新增顶层元素 `<Style name=...>` 与通用 `class=` 属性，把命名属性集合宏展开到任意节点。B 不依赖 A：class 是纯属性宏，对**任何**控件的**任何**属性生效（`<Btn class="primary">` 同样成立）。
**关联**：颜色解析 chokepoint 与 `/alpha` 后缀沿用 [color-tokens](2026-05-28-color-tokens-design.md)（PR #51）；填充渐变直接复用 [gradient-color](2026-06-13-gradient-color-design.md) 的逗号语法与 `ColorSpec`/`ResolveSpec`；本设计是后续**玻璃（backdrop blur）填充模式**的地基——玻璃 = 同一套 SDF 形状 + 另一种填充，单独成 spec。`<Frame>` 长出 `color` 后，[`PureContainerVisualAttrRules`](../../Runtime/Core/Lint/PureContainerVisualAttrRules.cs) 与 SKILL 的"纯容器没有 Image"叙述需同步调整（见 §8 / §9）。

---

## 1. 背景与目标

当前所有界面样式都由 sprite（.pxl / 图集）定义。目标是提供一条**现代 CSS 式**的替代路径：作者不画图素，直接用参数声明卡片、遮罩、描边框、发光钮的外观：

```xml
<Style name="card" color="surface/0.85" radius="16" borderWidth="1" borderColor="stroke/0.15"/>

<Frame class="card" anchor="top-stretch" height="220" margin="16,16,_,16">
  ...
</Frame>
```

设计约束：

1. **形状即 RectTransform**。不发明独立的形状语言——形状 = 节点自身的 rect（沿用现有 anchor/size/margin 体系）+ 角处理参数。SDF 解析式在 fragment 里求值，无网格细分、无贴图。
2. **class 是属性宏，不是样式引擎**。没有选择器、没有级联、没有继承树；只有"命名属性包 + 引用处合并"。因此它自动覆盖所有控件所有属性（含模板参数、含 Variant 后缀属性），实现只在展开层，运行时零成本。
3. **复用而非新造**。填充色走现有 token/`/alpha`/逗号渐变 chokepoint；共享与冲突语义抄 Template（commons 池 + 硬冲突）；ReSolve/Variant 走现有属性重放，无新机制。
4. 为玻璃留位：shader 的形状/描边/发光段落与未来玻璃填充共用，参数命名一次定对（玻璃的"假描边"就是这里的 border，发光就是这里的 glow）。

## 2. 子系统 A：`<Frame>` 程序化视觉属性

### 2.1 属性表

全部为 `[UIAttr]` 字符串 setter，Variant 覆写（`color.dark=` 等）与 ReSolve 自动生效。

| 属性 | 语法 | 默认 | 说明 |
|---|---|---|---|
| `color` | token / hex / CSS 命名 / `A,B` 渐变 / `/alpha` | —（无填充） | 填充色。走 `Theme.ResolveSpec`，逗号渐变=上下双色（复用 gradient-color 语义：第一段顶部）。 |
| `radius` | `R` / `TL,TR,BR,BL` / `pill` | `0` | 圆角半径 px。四值按 CSS `border-radius` 顺时针序（左上起）；`pill`=min(宽,高)/2 的胶囊。 |
| `borderWidth` | float px | `0` | **内**描边宽度（向内绘制，不影响布局，即 CSS `box-sizing: border-box` 直觉）。 |
| `borderColor` | token / hex / `/alpha`，**纯色 only** | `white` | 描边色。渐变值 → parse error（同 `*Modulate` 先例，报错不降级）。 |
| `glow` | float px | `0` | 外发光半径（SDF 距离衰减，smoothstep 到 0）。 |
| `glowColor` | token / hex / `/alpha`，**纯色 only** | 填充 Top 色 | 发光色。无 `color` 时缺省 white。 |

- 任一属性出现（基础值或 Variant 覆写）→ Frame 根 GO 上 lazy 挂 `ProceduralPanel`；一个都没有 → 保持今天的纯 RectTransform 容器，行为零变化。
- `raycastTarget` 恒为 `false`：Frame 保持不可交互，点击穿透（可点的着色区域仍用 `<Btn>`，与现有 lint 建议一致）。
- 只写 `borderWidth`（无 `color`）= 纯描边空心框，合法且常用。
- `sprite` 在 Frame 上**依旧无效**（sprite 属于 `<Image>`），lint 继续报（见 §8）。
- VStack / HStack / Grid / SafeArea 保持纯容器，v1 不长视觉属性（见 §7 扩展位）。需要带背景的栈：`<Frame class="card"><VStack .../></Frame>`。

### 2.2 radius 解析错误（parse-time）

| 输入 | 行为 |
|---|---|
| 逗号切出 2 段或 ≥5 段 | error：radius 接受 1 值或 4 值（TL,TR,BR,BL） |
| 负数 / 非数值段 | error，指明是哪一段 |
| `pill` 与逐角值混写（`pill,0,0,0`） | error：pill 是整体关键字 |

解析器 `RadiusParser` 放 `Runtime/Core/Layout/`（纯 C#，float only），UIXmlLint CLI 共享同一实现，作者在 CLI 阶段即可暴露语法错误（`ColorLiteralRules` 先例）。半径超过 rect 半边时 runtime clamp（CSS 同款行为），不报错——rect 尺寸解析期未知。

### 2.3 渲染实现：`ProceduralPanel`

`Runtime/Controls/Internal/ProceduralPanel.cs`，`ProceduralPanel : MaskableGraphic`：

- **单 pass shader** `Runtime/Resources/PromptUGUI/Material/UI-ProceduralPanel.shader`（`UI-Grayscale` 先例：Resources 加载）。fragment：圆角矩形 SDF（iq sdRoundBox，逐象限选半径）→ 由距离场派生三段：填充（`d < 0`）、内描边带（`-borderWidth < d < 0`，覆盖在填充上）、外发光（`0 < d < glow`，衰减）。抗锯齿用 `fwidth(d)` 的 smoothstep，分辨率无关。
- 包含 uGUI 标准段：`_ClipRect` + `UNITY_UI_CLIP_RECT`（RectMask2D 兼容）、stencil 属性块（`Mask` 组件兼容）——`MaskableGraphic` 的材质改写才能正常工作。
- **每 panel 一个材质实例**（`new Material(shared)`）。CanvasRenderer 不支持 MaterialPropertyBlock，而 SDF 需要 rect 尺寸/半径/色彩等 uniform。代价是 styled Frame 各占一个 drawcall、不参与合批——像素游戏 UI 同屏数十面板量级，可接受；顶点通道打包（参数塞 UV1-UV3 共享单材质）记为性能扩展位，不做 v1（§7）。材质实例在 `OnDestroy` 释放。
- **填充渐变**：`_FillTop` / `_FillBottom` 两个材质色，shader 按 rect 归一化 Y lerp。不复用 `GradientTint` 顶点方案（顶点色槽无法区分填充/描边/发光三种色），但**值语法与解析完全复用**：setter 调 `Theme.ResolveSpec` 得 `ColorSpec`，纯色时 Top==Bottom。
- **发光越界**：`OnPopulateMesh` 输出按 `glow` 向四周外扩的 quad（UV 携带 rect 局部坐标供 SDF 求值）。外扩顶点会被**祖先** RectMask2D 裁掉（预期行为，写进 SKILL）；自身 `mask="rect"` 只裁子级、不裁本 panel，发光不受自己的 mask 影响。
- rect 尺寸变化（`OnRectTransformDimensionsChange`）→ 更新材质 `_Size` + `SetVerticesDirty`；`pill` 与半径 clamp 在 shader 内按 `_Size` 现算，尺寸动画无 C# 逐帧成本。
- **绝不 Destroy**：Variant / ReSolve 往返只改材质参数（组件一旦创建跟随 Screen 生命周期，Strategy C / `GradientTint` 同哲学）。Variant 无法"删除"属性只能改值，故无需 disable 路径；`color` 切透明即视觉隐藏。
- 与 `PixelSnap` 的关系：SDF 边缘是亚像素 AA，与像素风的取舍由作者控制（radius=0 + 无 glow 时输出即硬边矩形）。量化/马赛克化留玻璃 spec 一并考虑，v1 不做。

## 3. 子系统 B：`<Style>` 声明

### 3.1 语法

```xml
<Style name="card" color="surface/0.85" radius="16" borderWidth="1" borderColor="stroke/0.15"/>
<Style name="card-tight" color="surface" radius="8" radius.mobile="4" padding="8,8,8,8"/>
```

- 顶层元素，与 `<Template>` / `<Theme>` / `<Screen>` 平级。无子节点（有子节点 → parse error）。
- `name` 必填，kebab-case `[a-z0-9-]`（与 Color token 同规），同文档重名 → parse error。
- 属性集合就是"要合并的属性包"，**任意属性名都合法**（含 Variant 后缀 `attr.var`）——Style 不知道也不关心目标控件是谁。仅四个结构性属性被禁止：`id` / `if` / `class` / `bind`（parse error；它们标识节点身份/存在性，不是样式）。
- Style **不能引用 Style**（无继承/组合）；复合外观用多 class（§4.2）。

### 3.2 IR 与解析

```csharp
// Runtime/Core/IR/StyleDef.cs
public sealed class StyleDef
{
    public string Name { get; }
    public Dictionary<string, string> Attributes { get; } = new();
    public Dictionary<string, List<(string Variant, string Value)>> VariantOverrides { get; } = new();
    public string OriginSrc { get; set; }   // 仅 commons reload 使用，镜像 TemplateDef
}
```

`UIDocument` 增 `Dictionary<string, StyleDef> Styles`；`UIDocumentParser` 解析规则与元素属性同款（含 `.variant` 后缀切分）。

### 3.3 共享 / 命名空间 / 热重载（全部镜像 Template）

- `DocumentLoader.LoadedDoc` 增 `Dictionary<StyleKey, StyleDef> Styles`（`StyleKey(ns, name)` 镜像 `TemplateKey`）；commons 池同步增 styles 段，`LoadCommonLibraryAsync(src, [as])` 一并收录，`as` 命名空间下的引用写 `class="ui:card"`（冒号引用与 sprite/icon 的 `Set:Name` 一致）。
- entry 文档与 commons 同名 → **硬冲突错误**（Template 同款，非 last-wins）。
- 热重载零新机制：entry 文档改 Style → 文档 reload 全 pipeline 重跑；commons 改 Style → `ReloadCommonLibraryAsync` 按 `OriginSrc` 换池 + DepGraph 触发依赖方，与 commons Template 完全同路径。

## 4. `class=` 引用与合并

### 4.1 展开位置：搭 `TemplateExpander` 的拷贝车

`TemplateExpander` 本就对每个节点深拷贝（`ExpandTree` / `ExpandNode`）。class 合并作为拷贝时的一步插入，不新增树遍历，也天然规避"共享 commons 模板体被就地污染"的问题（合并只发生在拷贝产物上）。`Expand(LoadedDoc)` 签名不变，styles 从 LoadedDoc 取。展开后输出树中**不存在** `class` 属性（已消费移除）——实例化器与运行时对 class 无感知，这是"零运行时成本"的由来。

覆盖面自动完整：Screen 树、`<Variant>` Add 块、FocusCursor、模板体内节点都走同一拷贝路径。模板体内 `class` 的值先经 `{{param}}` 替换再解析——`class="{{skin}}"` 动态选样式是自然结果，允许（未知名照常报错）。`<Screen>` 自身不接受 class（parse error）——Screen 根不是控件节点。

### 4.2 合并算法

```
node 携带 class="a b c"（空白分隔，可多个）：
1. 逐名解析 StyleKey → StyleDef，未知名 → TemplateException（消息含候选名）。
2. 左→右叠成一个属性包：后者覆盖前者（同名属性以右侧 class 为准）。
3. 属性包并入节点，按属性名原子判定：
   节点自身已声明该名（基础值 或 任一 .variant 后缀）→ 该名的包内容整体跳过；
   否则 Attributes / VariantOverrides 整体并入（保持 Style 内声明序）。
4. 目标是模板调用时，包先按 该模板 Params ∪ CommonAttrs 过滤，不匹配的名静默跳过。
5. 从输出节点移除 class。
```

要点与理由：

- **inline > class，按属性名原子**。inline 写了 `radius="20"`，class 的 `radius` 基础值**和** `radius.mobile` 一起被屏蔽——不做半合并（inline 基础值 + class 变体值混拼），行为可预测、可解释、实现最简。
- **Variant 免费兼容**：Style 里的 `.variant` 后缀属性原样并入节点的 `VariantOverrides`，运行时走现有 `VariantResolver` last-active-wins；由原子规则保证同名不会与节点自带条目交错。
- **不适用即忽略（CSS 语义），由构造免费获得**：并入的属性若目标控件没有对应 `[UIAttr]`，`ControlAttributeApplier` 现有逻辑本就静默跳过（`Meta.HasAttribute` 检查）。一个 `card` 样式可同时用在 Frame 和 Btn 上，各取所需——这正是 CSS "不适用的属性被忽略"的直觉，零新代码。
- **模板调用也能吃 class**（第 4 步）：包里与模板 Param 同名的项作为参数默认值参与展开，CommonAttrs（anchor/size/margin…）落到实例根——语义与"把这些属性 inline 写在调用上"完全一致；其余项静默跳过而非报错，否则跨控件共享的样式在模板调用上必然炸。inline 直写未知属性仍是硬错误（现状不变）——宽容只给 class 这种"广播"来源。
- `class=""` 或解析出零个名 → parse error（无意义写法即错误，不静默）。

### 4.3 优先级全景（运行时视角）

单一属性的最终值 = `VariantResolver`（last-active-wins）作用于合并后的节点，而节点内容按此优先序构成：

```
inline 属性（基础值+变体）  >  class 右侧样式  >  class 左侧样式
```

Theme token 解析、`/alpha`、渐变发生在值层，与来源无关——`color="surface/0.85"` 写在 Style 里和写在 inline 效果一致。

## 5. 与玻璃的关系（前瞻，不在本 spec 实施）

玻璃 spec 将在 `ProceduralPanel` 上增加一种填充模式（backdrop 采样 + 边缘光照 + URP RendererFeature 供纹理），形状（radius/pill）、border、glow 参数**原样共用**。本 spec 中 shader 的 SDF/描边/发光段落应写成可被玻璃变体 include 复用的结构（HLSL include 拆分），但不为玻璃预留半成品参数。

## 6. 明确不支持（parse error，非静默降级）

| 目标 | 原因 |
|---|---|
| `borderColor` / `glowColor` 渐变 | 描边带/发光环上的线性渐变视觉意义弱，且需额外 uniform；同 `*Modulate` 先例报错不降级 |
| Style 引用 Style / 继承 | 组合用多 class；级联树是本设计明确拒绝的复杂度 |
| Style 携带 `id` / `if` / `class` / `bind` | 结构性属性，不是样式 |
| `<Screen class=...>` | Screen 根不是控件节点 |

## 7. 不做的事（YAGNI 记录，留扩展位）

- `cornerSmoothing`（超椭圆/squircle，Figma corner smoothing）、`shape="cut"` 切角——shader 留 uniform 位，等真实需求。
- 偏移投影（drop-shadow = glow + offset）——`glowOffset` 扩展位。
- VStack / HStack / Grid / SafeArea 长视觉属性——属性集与实现可整体复用，等需求；现阶段外套 Frame。
- 圆角裁剪子内容（`overflow:hidden` + radius）——`Mask` 组件 + ProceduralPanel 的 stencil 路线可行，单独立项。
- 顶点通道打包参数以恢复合批——面板数上百再说。
- 多 class 的选择器/伪类/级联——永不。

## 8. Lint / XSD

- **`PureContainerVisualAttrRules` 调整**：`Frame` 对 `color` 从名单摘除（合法了）；`sprite` 继续报，消息改为引导 `<Image>` 或 Frame 程序化属性。841 行 SKILL 示例 `<Frame color="black/0.5"/>` 随之转正。
- **新增 `StyleRules`（Core/Lint）**：`<Style>` 缺 name / name 非 kebab / 同文件重名 / 含子节点 / 携带禁用属性 / `class=""` 空引用。
- **不做 CLI 侧 unknown-class 检查**：commons 在运行时注册，CLI 单文件视角永远无法证明一个 class 名不存在；未知名由运行时 TemplateException 兜底。
- `RadiusParser` 语法校验进 CLI（§2.2）。
- **XSD**：`<Style>` 顶层元素（anyAttribute）；Frame 增新属性；`class` 进通用属性组。生成器测试沿用 substring 断言。

## 9. 测试（Red 先行）

EditMode（`UI.ResetForTests` 约定）：

1. **RadiusParser**：1 值 / 4 值 / pill / §2.2 全部错误用例。
2. **Style 解析**：属性包 + `.variant` 后缀入 IR；§3.1 全部 parse error；`UIDocument.Styles` 形状。
3. **合并**：inline 覆盖（含"inline 基础值屏蔽 class 变体值"原子性）；多 class 右侧覆盖左侧；未知名报错；模板体内 class；`class="{{p}}"` 参数化；模板调用上 Param/CommonAttrs 过滤 + 其余静默跳过；展开产物无 class 属性。
4. **commons**：Style 入池 / `as` 命名空间引用 / 同名硬冲突 / `ReloadCommonLibraryAsync` 换池后依赖方产物变化（`DocumentLoaderTests` fake-files 模式）。
5. **ProceduralPanel**：lazy 挂载（无视觉属性不挂）；材质参数断言（radius 四值 / pill / 渐变 Top-Bottom / border / glow）；ReSolve/Variant 往返幂等不 Destroy；glow 外扩顶点数与包围盒；`borderColor` 渐变值报错。
6. **应用链路**：`<Frame class="card">` 端到端实例化出正确材质参数；`<Btn class="card">` 不适用属性静默忽略且 Btn 自身属性正常。
7. **XSD**：substring 断言。

PlayMode：一条 styled Frame + Variant 切换（radius.mobile）冒烟，确认重解算改材质不重建 GO。

## 10. SKILL 更新（同 PR，英文）

- `authoring-promptugui-xml/SKILL.md`：
  - `<Frame>` 一节：从"空容器"改写为"容器 + 可选程序化视觉"，新属性表（§2.1）、radius 语法、glow 会被祖先 mask 裁剪的注意项、"可点区域仍用 Btn"。
  - 新 **Style & class** 一节：声明语法、合并优先级（inline > 右 class > 左 class、按名原子）、不适用即忽略、模板调用行为、commons 共享/命名空间、禁用属性名单。
  - 主表 `<Frame>` 行与 `<Style>` 新行；Color Tokens 一节的 Frame 示例转正说明。
- C# skill 不更新：`StyleDef` / `ProceduralPanel` / `RadiusParser` 均 internal 或不改公共调用面（Frame 新增的 `[UIAttr]` setter 属 XML 面）。

## 11. 里程碑拆分

| | 内容 | 依赖 |
|---|---|---|
| **M1 渲染原语** | §2 全部：RadiusParser、ProceduralPanel、shader、Frame 属性、lint 调整 | 无 |
| **M2 Style/class** | §3–§4 全部：StyleDef、parser、commons、TemplateExpander 合并、StyleRules | 无（与 M1 正交，可并行；先后合流均可） |

两者合流后即为玻璃 spec（M3+）的完整地基。

## 12. 实施记录：与本设计的偏离

三处，都是实施中发现设计判断有误后改的：

### 12.1 参数放材质、形状放顶点（性能反转）

设计 §2.3 说"每 panel 一个材质实例，styled Frame 各占一个 drawcall，顶点通道打包记为扩展位"。实施时反过来了，因为原判断把成本排序搞错了：

- **rect 尺寸 + 局部坐标走顶点**（uv0 = 局部坐标、uv1 = 半尺寸），**其余参数走材质**。于是同一 style 的不同尺寸面板**参数完全相同** → `ProceduralMaterialCache` 按参数元组发同一个材质实例 → 能合批。`class="card"` 用 20 次 = 1 个材质。
- 更关键的是反向收益：颜色 / 圆角 / 描边改动**只换材质、不脏顶点**，不触发 Canvas 重建。而顶点打包方案下每次改参数都要 `SetVerticesDirty` → 整个 canvas mesh 重建 —— 那才是 uGUI 掉帧的头号来源，比 drawcall 严重得多。原设计等于用"重建"换"合批"，方向反了。
- `pill` 与半径 clamp 因此必须留在 shader 里按 `uv1` 现算（提前在 C# 解成数字会让不同尺寸的 pill 拿到不同参数，材质共享失效）。`RadiusSpec.IsPill` 是哨兵而非数值，就是为此。
- 材质释放走引用计数 + spare 栈复用而非 Destroy，让逐帧 tween 颜色也是零分配。
- 代价：`uv1` 需要 Canvas 打开 `AdditionalCanvasShaderChannels.TexCoord1`（panel 在 `OnEnable` / 换父 / canvas 变更时自动开）。

### 12.2 `[RequireComponent(typeof(CanvasRenderer))]` 必须显式写

`Graphic` 自带的 `[RequireComponent]` **不会**随 `AddComponent<T>()` 传递到运行时添加的子类上。缺了它，panel 在 uGUI 第一次 rebuild 时抛 `MissingComponentException`、什么都不画。

值得记下来的是**测试为什么没抓住**：EditMode 从不跑 canvas rebuild，所有断言读的是材质参数和直接调用的 `OnPopulateMesh`，2088 个测试全绿而组件根本画不出东西。是离屏渲染 PNG 肉眼看才暴露的。已补 `CanvasRebuildTests` 里那条强制 `Canvas.ForceUpdateCanvases()` 的回归测试。**结论：涉及 Graphic 子类的改动，EditMode 全绿不构成"能渲染"的证据，必须真渲染一次。**

### 12.3 lint 规则拆成两类而不是"Frame 摘掉 color"

设计 §8 说把 `Frame` 的 `color` 从 `PureContainerVisualAttrRules` 名单摘除。实际拆成两条独立路径更准：

- `Frame`：只报 `sprite`（它确实仍然没有 `Image`）。
- `VStack` / `HStack` / `Grid` / `SafeArea`：`sprite` **加上**全部程序化属性（`color` / `radius` / `borderWidth` / `borderColor` / `glow` / `glowColor`）都报，消息指路"套一层 `<Frame>`"。设计漏了这一半 —— 作者见 Frame 能写 `radius` 后，最自然的下一步就是往 `<VStack>` 上写，而那里会被静默丢弃。

另新增 `StyleRules`（`PUI-CLASS-EMPTY` / `PUI-PROCEDURAL-VALUE`），并在 `IRWalker` 里给 `doc.Styles` 单开一趟遍历 —— `<Style>` 不是 `ElementNode`，树遍历够不着它的属性值。

### 12.4 验证记录

- EditMode 2089 / EditorOnly 308 / PlayMode 171 全通过。
- 离屏渲染逐像素对比 `<Image>` 基准：`#ffffff/0.08`、`#06d6a0/0.25`、纯色、双色渐变（含"第一段在顶部"的方向）全部一致（渐变行完全相同，半透明行最大偏差 1/255）。曾疑似的"半透明偏亮"是 Linear 色彩空间的正常表现，`<Image>` 同色同结果。
- 几何探针：radius 24 的角被切掉、直边中点实心；`borderWidth="3"` 在 rect 左边缘向**内**恰好 3px；`pill` 左尖端实心而左上角被切；`radius="500"` clamp 到短边一半、行为等同 pill。
