# `raycastTarget` —— 指针命中是显式声明的，不是画出来的

> 状态：**已实现**（2026-09-15，分支 `feat/raycast-target`，跳过 plan 直接红绿；实施记录见 §11）。决策见 §10。
> 相关：主 spec §5（原语表）/ §5.1（通用属性）；
> `2026-08-26-procedural-surface-design.md`（§5「退位 Image：sprite 清空、alpha 归零」与 §13 表 —— 本文改掉这一条）；
> `2026-05-14-pointer-event-triggers-design.md`（PE-D12 / PE-D13 —— 本文作废这两条）；
> `2026-05-09-m5-1-default-control-alignment-design.md` §5.4（Toggle label `raycastTarget=true` 让整条可点 —— 不变）；
> `2026-05-27-progress-control-design.md` PB-D16（Progress 各层 `raycastTarget=false` —— 不变）；
> `2026-06-12-tutorial-system-design.md` §3（`SpotlightMask` 是引导层唯一的拦截者 —— 不变）。

## 1. 问题

### 1.1 面板穿透

`ProceduralPanel.Awake`（`Runtime/Controls/Internal/ProceduralPanel.cs:88-91`）强制 `raycastTarget = false`，理由写在
类注释里：Frame 历来是纯 RectTransform 容器、点击本来就穿；加个 `color=` 不该改变命中语义，raycast 列表也短。这条规则
跟 glass 无关。

但它在「sprite → 程序化」这条迁移路径上是个坑：

| 面板底写法 | 命中 |
|---|---|
| `<Image sprite="…9slice" color="…">`（MessageBox 的 `dialog`，`Modals/MessageBox.ui.xml:7`） | uGUI `Graphic` 默认 `raycastTarget=true`，**吃**点击 |
| `<Frame color="…" radius="…">`（今天所有新界面的写法） | 面板强制 false，**穿透** —— 点在面板空白处落到下层 Screen 的按钮上，或 `EventSystem.IsPointerOverGameObject()` 为 false、游戏世界收到这一下 |

skill 给的替代方案是「for a tinted clickable region use `<Btn>`」（`authoring-promptugui-xml/SKILL.md:118`）—— 对「只想挡住、
不想交互」的面板是错的工具。

### 1.2 `<Image>` 的 true 是 uGUI 漏出来的，不是设计出来的

grep 一遍 `Runtime/Controls/`：Progress 的 bg / fill / frame（PB-D16）、Decor、Icon、Text、Dropdown arrow、Toggle checkmark、
Slider fill / handle、Tab 的 icon / label、CaptionBuilder 的 icon / label / arrow —— **全是 false**。开着的只有交互控件自己的
命中层：Btn 宿主 Image、Toggle label、ScrollList `_bg`（拖动起点）、Carousel / TabMenu 的透明 catcher、Collapsible header、
Markdown 链接文本。

也就是说「只有需要命中的层才开」是库内部一直遵守的规则；外露标签里唯一的例外是 `<Image>` / `<RawImage>` ——
`AddComponent<FxImage>()` 直接继承了 `Graphic` 的默认 true。列表页上一行三张 `<Image>`、两百行，就是六百个每帧参与
`GraphicRaycaster` 循环的目标，而没有一个是想被点的。

### 1.3 三处文档 / 代码漂移

- **`<Image raycastTarget="…">` 这个属性不存在。** `Image.cs` 从没声明过 `RaycastTarget`（`git log -S` 只有 `5bb7ab8` 给
  `<Text>` 加过），而 `ControlAttributeApplier.cs:77` 对未知属性是 `continue` 静默跳过。可它在 `reference/animations.md:75`
  的 caveat、`reference/controls-tabs.md:158`、`StateSourceInactivePageTests.cs:36` 和
  `Samples~/CommonControls/…/CommonControls.ui.xml:140`（`<Image class="skin-wood" raycastTarget="false">`）里都在用 ——
  那张木纹底图实际上仍在吃点击。
- PE-D12 / PE-D13 基于「Image 已有 `raycastTarget` 属性」这个不存在的事实。
- `Image` 属性表（`SKILL.md:214-227`）没有这一行，跟 caveat 自相矛盾。

### 1.4 程序化控件靠一张看不见的 Image 当命中区

`ProceduralSurface.Retire()`（`ProceduralSurface.cs:151-153`）把宿主 Image 的 sprite 清空、alpha 归零，但**保持 enabled**，
注释说得很直白：面板故意 `raycastTarget=false`，禁用 Image 就没有命中区了。代价：每个 `<Btn radius="8">` /
`<Tab color=…>` / `<Toggle radius=…>` 在 SDF 面板底下多画一个全透明的 quad —— uGUI 不会因为 alpha=0 跳过几何，
这是实打实的 overdraw，而 2026-08-23 spec 把「全透明面板不出几何、零 overdraw」列为程序化表面的核心性能承诺之一。

