# ScrollList 网格模式 + 静态子节点（SGS）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 `<ScrollList>` 能写成「4 列卡片 + 竖向滚动 + XML 里直接摆 8 张占位卡」，且滚动条不再硬吃 20 个设计单位。交付验收：ssw_re 的 `Planet.ui.xml` 把 `<Grid id="slots">` 换成 `<ScrollList columns="4" cellSize="66x100">`，UIPreview 里 8 张占位卡 4×2 排布不出滚动条、12 张出第三行并可滚、`BindItems` 后占位卡消失且真数据按同一网格排布。

**Architecture:** `ScrollList.ApplyDirection` 升级为三态的 `ApplyLayoutMode`（Grid / Horizontal / Vertical），换组仍守「只在类型真变时 `DestroyImmediate` 再 `AddComponent`」；`cellSize` / `spacing` / `padding` 全部落到「存字段 + 换组后统一重放」的既有 pending 模式（`ControlAttributeApplier` 用 `HashSet` 遍历属性，到达顺序不可依赖）。`ChildHostTransform => _content` 把 XML 子节点交给 Content，静态卡的收集与清理照抄 `Carousel` / `CarouselView` 的 `_staticCollected` + `_bound` 双标记。`ScreenInstantiator` 在递归子节点**之前**给 ScrollList 预配置一次 Content 布局组（`PreConfigureContent`）——因为 apply 是 DFS 后序，不预配置的话子节点的 `ApplyCommon` 会读到还没换成 Grid 的 boot 组。lint 侧新增 `ScrollListRules`（两个码）并把 `ScrollList` 加进 IRWalker / ScreenInstantiator 的 layout-group 名单。

**Tech Stack:** Unity 6 / C# (LangVersion 9.0)、uGUI（`ScrollRect` / `GridLayoutGroup` / `ContentSizeFitter` / `Scrollbar`）。无新增包、无新标签。

需求依据：`docs~/superpowers/specs/2026-09-10-scrolllist-grid-and-static-children-requirements.md`（下文 §R1–R4 指该文档）。

## 已对齐的决策（2026-09-10）

| # | 决策 | 说明 |
|---|---|---|
| SGS-D1 | `spacing="a,b"` 两段写法是 **`V,H`** | 以 `Grid.Spacing` 的现有**代码**为准（`parts[0]→vertical`），与本仓库 `padding` 两段（`V,H`）、`margin` 四段（`T,R,B,L`）一致。SKILL.md 第 348 行写的 `H,V` 是**文档 bug**，本 PR 一并修掉并补测试钉住。 |
| SGS-D2 | 网格模式 `GetNativeSize()` 宽按内容算 | `width = padL + columns*cellW + (columns-1)*spacingH + padR`，`height` 仍是 `DefaultMainAxisLength`(200)。不写 size 的 `columns="4" cellSize="66x100"` 至少横着放得下 4 列，而不是塌到 160。 |
| SGS-D3 | `ScrollList.Spacing` 由 `float` 改成 `string` | 支持 `V,H`，与 `Grid.Spacing` 同型。XML / XSD 侧无感（`spacing` 在 `XsdGenerator._commonAttrNames` 里，per-control 类型被 `xs:string` 遮蔽）。仓库内无 `list.Spacing = 4f;` 形式的 C# 调用点。 |
| SGS-D4 | `columns="0"` = 退出网格模式 | `ControlAttributeApplier` 对 variant 覆盖是 `if (v == null) continue`（set-only），所以「不写 columns」无法从 variant 回退。约定 `columns="0"` 走 `direction` 的单列/单行；`columns="4" columns.portrait="0"` 因此能横竖屏切形态。负值按 0 处理。 |
| SGS-D5 | 子节点写 `size`/`width`/`height` 复用 `PUI-GRID-CHILD-SIZE` | 同一个缺陷、同一条修法，`LayoutGroupChildRules.CheckGridChild` 加一个 `parentTag` 参数即可；不另起 `PUI-SCROLL-CELL-SIZE`。 |

## Global Constraints

- **分支**：全部工作在 `feat/scrolllist-grid-static-children`（已建）。**绝不提交到 main。**
- **LangVersion 9.0**：无 primary constructor、无 collection expression `[]`、无 `[field: SerializeField]`。`??=` / target-typed `new()` 可用。
- **不用 System.Threading / Task**：本特性无异步。
- **Core 纯 C# 子集**：新建的 `Runtime/Core/Lint/ScrollListRules.cs` 与改动的 `LayoutGroupChildRules.cs` / `IRWalker.cs` **不得** `using UnityEngine`，也不得反向依赖 `PromptUGUI.Application`。
- **lint**：每个 Task 收尾从仓库根跑 `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`。**不要** `dotnet format analyzers --severity info`。
- **CLI 编译守门**：Task 8 之后跑 `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/`，确认规则改动在 Unity 外仍编译、内置文档零 error。
- **同 PR 必改 SKILL**（英文）：见 Task 10。
- **测试只经 Unity MCP 跑**。**禁止** `execute_menu_item("Assets/Reimport All")`；用 `refresh_unity(mode="force", scope="all")`。
- **Red first**：每个 Task 先写失败测试、跑一次看它**因正确的原因**失败，再写实现。
- **零回归守门**：`ScrollListTests` / `ScrollListContentSizingTests` / `HugSizingTests` / `ClampFitterTests` / `CarouselTests` / `ControlApplyCommonLayoutGroupTests` / `FlowAttributeTests` / `IRWalkerTests` / `LayoutGroupChildRulesTests` / `DocumentLinterTests` 每个里程碑末尾必跑。

