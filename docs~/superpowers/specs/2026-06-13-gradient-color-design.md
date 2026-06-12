# 渐变色支持（颜色值语法扩展，非新属性）

**日期**：2026-06-13
**状态**：设计阶段（待 review，未进入实施）
**作用域**：颜色**值语法**新增"逗号分隔双色 = 上下渐变"；不新增任何 XML 属性。所有"给某个 Graphic 上色"的属性（`color` / `frameColor` / `popupColor` / `progColor` / `bgColor` / `hoverColor` / `pressedColor` / `selectedColor` / `disabledColor` …）均接受渐变值。**保持纯色 only**（收到渐变值报错）：`*Modulate`、富文本 char-color、以及天生不是 Graphic 着色的属性——`InputField` 的 `textColor` / `placeholderColor` / `caretColor` / `selectionColor`（caret/selection 是 TMP 的 Color 字段，物理上无顶点可染；可编辑文本渐变无意义）、`Carousel` 的 `dotColor` / `dotSelectedColor`（几像素的圆点，CarouselView 内部以 Color 字段管理）。注：`<Frame>` 是纯容器没有 color 属性，不在范围内。
**关联**：色值解析 chokepoint 沿用 `/alpha` 后缀的设计（[`UI.Theme.Resolve`](../../Runtime/Application/UI.cs)，PR #51）；状态色叠乘公式 `(abs ?? color) × (mod ?? white)` 来自 PR #45。颜色值语法变化 → `authoring-promptugui-xml` SKILL 必须更新（见 §9）。

---

## 1. 背景与目标

主要需求是 Icon / 文字的渐变着色（标题金字、图标高光），未来扩展到 Frame / Image 大块背景的上下渐变。设计约束：

1. **不加属性**。控件颜色属性已经很多（`hoverColor` / `pressedColor` / `*Modulate` …），渐变作为一种**颜色值写法**进入系统，所有现有属性、Variant 覆写、theme token 自动获得能力。
2. 像素风游戏中渐变只用于 UI 小面积装饰，**平滑渐变即可**，不做色阶（banding）、不写 shader。
3. v1 只做**纵向（上→下）双色**。方向 / 多色标留扩展位，不实现。

## 2. 语法

**颜色值含逗号 → 按逗号切成两段，每段独立走现有 token + `/alpha` 解析；第一段为顶部色，第二段为底部色。**

```xml
<!-- theme token 定义命名渐变（推荐主用法） -->
<Color name="gold-grad" value="#ffe08a,#b8860b"/>

<!-- 引用：和普通色 token 无区别 -->
<Text color="gold-grad">标题</Text>

<!-- 内联、token 混用、alpha 后缀均合法 -->
<Icon name="coin" color="white,#aaa"/>
<Image color="accent,accent-dark/0.5"/>
```

注意区分**定义处**与**引用处**：theme token 定义（`<Color value>`）维持今天的"字面量 only"规则——每段只能是 hex / CSS 命名色，不可引用其他 token、不可带 `/alpha`（要半透明用 `#RRGGBBAA`）。token 引用、`/alpha` 后缀、token 混 hex 只发生在属性引用处（`ResolveSpec`）。引用一个渐变 token 再加 `/alpha`（`gold-grad/0.5`）合法：alpha 同时替换两端的 a。

语法选型记录：`A>B`（`>` 在 XML 属性里易被转义成 `&gt;`，且不常见）、`A-B`（与 kebab-case token 名如 `accent-dark` 冲突）、CSS `linear-gradient(A, B)`（语义最通用但太长）均被否决。逗号零歧义（token 名 / hex 中不可能出现）、最短、且正是 CSS 渐变色标的分隔符，对 LLM 作者直觉；未来扩三色 `A,B,C` 顺理成章。

### 2.1 解析错误（全部 parse-time）

| 输入 | 行为 |
|---|---|
| 逗号切出 >2 段 | error：目前只支持双色上下渐变 |
| 任一段解析失败（坏 hex / 未知 token） | 现有单色错误路径，报错指明是哪一段 |
| 段引用的 token 本身是渐变（`gold-grad,black`） | error：渐变不可嵌套 |
| 渐变值写在 `*Modulate` / char-color 上 | error：见 §6 |
| 空段（`a,` / `,b` / `,`） | error：渐变两段均不可为空 |

## 3. 值模型

