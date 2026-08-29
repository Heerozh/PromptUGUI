# `<TabMenu>` —— 弹出式 Tab 组（频道切换器 / 折叠导航）

**日期**: 2026-08-29
**状态**: **已实现**（Task 1–12 一轮做完，见 §14）。决策见 §2，2026-08-29 与作者对齐。
**相关**: `2026-05-27-tabbar-design.md`（`<TabBar>` / `<Tab>` 的出处，本文复用它的全部选中语义并抽出共享核心）、
`2026-05-09-m5-common-controls-design.md`（`<Dropdown>` 参考实现 —— 本文 §1 说明为何不在它上面扩）、
`2026-08-26-procedural-surface-design.md`（`ProceduralControl` / `SurfaceHost`）、
`2026-08-23-glass-fill-design.md`（弹窗面板走同一套 backdrop 规则）、
`2026-06-29-gamepad-keyboard-navigation-design.md`（`ContainmentRoot` / 模态焦点圈闭，本文 §7.8 复用）、
`2026-08-27-decor-primitives-design.md`（`<Decor>` 作为弹窗面板的子节点）。

**作用域**:
1. 新增 `Runtime/Controls/TabMenu.cs`（触发区 + 弹窗面板 + 弹窗 Content；`ProceduralControl`，表面宿主 = 弹窗面板）
2. 新增 `Runtime/Controls/Internal/TabGroupCore.cs`（从 `TabBar` 抽出的 Tab 组核心：静态收集 / `BindItems` / `itemTemplate` / 初始选中 / 订阅）；`TabBar.cs` 改为委托
3. `Runtime/Controls/Tab.cs`：为 caption 镜像暴露 `internal` 读取器 + `ContentChanged`
4. `Runtime/Controls/Internal/TriggerSpec.cs` / `TriggerSourceResolver.cs` / `Trigger.cs`：新增 `expand` / `collapse` 两种 TriggerKind（向上解析到最近 `<TabMenu>` 祖先）
5. `Runtime/Application/Screen.cs`：`ConfineNavigationToSelf` 泛化为 `ConfineNavigationTo(root)` + `RestoreNavigationConfine(previous)`
6. `Runtime/Application/UI.Modal.cs` + `Modals/ModalEscapeListener.cs`：Esc 先让展开中的 TabMenu 消费；listener 可选绑 `<Gamepad>/buttonEast`
7. `Runtime/Application/BuiltinPrimitives.cs` 注册；`ScreenInstantiator.cs` 把 `"TabMenu"` 加进 `selfIsLayoutGroup` + lint 分发
8. Lint：`TabRules.cs`（`PUI-TAB-PARENT` 接受 TabMenu 父）、新增 `TabMenuRules.cs`、`StateTriggerRules.cs`（`PUI-EXPAND-NO-SOURCE`）、`IRWalker.cs`、`BuiltinTags.cs`、`ProceduralSurfaceRules.SurfaceTags`、`NavTargetRules.SelectableTags`
9. Skills：`authoring-promptugui-xml/SKILL.md`（catalog 行 + 程序化表面表 + native-size / selectable 名单）、`reference/controls-tabs.md`（新 `<TabMenu>` 节）、`reference/animations.md`（`expand` / `collapse` 行）、`reference/navigation.md`（弹窗圈闭）、`scripting-promptugui-csharp/SKILL.md`（C# API）；主 spec §5 控件表追加一行
10. 测试：EditMode `TabMenuTests` / `TabMenuPlacementTests` / `TabMenuBindItemsTests` / `TabMenuTriggerTests` / `Navigation/TabMenuTrapTests` / `Lint/TabMenuRulesTests`；PlayMode `TabMenuPlayTests`；既有 `TabBar*Tests` 作为核心抽取的回归护栏

**依赖**: 无新增。LitMotion（`Animation.cs` 已无条件 `using LitMotion`）用于弹窗过渡；`ProceduralSurface` / `ToggleGroup` / `ModalEscapeListener` / `ExplicitNavigationResolver` 全部复用。

---

## 1. 背景

聊天面板顶栏要一个「频道切换器」：收起时是 `[🌐 星海频道 ▼]`，点开是一列频道，选一个就切到对应面板 —— 语义上就是 `<TabBar>`（互斥、每项 icon+文字、`bind=` 到一个 Frame），只是**呈现**收成了弹出式。

作者最先想到的是 `<Dropdown>`，但它做不到，而且不是改几个数字的问题：

- caption 内距 `(10,6)/(-25,-7)`、箭头贴右边 `-15` / 20×20、caption 与选项行字号 14、选项行高 20 全部写死（`Dropdown.cs:53-68, 106`；`ProceduralBuilders.AddText` 默认 14），没有 `fontSize` / `padding` 之类的口子。
- **弹窗里的每一行是 TMP_Dropdown 克隆出来的裸 uGUI `Toggle`**（`Dropdown.cs:117-140`），不是 PromptUGUI Control：`<Show on="state-*">`、`hoverColor` / `selectedColor`、`radius` / `glass`、每行独立 icon 一样都用不上；`bind=` 只能做成 `bind="a,b,c"` 按下标映射，还得和 C# 里 `BindOptions` 推的列表手工对齐。
- **弹窗宽度焊死等于触发按钮宽度**（Template 锚 0..1，`Dropdown.cs:72-77`），而设计图里触发区只有文字宽、菜单明显更宽。
- Dropdown **刻意不按 caption 算尺寸**（DSS-D2，怕选项切换时宽度跳）。对表单里的「画质：高」是对的，对频道切换器恰恰相反 —— 要的就是箭头紧跟文字。

把这三条都「完善」掉等于重写 Dropdown 的弹窗层，还背着 TMP_Dropdown 的包袱；而设计图需要的每一样 —— `icon` / `text` / `fontSize` / `bind` / 状态色 / `selectedSprite` / 程序化表面 / `<Show on="state-*">` / `isOn` / `BindItems` + `itemTemplate` —— `<Tab>` 全部已经有了。缺的只是一个「把一组 Tab 收进弹窗、用选中项当把手」的容器。

