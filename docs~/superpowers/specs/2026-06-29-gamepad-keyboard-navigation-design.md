# 手柄/键盘导航:焦点可见 + 移动光标 + 进屏聚焦/模态 trap

**日期**:2026-06-29
**状态**:设计阶段(已 review 通过,待实施计划)
**作用域**:
- 解封键盘/手柄焦点态:新增 `InteractState.Focused`,改 `StateBroadcaster.MapTransient`(把 uGUI `SelectionState.Selected` 从折叠成 `Normal` 改为映射到 `Focused`),复用既有 hover 视觉。
- 新增 `<FocusCursor>` 结构元素 + 内置默认光标 + `FocusCursorView` 控制器(主机风移动手指)。
- 新增"导航模式"中枢信号(Pointer ↔ Directional,按上次输入设备判定)。
- 新增初始焦点标记 `focus="true"`、模态选区限制(trap)+ 关闭还原。
- 新增显式导航覆盖 `nav="none"` 与 `navUp/navDown/navLeft/navRight="id"`。
- 新增 C# 门面 `UI.UseGamepadNavigation(...)` + `Screen.Focus(idPath)`。
- **仅支持新 Input System**(`#if ENABLE_INPUT_SYSTEM` 门控;旧系统下整套 no-op + 一次性警告)。不进 `package.json` 硬依赖。

**关联**:
- 状态模型沿用 `2026-05-30-clickable-state-visuals-design.md`(`IStateSource` / `StateBroadcaster` / `MapTransient` / 复合规则 §4.2)。
- 光标定位/像素吸附/逐帧跟随复用 Tutorial 体系(`2026-06-12-tutorial-system-design.md` §5.3 的 世界→屏幕→overlay 本地 三段式、`PixelSnap`、`LitMotion .AddTo`)。
- 模态 trap/还原复用 `UI.Modal` 的栈 + `RefreshTopListener` 手法(`2026-05-20-modal-layering-design.md`)。
- 输入门控范式沿用 `Runtime/Application/Modals/ModalEscapeListener.cs` 的 `#if ENABLE_INPUT_SYSTEM`。
- 公开 C# API 新增 → `scripting-promptugui-csharp` 必须更新;新增作者可写 XML(`<FocusCursor>` / `focus` / `nav*`) → `authoring-promptugui-xml` 必须更新(见 §14)。

---

## 1. 背景与目标

界面系统功能已丰富,但缺最关键的一块:**游戏手柄 / 键盘默认操作**(Tab/方向键移动焦点、确认/取消)。这对要出 PC 宽屏(可能接手柄)的像素游戏是硬需求。

调查现状(三处事实):

1. **控件已全是 uGUI `Selectable`**——Btn=`Button`、Tab/Toggle=`Toggle`、Slider=`Slider`、Dropdown=`TMP_Dropdown`、InputField=`TMP_InputField`、ScrollList=`ScrollRect`。`navigation` 全是默认的 `Automatic`。**唯一例外**:Carousel 是自定义 `CarouselView`(非 Selectable)。
2. **导航其实"差一点就能用"**:有 EventSystem + 正确 input module + 一个被选中的初始控件,方向键/摇杆本就能在控件间移动焦点。缺的不是导航算法。
3. **决定性缺口在视觉**:`StateBroadcaster.MapTransient` 把 uGUI `SelectionState.Selected`(ordinal 3,正是键盘/手柄聚焦那个态)**折叠成 `InteractState.Normal`**(源码注释 "keyboard focus is not checked")。焦点在移动,但**看不出焦点在哪**。

另外:EventSystem 现由 host 负责(库只在缺失时 `LogWarning`);库无输入系统硬依赖。

