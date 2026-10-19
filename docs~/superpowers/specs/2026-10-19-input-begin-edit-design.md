# 输入框：代码进入编辑、浮层随编辑结束而关、编辑中的 Esc

> 状态：**已实施**（实施记录见 §12）。**已对齐**（2026-10-19，与作者对齐，按推荐定：叫 `BeginEdit()` 并公开 `IsEditing`；「编辑结束即关」由 `<Popover>` 内建；编辑中的输入框排在 Esc 链尾；Esc 与 B 算、Start 不算）。
> 来源：搜索浮层（图标就地展开成输入框 + 候选列表，popover spec §17）的接入反馈；读源码做的评估。§1 的问题都是读代码得出的，复现由 §7 的 Red 测试给出。
> 相关：
> `2026-10-19-popover-design.md`（§4.4 关闭表、§4.8 焦点、§17 盖在锚点上、PO-D22 打开即编辑不内建）、
> `2026-10-18-popup-core-and-input-suggestions-design.md`（§4.4 Esc 链、§6.5 / SG-D15 按下进行中、§6.6 编辑边沿）、
> `2026-06-29-inputfield-nav-edit-mode-design.md`（两级导航门）。

## 1. 问题

### 1.1 代码进入编辑

- 今天唯一的入口是 `screen.Focus(id)`，它只做 `EventSystem.SetSelectedGameObject`：
  - 输入框**已经是选中项**时什么都不做（`SetSelectedGameObject` 对同一对象直接返回）。提交或 Esc 之后，TMP 只退出编辑、不清 EventSystem 的选中，所以「提交失败，回到输入框接着打」做不到。
  - 方向导航下，两级门 `InputFieldNavGate` 把选中带来的激活撤回。这是有意的：手柄落到输入框上不该弹键盘。
  - tvOS 上 TMP 的 `shouldActivateOnSelect` 为假，选中不进编辑。
- `InputField.IsEditing` 是 internal，宿主不知道输入框在不在编辑。

### 1.2 浮层随编辑结束而关

「输入框一结束编辑，浮层就收起」是搜索浮层的常见要求。宿主自己订 `OnEndEdit`、同步 `Close()`，有两处时序错误，宿主侧修不了：

- **提交失败时重新进入编辑。** 在 `OnSubmit` 里请求编辑后，TMP 照样 `DeactivateInputField()` 并发 `OnEndEdit`，宿主据此关了浮层。浮层收起时把选中还给锚点；当帧 `LateUpdate` 里 TMP 又把输入框激活，把选中抢回一个正在淡出的面板。淡出结束，面板失活，编辑再次结束。手机上键盘弹起又收回。
- **Android 上点候选行会丢。** 点游戏画面时系统可能先收键盘，编辑先结束，然后才抬手（SG-D15）。`<Suggestions>` 为此推迟关闭，等这次按压有结果。宿主同步关浮层，却让面板不再挡指针（`blocksRaycasts=false` 被候选面板继承），子弹层也随链关掉，这一下点击丢失。宿主看不到「有行正被按住」。

### 1.3 编辑中的 Esc 不算已处理

- 普通页面上（没有对话框、没有浮层），编辑中的输入框按 Esc：
  - Esc 链没有任何监听，`EscapeChain.Dispatch` 根本没被调用；
  - TMP 在 `EventSystem.Update` 里取消编辑、回滚文本；
  - `UI.EscapeConsumedThisFrame` 仍是假，游戏自己的返回键逻辑在同一下再清选中或 `Back()`。
- 游戏侧判断不了：
  - 游戏的 `Update` 与 `EventSystem.Update` 谁先跑不固定，读到 `isFocused` 时可能已经是假；
  - 判断只能在 Input System 回调那一刻做，那时输入框还在编辑，而这时链上可能一个监听都没有。
- 浮层里的输入框不受影响：这一下被 Esc 链用来关候选列表或浮层了。

## 2. 否决的方案

