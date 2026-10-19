# `<Suggestions>`：面板宽度、对齐，与不可选的行

> 状态：**已对齐**（2026-10-19，与作者对齐，按推荐定：`popupWidth` 只收像素；`placement` 加 `-start` / `-end`；不可选行由绑定回调里的 `slot.Interactable = false` 标记）。
> 来源：搜索浮层的接入反馈：右上角的窄输入框里，一行放不下「名字 · 类型 · 坐标」；「没有匹配」只能在提交时用吐司提示。读源码做的评估；复现由 §6 的 Red 测试给出。
> 相关：`2026-10-18-popup-core-and-input-suggestions-design.md`（§5.2 面板与输入框同宽、留待 `popupWidth`；§6.1 显示条件；§6.2 高亮；§6.4 提交表；§6.8 定位；§12 不做「无结果 / 加载中 / 不可选的行」）、`2026-08-29-tabmenu-design.md`（TM-D6 `popupWidth`）。

## 1. 问题

- **面板永远与输入框同宽**（上一份 spec §5.2）。窄的输入框（图标行里展开的搜索框）里，一行放不下要显示的几列。
  - `width` 是通用属性，由 `ApplyCommon` 写进 `sizeDelta`，控件分不清那是作者写的宽还是上次定位留下的宽，所以它被忽略（`PUI-SUGGEST-LAYOUT-ATTR`）。当时留了 `popupWidth` 这个名字（TabMenu 同名）。
- **面板更宽时，只会左对齐输入框，再被推回边界。** `PopupPlacer.Solve` 的水平规则是「左边对齐把手，右边溢出就往回推」。
  - 输入框在屏幕右侧时，更宽的面板被推到右边界：右沿贴的是安全区边，不是输入框的右沿，比输入框多伸出一截（等于输入框到屏幕边的距离）。
- **没有不可选的行。** 「没有匹配」「加载中」这类提示放进列表，就会被高亮、能被点选并提交。
  - 现在只能推一个空列表（列表收起），在提交时另外提示。

## 2. 否决的方案

**A. `popupWidth="hug"`，按行内容测宽。否决。**
- 行模板里的文字多是拉伸加省略号，首选宽就是整串文字的宽，测出来没有上限。
- 要按内容定宽，宽度又要能夹住，需要 `clamp(min, hug, max)` 一整套。先只收像素，与 TabMenu 一致。

**B. 自动决定朝哪边伸（输入框在右半屏就右对齐）。否决。**
- 结果取决于运行时的位置，换个分辨率就变，作者预料不到。写成显式的 `-start` / `-end`。

**C. 给 `BindItems` 加一个 `Func<T,bool> pickable` 参数。否决。**
- 在 `TKey = bool` 时，它会和带 key 的重载 `Func<T,TKey>` 静默撞重载（上一份 spec §2-G 的同一个问题）。
- 再加四个重载，API 太重。

**D. 专门的「空状态」槽（`<Empty>` 子元素）。否决。**
- 宿主推空列表有两种意思：没在搜（应关闭）和没搜到（应提示）。库分不清，宿主得另给信号。
- 不可选行由宿主明确地推一条进来，更直接，也覆盖「加载中」、分组标题。

## 3. 方案总览

1. **`popupWidth`**：面板宽度（像素），缺省 = 输入框宽（§4.1）。
2. **`placement="below-end"`** 等：面板比输入框宽时，`end` 让右沿对齐输入框右沿、往左伸（§4.1）。
3. **不可选行**：绑定回调里 `slot.Interactable = false`。这一行显示 `state-disabled`、跳过高亮、点了不提交（§4.2）。

## 4. 作者面

### 4.1 宽度与对齐

```xml
<InputField id="searchField" width="stretch" height="44" submitIcon="ui:search">
  <Suggestions id="hits" itemTemplate="HitRow" popupWidth="360" placement="below-end"/>
</InputField>
```

| 属性 | 值 | 默认 | 说明 |
|---|---|---|---|
| `popupWidth` | px ≥ 0 | `0` | 面板宽；`0` 或不写 = 输入框宽。比输入框窄也照写（显式胜出，同 TabMenu TM-D6） |
| `placement` | `below` \| `above`，可加 `-start` \| `-end` | `below`（= `below-start`） | 侧仍是偏好，规则不变；对齐是水平方向的：`start` 左沿对齐输入框左沿，`end` 右沿对齐输入框右沿 |

- **超出边界**：先按对齐放，右边溢出就往左推，但左沿不越过边界左沿（今天的夹边，起始边优先）。
  - `end` 时，面板比输入框左边的空间还宽，就从边界左沿起放，右边可能盖不到输入框右沿。
- `popupWidth` 为负数或不是数：警告，按 `0`（`PUI-SUGGEST-VALUE`）。
- `placement` 的值不在表里：抛错，lint 报 `PUI-SUGGEST-ENUM`（同今天）。
- `width` 照旧被忽略（`PUI-SUGGEST-LAYOUT-ATTR`），提示里指向 `popupWidth`。