**v1 目标(用户确认)**:
- 焦点可见——解封焦点态,**复用 hover 视觉**(作者零额外配置;没设 hover 就不显示,靠光标兜底)。
- **移动手指光标(主角)**——主机/JRPG 风:手指指向当前聚焦控件,拨摇杆手指跟着跳;光标内容用 XML 书写,带 `<Animation>` 动画;可模板化/全局默认。
- 进屏自动聚焦 + 模态焦点 trap + 关闭还原。
- 显式导航覆盖(`nav="none"` / `navUp..`)+ 初始焦点标记 `focus="true"`。
- **自动显隐**:手柄/键盘才显示焦点与手指;鼠标/触屏隐藏(手机竖屏永不出现)。
- 仅新 Input System。

**非目标**见 §13。

## 2. 设计总览——靠 uGUI EventSystem,不造引擎

导航交给 uGUI 的 EventSystem(WebGL 安全、无线程、久经考验,且控件已全是 Selectable)。库**只补四件事 + 一个中枢**:

| 件 | 复用的现成机器 | 新代码 |
|---|---|---|
| **中枢:导航模式信号**(§3) | 新 Input System 设备查询 | `NavigationMode` 静态信号 + 设备分类 |
| ① 焦点可见(§4) | `IStateSource`/`StateBroadcaster`/`StateTintReactor`(clickable-state-visuals) | 改 `MapTransient` + 加 `InteractState.Focused` + 模式门控 |
| ② 移动光标(§5) | Tutorial 的三段式定位 + `PixelSnap` + `LitMotion`;`InstantiateNode` 动态子树 | `<FocusCursor>` 元素 + `FocusCursorView` |
| ③ 进屏聚焦/trap/还原(§6) | `UI.Modal` 栈 + `RefreshTopListener` | 选区限制守卫 + 初始焦点 |
| ④ 显式覆盖(§7) | uGUI `Navigation`/`FindSelectable` | 构建后导航解析器 |

C# 表面只加 `UI.UseGamepadNavigation(...)` + `Screen.Focus(...)`(§8)。

## 3. 中枢:导航模式(Pointer ↔ Directional)

**为什么需要**:uGUI 的 `currentSelectionState` 优先级是 `Pressed > hasSelection(焦点) > 指针 Highlighted > Normal`。所以**鼠标点过一个控件后,它会保持 `Selected` 态**——这正是库当初折叠 `Selected → Normal` 的原因(否则鼠标点完按钮"粘"在高亮上)。因此焦点视觉**不能无条件解封**,必须只在导航语境(上次输入来自手柄/键盘)下才生效。这同一个信号顺带解决:手机端不出现手指、鼠标点完不粘高亮、手柄一拨焦点+手指一起亮。

**`UI.Navigation.Mode`(internal 信号,枚举 `{ Pointer, Directional }`)**:
- 初值 `Pointer`。
- 翻 `Directional`:收到来自 `Gamepad` 的摇杆/方向键/按钮,或 `Keyboard` 的导航键(方向键 / Tab / Submit / Cancel / WASD)事件。
- 翻 `Pointer`:`Mouse` 移动(delta 超阈值)/ 点击,或 `Touchscreen` 触摸。
- 检测实现:新 Input System 的 `InputSystem.onEvent`(轻量,按产生事件的设备分类)或逐帧轮询 `Gamepad.current` / `Keyboard.current` / `Pointer.current` 的 `lastUpdateTime`。具体取舍留实施计划;spec 只约定行为。

**模式翻转的副作用**(由导航控制器统一驱动):
- 翻转时,对**当前被选中的那个控件**(`EventSystem.current.currentSelectedGameObject`,任一时刻至多一个)重算其状态广播(`Focused ↔ Normal`),其余控件不受影响 → O(1)。
- 光标显隐随模式(§5.3)。

`Mode` 只在 `UI.UseGamepadNavigation` 启用后才有意义;未启用时恒 `Pointer`,全部新行为静默关闭。

## 4. 焦点可见(解封 + 复用 hover + 模式门控)

### 4.1 `InteractState.Focused` + `MapTransient`