**RUN(ClassName) = 跑 EditMode 测试的标准流程：**
1. `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
2. `mcp__UnityMCP__read_console(action="get", types=["error"])` —— 编译错误必须为空才继续
3. `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["ClassName"])` → 轮询 `mcp__UnityMCP__get_test_job(job_id=...)` 直到完成，读 pass/fail；**核对 `summary.total` > 0**

**RUNPLAY(ClassName)** 同上，`mode="PlayMode"` + `assembly_names=["PromptUGUI.Tests.PlayMode"]`（PlayMode 连跑前先 force refresh，否则第二个 job 会 0 条执行还报 Passed）。

---

## File Structure

| 文件 | 责任 | 动作 |
|---|---|---|
| `Runtime/Controls/ScrollList.cs` | `_columns` / `_cellSize` / `_spacingV` / `_spacingH` / `_scrollbarWidth` / `_scrollbarOverlay` 字段；`ApplyLayoutMode` 三态换组；`ApplyGroupMetrics`；`ChildHostTransform => _content`；静态卡收集；`PreConfigureContent`；`GetNativeSize` 网格分支；`ApplyScrollbarMetrics` | Modify |
| `Runtime/Controls/Grid.cs` | `Spacing` 两段顺序补注释（代码不变，钉住 `V,H`） | Modify |
| `Runtime/Application/ScreenInstantiator.cs` | 子节点递归前调 `PreConfigureContent`；`selfIsLayoutGroup` 加 `ScrollList`；派发 `ScrollListRules.CheckScrollList` | Modify |
| `Runtime/Core/Lint/ScrollListRules.cs` | `DeclaresGrid` 谓词 + `PUI-SCROLL-COLUMNS-DIRECTION` / `PUI-SCROLL-COLUMNS-CELLSIZE` | Create |
| `Runtime/Core/Lint/LayoutGroupChildRules.cs` | `CheckGridChild(child, parentTag)` 参数化父标签名 | Modify |
| `Runtime/Core/Lint/IRWalker.cs` | `isLayoutGroup` 加 `ScrollList`；派发 `CheckScrollList` + 网格模式下的 `CheckGridChild` | Modify |
| `Tests/EditMode/Controls/GridSpacingTests.cs` | 钉住 `<Grid spacing="V,H">` | Create |
| `Tests/EditMode/Controls/ScrollListGridTests.cs` | 换组不重复添加 / cellSize / spacing / padding / variant / `columns="0"` 回退 | Create |
| `Tests/EditMode/Controls/ScrollListStaticChildrenTests.cs` | 静态子节点落 Content / id 可取 / BindItems 后清掉且不回来 / ReSolve 存活 | Create |
| `Tests/EditMode/Controls/ScrollListScrollbarTests.cs` | `scrollbarWidth` / `scrollbarOverlay` / 默认值不变 | Create |
| `Tests/EditMode/Controls/ScrollListContentSizingTests.cs` | 网格模式 `GetNativeSize` | Modify |
| `Tests/EditMode/Controls/HugSizingTests.cs` | 网格模式 hug / clamp(hug) | Modify |
| `Tests/EditMode/Lint/ScrollListRulesTests.cs` | 两个新码 + `DeclaresGrid` | Create |
| `Tests/EditMode/Lint/LayoutGroupChildRulesTests.cs` | `CheckGridChild` parentTag | Modify |
| `Tests/EditMode/Lint/IRWalkerTests.cs` | ScrollList 子节点 anchor/margin 派发；网格子节点 size 派发 | Modify |
| `Tests/EditMode/Editor/XsdGeneratorTests.cs` | 新属性出现在 ScrollList 的 complexType | Modify |
| `Tests/PlayMode/Controls/CommonControlsPlayTests.cs` | 网格 + 静态卡过帧 smoke | Modify |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` | `<ScrollList>` 属性表 + 静态子节点语义 + uGUI 对照表行 + Grid `spacing` 行修正 + 错误表 | Modify |
| `.lint/UIXmlLint/README.md` | 两个新码 + `PUI-GRID-CHILD-SIZE` 行补 ScrollList | Modify |
| `docs~/superpowers/specs/2026-09-10-scrolllist-grid-and-static-children-requirements.md` | 末尾加「实施记录」节 | Modify |

---

## Task 0: 落 plan

- [ ] 分支 `feat/scrolllist-grid-static-children` 已建（`git checkout -b` 已执行）。
- [ ] `git add docs~/superpowers/plans/2026-09-10-scrolllist-grid-and-static-children.md` 并提交（分支上）。

---

# M1 —— 网格模式（§R1）

## Task 1: `spacing` 支持两段 `V,H`（SGS-D1 / SGS-D3）

**Files:**
- Modify: `Runtime/Controls/ScrollList.cs`
- Modify: `Runtime/Controls/Grid.cs`（只补注释）
- Test: `Tests/EditMode/Controls/GridSpacingTests.cs`（Create）

**Interface：**
```csharp
// ScrollList
private float _spacingV;   // 竖向间距（VerticalLayoutGroup.spacing / GridLayoutGroup.spacing.y）
private float _spacingH;   // 横向间距（HorizontalLayoutGroup.spacing / GridLayoutGroup.spacing.x）

/// <summary>Gap between items. One value = both axes; <c>"V,H"</c> = vertical, horizontal —
/// the same order <c>&lt;Grid spacing&gt;</c> and the two-part <c>padding</c> use.</summary>
[UIAttr, Preserve]
public string Spacing { set { ParseSpacing(value, out _spacingV, out _spacingH); ApplyGroupMetrics(); } }
```

`ParseSpacing`：空/null → `0,0`；1 段 → 两轴同值；2 段 → `parts[0]=V`、`parts[1]=H`；其它段数抛 `ArgumentException`（同 `VStack.ParseTRBL` 的语气）。`_` 与空串按 0。

`ApplyGroupMetrics()`（由 `ApplySpacingPadding` 改名扩容）：
```csharp
switch (_layoutGroup)
{
    case GridLayoutGroup g: g.spacing = new Vector2(_spacingH, _spacingV); break;   // Vector2 是 (x=H, y=V)
    case HorizontalLayoutGroup h: h.spacing = _spacingH; break;
    case VerticalLayoutGroup v: v.spacing = _spacingV; break;
}
```
padding 改走 `VStack.ParseTRBL`（顺带获得 `_` 支持与 3 段报错，与 VStack/HStack/Grid 一致），解析结果同时缓存到 `_padT/_padR/_padB/_padL` 供 `GetNativeSize` 用。

- [ ] **Step 1: 红测试** —— `GridSpacingTests`：
  - `<Grid spacing='10,20'>` → `GridLayoutGroup.spacing == (20, 10)`（x=H=20、y=V=10）。**这条现在就该绿**，作用是把现有代码行为钉死，防止后续照文档「修」反。
  - `<Grid spacing='6'>` → `(6, 6)`。
