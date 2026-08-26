# 主题驱动样式：`<Theme>` 承载 style pack + 运行时重合并

**日期**：2026-08-26
**状态**：设计中，未实施。
**作用域**：把 `<Style>` 属性包纳入 `<Theme>` 的管辖范围——`<Theme>` 从"只有颜色 token"扩展为"颜色 token + 命名属性包"。因为 class 是**属性宏**（对任何控件的任何属性生效），主题的表达力随之从"换配色"跃迁到"换皮肤"：sprite、圆角、描边、字号、内边距、玻璃参数都能整套替换。代价是 class→属性的合并必须从"展开期一次性"变成"主题切换时可重跑"。
**关联**：`<Style>` / `class=` 机制与合并优先级见 [procedural-style](2026-08-23-procedural-style-design.md) §3–§4，本设计不改那套语义，只加一层作用域；颜色 token 与 `base=` 链见 [color-tokens](2026-05-28-color-tokens-design.md)；主题切换触发 `Screen.ReSolve` 的既有链路见 `Runtime/Application/Screen.cs:207`。本设计**依赖** §9 的 lint 改造才能安全落地（两条新约束是静态约束，运行时查不出来）。

---

## 1. 背景与目标

今天 `<Theme>` 只管颜色：

```xml
<Theme name="dark" base="light">
  <Color name="surface" value="#111318"/>
</Theme>
```

换主题只能改颜色值。但界面的"皮肤感"大部分不在颜色上——像素 9-slice 的底图、圆角半径、描边粗细、字号、玻璃磨砂度，这些今天只能靠作者手写 Variant 或者干脆做两套 XML。

而 `<Style>` 已经是一个**对任何属性生效**的属性包。把它接进 `<Theme>`，主题就自动获得了整个属性面的表达力，不需要为每种视觉维度单独发明 theme token 类型：

```xml
<Style name="card" sprite="ui:panel" color="surface/0.85" radius="16" borderWidth="1"/>

<Theme name="pixel">
  <Style name="card" sprite="px:panel" radius="0" borderWidth="2"/>
</Theme>
```

`<Frame class="card">` 在 `pixel` 主题下自动换成像素底图、直角、粗描边。

**设计约束**：

1. **不改 `<Style>` / `class=` 的既有语义**。合并优先级（inline > 右 class > 左 class、按属性名原子）、"不适用即忽略"、commons 共享 / `as` 命名空间 / 热重载——全部原样。本设计只在查找 `StyleDef` 时多插一层主题作用域。
2. **不新增 ReSolve**。主题切换今天就已经触发全量 `Screen.ReSolve`；本设计只在那次 ReSolve 之前插一步"重算属性字典"。
3. **零成本回归**。不写 `<Style>` 进 `<Theme>` 的工程，代码路径与今天等价（没有 theme-scoped pack → 没有重合并）。
4. **静默的错等于没有错**。主题切换后"某个角变不回去"这类残留是无法 debug 的，所以本设计的两条约束（§6.1 / §6.2）必须由 lint 静态拦截，而不是文档口头约定。

## 2. 现状盘点（含实测基线）

三层机制今天各自的形态：

| 层 | 今天在哪跑 | 主题切换时 |
|---|---|---|
| 颜色 token | 属性 apply 期解析（`ColorSpec` → `ThemeStore.LookupChained`） | ✅ 随 ReSolve 重解析 |
| `class=` 合并 | 展开期一次性（`TemplateExpander` → `StyleMerger.Apply`），展开产物**不含 `class`** | ❌ 已经烘死 |
| 属性 apply | `ControlAttributeApplier.Apply`，初次实例化与 ReSolve 共用 | ✅ 全量重放 |

**实测基线**（host 工程 EditMode / Mono，200 个 `class="card"` 的 `<Frame>`）：

| 项目 | 耗时 |
|---|---|
| `class` 合并开销（有 class vs 同等属性内联，展开产物相同） | **0.449 ms**（≈2.2 μs/节点） |
| 一次 `Screen.ReSolve()`（VStack 内） | **31.5 ms** |
| 一次 `Screen.ReSolve()`（无 LayoutGroup） | **1.97 ms** |
| 合并占一次 ReSolve | **1.47 %**（LayoutGroup 内）／ **23 %**（无 LayoutGroup，绝对值仍 <0.5 ms） |

0.449 ms 是**上界**：现在的 `StyleMerger.Apply` 每节点都 `CloneWithoutClass`（新建 ElementNode + 拷贝全部字典）；运行时重合并不需要克隆。

**顺带记录**（不在本 spec 范围，另开 issue）：ReSolve 在 LayoutGroup 内是 O(N²)——100/200/400 节点分别 8.7/31.5/118.2 ms；同样节点数换成普通 `<Frame>` 父节点是 1.1/2.0/4.3 ms，完全线性。二次项来自 LayoutGroup 的重复 rebuild，与程序化属性无关（裸 Frame 也一样二次）。

## 3. 语法：`<Theme>` 内嵌 `<Style>`

`<Theme>` 的子元素从「只能是 `<Color>`」放宽为「`<Color>` 或 `<Style>`」。语法与顶层 `<Style>` 完全一致（属性包、无子节点、name kebab-case）：

```xml
<!-- 全局基线：定义完整属性集合 -->
<Style name="card"    sprite="ui:panel" color="surface/0.85" radius="16" borderWidth="1" borderColor="stroke/0.15"/>
<Style name="btn-cta" sprite="ui:btn"   pressedSprite="ui:btn-down" fontSize="15" radius="8"/>

<Theme name="modern">
  <Color name="surface" value="#f7f8fa"/>
  <Color name="stroke"  value="#1b1e28"/>
</Theme>

<Theme name="pixel" base="modern">
  <Color name="surface" value="#e8d8b0"/>
  <Style name="card"    sprite="px:panel" radius="0" borderWidth="2"/>
  <Style name="btn-cta" sprite="px:btn" pressedSprite="px:btn-down" fontSize="12" radius="0"/>
</Theme>
```

`class="card"` 的写法不变。`<Screen>` 依然不接受 `class`。

