# `<TabBar>` / `<Tab>` 控件设计

**日期**: 2026-05-27
**状态**: 设计阶段（待 review，未进入实施）
**作用域**:
1. 新增 `Runtime/Controls/TabBar.cs`（HorizontalLayoutGroup + 私有 ToggleGroup 容器）
2. 新增 `Runtime/Controls/Tab.cs`（UnityToggle + 可选 selected overlay + label + 可选 icon）
3. `Runtime/Application/BuiltinPrimitives.cs` 注册 `TabBar` / `Tab`
4. `Runtime/Application/ScreenInstantiator.cs` 把 `"TabBar"` 加进 `selfIsLayoutGroup` 集合
5. 新增 `Runtime/Core/Lint/TabRules.cs`（Tab 必须是 TabBar 直接子；TabBar 子只能是 Tab）
6. `Runtime/Core/Lint/IRWalker.cs` 入口 self-check 加 `Tab` / `TabBar` 分支
7. `authoring-promptugui-xml` SKILL.md 新增 `<TabBar>` / `<Tab>` 行 + 一节用例
8. `scripting-promptugui-csharp` SKILL.md 加 `BindItems<Tab,T>` / `OnSelectionChanged` / `SelectedTab` 用法
9. 主 spec `2026-05-07-promptugui-description-language-design.md` §5（控件表）追加两行

**依赖**: 无（复用 `Toggle` 已用的 `UnityEngine.UI.ToggleGroup`、`ScrollList` 的 `BindItems` 模式、`ProceduralBuilders` 共享图层）

---

## 1. 背景

当前作者想做"页卡切换"只能这么写：

```xml
<HStack>
  <Toggle group="page" isOn="true" text="编辑"/>
  <Toggle group="page" text="帮助"/>
  <Toggle group="page" text="设置"/>
</HStack>

<Frame id="editor_panel">...</Frame>
<Frame id="help_panel">...</Frame>
<Frame id="settings_panel">...</Frame>
```

问题：

- `Toggle` 视觉硬编码 "左 20x20 checkmark + 右 label"（`Toggle.cs:39-72`），做不了"整条按钮换底图"的 tab 外观
- 没有 selected-sprite 通道：`Color` setter 改的是 bg，但不随 `IsOn` 联动
- Frame 显示/隐藏要 author 自己写 `R3.Subscribe` 把 `Toggle.OnValueChanged` 接到 `frame.GameObject.SetActive`，每个 Toggle 都重复一遍
- 没有"模板 + 动态添加"机制，要从代码批量加 tab 只能调用方手糊 GameObject

需要一个**容器型**控件 `<TabBar>` 统一管理：互斥 group、共享的 normal/selected 视觉、可选的 itemTemplate + BindItems；以及配套的**叶子**控件 `<Tab>` 暴露 `bind="frame_id"` 声明式联动一个 Frame，省掉手写订阅。