**A. 让 `screen.Focus` 选中输入框即进编辑、无视两级门。否决。** `Focus` 是通用的选中 API。两级导航模型（手柄落到输入框上不弹键盘，按 A 才编辑）依赖它只选中。

**B. 叫 `InputField.Focus()`。否决。** 与 `screen.Focus` 同名不同义：方向导航下一个进编辑、一个不进。叫 `BeginEdit()`，与 `OnEndEdit` 成对。

**C. 「编辑结束即关」留给宿主订 `OnEndEdit`。否决。** §1.2 的两处时序要看「有没有行正被按住」「有没有待生效的激活」，只有库看得到。

**D. 「焦点离开面板即关」（选中项不在面板里就关）。否决**（同 popover spec §2-F / §12）。Android 收起键盘时选中项还在输入框上；点面板空白处，选中被清成 null。规则要看「编辑」，不看「选中」。

**E. 编辑中的输入框排在模态之前。否决。** 那样对话框里打字时，第一下 Esc 只退出编辑，第二下才关对话框。这改变现有对话框的行为；桌面与网页对话框的习惯是 Esc 直接关。排在链尾兜底。

**F. 由 Esc 链直接取消编辑（`DeactivateInputField()`）。否决。** TMP 的取消还要回滚文本，而它的 `m_WasCanceled` 是私有的。链只记账，取消仍由 TMP 做。

## 3. 方案总览

- **`InputField.BeginEdit()`** 进入编辑，不论选中状态与导航模式；**`IsEditing`** 公开（§4.1）。
- **`<Popover closeOnEditEnd="true">`**：面板里的编辑结束就关，原因 `EditEnded`。按压进行中、同帧重新进入编辑、转去用面板里别的控件，都不关（§4.2）。
- **Esc 链尾加一步**：有输入框在编辑，Esc / B 记为已处理，Start 不算（§4.4）。

## 4. 作者面

### 4.1 `InputField.BeginEdit()` / `IsEditing`

```csharp
public bool IsEditing { get; }   // 在编辑：有光标、收键盘输入。只选中、没在编辑时为假
public bool BeginEdit();         // 进入编辑；做不到时返回 false、什么都不做
```

- **做什么**：
  - 让输入框进入编辑：还不是选中项就选中它，手机上打开软键盘；
  - 不论导航模式，方向导航下等于按了 A；
  - 不论它此刻是不是选中项。
- **何时生效**：当帧的 `LateUpdate`（TMP 的激活点）。调用后 `IsEditing` 仍为假，下一次 `LateUpdate` 之后才为真。
- **已在编辑**：保持编辑。软键盘被收起了就再打开（TMP `ActivateInputField` 的既有行为）。
- **返回 false、什么都不做**：输入框未激活（包括所在浮层关着）、不可交互、只读。
- **光标**：沿用 TMP 的 `onFocusSelectAll`（默认全选）。提交失败后直接打字，就覆盖原文。
- **在回调里调用**：`OnSubmit` / `OnEndEdit` / `Suggestions.OnSubmit` / `Popover.OnOpened` 都可以。
  - 在 `OnSubmit` 里调用，这次编辑照样结束（`OnEndEdit` 照发），随后在同一帧重新开始。
  - 手机上，系统键盘的「完成」键已经收起键盘，所以键盘会再弹一次。这由系统决定，库避免不了。
- **`screen.Focus(id)` 不变**：只移动选中。指针模式下选中输入框即进编辑；方向导航下只选中。

### 4.2 `<Popover closeOnEditEnd>`

```xml
<Popover id="searchPop" on="click@searchBtn" placement="over-end" width="320" closeOnEditEnd="true">
  <InputField id="searchField" width="stretch" height="44" submitIcon="ui:search">
    <Suggestions id="searchHits" itemTemplate="HitRow"/>
  </InputField>
</Popover>
```

| 属性 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `closeOnEditEnd` | bool | `false` | 面板里的输入框结束编辑就关，原因 `EditEnded` |

