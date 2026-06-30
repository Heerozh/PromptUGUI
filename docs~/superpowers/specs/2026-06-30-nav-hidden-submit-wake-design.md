# 隐藏焦点下的 Submit 唤醒：鼠标用过后第一次确认只唤回光标、不误触

**日期**：2026-06-30
**状态**：设计阶段（已 review 通过，待实施计划）
**作用域**：修复方向导航的一个交互缺陷——鼠标用过后焦点光标隐藏、但 `EventSystem` 选区仍停在上次键盘/手柄选中的控件上，此时按确认键（回车 / 手柄 A）会**误触发那个看不见的旧焦点**。改为「先唤回光标、不触发」。

- 改 `NavigationController`：把 **Submit 类输入**（`Keyboard.enterKey` / `Gamepad.buttonSouth`）从「方向输入（翻 `Directional`）」判定里移除，只留真正的*移动*输入（摇杆 / dpad / 方向键 / Tab）翻模式。
- 新增单一来源的拦截入口 `UI.Navigation.TryWakeOnSubmit()`，在 `PuiButton` / `PuiToggle` 的 `OnSubmit` override 里各调用一行：Pointer 模式收到 Submit → 翻 `Directional` 唤回光标 **+ 吞掉本次点击**。
- **不碰公共 C# API、不碰 XML 语义**——纯 runtime 行为修复。

**关联**：
- 修订 `2026-06-29-gamepad-keyboard-navigation-design.md`（手柄/键盘导航母 spec）§3（导航模式翻转触发器）与 §11（边界表）的既有行为；沿用其 `UI.Navigation.Mode` 中枢、`FocusCursorView` 显隐门控、`IStateSource`/`StateBroadcaster`。
- 拦截范式沿用 `Runtime/Controls/Internal/InputFieldNavGate.cs`（实现 `ISubmitHandler` + 看 `UI.Navigation.IsDirectional` 决定行为）。

---

## 1. 背景与缺陷

母 spec 落地后，方向导航有两套并存的输入模型：

- **鼠标 = 指针模型**：点哪是哪，没有「持久焦点」概念。
- **手柄 / 键盘 = 焦点模型**：`EventSystem.currentSelectedGameObject` 是一个持久选中项。

`FocusCursorView.Tick()` 在 `Mode == Directional` 且选中项属于本屏时显示光标，否则把 `CanvasGroup.alpha` 设 0 **隐藏**（`FocusCursorView.cs:50`）。但隐藏光标时，**`EventSystem` 的选区并没有被清除**——`NavigationController` 从不清选区。于是「光标看不见」≠「焦点没了」：焦点仍在旧控件上，仍能被确认键触发。

**复现（用户报告场景，界面上只有 OK / Cancel 两个按钮）**：

1. 用键盘把焦点移到 **Cancel**（`Directional`，光标停在 Cancel 上）。
2. 用户移动鼠标 → `NavigationController` 判 `NotePointerInput()` → `Mode=Pointer` → 光标隐藏；**但选区仍是 Cancel**。
3. 用户没看到光标，自然以为「焦点没了」，想确认 OK，按下回车。
4. `NavigationController.Update` 当帧把 `enterKey` 当成方向输入（`NavigationController.cs:31`）→ `NoteDirectionalInput()` → `Mode` 翻 `Directional`；**与此同时** `InputSystemUIInputModule` 把 Submit 派发给 `currentSelectedGameObject`（Cancel）→ **Cancel.onClick 触发**。

结果：用户想点 OK，实际触发了 Cancel；下一帧光标才重新出现在 Cancel 上（此时已经晚了）。模式翻转并不能阻止 Submit——Submit 作用在陈旧选区上，翻转只是让光标事后重现。

## 2. 根因：可见性与可触发性被解耦

核心一句话：**焦点的「可见性」（由 `Mode` 驱动的光标显隐）与「可触发性」（`EventSystem` 选区）被解耦了。** 用户用「看不看得见光标」来判断「焦点在不在」，但系统用「选区指向谁」来决定 Submit 作用于谁。两者一旦不一致，就会出现「看不见的焦点被确认键命中」。

修复就是把两者重新绑死：**看不见焦点时，确认键不能作用在那个隐藏焦点上。** 用户选定的具体行为（见母 spec 之外的本次 brainstorming）是「**先唤回光标、不触发**」——鼠标之后第一次确认只把光标唤回它真正所在的元素，让用户看清后再决定，第二次确认才真正点击。这正是主机 / Steam Big Picture 的约定。

## 3. 设计

### 3.1 拦截逻辑（单一来源，放进 `UI.Navigation`）

```csharp
// internal —— 控件 OnSubmit 调它决定「这次确认是唤醒还是点击」
internal static bool TryWakeOnSubmit()
{
    if (!IsEnabled)     return false;   // 没开导航 → 正常点击（旧行为）
    if (IsDirectional)  return false;   // 光标可见 → 正常点击
    NoteDirectionalInput();             // Pointer 模式收到 Submit → 翻 Directional，下一帧光标唤回
    return true;                        // 吞掉这次 Submit
}
```