为什么不在 `Toggle` 上加 mode？已在 brainstorm 中评估并否决（参见对话）：Toggle 的 OnAttached 全部围绕 checkmark 布局编排，添加"full-button 模式" + selected 视觉属性会让 OnAttached 分支化、`GetNativeSize` 公式分裂、常量动态化；新做 Tab 几乎零回归风险、零共享代码损失（共享面只有 30 行的 font/ToggleGroup 模板）。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| TB-D1 | 不复用 Toggle | 新写 `Tab` 独立类，不继承 / 不扩 Toggle 模式 | Toggle 的 checkmark 布局硬编码；加 mode = OnAttached 分支化、常量动态化、`GetNativeSize` 公式分裂；新写 Tab 零回归 |
| TB-D2 | 容器形态 | `<TabBar>` 必填（不允许散在 HStack 里靠 group= 字符串维系） | 模板/动态添加/共享视觉都需要容器；和 ScrollList 形态对齐 |
| TB-D3 | 互斥载体 | `TabBar` OnAttached 在自己 GameObject 上 `AddComponent<UnityEngine.UI.ToggleGroup>`，私有；不入 `ToggleGroupRegistry` | TabBar 的 group 跟 Screen 级 `Toggle group="x"` 的语义不同（容器边界天然定义 group 范围），不要混用同一注册表 |
| TB-D4 | sprite 声明位置 | 仅 TabBar 暴露 `sprite` / `selectedSprite`，所有子 Tab 共享 | 一个 tab bar 内 99% 用例 tabs 长得一样；per-Tab override 留 v2 |
| TB-D5 | selected 视觉实现 | overlay 图层挂到 `UnityToggle.graphic` 通道，由 Unity 按 isOn 自动显隐 | 复用 UnityToggle 内置机制；不写 isOn listener；transition 默认 `None`（即时切换），未来要 fade 在一个地方改 |
| TB-D6 | pressed 视觉 | UnityToggle/Selectable 内置 color tint（默认 pressed 色 ≈ 0.78 灰），不暴露 attr | 跟 Btn 一致；author 不需要为 tab 单独学一套；要自定义可后续加 |
| TB-D7 | `allowSwitchOff` | **不暴露**，固定 `false` | 99% 用例总有一个 tab 选中；需求 #4 "按钮模式"靠"不写 selectedSprite"实现视觉退化，互斥仍在 |
| TB-D8 | 初始选中 | 解析所有 Tab 后，若没有任何 Tab `isOn="true"` → TabBar 自动把第一个 Tab 置 `IsOn=true` | 避免 Unity ToggleGroup 在 `allowSwitchOff=false` 下首帧仍无选中 + 所有 bind frame 显示的反直觉行为 |
| TB-D9 | Bind 解析时机 | Tab.Bind setter 只存字符串；首次 `OnValueChanged` fire 时调 `Screen.Get<Frame>(id)` 缓存 ref；找不到 → warn 一次 + 后续静默 | lazy 避开属性 apply 顺序问题；缓存避免每次 toggle 都查表 |
| TB-D10 | Bind 失败处理 | warn 一次（设置 `_bindId=null` 防重复 warn），后续静默；找到但不是 Frame → 也 warn | 显示控件失联不应崩 |
| TB-D11 | itemTemplate 默认 | 不写 = `"Tab"`（直接实例化 Tab 类） | 90% 动态场景 tabs 都长得一样，免去写 `<Template name="X"><Tab/></Template>` 样板 |
| TB-D12 | BindItems 重载 | 提供两个：`Action<Tab,T>`（默认 Tab 模板免泛型）+ `Action<TSlot,T>`（自定义 Template，与 ScrollList 完全一致） | 90% 场景 author 直接 `(tab, m) => tab.Text = m.Name`；复杂场景退回通用形态 |
| TB-D13 | 静态 XML 子 vs BindItems | 与 ScrollList 一致：BindItems 调用 = Dispose 现有所有 Tab + 重建；混用 BindItems 赢 | 心智一致；不引入新规则 |
| TB-D14 | TabBar 是否 layout group | 是，加进 `ScreenInstantiator.selfIsLayoutGroup` 列表；Tab 不能写 `anchor=` / `margin=`（被 HorizontalLayoutGroup 接管） | 跟 HStack 子项规则一致 |
| TB-D15 | TabBar 方向 | `direction="horizontal"`（默认） / `"vertical"`；同 ScrollList 命名 | 垂直 tab bar 稀少但有用；几乎免费（换 LayoutGroup 类型） |
| TB-D16 | 选中事件层级 | Tab 暴露 `OnValueChanged` / `OnSelected`；TabBar 暴露 `OnSelectionChanged: Observable<Tab>` | 静态 author 订阅个别 Tab；动态 BindItems 场景订阅 TabBar 拿当前 Tab 引用 |
| TB-D17 | OnSelectionChanged payload | `Observable<Tab>`（新激活的 Tab 引用，可能为 null） | author 拿到 Tab 后再走 `tabBar.SelectedIndex` 或自己维护 idx→model 映射；refs 比 idx 信息更多 |
| TB-D18 | Tab 不在 TabBar 下 | runtime `Debug.LogWarning` + Tab.\_toggle.group=null（仍能点，但无互斥）；lint warning | 不阻拦运行，便于 Editor 临时拖测试 |
| TB-D19 | TabBar 下混入非 Tab | runtime 不阻拦（HorizontalLayoutGroup 照常布局非 Tab 节点）；lint warning | 不破坏运行；author 知道后能修 |
| TB-D20 | Tab 自身是 layout group？ | 否；Tab 内部固定布局（bg/overlay/icon/label），不接 XML 子 | 跟 Btn / Toggle 一致；要复杂内容用 itemTemplate |
| TB-D21 | Variant override 范围 | 允许 override Tab 的 `text` / `isOn` / `bind` / `icon`；TabBar 上 `sprite` / `selectedSprite` / `direction` 也允许 | 都是值写入；切方向只是换 LayoutGroup（OnAfterApply 一次 reconcile） |

---

## 3. XML 形态

### 3.1 静态写法

```xml
<Screen name="Main">
  <TabBar id="topbar" anchor="top-stretch" height="40"
          sprite="ui:tab_normal" selectedSprite="ui:tab_selected"
          spacing="4">
    <Tab text="编辑" bind="editor_panel" isOn="true"/>
    <Tab text="帮助" bind="help_panel"/>
    <Tab text="设置" bind="settings_panel"/>
  </TabBar>

  <Frame id="editor_panel"   margin="40,0,0,0">...</Frame>
  <Frame id="help_panel"     margin="40,0,0,0">...</Frame>
  <Frame id="settings_panel" margin="40,0,0,0">...</Frame>
</Screen>
```

或包到 VStack 里：

```xml
<VStack>
  <TabBar id="topbar" height="40" sprite="..." selectedSprite="..."/>
  <Frame id="content">
    <Frame id="editor_panel">...</Frame>
    <Frame id="help_panel">...</Frame>
    <Frame id="settings_panel">...</Frame>
  </Frame>
</VStack>
```