**为什么内嵌而不是 `<Style theme="pixel">`**：`<Theme>` 已经是一个作用域块，`<Color>` 就在里面；`base=` 链、`ThemeStore` 的注册 / 热重载 / 跨文件重名检测全都按块走。内嵌复用这一整套，`<Style theme=...>` 则要为样式单独造一遍。

## 4. 解析与查找

### 4.1 IR

`ThemeBlock` 增一个字段，与 `Colors` 并列：

```csharp
public sealed class ThemeBlock
{
    public string Name;
    public string BaseName;
    public List<ColorEntry> Colors = new();
    public Dictionary<string, StyleDef> Styles = new();   // 新增
}
```

`ParseTheme` 的子节点分支从 `if (child.Name != "Color") throw` 改为 `Color` / `Style` 双分支，`Style` 分支直接复用现有 `ParseStyle` 的属性解析（抽成一个 `ParseStyleAttributes(el, name, forbidden)` helper，顶层 `<Style>` 与主题内 `<Style>` 共用，只是禁用名单不同——见 §6.3）。

`ThemeStore.Entry` 同步增 `Styles`，`Register` / `ReplaceFromSrc` 一并搬运。

### 4.2 有效 pack：全局样式是每条主题链的隐式根

查一个 `class="card"` 时，有效属性包 = **从根到叶依次折叠**：

```
全局 <Style name="card">  →  base 链最上游主题的 card  →  ...  →  当前主题的 card
```

折叠用**与 `StyleMerger` 多 class 折叠完全相同的原子规则**：后来者声明的每个属性名（`radius` 与 `radius.mobile` 视为同一个名字）整体屏蔽先前的同名声明，然后写入自己的值。实现上直接复用 `StyleDef.DeclaredNames` + 现有折叠循环，不新写一份。

这个"全局样式当隐式根"的决定有三个直接好处：

1. **零成本回归**。不写主题内 `<Style>` 的工程，链只有一环，结果与今天逐字节相同。
2. **§6.1 的残留问题基本自动消失**。全局样式提供了完整的属性名基线，主题只覆盖值；某个主题漏写 `radius` 时会回落到全局值，而不是"没有值"。
3. **作者心智模型简单**：全局 `<Style>` 是"这个组件长什么样"，主题内 `<Style>` 是"在这套皮肤下哪几个参数不一样"。

查不到任何一环 → 沿用现有的 `unknown style` 硬错误。

### 4.3 共享 / 命名空间 / 热重载

全部沿用 `<Theme>` 现有语义：主题跟着 `<Import>` 走、进 commons、跨文件同名硬冲突、`ReplaceFromSrc` 热重载。主题内 `<Style>` 不参与 `as=` 命名空间——它按主题名寻址，`class` 引用的是**样式名**，`ui:card` 这种带命名空间的引用查的仍然是全局 / commons 池。

## 5. 运行时重合并

### 5.1 展开期改动

`StyleMerger.Apply` 今天做两件事：合并 pack、删掉 `class`。改为：

- **保留** `class` 属性到展开产物（下游只有本机制读它，`ControlAttributeApplier` 已经会跳过不认识的属性名，无副作用）；
- 在节点上额外记录**作者内联写的属性名与值**：`ElementNode.InlineAttributes` / `InlineVariantOverrides`（仅带 class 的节点分配，其余为 null）。

`Attributes` / `VariantOverrides` 的最终内容不变，所以 `ScreenInstantiator` 与 `ControlAttributeApplier` **一行都不用改**。

### 5.2 重合并点

```
UI.Theme.Current = "pixel"
        │
        ├─ 遍历 UI._docs 的展开树：对每个带 class 的节点
        │     Attributes      = InlineAttributes      ∪ EffectivePack(class, newTheme)
        │     VariantOverrides = InlineVariantOverrides ∪ pack 的 .variant 部分
        │     （inline 优先，按属性名原子——与 StyleMerger 同一段代码）
        │
        └─ Theme.Changed → 每个 open Screen 的 ReSolve()（既有链路，不改）
```

在**文档级**而非 Screen 级重合并：同一份展开树可能被多个同时打开的 Screen 共享（`_nodeMap` 的 key 是共享的 ElementNode），主题又是全局的，所以按文档走一遍即可，不会重复劳动。

`ScreenInstantiator` 为 `BindItems` 克隆出来的动态子树（`_dynamicSubtrees`）不在 `_docs` 里，需要一并遍历——ReSolve 已经有这个遍历（`ApplyScalesTo`），复用同一份子树列表。

### 5.3 代价

按 §2 的实测：一次主题切换从 31.5 ms 变成约 32 ms（200 节点，LayoutGroup 内）。稳态与 `UI.Open` 路径零新增开销。**性能不是这个特性的成本项**，成本在下面三条约束和 §9 的 lint 改造。

## 6. 三条约束

### 6.1 属性名集合一致（残留问题）

`ControlAttributeApplier.cs:58-70` 遍历的是 `node.Attributes.Keys ∪ node.VariantOverrides.Keys`，解析不出值就 `continue`。**属性在新状态下"没有值"时 ReSolve 根本不碰它，控件保留旧值。**

今天用 Variant 就能复现（与主题无关的既有行为）：

```xml
<Image id='v' color.alt='#445566'/>                   <!-- 只在 variant 里声明 -->
<Image id='b' color='#112233' color.alt='#445566'/>   <!-- 有基础值 -->
```
```
alt=false (初始)   v=#FFFFFF   b=#112233
alt=true           v=#445566   b=#445566
alt=false (回来)   v=#445566   b=#112233   ← v 卡住
```

同形的还有 `Control.cs:357` 的 `if (hidden.HasValue) Hidden = ...`，以及 `Btn.cs:233` 的 `_pressedSpriteAuthored = true` 闩锁（一旦某主题写过 `pressedSprite`，之后省略它的主题拿不回内置按下效果）。

**约束**：对任意样式名 `s`，所有可能激活的主题解析出的 `EffectivePack(s, T)` 必须有**相同的属性名集合**。

§4.2 的"全局样式当隐式根"让绝大多数情况自动满足；剩下的漏网之鱼（某属性只在部分主题的 pack 里出现、全局样式没有基线值）由新 lint 规则 `PUI-THEME-STYLE-SHAPE` 拦截，报错信息直指"把 `radius` 加进全局 `<Style name='card'>` 作为基线，或在每个主题里都声明"。

