# 程序化表面：让任何控件都能被换出形状

> 状态：设计稿。实施前需要 plan。
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

（`Toggle` / `Slider` / `Dropdown` / `ScrollList` 的 `Selectable.targetGraphic` 本来就指在子节点上 —— 这条先例在 §5 有用。注意 `Slider` 的主表面是**轨道**，而它的 `targetGraphic` 是**滑块**，两者不是一个东西，见 §12。）

**写错了没人管。** `ControlAttributeApplier` 对控件没有的属性名直接 `continue`；parser 不知道每个控件有哪些属性；`Core/Lint` 是纯 C#、反射不到 Unity 侧的控件。三道关卡全部放行 —— 所以 `<Btn radius="8">` 是彻底静默的。

## 4. 否决的方案：就地换 Graphic

最直觉的做法是「声明了程序化属性就把这个表面的 `Image` 换成 `ProceduralPanel`」。**不行，两条理由，第二条是硬的。**

**① 一个 GameObject 上挂不了两个 Graphic。** 实测：往已有 `Image` 的节点 `AddComponent<RawImage>()` 被 Unity 拒绝，`CanvasRenderer` 始终只有一个（`Graphic` 是 `[RequireComponent(typeof(CanvasRenderer))]`）。所以「并存、按需显隐」这条路根本不存在。

**② 于是换 Graphic 就等于运行时 `Destroy` + `AddComponent`。** 而这正是 `PUI-MASK-VARIANT` 当年拒绝掉的那一类 —— 那条规则的原话是「would require AddComponent/Destroy at runtime」。整套变体 / 主题机制的地基是 **`VariantStore.Changed → Screen.ReSolve` 只重放属性、不重建 GameObject**（引用与 R3 订阅必须存活）。一个变体把 `glass` 开了又关，就会把控件的 Graphic 拆了又装，`Selectable.targetGraphic`、状态反应器捕获的基色、mask 的 stencil 全部跟着失效。

**否决。**

## 5. 方案：程序化底层（backing layer）

控件声明了任一程序化属性时，在主表面**旁边**懒挂一个铺满的 `ProceduralPanel` 子节点，让原 Image 退位：

```
Btn (GameObject)
├─ Image  _bg           ← 退位：sprite 清空、alpha 归零（组件保留，不销毁）
├─ __surface__          ← 懒挂的 ProceduralPanel，anchor=stretch
└─ Label / 作者写的子节点
```

- **没声明就一个都不挂** —— 跟 `Frame` 今天的规则一字不差，对不用这个特性的工程零成本。
- **挂上之后只改参数与可见性，永不销毁** —— 和 Add block 的 Strategy C 同构，变体来回切保持幂等。
- **`ProceduralPanel` 强制 `raycastTarget = false`**（它自己的注释写着「a Frame stays click-through」），所以点击照常落到控件本体上，不需要额外处理。

**机制不是新发明。** `Toggle._toggle.targetGraphic = _bg`（`Background` 子节点）、`Slider._slider.targetGraphic = _handle`（`Handle` 子节点）、Dropdown 的 item toggle 和两个滚动条 —— 「`targetGraphic` 指在子 Graphic 上」在这个仓库里已经是常态。把它指到 `__surface__` 走的是同一条路。

## 6. 范围：只覆盖「主表面」

**只有 `sprite=` / `color=` 今天已经在管的那一层**进入程序化模式。内层（Slider 的 fill/handle、Dropdown 的 arrow/popup/scrollbar、Toggle 的 checkmark）继续走各自的 sprite 钩子。

理由：Slider 有 3 层、Dropdown 有 6 层，全开就是 `fillRadius` / `handleRadius` / `popupRadius` / `scrollbarRadius`… 属性爆炸；而实际皮肤需求里，形状差异几乎都在主表面。真有需要，`weld` 那种「容器持有共享参数」的形状是后续可考虑的方向，不在本设计内。

## 7. `sprite` 与程序化在同一表面上互斥

贴图叠在 SDF 面上是一团糟，`Image.type` 的 sliced/tiled 推导对 SDF 也没有意义。规则：

- 声明了任一程序化属性 → 该表面进入程序化模式；同一表面上的 `sprite=` 是**矛盾声明**，lint 报错，运行时以程序化为准。
- `sprite="none"` / `sprite=""` **不算冲突** —— 它的语义是「清掉贴图」，跟进入程序化模式是一致的，而且换肤属性包里到处都是这个写法。
- `color=` **不冲突**：它在两种模式下都是填充色（程序化模式走 `Panel.SetFill`）。

## 8. 状态视觉怎么组合

- **`*Color` / `*Modulate` 直接可用**：它们驱动的是 `Graphic.color`，而 `ProceduralPanel` 是 `MaskableGraphic`。前提是 `targetGraphic` 指到 `__surface__`。
- **`targetGraphic` 的迁移必须是「算出来的」，不能一次性设。** 变体把程序化模式开了又关时，targetGraphic 要跟着回到原 Image；留在已隐藏的层上就是一个不可逆状态。这正是本周刚修的那一类缺陷（`Btn.ReconcileTransition`、`Progress.ReconcileLayers`、`StateTintReactor` 的基色），同样的形状：**从当前声明推，不从一次性快照推。**
- **`pressedSprite` / `disabledSprite` / `selectedSprite` 在程序化表面上没有意义** —— 它们是 `Image.overrideSprite` 交换。判为矛盾声明（lint 报）。它们与 ColorTint 的让位逻辑已经是算出来的（M2.5），这里要一并纳入同一个 `Reconcile`。
- **`DisabledGrayscaleInstaller`** 走子树 Graphic，`__surface__` 会被自动纳入。去色对 SDF 面是否有意义要验（§12）。

