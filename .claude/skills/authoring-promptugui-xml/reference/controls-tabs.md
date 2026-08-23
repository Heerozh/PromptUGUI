# Tabs (`<TabBar>` / `<Tab>`)

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md). The `<TabBar>` / `<Tab>` attribute tables live in the main doc's built-in primitives catalog; read this for layout patterns and lint rules. State colours: see [`states.md`](states.md).

`<TabBar>` 是 Tab 的容器；私有 `ToggleGroup`（`allowSwitchOff=false`）保证互斥，私有 `HorizontalLayoutGroup` / `VerticalLayoutGroup`（看 `direction=`）排布。**TabBar 本身没视觉**，只是布局容器；每个 `<Tab>` 自带 `sprite`（常态底）；`selectedSprite` 在选中时把 bg 自己的 `overrideSprite` 换成它、取消选中再换回 `sprite`（单图，无独立 overlay 节点；由 `isOn` 驱动，hover/按下选中 tab 不影响它；设了会把 `transition` 切到 None）。不写 `selectedSprite` 时 Tab 退化成纯色按钮视觉，互斥仍在。`selectedSprite=""` / `none` = 无 swap（no-op）。

```xml
<TabBar id="topbar" anchor="top-stretch" height="40">
  <Tab text="Edit"     sprite="ui:tab_normal" selectedSprite="ui:tab_selected" bind="editor_panel" isOn="true"/>
  <Tab text="Help"     sprite="ui:tab_normal" selectedSprite="ui:tab_selected" bind="help_panel"/>
  <Tab text="Settings" sprite="ui:tab_normal" selectedSprite="ui:tab_selected" bind="settings_panel"/>
</TabBar>

<Frame id="editor_panel"   margin="40,0,0,0">...</Frame>
<Frame id="help_panel"     margin="40,0,0,0">...</Frame>
<Frame id="settings_panel" margin="40,0,0,0">...</Frame>
```

要把所有 Tab 的视觉抽出来共享,定义一个 `<Template>` 让 Tab 在里面声明 sprite,然后当作 TabBar 子元素或者 `itemTemplate=` 使用（见下面 "Custom Tab layout via Template"）。

要给 TabBar 本身加一条背景条,直接把 TabBar 套进一个 `<Image>` —— `<Image>` 接受子节点,一层搞定。注意 Image / TabBar 的默认 anchor 是 top-left,需要显式写 `anchor=` 把它们撑开:

```xml
<Image sprite="ui:tabbar_bg" anchor="top-stretch" height="40">
  <TabBar id="topbar" anchor="stretch">...</TabBar>
</Image>
```

`bind="frame_id"` 让 Tab 选中时显示、未选时隐藏命名 Frame。lookup 是 lazy 的 —— 首次切换才解析并缓存。Tab `isOn="true"` 在 XML 里指定初始选中；都没写时 TabBar 自动选第一个。`bind=` 省略时只 fire `OnSelected`（C# 端自己处理）。`isOn` 是**运行时独占状态**：声明值是初始选中，但一旦用户/代码运行期改过它，ReSolve（窗口 resize / Variant / Theme 切换）**不会**把它打回声明默认值 —— 用户选中的 Tab 和 `bind` 的页面都保持不变。`isOn.variant`（如 `isOn.portrait`）仍然有效：只要运行期没动过，切到该 Variant 会正常重应用覆盖值；动过之后用户的选择优先。`<Toggle isOn>` 同理。（同款「运行期改过就不打回、没动过 Variant 照常覆盖」也适用于 `<Slider value>` / `<Dropdown value>` / `<Progress value>`。）

用自定义 `itemTemplate` 时（`<TabBar itemTemplate="MyTabTemplate"/>`），Template body 必须在树里某处包含恰好一个 `<Tab>`（通过 `ScopedIds` 或递归 `Control.Children` walk 在 `BindItems` 时定位）。

Tab 是 TabBar 的 layout group child —— 不能写 `anchor=` / `margin=`（`HorizontalLayoutGroup` 接管排布；TabBar 在 `selfIsLayoutGroup` 名单里）。

## Custom Tab layout

### Via Template (for shared styling across instances / dynamic BindItems)

When several tabs share one rich face, or tabs are generated at runtime from data, make the `<Tab>` the
Template root and put its content **inside** the Tab (children overlay the bg, Frame-style). `sprite` /
`selectedSprite` live on the `<Tab>` so every instance shares them without restating:

```xml
<Template name="FileTab">
  <Param name="text"/>
  <Param name="icon"/>
  <Param name="bind"/>
  <Param name="isOn" default="false"/>
  <Tab id="tab" width="80" height="96"
       sprite="ui:cell_normal" selectedSprite="ui:cell_selected"
       isOn="{{isOn}}" bind="{{bind}}">
    <Icon id="icon" name="ui:icon_{{icon}}"
          anchor="top-center" width="48" height="48"
          margin="8,0,0,0"/>
    <Text id="name" anchor="top-stretch" margin="60,4,0,4"
          fontSize="12" align="center" raycastTarget="false">{{text}}</Text>
  </Tab>
</Template>

<TabBar id="files">
  <FileTab text="111.png" icon="png" isOn="true" bind="panel1"/>
  <FileTab text="222.jpg" icon="jpg" bind="panel2"/>
</TabBar>
```

TabBar collects the Tab whether it is the Template root (as here) or nested inside a wrapper; auto-select and `OnSelectionChanged` work the same either way. Lint rules `PUI-TABBAR-CHILD` and `PUI-TAB-PARENT` are suppressed for Template-instance roots. The Tab's `width`/`height` is its layout-group cell size; its children use their own `anchor` / `margin` (Tab is not a layout group). Omit them and the Tab sizes to its own label (plus padding, min 44px tap target) — it never collapses to zero. `width="stretch"` splits the bar's remaining space evenly, exactly as in `<HStack>`.

> ⚠️ **Behaviour change.** `width` / `height` on a `<Tab>` used to be silently ignored — TabBar's layout group was left at Unity's default `childControlWidth/Height = false`, which only positions children and never resizes them, so every Tab stayed at the default 100×100 (overflowing the bar and overlapping its neighbours). TabBar now configures the group like `<VStack>` / `<HStack>` do, so the values you write actually land. Existing TabBars will shift — toward what the markup always said. Keep decorative children `raycastTarget=false` (`<Icon>` already is; add it on `<Text>`) so clicks fall through to the containing Tab.

For dynamic data, use `BindItems` with `itemTemplate="FileTab"` (the same Template works for both patterns).

## Lint 规则

| Code                   | 触发条件                                                                              | 级别    |
| ---------------------- | ------------------------------------------------------------------------------------- | ------- |
| `PUI-TAB-PARENT`       | `<Tab>` 不在 `<TabBar>` 直接父节点下（Template-instance root 内的 Tab 已豁免）        | warning |
| `PUI-TABBAR-CHILD`     | `<TabBar>` 的直接子节点不是 `<Tab>`，且子树里既无字面 `<Tab>` 也无模板调用（Template wrapper 与模板调用均已豁免——后者是非内置 tag，CLI 不展开 `<Import>`，可能含 `<Tab>`，如 `itemTemplate` 项） | warning |
| `PUI-TABBAR-DIRECTION` | `direction` 不是 `horizontal` / `vertical`                                            | error   |
