# 浮层内核 + 统一 Esc 链 + `<InputField>` 候选列表（`<Suggestions>`）

> 状态：**已实施**（2026-10-18 按评估结论的推荐方向起草，§13 按推荐定稿；2026-10-19 按实施计划 `plans/2026-10-18-popup-core-and-input-suggestions.md` 的计划期修正与审阅结论改齐；阶段 0 与阶段 1 均已实现，实施记录见 §16 / §17）。
> 来源：一份「给框架加通用浮层、让输入框支持候选列表（照 AutoSuggestBox）」的提议，以及读源码做的可行性评估。§1.2 的问题都是读代码得出的，没有在 Unity 里复现；复现由 §9 的 Red 测试给出。
> 相关：
> `2026-08-29-tabmenu-design.md`（TM-D8 弹层留在宿主子树 + overrideSorting；TM-D9 全局单开；TM-D12 收起态测量；TM-D13 过渡；TM-D15 焦点圈闭；TM-D16 Esc 消费；TM-D17 抽 `TabGroupCore` 的先例；TM-D19 默认皮肤；TM-D21 定位。阶段 0 把这些抽成内核）、
> `2026-10-01-router-page-layering-design.md`（§3 层带总表、§4.3 步长下限 `PopupSortingOffset + 2`）、
> `2026-05-20-modal-layering-design.md`、`2026-06-12-tutorial-system-design.md`（Esc 落点都先判 `IsBlockingInput`）、
> `2026-09-16-close-transition-design.md`（`SealInput` 收起展开中的菜单）、
> `2026-06-29-inputfield-nav-edit-mode-design.md`（两级编辑门 `InputFieldNavGate`）、
> `2026-09-12-scrollbar-part-element-design.md`（部件元素 + 宿主认领 `IScrollbarHost`）、
> `2026-09-14-scrolllist-row-reuse-design.md` / `2026-09-29-scrolllist-virtualization-design.md` §5.5（`ItemBinding`：位置复用 / 带 key 复用）。

## 1. 问题

### 1.1 要做的事

游戏里的搜索框（找玩家、找物品、找频道）需要「边打字边出候选」。输入框下方浮出一列候选，↑↓ 选，Enter 或点一下提交。提交时要知道用户是选了候选，还是直接提交了输入的文字。今天做不到：

- `<InputField>` 只有 `OnValueChanged` / `OnEndEdit` / `OnSubmit` 三个事件，没有候选列表。`OnValueChanged` 对代码赋值也会触发，分不出「用户在打字」。
- 浮层零件只存在于 `<TabMenu>` 的私有实现里，别处用不了：全屏点击拦截、层序、翻转定位、Esc、全局单开、手柄焦点圈闭。
- `<Dropdown>` 是 `TMP_Dropdown`，自带一套 blocker，选项是裸 Toggle，拿不来做候选列表。

### 1.2 现有浮层机制的漏洞

抽内核之前先列清现状的问题，内核要一并解决：

1. **路由 Modal 不问菜单。** `UI.Modal.OnEscapePressed` 首行调 `TabMenu.TryConsumeEscape()`（TM-D16）。路由 Modal 的 Esc 回调（`UI.Router.Layering.cs` 的 `ApplyRouteLayer`）却只判教程与 `IsTop`。所以路由 Modal 里开着 `<TabMenu>` 时，一次 Esc 会同时收起菜单并 `Back()`。`reference/navigation.md` 写的「a modal behind it survives that press」对路由 Modal 不成立。
2. **菜单自己的 Esc 不判教程。** 教程 spec 要求所有 Esc 落点先判 `Tutorial.IsBlockingInput`。TabMenu 后来加了第三个落点（它自己的监听），没有判；`UI.Modal` 的落点也是先收菜单、再判教程。结果是 Block 步骤里按 Esc，会收起引导正指着的菜单。
3. **被盖住的菜单仍占着 Esc。** 菜单展开时如果代码弹出 Modal、或 Router 推入子页，菜单不会收起：只有 Screen 开始关闭时 `SealInput` 才收，`Reconcile` 只关 ad-hoc 模态。于是之后的第一次 Esc 被这个看不见的菜单吃掉，模态不关。如果菜单在下层模态里，弹层（下层 +2）还会画在新模态（+1）之上，遮罩与新模态同层序，先后未定义。
4. **Dropdown 不在链上**。`TMP_Dropdown` 的列表固定排在 sortingOrder 30000（遮罩 29999），Esc 走 EventSystem 的 Cancel：`Show()` 会选中当前项（`TMP_Dropdown.cs:903`），选中的那一项的 `DropdownItem.OnCancel` 调 `Hide()`（`:45-50`）。模态里展开着 Dropdown 时按 Esc，Dropdown 会收起，模态的监听在同一下也会关掉模态。
5. **全局单开不适合嵌套。** `s_expanded` 是单例，开一个就收另一个（TM-D9）。今天没有嵌套所以没事。一旦浮层里能再开浮层（阶段 2 的子菜单、浮层里带候选的输入框），开内层就会把外层关掉。

## 2. 否决的方案

**A. 先做公开的 `<Popover>`，候选列表在它上面搭。否决（作为阶段 1 的路线）。** 候选列表要求输入框一直持有焦点（§6.5）。uGUI 在按下的那一刻，会从被按的物体向上找第一个 `ISelectHandler`；只要它不是当前选中项，就清掉选中。TMP 一失去选中就结束编辑，手机上还会收起键盘。候选行只有满足两个条件，点它才不会抢走焦点：挂在输入框自己的 GameObject 之下，且自身不是 Selectable。一个在别处声明、按 id 锚到输入框的浮层做不到这一点。所以候选列表是输入框的内部弹层，和 `<TabMenu>` 共用内核；公开的 `<Popover>` 留给阶段 2。

**B. 把候选列表做成 `<InputField>` 上的一组 `suggest*` / `popup*` 属性。否决。** TM-D3 否决 Dropdown 式 `popup*` 前缀的理由在这里同样成立：面板拿不到 `radius` / `glass` / `<Decor>`，除非再堆一串前缀属性。部件子元素（`<Scrollbar>`、`<Header>` 的先例）让面板就是一个普通的程序化表面。

**C. 候选行用 `<Btn>`（或任意 Selectable）。否决。** 理由同 A：按下 Selectable 就结束编辑，结束编辑会在按下时关掉列表，抬起时 click 已经找不到行（面板已失活）。

**D. 不接管按键，事后补救。否决。** 设想是让 TMP 照常处理 ↑↓ / Enter / Esc，再事后补救：↑↓ 之后把光标挪回去；Esc 之前关掉 `restoreOriginalTextOnEscape`，之后再 `ActivateInputField()`。但重新激活会全选文字（`onFocusSelectAll`），会重发 `onSelect` / `onEndEdit`，手机上键盘还会闪一下。

**E. 用一个中心 Esc 监听取代所有监听组件。否决。** 真正的问题是「谁来决定」散在四处（§1.2-1/2/3），不是监听者多。保留现有的 `ModalEscapeListener` 作为按键来源，只把决定收进一个函数（§4.4）。这样组件、`enabled` 规则，以及 10 个测试文件对 `ModalEscapeListener` 的用法（取组件、`FireForTests()`、断言有没有挂）都不用动。

**F. 照提议里的三种关闭策略做（外部按下吃掉 / 外部按下穿透 / 焦点离开组件范围）。推迟到阶段 2，并改写。** uGUI 的 selection 不等于焦点：点任何非 Selectable，包括浮层自己的空白处，都会把选中清成 null（`InputSystemUIInputModule` 的 `deselectOnBackgroundClick` 默认开）。所以「焦点离开即关」照字面实现，会在点浮层背景时把浮层关掉；它也和手柄焦点圈闭互斥。阶段 0 只做两种：遮罩吃掉（TabMenu），以及交给宿主决定（候选列表靠输入框结束编辑来关，§6.10）。

**G. 选中候选时自动把候选文字写进输入框（AutoSuggestBox 的 `TextMemberPath` / `UpdateTextOnSelect`）。推迟。** 做法只有两种。一是给 `BindItems` 加一个 `Func<T,string>` 参数，但在 `TKey = string` 时，它会和带 key 的重载 `Func<T,TKey>` 静默撞重载。二是约定从行里读字。v1 由 C# 在提交事件里自己写（§5.6）。

## 3. 方案总览

分三个阶段。本文实施阶段 0 与阶段 1，阶段 2 只记约束（§12）。

| 阶段 | 内容 | 对外面 |
|---|---|---|
| 0 | 从 `<TabMenu>` 抽出内部浮层内核 `PopupCore` 与浮层链 `PopupChain`；Esc 的决定收进 `EscapeChain`；更高的层出现时收起下层浮层；修掉 §1.2 的 1–4 | 无新 API；TabMenu 行为不变（修掉的漏洞除外） |
| 1 | `<InputField>` 的部件子元素 `<Suggestions>`；`PuiInputField` 在列表打开期间接管按键；`InputField.OnInput`；`submitIcon` | 新标签、新属性、新事件 |
| 2 | 公开的 `<Popover>`：运行期锚点、指针位置、穿透探测、侧放、嵌套 | 另起 spec |

```xml
<!-- 搜索框：打字出候选，↑↓ 选；Enter / 点行 / 放大镜提交 -->
<InputField id="search" placeholder="搜索玩家" width="320" submitIcon="ui/search">
  <Suggestions itemTemplate="PlayerRow" maxHeight="320" padding="4" radius="8"/>
</InputField>

<Template name="PlayerRow">
  <Frame height="44" color="#0000">
    <Show on="state-hover"><Frame anchor="stretch" color="white/0.06"/></Show>
    <Show on="state-selected"><Frame anchor="stretch" color="accent/0.25"/></Show>  <!-- 键盘高亮 -->
    <Text id="name" anchor="stretch" margin="0,12" fontSize="20"/>
  </Frame>
</Template>
```

