# 渐变色标位置 `A 70%, B` —— 让上下渐变的转换点可以挪

> 状态：**已实现**（一轮做完）。2026-08-30 与作者对齐走「路径 A」（只在程序化表面实现，顶点路径由 lint 报错）。
> 相关：`2026-06-13-gradient-color-design.md`（逗号双色语法的出处 —— 本文是它 §10 留的「多色标 / 方向」扩展位里的第一项）、
> `2026-08-23-procedural-style-design.md`（`_FillTop` / `_FillBottom` 与材质缓存 key 的出处）、
> `2026-08-26-procedural-surface-design.md`（九个控件共享 `ProceduralSurface`，以及「什么算声明了程序化模式」）、
> `2026-08-27-decor-primitives-design.md`（`<Decor>` 自带的第三份 fill shader）。

## 1. 问题

今天的渐变写法只有两端颜色，转换位置写死在 0%→100%：

```xml
<Frame color="primary-darker/0.45, complement-lighter/0.45" glass="true" radius="…"/>
```

结果是整块面板从上到下匀速插值 —— 顶部只有一线蓝、中段就已经混成灰紫、下半张全是金。作者要的是
**大面积保持蓝、只在底部收一点金边**。这不是调颜色能解决的：任何一对端点色，只要转换是全高线性的，
中段必然是两色的等量混合。缺的是**色标位置**。

绕不过去的现有手段：

- **叠两层 `<Frame>`**（上蓝下渐变）：多一个 draw call，两层的圆角 / `cut` 轮廓对不齐，玻璃折射要采样两次。
- **把底色调深、指望视觉上「像」**：改的是端点色，中段混合比例分毫未动。
- **贴一张渐变图**：丢掉主题换色的能力（`2026-08-23-procedural-style-design.md` §1 同一条论证）。

CSS 早就把这件事定死了 —— `linear-gradient(blue 70%, gold)`。本文把它搬进色值语法。

## 2. 否决的方案

| 写法 | 否决理由 |
|---|---|
| 新属性 `gradientStops="0.7,1"` | 违反 gradient spec §1「不加属性」。`color` / `hoverColor` / `frameColor` / `popupColor` / `bgColor` / `fillColor` … 每个都得配一份平行属性，且 Variant 覆写要成对写 |
| `A@0.7,B` | 更短，但 `@` 后面是 0..1 还是百分比会二义；且本仓已经用 `%` 表达比例（`SizeSpec`、`<Decor extent>`），再引入第二套写法 |
| ~~CSS color-hint `A,70%,B`（中点提示）~~ | ~~只有一个数，但语义是「50% 混合点在这里」，要非线性指数曲线；且和现有「渐变恰好两段」的报错路径正面冲突，三段值到底是三色还是色标提示无法静态区分~~ **这条否决是错的，已在 §14 推翻并实现**：裸 `70%` 段不带颜色，而颜色的三种写法（token `[a-z0-9-]` / hex `#…` / CSS 命名色）都不可能以 `%` 结尾，所以中间段是不是提示零歧义；而且它恰好是本文没解决的那个视觉问题的答案 |
| `linear-gradient(A 70%, B)` 全写法 | 语义最通用但太长，且逗号语法已经确立，包一层函数是纯噪音 |

## 3. 语法

**段内语法从 `色[/alpha]` 扩到 `色[/alpha][ 空格 位置%]`。位置从顶边量起** —— 0% = 顶边、100% = 底边，
与「第一段是顶部色」的既有约定同向。

```xml
<!-- 本文的目标案例：蓝色一路铺到 70%，最后 30% 收成金色 -->
<Frame color="primary-darker/0.45 70%, complement-lighter/0.45"/>

<!-- 两端都显式写 -->
<Btn color="primary 30%, complement 60%"/>   <!-- 上 30% 纯蓝、下 40% 纯金、中间 30% 过渡 -->

<!-- 定义处也能带，渐变 token 自带形状 -->
<Theme name="default">
  <Color name="panel-grad" value="#4a6fa5 70%,#c9a227"/>
</Theme>
```

省略即今天的行为：第一段默认 `0%`、第二段默认 `100%`。