（Frame 默认 anchor 是双轴 stretch，`Frame.cs:16-19`；Screen 根 / 普通 Frame 不挂 LayoutGroup，兄弟节点重叠是默认行为。）

### 3.2 button 模式（需求 #4）

不写 `selectedSprite` → overlay 不创建，selected 态视觉无反馈，但互斥/事件/Bind 全在：

```xml
<TabBar id="actions" sprite="ui:btn">
  <Tab text="保存"/>
  <Tab text="导出"/>
  <Tab text="删除"/>
</TabBar>
```

### 3.3 自定义 Template

```xml
<Template name="IconTab">
  <Tab id="tab">
    <!-- Tab 不接子，所以 Template 只是给 Tab 实例一个外层 id；icon 走 Tab.icon -->
  </Tab>
</Template>

<TabBar id="bar" itemTemplate="IconTab" sprite="..." selectedSprite="..."/>
```

> 注：itemTemplate 指向 Template 时，Template root 必须能 `Get<Tab>(...)` 到一个 Tab 实例（同 ScrollList itemTemplate）。

---

## 4. 属性表

### 4.1 `<TabBar>`

| 属性 | 取值 | 默认 | 作用 |
|---|---|---|---|
| `sprite` | sprite key | (none) | 所有子 Tab 的 normal bg；走 `UI.ResolveSprite` |
| `selectedSprite` | sprite key | (none) | 所有子 Tab 的 selected overlay；不写 → overlay 不创建（按钮模式，需求 #4） |
| `direction` | `horizontal` \| `vertical` | `horizontal` | 决定挂 `HorizontalLayoutGroup` 还是 `VerticalLayoutGroup` |
| `spacing` | float | `0` | LayoutGroup spacing |
| `padding` | `X` / `V,H` / `T,R,B,L` | (none) | LayoutGroup padding，同 ScrollList 解析 |
| `itemTemplate` | tag / Template 名 | `"Tab"` | BindItems 实例化的元素 tag；默认直接实例化 Tab 类 |

### 4.2 `<Tab>`

| 属性 | 取值 | 默认 | 作用 |
|---|---|---|---|
| `text` | string | `""` | label 文本，居中 |
| `isOn` | bool | `false` | 初始选中状态；多个 `true` → 后写的赢；都 false → TabBar 自动选第一个（TB-D8） |
| `bind` | Frame id | (none) | 同 Screen 内 Frame 的 id；选中 → `SetActive(true)`，未选中 → `SetActive(false)`；lazy 解析（TB-D9） |
| `font` | string | `"default"` | font type，同 Btn/Toggle |
| `fontSize` | int | `24` | TMP fontSize；跟 Btn 默认 24 对齐 |
| `icon` | sprite key | (none) | 可选 icon，居 label 左；不写则不创建 |

约束：
- `<Tab>` 不接受 XML 子元素；写了会 lint error（`PUI-TAB-CHILDREN`）。
- `<Tab>` 不能写 `anchor=` / `margin=`（TabBar 是 LayoutGroup 父，TB-D14）；lint error 同 HStack/VStack 子项规则。

---

## 5. C# API

### 5.1 `Tab`

```csharp
public sealed class Tab : Control
{
    [UIAttr, Preserve] public string Text { set; }
    [UIAttr, Preserve] public bool IsOn { get; set; }
    [UIAttr, Preserve] public string Bind { set; }
    [UIAttr, Preserve] public string Font { set; }
    [UIAttr("fontSize"), Preserve] public int FontSize { set; }
    [UIAttr(IsSprite = true), Preserve] public string Icon { set; }

    public Observable<bool> OnValueChanged { get; }
    public Observable<Unit> OnSelected { get; }   // 仅在 false → true 时 fire
}
```

### 5.2 `TabBar`

```csharp
public sealed class TabBar : Control
{
    [UIAttr(IsSprite = true), Preserve] public string Sprite { set; }
    [UIAttr(IsSprite = true), Preserve] public string SelectedSprite { set; }
    [UIAttr, Preserve] public string Direction { set; }   // "horizontal" | "vertical"
    [UIAttr, Preserve] public float Spacing { set; }
    [UIAttr, Preserve] public string Padding { set; }
    [UIAttr, Preserve] public string ItemTemplate { set; } // 默认 "Tab"

    public int Count { get; }
    public int SelectedIndex { get; }    // -1 表示无选中（仅在空列表场景出现，TB-D7 禁了 allowSwitchOff）
    public Tab SelectedTab { get; }
    public Tab GetAt(int index);

    public Observable<Tab> OnSelectionChanged { get; }   // 新激活的 Tab，可能为 null

    public IDisposable BindItems<T>(
        Observable<IReadOnlyList<T>> source,
        Action<Tab, T> bind);            // 默认 itemTemplate="Tab" 的简易重载

    public IDisposable BindItems<T, TSlot>(
        Observable<IReadOnlyList<T>> source,
        Action<TSlot, T> bind) where TSlot : class, IControl;   // 同 ScrollList 通用
}
```