```csharp
var field = screen.Get<InputField>("search");
var hits = new ReactiveProperty<IReadOnlyList<Player>>(Array.Empty<Player>());

field.OnInput.Subscribe(q => hits.Value = Filter(allPlayers, q)).AddTo(screen);   // 只在用户改字时
field.Suggestions.BindItems(hits, (IControl row, Player p) => row.Get<Text>("name").TextValue = p.Name,
                            key: p => p.Id).AddTo(screen);
field.Suggestions.OnSubmit.Subscribe(s =>
{
    if (s.IsChosen) { var p = hits.Value[s.ChosenIndex]; field.TextValue = p.Name; OpenProfile(p); }
    else Search(s.Text);
}).AddTo(screen);
```

## 4. 阶段 0：浮层内核

### 4.1 切分

| 归内核 `PopupCore`（`Controls/Internal`） | 留在宿主 |
|---|---|
| 面板上的 `Canvas`（打开时 `overrideSorting`）、`GraphicRaycaster`、`CanvasGroup`；开 / 关状态 | 面板里装什么，尺寸怎么算（通过尺寸回调交给内核），什么时候开、什么时候关 |
| 过渡：淡入 + 沿展开方向 8px 位移，时长 `transition`；非 Play 模式直接落终态；关闭动画放完才停用面板；重新打开会取消未完成的关闭（TM-D13 照搬） | TabMenu：caption、箭头翻转（跟内核的开关回调走同一时长）、`TabGroupCore`、「选中即收起」与 `_expanding` 守卫 |
| 加入 / 退出浮层链；层序；遮罩（`Blocker` 模式） | 候选列表：武装、高亮、提交（§6） |
| 手柄焦点圈闭（可选）：`ContainmentRoot` 切到面板、`ConfineNavigationTo`、落点回调；关闭时按 §4.3 还原 | |
| 面板上的 `ModalEscapeListener`：打开时启用；`AlsoCancelButton` 可选 | |
| 定位：调 `PopupPlacer`（§4.6），写 anchor / pivot / 位置 | |
| 收起态测量：`Screen.DeferDuringOpen` 停用面板；重建行时同帧临时激活（TM-D12 的两个钩子） | |
| `Dispose`：退链；销毁遮罩（遮罩挂在根 Canvas 下，宿主子树销毁时带不走它）；按 §4.3 还原 | |

```csharp
internal enum PopupDismiss { Blocker, Owner }   // 外部按下：遮罩吃掉 / 交给宿主（PC-D7）
internal enum PopupCloseReason { Owner, Escape, OutsidePress, Covered, HostGone }

internal sealed class PopupCore : IDisposable
{
    public PopupCore(Control host, RectTransform panel, PopupDismiss dismiss,
                     bool cageFocus, bool cancelButtonCloses);
    public bool IsOpen { get; }
    public float TransitionSeconds { get; set; }
    public Func<PopupLayout> Measure { get; set; }     // 宿主给尺寸与定位参数（gap / preferAbove / bounds）
    public Func<GameObject> FocusPick { get; set; }    // 圈闭时焦点落点（TabMenu：选中的 Tab）
    public event Action Opened;
    public event Action<PopupCloseReason> Closed;
    public void Open();
    public void Close(PopupCloseReason reason = PopupCloseReason.Owner);
    public void Reposition();                          // ReSolve / 推送之后重量重放
    public void OnHostAfterApply();                    // 宿主 apply 之后：被隐藏 / 不可交互就收起，否则重放；关着时经 DeferDuringOpen 停用面板
    public IDisposable BeginRebuild();                 // 收起态下同帧临时激活面板
    public void Dispose();
}
```

### 4.2 浮层链 `PopupChain`

静态的开着的浮层栈（自底向上），取代 `TabMenu.s_expanded`。

- **祖先**：B 的宿主 GameObject 在 A 的面板子树里，A 就是 B 的祖先。
- **打开 B**：从栈顶往下，先关掉所有不是 B 祖先的项，再把 B 压栈。今天「开一个收另一个」的行为是它的特例（没有嵌套时链深恒为 0）。
- **关闭 A**：先自顶向下关掉 A 之上的项，再关 A。Esc 只关栈顶，遮罩只关它自己的那一项。
- **层序**：链深为 k 的面板排在 `宿主根 Canvas.sortingOrder + SortingOffset + 2k`（`SortingOffset = 2`），遮罩排在面板 − 1。
  - 链深 ≥ 1 只会在阶段 2 出现。RPL §4.3 的步长下限（`SortingOffset + 2 = 4`）覆盖的是链深 0；每多一层要多 2，阶段 2 要重审这个下限。
- `DismissBelow(int order)`：自顶向下关掉宿主根 Canvas 序 `< order` 的项（原因 `Covered`）。
- `DismissUnder(Transform root)`：关掉宿主在 `root` 之下的项（原因 `HostGone`），取代 `TabMenu.CollapseIfUnder`。
- `Top` / `IsEmpty` / `ResetForTests`。

### 4.3 关闭原因与焦点还原

| 原因 | 来源 |
|---|---|
| `Owner` | 宿主调用：TabMenu 选中一行、`Collapse()`、宿主不可交互或被变体隐藏；链上新开了不相干的浮层；候选列表的提交 / 结束编辑 / 空推送 |
| `Escape` | Esc、手柄 Start / B（§4.4） |
| `OutsidePress` | 遮罩被点 |
| `Covered` | 更高的层出现（§4.5） |
| `HostGone` | 宿主失活、所在 Screen 开始关闭、`Dispose` |

不可交互与被隐藏归 `Owner`，是为了保持 TabMenu 今天的行为：收起后焦点回到把手。

还原只碰仍属于自己的东西，与原因无关（导航未启用时什么都不做，同今天）：

1. `ContainmentRoot` **仍是本面板**时，才还原成打开时记下的那个根。今天是无条件还原，可能会盖掉新出现的模态刚设好的圈闭。全局的 `ContainmentRoot` 与所在 Screen 的 `NavConfineRoot` 在打开时分别记下、关闭时各自还原（今天用全局值去还原 Screen 的圈闭，`TabMenu.cs:661, 677`）。
2. EventSystem 的选中项**在本面板内**时，才把它挪回宿主（`HostGone` 时清成 null）。选中项已在别处（新出现的层已经落了焦点），就不动。

TabMenu 的既有用例全部落在这两条里：展开时焦点被圈在面板内，所以收起时焦点回到把手，模态的圈闭也被还原。

### 4.4 Esc 链 `EscapeChain`

一个决定点 `EscapeChain.Dispatch(EscapeKey key, int pressId) → bool consumed`（`Runtime/Application/`）。`EscapeKey` 有两种：`Escape`（键盘 Esc 或手柄 Start），`CancelButton`（手柄 East，只有 `AlsoCancelButton` 的监听才发）。顺序：

| # | 条件 | 动作 | 修掉 |
|---|---|---|---|
| 1 | `Tutorial.IsBlockingInput` | 吞掉，什么都不做（浮层也不收） | §1.2-2 |
| 2 | 有展开中的 `<Dropdown>` 列表 | `Hide()`，消费 | §1.2-4 |
| 3 | 浮层链非空，且栈顶宿主的层不低于当前最上层模态 | 关栈顶（`Escape`），消费；记 `PopupConsumedFrame = Time.frameCount`；键盘 Esc 另记一笔「欠文本框一下 KeyDown」（§18） | §1.2-1 |
| 4 | `key == CancelButton` | 不消费（B 从来不关模态，维持现有契约） | |
| 5 | ad-hoc 模态栈非空 | 栈顶 `TryEscape`；被拒（如只有 OK）也算消费，同今天 | |
| 6 | 路由链栈顶是 Modal | `Back()`，消费；`Back()` 被路由守卫拒绝时会同步抛 `NavigationRejectedException`（`UI.Router.cs:30-35`），这里接住，仍算消费 | §1.2-1 |
| 7 | — | 不消费 | |

- **同一次按键只决定一次**：同一个 `pressId` 第二次进来，直接返回第一次的结果，什么也不做。真实监听传 `Time.frameCount`（同一次按键，所有监听都在同一帧听到）；`FireForTests()` 每次传一个新的负数 id，所以每次调用都算一次新按键。
- **监听组件**：`ModalEscapeListener` 去掉按实例的 `OnEscape` 回调，统一调 `Dispatch`（Input System 按触发的 control 区分 East 与其它；legacy 按 `KeyCode.Escape`）。各监听挂的位置不变：每个 ad-hoc 模态根（只有栈顶启用）、每个路由 Modal 根、每个打开的浮层面板。另有一处顺手修：InputActionMap 改为建一次、随 `enabled` 开关，不再每次 `OnEnable` 新建。候选列表开关频繁，每次新建会持续分配。
- **删除**：`TabMenu.TryConsumeEscape` / `s_escapeFrame` / `NotifyEscapeConsumedForTests` / `ForgetEscapeFrameForTests`。`UI.Modal.OnEscapePressed(slot)` 改成 `UI.Modal.TryHandleEscape()`，路由的 lambda 改成 `UI.Router.TryHandleEscape()`。
- **与文本框协调**：TMP 自己也处理 Esc（取消编辑、回滚文字），从两处听到：输入模块的 Cancel（与链同一次 Input System 更新，同帧），和 IMGUI 的 Escape KeyDown（在 EventSystem 的 Update 里读，可能晚一帧到）。`PopupConsumedFrame` 让 `PuiInputField.OnCancel` 认出「这一下已经被链用来关浮层了」；IMGUI 那一路按「欠账」处理，不看帧号（§6.6、§18）。
- **Dropdown**：`PuiDropdown` 覆盖 `CreateBlocker` / `DestroyBlocker`（TMP 里都是 `protected virtual`），据此维护一个「展开中」的静态集合，供第 2 步与 §4.5 使用。Dropdown 不进浮层链：它的 30000 层带不变（§12）。

