# `<Pages>` —— 互斥子视图容器（PGS）

> 状态：**已对齐**（2026-09-17 作者按 §12 的推荐项拍板，全部取第一列；实现分支 `feat/pages`，plan 见 `docs~/superpowers/plans/2026-09-17-pages.md`）。
> 相关：master spec §5（内置标签表，本文加一行）；`2026-08-29-tabmenu-design.md`（`Tab.bind` 的翻页与
> Open 期延迟隐藏，本文复用其机制、不复用其 ToggleGroup）；`2026-08-31-collapsible-design.md` §4.7（`expanded`
> 的运行期独占 —— `selected` 走同一套 `RuntimeStateAttr`）；`reference/controls-tabs.md`「Sub-views inside a
> page」（本文**替换**那一节的 "Code switches" 配方）；`2026-09-13-runtime-template-instantiate-design.md`
> §2-D（动态实例不是 `Children`，本文沿用）。
> 动机来源：宿主 ssw_re_client `Round/UI/Panels/Planet.ui.xml` 建造页（三个互斥子视图）+ 它的 UI Preview 工具。

## 1. 问题

宿主的星球面板「建造页」由三个铺满同一区域、互斥显示的子视图组成：`slotsView`（槽位网格）/ `newView`
（可建列表）/ `detailView`（建筑详情）。谁显示由 C# 决定（空槽点「建造」进 `newView`、卡片进 `detailView`、
「返回」回上一个）。今天它长这样：

```xml
<Frame id="pageBuild" anchor="stretch" margin="54,6,6,58">
  <Frame id="slotsView"  anchor="stretch">…</Frame>
  <Frame id="newView"    anchor="stretch">…</Frame>   <!-- 不写 hidden：ReSolve 会打回 -->
  <Frame id="detailView" anchor="stretch">…</Frame>   <!-- 同上 -->
</Frame>
```

```csharp
_slotsView.Hidden = true; _detailView.Hidden = true; _newView.Hidden = false;   // × 3 处
```

三个缺陷，第一个是库的：

1. **`reference/controls-tabs.md` 推荐的 "Code switches" 配方（兄弟 Frame + `hidden="true"` + C# 切 `Hidden`）
   在 ReSolve 下是坏的。** `hidden` 是通用属性，声明了就在每次 ReSolve（resize / Variant / Theme）被
   `Control.ApplyCommon` 打回声明值（`Control.cs:469`）；它不像 `isOn` / `value` / `expanded` 那样有
   `RuntimeStateAttr` 的「运行期改过就不打回」保护。宿主的对策只能是**不写 `hidden`**，靠 C# 在 Open 后设初态
   （Planet.ui.xml:140-147 一整段注释在解释这件事）。
2. 于是 UI Preview（只加载 XML、没有宿主代码）看到三层叠在一起，改一次 XML 看一次效果就要手动藏两层。
3. 「三选一」这个不变量只存在于 C# 与注释里：每处切换写三行，漏一行就两页同显；`ReSolve` 后 C# 还得
   延两帧重设（宿主 memory 里的 workaround）。

之前试过把 `newView` 做成 `<TabBar>` 里一个 `hidden` 的 `<Tab>` 互切（宿主 2026-09-12 spec §4.7），
撞上 uGUI ToggleGroup（`Tab.cs:204` 的 refuse 警告就是那次加的）。那条警告文案自己说的出路 —— "swap a page's
sub-views inside the page (toggle Hidden on sibling `<Frame>`s, or use a nested `<TabBar>`)" —— 前者是缺陷 1，
后者是给玩家点的、不是给代码切的。库里缺的是一个**无头翻页容器**：Qt `QStackedWidget` / Flutter
`IndexedStack` / Android `ViewFlipper` 那一类，"N 个孩子同一时刻只显示一个"由容器保证。

有了它：预览只显示 `selected` 那页、不需要任何标注；预览工具能对任何 `<Pages>` 通用地列一排页按钮、按文件记住
上次看的页；C# 一行 `Show("newView")`；ReSolve 安全性在控件里做一次。

## 2. 否决的方案