## 2. 否决的方案

**A. 「画了就吃」—— Frame 挂了面板即默认 `raycastTarget=true`。否决。** 它让命中取决于几何叠放顺序：一块 `anchor="stretch"`
的渐变遮罩 Frame 写在按钮**后面**就把按钮挡了，写在前面就没事。LLM 作者看不见叠放顺序，也不会每次都想到这层；而且它跟
§1.2 的库内规则相反。

**B. 只加属性、不改任何默认。否决。** `<Image>` 的 true 仍是漏出来的（§1.2），列表页的六百个目标还在，外露规则与库内规则
继续两套。

**C. 按树位置自动 —— `<Screen>` 直接子级默认 true、更深默认 false。否决。** 位置魔法让「同一段 XML 搬进模板就变了行为」，
比显式声明更难教给 LLM。用 lint 提醒（§5.2）代替。

**D. 新标签 `<Blocker>`。否决。** `<Frame raycastTarget="true">` 已经是它（§4.1）。

**E. 退位 Image 改成 `raycastTarget=false` 但保留 alpha=0 继续画。否决。** 那个 quad 还在（§1.4）。`enabled=false` 才是零成本，
而面板接管命中后它没有任何留下来的理由。

## 3. 方案总览

一条规则，库内库外同一份：

> **只有两种节点进 raycast 列表：交互控件自己的命中层，以及作者显式写了 `raycastTarget="true"` 的节点。**
> 其它一切默认穿透。一块面板要挡住指针，就在它的**根**上写 `raycastTarget="true"`；祖先永远画在后代下面，根 catcher
> 不可能遮住自己的交互子级，子级空白处的点击沿 `ExecuteHierarchy` 向上找不到 handler → 被吞掉，游戏世界收不到。

| 标签 | 今天 | 本文 | 属性 |
|---|---|---|---|
| `<Frame>` 无视觉属性 | 无 Graphic，穿透 | 不变 | `raycastTarget="true"` → 挂一块零几何的 catcher 面板（§4.1） |
| `<Frame color= / radius= / glass= …>` | 面板强制 false | **默认 false**；可开 | `raycastTarget`（bool，默认 false） |
| `<Image>` / `<RawImage>` | Graphic 默认 **true** | **默认 false**；可开；作为指针事件源时自动开（§4.3） | `raycastTarget`（bool，默认 false）—— 从「文档里有、代码里没有」变成真的 |
| `<Text>` | **TMP 默认 true**（`Text.OnAttached` 直接 `AddComponent<TextMeshProUGUI>()`；animations.md 与 PE spec 说的「默认 false」一直是错的 —— 每个 `<Text>` 都在 raycast 列表里） | **默认 false**；已有属性可开 | 不变 |
| `<Icon>` / `<Decor>` | 硬编码 false | 不变 | 无（写了 = `PUI-RAYCAST-TAG`） |
| `<Progress>` 各层 | false（PB-D16） | 不变 | 无 |
| `<Btn>` / `<Tab>` / `<Toggle>` / `<Slider>` / `<Scrollbar>` / `<Dropdown>` / `<InputField>` / `<ScrollList>` / `<Collapsible>` / `<TabMenu>` 的命中层 | Image 模式：宿主 Image；程序化模式：**alpha=0 的退位 Image** | Image 模式不变；程序化模式：**面板继承宿主 Image 的命中角色，Image `enabled=false`**（§4.2） | 无（写了 = `PUI-RAYCAST-TAG`） |
| Carousel / TabMenu 把手的透明 catcher、Toggle label、Tutorial `SpotlightMask`、Markdown 链接 | true | 不变 | — |
| VStack / HStack / Grid / SafeArea | 无 Graphic | 不变 | 无（写了 = `PUI-RAYCAST-TAG`） |

性能上这是严格的收缩：Unity 2021.2+ 的 `GraphicRegistry` 单独维护 raycastable 列表，false 的 Graphic 根本不进
`GraphicRaycaster.Raycast` 的循环；每帧每个指针一次 raycast，循环长度 = 开着的节点数。§1.4 的透明 quad 一并消失。

## 4. 语义细则

### 4.1 `raycastTarget` 属性（Frame / Image / RawImage；Text 已有）