- [ ] **Step 2**: RUN(GridSpacingTests) —— 应全绿（回归钉子）。
- [ ] **Step 3: 红测试** —— 在 `ScrollListGridTests`（Task 2 建，本步先建文件放这两条）：
  - `<ScrollList direction='vertical' spacing='10,20'>` → `VerticalLayoutGroup.spacing == 10f`。
  - `<ScrollList direction='horizontal' spacing='10,20'>` → `HorizontalLayoutGroup.spacing == 20f`。
  - `<ScrollList spacing='4'>` → 竖向 4（回归：单值写法不变）。
- [ ] **Step 4**: RUN(ScrollListGridTests) 红（`Spacing` 还是 float，`'10,20'` 转换失败）。
- [ ] **Step 5**: 实现 `ParseSpacing` / `ApplyGroupMetrics` / padding 走 `ParseTRBL`；`Grid.Spacing` 只在 `parts.Length == 2` 分支上方补一行注释说明「written order is V,H」。
- [ ] **Step 6**: RUN(ScrollListGridTests) + RUN(GridSpacingTests) + RUN(ScrollListTests) + RUN(ScrollListContentSizingTests) 全绿。
- [ ] **Step 7**: `dotnet format --verify-no-changes`；提交 `feat(scrolllist): spacing accepts "V,H" like <Grid>`。

## Task 2: `columns` / `cellSize` → 三态 `ApplyLayoutMode`（§R1）

**Files:**
- Modify: `Runtime/Controls/ScrollList.cs`
- Test: `Tests/EditMode/Controls/ScrollListGridTests.cs`

**Interface：**
```csharp
private int _columns;               // 0 = 不用网格（SGS-D4）
private Vector2? _cellSize;

/// <summary>Column count for the grid mode. <c>0</c> falls back to the single column / row that
/// <c>direction</c> describes — spell it out (rather than dropping the attribute) when a variant
/// has to leave the grid, because a variant that resolves to null is skipped, not reverted.</summary>
[UIAttr, Preserve]
public int Columns { set { _columns = Mathf.Max(0, value); ApplyLayoutMode(); } }

/// <summary>Uniform cell size <c>"WxH"</c>. Required in grid mode (<c>PUI-SCROLL-COLUMNS-CELLSIZE</c>).</summary>
[UIAttr, Preserve]
public string CellSize { set { _cellSize = ParseWxH(value); ApplyGroupMetrics(); } }

internal bool IsGrid => _columns >= 1 && _direction != "horizontal";
```

`ApplyLayoutMode()`（原 `ApplyDirection` 改名）：
```csharp
var wantGrid = IsGrid;                       // columns>=1 且非 horizontal
var wantHorizontal = !wantGrid && _direction == "horizontal";
var wantType = wantGrid ? typeof(GridLayoutGroup)
             : wantHorizontal ? typeof(HorizontalLayoutGroup)
             : typeof(VerticalLayoutGroup);
if (_layoutGroup == null || _layoutGroup.GetType() != wantType) { DestroyImmediate; AddComponent; }
```
换组纪律与注释原样保留（`LayoutGroup` 是 `DisallowMultipleComponent`，延迟 Destroy 会撞）。**只在类型真变时换**这一条是关键：ReSolve 每轮都会重放 setter，无变化必须零副作用。

网格分支的其余配置 = 现有 vertical 分支逐字相同（`_scroll.vertical = true`、Content 顶部锚点、`ContentSizeFitter` 竖 PreferredSize / 横 Unconstrained、`EnsureVerticalScrollbar`），只多：
```csharp
grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
grid.constraintCount = _columns;
grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
grid.startAxis = GridLayoutGroup.Axis.Horizontal;
grid.childAlignment = TextAnchor.UpperLeft;
```
`ApplyGroupMetrics` 里补 `if (_layoutGroup is GridLayoutGroup g && _cellSize.HasValue) g.cellSize = _cellSize.Value;`。

- [ ] **Step 1: 红测试** —— `ScrollListGridTests`：
  - `columns='4' cellSize='66x100'` → Content 上有 `GridLayoutGroup`，且 `VerticalLayoutGroup`/`HorizontalLayoutGroup` 都为 null（`GetComponents<LayoutGroup>().Length == 1`）。
  - `constraint == FixedColumnCount`、`constraintCount == 4`、`startCorner == UpperLeft`、`startAxis == Horizontal`、`childAlignment == UpperLeft`、`cellSize == (66,100)`。
  - `_scroll.vertical == true && horizontal == false`；`ContentSizeFitter` 竖 `PreferredSize`、横 `Unconstrained`；Content `anchorMin==(0,1)`、`anchorMax==(1,1)`、`pivot==(0.5,1)`。
  - **属性顺序无关**：`cellSize` 写在 `columns` 前面（`<ScrollList cellSize='66x100' columns='4'/>`）结果一致 —— 钉住 `HashSet` 遍历顺序不可依赖。
  - **换组不重复添加**：base `columns='4'`、variant `columns.portrait='0'`，切到 portrait 后 `GetComponents<LayoutGroup>().Length == 1` 且是 `VerticalLayoutGroup`；切回来又是 `GridLayoutGroup` 且 `Length == 1`。
  - **无变化不换组**：记下 `GridLayoutGroup` 实例引用，`screen.ReSolve()` 后引用不变（`Assert.AreSame`）。
  - `cellSize.portrait='67x100'` 切 variant 后 `GridLayoutGroup.cellSize.x == 67`（§R1 最后一条 / §R4 测试清单）。
  - `spacing='4'` + 网格 → `GridLayoutGroup.spacing == (4,4)`；`spacing='4,8'` → `(8,4)`。
  - `padding='2,3,4,5'` + 网格 → `GridLayoutGroup.padding == RectOffset(left:5, right:3, top:2, bottom:4)`。
  - `columns='4' direction='horizontal'` → 仍是 `HorizontalLayoutGroup`（运行时以 direction 为准；lint 在 Task 8 报错）。
- [ ] **Step 2**: RUN(ScrollListGridTests) 红。
- [ ] **Step 3**: 实现。`ParseWxH` 复用 `Grid.CellSize` 的写法但**不抛裸异常** —— 格式错时 `Debug.LogWarning` + 保持旧值（同 `Carousel.DotSize` 的先例）。
- [ ] **Step 4**: RUN(ScrollListGridTests) 绿；RUN(ScrollListTests) / RUN(ScrollListContentSizingTests) / RUN(HugSizingTests) 回归绿。
- [ ] **Step 5**: `dotnet format --verify-no-changes`；提交 `feat(scrolllist): columns + cellSize switch Content to GridLayoutGroup`。