**A. `<Frame pages="true" selected="…">`（Frame 上加模式属性）。否决。** 一个标签两种语义，lint /
XSD / 预览工具都要按属性再分支；`Frame` 已经是最重的容器（mask / glass 焊组 / 程序化面板），不再往上堆模式。

**B. 无头 `<TabBar>` / 复用 `TabGroupCore`。否决。** 拖着 uGUI `ToggleGroup` + `Selectable` 语义，正是
"隐藏 Tab 互切"失败的根源；翻页容器与选中态视觉无关。`Tab.ApplyBindFrame` 里真正要复用的只有
"Open 期延迟隐藏"这一条（`Screen.DeferDuringOpen`），它已经是 Screen 的公共机制。

**C. 预览专用变体（`hidden.preview="true"`，预览工具 `UI.Variants.Set("preview", true)`）。不做，但记录它成立。**
`VariantResolver` 在变体未激活时对 `hidden` 返回 null → `ApplyCommon(hidden: null)` 不写，所以它**不会**触发
缺陷 1，是零库改动的应急方案。否决理由是它只解决"藏"不解决"切"：看另一页还得改 XML，且互斥不变量依旧没人管。

**D. 每个子视图拆成模板文件 + `Screen.Instantiate` 按需挂。否决（作为本问题的解）。** 预览每个文件只看到自己
（Storybook 式），但 id 作用域 / `<Trigger>` 的 `@id` 解析全变，模板文件还得配预览壳才能进 UI Preview；改动面
远大于问题。它跟 `<Pages>` 不冲突，宿主愿意拆时照拆。

**E. 专用 `<Page>` 子元素（要求 `<Pages>` 的孩子必须是 `<Page>`）。否决。** `Tab.bind` 的页就是任意
`<Frame>`；模板调用当页（`<ShopList id="list"/>`）很常见；多一层无意义节点、多一个 id 作用域。
页 = 任意直接子节点，凭 `id` 被选中。

**F. 把 `hidden` / `interactable` 改成运行期独占（修缺陷 1 本身）。不在本文，见 §11。** 它修的是另一类
（单个节点由 C# 管显隐 / 可用态），与 `<Pages>` 正交、互补；分开立案。

## 3. 方案总览

```xml
<!-- 三选一：selected 指初始页；其余页 Open 结束时被停用；页不写 hidden -->
<Pages id="pageBuild" selected="slotsView" anchor="stretch" margin="54,6,6,58">
  <Frame id="slotsView"  anchor="stretch">…</Frame>
  <Frame id="newView"    anchor="stretch">…</Frame>
  <Frame id="detailView" anchor="stretch">…</Frame>
</Pages>

<!-- 模板调用当页；selected 按 Variant 切初始页（要有基础值，PUI-VARIANT-NO-BASE） -->
<Pages id="shop" selected="list" selected.portrait="detail" anchor="stretch">
  <ShopList   id="list"/>
  <ShopDetail id="detail"/>
</Pages>

<!-- 要底 / 圆角 / 玻璃：外面套 Frame（Pages 是纯容器，同 VStack） -->
<Frame color="surface/0.6" radius="8" anchor="stretch">
  <Pages id="wizard" anchor="stretch" margin="8">…</Pages>
</Frame>

<!-- Tab 直接 bind 到 Pages（§4.4 放宽）：Tab 管"这页在不在"，Pages 管"这页里哪个子视图" -->
<Tab text="建造" bind="pageBuild" isOn="true"/>
```

```csharp
var views = screen.Get<Pages>("pageBuild");
views.Show("newView");                       // 替代三行 Hidden；未知 id → 警告、不动
views.OnSelectedChanged.Subscribe(id => …);  // 真正切换时（含 Variant 重应用）
views.PageIds;                               // ["slotsView","newView","detailView"]，声明顺序

// 工具（UI Preview）：列出一个 Screen 里所有 <Pages>
foreach (var p in screen.FindAll<Pages>()) { /* p.Id, p.PageIds, p.Selected */ }
```

## 4. 作者面

### 4.1 `<Pages>` 属性