`InteractState` 追加一个值(置于末尾,避免扰动既有序数):
```
InteractState { Normal, Hover, Pressed, Selected, Disabled, Focused }
```
`StateBroadcaster.MapTransient(int ordinal)`:uGUI `SelectionState.Selected`(ordinal 3)**从 `Normal` 改为 `Focused`**。其余 ordinal 不变(0→Normal,1→Hover,2→Pressed,4→Disabled)。

注:`Selected`(库语义 = `Toggle.isOn` 持久态)与 `Focused`(键盘/手柄瞬时焦点)是**两个互不相干的维度**,沿用 clickable-state-visuals 的命名分离(那份 spec §8 已明确不可复用 "Selected" 一词)。

### 4.2 复合规则 + 模式门控

在 `StateBroadcaster` 的复合逻辑(clickable-state-visuals §4.2 的 `Current = (transient==Normal) ? (isOn?Selected:Normal) : transient`)前插一道模式门:

```
effTransient = (transient == Focused && UI.Navigation.Mode != Directional) ? Normal : transient
Current      = (effTransient == Normal) ? (isOn ? Selected : Normal) : effTransient
```

语义:
- **Pointer 模式**:`Focused` 被折回 `Normal` → 完全等同今天的行为(鼠标点完不粘高亮)。
- **Directional 模式**:`Focused` 保留;优先级 `Disabled > Pressed > Focused > Selected(isOn 基线) > Normal`。
  - 一个 isOn 的 Tab 被手柄聚焦:uGUI 报 `Selected(3)`(focus 优先于 hover)→ `Focused`,`Current=Focused`;其 `selectedSprite` 叠加层 / checkmark 走独立 `isOn` 通道,焦点期间照常显示"激活"(不丢)。
  - 同一 Tab 未聚焦未悬停:uGUI `Normal(0)` → `Current=Selected`(isOn 基线)。

`StateBroadcaster` 在 `Mode` 翻转时需要能重算(导航控制器对当前选中控件调用一次重算入口;沿用 `SetTransient`/`SetOn` 已有的"重算 + 重推 reevaluator"路径)。

### 4.3 视觉复用(`StateTintReactor`)

`StateTintReactor.MultiplierFor(InteractState)` 对 `Focused` **返回 hover 的乘子**(即 `_hover`,与 `MultiplierFor(Hover)` 同源)。**v1 不引入 `_focused` 字段、不引入 `focusColor`/`focusModulate`/`focusSprite` 属性**——焦点视觉 = hover 视觉。hover 未设(`_hover` 为单位白)则焦点不变色,靠光标兜底,符合"没有就不管,手指为主"。

> **预留逃生阀(非 v1)**:将来要让焦点区别于 hover,只需加 `_focused` 字段 + `focusColor`/`focusModulate` 属性 + `MapTransient` 已就位的 `Focused` 值,无需重开抽象。这是 clickable-state-visuals §7 同款"一处加值"扩展点。

### 4.4 `<Show>` / `state-*` 行为

- `Current` 现在可为 `Focused`。`<Show>` 的 `ReevaluateVisibility`(精确匹配 OR 未占用态回退 `Normal`)**不变**:无 `state-focused` 子块时,`Focused` 走 `Normal`-fallback。
- **`state-focused` 触发器与 `<Show on="state-focused">` 不在 v1**(YAGNI;用户未要求焦点专属子树切换)。`TriggerKind` 不加 `StateFocused`。焦点的"复用 hover"仅作用于 tint 通道,不波及 `<Show>` 的 hover 子块(即 `<Show on="state-hover">` 不会因焦点而显示——这是已知且可接受的轻微不对称;真要焦点切子树是将来的 `state-focused`)。

## 5. 焦点光标 `<FocusCursor>`

### 5.1 作者面——光标是一段 XML,不是 sprite 名

`<FocusCursor>` 是 `<Screen>` 的可选直接子元素;其子节点即光标视觉,**拥有标记语言全部能力**(`<Image>`/`<Icon>`/组合美术 + `<Animation>` 做 bob/pulse/rotate/fade;将来有帧动画控件亦自动可用):