### 4.2 不可选的行

```csharp
hits.BindItems(results, (IControl row, Hit h) =>
{
    row.Get<Text>("name").TextValue = h.Name;
    row.Interactable = !h.IsMessage;        // "No match" / "Loading…": shown, never chosen
}).AddTo(screen);
```

- **标记**：绑定回调里把这一行模板根的 `Interactable` 设为 `false`。
  - 列表在每次绑定前把这一行复位为可选：行是复用的，回调只为要禁用的项写 `false` 即可，不用写 `true`。
  - 只在绑定时读取。绑定之后再改 `Interactable`，要等下一次推送才生效。
- **外观**：行进入 `state-disabled`，模板里的 `<Show on="state-disabled">` 生效，没有内建样式。悬停、按下都没有反应。
- **高亮**：↑ / ↓（手柄上下）跳过不可选行。全是不可选行时，方向键不动，高亮停在「无」。
  - `HighlightedIndex = i` 指向不可选行时被忽略。
- **选择**：点不可选行，不提交、不关列表，输入框继续编辑。Enter / 提交钮在没有高亮时照旧是输入文字的提交（`ChosenIndex = −1`）。
- **显示条件不变**：只要推送非空就开，所以一条「没有匹配」就能让列表显示出来。`Count` 计入不可选行；`OnSubmit` 的下标仍是数据下标。
- **按下进行中（SG-D15）**：编辑在不可选行被按住期间结束，抬起时不选中，列表关闭。

## 5. 内核改动

- `PopupLayout` 加 `AlignEnd`。`PopupPlacer.Solve(…, preferAbove, alignEnd)`：
  - 起点 `dx = alignEnd ? handle.width − panel.width : 0`；
  - 再按今天的规则先推回右边界、再守住左边界；
  - TabMenu 不传，结果不变。
- `Suggestions`：
  - `PopupWidth` 属性；
  - `Placement` 解析成侧 + 对齐；
  - `Measure()` 用 `popupWidth`，传 `AlignEnd`；
  - `Rebuild` 绑定前复位、绑定后把 `PeekInteractable` 抄到行壳的 `FocuslessPress.Interactable`；
  - `MoveHighlight` 跳过不可选行；
  - `HighlightedIndex` 的 setter 与 `CommitChosen` 拒绝不可选行。
- `SuggestionsRules`：
  - `placement` 认 `-start` / `-end`；
  - `popupWidth` 的数值检查并进 `PUI-SUGGEST-VALUE`；
  - 布局属性的提示里提 `popupWidth`。

## 6. 测试（Red 先行）

- **`PopupPlacerTests`**（纯函数）：
  - `alignEnd` 右沿对齐；
  - `alignEnd` 被左边界夹住；
  - 不传时与今天相同。
- **`SuggestionsWidthTests`**：
  - `popupWidth` 放宽、收窄；
  - 不写时同输入框宽；
  - `below-end` 右沿对齐输入框右沿，默认左沿对齐；
  - 负数警告并回落。
- **`SuggestionsRulesTests`**：
  - `below-end` / `above-start` 不报；
  - `below-middle` 报 `ENUM`；
  - `popupWidth='-1'` 报 `VALUE`。
- **`SuggestionsUnpickableTests`**：
  - 绑定时置 false 的行是 `Disabled`；
  - 下一次推送复位；
  - ↓ 跳过；
  - 全不可选时高亮不动；
  - 设置 `HighlightedIndex` 被忽略；
  - 点击不提交、不关、继续编辑；
  - 全不可选的推送照样打开；
  - Enter 无高亮时提交输入文字；
  - SG-D15 抬起在不可选行上：不提交、关闭。

## 7. SKILL / 文档更新（同 PR，英文）

- **XML skill**：
  - `reference/controls-suggestions.md`：属性表（`popupWidth`、`placement` 的对齐）、*Placement* 一节、新一节 *Rows that cannot be chosen*，行状态表加 `state-disabled`，lint 表；
  - `reference/states.md` 的 Suggestions 行一段加 `state-disabled`；
  - 主 `SKILL.md` 的 `<Suggestions>` 段（「永远与输入框同宽」改写）。
- **C# skill**：Suggestions 小节加不可选行的写法，速查表。

## 8. 决策

| # | 题目 | 定 | 理由 |
|---|---|---|---|
| SW-D1 | `popupWidth` 取值 | 像素；`0` = 输入框宽；可比输入框窄 | 与 TabMenu 一致；hug 无上限（§2-A） |
| SW-D2 | 对齐 | `placement` 加 `-start` / `-end` | 显式，不随运行时位置变（§2-B）；同 Popover 的对齐词 |
| SW-D3 | 不可选行的标记 | 绑定回调里 `slot.Interactable = false`；绑定前复位 | 不撞重载（§2-C）；复用通用词汇与 `state-disabled` |
| SW-D4 | 不可选行的行为 | 显示、跳过高亮、不提交、不关 | 「没有匹配」「加载中」的需要 |