| 属性 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `selected` | 页 id | 第一页 | 初始显示的页。**运行期独占状态**（`RuntimeStateAttr`，同 `isOn` / `value` / `expanded`）：运行期改过就不被 ReSolve 打回；没动过时 `selected.<variant>` 照常覆盖 |
| 通用属性 | — | — | `id` / `anchor` / `size` / `width` / `height` / `margin` / `pivot` / `hidden` / `interactable` / `flow` / `if` 等照常（master §5.1）。`hidden` 藏的是整组 |

**没有视觉属性。** `<Pages>` 是纯容器（同 `VStack` / `HStack` / `Grid` / `SafeArea`）：根上没有 Graphic 也没有
ProceduralPanel，`color` / `sprite` / `radius` / `glass` … 一律被 `PUI-CONTAINER-VISUAL-ATTR` 报掉；
`raycastTarget` 被 `PUI-RAYCAST-TAG` 报掉。要底就外面套 `<Frame>`。

**默认 anchor 用 `<Frame>` 的规则（DSS-D13）**：没写 size 的轴 stretch、写了的轴 top/left。它是"一块区域"，
不是 `(Top, Left)` 的叶子。

### 4.2 页 —— 直接子节点

- **每个直接子节点都是一页**，不区分标签：`<Frame>` / `<VStack>` / 模板调用 / `<Animation>` 包着的子树都行。
- **每页必须有 `id`**（`PUI-PAGES-CHILD-ID`）。没有 id 的页选不中：运行时警告一次并永远停用。
- **页不写 `hidden`**（含 `hidden.<variant>` 与 `class=` 带入的，`PUI-PAGES-CHILD-HIDDEN`）：页的 activeSelf
  归 `<Pages>` 所有。写了不但多余，ReSolve 还会用它把选中页藏掉（缺陷 1）。
- 模板体里 `if="{{p}}"` 为假的页在展开期就不存在，不是页（`if=` 只在模板体内求值）；`selected` 指到它 → 运行时回退第一页并警告。
- 页数为 0 → `PUI-PAGES-EMPTY`（CLI 没有 warning 级，全部 issue 都是 error；运行时是 warning）。
- **v1 不支持动态页**：`<Variant><Add into="#pagesId">` 被 `PUI-PAGES-ADD-TARGET` 拒绝（Add 块与 Pages 会争
  同一个 activeSelf）；`Screen.Instantiate(tpl, pages)` 的实例按 §2-D 不是 `Children`，不成为页；
  `BindItems` 不提供。

### 4.3 Variant / Theme / ReSolve

- `selected` 注册为 `RuntimeStateAttr`（`BuiltinPrimitives`：`reg.Register<Pages>("Pages", null,
  runtimeStateAttr: "selected")`），`PeekRuntimeState()` 回读当前页 id。语义与 `Tab.isOn` 逐字相同：
  声明值是初态；`Show()` 改过后 ReSolve 跳过它；没改过时 `selected.portrait` 在切到 portrait 时重应用。
- 页自身不声明 `hidden` → ReSolve 的 `ApplyCommon` 不碰页的 activeSelf。`Pages.OnAfterApply` 每个 apply
  pass 末尾**重申**一遍全部页的 activeSelf（幂等，N 次 `SetActive`）作为兜底 —— 注意 ReSolve 遍历 `_nodeMap`
  没有父子顺序保证，兜底不依赖顺序，只依赖"页不声明 hidden"这条 lint 硬约束。
- Theme 切换对 `<Pages>` 无事可做（无视觉）。

### 4.4 `<Tab bind>` 放宽到任意控件

`Tab.ApplyBindFrame` 今天 `Get<Frame>(_bindId)`，`<Pages>` 不是 `Frame`（`Frame` 是 sealed）。放宽为
`Get<IControl>`：解析到任何控件都接受，`SetActive` 走 `IControl.GameObject`；警告文案改为
"did not resolve to a control"。文档里 `bind=` 的"通常目标"仍写 Frame。宿主的 `<SideTab bind="pageBuild">`
因此无需改动。

### 4.5 lint

新增 `Runtime/Core/Lint/PagesRules.cs`（纯 C#；CLI 与 `ScreenInstantiator` 共用；运行时全部是
`UILog.Warn`，**没有硬错误** —— 不为一个预览便利容器炸掉整个 Screen）：

