# 公开浮层 `<Popover>`（浮层阶段 2）

> 状态：**已实施**（实施记录见 §16）。**已对齐**（2026-10-19 起草。要作者拍板的四点按推荐定：独立元素；外部按下默认穿透；输入框不对抗失焦，改加 `InputField.Insert`；`<Show>` 认 `expand` / `collapse`。其余按推荐写；同日按实施计划 `plans/2026-10-19-popover.md` 的计划期修正与审阅结论改齐）。
> 来源：`2026-10-18-popup-core-and-input-suggestions-design.md` §12 的阶段 2 备忘；表情选择、列表行「…」菜单、右键菜单这类用法；读源码做的评估。§1.2 的问题都是读代码得出的，没有在 Unity 里复现；复现由 §9 的 Red 测试给出。
> 相关：
> `2026-10-18-popup-core-and-input-suggestions-design.md`（`PopupCore` / `PopupChain` / `EscapeChain` / `PopupPlacer` / `PopupBounds`；PC-D3 层序、PC-D6 焦点还原、PC-D7 外部按下；SG-D12 定侧锁定、SG-D13 边界）、
> `2026-08-29-tabmenu-design.md`（TM-D3 表面 = 面板、TM-D12 收起态测量、TM-D13 过渡、TM-D15 焦点圈闭、TM-D19 默认皮肤）、
> `2026-10-01-router-page-layering-design.md`（§4.3 步长下限）、
> `2026-08-31-hug-reveal-flip-checked-design.md`（hug、`@id` 的词法解析、`checked` 持久态）、
> `2026-09-29-scrolllist-virtualization-design.md`（窗口里行的交接）、
> `2026-06-29-inputfield-nav-edit-mode-design.md`。

## 1. 问题

### 1.1 要做的事

一块贴着某个元素浮出来、盖在页面之上的面板：

- 聊天输入框旁的表情钮：点开一格格表情，点一个，插到光标处；
- 列表每行的「…」：点开这一行的菜单，一个浮层服务所有行；
- 右键 / 长按某处：在指针位置弹出菜单；
- 点一个数值：旁边浮出它的构成。

今天只能手写：一块普通面板，C# 在按钮回调里切 `Hidden`。这样做缺了很多：

- 点外面、Esc、手柄 B 都不会关；被模态、路由页、Loading 盖住时也不收；
- 面板留在所在容器的层里，出不了它的 mask，还会被后画的兄弟盖住；
- 不会按空间翻面、夹进屏幕，也不跟着锚点走；
- 手柄打开后焦点不进去，关掉后也不回来；
- 点表情格（Selectable）会结束输入框的编辑，插入位置得宿主自己在 `onDeselect` 里记。

阶段 0 抽出的浮层内核已经有抬层、Esc 链、被盖住即收、过渡、圈闭，但只服务两个内部用户：`<TabMenu>` 和 `<Suggestions>`，两者的面板都挂在宿主之下。

### 1.2 内核还缺什么

1. **锚点不是面板的父物体。** `PopupCore.Reposition` 把面板的 anchor 写在宿主的角上（`PopupCore.cs:183-186`），前提是面板是宿主的子物体。`PopupChain.IsAncestorOf` 也只看宿主在不在对方面板里（`PopupChain.cs:156-161`）。浮层声明在一处、锚在另一处时，这两条都不成立。
2. **外部按下只有两种处理。** 一是 `Blocker`（遮罩吃掉），二是 `Owner`（宿主决定）。穿透留到了阶段 2（PC-D7）。
3. **`PopupPlacer` 只有上下两侧，且只会左对齐**（`PopupPlacer.cs:44-72`）。
4. **链深 ≥ 1 的层序没审过**（PC-D3）。路由页的步长下限 `SortingOffset + 2 = 4`（`UI.Router.Layering.cs:48`）只覆盖链深 0。
5. **宿主被祖先停用后，浮层还留在链上。**
   - 只有两条路会收起它：ReSolve（`PopupCore.OnHostAfterApply`）和 Screen 关闭（`Screen.cs:849` 的 `DismissUnder`）。
   - C# 把祖先设成 `Hidden = true` 时，面板跟着失活，看不见了，链上却还有它。
   - 后果举例：模态里展开着的 `<TabMenu>` 被这样藏掉之后，下一次 Esc 关掉的是这个看不见的菜单（`EscapeChain.cs:61-65` 的第 3 步），模态没关。
  - 修法见 §5.2 的清理（`Prune`）。
6. **`<Show>` 不认 `expand` / `collapse`。** 这两个值会落到 `Show.cs:76-86` 那段 switch 的 default 分支并抛错。所以想让触发钮显示「开着」的样子，只能用 `<Animation on="expand@…" reverse-on="collapse@…">` 拼出来。

## 2. 否决的方案

**A. 写成触发钮里的部件子元素（`<Btn><Popover>…</Popover></Btn>`）。否决。**
- `<Suggestions>` 挂在输入框下，是为了让输入框保住焦点（上一份 spec §2-A）。浮层没有这个需要，挂在 Btn 下反而处处吃亏：
- 面板在 Btn 子树里：
  - 指针移进面板时，Btn 收不到 exit，悬停态一直亮着；
  - Btn 的按下位移 / 缩放会带着面板一起动。
- 一个触发钮只能配一个面板，做不了「一个浮层服务多个锚点」：
  - 列表每行都要一个菜单实例；
  - 虚拟化回收时，菜单还要跟着行走。

**B. 外部按下默认吃掉（同 TabMenu）。否决作为默认，保留为 `dismiss="block"`。**
- 浮层关掉后，最常见的下一步就是点外面的东西：点输入框接着打字，或者点另一个数值看它的构成。吃掉的话，这些都要点两下。
- 菜单类，以及怕误触世界物体的地方，写 `dismiss="block"`。

**C. 让输入框在点浮层时继续编辑。否决。**
- 唯一做得到的结构是 `<Suggestions>` 那种：内容挂在输入框之下，且不是 Selectable（上一份 spec §2-A / §2-C）。
- 通用浮层的内容是任意控件，表情格本身就是 `<Btn>`，满足不了这个结构。
- 手机聊天的习惯本来就是表情面板顶替键盘。
- 真正缺的是「插到光标处」，由 `InputField.Insert` 补上（§4.12）。

**D. 打开时把面板挂到锚点下。否决。**
- 每换一个锚点，面板继承的 CanvasGroup 和 mask 都会变，`<Show on="state-*">` 向上找到的源也会变。
- 锚点在虚拟化列表的行里时，面板会被行带进池子。

**E. 面板做成 `<Frame>` 式的自由定位，必须写尺寸。否决。**
- `hug` 只在有内容尺寸的标签上成立（`PUI-HUG-TAG`）。
- 浮层大多是一列东西（标题 + 列表 + 按钮），按 `<VStack>` 排、默认 hug 最省事。要自由摆放，就在里面放一个定尺寸的 `<Frame>`。

**F. 用全屏透明遮罩接住按下，再转发给下面的目标。否决。**
- 转发要重新做射线检测，还要伪造输入模块的按压状态（`pointerPress`、拖动阈值、click 资格），会跟 uGUI 的版本绑死。
- 改照上一份 spec §12 的备忘做：每帧探测按下，再 `EventSystem.RaycastAll`（§5.4）。

**G. 同时做悬停提示（hover 开、离开关）。推迟。**
- 开关规则完全不同：要延迟，指针离开即关，不进 Esc 链，从不拿焦点。
- 以后另起一个控件，可以复用同一个内核。

## 3. 方案总览

一个独立的 `<Popover>` 元素，写在页面任意处，经 `on="click@<id>"` 或 C# 指向锚点。行为参照 HTML 的 `popover="auto"`：
- 点外面或 Esc 就关（轻关闭）；
- 打开一个不相干的浮层时，旧的自动关掉；
- 从浮层里打开的浮层算子层。

```xml
<!-- 表情：点钮开合，浮在钮的上方、右对齐；点一格插到光标处 -->
<HStack height="44" spacing="4">
  <InputField id="chat" width="stretch" height="stretch" placeholder="说点什么…"/>
  <Btn id="emojiBtn" width="44" height="stretch">
    <Icon name="ui/smile" anchor="center" size="24x24"/>
    <Show on="expand@emojiPop"><Frame anchor="stretch" color="accent/0.25"/></Show>   <!-- 开着时 -->
  </Btn>
</HStack>

<Popover id="emojiPop" on="click@emojiBtn" placement="top-end" padding="6" radius="8">
  <ScrollList id="emojiGrid" itemTemplate="EmojiCell" columns="8" cellSize="36x36" spacing="2" size="304x152"/>
</Popover>

<!-- 行菜单：manual，由 C# 按行打开；菜单宽度写死，行铺满 -->
<Popover id="rowMenu" width="160" padding="4" spacing="2">
  <Btn id="menuRename" class="menu-row" width="stretch" height="36">重命名</Btn>
  <Btn id="menuDelete" class="menu-row" width="stretch" height="36">删除</Btn>
</Popover>
```