**不采用**的替代方案：让 ReSolve 对"消失的属性"回放控件默认值。那需要 237 个 `[UIAttr]` setter 各自定义"复位语义"，是本设计 10 倍的工作量，而且很多属性（`mask` / `type` / `fit`）根本没有干净的"默认"。

### 6.2 theme-scoped style 不参与 `if=`

`TemplateExpander.cs:259` 在**展开期**求值 `if=`，false 的节点 `return null`，展开产物里根本不存在。主题要让它重新出现只能重新展开 + 重建 GameObject。

**约束**：theme-scoped style 不得（直接或经由模板 Param）驱动 `if=`。

分两处拦截：

- 节点自身：`<Frame class="skin" if="...">` —— `if` 已经在 `StyleForbiddenAttrs` 里，pack 里带不了 `if`，无需新增。
- 经由 Param：`<ui.Card class="skin">` 且 `skin` 提供的 Param 被模板体的 `if="{{p}}"` 消费 → 新 lint 规则 `PUI-THEME-STYLE-IF-PARAM`。

现状勘察：项目里现有 `.ui.xml` **零处**使用 `if="{{...}}"`（两种引号都查过），所以这条约束现在不损失任何东西。

### 6.3 theme-style 专用黑名单

顶层 `<Style>` 的禁用名单是 `{ id, if, class, bind }`（`UIDocumentParser.cs:324`）。主题内 `<Style>` 在此之上再禁两组：

```csharp
private static readonly string[] ThemeStyleForbiddenAttrs =
{
    "id", "if", "class", "bind",                    // 与顶层 <Style> 相同
    "text", "isOn", "value", "current",             // 运行时独占状态
    "mask", "showMask", "maskPadding",              // 已声明不支持按状态切换
};
```

**运行时独占状态**（`text` / `isOn` / `value` / `current`）：`ControlAttributeApplier` 的 `DefaultTextLockedByRuntime` / `RuntimeStateLockedByRuntime` 会在"运行期被代码改过"时**跳过重放**，所以主题写进去的新值大概率被静默吞掉——比不生效更糟的是不确定地生效。这四个名字来自 `BuiltinPrimitives.Register` 的 `defaultTextAttr` / `runtimeStateAttr` 声明。

**mask 家族**：`PUI-MASK-VARIANT`（`MaskAttributeRules.CheckVariantOverrides`）与 `PUI-PROG-MASK-VARIANT` 已经把"按 Variant 切换 mask 模式"整体判为 v1 不支持，理由正是"requires AddComponent / Destroy which has performance / lifetime issues"。主题切换走的是同一条重放路径，所以是同一件不支持的事换了个入口，必须一并挡住——否则作者会绕过一条已有的 lint 规则。

自定义控件若注册了名单外的 `runtimeStateAttr`，`ControlRegistry.Register` 增一条 `Debug.LogWarning`，提示该属性不应经由主题样式提供。（不做成硬错误：Core 层的 parser 不该反向依赖 Registry。）

## 7. 模板边界

`class=` 出现在三个位置，主题跟随能力不同：

| 位置 | 展开期行为 | 跟随主题 |
|---|---|---|
| 模板**体内**的节点 | `TemplateExpander.cs:273` → `StyleMerger.Apply(prepared, styles, null)`，**不过滤**，与普通节点同一行代码 | ✅ 完全一样 |
| 调用节点的 class → **公共属性**那半 | `ExpandInvocation` 拷到实例根的 `Attributes`（`TemplateExpander.cs:229-231`） | ✅ |
| 调用节点的 class → **Param** 那半 | `Substitution.Apply` 做字符串替换，烘进模板体的属性值 | ⚠️ 见下 |

Param 那半**技术上可解**：`ElementNode.AttributesRaw` 已经存了任何含 `{{...}}` 的属性的替换前原文（`UIDocumentParser.cs:497-502`），`ElementNode.TextArgs` 存了整份实参（`TemplateExpander.cs:288`），i18n 的 locale 切换正是拿这两样重新替换的（`ControlAttributeApplier.cs:76`）。主题重替换可以走同一条路。

**M1 不做**：调用节点上使用 theme-scoped style 且该 style 提供了 Param → lint 报 `PUI-THEME-STYLE-TEMPLATE-PARAM`，引导作者把 class 下沉到模板体内，或改用全局 `<Style>`（全局样式在调用点照常工作，只是不跟随主题）。M2 再打开（§14）。

安全性记录：`id` **不参与替换**（`SubstituteAttrs` 里是 `Id = src.Id`，只有 `Attributes` / `VariantOverrides` 过 `Substitution.Apply`；`id` 在 parser 阶段就 `continue` 掉、从不进 `Attributes`）。所以主题永远改不到 id，`_byId` / `Get<T>` 的路径不受影响。

## 8. Setter 可逆性（既有缺陷，本 spec 只钉 Red test）

§6.1 是"属性没有值"的问题；这一节是"属性有新值但 setter 不还原旧值"的问题。

对 `Runtime/Controls/` 下全部 **237 个 `[UIAttr]` 属性**做了逐个审计，不可逆的有 6 处，但要分成两类——**只有第一类是缺陷**：

**A. 真缺陷（无任何规则覆盖，作者写出来既不报错也不工作）**

| 属性 | 问题 | 位置 |
|---|---|---|
| `Tab.selectedSprite` | 给 sprite 时把 `transition` 设成 `None`，改回 `none` 不恢复 `ColorTint` —— 该 Tab 从此没有任何 hover/press 反馈，且没有任何属性能救回来 | Tab.cs:277 / 388 |
| `Progress.bg` / `Progress.frame` | `SetActive(true)` 无反向路径，`""` 直接 early-return | Progress.cs:94 / 118 |

**B. 已声明的不支持（不是缺陷）**：`Frame.mask` / `Image.mask` / `RawImage.mask` / `Progress.mask` 的 `AddComponent` 只加不减是真的，但 `PUI-MASK-VARIANT` 与 `PUI-PROG-MASK-VARIANT` 已经把按状态切换 mask 整体判为 v1 不支持，理由与我们发现的完全一致。所以它们不该"修"，而该**保持不支持并堵住新入口**——见 §6.3 把 mask 家族加进主题黑名单。