### 5.3 用法示例

```csharp
// 静态 XML 已写好 3 个 Tab：
var bar = screen.Get<TabBar>("topbar");
bar.OnSelectionChanged.Subscribe(tab => Debug.Log($"selected: {tab?.Get<Text>("...")}"));

// 动态：
bar.BindItems(
    Observable.Return<IReadOnlyList<TabModel>>(new[] {
        new TabModel("编辑",  "editor_panel"),
        new TabModel("帮助",  "help_panel"),
        new TabModel("设置",  "settings_panel"),
    }),
    (tab, m) => { tab.Text = m.Name; tab.Bind = m.FrameId; });

// 手动切：
bar.GetAt(1).IsOn = true;     // 同时取消其他 Tab 的 IsOn
```

---

## 6. 程序化层级（固定）

### 6.1 TabBar

```
TabBar (RectTransform + ToggleGroup + HorizontalLayoutGroup | VerticalLayoutGroup + ContentSizeFitter?)
└── Tab[0], Tab[1], ... （直接挂在 TabBar 下，HorizontalLayoutGroup 自动排）
```

无 Content/Viewport 嵌套（不是 ScrollList，不滚动）。`ContentSizeFitter` 按是否有显式 size 决定挂不挂（与 ScrollList 决策一致）。

### 6.2 Tab

```
Tab (RectTransform + UnityImage[bg] + UnityToggle)
├── Overlay (RectTransform + UnityImage; 按需创建; UnityToggle.graphic 绑这个，按 isOn 自动 SetActive)
├── Icon    (RectTransform + UnityImage; 按需创建; raycastTarget=false; 居 label 左侧)
└── Label   (RectTransform + TMP_Text; 永远存在; raycastTarget=false)
```

- bg = TabBar.Sprite，targetGraphic（Selectable 用它做 pressed tint）
- Overlay = TabBar.SelectedSprite；TabBar 没设 SelectedSprite → 不创建该子节点
- Icon = Tab.Icon；不设 → 不创建
- Label 永远存在；居中（无 icon）或居 icon 右（有 icon）

---

## 7. 行为细节

### 7.1 互斥（mutex）

- **TabBar.OnAttached**: 加 `ToggleGroup` 组件到自己 GameObject，`allowSwitchOff = false`（TB-D7）
- **Tab.OnAttached**: 沿 `transform.parent` 向上找第一个带 `ToggleGroup` 的 ancestor；找到 → `_toggle.group = it`；找不到 → `Debug.LogWarning("Tab '{id}' has no <TabBar> ancestor; mutual exclusion disabled")`

### 7.2 初始 IsOn 同步（TB-D8）

属性 apply 完毕后，`TabBar.OnAfterApply()` 末段触发 `SyncInitialSelection()`。它要解决两个问题：

1. **未被选中的 Tab 对应的 Frame 仍是 active**——UnityToggle 只在 `isOn` 真正发生变化时 fire `onValueChanged`，所以"attr 写 false（=默认值）"的 Tab 永远不会触发 §7.3 的 `OnIsOnChanged(false)` 路径，对应 bind frame 还是 GameObject 默认的 `activeSelf=true`。
2. **全部 Tab 都没写 isOn=true 时无选中**——`allowSwitchOff=false` 的 Unity ToggleGroup 不会自动补选。

```csharp
internal void SyncInitialSelection()
{
    if (_tabs.Count == 0) return;

    // (1) 先 reconcile: 对所有未选中 + 有 bind 的 Tab，强制把对应 Frame 关掉
    foreach (var t in _tabs)
    {
        if (!t.IsOn) t.ForceSyncBindFrame(isOn: false);   // 等价于 OnIsOnChanged(false)，但即使 _bindId 已 resolved 过也跑
    }

    // (2) 没有任何 Tab 在线 → 自动选第一个（这一步会触发 Tab[0] 的 onValueChanged，进而 bind frame SetActive(true)）
    bool anyOn = false;
    foreach (var t in _tabs) if (t.IsOn) { anyOn = true; break; }
    if (!anyOn) _tabs[0].IsOn = true;
}
```

`Tab.ForceSyncBindFrame(bool)` 是 internal helper：跟 §7.3 的 `OnIsOnChanged` 主体一样调 lazy-resolve + `_boundFrame.GameObject.SetActive(isOn)`，但**不**经过 UnityToggle 的 onValueChanged 路径（避免 fire 多余的 `_changed` / `_selected` 事件给 author 订阅）。

### 7.3 Bind → Frame 同步

