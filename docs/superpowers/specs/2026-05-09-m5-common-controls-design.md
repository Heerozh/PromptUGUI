# M5 常用控件设计：Toggle / Slider / Dropdown / ScrollList

**日期**：2026-05-09
**状态**：设计阶段（待 review，未进入实施）
**作用域**：四个常用控件的 XML 语法 / 通用属性 / C# API / Sample；主 spec §10 + §5 增量；SKILL.md 增量
**依赖**：基础设计 [`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §5（内置原语）/ §9.4（事件流约束）/ §9.5（数据推送）；i18n [`2026-05-08-i18n-fonts-design.md`](2026-05-08-i18n-fonts-design.md)（locale 触发 ReSolve）

---

## 1. 背景与目标

主 spec §12 把 M5 列为"内置 ScrollList / Toggle / Slider / Dropdown 自定义控件参考实现"。i18n（M5a/M5b）与 Icon 系统已先后落地并自成 spec；本 spec 解决 M5 仍欠的最后一笔——四个常用 UI 控件的参考实现。

**为什么不再当成"自定义控件示范"放 Samples**：

- 当前 `<Btn>` 已经走的是**程序化构造视觉 + 暴露 sprite/color 属性**的模式（无 Prefab）。Toggle/Slider/Dropdown 都能复用这一模式，没有非要 Prefab 才能实现的部分；ScrollList 同理。
- 主 spec §10 当年说"不开"是基于"像素游戏样式高度差异化"的视觉关切。但只要程序化版本足够中性 + 暴露样式属性 + 留 fork 入口，差异化由作者继承重写解决。
- 与 Btn 同一目录、同一注册路径，作者写 XML 与查 SKILL/XSD 的心智一致。

**目标**：

1. 四个控件 XML 写法尽量贴近自然语义，落到与 §5 内置原语同一栏目里
2. 全部走 R3 `Observable<T>`（与 §9.4 硬约束一致）
3. 数据走 `BindItems` / `BindOptions` 推送通路（与 §9.5 一致）
4. 与 i18n 透明集成：Toggle/Dropdown 的文本及选项走 `TrResolver`，locale 切换通过既有 `Variant.Changed → Screen.ReSolve` 通路重译
5. 与 Variant 透明集成：四个控件的所有 `[UIAttr]` 属性都能被 `attr.var` 覆盖
6. 主 spec §10 / §5 与 SKILL.md 同步——这是 M5 的"对外公告"环节

**显式不做**：

- ❌ ScrollList 虚拟化（v1 非虚拟化；公开 `BindItems` API 形状预留升级空间）
- ❌ Dropdown 用 XML 子节点声明选项（与 §9.5 推送原则不一致）
- ❌ ScrollList 的 identity-stable diff / 增量更新（v1 全量重建）
- ❌ ScrollList 的 GridLayout 模式（v1 仅 vertical / horizontal stack；网格场景作者用现有 `<Grid>` 自己拼）
- ❌ Toggle 的 `<ToggleGroup>` 父标签（用 `[UIAttr] string Group` 字符串键即可）
- ❌ 滑块上叠加数值文本气泡这种 UX 糖（作者用 R3 自己接到外层 Text）

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| M5-D1 | 装载方式 | 进 `Runtime/Controls/`，启动随 BuiltinPrimitives 自动注册 | 与 Btn 同路径；同一 SKILL / 同一 XSD；无 opt-in 模板代码 |
| M5-D2 | 视觉资产 | 程序化构造（无 Prefab） | 复用 Btn 模式；样式留 `sprite` / `color` 等属性 |
| M5-D3 | 事件接口 | 一律 `Observable<T>` | 主 spec §9.4 硬约束 |
| M5-D4 | ScrollList 虚拟化 | v1 非虚拟化 | 简化实现；BindItems API 不变即可后续升级 |
| M5-D5 | ScrollList 数据源 | `Observable<IReadOnlyList<T>>` | 与 §9.5 推送一致；订阅生命周期挂 Screen |
| M5-D6 | ScrollList 更新策略 | 全量重建 slot（v1） | 简单可读；非虚拟化前提下数百项可接受 |
| M5-D7 | Dropdown 选项源 | `BindOptions(Observable<IEnumerable<string \| DropdownOption>>)` | 与 BindItems 对称；无 XML 子标签 |
| M5-D8 | Toggle 互斥组 | `[UIAttr] string Group` + Screen 级 `ToggleGroupRegistry` | 不引入 `<ToggleGroup>` 父标签；跨嵌套都能拉同组 |
| M5-D9 | 双向绑定 | `IsOn` / `Value` / `Selected` 的 setter 触发 OnValueChanged | 与 uGUI 默认行为一致；不做 silent setter |
| M5-D10 | itemTemplate 解析 | Screen 实例化期解析为 `Func<Transform,IControl>`，命中 Control 类或 TemplateDef | 复用 ControlRegistry + LoadedDoc.Templates，错误统一为 ParseException |
| M5-D11 | i18n 接入 | Toggle.Text / Dropdown 选项 / ScrollList 任何子 Text 一律走 `TrResolver` | 与 Btn / `<Text>` 行为对齐 |
| M5-D12 | 主 spec §10 措辞 | 改成"默认开启的常用控件，复杂样式仍鼓励 fork" | 与本 spec 前提对齐；非破坏性 |

---

## 3. 完整可读的例子

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <!-- 装备槽位 Template，被 ScrollList 用作 itemTemplate -->
  <Template name="ItemSlot">
    <Param name="iconName" default=""/>
    <HStack height="48" spacing="8" padding="4">
      <Icon name="{{iconName}}" size="32x32"/>
      <Text id="label" size="20"/>
      <Frame width="0"/>
      <Text id="count" size="20"/>
    </HStack>
  </Template>

  <Screen name="Settings">
    <VStack id="root" anchor="center" size="480x600" spacing="16" padding="24">
      <Toggle id="muteAudio" group="audio">静音</Toggle>
      <Slider id="masterVol" min="0" max="1" value="0.8"/>
      <Dropdown id="quality"/>
      <ScrollList id="inv" itemTemplate="ItemSlot" anchor="stretch"/>
    </VStack>
  </Screen>
</PromptUGUI>
```

```csharp
var screen = UI.Open("Settings");

screen.Get<Toggle>("muteAudio").OnValueChanged
      .Subscribe(b => AudioMixer.Mute = b).AddTo(screen);

screen.Get<Slider>("masterVol").OnValueChanged
      .Subscribe(v => AudioMixer.Master = v).AddTo(screen);

var quality = screen.Get<Dropdown>("quality");
quality.BindOptions(Observable.Return(new[] { "Low", "Medium", "High" }))
       .AddTo(screen);
quality.OnSelected.Subscribe(QualitySettings.SetQualityLevel).AddTo(screen);

var list = screen.Get<ScrollList>("inv");
list.BindItems(player.Inventory, (IControl slot, Item item) => {
    slot.Get<Icon>("").Name = item.IconName;
    slot.Get<Text>("label").TextValue = item.Name;
    slot.Get<Text>("count").TextValue = $"x{item.Count}";
}).AddTo(screen);
```

---

## 4. Toggle

### 4.1 XML

```xml
<Toggle id="muteAudio"
        group="audio"          ← 同 group 名互斥；省略=独立
        isOn="true"            ← 初始值；缺省 false
        sprite="ui/check"      ← 选中态 graphic，可省
        color="#ffffff"
        font="default">
  静音                          ← §5.2 文本简写；走 TrResolver
</Toggle>
```

### 4.2 C# API

```csharp
public sealed class Toggle : Control
{
    [UIAttr] public string Text   { set; }   // 走 TrResolver；改 label
    [UIAttr] public string Font   { set; }   // 同 Btn 风格
    [UIAttr] public string Color  { set; }   // 背景色
    [UIAttr] public string Sprite { set; }   // 选中态 graphic
    [UIAttr] public bool   IsOn   { get; set; }
    [UIAttr] public string Group  { set; }   // 字符串键互斥组

    public Observable<bool> OnValueChanged { get; }
}
```

### 4.3 实现要点

- 程序化构造：`UnityEngine.UI.Toggle` + 背景 `Image` + checkmark `Image`（targetGraphic）+ 子 `TMP_Text` label
- `OnValueChanged` = `Subject<bool>`，订阅 `_toggle.onValueChanged`
- `Group` 通过 `ToggleGroupRegistry` 解析：每个 Screen 持有一个 `Dictionary<string, ToggleGroup>`；首次见 group 名时 lazily 创建一个空 GameObject 挂 `ToggleGroup`，绑给本 Toggle；Screen Dispose 时整体随 Screen 释放
- `Text` setter 通过 `TrResolver`（与 `Btn.Text` 一致），locale 切换时自动重译

---

## 5. Slider

### 5.1 XML

```xml
<Slider id="masterVol"
        min="0" max="1" value="0.5"
        wholeNumbers="false"
        direction="horizontal"   ← horizontal | vertical | reverse-horizontal | reverse-vertical
        color="#888888"
        sprite="ui/track"/>
```

### 5.2 C# API

```csharp
public sealed class Slider : Control
{
    [UIAttr] public float  Min          { set; }
    [UIAttr] public float  Max          { set; }
    [UIAttr] public float  Value        { get; set; }
    [UIAttr] public bool   WholeNumbers { set; }
    [UIAttr] public string Direction    { set; }
    [UIAttr] public string Color        { set; }
    [UIAttr] public string Sprite       { set; }

    public Observable<float> OnValueChanged { get; }
}
```

### 5.3 实现要点

- 程序化构造：`UnityEngine.UI.Slider` + Background + Fill (`Image`) + Handle (`Image`)
- `Direction` 字符串解析为 `Slider.Direction` enum；非法值 → ParseException
- 不内置数值文本气泡；作者要显示当前值用 R3 自己接外层 Text

---

## 6. Dropdown

### 6.1 XML

```xml
<Dropdown id="quality"
          value="0"            ← 初始选中索引；缺省 0
          color="#ffffff"
          sprite="ui/dropdown"
          font="default"/>
```

### 6.2 C# API

```csharp
public sealed class Dropdown : Control
{
    [UIAttr] public int    Value  { get; set; }
    [UIAttr] public string Color  { set; }
    [UIAttr] public string Sprite { set; }
    [UIAttr] public string Font   { set; }

    public Observable<int> OnSelected { get; }

    public IDisposable BindOptions(Observable<IEnumerable<string>> source);
    public IDisposable BindOptions(Observable<IEnumerable<DropdownOption>> source);
}

public readonly struct DropdownOption
{
    public readonly string Text;
    public readonly Sprite Icon;
    public DropdownOption(string text, Sprite icon = null) { Text = text; Icon = icon; }
}
```

### 6.3 实现要点

- 内部用 `TMP_Dropdown`（项目其他文本统一走 TMP）
- 选项 Text 走 `TrResolver`：locale 切换 ReSolve 时若 popup 打开则先 Hide → 重写 options → 还原；否则直接刷新选项缓存
- BindOptions 多次订阅：取 last source；前一个 IDisposable 由 caller 自己 Dispose（与 R3 标准用法一致）
- XML 不接受 `<Dropdown>` 子节点 v1；遇到子节点 → ParseException

---

## 7. ScrollList

### 7.1 XML

```xml
<ScrollList id="inv"
            anchor="stretch"
            direction="vertical"        ← vertical | horizontal；缺省 vertical
            spacing="4"
            padding="8"
            itemTemplate="ItemSlot"     ← 已注册 tag（Template 或 Control 类）
            sprite="ui/scroll-bg"
            color="#202020"/>
```

### 7.2 C# API

```csharp
public sealed class ScrollList : Control
{
    [UIAttr] public string ItemTemplate { set; }
    [UIAttr] public string Direction    { set; }
    [UIAttr] public float  Spacing      { set; }
    [UIAttr] public string Padding      { set; }
    [UIAttr] public string Color        { set; }
    [UIAttr] public string Sprite       { set; }

    public IDisposable BindItems<T, TSlot>(
        Observable<IReadOnlyList<T>> source,
        Action<TSlot, T> bind)
        where TSlot : class, IControl;

    public IDisposable BindItems<T>(
        Observable<IReadOnlyList<T>> source,
        Action<IControl, T> bind)
        => BindItems<T, IControl>(source, bind);
}
```

### 7.3 实现要点

- 程序化构造：宿主 GO 挂 `Image`（背景 + targetGraphic）+ `ScrollRect` + `Mask`；子节点 `Viewport`（带 `Mask` + `Image` 透明），其 child `Content` 挂 `VerticalLayoutGroup` 或 `HorizontalLayoutGroup` + `ContentSizeFitter`（PreferredSize）
- `Direction` 决定 LayoutGroup 类型 + ScrollRect.horizontal/vertical bool
- **itemTemplate 解析**：在 `ScreenInstantiator` 创建 ScrollList 时，按下序查询：
  1. 当前 `LoadedDoc.Templates` 里有同名 TemplateDef → 缓存预展开 `ElementNode`，工厂体调 `ScreenInstantiator.InstantiateNode(node, content)`
  2. `ControlRegistry` 里有同名 Control 类 → 缓存类型，工厂体走 `ControlRegistry.CreateInstance(tag, content)`
  3. 都未命中 → ParseException（与未知 XML 标签同级别错误）
- **更新策略 v1**：source 每次 emit → Dispose 现存 slot IControls + Destroy 其 GameObjects → 按新数据序列重建。简单线性。
- 公开 API 不变即可后续升级到虚拟化 / diff
- `Dispose`：取消订阅 + 销毁 slot + 主动 Dispose 各 slot IControl（与 Btn 等控件生命周期一致）

### 7.4 itemTemplate 与 ID 路径

itemTemplate 命中 TemplateDef 时，每个 slot 的根节点是 Template 展开的根（`<HStack>` 之类），其内部 id 走主 spec §7.4 的 ScopedIds：

```csharp
list.BindItems(items, (IControl slot, Item item) => {
    slot.Get<Text>("label").TextValue = item.Name;   // ItemSlot Template 内的 id="label"
});
```

itemTemplate 命中 Control 类时，slot 即该 Control 实例：

```csharp
list.BindItems(items, (ItemSlot slot, Item item) => {
    slot.Icon = item.IconName;
    slot.Count = item.Count;
});
```

---

## 8. 主 spec / SKILL 增量

### 8.1 主 spec 改两处

**§5 内置控件原语**表从 8 行扩到 12 行，新增四行：

| 标签 | 作用 |
|---|---|
| `<Toggle>` | 复选 / 单选（OnValueChanged: bool） |
| `<Slider>` | 数值滑块（OnValueChanged: float） |
| `<Dropdown>` | 下拉选择（OnSelected: int） |
| `<ScrollList>` | 滚动列表（BindItems）|

下方的"不开 Toggle/Slider/Dropdown/ScrollList"段落改成：

> 这四个控件作为参考实现默认开启，**视觉风格用属性 + 子节点表达**；需要项目级强差异化样式（例如像素描边、按下时震动等）作者继承相应类重写 `OnAttached` 即可。

### 8.2 SKILL.md 改三处

- **内置原语**速查行从 8 个扩到 12 个，加入四行最简语法
- **事件流**示例补 `OnValueChanged` / `OnSelected` 用法
- **数据绑定**示例补 `BindItems` / `BindOptions`

XSD 由 `XsdGenerator` 自动出，无须手改。

---

## 9. 测试矩阵

| 文件 | 内容 |
|---|---|
| `Tests/EditMode/Controls/ToggleTests.cs` | XML 解析（含 group / is-on / sprite / color / font / 文本简写）；IsOn setter 触发流；同 group 互斥；Tr 接入 |
| `Tests/EditMode/Controls/SliderTests.cs` | min/max/value 解析；Direction 4 个枚举；Value setter 触发流；WholeNumbers 步进 |
| `Tests/EditMode/Controls/DropdownTests.cs` | Value 解析；BindOptions(Observable.Return) 静态选项；BindOptions 重订阅替换；OnSelected 流；Tr 接入选项；非法子节点 ParseException |
| `Tests/EditMode/Controls/ScrollListTests.cs` | itemTemplate 命中 Template / Control / 未知三种路径；Direction = vertical / horizontal；BindItems 增删项；ScrollList Dispose 释放所有 slot |
| `Tests/PlayMode/Controls/CommonControlsPlayTests.cs` | 真 ScrollRect 滑动；Toggle group runtime 切换；Locale 切换走 ReSolve 后 Toggle.Text / Dropdown 选项重译 |

---

## 10. 风险与开放问题

| # | 风险 | 应对 |
|---|---|---|
| R1 | itemTemplate 在运行时动态实例化与现有 `ScreenInstantiator` 假设可能冲突 | 抽出 `ScreenInstantiator.InstantiateNode(node, parent)` 公共入口，ScreenInstantiator 与 ScrollList 共享路径；M5 实现里加专项测试覆盖 |
| R2 | ToggleGroupRegistry 跨 Screen 泄漏 | Registry 挂 Screen 实例字段；Screen.Dispose 主动清空 |
| R3 | TMP_Dropdown 在 popup 打开时改 options 视觉错乱 | Dropdown 在 ReSolve 检测 popup 打开 → 先 Hide 再写 options 再恢复；EditMode 单测覆盖此分支 |
| R4 | 程序化构造 4 个控件的样板代码臃肿 | 抽出 `Runtime/Controls/Internal/ProceduralBuilders.cs` 共享 RectTransform / Image / TMP_Text 构造 helpers |
| R5 | 主 spec §10 措辞修改影响历史决策语义 | 仅改"默认开启"，保留"鼓励 fork 视觉"的语义；diff 显式 |
| R6 | `<ScrollList>` 子节点写法被作者误用（期望塞静态项） | ParseException 提示"使用 BindItems 推送"，并附文档链接 |
| R7 | BindItems 的 Action<TSlot,T> 在 itemTemplate 不命中 TSlot 类型时 NRE | 工厂解析期校验：若 TSlot 与解析到的 Control 类不兼容 → 抛 InvalidCastException 带友好消息 |

---

## 11. 实施分期建议

M5 一个 PR 不切分，但三个区块顺序明确：

1. **M5-Step1 简单三件套**：Toggle / Slider / Dropdown（含 BindOptions、ToggleGroupRegistry、TrResolver 接入）
2. **M5-Step2 ScrollList**：itemTemplate 解析 + InstantiateNode 共享入口 + BindItems 全量重建
3. **M5-Step3 Sample + 文档**：`Samples~/CommonControls/` 演示 4 个控件 + R3 数据流；主 spec §10/§5 + SKILL 同步

---

_Spec 结束。下一步：用 writing-plans 拆 M5 实施计划。_