- **规则**：开着时，面板里（任意深度）有 `<InputField>` 进入过编辑，之后面板里没有输入框在编辑，就关。
- **判定时机**：在面板的逐帧步（`LateUpdate`）里判定。发现编辑结束，**再等一帧**才关。所以下面两种情况不关：
  - 同一帧里重新进入编辑，例如在 `OnSubmit` 里 `BeginEdit()`；
  - 点到面板里的另一个输入框。
- **算作仍在编辑**：`BeginEdit()` 已经请求、还没生效。
- **不关的情况**：
  - **候选列表还开着**：编辑在某一行被按住期间结束（SG-D15）。等这次按压有结果：
    - 在行上抬起，就选中提交，关不关由宿主的提交处理决定。宿主没关的话，候选列表随之关闭，本规则接着判定。
    - 在别处抬起，候选列表关闭，本规则在随后的帧里关浮层。
  - **选中项落到面板里别的可选控件上**（一个 `<Btn>`、一个开关）：用户转去用面板里别的控件了。这次编辑结束作废，之后再有输入框进入编辑，才重新开始判定。
- **会关的典型情况**：
  - Enter 或提交钮提交，而宿主在提交处理里既没 `Close()` 也没 `BeginEdit()`；
  - 点面板的空白处（`InputSystemUIInputModule` 的 `deselectOnBackgroundClick` 清掉选中）；
  - 手机上收起键盘；
  - 方向导航下按 A 进编辑，再 Enter 提交。
- **打开后从没进入过编辑**：不关。打开即编辑要在 `OnOpened` 里 `BeginEdit()`（PO-D22：不内建）。
- 与其它关闭方式（外部按下、Esc / B、被盖住……）并存，先到先关。
- 面板里没有输入框时，这个属性不起作用。
- 可被变体改写，下一帧生效。

### 4.3 关闭原因

`PopoverCloseReason` 加一项 `EditEnded`：`closeOnEditEnd` 且面板里的编辑结束了。§4.4 的关闭表加一行。选中项照 §4.8 还给锚点。

### 4.4 Esc 链：编辑中的输入框

链的顺序（spec 2026-10-18 §4.4）末尾加一步：

| # | 谁 | 做什么 | 哪些键 |
|---|---|---|---|
| 1 | 教程的 Block 步 | 吞掉 | Esc / Start / B |
| 2 | 打开的 `<Dropdown>` 列表 | 收起 | Esc / Start / B |
| 3 | 最上层浮层（不在顶层模态之下） | 关闭 | Esc / Start / B |
| 4 | 顶层临时对话框 | 应答 | Esc / Start |
| 5 | 路由模态 | 返回 | Esc / Start |
| **6** | **编辑中的输入框** | **记为已处理；取消编辑仍由 TMP 做（含回滚文本）** | **Esc / B** |

- **B**：走完第 3 步直接到第 6 步。B 从不关模态，这一点不变。
- **Start 不算第 6 步**：TMP 不拿 Start 取消编辑，游戏可能用它开暂停菜单。
- **输入法组字中按 Esc** 也算：编辑中的 Esc 归输入框。
- **只选中、没在编辑**的输入框不算。
- **不受影响的**：
  - 浮层里的输入框：第 3 步先关候选列表或浮层，不变；
  - 对话框里的输入框：第 4 / 5 步先应答，TMP 同时取消编辑，不变。
- **按键源**：输入框进入编辑时，在它身上启用一个 `ModalEscapeListener`（绑 Esc / Start / B），结束编辑时停用。
  - 有了 Input System，游戏的任何 `Update` 都读得到这一帧的结果；
  - 只有旧输入管理器时，照旧在 `LateUpdate` 里读（`UI.EscapeConsumedThisFrame` 的既有说明）。

## 5. 内核改动

### 5.1 `InputField` / `PuiInputField` / `InputFieldNavGate`