**分层位置**：与 `<TabBar>` 平级的 Tab 容器。`<TabBar>` 是「铺开的 Tab 组」，`<TabMenu>` 是「折叠的 Tab 组」；两者共享同一份选中语义实现（§2 TM-D17），子节点都是 `<Tab>`。`<Dropdown>` 保持表单选择器定位不动。

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| TM-D1 | 新控件 vs 扩 Dropdown | 新控件 `<TabMenu>` | §1：Dropdown 的项是裸 Toggle、弹窗宽度焊死、caption 不 hug；三条都是结构问题 |
| TM-D2 | 标签名 | `<TabMenu>` | 点出「Tab 语义 + 菜单式呈现」，与 `<TabBar>` / `<Tab>` 一眼是一家；`<TabDropdown>` 容易让人以为用法同 Dropdown（`BindOptions` / `value:int`） |
| TM-D3 | 表面归属 | **控件的程序化表面 = 弹窗面板**；触发区只有 caption + 箭头、默认无底 | `color` / `sprite` / `radius` / `glass` / `<Decor>` 全落在弹窗上，零新皮肤属性、主题词汇一次全拿到。触发区要底就套 `<Frame>` / `<Image>` —— 与「TabBar 本身没视觉」同一先例。否决 Dropdown 式 `popup*` 前缀（弹窗拿不到 glass / Decor，除非再堆 `popupGlass…`）与「作者自带 `<Frame>` 面板」（嵌三层，面板 anchor/margin 还得由控件接管） |
| TM-D4 | 子节点归属 | 子节点全部进弹窗（`ChildHostTransform` = 弹窗 Content）：`<Tab>`、包着 Tab 的 Template / `<Animation>` / `<Trigger>` 包装、`<Decor>` | 与 `<TabBar>`（子 = Tab）、`<Carousel>`（子进 Strip）同形；触发区不是作者可编辑的子树 |
| TM-D5 | caption 内容 | 自动镜像**选中 Tab** 的 `icon` + `text` | 零额外标记。Template 包装的富 Tab 没写 `text=` / `icon=` 时 caption 为空 —— 在 Tab 上补 `text=` 即可。`text=` / `icon=` 覆盖、自定义 caption slot 留 v2 |
| TM-D6 | 弹窗宽度 | `popupWidth=` 显式；缺省 = max(触发区宽, Content 首选宽) | 触发区 hug 文字、菜单按内容撑 —— 设计图的形态；Dropdown 式「等于触发区宽」作为下限保留 |
| TM-D7 | 项的横向尺寸 | 项**填满**弹窗宽（`childForceExpandWidth = true`）；Tab 的 `width=` 无效 → lint `PUI-TABMENU-ITEM-WIDTH` | 菜单行天然通栏；TabBar 的「尊重 `width=`」在竖排菜单里只会造出参差的行 |
| TM-D8 | 弹窗置顶 | 弹窗留在 TabMenu 子树里，**不 reparent**；展开时给面板加 `Canvas(overrideSorting)` + `GraphicRaycaster`；blocker 挂在 Screen 根 Canvas 下 | TMP_Dropdown 同款。Tab 的祖先链不变：`FindAncestorToggleGroup`、`OwnerScreenOf`、id scope、`bind=` 全部照旧；嵌套 Canvas + overrideSorting 同时逃出祖先 `RectMask2D` / stencil（`MaskUtilities` 在 overrideSorting 处截断）—— 放在 `<ScrollList>` 里的 TabMenu 弹窗不会被裁 |
| TM-D9 | 同时展开数 | 全局至多一个（`static s_expanded`）；展开另一个先收起前一个 | blocker 盖住全屏，第二个本来就点不到；单例让 Esc 消费（TM-D16）有唯一目标 |
| TM-D10 | 收起时机 | 选中任一 Tab（含点已选中的）→ 收起；点 blocker → 收起；Esc / 手柄 B → 收起 | 菜单的通用约定；`closeOnSelect="false"` 留 v2 |
| TM-D11 | 展开态与 ReSolve | 展开/收起是**运行期状态**：resize / Variant / Theme 的 ReSolve 不改它，只重算面板位置尺寸；不提供 XML `expanded=` | 与 `isOn` / `value` 的「运行期改过就不打回」同款；初始态永远是收起 |
| TM-D12 | 收起态下的测量 | Screen.Open 的 apply pass 期间弹窗保持 active，测量完成后经 `Screen.DeferDuringOpen` 停用；`BindItems` 重建期间**同帧**临时激活 | `Tab.bind` 的既有先例（`Tab.cs` ApplyBindFrame 注释）：inactive 上 `AddComponent` 的 TMP 不跑 Awake，`preferredWidth` 会算错 |
| TM-D13 | 过渡动画 | 内置：`transition`（默认 `0.15s`，`0` = 即时）= 面板 `CanvasGroup.alpha 0→1` + 沿展开方向 8px 位移 + 箭头垂直翻转；收起反放，放完再 `SetActive(false)` | 弹窗面板是内部节点，作者无法用 `<Animation>` 包住它；LitMotion 已是硬依赖。预设名 / 自定义曲线留 v2 |
| TM-D14 | Trigger 事件 | 新 kind `expand` / `collapse`（+ `expand@id` / `collapse@id`），**向上**解析到最近 `<TabMenu>` 祖先，同 `state-*` | 用途是弹窗内**逐项**入场动画（`<Animation on="expand" type="slidein-left" delay="0.05s"><Tab/></Animation>`，即 animations.md 的 Menu entry stagger 模式）和 C# 钩子。不能叫 `open` / `close`：`on="open"` 已是「Screen 打开」 |
| TM-D15 | 手柄 / 键盘导航 | 展开 → `UI.Navigation.ContainmentRoot` 切到弹窗面板、按 `confineRoot` 重算显式邻居、焦点落到当前选中 Tab；收起 → 恢复之前的 root（模态根或 null）、焦点回触发区 | 复用模态圈闭（`Screen.ConfineNavigationToSelf` → 泛化为 `ConfineNavigationTo(root)`）；blocker 的 Button `navigation = None`，不进导航图 |
| TM-D16 | Esc 消费 | `UI.Modal.OnEscapePressed` 首行调 `TabMenu.TryConsumeEscape()`：有展开中的就收起并返回；同帧已由 TabMenu 自己的 listener 收起过也返回 | 两个 `ModalEscapeListener` 同帧都会响，顺序不定；不做同帧 guard 会一次 Esc 同时关弹窗和模态 |
| TM-D17 | 与 TabBar 的关系 | 抽 `internal TabGroupCore`（静态收集 / `BindItems` / `ResolveFactory` / `FindTabIn` / `SyncInitialSelection` / 订阅）；TabBar、TabMenu 都委托 | 「Tab 组语义」保持单一实现；既有 `TabBar*Tests` 是抽取的回归护栏 |
| TM-D18 | 触发区交互 | `PuiButton`，`targetGraphic` = caption label（uGUI 默认 ColorTint 给 hover / pressed）；触发区**不是** `IStateSource` | 子节点都在弹窗里，`<Show on="state-*">` 对触发区无处安放；caption 状态色留 v2 |
| TM-D19 | 弹窗默认皮肤 | 与 Dropdown 弹窗同款：`pugui_9slice_round` + `DefaultPopupBgColor`；`sprite=""` 退化为纯色 | 不写任何皮肤也能用；主题一般会写 `class=` |
| TM-D20 | 弹窗裁切 | v1 不做 `mask` | 行高亮溢出圆角只在 `padding="0"` 时可见；作者写 `padding` 或给 Tab `radius` 即可。`mask` 留 v2 |
| TM-D21 | 弹窗定位 | 触发区**下方、左对齐**、间距 `popupGap`；右缘越出 Canvas → 整体左移贴边；下方放不下且上方更宽裕 → **向上翻**（pivot / anchor 对调） | TMP_Dropdown 的翻转行为；`popupAlign` 留 v2 |

## 3. XML 形态

### 3.1 频道切换器（本文动机）