### 3.1 语义

设 `s` 为「距顶边的归一化距离」（0 = 顶、1 = 底），色标位置为 `a`（第一段）、`b`（第二段）：

- `s ≤ a` → 纯第一色
- `a < s < b` → 线性插值
- `s ≥ b` → 纯第二色

`a == b` 是**合法**的硬边（`A 50%, B 50%` = 上下两段纯色对切，一个属性做出双色块）。
`a > b` 是作者笔误，**报错**而不是像 CSS 那样静默钳制 —— 本仓一贯选报错（gradient spec §6）。

### 3.2 解析顺序与错误

段内先按**空白**切（位置永远在最后，且不含 `/`），再对头部走既有的 `/alpha` 拆分。
token 是 `[a-z0-9-]`、hex 是 `#…`、CSS 命名色是纯字母 —— 三者都不含空白和 `%`，切分零歧义。

| 输入 | 行为 |
|---|---|
| `blue 70%`（值只有一段，无逗号） | error：色标位置需要双色渐变 —— 单色没有可挪的转换点 |
| `blue 70`（缺 `%`） | error：色标位置必须是百分比（如 `70%`） |
| `blue 120%` / `blue -5%` | error：色标位置必须在 0%..100% |
| `blue 70% 80%`（段内 >2 个空白分段） | error：一段最多一个色标位置 |
| `A 70%, B 30%`（位置倒挂） | error：第二个色标位置不能小于第一个 |
| 定义处 `<Color value="#fff 70%,#000">` | 合法。定义处「字面量 only」的规则不变，位置是形状不是颜色，照收 |
| 引用渐变 token 再加 `/alpha`（`panel-grad/0.5`） | 合法，位置原样保留，只换两端 alpha |

## 4. 值模型

`Runtime/Application/ColorSpec.cs` 加两个归一化 float（作者写 `%`，内部一律 0..1，与 shader 同单位）：

```csharp
internal readonly struct ColorSpec
{
    public readonly Color Top;
    public readonly Color Bottom;
    public readonly float TopStop;      // 距顶边，默认 0
    public readonly float BottomStop;   // 距顶边，默认 1
    public readonly bool IsGradient;

    /// 位置被挪过 —— 顶点路径无法表达的那种渐变（§5）。
    public bool HasStops => IsGradient && (TopStop != 0f || BottomStop != 1f);
}
```

- `Solid(c)` → 位置固定 0/1（无意义，但保持结构体全字段确定）。
- `Gradient(top, bottom)` 保留，等价于 `Gradient(top, bottom, 0f, 1f)`；新增四参重载。
- `Multiply(m)` **原样保留位置** —— 状态乘数只动颜色，不动形状。

## 5. 三条渲染路径，只有一条做得到

| 路径 | 实现 | 色标位置 |
|---|---|---|
| 程序化表面 | 三个 shader 各一行 `lerp(_FillBottom, _FillTop, t)` | ✅ 逐像素，精确 |
| 精灵图 Graphic | `GradientTint : BaseMeshEffect` 逐**顶点**染色 | ❌ 表达不了 |
| `<Text>` | TMP 原生 `VertexGradient`，逐字符 4 顶点 | ❌ 表达不了 |

**为什么顶点路径做不到。** `GradientTint.ModifyMesh` 把颜色写进顶点，硬件在顶点之间线性插值。
Simple 图只有四个角顶点（`s=0` 和 `s=1` 两排），把 §3.1 的分段函数在这两排上求值，得到的就是
`f(0)=Top` 和 `f(1)=Bottom` —— 与今天的全幅渐变逐位相同，位置信息在光栅化那一步被抹平。
Sliced 图在九宫格边界上多几排顶点，会得到一个**取决于 9-slice 边框宽度**的分段线性近似 ——
比错还糟，因为它看起来像生效了。`<Text>` 的 `VertexGradient` 是每个字形一个四顶点 quad，同理。

要在顶点路径上做对，必须**按两条水平线切三角形**（`GetUIVertexStream` → 裁剪 → `AddUIVertexTriangleStream`，
约 60–80 行 Sutherland–Hodgman，位置 / UV / 顶点色 / 切线全要插值）。本轮**不做**：目标场景是程序化面板，
而这段几何手术是一块独立的、可以后补的实现，不影响本文确立的语法（§12）。