返回 `true` = 调用方应**直接 return、不执行点击**；返回 `false` = 照常 `base.OnSubmit`。逻辑只此一处，三种控件共用。

### 3.2 两个 override（盖住 Btn / Tab / Toggle）

`PuiButton : Button` 与 `PuiToggle : UnityEngine.UI.Toggle`（`PuiToggle` 同时服务 `Tab` 和 `Toggle`），二者的 `OnSubmit` 都是 `public virtual`，各加一行守卫：

```csharp
// PuiButton
public override void OnSubmit(BaseEventData eventData)
{
    if (UI.Navigation.TryWakeOnSubmit()) return;
    base.OnSubmit(eventData);   // 原生 Press()/onClick
}

// PuiToggle
public override void OnSubmit(BaseEventData eventData)
{
    if (UI.Navigation.TryWakeOnSubmit()) return;
    base.OnSubmit(eventData);   // 原生 InternalToggle()
}
```

**只拦 Submit，不拦指针**：鼠标点击走 `IPointerClickHandler.OnPointerClick`（uGUI `Button`/`Toggle` 各自实现，未被本设计触碰）→ 鼠标点 OK 永远照常工作。

### 3.3 `NavigationController` 触发器收窄（关键前置）

把 `NavigationController.Update` 的方向输入判定里 **`Gamepad.buttonSouth.wasPressedThisFrame` 与 `Keyboard.enterKey.wasPressedThisFrame` 两项删除**，只保留真正的*移动*输入：

```
翻 Directional 的输入 = 摇杆 leftStick / dpad / 方向键 / Tab        （移动类，保留）
不再翻模式的输入     = buttonSouth / enterKey（Submit 类）          （删除）
buttonEast（Cancel/Back）保持不变                                   （取消语义，见 §4）
```

**为什么必删**：`TryWakeOnSubmit` 靠读 `IsDirectional` 判断「光标是否可见」。若 Submit 输入仍会翻模式，则按下确认键的那一帧——`NavigationController.Update` 与 `InputSystemUIInputModule` 的 `OnSubmit` 派发之间 **MonoBehaviour 执行序不保证**——一旦控制器先把 `Mode` 翻成 `Directional`，`OnSubmit` 里的守卫就会读到 `Directional` 而放行点击，bug 重现。删掉 Submit 触发器后，确认键那帧 `Mode` 稳定停在 `Pointer`，守卫可靠地走「唤醒」分支；确认键改由守卫内的 `NoteDirectionalInput()` 来翻模式（唤回路径），不再走控制器。

> 副作用方向是好的：确认键不再「建立」方向模式，只有移动输入才建立——这与「先唤回、再确认」完全自洽。纯键盘流不受影响：方向键负责建立 `Directional`，回车只在已 `Directional` 时点击。

### 3.4 数据流验证

**缺陷场景（修复后）**：键盘移到 Cancel（`Directional`，光标在 Cancel）→ 鼠标动（`Pointer`，光标隐藏，选区仍 Cancel）→ 按回车：`enterKey` 不再翻模式，`Mode` 稳定 `Pointer` → `InputSystemUIInputModule` 派 Submit 给 Cancel → `PuiButton.OnSubmit` → `TryWakeOnSubmit()` 读到 `!IsDirectional` → `NoteDirectionalInput()` + 返回 `true` → **Cancel 不触发**，下一帧 `FocusCursorView` 见 `Directional` + 选区 Cancel → 光标重现在 Cancel → 用户看清后按方向键移到 OK（移动输入，停在 `Directional`）→ 回车 → 守卫读到 `IsDirectional` → 放行 → **点 OK**。✓

**纯键盘流（无鼠标）**：方向键 → `Directional` 光标显示 → 回车 → 守卫 `IsDirectional` → 正常点击。✓

**开屏首次输入是确认键**（§6.1 初始焦点已设、`Pointer` 初值、光标隐藏）：按手柄 A / 回车 → 守卫 `!IsDirectional` → 唤回光标到初始焦点、不触发。第一次确认只是让光标现身，自洽。✓

## 4. 边界与取舍