```csharp
// Tab 内部
private string _bindId;
private bool _bindResolved;
private Frame _boundFrame;

public string Bind { set => _bindId = value; }   // 只存

// UnityToggle.onValueChanged 监听
private void OnIsOnChanged(bool isOn)
{
    _changed.OnNext(isOn);
    if (isOn) _selected.OnNext(Unit.Default);
    if (_bindId == null) return;
    if (!_bindResolved)
    {
        _boundFrame = UI.OwnerScreenOf(this)?.Get<Frame>(_bindId);
        if (_boundFrame == null)
            Debug.LogWarning($"Tab.bind='{_bindId}' did not resolve to a Frame; ignoring.");
        _bindResolved = true;
        _bindId = null;       // 防重复 warn
    }
    if (_boundFrame != null) _boundFrame.GameObject.SetActive(isOn);
}
```

初始化序列：

1. `ScreenInstantiator` DFS 创建所有 GameObject（含 TabBar / Tabs / Frames）
2. `SetActive(true)` 整树（Screen.cs:128 注释）
3. 属性 apply 按 DFS post-order：Tab.Bind setter 存字符串，Tab.IsOn setter 写 `_toggle.isOn`
4. UnityToggle.PlayEffect → `onValueChanged` fire → 走 7.3 的 `OnIsOnChanged` → 第一次解析 Frame + SetActive
5. TabBar.OnAfterApply 末段调 `SyncInitialSelection()`（兜底 7.2）

> 注：步骤 4 仅在 isOn 真正发生变化时触发；attr 写 false（=默认值）的 Tab 不会走这条路径。这意味着"未被选中 + bind 不空"的 Frame 还保持 GameObject 默认的 active=true。这个 reconcile gap 由步骤 5 的 `SyncInitialSelection`（§7.2）补齐。

### 7.4 BindItems 重建

跟 ScrollList 模式一致（`ScrollList.cs:230-247`）：

```csharp
public IDisposable BindItems<T>(Observable<IReadOnlyList<T>> src, Action<Tab, T> bind)
    => BindItems<T, Tab>(src, bind);

public IDisposable BindItems<T, TSlot>(Observable<IReadOnlyList<T>> src, Action<TSlot, T> bind)
    where TSlot : class, IControl
    => src.Subscribe(items => Rebuild(items, bind));

private void Rebuild<T, TSlot>(IReadOnlyList<T> items, Action<TSlot, T> bind)
    where TSlot : class, IControl
{
    if (_factory == null) _factory = ResolveFactory(_itemTemplate ?? "Tab");
    ClearTabs();          // Dispose 所有现有 Tab（含静态 XML 写的）
    for (int i = 0; i < items.Count; i++)
    {
        var node = _factory(RectTransform);   // node 可能是 Tab 直接实例，也可能是 Template root
        var tab = node as Tab ?? node.Get<Tab>(...);   // 失败抛 InvalidCastException
        _tabs.Add(tab);
        // 把新 Tab 绑到自己的 ToggleGroup（Tab.OnAttached 已经走了一次，但 BindItems 期间 _factory 创建的也会走）
        if (node is TSlot typed) bind(typed, items[i]);
        else throw new InvalidCastException($"itemTemplate='{_itemTemplate}' instantiated {node.GetType().Name}, expected {typeof(TSlot).Name}");
    }
    SyncInitialSelection();
}
```

### 7.5 OnSelectionChanged 触发

TabBar 在每个 Tab.OnValueChanged 上挂内部订阅；当某 Tab 变 `IsOn=true` 时，`OnSelectionChanged.OnNext(that_tab)`；当 BindItems 清空且新列表为空时 `OnSelectionChanged.OnNext(null)`。

### 7.6 Tab 视觉布局（OnAttached 末段）

```
Bg: RT 满铺 Tab (anchor stretch + offsetMin=Max=0)
Overlay (按需): RT 满铺 Tab; UnityImage; 父 UnityToggle.graphic 指它
Icon (按需): RT anchor left-middle, sizeDelta (24,24), 偏移由 padding 决定
Label: RT 满铺 Tab（无 icon）或 offsetMin.x = iconWidth + gap（有 icon）；TMP 居中
```

Tab 不暴露 padding/font color attribute（v1 用 ProceduralBuilders 的默认值），跟 Btn 风格一致。

---

## 8. 边界 / 错误处理