### 4.5 更高的层出现时收起

| 时机 | 调用 | 说明 |
|---|---|---|
| ad-hoc 模态物化（`UI.Modal` pump，在捕获 `prevSelected` **之前**） | `DismissBelow(它将得到的 sortingOrder)` | 先收起，`prevSelected` 才是菜单把手，而不是一行已失活的 Tab |
| 路由 Page / Modal 写层（`ApplyRouteLayer`） | `DismissBelow(slot)` | 父页上的浮层被子页盖住；路由 Modal 盖住所有页 |
| Loading overlay 物化 | `DismissBelow(Loading.SortingOrder)` | 模态里的浮层（1000 以上）不受影响 |
| Toast / Tutorial | 不收 | Toast 不挡输入；Tutorial 的 Block 步骤可能正指着浮层里的东西 |

`DismissBelow` 同时收起宿主根 Canvas 序更低的展开中 Dropdown（§4.4 的集合）。只有这几个层管理器会触发收起。非路由的 `UI.Open` 不会：非路由 Screen 的层序本来就不归层管理器（RPL-D10），框架也不知道它挡不挡输入。

### 4.6 定位：`PopupPlacer` 扩展

```csharp
public static PopupPlacement Solve(Rect handle, Vector2 panel, Rect bounds, float gap, bool preferAbove = false);
// PopupPlacement 新增 Room：选定那一侧去掉 gap 后剩下的空间，供宿主夹高度
```

- 先放偏好的一侧；放不下且另一侧更宽裕才翻面（TM-D21 的规则对称化）。
- 横向不变：左对齐，越出右缘就往回拉，但不越过左缘。
- `bounds` 由调用方给：TabMenu 给根 Canvas 的 rect（不变）；候选列表给安全边界（§6.8）。
- 保持纯函数，`TabMenuPlacementTests` 原样跑。

### 4.7 TabMenu 改造

- **公开面不变**：`Expand` / `Collapse` / `Toggle` / `IsExpanded` / `OnExpanded` / `OnCollapsed`、`transition`、箭头、圈闭与焦点落点、遮罩吃掉点击、同时只开一个（现在由链实现）、不可交互或被隐藏时收起。
- **internal 名字迁移**（测试改引用）：

  | 旧 | 新 |
  |---|---|
  | `TabMenu.BlockerName` | `PopupCore.BlockerName`（`"__PopupBlocker"`） |
  | `TabMenu.PopupSortingOffset` | `PopupChain.SortingOffset`（Router 的 `MinPageSortingOrderStep` 和它的报错消息改读这里） |
  | `TabMenu.HasExpandedMenu` | `PopupChain.IsEmpty` |
  | `TabMenu.CollapseIfUnder` | `PopupChain.DismissUnder` |
  | `TabMenu.TryConsumeEscape` | `EscapeChain` |
  | `TabMenu.ResetForTestsInternal` | `PopupChain.ResetForTests` + `EscapeChain.ResetForTests` |

- **行为变化只有修掉的漏洞**：§1.2-1..4。

## 5. 阶段 1：作者面

### 5.1 XML 形态

```xml
<InputField id="search" placeholder="…" submitIcon="ui/search">
  <Suggestions itemTemplate="Row" maxHeight="320"/>
</InputField>
```

- `<Suggestions>` 只能是 `<InputField>` 的直接子元素，每个输入框至多一个。
- 它的子元素只能是 `<Decor>` 与 `<Scrollbar>`。

### 5.2 `<Suggestions>` 属性

| 属性 | 取值 | 默认 | 语义 |
|---|---|---|---|
| `itemTemplate` | Template 名 / 注册的标签 | —（必填） | 行模板，解析同 `<ScrollList itemTemplate>` |
| `maxHeight` | px（≥ 0） | `320` | 面板高度上限；内容更高时在面板里滚动。`0` = 只受可用空间限制（同 `<Collapsible maxHeight>`） |
| `gap` | px（≥ 0） | `4` | 输入框与面板的间距 |
| `placement` | `below` \| `above` | `below` | 优先放哪一侧；放不下且另一侧更宽裕时翻面 |
| `padding` | 同 `<TabMenu padding>` | `0` | 面板内边距 |
| `spacing` | px | `0` | 行间距 |
| `transition` | 时长（`0.1s` / `100ms` / 秒数） | `0.1s` | 开 / 关过渡；`0` 为即时 |
| `color` · `sprite` · `tint` | 同 `<TabMenu>` 的面板 | 默认皮肤（TM-D19） | 面板底 |
| 程序化属性（`radius` / `border*` / `glow*` / `glass` …）+ `<Decor>` | 同 `<Frame>` | — | 全部落在面板上（同 TM-D3） |

以下布局属性被忽略（面板由输入框定位）：`anchor` / `size` / `width` / `height` / `margin` / `pivot` / `flow` / `scale` / `hidden`（与 `PUI-SCROLLBAR-LAYOUT-ATTR` 同一张表）。`ParticipatesInLayout = false`，同 `<Scrollbar>` / `<Decor>`。面板永远与输入框同宽：`width` 是通用属性，由 `ApplyCommon` 写进 `sizeDelta`，控件分不清那是作者写的宽还是上次定位留下的宽；需要更宽的面板时再加 `popupWidth`（TabMenu 同名）。

面板就是控件自己的 GameObject，每次 ReSolve 的 `ApplyCommon` 都会改写几何、`hidden` 还会 `SetActive`：`OnAfterApply` 把 active 拨回「是否打开」，打开着就重新定位。

### 5.3 `<InputField>` 新属性

| 属性 | 取值 | 默认 | 语义 |
|---|---|---|---|
| `submitIcon` | sprite 键（同 `sprite=`） | 无 | 输入框右端的提交钮，与 Enter 同义（§6.4）。不写就不建 |
| `submitIconSize` | px | `24` | 图标边长。点击区是输入框高度的正方形，文字区右侧内边距相应加宽 |
| `submitIconColor` | 颜色 | 默认字形色 | 图标色。按下 / 悬停 / 不可交互的反馈用 uGUI `ColorBlock` 的默认倍率 |

`submitIcon` 不依赖 `<Suggestions>`，单独用也成立（聊天的「发送」钮）。

### 5.4 图层结构

```
InputField                    (TMP_InputField → PuiInputField)
├─ Text Area (RectMask2D) ─ Placeholder / Text
├─ SubmitIcon                 (submitIcon= 时建：Image + FocuslessPress；不是 Selectable)
└─ Suggestions                (= 面板：Image + 程序化表面 + PopupCore 的 Canvas / GraphicRaycaster / CanvasGroup
   │                             + ScrollRect + PointerSink)
   ├─ Viewport (RectMask2D)
   │  └─ Content (VerticalLayoutGroup + ContentSizeFitter)
   │     ├─ Row               (FocuslessPress + HitCatcher + VerticalLayoutGroup：每项一个外壳)
   │     │  └─ <itemTemplate 的实例>
   │     └─ …
   ├─ Scrollbar               (作者写的 <Scrollbar>，否则默认条；同 Dropdown)
   └─ <Decor> …
```

- **`PointerSink`**：在面板空白处按下时把事件接住，不让它冒泡到 TMP 去挪光标、开始选字（§6.5）。它同时实现 pointer-down 与 click：输入模块对 click 单独解析处理者，只接 down 的话 click 会落到 `TMP_InputField.OnPointerClick`。
- **滚动条只显示、不接指针**：uGUI 的 `Scrollbar` 是 Selectable，按到它会让输入框失去选中（§6.5）。`WireScrollbar` 把轨道与手柄的 `raycastTarget` 关掉，滚动靠拖内容与滚轮。
- **`FocuslessPress`**：一个不是 Selectable 的按压组件，由候选行与提交钮共用。它处理指针的进 / 出 / 按 / 抬 / 点，内部持有一个 `StateBroadcaster`，并对外实现 `IStateSource`。

### 5.5 行模板与状态

- 行模板实例化在每行的外壳 `Row` 之下。外壳先建好，它的 `FocuslessPress` 就是模板里 `<Show on="state-*">` 的状态源。
  - `<Show>` 在应用属性时用 `GetComponentInParent<IStateSource>` 向上找源（`Trigger.OnAfterApply`，后序应用，发生在工厂调用之内），所以源必须在实例化之前就在父链上。实例化之后再往模板根上补组件，是找不到的。
  - 外壳带 `VerticalLayoutGroup`。工厂按「父节点有 LayoutGroup」把模板根当布局组子节点处理（同 ScrollList 的行）：`height` 生效，`anchor` / `margin` 不合法。
- 状态只用现有的四个值，不新增语法：
  - `normal`；
  - `hover`：指针在行内（Pointer 模式）；
  - `pressed`；
  - `selected`：键盘 / 手柄高亮。
  - 合成规则同 Toggle（`StateBroadcaster`）：高亮相当于 `isOn`，hover / pressed 临时覆盖它。
- Btn 的 `*Color` / `*Modulate` / `pressOffset` 等属性，行上没有（行不是 Btn）。状态视觉一律用 `<Show>`。
- 行模板里出现 Selectable（`<Btn>` / `<Toggle>` / `<InputField>` …）：lint 警告，运行期首次建行时也警告一次。点它会结束编辑（§2-C）。
- v1 的行里不支持 `on="expand"` 一类的展开触发器。

### 5.6 C# API

