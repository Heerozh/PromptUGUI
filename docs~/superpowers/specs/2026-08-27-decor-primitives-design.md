# 边缘装饰原语 `<Decor>`：bracket / tick / line / sprite —— 形状词汇（第二层）

> 状态：**草案，待评审**。
> 相关：`2026-08-27-corner-treatments-design.md`（第一层：角部处理，PR #106）、
> `2026-08-26-procedural-surface-design.md`（`__Surface` / Strategy C / 状态视觉）、
> `2026-08-26-theme-driven-style-design.md`（class= 属性包 / 主题重算）。

## 1. 问题

程序化表面 + 角部处理解决了「轮廓」，但参考图（星海指挥官主界面）里控件身上还有一类
**轮廓之外的小件**：选中卡片四角的金色角括号、选中 Tab 下方的指示三角、标题下的强调线。
这些是科幻 HUD 的固定词汇（augmented-ui / Arwes 都有对应原语），今天在这个库里：

- **bracket / tick 拼不出来。** L 形折线和三角形都不是圆角盒 SDF 的子集 —— cut 的 H
  clamp 到半高，两角全切得到的是六边形不是三角形；L 形描边没有任何组合能表达。
- **line 拼得出来但搭不起来。** 细长 `<Frame>` 加 glow 能画线，但「选中才出现」要手搭
  `<Show>` + 定位，「随主题换色/隐藏」要手挂 class —— 每个使用点重复一遍，LLM 作者
  最容易在这种多件套上出错。

还有一类**参数化永远做不动**的装饰：花纹、藤蔓、徽记 —— 任何 SDF 词汇表都覆盖不了。
它们的正确答案是画一张图，而 `.pxl` 的「LLM 写 → PxlPreview 渲染 → 看图迭代」闭环已经
被验证能产出可用素材，缺的只是把图**摆成装饰**的机制（多实例、镜像、主题切换、状态门控）。

分层设计的位置：这是形状扩展的第二层（命名装饰原语）。第一层（角部处理）已合入
（#106）；本层的 `kind="sprite"` 把「任意形状」交给 `.pxl`，显著消解第三层
（路径逃生舱）的需求压力 —— 它仍然只在有真实需求时立项。

## 2. 否决的方案

**宿主属性 `decor="bracket top-left+bottom-right 16"`。否决。** 主题增删装饰确实最自然
（属性包直接换），但三条代价都不小：状态门控要发明新语法（`decor.selected=`？而
`<Show>` 已经把这件事做完了）；多个装饰堆在一个属性字符串里，色值 / 尺寸内嵌成微语法，
lint 与可读性都塌；`class=` 属性包对任意标签生效（`StyleMerger` 不看 tag），子元素形态
的主题化本来就是免费的。主题去掉装饰的通道是 `kind="none"`（`sprite="none"` 的既有惯例）。

**贴合 SDF 轮廓摆放。推迟。** 装饰沿切角斜边 / 六边形尖端走需要宿主形状参数传递 + 沿边
坐标系，复杂度高一档；参考图两个用例（矩形卡片角括号、Tab 下缘三角）都是 rect 锚定就
够的。等出现「六边形按钮尖端上的括号」这类真实需求再立项。

**每种装饰一个独立标签（`<Bracket>` / `<Tick>`）。否决。** kind 是同一原语的参数而不是
不同原语 —— 定位模型、glow、主题化、状态门控全部同构；拆标签只是把一张属性表复制三份。

## 3. 方案总览

新内置标签 `<Decor>`：宿主的**非排版装饰子元素**，一个节点按 `at=` 生成 N 个实例。

```xml
<!-- 选中卡片的四角金色括号（参考图「星海巡洋舰」卡）-->
<Btn class="skin-card">
  <Show on="state-selected">
    <Decor kind="bracket" size="14" thickness="2" color="@accent" glow="6"/>
  </Show>
</Btn>

<!-- 选中 Tab 下方的指示三角 -->
<Tab class="nav-tab">皮肤
  <Show on="state-selected">
    <Decor kind="tick" at="bottom" size="12x6" color="@accent"/>
  </Show>
</Tab>

<!-- 面板标题的强调线 -->
<Frame class="panel-header">
  <Decor kind="line" at="bottom" size="60%" thickness="1" color="@accent/0.6"/>
</Frame>

<!-- 贴图美学主题的角饰：.pxl 像素画，按左上角作画、其余三角自动镜像 -->
<Frame class="panel">
  <Decor kind="sprite" sprite="ui:corner-vine"/>
</Frame>
```