| 代码 | 级别（CLI / 运行时） | 触发 |
|---|---|---|
| `PUI-PAGES-CHILD-ID` | error / warning（控件自报） | 直接子节点没有 `id` |
| `PUI-PAGES-SELECTED` | error / warning（控件自报） | `selected` 的基础值或任一 `selected.<variant>` 值不是任何直接子节点的 `id` |
| `PUI-PAGES-CHILD-HIDDEN` | error / warning | 直接子节点声明了 `hidden`（基础 / variant / `class=` 带入） |
| `PUI-PAGES-EMPTY` | error / warning | 没有直接子节点 |
| `PUI-PAGES-ADD-TARGET` | error / —（CLI-only） | `<Add into="#x">` 的 `x` 是 `<Pages>`（文档级检查，同 `CollapsibleRules.CheckGroups` 的挂法） |

CLI raw 遍就能查 `CHILD-ID` / `SELECTED`（模板调用的 `id` 写在调用节点上，展开前可见）；`CHILD-HIDDEN` 的
`class=` 来源只在 expanded 遍可见（`StyleAttributeView`，不确定时不报）。

现有规则的集合要加 `"Pages"`：`BuiltinTags.All`（`BuiltinTagsTests` 守着）、
`PureContainerVisualAttrRules.LayoutOnlyTags`、`RaycastRules.LayoutOnlyTags`、`HugRules.WayOut` 的
"wrap the content in a `<VStack>`" 分支（`PUI-HUG-TAG` 本身已对非 HugTags 全部生效，Pages 不支持 hug，见 §5.3）。

XSD：`XsdGenerator` 按 `registry.All` 反射非原语控件，`<Pages selected>` 自动出现；补一条 substring 断言。

## 5. 语义细节

### 5.1 初始选择与 Open 期延迟隐藏

Open 的 apply 是 DFS 后序（`Screen.cs:250-256`）：页先于 `<Pages>` 应用，`Children` 在 apply 前已建好。

1. `Selected` setter（`selected` 声明了才会被调）：校验 id ∈ `PageIds`；未知 → 警告 + 不改。
2. `OnAfterApply`：`_selected == null` → 取第一页（`selected` 未声明的默认）；然后 reconcile：
   `foreach page: SetActive(page.Id == _selected)`。
3. reconcile 里的**停用**在 Open 期间必须走 `owner.DeferDuringOpen(...)`（`Screen.cs:286`）—— 与 `Tab.bind`
   同一个理由：页里 TMP 等自动尺寸组件在 inactive 的 GameObject 上不 Awake、量不出 preferredWidth。
   启用保持即时。延迟动作在执行时**重读**当前 `_selected`（Open 期间 Variant 初始块等可能又改过）。
4. Open 结束后（`IsOpening == false`）一切切换即时。

### 5.2 运行期切换

`Show(id)` ≡ `Selected = id`：

- 同值 → no-op，不发事件。
- 未知 id → `UILog.Warn(this, …)` 一次（按 id 去重），不改。
- 否则：先启用新页、再停用旧页（避免一帧空白 / 布局抖两次），然后 `OnSelectedChanged.OnNext(id)`。
- Variant 重应用触发的切换同样发事件 —— 订阅方看到的是"选中页变了"，不关心谁改的。

### 5.3 尺寸 / 布局

- 页是自由定位的（同 Frame 的孩子）：页自己写 `anchor="stretch"` 是常态。
- `<Pages>` 作为 V/HStack 的孩子照常（`width="stretch" height="200"`）。
- **不支持 `hug` / `clamp(_, hug, _)`**（`PUI-HUG-TAG`，与 Frame 一致）："贴合选中页"要页高不一时重排父级，
  牵扯 `HugElement` 对 inactive 孩子的量算，另案。
- 页切换后 uGUI 自己会因 `OnEnable` / `OnDisable` 标脏布局，不额外 `MarkLayoutForRebuild`。

### 5.4 焦点 / 导航

停用页里的 Selectable 自动退出导航；焦点若在被停用的页内会随之丢失 —— **与 `Tab.bind` 今天一致**，v1 不做
焦点交接（§12 开放问题）。初始焦点指到非选中页内的控件同样落空（同 Tab）。