```csharp
// Runtime/Application/ColorSpec.cs。不能放 Core/Parser/：UIXmlLint CLI 把
// Core/Parser/** 整个编译进纯 .NET 可执行文件，UnityEngine.Color 进不去。
// 逗号切分的纯字符串逻辑（TrySplitGradient）放 ColorParser，lint 共享。
internal readonly struct ColorSpec
{
    public readonly Color Top;
    public readonly Color Bottom;   // 纯色时 == Top
    public readonly bool IsGradient;
}
```

- `UI.Theme` 新增 **internal** `ResolveSpec(string) → ColorSpec`：含逗号走渐变路径，否则等价于现有 `Resolve` 包成纯色 `ColorSpec`。不进公共 API 面（控件同程序集直接调）。
- 现有 `public Color Resolve(string)` 保留，签名不变；输入含逗号时 throw "this attribute does not support gradients"——不支持渐变的属性（`*Modulate` 等）继续调它，错误信息自动正确。
- **ThemeStore**：token 条目从 `Color` 拓宽为 `ColorSpec`，`LookupChained` 返回 `ColorSpec?`。渐变段引用 token 时只接受纯色结果（否则 §2.1 嵌套错误）。
- 现有 soft-fail 行为保持：theme 未注册时 token 解析回退 `white`（纯色 ColorSpec），Theme.Changed 后 ReSolve 重算。

## 4. 应用机制

### 4.1 统一 helper（一档属性的"免费"来源）

```csharp
// Runtime/Controls/Internal/ColorApplier.cs
internal static void Apply(Graphic g, ColorSpec spec)
```

- 纯色：`g.color = spec.Top`；若挂过 `GradientTint` 则 `enabled = false`。
- 渐变：`g.color = Color.white`（把 Graphic.color 槽让给乘数，见 §5）；lazy-add `GradientTint`，写入 Top/Bottom，`enabled = true`。
- **绝不 Destroy**（Variant / ReSolve 往返只 toggle，遵循 `ApplyViewportMask` 先例）。

所有"直接给某个 Graphic 上色"的 setter（`Image.color`、`Icon.color`、`Frame.color`、`frameColor`、`popupColor`、`progColor`、`bgColor` …）改为调 helper，渐变全线生效，每属性边际成本≈一行。

### 4.2 GradientTint（Image / Icon / Frame 及一切非文本 Graphic）

`GradientTint : BaseMeshEffect`（`Runtime/Controls/Internal/`）：

- `ModifyMesh` 先扫一遍顶点求 Y 范围（Sliced / Tiled 顶点数 >4，不能假设顺序），再按 `(y - minY) / (maxY - minY)` 归一化插值 `Lerp(Bottom, Top, t)`，**乘进**现有顶点色（`v.color *=`）。
- 乘法保证最终合成为 `纹理 × Graphic.color × 顶点渐变`——渐变占"基础色"槽，`Graphic.color` 留给状态乘数，两者天然正交（见 §5）。
- Top/Bottom 变更时 `SetVerticesDirty()`。
- 仅在 mesh rebuild 时执行，无逐帧成本。

### 4.3 Text（TMP 原生）

不走 mesh effect，直接用 TMP 自带能力：

- 渐变：`enableVertexGradient = true`，`colorGradient = new VertexGradient(Top, Top, Bottom, Bottom)`，`color = Color.white`。
- 纯色回退：`enableVertexGradient = false`，`color = spec.Top`。
- **语义注意**：TMP 的 VertexGradient 是**逐字符**渐变——每个字各自上 Top 下 Bottom（标题金字正是要这个效果），不是整段文本块从上到下。写进 SKILL。

Markdown / Toast / 模态等复用 Text / Image 控件的地方自动继承，无额外工作。

## 5. 状态色组合（二档：类型拓宽）

`StateColorSet`（`Runtime/Controls/Internal/StateColorSet.cs`）四个槽从 `Color?` 拓宽为 `ColorSpec?`。**注意它被 `StateTintReactor` 两用**：一份装绝对基色（abs），一份装乘数（mod）——`Resolve` 需要区分两种模式：abs 集合走 `Theme.ResolveSpec`（接受渐变），mod 集合走旧 `Theme.Resolve`（遇逗号即按 §6 报错），例如 `Resolve(..., allowGradient:)` 或拆两个工厂方法。`StateTintReactor` 应用规则：

| 槽位 | 现在 | 改后 |
|---|---|---|
| 绝对基色 abs（`hoverColor` 等，仅 targetGraphic） | 写 `Graphic.color` | 走 `ColorApplier.Apply`：纯色照旧；渐变 → 顶点槽 |
| 乘数 mod（`*Modulate`，子树扇出） | 乘进 `Graphic.color` | 不变（拒绝渐变，§6） |