状态门控 = 现成的 `<Show on="state-*">`，主题化 = 现成的 `class=`，**零个新机制** ——
`<Decor>` 只负责画和摆。

## 4. 属性表

| 属性 | 适用 kind | 值 | 默认 | 说明 |
|---|---|---|---|---|
| `kind` | — | `bracket` / `tick` / `line` / `sprite` / `none` | 无（见 §7） | 前三种为 SDF 绘制，`sprite` 为贴图绘制；`none` = 全部实例隐藏（主题去装饰的通道） |
| `at` | 全部 | 逗号表；bracket 收角 token（`top-left` 等四个），tick / line 收边 token（`top` / `bottom` / `left` / `right`），sprite 角边都收 | bracket / sprite=`top-left,top-right,bottom-right,bottom-left`；tick / line=`bottom` | token 沿用 `AnchorPreset` 词汇（`Core/IR/AnchorPreset.cs`），不另发明缩写 |
| `size` | 全部 | 数字 / `WxH`；line 另收 `%`（占边长比例）；sprite 另收 `native` | bracket=`12`；tick=`10x6`；line=`100%`；sprite=`native` | bracket：臂长（`WxH`=横竖臂不等）；tick：底×高；line：沿边长度；sprite：显式值=缩放，`native`=原生像素尺寸（同 `<Icon>` 惯例，配合 pixel-snap） |
| `thickness` | bracket / line | 数字 | `2` | 笔画宽。tick 是实心填充，写了报 lint warning |
| `color` | 全部 | 同 `color=`（token / `/alpha` / 渐变） | `white` | 复用现有解析路径（`UI.Theme.Resolve` + 渐变）；sprite 上是 tint（`Image.color`），渐变不适用 |
| `glow` / `glowColor` | SDF kind | 同 `<Frame>` | 同 `<Frame>` | 语义与默认值一字不差，SKILL 指向既有说明。sprite 无 SDF 距离场，写了报 lint warning |
| `sprite` | sprite | 同 `sprite=`（`ui:` 键等） | 无 | `kind="sprite"` 必填（§7）；`[UIAttr(IsSprite)]` 惯例，`UI.ResolveSprite` 解析（`Image.cs:42` 同款） |
| `mirror` | sprite | `true` / `false` | `true` | 规范锚点自动镜像 / 旋转（§5）；不对称花纹关掉 |
| `inset` | 全部 | 有符号数字 | `0` | 正=向内、负=向外；角沿对角线双轴等距，边沿内法线 |
| `offset` | tick / line | 有符号数字 | `0`（居中） | 沿边方向平移。bracket 写了报 lint warning |

解析错误（parse-time，纯 C# 子集，CLI 同步可见）：未知 kind / 未知 at token /
kind–at 不匹配（bracket 配边、tick 配角）/ at 重复 token / `%` 用在非 line 的 size /
`native` 用在非 sprite 的 size / `mirror` 非布尔 /
数值非有限或为负（thickness / size；inset / offset 允许负）。报错消息列出合法值。

## 5. 语义

**定位：rect 锚定。** 实例锚在宿主矩形的角 / 边中点上，`inset=0` 时压在边线上居中。
**不贴合** 切角 / 六边形的 SDF 轮廓（§2）。tick 尖端朝外（远离宿主中心）；朝内翻转
v1 不做，需求出现时加属性。

**sprite 的规范锚点与自动镜像。** 作者按**规范锚点**作画：角饰画左上角、边饰画底边；
库为其余锚点自动变换 —— 角 = `localScale(±1, ±1)` 镜像，`top` 边 = 垂直镜像，
`left` / `right` 边 = ±90° 旋转。整个控件层今天没有任何 flip 能力，这是 sprite 装饰
相对 userland `<Image>` 摆放的核心增量（否则四角要备四张翻转素材）。`mirror="false"`
整体关掉自动变换（不对称花纹）。SKILL 里此约定必须配图。