**因此本轮的契约是：色标位置只在程序化表面上生效，其余路径退回全幅渐变，并由 lint 报错告知作者。**

## 6. 报错渠道

沿用本仓既定的双轨（CLAUDE.md「Unity 跑时是 `Debug.LogWarning`，CLI 升级为非零 exit code」），
规则实现只有一份，放 `Runtime/Core/Lint/`：

### 6.1 CLI lint（硬错误）

新文件 `Runtime/Core/Lint/GradientStopRules.cs`，新错误码 **`PUI-GRADIENT-STOP-NO-SURFACE`**：

> `<Image id='icon'>: color="a 70%,b" 的色标位置在精灵图上不生效 —— 顶点渐变没有可以放置色标的几何。
> 去掉位置（退回全幅渐变），或改用程序化表面（`<Frame>`，或在本控件上加 radius/glass/border 之一）。`

覆盖面（**只报能证明的**，宁可漏报也不误报 —— `ProceduralSurfaceRules` 的既定取向）：

| 分类 | 节点 / 属性 | 判定 |
|---|---|---|
| 恒为顶点路径 | `<Image>` / `<Icon>` / `<RawImage>` 的 `color` | 无条件报 |
| 恒为 TMP 路径 | `<Text>` 的 `color`；各控件的 `textColor` / `itemTextColor` | 无条件报（附一句「`<Text>` 渐变是逐字符的，色标位置无处安放」） |
| 主表面未声明程序化 | 九个 `SurfaceTags` 控件的 `color`（`<Progress>` 是 `bgColor`）+ 绝对状态色 `hoverColor` / `pressedColor` / `selectedColor` / `disabledColor` | 复用 `ProceduralSurfaceRules.DeclaresProcedural`（`ProceduralAttrNames.NeedsPanel` 在本节点上有基态声明）→ 未声明才报 |
| 内层表面未声明形状 | `<Slider>` `fillColor`←`fillRadius`、`handleColor`←`handleRadius`；`<Progress>` `fillColor`←`fillRadius`、`frameColor`←`frameRadius` | 配对的 `<layer>Radius` 未声明才报 |
| 没有表面可言的内层 | `<Toggle>` `checkmarkColor`；`<TabMenu>` `arrowColor`；`<Dropdown>` `popupColor` / `itemColor` / `arrowColor` / `checkmarkColor` / `scrollbarColor` / `scrollbarHandleColor`；`<ScrollList>` `frameColor` / `scrollbarColor` / `scrollbarHandleColor` | 无条件报 |
| 弃权 | `class=` 无法解析（`StyleAttributeView.IsUncertain`）；仅 Variant 声明程序化（`radius.mobile`） | 不报 —— 与 `ProceduralSurfaceRules` 同样的取舍 |

一律走 `StyleAttributeView`：程序化属性由 `<Style>` / `class=` 携带的场景至少和内联一样常见，
看不见 style 的规则会把正确的 XML 报成坏的。

位置语法本身写坏（§3.2 的前四行）由 `ColorLiteralRules` 复用既有的 **`PUI-COLOR-GRADIENT-MALFORMED`**
报出 —— 位置是结构问题，与段内是 token 还是 hex 无关，因此对 bare word 也能查。

### 6.2 运行时（一次性 warning）

两个**判定确定、且每个 apply pass 只走一次**的位置，没有误报空间：

- `ProceduralSurface.Reconcile()`：`!on && _hasFill && spec.HasStops` → `Debug.LogWarning`。
  这里是唯一知道「这一趟到底走没走程序化」的地方。
- `<Image>` / `<Icon>` / `<RawImage>` / `<Text>` 这些**没有** `ProceduralSurface` 的控件：在
  `ColorApplier.Apply` / `LabelColorApplier.Apply` 里直接判 `HasStops` → warning。