- `IsEditing` 改为 public，语义不变（测试钩子 `_editingOverride` 照旧）。
- `BeginEdit()`：
  - 测试钩子钉着「不在编辑」时：改钉成在编辑，返回 true（与 `EndEditing()` 对称）；
  - 守卫：TMP 组件未激活、`!IsInteractable()`（含 CanvasGroup）、只读，返回 false；
  - 告诉两级门：这次激活是代码要的，选中引起的 `OnSelect` 不撤回（`InputFieldNavGate.ExpectEdit()`）；
  - `PuiInputField.RequestEdit()`：记下「请求待生效」，再调 `ActivateInputField()`。
- `PuiInputField.EditRequestPending`：
  - 在 `LateUpdate` 里，跑完 TMP 的激活步后清掉，只清这次 `LateUpdate` 开始前就有的请求。在 TMP 的 `LateUpdate` 里发起的请求（软键盘「完成」引出的回调），要下一次 `LateUpdate` 才生效。
  - `OnDisable` 时也清掉。
- **编辑中的输入框表**：`InputField` 上一张静态表，在编辑边沿进出（真实边沿 `NotifyEditingChanged`、测试钩子 `SetEditingForTests`、`Dispose`）。同一边沿启用 / 停用输入框自己的 `ModalEscapeListener`（`AlsoCancelButton = true`）。`InputField.AnyEditing` 逐项核对 `IsEditing`，表里的过期项不会误报。
- `InputFieldNavGate.ExpectEdit()`：清掉撤回标记，并让下一次 `OnSelect` 不再设它。看到 `isFocused` 或请求已被消费（`!EditRequestPending`）时，这个「期待」就结束；`OnDeselect` 时也结束。

### 5.2 `Popover`

- `[UIAttr] bool CloseOnEditEnd`。
- 逐帧步（`PopupCore.LateTick`）从 `Follow` 改为 `Tick`：先 `Follow`，仍开着就 `CheckEditEnd`。`TickForTests` 跟着走 `Tick`。
- `CheckEditEnd` 的状态：`_editSeen`（这次判定周期里见过编辑）、`_endNoted`（上一帧已发现结束），打开时清零。
  1. 面板里激活的 `PuiInputField`，任一 `IsEditing` 或 `EditRequestPending`：`_editSeen = true`，`_endNoted = false`，返回。
  2. `!_editSeen`：返回。
  3. 任一输入框的候选列表开着：`_endNoted = false`，返回（SG-D15，等按压有结果）。
  4. 选中项是面板里不带 `TMP_InputField` 的 `Selectable`：两个标记清零，返回。
  5. `!_endNoted`：`_endNoted = true`，返回。
  6. 以 `EditEnded` 关闭（`CloseAs`）。
- 每帧取一次面板里的输入框（`GetComponentsInChildren` 填一张复用的表）。只在 `closeOnEditEnd` 时做。

### 5.3 `EscapeChain` / `ModalEscapeListener`

- `EscapeKey` 加 `StartButton`。`ModalEscapeListener` 按触发它的控件分辨：`start` → `StartButton`，`buttonEast` → `CancelButton`，其余 → `Escape`。旧输入管理器路径只有键盘 Esc，不变。
- `Decide` 的末尾：
  - `CancelButton`：从「直接返回 false」改为返回 `InputField.AnyEditing`；
  - 第 4 / 5 步对 `Escape` / `StartButton` 一视同仁，不变；
  - 最后：`key == Escape && InputField.AnyEditing`。
- `UI.EscapeConsumedThisFrame` 的 XML 注释补上「一个正在编辑的输入框（Esc / B）」。

## 6. 报错

不新增。`closeOnEditEnd` 写成非 bool 时，走通用属性的类型错误。

## 7. 测试（Red 先行）

