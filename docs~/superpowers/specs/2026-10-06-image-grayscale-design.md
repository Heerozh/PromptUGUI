# `<Image>` / `<Icon>` 的 `grayscale` 属性

> 状态：**已定，实现中**（2026-10-06 起草；作者同意按本文推荐实施，分步提交）。分支 `feat/image-grayscale`。
> 需求来源：作者希望 icon / image 能用一个属性控制黑白 —— 锁定的道具、未拥有的角色头像这类**与禁用无关**的场景。
> 相关：
> `2026-06-16-disabled-grayscale-design.md`（禁用态默认置灰；DG-D1 当年否决了 `disabledGray` / `disabledModulate="monochrome"`
> —— 那是"禁用外观"的旋钮，本文是与禁用无关的通用视觉属性，两者不冲突）、
> `2026-09-02-image-fx-blur-glow-design.md`（`FxImage` / `UI/ImageFx`；§4.4 把禁用灰折进 fx 材质）、
> `2026-09-12-intensity-design.md` §5.5（禁用即熄灯）。

## 1. 现状

灰度只有一个入口：`DisabledGrayscaleInstaller`。`<Btn>` / `<Tab>` / `<Toggle>` / `<Collapsible>` 标题栏在作者没写任何
`disabled*` 时装上它，控制器只在 `InteractState.Disabled` 时动手。作者没有任何属性能让一张图变灰。

可 `<Image>` / `<Icon>` 底下的 `FxImage` 早就带着去色开关：`UI/ImageFx` 的 `_Desaturate`，`FxParams.Desaturate` 进材质缓存键。
只是这个开关今天只有禁用控制器（经 `ISelfGrayscale.SetDisabledGrayscale`）能拨。

两个常见误用，文档里要点名：

- `color="gray"` 是合法的 CSS 颜色，但乘色逐通道独立，只会把图压暗，算不出灰度（去色是跨通道的亮度加权）。
- `tint` 只认 `multiply` / `linear`；`tint="gray"` 运行时告警一次后按 `multiply` 画，lint 与 XSD 都不拦。

## 2. 目标与非目标

**目标**

1. `<Image grayscale="true">` / `<Icon grayscale="true">`：整张图（本体 + 光晕）按亮度去色。
2. 与禁用灰互相独立：任一为真即灰；按钮禁用 → 启用不会洗掉作者写的灰。
3. Variant / `<Style>` / Theme / 模板参数照常可用；C# `icon.Grayscale = true` 当帧生效。
4. 写在不支持的标签上、或取值不是 true / false 时，lint 报出来（运行时对未知属性一律静默）。
5. 不写 = 今天的样子：不挂材质、照常合批，零成本。

**非目标**

- 0–1 的部分去色（`grayscale="0.5"`）。
- `<RawImage>` / `<Frame>`（程序化面）/ `<Text>` / 各交互控件自己的 bg。
- 容器级扇出（"整张卡片置灰"）。
- runtime-owned（C# 写过之后 ReSolve 不再回放）。
- 改 `tint` 的取值集合。

## 3. 设计

### 3.1 属性

| 标签 | 属性 | 类型 | 默认 |
|---|---|---|---|
| `<Image>` / `<Icon>` | `grayscale` | bool（`true` / `false`） | `false` |

`[UIAttr] public bool Grayscale { get; set; }`，走 `ControlMeta` 的 `bool.Parse` —— 与 `raycastTarget` 同一条路：大小写不敏感、
首尾空白忽略；`""` / `yes` / `1` 是解析错误（运行时 `ParseException` 带位置，lint `PUI-FX-VALUE` 提前报）。
Variant 回退写基值 `grayscale="false"`；漏写基值的情况 `PUI-VARIANT-NO-BASE` 已经覆盖。

### 3.2 `FxImage`：两个开关

今天的 `_grayed` 拆成两个：

- `_disabledGray`：只由 `ISelfGrayscale.SetDisabledGrayscale` 写（禁用控制器）。
- `_grayscale`：作者开关，`internal bool Grayscale { get; set; }`，setter 走 `MarkDirty`。

合并：`FxParams.Desaturate = _grayscale || _disabledGray`；`HasMaterialFx` 加上 `_grayscale`。

光照：仍是 `_disabledGray ? 1 : _intensity` —— 只有禁用熄灯。intensity spec §5.5 的理由是"禁用的控件要读作熄灭"；作者同时写了
`grayscale` 和 `intensity`，就是两个都要：一个发光的灰图标。