**C. 顺带发现的两处覆盖缺口（M0 一并钉住）**

| 缺口 | 说明 |
|---|---|
| `RawImage` 的 mask 家族无人管 | `IRWalker` 只为 `Frame` / `Image` 分发 `MaskAttributeRules`；而 `VariantBaseRules` 又把 mask 家族整体排除（注释写"Owned by PUI-MASK-VARIANT in ALL cases"）。于是 `<RawImage mask='rect' mask.alt='self'/>` **两道闸门都漏**，静默接受、静默损坏 |
| `PUI-PROG-MASK-VARIANT` 的文案有误 | 消息明说 "other attrs (value / fill / **bg** / mode / direction) are safe in variants"（`ProgressAttributeRules.cs:72`），但 A 类已证明 `bg` 不安全。修 `Progress.bg` 时必须同步改这句 |

实测确认（今天的代码，纯 Variant，不需要任何新特性）：

```
<Frame mask='rect' mask.alt='none'>  →  翻转后 RectMask2D 仍在
<Image mask='self' mask.alt='rect'>  →  翻转后 RectMask2D + Mask 双份
<Image color=…     color.alt=…>      →  翻转后正常（对照组）
```

误报、实际可逆的：`Frame.weld`（`weld<=0` 会 `SetWeld(0)`，源码注释明写是为 Variant 准备的）、`Icon.name`、`Btn.text` / `Tab.text`、`Tab.sprite`、`Toggle.group`、`Markdown.*Color`、`Image.showMask`——空值都有明确复位分支。

**A / C 两类都是既有 bug，两行 XML 用 `.variant` 就能复现，不记在本特性账上。** 本 spec 的动作是：**先写 Red test 钉住正确行为**，修复另开 PR。这样主题特性上线时，这批用例要么已绿、要么明确挂着可见，不会变成"主题一换就有人报奇怪的 bug 但没人知道根因"。

Red test 的写法：每条都拿**翻转后的控件**对比**用目标值全新构建的控件**，而不是对比硬编码期望值——因为要断言的性质正是"A→B 之后必须等同于一开始就用 B 构建"，这也正是主题切换的正确性定义。B 类另配一条**绿色的特征化测试**，把"mask 就是不可逆"钉成显式契约，将来谁真把它做可逆了会看到这条测试失败，从而知道要一并撤掉 `PUI-MASK-VARIANT`。

## 9. Lint：让 CLI 追平 runtime（本设计的前置依赖）

### 9.1 现状

`IRWalker.Walk` 走的是**解析后、展开前**的原始 IR。因此：

- **模板体**被检查了，但以"声明空间"身份，且为此**主动关掉了 5 组规则**：`NavTargetRules.CheckNavTarget`（无 screen id 集合）、`StateTriggerRules.CheckStateSource`（可点击祖先在调用点才定）、`<Tab>` 父节点检查、`LayoutGroupChildRules.CheckNonLayoutChild`（模板体根的真实父节点在调用点才定）、`VariantBaseRules`（实例根可能从调用点拿到 CommonAttr 基线）。
- **模板调用点从来没被检查过**：tag 不匹配任何 per-tag 规则，Param 值不验证，Slot 子节点按"未知父节点"判。
- **style** 靠 `StyleAttributeView` 在原始 IR 上模拟一次 merge；查不到的 style 名（几乎必然来自 `<Import>` 的 commons 库，单文件 CLI 看不见）→ `IsUncertain` → 规则闭嘴。

而 `ScreenInstantiator` 走的是 `TemplateExpander.Expand` **之后**的树，7 组规则（`LayoutGroupChildRules` / `MaskAttributeRules` / `ImageFitRules` / `ProgressAttributeRules` / `TabRules` / `CarouselRules` / `NavTargetRules.CheckNav`）已经作用于展开后的值。

**结论：runtime 查展开后，CLI 查展开前。** 把 CLI 挪到展开后，模板检查是顺带白拿的，`IsUncertain` 的沉默模式基本可以退休——主题样式让 lint 变弱的问题因此自然消失，不需要单独想办法。

### 9.2 挡路的是依赖，不是规则

`.lint/UIXmlLint/UIXmlLint.csproj` 的注释已经写明：Template/Variants 被排除是因为它们 type-reference `PromptUGUI.Application`，"even though the algorithm is pure"。实查：

```
Runtime/Core/Template/StyleMerger.cs      -> Application.DocumentLoader.StyleKey
Runtime/Core/Template/TemplateExpander.cs -> Application.DocumentLoader.{LoadedDoc, StyleKey, TemplateKey}
```

零个 `UnityEngine` 引用。`DocumentLoader` 本身除了 `Awaitable` 异步管道，整个 body 就是 `UIDocumentParser.Parse` + 字典合并 + 冲突检测，纯 C#。

### 9.3 三步

1. **搬三个类型**：`LoadedDoc` / `TemplateKey` / `StyleKey` → `Core/IR/`。搬完 `Core/Template/**` 整个目录进 CLI 编译集。`VariantResolver` 不动——展开产物保留 `VariantOverrides`，variant 本来就是运行时解析。
2. **把 load/merge 算法从 IO 里剥出来**：`LoadInternalAsync` 现在把「递归 + resolver + 合并 + 查重」揉在一起。抽成 resolver 泛型的同步核心放 `Core/`，`DocumentLoader` 退化成 async 薄壳。**这是真正的工作量**，同时也是 `IsUncertain` 存在的根本原因。
3. **CLI 加文件系统 resolver**（相对路径解析 `src`），然后 `IRWalker.Walk(expandedDoc)`。

### 9.4 主题维度

CLI 能展开之后，`--theme <name>` / `--theme all` 按每个主题各展开一遍交叉验证。这是唯一能真正保住 `GlassRules` / `PureContainerVisualAttrRules` 的做法——否则一个节点在主题 A 下进 glass 模式、B 下不进，静态永远说不清。

新增三条规则：