```csharp
var chat = screen.Get<InputField>("chat");
screen.Get<ScrollList>("emojiGrid").BindItems(Observable.Return<IReadOnlyList<string>>(Emoji.Common),
    (Btn cell, string e) =>
    {
        cell.Get<Text>("glyph").TextValue = e;
        cell.OnClick.Subscribe(_ => chat.Insert(e)).AddTo(cell);   // 插到光标处；不在编辑时用上次的光标
    }).AddTo(screen);

// 一个菜单服务所有行
var menu = screen.Get<Popover>("rowMenu");
Item target = null;
list.BindItems(items, (IControl row, Item it) =>
{
    var more = row.Get<Btn>("more");
    more.OnClick.Subscribe(_ => { target = it; menu.Toggle(more); }).AddTo(row);
}, key: it => it.Id).AddTo(screen);
screen.Get<Btn>("menuDelete").OnClick.Subscribe(_ => { Delete(target); menu.Close(); }).AddTo(screen);

// 右键菜单：在你自己的指针回调里
menu.OpenAt(eventData.position);
```

## 4. 作者面

### 4.1 XML 形态

- **位置**：
  - `<Popover>` 写在 Screen 或模板里的任意位置。
  - 它**从不占布局**：放在 `<VStack>` / `<HStack>` / `<Grid>` 里，也不占格子、不贡献尺寸（`ParticipatesInLayout = false`，同 `<Decor>` / `<Scrollbar>`）。
- **建议写在哪**：
  - 触发钮附近，或 Screen 顶层；必须和触发钮在同一个 Screen 里。
  - 触发钮在模板实例里时，浮层也写进同一个实例：id 按词法作用域找（§4.3）。
- **子元素**：
  - 子元素就是面板内容，像 `<VStack>` 那样自上而下排。
  - `flow="false"` 的子元素照常自由定位（铺底、角标）。
  - `<Decor>` 装饰面板。
- **嵌套**：浮层里可以再放浮层、`<TabMenu>`、带 `<Suggestions>` 的输入框（§4.9）。

### 4.2 属性

| 属性 | 取值 | 默认 | 语义 |
|---|---|---|---|
| `on` | `click@<id>` \| `manual` | `manual` | 谁来开合（§4.3）。`click@<id>`：点那个 `<Btn>` 就切换开合，锚点就是它。`manual`：只由 C# 打开 |
| `placement` | `top` / `bottom` / `left` / `right`，可加 `-start` / `-center` / `-end` | `bottom-start` | 偏好放在锚点的哪一侧，交叉轴怎么对齐（§4.5） |
| `gap` | px（≥ 0） | `4` | 与锚点的间距 |
| `dismiss` | `pass` \| `block` | `pass` | 外部按下时：`pass` 关掉浮层，这一下照常落到下面；`block` 用全屏遮罩吃掉这一下（同 `<TabMenu>`） |
| `transition` | 时长（`0.15s` / `150ms` / 秒数） | `0.15s` | 开 / 关过渡：淡入，同时从锚点那一侧滑出 8px。`0` 为即时（同 TM-D13） |
| `width` / `height` / `size` | 数值 / `hug` / `clamp(min, hug, max)` | `hug` | 面板尺寸（§4.7） |
| `padding` · `spacing` · `childAlign` | 同 `<VStack>` | `0` · `0` · `upper-center` | 面板内的排版 |
| `color` · `sprite` · `tint` | 同 `<TabMenu>` 的面板 | 默认皮肤（TM-D19） | 面板底 |
| 程序化属性（`radius` / `border*` / `glow*` / `glass` …）+ `<Decor>` | 同 `<Frame>` | — | 全部落在面板上 |
| `interactable` | bool | `true` | `false`：打不开；开着时关掉 |

**忽略、并给 lint 警告的属性**：`anchor` / `margin` / `pivot` / `flow` / `scale` / `hidden`。面板的位置归 `placement` 管，显隐归开合管。

**不开放 `raycastTarget`**：面板永远接住指针，所以在面板空白处按下不会落到后面，也不算「外面」。

### 4.3 开合

- **`on="click@<id>"`**：
  - 点那个 `<Btn>`：关着时，就以它为锚点打开；开着且锚点就是它时，就关上（原因 `Trigger`）。
  - id 的解析同 `<Trigger on="click@…">`，走词法作用域：先看自己的子树，再逐层看外面的作用域（模板实例的 id 表），最后看 Screen 顶层。
  - 触发钮不能在浮层自己里面。
  - 解析推迟到浮层所在的那一次实例化（Screen 打开，或一个模板实例）完成之后。所以触发钮写在浮层前面、后面都行（§5.6；动态子树里 `<Trigger>` / `<Show>` 的 `@id` 也一并这样推迟）。
  - 找不到 id、找到的不是 `<Btn>`、或在浮层自己里面：抛 `InvalidOperationException`（同 `<Trigger>`）。
  - `on` 可被变体改写；改写后重新订阅。
- **`manual`**（默认）：只由 C# 打开（§4.11）。
- **开着时再按触发钮**：两种模式下「再点一下就关」都成立，只是走的路不同。
  - `pass`：触发钮算「里面」（§4.4），按下时不关，随后的 click 再把它切换成关；
  - `block`：这一下按在遮罩上，按下即关，click 被吃掉。
- **打不开的情况**（什么都不做）：
  - 浮层不可交互；
  - 锚点没有激活，或不可交互；
  - 浮层的父节点没有激活；
  - 浮层所在的层被盖住：宿主根序低于最上层模态，或 Loading 正在显示。否则代码在下层模态里打开的浮层会画在上层模态之上。

### 4.4 关闭

| 来源 | 原因（`PopoverCloseReason`） | 说明 |
|---|---|---|
| `Close()`；开着时 `Open(另一个锚点)`；浮层变得不可交互 | `Code` | |
| 再点 `on=` 的触发钮 | `Trigger` | |
| Esc、手柄 Start / B | `Escape` | 走 `EscapeChain` 第 3 步，只关链顶。B 也关（同 TabMenu 的 `cancelButtonCloses`） |
| 外部按下 | `OutsidePress` | `pass`：按在面板、锚点、链上更高的浮层之外（§5.4）。`block`：遮罩被点 |
| 模态、路由 Page / Modal、Loading 出现 | `Covered` | 既有的 `DismissBelow` |
| 锚点失效 | `AnchorLost` | 见 §4.6 |
| 链上另开了一个不相干的浮层（另一个 Popover、TabMenu、候选列表） | `Replaced` | 既有的 `PopupChain.Push` |
| 所在 Screen 开始关闭；浮层被祖先停用；`Dispose` | `HostGone` | |

- 点浮层里的内容**不会**自动关：表情要能连点。菜单类在 C# 里自己调 `Close()`。
- 教程的 Block 步骤拦截输入时，Esc 和外部按下都不关浮层（同 Esc 链第 1 步）。

### 4.5 定位

- **`placement` = 侧[-对齐]。**
  - 侧是偏好。偏好侧放不下、且对侧更宽裕时才翻面。这是 `PopupPlacer` 今天的规则，扩到了左右两侧。
  - 对齐指交叉轴：
    - `start`：对齐锚点的左边（放在左右两侧时，对齐上边）；
    - `center`：居中；
    - `end`：对齐右边（放在左右两侧时，对齐下边）。
  - 对齐之后如果越出边界，就往回拉，但起始边（左 / 上）不越出边界。
- **两侧都放不下时**：放在较宽裕的一侧，并沿主轴推回边界内，这时面板可能盖住锚点。面板比边界还大时，保证起始边留在边界内。
- **边界**：安全区，再扣掉可见的软键盘（`PopupBounds`，同 SG-D13）。
- **定侧锁定**：
  - 打开时选定一侧，开着期间不再翻面。内容变高、锚点移动都不翻，只重新夹边，防止面板来回跳（同 SG-D12）。
  - Canvas 的尺寸变了（转屏、改窗口大小）才重新选侧。软键盘带来的边界变化只按锁定的侧重新夹边：键盘区域会连续几帧变化，按它解锁会让面板来回翻面。
- **`OpenAt(屏幕点)`**：锚点是那个点（零尺寸），其余规则相同。默认 `bottom-start` 时，面板的左上角落在指针右下方，同常见的右键菜单。

### 4.6 跟随与锚点失效

