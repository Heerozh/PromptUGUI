# `<Collapsible>` —— 可展开 / 收起的内联面板（COL）

> 状态：**草案**（2026-08-31 与作者头脑风暴对齐，决策见 §10）。
> **依赖** `2026-08-31-hug-reveal-flip-checked-design.md`（FND）：§1 的 `ClampFitter` Hug 模式与 `IHugContent`、
> §2 的 `RevealDriver`、§3 的 `RotateFlipEffect`。在 FND M3 之后开工（FND §10）。
> 相关：`2026-08-29-tabmenu-design.md`（TM —— 标题栏 caption、`transition=`、`PlayTransition` 的打断语义、
> "inactive 父级下 TMP 报 0"的绕法，全部从它抽出复用）；`reference/states.md`（标题栏的状态视觉）；
> `reference/animations.md`（`expand` / `collapse` 触发器，本文把它们的源从 `<TabMenu>` 泛化到 `<Collapsible>`）；
> master spec §6.5（布局组子节点规则，body 沿用）。

## 1. 问题

HUD 右侧的任务追踪面板：一条标题栏「任务 ▼」，下面三行任务（图标 + 标题 + `(32/33)`），点标题栏收起只剩
标题栏、再点展开；底是半透明深色圆角一整块。设置页的分组、邮件 / 事件的多面板侧栏、聊天频道列表 —— 都是
同一个东西：**标题常驻、内容可折叠、折叠时下面的内容顶上来**。

今天最近的两个控件语义都不对：

- `<TabMenu>` 是**浮层弹窗**：独立 Canvas 提升排序 + 全屏 blocker + 全局只开一个 + 选中即关 + Escape 关闭
  （`TabMenu.cs:486-580`）。任务面板要的是常驻 HUD、行可点、别的地方照常可点。
- `<Tab bind="frame">` 站独能做"点一下显隐一个 Frame"，但它是 `PUI-TAB-PARENT` 错误 + 每次开屏 warning
  （`Tab.cs:87`）；`<Toggle>` 的布局又是死的（左 20×20 勾选框 + 右 label，`Toggle.cs:58`），当不了
  "标题在左、箭头在右"的标题栏。

再往下的四个基础缺口（贴合高度、高度动画、箭头翻转、持久态显隐）在 FND 里补；本文只定**控件**。

## 2. 否决的方案

**放宽 `<Tab>` 站独 + `bind=` 当折叠面板。否决。** 没有动画、没有 hug、箭头要 C#，且 Tab 的 `bind` 是
"显隐一个兄弟 Frame"而非"自己有 body"—— 标题与内容不是一个节点，`radius` / `glass` 圈不住整块。

**`<TabMenu inline="true">`。否决。** TabMenu 的 body 是一列互斥 `<Tab>`，选中即关；折叠面板的 body 是
任意子树。共用的只有标题栏和过渡，那些抽成内部零件即可（§6）。

**只出一个 `<Template>` 配方（Btn + Show + Animation）。否决。** FND §4 之后确实能拼出 80%（见 FND §4.4
的例子），但 hug 底图、高度过渡、箭头翻转、手风琴互斥、`expanded` 运行期独占、键盘导航一样都拼不全；
而"可折叠面板"是几乎每个游戏 UI 都有的原语，值得一个标签。

**横向折叠（侧抽屉）。不进 v1。** 同一套引擎换轴就行，但标题栏的排版完全不同；需求出现再加 `direction=`。

**`itemTemplate` + `BindItems`。不进 v1（COL-D4）。** 动态行写 `<ScrollList height="clamp(_, hug, 200)">`
当 body 子节点（FND §1），不再复制第三份 slot 机制（ScrollList / Carousel 各一份）。

## 3. 方案总览