| 代码 | 检查 |
|---|---|
| `PUI-THEME-STYLE-SHAPE` | §6.1：同一样式名在不同主题下解析出的属性名集合不一致 |
| `PUI-THEME-STYLE-IF-PARAM` | §6.2：theme-scoped style 提供的 Param 被 `if=` 消费 |
| `PUI-THEME-STYLE-TEMPLATE-PARAM` | §7：theme-scoped style 用在模板调用节点上且提供了 Param |

### 9.5 两遍都跑

展开后的树里 `if="false"` 的节点已不存在、`<Variant>` 的 Add 块也已分离。展开前那一遍能查作者写的死代码，展开后那一遍查真正会实例化的东西。保留两遍、按 `(Code, Id, Message)` 去重——CLI 不在热路径上。

**报错溯源**：`LintIssue` 现在是 `(Code, Tag, Id, Message)`，CLI 输出 `{path}: [{code}] {message}`，**本来就没有行号**，所以不存在"展开后行号错位"的损失。但跟进 `<Import>` 之后 issue 可能来自 commons 文件而非入口文件，需要在 `ElementNode` 上带 origin src + 模板调用链（`StyleDef.OriginSrc` 已是这个先例）。

## 10. 明确不支持（错误，非静默降级）

- 主题内 `<Style>` 携带 `id` / `if` / `class` / `bind`、运行时独占状态（`text` / `isOn` / `value` / `current`）、或 mask 家族（`mask` / `showMask` / `maskPadding`）→ parse error（§6.3）。
- 主题内 `<Style>` 带子节点、name 非 kebab-case、同主题内重名 → parse error，消息与顶层 `<Style>` 对齐。
- 同一样式名在不同主题下属性名集合不一致 → lint error（§6.1）。
- theme-scoped style 驱动 `if=`，或用在模板调用节点上提供 Param → lint error（§6.2 / §7）。
- `<Screen class=...>` → 沿用现有 parse error。

## 11. 不做的事（YAGNI 记录）

- **不做选择器 / 级联 / 继承树**。`<Style>` 依然是属性宏，主题只是给它加了一层作用域。
- **不做 setter 复位语义**。§6.1 用属性名集合约束绕开，而不是给 237 个 setter 定义"默认值"。
- **不做主题切换的 GameObject 重建**。`if=` 驱动的树形变化明确不支持，而不是"重建子树来支持它"。
- **不做主题内 `<Template>`**。模板是结构，主题是皮肤；结构随主题变是另一个量级的特性，没有需求前不开这个口子。
- **不做过渡动画**。主题切换是瞬时的属性重放；跨主题的补间需要属性级插值语义，另开 spec。

## 12. 测试（Red 先行）

EditMode（`UI.ResetForTests` 约定）：

1. **解析**：`<Theme>` 内 `<Style>` 进 `ThemeBlock.Styles`；§10 全部 parse error；`ThemeStyleForbiddenAttrs` 八个名字逐个报错。
2. **有效 pack 折叠**：全局样式当隐式根；`base=` 链上游→下游覆盖；原子性（主题声明 `radius.mobile` 屏蔽全局 `radius`）；主题不声明该样式时回落全局；三处都查不到时报 `unknown style`。
3. **运行时重合并**：`<Frame class="card">` 切主题后材质参数变化且 **GameObject 未重建**（对象引用比对）；inline 属性在切主题后仍然压过主题 pack；带 `.variant` 的 pack 在切主题 + 切 variant 的四种组合下取值正确。
4. **残留回归**（§6.1 的正面用例）：全局样式提供基线时，主题 A→B→A 往返后属性值回到初始。
5. **模板**：模板体内 class 跟随主题（正面）；调用点 class 的 CommonAttrs 半跟随主题（正面）；调用点 class 提供 Param 时 lint 报错（`PUI-THEME-STYLE-TEMPLATE-PARAM`）。
6. **commons / 热重载**：主题内 `<Style>` 入 commons 池、跨文件同名冲突、`ReloadCommonLibraryAsync` 换池后已打开 Screen 的视觉跟随变化。
7. **可逆性 Red test**（§8，M0 已交付，见 §15）：A 类三条 `.variant` 往返——`Tab.selectedSprite` 归零后 `transition` 回 `ColorTint`、`Progress.bg` / `frame` 置空后子节点 `activeSelf == false`；C 类一条 `<RawImage mask.alt=...>` 应报 `PUI-MASK-VARIANT`；B 类一条绿色特征化钉住 mask 的不可逆契约。全部以"翻转后 == 用目标值全新构建"的形式断言。
8. **主题黑名单**：§6.3 三组名字逐个 parse error；mask 家族被拒时的消息要指向 `PUI-MASK-VARIANT` 而不是只说"不允许"。
9. **Lint**（`PromptUGUI.Tests.EditMode`，规则是纯 C#）：三条新规则各正反用例；展开后 walk 与展开前 walk 的去重；跟 `<Import>` 后的 origin 归属。
10. **XSD**：`<Theme>` 允许 `<Style>` 子元素，substring 断言。

PlayMode：一条端到端冒烟——两个主题各带 `<Style name="card">`（不同 sprite + radius），`UI.Theme.Current` 来回切两次，断言材质参数与 sprite 引用跟随、GameObject 实例未变、无 console error。

性能守门：200 节点 `class` 树的主题切换耗时相对切换前基线不劣化超过 5%（用 §2 的测法）。

## 13. SKILL 更新（同 PR，英文）

- `authoring-promptugui-xml/SKILL.md`
  - **Style & class** 一节：加"theme-scoped style"小节——语法、全局样式当隐式根的折叠顺序、属性名集合一致约束、八项禁用属性、模板调用点的限制。
  - **Color Tokens** 一节：`<Theme>` 的子元素描述从"only `<Color>`"改为"`<Color>` / `<Style>`"，主表第 68 行同步。
  - 顶层元素表 `<Theme>` 行、`<Style>` 行同步。
  - 速查区（1181 行起）的 `STYLE/CLASS` 与 `STYLE LINT` 段补三条新规则代码。
- `scripting-promptugui-csharp/SKILL.md`：`UI.Theme.Current` 的描述从"重新解析颜色"改为"重新解析颜色**与样式包**"，并说明切换成本量级（§5.3）。
- `.lint/UIXmlLint/README.md`：展开后 lint、`--theme` 参数、两遍去重。
- 若 §7 的 M2 落地，再补模板 Param 跟随主题的说明。