```csharp
public sealed class Suggestions : ProceduralControl, IScrollbarHost
{
    // 推送：契约同 ScrollList（位置复用 / 带 key 复用，§6.9）
    public IDisposable BindItems<T>(Observable<IReadOnlyList<T>> source, Action<IControl, T> bind);
    public IDisposable BindItems<T, TSlot>(Observable<IReadOnlyList<T>> source, Action<TSlot, T> bind)
        where TSlot : class, IControl;
    public IDisposable BindItems<T, TKey>(Observable<IReadOnlyList<T>> source, Action<IControl, T> bind,
                                          Func<T, TKey> key);
    public IDisposable BindItems<T, TSlot, TKey>(Observable<IReadOnlyList<T>> source, Action<TSlot, T> bind,
                                                 Func<T, TKey> key) where TSlot : class, IControl;

    public int Count { get; }                    // 最近一次推送的项数
    public bool IsOpen { get; }
    public int HighlightedIndex { get; set; }    // -1 = 无；列表关着时写入被忽略
    public void Open();                          // 武装：编辑中且有项就显示（如「聚焦即出最近搜索」）
    public void Close();                         // 收起并解除武装

    public Observable<SuggestionSubmit> OnSubmit { get; }   // 每一次提交（§6.4）
    public Observable<int> OnHighlightChanged { get; }      // -1 = 回到「无高亮」
    public Observable<Unit> OnOpened { get; }
    public Observable<Unit> OnClosed { get; }
}

public readonly struct SuggestionSubmit
{
    public string Text { get; }        // 提交那一刻输入框里的文字
    public int ChosenIndex { get; }    // 选中项在最近一次推送里的下标；-1 = 提交的是输入的文字
    public bool IsChosen => ChosenIndex >= 0;
}

// InputField 新增
public Observable<string> OnInput { get; }   // 只在用户改字时发（§6.7）；OnValueChanged 不变
public Suggestions Suggestions { get; }      // 没有 <Suggestions> 子元素时为 null
```

- 选中候选**不**自动改写输入框文字（§2-G）。要改就在 `OnSubmit` 里写 `TextValue`：这是代码赋值，不发 `OnInput`，不会再触发查询。
- 异步查询由宿主保证「只推最新一次的结果」，例如 R3 的 `Switch`。不要把空列表当作「加载中」推过去：空推送会收起列表（§6.1），结果回来再弹开，会闪一下。

## 6. 阶段 1：语义细节

### 6.1 显示条件

**列表打开 ⟺ 编辑中 ∧ 已武装 ∧ `Count > 0` ∧ 输入框可交互、非 `readOnly`、在层级里激活。**

- **武装**：`OnInput`（用户改字），或者 `Open()`。
- **解除武装（并收起）**：Esc、任何一次提交、编辑结束、被盖住（§4.5）、`Close()`。
- **收起但不解除武装**：推送了空列表。下一次推送非空时就会弹开。
- 进入编辑本身不武装：聚焦输入框不会把上一轮的候选再弹出来。
- **打开期间的推送**：就地重建行，高亮重置为 −1（有变化时发 `OnHighlightChanged(-1)`），重量高度。放在哪一侧在打开时定好，推送不翻面，打字时面板不会上下跳。

### 6.2 键盘与手柄

列表打开、且不在输入法组字期间（§6.11）：

| 按键 | 动作 | TMP 是否看到 |
|---|---|---|
| ↓ | 高亮下移，循环：无 → 0 → … → n−1 → 无 | 否 |
| ↑ | 高亮上移，循环：无 → n−1 → … → 0 → 无 | 否 |
| Enter / 小键盘 Enter，有高亮 | 选中提交（§6.4） | 否 |
| Enter，无高亮 | 交给 TMP：普通提交并结束编辑（今天的行为） | 是 |
| Esc | 不处理：由 Esc 链关列表（§4.4）；链为关浮层用掉的那一下 Esc，它晚到的 KeyDown 与按住时的重复同样吞掉（§18） | 否 |
| 其余（字符、← →、Home / End、Backspace、Ctrl+A / C / V / X …） | 原样交给 TMP | 是 |

列表关着时，所有按键都交给 TMP，零行为变化。多行输入框同表（列表打开时 ↑↓ 归列表），另有 lint 警告。

手柄（`UI.Navigation` 启用、编辑中、列表打开）：

| 输入 | 到达处 | 动作 |
|---|---|---|
| 十字键 / 左摇杆 上下 | `PuiInputField.OnMove` | 同 ↑↓。今天编辑中的 `OnMove` 被 TMP 屏蔽，什么都不做 |
| South，有高亮 | `PuiInputField.OnSubmit` | 选中提交 |
| South，无高亮 | 同上 | TMP 默认（普通提交） |
| East | 浮层监听 → 链关列表；随后的 `OnCancel` | 列表关；本帧 TMP 不取消编辑 |

按键高亮时会把该行滚进可见区。按住 ↓ 时，没有 IMGUI KeyDown 的重复帧不会被接管循环 Use，输入模块的 Navigate 重复（键盘方向键也在其中）会到 `OnMove`；所以 `OnMove` 在处理过 IMGUI 方向键后的几帧内忽略 move，高亮一行一行走、不跳行。

### 6.3 指针

| 指针 | 动作 |
|---|---|
| 移入 / 移出行 | 行进入 / 离开 `hover`；不改变键盘高亮 |
| 在行上按下 / 抬起 | 行 `pressed`；在同一行上抬起 = 选中提交 |
| 在面板空白处按下 | `PointerSink` 吞掉 |
| 在面板里拖动、滚轮 | 面板滚动：ScrollRect 在 TMP 之前接到 |
| 点 `submitIcon` | 同 Enter：有高亮就是选中提交 |
| 点输入框以外的任何地方 | 输入框按 uGUI 规则失去选中 → 结束编辑 → 列表关 |

### 6.4 提交

| 入口 | 有高亮 | `InputField.OnSubmit` | `Suggestions.OnSubmit` | 之后 |
|---|---|---|---|---|
| Enter / 手柄 South | 否 | `text` | `(text, −1)` | 列表关，TMP 结束编辑 |
| Enter / 手柄 South | 是 | — | `(text, i)` | 列表关，结束编辑 |
| 点第 i 行 | — | — | `(text, i)` | 列表关，结束编辑 |
| `submitIcon` | 否 | `text` | `(text, −1)` | 列表关；若在编辑则结束编辑 |
| `submitIcon` | 是 | — | `(text, i)` | 同上 |
| 软键盘 Done / Go | （手机没有键盘高亮） | `text` | `(text, −1)` | TMP 结束编辑 |
| 两级导航下用 Submit 进入编辑 | — | — | — | 不是提交（`InputFieldNavGate` 的既有规则） |

- 两个事件都发时，先 `InputField.OnSubmit`，后 `Suggestions.OnSubmit`。
- `InputField.OnSubmit` 的含义收窄为「提交的是输入的文字」。不认识候选列表的订阅者，在用户点了候选时不会收到一个半截的输入。
- 有 `<Suggestions>` 时，只订 `Suggestions.OnSubmit` 就覆盖了全部提交。
- 没有 `<Suggestions>` 时，`submitIcon` 只发 `InputField.OnSubmit`。

### 6.5 焦点为什么留得住

按下时，`InputSystemUIInputModule` 对被按物体做 `GetEventHandler<ISelectHandler>`（向上找第一个 `ISelectHandler`）。结果不是当前选中项时就清掉选中；`deselectOnBackgroundClick` 打开时，找不到也清。TMP 的 `OnDeselect` 会 `DeactivateInputField`。由此有三条结构约束：

1. **面板挂在输入框 GameObject 之下**：从面板里任何物体向上找，第一个 `ISelectHandler` 都是输入框本身，也就是当前选中项，于是不清。
2. **行和提交钮都不是 Selectable，滚动条不接指针**：否则向上找会先停在它们身上（uGUI 的 `Scrollbar` 本身是 Selectable，所以它的轨道与手柄关掉 `raycastTarget`，§5.4）。
3. **行、面板空白处、提交钮都要自己接 `pointerDown`**：否则按下会冒泡到 `TMP_InputField.OnPointerDown` 去挪光标；而且 click 只发给接了 down 的物体。

拖动由面板上的 ScrollRect 先接到，不会落到 TMP 的「拖选文字」上。

**例外**：输入模块的 `deselectOnBackgroundClick` 被关掉时，点空白处不会让输入框失去选中，列表也就一直开着，和输入框「仍在编辑」一致，不另处理。

**按下进行中**：如果编辑在某一行被按住期间结束（比如 Android 上点游戏画面可能收起键盘），推迟到这次按压有结果再关：在同一行上抬起，就选中提交；在别处抬起，就关。

### 6.6 按键接管：`PuiInputField`

`InputField` 改挂 `PuiInputField : TMP_InputField`。不开候选列表时它不改变任何行为。

- **`OnUpdateSelected`**（TMP 里是 `public virtual`）：只在「列表打开 ∨ 链还欠着一下 Esc KeyDown ∨ 被吞掉的那下 Esc 还按着」（`TakesKeys`，§18）且不在组字窗口内时接管，其余直接 `base`。接管时：
  - 自己用 `Event.PopEvent` 取事件，每个 KeyDown 交给纯函数 `SuggestionKeys.Route(Event, 状态)` 决定动作（§6.2 的表）；
  - 不归列表的键交给 `KeyPressed`（`protected`）。返回 `Finish` 时照 `base` 的做法：未取消就 `SendOnSubmit()`，然后 `DeactivateInputField()`；
  - `ValidateCommand` / `ExecuteCommand` 的 `SelectAll` 交给 `SelectAll()`；
  - 收尾 `UpdateLabel()`，刷新文字网格；**只在确实消费了某个 KeyDown、或输入法正在组字时** `eventData.Use()`（照 TMP 自己的做法）。`InputSystemUIInputModule.ProcessNavigation` 一旦看到 update-selected 被 Use，整帧都不发 move / submit / cancel，无条件 Use 会让列表打开期间手柄完全失灵。
  - 用到的全是 TMP 的 `public` / `protected` 成员。`m_ProcessingEvent`、`m_WasCanceled`、组字状态、`MoveUp` / `MoveDown` 都是 `private`，所以不能只拦几个键再调回 `base`：没有「放回事件」的 API。