### 3.3 控件与 C#

`Image.Grayscale` / `Icon.Grayscale`：`get` 读作者开关（不含禁用灰），`set` 经 `ImageFxApplier.SetGrayscale` 落到 `FxImage`。

- apply 之外（C# 写）立即 `ImageFxApplier.Flush`：当帧换好材质，`material` 读回来就是新的。
- apply 之内交给 `OnAfterApply` 的那一次 Flush：setter 顺序不定，中途 flush 会为一个中间态白取一份材质。
  （`AsyncSpriteSlot` 用的就是同一个 `InApplyPass` 判断。）
- 节点上的 Image 不是 `FxImage`（来自 prefab）时，与 `blur` 一样告警一次并忽略；`get` 返回 `false`。

**ReSolve。** `grayscale` 是普通属性，不是 runtime-owned：XML（或它吃到的 `<Style>`）声明了它，ReSolve（resize / Variant /
换主题 / 换语言）就把声明值写回去，覆盖 C# 写的值；没声明，ReSolve 就不碰它。文档约定：**由代码驱动，就别在 XML 里声明**。

### 3.4 组合

- 光晕随本体一起灰：shader 对"本体 + 光晕"的合成结果去色，与禁用灰同一处。要"灰图标 + 彩色光晕"，叠两个节点。
- `color=`（含渐变）、状态 `*Modulate`、`tint="linear"`、`intensity`、CanvasGroup alpha：都在去色之前，只影响灰的明暗。
- 禁用灰：独立开关（§3.2）。作者灰不算 `disabled*`，不会关掉按钮的默认禁用灰；`stateReact="false"` 只把节点挡在禁用扇出之外，
  不影响作者灰。
- 合批：同参数的灰图共用一份 `UI/ImageFx` 材质，彼此可合批；与没灰的图分属两份材质。

### 3.5 lint

- **`PUI-FX-TAG`（扩展）**：`grayscale` 写在 `<Image>` / `<Icon>` 以外的**内置**标签上。按标签给提示：
  `<RawImage>`（它的材质槽给了 `tint=`，没接入）、`<Text>`（TMP 用自己的 shader，换一个灰色的 `color`）、
  `<Btn>` / `<Tab>` / `<Toggle>` / `<Collapsible>`（禁用态本来就整控件置灰；要灰里面的图，写在那个 `<Image>` / `<Icon>` 上）。
- **模板调用不判**（`BuiltinTags.IsBuiltin` 门，同 `RaycastRules`）：调用点上的同名属性只可能是 `<Param>`
  （`TemplateExpander` 对其余属性直接抛错），展开遍会在真实节点上原地判。
  **顺带修正**：`blur` 原来没有这道门，模板参数恰好叫 `blur` 时，raw 遍会误报 `PUI-FX-TAG`。
- **`PUI-FX-VALUE`（新增）**：`<Image>` / `<Icon>` 上 `grayscale` 的基值或任一变体值不被 `bool.TryParse` 接受
  （与运行时的 `bool.Parse` 同一实现）；`{{…}}` 不判。CLI-only —— 运行时本来就是硬错。
- 运行时不加告警：`blur` 的 `PUI-FX-TAG` 也是 CLI-only，未知属性在运行时一律静默（`ControlAttributeApplier`）。

### 3.6 XSD

`<Image>` / `<Icon>` 在 `XsdGenerator` 里是手写列表，各加 `("grayscale", "xs:boolean")`，同 `raycastTarget`。

### 3.7 文档

- `authoring-promptugui-xml/SKILL.md`：Image / Icon 属性表各加一行；`#### Blur & glow` 之后新增 `#### Grayscale`；
  `## Tint blend modes` 补一句：`tint` 不管去色，`color="gray"` 也不是黑白。
- `reference/states.md`：默认禁用灰一节注明作者灰与之独立。
- `scripting-promptugui-csharp/SKILL.md`：`Image.Grayscale` 小节（含 ReSolve 约定）。
- 顺带修两处文档错误：XML skill 的 tint 示例 `<Image src=…>`（`<Image>` 没有 `src`，应为 `sprite=`）；C# skill 的
  `coin.Glow = v`（`Glow` 是 string 属性，float 赋不进去，编译不过）。

## 4. 决策

