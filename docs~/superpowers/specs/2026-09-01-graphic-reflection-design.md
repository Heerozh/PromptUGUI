# `reflect` —— `<Image>` / `<Icon>` / `<RawImage>` 的网格级镜面倒影

> 状态：**备用方案，未立项**（2026-09-01 定案）。倒影改由既有原语手工组合实现 ——
> `flip="y"` + 带色标 / 提示的渐变 `color`，缺的顶点路径色标由
> `2026-09-01-vertex-gradient-stops-design.md`（VGS）补上，配方进 SKILL。本文完整保留：
> 一旦出现 `cover` 图倒影 / C# 动态换图倒影 / 按钮内随状态调制的倒影这类手工版难受的场景，
> 直接按本文出 plan（几何部分可复用 VGS 落地的 `MeshSlicer`）。§9 的待拍板项已由 VGS §11 覆盖。
> 相关：`2026-08-31-hug-reveal-flip-checked-design.md` §3（`rotation` / `flip`：同一组标签、同一条
> `BaseMeshEffect` 路径、同一种懒挂 / 不销毁惯例）；`2026-06-13-gradient-color-design.md`（`GradientTint`：
> 顶点色渐变、按网格包围盒归一）；`2026-08-27-decor-primitives-design.md`（`kind="sprite"` 的自动镜像；
> 「不做微语法属性」的取舍）；`2026-08-23-glass-fill-design.md`（backdrop 捕获 —— §10 解释为什么它
> **不能**用来做整卡倒影）。

## 1. 问题

两张参考图里的同一种视觉词汇：物件下方有一段**上下颠倒、越往下越透明**的镜像 —— 资源条里五个图标
落在玻璃台面上的淡淡倒影；关卡选择里卡片立在反光地板上的倒影。这是「物件放在一个有光泽的面上」的
最廉价暗示，UI 里出现频率不低（货币栏、道具格、展柜、卡片轮播）。

今天在这个库里做不出来：

- **控件层没有任何东西能画第二份自己。** `flip="y"` 能把图翻过来，但翻的是本体不是副本；要倒影只能
  再写一个 `<Image flip="y" color="white/0.3,white/0">`，手工对位、手工跟随尺寸，在 `<VStack>` 里还要
  多占一个槽位，sprite / type / 状态调制全部要作者自己保持同步。
- **美术侧预烘倒影进图**（图集里每个图标自带倒影）会让图标尺寸 / 锚点全部失真，`<Icon size="native">`
  和 pixel-snap 都跟着错，且换主题（有反光台面 / 没有）要两套图。

## 2. 否决的方案

**独立标签 `<Reflection/>`（仿 `<Decor>` 的子元素形态）。否决。** `<Decor>` 走子元素是因为它多实例、
要 `<Show>` 门控、要按主题整槽换 `kind`。倒影是**宿主自身像素的一份变换副本**，单实例、没有独立
形状、生命周期完全跟着宿主网格 —— 它和 `rotation` / `flip` / `glow` 一样是宿主的一个视觉属性。
主题开关走 `class=` 属性包给 `reflect="0"`，与 `kind="none"` / `sprite="none"` 同一惯例。

**克隆一个子 GameObject 挂第二个 `Image`。否决。** 要同步 sprite / type / color / 状态调制 / 灰度材质 /
`AspectRatioFitter` 结果 —— 每加一个属性都要多同步一项，且多一个 draw（不同 `Graphic.color` 就不
合批）。`BaseMeshEffect` 在同一份网格里追加顶点：同材质、同贴图、同一个 draw call，**所有已生效的东西
（渐变 tint、状态调制、CanvasGroup、灰度、`tint="linear"`）自动继承**，一行同步代码都没有。

**倒影 alpha 走自定义材质（把「离接触线的距离」塞进 TEXCOORD 由 fragment 算曲线）。否决。**
会和 `UI-LinearLightTint` / `UI-Grayscale` 的材质槽打架，还破坏与同图集 UI 的合批。线性渐隐
的 alpha 是 y 的仿射函数，光栅化的线性插值**精确**，顶点色就够（§4.3）；缓动曲线可以之后用
横向切条近似（§11）。