- **`OnMove` / `OnSubmit` / `OnCancel`**（手柄）：按 §6.2 覆盖，列表关着时一律 `base`。
- **`LateUpdate`**（TMP 里是 `protected virtual`）：检测 `isFocused` 的边沿，发内部的 `EditingChanged`。这一条覆盖了所有结束编辑的路径：失去选中、Enter、Esc、软键盘关闭、`InPlaceEditingChanged`、两级导航门。`OnDisable` 时补发 `false`。
- **可测性**：路由是纯函数，EditMode 直接喂 `Event.KeyboardEvent(...)`。编辑态经 `EditingChanged` 进入 `Suggestions`，EditMode 有测试钩子直接驱动它；真实的 TMP 激活与手柄事件走 PlayMode。IMGUI 事件队列没有注入口，所以 `PopEvent` 循环本身靠 PlayMode 冒烟加手测。

### 6.7 `OnInput`

在 TMP 的 `onValueChanged` 上发，条件是 `isFocused`，且不在代码写入（`TextValue` setter、XML 重放）之内。

- **会发**：打字、粘贴、删除、输入法上屏、软键盘同步。
- **不会发**：代码赋值。TMP 的 Esc 回滚发生在 `DeactivateInputField` 把 `m_AllowInput` 置假之后，因此也不发。
- 组字期间 `text` 不变，所以中文输入法下，候选在上屏后才更新。

### 6.8 定位

- **宽**：横向 stretch 到输入框（anchor x 0..1），随输入框伸缩，不必重放（§5.2：v1 没有宽度属性）。
- **高**：`min(内容首选高, maxHeight, Room)`；`maxHeight = 0` 时只取 `min(内容首选高, Room)`。超出部分在面板里滚动；默认条自动隐藏（同 Dropdown）。
- **侧**：打开时由 `PopupPlacer` 决定（`placement` 是偏好），之后的推送不翻面（§6.1）。
- **边界**：根 Canvas 的 rect 再收进安全区（换算同 `SafeAreaTracker`）。`TouchScreenKeyboard.visible` 时，从底边再扣掉键盘高度。只用 `area.height`：各平台 `area` 的坐标原点不同，而竖屏键盘总在底部。
- **重放时机**：打开、每次推送、`Suggestions.OnAfterApply`（ReSolve）。面板是输入框的子节点，位置跟着输入框走。
- **裁切**：Viewport 用 RectMask2D 做矩形裁切。圆角面板请用 `padding` 让行避开圆角（同 TM-D20 的做法）。

### 6.9 数据推送与行

- `BindItems` 的复用契约同 ScrollList（`ItemBinding` 直接复用）：
  - 无 key 时按位置复用；有 key 时按 key 复用；
  - 复用的行在再次 bind 之前 `ReleaseSubscriptions`；
  - `itemTemplate` 换名时整表重建。
- 不做虚拟化：候选列表本来就该短，宿主应当限制条数。
- 列表收起时推送，照样建行：临时激活面板测量，同帧停用（TM-D12）。
- `Count` / `HighlightedIndex` / `ChosenIndex` 都指最近一次推送。

### 6.10 关闭来源汇总

| 来源 | 内核原因 | 解除武装 | 附带 |
|---|---|---|---|
| 编辑结束（`EditingChanged(false)`） | `Owner` | 是 | 按下进行中时推迟（§6.5） |
| Esc / 手柄 B | `Escape` | 是 | — |
| 任一提交 | `Owner` | 是 | — |
| 推送空列表 | `Owner` | 否 | — |
| `Close()` | `Owner` | 是 | — |
| 被盖住（§4.5） | `Covered` | 是 | 输入框 `DeactivateInputField()`：不能让用户继续往被盖住的框里打字，再把列表顶出来 |
| 输入框不可交互 / 被隐藏 / 失活 / Screen 开始关闭 / Dispose | `HostGone` | 是 | — |

候选列表不圈焦点，也没有遮罩（`PopupDismiss.Owner`）。所有关闭都来自上表。

### 6.11 输入法与手机

- **输入法**：组字期间（`compositionString` 非空，取法同 TMP：当前输入模块的 `BaseInput`，没有就退回 `Input.compositionString`），以及组字结束的那一帧，不接管任何键。那时 ↑↓ / Enter 属于输入法候选窗。TMP 在 macOS 上为组字结束做的事件抑制也就原样生效。
- **软键盘**：TMP 在移动端默认会显示原生输入条（`shouldHideMobileInput` 默认 false，Runtime 从没设过）。v1 不改这一点，也不处理「输入框本身被键盘挡住」，见 §12。

## 7. 报错

新规则在 `Core/Lint/SuggestionsRules.cs`，其余是既有规则的扩展。阶段 0 没有新规则。

| Code | 触发 | 运行期 |
|---|---|---|
| `PUI-SUGGEST-OUTSIDE` | `<Suggestions>` 的父节点是内置标签但不是 `<InputField>`。父节点是模板调用时生遍跳过，由展开遍看真父，同 `ScrollbarRules.CheckOutside` | 警告；面板停用，永不打开 |
| `PUI-SUGGEST-DUPLICATE` | 同一 `<InputField>` 下出现第二个 `<Suggestions>` | 警告；后者停用（同 Scrollbar） |
| `PUI-SUGGEST-TEMPLATE` | 缺 `itemTemplate`，或值为空 | `BindItems` 抛 `InvalidOperationException`（同 ScrollList） |
| `PUI-SUGGEST-LAYOUT-ATTR` | 写了 §5.2 列出的布局属性（含 `width`） | 忽略 |
| `PUI-SUGGEST-CHILD` | 有 `<Decor>` / `<Scrollbar>` 以外的子元素 | 警告 |
| `PUI-SUGGEST-ENUM` | `placement` 不是 `below` / `above` | 抛 `ArgumentException`（同 `contentType`） |
| `PUI-SUGGEST-VALUE` | `maxHeight` < 0 或不是数，`gap` < 0 | 警告，用默认值 |
| `PUI-SUGGEST-MULTILINE` | 宿主的 `lineType` 不是 `single` | 警告 |
| `PUI-SUGGEST-ROW-SELECTABLE` | 展开遍：沿 `itemTemplate` 找到模板体，体内有 `NavTargetRules` 的可聚焦标签。接法同 `FontFloorRules`：`DocumentLinter` 在展开遍之后调 `SuggestionsRules.CheckRows(expanded)`，读 `screen.Templates`（`IRWalker` 只拿得到原始 `doc.Templates`，不知道 `itemTemplate` 的调用点）；只在 CLI 报 | 控件首次建行时自己警告一次 |
| `PUI-SCROLLBAR-OUTSIDE` | （扩展）`ScrollbarRules.HostTags` 加 `Suggestions` | — |
| `PUI-PROC-*` | （扩展）`ProceduralSurfaceRules.SurfaceTags` 加 `Suggestions`（`ProceduralAttrNamesTests` 双向钉住） | — |
| `PUI-PADDING-INT` | （扩展）`PaddingRules` 加 `Suggestions` | — |
| `PUI-RAYCAST-TAG` | （扩展）`RaycastRules.InteractiveTags` 加 `Suggestions`：面板永远接住指针 | — |
| `PUI-FONT-FLOOR` | （扩展）`FontFloorRules.RowHosts` 加 `Suggestions`：行模板里的字也按下限查 | 同既有 |

还有几处要一起改：`BuiltinTags` 加 `Suggestions`（`BuiltinTagsTests` 会自动钉住）；`IRWalker` 分派 `SuggestionsRules`（逐节点的检查、子循环里的 Outside、宿主上的 Duplicate）；`ScreenInstantiator` 做运行期镜像。行模板里的 `<Show on="state-*">` 不用改 `StateTriggerRules`：模板体本来就豁免 `PUI-STATE-NO-SOURCE`。XSD 是反射生成的，注册即出现，不手改（同 `<Scrollbar>`：语法上哪里都能写，位置由 lint 管）。`submitIcon*` 不需要新规则。

## 8. 实现地图

| 处 | 改动 |
|---|---|
| `Controls/Internal/PopupCore.cs`（新） | §4.1 的全部：面板组件、开关、过渡、遮罩、圈闭与 §4.3 的还原、监听、定位调用、`BeginRebuild`、`Dispose` |
| `Controls/Internal/PopupChain.cs`（新） | §4.2：栈、祖先、层序、`DismissBelow` / `DismissUnder`、`SortingOffset`、`ResetForTests` |
| `Controls/Internal/PopupPlacer.cs` | §4.6：`bounds`、`preferAbove`、`Room` |
| `Application/EscapeChain.cs`（新） | §4.4：`Dispatch`、按键去重、`PopupConsumedFrame`、测试按键 id、`ResetForTests` |
| `Application/Modals/ModalEscapeListener.cs` | 统一调 `Dispatch`；区分 East；InputActionMap 只建一次 |
| `Application/UI.Modal.cs` | `TryHandleEscape()`；物化时先 `DismissBelow` 再捕获 `prevSelected` |
| `Application/UI.Router.Layering.cs` | 路由 Modal 的监听改调 `Dispatch`；`TryHandleEscape()`；`ApplyRouteLayer` 里 `DismissBelow(slot)`；步长下限读 `PopupChain.SortingOffset` |
| `Application/Modals/LoadingOverlay.cs` | 物化时 `DismissBelow(Loading.SortingOrder)` |
| `Application/Screen.cs` | `SealInput` 改调 `PopupChain.DismissUnder` |
| `Application/UI.cs` | `ResetForTests` 改调 `PopupChain` / `EscapeChain` 的复位 |
| `Controls/Internal/PuiDropdown.cs` | 覆盖 `CreateBlocker` / `DestroyBlocker`，维护展开集合 |
| `Controls/TabMenu.cs` | 委托给 `PopupCore`（§4.7）；保留 caption / 箭头 / `TabGroupCore` |
| `Controls/Suggestions.cs`（新） | §5.2 的属性、§5.4 的结构、§5.6 的 API、§6 的状态机；`IScrollbarHost`（照 Dropdown 的 `WireScrollbar`） |
| `Controls/SuggestionSubmit.cs`（新） | 结构体 |
| `Controls/Internal/FocuslessPress.cs`（新） | 指针处理 + `StateBroadcaster` + `IStateSource`；行与提交钮共用 |
| `Controls/Internal/PointerSink.cs`（新） | 吞掉面板空白处的 down / click |
| `Controls/Internal/SuggestionKeys.cs`（新） | 纯函数的按键路由 |
| `Controls/Internal/PuiInputField.cs`（新） | §6.6 |
| `Controls/InputField.cs` | 挂 `PuiInputField`；`OnInput`；认领 `<Suggestions>`；`submitIcon*`；文字区右侧内边距 |
| `Application/ScreenInstantiator.cs` | `<InputField>` 认领 `<Suggestions>`（同 Scrollbar 的认领分支）；运行期 lint 镜像 |
| `Application/BuiltinPrimitives.cs` | `reg.Register<Suggestions>("Suggestions", null)` |
| `Core/Lint/*` | §7 |
| `Editor/`（XSD） | 反射生成，不手改 |