### 5.5 C# API

```csharp
namespace PromptUGUI.Controls
{
    public sealed class Pages : Control
    {
        /// 当前页 id。set = Show(id)。
        [UIAttr, Preserve] public string Selected { get; set; }
        /// 声明顺序。
        public IReadOnlyList<string> PageIds { get; }
        public IControl SelectedPage { get; }
        public void Show(string id);
        /// 选中页真正变化时（含 Variant 重应用）；同值不发。
        public Observable<string> OnSelectedChanged { get; }
        internal override string PeekRuntimeState() => Selected;
    }
}

public interface IScreen
{
    // …既有成员…
    /// 这个 Screen 里所有 T 类型的控件：静态树按声明顺序，之后是存活的动态子树（BindItems 行 /
    /// Screen.Instantiate 实例）按登记顺序。已销毁的不返回。给工具用（UI Preview 的 Pages 选择器、
    /// 调试面板）；业务代码仍按 id 走 Get<T>。
    IReadOnlyList<T> FindAll<T>() where T : class, IControl;
}
```

### 5.6 与其它机制的交互

- **`Get<T>` 路径不变**：页是普通子节点，`Get<Frame>("newView")` / `Get<…>("slot1/…")` 全部照旧。宿主迁移只改
  切换那几行。
- **嵌套**：`<Pages>` 里套 `<Pages>` 没有特殊规则；内层随外层页停用而停用，内层的 `Show()` 在停用时也照常记账
  （与"隐藏页上的 TabBar 照常从代码切"同理）。
- **`<Animation on="open">` 在非初始页里**：Open 时照常入队并在下一 tick 释放，页随后被停用 —— 动画在看不见的
  地方跑完，页再显示时是终态。与 `Tab.bind` 页今天的行为一致；页级 `on="…"` 源不进 v1（§10）。
- **直接写页的 `Hidden`**（`screen.Get<Frame>("newView").Hidden = true`）：不支持 —— 下一次 apply pass 的
  reconcile 会把它改回 `<Pages>` 认为的状态。要藏整组藏 `<Pages>`；要换页 `Show()`。
- **热重载**：Screen 重开 → 运行期选择归零回声明值（同 `isOn`）。预览工具自己按文件记忆再重放（§9）。

## 6. 实现地图

| 文件 | 改动 |
|---|---|
| `Runtime/Controls/Pages.cs`（新） | §5.5 的控件；`OnAfterApply` reconcile + `DeferDuringOpen`；`PeekRuntimeState` |
| `Runtime/Application/BuiltinPrimitives.cs` | `reg.Register<Pages>("Pages", null, runtimeStateAttr: "selected")` |
| `Runtime/Controls/Tab.cs` | `_boundFrame: Frame` → `IControl`；`Get<IControl>`；警告文案（§4.4） |
| `Runtime/Application/Screen.cs` | `IScreen.FindAll<T>()`：走 `Def.Root` 的 ElementNode 树 + `_nodeMap` 取声明顺序，再接 `_dynamicSubtrees`（先 `PruneDeadDynamicSubtrees`） |
| `Runtime/Core/Lint/PagesRules.cs`（新） | §4.5 五条；`CheckPages(node[, styles])` 自检 + `CheckAddTarget(screenDef)` 文档级 |
| `Runtime/Core/Lint/BuiltinTags.cs` / `PureContainerVisualAttrRules.cs` / `RaycastRules.cs` / `HugRules.cs` | 集合加 `"Pages"` |
| `Runtime/Core/Lint/IRWalker.cs` | 派发 `PagesRules`（节点级在 `Collapsible` 旁；文档级在 `CheckGroups` 旁） |
| `Runtime/Application/ScreenInstantiator.cs` | 同一处派发为运行时 `UILog.Warn`（照 `Collapsible` 的写法，无硬错误） |
| `Editor/XsdGenerator.cs` | 不改（反射自动出）；只加测试 |
| `.claude/skills/*`、master spec、`BEST_PRACTICES*.md` | §8 |

**Core 纯 C# 约束**：`PagesRules` 只碰 `ElementNode` / `ScreenDef` / `AddDirective.IntoPath`，不引 UnityEngine。