**整棵子树的倒影（参考图二整张卡片）。本期非目标**，理由与可行路线见 §10。

## 3. 语法

```xml
<!-- 资源条：图标下 35% 不透明的倒影，默认向下延伸图标高度的一半 -->
<HStack spacing="24">
  <Icon name="res:gold"    reflect="0.35"/>
  <Icon name="res:crystal" reflect="0.35"/>
</HStack>

<!-- 展柜：倒影更长、更淡，和物件之间留 2px 缝 -->
<Image sprite="item:blaster" reflect="0.25" reflectLength="80%" reflectGap="2"/>

<!-- 主题开关：反光台面主题给倒影，哑光主题收掉 -->
<Style name="shelf-icon" reflect="0.3"/>
<Theme name="matte"><Style name="shelf-icon" reflect="0"/></Theme>

<!-- 竖屏没有台面，倒影只在横屏出现 -->
<Icon name="ui:trophy" reflect="0.3" reflect.portrait="0"/>
```

| 属性 | 取值 | 默认 | 说明 |
|---|---|---|---|
| `reflect` | `0`–`1` 浮点 | `0`（无倒影） | 倒影在**接触线处的不透明度**，向下线性衰减到 0。`0` / `""` = 关（主题 / Variant 收掉倒影的通道） |
| `reflectLength` | `N`（px）/ `P%`（占**绘制高度**比例） | `50%` | 倒影从接触线向下延伸多远、在哪里衰减到零 |
| `reflectGap` | 有符号 px | `0` | 本体底边与倒影顶边之间的缝。**负值**把倒影往上拉 —— 素材自带透明留白时用它抵消（留白在镜像后会翻倍） |

- 仅 `<Image>` `<Icon>` `<RawImage>` —— 与 `rotation` / `flip` 完全同一组（它们是会生成网格的叶子
  Graphic）。写在其它标签 → lint error **`PUI-REFLECT-TAG`**："put `reflect` on the inner `<Image>` /
  `<Icon>`"。
- `reflect` 只收数字；`reflect="true"` 是错误，报错消息直接给出写法（`reflect="0.35"`）。库里
  `glow="12"` / `innerGlow="6"` 都是「数字即开关」，这里的数字是设计师真正会调的那个旋钮。
- Variant（`reflect.portrait="0"`）、`<Style>` / `class=`、Theme 照常。XSD：`Image` / `Icon` 在
  `XsdGenerator` 里是手列的，要补三个属性；`RawImage` 走反射自动带上。

## 4. 语义

### 4.1 几何

- **接触线 = 绘制几何的最低点**（经 `flip` / `rotation` 之后的网格包围盒 `minY`），**不是 rect 底边**。
  `<Icon>` 的 `preserveAspect` 有 letterbox、`rotation="45"` 的菱形、`type="contain"` 的适配结果，
  倒影都贴着**画出来的东西**而不是贴着 rect —— 和 `GradientTint` 按网格包围盒归一是同一条原则。
  代价：`type="filled"` 的网格随 `fillAmount` 收缩，接触线会跟着动；进度条不是倒影的场景，文档注明即可。
- 顶点 `y' = 2·minY − y − gap`；UV 原样随顶点（图随四边形一起颠倒，`RotateFlipEffect` 同一理由：
  9-slice 的边框镜像后仍是边框）。
- `%` 相对网格包围盒高度；倒影几何在 `y' < minY − gap − length` 处**被裁掉**（不是简单把远端 alpha
  设 0 —— 一个大三角形只靠两端顶点插不出「50% 处归零、其后为零」，必须切）。
- 镜像翻转三角形绕向，追加时交换两个索引保持与本体一致（库内所有 UI shader 都 `Cull Off`，这只是
  不留隐患）。

### 4.2 不碰的东西

- **排版**：RectTransform、anchor / margin / pivot、`LayoutElement`、`GetNativeSize()` 一律不变。
  倒影和 `glow` 一样只膨胀绘制几何 —— `<VStack>` 的下一个兄弟会压在倒影上，作者用 `spacing` /
  `margin` 留空。这是特性：参考图二卡片下方的页码点就压在倒影区里。