## 14. 里程碑拆分

| | 内容 | 依赖 |
|---|---|---|
| **M0 Red test** | §8 的 A 类 3 条 + C 类 1 条 red test（`Ignore` 挂起），B 类 1 条绿色特征化测试。纯测试，可先合 | 无 |
| **M1 Lint 展开化** | §9.1–§9.3：搬三个类型、剥离 load/merge、CLI 文件 resolver、`IRWalker` 走展开树、两遍去重 | 无（与主题特性正交，独立有价值） |
| **M2 主题样式** | §3–§7：IR / parser / ThemeStore / 有效 pack 折叠 / 运行时重合并 / 三条约束 | M1（§9.4 的三条规则要 CLI 能展开） |
| **M3 模板 Param 跟随** | §7 的 raw + args 重替换，去掉 `PUI-THEME-STYLE-TEMPLATE-PARAM` | M2 |

M0 与 M1 都不依赖主题特性、都能独立合入主干。M1 是 M2 的硬前置：没有它，§6.1 / §6.2 两条约束只能靠文档口头约定，而它们的违规表现是"换主题后某个属性静默不还原"——正是最难 debug 的那一类。

## 15. 实施记录

### M0（分支 `test/attr-reversibility-red`）

审计期把"6 处不可逆"当成 6 个待修 bug 写进了 §8 的第一版；动手时才发现其中 4 处（mask 家族）早已有 `PUI-MASK-VARIANT` / `PUI-PROG-MASK-VARIANT` 把它判为 v1 不支持，理由与审计结论逐字一致。**为它们写"应该可逆"的 red test 会直接推翻一条既有设计决定**，所以改成：B 类保持不支持、加一条绿色特征化测试钉住契约，同时把 mask 家族补进 §6.3 的主题黑名单（同一件不支持的事，不能让主题成为新入口）。

顺带查出两处覆盖缺口（§8 C 类）：`RawImage` 的 mask 家族同时漏出 `MaskAttributeRules` 的分发与 `VariantBaseRules` 的豁免；`PUI-PROG-MASK-VARIANT` 的文案把 `bg` 宣传成 variant-safe。

产出：

| 文件 | 内容 |
|---|---|
| `Tests/EditMode/Controls/AttributeReversibilityTests.cs` | A 类 3 条 red（`Ignore`）+ B 类 1 条绿色特征化 |
| `Tests/EditMode/Lint/IRWalkerMaskTests.cs` | C 类 RawImage 覆盖缺口 1 条 red（`Ignore`）；顺手订正 `Walk_NonFrameNonImageTags_NoMaskIssue` 里"mask 只 Frame/Image 暴露"的过期注释 |

验证：临时摘掉 `[Ignore]` 跑过一遍，4 条全部失败且失败原因正确（`Assume` 守卫全过，证明失败来自缺陷本身而非 setup）；恢复 `[Ignore]` 后全量 EditMode 2250 条 **2246 passed / 0 failed / 4 skipped**。

`[Ignore]` 而非留红：全量跑必须保持是有效的回归信号。修复 PR 逐条摘 `Ignore` 即可。

**本机环境缺口**：没有安装 dotnet SDK，`dotnet format` 与 `UIXmlLint` / `PxlPreview` CLI 在此机器上跑不了。本次改动只有测试文件，已人工核对缩进 / 行尾空白 / 文件结尾换行与同目录既有测试一致。

### M1（分支 `test/attr-reversibility-red`，两个 commit）

§9.3 的三步全部落地，实测行为与设计一致。三处值得记的偏离 / 发现：

**1. `DocumentAssembler` 没有自立命名空间。** 设计里没写它住哪；第一版放 `PromptUGUI.Loading`，编译炸 30 处 —— 那个名字会**遮蔽公共 API `PromptUGUI.Application.Modals.Loading`**（C# 从内往外解析裸标识符，任何 `PromptUGUI.*` 里的 `Loading` 都先命中命名空间）。改放 `PromptUGUI.Template`（它产出的 `LoadedDoc` 正是 `TemplateExpander` 的输入），理由写进了文件头注释。

**2. 两遍走查下沉到 `Core/Lint/DocumentLinter`，而不是留在 `Program.cs`。** 起因是一个实打实的 bug：`count += WalkExpanded(path, doc, Report)` 会先读 `count`（0），再执行右侧（闭包里 `Report` 把 `count` 加到 1），最后 `0 + 0` 把增量覆盖掉 —— 打印了 3 条却汇总成 1 条，**「只有展开遍报错」的文档 exit code 会是 0**。CLI 没有自己的测试工程，这类错误没人挡得住。把逻辑搬进 Core 之后 `PromptUGUI.Tests.EditMode` 能直接测（`DocumentLinterTests`，6 条），`Program.cs` 只剩 I/O 与 src→path 猜测。

**3. 「解析不到的 import 不算错误」是新增的设计决定。** 设计里没考虑：Addressables / 自定义 resolver 的工程根本没有磁盘形态。若按缺失即报错处理，会把今天干净的文件变成构建失败。改为：整条闭包全部解析成功才跑展开遍，否则只跑 raw 并在 stdout 留一行说明。`DocumentLinterTests.UnresolvableImports_SkipExpandedPass_ButKeepRawRules` 钉住这条。

产出：

| 文件 | 内容 |
|---|---|
| `Runtime/Core/IR/DocumentKeys.cs` · `LoadedDoc.cs` | 从 `Application.DocumentLoader` 搬出的三个类型 |
| `Runtime/Core/Template/DocumentAssembler.cs` | Import 闭包合并语义（纯 C#） |
| `Runtime/Application/DocumentLoader.cs` | 缩成异步预取壳（151 行 → 80 行） |
| `Runtime/Core/Lint/DocumentLinter.cs` | raw + expanded 两遍 + 去重 + `PUI-EXPAND` |
| `.lint/UIXmlLint/{UIXmlLint.csproj,Program.cs,README.md}` | 编译集加 `Core/Template`；文件系统 resolver |
| `Tests/EditMode/Lint/DocumentLinterTests.cs` | 6 条 |