**不能在 `ColorApplier.Apply` 里对九个控件抛异常。** `Btn.Color` 的 setter 同时喂
`ColorApplier.Apply(_bg, spec)` 和 `Surface.SetFill(spec)`，而此刻 `Reconcile()` 还没跑、
程序化模式尚未确定 —— 在那里抛会把正确的程序化按钮判死。这是把运行时诊断放在
`Reconcile()` 而不是 setter 的全部理由。

## 7. 实现改动面

1. **`Runtime/Core/Parser/ColorParser.cs`**（纯 C#，CLI 共享）：新增
   `TrySplitStop(string seg, out string baseValue, out float? stop, out string error)`。
   `TrySplitGradient` 不动（逗号仍然先切）。
2. **`Runtime/Application/ColorSpec.cs`**：两个 float + `HasStops` + 四参 `Gradient` 重载（§4）。
3. **`Runtime/Application/UI.cs`**：
   - `ResolveSpec` / `ResolveSingle`：每段先剥位置再剥 `/alpha`；两段的位置合成进 `ColorSpec`；
     单段值带位置 → throw（§3.2 第一行）。
   - `ParseThemeColor`：定义处同样剥位置。
   - `Resolve`（单色签名）不动 —— 它已经在遇渐变时 throw。
4. **`Runtime/Core/Parser/UIDocumentParser.cs:182`**：`<Color value>` 校验前剥位置。
5. **`Runtime/Core/Lint/ColorLiteralRules.cs`**：`CheckSegment` 剥位置，坏位置报
   `PUI-COLOR-GRADIENT-MALFORMED`。
6. **`Runtime/Core/Lint/GradientStopRules.cs`**（新）+ `IRWalker` 挂点（§6.1）。
   `ProceduralSurfaceRules.DeclaresProcedural` 从 `private` 提到 `internal static` 供复用。
7. **三个 shader** 各加一个 `_FillStops` 属性 + 一行调用，数学落在 `UI-PanelSDF.cginc` 的
   **一个共享函数**里 —— 三处原本就是逐字相同的 `lerp`，而色标位置必须三处一致（否则同一份
   `color=` 在不透明面板、玻璃面板和 `<Decor>` 上渐变到不同的地方）。这正是
   `PuguiApplyOuterGlow` / `PuguiApplyInnerGlow` 当初共享的同一条论证：

   ```hlsl
   float4 PuguiFillRamp(float2 p, float2 b, float4 top, float4 bottom, float2 stops)
   {
       float s = saturate((b.y - p.y) / max(2.0 * b.y, 1e-4));            // 0 = 顶边
       float u = saturate((s - stops.x) / max(stops.y - stops.x, 1e-4));
       return lerp(top, bottom, u);
   }
   ```

   默认 `(0,1)` 时 `u = s = 1-t`，`lerp(Top,Bottom,1-t) ≡ lerp(Bottom,Top,t)` —— 与今天逐位相同，
   零回归。`a == b` 的硬边靠 `1e-4` 的下限退化成一个像素以内的跳变（无 AA，可接受；真要抗锯齿
   等有需求再说）。三处调用：`UI-GlassPanel.shader`、`UI-ProceduralPanel.shader`、`UI-Decor.shader`。
8. **材质缓存 key**：`ProceduralMaterialCache.PanelParams` 与 `DecorMaterialCache` 各加两个 float
   （ctor / `Equals` / `GetHashCode` / `Configure`）。默认值不变 ⇒ 现有面板不会多占一份材质。
9. **`SetFill` 签名收窄**：`ProceduralSurface` / `ProceduralPanel` / `DecorPanel` 的
   `SetFill(Color, Color)` → `SetFill(in ColorSpec)`。顺手把散在 15 个控件里的
   `Surface.SetFill(spec.Top, spec.Bottom)` 收成 `Surface.SetFill(spec)` —— 否则每个控件都要
   再补两个参数，位置的传递面比颜色还宽。
   `StateTintReactor.cs:242`（`_panel.SetFill(basis.Top, basis.Bottom)`）同改，状态渐变因此免费带位置。
10. **`GradientTint` / `Text`**：只接位置用于 §6.2 的判定，渲染仍走全幅（`_top`/`_bottom` 不变）。

## 8. ReSolve / Variant / 主题

位置是**值的一部分**，因此全部免费：