**排版中立，强制的。** `<Decor>` 节点恒定 `LayoutElement.ignoreLayout = true` +
`raycastTarget = false` + `GetNativeSize() → null`，自身 rect 铺满宿主、sizeDelta 为零：

- Stack 宿主里不占槽位不贡献 preferred（`flow="false"` 通道的既有语义，`Control.cs:246`，
  但 Decor 不需要作者写 —— 装饰没有参与排版的场景）；
- `<Show><Decor/></Show>` 不会经 `Trigger.GetNativeSize` 的单子透传把装饰尺寸当内容尺寸
  报给父 Stack（透传读到 null / 零，回退原行为）；
- 因此 **`anchor` / `size` / `width` / `height` / `margin` / `flow` 在 `<Decor>` 上是矛盾
  声明**，lint 报错（§7）—— 定位只由 `at` / `inset` / `offset` 表达。

**绘制顺序 = XML 顺序。** uGUI 按层级序绘制，装饰写在内容后面就压在内容上（角括号
通常写最后）。库不强制排序 —— `__Surface` 恒 index 0 是因为它是「底」，装饰没有恒定的
上下答案。

**`<Decor>` 是叶子标签。** 子节点 = parse error（同 `<Icon>` 一类）。`id=` 允许
（动画目标 / 调试）。

**状态与主题，全部走既有机制：**

- 选中才显示 = `<Show on="state-selected">` 包裹 —— Strategy C 显隐、Normal fallback、
  最近祖先状态源，全部现成；
- 随主题换色 / 换尺寸 / 换 kind = `class=` 属性包（`StyleMerger` 不看 tag）；主题去掉
  装饰 = 包里给 `kind="none"`；
- Variant 覆盖逐属性照常（`at.mobile="top-left,bottom-right"` 之类），`at` 集合变化走
  实例 reconcile（§6）。

## 6. 实现地图

### 6.1 节点机制

```
Btn
├─ __Surface                 （已有，恒 index 0）
├─ Label / 作者子节点
└─ Decor（控件节点：stretch 铺满、零尺寸、ignoreLayout、无 Graphic）
    ├─ __Decor:top-left      （DecorPanel 实例，锚于对应角/边）
    └─ __Decor:top-right
```

- 实例按 `at` token 懒建，**建后只 SetActive 切换、永不销毁**（Add 块 Strategy C），
  `at` / `kind` 的 Variant 来回切幂等。命名 `__Decor:<token>`，沿 `__Surface` /
  `__FocusCursor` 的库属节点惯例。
- **实例槽是双节点的（按需）**：`kind` 在 sprite ↔ SDF 间被主题 / Variant 切换时，
  `Image` 与 `DecorPanel` 不能同节点共存（`Graphic` 是 `[DisallowMultipleComponent]`），
  实例槽按需长出第二个子节点，两者只显隐不销毁 —— `__Surface` + 退位 Image 已经跑着的
  同一形状。只用一种画法的文档永远只建一个。
- 属性重放走 `OnBeforeApply` 清标记 → setter 重声明 → `OnAfterApply` reconcile ——
  与 `ProceduralSurface.BeginPass / Reconcile` 同一模式（「从当前声明推，不 latch」）。
- 实例 rect：锚点钉在角 / 边中点，sizeDelta = 装饰包围盒；glow 的几何膨胀在
  `DecorPanel` 网格里做（照搬 `ProceduralPanel` 的 quad 膨胀），不动 rect。

### 6.2 渲染

新 `DecorPanel : MaskableGraphic`（`Controls/Internal/`）+ `UI-Decor.shader`：

- **SDF**：bracket = 两条线段的 min 减半笔宽（L 折线描边）；tick = 等腰三角形（解析
  SDF 现成）；line = 既有圆角盒。`sdSegment` / `sdTriangle` 进 `UI-PanelSDF.cginc`
  共享（AA 惯例一致）。
- **合成**：fill（纵向渐变）+ glow，照抄面板 shader 的合成段与 `PuguiOver`。
  **无 border**（描边的描边没有语义）、**无 glass**（内层不给玻璃的同一条理由：同一张
  backdrop 采两遍，见 procedural-surface spec §6）。也因此**不需要法线** —— 装饰实现量
  里最重的一块（`PuguiSdNormal` 那类）整个不存在。