```xml
<Style name="menu-item" sprite="" height="44" radius="8"
       hoverColor="white/0.08" selectedColor="primary/0.35" fontSize="18"/>

<HStack anchor="top-stretch" height="64" padding="0,16,0,16" spacing="8">
  <TabMenu id="channel" fontSize="22" textColor="white" iconSize="24"
           popupWidth="240" padding="8" spacing="4"
           radius="12" glass="true" frost="0.5" color="primary-dark/0.6"
           borderWidth="1" borderColor="primary-lighter/0.5">
    <Tab class="menu-item" icon="ui:globe" text="星海频道" bind="ch_world" isOn="true"/>
    <Tab class="menu-item" icon="ui:guild" text="公会频道" bind="ch_guild"/>
    <Tab class="menu-item" icon="ui:team"  text="队伍频道" bind="ch_team"/>
    <Decor kind="bracket" at="tl,br" extent="14"/>
  </TabMenu>
  <Frame width="stretch"/>
  <Icon name="ui:members" size="20x20"/>
  <Text fontSize="20">128</Text>
</HStack>

<Frame id="ch_world" anchor="stretch" margin="64,0,0,0">…</Frame>
<Frame id="ch_guild" anchor="stretch" margin="64,0,0,0">…</Frame>
<Frame id="ch_team"  anchor="stretch" margin="64,0,0,0">…</Frame>
```

- 触发区 = `🌐 星海频道 ▼`，hug 内容，透明；在 `<HStack>` 里是普通 layout child。
- `radius` / `glass` / `color` / `borderWidth` / `<Decor>` 落在**弹窗面板**上；`padding` / `spacing` 是弹窗里 Tab 的竖排间距。
- 选「公会频道」→ caption 变 `🏰 公会频道 ▼`，`ch_guild` 显示、其余隐藏，弹窗收起。

### 3.2 触发区要底：套一层

```xml
<Frame radius="20" color="surface/0.6" borderWidth="1">
  <TabMenu id="sort" anchor="stretch" margin="0,12,0,12" fontSize="16">
    <Tab text="按时间" isOn="true"/>
    <Tab text="按热度"/>
  </TabMenu>
</Frame>
```

同「给 TabBar 加背景条就套 `<Image>`」。

### 3.3 弹窗内逐项入场动画 + 动态项

```xml
<Template name="ChannelTab">
  <Param name="text"/>
  <Param name="icon"/>
  <Param name="bind"/>
  <Animation on="expand" type="slidein-left" duration="0.12s">
    <Tab id="tab" class="menu-item" icon="ui:{{icon}}" text="{{text}}" bind="{{bind}}"/>
  </Animation>
</Template>

<TabMenu id="channel" itemTemplate="ChannelTab" popupWidth="240" padding="8"/>
```

```csharp
screen.Get<TabMenu>("channel")
      .BindItems(channels, (Tab tab, Channel c) => { tab.Text = c.Name; tab.Icon = c.IconKey; })
      .AddTo(screen);
```

`on="expand"` 向上找到 `<TabMenu>`；`<Animation>` 包装 Tab 在 Content 的竖排里占一格（`Animation.GetNativeSize` 转发子节点尺寸，与 TabBar 的 Template 包装规则一致）。

## 4. 属性表

除下表外，`<TabMenu>` 接受全部通用属性（`anchor` / `size` / `margin` / `hidden` / `interactable` / `flow` / `class` / `if` / `focus` / `nav*`）与 `ProceduralControl` 的 15 个程序化属性（`radius` / `borderWidth` / `borderColor` / `glow` / `glowColor` / `innerGlow` / `innerGlowColor` / `glass` / `frost` / `depth` / `dispersion` / `lightAngle` / `lightIntensity` / `saturation` / `noise`），**全部作用于弹窗面板**。

### 4.1 触发区（caption）

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `fontSize` | int | `24` | caption 字号（与 `<Tab>` label 默认一致） |
| `textColor` | color | 默认 ink | caption 文字色；token / `/alpha` / 渐变 |
| `font` | string | `default` | `FontApplier` 字体槽 |
| `iconSize` | float | `24` | caption icon 边长；选中 Tab 没有 icon 时 icon 槽整个不占位 |
| `arrow` · `arrowColor` | sprite key / color | `pugui_caret` / glyph 白 | 箭头。`arrow=""` 隐藏（无图 Image 会画成实心块，直接关组件）。展开时翻转 180°，随 `transition` 动画 |
| `arrowSize` | float | `16` | 箭头边长 |
| `gap` | float | `6` | icon–label–arrow 之间的间距 |

触发区内边距固定：横 4、纵 6，最小高 44（tap target，同 Btn / Tab）。

### 4.2 弹窗

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `popupWidth` | float | auto | 面板宽。缺省 = max(触发区宽, Content 首选宽)（TM-D6） |
| `popupGap` | float | `4` | 触发区与面板的间距（向上翻时同样适用） |
| `padding` | `t,r,b,l` / `v,h` / `all` | `0` | 面板内边距（Content 的 `VerticalLayoutGroup.padding`；语法同 `<TabBar padding>`） |
| `spacing` | float | `0` | 项间距 |
| `color` | color | `DefaultPopupBgColor` | 面板底色；程序化模式下即填充色（spec §7 同 Frame） |
| `sprite` | sprite key | `pugui_9slice_round` | 面板底图（自动 9-slice）；`""` / `none` = 纯色 |
| `tint` | `multiply` / `linear` | `multiply` | 面板底图混合模式 |
| `transition` | duration | `0.15s` | 展开 / 收起过渡时长，`0.15s` / `150ms` / 裸秒数；`0` = 即时（TM-D13） |
| `itemTemplate` | tag / Template 名 | `Tab` | `BindItems` 的项模板，同 `<TabBar>` |

### 4.3 `<Tab>` 在 `<TabMenu>` 里

与在 `<TabBar>` 里完全相同（`text` / `icon` / `bind` / `isOn` / 状态色 / `selectedSprite` / 程序化表面 / `<Show on="state-*">`），差别只有：

- `width=` 无效（项填满面板宽，TM-D7）；`height=` 有效，缺省走 `Tab.GetNativeSize`（label + padding，最小 44）。
- `anchor=` / `margin=` 非法（TabMenu 是 layout-group 型父，`PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN`）。
- Tab 的 `text` / `icon` 会被 caption 镜像。

## 5. C# API

### 5.1 `TabMenu`

```csharp
public sealed class TabMenu : ProceduralControl
{
    // 展开 / 收起
    public bool IsExpanded { get; }
    public void Expand();
    public void Collapse();
    public void Toggle();
    public Observable<Unit> OnExpanded { get; }     // 过渡开始时
    public Observable<Unit> OnCollapsed { get; }    // 过渡开始时（面板在过渡结束后 SetActive(false)）

    // 选中语义 —— 与 TabBar 逐字相同
    public Observable<Tab> OnSelectionChanged { get; }   // 选中变化；BindItems 清空时 fire null
    public Tab SelectedTab { get; }
    public int SelectedIndex { get; }                    // -1 = 无
    public int Count { get; }
    public Tab GetAt(int index);
    public IDisposable BindItems<T>(Observable<IReadOnlyList<T>> source, Action<Tab, T> bind);
    public IDisposable BindItems<T, TSlot>(Observable<IReadOnlyList<T>> source, Action<TSlot, T> bind)
        where TSlot : class, IControl;

    // caption
    public void RefreshCaption();   // 一般不需要：选中变化 / Tab.Text / Tab.Icon 变化 / ReSolve 都会自动刷新

    // 内部
    internal static bool TryConsumeEscape();        // UI.Modal.OnEscapePressed 首行调用（TM-D16）
    internal static int PopupSortingOffset = 2;     // 面板 = 根 Canvas order + 2，blocker = + 1
}
```