```xml
<!-- 最简：属性标题 + 自动箭头，body = 子节点，高度贴合，点标题收放 -->
<Collapsible id="tasks" text="任务" anchor="top-right" width="150" margin="90,20,_,_"
             sprite="none" color="surface/0.55" radius="10" headerHeight="24" transition="0.2s">
  <ScrollList id="list" itemTemplate="TaskRow" width="stretch" height="clamp(_, hug, 200)"
              sprite="none" scrollbar=""/>
</Collapsible>

<!-- 自定义标题栏：<Header> 槽；箭头仍由库画在右侧并跟着翻转 -->
<Collapsible id="tasks" width="150">
  <Header>
    <HStack anchor="stretch" padding="0,8" spacing="6">
      <Icon name="ui:quest" size="14x14"/>
      <Text width="stretch">任务</Text>
      <Text id="count" tr="false">3</Text>
    </HStack>
  </Header>
  <TaskRow .../>
  <TaskRow .../>
</Collapsible>

<!-- 手风琴：同 group 只开一个，全部收起也行 -->
<VStack anchor="stretch-left" width="240" margin="16,_,16,16" spacing="4">
  <Collapsible text="画面" group="settings" width="stretch">…</Collapsible>
  <Collapsible text="音频" group="settings" width="stretch" expanded="false">…</Collapsible>
  <Collapsible text="操作" group="settings" width="stretch" expanded="false">…</Collapsible>
</VStack>

<!-- 行随展开滑入：expand / collapse 现在也能指到 Collapsible -->
<Template name="TaskRow">
  <Animation on="expand" reverse-on="collapse" translate="-12,0:0,0" fade="0:1" duration="0.12s">
    <Btn id="row" width="stretch" height="32" sprite="none" color="#0000">…</Btn>
  </Animation>
</Template>

<!-- Variant：竖屏默认收起 -->
<Collapsible text="任务" expanded="true" expanded.portrait="false" .../>
```

- 一个标签 = 标题栏（可点、有状态视觉）+ body（VStack 语义、hug、可限高滚动）+ 过渡（高度 + 淡入 + 箭头）+
  `expanded` 运行期独占 + `group=` 互斥 + 键盘 / 手柄可聚焦。
- 主表面是**整块面板**（标题 + body 一起），`color` / `radius` / `glass` / `sprite` 圈住整块；收起时面板就是
  标题栏那么高。
- 高度**恒为 hug**（标题 + body），`height=` / `size=` 是 parse error；宽度归作者。

## 4. 作者面

### 4.1 `<Collapsible>` 属性

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `text` | string | — | 标题文字（i18n msgid，`tr` / `ctx` 同 `<Btn>`）。与 `<Header>` 互斥 |
| `font` · `fontSize` · `textColor` | 同 `<Btn>` | `default` · — · ink | 标题文字 |
| `icon` · `iconColor` | sprite key · color | — | 标题左侧 24×24 图标（同 `<Tab icon>` 的位置规则）。与 `<Header>` 互斥 |
| `arrow` · `arrowColor` · `arrowSize` | sprite key · color · px | `pugui_caret` · glyph 色 · `16` | 右侧箭头。`arrow=""` **隐藏**（箭头区宽度归 0）。有 `<Header>` 时仍由库画 |
| `headerHeight` | px | `44` | 标题栏高（Btn 的默认高；HUD 面板常写 24） |
| `headerColor` · `headerSprite` | color · sprite key | 透明 · — | 标题栏自己的底（叠在主表面之上）。默认透明 = 与面板同一块 |
| `hoverColor` · `pressedColor` · `disabledColor` | color | — | 标题栏 bg 的绝对状态色（同 `<Btn>`，作用于 `headerColor` 那一层） |
| `hoverModulate` · `pressedModulate` · `disabledModulate` | color | white | 相对乘子，扩散到**标题栏**的子 Graphic（不进 body） |
| `pressedOffset` | `x,y` | — | 标题栏内容按下位移（同 `<Btn>`） |
| `expanded` | bool | `true` | 初始展开。**运行期独占**（同 `isOn` / `value` / `current`）：用户点过之后 ReSolve 不重置；`.variant` 允许；Theme `<Style>` 禁止 |
| `group` | string | — | 手风琴组（Screen 范围）。同组内展开一个自动收起其它；允许全收起 |
| `transition` | 时长（`0.2s` / `200ms` / 浮点秒） | `0.2s` | 展开 / 收起过渡；`0` = 瞬切 |
| `maxHeight` | px | — | body 最大高；超出可滚动（`ScrollRect` 竖向、无滚动条） |
| `spacing` · `padding` | 同 `<VStack>` | — | **body** 的行距与内距（标题栏不受影响） |
| `color` · `sprite` · `tint` · `radius` · `borderWidth` · `borderColor` · `glow*` · `innerGlow*` · `glass` (+ 调参) | 同 `<Frame>` / 程序化表面 | 库默认 sliced 底 | **主表面 = 整块面板**。`sprite="none"` + `color` 走纯色 / 圆角 / 玻璃 |
| `interactable` | bool | `true` | `false` = 标题栏禁用（不可收放、进 Disabled 视觉）+ CanvasGroup 级联到 body |
| `focus` · `nav` · `navUp/Down/Left/Right` | 同其它 Selectable | — | 标题栏是 Selectable：可聚焦、Submit 收放 |
| `width` / `width.*` / `class` / `anchor` / `margin` / `hidden` / `flow` / `scale` | 通用 | — | 照常 |
| `height` / `size`（含 `.variant`） | — | — | **parse error `PUI-COLLAPSIBLE-HEIGHT`**：高度恒为 hug |