- **点击**：Graphic 的 raycast 按 rect 判定，倒影区永远不接点击。
- **合批**：不换材质、不换贴图，同图集照常合批。

### 4.3 颜色与继承

- 倒影顶点色 = 本体顶点色 × `(alpha 系数)`，系数在接触线 = `reflect`，在 `reflectLength` 处 = 0，中间
  线性。alpha 是 y 的仿射函数 → 三角形内线性插值**精确**，无需切条。
- `Graphic.color`（含状态 `*Modulate` 调制）、`GradientTint`、CanvasGroup alpha、禁用灰度材质、
  `tint="linear"` 全部自动作用于倒影 —— 因为它们要么已烘进顶点色、要么在 CanvasRenderer / 材质层。
  按下按钮，倒影一起变暗；元素淡出，倒影一起淡出。

### 4.4 与其它机制的组合

| 组合 | 行为 |
|---|---|
| `flip` / `rotation` | 先转 / 翻本体，再对结果做倒影（`flip="y"` + `reflect` = 倒挂的图带自己的倒影，自洽） |
| 祖先 `mask="rect"` / `mask="self"` | 按顶点裁，倒影伸出父级的部分被裁掉 —— 正确行为 |
| 自身 `mask="rect"` | `RectMask2D` 不裁自己那一层的 Graphic，倒影完整，子节点照常被裁 |
| 自身 `mask="self"` | stencil 用的是同一份网格 → **倒影区也成为遮罩的一部分**，子节点会在倒影区露出来。lint warning `PUI-REFLECT-MASK`；要遮罩就别在同一节点要倒影 |
| `type="contain"` / `cover"` | `AspectRatioFitter` 先定 rect，倒影贴适配后的几何；`cover` 溢出部分由父级 mask 裁，倒影同理 |
| `sliced` / `tiled` | 整张网格镜像，边框仍是边框 |
| `<Animation>`（transform 级 proxy） | 倒影随网格一起动，互不知晓 |
| pixel-perfect | 顶点是像素对齐坐标的镜像，`reflectGap` 取整数即可保持对齐；`.pxl` 素材的 alpha 渐隐不是像素风的（无抖动），是否要 stepped 版本等真实需求 |

### 4.5 C# 面

`Image.Reflect` / `Icon.Reflect` / `RawImage.Reflect`（`float`，可读写、可补间 —— 展柜选中时把倒影
从 0.15 推到 0.4，每帧 `SetVerticesDirty`，与 `Rotation` 补间同价）；`ReflectLength`（`string`）、
`ReflectGap`（`float`）。

## 5. 实现地图

### 5.1 `Controls/Internal/ReflectionEffect.cs`（新，`BaseMeshEffect`）

```
ModifyMesh(vh):
  stream ← vh.GetUIVertexStream()          // 三角形列表（Shadow/Outline 同款读法；VertexHelper 不暴露索引）
  minY/maxY ← 包围盒；H ← maxY−minY；L ← 是 % ? H·p : px
  L ≤ 0 或 opacity ≤ 0 → return
  yTop ← minY − gap；yEnd ← yTop − L
  对每个三角形 (a,b,c)：
      镜像三点：y' = 2·minY − y − gap；alpha ×= opacity · (yTop − y') / L
      以 (a', c', b') 绕向、对半平面 y' ≥ yEnd 做 Sutherland–Hodgman 裁剪（得 0 / 1 / 2 个三角形），
      裁出的新顶点按边线性插值 position / uv0 / uv1 / color（切线上 alpha 恰为 0）
      追加到 stream
  vh.Clear(); vh.AddUIVertexTriangleStream(stream)
```

- 恒等（`opacity ≤ 0` 或 `L ≤ 0`）时 `enabled = false`，`ModifyMesh` 早退；与 `RotateFlipEffect` 同款
  「懒挂、禁用不销毁」（Variant 往返幂等）。
- 本体被 de-index（四边形 4 顶点 → 6），`Shadow` 早就这么做，可忽略。