- `color.mobile="A 40%,B"` 走同一个 setter。
- 主题切换后 `ReSolve` 重放 → 新的 `ColorSpec` 带新位置 → `SetFill` 幂等。
- 渐变 token 携带位置（§3 定义处），换主题即可换转换点 —— 皮肤能改的东西又多一样。
- `Multiply` 保位置 ⇒ 纯色 `*Modulate` 压在带位置的渐变基色上，形状不动、只变暗。

## 9. 测试

EditMode（`UI.ResetForTests` 约定）：

1. **`ColorParser.TrySplitStop`**：`"blue 70%"` / `"#fff/0.5 30%"` / 无位置 / §3.2 全部错误用例。
2. **`ResolveSpec`**：两段各带位置 / 只第一段带 / token 携带位置 / `token/alpha` 保位置 /
   单段带位置 throw / 位置倒挂 throw。
3. **`<Color value>` 定义处**：带位置注册成功、坏位置 `ParseException`。
4. **`ColorSpec`**：`HasStops` 真值表；`Multiply` 保位置；`Solid` 位置为 0/1。
5. **`PanelParams`**：位置不同 → 不同 key（`Equals` / `GetHashCode`）；位置默认 → 与今天同一个 key
   （防止老工程凭空多一批材质）。
6. **Lint `GradientStopRules`**：§6.1 表格逐行 —— `<Image>` 报、`<Frame>` 不报、
   `<Btn radius="8">` 不报 / 裸 `<Btn>` 报、`class=` 携带 `radius` 时不报、`IsUncertain` 弃权、
   `<Slider fillColor="a 70%,b">` 无 `fillRadius` 报 / 有则不报。
7. **`ColorLiteralRules`**：坏位置 → `PUI-COLOR-GRADIENT-MALFORMED`。
8. **运行时 warning**：`LogAssert.Expect` 断言 `<Image color="a 70%,b">` 出 warning；
   `<Frame color="a 70%,b">` 不出。

PlayMode：一条 `<Frame glass="true" color="A 70%,B">` 的渲染冒烟 —— 断言材质上
`_FillStops == (0.7, 1, 0, 0)`（像素级正确性靠 `.lint/PxlPreview` 之外的肉眼验收，见 §11）。

## 10. SKILL 更新（同 PR，英文）

- `authoring-promptugui-xml/SKILL.md` → **Gradients** 一节：位置语法、从顶边量、默认 0%/100%、
  硬边写法、§3.2 错误表，以及**「位置只在程序化表面生效」**这条契约 + 两个新错误码。
- `authoring-promptugui-xml/reference/states.md`：绝对状态色带位置的可用条件（同主表面判定）。
- `reference/glass.md` / `reference/decor.md`：填充渐变行补一句位置可写。
- C# skill 不更新：`ColorSpec` / `ResolveSpec` 均 internal，公共 API 面零变化。

## 11. 验收

作者的原始场景改成一行，肉眼确认蓝色占据上 70%、金色只在底部收边：

```xml
<Frame color="primary-darker/0.45 70%, complement-lighter/0.45" .../>
```

## 12. 不做的事（YAGNI 记录）

- ~~**顶点路径的三角形切分**（§5 那 60–80 行）~~ —— **已补**（2026-09-01，见
  `2026-09-01-vertex-gradient-stops-design.md`）。触发它的正是本文预料的那类需求：倒影要
  「上实下透、到一半消失」。改动面与这里写的一致 —— 语法一个字没动，`GradientStopRules`
  收窄到只剩 TMP，运行期 warning 删掉四处。两件本文没预料到的：透明端顺手做成了几何剔除，
  以及 `flip` 与渐变的执行顺序必须钉死（否则属性书写顺序会决定倒影朝哪边）。
- **≥3 色标**（`A 0%,B 40%,C 100%`）—— 逗号天然可扩段数，shader 要多两组 uniform，等有需求。
- **方向 / 角度**（横向、45°）—— gradient spec §10 留的另一个扩展位，与本文正交。
- **色标之间的非线性缓动**（CSS color-hint 那种）—— 两个位置已经覆盖本文的目标场景。
- **硬边的抗锯齿** —— `a == b` 时那一个像素的跳变，等有人抱怨再说。