### 4.2 `<Header>` 结构元素

- 不是控件（同 `<Param>` / `<Slot>` / `<FocusCursor>`）：parser 从 `<Collapsible>` 的子节点里摘出，其子节点
  挂进标题栏宿主；`Get<T>` 不可取到 `<Header>` 本身，但**能按 id 取到它里面的节点**（作用域同普通子节点，
  `Get<Text>("tasks/count")`）。
- 必须是 `<Collapsible>` 的**第一个**子节点、至多一个：否则 `PUI-COLLAPSIBLE-HEADER-FIRST` /
  `PUI-COLLAPSIBLE-HEADER-MULTI`。出现在别处 → `PUI-HEADER-OUTSIDE`。
- 无属性（v1）。宿主 rect = 标题栏 rect 去掉右侧箭头区（`arrowSize + 2×8`；`arrow=""` 时为 0），其子节点
  **自由定位**（`anchor` / `margin` 合法，`stretch` 关键字不合法 —— 同 `<Frame>` 子节点）。
- 与 `text` / `icon` / `font` / `fontSize` / `textColor` / `iconColor` 同写 → `PUI-COLLAPSIBLE-HEADER-CONFLICT`。
- 点击穿透：宿主下的 `<Text>` / `<Icon>` 默认 `raycastTarget=false`，整条标题栏都是点击区；作者放进去的
  `<Btn>` 自己吃点击、不触发收放（uGUI 命中最上层）。

### 4.3 body

- `<Header>` 之外的全部子节点 = body，**`<VStack>` 语义**：`childControlWidth/Height = true`、
  `childForceExpandWidth = true`（行填满宽度，同 TabMenu 弹板的理由）、`spacing` / `padding` 归它。
  子节点规则同 `<VStack>` 子节点：`anchor` / `margin` 报 `PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN`，
  `width="stretch"` / `height="hug"` / `flow="false"` 合法。
- 高度 = 子节点 preferred 之和（hug）；写了 `maxHeight` → `min(hug, maxHeight)`，超出部分 `ScrollRect`
  竖向拖动 / 滚轮，v1 无滚动条（§9）。
- 收起完成后 body 内容 `SetActive(false)`（不渲染、不参与导航、不吃点击）；展开前先激活再量（§5.2）。

### 4.4 状态视觉

标题栏是 `IStateSource`（`Btn` 语义：Normal / Hover / Pressed / Disabled，无 Selected）。`<Header>` 里的
`<Show on="state-hover">` / `<Animation on="state-pressed">` 向上解析到它；`*Color` 作用于 `headerColor`
层，`*Modulate` 扩散到标题栏子树（body 不受影响，body 子节点也不需要 `stateReact="false"`）。
`interactable="false"` → Disabled（默认整条标题栏去饱和，states.md 规则）。