- **EditMode**
  - `InputFieldBeginEditTests`：
    - 未激活 / 不可交互 / 只读时返回 false，且不留下待生效的请求；
    - 可以编辑时返回 true，并留下请求；
    - 测试钩子钉着「不在编辑」时，改钉成在编辑；
    - 方向导航下，`BeginEdit` 之后选中引起的 `OnSelect` 不撤回。
  - `PopoverCloseOnEditEndTests`（`SetEditingForTests` + `TickForTests`）：
    - 编辑结束后的第二帧才关，原因 `EditEnded`；
    - 中间那一帧重新编辑就不关；
    - 请求待生效算在编辑；
    - 从没编辑过不关；
    - 属性为 false 不关；
    - 候选列表开着（按住一行时编辑结束）先不关，列表关了才关；
    - 选中项落到面板里的 `<Btn>` 上不关，之后清掉选中也不关；
    - 再编辑、再结束才关；
    - 关后选中项回到锚点。
  - `EscapeChainTests` 新增：
    - 普通页面上有输入框在编辑：Esc 记为已处理，B 也是，Start 不是；
    - 没在编辑不算；
    - 对话框里有输入框在编辑时，对话框照旧应答；
    - 浮层里的输入框在编辑时，浮层照旧先关；
    - 输入框的监听只在编辑时启用。
- **PlayMode**
  - `InputFieldBeginEditPlayTests`：
    - 提交后输入框仍是选中项，`BeginEdit` 重新进入编辑；`screen.Focus` 做不到（复现 §1.1）；
    - 方向导航下，已选中未编辑时 `BeginEdit` 进入编辑；
    - 方向导航下，未选中时 `BeginEdit` 进入编辑（门不撤回）；
    - 在 `OnSubmit` 里 `BeginEdit` 之后仍在编辑。
  - `PopoverCloseOnEditEndPlayTests`：
    - 在 `OnSubmit` 里 `BeginEdit`，浮层不关，输入框在编辑（复现 §1.2 的第一处）；
    - 清掉选中，下一帧以 `EditEnded` 关闭。
  - 设备用例（`InputTestFixture`）：
    - 普通页面上，编辑中的输入框按 Esc：这一帧 `EscapeConsumedThisFrame` 为真，编辑结束（复现 §1.3）；
    - 手柄 Start 不算；
    - 手柄 B 算。

## 8. 演示同步（同 PR）

样例的聊天搜索（`CommonControls`）：

- `findPop` 加 `closeOnEditEnd="true"`；
- `OnOpened` 里 `input.BeginEdit()`，替换 `screen.Focus`；
- 提交时没有命中：`input.BeginEdit()` 回到输入框，不再静默返回。

## 9. SKILL / 文档更新（同 PR，英文）

- **XML skill**：
  - `reference/controls-popover.md`：属性表加 `closeOnEditEnd`；关闭表加 `EditEnded`；「浮层里放输入框」一节改用 `BeginEdit` 与 `closeOnEditEnd`，写清 §4.2 的规则；
  - 主 `SKILL.md` 的 `<Popover>` 目录行加 `closeOnEditEnd`。
- **C# skill**：
  - InputField 小节：`BeginEdit()` / `IsEditing`，以及与 `screen.Focus` 的区别；
  - Popover 小节：配方改为 `BeginEdit`，`EditEnded`；
  - Esc 一节的顺序表加第 6 行；`EscapeConsumedThisFrame` 的说明补上输入框；
  - 速查表。

## 10. 决策

| # | 题目 | 定 | 理由 |
|---|---|---|---|
| BE-D1 | 名字 | `BeginEdit()`，`IsEditing` 公开 | 与 `OnEndEdit` 成对；不与 `screen.Focus`（只选中）混淆 |
| BE-D2 | 方向导航下 | 直接进编辑 | 代码明确要求，等于按了 A；两级模型只管导航选中 |
| BE-D3 | 光标 | 沿用 `onFocusSelectAll`（全选） | 提交失败后直接重打 |
| BE-D4 | 编辑结束即关 | 库内建，`<Popover closeOnEditEnd>` | §1.2 的时序只有库看得到 |
| BE-D5 | 判定 | 看「编辑」，不看「选中」；结束后再等一帧；候选按压中、转去用面板里别的控件，都不关 | §2-D；同帧重进编辑、点另一个输入框都要能活下来 |
| BE-D6 | Esc 链位置 | 链尾兜底 | 现有浮层 / 对话框行为一条不变（§2-E） |
| BE-D7 | 哪些键 | Esc、B 算；Start 不算；组字中也算 | TMP 用 Esc / B 取消编辑，不用 Start |
| BE-D8 | 谁取消 | TMP；链只记账 | 回滚文本是 TMP 的私有状态（§2-F） |