- 类型 bool，默认 false。是**普通属性**：`<Style>` / `class=` 包、Variant 覆盖（`raycastTarget.portrait="true"`）、
  `<Theme>` 的样式覆盖都照常经过 —— 没有任何特殊通道。`PUI-VARIANT-NO-BASE` / `PUI-THEME-STYLE-SHAPE` 的既有规则照常约束它。
- **Frame 上写 `true` 会懒挂 `ProceduralPanel`**（跟写 `color=` 一样走 `Panel` 属性），但它**不在 `ProceduralAttrNames`
  里**：它不是形状，不宣告程序化模式（`ProceduralControl` 根本不暴露它），`PUI-CONTAINER-VISUAL-ATTR` 那套「Frame 能画的那组」
  的判定不包含它 —— 也进不去：`ProceduralAttrNamesTests.OnlyFrame_HasThePanelRequiringAttributes` 断言 `NeedsPanel` 里的名字
  只有 Frame 与 SurfaceTags 认识，而 Image / RawImage / Text 都有 `raycastTarget`。唯一的边角：`<Frame raycastTarget="true"
  mask="self">` 运行时有 Graphic、lint 却按「无 Graphic」报 `PUI-MASK-FRAME-SELF` —— 这个组合本来就没意义（没有形状可裁），接受。
- **无视觉属性 + `raycastTarget="true"` = 透明 catcher。** 面板 `ComputeVisible()` 为 false → `OnPopulateMesh` 出零顶点，
  零 overdraw，但 `raycastTarget=true`；uGUI 的命中测试走 `RectTransformUtility.RectangleContainsScreenPoint`，
  不看网格。这替代了今天 `<Image color="#00000000">` 那种 hack（Carousel / TabMenu 内部的 catcher 是带几何的 alpha-0
  Image，本文不动它们；要不要也换成零几何面板见 §9）。
- **要 PlayMode 验证的一点**：`GraphicRaycaster.Raycast` 跳过 `graphic.depth == -1` 与 `canvasRenderer.cull` 的 Graphic。
  `depth` 来自 `CanvasRenderer.absoluteDepth`，由 Canvas 批处理时按层级分配 —— **一个零顶点的 CanvasRenderer 是否拿到有效
  depth，我没有可靠的一手证据**（现有 catcher 都带几何）。§7 的 PlayMode 用例 `Bare_catcher_frame_is_hit` 就是为了回答它；
  若答案是 -1，兜底是 `OnPopulateMesh` 在 `raycastTarget && !visible` 时出**一个三顶点重合的退化三角形**（零像素、零 overdraw，
  但让 renderer 有几何）。面板任何时候都不设 `canvasRenderer.cull`。
- 面板默认值仍在 `ProceduralPanel.Awake` 里置 false（新面板一律穿透），由拥有者（Frame 的 setter、§4.2 的 `ProceduralSurface`）
  决定要不要开。类注释「forced off」改为「off by default; the owner decides」。
- weld 承载者（`_group != null`，自身面板被 suppress）写 `raycastTarget="true"`：走同一条零几何 catcher 路径，融合面在
  `GlassWeld` 子节点上仍是 false。

### 4.2 程序化表面接管命中角色

`ProceduralSurface`（`Runtime/Controls/Internal/ProceduralSurface.cs`）：

| | 今天 | 本文 |
|---|---|---|
| `Retire()`（表面开启的每一趟） | sprite 清空、alpha 归零、Image 保持 enabled | sprite 清空（仍是「本趟是否声明了 sprite」的探针，`Restore` 靠它）；**首次退位记下 `_hostImage.raycastTarget`**；**`_hostImage.enabled = false`**；`_panel.raycastTarget = 记下的值`。不再动 alpha |
| `Restore()`（表面关闭） | sprite / alpha 按「本趟是否声明」恢复 | sprite 规则不变；**`_hostImage.enabled = true`**；`_panel.raycastTarget = false`。颜色不必恢复 —— 从没改过 |
| `Reconcile()` 里读宿主颜色喂面板填充 | `_retired ? _retiredColor : _hostImage.color` | `_hostImage.color` 一直是活的（不再归零），`_retiredColor` 及其分支删掉 |

「面板继承宿主 Image 退位时的 `raycastTarget`」这一条让所有 SurfaceTags 无需逐个写规则：

| 宿主 | 宿主 Image 退位时的值 | 面板 |
|---|---|---|
| Btn / Tab / Dropdown / InputField 根、Toggle `Background`、Collapsible header、ScrollList `_bg`（ScrollRect 拖动起点）、Scrollbar track、TabMenu 弹窗面板 | true | **true** |
| Slider `Fill` / `Handle`、Progress `Bg` / `Fill` / `Frame` 内层表面 | false（`AddImage(…, raycast: false)`） | false |
| Scrollbar handle（`AddInnerSurface(host, selectable)`） | 按现状 | 跟着现状 |