**展开态没有 `checked` 事件** —— 用已有的 `expand` / `collapse`（§4.5）。`<Show on="expand@id">` 不支持
（§9 开放问题 2）。

### 4.5 触发器：`expand` / `collapse` 的源泛化

- `on="expand"` / `"collapse"`（裸）向上解析到**最近的** `<TabMenu>` **或** `<Collapsible>`；`@id` 形式按
  FND §4.3 的词法作用域。错误消息改为 "no `<TabMenu>` / `<Collapsible>` ancestor found"。
- `expand` 在展开**开始**时触发（body 已激活、尺寸已量、补间开始），`collapse` 在收起开始时触发 —— 行动画
  与面板过渡同步起步。
- `<Animation on="expand" reverse-on="collapse">`（FND §2）是行进出的标准写法（§3 第四例）。
- 开屏时 `expanded="true"` **不**触发 `expand`（与 TabMenu 一致：`expand` 是"打开了"，不是"是开着的"）。
  开屏时的 `expanded="false"` 也不触发 `collapse`。

### 4.6 `group=` 手风琴

- Screen 范围的命名组（`Screen.CollapsibleGroups`，与 `ToggleGroups` 同生命周期：`Screen.cs:55` 旁、
  `Close` 时清空）。**不是** uGUI `ToggleGroup`（它只协调 `Toggle` 组件），是一张 `name → List<Collapsible>`。
- 展开 A → 同组其它已展开的先 `Collapse()`（各自播自己的过渡）→ A 播展开。允许全部收起（点开着的那个
  就收起，不像 `TabBar` 的 `allowSwitchOff=false`）。
- 开屏时同组多个 `expanded="true"`：**文档顺序第一个**胜出，其余以 `expanded=false` 建立（不播过渡）；
  lint `PUI-COLLAPSIBLE-GROUP-MULTI-EXPANDED`（CLI error / 运行时 warning）。`Add` 块 / BindItems 生出的
  成员按到达顺序加入。
- `expanded.portrait="true"` 这类 Variant 覆盖走同一条互斥路径（ReSolve 里对该组重新裁决，仍是"第一个胜出"）。

### 4.7 Variant / Theme / ReSolve

- `expanded` 注册为 `runtimeStateAttr`（`BuiltinPrimitives.Register<Collapsible>("Collapsible", runtimeStateAttr: "expanded")`），
  `PeekRuntimeState()` 返回当前值：用户点过之后 Variant / Theme / resize 的重放跳过它；`expanded.variant`
  的切换仍生效（applier 的既有规则）。
- `expanded` 进 `UIDocumentParser.ThemeStyleForbiddenAttrs`（理由同 `isOn`）。
- 其余属性重放不重建：`headerHeight` 改 `LayoutElement`，`maxHeight` 改上限并重量，`transition` 只影响下次。
- 过渡中途 ReSolve：`OnAfterApply` 重断言 body 的当前 box（FND §2.4.7 同款）。

### 4.8 lint

| 代码 | 条件 |
|---|---|
| `PUI-COLLAPSIBLE-HEIGHT` | `height` / `size`（含 `.variant`）写在 `<Collapsible>` 上 |
| `PUI-COLLAPSIBLE-HEADER-FIRST` | `<Header>` 不是第一个子节点 |
| `PUI-COLLAPSIBLE-HEADER-MULTI` | 多于一个 `<Header>` |
| `PUI-COLLAPSIBLE-HEADER-CONFLICT` | `<Header>` 与 `text` / `icon` / `font` / `fontSize` / `textColor` / `iconColor` 同写 |
| `PUI-HEADER-OUTSIDE` | `<Header>` 不是 `<Collapsible>` 的直接子节点 |
| `PUI-COLLAPSIBLE-GROUP-MULTI-EXPANDED` | 同文档、同 `group` 内多于一个 `expanded="true"`（或默认 true） |
| `PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN` | body 子节点写 `anchor` / `margin`（既有规则，`<Collapsible>` 加入布局组标签表；`<Header>` 子节点**豁免**） |
| `PUI-PROC-SPRITE-CONFLICT` 等 | 既有程序化表面规则（加入 `SurfaceTags`） |
| `PUI-EXPAND-NO-SOURCE` | 裸 `expand` / `collapse` 无 `<TabMenu>` / `<Collapsible>` 祖先（既有规则扩源） |