## 13. 实施记录（2026-08-30）

- **测试**：EditMode 3010/3010、EditorOnly 310/310、PlayMode 178/178 全绿。既有的
  `DecorRenderTests` / `ProceduralSurfaceRenderTests` 是真渲染 + 逐像素探针，因此它们的通过
  同时证明了三个 shader 编译成功、默认色标零回归。
- **`ProceduralSurfaceRules.DeclaresProcedural` 没有复用**（§7.6 的原计划）。两条规则的偏向相反：
  那边「没声明 → 不报」，这边「没声明 → 报」，所以宽松的答案在两边分别是不同的实现。
  `GradientStopRules.DeclaresSurface` 因此用 `styles.Declares`（含 variant 覆写）而不是只看基态 ——
  `radius.mobile` 在那个 variant 里确实能画出色标，报错就是误报。
- **`SetFill` 收窄成 `SetFill(in ColorSpec)`**，`ProceduralSurface` / `Decor` 内部的
  `_fillTop`/`_fillBottom` 双字段一并折成一个 `ColorSpec`，15 个控件的调用点各省两个参数。
- **顺带修文档**：`SKILL.md` 把 `<Progress>` 的渐变属性写成了 `color`，实际是 `fillColor`
  （Progress 没有 `color` 属性）。

## 14. 追加：色标提示（colour hint）——2026-08-30 同日

**问题**：`A 70%, B` 上线后作者反馈「渐变非常突兀，分界线很明确」。不是 bug ——
色标是**切** ramp：70% 那一行斜率从 0 突然跳到满值，颜色连续但**导数**不连续，
而人眼对导数断点极其敏感（马赫带），于是看见一条实际不存在的分界线。CSS 的
`linear-gradient(A 70%, B)` 有完全相同的性质。离线把四种 ramp 用作者的两端色渲染出来
对比确认了这一点（`0%,100%` / `70%,100%` / `30%,100%` / hint 70%）——第二格逐像素复现了
作者的截图。

**作者真正要的**是「整段都在过渡，但偏向底部」，CSS 里那是另一个原语：**colour hint**。

```xml
<Frame color="primary, 70%, complement"/>      <!-- 50% 混合点落在 70% 处，全程无拐点 -->
<Frame color="primary 20%, 60%, complement"/>  <!-- 与色标可组合；提示在色标的坐标系里 -->
```

语义与实现：

- 中间一段是**裸百分比**（不带颜色）。零歧义 —— 见 §2 的更正。
- 解 `t^E = 0.5` 得指数 `E = log(0.5) / log(t)`，`t` 是提示在 **ramp 内部**的归一化位置
  `(h - a) / (b - a)`。提示落在两色标正中 → `E = 1`，退化成线性，与 CSS 一致。
- 两端退化（提示压在某个色标上）= 瞬时翻转，指数为 0 或 ∞。把 `t` 夹进开区间内侧
  （±1e-3）让指数有限，画面上就是硬边，同一张图。
- 指数在 C# 侧（`ColorParser.StopCurveExponent`，纯 C#，CLI 共享）算好，随
  `_FillStops.z` 进材质 —— 那个 `float4` 本来就空着两个槽，零新增 uniform。
  shader 里 `if (stops.z != 1.0) u = pow(u, stops.z);`，uniform 分支，
  既有面板逐位不变。
- 提示与色标一样进 `PanelParams` / `DecorParams` 的 key，一样由
  `PUI-GRADIENT-STOP-NO-SURFACE` 管顶点路径 —— `ColorSpec.HasStops` 的判据加上
  `Curve != 1`。

顺带把 `TrySplitGradient` 的 out 参数收进 `ColorParser.GradientParts` 结构体：
第三次加参数时六个 out 已经读不清了，而这四个数（两个色标 + 提示 + 派生指数）本来就
必须一起看。

**错误**（全部 parse-time / resolve-time）：提示不夹在两色之间、提示落在两色标之外、
四段以上。三色 `A,B,C` 的旧报错文案加上「可选一个提示百分比」。

**测试**：EditMode 3030/3030。