用法：

```csharp
var menu = screen.Get<TabMenu>("channel");
menu.OnSelectionChanged.Subscribe(tab => Chat.Switch(tab?.Id)).AddTo(screen);
menu.OnExpanded.Subscribe(_ => Sfx.Play("ui_open")).AddTo(screen);
// 键盘快捷键手动展开
menu.Expand();
```

### 5.2 `Tab` 追加（internal）

```csharp
internal string CaptionText { get; }        // _label?.text
internal Sprite CaptionIcon { get; }        // _icon?.sprite
internal event Action ContentChanged;       // Text / Icon setter 末尾触发；TabMenu 只订阅当前选中项
```

### 5.3 `TabGroupCore`（internal，`Controls/Internal/`）

```csharp
internal sealed class TabGroupCore : IDisposable
{
    public TabGroupCore(Control owner, Func<RectTransform> itemHost);
    public string ItemTemplate { set; }                 // 置空 factory，同 TabBar.ItemTemplate
    public IReadOnlyList<Tab> Tabs { get; }
    public int SelectedIndex { get; }
    public Tab SelectedTab { get; }
    public Observable<Tab> SelectionChanged { get; }

    public void CollectStatic(IReadOnlyList<IControl> children);   // _bound 后 no-op
    public IDisposable BindItems<T, TSlot>(Observable<IReadOnlyList<T>> source, Action<TSlot, T> bind,
                                          Action beforeRebuild = null, Action afterRebuild = null)
        where TSlot : class, IControl;                              // TabMenu 用两个钩子做同帧临时激活（TM-D12）
    public void SyncInitialSelection();
    public void WireTabSubscriptions();
    public static Tab FindTabIn(IControl node);
}
```

`TabBar` 保留全部公开成员（`Count` / `SelectedIndex` / `SelectedTab` / `GetAt` / `BindItems` / `OnSelectionChanged`），实现改为转发；`ApplyDirection` / `ApplySpacingPadding` / `ApplyChildSizing` 留在 TabBar（布局是 TabBar 自己的事）。`padding` 字符串解析抽成 `Internal.PaddingParser.Parse(string) → RectOffset`，两边共用。

## 6. 程序化层级（固定）

```
TabMenu                      RectTransform + Image(sprite=null, color=(0,0,0,0), raycastTarget=true)
                             + PuiButton(targetGraphic=Label, onClick→Toggle) + ToggleGroup(allowSwitchOff=false)
├── Icon                     Image, raycast=false; anchor left-middle; iconSize²；无 icon 时 enabled=false
├── Label                    TMP, raycast=false; anchor left-middle; fontSize 24; 宽 = preferred
├── Arrow                    Image(pugui_caret), raycast=false; anchor left-middle; arrowSize²; 展开时 localRotation z=180
└── Popup                    RectTransform + Image(bg, 9-slice round) + CanvasGroup + Canvas(overrideSorting) + GraphicRaycaster
    │                        ← SurfaceHost；anchor 见 §7.2；收起态 inactive
    ├── __Surface            ProceduralSurface 节点（懒建，sibling 0）
    └── Content              RectTransform(stretch) + VerticalLayoutGroup   ← ChildHostTransform
        │                    childControlWidth/Height=true, childForceExpandWidth=true, childForceExpandHeight=false
        ├── <Tab> …          作者子节点 / BindItems 项
        └── __Decor:*        <Decor> 实例（ParticipatesInLayout=false → ignoreLayout；stretch 到 Content = 面板矩形）

Blocker (Screen 根 Canvas 直接子节点，展开时创建、收起时 SetActive(false)、Dispose 时销毁)
                             RectTransform(stretch) + Image(color=(0,0,0,0)) + Canvas(overrideSorting, root+1)
                             + GraphicRaycaster + Button(navigation=None, onClick→Collapse)
```

- `Popup` 的 `Canvas` / `GraphicRaycaster` 在 `OnAttached` 就加上（inactive 时无开销），避免展开时 `AddComponent` 触发一帧重建。
- `__Surface` 挂在 `Popup` 而不是 `Content` 下，所以不会被 `VerticalLayoutGroup` 当成一行排版。
- Icon / Label / Arrow 手工定位（不用 HorizontalLayoutGroup），`GetNativeSize` 与 Tab 一样走 `label.GetPreferredValues(text)` 的确定性公式。

## 7. 行为细节

### 7.1 展开 / 收起

**Expand()**（已展开 / `!Interactable` / GameObject 非 active → no-op）：

1. `s_expanded?.Collapse(immediate: true)`；`s_expanded = this`。
2. 面板 `SetActive(true)`；Canvas `overrideSorting = true`，`sortingOrder = rootCanvas.sortingOrder + PopupSortingOffset`（rootCanvas = `GetComponentInParent<Canvas>().rootCanvas`）。
3. `LayoutRebuilder.ForceRebuildLayoutImmediate(Content)` → §7.2 算宽高与位置。
4. blocker：首次创建（挂 rootCanvas 下，最后一个 sibling），之后 `SetActive(true)`；`sortingOrder = root + PopupSortingOffset - 1`。
5. 导航（§7.8）、Esc listener（§7.9）。
6. 箭头翻转 + 面板过渡（§7.7）；`_expanded = true`；`OnExpanded.OnNext`。

**Collapse(immediate = false)**（未展开 → no-op）：

1. blocker `SetActive(false)`；导航 / Esc 复原；`s_expanded = null`；`_expanded = false`；`OnCollapsed.OnNext`。
2. `immediate || transition == 0` → 面板立即 `SetActive(false)`；否则反放过渡，`OnComplete` 再 `SetActive(false)`。过渡中再次 `Expand()` → 取消反放、从当前 alpha 正放。

**Toggle()** = `IsExpanded ? Collapse() : Expand()`；触发区 `PuiButton.onClick` 接它。

### 7.2 面板定位与尺寸

在 `Expand()` 第 3 步、`OnAfterApply`（展开态）与 `BindItems` 重建后执行：

```
contentW  = LayoutUtility.GetPreferredWidth(Content)      // VerticalLayoutGroup = 最宽子项 + padding
contentH  = LayoutUtility.GetPreferredHeight(Content)
popupW    = popupWidth ?? max(RectTransform.rect.width, contentW)
Popup.sizeDelta = (popupW, contentH)

// 默认：挂在触发区左下角，向下长
Popup.anchorMin = anchorMax = (0, 0); pivot = (0, 1); anchoredPosition = (0, -popupGap)

// 用根 Canvas 的矩形做越界判断（RectTransformUtility 换算到 rootCanvas 空间）
overflowBelow = 面板底缘 < canvas 底缘
roomAbove     = 触发区顶缘到 canvas 顶缘的距离
if (overflowBelow && roomAbove > 面板可用的下方空间)   → 向上翻：anchor (0,1), pivot (0,0), y = +popupGap
overflowRight = 面板右缘 - canvas 右缘
if (overflowRight > 0)                                  → anchoredPosition.x -= overflowRight
```

