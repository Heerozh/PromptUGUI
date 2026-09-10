# ScrollList 网格模式 + 静态子节点 —— 需求说明

日期：2026-09-10
提出方：ssw_re 星球面板
状态：需求，待本库 agent 出 design / plan

## 1. 背景：想写什么、现在为什么写不出来

星球面板的「建造槽位」是一块 4 列卡片区：卡片数量由星球等级决定（当前 8 张，会增长），
超过一屏时**按列换行、竖向滚动**，并且 `.ui.xml` 里要能直接写 8 张占位卡，UIPreview / 设计
迭代时不用起 C# 就能看到效果。目标写法：

```xml
<ScrollList id="slots" anchor="stretch" margin="16,0,0,2"
  columns="4" cellSize="66x100" cellSize.portrait="67x100" spacing="4"
  sprite="none" color="#0000" scrollbar="" itemTemplate="BuildSlot">
  <BuildSlot id="slot1" index="1号槽位" icon="Building:MetalExtractor" name="金属精炼厂" level="Lv.5" />
  <BuildSlot id="slot2" … />
  …
</ScrollList>
```

运行期再 `list.BindItems(slots$, (slot, data) => …)` 用真实数据替换占位卡。

现状（`Runtime/Controls/ScrollList.cs`）做不到，两处缺口：

1. **Content 只有 Vertical / Horizontal LayoutGroup**（`ApplyDirection`），没有网格模式 —— 只能单列 / 单行。
2. **不接管 XML 静态子节点**：`ChildHostTransform` 没有覆写，写在 `<ScrollList>` 下的子节点被挂到
   ScrollList 根节点上、落在 Viewport 之外 —— 既不滚动、也不参与 Content 尺寸；唯一的入口是 C# `BindItems`。
   （对比 `<Carousel>`：`ChildHostTransform => _strip` + `OnAfterApply` 里 `SetStaticCards(Children)`，
   静态卡与 `BindItems` 可以共存。）

目前只能退回 `<Grid>`（不滚动）或「行模板 + C# 绑定」（预览是空的）。

## 2. 需求

### R1 网格模式：`columns="N"`

- 新属性 `columns`（int ≥ 1）。写了就把 Content 的布局组换成 `GridLayoutGroup`：
  `constraint = FixedColumnCount(N)`，`startCorner = UpperLeft`，`startAxis = Horizontal`，
  `childAlignment = UpperLeft`。
- 配套 `cellSize="WxH"`（网格模式下**必填**，语义同 `<Grid cellSize>`），`spacing` 支持单值或 `H,V`
  （同 `<Grid>`），`padding` 沿用现有四段写法。
- 网格模式只支持竖向滚动（行向下增长）：`ContentSizeFitter` 竖向 PreferredSize、横向 Unconstrained；
  Content 锚点同现有 vertical 分支。`columns` 与 `direction="horizontal"` 同时出现 = lint error
  （建议码 `PUI-SCROLL-COLUMNS-DIRECTION`）；横向网格（`rows=`）不在本需求内。
- 换组件遵守 `ApplyDirection` 里已有的纪律：只在布局组类型真变时 `DestroyImmediate` 再 `AddComponent`
  （`LayoutGroup` 是 `DisallowMultipleComponent`，延迟 Destroy 会撞）。
- `columns` / `cellSize` / `spacing` 都可 `.variant` 覆盖（星球面板横竖屏格宽不同），走正常 ReSolve。
- `height="hug"` / `clamp(_, hug, N)` 在网格模式下要成立：`IHugContent.ContentSize` 读 Content 的
  preferred 高度，GridLayoutGroup 自己会按行数算，理论上不用改，但要有测试钉住。

### R2 静态子节点进 Content

- `ChildHostTransform => _content`：XML 子节点直接成为 Content 的布局子节点，成为初始 slot 列表。
- 与 `BindItems` 的关系照抄 Carousel：首次 `Rebuild` 时静态卡随 `ClearSlots` 一起清掉，之后 ReSolve
  不再收回静态引用（Carousel 的 `_bound` 标记）。静态卡的 `Dispose` 要能被重复调用（Carousel 注释
  "static cards double-dispose safely"）。
- 静态子节点可以是 Template 调用（如 `BuildSlot`），其 id 按现有规则可取（`slot1/btn`）。
- 有静态子节点时 `itemTemplate` **可选**（只在 `BindItems` 前必须有值，现有 `Rebuild` 的检查保留）；
  现在 "itemTemplate 必填" 的 lint 要相应放宽。
- 子节点的排版属性校验：
  - 网格模式下子节点不得写 `size` / `width` / `height`（同 `<Grid>` 的 `PUI-GRID-CHILD-SIZE`，
    可复用或另起 `PUI-SCROLL-CELL-SIZE`）。
  - 单列 / 单行模式下按 LayoutGroup 子节点规则（`width="stretch"` 合法，`anchor` / `margin`
    报 `PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN`，`flow="false"` 允许）。
  - 无 `<Slot/>` 概念，ScrollList 不是模板。

### R3 滚动条尺寸可配

- 现在滚动条是硬编码 20 宽 + `AutoHideAndExpandViewport`。在 640x360 参考画布下 20 个单位会挤掉
  接近一列（星球面板的格宽只有 66），网格模式基本不能用默认值。
- 需要：`scrollbarWidth="N"`（或等价命名），并允许选择「滚动条叠在内容上、不挤 Viewport」
  （`ScrollbarVisibility.AutoHide`）—— 例如 `scrollbarOverlay="true"`。默认值保持现状不变，
  别动已有调用点。

### R4 配套

- `Editor/XsdGenerator` 重跑，`PromptUGUI.gen.xsd` 带上新属性。
- skill 文档（`authoring-promptugui-xml` 的 `<ScrollList>` 属性表与 uGUI 对照表）加：
  `columns` / `cellSize` / `scrollbarWidth` / 静态子节点语义 / 新 lint 码。
- 测试：`Tests/EditMode/Controls/ScrollListTests.cs` / `ScrollListContentSizingTests.cs` 补：
  columns 切换布局组类型且不重复添加；静态子节点落在 Content 下并计入 Content preferred 高度；
  `BindItems` 一次后静态卡被清掉且不再回来；hug 高度在网格模式下等于行数 × cell + spacing + padding；
  `cellSize.variant` 切换后 GridLayoutGroup.cellSize 跟着变；`columns` + `direction="horizontal"` 被 lint 拒绝。

## 3. 非目标

- 不做虚拟化 / slot 复用池（8～30 张卡，全量实例化即可）。
- 不做横向网格（`rows=`）、不做多模板混排。
- 不改 `<Grid>`（它仍是不滚动的固定网格）。

## 4. 验收

ssw_re 侧把 `Planet.ui.xml` 的 `<Grid id="slots">` 换成上面的 `<ScrollList columns="4">`，
UIPreview 里：8 张占位卡 4x2 排布、不出滚动条；把预置卡加到 12 张时出现第三行并可竖向滚动，
滚动条不挤掉第 4 列；C# `BindItems` 后占位卡消失、真数据卡按同样网格排布。