- **跟随**：开着期间每帧（`LateUpdate`）比对锚点在画布上的矩形，变了就重新定位。锚点被动画移走、所在列表滚动、布局变化时，面板都跟着走。
- **锚点失效**：出现下面任一情况，按 `AnchorLost` 关闭。
  - 锚点被销毁，或不再激活，或不可交互（有 Selectable 看 `IsInteractable()`，否则看控件自己的 CanvasGroup；不读 `IControl.Interactable`，它会懒建 CanvasGroup）。
  - 锚点整个滚出了它所在的裁切区：取锚点各级 `RectMask2D` 祖先的交集，遇到 overrideSorting 的 Canvas 就停（同 `MaskUtilities`）。所以锚在另一个浮层里的锚点，不会被背后的列表误判为「被裁掉」。
  - 锚点所在的 `<ScrollList>` 行停进了池子、被退役，或改去显示另一项：
    - 虚拟化滚动时，离开窗口的行会直接交给新进来的项（`ScrollList.cs:1898-1905` 的 `TakeRow`）；
    - 推送之后，同一行对应了别的 key。
- **不算失效的重绑**：
  - 同一项在每次推送时的重绑不算，因为推送会重绑每一行；
  - 位置复用（不带 key）时，行的身份就是下标，同一下标的重绑也不算。
- **`OpenAt` 打开的浮层**：没有锚点，只在 Canvas 尺寸变化时重新夹边。

### 4.7 尺寸

- 面板按 `<VStack>` 排子元素：尺寸 = `padding` + `spacing` + 子元素的偏好尺寸。
- `width` / `height` 的取值：
  - 省略或 `hug`：等于内容；
  - 数值：固定；
  - `clamp(min, hug, max)`：等于内容，但夹在上下限之间；
  - `stretch` / `N%` / `native`：报错（`PUI-POPOVER-SIZE`）。面板不在任何布局里，也没有父尺寸可分。
- **内容变化**（推送、换行、切语言）在同一次画布更新里跟上，同 hug。开着时尺寸变了，就按锁定的侧重新夹边。
- **子元素的尺寸规则同 `<VStack>`**：
  - 高度 hug 时，`height="stretch"` 的子元素会塌成 0（`PUI-HUG-STRETCH-CHILD`）；
  - 宽度 hug 时，宽度由写了宽度（或有内容宽度）的子元素撑开，`width="stretch"` 的子元素铺满这个宽；
  - 菜单类通常写死面板宽度，行写 `width="stretch"`。
- **内容太高**：在面板里放一个 `<ScrollList height="clamp(_, hug, N)">`。

### 4.8 焦点

- **打开时**：
  - 导航已启用（`UI.UseGamepadNavigation()`）且面板里有可聚焦项：把焦点圈在面板里，落到第一个可聚焦项（同 TM-D15）。
  - 面板里没有可聚焦项（纯信息浮层）：不圈，也不挪焦点。
- **关闭时**只还原仍属于自己的东西（PC-D6）：
  - 圈闭仍是本面板，才还原圈闭；
  - 选中项还在面板里，才把它挪回锚点。锚点不可选、或浮层是 `OpenAt` 打开的，就挪回打开时的选中项；都不行就清空。
  - 不论打开时有没有圈，挪回选中项这一步都做。鼠标点过面板里的 `<Btn>` 时，它可能成了选中项；面板一关，它就变成一个失活的选中项。
- **表情场景**：点触发钮、点表情格（都是 Selectable）会结束输入框的编辑，手机上还会收起键盘。这是有意的（§2-C），插入用 `Insert`（§4.12）。

### 4.9 嵌套与层序

- **祖先**：B 的宿主**或 B 的锚点**在 A 的面板子树里，A 就是 B 的祖先。所以从浮层里的按钮打开的浮层是子层，不论它声明在哪。
- **不相干的浮层**：打开的浮层不是任何开着项的后代时，先关掉整条链（原因 `Replaced`），同今天。
- **层序不变**：链深为 k 的面板 = 宿主根序 + 2 + 2k，遮罩 = 面板 − 1（PC-D3）。
- **路由页上能叠几层**：
  - 路由页的步长（`PageSortingOrderStep`，默认 10）决定一页上能叠几层而不越过下一页：要求 2 + 2k < 步长。默认步长 10 时，链深最多到 3。
  - 宿主是路由页、且这次打开的一层越过了下一页的层序时，Console 警告一次，仍然打开。
  - 不改步长下限（4 只保证链深 0）。
  - 真正撞上，只会发生在上层页不全屏、下层页还能被点到的时候：上层页出现时，`DismissBelow` 已经收起了下层页的浮层。
- **ad-hoc 模态**：模态之间层序只差 1，但不受影响：
  - 上层模态出现时，会收起下层模态里的浮层（`Covered`）；
  - 下层模态被盖住，也点不到。

### 4.10 `expand` / `collapse` 与 `<Show>`

- **`<Popover>` 是 `expand` / `collapse` 的源**，同 `<TabMenu>` / `<Collapsible>`：
  - 面板里不带 `@id` 的 `on="expand"` 会向上找到它，用来做行的入场动画；
  - 别处用 `expand@<id>` / `collapse@<id>` 指向它；
  - `expand` 在打开开始时触发。此时内容已经激活、量过尺寸、定好位置（同 `IExpandable.OnExpanded` 的契约）。
- **`<Show>` 新增认 `expand` / `collapse`**，作为持久态，同 `checked`：
  - `<Show on="expand@pop">` 开着时显示，`collapse@pop` 关着时显示；
  - 首帧按当时的状态落定。显隐由源头驱动（§5.7），所以 `<Collapsible expanded="true">`、手风琴裁决后的初态、动态行里的折叠块都从第一帧就对；
  - 对 `<TabMenu>` / `<Collapsible>` 一样成立。例如在 `<Collapsible>` 的 `<Header>` 里写不带 `@id` 的 `<Show on="expand">` 来换箭头。
- **误用**：不带 `@id`、写在自己源的面板里的 `<Show on="expand|collapse">` 没有意义，还会出错，lint 会警告（`PUI-SHOW-EXPAND-SELF`）：
  - 打开时内容已经量完，`expand` 那块这时才出现，面板尺寸就不对了；
  - `collapse` 那块会在关闭过渡里闪出来。

### 4.11 C# API

```csharp
public sealed class Popover : ProceduralControl
{
    public bool IsOpen { get; }
    public IControl Anchor { get; }                // 当前锚点；关着、或由 OpenAt 打开时为 null

    public void Open();                            // 以 on= 的触发钮为锚点；没有 on= 时抛 InvalidOperationException
    public void Open(IControl anchor);             // 同一 Screen 里的任意控件
    public void OpenAt(Vector2 screenPosition);    // 屏幕像素（同 PointerEventData.position）
    public void Toggle();                          // 以 on= 的触发钮；没有 on= 时抛 InvalidOperationException
    public void Toggle(IControl anchor);           // 开着且锚点就是它 → 关；否则以它打开
    public void Close();

    public Observable<Unit> OnOpened { get; }                  // 打开开始：已量好、定好位
    public Observable<PopoverCloseReason> OnClosed { get; }    // 关闭开始，带原因（§4.4）
}

public enum PopoverCloseReason { Code, Trigger, Escape, OutsidePress, Covered, AnchorLost, Replaced, HostGone }
```

- **`Open(anchor)` 在已开着时**：锚点相同，什么都不做；锚点不同，先关（`Code`），再以新锚点打开。
- **参数错误**：`anchor` 为 null 时抛 `ArgumentNullException`；锚点不在同一个 Screen 里、或在浮层自己里面时，抛 `ArgumentException`。
- **打不开**的情况同 §4.3，什么都不做；调用之后看 `IsOpen`。
- **`Hidden`**：读到的是 `!IsOpen`；写 `true` 等于 `Close()`；写 `false` 什么都不做（打开要有锚点）。XML 的 `hidden` 被忽略（§4.2）。为此 `Control.Hidden` 改为 `virtual`。
- **`Dispose`**：开着时先发 `OnClosed(HostGone)`，再释放各个事件流。
- **重入**：`OnClosed` 的回调里打开别的浮层、或在 `Replaced` 的回调里调 `Close()`，链上都不会留下僵尸项。
- **取面板里的控件**：照常按 id 取。浮层不是 id 作用域，`screen.Get<Btn>("menuDelete")` 直接就能拿到。

### 4.12 `InputField.Insert`

```csharp
public bool Insert(string text);   // 插到光标处；放不下或正在组字时返回 false，什么都不做
```

- **正在编辑**：替换当前选区；没有选区就插在光标处。插完，光标落在插入的文字之后。
- **不在编辑**：
  - 用上次结束编辑时的光标与选区。编辑期间每帧记下这两样，失焦那一刻再记一次（算上 TMP 失焦时追加的组字串），不依赖 TMP 失焦后是否保留它们；
  - 从没编辑过，就接在末尾；
  - 文字被代码改短了，就夹到末尾。