位置只在展开 / ReSolve / 重建时算，不逐帧跟随（面板是触发区的子节点，随之移动）。

### 7.3 选中 → 收起 + caption 镜像

- `TabGroupCore.SelectionChanged` → `RefreshCaption()`；若 `IsExpanded` → `Collapse()`。
- 点已选中的 Tab：`ToggleGroup(allowSwitchOff=false)` 不会产生 `onValueChanged`，因此 TabMenu 另外订阅每个 Tab 的 `PuiToggle.onClick`-等价事件（`Tab` 内部 `OnSubmitOrClick`，复用 `PuiToggle` 已有的 submit-wake 路径）→ 也收起。
- `RefreshCaption()`：`Label.text = SelectedTab?.CaptionText ?? ""`；`Icon.sprite = SelectedTab?.CaptionIcon`，`Icon.enabled = sprite != null`；重排 Icon / Label / Arrow 的 x 偏移；若父是 layout group 则 `LayoutRebuilder.MarkLayoutForRebuild`（native size 变了）。同时把 `ContentChanged` 订阅切到新的选中 Tab。

### 7.4 初始化序列

1. `ScreenInstantiator` DFS 建树；`TabMenu.OnAttached` 建 §6 层级，`Popup` 此时 **active**。
2. 子节点经 `ChildHostTransform` 进 `Content`；`Tab.OnAttached` 沿 transform 向上找到 TabMenu 的 `ToggleGroup`。
3. `root.SetActive(true)` 整树激活 → 所有 TMP Awake。
4. 属性 apply（DFS post-order）：Tab 的 `isOn` / `bind` 照 TabBar 走；`TabMenu.OnAfterApply`：`core.CollectStatic(Children)` → `SyncInitialSelection` → `WireTabSubscriptions` → `RefreshCaption` → `owner.DeferDuringOpen(() => Popup.SetActive(false))`。
5. Open 收尾执行 deferred：面板停用。此时没有渲染过一帧，不闪。

`Tab.bind` 自己的 deferred 初始隐藏与此并列，互不影响。

### 7.5 BindItems 重建

`core.BindItems(source, bind, beforeRebuild, afterRebuild)`：

- `beforeRebuild`：若面板 inactive → `Popup.SetActive(true)`，记 `_tempActivated`。
- 重建（`ClearTabs` → factory 建项 → `bind` → `SyncInitialSelection` → `Wire`）。
- `afterRebuild`：`RefreshCaption()`；`IsExpanded` → §7.2 重算；`_tempActivated` → `Popup.SetActive(false)`。

同帧激活→建→停用，不会渲染出来；新 Tab 的 TMP 在 `SetActive(true)` 时同步 Awake，`GetNativeSize` 测得准（TM-D12）。空列表：`SelectedTab = null`，caption 清空（只剩箭头），`OnSelectionChanged(null)`。

### 7.6 ReSolve（resize / Variant / Theme）

- `OnBeforeApply`：`base`（程序化表面 BeginPass）；`_transitionDeclared = false` 等每-pass 状态清零。
- 各 setter 重写 caption 字号 / 颜色 / 箭头 / 弹窗皮肤 / padding / spacing。
- `OnAfterApply`：`base`（Surface.Reconcile，面板 Image ↔ 程序化面板切换）；`core.CollectStatic`（`_bound` 后 no-op）；`SyncInitialSelection`（`isOn` 是运行期独占状态，用户选过的不打回）；`RefreshCaption`；`GameObject.activeInHierarchy == false && IsExpanded` → `Collapse(immediate: true)`（`hidden="true"` 的 Variant 把它藏了）；`IsExpanded` → §7.2 重算 + 取消进行中的过渡并 snap 到终态。
- 展开态不变（TM-D11）。

### 7.7 过渡动画

LitMotion，两条 `MotionHandle`（面板、箭头），存在 TabMenu 上，`Dispose` / 下一次过渡前 `TryCancel`：

- 面板：`CanvasGroup.alpha` 0→1；`Popup.anchoredPosition.y` 从终态偏 8px（向下长时 +8、向上翻时 -8）→ 终态；easing `OutCubic`；时长 `transition`。
- 箭头：`localScale.y` 1→-1（收起 -1→1）—— **镜像，不是旋转**，见 §14.7。
- `transition="0"`：不建 motion，直接写终态。
- 过渡期间 blocker 已生效、Tab 可点（不等动画）。

### 7.8 导航圈闭（`UI.Navigation.IsEnabled` 时）

展开：

```csharp
_prevConfine = owner.NavConfineRoot;          // 模态根 / null
owner.ConfineNavigationTo(Popup);             // NavConfineRoot = Popup；ExplicitNavigationResolver.Resolve(confineRoot: Popup)
UI.Navigation.ContainmentRoot = Popup;        // 每帧 EnforceContainment 把选择关在面板里
EventSystem.SetSelectedGameObject(SelectedTab?.GameObject ?? Navigation.FirstFocusableUnder(Popup));
```

收起：

```csharp
owner.RestoreNavigationConfine(_prevConfine); // NavConfineRoot = 之前；按之前的 confine 重算
UI.Navigation.ContainmentRoot = _prevConfine;
EventSystem.SetSelectedGameObject(GameObject); // 焦点回触发区
```

`Screen.ConfineNavigationToSelf()` 改为 `ConfineNavigationTo(RootGameObject)` 的调用；ReSolve 沿用 `NavConfineRoot` 重算，因此 resize 期间展开着的弹窗保持圈闭。触发区 `PuiButton` 本身进导航图（`nav*` / `focus` 可写，`NavTargetRules.SelectableTags` 加 `TabMenu`）；Submit（A / Enter）在触发区上 = `Toggle()`。

### 7.9 Esc / 手柄 B

展开时在 `Popup` 上启用一个 `ModalEscapeListener`（`AlsoCancelButton = true` → 额外绑 `<Gamepad>/buttonEast`；legacy Input 分支仍只有 Escape），回调 `Collapse(); s_escapeFrame = Time.frameCount;`。

`UI.Modal.OnEscapePressed` 首行：

```csharp
if (Controls.TabMenu.TryConsumeEscape()) return;
// TryConsumeEscape: s_expanded != null → Collapse 并返回 true；
//                   否则 s_escapeFrame == Time.frameCount → true（TabMenu 的 listener 已先响过）；否则 false
```

两种同帧顺序都只关弹窗不关模态。`Tutorial.IsBlockingInput` 的判断保持在其后（引导期间 Esc 仍被吞）。

### 7.10 `on="expand"` / `on="collapse"`