## 5. 语义细节

### 5.1 尺寸

运行时结构（Hierarchy 里按此对照）：

```
Collapsible            RectTransform + [Image / ProceduralPanel 主表面] + VerticalLayoutGroup(spacing 0, 无 padding,
                       │              childControl=true, forceExpandWidth=true) + [ClampFitter(Hug) 自由定位时]
├─ Header              RectTransform + Image(headerColor/headerSprite, raycast) + PuiButton + LayoutElement(preferredHeight=headerHeight)
│   ├─ Icon / Label    （属性标题时懒建；同 TabMenu 的 BuildCaption / LayoutCaption）
│   ├─ Host            （<Header> 槽的子节点挂这里；rect = 标题栏去掉箭头区）
│   └─ Arrow           Image(pugui_caret) + RotateFlipEffect
└─ Body                RectTransform + RectMask2D + CanvasGroup + LayoutElement(preferredHeight = 补间值) [+ ScrollRect 当 maxHeight]
    └─ Content         RectTransform + VerticalLayoutGroup(spacing/padding) [+ ContentSizeFitter(竖向 preferred) 当 maxHeight]
                       ← ChildHostTransform：作者的 body 子节点
```

- **高度恒为 hug**：布局组父级下由根的 `VerticalLayoutGroup`（它本身是 `ILayoutElement`）被量；自由定位父级
  下 `ApplyCommon` 自动挂 `ClampFitter(Hug)`（`Collapsible : IHugContent`，内容 = 根 LayoutGroup 的 preferred）。
  作者写 `height=` → `PUI-COLLAPSIBLE-HEIGHT`。
- **宽度归作者**：`width` / `width="stretch"`（布局组内）/ `N%` / `clamp(…)` 照常。自由定位下**不写**
  `width` = 宽也 hug（`max(标题 caption preferred, body preferred)`），与嵌套 stack 的直觉一致。
- 收起态：body 的 `LayoutElement.preferredHeight = 0`，根高 = `headerHeight`，主表面随根 rect 收缩，圆角
  自然落在标题栏下沿。

### 5.2 展开 / 收起过渡

复用 FND §2 的 `RevealDriver`，三条补间同起同止、时长 `transition`、`Ease.OutCubic`：

| 通道 | 从 → 到（展开） | 绑定 |
|---|---|---|
| body 高度 | `0 → min(contentPreferred, maxHeight)` | `Body.LayoutElement.preferredHeight`（`RevealDriver.ApplyBox`） |
| body 淡入 | `0 → 1` | `Body.CanvasGroup.alpha` |
| 箭头 | `0° → 180°` | `Arrow.RotateFlipEffect.Rotation`（FND §3；网格级，pivot 无关） |

- **打断安全**：任何一次 `Expand()` / `Collapse()` 先 `TryCancel` 在飞的三条 handle，再从**当前值**起补
  （TM `PlayTransition` 的规则，`TabMenu.cs:591-642`）。
- **展开顺序**：`Content.SetActive(true)` → TMP 量尺寸绕法（`BeforeRebuild` / `AfterRebuild`，`TabMenu.cs:353-374`）
  → `ForceRebuildLayoutImmediate(Content)` → 读 preferred → 触发 `expand` → 起补间。
- **收起顺序**：触发 `collapse` → 起补间 → **只有完整跑完的收起**才 `Content.SetActive(false)`（回调里比对
  handle，防"刚重新展开却被上一次收起的回调关掉"，`TabMenu.cs:631-641`）。
- `transition="0"` 或 `!Application.isPlaying`（EditMode 测试）→ 直接写终态、同步激活 / 停用，无 handle。
- 开屏 `expanded="false"`：body 以 `DeferDuringOpen` 停用（`Screen.cs:216` 的先例 —— 让 TMP 在 active 时
  先量一次），箭头直接置 0°、body alpha 0，不播过渡。