- **遵守 `characterLimit`**：放不下就整段不插。原因有两个：TMP 的程序写入不检查上限（ugui 2.x 的 `TMP_InputField.SetText` 里那段检查被注释掉了）；截断还可能切开 emoji 的代理对。
- **算代码写入**：`OnValueChanged` 照发，`OnInput` 不发，候选列表也不武装。
- **不改焦点**：编辑中就继续编辑；不在编辑就保持不在编辑，不重新激活，所以手机上不会弹出键盘。
- **正在组字**（输入法有未提交的组字串）、或输入框只读：返回 false。
- **空串或 null**：什么都不做，返回 true。

## 5. 内核改动

### 5.1 `PopupCore`

- **两种定位方式**，由宿主每次给的 `PopupLayout` 决定，不新增构造参数：
  - 不带画布矩形：今天的做法，anchor 写在宿主的角上。TabMenu / Suggestions 照旧，调用与测试都不动。
  - 带画布矩形（`PopupLayout.Free(rect, side, align)`，Popover 用）：
    - 面板的父节点可以是任意节点；
    - 把画布空间的目标矩形换到父节点的本地坐标，`anchoredPosition` = 父节点本地的 pivot 点 − 父节点 rect 的中心；
    - `anchorMin = anchorMax = (0.5, 0.5)`，pivot 按侧与对齐取；
    - 不写 sizeDelta，尺寸归宿主的 fitter（§5.8）。
- **过渡改补间一个位移分量**：每帧写 `rest + offset`，`Reposition` 只更新 `rest`。今天补间直接占着 `anchoredPosition`（`PopupCore.cs:276-282`），过渡期间的任何 `Reposition` 都会被它盖掉、面板停在旧位置；候选列表在打开过渡里推送时同样中招。滑入方向按侧决定。
- **关闭即不挡指针**：关闭时 `blocksRaycasts = false`，打开时恢复。淡出中的面板不再接住按下，按在一个正在淡出的子层上也不会被当成「外面」。
- **根 Canvas**：宿主就是面板时，从面板的父节点开始找；否则会先找到面板自己的嵌套 Canvas，算出错误的根序，遮罩也会挂到面板下面。
- **外部按下**：`PopupDismiss` 新增 `PassThrough`（不建遮罩）。`Dismiss` 可写，关着时改，下次打开生效。
- **圈闭**：
  - 改为每次打开时判定，三个条件同时成立才圈：构造参数 `cageFocus` 为真；导航已启用；面板里有可聚焦项（`FirstFocusableUnder` 非空）。TabMenu 的面板有行时，行为不变。
  - 圈住时，面板里的活 Selectable（包括 BindItems 建出的行）也转成圈在面板内的显式导航。今天 `ExplicitNavigationResolver` 只转换 `_nodeMap` 里的节点，表情格会从面板边缘走出去，再被圈闭拉回第一格。
- **还原**：
  - 还原目标由宿主给（回调）；不给时同今天，还原到宿主。永远不还原到正在关的面板。
  - 还原条件不变：选中项在面板里才挪。另加一个开关：不圈时也做这一步（Popover 打开；TabMenu / Suggestions 不开，行为不变）。
  - Popover 的取法：锚点可选、且关闭原因不是 `AnchorLost` / `HostGone` → 锚点；否则 → 打开时的选中项；都不行 → 清空。锚点可能已经被回收去显示别的项，所以这两个原因不还原到它。
- **链需要的新信息**：锚点、遮罩、`Dismiss`、打开的那一帧（`OpenFrame`）。
- **面板守卫** `PopupPanelGuard`：挂在面板上，`[DefaultExecutionOrder(-1000)]`，只在 Play 模式干活：
  - `Update`：驱动外部按下探测（§5.4）；
  - `LateUpdate`：回调宿主，即 Popover 的跟随（§4.6）；
  - `OnDisable`：排一次下一帧的清理（§5.2）。

### 5.2 `PopupChain`

- **`IPopupEntry` 新增成员**：`Transform Anchor`（没有锚点时为 null）、`GameObject Blocker`、`PopupDismiss Dismiss`、`int OpenFrame`。
- **祖先判定**：宿主**或锚点**在对方面板子树里（§4.9）。
- **清理 `Prune()`**（修 §1.2-5）：
  - 面板为 null、或不在激活层级里的项，按 `HostGone` 关掉。
  - 每个查询点之前先跑一遍：`Push`、`Top`、`EscapeChain` 的判定、`DismissBelow` / `DismissUnder`、外部按下探测。
  - 面板被停用时，守卫在 Play 模式下用 `Awaitable` 排一次下一帧的清理，让 `OnCollapsed` / `OnClosed` 及时发出。
  - 不在 `OnDisable` 里直接关：那时 Unity 正在停用这棵层级，关闭路径与 `Closed` 的订阅者再 `SetActive` 会被拒绝（「already being activated or deactivated」）。
- **`DismissUnder(root, reason)`**：宿主或锚点在 `root` 之下就关。Screen 关闭时传 `HostGone`（默认值）；ScrollList 的行钩子传 `AnchorLost`（§5.5）。
- **新的关闭原因**：`AnchorLost`；`Replaced`（`Push` 关掉不相干项时用，今天归 `Owner`）。TabMenu / Suggestions 的关闭回调不按 `Owner` 分支，行为不变。
- **外部按下的判定** `OutsideCloseFrom(hit, pressFrame)`：纯函数，见 §5.4。
- **重入**：关闭回调里打开别的浮层、`Replaced` 的回调里调 `Close()`，都不能留下僵尸项。
- **深度警告**（§4.9）：`Push` 之后，宿主是路由页、且 `2 + 2k ≥ PageSortingOrderStep` 时，警告一次。为此新增内部的 `UI.Router.IsRoutedPage(Screen)`。

### 5.3 `PopupPlacer.SolveSide`

```csharp
internal enum PopupSide { Top, Bottom, Left, Right }
internal enum PopupAlign { Start, Center, End }

internal readonly struct SidePlacement { public readonly Rect Panel; public readonly PopupSide Side; }   // 画布空间
internal static SidePlacement SolveSide(Rect anchor, Vector2 panel, Rect bounds, float gap,
                                        PopupSide prefer, PopupAlign align, PopupSide? locked = null);
```

- **选侧**：
  - 未锁定：偏好侧放不下（`panel + gap > 该侧空间`）、且对侧更宽裕时，翻到对侧；
  - 锁定：只用锁定的侧。
- **交叉轴**：先按 `align` 对齐，再夹进 `bounds`，起始边优先。
- **主轴**：
  - 放得下，就贴着锚点；
  - 放不下，就推回 `bounds` 内，可能盖住锚点；
  - 比 `bounds` 还大，起始边优先。
- 仍是纯函数，同今天的 `Solve`。`Solve` 原样保留。

### 5.4 外部按下探测

- **判定**是 `PopupChain` 上的纯函数 `OutsideCloseFrom(Transform hit, int pressFrame)`，可以用假条目单测：
  1. 从链顶往下，找第一个「包含」`hit` 的项：`hit` 在它的面板子树里、在它的锚点子树里，或者就是它的遮罩。`hit` 为 null（按在空处）时，谁都不包含。
  2. 在这一项之上（没找到就从链底开始），找最低的 `PassThrough` 项：
     - 它是在 `pressFrame` 那一帧或之后才打开的，不关，免得打开浮层的那一下又把它关掉（比如在按下回调里调了 `OpenAt`）；
     - 否则把它连同其上的全部关掉（`OutsidePress`）。
  3. `Owner` / `Blocker` 项不由探测关：遮罩自己会关它们；它们只会随链被连带关掉。
- **读按下**（`OutsidePressProbe`）：
  - Input System：在 `InputSystem.onAfterUpdate` 里锁存按下（跳过编辑器更新）：鼠标三个键、笔尖、每个触点的 `wasPressedThisFrame` 与位置。Fixed / Manual 更新模式下 `wasPressedThisFrame` 和帧对不上，锁存才稳。
  - 旧输入（`#elif ENABLE_LEGACY_INPUT_MANAGER`）：`GetMouseButtonDown(0..2)`，以及 `touches` 里 `phase == Began` 的触点。只开 Input System 的工程里旧 `Input` 会抛错，所以两段是互斥的编译分支。
  - 每个按下：`EventSystem.RaycastAll`，取最上面的结果，交给判定。
- **时机**：守卫带 `[DefaultExecutionOrder(-1000)]`，判定在 EventSystem 处理这次按下之前做完；关闭中的面板已经不挡指针（§5.1），所以这一下照常落到下面。按批次去重（EditMode 测试里帧号不前进）。
- **不探测的时候**：
  - 链上没有 `PassThrough` 项；
  - 教程拦截输入（同 Esc 链第 1 步）；
  - 没有 EventSystem；
  - 有展开着的 `<Dropdown>` 列表：它挂在根 Canvas 下，在列表里选一项会被当成「外面」，浮层一关，这次选择就丢了。