```xml
<Screen ...>
  <FocusCursor side="left" offset="-4,0">
    <Image sprite="hand" size="16,16">
      <Animation trigger="always" loop="true"
                 translate="2,0" duration="0.4" ease="inOutSine"/>  <!-- 手指来回点 -->
    </Image>
  </FocusCursor>
  <!-- 界面其余 -->
</Screen>
```

`<FocusCursor>` 属性:
- `side="left|right|top|bottom"`(默认 `left`)——手指停在目标的哪条边(`left` = 停目标左侧、指向右)。
- `offset="x,y"`(默认 `0,0`,设计单位)——在 `side` 基础上的微调。

**模板化/复用**:把光标写成 commons 里的 `<Template name="HandCursor">`,用 `<FocusCursor><HandCursor/></FocusCursor>`;跨屏统一用全局默认(§5.2)。

**实现**:`<FocusCursor>` 不是布局控件。`ScreenInstantiator` 识别它,把其子树经既有 `InstantiateNode`(BindItems/Markdown 动态子树同款,自动登记进 `Screen._dynamicSubtrees`、自动应用 scale)实例化进 Screen 根下一个**独立非布局 overlay** `RectTransform`(顶层兄弟,`ignoreLayout`),并 `AddComponent<FocusCursorView>`。光标不进任何 HStack/Grid 布局。

### 5.2 内置默认光标 + 全局默认

- 库内置 `Runtime/Resources/PromptUGUI/Navigation/FocusCursor.ui.xml`,其 `<Screen>` 内含一个 `<FocusCursor>`,手指复用内置子精灵 `PromptUGUI/Defaults/pugui.png#pugui_caret`(Tutorial 手指同款,零配置即有可用光标)。
- `UI.UseGamepadNavigation(defaultCursor: null)` → 用内置默认;传 src key → 用 host 的。
- **某 Screen 没写 `<FocusCursor>` 时,控制器实例化全局默认的 `<FocusCursor>` 子树进该屏 overlay**。
- **全局默认是必需而非便捷**:4 个内置模态(MessageBox/InputBox/Loading/MarkdownBox)的 XML 里没有 `<FocusCursor>`,靠全局默认才能让手指出现在对话框里。

### 5.3 `FocusCursorView` 控制器

挂在光标 overlay 根上,`LateUpdate` 逐帧(沿用 Tutorial `TutorialOverlayView` 手法):
- **显隐**:仅当 `UI.Navigation.Mode == Directional` **且** `EventSystem.current.currentSelectedGameObject` 属于本 Screen 子树时显示;否则隐藏(`CanvasGroup.alpha`)。
- **定位**:取目标控件 `RectTransform`,经三段式(`TransformPoint` 世界 → `WorldToScreenPoint` → `ScreenPointToLocalPointInRectangle`)换算到光标 overlay 本地坐标,按 `side`/`offset` 摆到目标对应边;`PixelSnap` 吸到整数设备像素(防像素游戏手指抖动)。
- **滑动过渡**:目标切换时用 `LitMotion` 把光标根从旧位插值到新位(`.AddTo(光标GO)`——避免 motion handle 比目标活久的既有崩溃模式)。`<Animation>` 动的是光标根的**子节点**(idle 摆动),与控制器移动根互不打架(两级分离)。
- **目标失效**:`currentSelectedGameObject` 为空或被销毁 → 隐藏。
- **resize / ReSolve / Variant 切换**:逐帧跟随天然生效,无需订阅。

## 6. 进屏聚焦 + 模态 trap + 关闭还原

### 6.1 初始焦点 `focus="true"`