| 场景 | 行为 |
|---|---|
| 未调 `UseGamepadNavigation`（`IsEnabled==false`） | `TryWakeOnSubmit` 立即返回 `false` → 完全旧行为，零影响 |
| 已 `Directional`（光标可见）按确认 | 守卫返回 `false` → 正常点击/toggle（无额外延迟） |
| 鼠标直接点按钮（任何模式） | 走 `OnPointerClick`，不经本守卫 → 照常触发 |
| 手柄 B（`buttonEast`，back 语义） | **不经本守卫**：库内无 `ICancelHandler` 实现，故 East 不会作用在任何（隐藏的）焦点上——它只重新建立 `Directional`，因此保留在模式翻转列表无害。（模态关闭走 `ModalEscapeListener` 绑定的 Esc / 手柄 Start，**非** East。） |
| 空格确认（Submit action 默认绑定之一） | 空格本就不在 `NavigationController` 触发列表 → `Pointer` 模式按空格同样走守卫唤醒，无需额外改动 |
| 屏上无选区（`currentSelectedGameObject==null`） | 不派发 `OnSubmit` → 守卫不运行；按确认无反应（与现状一致，按方向键开始导航） |
| **模态/屏刚打开、用户首次就按确认键**（从未拨过方向键，`Pointer` 初值、光标隐藏、初始焦点已设在 OK） | 第一次确认**唤回光标到 OK、不确认**，需第二次确认才点 OK。这是「先唤回」规则的统一后果（修复前是首次回车直接点 OK，类似 Windows 默认按钮）。属**有意的行为变更**——用户选定「光标不可见时确认必须先唤醒」，故快速 Enter 关对话框从冷态起需两下 |
| **InputField 排除在 v1 之外** | `TMP_InputField`（非我们的子类）的两级编辑由 `InputFieldNavGate` + TMP 自身 `OnSubmit` 处理，路径更复杂；且「误按回车进编辑」远没有「误触 Cancel」有害。`InputFieldNavGate` 依赖*方向键*已建立 `Directional`，不受 §3.3 删 `enterKey` 影响。本次范围聚焦 Btn/Tab/Toggle |
| Carousel（非 Selectable） | 本就不参与手柄导航（母 spec §13），与本设计无关 |

## 5. 测试（TDD——红先行）

**EditMode**（`UI.ResetForTests` + `UI.Navigation.Mode` 内部可设，仿现有 Navigation 测试）：

1. `TryWakeOnSubmit` 三分支：`IsEnabled==false` → `false` 且 `Mode` 不变；`Mode==Directional` → `false` 且不变；`IsEnabled && Mode==Pointer` → 返回 `true` 且 `Mode` 翻 `Directional`。
2. `PuiButton.OnSubmit` 在 `Pointer` 模式（导航已启用）：`onClick` **不**触发，`Mode` 翻 `Directional`（构一个挂 `PuiButton` 的 GO，订阅 `onClick`，直接调 `OnSubmit(new BaseEventData(es))`）。
3. `PuiButton.OnSubmit` 在 `Directional` 模式：`onClick` 正常触发一次。
4. `PuiToggle.OnSubmit` 在 `Pointer` 模式：`isOn` 不翻转、`Mode` 翻 `Directional`；`Directional` 模式：`isOn` 正常翻转。
5. 导航未启用（`IsEnabled==false`）：`PuiButton.OnSubmit` 在任意 `Mode` 都正常点击（回归保护）。

**PlayMode**（仿 `CarouselPlayTests`，真实 EventSystem + InputSystem TestFramework，可选——若 runner 不稳先以 EditMode 守底）：

6. 键盘选中 Cancel → 注入鼠标移动（光标隐藏）→ 注入回车 → 断言 Cancel `onClick` 未触发、光标下一帧重现；再注入方向键到 OK + 回车 → OK 触发。

## 6. SKILL 更新与风险

**SKILL**：本次为**纯 runtime 默认行为修复**，无新增 XML 元素/属性、无新增公共 C# API → 按既有约定（透明默认行为豁免）**默认不需要 SKILL 改动**。唯一例外：若母 spec §14 规划的 `reference/navigation.md` 已落地并描述了「鼠标↔手柄模式切换」的 UX，则补一句「鼠标之后第一次确认只唤回光标、不触发」。实施时核查该文件是否存在再定。

| 风险 | 缓解 |
|---|---|
| 执行序导致守卫读到过期 `Mode` | §3.3 删 Submit 触发器后，确认键那帧 `Mode` 不会被控制器翻动，守卫读数稳定；EditMode #2/#3 锁住「Pointer 吞、Directional 放」 |
| 删 `enterKey`/`buttonSouth` 误伤其它依赖确认键翻模式的逻辑 | 全仓 grep `Mode`/`IsDirectional` 消费方共四处：`FocusCursorView`（光标显隐）、`StateBroadcaster.cs:59`（焦点 tint 门，母 spec §4.2）、`InputFieldNavGate`（两级编辑门）、`RepokeSelected`（翻转重绘）。四者**都只读「当前是否 `Directional`」**，而 `Directional` 由*移动*输入建立并持续保持——删 Submit 触发器至多让「指针输入→紧接 Submit」这一序列里 `Mode` 多停留 `Pointer` 一帧，绝不会让导航序列中的 `Directional` 变弱；故无一受损（InputField 两级编辑由方向键建立 `Directional`，与回车无关） |
| 漏盖某种可点击控件 | Btn=`PuiButton`、Tab/Toggle=`PuiToggle` 已是全部走 Submit 的内置可点击控件；Slider/Dropdown 的 Submit 语义（拖动/展开）不属于「误触按钮」类，v1 不纳入，留作后续 |
| `base.OnSubmit` 签名/可见性变化 | uGUI `Button.OnSubmit`/`Toggle.OnSubmit` 均 `public virtual`，override 稳定；编译期校验 |