- **测试注入**：`PressesForTests`（位置 + 帧号 + 批次号）、`HitForTests`（绕过射线）、`TickForTests()`。真射线只在 PlayMode 测。
- 不吃掉任何事件。

### 5.5 `<ScrollList>`：行换了项就通知

- **何时通知**：下面三处调 `PopupChain.DismissUnder(行, AnchorLost)`：
  - 行退役（`Retire`）：挪进池子与 `Dispose` 之前；
  - 行停进池子（`Park`）：`ReleaseSubscriptions` 之前；
  - 虚拟化窗口里，离开窗口的行直接交给新进来的项（`TakeRow` 的 `_leaving` 分支）。这一行一直保持激活，「锚点失活」抓不到它。
- **为什么只挂这三处**：
  - 非虚拟列表里，复用的行永远显示同一项（同一个 key，或同一个下标）——前提是拖动排序之后快照也跟着移（下一条）；
  - 虚拟列表的推送也按 key / 下标把行映射过去（`RemapRealized`）；
  - 推送会重绑每一行，同一项的重绑路径（`BindRow`、`Rebuild` 的绑定循环、`ReleaseSubscriptions`）一概不挂。
- **拖动排序**：`PermuteSlots` 同步移动绑定快照（新增 `IItemBinding.Move(from, to)`）。今天带 key 的列表拖动排序之后再推送，被拖的行会和邻行互换内容：标签看着对，行身份错了。
- **重入**：通知包在一个辅助函数里。链为空就直接返回；否则在内部保存并设置 `_inSync = true`，调完再恢复。浮层的关闭回调里再推送同一个列表时，推送会排队，不会在迭代 `_keep` / `_slots` 的过程中重入。`VirtualPush` 的 `ClearSlots` 与 `RemapRealized → Park` 本来就在 `_inSync` 之外执行，所以保存与恢复放在辅助函数里面。

### 5.6 id 解析

- `TriggerSourceResolver.ResolveId` 的参数从 `Trigger` 放宽为 `Control`，再加一个可选的标签名，用在报错消息里；`<Trigger>` 既有的消息不变。
- 新增 `FindTriggerBtn`：跳过浮层自己的子树，并要求找到的是 `<Btn>`。
- **推迟到实例化收尾**（§4.3）：
  - Screen 打开时：走 `DeferDuringOpen`。整屏先全部实例化、再统一应用属性，前向引用本来就成立。
  - 动态子树：新增 `ScreenInstantiator.DeferAfterBuild`（深度计数 + try/finally），在 `InstantiateNode`（`ReplaceScopedIds` 之后）与内联路径的 `ApplyAddBlock` 结束时冲刷。
  - 适用对象：Popover 的 `on=`，以及 `<Trigger>` / `<Animation>` / `<Show>` 的 `@id`。今天动态行是逐节点立刻应用属性的，行里引用后面兄弟的 `@id` 会失败。表情布局正好会撞上：触发钮里的 `<Show on="expand@pop">` 写在 Popover 前面。

### 5.7 `<Show>` 认 `expand` / `collapse`

- 走源头登记，同 `checked`（`IToggleSource.RegisterCheckedShow`）：新增 `IExpandable.RegisterExpandShow(bool want, Action reevaluate)`。
  - `<Collapsible>` 在定初态、手风琴裁决（`AdjudicateGroup`）、展开、收起时重算；
  - `<TabMenu>` / `<Popover>` 在开、关时重算。
- 为什么不只订阅事件：
  - `<Collapsible>` 在自己的 `OnAfterApply` 里静默定初态（`Collapsible.cs:658-663`）；
  - 手风琴的裁决排在 Show 之后，静默翻转；
  - 动态行里什么都不推迟。
  - 只订阅 `OnExpanded` / `OnCollapsed`，这三种情况都会错。
- 不带 `@id` 时用 `FindExpandable` 向上找源；它的报错消息里加上 `<Popover>`。

### 5.8 `Control`：几何归自己管的控件

- **新增** `protected internal virtual bool OwnsGeometry => false`。
- **为真时，`ApplyCommon`**：
  - 解析 `SizeSpec`，交给 `ApplyOwnSize(SizeSpec)`，由控件存下并校验；
  - 照常写 `interactable`；
  - 不解析 `anchor` / `pivot` / `margin`，免得被忽略的属性在解析时抛错；
  - 不应用 `hidden`：否则 `hidden.portrait` 没有基值时，转个屏浮层就会被关掉；
  - 父节点是布局组时，只挂 `ignoreLayout`；其余几何一概不写，也不挂 `ClampFitter` / `HugElement`。
- `Screen.ApplyScaleToNodeCore` 跳过这类控件，否则 scale 与盒子补偿会写到面板上。
- `Control.Hidden` 改为 `virtual`（Popover 覆写，§4.11）。
- **Popover 的尺寸**：
  - 面板上的 `PopoverFitter`（`ILayoutSelfController`，时机同 `ContentSizeFitter`）在布局阶段读面板 VLG 的偏好尺寸，按 hug / 数值 / 上下限写 `sizeDelta`。
  - 开着时，只在 `SetLayoutVertical` 里、且尺寸真的变了时，才请内核重新夹边。`Open()` 内部那次 `ForceUpdateCanvases`（圈闭时的导航转换会触发）里只改尺寸，不重入定位。
  - 位置由 `PopupCore` 写。

### 5.9 `PuiInputField` / `InputField`

- **光标记录**从两处取：
  - `PuiInputField.LateUpdate`：编辑期间每帧记 `stringPosition` 与选区两端；
  - 覆写的 `OnDeselect`：在调 base 之前记，并把 base 将要追加的组字串长度算进去。
- **`InputField.Insert`**（§4.12）：
  - 「在不在编辑」看 `InputField.IsEditing`；
  - 写入走 `TextValue` 的程序写入路径（`_programmaticWrite`）；
  - 夹到文字长度由自己做（Editor 非 Play 时 TMP 不夹）；
  - 编辑中写完，把光标与选区设到插入的文字之后；不在编辑时，把记下的光标挪过去。

## 6. 图层结构

```
<声明处的父节点>
└─ Popover            (= 面板：Image（程序化表面）+ VerticalLayoutGroup + PopoverFitter
   │                     + PopupCore 的 Canvas / GraphicRaycaster / CanvasGroup
   │                     + PanelSink + PopupPanelGuard + ExpandableMarker
   │                     + 开着时：ModalEscapeListener)
   ├─ <作者的子元素>    (按 VStack 排；flow="false" 的自由定位)
   └─ <Decor> …
```

- **VLG 挂在面板自己身上**，`ChildHostTransform` 就是面板：
  - 程序化表面的节点自带 `LayoutElement.ignoreLayout`（`ProceduralSurface.cs:233`），`<Decor>` 不参与布局，所以都不会被当成一行；
  - 面板是布局根：内容一变，fitter 就在同一次重排里跟着跑。VLG 若挂在子节点上，`LayoutRebuilder.MarkLayoutForRebuild` 停在子节点，父节点上的 fitter 永远轮不到；
  - `<Add into="#pop">` 也正好落进面板。
- **`PanelSink`**：
  - 接住面板空白处的按下、点击、拖动与滚轮，不让它们冒泡到声明处的祖先。所以浮层写在列表行里时，在面板上拖动不会拖动下面的列表；写在 `<Btn>` 里时，点面板不会点到那个 Btn。
  - 它是 `PointerSink` 的扩展版；`<Suggestions>` 用的那个不动。
- **关着时整个面板停用**（TM-D12）：
  - Screen 打开的测量遍结束后才停用，内容先量一次；
  - 关着时改的内容，在下次打开时再量。面板里的虚拟化列表要等显示出来才建行，所以面板会在打开后的下一帧再调一次尺寸，淡入基本盖得住。

## 7. 报错

新规则在 `Core/Lint/PopoverRules.cs`，其余是既有规则的扩展。