不受影响的既有事实（列出来是因为它们容易被误伤）：
- `StateTintReactor.Detach()` 在 targetGraphic 移到面板时已把退位 Image 上的反应器摘掉（`StateTintReactor.cs:145-160`），
  禁用 Image 不碰状态着色；`Selectable.targetGraphic` 早已指向面板。
- `LayoutUtility` 只问 `isActiveAndEnabled` 的 `ILayoutElement`，退位 Image 在程序化模式下本来就没有 sprite，没有它的
  preferred size 可丢。
- `DisabledGrayscaleController` 扫描后代 Graphic 换材质：disabled 的 Image 换了也无害，跟今天一样。
- 2026-08-26 spec §13 表「任意控件（`__surface__` 在子节点）→ 退位 Image alpha-clip 全丢弃 → 把子节点全裁没」：`mask="self"`
  在程序化控件上从前就静默失效，现在仍然失效（Mask 要同一 GO 上的 Graphic，Image 禁用了更没有），lint 现状不变。
- Btn 的 `IPointerEventSource` 中继在宿主 GO 上，命中落在 `__Surface` 子节点后沿父链冒泡到它；`Selectable.OnPointerEnter/Exit`
  同理。指针在 `__Surface` 与（raycast=false 的）label 之间移动不产生 exit/enter —— label 根本不是命中目标。

### 4.3 指针事件源自动开（`<Image>` / `<RawImage>`）

`<Image>` 翻成默认 false 后，`on="hover-enter@img"` 与 C# 的 `img.OnPointerDown.Subscribe(…)` 会静默失效 —— 正是 PE-D12
当年只能标 caveat 的那类事。两条路都经过 `Image.EnsureRelay()`（trigger 走 `IPointerEventSource.OnPointerEnter/Exit/Down`
的 getter），所以在那里解决：

```
_authored   : bool?   ← raycastTarget= setter 写（含 Variant / class）
_relayWanted: bool    ← EnsureRelay() 置 true，永不清
Reconcile(): _img.raycastTarget = _authored ?? _relayWanted
             if (_authored == false && _relayWanted) UILog.Warn(this, "raycastTarget=\"false\" but used as a pointer event source — pointer events will never arrive")  // 每控件一次
```

setter 与 `EnsureRelay()` 都调 `Reconcile()`，所以属性先到还是订阅先到、以及 ReSolve 重放 `raycastTarget="false"`，结果都一样。
PE-D12（「不校验、运行时不报」）与 PE-D13（「不主动改 raycastTarget」）作废；`animations.md:75` 的 caveat 改写为这条 warning。
`<Btn>` 不需要 —— 它的命中层永远开着。CenteredSlideBox / MarkdownBox 的 `backdrop.OnPointerDown` 订阅由这条继续工作，
但它们的 XML 仍显式写 `raycastTarget="true"`（§6）：模态拦截不该依赖 C# 有没有订阅。

### 4.4 与 `interactable` / CanvasGroup 的关系

不变。`interactable="false"` 只动 `CanvasGroup.interactable`，`blocksRaycasts` 保持 true —— 一个 `raycastTarget="true"`
的面板被禁用后仍然吃点击（标准 Unity 禁用语义，`SKILL.md:829` 那条照旧）。CanvasGroup 不能凭空造出命中区：子树里没有一个
raycast target，`blocksRaycasts` 什么都挡不住 —— 这是「面板根写 `raycastTarget="true"`」的另一个理由。

### 4.5 catcher 的位置

祖先，或最先绘制的兄弟。uGUI 父先画、子后画，`GraphicRaycaster` 取最上层命中，`ExecuteHierarchy` 沿父链找 handler：
根 catcher 遮不住交互子级，交互子级空白处的点击落回根。**后序兄弟**才会遮住前面的兄弟 —— 这也是 A 案被否的原因，
现在它只在作者显式写了 `true` 时发生。

## 5. Lint

### 5.1 `PUI-RAYCAST-TAG`（error；raw + expanded；运行时 `UILog.Warn`）

`raycastTarget` 写在 Frame / Image / RawImage / Text 以外的标签上。消息分三种：
- VStack / HStack / Grid / SafeArea / Show / Trigger / Animation：「no Graphic — nothing to hit; put it on the `<Frame>` /
  `<Image>` that draws」；
- Icon / Decor / Progress：「always click-through; wrap in a `<Frame raycastTarget="true">` to catch」；
- 交互控件（`ProceduralSurfaceRules.SurfaceTags` ∪ Carousel / TabBar / Markdown）：「catches the pointer by definition;
  not configurable」。