- **GS-D1 独立属性，而非 `tint="gray"`。** `tint` 是"颜色怎样与 sprite 混合"，灰度是混合之后的滤镜，两者正交：shader 里本来就是
  `_TintLinear` 与 `_Desaturate` 两个独立参数，一张 `tint="linear"` 重新上色的图也可能要灰（锁定态）。`tint` 还挂在 13 个控件上、
  多数只作用于 bg，`tint="gray"` 写在 `<Btn>` 上就成了"背景灰了、字和图标没灰"。
- **GS-D2 两个开关，不复用 `_grayed`。** 复用的话，按钮里一个 `grayscale="true"` 的图标会在按钮禁用 → 启用时被
  `SetDisabledGrayscale(false)` 洗掉。
- **GS-D3 作者灰不熄灯。** 见 §3.2。
- **GS-D4 严格 bool，不收空串。** 对话里先推荐过"空串算 false"（照 `intensity=""` 的回退约定），落实时改了：`intensity` 收空串是因为
  它的"关"没有自然的写法，bool 的回退值就是 `false`；要收空串就得把属性做成 string，C# 端成了 `icon.Grayscale = "true"`；模板参数
  默认值写 `default="false"` 即可。lint 对 `grayscale=""` 给出明确提示。
- **GS-D5 不做 runtime-owned。** 每个控件只有一个 `RuntimeStateAttr` 槽（`<Icon>` 的是 `name`），要做就得把它扩成集合，牵动
  `ControlAttributeApplier` 的锁判定；先以文档约定解决。
- **GS-D6 只 `<Image>` / `<Icon>`。** `<RawImage>` 不是 `FxImage`，只能换材质，会和 `tint="linear"` 抢同一个槽；`<Frame>` 的
  `ProceduralPanel.SetDisabledGrayscale` 里还带着禁用专属的行为（玻璃变薄、雾气冻结），作者灰要不要跟需要另议；容器级扇出是另一个
  设计（可复用 `StateSubtree` 的收集）。
- **GS-D7 lint 只在 CLI。** 同 `blur` 的 `PUI-FX-TAG`。

## 5. 测试

`FxImageTests`（EditMode）：

1. `grayscale='true'` 的 Icon / Image 拿到 `UI/ImageFx`，`_Desaturate = 1`；两个同参数的共用一份材质。
2. `grayscale='false'` / 不写：不挂材质。
3. Variant：`grayscale='false' grayscale.locked='true'` 往返，关掉时材质回池。
4. `class=` 带来的 `grayscale` 生效。
5. C#：`icon.Grayscale = true` 当即换好材质（不等 canvas rebuild）；getter 读回；再设 `false` 回到不挂材质。
6. ReSolve：没声明 → 保留 C# 的值；声明了 → 写回声明值（钉住 §3.3 的契约）。
7. `grayscale` + `intensity`：作者灰保留光照。
8. `grayscale='maybe'` → `ParseException`，消息带属性名。

`DisabledGrayscaleTests`：

9. `<Btn>` 内 `grayscale='true'` 的 Image：禁用 → 启用后仍灰；禁用期间熄灯，启用后在作者灰下重新点亮。
10. 作者灰不压制按钮 bg 的默认禁用灰。

`ImageFxRenderTests`：

11. 红图标 `grayscale='true' glow='8'`：本体与光晕都 r ≈ g ≈ b；同一个探针在不写时是红的。

`ImageFxRulesTests`：

12. `grayscale` 在 Image / Icon 上不报；在 Frame / Btn / Text / VStack / RawImage 上报 `PUI-FX-TAG`，RawImage / Text / Btn 的消息带各自的提示。
13. 模板调用点上的 `grayscale` / `blur` 参数不报 `PUI-FX-TAG`（`blur` 那条先红：修正前误报）。
14. `class=` 带来的 `grayscale` 落在 Frame 上照样报。
15. `PUI-FX-VALUE`：`maybe` / `""` / `1` 报；`true` / `False` / `{{x}}` 不报；变体值也判。

`XsdGeneratorTests`：

16. Image、Icon 各列一次 `grayscale`。

## 6. 范围外 / 后续

- 0–1 强度与渐变过渡：要把 shader 的 `_Desaturate > 0.5` 改成 lerp，而且 tween 会把一串小数值写进材质缓存键。
- `<RawImage>` / `<Frame>` / 交互控件 bg / 容器级扇出。
- runtime-owned（`RuntimeStateAttr` 扩成集合）。