## Task 3: 网格模式 `GetNativeSize`（SGS-D2）

**Files:**
- Modify: `Runtime/Controls/ScrollList.cs`
- Test: `Tests/EditMode/Controls/ScrollListContentSizingTests.cs`

**Interface：**
```csharp
public override Vector2? GetNativeSize()
{
    if (IsGrid && _cellSize.HasValue)
    {
        // The cells are authoritative in grid mode: a list that defaults to 160 wide would clip the
        // 4th of four 66-wide columns before the author ever sees it. Height keeps the viewport
        // default — how many rows are visible is a viewport choice, not a content one.
        var w = _padL + _padR + _columns * _cellSize.Value.x
              + Mathf.Max(0, _columns - 1) * _spacingH;
        return new Vector2(w, DefaultMainAxisLength);
    }
    return _direction == "horizontal" ? ... ;   // 原样
}
```
**注意**：不把滚动条宽度算进去。`scrollbarOverlay="false"`（默认）时 `AutoHideAndExpandViewport` 仍会从视口里吃掉 `scrollbarWidth - 3`；作者要 4 列完整可见就写 `scrollbarOverlay="true"` 或显式 size。这一点写进 SKILL。

- [ ] **Step 1: 红测试** —— `ScrollListContentSizingTests` 新增：
  - `<ScrollList columns='4' cellSize='66x100' spacing='4' padding='0'/>` 在 Frame 里不写 size → `GetNativeSize() == (66*4 + 4*3, 200) == (276, 200)`。
  - 加 `padding='0,8,0,8'` → 宽 `+16 == 292`。
  - `columns='4'` 但**不写** `cellSize` → 回落到 `(160, 200)`（不猜 GridLayoutGroup 的 100×100 默认值）。
  - `columns='0'` → 与今天的 `(160, 200)` 逐字相同（回归）。
  - `columns='4' direction='horizontal'` → `(200, 160)`（非网格，回归）。
  - 不写 size 时 `RectTransform.sizeDelta` 跟着 native 走（沿用该文件既有的 `..._sizeDelta_matches_native` 断言形状）。
- [ ] **Step 2**: RUN(ScrollListContentSizingTests) 红。
- [ ] **Step 3**: 实现（含 `_padT/_padR/_padB/_padL` 缓存字段，Task 1 已备）。
- [ ] **Step 4**: RUN(ScrollListContentSizingTests) 绿；RUN(ScrollListGridTests) 回归绿。
- [ ] **Step 5**: `dotnet format --verify-no-changes`；提交 `feat(scrolllist): grid-mode native width follows columns x cellSize`。

## Task 4: 网格模式下 hug / clamp(hug) 成立（§R1 最后一条）

**Files:**
- Test only: `Tests/EditMode/Controls/HugSizingTests.cs`
- Modify（预期为空）: `Runtime/Controls/ScrollList.cs`

`IHugContent.ContentSize` 读的是 `LayoutUtility.GetPreferredSize(_content, axis)`；`GridLayoutGroup.CalculateLayoutInputVertical` 对 `FixedColumnCount` 的行数是 `ceil(count / columns)`、与 Content 宽度无关，所以**理论上零实现改动**。本 Task 的产出就是把这条钉住。

- [ ] **Step 1: 红/绿测试** —— `HugSizingTests` 新增（沿用该文件的 `ListDoc` / `OpenList` / `Drain` 骨架，加一个网格版 `GridListDoc`）：
  - `columns='3' cellSize='40x40' spacing='0' height='hug'` + 7 张静态卡 → 列表高 `== 3*40 == 120`（3 行）。
  - 同上 `spacing='4,4'` → `3*40 + 2*4 == 128`；再加 `padding='6,0,6,0'` → `+12 == 140`。
  - `height='clamp(_, hug, 100)'` + 7 张卡 → 列表高 100，且 `content.rect.height > 100`（还能滚）。
  - `LayoutUtility.GetPreferredSize(content, 1) == list.RectTransform.rect.height`（hug 的契约：我的高就是内容的 preferred 高）。
  - 卡片来源分别用「静态子节点」和「`BindItems`」各跑一遍，结果一致。
- [ ] **Step 2**: RUN(HugSizingTests)。**若绿**：记录「零实现改动」，跳到 Step 4。**若红**：诊断（大概率是 `_content` 自带 `ContentSizeFitter` 使它成为独立 rebuild root、深度排序导致首帧读到陈旧 preferred），在 `ScrollList` 里补一次 `LayoutRebuilder.MarkLayoutForRebuild(_content)`（换组后）再跑。
- [ ] **Step 3**: 若有实现改动，重跑 RUN(HugSizingTests) 直到绿。
- [ ] **Step 4**: RUN(ClampFitterTests) 回归绿。
- [ ] **Step 5**: `dotnet format --verify-no-changes`；提交 `test(scrolllist): pin hug/clamp(hug) in grid mode`。

---

# M2 —— 静态子节点（§R2）

## Task 5: `ChildHostTransform => _content` + 静态卡收集

**Files:**
- Modify: `Runtime/Controls/ScrollList.cs`
- Test: `Tests/EditMode/Controls/ScrollListStaticChildrenTests.cs`（Create）

**Interface：**
```csharp
// 静态 XML 子卡与 BindItems 建的卡都进 Content（同 Carousel 的 _strip）。
protected internal override Transform ChildHostTransform => _content;

private bool _staticCollected;
private bool _bound;

internal override void OnAfterApply()
{
    base.OnAfterApply();
    // 首次 apply 把已建好的静态子卡收进 _slots；只跑一次，且 BindItems 调过之后（_bound）不再收，
    // 免得 ReSolve 把已 Dispose 的旧引用收回来。Apply 是 DFS 后序 —— 到这里子节点已全部建好并 apply 完。
    if (!_staticCollected && !_bound)
    {
        _staticCollected = true;
        foreach (var c in Children) _slots.Add(c);
    }
    ...既有 mask / frame 逻辑...
}

private void ClearSlots()
{
    _bound = true;   // 标记已动态绑定：之后 ReSolve 的静态收集不再执行
    foreach (var s in _slots) s.Dispose();   // 静态卡可安全重复 Dispose（HostGameObject == null 早返）
    _slots.Clear();
}
```
`Dispose()` 保持 `ClearSlots(); base.Dispose();` 不变（静态卡会被 double-dispose，安全）。
`Rebuild` 里 `itemTemplate` 未设仍抛 `InvalidOperationException`（§R2「只在 BindItems 前必须有值」）。