验证：EditMode 2256 → 2252 passed / 0 failed / 4 skipped（4 skipped 是 M0 的 red）；EditorOnly 308/308；`dotnet format --verify-no-changes --severity warn` 无输出；CLI 对仓库全部 13 个 `.ui.xml` 仍然零 issue（无回归），对专门造的 fixture 正确报出 3 条并 exit 1。新测试的有效性用「临时摘掉展开遍」验证过：6 条里 4 条转红，另 2 条（去重、无法解析时降级）本就该两边成立。

**尚未做**：§9.4 的 `--theme` 与三条主题规则属于 M2。

### M1 追加：origin 溯源（§9.5）

`ElementNode.OriginSrc` 由 `UIDocumentParser.Parse(xml, src)` 一次性打戳（解析后遍历整份文档，避免把 `src` 穿过十几个私有解析方法），并在展开期的全部 6 个 clone 点逐层传递（`TemplateExpander` ×5 + `StyleMerger.CloneWithoutClass`）。`LintIssue` 增 `Origin`，由 `IRWalker` **集中**打戳 —— 规则一行不用改，新规则自动获得正确归属。

一个不显然的点：`LayoutGroupChildRules` 这类规则在**父节点的帧**里运行却是在说**子节点**，所以不能继承父节点的 origin，那 5 处显式用 `child.OriginSrc`（`LintOriginTests.ChildTargetedRule_IsAttributedToTheChild_NotItsParent` 钉住）。`<Style>` 不是 ElementNode，用它自己已有的 `StyleDef.OriginSrc`。

CLI 端给 import 打戳时用的是**解析出来的磁盘路径**而非 `imp.Src`：`src` 是 resolver key，作者打不开；lookup 键仍用 `imp.Src` 不变。

效果：

```
$ UIXmlLint uses-tmpl.ui.xml
tmpl.ui.xml: [PUI-MASK-FRAME-SELF] <Frame id='card'>: mask="self" requires ...
```

**行号仍然没有**，本次也没做：`LintIssue` 从来就没有行号，`XmlDocument` 的节点默认不实现 `IXmlLineInfo`，要拿到得换一套读取方式。同一个模板被调用多次时，"哪一次调用"也仍不可区分（需要模板调用链，`TextArgs` 有原料但没接）。这两条都留给需要时再说。

验证：EditMode 2262 → 2258 passed / 0 failed / 4 skipped；EditorOnly 308/308；`dotnet format` 无输出；CLI 对仓库 13 个文件仍零 issue；跨文件 fixture 正确指向库文件。有效性用「摘掉模板内联时的 origin 传递」验证：6 条里 3 条转红，失败信息正是这个 bug 本身（`Expected: "skin.ui" But was: "main.ui"`）。

### M2.1 / M2.2（分支 `test/attr-reversibility-red`）

**路线调整（§4.2 / §5 的实现顺序）**：设计里主题 pack 是在**展开期**折进 `class=` 的，M2.3 的运行时重合并再负责后续切换。实现时发现这会有两条路径做同一件事，而且展开期那条拿不到 commons 主题 —— `LoadCommonLibraryAsync` 注册的主题在 `ThemeStore` 里，不在后续某次 `LoadedDoc.Themes` 中。改为：**展开期照旧只合全局 pack，主题层完全由重合并那一步负责**（它本来就要在 Open 时跑一次）。一条路径，`ThemeStore` 是唯一真相源，M2.4 的 `--theme` 也复用同一个函数。

**§4.3 收紧为明确规则**：设计里"主题内 `<Style>` 不参与 `as=` 命名空间"写得含糊。落实为：**主题样式只对 `StyleKey(null, name)` 生效** —— `as="ui"` 导入的 commons 库其样式键是 `StyleKey("ui", name)`，主题没有自己的命名空间去匹配，所以「给带命名空间的样式换肤」目前不支持。写进了 `ThemeStyleResolver` 的注释与测试。要支持得先给 `<Theme>` 一个命名空间概念，另议。

产出：

| 文件 | 内容 |
|---|---|
| `Runtime/Core/IR/ThemeBlock.cs` | `Styles` 字段 |
| `Runtime/Core/Parser/UIDocumentParser.cs` | `ParseStylePack` 抽取共用；`<Theme>` 接受 `<Style>`；`ThemeStyleForbiddenAttrs` + 分组理由文案；`StampOrigin` 顺带给 `StyleDef` 打戳 |
| `Runtime/Application/ThemeStore.cs` · `UI.cs` | 携带主题样式；`Register` 加只有颜色的兼容重载（既有 ~20 处调用点不动） |
| `Runtime/Core/Template/ThemeStyleResolver.cs` | 有效 pack 折叠：全局当隐式根 → base 链 root-first → 激活主题；按属性名原子；无主题/无主题样式时原样返回不分配 |
| `Tests/EditMode/Parser/ThemeStyleParsingTests.cs` · `Template/ThemeStyleResolverTests.cs` | 22 + 11 条 |

验证：EditMode 2295 → 2291 passed / 0 failed / 4 skipped；`dotnet format` 无输出；UIXmlLint 13 个文件零 issue。

**剩余**：M2.3（保留 `class` + inline 快照、`ThemeStyleApplier` 重合并、接 Open 与 `Theme.Changed`）、M2.4（三条 lint 规则 + `--theme` + SKILL）。

### M2.3（分支 `test/attr-reversibility-red`）

运行时重合并落地。**实现方式与 §5.1 设计的不同**：设计里是给节点存一份「作者内联属性快照」，重合并时用「快照 ∪ 新 pack」重建。实现时发现那样会**丢掉模板调用点合并到实例根上的公共属性** —— `ExpandInvocation` 在 `StyleMerger` 之后才把 invocation 的 CommonAttrs 写进 `instanceRoot.Attributes`，它们既不在快照里也不在 pack 里。

改为记 `ElementNode.StyleAttrNames`：**上一次 pack 贡献了哪些属性名**。重合并 = 删掉这些名字 → 快照当前剩下的（内联 + invocation 公共属性）→ 铺新 pack。不需要额外的快照字典，一个名字集合就够，而且天然幂等。

其余落地要点：