- `TriggerSpec`：`TriggerKind.Expand` / `Collapse`；bare 形式与 `expand@<id>` / `collapse@<id>`。
- `Trigger.InitTriggerSubscription`：`SubscribeExpand(kind)` → `TriggerSourceResolver.FindTabMenu(this, sourceId)`：bare → `GameObject.GetComponentInParent<TabMenuMarker>(true)`（TabMenu 在自身 GameObject 上挂一个空 `TabMenuMarker : MonoBehaviour`，与 `IStateSource` 的向上查找同法；`includeInactive` 因为弹窗收起态 inactive）→ 订阅 `OnExpanded` / `OnCollapsed`；`@id` → `ScopedIds` 查找 + 类型校验。
- 找不到祖先 → `InvalidOperationException`（同 `state-*`）；lint `PUI-EXPAND-NO-SOURCE`。
- `<Show>` 不接受这两个值（`Show` 只认 `state-*`，保持不变）。

### 7.11 生命周期

- `Dispose`：取消 motion；blocker `Destroy`（它不在 TabMenu 子树里）；`s_expanded == this` → 置空、导航 / Esc 复原；`core.Dispose()`；`Locale.Changed` 反订阅；`base.Dispose()`。
- `Screen.Close` 展开中：Screen 根被销毁 → blocker（根 Canvas 子节点）随之销毁；`Dispose` 路径同上做兜底。
- `interactable="false"`：`PuiButton.interactable = false` + `base`（CanvasGroup）；展开中 → `Collapse(immediate: true)`。

## 8. 边界 / 错误处理

| 场景 | 处理 |
|---|---|
| `<TabMenu>` 下没有任何 Tab | 合法；caption 只剩箭头；`Expand()` 弹出空面板（padding 高）；`SelectedTab = null` |
| 子节点不是 Tab / 包装 / `<Decor>` | lint warning `PUI-TABMENU-CHILD`；运行时不阻拦（竖排照常，无 tab 语义） |
| Tab 写 `width=` | lint warning `PUI-TABMENU-ITEM-WIDTH`；运行时被 `childForceExpandWidth` 覆盖 |
| Tab 写 `anchor=` / `margin=` | `PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN`（TabMenu 在 layout-group 名单里） |
| `popupWidth` 小于触发区宽 | 尊重作者值（显式胜出），不夹 |
| 面板比 Canvas 还高 | 不翻、不裁；`popupMaxHeight` + 滚动留 v2 |
| 触发区在 `<ScrollList>` / masked `<Frame>` 里 | 面板经 overrideSorting Canvas 逃出裁切（TM-D8）；触发区自身仍被裁 |
| 两个 TabMenu 先后展开 | 后者展开时前者 `Collapse(immediate: true)`（TM-D9） |
| 展开中切 Variant 把 TabMenu `hidden` | `OnAfterApply` 检测 `!activeInHierarchy` → `Collapse(immediate: true)` |
| 展开中 `BindItems` 重建 | 面板本就 active，不临时激活；重建后重算尺寸；焦点若在被销毁的 Tab 上 → `EnforceContainment` 下一帧落回面板首个可聚焦项 |
| `bind="x"` 找不到 / 不是 Frame | 同 Tab 既有行为：首次切换 LogWarning 一次 |
| `itemTemplate` 指向不存在的 tag / Template | `ParseException`（同 TabBar） |
| `transition` 解析失败 | LogWarning，退回 0.15s |
| `on="expand"` 无 TabMenu 祖先 | 运行时 `InvalidOperationException`；lint `PUI-EXPAND-NO-SOURCE`（Template body / instance root 豁免） |
| Esc 时模态在上、弹窗在下层页面 | 弹窗属于下层页面时，上层模态自己的 listener 才是活的（`RefreshTopListener`）；`TryConsumeEscape` 仍会先收起弹窗、吞掉这次 Esc —— 可接受（弹窗被模态盖住本来就该关） |

## 9. Lint 规则

`Runtime/Core/Lint/TabMenuRules.cs`（新）；`IRWalker.WalkNode` 与 `ScreenInstantiator.InstantiateRecursive` 同源分发。

| Code | 触发条件 | 级别 |
|---|---|---|
| `PUI-TAB-PARENT`（既有，放宽） | `<Tab>` 的直接父不是 `<TabBar>` **也不是** `<TabMenu>`（Template-instance root 内豁免） | warning |
| `PUI-TABMENU-CHILD` | `<TabMenu>` 直接子的子树里既无字面 `<Tab>`、也不是 Template 调用、也不是 `<Decor>` | warning |
| `PUI-TABMENU-ITEM-WIDTH` | `<TabMenu>` 直接子（或其 `<Tab>`）写了 `width=`（含 variant 覆盖） | warning |
| `PUI-EXPAND-NO-SOURCE` | bare `on="expand"` / `on="collapse"` 的 `<Trigger>` / `<Animation>` 没有 `<TabMenu>` 祖先（`@id` 形式与 Template body 豁免）—— 并入 `StateTriggerRules.CheckStateSource` 的祖先扫描，加一个 `hasTabMenuAncestor` 位 | error |
| `PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN`（既有） | `<TabMenu>` 加入 `IRWalker` / `ScreenInstantiator` 的 layout-group 名单后自动覆盖其子节点 | error |

名单同步：`BuiltinTags.All`（`BuiltinTagsTests` 守）、`ProceduralSurfaceRules.SurfaceTags`、`NavTargetRules.SelectableTags`、`StateTriggerRules` 的 expand 分支。

## 10. 实现要点

### 10.1 `Runtime/Controls/Internal/TabGroupCore.cs`（新）

从 `TabBar.cs` 原样搬出：`_tabs` / `_factory` / `_itemTemplate` / `_itemsSub` / `_tabSubs` / `_bound` / `_selectionChanged`、`BindItems` / `Rebuild` / `ClearTabs` / `FindTabIn` / `ResolveFactory` / `WireTabSubscriptions` / `SyncInitialSelection` / `CollectStaticTabs`。`ResolveFactory` 的错误信息里的 `<TabBar itemTemplate=…>` 改成用 owner 的 tag 拼。`Rebuild` 加 `beforeRebuild` / `afterRebuild` 两个可选钩子。`owner` 用于 `UI.OwnerScreenOf(owner)`；`itemHost` 委托返回放项的 RectTransform（TabBar → 自身，TabMenu → Content）。

### 10.2 `Runtime/Controls/TabBar.cs`

保留公开面；字段换成 `private readonly TabGroupCore _core`；`OnAfterApply` → `_core.CollectStatic(Children); _core.SyncInitialSelection(); _core.WireTabSubscriptions();`。既有测试不改一行就应全绿。

### 10.3 `Runtime/Controls/TabMenu.cs`（新）

```csharp
public sealed class TabMenu : ProceduralControl
{
    private protected override GameObject SurfaceHost => _popup.gameObject;
    private protected override Selectable SurfaceSelectable => null;
    protected internal override Transform ChildHostTransform => _content;

    public override Vector2? GetNativeSize()
    {
        var pref = _label.GetPreferredValues(_label.text ?? "");
        var w = PadX * 2 + (_icon.enabled ? _iconSize + _gap : 0) + pref.x + (_arrow.enabled ? _gap + _arrowSize : 0);
        return new Vector2(w, Mathf.Max(MinTapHeight, pref.y + PadY * 2));
    }
    …
}
```

关键私有方法：`BuildCaption()` / `BuildPopup()` / `LayoutCaption()` / `PlacePopup()` / `EnsureBlocker()` / `PlayTransition(bool expanding)` / `PushNav()` / `PopNav()`。注册：`reg.Register<TabMenu>("TabMenu", null)`（无 `defaultTextAttr`、无 `runtimeStateAttr` —— 展开态不是声明属性）。