- **标准 uGUI 块照带**：`_Stencil` 全套 + `UNITY_UI_CLIP_RECT` —— 装饰要能被祖先
  mask / RectMask2D 正常裁剪。装饰自己**不做**遮罩源（`mask=` 不在属性表里）。
- **材质缓存**：`DecorParams` key + 引用计数，复用 `ProceduralMaterialCache` 的模式
  （独立字典还是泛型化由 plan 定）。同参数实例共材质 → 四个括号可合批成一个 draw。
- **Canvas 通道**：半尺寸走 TEXCOORD1，`EnsureCanvasChannels` 的既有做法照搬。
- **sprite 实例不走以上任何一条**：就是普通 `UnityEngine.UI.Image`（无自定义 shader、
  不进 `DecorParams` 材质缓存），`UI.ResolveSprite` 解析、tint 走 `Image.color`、
  native 尺寸 + pixel-snap 全部既有体系。同图集可与其他 UI 合批。

### 6.3 数据流与接线

| 层 | 文件 | 改动 |
|---|---|---|
| 解析 | `Runtime/Core/Parser/DecorSpec.cs`（新） | kind / at / size / inset / offset / mirror 的纯 C# 解析，CLI lint 直接编译（同 `RadiusParser` 地位）。`sprite` 值是不透明键，解析期只查非空 |
| 控件 | `Runtime/Controls/Decor.cs`（新） | 属性 setter + 实例 reconcile（含 sprite ↔ SDF 双节点显隐与镜像变换）；`GetNativeSize() → null` |
| 渲染 | `Controls/Internal/DecorPanel.cs` + `UI-Decor.shader`（新）、`UI-PanelSDF.cginc`（+2 个 SDF） | §6.2 |
| 注册 | `Application/BuiltinPrimitives.cs` + `Core/Lint/BuiltinTags.cs` | 各一行；`BuiltinTagsTests` 守卫镜像 |
| lint | `Core/Lint/DecorRules.cs`（新） | §7 |
| XSD | `Editor/XsdGenerator.cs` | 新标签自动进 schema（反射注册表），确认叶子约束表达 |

`color` / `glow` / `glowColor` 复用 `UI.Theme.Resolve` 与渐变解析的现有调用形态
（`ProceduralControl.cs` 的 setter 同款），不新写颜色路径。

## 7. lint 规则

| 规则 | 遍 | 级别 |
|---|---|---|
| `PUI-DECOR-KIND`：`<Decor>` 最终没有 `kind` | expanded（class 合并后才判，主题包可能供值） | error |
| `PUI-DECOR-LAYOUT-ATTR`：`<Decor>` 上写排版属性（`anchor` / `width` / `height` / `margin` / `flow` —— 注意 `size` 不在列：Decor 的 `size` 是装饰尺寸，与通用排版 `size` 同名不同义，XSD/SKILL 要写清） | raw | error |
| `PUI-DECOR-SPRITE`：`kind="sprite"` 而最终没有 `sprite=` | expanded（同上，主题包可能供值） | error |
| `PUI-DECOR-ATTR`：kind 不适用的属性（tick+`thickness`、bracket+`offset`、sprite+`glow`/`thickness`、非 sprite+`sprite`/`mirror`） | expanded | warning |
| 语法类（未知 kind / at 不匹配 / `%` 越界 / 重复 token） | parser（两遍都见） | error |

## 8. SKILL 更新（每个落地里程碑的同一 PR 内）

- 主 `SKILL.md`：原语目录加 `<Decor>` 行 + stub 指针。
- 新建 `reference/decor.md`：属性表、四种 kind 的观感示意、`<Show>` 组合示例、
  `kind="none"` 主题惯例、「写在内容后压在内容上」的排序说明、sprite 规范锚点与
  自动镜像的配图说明。
- `authoring-promptugui-pxl/SKILL.md` 补一句交叉引用：装饰素材（角饰 / 边饰）按
  规范锚点作画，摆放交给 `<Decor kind="sprite">`。
- **`CLAUDE.md` 的 reference 路由表加一行**（`<Decor>` → `reference/decor.md`）。
- C# SKILL 不动（无新公共 API；`Get<Decor>` 走既有泛型）。

## 9. 成本