| 场景 | 处理 |
|---|---|
| `<Tab>` 不在 `<TabBar>` 下 | lint warning `PUI-TAB-PARENT`；runtime LogWarning，`_toggle.group=null`（仍能 toggle，无互斥） |
| `<TabBar>` 下混入非 `<Tab>` 元素 | lint warning `PUI-TABBAR-CHILD`；runtime 不阻拦（HorizontalLayoutGroup 照常排版） |
| `bind="x"` 但 `x` 不存在 / 不是 Frame | 首次 OnValueChanged 时 LogWarning 一次，`_bindId=null` 防重复（TB-D10） |
| 多个 Tab 写 `isOn="true"` | 后写的覆盖（Unity ToggleGroup `allowSwitchOff=false` 标准行为） |
| 全部 Tab 都没写 `isOn` | TabBar.SyncInitialSelection 自动把第 0 个 Tab 置 IsOn=true（TB-D8） |
| BindItems 传空列表 | ClearTabs；`SelectedIndex=-1`；`OnSelectionChanged.OnNext(null)`；所有 bind 过的 Frame 保留最后状态（不主动复原；和 ScrollList 一致） |
| `itemTemplate` 指向不存在的 tag/Template | `ResolveFactory` 抛 `ParseException`（同 ScrollList） |
| `<Tab>` 包含子元素 | lint error `PUI-TAB-CHILDREN`；runtime "未知子节点" 路径 warn + 忽略 |
| `<Tab>` 写 `anchor=` / `margin=` | lint error `PUI-LAYOUTCHILD-*`（沿用 HStack 子项规则；TabBar 加入 selfIsLayoutGroup 后自动覆盖） |
| `<TabBar>` 自身嵌在 HStack / VStack 下 | 合法；TabBar 走 LayoutGroupChildRules 不允许 anchor/margin（与 HStack 嵌 HStack 一致） |
| Tab.Bind 指向的 Frame 在 BindItems 重建期间被某 Tab Dispose 触发 SetActive | 不会发生 —— Tab Dispose 不动 Frame，仅断 toggle 订阅；Frame 是 Screen 共享节点 |

---

## 9. Lint 规则

`Runtime/Core/Lint/TabRules.cs`（新文件）；`IRWalker.WalkNode` 入口 self-check 加 `Tab` / `TabBar` 分支；`ScreenInstantiator.InstantiateRecursive` 同源 `Debug.LogWarning`。

| Code | 触发条件 | 信息（节选） | 级别 |
|---|---|---|---|
| `PUI-TAB-PARENT` | `<Tab>` 的父不是 `<TabBar>`（也不是 itemTemplate 展开的 Template root 在 BindItems 阶段，这种情况 lint 不可见） | "Tab 必须是 TabBar 的直接子元素；当前父为 '{tag}'，互斥/共享视觉将失效。" | warning |
| `PUI-TABBAR-CHILD` | `<TabBar>` 子中存在非 `<Tab>` 节点（且非 Template invocation——这个由 TemplateExpander 处理） | "TabBar 期望子元素全部是 <Tab>；发现 '{tag}'，会被 HorizontalLayoutGroup 照常排版但语义不正确。" | warning |
| `PUI-TAB-CHILDREN` | `<Tab>` 自身包含子元素 | "Tab 是 leaf 控件，不接受子元素。用 text / icon 属性表达内容。" | error |
| `PUI-TABBAR-DIRECTION` | `direction` 不在 `horizontal` / `vertical` | "TabBar.direction 合法值: horizontal, vertical。" | error |
| `PUI-TAB-BIND-EMPTY` | `bind=""`（空字符串，非未设） | "Tab.bind 空字符串无意义；删除属性或填入 Frame id。" | warning |

runtime 一律 `Debug.LogWarning`；CLI `UIXmlLint` 按 level 决定 exit code。

> 注：`PUI-TAB-PARENT` / `PUI-TABBAR-CHILD` 用 warning 而非 error 是为了不阻断"父用 Template 间接展开成 TabBar"这种合法但 lint 静态看不出来的场景。

---

## 10. 实现要点

### 10.1 `Runtime/Controls/Tab.cs`（新文件）

骨架（详细 visual 布局逻辑放 plan）：

```csharp
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;
using UnityToggle = UnityEngine.UI.Toggle;

namespace PromptUGUI.Controls
{
    public sealed class Tab : Control
    {
        private UnityImage _bg;
        private UnityImage _overlay;   // 可能为 null
        private UnityImage _icon;      // 可能为 null
        private TMP_Text _label;
        private UnityToggle _toggle;
        private string _fontType = "default";
        private string _bindId;
        private bool _bindResolved;
        private Frame _boundFrame;
        private readonly Subject<bool> _changed = new();
        private readonly Subject<Unit> _selected = new();

        public override void OnAttached()
        {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultBtnColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);

            _toggle = GameObject.GetComponent<UnityToggle>() ?? GameObject.AddComponent<UnityToggle>();
            _toggle.targetGraphic = _bg;

            _label = ProceduralBuilders.AddText(RectTransform, "Label");
            _label.alignment = TextAlignmentOptions.Center;
            _label.raycastTarget = false;
            _label.fontSize = 24;
            var lrt = _label.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            ApplyFont();

            // overlay / icon 都是 lazy，由 TabBar.OnAfterApply 或 setter 触发
            _toggle.onValueChanged.AddListener(OnIsOnChanged);
            UI.Locale.Changed += ApplyFont;

            // 找父 TabBar 的 ToggleGroup
            var bar = FindAncestorTabBar();
            if (bar != null) _toggle.group = bar.InternalToggleGroup;
            else Debug.LogWarning($"Tab has no <TabBar> ancestor; mutex disabled.");
        }

        internal void EnsureOverlay(Sprite selectedSprite)
        {
            if (selectedSprite == null) return;
            if (_overlay == null)
            {
                _overlay = ProceduralBuilders.AddImage(RectTransform, "Overlay", raycast: false);
                var rt = _overlay.rectTransform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                _toggle.graphic = _overlay;
            }
            _overlay.sprite = selectedSprite;
            ProceduralBuilders.ApplyImageAutoSliced(_overlay);
        }

        internal void ApplyBgSprite(Sprite normalSprite)
        {
            if (normalSprite == null) return;
            _bg.sprite = normalSprite;
            ProceduralBuilders.ApplyImageAutoSliced(_bg);
        }

        // Text / IsOn / Bind / Font / FontSize / Icon attrs ... (同 Btn 风格)

        private void OnIsOnChanged(bool isOn) { /* 7.3 流程 */ }
        // ...
    }
}
```