## 7. 测试（Red first）

`Tests/EditMode/Controls/PagesTests.cs`：

1. `Default_Selects_First_Page_And_Deactivates_Rest_After_Open`
2. `Selected_Attr_Picks_Initial_Page`
3. `Show_Switches_ActiveSelf_And_Fires_OnSelectedChanged`
4. `Show_Same_Id_Is_NoOp_Without_Event`
5. `Show_Unknown_Id_Warns_Once_And_Keeps_Selection`（`LogAssert.Expect` 一次，再调不再警告）
6. `Runtime_Selection_Survives_ReSolve`（Variant 翻转 + Theme 切换后仍是 `Show()` 的那页）
7. `Selected_Variant_Override_Applies_When_Untouched`（`selected="a" selected.portrait="b"`）
8. `Template_Invocation_Is_A_Page`（`<ShopList id="list"/>` 能被 `Show("list")`）
9. `Initial_Hide_Is_Deferred_Until_Open_Measured`（非初始页里的 `<Btn text>` 量到正确 preferredWidth —— 照
   `BtnContentSizingTests` 的 Tab 版本改写）
10. `Reconcile_On_ReSolve_Reasserts_Page_ActiveSelf`（外部 `SetActive(false)` 选中页后 ReSolve 恢复）
11. `Child_Without_Id_Warns_And_Stays_Inactive`
12. `Selected_Pointing_At_If_False_Child_Falls_Back_To_First`
13. `Nested_Pages_Inner_Show_Works_While_Outer_Page_Inactive`

`Tests/EditMode/Controls/TabBarTests.cs` 追加：`Tab_Bind_To_Pages_Switches_It`。

`Tests/EditMode/Application/ScreenFindAllTests.cs`：静态顺序；含动态子树实例；销毁后不含。

`Tests/EditMode/Lint/PagesRulesTests.cs`：五条各一红一绿；`IRWalker` 派发；`DocumentLinter` raw / expanded 两遍
去重；`PUI-PAGES-CHILD-HIDDEN` 的 `class=` 来源在 expanded 遍命中、raw 遍不误报。
`PureContainerVisualAttrRulesTests` / `RaycastRulesTests` 各加一个 `<Pages color=…>` / `<Pages raycastTarget>` 用例。
`BuiltinTagsTests` 守注册；`XsdGeneratorTests` 断言含 `name="Pages"` 与 `selected`。

## 8. SKILL / 文档更新（同一 PR，英文）

- `authoring-promptugui-xml/SKILL.md`：内置标签表加 `<Pages>` 一行（"one of N children visible; `selected=`;
  pages need `id`, never `hidden`; no visuals — wrap in `<Frame>` for a background"）；控件→组件映射表加一行
  （`RectTransform` only）；`bind=` 的说明改为"any control (usually a `<Frame>` or `<Pages>`)"。
- `authoring-promptugui-xml/reference/controls-tabs.md`「Sub-views inside a page」：**"Code switches" 配方换成
  `<Pages>`**，并写明旧配方为什么坏（`hidden` 被 ReSolve 重放）；"The player switches" 保留。
- `scripting-promptugui-csharp/SKILL.md`：`Pages`（`Selected` / `Show` / `PageIds` / `OnSelectedChanged`）；
  `IScreen.FindAll<T>()`；TabBar 一节里 "flip `Hidden` on sibling frames" 的示例改成 `Show()`。
- `CLAUDE.md` 路由表：`<Pages>` → XML skill 主文档（不单开 reference 文件，控件小）。
- master spec §5 标签表加一行；`BEST_PRACTICES.md` / `.zh.md` 的 Tab 段落各补一句"页内子视图用 `<Pages>`"。

## 9. 预览工具契约与宿主迁移（宿主侧，另案）

**UI Preview（`Assets/Tools/UI Preview/UIPreview.cs`）**，在宿主仓库单独立任务：

- 每次 `OpenScreen` 后 `screen.FindAll<Pages>()`；面板里每个 `<Pages>` 一行：标签 = `Id`，按钮 = `PageIds`，
  高亮 `Selected`；点按钮 `Show(id)`。