同 `PUI-FLIP-TAG` / `PUI-FX-TAG` 的形态；模板调用标签在 raw 遍不判（看不见体）。

### 5.2 `PUI-RAYCAST-UNDECIDED`（warn；**expanded-only、CLI-only**）

「最外层的画出来的面没有表态」—— 直接对准 §1.1 的遗漏。从每个 `<Screen>` 根向下走：

1. 节点声明了 `raycastTarget`（经 `StyleAttributeView` 解析 `class=`；`IsUncertain` → 跳过整棵子树）：`true` → 覆盖，**停**；
   `false` → 继续进子级。
2. 节点是交互控件（§5.1 第三组）或 Progress：**停**，不报（它们自己管命中；子级在它的命中区里）。
3. 节点是**画出来的面**且未声明：**报**，停。「画出来的面」= `<Image>` / `<RawImage>`（它们无条件出一个 quad），或
   `<Frame>` 带任一 `ProceduralAttrNames.All` 里的属性（含 `weld`）。
4. 其它（纯容器、Text、Icon、Decor、Show、Trigger、Animation、Add 块、模板包裹）：继续进子级。

消息：`<Frame color=…> is the outermost drawn surface here and does not say whether it catches the pointer — write raycastTarget="true" (a panel that blocks what is behind it) or "false" (decoration)`。

一屏通常只报一两次（面板根 + 模态 backdrop），修完再跑会露出下一层。expanded-only 是因为 raw 遍看不见模板体里的面；
CLI-only 跟 `PUI-CONTAINER-VISUAL-ATTR` 同理 —— 它是作者工具的提醒，运行时刷 warning 是噪音。

### 5.3 不加的

不做「后序兄弟遮住交互控件」的几何推断 —— 默认 false 之后它只在作者显式写 true 时发生，那是作者的决定。

## 6. 内置 XML 与演示页面（同 PR 改好；无兼容层、无迁移文档）

| 文件 | 改动 |
|---|---|
| `Modals/MessageBox` / `InputBox` / `Loading` / `MarkdownBox` / `CenteredSlideBox.ui.xml` | `backdrop` 与 `dialog` 两张 `<Image>` 写 `raycastTarget="true"`。少了 dialog 那条，MarkdownBox 点对话框正文会落到 backdrop → `OnPointerDown` 关窗。CenteredSlideBox 的 `panel` Frame 保持无 Graphic：点卡片以外的地方落到 backdrop → 取消，是既有 UX（CSB-D9） |
| `Toast.ui.xml` | 不动（只有 Text；Toast overlay 本来 `blocksRaycasts=false`） |
| `Tutorial/TutorialOverlay.ui.xml` | 不动。`bubble` / `finger` 变成穿透 —— 引导层唯一的拦截者本来就是 `SpotlightMask`（2026-06-12 §3），气泡压到洞上时今天反而会挡住目标 |
| `Navigation/FocusCursor.ui.xml` | 不动 |
| `Samples~/CommonControls/…/CommonControls.ui.xml` | `Skin` 模板根 `<Frame anchor="stretch">` 写 `raycastTarget="true"`（面板底 = catcher，两套主题都拦）；`skin-wood` 那张 `<Image raycastTarget="false">` 现在真的生效，保留；其余 `<Image>` 按默认 false 不写；`DemoCard` 的 `bg` 不写（卡在 Carousel 的 catcher 里） |
| `Samples~/ProceduralStyle/…/ProceduralStyle.ui.xml` | `<Style name="panel">` 与 `app-bg` 加 `raycastTarget="true"`（页面板与最底层 app 背景各是一层 catcher）；`btn-*` / `hero` / `card` 在 Btn / 面板里，不写 |
| 宿主工程 ssw_re_client 的面板 | 不在本仓库；跑 CLI 的 `PUI-RAYCAST-UNDECIDED` 逐屏补 |

## 7. 测试（Red 先行）