## 9. 测试（Red 先行）

**阶段 0：先复现 §1.2，再重构**

- `EscapeChainTests`（新）：
  - 链的顺序表逐行一例；
  - 路由 Modal 里展开 TabMenu，一次 `Dispatch` 只收菜单、模态还在（§1.2-1）；
  - Block 引导期间菜单不收（§1.2-2）；
  - B 只关浮层，不关模态；
  - 同一 `pressId` 第二次进来什么都不做，`FireForTests()` 每次都是新按键；
  - 什么都没开时不消费。
- `PopupCoverTests`（新）：
  - 菜单展开时用代码弹出 ad-hoc 模态：菜单收起，且发生在 `prevSelected` 捕获之前；关掉模态后焦点回到菜单把手（§1.2-3）；
  - 路由推入子页时，父页的菜单收起；
  - Loading 出现时页上的菜单收起，模态里的不收；
  - Toast 和 Tutorial 不收；
  - 展开中的 Dropdown 在模态出现时收起，Esc 只收 Dropdown（§1.2-4，先确认复现）。
- `PopupChainTests`（新，用测试程序集里的最小宿主）：
  - 压栈会关掉非祖先；嵌套时保留祖先；
  - 关闭父项时自顶向下关掉子项；
  - 链深 k 的层序与遮罩序；
  - `DismissBelow` 按根 Canvas 序判断；`DismissUnder`；
  - `Dispose` 退链；`ResetForTests`；
  - §4.3 的两条还原规则（`ContainmentRoot` 已被别人改过时不动；选中项在面板外时不动）。
- `PopupPlacerTests`：
  - `preferAbove` 的对称翻面；
  - `Room` 的数值；
  - `bounds` 小于画布时的夹取；
  - `TabMenuPlacementTests` 原样通过。
- **回归**：
  - TabMenu：`TabMenuTests` / `TabMenuPlacementTests` / `TabMenuTrapTests`（Escape 一节的四个用例改写到 `EscapeChain` 上，场景不变）/ `TabMenuBindItemsTests` / `TabMenuTriggerTests` / `TabMenuPlayTests`；
  - 模态：`ModalStackTests`、`MessageBoxRequestTests`、`InputBoxTests`、`MarkdownBoxTests`、`CenteredSlideBoxTests`、`LoadingOverlayTests`、`ModalEscapePlayModeTests`；
  - 路由与教程：`RouterLayeringTests`、`RouterModalTabTests`、`TutorialRunTests`；
  - 以上全绿。

**阶段 1**

- `SuggestionKeysTests`（纯函数）：
  - `Event.KeyboardEvent("down" / "up" / "return" / "[enter]" / "escape" / "a" / "left" / "home")` × 状态（开 / 关、高亮有无、组字中 / 组字刚结束、链本帧已消费 Esc），按 §6.2 的表断言动作，以及「是否交给 TMP」。
- `SuggestionsStructureTests`：
  - 认领：`InputField.Suggestions` 非空，面板是输入框的子节点；
  - 面板组件齐全；默认滚动条；
  - 放在非 `<InputField>` 下时惰性并警告；重复的那个被停用。
- `SuggestionsFocusTests`：
  - 对行、面板空白处、提交钮，`ExecuteEvents.GetEventHandler<ISelectHandler>` 都返回输入框；
  - 行与提交钮上没有 `Selectable`；
  - 行模板里放 `<Btn>` 时警告一次。
- `SuggestionsVisibilityTests`（编辑态测试钩子）：
  - §6.1 的武装 / 解除表逐行；
  - 空推送收起但不解除武装；
  - `Open()` 要求在编辑中；
  - 被盖住时输入框被结束编辑；
  - 不可交互、`readOnly` 时不打开；
  - 打开期间的推送不翻面，高亮重置。
- `SuggestionsSubmitTests`：
  - §6.4 的表逐行，断言两个事件的有无、参数与顺序，列表是否关闭、编辑是否结束（钩子）；
  - 两级导航下用 Submit 进入编辑不算提交。
- `SuggestionRowStateTests`：
  - 行模板里的 `<Show on="state-hover|pressed|selected">` 能找到源并随状态显隐；
  - 高亮对应 `selected`，hover 在指针进入期间覆盖它。
- `SuggestionsBindItemsTests`：
  - 位置复用（跨推送是同一批 GameObject）；带 key 复用；
  - 复用前 `ReleaseSubscriptions`；
  - 换模板名时整表重建；
  - 收起态推送时测量正确（TM-D12 同款断言）。
- `SuggestionsPlacementTests`：
  - 下方、上方、翻面；`maxHeight` 与 `Room` 夹高；
  - 与输入框同宽；`maxHeight = 0` 只受可用空间限制；
  - 安全区边界（`SafeAreaTracker` 的测试注入）；
  - 键盘高度（新增测试注入）。
- `InputFieldOnInputTests`：
  - `TextValue` 赋值、XML 重放、Esc 回滚都不发；
  - 编辑中经 `ProcessEvent` 打字会发（PlayMode）。
- `InputFieldSuggestionsPlayTests`（PlayMode）：
  - 指针点击进入编辑，推送后面板打开；
  - 手柄的 `moveHandler` / `submitHandler` / `cancelHandler` 驱动高亮、选中提交与关闭，且 East 不回滚文字；
  - 输入框失去选中时列表关闭；按下进行中时推迟关闭。
- `SubmitIconTests`：
  - 写了 `submitIcon` 才建；文字区右侧内边距加宽；
  - 点击时按 §6.4 有无高亮两路；
  - 不可交互时不提交；
  - 不是 Selectable。
- **lint**：`SuggestionsRulesTests` 覆盖 §7 每行一正一反；各扩展规则的既有测试各加一例。

## 10. 演示同步（同 PR）

- `Samples~/CommonControls` 的「表单输入」页加一个搜索框：`<Suggestions>` 配一份固定名单，在 `OnInput` 里本地过滤；旁边一个 `<Text>` 显示最近一次提交（选中第几项 / 输入的文字）；`submitIcon` 用样例已有图集里的放大镜（没有就补一个 `.pxl`）。
- 农场皮与玻璃皮各给面板一个同名属性包，让主题换皮也覆盖到这里。

## 11. SKILL / 文档更新（同 PR，英文）

阶段 0（行为变化）：

- `reference/navigation.md`「Popup focus trap」：补全 Esc 规则。路由 Modal 与 ad-hoc 模态一样，菜单先收；Block 引导期间什么都不收；被模态、子页、Loading 盖住的菜单自动收起。
- `reference/controls-tabs.md`「Opening and closing」：加上「更高的层出现时收起」。
- C# skill 的 Modal `### Behavior`：写明 Esc 的决定顺序（§4.4 的表）。Layering 一节里的「an expanded `<TabMenu>`」泛化为「an open popup（TabMenu / suggestion list）」。

阶段 1：

- **新** `authoring-promptugui-xml/reference/controls-suggestions.md`，内容包括：
  - 配方；属性表；
  - 行模板与状态，以及为什么行里不能放 Selectable；
  - 键盘、手柄、指针三张表；提交表；显示条件；
  - 定位；输入法与手机的说明；
  - lint 表。
- `authoring-promptugui-xml/SKILL.md`：
  - built-in 目录加 `<Suggestions>` 行（部件元素，只能在 `<InputField>` 里）；
  - `### <InputField>` 表加 `submitIcon` / `submitIconSize` / `submitIconColor`，以及 `（子元素）<Suggestions …/>` 行，并指向 reference；
  - 「哪些标签画程序化」与 raycast 段的列表加 `Suggestions`；
  - 加 `### <Suggestions>` 桩小节。
- `reference/states.md`：候选行的 `state-hover` / `pressed` / `selected`（`selected` 即键盘高亮）。
- `scripting-promptugui-csharp/SKILL.md`：
  - InputField 事件加 `OnInput`，写明与 `OnValueChanged` 的区别；
  - 新小节 Suggestions：`BindItems` / `OnSubmit` / `HighlightedIndex` / `Open` / `Close`，以及「只推最新结果、别推空列表当加载中」；
  - 速查表的 EVENTS 与 DATA PUSH；
  - `### InputField two-level edit model` 补上手柄在候选列表里的行为。
- `AGENTS.md` 的 SKILL 路由加一条：`<Suggestions>`（行模板与状态、键盘 / 手柄 / 指针、提交、`PUI-SUGGEST-*`）→ `reference/controls-suggestions.md`；`InputField.OnInput` / `Suggestions` 的 C# API 同时改 C# skill。同一节开头列 reference 文件的那一行已经过时（只列了六个），顺手补齐。
- 主 spec `2026-05-07-...-design.md` §5 控件表加 `<Suggestions>` 行，链接本文。`2026-10-01-router-page-layering-design.md` §3 / §4.3 加注：`PopupSortingOffset` 已移到 `PopupChain.SortingOffset`，层带改称「浮层遮罩 / 面板」。