### 10.4 `Runtime/Controls/Tab.cs`

`Text` / `Icon` setter 末尾 `ContentChanged?.Invoke()`；新增 `internal string CaptionText`、`internal Sprite CaptionIcon`；新增 `internal Observable<Unit> OnActivated`（isOn 变 true **或** 点击已选中项）供 §7.3 —— 由 `PuiToggle` 的 `onClick`-等价通道提供（`PuiToggle` 已有 `OnSubmit` wake 路径，加一个 `IPointerClickHandler` 转发即可）。

### 10.5 Trigger 三件套

`TriggerSpec`：enum 加 `Expand, Collapse`；`s_prefixedKinds` 加 `("expand@", …)`, `("collapse@", …)`；bare 表加 `"expand"` / `"collapse"`。`Trigger.InitTriggerSubscription` 加 `case Expand/Collapse: SubscribeExpand(kind)`。`TriggerSourceResolver.FindTabMenu(trigger, sourceId)` 仿 `FindStateSource`。

### 10.6 `Runtime/Application/Screen.cs`

```csharp
internal void ConfineNavigationTo(GameObject root) { NavConfineRoot = root; Resolve(confineRoot: root); }
internal void RestoreNavigationConfine(GameObject previous) { NavConfineRoot = previous; Resolve(confineRoot: previous); }
internal void ConfineNavigationToSelf() => ConfineNavigationTo(RootGameObject);   // 保留给 UI.Modal
```

### 10.7 `UI.Modal.cs` / `ModalEscapeListener.cs`

`OnEscapePressed` 首行 `if (Controls.TabMenu.TryConsumeEscape()) return;`。`ModalEscapeListener` 加 `internal bool AlsoCancelButton`，`OnEnable` 里按它决定是否 `AddBinding("<Gamepad>/buttonEast")`。

### 10.8 `ScreenInstantiator.cs` / `IRWalker.cs`

`selfIsLayoutGroup` / `isLayoutGroup` 两处名单加 `"TabMenu"`；`isTabBar` 改为 `isTabGroup = node.Tag is "TabBar" or "TabMenu"`；lint 分发加 `else if (node.Tag == "TabMenu") TabMenuRules.CheckTabMenu(node)`。

### 10.9 测试（Red first）

| 文件 | 覆盖 |
|---|---|
| `Tests/EditMode/Controls/TabMenuTests.cs` | 注册；子节点落在 Content；caption 镜像 text/icon 与选中变化；`GetNativeSize` hug；Open 后面板 inactive；`Expand` 激活面板 + 建 blocker + sorting；`Collapse` 反之；选中即收起；`bind` 显隐同 TabBar；展开态跨 ReSolve；第二个展开收起第一个；程序化属性落在 Popup（`__Surface` 是 Popup 子节点）；`color` / `sprite` 落在面板 Image；`transition="0"` 即时；`interactable=false` 收起 |
| `Tests/EditMode/Controls/TabMenuPlacementTests.cs` | 默认左下；宽 = max(触发区, 内容)；`popupWidth` 显式；下方不够向上翻；右缘夹回 |
| `Tests/EditMode/Controls/TabMenuBindItemsTests.cs` | 重建替换静态 Tab；收起态重建后 label preferred > 0（同帧临时激活）；空列表清 caption；包装 Template 找到 Tab |
| `Tests/EditMode/Controls/TabMenuTriggerTests.cs` | `expand` / `collapse` / `@id` 解析；bare 向上解析；`OnFire` 时机；无祖先抛错 |
| `Tests/EditMode/Navigation/TabMenuTrapTests.cs` | 展开 → `ContainmentRoot = Popup`、焦点在选中 Tab；收起恢复；Esc 收弹窗不关模态（两种顺序） |
| `Tests/EditMode/Lint/TabMenuRulesTests.cs` + `BuiltinTagsTests` | §9 全部规则 |
| `Tests/EditMode/Controls/TabBar*Tests.cs`（既有） | 核心抽取回归 |
| `Tests/PlayMode/Controls/TabMenuPlayTests.cs` | 真实 Canvas 排序；blocker `onClick.Invoke` 收起；过渡结束后面板 inactive |

## 11. 跟现有 spec / SKILL 的整合点

### 11.1 主 spec `2026-05-07-promptugui-description-language-design.md`

§5 控件表加一行：`<TabMenu>` — 弹出式 Tab 组；子 = `<Tab>`；表面 = 弹窗面板；caption 镜像选中项；`OnSelectionChanged` / `OnExpanded` / `OnCollapsed`；`itemTemplate` + `BindItems` — RectTransform + PuiButton + ToggleGroup + 内部 Popup(Canvas overrideSorting)（详见本文）。§5.7 提一句 `on="expand"` / `collapse`。

### 11.2 `authoring-promptugui-xml/SKILL.md`

- 内置 tag catalog 加 `<TabMenu>` 行，指向 `reference/controls-tabs.md#tabmenu`。
- "Which tags draw procedurally" 名单与「程序化表面 → 主表面」表加 `<TabMenu>` → **弹窗面板**。
- native-size 名单加 `<TabMenu>`（caption hug；最小高 44）；`focus` / `nav*` 的 Selectable 名单加 `<TabMenu>`；`tint` 名单加 `<TabMenu>`（面板底图）。
- Re-skinning 段：`<TabMenu arrow= arrowColor=>`。

### 11.3 `reference/controls-tabs.md`

新 `## <TabMenu>` 节：§3 的三个例子、§4 属性表、`<Tab>` 在其中的差异（`width` 无效 / 项通栏）、caption 镜像规则、展开收起交互（blocker / Esc / B / 选中即收）、定位与翻转、`transition`、lint 表。标题改为 "Tabs (`<TabBar>` / `<TabMenu>` / `<Tab>`)"。

### 11.4 `reference/animations.md`

`on=` 表加 `expand` / `expand@<id>` / `collapse` / `collapse@<id>` 四行；"UPWARD" 段落把 TabMenu 祖先加进来；Patterns 加「菜单项入场」例子。

### 11.5 `reference/navigation.md`

Selectable 名单加 `<TabMenu>`；新段落「Popup focus trap」：展开时圈闭到面板、B / Esc 收起、焦点回触发区。

### 11.6 `scripting-promptugui-csharp/SKILL.md`

`TabMenu` API（§5.1）；事件速查表加 `TabMenu.OnSelectionChanged:Tab / OnExpanded / OnCollapsed`；DATA PUSH 加 `TabMenu.BindItems`。

## 12. Out of Scope（v2 候选）

- `popupAlign="left|center|right"`、`popupMaxHeight` + 内部滚动
- `closeOnSelect="false"`（多选式菜单）、hover 展开、键盘首字母跳转
- 弹窗 `mask`（TM-D20）
- caption 覆盖（`text=` / `icon=` 固定 caption）与自定义 caption slot（TM-D5）
- 过渡预设名 / 自定义曲线（`expandAnim="scalein"`）（TM-D13）
- 触发区状态色（`hoverColor` 等作用于 caption）与 `IStateSource`（TM-D18）
- `<Show on="expand">`
- 弹窗里嵌套 TabMenu
- `<Dropdown>` 的 `fontSize` / `itemFontSize` —— 独立小改，与本文无关