- **展开产物保留 `class=`**（原来是合并后删掉）。契约变了，`StyleMergeTests` 里三条断言「展开后不含 class」的测试相应改写 —— 真正让这个特性零成本的从来不是删掉 `class`，而是**值已经折成普通属性**，控件与 `ScreenInstantiator` 依旧不知道样式存在。
- `ExpandInvocation` 要跳过保留下来的 `class`，否则报「unknown attribute 'class'」。
- **`StyleAttributeView.IsUncertain` 对已合并节点直接返回 false**。否则展开遍看到 `class=` 又会噤声，把 M1 刚拿到的覆盖还回去。实测确认 fixture 里的报错仍然照常触发。
- 重合并挂在 `Screen.Open` 与 `Screen.ReSolve` 的**开头**，而不是单独的 theme-changed 钩子：它幂等，而 ReSolve 本来就是切主题触发的（`_themeHandler`），一个调用点同时覆盖 resize / Variant / Theme / 首次构建。
- 两道早退让不用主题样式的工程**一分钱不花**：文档没有 `<Style>`；以及 —— 真正重要的那条 —— `ThemeStore.AnyThemeStyles` 为 false，也就是此特性之前的每一个工程。

实现期抓到的 bug（测试抓的）：`ReMerge` 里判断「节点自己声明过」时查的是**正在被改写**的节点，于是 pack 刚写进去的 base `color` 会让同名的 `color.mobile` 被跳过。`Apply` 查的是合并前的 `src` 所以没这问题。改为先快照 `selfDeclared` 再铺 pack。

**与 §7 的偏离**：设计说调用点 class 的「公共属性那半」可以跟随主题。实现下来**两半都不跟随** —— invocation 节点本身在展开后就不存在了，它的 `class` 无处安放；把它挪到实例根又会和模板体根自己的 `class` 撞车。M2.4 的 `PUI-THEME-STYLE-TEMPLATE-PARAM` 因此要覆盖整个「调用点用 theme-scoped style」的情况，而不只是 Param 那半。

**顺带发现的既有缺口**（与主题无关，未修）：`itemTemplate` 走的是 `InstantiateNode(tpl.Body, ...)`，即**未展开**的模板体 —— 所以 `<Template>` 体内的 `class=` 在被当作 itemTemplate 使用时**从来就没生效过**。重合并没有捎带修它：那些 body 是 `loaded.Templates`（可能就是 commons 池）里共享的原始 IR，就地改写会跨文档泄漏。

产出：`ElementNode.StyleAttrNames`、`StyleMerger.ReMerge` / `ComputePack` / `CloneForMerge`、`Core/Template/ThemeStyleApplier`、`ScreenDef.Styles`、`ThemeStore.AnyThemeStyles` / `ResolveStyles`、`Screen.ReMergeThemeStyles`，加 `Tests/EditMode/Application/ThemeStyleSwitchTests.cs`（8 条）。

验证：EditMode 2303 → 2299 passed / 0 failed / 4 skipped；EditorOnly 308/308；`dotnet format` 无输出；UIXmlLint 仓库 13 个文件仍零 issue，fixture 的跨 import / class 供给报错全部照常。

### M2.4（分支 `test/attr-reversibility-red`）

lint 规则 + SKILL。三处与设计不同：

**1. `PUI-THEME-STYLE-IF-PARAM` 取消。** M2.3 之后它**永远不可能触发**：`if` 在样式黑名单里（普通节点走不通），而唯一的 Param 通道是模板调用点 —— 那已经被 `PUI-THEME-STYLE-ON-INVOCATION` 整个盖住（M2.3 的偏离让这条规则从「只管 Param 那半」扩到了「调用点上的 theme-scoped class 一律不跟随」）。写一条永远不响的规则是负债，不写。

**2. `--theme` 不做成 flag。** 既然主题层是「展开后重合并」，CLI 直接**对文档声明的每个主题各走一遍展开树并去重** —— 不用作者记参数，主题改不动的东西也不会多出输出。`ThemeStyleRulesTests.ProblemVisibleOnlyUnderOneTheme_IsStillFound` 钉住：只在某一个皮肤下才成立的 `sprite` on `<VStack>`，单看一个状态会整个漏掉。

**3. 新增第三条规则 `PUI-THEME-STYLE-NO-BASELINE`。** 写测试时才发现设计没想清楚的一个点：**只在主题里声明、没有全局对应的样式，`class=` 根本引用不到** —— 展开期只认全局池，会抛 unknown style，而那条信息没法提到它其实写在哪个 `<Theme>` 里。这个行为本身是对的（全局样式当基线正是 §6.1 成立的前提），缺的是可操作的诊断。顺带把主题规则移到了**尝试展开之前**，否则展开失败会把它们整个短路掉。

规则位置：`ThemeStyleRules` 是 `internal`（其余规则类都是 public）—— `StyleKey` 是 internal，为了给一个 lint 参数标类型就把它提到公共 API 是本末倒置；消费者只有 `DocumentLinter` 和测试。

产出：`Runtime/Core/Lint/ThemeStyleRules.cs`（三条规则）、`DocumentLinter` 接入 + 逐主题走查、`Tests/EditMode/Lint/ThemeStyleRulesTests.cs`（12 条）、`authoring-promptugui-xml/SKILL.md` 新增 **Theme-scoped styles** 一节 + 顶层元素表 + lint 速查、`scripting-promptugui-csharp/SKILL.md` 的 `UI.Theme.Set` 说明。

验证：EditMode 2315 → 2311 passed / 0 failed / 4 skipped；`dotnet format` 无输出；UIXmlLint 仓库 13 个文件仍零 issue，themed fixture 上 SHAPE 与 ON-INVOCATION 两条都正确触发。

---

## 16. 现状小结

M0 / M1 / M2 全部落地。§9.4 的 `--theme` 以「自动逐主题走查」代替，§6.2 的规则取消（见 M2.4）。仍然开着的：

- **§8 A 类三处 setter 可逆性**：red test 挂着 `[Ignore]`，修复未做。
- **`itemTemplate` 的 `class=` 从来没生效过**（M2.3 发现的既有缺口，与主题无关）。
- **lint 无行号**；同一模板多次调用时不可区分是哪一次（M1 记录）。
- **带命名空间的样式不能被主题覆盖**（M2.2 的 §4.3 收紧）。