## 12. 不做的事 / 阶段 2 备忘

- **公开的 `<Popover>`**：已由 [`2026-10-19-popover-design.md`](2026-10-19-popover-design.md) 实现 —— 运行期锚点与 `OpenAt`、锚点失效（含 virtualize 下的行回收）、外部按下穿透探测、四向侧放与对齐、链深层序的告警都在那份 spec 里定。「焦点离开即关」没有做：浮层不对抗输入框失焦，插入走 `InputField.Insert`。
- **聊天 @ 补全**：锚在光标处；接受时替换 @ 那一段；Enter / Tab 是「接受」而不是「发送」；由触发字符开启。v2 可以考虑 `commit="fill"` 加光标锚定。
- 高亮时把候选文字预览进输入框、`TextMemberPath`（§2-G）。
- 候选列表虚拟化；「无结果」与「加载中」行；分组标题；不可选的行。
- ↓ 在列表关着时重新弹出列表（浏览器的做法）。
- 行里的 `on="expand"` 入场动画（给面板挂 `ExpandableMarker`、`MenuSourceTags` 加 `Suggestions`）。
- 面板按程序化 `radius` 做圆角裁切（v1 是矩形裁切，§6.8）。
- TabMenu 改为按安全区定位（PC-D9 保持不变）。
- 键盘挡住输入框本身的避让；`hideMobileInput` 属性。
- Dropdown 的 30000 层带（§4.4 只把它接进 Esc 与「被盖住即收」）。

## 13. 决策记录（2026-10-18 与作者对齐，按推荐已定）

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| PC-D1 | 抽取方式 | 内部 `PopupCore`，TabMenu 与候选列表委托它 | TM-D17 抽 `TabGroupCore` 的先例；TabMenu 内嵌 `<Popover>` 控件会与 TM-D3（表面 = 面板）冲突 |
| PC-D2 | 同时打开数 | 浮层链（单链）取代全局单例 | §1.2-5；没有嵌套时退化为今天的行为 |
| PC-D3 | 层序 | 宿主根序 + 2 + 2·链深，遮罩为面板 − 1 | 链深 0 时与今天逐值相同；阶段 2 重审步长下限 |
| PC-D4 | Esc | 一个决定点 + 保留现有监听作按键来源 + 按次去重 | §2-E；修 §1.2-1/2 |
| PC-D5 | 被盖住 | 模态 / 路由 Page·Modal / Loading 出现时 `DismissBelow`；Toast / Tutorial 不算 | §1.2-3；Tutorial 可能正指着浮层 |
| PC-D6 | 焦点还原 | 只还原仍属于自己的东西 | 避免盖掉新层刚设好的圈闭与焦点 |
| PC-D7 | 外部按下 | `Blocker`（吃掉）/ `Owner`（宿主决定）；穿透留给阶段 2 | §2-F |
| PC-D8 | 定位 | `PopupPlacer` 加 `bounds` / `preferAbove` / `Room`，保持纯函数 | 候选列表要夹高度、按安全区放；TabMenu 调用不变 |
| PC-D9 | TabMenu | 公开行为不变，只修漏洞；不改为安全区定位 | 纯重构有现成测试做护栏；安全区留作单独改动 |
| PC-D10 | Dropdown | 接进 Esc 第 2 步与 `DismissBelow`，不进链、不改层带 | §1.2-4，改动面最小；先写复现测试，不成立就删掉此条 |
| SG-D1 | 形态 | `<InputField>` 的部件子元素 `<Suggestions>`；面板就是它自己的程序化表面 | §2-B；有 `<Scrollbar>` / `<Header>` 的先例 |
| SG-D2 | 面板位置 | 挂在输入框 GameObject 之下 | §6.5 的结构前提 |
| SG-D3 | 行 | 每行一个内部外壳（`FocuslessPress`），模板实例化在它下面；模板里的 Selectable 给警告 | §2-C；`<Show>` 在应用属性时找源（§5.5） |
| SG-D4 | 高亮的状态名 | `state-selected` | 不新增 `state-focused` 语法（今天 `TriggerSpec` 不认识它，`<Show>` 只映射五个状态）；列表框里的高亮项就是 selected 项；合成规则现成（Toggle） |
| SG-D5 | 何时显示 | 编辑中 ∧ 已武装 ∧ 有项；武装来自 `OnInput` / `Open()` | 聚焦不弹旧候选；空推送只收起不解除武装 |
| SG-D6 | 键盘 / 手柄 | §6.2 的表；组字期间不接管 | 照 AutoSuggestBox；中文输入法不能被破坏 |
| SG-D7 | 提交事件 | `Suggestions.OnSubmit(Text, ChosenIndex)` 覆盖全部入口；`InputField.OnSubmit` 只发「提交的是输入的文字」 | 提议里的「提交时带上是否选了候选」；不认识候选的订阅者不会收到半截输入 |
| SG-D8 | 选中后改写文字 | 不自动改写，由 C# 决定 | §2-G |
| SG-D9 | 用户输入事件 | `InputField.OnInput`（HTML `input` 语义） | 代码赋值不触发查询；`OnValueChanged` 不变 |
| SG-D10 | 提交钮 | `<InputField submitIcon>`，不是 Selectable，与 Enter 同义；没有候选时也能用 | 提议里的「回车小钮」；做成外部 `<Btn>` 的话，按下时会结束编辑并丢掉高亮 |
| SG-D11 | 行复用 | 同 ScrollList（`ItemBinding`），不做虚拟化；每次推送重置高亮 | 每敲一个键都会推送 |
| SG-D12 | 尺寸与侧 | 永远与输入框同宽（v1 无宽度属性）；高为 `min(内容, maxHeight, Room)`，`maxHeight = 0` 只受 `Room` 限制；打开时定侧，推送不翻面 | 打字时面板不跳；`width` 是通用属性，控件分不清作者值与上次定位的残留 |
| SG-D13 | 边界 | 安全区；可见的软键盘只扣高度 | 竖屏手机是目标平台；`area` 的坐标原点因平台而异 |
| SG-D14 | 过渡默认值 | `0.1s` | 候选列表开关频繁，比 TabMenu 的 0.15s 更利落 |
| SG-D15 | 按下进行中 | 推迟因编辑结束而起的关闭，直到这次按压有结果 | Android 点游戏画面可能先收键盘，这次点击不能丢 |

## 14. 里程碑拆分

| M | 内容 | 依赖 |
|---|---|---|
| M0 | `PopupPlacer` 扩展；`PopupChain` + `PopupCore`；TabMenu 委托过去。纯重构，TabMenu 全部测试保持绿 | — |
| M1 | `EscapeChain` 与监听改线；§1.2-1..4 的修复（Red 先行）；`DismissBelow` 的三个钩子；阶段 0 的文档 | M0 |
| M2 | `<Suggestions>`：结构、认领、`FocuslessPress` 行、`BindItems` 复用、定位、显示状态机（编辑态钩子）、注册 | M0 |
| M3 | `PuiInputField`：按键接管、组字让路、手柄、`OnInput`、编辑边沿、Esc 同帧协调；提交路由；PlayMode 测试 | M1、M2 |
| M4 | `submitIcon`（可独立砍掉，不影响 M2 / M3） | M3 |
| M5 | lint：`SuggestionsRules` + 扩展；CLI 跑遍 `Runtime/Resources` 与 `Samples~` | M2 |
| M6 | 样例；阶段 1 的 SKILL；`AGENTS.md` 路由；主 spec 与 RPL 加注 | M1–M5 |

建议阶段 0（M0 + M1）单独开分支提 PR，合并后再开阶段 1。阶段 0 改动面广（每个模态的 Esc 都经过它），先单独落地，回归更清楚。

## 15. 风险与回滚

| 风险 | 缓解 |
|---|---|
| TMP 内部随 ugui 版本变化（接管循环照着 2.0.0 的 `OnUpdateSelected` 写） | 只在列表打开时接管，其余一律 `base`；路由是纯函数有单测；PlayMode 冒烟；升级 ugui 时对照 `OnUpdateSelected` 重新核对 |
| 输入法的平台差异（Windows / macOS / 移动端） | 组字窗口内不接管（含结束那一帧）；Windows 与 macOS 手测中文 / 日文输入法 |
| Android 点游戏画面可能收起键盘，先于点击结束编辑 | SG-D15；真机 QA |
| `TouchScreenKeyboard` 的可见性与高度不可靠 | 只用高度、尽力而为；测试注入 |
| Esc 改线影响每一个模态 | 监听组件与 `enabled` 规则不变；全部模态、路由、教程的测试做护栏；阶段 0 单独 PR |
| 「被盖住即收」与宿主预期冲突（比如宿主想让菜单在 Loading 下保持展开） | 只有三个层管理器触发；写进 skill；需要时可加开关（不预先做） |
| 回滚 | 阶段 1 是纯新增（新标签、新属性、新事件），可整体回退；阶段 0 回退到 `TabMenu` 的私有实现与原 Esc 落点即可，`PopupPlacer` 的扩展向后兼容 |

## 16. 实施记录（阶段 0）

**§1.2 四条复现**：都先写成测试，在改线之前全部红在预期断言上，四条都成立（PC-D10 因此保留）。

| # | 测试 | 改线前的结果 |
|---|---|---|
| 1 | `EscapeChainTests.Routed_modal_with_an_open_menu_closes_only_the_menu` | 菜单收了，路由 Modal 也 `Back()` 掉了 |
| 2 | `EscapeChainTests.Blocking_tutorial_leaves_an_open_menu_open` | Block 步骤期间菜单被 Esc 收起 |
| 3 | `PopupCoverTests.Opening_a_modal_by_code_collapses_an_open_menu` / `Closing_that_modal_returns_the_focus_to_the_menu_handle` | 菜单留在模态下面；模态关掉后焦点回到一行已失活的 Tab |
| 4 | `DropdownEscapePlayTests.Escape_on_an_expanded_dropdown_inside_a_modal_closes_only_the_dropdown`（真按键） | 模态被同一下关掉，并且触发 Input System 断言「Must not destroy InputActionState while executing an action callback within it」 |