| Code | 触发 | 运行期 |
|---|---|---|
| `PUI-POPOVER-ON` | `on=` 不是 `click@<id>` / `manual` | 抛 `ArgumentException` |
| `PUI-POPOVER-ENUM` | `placement` / `dismiss`（基值或任一变体）不在 §4.2 的表里 | 抛 `ArgumentException`（同 `PUI-SUGGEST-ENUM`） |
| `PUI-POPOVER-VALUE` | `gap` < 0 或不是数；`transition` 解析不了 | 警告，用默认值（`gap` 是 string setter：`float` 型属性写成非数时，`ControlMeta` 直接抛错，到不了 setter） |
| `PUI-POPOVER-SIZE` | `width` / `height` / `size` 是 `stretch` / `%` / `native`（含 `clamp(_, stretch, …)`）。CLI 里 `SizeSpec` 不可用，按字符串匹配，同 `HugRules.IsHugValue` | 抛 `ArgumentException` |
| `PUI-POPOVER-LAYOUT-ATTR` | 写了 `anchor` / `margin` / `pivot` / `flow` / `scale` / `hidden` | 忽略，警告 |
| `PUI-POPOVER-PARENT` | 是 `<ScrollList>` / `<TabBar>` / `<TabMenu>` / `<Carousel>` / `<Pages>` 的直接子元素（会被当成行、页签、页）；或写在 `<Btn>` / `<Tab>` / `<Toggle>` 里面（面板会继承它们的悬停、按下、置灰视觉） | 只在 CLI 报 |
| `PUI-POPOVER-HUG-WIDTH` | 宽度 hug，而每个子元素都是 `width="stretch"`：面板宽为 0 | 只在 CLI 报 |
| `PUI-SHOW-EXPAND-SELF` | 不带 `@id` 的 `<Show on="expand">` / `collapse` 落在自己源的面板里：在 `<Popover>` / `<TabMenu>` 里，或在 `<Collapsible>` 的 `<Header>` 之外。按「最近的源」判断：做成自带祖先状态的整屏遍历（照 `CooldownRules.CheckSources`），状态在每个源处覆盖；模板调用处复位，交给展开遍。`<TabMenu>` 的 `itemTemplate` 行两遍都看不到 | 只在 CLI 报 |
| `PUI-HUG-TAG` | （扩展）`HugRules.HugTags` 加 `Popover` | — |
| `PUI-HUG-STRETCH-CHILD` | （扩展）`<Popover>` 按 `<VStack>` 的主轴（height）查；不写 height 也视同 hug | — |
| `PUI-HUG-SCALE` / `PUI-CLAMP-SCALE` | （扩展）对 `<Popover>` 豁免：它的 `scale` 本来就被忽略，只报 `PUI-POPOVER-LAYOUT-ATTR` | — |
| `PUI-PROC-*` | （扩展）`ProceduralSurfaceRules.SurfaceTags` 加 `Popover` | — |
| `PUI-PADDING-INT` | （扩展）`PaddingRules` 加 `Popover` | — |
| `PUI-RAYCAST-TAG` / `PUI-RAYCAST-UNDECIDED` | （扩展）`RaycastRules.InteractiveTags` 加 `Popover`：面板永远接住指针 | — |
| `PUI-EXPAND-NO-SOURCE` | （扩展）`StateTriggerRules.MenuSourceTags` 加 `Popover`；规则与运行期的消息里都加上它 | — |

要一起改的其他地方：
- `BuiltinTags` 加 `Popover`（`BuiltinTagsTests` 拿它和已注册的标签比对）；
- `IRWalker` 分派 `PopoverRules`；
- 两处「父节点是布局组」的硬编码表加 `Popover`：`IRWalker` 的子循环与 `ScreenInstantiator` 的 `selfIsLayoutGroup`。Popover 的子元素因此按布局组子节点检查；
- 父节点一侧对 `<Popover>` 子节点的布局检查一律跳过，Popover 节点自身也跳过：`LayoutGroupChildRules.CheckChild` / `CheckNonLayoutChild`、`MarginAnchorRules`、Grid 子尺寸。浮层不在任何流里，这些问题只由 `PUI-POPOVER-*` 报；
- `ScreenInstantiator` 做运行期镜像，只镜像 `PUI-POPOVER-LAYOUT-ATTR`；`ON` / `ENUM` / `VALUE` / `SIZE` 由 setter 自己抛错或警告；
- `.lint/UIXmlLint/README.md` 的规则表，以及 `PUI-HUG-TAG` / `PUI-RAYCAST-TAG` / `PUI-PADDING-INT` 三行里的标签列表。

不进 lint 的：
- 触发钮的 id 解析不到：同 `<Trigger>` 的 `@id`，由运行期报错。
- XSD：反射生成，注册即出现。

## 8. 实现地图

| 处 | 改动 |
|---|---|
| `Controls/Popover.cs`（新） | §4 的全部：属性、`on=` 的推迟解析与订阅、开合 API、跟随与锚点失效、`IExpandable`、关闭原因的映射 |
| `Controls/PopoverCloseReason.cs`（新） | 公开枚举 |
| `Controls/Internal/PopoverFitter.cs`（新） | §5.8 的尺寸 |
| `Controls/Internal/PanelSink.cs`（新） | §6 |
| `Controls/Internal/PopupPanelGuard.cs`（新） | §5.1 的帧驱动与下一帧清理 |
| `Controls/Internal/OutsidePressProbe.cs`（新） | §5.4 |
| `Controls/Internal/PopupCore.cs` | §5.1 |
| `Controls/Internal/PopupChain.cs` | §5.2 |
| `Controls/Internal/PopupPlacer.cs` | §5.3 |
| `Controls/Internal/TriggerSourceResolver.cs`、`Controls/Trigger.cs` | §5.6；`FindExpandable` 的报错消息 |
| `Controls/Internal/IExpandable.cs`、`Controls/Collapsible.cs`、`Controls/TabMenu.cs`、`Controls/Show.cs` | §5.7 |
| `Controls/Control.cs`、`Application/Screen.cs` | §5.8 的 `OwnsGeometry`、`virtual Hidden`、scale 跳过 |
| `Controls/ScrollList.cs`、`Controls/Internal/ItemBinding.cs` | §5.5 |
| `Controls/InputField.cs`、`Controls/Internal/PuiInputField.cs` | §5.9 |
| `Application/Navigation/ExplicitNavigationResolver.cs` | §5.1 的活 Selectable 圈闭 |
| `Application/ScreenInstantiator.cs` | `DeferAfterBuild`；布局组表；父侧跳过；运行期 lint 镜像 |
| `Application/EscapeChain.cs` | 判定前先 `Prune` |
| `Application/UI.Router*.cs` | 内部的 `IsRoutedPage(Screen)` |
| `Application/BuiltinPrimitives.cs` | 注册 `Popover` |
| `Application/UI.cs` | `ResetForTests` 复位探测、告警集合、收尾队列、待清理旗标 |
| `Core/Lint/*` | §7 |

## 9. 测试（Red 先行）

先写 Red，再实现。纯函数和 EditMode 覆盖得了的放 EditMode；要真指针、真手柄、真帧的放 PlayMode。EditMode 的约束：没有 `Update` / `LateUpdate` / LitMotion；`Time.frameCount` 不前进；Overlay Canvas 没有确定尺寸（宿主写 `anchor='center'`，配合 `PopupBounds.BoundsOverride`）；测试程序集不引用 Input System。

**内核**
- `PopupPlacerSideTests`（纯函数）：四个侧 × 三种对齐；只在有用时翻面；交叉轴夹边、起始边优先；两侧都放不下时推回边界；面板比边界还大；锁定的侧不翻；零尺寸锚点；`placement` 的解析。
- `PopupCoreFreeTests`：自由框落在给定的画布矩形上（父节点平移、pivot 不居中、带缩放）；pivot 跟侧与对齐；不写 sizeDelta；过渡期间重新定位落在新位置；关闭即不挡指针；宿主就是面板时根 Canvas 找对；`PassThrough` 不建遮罩；没有可聚焦项不圈；不圈时也把面板里的选中项挪走。
- `PopupHostDisabledTests`（**Red**）：模态里的 TabMenu 展开时停用它的祖先 → 下一次 Esc 落到模态（§1.2-5）；再开一个浮层时清掉藏起来的那个。
- `PopupChainTests`（补用例）：锚点在别的面板里 = 子层；`Push` 按 `Replaced` 关不相干的项；`DismissUnder` 认锚点；`OutsideCloseFrom` 的每一种情况；关闭回调里开关别的浮层不留僵尸；路由页上越界时警告一次。
- `OutsidePressProbeTests`（注入按下与命中）：按在外面 → 关；按在面板里、锚点上 → 不关；打开那一帧的按下不算；教程拦截时不算；`<Dropdown>` 列表展开时不探测；`Owner` / `Blocker` 项不被探测关；同一批次不判两次。