## 11. 风险

- **TMP 的待激活标记是私有的**：`BeginEdit` 之后、`LateUpdate` 之前，输入框被停用（浮层同帧又关了），TMP 留着这个标记，下次启用时会自己进入编辑。指针点击输入框后同帧停用也一样，这是 TMP 的既有行为，不另处理。
- **WebGL 手机浏览器**只允许在用户手势里弹键盘，代码进入编辑可能不弹。未验证。
- **`closeOnEditEnd` 每帧扫一遍面板里的输入框**：面板小，用复用的表、不分配，只在属性打开时做。

## 12. 实施记录

**先红后绿**：每个 Task 先放不改行为的桩，再写测试，红在预期处。

| 测试 | 改线前的结果 |
|---|---|
| `InputFieldBeginEditTests`（三条） | `BeginEdit` 返回 false、不留请求；方向导航下选中仍被撤回 |
| `InputFieldBeginEditPlayTests`（五条） | 提交后、方向导航下、`OnSubmit` 里都没进入编辑 |
| `EscapeChainFieldTests`（四条） | 编辑中的 Esc / B 没被记为已处理；输入框身上没有监听 |
| `InputFieldEscapePlayTests`（两条） | 真按 Esc / B 取消编辑的那一帧，`EscapeConsumedThisFrame` 为假（复现 §1.3） |
| `PopoverCloseOnEditEndTests`（四条） | 编辑结束后浮层一直开着 |
| `PopoverCloseOnEditEndPlayTests`（两条） | 提交、清掉选中后浮层一直开着 |

**改线前就绿的**（守住不变的行为）：
- `After_a_submit_screen_Focus_cannot_reenter_the_edit`：复现 §1.1，`screen.Focus` 对已选中的输入框什么都不做。
- 拒绝的三种情况（未激活、不可交互、只读）。
- Start 不算、没在编辑不算、对话框与浮层照旧先应答。
- `A_failed_submit_that_goes_back_to_the_field_keeps_it_open`：改线前浮层本来就不关，守住实现。

**与设计的偏差 / 补充**：
- 测试自己的错，各一次：
  - 同一份文档的两个 Screen 走路由时要写 `screen:`；
  - 没有 `on=` 的浮层要 `Open(anchor)`。
- `BeginEdit` 在测试钩子钉着「在编辑」时直接返回 true（不碰 TMP），与钉着「不在编辑」时改钉成在编辑对称。
- `InputFieldNavGate` 的「期待」在 `Update` 和 `LateUpdate` 都结算：看到编辑开始，或请求已被消费。
- `EscapeChain.ResetForPlaySession` 顺带清空编辑中输入框表。表里的项每次都核对 `IsEditing`，清空只是不让上一次 Play 的对象留在表里。

**验证**：
- EditMode 5679 / EditorOnly 471 / PlayMode 326 全绿。
- `dotnet format --verify-no-changes` 干净。
- UIXmlLint 对 `Runtime/Resources/` 与 `Samples~/` 零问题。
- 样例的 C# 在宿主编辑器里用 Roslyn 对已加载的程序集编译：0 错误。`.lint` 工作区的 Unity 引用路径在本机已失效，编不了。
- 留待手测：
  - 手机真机上提交失败后 `BeginEdit` 键盘再弹一次；
  - Android 收键盘时点候选行，浮层不丢这一下；
  - 手柄打开搜索浮层即进入编辑。