- [ ] **Step 1: 红测试** —— `ScrollListStaticChildrenTests`：
  - `<ScrollList id='sl'><Frame id='a'/><Frame id='b'/></ScrollList>` → `a` / `b` 的 `transform.parent` 是 `sl/Viewport/Content`；`sl.SlotCount == 2`。
  - 静态卡计入 Content preferred 高：`LayoutUtility.GetPreferredSize(content, 1) > 0` 且等于两卡高之和（`spacing='0'`）。
  - `itemTemplate` **不写**也能开：`<ScrollList id='sl'><Frame id='a'/></ScrollList>` 不抛。
  - 静态子节点是 Template 调用：`<Template name='BuildSlot'><Frame><Btn id='btn'/></Frame></Template>` + `<ScrollList><BuildSlot id='slot1'/></ScrollList>` → `screen.Get<Btn>("slot1/btn")` 拿得到。
  - `BindItems` 一次后：静态卡的 `GameObject == null`、`SlotCount == items.Count`、Content 的 child 全是新卡。
  - `BindItems` 之后再 `screen.ReSolve()` → `SlotCount` 不变（静态引用没被收回来）、不抛。
  - 未 `BindItems` 时 `screen.ReSolve()` 两次 → `SlotCount` 仍是静态卡数（`_staticCollected` 只收一次，不重复累加）。
  - `itemTemplate` 未设时调 `BindItems` → `InvalidOperationException`（回归）。
  - `screen.Close()` 不抛（double-dispose 路径）。
- [ ] **Step 2**: RUN(ScrollListStaticChildrenTests) 红。
- [ ] **Step 3**: 实现。
- [ ] **Step 4**: RUN(ScrollListStaticChildrenTests) 绿；RUN(ScrollListTests) / RUN(CarouselTests) / RUN(CarouselBindItemsTests) 回归绿。
- [ ] **Step 5**: `dotnet format --verify-no-changes`；提交 `feat(scrolllist): XML children become Content slots`。

## Task 6: ScrollList 进 layout-group 名单 + Content 预配置

**Files:**
- Modify: `Runtime/Application/ScreenInstantiator.cs`
- Modify: `Runtime/Controls/ScrollList.cs`
- Test: `Tests/EditMode/Controls/ScrollListStaticChildrenTests.cs`

**背景（必须理解，否则会写出「首帧一个样、ReSolve 另一个样」的 bug）：** apply 是 DFS **后序** —— 子节点的 `ApplyCommon` 在父 ScrollList 的 apply **之前**跑。`Control.ApplyCommon` 读的是 `LayoutHost.parent.GetComponent<LayoutGroup>()` 的**实时组件**，而 `columns` 要到父节点 apply 时才换组，所以不预配置的话，网格模式的子节点第一遍会按 `VerticalLayoutGroup` 解算（`parentIsGrid == false`、走 `fillCrossX` 分支）。同理 `<Text scale=>` 的 scale-host wrapper 判据（`GetComponent<HorizontalOrVerticalLayoutGroup>()`）也会误判 —— `<Grid>` 本来靠「GridLayoutGroup 不是 HorizontalOrVerticalLayoutGroup」自动豁免，ScrollList 也得走到同一状态。

**Interface：**
```csharp
// ScrollList
/// <summary>
/// Configure the Content layout group from the node's own declaration BEFORE its children are
/// instantiated into it. The apply pass is DFS post-order, so a child resolves its geometry against
/// whatever group Content carries at instantiation time; without this a grid list's children would
/// measure against the boot VerticalLayoutGroup on the first pass and against the GridLayoutGroup on
/// every ReSolve after. Idempotent — the real setters still run in the apply pass.
/// </summary>
internal void PreConfigureContent(string direction, string columns)
{
    if (!string.IsNullOrEmpty(direction)) _direction = direction;
    if (int.TryParse(columns, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        _columns = Math.Max(0, n);
    ApplyLayoutMode();
}
```

```csharp
// ScreenInstantiator.InstantiateRecursive，childScope 建好之后、子节点循环之前
if (control is Controls.ScrollList scrollList)
    scrollList.PreConfigureContent(
        VariantResolver.ResolveAttribute(node, "direction", _variants),
        VariantResolver.ResolveAttribute(node, "columns", _variants));

var selfIsLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid" or "TabBar" or "TabMenu"
                                 or "Carousel" or "Collapsible" or "ScrollList";
```

- [ ] **Step 1: 红测试** —— `ScrollListStaticChildrenTests` 新增：
  - 网格 ScrollList 的静态子节点在**首次 Open**（未经 ReSolve）后，其 `RectTransform.sizeDelta` 与 `ReSolve()` 之后逐字相同 —— 首帧与再解算一致。
  - 网格 ScrollList 下的 `<Text scale='2'>` **没有** `[scale-host]` wrapper 父节点（与 `<Grid>` 一致）；非网格 ScrollList 下的同一写法**有** wrapper。（对照 `ScaledTextWrapperTests` 的 Grid 用例）
  - `<ScrollList><Frame anchor='top-left'/></ScrollList>` → 运行时 `Debug.LogWarning` 含 `PUI-LAYOUT-ANCHOR` 的文案（用 `LogAssert.Expect`，参照 `Tests/PlayMode/Lifecycle/StackChildWarningTests.cs` 的正则形状；EditMode 版放本文件）。
  - `<ScrollList><Frame margin='4'/></ScrollList>` → `PUI-LAYOUT-MARGIN` 文案。
  - `<ScrollList><Frame flow='false'/></ScrollList>` → 不报（`MightBeOutOfFlow` 豁免）。
- [ ] **Step 2**: RUN(ScrollListStaticChildrenTests) 红。
- [ ] **Step 3**: 实现（`PreConfigureContent` + `selfIsLayoutGroup` 加名）。注意 `LayoutGroupChildRules.CheckNonLayoutChild`（`flow` inert）现在对 ScrollList 子节点**不再**触发 —— 这正是期望。
- [ ] **Step 4**: RUN(ScrollListStaticChildrenTests) 绿；RUN(ScaledTextWrapperTests) / RUN(ControlApplyCommonLayoutGroupTests) / RUN(FlowAttributeTests) / RUN(ScrollListGridTests) / RUN(HugSizingTests) 回归绿。
- [ ] **Step 5**: `dotnet format --verify-no-changes`；提交 `feat(scrolllist): Content group is configured before children; ScrollList joins the layout-group list`。