### 5.2 `Controls/Internal/ReflectionApplier.cs`（新）—— 三控件共用落点 + **顺序保证**

uGUI 按 `GetComponents<IMeshModifier>()` 的**组件顺序**依次调 `ModifyMesh`，即 `AddComponent` 的先后。
`GradientTint`（`ColorApplier` 懒挂）和 `RotateFlipEffect`（`RotateFlipApplier` 懒挂）都是按属性
setter 触发时机挂上的，而 setter 顺序来自 `HashSet` 遍历、不保证。倒影**必须最后跑**（否则渐变会
跨本体 + 倒影归一、旋转会把倒影一起绕中心转）。

做法：`ReflectionApplier` 第一次挂 `ReflectionEffect` 之前，先把所有前置效果**以禁用态预先挂好**
（`GetComponent<GradientTint>() ?? AddComponent`，`RotateFlipEffect` 同理）。两者本来就永不销毁、
只切 `enabled`，因此它们的槽位永远排在倒影前面 —— 之后 Variant 才把颜色切成渐变也不会改变顺序。
代价是有倒影的 Graphic 多两个禁用组件。

**守卫测试**：反射枚举 `PromptUGUI.Runtime` 里所有 `BaseMeshEffect` 子类，断言除 `ReflectionEffect`
外每一个都在 `ReflectionApplier.Predecessors` 里 —— 将来新增网格效果时测试先红。

（备选：把三个效果合并成一个固定顺序的 `MeshEffectChain` 组件，顺带治好渐变 vs 旋转今天已存在的
顺序不确定性。改动面大一档、要动 `GradientTint` 相关测试；作为可选的后续重构记录在 §11。）

### 5.3 其余

| 层 | 文件 | 改动 |
|---|---|---|
| 解析 | `Core/Parser/ReflectSpec.cs`（新，纯 C#） | `reflect` 范围、`reflectLength` 的 px / `%` 、`reflectGap` 数值；控件 setter 与 lint 共用，CLI 直接编译（`RadiusParser` 地位） |
| 控件 | `Image.cs` / `Icon.cs` / `RawImage.cs` | `[UIAttr, Preserve] Reflect` / `ReflectLength` / `ReflectGap`，每个 setter 重放三值到 applier（`Rotation` / `Flip` 同款） |
| lint | `Core/Lint/ReflectRules.cs`（新） | §6 |
| XSD | `Editor/XsdGenerator.cs` | `Image` / `Icon` 手列表各加三项（`reflect` `xs:float`、`reflectLength` `xs:string`、`reflectGap` `xs:float`） |
| 注册 / 图集 / i18n 扫描 | 无 | 非 sprite、非文本属性 |

## 6. lint

| 规则 | 遍 | 级别 | 触发 |
|---|---|---|---|
| `PUI-REFLECT-TAG` | raw | error | `reflect*` 写在 `<Image>` / `<Icon>` / `<RawImage>` 以外的标签（含 `<Btn>` / `<Frame>` / `<Text>`） |
| `PUI-REFLECT-VALUE` | raw + expanded | error | `reflect` 非数字或超出 0–1（含 `true`）；`reflectLength` 非 `N` / `P%` 或为负；`reflectGap` 非数字。跳过 `{{}}` 占位 |
| `PUI-REFLECT-ATTR` | expanded（class 合并后判） | warning | 写了 `reflectLength` / `reflectGap` 但最终没有 `reflect`（什么都不会画） |
| `PUI-REFLECT-MASK` | expanded | warning | `reflect` 与 `mask="self"` 同节点（倒影区成为遮罩的一部分，§4.4） |

## 7. 测试

- `ReflectionEffectTests`（EditMode，纯几何，2×2 原点四边形）：全长倒影后 stream 为 12 顶点；镜像 y
  正确、uv 随顶点；接触线 alpha = `opacity × 原 alpha`、远端为 0；`reflectLength="50%"` 时几何在
  y = −2 被切断且切线 alpha = 0；`reflectGap` 平移；`opacity=0` 不追加且组件禁用；绕向与本体一致。