## 9. 成本

| | 开销 |
|---|---|
| 没用程序化皮肤的控件 | **零** —— 一个组件都不挂，和今天逐字节相同 |
| 用了的控件 | 多一个 GameObject + CanvasRenderer + 一次 draw |
| 同 style 的多个面板 | 共享材质、可合批（`ProceduralMaterialCache`） |
| 玻璃 backdrop 采集 | 每帧一次固定开销，与面板数量无关；没有可见玻璃面板时不存在 |

## 10. 这套要替代的 userland 写法

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

## 11. 与 lint / SKILL 的关系

这三条**不依赖本特性**，可以先做；但第一条在本特性落地后要反过来删掉，所以要一起规划：

1. **`PureContainerVisualAttrRules` 补第三档。** 它已经拿着那份一模一样的程序化属性清单（`color radius borderWidth borderColor glow glowColor glass frost depth dispersion lightAngle lightIntensity saturation noise weld`），也已经在做「这个标签会静默丢弃这些属性」的判断 —— 只是 `AppliesTo` 现在只覆盖 `Frame`（查 `sprite`）和四个纯排版容器（查全部）。缺的是中间那档：**挂了 Image、所以 `color`/`sprite` 有效，但没有 `ProceduralPanel`、所以程序化那一组一律被丢。** 那个类的注释里作者当年明确考虑过 `<Btn>` 并把它排除在 `LayoutOnlyTags` 之外（正确 —— `color` 在 Btn 上有效），但没人补上第三档。
2. **SKILL 缺一句边界话。** `glass.md` 写的是 "All live on `<Frame>`"，但紧接着 "all work through `<Style>` / `class=` … **like any other attribute**" —— 读起来像是通用的。主 SKILL 从正面说了 Frame 会自绘、从反面说了纯排版容器啥也画不了，唯独中间这档从没人写过。
3. **更大的洞：内置控件上写错属性名是完全静默的。** 同一轮里我写 `<ScrollList scrollbarSprite="none">` 什么都没发生 —— 它的 XML 名其实是 `scrollbar`（`[UIAttr(IsSprite)]` 会剥掉尾部 `Sprite`，好和 `<Dropdown scrollbar=>` 对齐）。要让 CLI 能查，得把「tag → 属性名」这张表送进 `Core/Lint`。零件都在：`Editor/XsdGenerator.cs` 已经在反射注册表生成 schema，`BuiltinTags` + `BuiltinTagsTests` 是「手工镜像 + 守卫测试」的现成先例。**独立一条，不属于本设计。**

## 12. 开放问题（plan 之前要定）

1. **`Slider` 的主表面是轨道，`targetGraphic` 是滑块。** 程序化模式下状态色该落在哪个上？（倾向：`targetGraphic` 不动，程序化只换轨道的画法；但那样 `hoverColor` 就够不到轨道，与其他控件不一致。）
2. **`weld` 能不能跨控件？** 初判不能 —— weld 的成员是同一个 carrier 的**直接子级**，而各控件的 `__surface__` 分属不同父节点。要不要给「一组控件融成一块玻璃」留口子。
3. **`mask="self"` 与程序化表面。** stencil `Mask` 需要一个 `Image` 当遮罩源（`PUI-MASK-SELF-NO-SPRITE` 就是在说这个），SDF 面不是 Image。两者同时声明怎么判。
4. **`Progress` 的 bg / fill 默认就没有贴图**（纯色层），它算不算「已经是程序化的一种」？要不要一并纳入。
5. **Disabled 去色对 SDF 面的语义** —— 玻璃面被去色之后是什么样，需要肉眼验。
6. **`__surface__` 的层序**：它必须在控件自有内容（label / checkmark / arrow）之下、在作者写的子节点之下还是之上？Btn 的 auto-label 是控件建的，作者子节点在其后 —— 需要一条明确规则。

## 13. 里程碑拆分（草案）

| | 内容 | 依赖 |
|---|---|---|
| **M0 Red test** | 钉住 §7 / §8 的契约：程序化属性在 Image 系控件上生效、变体来回切幂等、`sprite` 冲突报错、`pressedSprite` 冲突报错 | 无 |
| **M1 表面抽象 + 打通一个控件** | 把 `Frame` 的懒挂逻辑提成共享件，`Btn` 第一个接上（主表面在自身节点，最简单的形状） | M0 |
| **M2 铺开** | Toggle / Slider / Dropdown / InputField / ScrollList / Progress —— 主表面在子节点的那几个 | M1 |
| **M3 lint + SKILL** | 删掉 §11.1 那一档（不再是错误），改成 §7 / §8 的冲突规则；SKILL 改写边界描述 | M2 |

§11 的 1 与 2 可以**先于 M0** 单独合入 —— 在本特性落地前，`<Btn radius="8">` 确实是错的，早一天报错早一天省事。