**EditMode `Tests/EditMode/Controls/RaycastTargetTests.cs`**
- `Frame_WithVisuals_IsClickThroughByDefault`：`<Frame color='#fff'>` → 面板 `raycastTarget == false`（`FrameProceduralPanelTests:64` 保留）。
- `Frame_RaycastTargetTrue_MakesItsPanelAHitTarget`。
- `Frame_RaycastTargetTrue_WithoutVisuals_AttachesAZeroGeometryCatcher`：面板存在、`raycastTarget`、`IsPanelVisible == false`、`BuildMeshForTests` 出 0 顶点。
- `Frame_RaycastTargetFalse_OnPaintedFrame_StaysOff`。
- `Frame_RaycastTarget_FollowsVariantOverride`：`raycastTarget="false" raycastTarget.portrait="true"`，切 Variant 后翻转，切回再翻回。
- `Frame_RaycastTarget_ViaStyleClass`：`<Style name="panel" raycastTarget="true"/>` + `class="panel"`。
- `Frame_RaycastTarget_IsNotAProceduralAttr`：`ProceduralAttrNames.All` 不含它；`<Btn raycastTarget="true">` 不挂表面（属性被跳过）。
- `Image_IsClickThroughByDefault` / `Image_RaycastTargetTrue` / `RawImage_IsClickThroughByDefault` / `RawImage_RaycastTargetTrue`。
- `Image_PointerSubscription_TurnsRaycastOn`：C# 取 `OnPointerDown` → true。
- `Image_HoverTrigger_TurnsRaycastOn`：`<Trigger on="hover-enter@img">` → true。
- `Image_ExplicitFalse_PlusPointerSource_StaysOff_AndWarns`：`LogAssert.Expect` 一次，且 ReSolve 后不再重复。
- `Image_ReSolve_KeepsRelayDrivenRaycast`：订阅后切 Variant，仍 true。
- `Text_DefaultFalse_Unchanged`。

**`ProceduralSurfaceContractTests` / `ProceduralSurfaceRolloutTests` 改写**
- `Surface_IsClickThrough` → `Surface_TakesOverTheHostImagesHitRole`：`<Btn radius='8'>` 面板 `raycastTarget == true`，宿主 Image `enabled == false`。
- Rollout `EveryWiredControl_AttachesAndDrawsASurface` 里 `Assert.IsFalse(panel.raycastTarget)` 改为 `== 宿主 Image 退位前的值`；
  新增 `EveryWiredControl_DisablesTheRetiredImage` 与 `LeavingProceduralMode_ReenablesTheImage_AndDropsThePanelsRaycast`
  （`radius.portrait` 无 base 的形状：切走后 Image `enabled`、面板 false）。
- 内层：Slider fill / handle 面板 false；`ProgressTests:298 / 344` 保留；`ScrollbarTests:499` 的 track 断言迁到面板上。
- `Color_FeedsTheSurfacesFill_NotTheRetiredImage` 等读 `_retiredColor` 的用例按 §4.2 第三行调整（宿主颜色不再归零）。

**Lint `Tests/EditMode/Lint/RaycastRulesTests.cs`**
- TAG：VStack / Btn / Icon / Decor 各一条报；Frame / Image / RawImage / Text 不报；raw 遍模板调用标签不报。
- UNDECIDED：最外层画了的 Frame 未声明 → 报（带 `src:line`，模板体里带 `(via …)`）；`true` → 子级不查；`false` → 子级继续查并报里面那层；
  Btn 里的 `<Frame class="btn-ghost">` 不报；`<Screen>` 下的 `<Image id="backdrop">` 报；`class=` 给的 `raycastTarget` 算声明；
  不确定样式跳过；`SafeArea` 与 `VStack` 透传。
- `UIXmlLint` 对 `Runtime/Resources/` 与两个 Samples 目录 exit 0（§6 改完之后）。

**PlayMode `Tests/PlayMode/RaycastHitTests.cs`**（真 Canvas + `GraphicRaycaster` + `EventSystem`，`Canvas.ForceUpdateCanvases()` 后 `RaycastAll`）
- `Bare_catcher_frame_is_hit`：`<Frame raycastTarget="true">` 被命中 —— **§4.1 depth 问题的裁决用例**；红了就上退化三角形兜底。
- `Painted_frame_without_raycastTarget_is_not_hit`。
- `Procedural_Btn_is_hit_on_its_panel_and_clicks`：命中 GO 是 `__Surface`，`ExecuteEvents.ExecuteHierarchy(pointerClick)` 触发 `OnClick`。
- `Retired_host_image_draws_nothing`：程序化 Btn 宿主 `Image.enabled == false`，其 `CanvasRenderer` 无几何。
- `Image_default_is_not_hit_but_pointer_subscription_makes_it_hit`。

**XSD**：`StringAssert.Contains("raycastTarget")` 于 Frame / Image / RawImage。

Unity MCP 跑；`dotnet format --verify-no-changes --severity warn` 过；`.lint/UIXmlLint` 对 §6 的目录过。

## 8. 影响面 / SKILL 同步（同 PR，英文）