---

# M3 —— 滚动条尺寸（§R3）

## Task 7: `scrollbarWidth` / `scrollbarOverlay`

**Files:**
- Modify: `Runtime/Controls/ScrollList.cs`
- Test: `Tests/EditMode/Controls/ScrollListScrollbarTests.cs`（Create）

**Interface：**
```csharp
private float _scrollbarWidth = 20f;    // 默认保持现状（§R3「别动已有调用点」）
private bool _scrollbarOverlay;         // false = AutoHideAndExpandViewport（现状）

/// <summary>Thickness of the scrollbar: the width of the vertical bar, the height of the horizontal
/// one. Both directions share it, like the rest of the scrollbar skin. Default 20 — at a 640x360
/// reference canvas that is most of a 66-wide grid column, so grid lists usually want a smaller
/// value or <c>scrollbarOverlay="true"</c>.</summary>
[UIAttr, Preserve]
public float ScrollbarWidth { set { _scrollbarWidth = Mathf.Max(0f, value); ApplyScrollbarMetrics(); } }

/// <summary>Draw the scrollbar ON TOP of the content instead of shrinking the viewport for it
/// (<c>ScrollbarVisibility.AutoHide</c> vs the default <c>AutoHideAndExpandViewport</c>).</summary>
[UIAttr, Preserve]
public bool ScrollbarOverlay { set { _scrollbarOverlay = value; ApplyScrollbarMetrics(); } }
```

`ApplyScrollbarMetrics()` 与既有 `ApplyScrollbarSkin()` 同构（两根都刷、滚动条懒建所以属性先写后建也生效）：
- vertical：`rt.sizeDelta = (w, 0)`；horizontal：`rt.sizeDelta = (0, w)`
- 两者：`handleRect.parent.sizeDelta = (-w, -w)`、`handleRect.sizeDelta = (w, w)`（Sliding Area 由 `bar.handleRect.parent` 取，不新增字段）
- `_scroll.verticalScrollbarVisibility = _scrollbarOverlay ? AutoHide : AutoHideAndExpandViewport`（horizontal 同）
- `_scroll.verticalScrollbarSpacing = Mathf.Max(-w, -3f)` —— 保住默认 20 → `-3` 的现状，同时挡住 `w < 3` 时视口反而变大
- `EnsureVerticalScrollbar` / `EnsureHorizontalScrollbar` 里的三处硬编码 20 改成读 `_scrollbarWidth`，并在结尾调 `ApplyScrollbarMetrics()`

- [ ] **Step 1: 红测试** —— `ScrollListScrollbarTests`：
  - **默认不变**：不写新属性 → 竖条 `rt.sizeDelta.x == 20`、Sliding Area `(-20,-20)`、handle `(20,20)`、`verticalScrollbarVisibility == AutoHideAndExpandViewport`、`verticalScrollbarSpacing == -3`。横条镜像。
  - `scrollbarWidth='6'` → 竖条 `sizeDelta.x == 6`、Sliding `(-6,-6)`、handle `(6,6)`、`verticalScrollbarSpacing == -3`。
  - `scrollbarWidth='2'` → `verticalScrollbarSpacing == -2`（不让视口变大）。
  - `direction='horizontal' scrollbarWidth='6'` → 横条 `sizeDelta.y == 6`。
  - `scrollbarOverlay='true'` → `verticalScrollbarVisibility == AutoHide`；`false` → `AutoHideAndExpandViewport`。
  - **写在建条之前也生效**：`<ScrollList scrollbarWidth='6' direction='vertical'/>`（属性顺序打乱两种排列各测一次）→ 结果一致。
  - `scrollbarWidth.portrait='4'` variant 切换 → `sizeDelta.x` 跟着变（ReSolve 重放）。
  - 既有 `scrollbar` / `scrollbarColor` / `scrollbarHandle` / `scrollbarHandleColor` 与新属性共存不互相清掉。
- [ ] **Step 2**: RUN(ScrollListScrollbarTests) 红。
- [ ] **Step 3**: 实现。
- [ ] **Step 4**: RUN(ScrollListScrollbarTests) 绿；RUN(ScrollListTests) / RUN(DefaultSkinTests) 回归绿。
- [ ] **Step 5**: `dotnet format --verify-no-changes`；提交 `feat(scrolllist): scrollbarWidth + scrollbarOverlay`。

---

# M4 —— lint / XSD / 文档（§R4）

## Task 8: `ScrollListRules` + `CheckGridChild` 参数化 + 两处接线

**Files:**
- Create: `Runtime/Core/Lint/ScrollListRules.cs`
- Modify: `Runtime/Core/Lint/LayoutGroupChildRules.cs`
- Modify: `Runtime/Core/Lint/IRWalker.cs`
- Modify: `Runtime/Application/ScreenInstantiator.cs`
- Test: `Tests/EditMode/Lint/ScrollListRulesTests.cs`（Create）
- Test: `Tests/EditMode/Lint/LayoutGroupChildRulesTests.cs`、`Tests/EditMode/Lint/IRWalkerTests.cs`

**Interface（纯 C#，`using UnityEngine` 禁止）：**
```csharp
public static class ScrollListRules
{
    public const string ColumnsDirectionCode = "PUI-SCROLL-COLUMNS-DIRECTION";
    public const string ColumnsCellSizeCode  = "PUI-SCROLL-COLUMNS-CELLSIZE";

    /// <summary>True when this &lt;ScrollList&gt; asks for the grid in ANY configuration — a base
    /// <c>columns</c> or any <c>columns.variant</c> that is not "0". Declared, not resolved, the same
    /// way <c>MightBeOutOfFlow</c> reads flow: the CLI and the runtime must agree on which documents
    /// carry a grid.</summary>
    public static bool DeclaresGrid(ElementNode n);

    /// <summary>Self-check on the &lt;ScrollList&gt; node. Dispatched by both IRWalker (CLI error)
    /// and ScreenInstantiator (runtime warning).</summary>
    public static IEnumerable<LintIssue> CheckScrollList(ElementNode n);
}
```
- `PUI-SCROLL-COLUMNS-DIRECTION`：`DeclaresGrid(n)` 且 `direction`（base 或任一 variant）为 `horizontal`。文案：横向网格（`rows=`）不在 v1 内；要么去掉 `columns`，要么去掉 `direction="horizontal"`。
- `PUI-SCROLL-COLUMNS-CELLSIZE`：`DeclaresGrid(n)` 且 `cellSize` 在 base / 任一 variant / `class=`（走 `StyleAttributeView`）里都没有。文案：网格模式的格子尺寸只能由 `cellSize` 给，缺省会落到 `GridLayoutGroup` 的 100×100。