- `ImageReflectTests`：属性落点；**先设 `reflect` 再把 `color` 切成渐变，`GetComponents<IMeshModifier>()`
  最后一个仍是 `ReflectionEffect`**；Variant 往返禁用不销毁；`<Btn reflect>` 报 `PUI-REFLECT-TAG`；
  RectTransform / `GetNativeSize()` 不变。
- `ReflectRulesTests`：§6 四条各正反例。
- `ReflectionRenderTests`（EditMode，`Camera.Render()` 到 RT，`DecorRenderTests` 同款夹具）：
  第一条永远是夹具自检（本体像素亮）；有 `reflect` 时接触线正下方像素亮、无 `reflect` 时暗；
  `reflectLength` 之外暗；沿倒影向下亮度单调递减；`reflectGap` 留出暗带。数据层测试对「屏幕上没有」
  是盲的，这一组是真正的回归防线。
- 前置效果守卫测试（§5.2）。
- `XsdGeneratorTests`：三个属性名的 substring 断言。

## 8. SKILL 更新（同一 PR，英文）

- `authoring-promptugui-xml/SKILL.md`：`<Image>` 属性表加三行 + 紧接 **Rotation & flip** 新增
  **Reflection** 小节（示例、接触线 = 绘制几何、不占排版、`mask="self"` 注意、`reflectGap` 负值抵消
  透明留白）；`<Icon>` / `<RawImage>` 表各加三行；Quick reference 加一行
  `REFLECT reflect="0.35" reflectLength="50%" reflectGap="0"`；lint 码表加四条。
- `scripting-promptugui-csharp/SKILL.md`：`Image.Reflect` 可补间，一句话挂在 `Rotation` 那节旁边。
- `.lint/UIXmlLint/README.md`：规则列表。
- `authoring-promptugui-pxl/SKILL.md`：一句 —— 倒影由 `reflect=` 生成，**不要**把倒影画进 `.pxl`。

## 9. 待拍板的决策

1. **命名与形态**：`reflect` / `reflectLength` / `reflectGap`，`reflect` 的值直接就是不透明度。
   备选：`reflection*` 全称；或 `reflect="true"` + `reflectOpacity`。推荐前者 —— 少一个属性，
   数字即开关与 `glow` 同调。
2. **接触线取绘制几何底边**（非 rect 底边）。`<Icon>` letterbox / 旋转下更合理；`filled` 的接触线
   会随进度移动是接受的代价。
3. **线性渐隐、不切条**。精确、零额外顶点；参考图二那种「很快消失」用短 `reflectLength` 表达。
   缓动曲线（`reflectFade`）留到有人要。
4. **v1 只做三个叶子 Graphic**。整卡 / 子树倒影是另一个机制（§10），`<Btn sprite=>` 的底图倒影
   与程序化表面倒影也留待后续（前者用同一个 applier 几乎白给，后者要动 SDF shader）。
5. **`reflectLength` 默认 `50%`**。
6. **顺序保证用「预挂前置效果」而非重构成 chain**（§5.2）。
7. **「地面」叠加顺序（倒影 < 地面 < 物件）**：v1 不加机制，用 §13 的叠层配方；独立节点
   `<Reflection of=>` 作为后续候选记录在 §13.3。

## 10. 非目标：整棵子树的倒影（参考图二）

参考图二里被反射的是整张卡片（封面图 + 文字 + 角标 + 底部信息条）。uGUI 里没有「把一棵子树再画
一遍」的廉价通道：

- **glass 的 backdrop 采样镜像位置**：backdrop 是相机图像（`AfterRenderingPostProcessing`），
  Screen Space Overlay 画布根本不在里面；Screen Space Camera 下拿到的是上一帧含自身的整帧 —— 反馈回路。
  不可用。
- **专用相机渲到 RenderTexture 再贴 `<RawImage flip="y">`**：每个倒影一台相机 + 一张 RT + 图层隔离，
  逐帧成本与工程复杂度都不是 UI 属性该有的量级。