- 选择按 `(文件相对路径, Pages.Id)` 存 `EditorPrefs`；每次 `LoadFileAsync` / `OpenScreen` 完成后重放 —— 这才是
  把"每次改完 XML 都要手动藏"消掉的那一步。
- 嵌套 `<Pages>` 按 `FindAll` 顺序自然是外层在前。

**宿主迁移（Planet）**：

```xml
<Pages id="pageBuild" selected="slotsView" anchor="stretch" margin="54,6,6,58">
  <Frame id="slotsView"  anchor="stretch">…</Frame>
  <Frame id="newView"    anchor="stretch">…</Frame>
  <Frame id="detailView" anchor="stretch">…</Frame>
</Pages>
```

```csharp
_views = Screen.Get<Pages>("pageBuild");
_views.Show("newView");      // PlanetDockSection.cs:1099-1101 / 1117-1119 / 1132-1134 各三行 → 一行
```

`<SideTab bind="pageBuild">` 不动（§4.4）；Planet.ui.xml:140-147 那段"为什么不写 hidden"的注释删掉。
`noOptions` / `avatar` / `noJobs` 这类**单节点**由 C# 管显隐的地方**不是**本文范围 —— 见 §11。

## 10. 非目标

- 页间过渡动画（crossfade / 滑动，`transition=` 或页级 `on="page-in" / "page-out"` 源）：需要停用前等动画、
  与 Screen.Close 两阶段同构，v2。
- `hug` / 贴合选中页（§5.3）。
- 动态页（`<Add>` / `Screen.Instantiate` / `BindItems`）；按下标选页（`Show(int)`）。
- 手势翻页 / 指示点 —— 那是 `<Carousel>`。
- 焦点交接（§5.4）。
- 懒建页 / 销毁未选中页：页永远存活，只切 activeSelf（同 `Tab.bind`）。

## 11. 相关缺陷（另案，不在本文实现）

调研时确认了两条通用属性的 ReSolve 行为，是缺陷 1 的根，也是宿主 `noOptions` / `avatar` / `noJobs` /
按钮可用态全部"不写 XML、C# 延两帧重设"的根：

1. **`interactable` 无条件重放。** `ControlAttributeApplier.cs:151` 把未声明当 `true`，`Control.ApplyCommon` 末尾
   无条件 `Interactable = interactable`（`Control.cs:470`）→ **每次 ReSolve 把 C# 禁用的控件重新启用**，写不写
   XML 都一样。
2. **`hidden` 声明即重放**（`Control.cs:469`）→ 宿主只能不写、初态改由 C# 设 → 预览失真。

建议另开 spec：把 `hidden` / `interactable` 纳入与 `RuntimeStateAttr` 同款的「声明值 = 初态；运行期改过不打回；
没动过时 Variant 照常覆盖」，基线取"上次 apply 后回读的值"（per-Control `_lastAppliedHidden` /
`_lastAppliedInteractable`），`interactable` 未声明时不再写。它与本文正交：`<Pages>` 管"N 选 1"，它管"单个节点"。

## 12. 已定的决策 / 待作者确认

| # | 决策 | 备选 |
|---|---|---|
| D1 | 标签名 `<Pages>`，页 = 直接子节点 | `<Switcher>` / `<Deck>` / `<ViewStack>`；专用 `<Page>` 子元素（§2-E） |
| D2 | 纯容器、无视觉（同 VStack）；要底套 Frame | Frame 模式属性（§2-A）；复制 Frame 的面板层 |
| D3 | 只按 `id` 选页；`selected` 默认第一页 | 下标 |
| D4 | `Tab.bind` 放宽到任意 `IControl` | 宿主多套一层 Frame |
| D5 | 新增公开 `IScreen.FindAll<T>()` 给工具 | 预览工具反射 `Screen.NodeMap`（internal，脆） |
| D6 | v1 拒绝 `<Add into>` 指向 Pages（lint error） | 运行时容忍 + 警告 |
| D7 | lint 五条运行时全是 warning，无硬错误 | `CHILD-HIDDEN` 做硬错误 |
| D8 | 页级动画源 / `transition=` 不进 v1 | — |
| D9 | 文档进主 SKILL.md，不单开 `reference/controls-pages.md` | 单开 |