- `hidden="true"` 的 Collapsible 里 `Expand()` 直接置终态（同 TabMenu `GameObject.activeInHierarchy` 门）。

### 5.3 标题栏

- 属性标题布局（沿用 TM `BuildCaption` / `LayoutCaption`）：`[icon 24×24, 左 margin 8][gap 8][label 竖向铺满、
  左对齐、水平 stretch][arrow arrowSize, 右 margin 8]`；无 `icon` 时 label 从左 margin 12 起。label 懒建（写了
  `text` / `font*` / `textColor` 才有）。
- 箭头静止态：展开 = 原图朝向（`pugui_caret` 朝下，与设计稿「任务 ▼」一致），收起 = 转 180°（朝上）。
  自定义 `arrow=` 请给"展开态"的图。
- `PuiButton.onClick` → `Toggle()`；Submit / 手柄 A 同源；`interactable=false` 时 `Button.interactable=false`
  进 Disabled。
- 状态反应器安装同 `<Btn>`（`StateTintInstaller` / `DisabledGrayscaleInstaller`，`Children` = 标题栏子树）。

### 5.4 焦点 / 导航

- `<Collapsible>` 进 `NavTargetRules.SelectableTags`；`focus` / `nav*` 作用于标题栏 Selectable。
- 收起时 body 内容 inactive → 自动不在导航图里；展开后回到几何自动填充。
- 不做焦点陷阱（那是弹窗的事）。

### 5.5 C# API

```csharp
var panel = screen.Get<Collapsible>("tasks");
panel.IsExpanded;                       // 只读
panel.Expand(); panel.Collapse(); panel.Toggle();
panel.OnExpanded / panel.OnCollapsed;   // Observable<Unit>，与 <Animation on="expand"> 同源
panel.OnToggled;                        // Observable<bool>（展开 = true）
panel.Text = "…"; panel.Icon = "ui:x";  // 属性标题
panel.Interactable = false;
panel.OnState;                          // 标题栏 InteractState
screen.Get<Text>("tasks/count");        // <Header> 里的节点按路径取
```

`IsExpanded` 的 setter 私有 —— 状态只经 `Expand` / `Collapse` / `Toggle` 变，保证组互斥与事件成对。

## 6. 实现地图

**从 TabMenu 抽出的共享内部零件**（TabMenu 改为调用它们，行为不变，由既有 TabMenu 测试守门）：

- `Controls/Internal/CaptionBuilder.cs`：icon + label + arrow 的建节点 / 排版 / 属性落点（`BuildCaption` /
  `LayoutCaption` / `arrow*` 三属性）。
- `Controls/Internal/IExpandable.cs` + `ExpandableMarker`（`TabMenuMarker` 更名泛化）：`IsExpanded` /
  `OnExpanded` / `OnCollapsed`；`TriggerSourceResolver.FindTabMenu` → `FindExpandable`；`Trigger.SubscribeMenu`
  改按接口订阅；`StateTriggerRules.IsMenuSourceTag` 加 `"Collapsible"`。
- TM 的箭头翻转改用 `RotateFlipEffect`（FND §3）替代负缩放 —— 顺手统一，TM 的 `SetArrowFlip` /
  `CurrentArrowFlip` 退役。
- inactive-TMP 测量绕法（`BeforeRebuild` / `AfterRebuild`）上提为 `Internal/InactiveMeasure.cs`。

**新增**

- `Controls/Collapsible.cs`（`sealed`，`ProceduralControl`，`SurfaceHost` = 根；`ChildHostTransform` = `Content`；
  实现 `IStateSource`（转发标题栏 PuiButton）、`IExpandable`、`IHugContent`；`PeekRuntimeState`）。