## 13. 风险与回滚

| 风险 | 缓解 |
|---|---|
| 面板 `sortingOrder = root + 2` 被同 sorting layer 里 order 更高的另一页面盖住 | `PopupSortingOffset` 是 internal static，可整体调；Router 分层页面若用了大间隔 order，在实施期确认一次并写进 skill |
| 嵌套 Canvas 让面板逃出祖先 mask，同时也逃出祖先 `CanvasGroup` 的 `blocksRaycasts`？ | 不会：`CanvasGroup` 沿 transform 链生效，与 Canvas 无关；`interactable=false` 的父级仍能挡住面板 |
| `Popup` 收起态 inactive 期间 ReSolve 重写 Tab 属性 | 与 `Tab.bind` 隐藏页同款路径（inactive 上 apply 是既有行为）；TMP 已 Awake 过，`GetPreferredValues` 可用 |
| BindItems 同帧临时激活触发 `Toggle.OnEnable` → `ToggleGroup` 注册顺序导致多个 `isOn=true` 抖动 | `SyncInitialSelection` 在激活后执行，最终只有一个 on；与 TabBar 相同的兜底 |
| 展开中 `ReSolve` 取消过渡 snap 到终态，视觉上跳一下 | 只发生在 resize / Variant / Theme 切换的那一帧，可接受 |
| `TabGroupCore` 抽取改坏 TabBar | 既有 `TabBarTests` / `TabBarBindItemsTests` / `TabBarChildSizingTests` 覆盖全部公开行为；抽取是纯搬运，先跑绿再动 TabMenu |
| Esc 同帧双 listener 顺序 | `TryConsumeEscape` 的两段判断覆盖两种顺序（§7.9），`TabMenuTrapTests` 两种顺序都测 |
| blocker `Button` 被 `ExplicitNavigationResolver` 当成邻居 | `navigation = Navigation.defaultNavigation` 改为 `None`；`FocusableUnder` 只在 `ContainmentRoot`（面板）下找 |
| XSD 不自动更新 | 同所有新 `[UIAttr]`：`Tools → PromptUGUI → Schema → Generate XSD`；`XsdGeneratorTests` 加 `TabMenu` 的 substring 断言 |
| 回滚 | 新文件独立；`TabBar` 委托 `TabGroupCore` 是等价重构，可单独保留 |

---

## 14. 实施记录（与设计的差异）

实施计划：[`../plans/2026-08-29-tabmenu.md`](../plans/2026-08-29-tabmenu.md)。设计整体照做，四处在写代码时被现实修正：

**14.1 互斥不能靠 uGUI 的 ToggleGroup（改动 TM-D4 的隐含前提）。**
收起态弹窗是 inactive，而 uGUI `Toggle.Set` 把 `NotifyToggleOn` 的调用挡在 `IsActive()` 后面，
被禁用的 Toggle 也已经从 group 注销。结果：菜单关着时用代码写 `tab.IsOn = true` 会同时选中两项、
两个 `bind` 页面一起显示。互斥改由 `TabGroupCore` 在发 `SelectionChanged` **之前**自己保证
（`EnforceExclusive`）—— 对 `<TabBar>` 是幂等 no-op，因为 ToggleGroup 已经先做过。
`ToggleGroup` 仍然挂着（键盘/手柄路径与既有 Tab 行为依赖它）。

**14.2 「选中即收起」需要一个展开期的重入保护（§7.1 补充）。**
`Expand()` 里 `SetActive(true)` 弹窗子树，会让 uGUI 在 `OnEnable` 重新校验 ToggleGroup 并**补发**
一次「已选中」事件。而「选中即收起」把它当成用户选择 —— 菜单在打开它的同一次调用里自己关掉了
（`execute_code` 实测：`Expand()` 返回后 `IsExpanded` 已是 `false`）。加 `_expanding` 标志，
展开进行中忽略选中驱动的收起；用户点击与代码设选中两条路径都保留。

**14.3 Esc 让位需要同帧 guard（§7.9 已预见，实测确认必要）。**
`TabMenu` 自己的 listener 与 `UI.Modal.OnEscapePressed` 响应同一次按键、顺序不定。
`TryConsumeEscape()` 因此有两段判断：有展开中的菜单 → 收起并返回 true；否则 `s_escapeFrame ==
Time.frameCount` → 也返回 true。两种顺序都只关菜单、不关模态。
`s_expanded` / `s_escapeFrame` 纳入 `UI.ResetForTests`（EditMode 里 `Time.frameCount` 几乎不推进，
不清会让「已消费」永久为真）。

**14.4 `<TabMenu>` 是 `ProceduralSurfaceRollout` 契约的第三个正当例外。**
其它控件只画一张脸，表面替换它、`targetGraphic` 跟着走。TabMenu 画两处：会 hover/press 的把手，
和 `radius` / `glass` 描述的弹窗面板。把 `targetGraphic` 指向面板会让「悬停把手」染色整个菜单，
所以它留在 caption 上。已按 Slider / Progress 的既有写法加具名测试记录（`TabMenu_KeepsTargetGraphicOnItsCaption`）。

**14.5 定位数学抽成纯函数。**
EditMode 无法给 ScreenSpaceOverlay canvas 一个确定的尺寸，所以 §7.2 的规则落在
`Internal/PopupPlacer.Solve(handle, panel, canvas, gap)` 里单测；控件侧只测接线。

**14.6 `GetNativeSize` 必须自己去看 Tab，不能读 caption 标签（§6 补充）。**
`ApplyCommon` 在 `OnAfterApply` 填 caption **之前**测量控件，所以读 `_label.text` 测到的是空串 ——
把手在 HStack 里被排成 30px（只够内边距 + 箭头），不管频道叫什么。改为回退到 `PeekSelectedContent()`：
直接走 `Children` 找 `isOn` 的 Tab（没有就取第一个，镜像自动选中规则）读它的 text / icon。
子节点在 DFS post-order 里已经 apply 完，所以这份数据是准的。
运行期改 caption 不重排（`LayoutElement` 停在打开时的快照）是 `<Btn>` 同款既有行为，已在 skill 里写明。

**14.7 箭头必须是垂直镜像，不能是 180° 旋转（§7.7 修正，作者实测反馈）。**
旋转绕 pivot 发生，而箭头的 pivot 是它的**左边缘**（`LayoutCaption` 靠这一点把它按左侧摆在 label 之后）——
转 180° 会把整个字形甩到摆放点的左边，菜单每次打开箭头都横向跳一下。改成 `localScale.y` 1→-1：
pivot 的 y 已经居中，纵向镜像不动 x。回归测试同时断言 `anchoredPosition`、世界空间左缘、
以及 `localEulerAngles.z == 0`（确认是翻转不是旋转）。

**其余按设计实现**：TM-D1…D21 全部照做；§12 的 Out of Scope 一项未做。
测试：EditMode 2844、EditorOnly 310、PlayMode 176，全绿。