开放问题（留给 plan / 实现期）：

- 焦点在被停用页内时要不要自动移到新页的第一个 Selectable（`Screen.Focus`）。倾向 v1 不做、与 Tab 一致。
- `OnSelectedChanged` 要不要带 `from`（`(string from, string to)`）。倾向只给 `to`，`from` 订阅方自己记。
- `PUI-PAGES-CHILD-HIDDEN` 是否放宽 `hidden.<variant>`（"某页在 portrait 下不存在"）—— 应改写成
  `selected.portrait` + 该页在 portrait 下永不被选；如有真实需求再议。

## 13. 里程碑

- **M1** 控件 + 注册 + `BuiltinTags` + `PagesTests`（§7 1-13）。
- **M2** `PagesRules` + 四个集合 + IRWalker / ScreenInstantiator 派发 + lint 测试 + XSD 断言。
- **M3** `Tab.bind` 放宽 + `IScreen.FindAll<T>()` + 各自测试。
- **M4** SKILL / master spec / BEST_PRACTICES（§8）。`dotnet format --verify-no-changes`；UnityMCP 跑
  `PromptUGUI.Tests.EditMode` + `EditorOnly` 全绿。
- 宿主（另案，ssw_re_client）：UI Preview 的 Pages 选择器 + 记忆；Planet 迁移。

## 14. 实施记录

分支 `feat/pages`，四个 commit（M1 控件 / M2 lint / M3 Tab.bind + FindAll / M4 文档），每步 UnityMCP 跑
`PromptUGUI.Tests.EditMode`（受影响的测试类）全绿；收尾整套 `PromptUGUI.Tests.EditMode` 4318 条 + `PromptUGUI.Tests.EditorOnly` 348 条全绿，`dotnet format` 干净，
`UIXmlLint -- Runtime/Resources/` 零 issue。

### 14.1 与设计的偏差

- **`if="false"` 不是页级特性**（§4.2 原稿写错）：`if=` 只在模板体内求值（`TemplateExpander.ExpandNode`），
  Screen 里直接写 `<Frame id="b" if="false">` 节点照样存在。测试改为模板 `<Param>` 驱动的 `if="{{detail}}"`。
- **lint 级别**：CLI 没有 warning 级（`UIXmlLint` 对每条 issue 都非零退出），`PUI-PAGES-EMPTY` 在 CLI 也是 error；
  运行时五条里 `CHILD-ID` / `SELECTED` 由控件自己连同处置一起报（`ScreenInstantiator` 跳过这两码），
  `ADD-TARGET` 要看 Variant 块、CLI-only。
- **子节点没 id 的运行时警告由控件发**（`Pages.Reconcile`，一次），不经 lint 派发，避免同一件事报两遍。
- **`OnSelectedChanged` 在初始建立时不发**（`previous == null`），只在真正从一页换到另一页时发——含 Variant 重应用。
- **`FindAll<T>()` 的顺序来源**：静态树走 `Def.Root`（`_nodeMap` 是无序字典），Add 块的节点跟在其后，动态子树按登记
  顺序从各自 `Root.SourceNode` 走（同一列表的行共享 ElementNode，必须经子树自己的 map）。
- `Tab._boundFrame` 字段名未改（类型改为 `IControl`），`SetActive` 仍走 `GameObject`（与 Frame 时代行为一致）。

### 14.2 未做 / 另案

- §9 宿主侧：UI Preview 的 Pages 选择器 + 按文件记忆；Planet.ui.xml / PlanetDockSection 迁移。
- §11 `hidden` / `interactable` 的运行期接管（另开 spec）。
- §10 全部非目标（过渡动画、hug、动态页、焦点交接）。

### 14.3 工具链笔记

- 连跑两次 EditMode 测试时第二次会被 "Scene(s) Have Been Modified"（Untitled，测试残留）模态框挡住，MCP 表现为
  `ping not answered` + WebSocket 反复断线；用 Win32 枚举窗口点 "Don't Save" 解开，job 会照常跑完。
  跑前把脏 Untitled 场景换回 Login 可预防。