**控件**
- `ControlOwnsGeometryTests`：不写几何；被忽略的属性不解析；尺寸交给 `ApplyOwnSize`；在布局组里只挂 `ignoreLayout`；照写 `interactable`，不写 `hidden` 与 `scale`；`Hidden` 可覆写。
- `PopoverSizeTests`：注册；面板就是表面、默认皮肤；放在 `<VStack>` 里不占位；hug / 数值 / clamp；表面节点与 `<Decor>` 不进栈；里面放 clamp-hug 的 `<ScrollList>`；开着时内容变化 → 调尺寸并重新夹边；什么都没变的 ReSolve 不弄脏布局；属性的非法值与回退。
- `PopoverOpenCloseTests` / `PopoverCloseReasonTests`：`on="click@…"` 切换；触发钮写在前、写在后、在同一个行模板里；触发钮在浮层里或不是 `<Btn>` 报错；变体改写 `on`；`Open(anchor)` 换锚点；`OpenAt`；`Toggle(anchor)`；打不开的各种情况（含被模态盖住）；`Hidden` 与 `Interactable`；`Dispose` 报 `HostGone`；`expand` / `collapse` 触发器；§4.4 表里的每个关闭原因。
- `PopoverFollowTests`（`TickForTests()` 驱动）：跟随移动的锚点；定侧锁定；Canvas 尺寸变了才重选侧；软键盘只重新夹边；锚点被销毁 / 停用 / 不可交互 / 滚出视口 → `AnchorLost`；锚在另一个浮层里时不被背后的列表判为裁掉；`AnchorLost` 不还原到锚点；`OpenAt` 没有锚点可失。
- `PopoverFocusTests`：圈住并落到第一个可聚焦项；BindItems 的格子也圈在面板里；没有可聚焦项时不圈；关后回到锚点；不圈时面板里的选中项也挪回；`OpenAt` 还原到打开前的选中项；焦点已经在别处就不动。
- `PopoverNestingTests`：浮层里开浮层（层序 +4）；Esc 逐层关；不相干的菜单关掉整条链；从浮层里 `OpenAt` 一个声明在外面的浮层会替换它；浮层里带候选列表的输入框；按在父层里只关子层；淡出中的子层不算「外面」。
- `DynamicIdResolutionTests`（**Red**）：行模板里 `<Show on="checked@t">` 写在 `<Toggle id="t">` 前面；ReSolve 激活的 `<Add>` 块；解析错误仍在这次构建结束时报出。
- `ShowExpandTests`：`<Show on="expand@pop">` / `collapse@pop` 跟着 Popover 与 TabMenu；`<Collapsible expanded="true">` 的 `<Header>` 里的块在打开时就对；手风琴裁决后的初态；行模板里的折叠块从第一帧就对。
- `ScrollListPopoverAnchorTests`：带 key 推送同一项 → 不关；位置复用推送 → 不关；推送去掉那一项 → `AnchorLost`；空推送 → 关；虚拟化滚动把行交给另一项 → 关；关闭回调里再推送同一个列表不抛。
- `ScrollListReorderTests`（补用例，**Red**）：带 key 的列表拖动排序后推送，被拖的行仍显示原来那一项。

**输入框、lint、XSD**
- `InputFieldInsertTests`：从没编辑过接在末尾；用上次的光标；替换记下的选区；被代码改短后夹到末尾；编辑中替换实时选区；超上限不插；只读拒绝；组字中拒绝；空串成功且不改；`OnValueChanged` 发一次。
- `PopoverRulesTests`：每条新规则正 / 反例；§7 的各项扩展；父侧检查跳过；`PUI-SHOW-EXPAND-SELF` 的「最近的源」；运行期镜像的警告。
- `XsdGeneratorTests`：`Popover_lists_its_attributes`（断言 `name="Popover"` 与 `name="dismiss"`——`placement` / `gap` / `on` 别的标签上已经有了），再校验一份含 Popover 的样例。

**PlayMode**
- `PopoverPlayTests`：过渡从锚点一侧滑出；滑入期间跟随也落在锚点旁；`LateUpdate` 里跟着滚动的列表走；藏掉祖先后下一帧被清理；编辑中 `Insert` 替换实时选区且继续编辑；`Insert` 不发 `OnInput`；点浮层里的 `<Btn>` 时在失焦那一刻记下光标。
- `PopoverDevicePlayTests`（`InputTestFixture`）：真鼠标按在外面 → 关，且这一下落到了下面的 `<Btn>`；再按触发钮 → 关；按在面板上 → 不关；触屏按在外面 → 关；在按下回调里 `OpenAt` 不会把自己关掉；`block` 模式吃掉这一下；在浮层里的 `<Dropdown>` 里选一项，浮层不关；手柄 B 与键盘 Esc 关闭、焦点回到触发钮；方向键在圈住的网格里走不出去。

## 10. 演示同步（同 PR）

CommonControls 的聊天页加一行输入：输入框 + 颜文字钮 + 发送钮。

- 颜文字浮层写 `on="click@…"`、`placement="top-end"`，里面是一格格 ASCII 颜文字（`:)` `:D` `^_^` …，字体里都有）。点一格，调 `Insert`。
- 发送：把这句加进消息流。
- 两套皮各给浮层和格子一份样式。
- 触发钮用 `<Show on="expand@…">` 显示「开着」。

## 11. SKILL / 文档更新（同 PR，英文）

- **新 `reference/controls-popover.md`**：
  - 属性、开合、关闭表、定位、跟随与锚点失效、尺寸、焦点、嵌套、`expand` / `<Show>`、`PUI-POPOVER-*`；
  - 三个配方：表情、行菜单、右键菜单。
- **XML `SKILL.md`**：
  - 标签目录加一行；`### <Popover>` 存根，指向 reference；
  - hug 标签表、程序化表面表、raycast 交互标签表都加 `Popover`；
  - `<Show>` 的 `on=` 列表加 `expand` / `collapse`。
- **`reference/animations.md`**：`expand` / `collapse` 那两行的源加 `<Popover>`；写明 `<Show>` 认这两个持久态。
- **`reference/states.md`**：`<Show>` 的合法 `on=` 加 `expand` / `collapse`（顺手补上漏列的 `ready` / `cooling`）。
- **`reference/navigation.md`**：浮层的圈闭与 B。
- **`.lint/UIXmlLint/README.md`**：新规则的行；三行标签列表加 `Popover`。
- **C# `SKILL.md`**：
  - 新小节 Popover：API、关闭原因、一个浮层服务多行、`OpenAt`；
  - `InputField.Insert`；
  - 速查表。
- **`AGENTS.md`**：reference 列表与路由加一条：`<Popover>` → `reference/controls-popover.md`；`InputField.Insert` 同时改 C# skill。
- **主 spec**：§5 控件表加 `<Popover>` 行；raycast 交互标签列表也加上。
- **`2026-10-18-popup-core-and-input-suggestions-design.md`**：§12 的第一条改成指向本文。

## 12. 不做的事

- 悬停提示（Tooltip，§2-G）。
- 长按打开：`press` 只有按下那一瞬。
- 指向锚点的小尖角；与锚点同宽。
- 「焦点离开即关」：圈闭时焦点出不去；不圈时，选中项本来就不在面板里。
- 「点内容即关」的声明式写法。
- 把 `<TabMenu>` / `<Dropdown>` / `<Suggestions>` 迁到 `<Popover>` 上（PC-D1 / PC-D10 不变）。
- 改路由页的步长下限（§4.9）。
- 锚点被别的东西盖住（而不是被裁切）时关闭。

## 13. 决策记录（2026-10-19 与作者对齐，按推荐已定）

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| PO-D1 | 形态 | 独立元素，声明在任意处，经 `on=` / C# 指向锚点 | §2-A；一个浮层服务多个锚点 |
| PO-D2 | 开合 | `on="click@<id>"` 切换 / `manual`（默认）；锚点默认是触发钮 | 复用 `<Trigger>` 的语法与词法 id |
| PO-D3 | 外部按下 | 默认 `pass`：关掉，这一下照常落下；`block` 用遮罩吃掉 | §2-B；HTML popover 与手机聊天的习惯 |
| PO-D4 | 点内容 | 不自动关 | 表情要连点；菜单在 C# 里关 |
| PO-D5 | 焦点 | 导航启用且有可聚焦项才圈；关闭时选中项在面板里就挪回锚点 | 同 TM-D15 / PC-D6；纯信息浮层不该挪焦点 |
| PO-D6 | 输入框 | 不对抗失焦；加 `InputField.Insert` | §2-C |
| PO-D7 | 定位 | `placement` = 侧[-对齐]，默认 `bottom-start`；翻面同 `PopupPlacer`；两侧都放不下时推回边界内；开着时锁定侧，边界尺寸变了才重选 | 扩展今天的规则；SG-D12 |
| PO-D8 | 边界 | 安全区，扣掉可见的软键盘 | 同 SG-D13 |
| PO-D9 | 跟随 | 每帧跟锚点；锚点失效即关（含行换了项） | 列表行的菜单不能指着别的项 |
| PO-D10 | 尺寸 | 面板按 VStack 排；默认 hug，可写数值 / clamp | §2-E |
| PO-D11 | 嵌套与层序 | 宿主或锚点在对方面板里 = 子层；每层 +2 不变；路由页上越界时警告一次 | 不改步长下限，向后兼容 |
| PO-D12 | `expand` 源与 `<Show>` | Popover 是 `IExpandable`；`<Show>` 认 `expand` / `collapse` 持久态 | 触发钮要显示「开着」；TabMenu / Collapsible 同样受益 |
| PO-D13 | 关闭原因 | 公开枚举，`OnClosed` 带出 | 菜单要分清「取消」与「选完关」 |
| PO-D14 | 宿主被停用 | 内核按 `HostGone` 关闭，TabMenu 一并受益 | §1.2-5 |
| PO-D15 | 触发钮的解析时机 | 实例化完成之后 | 触发钮写在前、写在后都行 |