- 任意可聚焦控件上 `focus="true"` 标记本屏初始焦点。
- Screen/Modal 打开完成后,`EventSystem.SetSelectedGameObject(目标)`:有 `focus="true"` 选它;否则选**文档序第一个可聚焦控件**(保证手柄一上来有着力点)。
- 在 Pointer 模式下设置选区**无视觉副作用**(`Focused` 被门控折回 `Normal`、光标隐藏),故开屏即设是安全的。
- 多个 `focus="true"`:取第一个;CLI lint 可出重复警告(§14,可选)。

### 6.2 模态 trap(导航笼 + 选区限制)

uGUI 的 `Automatic` 导航**不认 Canvas 排序/raycast 遮挡**——方向键能从模态"逃"到背后的按钮(指针被 backdrop 吃掉,但键盘/手柄不会)。实测病征:背后一页的某按钮在屏幕坐标上比模态内的相邻按钮更"贴"焦点控件,几何搜索就选了它 → 选区逃出模态 → 守卫吸回栈顶模态首个可聚焦控件 → **焦点黏在首按钮、方向键到不了同模态的其它按钮**。两层防护:

- **导航笼(主):** 模态绑定完成后由 `UI.Modal` 调 `Screen.ConfineNavigationToSelf` → `ExplicitNavigationResolver` 的 confine 分支把模态内每个可聚焦控件转成 `Explicit` 导航,四向邻居用**限定在模态子树候选内**的几何打分算出(复刻 uGUI `FindSelectable`/`GetPointOnRectEdge` 的评分,但只在子树里挑)。于是方向键从根上到不了模态外,且模态内相邻控件可靠互达。几何须在布局稳定后取,故 confine 分支先 `Canvas.ForceUpdateCanvases()`(开屏时叠加 canvas 尚未定尺寸 + HStack 未排版);resize/ReSolve 经 `Screen.NavConfineRoot` 重算。
- **吸回(兜底):** 导航控制器维护"当前限制根" = 栈顶模态 Screen 根(复用 `UI.Modal._stack` + `RefreshTopListener` 同款"仅栈顶生效");每帧校验 `currentSelectedGameObject` 在限制根子树内,逃逸则吸回。装笼后正常不再触发,留作兜底。

### 6.3 关闭还原

- 模态打开时,在其 `Slot` 上记录打开前的 `currentSelectedGameObject`。
- 模态关闭(`RemoveSlot`)后,`SetSelectedGameObject(记录值)`(若仍存活);随后由 §6.2 守卫保证选区落在新的栈顶/底屏。

## 7. 显式导航覆盖

均为可交互控件上的私有 `[UIAttr]`(逃生阀,默认不写 = `Automatic`):

- **`nav="none"`** → `Selectable.navigation = { mode = None }`。uGUI `FindSelectable` 跳过 `mode==None` 的候选,故该控件**既不向外导航、也不被导航到**(整体跳过),一行达成用户意图。
- **`navUp` / `navDown` / `navLeft` / `navRight="id"`** → 切 `Navigation.Mode.Explicit`,把指定方向接到目标控件的 `Selectable`(`id` 经 Screen 的 id-path 解析,同 `Screen.Get`,支持 ScopedIds)。
  - **未指定方向自动补 `Automatic` 邻居**:Explicit 模式下未填的方向会"死路"。故构建后解析器对有覆盖的控件,先用 `FindSelectableOnUp/Down/Left/Right`(几何)算出各方向 Automatic 邻居,再用 XML 覆盖项替换指定方向,合并后写入 Explicit。**不会因只写了 `navUp` 就锁死其余三向**。
  - 时机:布局稳定后(Screen 打开末尾)运行一遍;`ReSolve`/Variant/resize 后重算(几何邻居可能变)。

实现:`Runtime/Application/Navigation/ExplicitNavigationResolver.cs`,在 Screen 打开/重排末尾对带 `nav*` 的节点求解。

## 8. C# API