- `Controls/Internal/CollapsibleGroupRegistry.cs` + `Screen.CollapsibleGroups`。
- `Core/Lint/CollapsibleRules.cs`（纯 C#，§4.8 前六条）+ `IRWalker` / `ScreenInstantiator` 双分发。
- Parser：`<Header>` 摘出 —— `ElementNode` 上加 `HeaderChildren`（或 `CollapsibleDef`），照 `<FocusCursor>`
  的处理（`UIDocumentParser.cs:314-318`）；`ScreenInstantiator` 把它们实例化到 `Header/Host` 下、id 进同一
  作用域；`IRWalker` 对 `<Header>` 子节点按自由定位规则、对其余子节点按布局组规则（`isLayoutGroup` 表加
  `Collapsible`，但要跳过 `Header` 分支）。
- `Core/Lint/BuiltinTags.cs` 加 `Collapsible`（`Header` 不是控件、不进表；lint 单独识别）。
- `BuiltinPrimitives.cs` 注册（`runtimeStateAttr: "expanded"`）。
- `ProceduralSurfaceRules.SurfaceTags` / `GradientStopRules` / `NavTargetRules.SelectableTags` /
  `UIDocumentParser.ThemeStyleForbiddenAttrs`（`expanded`）/ `Editor/I18n/XmlStringScanner.TextHostingTags`
  （`text` 属性要被抽成 msgid）。
- XSD：反射生成，`<Header>` 作为 `Collapsible` 的可选首子元素需在 `XsdGenerator` 手写一处（同 `FocusCursor`
  在 `Screen` 下的声明方式）。
- `UI.ResetForTests`：无进程级静态（组注册表挂在 Screen 上），不需要。

## 7. 测试

- `CollapsibleTests`（EditMode）：结构（Header / Body / Content / Arrow 节点、组件）；属性标题懒建；`<Header>`
  子节点挂点与 `Get` 路径；`expanded` 初值与 `PeekRuntimeState`；`transition=0` 下 `Toggle()` 终态（body LE、
  alpha、箭头 180、Content active）；`headerHeight` / `maxHeight` / `spacing` / `padding` 落点；`interactable`。
- `CollapsibleSizeTests`（EditMode）：自由定位 hug（三行 32 + 标题 24 = 120）；`maxHeight` 封顶 + `ScrollRect`
  存在；布局组父级下兄弟节点在收起后上移；宽度不写 = hug。
- `CollapsibleGroupTests`：互斥、全收起、开屏多 true 第一个胜出、Variant 覆盖重新裁决、Screen 关闭清表。
- `CollapsibleTriggerTests`：裸 `expand` / `collapse` 解析到 Collapsible；嵌在 TabMenu 里的 Collapsible 各找各的；
  开屏不触发；`<Animation on="expand" reverse-on="collapse">` 行订阅成对。
- `CollapsibleRulesTests`：§4.8 六条正反例；body 子节点 `anchor` 报错而 `<Header>` 子节点不报。
- `CollapsiblePlayTests`（PlayMode）：过渡中兄弟节点单调位移；中途反向无跳变；收起完成后 Content inactive、
  重新展开取消上一次的停用回调；ReSolve 中途重解算 box 不重置。
- TabMenu 既有测试全绿（零件抽出的回归守门）。

## 8. SKILL 更新（同一 PR 内，英文）

- `authoring-promptugui-xml/SKILL.md`：内置目录表加 `<Collapsible>` 行；新 `### <Collapsible>` 小节（属性表、
  `<Header>`、body 规则、`expanded` 运行期独占、lint 码）；程序化表面标签表 / `focus=` 列表 / native-size
  列表 / `tint=` 列表 / nav 列表各加一项；uGUI 对照表加一行；Quick reference 一行。
- 新 `reference/controls-collapsible.md`：完整属性表、结构图、手风琴 / 动态行 / 自定义标题三个配方、与
  `<TabMenu>` 的取舍表、lint 表。
- `reference/animations.md`：`expand` / `collapse` 行的源改为 "`<TabMenu>` or `<Collapsible>`"；行动画 pattern
  改用 `reverse-on`。
- `reference/controls-tabs.md`：`<TabMenu>` 小节加一句"内联折叠面板请用 `<Collapsible>`"。
- `reference/navigation.md`：Selectable 列表加 `<Collapsible>`。
- `scripting-promptugui-csharp/SKILL.md`：§5.5 的 API 块。
- `PromptUGUI/CLAUDE.md` 第 31 行路由表加 `controls-collapsible.md`。