- **`authoring-promptugui-xml/SKILL.md`**：
  - 新增一小节 **Pointer hit-testing**（放在通用属性附近）：§3 那条规则 + 「面板根写 `raycastTarget="true"`」的惯用法 +
    catcher 必须是祖先或先绘制兄弟 + `interactable` 不造命中区；
  - `<Frame>` 段：删掉「A Frame never blocks clicks even when it draws; for a tinted clickable region use `<Btn>`」，
    属性表加 `raycastTarget`；`<Image>` / `<RawImage>` 表加行（含「作为指针事件源时自动开，显式 false 会 warn」）；
  - `<Tab>` 段「点穿子节点需 `raycastTarget="false"`（`<Icon>` 已是）」删掉 —— 子节点默认就穿；
  - 模态配方（`SKILL.md:1630` 附近）：backdrop 例子写上 `raycastTarget="true"`，并加一句 dialog 面板也要；
  - 原语 / 结构表里 `<Icon>`、`<Progress>` 的 `raycastTarget=false` 备注保留；
  - 错误码表加 `PUI-RAYCAST-TAG` / `PUI-RAYCAST-UNDECIDED`。
- **`reference/animations.md:73-75`**：事件源范围段改写 —— `<Image>` 默认 false 但作为事件源自动开；caveat 改为「显式 `false` + 事件源 → warning」。
- **`reference/controls-tabs.md:135-165`**：示例里的 `raycastTarget="false"` 删掉，「Keep decorative children raycastTarget=false」改为默认即是。
- **`reference/decor.md:107`**：不变。
- **`scripting-promptugui-csharp/SKILL.md`**：`1290-1297` 与 `1504` 的模态 / 排障两处改成 `raycastTarget="true"` 的写法；
  `Image` 的 `OnPointer*` 节加一句「订阅即打开 raycastTarget；作者显式 false 时不会打开、会 warn」。
- **主 spec**：§5 表 `<Frame>` 行补「`raycastTarget`」；§5.1 后加 **5.2 指针命中** 三行（本文 §3 的规则 + 默认表指回本文）。
- **`2026-05-14-pointer-event-triggers-design.md`**：PE-D12 / PE-D13 标「2026-09-15 作废，见本文 §4.3」。
- **`2026-08-26-procedural-surface-design.md`**：§5「退位：sprite 清空、alpha 归零」处加注「2026-09-15 起 Image `enabled=false`、面板继承命中，见本文 §4.2」。
- **`.lint/UIXmlLint/README.md`**：两条新码。
- XSD 由生成器按 `[UIAttr]` 自动带出。

## 9. 不做的事 / 后续

- **SDF 形状精确命中**：`ProceduralPanel : ICanvasRaycastFilter`，`IsRaycastLocationValid` 用同一个 SDF —— `radius="pill"`
  圆角外不吃点击。glow 已在 rect 外，本来就不吃。等有人真的点到圆角外再做。
- **Carousel / TabMenu 的 alpha-0 Image catcher 换成零几何面板**：省两个 quad；等 §7 的 `Bare_catcher_frame_is_hit` 绿了顺手做。
- **`<Icon>` 开放 `raycastTarget`**：PE 期决定 Icon 不做事件源，维持；要 hover 反馈用 `<Image>`（现在会自动开）或包 `<Btn>`。
- **`<Screen modal="true">` 自动 backdrop**：另案；本文维持「backdrop 是作者的责任」。
- **宿主工程逐屏补 `raycastTarget="true"`**：宿主自己的 PR，靠 CLI 的 `PUI-RAYCAST-UNDECIDED` 找。

## 10. 决策表

| # | 决策 | 取值 | 理由 |
|---|---|---|---|
| RT-D1 | 属性名 | `raycastTarget` | `<Text>` 已有、uGUI 原词、测试与 sample 早已在写；不造第二套词汇 |
| RT-D2 | 暴露范围 | Frame / Image / RawImage / Text；不做 common attribute | 纯容器没 Graphic，交互控件必须吃；其它标签写了 = `PUI-RAYCAST-TAG` |
| RT-D3 | 默认值 | 一律 false | §1.2：库内规则本来如此；`<Image>` 的 true 是漏出来的 |
| RT-D4 | 面板要挡指针怎么写 | 面板**根**写 `raycastTarget="true"` | 祖先遮不住后代；一块面板一行；否决 A / C（§2） |
| RT-D5 | 无视觉属性的 `<Frame raycastTarget="true">` | 零几何 catcher 面板 | 替代 alpha-0 Image hack；depth 风险由 PlayMode 裁决，兜底退化三角形（§4.1） |
| RT-D6 | 程序化控件的命中层 | 面板继承宿主 Image 退位时的 `raycastTarget`；Image `enabled=false` | 去掉 §1.4 的透明 quad；一条继承规则覆盖全部 SurfaceTags，内层表面自然为 false |
| RT-D7 | `<Image>` 作为指针事件源 | `EnsureRelay()` 自动开；显式 false 优先并 warn 一次 | 两条路都经 `EnsureRelay`；PE-D12 / D13 作废 |
| RT-D8 | 模态 backdrop / dialog | XML 显式 `raycastTarget="true"`，不依赖 C# 订阅 | 模态拦截是声明，不是副作用 |
| RT-D9 | 「忘了写」的兜底 | `PUI-RAYCAST-UNDECIDED`（warn，expanded-only，CLI-only） | 对准最外层的面；一屏一两条；否决位置魔法 C |
| RT-D10 | 兼容 / 迁移 | 无兼容层、无迁移文档；内置 XML 与 Samples 同 PR 改好 | 未上线（作者确认） |
| RT-D11 | 是否做 `ICanvasRaycastFilter` 形状命中 | 不做 | §9 |