- **代理 Graphic 逐个镜像子树里每个 Graphic 的网格**（TMP 走 `textInfo.meshInfo`、其余走末位
  `IMeshModifier` 捕获）：技术上可行、可复用本文的顶点数学，但要处理脏跟踪、局部→宿主坐标变换、
  TMP 子网格、CanvasGroup 传递 —— 是一个独立的中型特性，需求出现时立项。

**今天的做法**：卡片的倒影视觉上由封面图主导 —— 给卡片里的 `<Image type="cover">` 写 `reflect`
（父级 mask 记得留出倒影高度，或把封面图放到 mask 之外的层）；参考图二中倒影只有卡高的 ~12%
且很快消失，封面图底部的倒影已经是那个观感。

## 11. 开放问题（留给 plan / 实现期）

- `ReflectionEffect` 与 `Shadow` / `Outline`（uGUI 自带，用户 prefab 可能挂）的顺序：不在
  `Predecessors` 里也不该报错 —— 守卫测试只枚举本库程序集。
- 是否给 `ReflectionEffect` 一个上限保护（`reflectLength` 极大时几何只是更长，无二次成本，暂不需要）。
- `MeshEffectChain` 重构（§5.2 备选）是否顺手做：会同时确定 `GradientTint` 与 `RotateFlipEffect`
  的相对顺序（今天是谁先挂谁先跑），需要先问作者渐变该跟着图转还是钉在屏幕竖直方向。
- 缓动渐隐（切 4–6 条横带、alpha 取曲线值）、方向（`reflectAt="top|left|right"`，用 `AnchorPreset`
  的边词汇）、倒影压扁（`reflectScale`，透视地板观感）—— 属性名已按方向中性预留，都不在 v1。

## 12. 成本

| | 开销 |
|---|---|
| 不写 `reflect` 的文档 | **零** —— 不挂任何组件 |
| 一个有倒影的 Graphic | 同一 draw call 内顶点约 ×2（本体 de-index 后 6 + 倒影 ≤ 12），两个禁用的前置效果组件；材质 / 贴图不变，合批不变 |
| 补间 `Reflect` | 每帧一次网格重建，与补间 `Rotation` 同价 |

## 13. 「地面」叠加顺序：倒影 < 地面 < 物件

参考图二的地板是半透明瓷砖：卡片**站在**地面上（地面在卡片之下），倒影却**沉在**地面之下
（瓷砖的格线压在倒影上面）。三层的绘制顺序是 **倒影 → 地面 → 物件**。

### 13.1 属性形态做不到

`reflect=` 把倒影追加进宿主**同一份网格**、同一个 draw call —— 倒影与本体永远同一层，
不可能在两者之间插进一张地面。这是 §2 选择同网格追加时买下的限制：它换来的是零同步、
零额外 draw、全部视觉状态自动继承。

### 13.2 零机制配方：地面叠层条（v1 采用）

利用「站在同一地面上的物件共享一条基线」：基线以上只有物件，基线以下只有倒影。
把地面**再画一遍**，但用 `mask="rect"` 只露出基线以下的那一条，半透明压在倒影上 ——
它永远碰不到物件。

```xml
<Frame id="stage" anchor="bottom-stretch" height="420">
  <Image sprite="bg:floor-tiles" anchor="stretch"/>                    <!-- ① 地面 -->
  <Frame anchor="top-stretch" height="300">                             <!-- ② 物件层：基线 = 本 Frame 底边 -->
    <HStack anchor="bottom-center" spacing="20">
      <Image sprite="card:01" reflect="0.3" reflectLength="40%"/>       <!--   倒影随网格伸到基线以下 -->
      <Image sprite="card:02" reflect="0.3" reflectLength="40%"/>
    </HStack>
  </Frame>
  <Frame anchor="bottom-stretch" height="120" mask="rect">              <!-- ③ 地面叠层：只盖基线以下 -->
    <Image sprite="bg:floor-tiles" anchor="bottom-stretch" height="420" color="white/0.6"/>
  </Frame>
</Frame>
```