| | 开销 |
|---|---|
| 不用 `<Decor>` 的文档 | **零** —— 新标签不写不挂任何东西 |
| 一个 `<Decor>` 节点 | 1 个容器 GameObject + N 实例（GameObject + CanvasRenderer）；同参数实例共材质可合批 |
| `kind="none"` / Show 隐藏态 | 实例保留、SetActive(false)，不出几何零 overdraw（`ProceduralPanel.ComputeVisible` 同款剔除） |
| shader | 无 border / 无 glass / 无法线 —— 比面板 shader 轻一档 |
| sprite 实例 | 普通 `Image`，无自定义材质；同图集与其他 UI 合批 |

## 10. 已定的决策（2026-08-27 与作者对齐）

1. **子元素 `<Decor>`，不做宿主属性。** 状态门控 / 主题化 / Variant 全部复用既有机制；
   主题去装饰走 `kind="none"`。
2. **v1 kind = bracket + tick + line。** dot / diamond（铆钉类）SDF 都现成、增量成本低，
   但参考图没用到，语法按可追加设计。
3. **v1 带 `glow` / `glowColor`。** 参考图括号的晕开是观感底线；合成框架复用后成本低。
4. **rect 锚定，不贴合 SDF 轮廓。** 参考图两个用例都不受影响；贴合轮廓推迟（§2）。
5. **`at` token 沿用 `AnchorPreset` 词汇**（`top-left` 等），不发明 `tl/br` 缩写 ——
   与 `anchor=` 一致的词汇表对 LLM 是一份记忆而不是两份。（讨论时预览里写的是缩写，
   定稿改为长词，此处如实记录。）
6. **tick 尖端恒朝外。** 翻转属性等需求出现再加。
7. **无 border / 无 glass / 装饰不当遮罩源。** 理由见 §6.2。
8. **`kind="sprite"`（2026-08-27 追加对齐）。** 两条理由：贴图美学主题（像素等）的装饰
   观感 SDF 给不了 —— 主题包整槽换 kind，装饰跟着主题换皮；任意自定义形状的逃生舱，
   复用 `.pxl` 已验证的作画闭环。镜像约定默认开、`mirror="false"` 退出。与仓库定位
   调整同步（主打程序化高清表面，贴图美学是备选主题方向 —— CLAUDE.md 同日更新）。

## 11. 开放问题（留给 plan / 实现期）

- bracket 折线端头：方头还是圆头（`sdSegment - t/2` 天然圆头；参考图观感偏方头，
  可能要 `max` 分量距离改方头）—— render 对比后定。
- `DecorParams` 材质缓存与 `PanelParams` 是否泛型化合并 —— 纯实现取舍。
- line 的 `%` 长度在宿主 rect 为零帧（首帧布局前）的解算时机 —— 跟随 pill 的思路
  在 shader 里按半尺寸解，还是 `OnRectTransformDimensionsChange` 里改 rect；plan 定。
- 边类 sprite 装饰的 tiled 重复：tiled 提示的 `.pxl`（2026-06-12 spec，importer 已支持）
  + `Image.type=Tiled` 沿边重复链条 / 藤蔓纹样 —— 贴图边饰的正统画法。v1 不做，甜点后补。

## 12. 里程碑拆分（草案）

| | 内容 | 依赖 |
|---|---|---|
| **M0 Red** | `DecorSpec` 解析全矩阵（§4 逐条）+ 排版中立契约（ignoreLayout / GetNativeSize / Show 透传）| 无 |
| **M1 打通** | 标签接线 + 实例机制 + **line**（SDF 现成，最小可渲染）+ 材质缓存 + render 基线；SKILL 目录行同 PR | M0 |
| **M2 刚需形状** | bracket + tick SDF + glow 膨胀 + render tests（参考图三件套复刻）；`reference/decor.md` 同 PR | M1 |
| **M3 sprite kind** | `sprite` / `mirror` 属性 + 实例双节点 + 镜像 / 旋转变换 + render tests；`reference/decor.md` 的 sprite 节与 pxl SKILL 交叉引用同 PR | M1 |
| **M4 收尾** | lint 全部（§7）+ `kind="none"` / 主题切换（含 sprite ↔ SDF 整槽切换）/ Variant reconcile 集成测试 + CLAUDE.md 路由行 | M1 |