`LayoutGroupChildRules.CheckGridChild(ElementNode child, string parentTag = "Grid")` —— 文案里的 `<Grid>` / `<Grid cellSize="WxH">` 换成 `<{parentTag}>` / `<{parentTag} cellSize="WxH">`。既有调用点不传参、行为逐字不变（SGS-D5）。

`IRWalker` 改两处：
```csharp
var isLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid" or "TabBar" or "TabMenu"
                             or "Carousel" or "Collapsible" or "ScrollList";
...
else if (node.Tag == "ScrollList")
    foreach (var issue in ScrollListRules.CheckScrollList(node, styles)) yield return issue;
...
// 子节点循环里
if (node.Tag == "Grid")
    foreach (var issue in LayoutGroupChildRules.CheckGridChild(child)) ...
else if (node.Tag == "ScrollList" && ScrollListRules.DeclaresGrid(node))
    foreach (var issue in LayoutGroupChildRules.CheckGridChild(child, "ScrollList")) ...
```
`ScreenInstantiator` 只派发 `CheckScrollList`（`CheckGridChild` 按其 XML 注释保持 **CLI-only**：作者写了个我们忽略的东西，运行时没有可见缺陷）。

- [ ] **Step 1: 红测试** —— `ScrollListRulesTests`（纯 IR，不起 Unity 场景，参照 `CarouselRulesTests`）：
  - `DeclaresGrid`：`columns='4'` → true；`columns='0'` → false；无 `columns` → false；`columns='0' columns.portrait='4'` → true；`columns='4' columns.portrait='0'` → true。
  - `columns='4' direction='horizontal'` → 一条 `PUI-SCROLL-COLUMNS-DIRECTION`；`direction.portrait='horizontal'` 同样命中；`columns='0' direction='horizontal'` → 无。
  - `columns='4'` 无 `cellSize` → `PUI-SCROLL-COLUMNS-CELLSIZE`；有 `cellSize` / `cellSize.portrait` / `class=` 带 `cellSize` → 无。
  - `columns='0'` 无 `cellSize` → 无。
- [ ] **Step 2**: `LayoutGroupChildRulesTests` 加：`CheckGridChild(child, "ScrollList")` 的文案含 `<ScrollList cellSize="WxH">`；默认调用文案逐字不变。
- [ ] **Step 3**: `IRWalkerTests` 加：
  - `<ScrollList><Frame anchor='top-left'/></ScrollList>` → `PUI-LAYOUT-ANCHOR`；`margin` → `PUI-LAYOUT-MARGIN`；`flow='false'` → 都不报。
  - `<ScrollList columns='4' cellSize='40x40'><Frame width='30'/></ScrollList>` → `PUI-GRID-CHILD-SIZE`，且 message 里是 `<ScrollList`。
  - `<ScrollList columns='0'><Frame width='30'/></ScrollList>` → 不报 size（单列模式下子节点的 size 有意义）。
  - `<ScrollList><Frame flow='false'/></ScrollList>` → 不报 `PUI-FLOW-OUTSIDE-GROUP`（ScrollList 现在是 layout group）。
- [ ] **Step 4**: RUN(ScrollListRulesTests) / RUN(LayoutGroupChildRulesTests) / RUN(IRWalkerTests) 红。
- [ ] **Step 5**: 实现三个文件 + `ScreenInstantiator` 派发。
- [ ] **Step 6**: RUN(ScrollListRulesTests) / RUN(LayoutGroupChildRulesTests) / RUN(IRWalkerTests) / RUN(DocumentLinterTests) / RUN(LintOriginTests) 全绿。
- [ ] **Step 7**: **CLI 编译守门** —— `cd .lint && dotnet restore PromptUGUI.Lint.slnx`，再从仓库根 `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` → exit 0、零 error（证明规则改动没把 `UnityEngine` 带进 Core 子集）。
- [ ] **Step 8**: `dotnet format --verify-no-changes`；提交 `feat(lint): ScrollList grid rules + ScrollList joins the layout-group list`。

## Task 9: XSD 重跑

**Files:**
- Modify: `Tests/EditMode/Editor/XsdGeneratorTests.cs`

新属性全部由 `[UIAttr]` 反射产出，`XsdGenerator` 无需改代码：`columns` → `xs:int`、`cellSize` → `xs:string`、`scrollbarWidth` → `xs:float`、`scrollbarOverlay` → `xs:boolean`。`spacing` 在 `_commonAttrNames` 里，per-control 不再发一份，所以 SGS-D3 的类型变化对 XSD 不可见。`controlGroup` 本来就允许任意控件带子节点，`<ScrollList>` 下写 `<BuildSlot/>` 不需要额外改动。

- [ ] **Step 1: 红测试** —— `XsdGeneratorTests` 加（沿用该文件的 `StringAssert.Contains` 风格）：
  - 生成结果里 ScrollList 的 complexType 含 `name="columns" type="xs:int"`、`name="cellSize"`、`name="scrollbarWidth" type="xs:float"`、`name="scrollbarOverlay" type="xs:boolean"`。
  - 结果仍能 `XmlSchemaSet.Compile()`（该文件已有的编译断言）。
- [ ] **Step 2**: RUN(XsdGeneratorTests) 红 → 实现（预期只需测试改动；若红是因为反射漏了属性，回头补 `[UIAttr]`）。
- [ ] **Step 3**: RUN(XsdGeneratorTests) 绿。
- [ ] **Step 4**: 在宿主工程跑一次 `Tools → PromptUGUI → Schema → Generate XSD`（`mcp__UnityMCP__execute_menu_item`，菜单名以 `Editor/XsdMenu.cs` 的 `[MenuItem]` 为准），确认 `Assets/PromptUGUI.gen.xsd` 的 diff 只多这四个属性。
- [ ] **Step 5**: 提交 `test(xsd): pin the new ScrollList attributes`。