> `ProceduralBuilders.ApplyImageAutoSliced(...)` 当前没这个 helper —— plan 阶段决定是抽出来还是 inline `img.type = img.sprite.border != Vector4.zero ? Sliced : Simple`。

### 10.2 `Runtime/Controls/TabBar.cs`（新文件）

```csharp
public sealed class TabBar : Control
{
    private ToggleGroup _group;
    private LayoutGroup _layout;
    private string _direction = "horizontal";
    private float _spacing;
    private string _padding;
    private string _itemTemplate;
    private Sprite _sprite;
    private Sprite _selectedSprite;
    private Func<RectTransform, IControl> _factory;
    private readonly List<Tab> _tabs = new();
    private readonly Subject<Tab> _selectionChanged = new();
    private IDisposable _activeBindSub;

    internal ToggleGroup InternalToggleGroup => _group;

    public override void OnAttached()
    {
        _group = GameObject.AddComponent<ToggleGroup>();
        _group.allowSwitchOff = false;
        ApplyDirection();
    }

    private void ApplyDirection() { /* 同 ScrollList.ApplyDirection 但无 viewport */ }

    [UIAttr(IsSprite = true), Preserve]
    public string Sprite { set { _sprite = UI.ResolveSprite(value); PushVisualToTabs(); } }

    [UIAttr(IsSprite = true), Preserve]
    public string SelectedSprite { set { _selectedSprite = UI.ResolveSprite(value); PushVisualToTabs(); } }

    // direction / spacing / padding / itemTemplate setters

    internal override void OnAfterApply()
    {
        // 收集子节点中的 Tab
        CollectStaticTabs();
        PushVisualToTabs();
        SyncInitialSelection();
        WireTabSubscriptions();
    }

    private void CollectStaticTabs() { /* 遍历 children, 找 Tab; 同时 hookup 到 ToggleGroup（若 Tab.OnAttached 没找到）*/ }
    private void PushVisualToTabs() { foreach (var t in _tabs) { t.ApplyBgSprite(_sprite); t.EnsureOverlay(_selectedSprite); } }
    private void SyncInitialSelection() { /* 7.2 */ }
    private void WireTabSubscriptions()
    {
        _activeBindSub?.Dispose();
        var d = new CompositeDisposable();
        foreach (var t in _tabs)
            t.OnValueChanged.Where(on => on).Subscribe(_ => _selectionChanged.OnNext(t)).AddTo(d);
        _activeBindSub = d;
    }

    // BindItems / Count / SelectedIndex / SelectedTab / GetAt / OnSelectionChanged
}
```

### 10.3 `Runtime/Application/BuiltinPrimitives.cs`

```csharp
reg.Register<TabBar>("TabBar", null);
reg.Register<Tab>("Tab", null);
```

### 10.4 `Runtime/Application/ScreenInstantiator.cs`

```csharp
// L237 附近
var selfIsLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid" or "TabBar";
```

self-check 入口（创建 control 之前的 lint 同源块）追加 Tab / TabBar 分支调用 `TabRules`。

### 10.5 `Runtime/Core/Lint/TabRules.cs`（新文件）

跟 `MaskAttributeRules` 同模式：static class + `CheckTab(ElementNode)` / `CheckTabBar(ElementNode)` 返回 `IEnumerable<LintIssue>`。

### 10.6 `Runtime/Core/Lint/IRWalker.cs`

`WalkNode` 入口 self-check 追加：

```csharp
else if (node.Tag == "Tab")
    foreach (var issue in TabRules.CheckTab(node)) yield return issue;
else if (node.Tag == "TabBar")
    foreach (var issue in TabRules.CheckTabBar(node)) yield return issue;
```

---

## 11. 跟现有 spec / SKILL 的整合点

### 11.1 主 spec `2026-05-07-promptugui-description-language-design.md`

§5（控件表）追加两行：

> `<TabBar>` | Tab 容器，私有 ToggleGroup + HorizontalLayoutGroup；共享 normal/selected sprite；支持 itemTemplate + BindItems | RectTransform + ToggleGroup + LayoutGroup（详见 [`2026-05-27-tabbar-design.md`](2026-05-27-tabbar-design.md)）
>
> `<Tab>` | TabBar 子项；UnityToggle + 居中 label + 可选 icon + 可选 selected overlay；`bind="frame_id"` 选中态联动 Frame 显隐 | RectTransform + UnityImage + UnityToggle |