## 11. 实施记录（2026-09-15）

五步提交：属性 + 默认值（`RaycastIntent` / `Frame.RaycastTarget` / `Text` 默认 false）→ 表面接管命中（`ProceduralSurface`）
+ PlayMode 命中测试 → 两条 lint → 内置 XML 与 Samples → 文档（skill × 2、主 spec §5.1.1、PE-D12/13 与 2026-08-26 spec 加注、
CLI README、XSD）。EditMode 3923 / EditorOnly 344 / PlayMode 207 全绿；`UIXmlLint` 对 `Runtime/Resources/` 与两个 Samples 目录零 issue；
`dotnet format --verify-no-changes --severity warn` 过。

**与 §3–§7 的偏差 / 实施中才知道的事**

- **`<Text>` 的默认值其实一直是 true**（§3 表已改）。原因是两层：`Text.OnAttached` 没设过 `raycastTarget`，而且就算设了也会被
  冲掉 —— Screen 的树是在 **inactive** 根下建好再一次 `SetActive(true)` 的（`Screen.Open`），TMP 的 `Awake → LoadDefaultSettings()`
  在一个 `fontSize` 仍为 -99 的新组件上会重新写 `raycastTarget = TMP_Settings.enableRaycastTarget`，落在 `OnAttached` 之后、
  属性应用之前。库内其它 TMP label（Btn / Toggle / InputField / `ProceduralBuilders.AddText`）之所以 false 能留住，只是因为它们
  在激活前就设了 `fontSize`。所以 `<Text>` 的值在 `OnAfterApply` 里再写一次（也顺带覆盖每次 ReSolve）。
- **RT-D5 的 depth 问题有了答案：零几何的 catcher 面板会被 `GraphicRaycaster` 返回**（`RaycastHitPlayTests.Bare_catcher_frame_is_hit_by_the_raycaster`，
  真 Canvas + EventSystem）。退化三角形的兜底没有做、也不需要做。
- **属性按趟清零**（`OnBeforeApply`），不止是「普通属性」：`Frame` / `Image` / `RawImage` / `Text` 的 `raycastTarget` 都在趟首回到
  未声明态，所以 variant-only 的 `raycastTarget.mobile="true"` 在变体离开时会真的关掉（`*_VariantOnlyRaycastTarget_TurnsOffAgainWhenTheVariantLeaves`
  三条），跟 `ProceduralSurface.BeginPass` 同一条纪律，而不是交给 `PUI-VARIANT-NO-BASE` 兜。`RaycastIntent` 因此有 `BeginPass` / `EndPass`。
- **`PUI-RAYCAST-UNDECIDED` 不需要「expanded-only」的管道**：模板体（`inTemplateBody`）不判、非内置标签（raw 遍里的调用）当作不透明
  停下，展开遍自然会在原位判到真实节点并带上 `(via …)`；两遍按消息去重。它是 Screen 级的独立遍历（`RaycastRules.CheckUndecided`），
  从 `IRWalker.Walk` 的 Screen 循环里调用，自己盖 `WithSource`。
- Samples 的做法与 §6 写的略有不同：CommonControls 没有改 `Skin` 模板，而是把 **每页的根 Frame** 写成 `raycastTarget="true"`
  （bare catcher），Skin 与内容都在它里面 —— 这才是 §3 说的「面板根写一行」；ProceduralStyle 也是在页面 Frame / `app-bg` /
  顶栏 / tab 轨道上内联写，两个 skin 文件保持纯视觉；`Backdrop` 的壁纸 `RawImage` 写 `false`。
- 顺带发现、已修：`XsdGenerator` 的 Frame / Image 是手写属性表，`raycastTarget` 要各补一行（`Frame_Image_and_Text_list_raycastTarget`）。