合成公式不变：`最终 = 纹理 × (mod ?? white) × 基色`。实现上 reactor 直接把 mod **预乘**进基色两端（`ColorSpec(Top×m, Bottom×m)`）再交 `ColorApplier.Apply`——纯色路径退化为现状逐位相乘，渐变路径落顶点槽 + `Graphic.color=white`，视觉等价、簿记单一。reactor 的基色捕获改为 `ColorApplier.Peek(graphic)`（GradientTint enabled 时读它，否则读 `graphic.color`），否则作者渐变基色会被捕获成 white。状态切换的颜色 tween 仅在"from、to 均为纯色"时保留，任一端是渐变即 snap（沿用 CrossesTransparency 的 snap 先例）。状态切换时 reactor 调 helper 换槽——纯色基色 + 渐变 hoverColor、渐变基色 + 纯色 hoverColor 的混合往返都由 helper 的 enable/disable 收口。现有"re-Configure 时 `OnState(_source.Current)` 重刷"（47d181b）保证 ReSolve 后渐变状态色不丢。

`selectedSprite` / `pressedSprite` / `disabledSprite`（overrideSprite 换图）与本设计正交，不动。

## 6. 明确不支持（parse error，非静默降级）

| 目标 | 原因 | 错误信息要点 |
|---|---|---|
| `*Modulate`（`hoverModulate` 等） | 乘数的实现就是 `Graphic.color` 槽 + 子树扇出；渐变乘数需双重逐顶点乘法，且"按住时上深下浅变暗"无真实需求 | "Modulate 是乘数，不支持渐变" |
| 富文本 char-color（`<c=>` 段内色） | 需逐字符段写顶点色，TMP 另一套实现 | "char-color 不支持渐变" |

收到渐变值时报错而不是取 Top 色——静默降级会让作者以为写法生效了。

## 7. ReSolve / Variant / 运行时往返

- 渐变是**值**，`color.mobile="a,b"` 等 Variant 覆写走同一 setter，零额外逻辑。
- ReSolve 重放属性 → setter 调 helper → enable/disable 幂等，无 GameObject 重建。
- `GradientTint` 组件一旦创建跟随 Graphic 存活整个 Screen 生命周期（同 Add 块 Strategy C 哲学）。

## 8. 测试

EditMode（沿用 `UI.ResetForTests` 约定）：

1. **解析**：逗号切分；每段 token / hex / `/alpha` 组合；§2.1 全部错误用例；`Resolve`（旧签名）遇逗号 throw。
2. **ThemeStore**：渐变 token 注册 / 链式查找 / 嵌套渐变报错；soft-fail 回退 white。
3. **ColorApplier**：纯色↔渐变往返（enabled toggle、不 Destroy）；`Graphic.color` 槽让位正确。
4. **GradientTint**：Simple 4 顶点上下两色断言；Sliced 多顶点按 Y 归一化连续；与 `Graphic.color` 乘法叠加。
5. **Text**：VertexGradient 赋值 / 纯色回退 `enableVertexGradient=false`。
6. **状态色**：渐变 `hoverColor` 进 hover 换顶点槽、回 Normal 还原；渐变基色 × 纯色 `pressedModulate` 叠乘；ReSolve 后状态渐变不丢（OnState 重刷路径）。
7. **XSD**：颜色属性仍为 string，生成器无 pattern 收紧即可（substring 断言确认无回归）。

PlayMode：一条渐变 Btn hover/pressed 交互冒烟（EventSystem 真实路径，参照 common-controls sample 的教训）。

## 9. SKILL 更新（同 PR，英文）

- `authoring-promptugui-xml/SKILL.md` Color tokens 一节：逗号渐变语法、第一色在顶、token / 内联 / alpha 后缀组合、不支持名单（`*Modulate` / char-color）、TMP 逐字符语义。
- `authoring-promptugui-xml/reference/states.md`：`*Color` 接受渐变、`*Modulate` 不接受。
- C# skill 不更新：`ColorSpec` / `ResolveSpec` 均 internal，公共 API 面零变化。

## 10. 不做的事（YAGNI 记录）

- 方向（横向 / 角度）、≥3 色标、径向渐变——语法已留位（逗号天然可扩段数；方向可加后缀），实现等真实需求。
- 色阶 / dithering shader——像素风顾虑确认过：仅用于 UI 小面积，平滑可接受。
- 渐变 Modulate、char-color 渐变——见 §6。