`Runtime/Application/UI.Navigation.cs`:
```csharp
public static partial class UI
{
    public static class Navigation
    {
        // 一行装好:确保 EventSystem + InputSystemUIInputModule(新系统),
        // 启用导航模式检测/选区限制/光标。幂等。无新系统时 no-op + 一次性警告。
        // defaultCursor: 全局默认光标的 src key;null = 用内置默认。
        public static void Enable(string defaultCursor = null);

        public static bool IsEnabled { get; }
        // internal: Mode 信号(§3)、模式翻转重算入口、限制根(§6.2)
    }

    // 便捷别名,对齐 UseResourcesResolver 命名风格
    public static void UseGamepadNavigation(string defaultCursor = null);  // = Navigation.Enable
}
```
`Runtime/Application/Screen.cs`:
```csharp
public partial class Screen   // 经 IScreen 暴露
{
    public void Focus(string idPath);   // = EventSystem.SetSelectedGameObject(Get(idPath).GameObject)
}
```

用法:
```csharp
UI.UseResourcesResolver("UI");
UI.UseGamepadNavigation();                 // 内置手指
// 或 UI.UseGamepadNavigation("UI/cursors/myhand");
var screen = await UI.Open("MainMenu");
screen.Focus("playBtn");                    // 可选:代码指定初始焦点
```

`ResetForTests` 清空导航启用态/模式/限制根/全局默认。

## 9. 输入系统门控(仅新 Input System)

- 整套导航代码包在 `#if ENABLE_INPUT_SYSTEM`(Unity 在 `com.unity.inputsystem` 存在时自动定义,无需自定义 symbol)。
- `#else`(仅旧 Input Manager 或两者皆无):`UI.UseGamepadNavigation` no-op + **一次性** `Debug.LogWarning`("手柄/键盘导航需要 New Input System 包");既有旧输入用户不被破坏,只是没手柄导航。
- 不进 `package.json` `dependencies`(保持可选,沿用 `PROMPTUGUI_HAS_*` 可选门控哲学)。
- `EventSystem` 仍归 host;但 `Enable` 提供一行兜底:场景无 EventSystem 时创建一个并挂 `InputSystemUIInputModule`(默认 Move/Submit/Cancel/Point/Click actions)。这是**显式 helper**,不违背"库默认不创建 EventSystem"。
- WebGL:全 Update/LateUpdate + LitMotion + Awaitable,InputSystem 本身 WebGL 兼容,无线程。

## 10. 生命周期与时序

```
UI.UseGamepadNavigation(defaultCursor)
  └─ 确保 EventSystem(+InputSystemUIInputModule)、启动模式检测、置 IsEnabled、记下全局默认光标

UI.Open(screen) / Modal.Open
  ├─ 实例化控件树(各 nav*/focus 属性随 ControlAttributeApplier 落到 Selectable)
  ├─ ExplicitNavigationResolver 求解 nav* 覆盖(§7,布局末尾)
  ├─ <FocusCursor> 子树(或全局默认)→ overlay + FocusCursorView
  ├─ 选初始焦点(focus="true" 或文档序第一个)→ SetSelectedGameObject
  └─ (模态)Slot 记录打开前选区

每帧(导航控制器 + FocusCursorView.LateUpdate)
  ├─ 设备事件 → 更新 UI.Navigation.Mode;翻转则重算当前选中控件(Focused↔Normal)
  ├─ 选区限制守卫:有模态时 currentSelected 必须在栈顶模态内,逃逸则吸回
  └─ 光标:Directional 且选中在本屏 → 定位+像素吸附+滑动+显示;否则隐藏

Modal 关闭(RemoveSlot)
  └─ SetSelectedGameObject(记录的打开前选区)
```

## 11. 边界与防御