## 9. 非目标

1. 横向折叠 / 侧抽屉（`direction=`）。
2. `itemTemplate` + `BindItems`（用 `<ScrollList height="clamp(_, hug, N)">`）。
3. body 滚动条皮肤（`scrollbar*=`）—— v1 无滚动条，需求出现再对齐 `<ScrollList>` 的属性名。
4. 标题栏在底部（"从下往上展开"）—— 用 `anchor="bottom-*"` 的自由定位 + FND `reveal` 手拼。
5. 嵌套手风琴的联动动画（内层展开时外层同时长高）—— hug 链本身就连续跟随，无需额外机制。

## 10. 已定的决策（2026-08-31 与作者对齐）

1. **COL-D1** 标题栏 = 属性三件套（`text` / `icon` / `arrow`）+ 可选 `<Header>` 槽；箭头始终由库画并翻转。
2. **COL-D2** body 默认 hug，`maxHeight` 限高后内部滚动；`height=` / `size=` 是 parse error。
3. **COL-D3** v1 含 `group=` 手风琴：Screen 范围命名组、允许全收起、开屏多 true 取文档顺序第一个 + lint。
4. **COL-D4** v1 不含 `itemTemplate` / `BindItems`，动态行走 `<ScrollList height="hug">`。
5. **COL-D5** `expanded` 默认 `true`、运行期独占（`runtimeStateAttr`）。
6. **COL-D6** 主表面是整块面板（标题 + body），标题栏另有 `headerColor` / `headerSprite` 叠层。
7. **COL-D7** `expand` / `collapse` 触发器泛化到 `IExpandable`（TabMenu + Collapsible），Collapsible 不引入 `checked`。

作者未单独裁定、按惯例定下的：`headerHeight` 默认 44（Btn 默认）；`transition` 默认 0.2s（内联高度过渡比
TM 弹板的 0.15s 略长）；箭头收起态 = 180°；收起完成停用 `Content`；开屏不触发 `expand` / `collapse`；
`<Header>` 必须首位、无属性；`*Modulate` 只扩散到标题栏。

## 11. 开放问题（留给 plan / 实现期）

1. 箭头收起态 90°（▶，树形控件惯例）还是 180°（▲）？v1 定 180°，若设计稿要 ▶，加 `arrowCollapsed="right|up"`。
2. 是否给 `<Show>` 加 `expanded@id` / `collapsed@id`（持久态，对齐 FND 的 `checked`）？目前认为 `<Header>` +
   库画箭头已覆盖主要需求，先不加。
3. `<Header>` 是否需要 `height=`（替代 `headerHeight`）？先不要，避免两处可写。
4. `maxHeight` 触发滚动时是否要 `softness` 羽化（`Carousel` 有先例）？看实际观感。
5. 从 TabMenu 抽 `CaptionBuilder` 时，TM 的 caption 是"选中项 icon + text"动态的，Collapsible 是静态的 ——
  抽出的粒度以"建节点 + 排版"为界，数据来源各自保留。

## 12. 里程碑

- **M0** TabMenu 零件抽出（`CaptionBuilder` / `IExpandable` / `InactiveMeasure` / 箭头改 `RotateFlipEffect`），
  TabMenu 测试全绿。
- **M1** `<Collapsible>` 骨架：结构、属性标题、`<Header>` 摘出与实例化、hug、`transition=0` 收放、注册 / XSD /
  BuiltinTags、EditMode 测试。
- **M2** 过渡：`RevealDriver` 三通道、打断安全、停用 / 激活时序、`expand` / `collapse` 触发、PlayMode 测试。
- **M3** `group=`、`expanded` 运行期独占、Variant 互斥裁决、lint 六条、SKILL 与 `controls-collapsible.md`。
- 前置：FND M1–M3 完成。

## 13. 实施记录

（实现后追加：与本设计的偏差、实测数据、被推翻的决策。）