### 11.2 `authoring-promptugui-xml/SKILL.md`

1. Built-in primitives 表追加两行（attrs 列见 §4）
2. 新增 "Tabs" 小节，含：
   - 静态用例（§3.1）
   - button 模式（§3.2，需求 #4）
   - 自定义 Template（§3.3）
   - lint codes 列表（§9）
   - "bind 不写 → 用户自己用 OnSelected 接业务"的一句话
3. Quick reference 末尾加一行：
   > `<TabBar sprite="..." selectedSprite="..."><Tab text="A" bind="frame_a" isOn="true"/>...</TabBar>` —— 私有互斥 group；bind 联动 Frame 显隐；动态走 BindItems

### 11.3 `scripting-promptugui-csharp/SKILL.md`

- `BindItems` 段补：TabBar 同 ScrollList 风格的两个重载；默认 `(Tab, T)` 简易重载免泛型
- 新一句：`tabBar.OnSelectionChanged.Subscribe(tab => ...)` —— TabBar 级事件给动态场景；静态场景用 `tab.OnSelected` per-Tab 订阅
- `tab.IsOn = true` 触发互斥 + bind 的 Frame 自动 SetActive，无需手写 `frame.GameObject.SetActive(...)`

### 11.4 addressables skill 无关，不动。

---

## 12. Out of Scope

- **per-Tab sprite override** —— TB-D4 留 v2；99% 用例 tabs 长得一样
- **`allowSwitchOff=true`（无任何 tab 选中也合法）** —— TB-D7 砍掉；按钮模式靠"不写 selectedSprite"实现视觉退化
- **垂直方向的"侧边栏 tab"完整视觉规范** —— TB-D15 允许 `direction="vertical"` 但 sprite 比例 / label 旋转之类由作者自己处理
- **Fade transition** —— TB-D5 固定 UnityToggle.Transition.None；要 fade 后续在 TabBar 加 attr
- **Tab 关闭按钮 / drag-reorder** —— v1 不做
- **嵌套 TabBar（tab 里再开 tab）** —— 不阻拦，但 bind 一次只能联动一个 Frame；嵌套语义由 author 用 OnSelected 自定义
- **动画过渡 frame 显隐** —— bind 是直接 SetActive；要淡入淡出用 `<Animation>` 控件或自己接 OnSelected 写
- **Tab 内复杂内容（icon + 多行 + badge）** —— Tab 是 leaf，不接子。复杂内容用 itemTemplate + 用户 Template 把 Tab 包装在外层 Frame 里

---

## 13. 风险与回滚

| 风险 | 缓解 |
|---|---|
| Tab.OnAttached 找父 TabBar 时父链还没建好 | OnAttached 是 DFS post-order，Tab 创建时父 TabBar 已 attach；如果遇到 nested 场景仍找不到 → LogWarning 不崩 |
| 多 Tab 都 `isOn="true"` 时 attribute apply 顺序导致状态抖 | 由 `_toggle.group` 的 `allowSwitchOff=false` 兜底；setter 顺序最终只有一个 true；UnityToggle 自动处理 |
| BindItems 重建期间旧 Tab Dispose 时它正在播 UnityToggle.PlayEffect | Dispose 先解订阅再销 GameObject，PlayEffect 协程被 GameObject Destroy 自动取消 |
| `OnSelectionChanged` 在 BindItems 清空时是否要 fire null | 是，TB-D17 明确：清空后 SelectedTab=null + fire null；订阅者要 handle null |
| TabBar 拖到 Scroll View 里 | 合法但 ToggleGroup 没问题；Tab.OnAttached 仍找得到 TabBar（ScrollList 是它祖先而非中间 ToggleGroup） |
| 父 TabBar 被 Destroy 时 ToggleGroup 跟着没 | UnityToggle.group 引用悬挂 → Unity 内部按 null 处理（Selectable 健壮）；不影响 Player |
| `<Tab>` 写 anchor="..." 想脱出 LayoutGroup | TB-D14 + lint 阻断；HorizontalLayoutGroup 仍会覆盖；author 看 lint 修 |
| `bind` 指向的 Frame 在另一个 Variant 中被 SetActive | Tab 的 SetActive 与 Variant 的 SetActive 都直接写 GameObject.activeSelf，最后写的赢；author 需要意识到这种冲突（doc 一句提示） |
| `OnAfterApply` 比单 attr setter 晚 → BindItems 在 attr apply 之前调用（很罕见，必须在 user code 手动调） | 文档说明：BindItems 应在 `screen.Get<TabBar>(...)` 拿到后调，那时 `OnAfterApply` 已跑完；Rebuild 内会再调 SyncInitialSelection 兜底 |
| XSD 不自动更新 | 跟所有新 `[UIAttr]` 一样手动 `Tools → PromptUGUI → Schema → Generate XSD`；CLAUDE.md 已说明 |