| 场景 | 行为 |
|---|---|
| 未调 `UseGamepadNavigation` | 全部新行为静默关闭;`Mode` 恒 `Pointer`;光标不实例化;`MapTransient` 的 `Focused` 被门控折回 `Normal`(等同今天) |
| 项目无 New Input System | `UseGamepadNavigation` no-op + 一次性警告 |
| 鼠标点完控件 | 仍 `Selected`,但 `Mode==Pointer` → 折回 `Normal`,不粘高亮(与今天一致) |
| 手机竖屏(纯触屏) | 触摸保持 `Pointer` 模式 → 手指永不出现、焦点不显示 |
| 屏上无任何可聚焦控件 | 不设选区;光标隐藏 |
| `nav="none"` 控件 | 不被导航到、也不向外导航(`FindSelectable` 跳过) |
| 只写 `navUp` | 其余三向自动补 Automatic 邻居,不死路 |
| `navUp="不存在的id"`(typo 或目标在未激活 variant 块内) | 该方向静默回落几何邻居(运行时**不抛**);未激活块内的目标在激活(ReSolve)时自愈;CLI lint(`PUI-NAV-UNKNOWN-TARGET`)提前抓 typo(§14) |
| 模态叠模态 | 限制根 = 栈顶;逃逸吸回栈顶;逐层关闭逐层还原 |
| Carousel(非 Selectable) | v1 不参与手柄导航(§13);其内部"上一张/下一张"靠自身既有交互 |
| resize / Variant 切换 | 光标逐帧跟随;`ExplicitNavigationResolver` 重算;选区不变(`currentSelectedGameObject` 是 GO 引用,ReSolve 不重建 GO) |
| 焦点控件被销毁(BindItems 重建) | `currentSelectedGameObject` 变空 → 光标隐藏;守卫下一帧吸回限制根初始焦点 |

## 12. 测试(TDD——红先行)

**EditMode**(`UI.ResetForTests` + fake resolver,仿 `DocumentLoaderTests`;不依赖真实输入):
1. `MapTransient`:ordinal 3 → `Focused`(回归:旧行为是 `Normal`);其余 ordinal 不变。
2. 复合 + 模式门控:`Mode=Pointer` 时 transient `Focused` → `Current=Normal`(isOn=false)/`Selected`(isOn=true);`Mode=Directional` 时 → `Current=Focused`;`Disabled`/`Pressed` 始终压过 `Focused`。
3. `StateTintReactor.MultiplierFor(Focused) == MultiplierFor(Hover)`(同源);hover 未设时 `Focused` 为单位白(不变色)。
4. `ExplicitNavigationResolver`:`nav="none"` → `mode==None`;`navUp="id"` → `mode==Explicit` 且 up 接对目标、其余三向 = 几何邻居;`navUp` 指 ScopedId 正确解析;`navUp` 指不存在 id → 静默回落几何(运行时**不抛**,Mode 仍 `Explicit`);`navDown` 指未激活 variant 块目标不崩、激活时自愈。
5. 初始焦点:`focus="true"` 选中该控件;无标记选文档序第一个可聚焦;多标记取第一个。
6. 选区限制守卫(纯逻辑):给定限制根 + 一个根外 GO,断言"逃逸"判定为真并返回吸回目标。
7. `FocusCursorView` 定位(给定目标 rect + overlay 尺寸 + side/offset):光标落点正确、像素吸附为整数。
8. `<FocusCursor>` 子树被 hoist 进 overlay(非布局子);无 `<FocusCursor>` 时取全局默认。

**PlayMode**(仿 `CarouselPlayTests`;真实 EventSystem + `InputSystemUIInputModule`,用 InputSystem TestFramework 注入设备事件):
9. 手柄/键盘移动选区 → `Mode` 翻 Directional、焦点控件显 hover tint、光标跟到该控件;鼠标移动 → `Mode` 翻 Pointer、tint 消失、光标隐藏。
10. 模态打开 → 选区被 trap 在模态内(方向键到不了背后控件);关闭 → 还原到打开前选区。
11. `navUp` 显式路由把选区送到指定控件。

> 注:PlayMode 用例需测试工程装 New Input System + 其 TestFramework;本仓 PlayMode runner 历史上偶发不稳(见项目记忆),失败先排查环境再判真伪。

## 13. 非目标(v1)