计划期修正与审阅结论（P1–P26，见 `plans/2026-10-19-popover.md`）已写回 §4–§9、§14，不另列决策号。

## 14. 里程碑拆分

| M | 内容 | 依赖 |
|---|---|---|
| M0 | 几何与定位（风险最高，先打通）：`PopupPlacer.SolveSide`；`Control.OwnsGeometry` / `virtual Hidden` / scale 跳过；`PopupCore` 的自由框、位移分量过渡、不挡指针、根 Canvas、还原目标、逐次判定的圈闭；Popover 骨架与 `PopoverFitter` | — |
| M1 | 链与生命周期：`Prune`（§1.2-5 先写 Red）、锚点祖先、新原因、`DismissUnder` 认锚点、`OutsideCloseFrom`、深度告警；`OutsidePressProbe` | M0 |
| M2 | 控件 API：动态子树里推迟 `@id`（Red 先行）；开合 API、`on=`、关闭原因、`IExpandable`；跟随与锚点失效；焦点（含活 Selectable 的圈闭）；嵌套 | M1 |
| M3 | `<Show>` 的 `expand` / `collapse`（源头登记）；`<ScrollList>` 的行钩子；`PermuteSlots` 同步快照（单独提交） | M2 |
| M4 | `InputField.Insert`（与浮层无依赖，可单独砍掉） | — |
| M5 | lint（`PopoverRules` 与各项扩展、`PUI-SHOW-EXPAND-SELF`）；XSD；PlayMode | M2–M4 |
| M6 | 样例；SKILL；`AGENTS.md`；主 spec 与上一份 spec 加注；全量回归与实施记录 | M5 |

建议一个分支、一个 PR：内核改动向后兼容，其余都是新增；每个里程碑一个或几个提交。

## 15. 风险与回滚

| 风险 | 缓解 |
|---|---|
| 探测读不到按下：自定义输入模块、Input System 跑在 fixed update 里、远程桌面模拟的触屏 | 两套输入都读；测试可注入。读不到时退化为「点外面不关」，Esc / B / 触发钮照常能关；需要时写 `dismiss="block"` |
| 跟随晚一帧：锚点在布局阶段（`LateUpdate` 之后）才移动 | 动画、滚动都发生在 `Update` 里，当帧就能跟上；只有布局引起的移动要等到下一帧 |
| 声明处的父节点带缩放（`scale` / 嵌套 Canvas） | 尺寸按父节点相对画布的缩放换算；skill 建议把浮层写在 Screen 顶层 |
| 嵌套越过下一页 | 只在下层页还能被点到时发生；会警告一次 |
| `<Show>` 认了 `expand` 之后，有人把它写在面板里误用 | `PUI-SHOW-EXPAND-SELF` |
| `OwnsGeometry` 绕开了 `ApplyCommon` 的几何 | 只有 Popover 打开它；尺寸语法仍由 `SizeSpec` 解析与校验 |
| 回滚 | `<Popover>` 与 `Insert` 是纯新增；内核扩展向后兼容，TabMenu / Suggestions 的调用不变；`<Show>` 的两个新值可以单独回退 |

## 16. 实施记录

**先红后绿的复现**（改线之前都红在预期的断言上）：

| 测试 | 改线前的结果 |
|---|---|
| `PopupHostDisabledTests.A_menu_hidden_with_its_ancestor_no_longer_eats_Escape`（§1.2-5） | 祖先被隐藏的 TabMenu 仍在链上，Esc 被它吃掉，模态没关 |
| `DynamicIdResolutionTests`（三条） | 动态行里写在前面的 `<Show on="checked@t">` 找不到后面的 `<Toggle id="t">` |
| `ShowExpandTests`（五条） | `<Show on="expand…">` 解析时报「只接受 state-*」 |
| `ScrollListPopoverAnchorTests`（五条该关的） | 行退役 / 停进池子 / 交给别的项之后，浮层仍开着 |
| `ScrollListReorderTests.Keyed_push_after_a_reorder_keeps_rows_with_their_items` | 被拖的行显示的是邻行的项（P12 的问题成立） |
| `InputFieldInsertTests`（对着桩） | 九条红；只读、组字两条在桩下天然为真 |
| `PopoverRulesTests` | 二十条里十九条红 |

**没能先红的**：下列测试类与实现一起第一次运行，红只体现为编译错误：`ControlOwnsGeometryTests`、`PopupCoreFreeTests`、`PopoverSizeTests`、`OutsidePressProbeTests`、`PopoverOpenCloseTests`、`PopoverCloseReasonTests`、`PopoverFollowTests`、`PopoverFocusTests`（活 Selectable 的圈闭先于测试写好）；`PopoverNestingTests` 按计划预期不需要新代码，写好即绿。

**与设计的偏差 / 补充**：

- **`OwnsGeometry` / `ApplyOwnSize` 是 `internal virtual`**，不是 §5.8 写的 `protected internal`：自定义控件暂时不能选择自管几何。
- **活 Selectable 的圈闭**没有另开入口：`ExplicitNavigationResolver.Resolve` 的圈闭模式在转换完 `_nodeMap` 里的节点之后，把圈闭根下其余活着的 Selectable（`BindItems` 建出的行）也转成圈在根内的显式导航。模态与 TabMenu 走的是同一条路，所以模态里 `BindItems` 的行从此也圈在模态里。
- **外部按下的锁存**：只保留本帧的按下会丢掉「探测跑完之后才锁存」的按下（手动更新、测试里在协程中注入）。现在保留上一帧及之后的按下，更早的丢掉；打开那一帧的按下由 `OpenFrame` 比较排除，规则不变。真设备的 PlayMode 用例暴露了这一点。
- **锁存的订阅**：`ResetForTests` 会退订并清掉「已订阅」旗标。`InputTestFixture` 每个用例都换一套输入系统，否则从第二个用例起探测就读不到按下。
- **`PUI-POPOVER-SIZE` 不管 `native`**：除 `<Icon>` 外写 `native` 在解析期就报错，这条规则只会看到 `stretch` / `%`（含 `clamp` 中项）。
- **`PUI-POPOVER-PARENT` 的「写在 Btn / Tab / Toggle 里」** 用 `IRWalker` 已有的祖先旗标；模板体不知道自己的祖先，所以只在展开遍里看得到。
- **`PUI-SHOW-EXPAND-SELF`** 放在 `StateTriggerRules`，整屏遍历；`<Header>` 不是内置标签，遍历里单独认它。
- **组字判定**同上一份 spec 的阶段 1：取输入模块的 `BaseInput.compositionString`，退回 `Input.compositionString`，抛 `InvalidOperationException` 就当没有；另加测试注入 `CompositionForTests`。
- **编辑中的 `Insert`** 用 `stringPosition` 落光标（它会同步字符位置）；`InputField` 内部对 TMP 组件的引用改成 `PuiInputField` 类型。
- `ScrollListRules.IsDuration` 改为 internal，供 `PopoverRules` 共用。
- **样例**：输入框的占位符与样例里其他占位符一样不进 `.po`；表情钮与表情格写 `tr="false"`。

**验证**：
- EditMode 5625 / EditorOnly 471 / PlayMode 311 全绿。
- `dotnet format --verify-no-changes` 干净。
- UIXmlLint 对 `Runtime/Resources/` 与 `Samples~/` 零问题。一份故意写错的 `.ui.xml` 报出 `ON` / `ENUM`×2 / `VALUE`×2 / `SIZE` / `LAYOUT-ATTR` / `PARENT`×2 / `SHOW-EXPAND-SELF`；`HUG-WIDTH` 由单测覆盖。
- 样例的 C# 与运行时一起在 Unity 外编译通过。一个临时的 EditMode 冒烟（未提交）在两套皮下打开样例的聊天页、点开表情浮层并插入。
- 真设备的 PlayMode 用例（`InputTestFixture`）覆盖：鼠标 / 触屏点外面、再点触发钮、点面板、`OpenAt` 来自按下、`block`、浮层里的 `<Dropdown>`、手柄 B / Esc 与焦点还原、圈住的网格。
- 留待手测：手机真机（点外面、软键盘出现时的位置）、手柄真机、两套皮的观感、列表行里的浮层在拖动面板时不带着列表滚、浮层里 `<Dropdown>` 在真机上选项。