## Task 10: SKILL 文档（英文）+ Grid `spacing` 文档修正

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`
- Modify: `.lint/UIXmlLint/README.md`

改动清单：
1. `<Grid>` 属性表（约 348 行）`spacing` 一行：`single 或 H,V` → `single 或 V,H`（SGS-D1，代码本来就是 V,H）。
2. `<ScrollList>` 小节开头改口：不再是「项只能 C# 侧 `BindItems` 注入」—— XML 子节点直接成为初始 slot；`BindItems` 第一次调用会把静态卡清掉（同 `<Carousel>`），此后 `screen.Get` 拿到的静态卡引用已销毁。
3. `<ScrollList>` 属性表：
   - `itemTemplate`：`必填` → `BindItems 前必填；只写静态子节点时可省`
   - 新增 `columns`（int，`0` = 不用网格 / 见 SGS-D4 的 variant 用法）
   - 新增 `cellSize`（`WxH`，网格模式必填）
   - `spacing` 一行补 `single 或 V,H`
   - 新增 `scrollbarWidth`（float，默认 20；640×360 参考画布下 20 会吃掉大半列）
   - 新增 `scrollbarOverlay`（bool，默认 false）
   - 补一句：不写 size 时网格模式的原生宽 = `columns × cellSize` + spacing + padding（**不含**滚动条；默认非 overlay 的滚动条仍会从视口里吃掉 `scrollbarWidth − 3`）
4. 子节点排版约束一段：网格模式下子节点不得写 `size`/`width`/`height`（`PUI-GRID-CHILD-SIZE`）；单列/单行模式按 layout-group 子节点规则（`width="stretch"` 合法，`anchor`/`margin` 报 `PUI-LAYOUT-ANCHOR`/`PUI-LAYOUT-MARGIN`，`flow="false"` 允许）。
5. uGUI 对照表（约 97 行）`<ScrollList>` 行：`ScrollRect+Mask（BindItems）` → `ScrollRect + Mask + Vertical/Horizontal/GridLayoutGroup（静态子节点 + BindItems）`。
6. 错误表加 `PUI-SCROLL-COLUMNS-DIRECTION` / `PUI-SCROLL-COLUMNS-CELLSIZE` 两行。
7. `.lint/UIXmlLint/README.md`：两个新码各一行；`PUI-GRID-CHILD-SIZE` 那行补「`<Grid>` 与网格模式的 `<ScrollList>` 都算」。

星球面板级别的完整例子（放 `<ScrollList>` 小节）：
```xml
<ScrollList id="slots" anchor="stretch" margin="16,0,0,2"
  columns="4" cellSize="66x100" cellSize.portrait="67x100" spacing="4"
  sprite="none" color="#0000" scrollbar="" scrollbarWidth="6" scrollbarOverlay="true"
  itemTemplate="BuildSlot">
  <BuildSlot id="slot1" index="1" icon="Building:MetalExtractor" name="Refinery" level="Lv.5"/>
  <BuildSlot id="slot2" .../>
</ScrollList>
```

- [ ] **Step 1**: 按上面 7 条改文档（**英文**，除既有中文段落的行内补充随原文语言）。
- [ ] **Step 2**: 例子过 lint：把上面的 XML 存成临时 `.ui.xml`（配一个 `<Template name="BuildSlot">`），跑 `dotnet run --project .lint/UIXmlLint -- <file>` → exit 0。
- [ ] **Step 3**: 提交 `docs(skill): ScrollList grid mode, static children, scrollbar sizing; fix <Grid spacing> order`。

## Task 11: 全量回归 + PlayMode smoke + PR

**Files:**
- Modify: `Tests/PlayMode/Controls/CommonControlsPlayTests.cs`
- Modify: `docs~/superpowers/specs/2026-09-10-scrolllist-grid-and-static-children-requirements.md`

- [ ] **Step 1**: PlayMode smoke —— 网格 ScrollList（`columns='4' cellSize='40x40'`）带 6 张静态卡，过一帧后：Content 高 == 2 行；`BindItems` 12 项后 == 3 行；不报 error。RUNPLAY(CommonControlsPlayTests)。
- [ ] **Step 2**: 全量 —— `refresh_unity(mode="force", scope="all")` → `read_console(types=["error"])` 空 → `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` 全绿 → `PromptUGUI.Tests.EditorOnly` 全绿 → `run_tests(mode="PlayMode", ...)` 全绿。
- [ ] **Step 3**: `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx` 干净；`dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` exit 0。
- [ ] **Step 4**: 需求文档末尾加「## 5. 实施记录」：落地的属性名、两个新 lint 码、SGS-D1..D5 的结论、以及下面「已知限制」两条。
- [ ] **Step 5**: 开 PR（base `main`），描述里写清 §R1–R4 的对应关系与 SGS-D1..D5；PR body 末尾加会话链接。**不合并**，等用户 review。

---

## 已知限制（写进实施记录，不在本 PR 解决）

1. **`<Add into="#scrolllist">` 仍落在 ScrollList 根上，不进 Content。** `ScreenInstantiator.ResolveAddTarget` 返回的是 `control.GameObject.transform` 而非 `ChildHostTransform` —— 这是 `<Carousel>` / `<Collapsible>` / `<TabMenu>` / `<Markdown>` / `<Btn>` / `<Toggle>` / `<Tab>` 共有的既有行为，改它是一次跨控件的统一变更，不搭在本需求里。需要 variant 增删卡片的场景先用 `BindItems`。
2. **grid ⇄ 非 grid 的 variant 翻转，子节点几何要等下一轮 ReSolve 才完全归位。** `Screen.ReSolve` 遍历的是 `_nodeMap`（`Dictionary`，顺序不定），所以父 ScrollList 换组与子节点 `ApplyCommon` 的先后不确定。首次 Open 由 Task 6 的 `PreConfigureContent` 保证正确；ReSolve 只有在**布局组类型真的变了**（`columns` 在 0 与 ≥1 之间翻）时才受影响，`cellSize.variant` / `columns` 在两个网格值之间切换不受影响（组件类型不变）。彻底解法是让 ReSolve 按 `Def` 的 DFS 前序遍历（父先于子），属于 `Screen` 的独立改动。
3. **`<Grid columns=... >` 缺 `cellSize` 没有对应 lint 码。** 本 PR 只给 `<ScrollList>` 加了 `PUI-SCROLL-COLUMNS-CELLSIZE`；`<Grid>` 同样的缺陷仍然静默落到 100×100。可作为后续小改。