- 叠层里的地面 `Image` 与 ① 同尺寸同锚点（`height="420"` 贴 stage 底），瓷砖纹理逐像素对齐，
  只是被外层 `mask="rect"` 裁到最下面 120px；`color="white/0.6"` 是「地面盖住倒影的程度」，
  与 `reflect=` 一起构成两个旋钮。
- 适用条件：**同一地面上的物件基线一致**（一排图标、一排卡片、一层货架）—— 也就是几乎所有
  会用到倒影的场景。基线不一致的物件（同一地面、不同高度站位）叠层条会盖住站得低的那个的
  身体，那是 §13.3 的场景。
- 成本：多一次地面纹理的 draw（叠层条通常与 ① 同图集，可合批）；无新机制、无新属性、无 lint。
- 叠层 `Image` 会接住地面带上的点击；物件不在带内，不受影响。
- SKILL 的 **Reflection** 小节收录此配方（§8）。

### 13.3 后续候选：独立节点 `<Reflection of="id"/>`（代理 Graphic）

真正的「倒影单独一层、放在树的任意位置」需要第二种机制：一个代理 `MaskableGraphic`，
按 XML 顺序占自己的绘制槽位，画的是被引用 Graphic 的最终网格经镜像 + 渐隐后的副本。

```xml
<Image sprite="bg:floor-tiles"/>
<Reflection of="hero" reflect="0.3" reflectLength="40%"/>   <!-- 在地面之前 → 沉在地面下 -->
<Image sprite="bg:floor-tiles" color="white/0.6"/>
<Image id="hero" sprite="card:01"/>
```

机制要点（都有对应先例或已知解法，但每条都是要写、要测的东西）：

| 环节 | 做法 | 坑 |
|---|---|---|
| 拿到源的最终网格 | 源上挂一个末位 `IMeshModifier`（§5.2 同一套顺序保证）把三角形流拷进缓冲 | — |
| 通知代理重建 | **不能**在源的 rebuild 里 `SetVerticesDirty` 代理 —— `CanvasUpdateRegistry` 在 rebuild 循环中拒绝新注册并报错。改为代理暴露内部方法直接调自己的 `UpdateGeometry()` + `UpdateMaterial()`（`protected`，绕开注册表，`PerformUpdate` 自己就是这么设网格的） | 同帧无延迟，但属于「利用实现细节」 |
| 源移动 / 布局重排（网格不重建） | 在 `Canvas.willRenderCanvases`（`PixelSnap` 同一钩子，排在 `PerformUpdate` 之后、布局已定）比较源与代理的 `localToWorldMatrix`、`GetInheritedAlpha()`、`isActiveAndEnabled`，变了就同步重建 | 每帧轮询一次矩阵 |
| 坐标 | 源局部 → 世界 → 代理局部；镜像与渐隐仍在源局部空间算（复用 §5.1 的顶点数学） | — |
| 材质 / 贴图 | 代理 `mainTexture` / `material` 转发源的（`material` 而非 `materialForRendering`，遮罩由代理自己那条 `MaskableGraphic` 管线补） | 源换灰度 / linear 材质要跟着 dirty（`RegisterDirtyMaterialCallback`） |
| 引用解析 | `of="id"` 走 `bind=` / `click@id` 同一套作用域规则（`TriggerSourceResolver`） | 模板内 `ScopedIds`；lint 悬空引用 |

**不自动继承的东西**（与属性形态的根本差别）：源祖先的 CanvasGroup（用 `GetInheritedAlpha()`
补）、源祖先的 mask（代理只被**自己**的祖先裁 —— 源在 ScrollList 里滚出视口，代理不会跟着消失，
除非把 `<Reflection>` 放进同一个 mask）、源的 `hidden`（轮询补）。

**复杂度估算：约为 v1 的 2 倍**（新标签 + 代理 Graphic + 捕获效果 + 每帧钩子 + 引用解析 + lint +
渲染测试），没有未知项但有两处依赖 uGUI 实现细节（rebuild 内同步 `UpdateGeometry`、
`willRenderCanvases` 的订阅顺序）。§13.2 的配方覆盖了共同基线的全部场景，因此**后置**：
等出现「同一地面、基线不一致、且必须沉在地面下」的真实需求再立项。