**与设计的偏差 / 补充**：

- **监听在自己的回调里被销毁**（第 4 条复现顺带暴露，所有真按键关模态的路径都有）：按 Esc 关掉模态时 `Object.Destroy` 同步走到监听的 `OnDisable`，旧实现在那里 `Dispose()` 自己的 InputActionMap。现在 `OnDisable` 只 `Disable()`（回调内允许），`OnDestroy` 才释放；若释放发生在任何监听的回调之内，map 推迟到 `InputSystem.onAfterUpdate` 再 `Dispose()`。补了 `DropdownEscapePlayTests.Escape_with_no_list_open_closes_the_modal` 守住。
- **`AlsoCancelButton` 改成属性**：map 已建好时设置会重建绑定，修掉「菜单第一次展开时手柄 B 不生效」。测试用 internal 只读 `BindsCancelButton`。
- **第 3 步「不低于当前最上层模态」**：取 ad-hoc 栈顶与路由链里最高 Modal 两者根 Canvas 的 `sortingOrder` 的较大者（`UI.Router.TopModalScreen` 为此新增，internal）。
- **Dropdown 第 2 步对 `CancelButton` 同样生效**（表里第 4 步之前），与「B 只关浮层」一致；不记 `PopupConsumedFrame`（那是给文本框用的，Dropdown 展开时焦点不在文本框上）。
- **Dropdown 的「展开」以 TMP 的遮罩为准**：`Hide()` 一开始淡出就出集合，淡出期间 `IsExpanded` 仍为 true 但不再挡 Esc。
- `UI.Router.IsTop` 已无调用方，删除。

## 17. 实施记录（阶段 1）

**与设计的偏差 / 补充**：

- **面板宽度**：每次定位时把面板宽设为输入框宽（`PopupCore.Reposition` 写点锚 + `sizeDelta`），而不是 §6.8 写的横向 stretch。效果相同；差别只在打开期间输入框被布局改宽时，面板等到下一次重放（推送 / ReSolve）才跟上。
- **`itemTemplate` 的解析时机**：同 `<ScrollList>`，在属性应用时解析，名字不存在立刻报 `ParseException`；缺省时 `BindItems` 的推送抛 `InvalidOperationException`（R3 记日志）。
- **侧的锁定**：用一个纯函数 `SuggestionsPlacement.Solve(field, bounds, content, maxHeight, gap, above, locked)` 同时给出高与侧：未锁时翻面规则同 `PopupPlacer`（`content > 偏好侧空间 ∧ 另一侧更宽裕`），锁定（打开期间）只按该侧重夹高度。内核再交给 `PopupPlacer` 时不会翻面，因为高度已不超过该侧空间。
- **按键接管的 Navigate 抑制**（P21）：`PuiInputField` 在处理过 IMGUI 方向键后的 **0.6 秒**内忽略垂直 `OnMove`（输入模块的 Navigate 首次重复在 0.5 秒），而不是「几帧」——按住 ↓ 时 IMGUI 重复与 Navigate 重复会交错到达，按帧数挡不住后者。
- **组字判定**：取当前输入模块的 `BaseInput.compositionString`，取不到退回 `Input.compositionString`；后者在只开 Input System 的工程里若抛 `InvalidOperationException` 就当作没有组字。
- **SG-D15 的实现**：`FocuslessPress` 在抬起时发 `Released(willClick)`（指针仍在行内、未转为拖动、可交互），列表据此在点击之前决定「选中」或「关」；随后到来的 click 发现列表已关就什么都不做。
- **运行期镜像**：`ScreenInstantiator` 只镜像控件自己不说的 `PUI-SUGGEST-LAYOUT-ATTR` / `PUI-SUGGEST-CHILD`，以及宿主上的 `PUI-SUGGEST-MULTILINE`；`TEMPLATE` / `ENUM` / `VALUE` 由 `BindItems` / setter 自己抛或警告，`ROW-SELECTABLE` 只在 CLI 报（控件首次建出这种行时自己警告一次）。
- **`PUI-RAYCAST-UNDECIDED` 的扩展**：`<Suggestions>` 进了 `InteractiveTags`，但放在 `<InputField>` 里时本来就被输入框覆盖（输入框是交互标签，整棵子树不再往下查），只有放错位置时这条扩展才起作用。
- `Control.CanvasGroup` 改为先找后建（P19），并用显式 null 判断：编辑器里缺失的原生组件 `GetComponent` 回来的是假 null，`??` 会把它当成真对象。

**验证**：EditMode / EditorOnly / PlayMode 三个程序集全绿；`dotnet format --verify-no-changes` 干净；UIXmlLint 对 `Runtime/Resources/` 与 `Samples~/` 零问题，scratchpad 里一份故意出错的 `.ui.xml` 九个 `PUI-SUGGEST-*` 全部报出。真按键的 PlayMode 用例（`InputTestFixture`）覆盖手柄十字键移高亮与 Esc 只关列表；IMGUI 事件循环本身（`Event.PopEvent` 那一行）、输入法、手机真机与样例的观感留待手测。

## 18. 修复：Esc 的 IMGUI KeyDown 晚一帧到

**现象**（编辑器里真按键）：`<Popover closeOnEditEnd="true">` 里一个带 `<Suggestions>` 的输入框，列表开着按一下 Esc —— 列表关了，下一帧编辑被取消、文字回滚，浮层随之以 `EditEnded` 关闭。不在浮层里的带候选输入框，按一下 Esc 也会把编辑一起取消。预期是第一下只关列表、继续编辑。

**原因**：链在第 N 帧的 Input System 回调里关掉列表，并记 `PopupConsumedFrame = N`。同一下按键 TMP 会从两处收到：

- 输入模块的 Cancel —— `InputSystemUIInputModule` 按 `WasPerformedThisDynamicUpdate` 发，与链是同一次 Input System 更新，必在第 N 帧，`OnCancel` 的同帧判断防得住；
- IMGUI 的 Escape KeyDown —— TMP 在 EventSystem 的 Update 里用 `Event.PopEvent` 读，与 Input System 是两条独立的事件来路，编辑器里实测会晚到 N+1。那时 `PopupConsumedFrame == Time.frameCount` 已不成立，列表也已关（`ListTakesKeys` 为假），于是走 `base.OnUpdateSelected`，TMP 当成取消编辑。

TMP 的回滚只有这两个入口（另一个是软键盘取消，与桌面无关）：`KeyPressed` 里的 `KeyCode.Escape`，和 `OnCancel`。字符 27 被 `IsValidChar` 挡掉，不会回滚。所以凡是回滚，必是 Escape KeyDown 在第 N 帧之外到了 `base`。`InputTestFixture` 只模拟 Input System、不产生 IMGUI 事件，原有的 PlayMode 用例抓不到这个时序。

**修法**：IMGUI 那一路不再按「同一帧」判断，改成「链欠输入框一下 Esc KeyDown」。

- **记账**（`EscapeChain`，§4.4 第 3 步）：键盘 Esc（`EscapeKey.Escape`）关掉浮层时，记下这一帧。手柄的 East / Start 没有 IMGUI KeyDown，不记。`EscapeOwed` 只在记账后的 `OwedEscapeFrames = 3` 帧内为真：一直没人收这笔账，说明那下 KeyDown 在链听到之前就过去了。
- **收账**（`PuiInputField`）：只要还欠着，`OnUpdateSelected` 就接管 IMGUI 循环。下一个 Escape KeyDown 经 `SuggestionKeys.Route` 吞掉，并清账（`SettleOwedEscape`）。
- **按住**：被吞掉的那下 Esc 在 KeyUp 之前仍算同一次按键。它的系统重复 KeyDown 也吞掉，接管一直持续到 Escape KeyUp。旧实现里按住 Esc 过了重复延迟（约 0.5 秒），TMP 同样会取消编辑；这次一并修掉。编辑开始或结束（`isFocused` 的边沿）、`OnDisable` 都会清掉「按住」，免得 KeyUp 没来就卡住。
- **KeyUp 先到**：已经欠着，却先看到 Escape KeyUp —— 说明 IMGUI 跑在链前面，那下 KeyDown 已被开着的列表吞掉 —— 这时清账，下一次按 Esc 照常归 TMP。
- **`OnCancel` 不变**：仍按 `PopupConsumedFrame == Time.frameCount` 判断，理由见上。
- **测试**：`ForgetPopupConsumedFrameForTests` 一并清账，`ResetForPlaySession` 也清。

**测试**（先红）：`InputFieldSuggestionsDevicePlayTests.An_escape_keydown_a_frame_late_does_not_cancel_the_edit` 用真键盘按下 Esc，在 N+1 帧手动把 IMGUI 的 Escape KeyDown 交给 `HandleKeyDown`。改前红在「编辑被取消」上。另有：

- `PopoverDevicePlayTests.Escape_in_a_search_popover_closes_only_the_suggestions_though_TMP_reads_it_a_frame_late`：上面的现象原样复现，断言浮层不关、`OnClosed` 不发；
- `A_held_escape_keeps_its_key_repeats_from_TMP`、`An_owed_escape_nobody_collects_lapses`（PlayMode）；
- `PuiInputFieldTests` 的收账、按住、KeyUp 先到三例；
- `EscapeChainTests.Only_a_keyboard_popup_press_is_owed_to_a_text_field` / `A_press_that_closes_no_popup_owes_nothing`。

真机上 IMGUI KeyDown 落在哪一帧仍只能手测；修法对 N、N+1 到 N+3 帧都成立，KeyDown 先于链到达也成立。