- **焦点专属视觉**:`focusColor` / `focusModulate` / `focusSprite` / `state-focused` 触发器 / `<Show on="state-focused">`(已留扩展点 §4.3/4.4;焦点暂复用 hover)。
- **Carousel 纳入手柄导航**(需给它包 Selectable 代理,单独小 spec)。
- **旧 Input Manager 支持**(用户明确只要新系统)。
- **每控件光标覆盖**(`focusCursorOffset` / `focusCursor="none"` 于单控件):`<FocusCursor side/offset>` 已覆盖全局;按控件微调留将来。
- **自定义"导航键位/确认取消按键重绑"**:用 `InputSystemUIInputModule` 默认 actions;host 要改自行配置 module。
- **声明式焦点组 / `navOrder` 遍历序 / 完整四向 nav graph DSL**:用 `Automatic` + `nav*` 逃生阀,不做完整声明系统(用户选"渐进式"档)。

## 14. SKILL 更新(同 PR,英文)

- `authoring-promptugui-xml`:
  - 新增 `<FocusCursor>` 元素(`side`/`offset`、子树即光标视觉、可含 `<Animation>`、模板化与全局默认);
  - 通用可交互属性 += `focus`(初始焦点)、`nav`(`none`)、`navUp`/`navDown`/`navLeft`/`navRight`(显式覆盖,值为 id);
  - 大概率新开 `reference/navigation.md`(深水区:导航模式、模态 trap、显式覆盖的自动补邻居、内置默认光标),主文档留行 + 指针。
- `scripting-promptugui-csharp`:`UI.UseGamepadNavigation(defaultCursor)` / `UI.Navigation.Enable`+`IsEnabled` / `Screen.Focus(idPath)`;`InteractState` 新增 `Focused` 值(复用 hover、Directional-only 的说明);注明"仅新 Input System"。
- CLI lint(可选,`Runtime/Core/Lint/`):`PUI-NAV-UNKNOWN-TARGET`(`nav*` 指向不存在 id)、`PUI-NAV-ON-NON-SELECTABLE`(`nav*`/`focus` 落在非可交互控件)、`PUI-FOCUS-DUP`(同屏多个 `focus="true"`)。按价值择一两条进 v1。

## 15. 风险与回滚

| 风险 | 缓解 |
|---|---|
| 解封 `Selected→Focused` 让鼠标点完粘高亮 | §3 模式门控:Pointer 模式照旧折回 `Normal`;回归测试 #2 覆盖 |
| `InteractState` 追加值漏改某 `switch`(穷举) | 编译/测试驱动:`StateTintReactor`/`<Show>`/triggers 都 `switch` 它,缺分支编译告警或测试失败;新值置末尾、`default` 兜底 |
| uGUI `FindSelectableOnX` 在布局未稳时算错邻居 | `ExplicitNavigationResolver` 在 Screen 打开/重排**末尾**跑;resize/ReSolve 重算 |
| 模态方向键逃到背后控件 / 焦点黏首按钮 | §6.2 导航笼:模态内控件转 `Explicit` + 子树内几何邻居,从根上不外溢、模态内可靠互达;每帧吸回降为兜底(装笼后正常不触发) |
| 设备检测误判(同时插手柄又动鼠标) | 以"最近一次事件设备"为准,逐帧更新;边界是用户主动切设备,符合直觉 |
| `<FocusCursor>` hoist 进 overlay 与 scale/PixelSnap 交互 | 复用 `InstantiateNode` 的 `_dynamicSubtrees` 既有 scale 路径 + `PixelSnap`;EditMode #7/#8 覆盖定位与 hoist |
| New Input System 未装时整个特性编译/运行 | `#if ENABLE_INPUT_SYSTEM` 全包;`#else` no-op + 一次性警告;不进硬依赖 |
| PlayMode 输入注入需 InputSystem TestFramework | 测试工程预置;runner 不稳时先排查环境(项目记忆) |
